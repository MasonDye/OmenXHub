// CpuAffinity/JobObjectManager.cs - Job Object 生命周期管理
// 参考 CpuAffinityManager.Enforcement.JobObjectManager
// Job Object 是内核对象，进程被分配后无法用 SetProcessAffinityMask 越权改回亲和性
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace OmenSuperHub.Services.CpuAffinity {

  /// <summary>
  /// 管理 Job Object 句柄。命名保证跨进程重启可重新 attach。
  /// ponytail: 句柄在 _pidToJob 持有，进程退出时需 ReleaseJob 清理。
  /// </summary>
  public class JobObjectManager : IDisposable {
    readonly object _lock = new object();
    readonly Dictionary<int, IntPtr> _pidToJob = new Dictionary<int, IntPtr>();
    bool _disposed;

    /// <summary>创建或取回该 PID 对应的命名 Job Object。</summary>
    public IntPtr GetOrCreateJob(int pid) {
      lock (_lock) {
        if (_disposed) return IntPtr.Zero;
        if (_pidToJob.TryGetValue(pid, out IntPtr existing)) return existing;
        string name = $"OmenSuperHub_CoreKeep_Job_{pid}";
        IntPtr hJob = Kernel32.CreateJobObject(IntPtr.Zero, name);
        if (hJob != IntPtr.Zero) _pidToJob[pid] = hJob;
        return hJob;
      }
    }

    /// <summary>在 Job Object 上设置 CPU 亲和性限制。</summary>
    public bool SetCpuAffinityLimit(IntPtr hJob, ulong mask) {
      if (hJob == IntPtr.Zero) return false;
      var limits = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION {
        BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION {
          LimitFlags = JobLimitFlags.JOB_OBJECT_LIMIT_AFFINITY,
          Affinity = (UIntPtr)mask
        }
      };
      int size = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
      IntPtr ptr = Marshal.AllocHGlobal(size);
      try {
        Marshal.StructureToPtr(limits, ptr, false);
        return Kernel32.SetInformationJobObject(hJob, JOBOBJECTINFOCLASS.JobObjectExtendedLimitInformation, ptr, (uint)size);
      } finally { Marshal.FreeHGlobal(ptr); }
    }

    /// <summary>分配进程到 Job Object。</summary>
    public bool AssignProcess(IntPtr hJob, IntPtr hProcess) {
      if (hJob == IntPtr.Zero || hProcess == IntPtr.Zero) return false;
      return Kernel32.AssignProcessToJobObject(hJob, hProcess);
    }

    /// <summary>查询 PID 是否已有 Job（不创建）。返回 IntPtr.Zero 表示无。</summary>
    public IntPtr TryGetJob(int pid) {
      lock (_lock) return _pidToJob.TryGetValue(pid, out IntPtr h) ? h : IntPtr.Zero;
    }

    /// <summary>进程退出时清理对应 Job。</summary>
    public void ReleaseJob(int pid) {
      lock (_lock) {
        if (_pidToJob.TryGetValue(pid, out IntPtr hJob)) {
          Kernel32.CloseHandle(hJob);
          _pidToJob.Remove(pid);
        }
      }
    }

    /// <summary>释放所有 Job 句柄（关闭功能/退出时调用）。</summary>
    public void ReleaseAll() {
      lock (_lock) {
        foreach (var h in _pidToJob.Values) Kernel32.CloseHandle(h);
        _pidToJob.Clear();
      }
    }

    public void Dispose() {
      lock (_lock) {
        if (_disposed) return;
        _disposed = true;
        foreach (var h in _pidToJob.Values) Kernel32.CloseHandle(h);
        _pidToJob.Clear();
      }
    }
  }
}
