// CpuAffinity/CpuTopology.cs - CPU 拓扑信息 + 掩码构建
// 参考 CpuAffinityManager.Cpu.CpuTopology
using System;
using System.Collections.Generic;

namespace OmenSuperHub.Services.CpuAffinity {

  /// <summary>
  /// CPU 拓扑信息：P/E 核掩码、SMT 布局、CCD、Socket。
  /// </summary>
  public class CpuTopology {
    // ponytail: init 关键字在 .NET Framework 4.8.1 不支持，改用 set
    public int TotalLogicalProcessors { get; set; }
    public int PcoreCount { get; set; }
    public int EcoreCount { get; set; }
    public bool SmtEnabled { get; set; }
    public ulong PcoreMask { get; set; }
    public ulong EcoreMask { get; set; }
    public ulong Smt0Mask { get; set; }
    public ulong Smt1Mask { get; set; }
    public ulong Ccd0Mask { get; set; }
    public ulong Ccd1Mask { get; set; }
    public int SocketCount { get; set; }
    public List<ulong> SocketMasks { get; set; } = new List<ulong>();

    // ponytail: 掩码构建器字典 — 模式名 → 由拓扑生成掩码
    public static readonly Dictionary<string, Func<CpuTopology, ulong>> MaskBuilders = new Dictionary<string, Func<CpuTopology, ulong>> {
      ["all-cores"]      = t => ClampToLogicalProcessors(~0UL, t.TotalLogicalProcessors),
      ["p-cores"]        = t => t.PcoreMask,
      ["e-cores"]        = t => t.EcoreMask,
      ["p-cores-smt"]    = t => t.PcoreMask,
      ["p-cores-no-smt"] = t => t.PcoreMask & ~t.Smt1Mask,
      ["p-cores-first"]  = t => t.Smt0Mask & t.PcoreMask,
      ["no-smt"]         = t => t.Smt0Mask,
      ["first-half"]     = t => BuildHalfMask(t, true),
      ["second-half"]    = t => BuildHalfMask(t, false),
      ["ccd0"]           = t => t.Ccd0Mask,
      ["ccd1"]           = t => t.Ccd1Mask,
    };

    /// <summary>
    /// 由模式字符串构建掩码，支持复合回退链 mode1|mode2 与 socket 过滤 mode@socket0。
    /// </summary>
    public static ulong BuildMask(CpuTopology topology, string mode, ulong? customMask = null) {
      if (string.IsNullOrWhiteSpace(mode)) return 0;
      if (mode == "custom")
        return ClampToLogicalProcessors(customMask ?? 0, topology.TotalLogicalProcessors);

      // 解析 socket 后缀
      int socketIdx = -1;
      int atIdx = mode.IndexOf('@');
      string modePart = mode;
      if (atIdx > 0) {
        modePart = mode.Substring(0, atIdx);
        string sock = mode.Substring(atIdx + 1);
        if (sock.StartsWith("socket", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(sock.Substring(6), out int si))
          socketIdx = si;
      }

      // 复合回退链
      if (modePart.Contains("|")) {
        foreach (var fb in modePart.Split('|')) {
          ulong m = BuildSingleMask(topology, fb.Trim(), customMask);
          if (m != 0)
            return ClampToLogicalProcessors(ApplySocketFilter(m, topology, socketIdx), topology.TotalLogicalProcessors);
        }
        return 0;
      }

      ulong result = BuildSingleMask(topology, modePart, customMask);
      return ClampToLogicalProcessors(ApplySocketFilter(result, topology, socketIdx), topology.TotalLogicalProcessors);
    }

    static ulong BuildSingleMask(CpuTopology t, string mode, ulong? customMask) {
      if (MaskBuilders.TryGetValue(mode, out var b)) return b(t);
      return 0;
    }

    public static ulong ClampToLogicalProcessors(ulong mask, int total) {
      if (total <= 0) return 0;
      if (total >= 64) return mask;
      return mask & ((1UL << total) - 1);
    }

    static ulong ApplySocketFilter(ulong mask, CpuTopology t, int socketIndex) {
      if (socketIndex < 0 || t.SocketMasks.Count == 0) return mask;
      if (socketIndex < t.SocketMasks.Count) return mask & t.SocketMasks[socketIndex];
      return 0;
    }

    public static ulong BuildHalfMask(CpuTopology t, bool firstHalf) {
      int half = t.TotalLogicalProcessors / 2;
      if (half >= 64) half = 64;
      ulong mask = 0;
      int start = firstHalf ? 0 : half;
      int end = firstHalf ? half : t.TotalLogicalProcessors;
      if (end > 64) end = 64;
      for (int i = start; i < end; i++) mask |= 1UL << i;
      return mask;
    }

    public override string ToString() {
      var parts = new List<string> {
        $"CPU: {TotalLogicalProcessors}L",
        $"{PcoreCount}P+{EcoreCount}E",
        $"SMT={(SmtEnabled ? "On" : "Off")}"
      };
      if (SocketCount > 1) parts.Add($"{SocketCount} Sockets");
      parts.Add($"P=0x{PcoreMask:X}");
      parts.Add($"E=0x{EcoreMask:X}");
      return string.Join(", ", parts);
    }
  }
}
