// CoreKeepService.cs - 进程亲和性与优先级管理
// 通过 P/Invoke 设置进程 CPU 亲和性掩码和优先级类，监听进程启动自动应用规则
// 包含：守护定时器、拓扑检测、核心竞速、模式选择
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OmenSuperHub.Services {
  [DataContract]
  public class CoreKeepEntry {
    [DataMember] public bool Enabled { get; set; }
    [DataMember] public string ProcessName { get; set; }
    [DataMember] public uint PriorityClass { get; set; }
    [DataMember] public long AffinityMask { get; set; }
    [DataMember] public int ProcessId { get; set; }
    [DataMember] public string CapturedAt { get; set; }
    [DataMember] public bool GuardEnabled { get; set; } = true;
    [DataMember] public string CoreMode { get; set; } = "All";
    [DataMember] public int[] PreferredCores { get; set; }
  }

  [DataContract]
  public class CoreKeepData {
    [DataMember] public bool MasterEnabled { get; set; }
    [DataMember] public int GuardIntervalMs { get; set; } = 2000;
    [DataMember] public List<CoreKeepEntry> Entries { get; set; } = new List<CoreKeepEntry>();
  }

  /// <summary>CPU 拓扑信息</summary>
  public struct CoreTopologyInfo {
    public int TotalLogical;
    public int PhysicalCoreCount;
    public bool IsHybrid;
    public bool IsDualCcd;
    public int[] PerformanceCores; // 逻辑处理器索引
    public int[] EfficientCores;
    public int Ccd0Count;
    public int Ccd1Count;
  }

  /// <summary>单核心竞速结果</summary>
  public struct CoreBenchResult {
    public int CoreIndex;
    public long Score;      // ticks, lower = better
    public double Relative; // 1.0 = best core
  }

  /// <summary>进程当前亲和性状态</summary>
  public struct ProcessAffinityState {
    public bool Running;
    public uint PriorityClass;
    public long AffinityMask;
  }

  public static class CoreKeepService {
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool SetProcessAffinityMask(IntPtr hProcess, IntPtr dwProcessAffinityMask);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool SetPriorityClass(IntPtr hProcess, uint dwPriorityClass);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern uint GetPriorityClass(IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetProcessAffinityMask(IntPtr hProcess, out IntPtr lpProcessAffinityMask, out IntPtr lpSystemAffinityMask);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll")]
    static extern IntPtr SetThreadAffinityMask(IntPtr hThread, IntPtr dwThreadAffinityMask);

    [DllImport("kernel32.dll")]
    static extern IntPtr GetCurrentThread();

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern int GetActiveProcessorCount(ushort groupNumber);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetLogicalProcessorInformationEx(int relationshipType, IntPtr buffer, ref int returnedLength);

    const uint PROCESS_QUERY_INFORMATION = 0x0400;
    const uint PROCESS_SET_INFORMATION = 0x0200;
    const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    const ushort ALL_PROCESSOR_GROUPS = 0xFFFF;
    const int RelationProcessorCore = 0;
    const int RelationNumaNode = 1;

    static readonly string ConfigPath = Path.Combine(
      AppDomain.CurrentDomain.BaseDirectory, "CoreKeep.json");
    static readonly string BenchPath = Path.Combine(
      AppDomain.CurrentDomain.BaseDirectory, "CoreKeepBench.json");

    static ManagementEventWatcher _watcher;
    static Dictionary<string, CoreKeepEntry> _activeEntries = new Dictionary<string, CoreKeepEntry>();
    static Timer _guardTimer;
    static int _guardRunning;

    // 缓存拓扑
    static CoreTopologyInfo? _cachedTopology;

    // ══════════════════════════════════════
    //  持久化
    // ══════════════════════════════════════

    public static CoreKeepData Load() {
      try {
        if (!File.Exists(ConfigPath)) return new CoreKeepData();
        using (var ms = new MemoryStream(File.ReadAllBytes(ConfigPath))) {
          var ser = new DataContractJsonSerializer(typeof(CoreKeepData));
          var data = (CoreKeepData)ser.ReadObject(ms) ?? new CoreKeepData();
          if (data.Entries == null) data.Entries = new List<CoreKeepEntry>();
          if (data.GuardIntervalMs < 1000) data.GuardIntervalMs = 2000;
          return data;
        }
      } catch { return new CoreKeepData(); }
    }

    public static void Save(CoreKeepData data) {
      try {
        var ser = new DataContractJsonSerializer(typeof(CoreKeepData));
        using (var ms = new MemoryStream()) {
          ser.WriteObject(ms, data);
          File.WriteAllBytes(ConfigPath, ms.ToArray());
        }
      } catch { }
    }

    // ══════════════════════════════════════
    //  捕获 / 应用
    // ══════════════════════════════════════

    public static CoreKeepEntry CaptureFromProcess(string processName) {
      var entry = new CoreKeepEntry { ProcessName = processName, CapturedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") };
      try {
        Process[] procs = Process.GetProcessesByName(processName.Replace(".exe", ""));
        if (procs.Length == 0) return entry;
        int pid = procs[0].Id;
        CapturePidData(pid, entry);
      } catch { }
      return entry;
    }

    public static CoreKeepEntry CaptureFromPid(int pid) {
      Process p = null;
      try { p = Process.GetProcessById(pid); } catch { }
      if (p == null) {
        return new CoreKeepEntry { ProcessName = $"PID:{pid}", CapturedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") };
      }
      var entry = new CoreKeepEntry {
        ProcessName = p.ProcessName + ".exe",
        ProcessId = pid,
        CapturedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
      };
      CapturePidData(pid, entry);
      return entry;
    }

    static void CapturePidData(int pid, CoreKeepEntry entry) {
      IntPtr hProc = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
      if (hProc == IntPtr.Zero) hProc = OpenProcess(PROCESS_QUERY_INFORMATION, false, pid);
      if (hProc != IntPtr.Zero) {
        try {
          entry.PriorityClass = GetPriorityClass(hProc);
          IntPtr affinity;
          if (GetProcessAffinityMask(hProc, out affinity, out _))
            entry.AffinityMask = affinity.ToInt64();
        } finally { CloseHandle(hProc); }
      }
    }

    public static void ApplyToProcess(string processName, CoreKeepEntry entry) {
      if (string.IsNullOrEmpty(processName) || !entry.Enabled) return;
      try {
        // ponytail: 有 PID 优先按 PID 精准命中；否则按进程名
        if (entry.ProcessId > 0) {
          var p = Process.GetProcessById(entry.ProcessId);
          ApplyToPid(p.Id, entry);
        } else {
          Process[] procs = Process.GetProcessesByName(processName.Replace(".exe", ""));
          foreach (Process p in procs)
            ApplyToPid(p.Id, entry);
        }
      } catch { }
    }

    static void ApplyToPid(int pid, CoreKeepEntry entry) {
      IntPtr hProc = OpenProcess(PROCESS_SET_INFORMATION, false, pid);
      if (hProc != IntPtr.Zero) {
        try {
          if (entry.PriorityClass != 0)
            SetPriorityClass(hProc, entry.PriorityClass);
          if (entry.AffinityMask != 0)
            SetProcessAffinityMask(hProc, new IntPtr(entry.AffinityMask));
        } finally { CloseHandle(hProc); }
      }
    }

    /// <summary>查询进程当前实际的亲和性和优先级（不修改）</summary>
    public static ProcessAffinityState QueryProcessState(string processName, int processId = 0) {
      var state = new ProcessAffinityState { Running = false };
      try {
        if (processId > 0) {
          Process p = Process.GetProcessById(processId);
          QueryPidState(p.Id, ref state);
          return state;
        }
        Process[] procs = Process.GetProcessesByName(processName.Replace(".exe", ""));
        if (procs.Length == 0) return state;
        QueryPidState(procs[0].Id, ref state);
      } catch { }
      return state;
    }

    static void QueryPidState(int pid, ref ProcessAffinityState state) {
      state.Running = true;
      IntPtr hProc = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
      if (hProc == IntPtr.Zero) hProc = OpenProcess(PROCESS_QUERY_INFORMATION, false, pid);
      if (hProc != IntPtr.Zero) {
        try {
          state.PriorityClass = GetPriorityClass(hProc);
          IntPtr affinity;
          if (GetProcessAffinityMask(hProc, out affinity, out _))
            state.AffinityMask = affinity.ToInt64();
        } finally { CloseHandle(hProc); }
      }
    }

    // ══════════════════════════════════════
    //  自动应用（WMI 进程启动监听 + 守护定时器）
    // ══════════════════════════════════════

    public static void StartAutoApply(CoreKeepData data) {
      StopAutoApply();
      if (!data.MasterEnabled) return;
      _activeEntries.Clear();
      foreach (var entry in data.Entries) {
        if (!entry.Enabled || string.IsNullOrEmpty(entry.ProcessName)) continue;
        string name = entry.ProcessName.Replace(".exe", "").ToLowerInvariant();
        if (!_activeEntries.ContainsKey(name))
          _activeEntries[name] = entry;
      }
      if (_activeEntries.Count == 0) return;
      // Apply to already running processes
      foreach (var kv in _activeEntries)
        ApplyToProcess(kv.Key, kv.Value);
      // Watch for new processes via WMI
      try {
        _watcher = new ManagementEventWatcher(
          new WqlEventQuery("SELECT * FROM Win32_ProcessStartTrace"));
        _watcher.EventArrived += (s, e) => {
          string name = e.NewEvent.Properties["ProcessName"]?.Value?.ToString()?.ToLowerInvariant();
          if (name != null && _activeEntries.ContainsKey(name))
            ApplyToProcess(name, _activeEntries[name]);
        };
        _watcher.Start();
      } catch { }
      // Start guard timer (用户可配置间隔)
      StartGuardTimer(data.GuardIntervalMs);
    }

    public static void StopAutoApply() {
      StopGuardTimer();
      try { _watcher?.Stop(); _watcher?.Dispose(); } catch { }
      _watcher = null;
      _activeEntries.Clear();
    }

    // ══════════════════════════════════════
    //  守护定时器
    // ══════════════════════════════════════

    static void StartGuardTimer(int intervalMs) {
      StopGuardTimer();
      if (intervalMs < 1000) intervalMs = 2000;
      _guardTimer = new Timer(_ => GuardTick(), null, intervalMs, intervalMs);
    }

    static void StopGuardTimer() {
      try { _guardTimer?.Change(Timeout.Infinite, Timeout.Infinite); } catch { }
      try { _guardTimer?.Dispose(); } catch { }
      _guardTimer = null;
      _guardRunning = 0;
    }

    static void GuardTick() {
      // ponytail: 防重入 — 如果一次 tick 耗时超过间隔，跳过重叠
      if (Interlocked.Exchange(ref _guardRunning, 1) == 1) return;
      try {
        // 遍历时用快照，避免迭代中修改
        var entries = _activeEntries.Values.ToArray();
        foreach (var entry in entries) {
          if (!entry.Enabled || !entry.GuardEnabled) continue;
          var state = QueryProcessState(entry.ProcessName, entry.ProcessId);
          if (!state.Running) continue;
          bool needApply = false;
          if (entry.PriorityClass != 0 && state.PriorityClass != entry.PriorityClass)
            needApply = true;
          if (entry.AffinityMask != 0 && state.AffinityMask != entry.AffinityMask)
            needApply = true;
          if (needApply)
            ApplyToProcess(entry.ProcessName, entry);
        }
      } finally {
        _guardRunning = 0;
      }
    }

    /// <summary>更新守护定时器间隔（用户调整时调用）</summary>
    public static void UpdateGuardInterval(int intervalMs) {
      if (_guardTimer != null && _activeEntries.Count > 0)
        StartGuardTimer(intervalMs);
    }

    // ══════════════════════════════════════
    //  拓扑检测
    // ══════════════════════════════════════

    /// <summary>
    /// 检测 CPU 是否为 hybrid 架构，以及获取物理核心数和 CCD 数。
    /// 方法：
    ///   1. WMI 查 CPU 名称 → 12th~15th Gen / Core Ultra → hybrid
    ///   2. 若 hybrid → 用 GetLogicalProcessorInformationEx 解析每核的 EfficiencyClass
    ///   3. 若 hybrid 但 API 失败 → WMI arithmetic fallback
    /// </summary>
    public static CoreTopologyInfo GetTopology() {
      if (_cachedTopology.HasValue) return _cachedTopology.Value;
      var info = new CoreTopologyInfo();
      try {
        info.TotalLogical = GetActiveProcessorCount(ALL_PROCESSOR_GROUPS);
        if (info.TotalLogical <= 0) info.TotalLogical = Environment.ProcessorCount;
        info.PhysicalCoreCount = info.TotalLogical;
        var pSet = new HashSet<int>();
        var eSet = new HashSet<int>();

        // ── Step 1: WMI 判断是否为 hybrid ──
        int wmiPhysCores = 0;
        bool isHybrid = false;
        try {
          using (var mos = new ManagementObjectSearcher("SELECT Name, NumberOfCores FROM Win32_Processor"))
            foreach (var mo in mos.Get()) {
              string name = mo["Name"]?.ToString() ?? "";
              if (name.IndexOf("12th", StringComparison.OrdinalIgnoreCase) >= 0 ||
                  name.IndexOf("13th", StringComparison.OrdinalIgnoreCase) >= 0 ||
                  name.IndexOf("14th", StringComparison.OrdinalIgnoreCase) >= 0 ||
                  name.IndexOf("15th", StringComparison.OrdinalIgnoreCase) >= 0 ||
                  name.IndexOf("Core Ultra", StringComparison.OrdinalIgnoreCase) >= 0)
                isHybrid = true;
              int.TryParse(mo["NumberOfCores"]?.ToString() ?? "0", out wmiPhysCores);
            }
        } catch { }

        // ── Step 2: hybrid 时用 OS API 解析 ──
        if (isHybrid) {
          // 用 GetLogicalProcessorInformationEx 精确解析每核的 EfficiencyClass
          // PROCESSOR_RELATIONSHIP 结构：
          //   +0: Flags(1) +1: EfficiencyClass(1) +2: Reserved(20) +22: GroupCount(2)
          //   +24: GROUP_AFFINITY[GroupCount] (每个: Mask(8) + Group(2) + Reserved(6) = 16 字节)
          // EfficiencyClass: P-core=1, E-core>=2
          int bufSize = 8192;
          IntPtr buf = Marshal.AllocHGlobal(bufSize);
          try {
            int retLen = bufSize;
            bool ok = GetLogicalProcessorInformationEx(RelationProcessorCore, buf, ref retLen);
            if (!ok && Marshal.GetLastWin32Error() == 122) {
              Marshal.FreeHGlobal(buf);
              buf = Marshal.AllocHGlobal(retLen);
              ok = GetLogicalProcessorInformationEx(RelationProcessorCore, buf, ref retLen);
            }
            if (ok) {
              int offset = 0;
              while (offset + 32 <= retLen) {
                int rel = Marshal.ReadInt32(buf, offset);
                int size = Marshal.ReadInt32(buf, offset + 4);
                if (rel != RelationProcessorCore || size <= 0) { offset += Math.Max(size, 8); continue; }
                byte effClass = Marshal.ReadByte(buf, offset + 9);
                bool isEfficient = effClass > 1; // P-core=1, E-core>=2
                short groupCount = Marshal.ReadInt16(buf, offset + 30);
                if (groupCount <= 0) { offset += size; continue; }
                int maskOff = offset + 32; // GROUP_AFFINITY 起始
                for (int g = 0; g < groupCount && maskOff + 8 <= offset + size; g++) {
                  long mask = Marshal.ReadInt64(buf, maskOff);
                  for (int bit = 0; bit < 64 && bit < info.TotalLogical; bit++) {
                    if ((mask & (1L << bit)) != 0) {
                      if (isEfficient) eSet.Add(bit);
                      else pSet.Add(bit);
                    }
                  }
                  maskOff += 16;
                }
                offset += size;
              }
            }
          } finally { Marshal.FreeHGlobal(buf); }
        }

        // ── Step 3: Fallback ──
        if (pSet.Count == 0 && eSet.Count == 0) {
          if (isHybrid && wmiPhysCores > 0 && info.TotalLogical > wmiPhysCores) {
            // 算术反推：P核有超线程，E核没有
            int pCount = info.TotalLogical - wmiPhysCores; // P = TL - PC
            // 假设 P 核在低位，E 核在高位 (Windows 枚举习惯)
            for (int i = 0; i < info.TotalLogical; i++)
              (i < pCount ? pSet : eSet).Add(i);
          } else {
            pSet.UnionWith(Enumerable.Range(0, info.TotalLogical));
          }
        }

        bool hasP = pSet.Count > 0;
        bool hasE = eSet.Count > 0;
        info.IsHybrid = hasP && hasE;
        info.PerformanceCores = hasP ? pSet.OrderBy(x => x).ToArray() : Enumerable.Range(0, info.TotalLogical).ToArray();
        info.EfficientCores = hasE ? eSet.OrderBy(x => x).ToArray() : Array.Empty<int>();

        // ── NUMA/CCD 检测 ──
        int ccd0Count = 0;
        try {
          int numaBytes = 4096;
          IntPtr numaBuf = Marshal.AllocHGlobal(numaBytes);
          try {
            int retLen2 = numaBytes;
            if (GetLogicalProcessorInformationEx(RelationNumaNode, numaBuf, ref retLen2) ||
                (Marshal.GetLastWin32Error() == 122 && GetLogicalProcessorInformationEx(RelationNumaNode, numaBuf, ref retLen2))) {
              int offset = 0;
              while (offset + 8 <= retLen2) {
                int hdrRel = Marshal.ReadInt32(numaBuf, offset);
                int hdrSize = Marshal.ReadInt32(numaBuf, offset + 4);
                if (hdrRel == RelationNumaNode && hdrSize > 0) {
                  int body = offset + 8;
                  uint nodeNum = (uint)Marshal.ReadInt32(numaBuf, body);
                  if (nodeNum == 0) {
                    ulong mask = (ulong)Marshal.ReadInt64(numaBuf, body + 4);
                    for (int i = 0; i < 64 && i < info.TotalLogical; i++)
                      if ((mask & (1UL << i)) != 0) ccd0Count++;
                  }
                }
                offset += Math.Max(hdrSize, 8);
              }
            }
          } finally { Marshal.FreeHGlobal(numaBuf); }
        } catch { }

        info.Ccd0Count = ccd0Count;
        info.Ccd1Count = info.TotalLogical - ccd0Count;
        info.IsDualCcd = ccd0Count > 0 && ccd0Count < info.TotalLogical;
        if (wmiPhysCores > 0) info.PhysicalCoreCount = wmiPhysCores;

        _cachedTopology = info;
      } catch {
        info.TotalLogical = Environment.ProcessorCount;
        info.PerformanceCores = Enumerable.Range(0, info.TotalLogical).ToArray();
        info.EfficientCores = Array.Empty<int>();
        _cachedTopology = info;
      }
      return info;
    }

    /// <summary>根据模式和拓扑计算亲和性掩码</summary>
    public static long ModeToAffinityMask(string mode, int[] preferredCores) {
      var topo = GetTopology();
      switch (mode) {
        case "All":
          return (1L << topo.TotalLogical) - 1;
        case "Performance":
          if (topo.PerformanceCores.Length > 0)
            return topo.PerformanceCores.Aggregate(0L, (mask, idx) => mask | (1L << idx));
          return (1L << topo.TotalLogical) - 1;
        case "Efficiency":
          if (topo.EfficientCores.Length > 0)
            return topo.EfficientCores.Aggregate(0L, (mask, idx) => mask | (1L << idx));
          if (topo.IsDualCcd && topo.Ccd1Count > 0) {
            long mask = 0;
            for (int i = topo.Ccd0Count; i < topo.TotalLogical; i++) mask |= 1L << i;
            return mask;
          }
          return (1L << (topo.TotalLogical / 2)) - 1;
        case "Auto": {
            var bench = LoadBenchResults();
            if (bench.Count > 0) {
              int take = Math.Max(1, topo.TotalLogical / 2);
              var best = bench.OrderBy(b => b.Score).Take(take).ToList();
              return best.Aggregate(0L, (mask, b) => mask | (1L << b.CoreIndex));
            }
            goto case "Performance";
          }
        case "Manual":
          if (preferredCores != null && preferredCores.Length > 0)
            return preferredCores.Aggregate(0L, (mask, idx) => mask | (1L << idx));
          return (1L << topo.TotalLogical) - 1;
        default:
          return (1L << topo.TotalLogical) - 1;
      }
    }

    // ══════════════════════════════════════
    //  核心竞速
    // ══════════════════════════════════════

    static List<CoreBenchResult> _benchCache;

    public static List<CoreBenchResult> LoadBenchResults() {
      if (_benchCache != null) return _benchCache;
      try {
        if (!File.Exists(BenchPath)) return new List<CoreBenchResult>();
        using (var ms = new MemoryStream(File.ReadAllBytes(BenchPath))) {
          var ser = new DataContractJsonSerializer(typeof(List<CoreBenchResult>));
          _benchCache = (List<CoreBenchResult>)ser.ReadObject(ms) ?? new List<CoreBenchResult>();
          return _benchCache;
        }
      } catch { return new List<CoreBenchResult>(); }
    }

    static void SaveBenchResults(List<CoreBenchResult> results) {
      try {
        var ser = new DataContractJsonSerializer(typeof(List<CoreBenchResult>));
        using (var ms = new MemoryStream()) {
          ser.WriteObject(ms, results);
          File.WriteAllBytes(BenchPath, ms.ToArray());
        }
        _benchCache = results;
      } catch { }
    }

    /// <summary>运行核心竞速，durationMs = 每个核心的测试时间</summary>
    public static List<CoreBenchResult> RunBenchmark(int durationMs = 500) {
      var topo = GetTopology();
      int total = topo.TotalLogical;
      var results = new List<CoreBenchResult>(total);
      long bestScore = long.MaxValue;

      for (int core = 0; core < total; core++) {
        long score = long.MaxValue;
        var thread = new Thread(() => {
          // ponytail: 绑定到指定核心 — 用 SetThreadAffinityMask
          IntPtr oldAff = SetThreadAffinityMask(GetCurrentThread(), new IntPtr(1L << core));
          var sw = Stopwatch.StartNew();
          long iters = 0;
          while (sw.ElapsedMilliseconds < durationMs) {
            // 纯数学计算，无内存分配，避免 GC 干扰
            double x = 1.0;
            for (int i = 0; i < 5000; i++)
              x = Math.Sin(x) * Math.Cos(x) + Math.Tan(x * 0.5);
            iters++;
          }
          sw.Stop();
          // ponytail: 用 ticks 而不是 ms，精度更高
          score = sw.ElapsedTicks;
          SetThreadAffinityMask(GetCurrentThread(), oldAff);
        }) {
          IsBackground = true,
          Priority = ThreadPriority.Lowest,
          Name = $"CoreBench-{core}"
        };
        thread.Start();
        thread.Join(durationMs + 2000);
        if (score < bestScore) bestScore = score;
        results.Add(new CoreBenchResult { CoreIndex = core, Score = score });
      }

      // 计算相对值
      if (bestScore > 0) {
        for (int i = 0; i < results.Count; i++) {
          results[i] = new CoreBenchResult {
            CoreIndex = results[i].CoreIndex,
            Score = results[i].Score,
            Relative = (double)bestScore / results[i].Score
          };
        }
      }

      // 按核心索引排序
      results.Sort((a, b) => a.CoreIndex.CompareTo(b.CoreIndex));
      SaveBenchResults(results);
      return results;
    }

    // ══════════════════════════════════════
    //  工具方法
    // ══════════════════════════════════════

    /// <summary>根据模式名称和拓扑生成掩码，并更新 entry 的 AffinityMask</summary>
    public static void ApplyModeToEntry(CoreKeepEntry entry, string mode, int[] preferredCores = null) {
      entry.CoreMode = mode;
      entry.PreferredCores = preferredCores;
      long mask = ModeToAffinityMask(mode, preferredCores ?? entry.PreferredCores);
      if (mask != 0) entry.AffinityMask = mask;
    }

    public static string PriorityClassName(uint val) {
      switch (val) {
        case 0x00000040: return Strings.CoreKeepPriorityIdle;
        case 0x00004000: return Strings.CoreKeepPriorityBelowNormal;
        case 0x00000020: return Strings.CoreKeepPriorityNormal;
        case 0x00008000: return Strings.CoreKeepPriorityAboveNormal;
        case 0x00000080: return Strings.CoreKeepPriorityHigh;
        case 0x00000100: return Strings.CoreKeepPriorityRealtime;
        default: return Strings.CoreKeepPriorityUnknown;
      }
    }

    public static string PriorityClassShortName(uint val) {
      switch (val) {
        case 0x00000040: return "IDLE";
        case 0x00004000: return "BELOW_NORMAL";
        case 0x00000020: return "NORMAL";
        case 0x00008000: return "ABOVE_NORMAL";
        case 0x00000080: return "HIGH";
        case 0x00000100: return "REALTIME";
        default: return val.ToString();
      }
    }

    public static void ClearTopologyCache() { _cachedTopology = null; }
  }
}