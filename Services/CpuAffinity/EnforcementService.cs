// CpuAffinity/EnforcementService.cs - 强制分派服务
// 参考 CpuAffinityManager.Enforcement.EnforcementService
// 按 level 分派：soft-cpu-sets(最低) / hard-affinity / job-enforced / job-locked(最高)
using System;
using System.Runtime.InteropServices;

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
