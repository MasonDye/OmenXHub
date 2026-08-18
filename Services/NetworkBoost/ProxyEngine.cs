using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace OmenSuperHub.Services.NetworkBoost {
  /// <summary>
  /// 本地 SOCKS5 + HTTP 双协议代理引擎，对每个出站连接用 IP_UNICAST_IF(31) 绑定物理网卡，
  /// 在选中网卡间做轮询调度。附带每秒一次的网卡流量监控线程（写回 NicInfo.Down/Up/Connections）。
  /// </summary>
  internal sealed class ProxyEngine {
    readonly List<NicInfo> _nics;
    readonly int _socksPort;
    readonly int _httpPort;
    readonly RateLimiter _engineLimiter;  // per-engine 限速（per-process 用）
    readonly Dictionary<string, NetworkInterface> _nifMap = new Dictionary<string, NetworkInterface>(StringComparer.OrdinalIgnoreCase);
    readonly ConcurrentDictionary<Socket, byte> _clients = new ConcurrentDictionary<Socket, byte>();
    readonly object _rrLock = new object();
    readonly object _connLock = new object();
    readonly Dictionary<string, int> _perNicConnections = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    TcpListener _socks;
    TcpListener _http;
    Thread _monitorThread;
    volatile bool _running;
    int _rrCursor;
    int _connections;
    const int ConnectTimeoutMs = 8000;

    // 全局 + per-NIC 限速器，所有 ProxyEngine 实例共享
    static RateLimiter _globalLimiter;
    static readonly Dictionary<string, RateLimiter> _nicLimiters =
      new Dictionary<string, RateLimiter>(StringComparer.OrdinalIgnoreCase);

    public static void SetGlobalLimit(int rateKBps) {
      _globalLimiter = rateKBps > 0 ? new RateLimiter(rateKBps) : null;
    }
    public static void SetNicLimit(string nicName, int rateKBps) {
      if (rateKBps > 0) _nicLimiters[nicName] = new RateLimiter(rateKBps);
      else _nicLimiters.Remove(nicName);
    }
    public static void ClearNicLimits() => _nicLimiters.Clear();

    public int SocksPort => _socksPort;
    public int HttpPort => _httpPort;
    public bool IsRunning => _running;
    public int TotalConnections => _connections;
    public IReadOnlyList<NicInfo> Nics => _nics;

    public ProxyEngine(List<NicInfo> nics, int socksPort, RateLimiter engineLimiter = null) {
      _nics = nics;
      _socksPort = socksPort;
      _httpPort = socksPort + 1;
      _engineLimiter = engineLimiter;
      foreach (var nif in NetworkInterface.GetAllNetworkInterfaces()) {
        if (nics.Any(n => string.Equals(n.Name, nif.Name, StringComparison.OrdinalIgnoreCase)))
          _nifMap[nif.Name] = nif;
      }
    }

    public void Start() {
      if (_running) return;
      _running = true;
      _socks = new TcpListener(IPAddress.Loopback, _socksPort);
      _socks.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
      _socks.Start();
      _http = new TcpListener(IPAddress.Loopback, _httpPort);
      _http.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
      _http.Start();
      var t1 = new Thread(AcceptSocksLoop) { IsBackground = true };
      var t2 = new Thread(AcceptHttpLoop) { IsBackground = true };
      _monitorThread = new Thread(MonitorLoop) { IsBackground = true };
      t1.Start(); t2.Start(); _monitorThread.Start();
    }

    public void Stop() {
      if (!_running) return;
      _running = false;
      try { _socks.Stop(); } catch { }
      try { _http.Stop(); } catch { }
      foreach (var s in _clients.Keys.ToList()) { try { s.Close(); } catch { } }
      _clients.Clear();
    }

    // ---------- 监听 ----------
    void AcceptSocksLoop() {
      while (_running) {
        try {
          var c = _socks.AcceptTcpClient();
          _clients.TryAdd(c.Client, 0);
          var t = new Thread(() => HandleSocksClient(c)) { IsBackground = true };
          t.Start();
        } catch { if (!_running) break; }
      }
    }

    void AcceptHttpLoop() {
      while (_running) {
        try {
          var c = _http.AcceptTcpClient();
          _clients.TryAdd(c.Client, 0);
          var t = new Thread(() => HandleHttpClient(c)) { IsBackground = true };
          t.Start();
        } catch { if (!_running) break; }
      }
    }

    // ---------- SOCKS5 ----------
    void HandleSocksClient(TcpClient client) {
      try {
        using (client) {
          var stream = client.GetStream();
          byte[] hdr = ReadExactly(stream, 2);
          if (hdr == null || hdr[0] != 5) return;
          if (ReadExactly(stream, hdr[1]) == null) return;
          stream.WriteByte(5); stream.WriteByte(0);
          byte[] req = ReadExactly(stream, 4);
          if (req == null || req[0] != 5 || req[1] != 1) return;
          string host; int port;
          if (req[3] == 1) { byte[] b = ReadExactly(stream, 4); if (b == null) return; host = new IPAddress(b).ToString(); }
          else if (req[3] == 3) { byte[] l = ReadExactly(stream, 1); if (l == null) return; byte[] d = ReadExactly(stream, l[0]); if (d == null) return; host = Encoding.ASCII.GetString(d); }
          else return;
          byte[] pb = ReadExactly(stream, 2);
          if (pb == null) return;
          port = (pb[0] << 8) | pb[1];
          Socket up = ConnectOnNic(host, port, out string nicName);
          if (up == null) {
            try { stream.Write(new byte[] { 5, 1, 0, 1, 0, 0, 0, 0, 0, 0 }, 0, 10); } catch { }
            return;
          }
          var remote = (IPEndPoint)up.RemoteEndPoint;
          byte[] reply = new byte[10];
          reply[0] = 5; reply[2] = 0; reply[3] = 1;
          byte[] addr = remote.Address.GetAddressBytes();
          Array.Copy(addr, 0, reply, 4, 4);
          reply[8] = (byte)(remote.Port >> 8); reply[9] = (byte)remote.Port;
          stream.Write(reply, 0, 10);
          PumpWithCount(stream, new NetworkStream(up, true), nicName);
        }
      } catch { }
    }

    // ---------- HTTP 代理 ----------
    void HandleHttpClient(TcpClient client) {
      try {
        using (client) {
          var stream = client.GetStream();
          byte[] header = ReadHeader(stream);
          if (header == null) return;
          string firstLine = ReadFirstLine(header);
          if (firstLine == null) return;
          string[] parts = firstLine.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
          if (parts.Length < 3) return;
          bool isConnect = parts[0].Equals("CONNECT", StringComparison.OrdinalIgnoreCase);
          string target = parts[1];
          string host; int port;
          if (isConnect) {
            if (!TryParseHostPort(target, 443, out host, out port)) return;
          } else if (target.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) {
            Uri u;
            if (!Uri.TryCreate(target, UriKind.Absolute, out u)) return;
            host = u.Host; port = u.IsDefaultPort ? 80 : u.Port;
          } else {
            if (!TryParseHostHeader(header, out host)) return;
            port = 80;
          }
          Socket up = ConnectOnNic(host, port, out string nicName);
          if (up == null) return;
          var upstream = new NetworkStream(up, true);
          if (isConnect) {
            byte[] resp = Encoding.ASCII.GetBytes("HTTP/1.1 200 Connection Established\r\n\r\n");
            stream.Write(resp, 0, resp.Length);
          } else {
            upstream.Write(header, 0, header.Length);
          }
          PumpWithCount(stream, upstream, nicName);
        }
      } catch { }
    }

    // ---------- 出站连接 ----------
    NicInfo NextNic() {
      lock (_rrLock) {
        var nic = _nics[_rrCursor % _nics.Count];
        _rrCursor = (_rrCursor + 1) % _nics.Count;
        return nic;
      }
    }

    /// <summary>解析目标（必要时 DNS），选网卡、IP_UNICAST_IF 绑定、连接。失败返回 null。</summary>
    Socket ConnectOnNic(string host, int port, out string nicName) {
      nicName = null;
      IPAddress ip;
      if (!IPAddress.TryParse(host, out ip)) {
        try {
          var v4 = Dns.GetHostAddresses(host).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
          if (v4 == null) return null;
          ip = v4;
        } catch { return null; }
      }
      var nic = NextNic();
      nicName = nic.Name;
      try {
        var sock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        try {
          // IP_UNICAST_IF(31)：IPv4 接口索引须为网络字节序，且必须在 Bind/Connect 之前设置
          sock.SetSocketOption(SocketOptionLevel.IP, (SocketOptionName)31,
            BitConverter.GetBytes(IPAddress.HostToNetworkOrder(nic.Index)));
        } catch { }
        if (!string.IsNullOrEmpty(nic.Ip)) {
          try { sock.Bind(new IPEndPoint(IPAddress.Parse(nic.Ip), 0)); } catch { }
        }
        IAsyncResult ar = sock.BeginConnect(new IPEndPoint(ip, port), null, null);
        if (!ar.AsyncWaitHandle.WaitOne(ConnectTimeoutMs)) { try { sock.Close(); } catch { } return null; }
        sock.EndConnect(ar);
        return sock;
      } catch { return null; }
    }

    // ---------- 数据泵与统计 ----------
    void PumpWithCount(NetworkStream down, NetworkStream up, string nicName) {
      if (nicName != null) IncPerNic(nicName, +1);
      Interlocked.Increment(ref _connections);
      try {
        var limiters = BuildLimiters(nicName);
        Pump(down, up, limiters);
      }
      finally {
        if (nicName != null) IncPerNic(nicName, -1);
        Interlocked.Decrement(ref _connections);
      }
    }

    RateLimiter[] BuildLimiters(string nicName) {
      var list = new List<RateLimiter>(3);
      if (_globalLimiter != null) list.Add(_globalLimiter);
      if (nicName != null) {
        RateLimiter nl;
        if (_nicLimiters.TryGetValue(nicName, out nl)) list.Add(nl);
      }
      if (_engineLimiter != null) list.Add(_engineLimiter);
      return list.Count > 0 ? list.ToArray() : null;
    }

    static void Pump(Stream a, Stream b, RateLimiter[] limiters) {
      var t1 = new Thread(() => CopyLoop(a, b, limiters)) { IsBackground = true };
      var t2 = new Thread(() => CopyLoop(b, a, limiters)) { IsBackground = true };
      t1.Start(); t2.Start();
      t1.Join(); t2.Join();
    }

    static void CopyLoop(Stream from, Stream to, RateLimiter[] limiters) {
      try {
        var buf = new byte[65536];
        int n;
        while ((n = from.Read(buf, 0, buf.Length)) > 0) {
          if (limiters != null)
            foreach (var lim in limiters) lim.Consume(n);
          to.Write(buf, 0, n);
        }
      } catch { }
      try { from.Close(); } catch { }
      try { to.Close(); } catch { }
    }

    void IncPerNic(string name, int delta) {
      lock (_connLock) {
        int v;
        _perNicConnections.TryGetValue(name, out v);
        v += delta;
        _perNicConnections[name] = Math.Max(0, v);
      }
    }

    // ---------- 流量监控（网卡级计数器，1s） ----------
    void MonitorLoop() {
      var lastRecv = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
      var lastSent = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
      foreach (var kv in _nifMap) {
        try {
          var st = kv.Value.GetIPv4Statistics();
          lastRecv[kv.Key] = st.BytesReceived;
          lastSent[kv.Key] = st.BytesSent;
        } catch { }
      }
      while (_running) {
        Thread.Sleep(1000);
        foreach (var kv in _nifMap) {
          var nic = _nics.FirstOrDefault(n => string.Equals(n.Name, kv.Key, StringComparison.OrdinalIgnoreCase));
          if (nic == null) continue;
          long r = 0, s = 0;
          try { var st = kv.Value.GetIPv4Statistics(); r = st.BytesReceived; s = st.BytesSent; } catch { }
          long pr, ps;
          lastRecv.TryGetValue(kv.Key, out pr); lastSent.TryGetValue(kv.Key, out ps);
          nic.DownMbps = Math.Max(0, (r - pr) / 1048576.0);
          nic.UpMbps = Math.Max(0, (s - ps) / 1048576.0);
          lastRecv[kv.Key] = r; lastSent[kv.Key] = s;
        }
        lock (_connLock) {
          foreach (var kv in _perNicConnections) {
            var nic = _nics.FirstOrDefault(n => string.Equals(n.Name, kv.Key, StringComparison.OrdinalIgnoreCase));
            if (nic != null) nic.Connections = kv.Value;
          }
        }
      }
    }

    // ---------- 基础 IO ----------
    static byte[] ReadExactly(Stream s, int n) {
      var buf = new byte[n];
      int got = 0;
      while (got < n) {
        int r = s.Read(buf, got, n - got);
        if (r <= 0) return null;
        got += r;
      }
      return buf;
    }

    static byte[] ReadHeader(Stream s) {
      var ms = new MemoryStream();
      var buf = new byte[4096];
      while (ms.Length < 131072) {
        int r = s.Read(buf, 0, buf.Length);
        if (r <= 0) return null;
        ms.Write(buf, 0, r);
        byte[] data = ms.ToArray();
        if (HasHeaderTerminator(data)) return data;
      }
      return null;
    }

    static bool HasHeaderTerminator(byte[] d) {
      for (int i = 0; i < d.Length - 3; i++)
        if (d[i] == '\r' && d[i + 1] == '\n' && d[i + 2] == '\r' && d[i + 3] == '\n') return true;
      for (int i = 0; i < d.Length - 1; i++)
        if (d[i] == '\n' && d[i + 1] == '\n') return true;
      return false;
    }

    static string ReadFirstLine(byte[] header) {
      int end = 0;
      while (end < header.Length && header[end] != '\n') end++;
      if (end == header.Length) return null;
      return Encoding.ASCII.GetString(header, 0, end).TrimEnd('\r');
    }

    static bool TryParseHostPort(string target, int defaultPort, out string host, out int port) {
      host = target; port = defaultPort;
      int colon = target.LastIndexOf(':');
      if (colon <= 0) return true;
      if (target.IndexOf(':') != colon) return true; // IPv6 字面量，整串当 host
      int p;
      if (!int.TryParse(target.Substring(colon + 1), out p)) return true;
      host = target.Substring(0, colon);
      port = p;
      return true;
    }

    static bool TryParseHostHeader(byte[] header, out string host) {
      host = null;
      string text = Encoding.ASCII.GetString(header);
      int idx = text.IndexOf("Host:", StringComparison.OrdinalIgnoreCase);
      if (idx < 0) return false;
      int start = idx + 5;
      while (start < text.Length && text[start] == ' ') start++;
      int end = start;
      while (end < text.Length && text[end] != '\r' && text[end] != '\n') end++;
      host = text.Substring(start, end - start).Trim();
      return host.Length > 0;
    }
  }
}
