// CpuTopologyService.cs — CPU 拓扑检测
// 使用 GetLogicalProcessorInformationEx + GetSystemCpuSetInformation
// 检测核心拓扑（CCD/CCX/效率等级/缓存分组）
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace OmenSuperHub.Services {
  public struct CoreInfo {
    public int LogicalIndex;      // 全局逻辑处理器索引
    public int Group;             // 处理器组号
    public int CoreIndex;         // 物理核心索引
    public int EfficiencyClass;   // 0=P-core, 1+=E-core (hybrid), -1=unknown
    public int CcdId;             // CCD ID (AMD), -1 = N/A
    public int CcxId;             // CCX ID (AMD), -1 = N/A
    public bool IsSmt;            // 是否 SMT 线程
    public int L3CacheId;         // L3 cache 分组 ID
    public bool IsPerformance => EfficiencyClass == 0;
    public bool IsEfficiency => EfficiencyClass >= 1;
  }

  internal static class CpuTopologyService {
    static List<CoreInfo> _cachedCores;
    static string _cachedSummary;
    static readonly object _lock = new();

    // ── Win32 structs ──
    enum LOGICAL_PROCESSOR_RELATIONSHIP {
      RelationProcessorCore = 0,
      RelationNumaNode = 1,
      RelationCache = 2,
      RelationProcessorPackage = 3,
      RelationGroup = 4,
      RelationProcessorDie = 5,
      RelationNumaNodeEx = 6,
      RelationProcessorModule = 7,
    }

    enum CACHE_TYPE { CacheUnified = 0, CacheInstruction = 1, CacheData = 2, CacheTrace = 3 }

    [StructLayout(LayoutKind.Sequential)]
    struct SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX {
      public int Relationship; // LOGICAL_PROCESSOR_RELATIONSHIP
      public int Size;
      // followed by union
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PROCESSOR_RELATIONSHIP {
      public byte Flags; // bit 0 = SMT
      public byte EfficiencyClass;
      public byte Reserved1;
      public byte Reserved2;
      public int GroupCount;
      // followed by GROUP_AFFINITY[GroupCount]
    }

    [StructLayout(LayoutKind.Sequential)]
    struct GROUP_AFFINITY {
      public ulong Mask;
      public ushort Group;
      public ushort Reserved1;
      public ushort Reserved2;
      public ushort Reserved3;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct CACHE_RELATIONSHIP {
      public byte Level;
      public byte Associativity;
      public ushort LineSize;
      public int CacheSize;
      public int Type; // CACHE_TYPE
      // followed by GROUP_AFFINITY[GroupCount]
    }

    [StructLayout(LayoutKind.Sequential)]
    struct NUMA_NODE_RELATIONSHIP {
      public uint NodeNumber;
      // followed by GROUP_AFFINITY[GroupCount]
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetLogicalProcessorInformationEx(
        int relationshipType, IntPtr buffer, ref int returnedLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetSystemCpuSetInformation(
        IntPtr info, int infoLength, out int returnedLength,
        IntPtr process, uint flags);

    struct SYSTEM_CPU_SET_INFORMATION {
      public uint Size;
      public short CpuSetId;     // Actually ushort but we use short for alignment
      public short Group;
      public byte LogicalProcessorIndex;
      public byte CoreIndex;
      public byte EfficiencyClass;
      public byte Reserved;
      public ulong AllFlags;     // bit 0 = SMT, bit 1 = Parked, bit 2 = Allocated
      public short RealTimeBudget;
      public short Reserved2;
      public int Reserved3;
    }

    const int CpuSetInformationType = 2;

    /// <summary>获取完整核心拓扑。结果已缓存，首次调用约 1ms。</summary>
    public static List<CoreInfo> GetCores() {
      if (_cachedCores != null) return _cachedCores;
      lock (_lock) {
        if (_cachedCores != null) return _cachedCores;
        _cachedCores = DetectCores();
        return _cachedCores;
      }
    }

    public static string GetSummary() {
      if (_cachedSummary != null) return _cachedSummary;
      var cores = GetCores();
      int total = cores.Count;
      int smt = cores.Count(c => c.IsSmt);
      int pCores = cores.Count(c => c.IsPerformance && !c.IsSmt);
      int eCores = cores.Count(c => c.IsEfficiency && !c.IsSmt);
      var ccds = cores.Where(c => c.CcdId >= 0).Select(c => c.CcdId).Distinct().ToList();
      string ccdInfo = ccds.Count > 0 ? $" | CCD×{ccds.Count}" : "";
      string hybridInfo = eCores > 0 ? $" | P{pCores}+E{eCores}" : "";
      _cachedSummary = $"{total} 线程 ({total - smt} 物理核){hybridInfo}{ccdInfo}";
      return _cachedSummary;
    }

    /// <summary>清除缓存（在 CPU 热插拔后调用）</summary>
    public static void Reset() { lock (_lock) { _cachedCores = null; _cachedSummary = null; } }

    static List<CoreInfo> DetectCores() {
      var cores = new List<CoreInfo>();
      try {
        // 方法1: GetSystemCpuSetInformation (Win10+, 最精确的 CPU 拓扑)
        var cpuSets = GetSystemCpuSets();
        if (cpuSets.Count > 0) {
          // CPU set 已经包含所有信息: 组、核心索引、效率等级、SMT 状态
          for (int i = 0; i < cpuSets.Count; i++) {
            var cs = cpuSets[i];
            cores.Add(new CoreInfo {
              LogicalIndex = i,
              Group = cs.Group,
              CoreIndex = cs.CoreIndex,
              EfficiencyClass = cs.EfficiencyClass > 0 ? cs.EfficiencyClass : 0,
              IsSmt = (cs.AllFlags & 1) != 0,
              CcdId = -1,
              CcxId = -1,
              L3CacheId = -1,
            });
          }
          // AMD: 用 NUMA 拓扑补充 CCD 信息
          if (HasAmdCpu()) {
            var ccdMap = GetAmdCcdMapFromNuma();
            var l3Map = GetL3CacheGroupMap();
            for (int i = 0; i < cores.Count; i++) {
              var c = cores[i];
              if (ccdMap.TryGetValue(c.LogicalIndex, out int ccd))
                c.CcdId = ccd;
              if (l3Map.TryGetValue(c.LogicalIndex, out int l3))
                c.L3CacheId = l3;
              cores[i] = c;
            }
          }
          return cores;
        }

        // 方法2: GetLogicalProcessorInformationEx (fallback)
        cores = DetectFromLogicalProcessorInfo();
      } catch (Exception ex) {
        Debug.WriteLine($"[CpuTopology] 检测失败: {ex.Message}");
      }
      if (cores.Count == 0) {
        // 最终 fallback: 使用 Environment.ProcessorCount
        for (int i = 0; i < Environment.ProcessorCount; i++)
          cores.Add(new CoreInfo { LogicalIndex = i, CoreIndex = i, EfficiencyClass = 0, IsSmt = false });
      }
      return cores;
    }

    static List<(short Group, short CoreIndex, byte EfficiencyClass, ulong AllFlags)> GetSystemCpuSets() {
      var result = new List<(short, short, byte, ulong)>();
      int bufSize = 0;
      GetSystemCpuSetInformation(IntPtr.Zero, 0, out bufSize, IntPtr.Zero, 0);
      if (bufSize <= 0) return result;
      IntPtr buf = Marshal.AllocHGlobal(bufSize);
      try {
        if (!GetSystemCpuSetInformation(buf, bufSize, out bufSize, IntPtr.Zero, 0))
          return result;
        int offset = 0;
        while (offset < bufSize) {
          var header = Marshal.PtrToStructure<SYSTEM_CPU_SET_INFORMATION>(IntPtr.Add(buf, offset));
          if (header.Size == 0) break;
          // CPU set type ID 在结构体开头前4字节 — 我们读 Size 前的 Type 字段
          int type = Marshal.ReadInt32(IntPtr.Add(buf, offset));
          if (type == CpuSetInformationType) {
            result.Add(((short)header.Group, (short)header.CoreIndex, header.EfficiencyClass, header.AllFlags));
          }
          offset += (int)header.Size;
        }
      } finally { Marshal.FreeHGlobal(buf); }
      return result;
    }

    static List<CoreInfo> DetectFromLogicalProcessorInfo() {
      var cores = new List<CoreInfo>();
      var smtFlags = new HashSet<int>(); // 哪些逻辑索引是 SMT
      var efficiencyMap = new Dictionary<int, int>(); // 逻辑索引 → 效率等级
      var groupAffs = new List<(int idx, GROUP_AFFINITY aff)>();

      // 第一遍: 枚举 RelationProcessorCore
      int bufSize = 0;
      GetLogicalProcessorInformationEx((int)LOGICAL_PROCESSOR_RELATIONSHIP.RelationProcessorCore,
          IntPtr.Zero, ref bufSize);
      if (bufSize <= 0) return cores;
      IntPtr buf = Marshal.AllocHGlobal(bufSize);
      try {
        if (!GetLogicalProcessorInformationEx((int)LOGICAL_PROCESSOR_RELATIONSHIP.RelationProcessorCore,
            buf, ref bufSize))
          return cores;

        int offset = 0;
        int coreIdx = 0;
        while (offset < bufSize) {
          var header = Marshal.PtrToStructure<SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX>(
              IntPtr.Add(buf, offset));
          if (header.Size == 0) break;
          if (header.Relationship == (int)LOGICAL_PROCESSOR_RELATIONSHIP.RelationProcessorCore) {
            var procRel = Marshal.PtrToStructure<PROCESSOR_RELATIONSHIP>(
                IntPtr.Add(buf, offset + Marshal.SizeOf<SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX>()));
            bool isSmt = (procRel.Flags & 1) != 0;
            // 读取 GROUP_AFFINITY 数组
            int affOffset = offset + Marshal.SizeOf<SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX>()
                + Marshal.SizeOf<PROCESSOR_RELATIONSHIP>();
            for (int g = 0; g < procRel.GroupCount; g++) {
              var aff = Marshal.PtrToStructure<GROUP_AFFINITY>(IntPtr.Add(buf, affOffset));
              // 解码 mask 中的每个位
              for (int bit = 0; bit < 64; bit++) {
                if ((aff.Mask & (1UL << bit)) != 0) {
                  int logIdx = groupAffs.Count;
                  groupAffs.Add((logIdx, aff));
                  if (isSmt) smtFlags.Add(logIdx);
                  efficiencyMap[logIdx] = procRel.EfficiencyClass;
                }
              }
              affOffset += Marshal.SizeOf<GROUP_AFFINITY>();
            }
            coreIdx++;
          }
          offset += header.Size;
        }

        // 构建 CoreInfo
        foreach (var (logIdx, aff) in groupAffs) {
          cores.Add(new CoreInfo {
            LogicalIndex = logIdx,
            Group = aff.Group,
            CoreIndex = -1, // 从 CPU set 才能知道物理核心索引
            EfficiencyClass = efficiencyMap.TryGetValue(logIdx, out int eff) ? eff : 0,
            IsSmt = smtFlags.Contains(logIdx),
            CcdId = -1,
            CcxId = -1,
            L3CacheId = -1,
          });
        }
      } finally { Marshal.FreeHGlobal(buf); }

      return cores;
    }

    static Dictionary<int, int> GetAmdCcdMapFromNuma() {
      var map = new Dictionary<int, int>();
      int bufSize = 0;
      GetLogicalProcessorInformationEx((int)LOGICAL_PROCESSOR_RELATIONSHIP.RelationNumaNode,
          IntPtr.Zero, ref bufSize);
      if (bufSize <= 0) return map;
      IntPtr buf = Marshal.AllocHGlobal(bufSize);
      try {
        if (!GetLogicalProcessorInformationEx((int)LOGICAL_PROCESSOR_RELATIONSHIP.RelationNumaNode,
            buf, ref bufSize))
          return map;
        int offset = 0;
        while (offset < bufSize) {
          var header = Marshal.PtrToStructure<SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX>(
              IntPtr.Add(buf, offset));
          if (header.Size == 0) break;
          if (header.Relationship == (int)LOGICAL_PROCESSOR_RELATIONSHIP.RelationNumaNode) {
            var numa = Marshal.PtrToStructure<NUMA_NODE_RELATIONSHIP>(
                IntPtr.Add(buf, offset + Marshal.SizeOf<SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX>()));
            int affOffset = offset + Marshal.SizeOf<SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX>()
                + Marshal.SizeOf<NUMA_NODE_RELATIONSHIP>();
            var aff = Marshal.PtrToStructure<GROUP_AFFINITY>(IntPtr.Add(buf, affOffset));
            for (int bit = 0; bit < 64; bit++) {
              if ((aff.Mask & (1UL << bit)) != 0)
                map[bit] = (int)numa.NodeNumber;
            }
          }
          offset += header.Size;
        }
      } finally { Marshal.FreeHGlobal(buf); }
      return map;
    }

    static Dictionary<int, int> GetL3CacheGroupMap() {
      var map = new Dictionary<int, int>();
      int bufSize = 0;
      GetLogicalProcessorInformationEx((int)LOGICAL_PROCESSOR_RELATIONSHIP.RelationCache,
          IntPtr.Zero, ref bufSize);
      if (bufSize <= 0) return map;
      IntPtr buf = Marshal.AllocHGlobal(bufSize);
      try {
        if (!GetLogicalProcessorInformationEx((int)LOGICAL_PROCESSOR_RELATIONSHIP.RelationCache,
            buf, ref bufSize))
          return map;
        int offset = 0;
        int l3Id = 0;
        while (offset < bufSize) {
          var header = Marshal.PtrToStructure<SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX>(
              IntPtr.Add(buf, offset));
          if (header.Size == 0) break;
          if (header.Relationship == (int)LOGICAL_PROCESSOR_RELATIONSHIP.RelationCache) {
            var cache = Marshal.PtrToStructure<CACHE_RELATIONSHIP>(
                IntPtr.Add(buf, offset + Marshal.SizeOf<SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX>()));
            if (cache.Level == 3) {
              int affOffset = offset + Marshal.SizeOf<SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX>()
                  + Marshal.SizeOf<CACHE_RELATIONSHIP>();
              var aff = Marshal.PtrToStructure<GROUP_AFFINITY>(IntPtr.Add(buf, affOffset));
              for (int bit = 0; bit < 64; bit++) {
                if ((aff.Mask & (1UL << bit)) != 0)
                  map[bit] = l3Id;
              }
              l3Id++;
            }
          }
          offset += header.Size;
        }
      } finally { Marshal.FreeHGlobal(buf); }
      return map;
    }

    static bool _isAmd;
    static bool _vendorChecked;
    static bool HasAmdCpu() {
      if (!_vendorChecked) {
        _isAmd = OmenHardware.HasAmdCpu();
        _vendorChecked = true;
      }
      return _isAmd;
    }
  }
}
