// CpuAffinity/Native.cs - 全部 P/Invoke 与结构体定义
// 参考 CpuAffinityManager.Native，集中 NT API、Job Object、Token、LogicalProcessor
using System;
using System.Runtime.InteropServices;

namespace OmenSuperHub.Services.CpuAffinity {

  // ══════════════════════════════════════
  //  NT 结构体
  // ══════════════════════════════════════

  [StructLayout(LayoutKind.Sequential)]
  public struct OBJECT_ATTRIBUTES {
    public uint Length;
    public IntPtr RootDirectory;
    public IntPtr ObjectName;
    public uint Attributes;
    public IntPtr SecurityDescriptor;
    public IntPtr SecurityQualityOfService;

    public static OBJECT_ATTRIBUTES Create() => new OBJECT_ATTRIBUTES {
      Length = (uint)Marshal.SizeOf<OBJECT_ATTRIBUTES>(),
      Attributes = 0x40 // OBJ_CASE_INSENSITIVE
    };
  }

  [StructLayout(LayoutKind.Sequential)]
  public struct CLIENT_ID {
    public IntPtr UniqueProcess;
    public IntPtr UniqueThread;
  }

  public enum PROCESS_INFORMATION_CLASS : uint {
    ProcessAffinityMask = 0x15,
    ProcessDefaultCpuSets = 0x42
  }

  // kernel32 SetProcessInformation 的进程信息类（与 ntdll PROCESS_INFORMATION_CLASS 不同枚举空间）
  public enum Kernel32ProcessInfoClass : uint {
    ProcessMemoryPriority = 2
  }

  public enum SYSTEM_INFORMATION_CLASS : uint {
    SystemCpuSetInformation = 0x49
  }

  // PROCESS_ACCESS 标志
  public static class ProcessAccess {
    public const uint PROCESS_TERMINATE = 0x0001;
    public const uint PROCESS_SET_QUOTA = 0x0100;
    public const uint PROCESS_SET_INFORMATION = 0x0200;
    public const uint PROCESS_QUERY_INFORMATION = 0x0400;
    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
  }

  // NT_STATUS 码
  public static class NtStatus {
    public const uint STATUS_SUCCESS = 0x00000000;
  }

  // ══════════════════════════════════════
  //  CPU Set 结构体（SystemCpuSetInformation）
  // ══════════════════════════════════════

  [StructLayout(LayoutKind.Sequential)]
  public struct SYSTEM_CPU_SET_INFORMATION {
    public uint Size;
    public CPU_SET_INFORMATION_TYPE Type;
    public CpuSetUnion CpuSet;

    [StructLayout(LayoutKind.Explicit)]
    public struct CpuSetUnion {
      [FieldOffset(0)]  public uint Id;
      [FieldOffset(4)]  public ushort Group;
      [FieldOffset(6)]  public byte LogicalProcessorIndex;
      [FieldOffset(7)]  public byte CoreIndex;
      [FieldOffset(8)]  public byte LastLevelCacheIndex;
      [FieldOffset(9)]  public byte NumaNodeIndex;
      [FieldOffset(10)] public byte EfficiencyClass;
      [FieldOffset(11)] public byte AllFlags;
      [FieldOffset(12)] public uint Reserved;
      [FieldOffset(16)] public ulong AllocationTag;
    }
  }

  public enum CPU_SET_INFORMATION_TYPE : uint {
    CpuSetInformation = 0
  }

  // ══════════════════════════════════════
  //  Logical Processor Information（EX API）
  //  ponytail: 用 GetLogicalProcessorInformationEx（条目自带 Size，遍历安全）。
  //  旧的非 EX API 结构体在 x64 上布局有 ULONG_PTR 对齐，曾导致字段错位；
  //  现统一用 EX API + 固定偏移解包（见 CpuTopologyService.EnumerateEx）。
  // ══════════════════════════════════════

  public enum LOGICAL_PROCESSOR_RELATIONSHIP : uint {
    RelationProcessorCore = 0,
    RelationNumaNode = 1,
    RelationCache = 2,
    RelationProcessorPackage = 3,
    RelationGroup = 4,
    RelationProcessorDie = 5,
    RelationProcessorModule = 7,
    RelationAll = 0xFFFF
  }

  // PROCESSOR_RELATIONSHIP / GROUP_AFFINITY 使用固定偏移解包（见 CpuTopologyService），不定义结构体。

  // ══════════════════════════════════════
  //  Job Object 结构体
  // ══════════════════════════════════════

  public enum JOBOBJECTINFOCLASS {
    JobObjectExtendedLimitInformation = 9
  }

  [StructLayout(LayoutKind.Sequential)]
  public struct JOBOBJECT_BASIC_LIMIT_INFORMATION {
    public long PerProcessUserTimeLimit;
    public long PerJobUserTimeLimit;
    public uint LimitFlags;
    public UIntPtr MinimumWorkingSetSize;
    public UIntPtr MaximumWorkingSetSize;
    public uint ActiveProcessLimit;
    public UIntPtr Affinity;
    public uint PriorityClass;
    public uint SchedulingClass;
  }

  [StructLayout(LayoutKind.Sequential)]
  public struct IO_COUNTERS {
    public ulong ReadOperationCount;
    public ulong WriteOperationCount;
    public ulong OtherOperationCount;
    public ulong ReadTransferCount;
    public ulong WriteTransferCount;
    public ulong OtherTransferCount;
  }

  [StructLayout(LayoutKind.Sequential)]
  public struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION {
    public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
    public IO_COUNTERS IoInfo;
    public UIntPtr ProcessMemoryLimit;
    public UIntPtr JobMemoryLimit;
    public UIntPtr PeakProcessMemoryUsed;
    public UIntPtr PeakJobMemoryUsed;
  }

  // 100ns 单位时间戳（GetProcessTimes 输出）
  [StructLayout(LayoutKind.Sequential)]
  public struct FILETIME {
    public uint Low;
    public uint High;
    /// <summary>无符号 100ns 计数，可直接相减得到时间差。</summary>
    public long Ticks100 => ((long)High << 32) | Low;
  }

  // SetProcessInformation(ProcessMemoryPriority) 输入结构
  [StructLayout(LayoutKind.Sequential)]
  public struct PROCESS_MEMORY_PRIORITY {
    public uint MemoryPriority;
  }

  public static class JobLimitFlags {
    public const uint JOB_OBJECT_LIMIT_AFFINITY = 0x0010;
  }

  // ══════════════════════════════════════
  //  Token 结构体
  // ══════════════════════════════════════

  [StructLayout(LayoutKind.Sequential)]
  public struct LUID {
    public uint LowPart;
    public int HighPart;
  }

  [StructLayout(LayoutKind.Sequential)]
  public struct LUID_AND_ATTRIBUTES {
    public LUID Luid;
    public uint Attributes;
  }

  [StructLayout(LayoutKind.Sequential)]
  public struct TOKEN_PRIVILEGES {
    public uint PrivilegeCount;
    public LUID_AND_ATTRIBUTES Privileges;

    public TOKEN_PRIVILEGES(LUID luid, uint attributes) {
      PrivilegeCount = 1;
      Privileges = new LUID_AND_ATTRIBUTES { Luid = luid, Attributes = attributes };
    }
  }

  public static class TokenPrivilegeAttributes {
    public const uint SE_PRIVILEGE_ENABLED = 0x00000002;
  }

  // ══════════════════════════════════════
  //  P/Invoke: kernel32
  // ══════════════════════════════════════

  public static class Kernel32 {
    const string K = "kernel32.dll";

    [DllImport(K, SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string lpName);

    [DllImport(K, SetLastError = true)]
    public static extern bool SetInformationJobObject(IntPtr hJob, JOBOBJECTINFOCLASS infoClass, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

    [DllImport(K, SetLastError = true)]
    public static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [DllImport(K, SetLastError = true)]
    public static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport(K, SetLastError = true)]
    public static extern bool SetProcessAffinityMask(IntPtr hProcess, UIntPtr dwProcessAffinityMask);

    [DllImport(K, SetLastError = true)]
    public static extern uint GetPriorityClass(IntPtr hProcess);

    [DllImport(K, SetLastError = true)]
    public static extern bool SetPriorityClass(IntPtr hProcess, uint dwPriorityClass);

    [DllImport(K, SetLastError = true)]
    public static extern bool GetProcessAffinityMask(IntPtr hProcess, out IntPtr lpProcessAffinityMask, out IntPtr lpSystemAffinityMask);

    // 进程 CPU 时间（kernel+user，100ns 单位）— CPU 占用率采样的两拍差分来源
    [DllImport(K, SetLastError = true)]
    public static extern bool GetProcessTimes(IntPtr hProcess, out FILETIME lpCreationTime, out FILETIME lpExitTime, out FILETIME lpKernelTime, out FILETIME lpUserTime);

    // 内存优先级（1=VeryLow 2=Low 3=Medium 4=BelowNormal 5=Normal）
    [DllImport(K, SetLastError = true)]
    public static extern bool SetProcessInformation(IntPtr hProcess, Kernel32ProcessInfoClass ProcessInformationClass, ref PROCESS_MEMORY_PRIORITY ProcessInformation, uint ProcessInformationSize);

    // ponytail: QueryFullProcessImageName 比 Process.MainModule.FileName 快得多且可访问受保护进程（仅需 QUERY_LIMITED_INFORMATION）
    [DllImport(K, SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool QueryFullProcessImageName(IntPtr hProcess, uint dwFlags, System.Text.StringBuilder lpExeName, ref uint lpdwSize);

    [DllImport(K, SetLastError = true)]
    public static extern bool CloseHandle(IntPtr hObject);

    [DllImport(K, SetLastError = true)]
    public static extern IntPtr GetCurrentProcess();

    [DllImport(K, SetLastError = true)]
    public static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

    // ponytail: EX 版本条目自带 Size 字段，遍历安全；RelationshipType 见 LOGICAL_PROCESSOR_RELATIONSHIP
    [DllImport(K, SetLastError = true)]
    public static extern bool GetLogicalProcessorInformationEx(uint RelationshipType, IntPtr Buffer, ref int ReturnedLength);

    // ponytail: GetFirmwareType 返回 1=Bios 2=Uefi，用于 UEFI 重启能力检测
    [DllImport(K, SetLastError = true)]
    public static extern bool GetFirmwareType(out uint firmwareType);

    [DllImport(K, SetLastError = true)]
    public static extern bool GetSystemCpuSetInformation(IntPtr Information, uint BufferLength, out uint ReturnedLength, IntPtr Process, uint Flags);

    public const uint TOKEN_QUERY = 0x0008;
    public const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
  }

  // ══════════════════════════════════════
  //  P/Invoke: advapi32
  // ══════════════════════════════════════

  public static class Advapi32 {
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool LookupPrivilegeValue(string lpSystemName, string lpName, out LUID lpLuid);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool AdjustTokenPrivileges(IntPtr TokenHandle, bool DisableAllPrivileges,
      ref TOKEN_PRIVILEGES NewState, uint BufferLength, IntPtr PreviousState, IntPtr ReturnLength);
  }

  // ══════════════════════════════════════
  //  P/Invoke: ntdll
  // ══════════════════════════════════════

  public static class Ntdll {
    [DllImport("ntdll.dll", SetLastError = true)]
    public static extern int NtSetInformationProcess(IntPtr ProcessHandle, PROCESS_INFORMATION_CLASS infoClass, ref UIntPtr ProcessInformation, uint ProcessInformationLength);

    [DllImport("ntdll.dll", SetLastError = true)]
    public static extern int NtOpenProcess(out IntPtr ProcessHandle, uint DesiredAccess, ref OBJECT_ATTRIBUTES ObjectAttributes, ref CLIENT_ID ClientId);

    [DllImport("ntdll.dll", SetLastError = true)]
    public static extern int NtClose(IntPtr Handle);
  }

  // ══════════════════════════════════════
  //  SeDebugPrivilege 启用
  // ══════════════════════════════════════

  /// <summary>
  /// 启用 SeDebugPrivilege，允许访问受保护进程。
  /// ponytail: 进程级，只需启用一次。需管理员权限（项目 manifest 已设 requireAdministrator）。
  /// </summary>
  public static class TokenPrivileges {
    static bool _enabled;
    static readonly object _lock = new object();

    public static bool EnableDebugPrivilege() {
      lock (_lock) {
        if (_enabled) return true;
        if (!Kernel32.OpenProcessToken(Kernel32.GetCurrentProcess(),
            Kernel32.TOKEN_QUERY | Kernel32.TOKEN_ADJUST_PRIVILEGES, out IntPtr hToken))
          return false;
        try {
          if (!Advapi32.LookupPrivilegeValue(null, "SeDebugPrivilege", out LUID luid)) return false;
          var tp = new TOKEN_PRIVILEGES(luid, TokenPrivilegeAttributes.SE_PRIVILEGE_ENABLED);
          bool ok = Advapi32.AdjustTokenPrivileges(hToken, false, ref tp,
            (uint)Marshal.SizeOf<TOKEN_PRIVILEGES>(), IntPtr.Zero, IntPtr.Zero);
          // ponytail: AdjustTokenPrivileges 返回 true 不代表成功 — 检查 ERROR_NOT_ALL_ASSIGNED (1300)
          if (!ok) return false;
          int err = Marshal.GetLastWin32Error();
          if (err == 1300) return false;
          _enabled = true;
          return true;
        } finally { Kernel32.CloseHandle(hToken); }
      }
    }
  }
}
