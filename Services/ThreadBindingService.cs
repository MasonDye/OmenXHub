// ThreadBindingService.cs — 实时线程/进程核心绑定
// 使用 SetThreadGroupAffinity + SetProcessDefaultCpuSets 实现即时生效
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace OmenSuperHub.Services {
  internal static class ThreadBindingService {
    // ── P/Invoke ──
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr OpenThread(uint dwDesiredAccess, bool bInheritHandle, uint dwThreadId);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool SetThreadGroupAffinity(IntPtr hThread, ref GROUP_AFFINITY groupAffinity, IntPtr previousGroupAffinity);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetThreadGroupAffinity(IntPtr hThread, out GROUP_AFFINITY groupAffinity);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool SetThreadIdealProcessorEx(IntPtr hThread, ref PROCESSOR_NUMBER idealProcessor, IntPtr previousIdealProcessor);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool SetProcessDefaultCpuSets(IntPtr hProcess, ulong[] cpuSetIds, uint count);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool SetProcessDefaultCpuSetMasks(IntPtr hProcess, ref GROUP_AFFINITY mask, uint count);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetProcessDefaultCpuSets(IntPtr hProcess, ulong[] cpuSetIds, uint count, out uint requiredCount);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr GetCurrentProcess();

    [StructLayout(LayoutKind.Sequential)]
    struct GROUP_AFFINITY {
      public ulong Mask;
      public ushort Group;
      public ushort Reserved1;
      public ushort Reserved2;
      public ushort Reserved3;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PROCESSOR_NUMBER {
      public ushort Group;
      public byte Number;
      public byte Reserved;
    }

    const uint THREAD_SET_LIMITED_INFORMATION = 0x0400;
    const uint THREAD_QUERY_LIMITED_INFORMATION = 0x0800;
    const uint THREAD_SET_INFORMATION = 0x0020;
    const uint THREAD_QUERY_INFORMATION = 0x0040;
    const uint PROCESS_SET_INFORMATION = 0x0200;

    static ulong _allCoresMask;
    static ushort _groupCount;

    static void EnsureInit() {
      if (_allCoresMask == 0) {
        _groupCount = (ushort)Math.Max(1, (int)GetActiveProcessorGroupCount());
        for (int g = 0; g < _groupCount; g++)
          _allCoresMask |= GetAllAffinityForGroup(g);
      }
    }

    static ulong GetAllAffinityForGroup(int group) {
      ulong mask = 0;
      int count = GetActiveProcessorCount((ushort)group);
      for (int i = 0; i < count && i < 64; i++) mask |= (1UL << i);
      return mask;
    }

    [DllImport("kernel32.dll")]
    static extern ushort GetActiveProcessorGroupCount();

    [DllImport("kernel32.dll")]
    static extern int GetActiveProcessorCount(ushort groupNumber);

    /// <summary>将线程绑定到指定组 + 掩码的核心</summary>
    public static bool BindThread(int threadId, ushort group, ulong affinityMask) {
      IntPtr hThread = OpenThread(THREAD_SET_LIMITED_INFORMATION | THREAD_QUERY_LIMITED_INFORMATION, false, (uint)threadId);
      if (hThread == IntPtr.Zero) return false;
      try {
        var aff = new GROUP_AFFINITY { Group = group, Mask = affinityMask };
        return SetThreadGroupAffinity(hThread, ref aff, IntPtr.Zero);
      } finally { CloseHandle(hThread); }
    }

    /// <summary>解绑线程（恢复全核心）</summary>
    public static bool UnbindThread(int threadId) {
      EnsureInit();
      return BindThread(threadId, 0, _allCoresMask);
    }

    /// <summary>将进程的所有线程绑定到指定组+掩码</summary>
    public static int BindProcess(int processId, ushort group, ulong affinityMask) {
      int count = 0;
      foreach (int tid in EnumThreads(processId)) {
        if (BindThread(tid, group, affinityMask)) count++;
      }

      // 也尝试设置进程默认 CPU Sets (Win10+)
      try {
        IntPtr hProcess = OpenProcess(PROCESS_SET_INFORMATION, false, processId);
        if (hProcess != IntPtr.Zero) {
          try {
            var aff = new GROUP_AFFINITY { Group = group, Mask = affinityMask };
            SetProcessDefaultCpuSetMasks(hProcess, ref aff, 1);
          } finally { CloseHandle(hProcess); }
        }
      } catch { }

      return count;
    }

    /// <summary>解绑进程所有线程（恢复全核心）</summary>
    public static int UnbindProcess(int processId) {
      EnsureInit();
      return BindProcess(processId, 0, _allCoresMask);
    }

    /// <summary>将进程绑定到指定 CCD 的所有核心</summary>
    public static int BindProcessToCcd(int processId, int ccdId) {
      var cores = CpuTopologyService.GetCores()
          .Where(c => c.CcdId == ccdId)
          .ToList();
      if (cores.Count == 0) return 0;
      return BindCoreList(processId, cores);
    }

    /// <summary>将进程绑定到指定效率等级的核心</summary>
    public static int BindProcessToEfficiencyClass(int processId, bool performance) {
      var cores = CpuTopologyService.GetCores()
          .Where(c => performance ? c.IsPerformance : c.IsEfficiency)
          .ToList();
      if (cores.Count == 0) return 0;
      return BindCoreList(processId, cores);
    }

    /// <summary>将进程绑定到指定索引列表的核心</summary>
    public static int BindProcessToCoreIndices(int processId, int[] coreIndices) {
      var set = new HashSet<int>(coreIndices);
      var cores = CpuTopologyService.GetCores()
          .Where(c => set.Contains(c.LogicalIndex))
          .ToList();
      return BindCoreList(processId, cores);
    }

    static int BindCoreList(int processId, List<CoreInfo> cores) {
      if (cores.Count == 0) return 0;
      // 按组分组构建掩码
      var groupMask = new Dictionary<ushort, ulong>();
      foreach (var c in cores) {
        if (!groupMask.ContainsKey((ushort)c.Group))
          groupMask[(ushort)c.Group] = 0;
        groupMask[(ushort)c.Group] |= (1UL << c.LogicalIndex);
      }
      int count = 0;
      foreach (int tid in EnumThreads(processId)) {
        foreach (var kv in groupMask) {
          if (BindThread(tid, kv.Key, kv.Value)) count++;
        }
      }
      return count;
    }

    [DllImport("kernel32.dll")]
    static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    /// <summary>枚举进程的所有线程 ID</summary>
    public static List<int> EnumThreads(int processId) {
      var tids = new List<int>();
      IntPtr h = CreateToolhelp32Snapshot(4/*TH32CS_SNAPTHREAD*/, 0);
      if (h == (IntPtr)(-1)) return tids;
      try {
        var te = new THREADENTRY32 { dwSize = Marshal.SizeOf<THREADENTRY32>() };
        if (Thread32First(h, ref te)) {
          do {
            if (te.th32OwnerProcessID == processId)
              tids.Add(te.th32ThreadID);
          } while (Thread32Next(h, ref te));
        }
      } finally { CloseHandle(h); }
      return tids;
    }

    [DllImport("kernel32.dll")]
    static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll")]
    static extern bool Thread32First(IntPtr hSnapshot, ref THREADENTRY32 lpte);

    [DllImport("kernel32.dll")]
    static extern bool Thread32Next(IntPtr hSnapshot, ref THREADENTRY32 lpte);

    [StructLayout(LayoutKind.Sequential)]
    struct THREADENTRY32 {
      public int dwSize;
      public int cntUsage;
      public int th32ThreadID;
      public uint th32OwnerProcessID;
      public int tpBasePri;
      public int tpDeltaPri;
      public uint dwFlags;
    }
  }
}
