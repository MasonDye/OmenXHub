// CpuAffinity/EnforcementService.cs - 强制分派服务
// 参考 CpuAffinityManager.Enforcement.EnforcementService
// 按 level 分派：soft-cpu-sets(最低) / hard-affinity / job-enforced / job-locked(最高)
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace OmenSuperHub.Services.CpuAffinity {

  /// <summary>
  /// 中央强制服务：根据规则 Action.Level 分派到对应机制。
  /// </summary>
  public class EnforcementService {
    readonly CpuTopologyService _topoService;
    readonly JobObjectManager _jobManager;

    public EnforcementService(CpuTopologyService topoService, JobObjectManager jobManager) {
      _topoService = topoService;
      _jobManager = jobManager;
    }

    /// <summary>应用规则到指定 PID。按 level 分派，失败回退 hard-affinity。</summary>
    public bool Apply(int pid, RuleEntry rule, CpuTopology topology) {
      if (pid <= 0 || rule?.Action == null) return false;

      string mode = rule.Action.Mode;
      // socket 后缀
      if (rule.Action.SocketIndex.HasValue && rule.Action.SocketIndex.Value >= 0)
        mode += $"@socket{rule.Action.SocketIndex.Value}";

      ulong mask = CpuTopology.BuildMask(topology, mode, rule.Action.GetCustomMask());
      if (mask == 0) return false;

      bool ok = rule.Action.Level switch {
        "soft-cpu-sets" => ApplyCpuSets(pid, mask),
        "hard-affinity" => ApplyHardAffinity(pid, mask),
        "job-enforced" => ApplyJobEnforced(pid, mask, false) || ApplyHardAffinity(pid, mask),
        "job-locked" => ApplyJobEnforced(pid, mask, true) || ApplyHardAffinity(pid, mask),
        _ => ApplyHardAffinity(pid, mask)
      };

      // 优先级类（独立于亲和性）
      if (!string.IsNullOrEmpty(rule.Action.CpuPriority)) {
        ApplyPriority(pid, ParsePriority(rule.Action.CpuPriority));
      }
      // 内存优先级（独立于亲和性）
      int memPrio = ParseMemoryPriority(rule.Action.MemoryPriority);
      if (memPrio > 0) ApplyMemoryPriority(pid, memPrio);
      // 主线程绑定：把最忙线程绑定到掩码首核
      if (rule.Action.MainThreadBind) ApplyMainThreadBind(pid, mask);
      return ok;
    }

    /// <summary>恢复进程为 all-cores：Job 改全 + hard 设全 + 释放 Job 句柄。</summary>
    public bool Relax(int pid, CpuTopology topology) {
      if (pid <= 0) return false;
      ulong mask = CpuTopology.BuildMask(topology, "all-cores");
      if (mask == 0) return false;

      bool jobUpdated = false;
      IntPtr hJob = _jobManager.TryGetJob(pid);
      if (hJob != IntPtr.Zero) {
        jobUpdated = _jobManager.SetCpuAffinityLimit(hJob, mask);
        _jobManager.ReleaseJob(pid);
      }
      bool hardUpdated = ApplyHardAffinity(pid, mask);
      // ponytail: 无快照机制，恢复统一写内存优先级默认值 Normal(5)；主线程解绑由跟踪表驱动
      ApplyMemoryPriority(pid, 5);
      RelaxMainThread(pid);
      return jobUpdated || hardUpdated;
    }

    // ── soft-cpu-sets：NtSetInformationProcess(ProcessDefaultCpuSets = 0x42)，最低优先级 ──
    static bool ApplyCpuSets(int pid, ulong mask) {
      IntPtr hProc = OpenProcessForWrite(pid);
      if (hProc == IntPtr.Zero) return false;
      try {
        UIntPtr maskPtr = (UIntPtr)mask;
        int status = Ntdll.NtSetInformationProcess(hProc, PROCESS_INFORMATION_CLASS.ProcessDefaultCpuSets,
          ref maskPtr, (uint)IntPtr.Size);
        return status == 0;
      } finally { Ntdll.NtClose(hProc); }
    }

    // ── hard-affinity：NtSetInformationProcess(ProcessAffinityMask=0x15)，回退 kernel32 ──
    static bool ApplyHardAffinity(int pid, ulong mask) {
      IntPtr hProc = OpenProcessForWrite(pid);
      if (hProc == IntPtr.Zero) return false;
      try {
        UIntPtr maskPtr = (UIntPtr)mask;
        int status = Ntdll.NtSetInformationProcess(hProc, PROCESS_INFORMATION_CLASS.ProcessAffinityMask,
          ref maskPtr, (uint)IntPtr.Size);
        if (status == 0) return true;
        // 回退 kernel32
        return Kernel32.SetProcessAffinityMask(hProc, maskPtr);
      } finally { Ntdll.NtClose(hProc); }
    }

    // ── job-enforced：Job Object，唯一能阻止进程自行修改亲和性 ──
    bool ApplyJobEnforced(int pid, ulong mask, bool lockBreakaway) {
      TokenPrivileges.EnableDebugPrivilege();
      IntPtr hProc = OpenProcessForJob(pid);
      if (hProc == IntPtr.Zero) return false;
      try {
        IntPtr hJob = _jobManager.GetOrCreateJob(pid);
        if (hJob == IntPtr.Zero) return false;
        if (!_jobManager.SetCpuAffinityLimit(hJob, mask)) return false;
        // ponytail: lockBreakaway 实际由 Job 默认行为决定（不设 BREAKAWAY_OK 即拒绝脱离）
        return _jobManager.AssignProcess(hJob, hProc);
      } finally { Ntdll.NtClose(hProc); }
    }

    static void ApplyPriority(int pid, uint priorityClass) {
      if (priorityClass == 0) return;
      IntPtr hProc = OpenProcessForWrite(pid);
      if (hProc == IntPtr.Zero) return;
      try { Kernel32.SetPriorityClass(hProc, priorityClass); }
      finally { Kernel32.CloseHandle(hProc); }
    }

    // ── 内存优先级（SetProcessInformation(ProcessMemoryPriority)） ──

    /// <summary>1=VeryLow 2=Low 3=Medium 4=BelowNormal 5=Normal(默认)。&lt;1 或 &gt;5 不设置。</summary>
    static void ApplyMemoryPriority(int pid, int memPrio) {
      if (memPrio < 1 || memPrio > 5) return;
      IntPtr hProc = OpenProcessForWrite(pid);
      if (hProc == IntPtr.Zero) return;
      try {
        var info = new PROCESS_MEMORY_PRIORITY { MemoryPriority = (uint)memPrio };
        Kernel32.SetProcessInformation(hProc, Kernel32ProcessInfoClass.ProcessMemoryPriority,
          ref info, (uint)Marshal.SizeOf<PROCESS_MEMORY_PRIORITY>());
      } finally { Ntdll.NtClose(hProc); }
    }

    /// <summary>内存优先级名 → int；无效返回 -1（不设置）。</summary>
    public static int ParseMemoryPriority(string name) {
      if (string.IsNullOrEmpty(name)) return -1;
      switch (name.ToLowerInvariant()) {
        case "verylow": return 1;
        case "low": return 2;
        case "medium": return 3;
        case "belownormal": return 4;
        case "normal": return 5;
        default: return -1;
      }
    }

    // ── 主线程绑定（3×75ms 采样最忙线程） ──

    // 已绑定的主线程 tid 跟踪表（pid → tid），Relax 时解绑。进程退出后残留条目无害（Unbind 失败被吞）。
    static readonly ConcurrentDictionary<int, int> _boundMainThreads = new ConcurrentDictionary<int, int>();

    /// <summary>把进程最忙线程绑定到掩码的第一个核心。</summary>
    static void ApplyMainThreadBind(int pid, ulong mask) {
      int firstCore = LowestSetBit(mask);
      if (firstCore < 0) return;
      int tid = DetectMainThread(pid);
      if (tid <= 0) return;
      try {
        if (ThreadBindingService.BindThread(tid, 0, 1UL << firstCore))
          _boundMainThreads[pid] = tid;
      } catch { }
    }

    /// <summary>解绑此前绑定的主线程并清除跟踪。</summary>
    static void RelaxMainThread(int pid) {
      if (_boundMainThreads.TryRemove(pid, out int tid)) {
        try { ThreadBindingService.UnbindThread(tid); } catch { }
      }
    }

    /// <summary>3 轮 × 75ms 采样线程 CPU 时间，取至少 2 轮一致的最忙线程（稳定性判定）。</summary>
    // ponytail: 天花板 — 进程内线程全忙/均匀分布时最忙线程不稳定，绑定可能漂移。
    // 升级路径：主线程绑定后加跟踪循环周期重绑，此处仅依赖守护重应用路径。
    static int DetectMainThread(int pid) {
      try {
        int? winner = null;
        int agree = 0;
        for (int round = 0; round < 3; round++) {
          var t0 = SnapshotThreadCpu(pid);
          if (t0 == null) return -1;
          Thread.Sleep(75);
          var t1 = SnapshotThreadCpu(pid);
          if (t1 == null) return -1;
          int top = -1;
          double topDelta = -1;
          foreach (var kv in t1) {
            double d = kv.Value - (t0.TryGetValue(kv.Key, out long v) ? v : 0);
            if (d > topDelta) { topDelta = d; top = kv.Key; }
          }
          if (top <= 0) return -1;
          if (top == winner) agree++;
          else { winner = top; agree = 1; }
        }
        return agree >= 2 ? winner.Value : -1;
      } catch { return -1; }
    }

    static Dictionary<int, long> SnapshotThreadCpu(int pid) {
      try {
        using (var p = System.Diagnostics.Process.GetProcessById(pid)) {
          var dict = new Dictionary<int, long>();
          foreach (System.Diagnostics.ProcessThread pt in p.Threads) {
            try { dict[pt.Id] = pt.TotalProcessorTime.Ticks; } catch { }
          }
          return dict;
        }
      } catch { return null; }
    }

    static int LowestSetBit(ulong mask) {
      for (int i = 0; i < 64; i++) if ((mask & (1UL << i)) != 0) return i;
      return -1;
    }

    // ── 进程打开：先 kernel32，失败回退 NtOpenProcess 绕过 ACL ──
    static IntPtr OpenProcessForWrite(int pid) {
      IntPtr h = Kernel32.OpenProcess(
        ProcessAccess.PROCESS_SET_INFORMATION | ProcessAccess.PROCESS_QUERY_LIMITED_INFORMATION,
        false, (uint)pid);
      if (h != IntPtr.Zero) return h;
      return NtOpenProcessInternal(pid,
        ProcessAccess.PROCESS_SET_INFORMATION | ProcessAccess.PROCESS_QUERY_LIMITED_INFORMATION);
    }

    static IntPtr OpenProcessForJob(int pid) {
      // AssignProcessToJobObject 需 PROCESS_SET_QUOTA | PROCESS_TERMINATE
      IntPtr h = Kernel32.OpenProcess(
        ProcessAccess.PROCESS_TERMINATE | ProcessAccess.PROCESS_SET_QUOTA |
        ProcessAccess.PROCESS_SET_INFORMATION | ProcessAccess.PROCESS_QUERY_INFORMATION,
        false, (uint)pid);
      if (h != IntPtr.Zero) return h;
      return NtOpenProcessInternal(pid,
        ProcessAccess.PROCESS_TERMINATE | ProcessAccess.PROCESS_SET_QUOTA |
        ProcessAccess.PROCESS_SET_INFORMATION | ProcessAccess.PROCESS_QUERY_INFORMATION);
    }

    static IntPtr NtOpenProcessInternal(int pid, uint desiredAccess) {
      var oa = OBJECT_ATTRIBUTES.Create();
      var cid = new CLIENT_ID { UniqueProcess = (IntPtr)pid, UniqueThread = IntPtr.Zero };
      int status = Ntdll.NtOpenProcess(out IntPtr hProc, desiredAccess, ref oa, ref cid);
      return status == 0 ? hProc : IntPtr.Zero;
    }

    /// <summary>查询进程当前亲和性掩码。</summary>
    public ulong QueryAffinity(int pid) {
      TokenPrivileges.EnableDebugPrivilege();
      IntPtr h = Kernel32.OpenProcess(ProcessAccess.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)pid);
      if (h == IntPtr.Zero)
        h = NtOpenProcessInternal(pid, ProcessAccess.PROCESS_QUERY_LIMITED_INFORMATION);
      if (h == IntPtr.Zero) return 0;
      try {
        if (Kernel32.GetProcessAffinityMask(h, out IntPtr proc, out _))
          return (ulong)proc.ToInt64();
        return 0;
      } finally { Ntdll.NtClose(h); }
    }

    /// <summary>查询进程当前优先级类。</summary>
    public uint QueryPriority(int pid) {
      TokenPrivileges.EnableDebugPrivilege();
      IntPtr h = Kernel32.OpenProcess(ProcessAccess.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)pid);
      if (h == IntPtr.Zero)
        h = NtOpenProcessInternal(pid, ProcessAccess.PROCESS_QUERY_LIMITED_INFORMATION);
      if (h == IntPtr.Zero) return 0;
      try { return Kernel32.GetPriorityClass(h); }
      finally { Ntdll.NtClose(h); }
    }

    public static uint ParsePriority(string name) {
      if (string.IsNullOrEmpty(name)) return 0;
      switch (name.ToLowerInvariant()) {
        case "idle": return 0x40;
        case "belownormal": return 0x4000;
        case "normal": return 0x20;
        case "abovenormal": return 0x8000;
        case "high": return 0x80;
        case "realtime": return 0x100;
        default: return 0;
      }
    }
  }
}
