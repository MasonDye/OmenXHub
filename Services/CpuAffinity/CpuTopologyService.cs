// CpuAffinity/CpuTopologyService.cs - 结构化 CPU 拓扑检测
// 参考 CpuAffinityManager.Cpu.CpuTopologyService
// 用 CPU Set API 检测 P/E 核 + GetLogicalProcessorInformation 检测 SMT/CCD/Socket
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace OmenSuperHub.Services.CpuAffinity {

  /// <summary>
  /// 检测 CPU 拓扑。结果缓存（进程级不变）。
  /// </summary>
  public class CpuTopologyService {
    CpuTopology _cached;
    readonly object _lock = new object();

    public CpuTopology Detect() {
      if (_cached != null) return _cached;
      lock (_lock) {
        if (_cached != null) return _cached;
        _cached = DetectInternal();
        return _cached;
      }
    }

    public ulong BuildMask(string mode, ulong? customMask = null)
      => CpuTopology.BuildMask(Detect(), mode, customMask);

    static CpuTopology DetectInternal() {
      int totalLogical = Environment.ProcessorCount;
      ulong pcoreMask = ~0UL, ecoreMask = 0, smt0Mask = 0, smt1Mask = 0, ccd0Mask = 0, ccd1Mask = 0;
      int pcoreCount = totalLogical, ecoreCount = 0;
      bool smtEnabled = false;

      // Step 1: CPU Set API 检测 efficiency class
      var effMap = QueryCpuSetEfficiency(totalLogical);
      if (effMap.Count > 0) {
        pcoreMask = 0; ecoreMask = 0; pcoreCount = 0; ecoreCount = 0;
        byte maxEff = effMap.Values.Max();
        byte minEff = effMap.Values.Min();
        foreach (var kvp in effMap) {
          if (kvp.Key >= 64) continue;
          ulong bit = 1UL << kvp.Key;
          // ponytail: hybrid 时较低 efficiency class 为 E 核；非 hybrid 时 max==min，全部归 P 核
          if (maxEff > minEff && kvp.Value < maxEff) {
            ecoreMask |= bit; ecoreCount++;
          } else {
            pcoreMask |= bit; pcoreCount++;
          }
        }
        // 兜底：无 P 核检测到则全部作 P 核
        if (pcoreMask == 0 && ecoreMask != 0) {
          pcoreMask = ecoreMask; pcoreCount = ecoreCount; ecoreMask = 0; ecoreCount = 0;
        }
      }

      // Step 2: SMT 布局
      var smt = DetectSmtLayout(totalLogical);
      if (smt.HasValue) {
        smtEnabled = smt.Value.smtEnabled;
        smt0Mask = smt.Value.smt0Mask;
        smt1Mask = smt.Value.smt1Mask;
      }

      // Step 3: AMD CCD 布局
      var ccd = DetectCcdLayout();
      if (ccd.HasValue) {
        ccd0Mask = ccd.Value.ccd0Mask;
        ccd1Mask = ccd.Value.ccd1Mask;
      }

      // Step 4: Socket 布局
      var sockets = DetectSockets();

      return new CpuTopology {
        TotalLogicalProcessors = totalLogical,
        PcoreCount = pcoreCount,
        EcoreCount = ecoreCount,
        SmtEnabled = smtEnabled,
        PcoreMask = pcoreMask,
        EcoreMask = ecoreMask,
        Smt0Mask = smt0Mask,
        Smt1Mask = smt1Mask,
        Ccd0Mask = ccd0Mask,
        Ccd1Mask = ccd1Mask,
        SocketCount = sockets.socketCount,
        SocketMasks = sockets.socketMasks
      };
    }

    /// <summary>查询 CPU Set 获取每逻辑处理器的 efficiency class。</summary>
    static Dictionary<int, byte> QueryCpuSetEfficiency(int totalLogical) {
      var result = new Dictionary<int, byte>();
      if (!Kernel32.GetSystemCpuSetInformation(IntPtr.Zero, 0, out uint retLen, IntPtr.Zero, 0) && retLen == 0)
        return result;
      if (retLen == 0) return result;

      IntPtr buf = Marshal.AllocHGlobal((int)retLen);
      try {
        if (!Kernel32.GetSystemCpuSetInformation(buf, retLen, out _, IntPtr.Zero, 0))
          return result;
        uint offset = 0;
        while (offset < retLen) {
          var info = Marshal.PtrToStructure<SYSTEM_CPU_SET_INFORMATION>(buf + (int)offset);
          if (info.Size == 0) break;
          if (info.Type == CPU_SET_INFORMATION_TYPE.CpuSetInformation &&
              info.CpuSet.Group == 0 && info.CpuSet.LogicalProcessorIndex < 64) {
            result[info.CpuSet.LogicalProcessorIndex] = info.CpuSet.EfficiencyClass;
          }
          offset += info.Size;
        }
        if (result.Count < Math.Min(totalLogical, 64)) result.Clear();
      } finally { Marshal.FreeHGlobal(buf); }
      return result;
    }

    // ── EX API 检测（GetLogicalProcessorInformationEx）──
    // ponytail: 用 EX API + 固定偏移解包，替代旧的非 EX 结构体（其 x64 布局 ULONG_PTR 对齐
    // 导致字段错位、SMT/CCD/Socket 全部检测失效）。
    // 条目布局：Header = rel(int)@0 + size(int)@4；union 从 @8 起。
    // PROCESSOR_RELATIONSHIP（core/die/package 共用）：
    //   Flags@0, EfficiencyClass@1, Reserved[20], GroupCount(WORD)@22, GROUP_AFFINITY[GroupCount]@24。
    // GROUP_AFFINITY：Mask(ULONG_PTR)@0, Group(WORD)@8。

    /// <summary>枚举指定关系的 EX 条目，回调 (buffer, 条目偏移, 条目大小)。返回是否至少枚举到一个条目。</summary>
    static bool EnumerateEx(LOGICAL_PROCESSOR_RELATIONSHIP relationship, Action<IntPtr, int, int> onEntry) {
      int len = 0;
      if (!Kernel32.GetLogicalProcessorInformationEx((uint)relationship, IntPtr.Zero, ref len) || len <= 0)
        return false;
      IntPtr buf = Marshal.AllocHGlobal(len);
      try {
        if (!Kernel32.GetLogicalProcessorInformationEx((uint)relationship, buf, ref len))
          return false;
        int offset = 0;
        while (offset + 8 <= len) {
          int rel = Marshal.ReadInt32(buf, offset);
          int size = Marshal.ReadInt32(buf, offset + 4);
          if (size == 0) break;
          if (rel == (int)relationship) onEntry(buf, offset, size);
          offset += size;
        }
        return true;
      } finally { Marshal.FreeHGlobal(buf); }
    }

    /// <summary>读取 PROCESSOR_RELATIONSHIP 条目的第一个 GROUP_AFFINITY mask（ulong，entryOffset+8+24 处）。</summary>
    static ulong ReadFirstGroupMask(IntPtr buf, int entryOffset) {
      ushort groupCount = (ushort)Marshal.ReadInt16(buf, entryOffset + 8 + 22);
      if (groupCount < 1) return 0;
      return (ulong)Marshal.ReadInt64(buf, entryOffset + 8 + 24);
    }

    /// <summary>检测 SMT 布局。每物理核的 mask 含全部 SMT 兄弟，最低 bit 为 SMT0。</summary>
    static (bool smtEnabled, ulong smt0Mask, ulong smt1Mask)? DetectSmtLayout(int totalLogical) {
      if (totalLogical > 64) return null;
      ulong smt0 = 0, smt1 = 0;
      bool hasSmt = false, any = false;
      bool ok = EnumerateEx(LOGICAL_PROCESSOR_RELATIONSHIP.RelationProcessorCore, (buf, off, _) => {
        ulong mask = ReadFirstGroupMask(buf, off);
        if (mask == 0) return;
        any = true;
        ulong lowest = mask & (~mask + 1); // 最低 set bit = SMT0
        smt0 |= lowest;
        ulong rest = mask & ~lowest;
        if (rest != 0) { smt1 |= rest; hasSmt = true; }
      });
      if (!ok || !any) return null;
      return (hasSmt, smt0, smt1);
    }

    /// <summary>检测 AMD CCD 布局（RelationProcessorDie）。Intel 无 die 条目，返回 null。</summary>
    static (ulong ccd0Mask, ulong ccd1Mask)? DetectCcdLayout() {
      var dies = new List<ulong>();
      bool ok = EnumerateEx(LOGICAL_PROCESSOR_RELATIONSHIP.RelationProcessorDie, (buf, off, _) => {
        ulong mask = ReadFirstGroupMask(buf, off);
        if (mask != 0) dies.Add(mask);
      });
      if (!ok || dies.Count < 2) return null;
      return (dies[0], dies[1]);
    }

    /// <summary>检测物理 CPU Socket（RelationProcessorPackage）。</summary>
    static (int socketCount, List<ulong> socketMasks) DetectSockets() {
      var masks = new List<ulong>();
      bool ok = EnumerateEx(LOGICAL_PROCESSOR_RELATIONSHIP.RelationProcessorPackage, (buf, off, _) => {
        ulong mask = ReadFirstGroupMask(buf, off);
        if (mask != 0) masks.Add(mask);
      });
      if (!ok) return (1, masks);
      return (masks.Count > 0 ? masks.Count : 1, masks);
    }
  }
}
