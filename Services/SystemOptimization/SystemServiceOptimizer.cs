// SystemServiceOptimizer.cs - Windows 服务优化
// SCM 枚举服务 → 查看启动类型（自动/手动/禁用）→ 修改 + 失败回滚
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace OmenSuperHub.Services.SystemOptimization {

  /// <summary>服务当前状态（值 = SERVICE_STATUS dwCurrentState）。</summary>
  public enum ServiceState {
    Unknown = 0, Stopped = 1, StartPending = 2, StopPending = 3,
    Running = 4, ContinuePending = 5, PausePending = 6, Paused = 7
  }

  /// <summary>启动类型（值 = QUERY_SERVICE_CONFIG dwStartType）。</summary>
  public enum ServiceStartupType {
    Unknown = -1, Boot = 0, System = 1, Automatic = 2, Manual = 3, Disabled = 4
  }

  public sealed class ServiceItem {
    public string Name { get; set; }
    public string DisplayName { get; set; }
    public ServiceState State { get; set; }
    public ServiceStartupType StartupType { get; set; }
    /// <summary>仅自动/手动/禁用可改（Boot/System 驱动服务不可改）。</summary>
    public bool CanChange { get; set; }
  }

  public static class SystemServiceOptimizer {

    // ── SCM 常量 ──
    const uint SC_MANAGER_CONNECT = 0x0001;
    const uint SC_MANAGER_ENUMERATE_SERVICE = 0x0004;
    const uint SC_MANAGER_ALL_ACCESS = 0xF003F;
    const uint SERVICE_QUERY_CONFIG = 0x0001;
    const uint SERVICE_QUERY_STATUS = 0x0004;
    const uint SERVICE_CHANGE_CONFIG = 0x0002;
    const int SC_ENUM_PROCESS_INFO = 0;
    const uint SERVICE_WIN32 = 0x30;
    const uint SERVICE_STATE_ALL = 3;
    const int ERROR_MORE_DATA = 234;
    const int ERROR_INSUFFICIENT_BUFFER = 122;

    // ── SCM P/Invoke ──
    static class Scm {
      [StructLayout(LayoutKind.Sequential)]
      public struct SERVICE_STATUS_PROCESS {
        public uint dwServiceType;
        public uint dwCurrentState;
        public uint dwControlsAccepted;
        public uint dwWin32ExitCode;
        public uint dwServiceSpecificExitCode;
        public uint dwCheckPoint;
        public uint dwWaitHint;
        public uint dwProcessId;
        public uint dwServiceFlags;
      }

      [StructLayout(LayoutKind.Sequential)]
      public struct ENUM_SERVICE_STATUS_PROCESS {
        public IntPtr lpServiceName;
        public IntPtr lpDisplayName;
        public SERVICE_STATUS_PROCESS ServiceStatusProcess;
      }

      [StructLayout(LayoutKind.Sequential)]
      public struct QUERY_SERVICE_CONFIG {
        public uint dwServiceType;
        public uint dwStartType;
        public uint dwErrorControl;
        public IntPtr lpBinaryPathName;
        public IntPtr lpLoadOrderGroup;
        public uint dwTagId;
        public IntPtr lpDependencies;
        public IntPtr lpServiceStartName;
        public IntPtr lpDisplayName;
      }

      [DllImport("Advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "OpenSCManager", SetLastError = true)]
      public static extern IntPtr OpenSCManager(string machine, string database, uint access);

      [DllImport("Advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "OpenService", SetLastError = true)]
      public static extern IntPtr OpenService(IntPtr hManager, string serviceName, uint access);

      [DllImport("Advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "EnumServicesStatusEx", SetLastError = true)]
      [return: MarshalAs(UnmanagedType.Bool)]
      public static extern bool EnumServicesStatusEx(IntPtr hManager, int infoLevel, uint serviceType, uint serviceState,
        IntPtr buffer, uint bufferSize, out uint bytesNeeded, out uint servicesReturned, ref uint resumeHandle, string groupName);

      [DllImport("Advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "QueryServiceConfig", SetLastError = true)]
      [return: MarshalAs(UnmanagedType.Bool)]
      public static extern bool QueryServiceConfig(IntPtr hService, IntPtr buffer, uint bufferSize, out uint bytesNeeded);

      [DllImport("Advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "ChangeServiceConfig", SetLastError = true)]
      [return: MarshalAs(UnmanagedType.Bool)]
      public static extern bool ChangeServiceConfig(IntPtr hService, uint dwServiceType, uint dwStartType, uint dwErrorControl,
        string lpBinaryPathName, string lpLoadOrderGroup, IntPtr lpdwTagId, string lpDependencies,
        string lpServiceStartName, string lpPassword, string lpDisplayName);

      [DllImport("Advapi32.dll", EntryPoint = "CloseServiceHandle", SetLastError = true)]
      [return: MarshalAs(UnmanagedType.Bool)]
      public static extern bool CloseServiceHandle(IntPtr handle);
    }

    // ── 枚举 ──

    public static List<ServiceItem> Enumerate() {
      var result = new List<ServiceItem>();
      IntPtr manager = Scm.OpenSCManager(null, null, SC_MANAGER_CONNECT | SC_MANAGER_ENUMERATE_SERVICE);
      if (manager == IntPtr.Zero) return result;
      try {
        // 第一次调用拿缓冲区大小（期望 ERROR_MORE_DATA=234）
        uint bytesNeeded = 0, count = 0, resume = 0;
        Scm.EnumServicesStatusEx(manager, SC_ENUM_PROCESS_INFO, SERVICE_WIN32, SERVICE_STATE_ALL,
          IntPtr.Zero, 0, out bytesNeeded, out count, ref resume, null);
        int firstErr = Marshal.GetLastWin32Error();
        if (bytesNeeded == 0 && firstErr != ERROR_MORE_DATA) return result;

        IntPtr buffer = Marshal.AllocHGlobal((int)bytesNeeded);
        try {
          resume = 0;
          if (!Scm.EnumServicesStatusEx(manager, SC_ENUM_PROCESS_INFO, SERVICE_WIN32, SERVICE_STATE_ALL,
            buffer, bytesNeeded, out bytesNeeded, out count, ref resume, null)) return result;

          int structSize = Marshal.SizeOf<Scm.ENUM_SERVICE_STATUS_PROCESS>();
          for (int i = 0; i < count; i++) {
            var item = Marshal.PtrToStructure<Scm.ENUM_SERVICE_STATUS_PROCESS>(IntPtr.Add(buffer, i * structSize));
            string name = Marshal.PtrToStringUni(item.lpServiceName) ?? "";
            if (string.IsNullOrWhiteSpace(name)) continue;
            string display = Marshal.PtrToStringUni(item.lpDisplayName) ?? name;
            int startType = QueryStartType(manager, name);
            result.Add(new ServiceItem {
              Name = name,
              DisplayName = display,
              State = (ServiceState)item.ServiceStatusProcess.dwCurrentState,
              StartupType = (ServiceStartupType)startType,
              CanChange = startType >= 2 && startType <= 4
            });
          }
        } finally { Marshal.FreeHGlobal(buffer); }
      } finally { Scm.CloseServiceHandle(manager); }
      return result.OrderBy(s => s.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>查询单个服务当前启动类型；失败返回 (int)Unknown。</summary>
    static int QueryStartType(IntPtr manager, string serviceName) {
      IntPtr h = Scm.OpenService(manager, serviceName, SERVICE_QUERY_CONFIG);
      if (h == IntPtr.Zero) return (int)ServiceStartupType.Unknown;
      try {
        uint needed = 0;
        Scm.QueryServiceConfig(h, IntPtr.Zero, 0, out needed);
        int err = Marshal.GetLastWin32Error();
        if (needed == 0 || err != ERROR_INSUFFICIENT_BUFFER) return (int)ServiceStartupType.Unknown;
        IntPtr buffer = Marshal.AllocHGlobal((int)needed);
        try {
          if (!Scm.QueryServiceConfig(h, buffer, needed, out needed)) return (int)ServiceStartupType.Unknown;
          var config = Marshal.PtrToStructure<Scm.QUERY_SERVICE_CONFIG>(buffer);
          return (int)config.dwStartType;
        } finally { Marshal.FreeHGlobal(buffer); }
      } finally { Scm.CloseServiceHandle(h); }
    }

    // ── 一键优化 ──

    /// <summary>推荐优化方案（安全项）：非必需服务自动→手动（按需启动）或禁用。
    /// 游戏本场景最常被关闭的遥测/同步/Xbox 服务，全部可随时手动恢复。</summary>
    public static readonly (string Name, ServiceStartupType Target)[] RecommendedPreset = {
      ("DiagTrack", ServiceStartupType.Disabled),        // 连接用户体验与遥测
      ("dmwappushservice", ServiceStartupType.Disabled), // 设备管理 WAP 推送
      ("SysMain", ServiceStartupType.Manual),            // Superfetch（SSD 场景可关）
      ("WSearch", ServiceStartupType.Manual),            // Windows 搜索索引
      ("WerSvc", ServiceStartupType.Manual),             // Windows 错误报告
      ("PcaSvc", ServiceStartupType.Manual),             // 程序兼容性助手
      ("MapsBroker", ServiceStartupType.Manual),         // 地图下载
      ("WMPNetworkSvc", ServiceStartupType.Manual),      // 媒体共享
      ("Fax", ServiceStartupType.Disabled),              // 传真
      ("RetailDemo", ServiceStartupType.Disabled),       // 零售演示
      ("lfsvc", ServiceStartupType.Manual),              // 地理位置
      ("NcbService", ServiceStartupType.Manual),         // 网络连接助手
      ("WpnService", ServiceStartupType.Manual),         // 推送通知
      ("TabletInputService", ServiceStartupType.Manual), // 触摸键盘
      ("XblAuthManager", ServiceStartupType.Manual),     // Xbox 认证
      ("XblGameSave", ServiceStartupType.Manual),        // Xbox 存档
      ("XboxNetApiSvc", ServiceStartupType.Manual),      // Xbox 网络
      ("XboxGipSvc", ServiceStartupType.Manual)          // Xbox 输入外设
    };

    /// <summary>恢复方案：一键优化涉及的服务恢复到常见系统默认启动类型。</summary>
    public static readonly (string Name, ServiceStartupType Target)[] DefaultPreset = {
      ("DiagTrack", ServiceStartupType.Automatic),       // 遥测
      ("dmwappushservice", ServiceStartupType.Automatic),// 设备管理 WAP 推送
      ("SysMain", ServiceStartupType.Automatic),         // Superfetch
      ("WSearch", ServiceStartupType.Automatic),         // Windows 搜索索引
      ("WerSvc", ServiceStartupType.Manual),             // Windows 错误报告
      ("PcaSvc", ServiceStartupType.Automatic),          // 程序兼容性助手
      ("MapsBroker", ServiceStartupType.Automatic),      // 地图下载
      ("WMPNetworkSvc", ServiceStartupType.Manual),      // 媒体共享
      ("Fax", ServiceStartupType.Disabled),              // 传真
      ("RetailDemo", ServiceStartupType.Disabled),       // 零售演示
      ("lfsvc", ServiceStartupType.Manual),              // 地理位置
      ("NcbService", ServiceStartupType.Manual),         // 网络连接助手
      ("WpnService", ServiceStartupType.Automatic),      // 推送通知
      ("TabletInputService", ServiceStartupType.Manual), // 触摸键盘
      ("XblAuthManager", ServiceStartupType.Manual),     // Xbox 认证
      ("XblGameSave", ServiceStartupType.Manual),        // Xbox 存档
      ("XboxNetApiSvc", ServiceStartupType.Manual),      // Xbox 网络
      ("XboxGipSvc", ServiceStartupType.Manual)          // Xbox 输入外设
    };

    /// <summary>批量应用一键优化推荐方案。</summary>
    public static (int Applied, int AlreadyOptimal, int Skipped, int Failed, List<string> Failures) ApplyRecommendedPreset()
      => ApplyPreset(RecommendedPreset);

    /// <summary>批量恢复系统默认服务启动类型。</summary>
    public static (int Applied, int AlreadyOptimal, int Skipped, int Failed, List<string> Failures) ApplyDefaultPreset()
      => ApplyPreset(DefaultPreset);

    /// <summary>批量应用服务预设。每个服务独立校验+失败回滚，单项失败不影响其他项。</summary>
    static (int Applied, int AlreadyOptimal, int Skipped, int Failed, List<string> Failures) ApplyPreset(
        (string Name, ServiceStartupType Target)[] preset) {
      int applied = 0, already = 0, skipped = 0, failed = 0;
      var failures = new List<string>();
      IntPtr manager = Scm.OpenSCManager(null, null, SC_MANAGER_ALL_ACCESS);
      if (manager == IntPtr.Zero) {
        return (0, 0, 0, preset.Length, preset.Select(r => r.Name).ToList());
      }
      try {
        foreach (var (name, target) in preset) {
          int before = QueryStartType(manager, name);
          if (before < 2 || before > 4) { skipped++; continue; } // 服务不存在或不可改
          if (before == (int)target) { already++; continue; }
          IntPtr h = Scm.OpenService(manager, name, SERVICE_CHANGE_CONFIG);
          if (h == IntPtr.Zero) { skipped++; continue; }
          try {
            bool ok = Scm.ChangeServiceConfig(h, uint.MaxValue, (uint)target, uint.MaxValue,
              null, null, IntPtr.Zero, null, null, null, null);
            int after = QueryStartType(manager, name);
            if (ok && after == (int)target) {
              applied++;
            } else {
              if (after != before)
                Scm.ChangeServiceConfig(h, uint.MaxValue, (uint)before, uint.MaxValue,
                  null, null, IntPtr.Zero, null, null, null, null);
              failed++;
              failures.Add(name);
            }
          } finally { Scm.CloseServiceHandle(h); }
        }
      } finally { Scm.CloseServiceHandle(manager); }
      return (applied, already, skipped, failed, failures);
    }

    // ── 修改 ──

    /// <summary>修改服务启动类型；成功后校验，失败自动回滚。</summary>
    public static bool SetStartupType(string serviceName, ServiceStartupType target) {
      if (string.IsNullOrWhiteSpace(serviceName) || serviceName.IndexOf('\\') >= 0 || serviceName.IndexOf('/') >= 0) return false;
      if (target != ServiceStartupType.Automatic && target != ServiceStartupType.Manual && target != ServiceStartupType.Disabled) return false;

      IntPtr manager = Scm.OpenSCManager(null, null, SC_MANAGER_ALL_ACCESS);
      if (manager == IntPtr.Zero) return false;
      try {
        int before = QueryStartType(manager, serviceName);
        if (before < 2 || before > 4) return false; // 不可改或查询失败
        if (before == (int)target) return true;

        IntPtr h = Scm.OpenService(manager, serviceName, SERVICE_CHANGE_CONFIG);
        if (h == IntPtr.Zero) return false;
        try {
          bool ok = Scm.ChangeServiceConfig(h, uint.MaxValue, (uint)target, uint.MaxValue,
            null, null, IntPtr.Zero, null, null, null, null);
          // 校验 + 失败回滚
          int after = QueryStartType(manager, serviceName);
          if (ok && after == (int)target) return true;
          if (after != before)
            Scm.ChangeServiceConfig(h, uint.MaxValue, (uint)before, uint.MaxValue,
              null, null, IntPtr.Zero, null, null, null, null);
          return false;
        } finally { Scm.CloseServiceHandle(h); }
      } finally { Scm.CloseServiceHandle(manager); }
    }
  }
}
