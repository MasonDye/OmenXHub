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
      // ponytail: 解耦懒分配 —— 不再在 cctor 无条件 AllocHGlobal。cctor 在首次访问本类型
      // 任意成员时触发(含 RestoreEcoQos 里 SetEnabled(false)),导致用户从未启用 EcoQoS 也分配
      // 两块非托管内存并 marshal。改为首启用 Start() 时才分配,Cleanup() 释放。
    }

    // ponytail: 首次真正启用节流时才分配非托管缓冲,未启用功能不占非托管内存。
    static void EnsureNativeBuffers() {
      if (pThrottleOn != IntPtr.Zero) return;
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
        if (_disposed) return;   // Cleanup 后禁止重启,防止新 tick 使用已释放缓冲
        _stopGen++;              // 作废在途 StopCore(它醒来看 gen 变化即返回,不再销毁 timer)
        EnsureNativeBuffers();   // 懒分配,未启用时不分配
        _stopping = false;
        if (_throttleTimer == null) {
          _throttleTimer = new Timer(_ => ThrottleTick(), null, 0, 2000);
        } else {
          // timer 存活但可能已被 StopCore 停用(Change Infinite):所有权已移交本方法,
          // 必须重新激活,否则 StopCore 作废返回后留下永久停用的 timer = 静默失效。
          _throttleTimer.Change(0, 2000);
        }
      }
    }

    // ponytail: Stop/Cleanup 与在飞回调的生命周期协议 ——
    //   tick 侧:拿到 _tickRunning 单飞资格后持锁登记 _currentTicks++ 并快照 _epoch;
    //     _stopping/epoch 过期则在入口与枚举循环检查点让路(不触碰字典/缓冲);
    //     finally 先还单飞资格,再持锁 _currentTicks-- + PulseAll(顺序见 finally 注释)。
    //   Stop/Cleanup 侧(StopCore,持 _lock;Monitor.Wait 排空期间会临时释放该锁,醒来
    //     先检查 _stopGen 是否被 Start 推进 —— 被推进即作废退出,timer 所有权已移交 Start):
    //     置 _stopping+epoch++ → timer.Change(Infinite) → Wait 排空(10s 超时)。
    //     排空成功→恢复进程状态+Clear+Dispose timer+置 null;
    //     超时→同样 Dispose+置 null(销毁不等待回调,不产生新竞态;留一个已停用的 timer
    //     给 Start 反而会造成"重新启用后无周期节流"的静默失效),但不做恢复/Clear,置
    //     _leakBuffers 让 Cleanup 跳过 FreeHGlobal(宁泄两块结构体不 UAF)。
    //   恢复遍历在锁内做 OpenProcess(原实现同样如此,非回退);排空等待期间
    //     调用方(如 SetEnabled(false),可能来自 UI 线程)最长阻塞 10s —— 只在病态卡死时。
    static void StopCore(bool disposing) {
      for (int attempt = 0; ; attempt++) {
      int genAtStart;
      bool aborted = false;
      lock (_lock) {
        genAtStart = _stopGen;
        _stopping = true;
        _epoch++;                              // 已进入工作体的 tick 在循环检查点提前让路
        _throttleTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        var deadline = System.Diagnostics.Stopwatch.GetTimestamp() + (long)(System.Diagnostics.Stopwatch.Frequency * 10);
        while (true) {
          // 每次从 Wait 醒来(或首次进入)都先查代际:Start 在锁内改 _stopGen 本身就证明
          // 它拿到过锁,本方重新持锁后必然可见 —— 不依赖 PulseAll 唤醒。
          if (_stopGen != genAtStart) { aborted = true; break; }   // 被 Start 作废:timer 所有权已移交
          if (Volatile.Read(ref _currentTicks) == 0) break;
          if (!Monitor.Wait(_lock, 100) && System.Diagnostics.Stopwatch.GetTimestamp() >= deadline) {
            // 超时同样要再过一遍代际检查才能落入超时分支 —— 否则销毁的是 Start 刚重新
            // 激活的 timer(启用状态 true 但无周期回调的静默失效)。
            if (_stopGen != genAtStart) { aborted = true; break; }
            break;
          }
        }
        if (!aborted) {
          bool drained = Volatile.Read(ref _currentTicks) == 0;
          if (!drained) {
            _leakBuffers = true;
            Logger.Error("[EcoQos] 10s 内节流回调未退出:跳过进程恢复与状态清理,禁止释放非托管缓冲(避免 UAF)");
          } else {
            foreach (var kv in _pidState) {
              if (kv.Value) ApplyEcoQos(kv.Key, false);
            }
            _pidState.Clear();
          }
          // timer 必销毁置 null:超时路径留用=静默失效(见上);排空成功路径必须 Dispose。
          _throttleTimer?.Dispose();
          _throttleTimer = null;
          if (disposing) {
            _disposed = true;
            if (!_leakBuffers) {
              if (pThrottleOn != IntPtr.Zero) { Marshal.FreeHGlobal(pThrottleOn); pThrottleOn = IntPtr.Zero; }
              if (pThrottleOff != IntPtr.Zero) { Marshal.FreeHGlobal(pThrottleOff); pThrottleOff = IntPtr.Zero; }
            }
          }
        }
      }
      if (aborted && disposing && attempt == 0) {
        // Cleanup 被并发的 Start 作废:绝不半途释放缓冲与 _disposed,完整重跑一轮停止。
        Logger.Warn("[EcoQos] Cleanup 被 Start 打断,重跑完整停止");
        continue;
      }
      // 非 abort(正常完成)、Stop 被作废(Start 已接管 timer)、或 Cleanup 二次被打断
      // (用户仍在快速切换,放弃本轮,进程退出时 OS 回收) —— 都到此为止。
      if (aborted && disposing)
        Logger.Error("[EcoQos] Cleanup 再次被 Start 打断,放弃本轮清理(进程退出时由 OS 回收)");
      return;
      }
    }

    public static void Stop() => StopCore(false);

    public static void Cleanup() {
      StopCore(true);
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
    static int _currentTicks;     // 在飞 tick 计数;所有权在 _lock(tick 登记/自减均持锁,Stop 在锁上 Wait)
    static long _epoch;           // Stop/Cleanup 每轮 +1;tick 入口快照,过期在检查点让路
    static int _stopGen;          // Start 递增作废旧停止流程;读写均持 _lock
    static bool _disposed;        // Cleanup 后拒绝 Start(),防新 tick 触碰已释放缓冲
    static bool _leakBuffers;     // Stop 等待超时后置位:缓冲仍可能被在飞回调引用
    static volatile bool _stopping; // Stop 开始即置位:tick 入口/循环检查点据此让路

    static bool TargetThrottle(uint pid, string name, bool shouldThrottle, uint fgPid) {
      if (Blacklist.Contains(name)) return true;
      if (!shouldThrottle) return false;
      if (Whitelist.Contains(name)) return false;
      if (fgPid > 0 && pid == fgPid) return false;
      return true;
    }

    static void ThrottleTick() {
      // ponytail: skip if previous tick still running — prevents ThreadPool inflation
      // _tickRunning Exchange 是"至多一个在飞 tick"契约:_currentTicks 只在抢到资格的
      // 线程里增减(无锁配对),Stop 的排空等待依赖该契约。改动本方法不得引入绕过
      // Exchange 的第二条 tick 入口,否则 Stop 会提前放行。
      if (Interlocked.Exchange(ref _tickRunning, 1) != 0) return;
      long myEpoch;
      lock (_lock) {
        if (_disposed) { Interlocked.Exchange(ref _tickRunning, 0); return; }
        _currentTicks++;
        myEpoch = _epoch;
      }
      bool localStopping = _stopping;
      try {
        // Stop/Cleanup 已置位或 epoch 过期:已入队/迟到的回调在此让路(不触碰 pid 状态/缓冲)
        if (localStopping) return;

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
            // 循环检查点:整轮枚举可达数秒,Stop 中途到来时尽早让路,缩短排空等待
            if (_stopping || Volatile.Read(ref _epoch) != myEpoch) return;
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
      } catch (Exception ex) {
        // ponytail: 线程池 Timer 回调里逃逸的异常在 net481 会终止进程 —— searcher.Get()/
        // WMI 枚举推进/SystemInformation 等均可抛(循环体内的 catch 只兜单条进程记录)。
        // 本轮 tick 作废,下一轮照常;计数归还交给 finally。
        Logger.Error("[EcoQos] 节流回调异常(本轮跳过): " + ex.Message);
      } finally {
        // 工作体已结束，先释放单飞资格，再持锁递减在飞计数并通知等待者。
        // 两步之间新 tick 可能登记；Stop 必须重新检查计数和代际，不能仅凭通知认定排空。
        Interlocked.Exchange(ref _tickRunning, 0);
        lock (_lock) { _currentTicks--; Monitor.PulseAll(_lock); }
      }
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
