// CpuAffinity/CoreKeepService.cs - 兼容层 + 自动应用 + 守护 + 监控 + 竞速
// 保留旧 CoreKeepEntry/CoreKeepData 兼容旧 CoreKeep.json
// 内部用 RuleEngine + EnforcementService + CpuTopologyService 新架构
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading;

namespace OmenSuperHub.Services.CpuAffinity {

  // ══════════════════════════════════════
  //  旧 json 兼容模型（PerfPage/CoreKeepPage 共用）
  // ══════════════════════════════════════

  [DataContract]
  public class CoreKeepEntry {
    [DataMember] public bool Enabled { get; set; }
    [DataMember] public string ProcessName { get; set; }
    [DataMember] public uint PriorityClass { get; set; }
    [DataMember] public long AffinityMask { get; set; }
    [DataMember] public int ProcessId { get; set; }
    [DataMember] public string CapturedAt { get; set; }
    [DataMember] public bool GuardEnabled { get; set; } = true;
    [DataMember] public string CoreMode { get; set; } = "all-cores";
    [DataMember] public int[] PreferredCores { get; set; }
    [DataMember] public string EnforcementLevel { get; set; } = "hard-affinity";
    [DataMember] public string PathFilter { get; set; }
    [DataMember] public List<string> ExcludePatterns { get; set; }
  }

  [DataContract]
  public class CoreKeepData {
    [DataMember] public bool MasterEnabled { get; set; }
    [DataMember] public int GuardIntervalMs { get; set; } = 2000;
    [DataMember] public List<CoreKeepEntry> Entries { get; set; } = new List<CoreKeepEntry>();
  }

  public struct CoreTopologyInfo {
    public int TotalLogical;
    public int PhysicalCoreCount;
    public bool IsHybrid;
    public bool IsDualCcd;
    public int[] PerformanceCores;
    public int[] EfficientCores;
    public int Ccd0Count;
    public int Ccd1Count;
    public long Smt0Mask;
    public long Smt1Mask;
    public bool HasSmt;
  }

  public struct CoreBenchResult {
    public int CoreIndex;
    public long Score;
    public double Relative;
  }

  public struct ProcessAffinityState {
    public bool Running;
    public uint PriorityClass;
    public long AffinityMask;
  }

  // ══════════════════════════════════════
  //  CoreKeepService 静态门面
  // ══════════════════════════════════════

  public static class CoreKeepService {
    static readonly string ConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CoreKeep.json");
    static readonly string BenchPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CoreKeepBench.json");

    static readonly CpuTopologyService _topoService = new CpuTopologyService();
    static readonly JobObjectManager _jobManager = new JobObjectManager();
    static readonly EnforcementService _enforcement = new EnforcementService(_topoService, _jobManager);
    static readonly RuleEngine _ruleEngine = new RuleEngine();

    static ManagementEventWatcher _watcher;
    static Timer _guardTimer;
    static int _guardRunning;
    static CoreKeepData _activeData;

    // ── 拓扑 ──

    public static CpuTopology GetTopology() => _topoService.Detect();

    /// <summary>旧 PerfPage 兼容拓扑结构。</summary>
    public static CoreTopologyInfo GetTopologyInfo() {
      var t = _topoService.Detect();
      var info = new CoreTopologyInfo {
        TotalLogical = t.TotalLogicalProcessors,
        PhysicalCoreCount = t.PcoreCount,
        IsHybrid = t.EcoreCount > 0,
        IsDualCcd = t.Ccd0Mask != 0 && t.Ccd1Mask != 0,
        PerformanceCores = MaskToIndices(t.PcoreMask),
        EfficientCores = MaskToIndices(t.EcoreMask),
        Ccd0Count = BitCount(t.Ccd0Mask),
        Ccd1Count = BitCount(t.Ccd1Mask),
        Smt0Mask = (long)t.Smt0Mask,
        Smt1Mask = (long)t.Smt1Mask,
        HasSmt = t.SmtEnabled
      };
      return info;
    }

    static int[] MaskToIndices(ulong mask) {
      var list = new List<int>();
      for (int i = 0; i < 64; i++) {
        if ((mask & (1UL << i)) != 0) list.Add(i);
      }
      return list.ToArray();
    }

    static int BitCount(ulong mask) {
      int c = 0;
      while (mask != 0) { c++; mask &= mask - 1; }
      return c;
    }

    // ── 持久化 ──

    public static CoreKeepData Load() {
      try {
        if (!File.Exists(ConfigPath)) return new CoreKeepData();
        using (var fs = File.OpenRead(ConfigPath)) {
          var ser = new DataContractJsonSerializer(typeof(CoreKeepData));
          return (CoreKeepData)ser.ReadObject(fs) ?? new CoreKeepData();
        }
      } catch { return new CoreKeepData(); }
    }

    public static void Save(CoreKeepData data) {
      using (var fs = File.Create(ConfigPath)) {
        var ser = new DataContractJsonSerializer(typeof(CoreKeepData));
        ser.WriteObject(fs, data);
      }
      // ponytail: 保存后同步规则引擎
      SyncRuleEngine(data);
    }

    /// <summary>把 CoreKeepData 转换为 RuleEntry 同步到 RuleEngine。</summary>
    static void SyncRuleEngine(CoreKeepData data) {
      var rules = new List<RuleEntry>();
      if (data?.Entries != null) {
        int i = 0;
        foreach (var e in data.Entries) {
          if (!e.Enabled) { i++; continue; }
          rules.Add(new RuleEntry {
            Id = $"corekeep-{i}",
            Name = e.ProcessName ?? "",
            Enabled = e.Enabled,
            Match = new RuleMatch {
              Process = e.ProcessName ?? "",
              Path = e.PathFilter,
              Exclude = e.ExcludePatterns
            },
            Action = new RuleAction {
              Mode = string.IsNullOrEmpty(e.CoreMode) ? "all-cores" : e.CoreMode,
              Level = string.IsNullOrEmpty(e.EnforcementLevel) ? "hard-affinity" : e.EnforcementLevel,
              CpuPriority = PriorityToName(e.PriorityClass)
            }
          });
          i++;
        }
      }
      _ruleEngine.SetRules(rules);
    }

    static string PriorityToName(uint pc) {
      switch (pc) {
        case 0x40: return "idle";
        case 0x4000: return "belowNormal";
        case 0x20: return "normal";
        case 0x8000: return "aboveNormal";
        case 0x80: return "high";
        case 0x100: return "realtime";
        default: return null;
      }
    }

    // ── 自动应用 / 监控 ──

    /// <summary>CoreKeep 后台是否正在运行（watcher + guard timer 已启动）。</summary>
    public static bool IsRunning => _activeData != null;

    /// <summary>仅同步规则到 RuleEngine，不重启 watcher/guard。用于已运行时刷新规则。</summary>
    public static void SyncRules(CoreKeepData data) => SyncRuleEngine(data);

    public static void StartAutoApply(CoreKeepData data) {
      StopAutoApply();
      _activeData = data;
      SyncRuleEngine(data);
      // ponytail: ApplyAll 后台执行 — 同步枚举全部进程 + P/Invoke 设亲和性会阻塞 UI 线程数秒
      ThreadPool.QueueUserWorkItem(_ => { try { ApplyAll(); } catch { } });
      // ponytail: WMI watcher 创建+Start 后台执行 — ManagementEventWatcher.Start() 冷启动（WMI 服务首次连接）
      // 阻塞 UI 线程 1-3s，是"第一次打开核心保持卡顿且无法操作"的根因。
      // EventArrived 在 WMI 投递线程触发，不依赖 Start 调用线程。先赋值 _watcher 再 Start：StopAutoApply 可见即 Dispose；
      // Start 后二次校验 _watcher!=w 防泄漏（期间被 Stop 中断则丢弃此 watcher）。
      ThreadPool.QueueUserWorkItem(_ => {
        try {
          var w = new ManagementEventWatcher(new WqlEventQuery("SELECT * FROM Win32_ProcessStartTrace"));
          w.EventArrived += (s, e) => {
            try {
              int pid = Convert.ToInt32(e.NewEvent.Properties["ProcessID"].Value);
              string name = e.NewEvent.Properties["ProcessName"].Value?.ToString() ?? "";
              // ponytail: 延迟 200ms 让进程完成初始化，避免过早 Apply 失败
              ThreadPool.QueueUserWorkItem(_2 => {
                Thread.Sleep(200);
                try { ApplyToPidByName(pid, name); } catch { }
              });
            } catch { }
          };
          _watcher = w;
          w.Start();
          if (_watcher != w) { try { w.Stop(); w.Dispose(); } catch { } } // 期间被 Stop 中断则丢弃
        } catch { }
      });
      StartGuardTimer(data.GuardIntervalMs);
    }

    public static void StopAutoApply() {
      StopGuardTimer();
      try { _watcher?.Stop(); _watcher?.Dispose(); } catch { }
      _watcher = null;
      // ponytail: RelaxAll 后台执行 — 恢复亲和性是 fire-and-forget，不阻塞 UI
      var data = _activeData;
      _activeData = null;
      if (data?.Entries != null && data.Entries.Count > 0)
        ThreadPool.QueueUserWorkItem(_ => { try { RelaxAll(data); } catch { } });
    }

    static void ApplyAll() {
      var topo = _topoService.Detect();
      var procs = new List<Process>();
      try { procs = Process.GetProcesses().ToList(); } catch { return; }
      foreach (var p in procs) {
        try {
          if (p.Id == 0 || p.Id == 4) continue;
          string name = "";
          try { name = p.ProcessName + ".exe"; } catch { }
          ApplyToPidByName(p.Id, name);
        } catch { }
        finally { try { p.Dispose(); } catch { } }
      }
    }

    /// <summary>用 QueryFullProcessImageName 获取进程路径 — 比 Process.MainModule.FileName 快且可访问受保护进程。</summary>
    static string GetProcessPath(int pid) {
      if (pid <= 0) return "";
      try {
        IntPtr h = Kernel32.OpenProcess(ProcessAccess.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)pid);
        if (h == IntPtr.Zero) return "";
        try {
          var sb = new System.Text.StringBuilder(260);
          uint size = (uint)sb.Capacity;
          return Kernel32.QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : "";
        } finally { Kernel32.CloseHandle(h); }
      } catch { return ""; }
    }

    static void ApplyToPidByName(int pid, string name) {
      string path = GetProcessPath(pid);
      var rule = _ruleEngine.Match(name, path);
      if (rule == null) return;
      var topo = _topoService.Detect();
      _enforcement.Apply(pid, rule, topo);
    }

    static void RelaxAll(CoreKeepData data) {
      if (data?.Entries == null) return;
      var topo = _topoService.Detect();
      foreach (var e in data.Entries) {
        try {
          if (e.ProcessId > 0) {
            _enforcement.Relax(e.ProcessId, topo);
          } else if (!string.IsNullOrEmpty(e.ProcessName)) {
            string procName = e.ProcessName.Replace(".exe", "");
            Process[] procs;
            try { procs = Process.GetProcessesByName(procName); } catch { continue; }
            foreach (var p in procs) {
              try { _enforcement.Relax(p.Id, topo); }
              catch { }
              finally { try { p.Dispose(); } catch { } }
            }
          }
        } catch { }
      }
    }

    // ── 守护定时器 ──

    static void StartGuardTimer(int intervalMs) {
      StopGuardTimer();
      if (intervalMs < 500) intervalMs = 500;
      _guardTimer = new Timer(GuardTick, null, intervalMs, intervalMs);
    }

    static void StopGuardTimer() {
      var t = _guardTimer;
      _guardTimer = null;
      try { t?.Dispose(); } catch { }
    }

    public static void UpdateGuardInterval(int ms) {
      if (_guardTimer != null && ms >= 500) {
        _guardTimer.Change(ms, ms);
      }
    }

    static void GuardTick(object state) {
      if (Interlocked.Exchange(ref _guardRunning, 1) == 1) return;
      try {
        var topo = _topoService.Detect();
        Process[] procs;
        try { procs = Process.GetProcesses(); } catch { return; }

        foreach (var p in procs) {
          try {
            int pid = p.Id;
            if (pid == 0 || pid == 4) continue;
            string name = "";
            try { name = p.ProcessName + ".exe"; } catch { }
            string path = GetProcessPath(pid);

            var rule = _ruleEngine.Match(name, path);
            if (rule?.Action == null) continue;
            // 仅 hard/job 级别需持续守护
            if (rule.Action.Level != "hard-affinity" &&
                rule.Action.Level != "job-enforced" &&
                rule.Action.Level != "job-locked") continue;

            ulong expected = CpuTopology.BuildMask(topo,
              rule.Action.SocketIndex.HasValue && rule.Action.SocketIndex >= 0
                ? rule.Action.Mode + $"@socket{rule.Action.SocketIndex}"
                : rule.Action.Mode,
              rule.Action.GetCustomMask());
            if (expected == 0) continue;

            ulong current = _enforcement.QueryAffinity(pid);
            if (current == 0 || current == expected) continue;
            _enforcement.Apply(pid, rule, topo);
          } catch { }
          finally { try { p.Dispose(); } catch { } }
        }
      } catch { }
      finally { Volatile.Write(ref _guardRunning, 0); }
    }

    // ── 单条应用（PerfPage 旧调用） ──

    public static void ApplyToProcess(string processName, CoreKeepEntry entry) {
      if (entry == null || !entry.Enabled) return;
      var topo = _topoService.Detect();
      ulong mask = entry.AffinityMask != 0
        ? (ulong)entry.AffinityMask
        : CpuTopology.BuildMask(topo, entry.CoreMode ?? "all-cores");

      Process[] procs;
      string pn = (processName ?? "").Replace(".exe", "");
      try { procs = Process.GetProcessesByName(pn); } catch { return; }

      foreach (var p in procs) {
        try {
          var rule = new RuleEntry {
            Id = $"corekeep-{p.Id}",
            Name = processName,
            Match = new RuleMatch { Process = processName ?? "" },
            Action = new RuleAction {
              Mode = entry.CoreMode ?? "all-cores",
              Level = string.IsNullOrEmpty(entry.EnforcementLevel) ? "hard-affinity" : entry.EnforcementLevel,
              CpuPriority = PriorityToName(entry.PriorityClass)
            }
          };
          _enforcement.Apply(p.Id, rule, topo);
        } catch { }
        finally { try { p.Dispose(); } catch { } }
      }
    }

    // ── 模式掩码构建（旧 API） ──

    public static long ModeToAffinityMask(string mode, int[] selectedCores) {
      var topo = _topoService.Detect();
      if (mode == "Manual" && selectedCores != null) {
        ulong m = 0;
        foreach (int i in selectedCores) if (i >= 0 && i < 64) m |= 1UL << i;
        return (long)CpuTopology.ClampToLogicalProcessors(m, topo.TotalLogicalProcessors);
      }
      // 旧 UI mode 名映射到新 mode 名
      string newMode = MapLegacyMode(mode);
      return (long)CpuTopology.BuildMask(topo, newMode);
    }

    /// <summary>旧 UI 模式名 → 新模式名。</summary>
    static string MapLegacyMode(string mode) {
      if (string.IsNullOrEmpty(mode)) return "all-cores";
      switch (mode) {
        case "All": return "all-cores";
        case "Performance": return "p-cores";
        case "Efficient": return "e-cores";
        case "Auto": return "p-cores"; // ponytail: 旧 Auto 默认走 P 核
        case "Manual": return "custom";
        case "PerformanceFirst": return "p-cores-first";
        case "NoSmt": return "no-smt";
        default: return mode; // 新模式名直接透传
      }
    }

    // ── 状态查询（旧 API） ──

    public static ProcessAffinityState QueryProcessState(string processName, int pid = 0) {
      if (pid > 0) return QueryByPid(pid);
      if (!string.IsNullOrEmpty(processName)) {
        Process[] procs;
        try { procs = Process.GetProcessesByName(processName.Replace(".exe", "")); } catch { return default; }
        foreach (var p in procs) {
          try {
            var s = QueryByPid(p.Id);
            if (s.Running) return s;
          } finally { try { p.Dispose(); } catch { } }
        }
      }
      return default;
    }

    static ProcessAffinityState QueryByPid(int pid) {
      try {
        var p = Process.GetProcessById(pid);
        try {
          ulong mask = _enforcement.QueryAffinity(pid);
          uint prio = _enforcement.QueryPriority(pid);
          return new ProcessAffinityState {
            Running = true,
            PriorityClass = prio,
            AffinityMask = (long)(mask != 0 ? mask : (ulong)p.ProcessorAffinity.ToInt64())
          };
        } finally { try { p.Dispose(); } catch { } }
      } catch { return default; }
    }

    // ── 捕获（旧 API） ──

    public static CoreKeepEntry CaptureFromPid(int pid) {
      var e = new CoreKeepEntry { Enabled = true, ProcessId = pid, CapturedAt = DateTime.Now.ToString("s") };
      try {
        var p = Process.GetProcessById(pid);
        try {
          e.ProcessName = p.ProcessName + ".exe";
          try { e.AffinityMask = p.ProcessorAffinity.ToInt64(); } catch { }
        } finally { try { p.Dispose(); } catch { } }
      } catch { }
      return e;
    }

    public static CoreKeepEntry CaptureFromProcess(string processName) {
      return new CoreKeepEntry {
        Enabled = true,
        ProcessName = processName,
        CapturedAt = DateTime.Now.ToString("s")
      };
    }

    public static void ApplyModeToEntry(CoreKeepEntry entry, string mode) {
      if (entry == null) return;
      entry.CoreMode = mode;
      entry.AffinityMask = ModeToAffinityMask(mode, null);
    }

    // ── 优先级名 ──

    public static string PriorityClassName(uint pc) {
      switch (pc) {
        case 0x40: return "Idle";
        case 0x4000: return "BelowNormal";
        case 0x20: return "Normal";
        case 0x8000: return "AboveNormal";
        case 0x80: return "High";
        case 0x100: return "RealTime";
        default: return pc == 0 ? "-" : "0x" + pc.ToString("X");
      }
    }

    // ── 核心竞速 ──

    public static List<CoreBenchResult> RunBenchmark(int iterations) {
      var topo = _topoService.Detect();
      var results = new List<CoreBenchResult>();
      long best = long.MaxValue;
      for (int core = 0; core < topo.TotalLogicalProcessors && core < 64; core++) {
        long score = BenchCore(core, iterations);
        results.Add(new CoreBenchResult { CoreIndex = core, Score = score });
        if (score < best) best = score;
      }
      for (int i = 0; i < results.Count; i++) {
        var r = results[i];
        r.Relative = best == 0 ? 0 : (double)best / r.Score;
        results[i] = r;
      }
      SaveBench(results);
      return results;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    static extern IntPtr SetThreadAffinityMask(IntPtr hThread, IntPtr dwThreadAffinityMask);
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    static extern IntPtr GetCurrentThread();

    static long BenchCore(int coreIdx, int iterations) {
      var sw = System.Diagnostics.Stopwatch.StartNew();
      // ponytail: 简单 CPU 烧测 — 设置线程亲和性后做密集乘法
      try {
        var old = SetThreadAffinityMask(GetCurrentThread(), new IntPtr(1L << coreIdx));
        try {
          long acc = 0;
          for (int i = 0; i < iterations; i++) acc += i * i;
          // 防止优化器消除
          if (acc == long.MaxValue) GC.KeepAlive(acc);
        } finally { SetThreadAffinityMask(GetCurrentThread(), old); }
      } catch { }
      sw.Stop();
      return sw.ElapsedTicks;
    }

    static void SaveBench(List<CoreBenchResult> results) {
      try {
        using (var fs = File.Create(BenchPath)) {
          var ser = new DataContractJsonSerializer(typeof(List<CoreBenchResult>));
          ser.WriteObject(fs, results);
        }
      } catch { }
    }

    public static List<CoreBenchResult> LoadBench() {
      try {
        if (!File.Exists(BenchPath)) return null;
        using (var fs = File.OpenRead(BenchPath)) {
          var ser = new DataContractJsonSerializer(typeof(List<CoreBenchResult>));
          return (List<CoreBenchResult>)ser.ReadObject(fs);
        }
      } catch { return null; }
    }

    // ── 进程枚举（参考 CpuAffinityManager ProcessListViewModel） ──

    public struct ProcessInfo {
      public int Pid;
      public string Name;
      public string Path;
      public string AffinityHex;
      public string MatchedRule;
      public string RuleLevel;
    }

    /// <summary>枚举所有进程并匹配规则，返回带规则信息的列表。</summary>
    public static List<ProcessInfo> EnumerateProcesses() {
      var result = new List<ProcessInfo>();
      Process[] procs;
      try { procs = Process.GetProcesses(); } catch { return result; }
      foreach (var proc in procs) {
        try {
          int pid = proc.Id;
          if (pid == 0 || pid == 4) continue;
          string name = "";
          try { name = proc.ProcessName + ".exe"; } catch { continue; }
          string path = GetProcessPath(pid);
          string aff = "N/A";
          try { aff = $"0x{proc.ProcessorAffinity.ToInt64():X}"; } catch { }
          var rule = _ruleEngine.Match(name, path);
          result.Add(new ProcessInfo {
            Pid = pid, Name = name, Path = string.IsNullOrEmpty(path) ? "(protected)" : path,
            AffinityHex = aff, MatchedRule = rule?.Name ?? "", RuleLevel = rule?.Action.Level ?? ""
          });
        } catch { }
        finally { try { proc.Dispose(); } catch { } }
      }
      return result.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Pid).ToList();
    }

    // ── 快速应用（不创建规则，直接对 PID 应用 mode+level） ──

    /// <summary>对指定 PID 快速应用亲和性，不持久化为规则。</summary>
    public static bool QuickApply(int pid, string mode, string level) {
      if (pid <= 0) return false;
      var topo = _topoService.Detect();
      var rule = new RuleEntry {
        Id = "quick", Name = "Quick Action",
        Action = new RuleAction { Mode = mode, Level = level }
      };
      return _enforcement.Apply(pid, rule, topo);
    }

    /// <summary>恢复指定 PID 为全部核心。</summary>
    public static bool RelaxPid(int pid) {
      if (pid <= 0) return false;
      var topo = _topoService.Detect();
      return _enforcement.Relax(pid, topo);
    }

    // ── 拓扑可视化数据 ──

    public struct CoreVisualItem {
      public int Index;
      public string CoreType;   // "P" / "E" / "SMT0" / "SMT1"
      public string Tooltip;
    }

    /// <summary>返回核心可视化列表，用于 UI 绘制颜色编码的核心网格。</summary>
    public static List<CoreVisualItem> GetCoreVisuals() {
      var t = _topoService.Detect();
      var list = new List<CoreVisualItem>();
      for (int i = 0; i < t.TotalLogicalProcessors && i < 64; i++) {
        ulong bit = 1UL << i;
        string type, tooltip;
        if ((t.PcoreMask & bit) != 0) {
          type = "P";
          tooltip = $"Core {i}: P-Core (Performance)";
        } else if ((t.EcoreMask & bit) != 0) {
          type = "E";
          tooltip = $"Core {i}: E-Core (Efficient)";
        } else if (t.SmtEnabled && (t.Smt1Mask & bit) != 0) {
          type = "SMT1";
          tooltip = $"Core {i}: SMT Thread 1";
        } else {
          type = "SMT0";
          tooltip = $"Core {i}: SMT Thread 0";
        }
        list.Add(new CoreVisualItem { Index = i, CoreType = type, Tooltip = tooltip });
      }
      return list;
    }
  }
}
