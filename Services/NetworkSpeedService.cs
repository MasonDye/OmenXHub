// NetworkSpeedService.cs - 网速实时监控
// 调用 NetworkInterface.GetIPv4Statistics() —— 与任务管理器相同的底层数据
using System;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;

namespace OmenSuperHub.Services {
  internal static class NetworkSpeedService {
    static NetworkInterface _iface;
    static long _prevDown, _prevUp;
    static DateTime _prevTime;
    static bool _init;
    static readonly object _lock = new();

    static void EnsureInit() {
      if (_init) return;
      lock (_lock) {
        if (_init) return;
        try { PickInterface(); } catch { }
        _init = true;
      }
    }

    static void PickInterface() {
      // 选取有网关、非回环、有流量的接口（与任务管理器逻辑一致）
      _iface = NetworkInterface.GetAllNetworkInterfaces()
          .Where(ni => {
            try {
              return ni.OperationalStatus == OperationalStatus.Up
                  && ni.NetworkInterfaceType != NetworkInterfaceType.Loopback;
            } catch { return false; }
          })
          .OrderByDescending(ni => {
            try {
              var gw = ni.GetIPProperties()?.GatewayAddresses;
              return gw?.Count > 0 ? 1 : 0;
            } catch { return 0; }
          })
          .ThenByDescending(ni => {
            try {
              var s = ni.GetIPv4Statistics();
              return s.BytesReceived + s.BytesSent;
            } catch { return 0L; }
          })
          .FirstOrDefault();

      if (_iface != null) {
        var stats = _iface.GetIPv4Statistics();
        _prevDown = (long)stats.BytesReceived;
        _prevUp = (long)stats.BytesSent;
        _prevTime = DateTime.UtcNow;
        Debug.WriteLine($"[NetworkSpeedService] 使用: {_iface.Name} ({_iface.Description})");
      } else {
        Debug.WriteLine("[NetworkSpeedService] 未找到合适的网络接口");
      }
    }

    public static bool IsAvailable {
      get { EnsureInit(); return _iface != null; }
    }

    public static (double downKBps, double upKBps) GetSpeed() {
      EnsureInit();
      if (_iface == null) return (0, 0);
      try {
        var stats = _iface.GetIPv4Statistics();
        long nowDown = (long)stats.BytesReceived;
        long nowUp = (long)stats.BytesSent;
        var now = DateTime.UtcNow;
        double secs = (now - _prevTime).TotalSeconds;
        if (secs <= 0) return (0, 0);
        // ponytail: counter may reset (adapter reinit); clamp to 0
        double down = nowDown >= _prevDown ? (nowDown - _prevDown) / secs / 1024.0 : 0;
        double up   = nowUp   >= _prevUp   ? (nowUp   - _prevUp)   / secs / 1024.0 : 0;
        _prevDown = nowDown;
        _prevUp = nowUp;
        _prevTime = now;
        return (Math.Max(0, down), Math.Max(0, up));
      } catch { return (0, 0); }
    }

    public static void Reset() {
      lock (_lock) { _iface = null; _init = false; }
    }
  }
}
