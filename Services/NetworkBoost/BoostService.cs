using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using OmenSuperHub;

namespace OmenSuperHub.Services.NetworkBoost {
  /// <summary>
  /// 多网卡加速编排器。proxy 模式：本地 SOCKS5/HTTP 代理 + WinINet 系统代理；
  /// tun 模式：三端口出站池 (2001 有线 / 2003 无线 / 2005 聚合) + sing-box TUN。
  /// 网卡流量/连接数由各 ProxyEngine 写回 NicInfo，页面每秒轮询 RefreshTotals()。
  /// </summary>
  internal static class BoostService {
    public const int SocksPort = 10800;
    // ponytail: 每个 ProxyEngine 占两个端口 (socks=N, http=N+1)，池端口必须步进 2，
    // 否则上一引擎的 HTTP 口撞下一引擎的 SOCKS 口；且 ReuseAddress 让双绑都成功，
    // 表现为默认出站流量随机串到别的引擎(旧值 2001/2002/2003 即此 bug)。
    public const int PoolEthPort = 2001;
    public const int PoolWifiPort = 2003;
    public const int PoolAggPort = 2005;

    public static List<NicInfo> AllNics { get; private set; } = new List<NicInfo>();
    public static List<NicInfo> SelectedNics { get; private set; } = new List<NicInfo>();
    public static bool IsRunning { get; private set; }
    public static bool IsTun { get; private set; }
    public static int TotalConnections { get; private set; }
    public static double TotalDownMbps { get; private set; }
    public static double TotalUpMbps { get; private set; }

    public static event Action<string> OnLog;

    static ProxyEngine _engine;
    static readonly List<ProxyEngine> _pool = new List<ProxyEngine>();

    public static void Scan() {
      AllNics = NicScanner.Scan();
      ApplySavedSelection();
    }

    public static void ApplySavedSelection() {
      var saved = new HashSet<string>(
        (ConfigService.BoostSelectedNics ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries),
        StringComparer.OrdinalIgnoreCase);
      SelectedNics = AllNics.Where(n => saved.Contains(n.Name)).ToList();
    }

    public static void SetSelected(string name, bool on) {
      var nic = AllNics.FirstOrDefault(n => n.Name == name);
      if (nic == null) return;
      if (on) { if (!SelectedNics.Contains(nic)) SelectedNics.Add(nic); }
      else SelectedNics.Remove(nic);
      ConfigService.BoostSelectedNics = string.Join(",", SelectedNics.Select(n => n.Name));
      ConfigService.Save("BoostSelectedNics");
    }

    public static void SetMode(string mode) {
      ConfigService.BoostMode = mode == "tun" ? "tun" : "proxy";
      ConfigService.Save("BoostMode");
    }

    public static bool Start(out string error) {
      error = "";
      if (SelectedNics.Count == 0) { error = Strings.BoostNoNicSelected; return false; }
      StopInternal();
      SetupLimiters();
      return ConfigService.BoostMode == "tun" ? StartTun(out error) : StartProxy(out error);
    }

    static void SetupLimiters() {
      ProxyEngine.SetGlobalLimit(ConfigService.BoostGlobalLimitKBps);
      ProxyEngine.ClearNicLimits();
      if (ConfigService.BoostNicLimitKBps > 0) {
        foreach (var nic in SelectedNics)
          ProxyEngine.SetNicLimit(nic.Name, ConfigService.BoostNicLimitKBps);
      }
    }

    static void ClearLimiters() {
      ProxyEngine.SetGlobalLimit(0);
      ProxyEngine.ClearNicLimits();
    }

    public static void Stop() => StopInternal();

    static bool StartProxy(out string error) {
      error = "";
      try {
        _engine = new ProxyEngine(new List<NicInfo>(SelectedNics), SocksPort);
        _engine.Start();
        SystemProxyManager.Enable("127.0.0.1:" + (_engine.HttpPort), "127.0.0.1:" + _engine.SocksPort);
        Log("SOCKS5 代理 127.0.0.1:" + _engine.SocksPort + "   HTTP 代理 127.0.0.1:" + _engine.HttpPort);
        IsRunning = true;
        IsTun = false;
        return true;
      } catch (Exception ex) {
        error = ex.Message;
        StopInternal();
        return false;
      }
    }

    static bool StartTun(out string error) {
      error = "";
      try {
        var eth = ClassifyEthernet(SelectedNics);
        var wifi = ClassifyWifi(SelectedNics);
        _pool.Add(new ProxyEngine(eth, PoolEthPort));
        _pool.Add(new ProxyEngine(wifi, PoolWifiPort));
        _pool.Add(new ProxyEngine(new List<NicInfo>(SelectedNics), PoolAggPort));
        string exe = Assembly.GetEntryAssembly()?.Location ?? "";
        string configPath = TunManager.ConfigPath;
        var dir = System.IO.Path.GetDirectoryName(configPath);
        if (dir != null) System.IO.Directory.CreateDirectory(dir);
        // 生成 sing-box 配置，获取 per-process 限速端口列表
        List<KeyValuePair<int, double>> limitedPorts;
        SingboxConfigGenerator.Write(configPath, RoutingRuleStore.Load(), exe, out limitedPorts);
        // 为每个限速进程创建独立的 ProxyEngine（带限速器）
        foreach (var lp in limitedPorts) {
          var limiter = new RateLimiter(lp.Value);
          var eng = new ProxyEngine(new List<NicInfo>(SelectedNics), lp.Key, limiter);
          _pool.Add(eng);
          Log("限速进程端口 " + lp.Key + " → " + lp.Value + " KB/s");
        }
        foreach (var e in _pool) e.Start();
        if (!TunManager.Start(configPath, out error)) {
          Log("sing-box 启动失败: " + error);
          StopInternal();
          return false;
        }
        Log("sing-box TUN 已启动  " + SingboxConfigGenerator.TunGateway + "/30");
        IsRunning = true;
        IsTun = true;
        return true;
      } catch (Exception ex) {
        error = ex.Message;
        StopInternal();
        return false;
      }
    }

    static void StopInternal() {
      if (_engine != null) { _engine.Stop(); _engine = null; }
      foreach (var e in _pool) e.Stop();
      _pool.Clear();
      ClearLimiters();
      SystemProxyManager.Disable();
      TunManager.Stop();
      IsRunning = false;
      IsTun = false;
      TotalConnections = 0; TotalDownMbps = 0; TotalUpMbps = 0;
      foreach (var n in SelectedNics) { n.DownMbps = 0; n.UpMbps = 0; n.Connections = 0; }
    }

    public static void RefreshTotals() {
      TotalConnections = 0;
      if (_engine != null) TotalConnections += _engine.TotalConnections;
      foreach (var e in _pool) TotalConnections += e.TotalConnections;
      TotalDownMbps = SelectedNics.Sum(n => n.DownMbps);
      TotalUpMbps = SelectedNics.Sum(n => n.UpMbps);
    }

    static List<NicInfo> ClassifyEthernet(List<NicInfo> all) {
      var list = all.Where(n => !IsWifi(n)).ToList();
      return list.Count > 0 ? list : all;
    }

    static List<NicInfo> ClassifyWifi(List<NicInfo> all) {
      var list = all.Where(IsWifi).ToList();
      return list.Count > 0 ? list : all;
    }

    static bool IsWifi(NicInfo n) {
      if (n.IfType == 71) return true;
      string a = (n.Name ?? "").ToLowerInvariant();
      return a.Contains("wlan") || a.Contains("wi-fi") || a.Contains("wifi") ||
             a.Contains("wireless") || a.Contains("无线");
    }

    static void Log(string msg) => OnLog?.Invoke(msg);

    /// <summary>--selftest: 断言端口分配不变量 —— 每引擎占 {socks=N, http=N+1} 两端口,
    /// 池端口两两不交叠、不侵入 proxy 模式口与限速端口区。断言的是分配规则而非具体值,
    /// 以后调整步进/起始只需代码跟着过,不必改断言。</summary>
    public static string SelfCheck() {
      var pool = new[] { PoolEthPort, PoolWifiPort, PoolAggPort };
      var used = new HashSet<int>();
      foreach (var p in pool) {
        if (!used.Add(p)) return "FAIL Boost port dup (socks): " + p;
        if (!used.Add(p + 1)) return "FAIL Boost port http collides another socks: " + (p + 1);
      }
      if (used.Max() >= SingboxConfigGenerator.LimitPortBase)
        return "FAIL Boost pool ports intrude on limit-port range";
      if (used.Contains(SocksPort) || used.Contains(SocksPort + 1))
        return "FAIL Boost pool collides with proxy-mode port";
      return "PASS Boost port allocation disjoint";
    }
  }
}
