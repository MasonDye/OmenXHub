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

    // ponytail: winnt.h 真实布局是 Flags@0 EfficiencyClass@1 Reserved[20]@2 GroupCount(WORD)@22，
    // GROUP_AFFINITY[] 从 @24 起。旧声明把 GroupCount 放在 @4（读到保留字节 0 → 循环零次、
    // 本路径恒空），偏移注释见 CpuAffinity/AffinityTopology.EnumerateEx 同一族修复。
    [StructLayout(LayoutKind.Sequential)]
    struct PROCESSOR_RELATIONSHIP {
      public byte Flags; // bit 0 = SMT
      public byte EfficiencyClass;
      [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
      public byte[] Reserved;
      public ushort GroupCount;
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

    // winnt.h: Level@0 Assoc@1 LineSize@2 CacheSize@4 Type@8 Reserved@12 GROUP_AFFINITY@16。
    // 旧版漏声明 Reserved，SizeOf=12 让 GROUP_AFFINITY 偏移手算错 4 字节。
    [StructLayout(LayoutKind.Sequential)]
    struct CACHE_RELATIONSHIP {
      public byte Level;
      public byte Associativity;
      public ushort LineSize;
      public int CacheSize;
      public int Type; // CACHE_TYPE
      public uint Reserved;
      public GROUP_AFFINITY GroupAffinity;
    }

    // winnt.h: NodeNumber(DWORD) + GROUP_AFFINITY(@8 对齐,即偏移 8)。声明带 GroupAffinity
    // 字段让 Marshal 自动补对齐 pad —— 旧版只声明 uint 再手算 SizeOf 会得 12,漏掉 4 字节 pad。
    [StructLayout(LayoutKind.Sequential)]
    struct NUMA_NODE_RELATIONSHIP {
      public uint NodeNumber;
      public GROUP_AFFINITY GroupAffinity;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetLogicalProcessorInformationEx(
        int relationshipType, IntPtr buffer, ref int returnedLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetSystemCpuSetInformation(
        IntPtr info, int infoLength, out int returnedLength,
        IntPtr process, uint flags);

    // ponytail: 直接按 winnt.h 固定偏移读 (SYSTEM_CPU_SET_INFORMATION: Size@0 Type@4,
    // union: Id@8 Group@12 LPI@14 CoreIndex@15 LLC@16 NumaNode@17 EfficiencyClass@18
    // AllFlags@19)。旧声明字段序与原生布局错位(且注释"bit0=SMT"实为 RealTime 位),
    // Type 又误读偏移 0 的 Size —— 本路径曾恒返回空。偏移族与 CpuAffinity/AffinityTopology
    // .QueryCpuSetEfficiency 同一基准实现。
    static List<(int Lpi, int Core, byte Eff)> GetSystemCpuSets() {
      var result = new List<(int Lpi, int Core, byte Eff)>();
      int bufSize = 0;
      GetSystemCpuSetInformation(IntPtr.Zero, 0, out bufSize, IntPtr.Zero, 0);
      if (bufSize <= 0) return result;
      IntPtr buf = Marshal.AllocHGlobal(bufSize);
      try {
        if (!GetSystemCpuSetInformation(buf, bufSize, out bufSize, IntPtr.Zero, 0))
          return result;
        int offset = 0;
        while (offset + 20 <= bufSize) {
          uint size = (uint)Marshal.ReadInt32(buf, offset);
          if (size == 0) break;
          if (Marshal.ReadInt32(buf, offset + 4) == 0) {   // Type == CpuSetInformation
            ushort group = (ushort)Marshal.ReadInt16(buf, offset + 12);
            int lpi = Marshal.ReadByte(buf, offset + 14);
            int core = Marshal.ReadByte(buf, offset + 15);
            byte eff = Marshal.ReadByte(buf, offset + 18);
            if (group == 0 && lpi < 64) result.Add((lpi, core, eff));
          }
          offset += (int)size;
        }
        result.Sort((a, b) => a.Lpi.CompareTo(b.Lpi));
        // CPU Set 不可信/不完整时交给调用方走 EX fallback（同 AffinityTopology 守卫）
        if (result.Count < Math.Min(Environment.ProcessorCount, 64)) result.Clear();
      } finally { Marshal.FreeHGlobal(buf); }
      return result;
    }
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
          // SMT 兄弟 = 同一 CoreIndex 出现多次;首现线程标非 SMT,后续标 SMT。
          // (旧实现用 AllFlags bit0,那是 RealTime 位不是 SMT 位,恒 false。)
          var seen = new HashSet<int>();
          foreach (var cs in cpuSets) {
            bool isSmt = !seen.Add(cs.Core);
            cores.Add(new CoreInfo {
              LogicalIndex = cs.Lpi,
              Group = 0,
              CoreIndex = cs.Core,
              EfficiencyClass = cs.Eff,
              IsSmt = isSmt,
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
          return NormalizeEfficiency(cores);
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
      return NormalizeEfficiency(cores);
    }

    /// <summary>把 raw EfficiencyClass 归一为 CoreInfo 文档约定(0=P/最高级,1+=E)。
    /// Windows 原生 class 数值方向与 CoreInfo 约定相反(本机 14650HX 实测:E 报 0、P 报最大值;
    /// AffinityTopology 按"max=P"分类且经真机验证,SelfCheck 的 oracle 交叉比对即抓出过
    /// 不归一导致的 P/E 计数翻转)。单一 class 时归一为恒 0(全 P),非 hybrid 语义不变。</summary>
    static List<CoreInfo> NormalizeEfficiency(List<CoreInfo> cores) {
      if (cores.Count == 0) return cores;
      int max = 0;
      foreach (var c in cores) max = Math.Max(max, c.EfficiencyClass);
      if (max == 0) return cores;
      for (int i = 0; i < cores.Count; i++) {
        var c = cores[i];
        c.EfficiencyClass = max - c.EfficiencyClass;
        cores[i] = c;
      }
      return cores;
    }

    static List<CoreInfo> DetectFromLogicalProcessorInfo() {
      // ponytail: 降级路径 —— 仅当 GetSystemCpuSetInformation 不可用时触发(现方法1已修好,
      // 常态不走)。logIdx=累加器假设单处理器组且掩码位连续(笔记本恒成立);跨组机型
      // 位号会错位,但 ThreadBindingService 也只处理 group0,故接受此边界不额外修。
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
            var numaAff = numa.GroupAffinity;   // 结构体自带对齐 pad,不再手算偏移
            for (int bit = 0; bit < 64; bit++) {
              if ((numaAff.Mask & (1UL << bit)) != 0)
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
              var cacheAff = cache.GroupAffinity;   // 结构体自带对齐 pad,不再手算偏移
              for (int bit = 0; bit < 64; bit++) {
                if ((cacheAff.Mask & (1UL << bit)) != 0)
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
