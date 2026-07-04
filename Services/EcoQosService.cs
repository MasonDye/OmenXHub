// EcoQosService.cs - 进程功耗节流管理
// 使用 ProcessPowerThrottling API 控制前台/后台进程的功耗限制，支持白名单/黑名单
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;

namespace OmenSuperHub.Services {
  public static class EcoQosService {
    // ─── P/Invoke ────────────────────────────────────────────
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool SetProcessInformation(IntPtr hProcess, PROCESS_INFORMATION_CLASS processInformationClass,
        IntPtr processInformation, uint processInformationSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool SetPriorityClass(IntPtr hProcess, uint dwPriorityClass);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool CloseHandle(IntPtr hObject);

    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    enum PROCESS_INFORMATION_CLASS {
      ProcessMemoryPriority,
      ProcessMemoryExhaustionInfo,
      ProcessAppMemoryInfo,
      ProcessInPrivateInfo,
      ProcessPowerThrottling,
      ProcessReservedValue1,
      ProcessTelemetryCoverageInfo,
      ProcessProtectionLevelInfo,
      ProcessLeapSecondInfo,
      ProcessInformationClassMax,
    }

    [Flags]
    enum ProcessorPowerThrottlingFlags : uint {
      None = 0,
      PROCESS_POWER_THROTTLING_EXECUTION_SPEED = 0x1,
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PROCESS_POWER_THROTTLING_STATE {
      public const uint CURRENT_VERSION = 1;
      public uint Version;
      public ProcessorPowerThrottlingFlags ControlMask;
      public ProcessorPowerThrottlingFlags StateMask;
    }

    const uint PROCESS_SET_INFORMATION = 0x0200;
    const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    const uint IDLE_PRIORITY_CLASS = 0x40;
    const uint NORMAL_PRIORITY_CLASS = 0x20;

    static readonly int szBlock = Marshal.SizeOf<PROCESS_POWER_THROTTLING_STATE>();
    static IntPtr pThrottleOn;
    static IntPtr pThrottleOff;
    static Timer _throttleTimer;
    static readonly object _lock = new object();

    // ─── Config ──────────────────────────────────────────────
    public static bool IsEnabled { get; private set; }
    public static bool ThrottleWhenPluggedIn { get; set; }
    public static HashSet<string> Whitelist { get; private set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public static HashSet<string> Blacklist { get; private set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    static EcoQosService() {
      var throttleOn = new PROCESS_POWER_THROTTLING_STATE {
        Version = PROCESS_POWER_THROTTLING_STATE.CURRENT_VERSION,
        ControlMask = ProcessorPowerThrottlingFlags.PROCESS_POWER_THROTTLING_EXECUTION_SPEED,
        StateMask = ProcessorPowerThrottlingFlags.PROCESS_POWER_THROTTLING_EXECUTION_SPEED,
      };
      var throttleOff = new PROCESS_POWER_THROTTLING_STATE {
        Version = PROCESS_POWER_THROTTLING_STATE.CURRENT_VERSION,
        ControlMask = ProcessorPowerThrottlingFlags.PROCESS_POWER_THROTTLING_EXECUTION_SPEED,
        StateMask = ProcessorPowerThrottlingFlags.None,
      };
      pThrottleOn = Marshal.AllocHGlobal(szBlock);
      pThrottleOff = Marshal.AllocHGlobal(szBlock);
      Marshal.StructureToPtr(throttleOn, pThrottleOn, false);
      Marshal.StructureToPtr(throttleOff, pThrottleOff, false);
    }

    public static void Initialize(bool enabled, bool throttlePlugged, string whitelistStr, string blacklistStr) {
      IsEnabled = enabled;
      ThrottleWhenPluggedIn = throttlePlugged;
      Whitelist = new HashSet<string>(
        whitelistStr.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
          .Select(s => s.Trim()).Where(s => s.Length > 0),
        StringComparer.OrdinalIgnoreCase);
      Blacklist = new HashSet<string>(
        blacklistStr.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
          .Select(s => s.Trim()).Where(s => s.Length > 0),
        StringComparer.OrdinalIgnoreCase);
      if (enabled) Start();
      else Stop();
    }

    public static void Start() {
      lock (_lock) {
        if (_throttleTimer == null) {
          _throttleTimer = new Timer(_ => ThrottleTick(), null, 0, 2000);
        }
      }
    }

    public static void Stop() {
      lock (_lock) {
        _throttleTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        _throttleTimer?.Dispose();
        _throttleTimer = null;
      }
    }

    public static void Cleanup() {
      Stop();
      if (pThrottleOn != IntPtr.Zero) { Marshal.FreeHGlobal(pThrottleOn); pThrottleOn = IntPtr.Zero; }
      if (pThrottleOff != IntPtr.Zero) { Marshal.FreeHGlobal(pThrottleOff); pThrottleOff = IntPtr.Zero; }
    }

    public static void SetEnabled(bool enabled) {
      IsEnabled = enabled;
      ConfigService.EcoQosEnabled = enabled;
      ConfigService.Save("EcoQosEnabled");
      if (enabled) Start();
      else Stop();
    }

    public static void SetThrottlePlugged(bool val) {
      ThrottleWhenPluggedIn = val;
      ConfigService.EcoQosThrottlePlugged = val;
      ConfigService.Save("EcoQosThrottlePlugged");
    }

    public static void SaveWhitelist(string text) {
      Whitelist = new HashSet<string>(
        text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
          .Select(s => s.Trim()).Where(s => s.Length > 0),
        StringComparer.OrdinalIgnoreCase);
      ConfigService.EcoQosWhitelist = string.Join("\n", Whitelist);
      ConfigService.Save("EcoQosWhitelist");
    }

    public static void SaveBlacklist(string text) {
      Blacklist = new HashSet<string>(
        text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
          .Select(s => s.Trim()).Where(s => s.Length > 0),
        StringComparer.OrdinalIgnoreCase);
      ConfigService.EcoQosBlacklist = string.Join("\n", Blacklist);
      ConfigService.Save("EcoQosBlacklist");
    }

    static readonly Dictionary<uint, bool> _pidState = new Dictionary<uint, bool>();
    static int _tickRunning;

    static bool TargetThrottle(uint pid, string name, bool shouldThrottle, uint fgPid) {
      if (Blacklist.Contains(name)) return true;
      if (!shouldThrottle) return false;
      if (Whitelist.Contains(name)) return false;
      if (fgPid > 0 && pid == fgPid) return false;
      return true;
    }

    static void ThrottleTick() {
      // ponytail: skip if previous tick still running — prevents ThreadPool inflation
      if (Interlocked.Exchange(ref _tickRunning, 1) != 0) return;
      try {
        uint fgPid = 0;
        try {
          var fgHwnd = GetForegroundWindow();
          if (fgHwnd != IntPtr.Zero) GetWindowThreadProcessId(fgHwnd, out fgPid);
        } catch { }

        bool onBattery = System.Windows.Forms.SystemInformation.PowerStatus.PowerLineStatus ==
                         System.Windows.Forms.PowerLineStatus.Offline;
        bool shouldThrottle = IsEnabled && (onBattery || ThrottleWhenPluggedIn);

        int sessionId;
        int myId;
        using (var self = Process.GetCurrentProcess()) {
          sessionId = self.SessionId;
          myId = self.Id;
        }

        // ponytail: WMI query fetches only PID/Name/SessionId instead of full Process objects
        using (var searcher = new ManagementObjectSearcher(
            $"SELECT ProcessId, Name, SessionId FROM Win32_Process"))
        using (var procs = searcher.Get()) {
          var currentPids = new HashSet<uint>();
          foreach (ManagementObject obj in procs) {
            try {
              uint id = Convert.ToUInt32(obj["ProcessId"]);
              if (id == myId) continue;
              int sid = Convert.ToInt32(obj["SessionId"]);
              if (sid != sessionId) continue;
              string name = (obj["Name"] as string) ?? "";
              string lower = name.ToLowerInvariant();
              currentPids.Add(id);

              bool target = TargetThrottle(id, lower, shouldThrottle, fgPid);
              if (_pidState.TryGetValue(id, out var prev) && prev == target) continue;

              ApplyEcoQos(id, target);
              _pidState[id] = target;
            } catch { }
          }
          // Cleanup stale PIDs when cache grows significantly
          if (_pidState.Count > currentPids.Count * 2 + 100) {
            foreach (var pid in _pidState.Keys.Where(k => !currentPids.Contains(k)).ToArray())
              _pidState.Remove(pid);
          }
        }
      } finally { Interlocked.Exchange(ref _tickRunning, 0); }
    }

    static void ApplyEcoQos(uint pid, bool enable) {
      IntPtr hProcess = OpenProcess(PROCESS_SET_INFORMATION | PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
      if (hProcess == IntPtr.Zero) return;
      try {
        SetProcessInformation(hProcess, PROCESS_INFORMATION_CLASS.ProcessPowerThrottling,
            enable ? pThrottleOn : pThrottleOff, (uint)szBlock);
        SetPriorityClass(hProcess, enable ? IDLE_PRIORITY_CLASS : NORMAL_PRIORITY_CLASS);
      } finally {
        CloseHandle(hProcess);
      }
    }
  }
}
