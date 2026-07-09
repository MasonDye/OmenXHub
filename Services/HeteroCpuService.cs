// HeteroCpuService.cs - AMD 异构 CPU 调度管理
// 提供两种模式：
//   1. 注册表模式 — 写 HKLM SmallProcessorMask（需重启）
//   2. 实时绑定模式 — SetThreadGroupAffinity（即时生效）
using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace OmenSuperHub.Services {
  internal static class HeteroCpuService {
    private const string KGroupPath = @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel\Kgroups\00";
    private const string KernelPath = @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel";
    private const ushort ALL_PROCESSOR_GROUPS = 0xFFFF;

    [DllImport("kernel32.dll")]
    static extern int GetActiveProcessorCount(ushort groupNumber);

    [DllImport("kernel32.dll")]
    static extern int GetMaximumProcessorCount(ushort groupNumber);

    public static bool IsActive() {
      try {
        using (var key = Registry.LocalMachine.OpenSubKey(KGroupPath))
          return key != null && key.GetValue("SmallProcessorMask") != null;
      } catch { return false; }
    }

    public static string ReadSmallProcessorMask() {
      try {
        using (var key = Registry.LocalMachine.OpenSubKey(KGroupPath)) {
          if (key?.GetValue("SmallProcessorMask") is byte[] raw)
            return string.Concat(raw.Select(b => b.ToString("X2")));
        }
      } catch { }
      return "FFFF0000";
    }

    static int ReadDword(string path, string name, int def) {
      try {
        using (var key = Registry.LocalMachine.OpenSubKey(path))
          return (int)(key?.GetValue(name, def) ?? def);
      } catch { return def; }
    }

    public static int ReadDefaultPolicy() => ReadDword(KernelPath, "DefaultDynamicHeteroCpuPolicy", 2);
    public static int ReadExpectedRuntime() => ReadDword(KernelPath, "DynamicCpuPolicyExpectedRuntime", 1450);
    public static int ReadImportantPolicy() => ReadDword(KernelPath, "DynamicHeteroCpuPolicyImportant", 2);
    public static int ReadImportantShortPolicy() => ReadDword(KernelPath, "DynamicHeteroCpuPolicyImportantShort", 3);
    public static int ReadPolicyMask() => ReadDword(KernelPath, "DynamicHeteroCpuPolicyMask", 7);
    public static int ReadImportantPriority() => ReadDword(KernelPath, "DynamicHeteroCpuPolicyImportantPriority", 8);

    public static void WriteSmallProcessorMask(string hex) {
      try {
        hex = hex?.Replace(" ", "").Replace("0x", "").Replace("0X", "") ?? "";
        if (hex.Length % 2 != 0) hex = "0" + hex;
        var bytes = Enumerable.Range(0, hex.Length / 2)
          .Select(i => Convert.ToByte(hex.Substring(i * 2, 2), 16)).ToArray();
        using (var key = Registry.LocalMachine.CreateSubKey(KGroupPath))
          key?.SetValue("SmallProcessorMask", bytes, RegistryValueKind.Binary);
      } catch (Exception ex) {
        Logger.Error($"WriteSmallProcessorMask error: {ex.Message}");
      }
    }

    static void WriteDword(string path, string name, int value) {
      try {
        using (var key = Registry.LocalMachine.CreateSubKey(path))
          key?.SetValue(name, value, RegistryValueKind.DWord);
      } catch (Exception ex) {
        Logger.Error($"WriteDword({name}) error: {ex.Message}");
      }
    }

    public static void WriteDefaultPolicy(int v) => WriteDword(KernelPath, "DefaultDynamicHeteroCpuPolicy", v);
    public static void WriteExpectedRuntime(int v) => WriteDword(KernelPath, "DynamicCpuPolicyExpectedRuntime", v);
    public static void WriteImportantPolicy(int v) => WriteDword(KernelPath, "DynamicHeteroCpuPolicyImportant", v);
    public static void WriteImportantShortPolicy(int v) => WriteDword(KernelPath, "DynamicHeteroCpuPolicyImportantShort", v);
    public static void WritePolicyMask(int v) => WriteDword(KernelPath, "DynamicHeteroCpuPolicyMask", v);
    public static void WriteImportantPriority(int v) => WriteDword(KernelPath, "DynamicHeteroCpuPolicyImportantPriority", v);

    public static void RemoveAll() {
      try {
        using (var key = Registry.LocalMachine.CreateSubKey(KGroupPath))
          key?.DeleteValue("SmallProcessorMask", false);
        using (var key = Registry.LocalMachine.CreateSubKey(KernelPath)) {
          key?.DeleteValue("DefaultDynamicHeteroCpuPolicy", false);
          key?.DeleteValue("DynamicCpuPolicyExpectedRuntime", false);
          key?.DeleteValue("DynamicHeteroCpuPolicyImportant", false);
          key?.DeleteValue("DynamicHeteroCpuPolicyImportantShort", false);
          key?.DeleteValue("DynamicHeteroCpuPolicyMask", false);
          key?.DeleteValue("DynamicHeteroCpuPolicyImportantPriority", false);
        }
      } catch (Exception ex) {
        Logger.Error($"HeteroCpu RemoveAll error: {ex.Message}");
      }
    }

    // ════════════════════════════════════════════════════════════
    // Auto‑detect AMD dual‑CCD topology
    // ════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns (isAmdDualCcd, totalLogicalProcessors, ccd0Count, suggestedMaskHex).
    /// </summary>
    public static (bool supported, int totalLp, int ccd0Lp, string maskHex) DetectDualCcd() {
      try {
        bool isAmd = false;
        int coreCount = 0;
        using (var mos = new ManagementObjectSearcher("SELECT * FROM Win32_Processor"))
          foreach (var mo in mos.Get()) {
            string man = mo["Manufacturer"]?.ToString() ?? "";
            if (man.IndexOf("authenticamd", StringComparison.OrdinalIgnoreCase) >= 0 ||
                man.IndexOf("amd", StringComparison.OrdinalIgnoreCase) >= 0)
              isAmd = true;
            int cores = 0;
            int.TryParse(mo["NumberOfCores"]?.ToString() ?? "0", out cores);
            coreCount = Math.Max(coreCount, cores);
          }
        if (!isAmd) return (false, 0, 0, "");

        int totalLp = GetActiveProcessorCount(ALL_PROCESSOR_GROUPS);
        if (totalLp < 12) return (false, totalLp, 0, "");

        // For dual‑CCD: try NUMA node detection first
        int ccd0Count = DetectCcdBoundary(totalLp);
        if (ccd0Count <= 0 || ccd0Count >= totalLp) {
          // Fallback: assume equal split for 12‑16 core AMD
          int coresPerCcd = coreCount / 2;
          if (coresPerCcd < 1) coresPerCcd = totalLp / 4;
          ccd0Count = coresPerCcd * 2; // LP per CCD (hyperthreading)
        }

        // Validate: CCD0 + CCD1 should roughly equal total (allow small mismatch)
        int ccd1Count = totalLp - ccd0Count;
        if (ccd0Count <= 0 || ccd1Count <= 0)
          return (false, totalLp, 0, "");

        string mask = GenerateMask(totalLp, ccd0Count);
        return (true, totalLp, ccd0Count, mask);
      } catch {
        return (false, 0, 0, "");
      }
    }

    /// <summary>
    /// Use NUMA topology to find CCD boundary.
    /// Returns logical processor count of the first NUMA node (CCD0).
    /// </summary>
    static int DetectCcdBoundary(int totalLp) {
      try {
        int maxBytes = ((totalLp + 63) / 64) * 8;
        IntPtr buffer = Marshal.AllocHGlobal(maxBytes);
        try {
          int retLen = maxBytes;
          bool ok = GetLogicalProcessorInformationEx(RelationNumaNode, buffer, ref retLen);
          if (!ok && Marshal.GetLastWin32Error() == 122) { // ERROR_INSUFFICIENT_BUFFER
            Marshal.FreeHGlobal(buffer);
            buffer = Marshal.AllocHGlobal(retLen);
            ok = GetLogicalProcessorInformationEx(RelationNumaNode, buffer, ref retLen);
          }
          if (!ok) return 0;

          int offset = 0;
          while (offset < retLen) {
            var header = Marshal.PtrToStructure<SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX>(IntPtr.Add(buffer, offset));
            if (header.Relationship == RelationNumaNode) {
              // Read the NUMA_NODE info right after the header
              int numaOffset = offset + Marshal.SizeOf<SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX>();
              var numa = Marshal.PtrToStructure<NUMA_NODE_RELATIONSHIP>(IntPtr.Add(buffer, numaOffset));
              // Only process node 0 (the first CCD)
              if (numa.NodeNumber == 0) {
                int count = 0;
                for (int i = 0; i < numa.AffinityMask.Length * 8 && i < 64; i++) {
                  if ((numa.AffinityMask[i / 8] & (1 << (i % 8))) != 0) count++;
                }
                return count;
              }
            }
            offset += header.Size;
          }
        } finally {
          Marshal.FreeHGlobal(buffer);
        }
      } catch { }
      return 0;
    }

    static string GenerateMask(int totalLp, int ccd0Count) {
      int byteCount = Math.Max(8, ((totalLp + 7) / 8));
      byte[] mask = new byte[byteCount];
      for (int i = 0; i < ccd0Count && i < totalLp; i++)
        mask[i / 8] |= (byte)(1 << (i % 8));
      return string.Concat(mask.Select(b => b.ToString("X2")));
    }

    public static string GetCpuInfo() {
      try {
        using (var mos = new ManagementObjectSearcher("SELECT Name, Manufacturer, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor"))
          foreach (var mo in mos.Get())
            return $"{mo["Name"]} ({mo["NumberOfCores"]} cores / {mo["NumberOfLogicalProcessors"]} threads)";
      } catch { }
      return "";
    }

    // ════════════════════════════════════════════════════════════
    // Win32 structures for GetLogicalProcessorInformationEx
    // ════════════════════════════════════════════════════════════

    const int RelationNumaNode = 1;

    [StructLayout(LayoutKind.Sequential)]
    struct SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX {
      public int Relationship;
      public int Size;
      // followed by the specific relationship data
    }

    [StructLayout(LayoutKind.Sequential)]
    struct NUMA_NODE_RELATIONSHIP {
      public uint NodeNumber;
      [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
      public byte[] AffinityMask; // 64-bit mask
      public int Reserved;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetLogicalProcessorInformationEx(
        int relationshipType,
        IntPtr buffer,
        ref int returnedLength);

    // ════════════════════════════════════════════════════════════
    // 实时绑定模式 (v2)
    // ════════════════════════════════════════════════════════════

    /// <summary>获取当前 CPU 拓扑摘要文本</summary>
    public static string GetTopologySummary() => CpuTopologyService.GetSummary();


    /// <summary>实时绑定进程到指定 CCD</summary>
    public static int BindProcessToCcd(int processId, int ccdId) {
      return ThreadBindingService.BindProcessToCcd(processId, ccdId);
    }

    /// <summary>实时绑定进程到 P-core 或 E-core</summary>
    public static int BindProcessToClass(int processId, bool usePerformance) {
      return ThreadBindingService.BindProcessToEfficiencyClass(processId, usePerformance);
    }

    /// <summary>实时解绑进程（恢复全核心）</summary>
    public static int UnbindProcess(int processId) {
      return ThreadBindingService.UnbindProcess(processId);
    }

    /// <summary>获取所有可用的核心选择策略文本</summary>
    public static List<string> GetBindingStrategies() {
      var list = new List<string>();
      var cores = CpuTopologyService.GetCores();
      var ccds = cores.Where(c => c.CcdId >= 0).Select(c => c.CcdId).Distinct().OrderBy(x => x).ToList();
      if (ccds.Count > 0) {
        foreach (var ccd in ccds) {
          int count = cores.Count(c => c.CcdId == ccd && !c.IsSmt);
          list.Add($"CCD {ccd} ({count} 物理核)");
        }
      }
      if (cores.Any(c => c.IsEfficiency)) {
        list.Add("P-Core Only (性能核)");
        list.Add("E-Core Only (能效核)");
      }
      list.Add("全部核心 (解绑)");
      return list;
    }

    /// <summary>应用绑定策略到进程</summary>
    public static int ApplyBindingStrategy(int processId, int strategyIndex) {
      var cores = CpuTopologyService.GetCores();
      var ccds = cores.Where(c => c.CcdId >= 0).Select(c => c.CcdId).Distinct().OrderBy(x => x).ToList();
      int idx = 0;
      foreach (var ccd in ccds) {
        if (idx == strategyIndex) return ThreadBindingService.BindProcessToCcd(processId, ccd);
        idx++;
      }
      if (cores.Any(c => c.IsEfficiency)) {
        if (idx == strategyIndex) return ThreadBindingService.BindProcessToEfficiencyClass(processId, true);
        idx++;
        if (idx == strategyIndex) return ThreadBindingService.BindProcessToEfficiencyClass(processId, false);
        idx++;
      }
      // 全部核心 = 解绑
      return ThreadBindingService.UnbindProcess(processId);
    }
  }
}
