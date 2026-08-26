// CpuAffinity/CoreKeepService.cs - 兼容层 + 自动应用 + 守护 + 监控 + 竞速
// 保留旧 CoreKeepEntry/CoreKeepData 兼容旧 CoreKeep.json
// 内部用 RuleEngine + EnforcementService + CpuTopologyService 新架构
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
using Microsoft.Win32;
using OmenSuperHub.Services.SystemOptimization;

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
    // ponytail: IFEO IO 优先级，-1=不设置，0=VeryLow 1=Low 3=High（2=Normal 是默认无需写）。持久化到注册表 IFEO。
    [DataMember] public int IoPriority { get; set; } = -1;
    // ponytail: 内存优先级，-1=不设置，1=VeryLow 2=Low 3=Medium 4=BelowNormal 5=Normal(进程默认)。
    // 运行时 SetProcessInformation 生效，非持久化注册表。
    [DataMember] public int MemoryPriority { get; set; } = -1;
    /// <summary>把最忙线程绑定到亲和性掩码的首核。</summary>
    [DataMember] public bool MainThreadBind { get; set; }
  }

  [DataContract]
  public class CoreKeepData {
    [DataMember] public bool MasterEnabled { get; set; }
    [DataMember] public int GuardIntervalMs { get; set; } = 2000;
    [DataMember] public List<CoreKeepEntry> Entries { get; set; } = new List<CoreKeepEntry>();
    // ponytail: 系统保留 CPU 核心集 — 全局黑名单，所有进程的 affinity 会剔除这些核心。
    // 用户态 hard-affinity 实现，非内核级保留；守护定时器周期重设。
    [DataMember] public bool SystemReservedEnabled { get; set; }
    [DataMember] public int[] SystemReservedCores { get; set; }
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
              CpuPriority = PriorityToName(e.PriorityClass),
              MemoryPriority = MemoryPriorityToName(e.MemoryPriority),
              MainThreadBind = e.MainThreadBind
            }
          });
          i++;
        }
      }
      _ruleEngine.SetRules(rules);
      // ponytail: IFEO IO 优先级持久化到注册表，无需守护定时器重设（启动时读一次）
      SyncIfeoIoPriority(data);
    }

    const string IfeoRoot = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options";

    /// <summary>把所有规则的 IoPriority 同步到 IFEO 注册表。已存在且值相同则跳过。</summary>
    static void SyncIfeoIoPriority(CoreKeepData data) {
      if (data?.Entries == null) return;
      using (var root = Microsoft.Win32.RegistryKey.OpenBaseKey(
        Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64)) {
        // 第一遍：收集已处理的 exe 名，避免重复
        var handled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in data.Entries) {
          if (e == null || !e.Enabled) continue;
          string exe = (e.ProcessName ?? "").Trim();
          if (string.IsNullOrEmpty(exe)) continue;
          // 规范化：确保带 .exe
          if (!exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) exe += ".exe";
          if (!handled.Add(exe.ToLowerInvariant())) continue;
          try { WriteIfeoIoPriority(root, exe, e.IoPriority); } catch { }
        }
      }
    }

    static void WriteIfeoIoPriority(Microsoft.Win32.RegistryKey root, string exe, int priority) {
      using (var key = root.CreateSubKey(IfeoRoot + @"\" + exe, true)) {
        if (key == null) return;
        if (priority < 0) {
          // -1 = 不设置：删除值，若子键空则删除子键
          key.DeleteValue("IoPriority", false);
          key.Flush();
          // 不主动删子键——其他工具可能也用了这个 IFEO 键
        } else {
          var cur = key.GetValue("IoPriority", null,
            Microsoft.Win32.RegistryValueOptions.DoNotExpandEnvironmentNames);
          if (cur is int existing && existing == priority) return; // 无变化
          key.SetValue("IoPriority", priority, Microsoft.Win32.RegistryValueKind.DWord);
          key.Flush();
        }
      }
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

    /// <summary>内存优先级 int → 规则动作名；&lt;1 或 &gt;5 返回 null（不设置）。</summary>
    static string MemoryPriorityToName(int p) {
      switch (p) {
        case 1: return "veryLow";
        case 2: return "low";
        case 3: return "medium";
        case 4: return "belowNormal";
        case 5: return "normal";
        default: return null;
      }
    }

    /// <summary>内存优先级名 → int；无效返回 -1（不设置）。</summary>
    public static int MemoryPriorityToInt(string name) {
      if (string.IsNullOrEmpty(name)) return -1;
      switch (name.ToLowerInvariant()) {
        case "verylow": return 1;
        case "low": return 2;
        case "medium": return 3;
        case "belownormal": return 4;
        case "normal": return 5;
        default: return -1;
      }
    }

    // ── 系统保留 CPU 核心集（ReservedCpuSets 注册表，Win11 19044+，重启生效） ──
    // 系统保留 CPU 集注册表语义：
    // HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\kernel\ReservedCpuSets = REG_BINARY(8字节 ulong LE)
    // mask 的 set bit = 内核保留的核心（不调度给用户进程）。写入后重启生效。

    const string ReservedCpuSetsKeyPath = @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel";
    const string ReservedCpuSetsValueName = "ReservedCpuSets";

    /// <summary>Win11 build ≥ 19044 才支持 ReservedCpuSets。</summary>
    // ponytail: 不能用 Environment.OSVersion — app.manifest 未声明 supportedOS Win10/11，
    // 系统会以 Win8 兼容模式返回 6.2.9200，导致真实 Win11 被误判为不支持。改读注册表 CurrentBuild。
    public static bool IsReservedCpuSetsSupported() {
      try {
        using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
          @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", false)) {
          if (key == null) return false;
          var v = key.GetValue("CurrentBuild", null,
            Microsoft.Win32.RegistryValueOptions.DoNotExpandEnvironmentNames);
          if (v == null) return false;
          return int.TryParse(v.ToString(), out int build) && build >= 19044;
        }
      } catch { return false; }
    }

    /// <summary>系统保留核心掩码（set bit = 保留的核心）。0 表示无保留。</summary>
    // ponytail: 必须直接 Load() 而非 _activeData —— 页面 Save() 不更新 _activeData，
    // 守护运行时会读到旧数据导致 mask=0（写入被当作清除）。
    public static ulong GetReservedMask() {
      var data = Load();
      if (!data.SystemReservedEnabled || data.SystemReservedCores == null || data.SystemReservedCores.Length == 0)
        return 0;
      ulong m = 0;
      foreach (int i in data.SystemReservedCores) if (i >= 0 && i < 64) m |= 1UL << i;
      return m;
    }

    /// <summary>读注册表当前 ReservedCpuSets 掩码，不存在或读取失败返回 0。</summary>
    public static ulong ReadReservedCpuSetsRegistry() {
      try {
        using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(ReservedCpuSetsKeyPath, false)) {
          if (key == null) return 0;
          if (!(key.GetValue(ReservedCpuSetsValueName, null,
                Microsoft.Win32.RegistryValueOptions.DoNotExpandEnvironmentNames) is byte[] bytes) || bytes.Length != 8)
            return 0;
          return BitConverter.ToUInt64(bytes, 0);
        }
      } catch { return 0; }
    }

    /// <summary>写注册表 ReservedCpuSets。mask=0 时删除值。返回是否成功。需重启生效。</summary>
    public static bool WriteReservedCpuSetsRegistry(ulong mask) {
      if (!IsReservedCpuSetsSupported()) return false;
      try {
        using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(ReservedCpuSetsKeyPath, true)) {
          if (key == null) return false;
          if (mask == 0) {
            key.DeleteValue(ReservedCpuSetsValueName, false);
          } else {
            byte[] data = BitConverter.GetBytes(mask); // little-endian ulong, 8 bytes
            key.SetValue(ReservedCpuSetsValueName, data, Microsoft.Win32.RegistryValueKind.Binary);
          }
          key.Flush();
          return true;
        }
      } catch { return false; }
    }

    /// <summary>应用系统保留到注册表（基于当前配置）。返回是否写入成功。需重启生效。</summary>
    public static bool ApplySystemReservedToRegistry() {
      ulong mask = GetReservedMask();
      // 启用时 mask 可能为 0（用户未勾选核心）— 仍写 0 等于清除
      if (!GetReservedEnabled()) mask = 0;
      return WriteReservedCpuSetsRegistry(mask);
    }

    /// <summary>系统保留是否启用。</summary>
    public static bool GetReservedEnabled() {
      var data = Load();
      return data.SystemReservedEnabled;
    }

    /// <summary>读系统当前实际生效的保留核心 mask。
    /// 依据 CPU Set 的 AllFlags bit3 (RealTime)
    /// 即内核实际保留的核心，重启后与注册表 ReservedCpuSets 一致表示已生效。</summary>
    public static ulong ReadEffectiveReservedMask() {
      try {
        if (!Kernel32.GetSystemCpuSetInformation(IntPtr.Zero, 0, out uint retLen, IntPtr.Zero, 0) || retLen == 0)
          return 0;
        IntPtr buf = Marshal.AllocHGlobal((int)retLen);
        try {
          if (!Kernel32.GetSystemCpuSetInformation(buf, retLen, out _, IntPtr.Zero, 0))
            return 0;
          ulong mask = 0;
          uint offset = 0;
          while (offset + 8 <= retLen) {
            uint size = (uint)Marshal.ReadInt32(buf, (int)offset);
            if (size < 32 || size == 0) break;
            int type = Marshal.ReadInt32(buf, (int)offset + 4);
            if (type == 0) {
              // SYSTEM_CPU_SET_INFORMATION: Size@0 Type@4 Union@8 → LPI@14 AllFlags@19
              byte lpi = Marshal.ReadByte(buf, (int)offset + 14);
              byte flags = Marshal.ReadByte(buf, (int)offset + 19);
              if ((flags & 0x08) != 0 && lpi < 64) mask |= 1UL << lpi;
            }
            offset += size;
          }
          return mask;
        } finally { Marshal.FreeHGlobal(buf); }
      } catch { return 0; }
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
      // ReservedCpuSets 是注册表方案（重启生效），不需要守护定时器重设，故不在此调用
      ThreadPool.QueueUserWorkItem(_ => { try { ApplyAll(); } catch { } });
      // ponytail: 删除 WMI __InstanceCreationEvent watcher — 订阅会让 wmiprvse.exe(WMI 宿主)
      // 常驻占内存,且内核 ETW trace 类在本机不投递。守护定时器本身每 intervalMs 全量
      // ApplyAll 已覆盖新进程应用;首次 due 提前到 500ms 补偿启动突发,新进程最迟 ~2s(默认)生效。
      StartGuardTimer(data.GuardIntervalMs);
    }

    public static void StopAutoApply() {
      StopGuardTimer();
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
      // ponytail: 首次 due 500ms — 补偿已删除的 WMI watcher,让启动瞬间的进程尽快被首轮 Apply 覆盖
      _guardTimer = new Timer(GuardTick, null, 500, intervalMs);
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
              CpuPriority = PriorityToName(entry.PriorityClass),
              MemoryPriority = MemoryPriorityToName(entry.MemoryPriority),
              MainThreadBind = entry.MainThreadBind
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
      /// <summary>CPU 占用率百分比，-1 = 未采样/无法访问。</summary>
      public double CpuUsagePercent;
      /// <summary>是否可被批量路由：排除系统进程(pid≤4)与自身。</summary>
      public bool CanBatchManage;
    }

    static readonly int _selfPid = Process.GetCurrentProcess().Id;

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
            AffinityHex = aff, MatchedRule = rule?.Name ?? "", RuleLevel = rule?.Action.Level ?? "",
            CpuUsagePercent = -1, CanBatchManage = pid > 4 && pid != _selfPid
          });
        } catch { }
        finally { try { proc.Dispose(); } catch { } }
      }
      return result.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Pid).ToList();
    }

    // ── CPU 占用采样（GetProcessTimes 两拍差分） ──

    /// <summary>CPU% 纯计算：cpu 时间差分(100ns) / 墙钟时间 / 核心数 × 100。供自检与采样共用。</summary>
    public static double ComputeCpuPercent(long delta100ns, long elapsedTicks, long frequency, int cores) {
      if (elapsedTicks <= 0 || cores <= 0 || delta100ns < 0) return 0;
      double wallSec = (double)elapsedTicks / frequency;
      double cpuSec = (double)delta100ns / 1e7;
      if (wallSec <= 0) return 0;
      double pct = cpuSec / wallSec / cores * 100.0;
      if (pct < 0) return 0;
      if (pct > 100) return 100;
      return pct;
    }

    static long ProcessCpuTicks(int pid) {
      if (pid <= 0) return -1;
      try {
        IntPtr h = Kernel32.OpenProcess(ProcessAccess.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)pid);
        if (h == IntPtr.Zero) return -1;
        try {
          if (!Kernel32.GetProcessTimes(h, out FILETIME c, out FILETIME e, out FILETIME k, out FILETIME u))
            return -1;
          return k.Ticks100 + u.Ticks100;
        } finally { Kernel32.CloseHandle(h); }
      } catch { return -1; }
    }

    /// <summary>两拍差分采样 CPU 占用率（t1 记录 → interval 后 t2 → 差分/墙钟/核数）。</summary>
    public static Dictionary<int, double> SampleCpuUsage(List<int> pids, int intervalMs) {
      var result = new Dictionary<int, double>();
      if (pids == null || pids.Count == 0) return result;
      int cores = Environment.ProcessorCount;
      var t0 = new Dictionary<int, long>();
      foreach (int pid in pids) t0[pid] = ProcessCpuTicks(pid);
      if (intervalMs < 50) intervalMs = 50;
      Thread.Sleep(intervalMs);
      var sw = Stopwatch.StartNew();
      foreach (int pid in pids) {
        long prev = t0.TryGetValue(pid, out long p0) ? p0 : -1;
        long cur = ProcessCpuTicks(pid);
        result[pid] = (prev < 0 || cur < 0)
          ? -1
          : ComputeCpuPercent(cur - prev, sw.ElapsedTicks, Stopwatch.Frequency, cores);
      }
      return result;
    }

    /// <summary>枚举 + CPU 采样合并（调用方放在后台线程，采样耗时约 intervalMs）。</summary>
    public static List<ProcessInfo> EnumerateProcessesWithCpu(int intervalMs = 500) {
      var procs = EnumerateProcesses();
      var pids = new List<int>();
      foreach (var p in procs) pids.Add(p.Pid);
      var cpu = SampleCpuUsage(pids, intervalMs);
      for (int i = 0; i < procs.Count; i++) {
        var pi = procs[i];
        pi.CpuUsagePercent = cpu.TryGetValue(pi.Pid, out double v) ? v : -1;
        procs[i] = pi;
      }
      return procs;
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
    // ponytail: 判定顺序必须 SMT1 → E → SMT0 → P。
    // hybrid CPU 上 P 核的 SMT 兄弟线程效率类相同、全在 PcoreMask 里，若先判 PcoreMask
    // 则 SMT 线程全显示成 P，表格里永远看不到 SMT0/SMT1（原实现 bug）。
    public static List<CoreVisualItem> GetCoreVisuals() {
      var t = _topoService.Detect();
      var list = new List<CoreVisualItem>();
      for (int i = 0; i < t.TotalLogicalProcessors && i < 64; i++) {
        ulong bit = 1UL << i;
        string type, tooltip;
        if (t.SmtEnabled && (t.Smt1Mask & bit) != 0) {
          type = "SMT1";
          tooltip = $"Core {i}: SMT Thread 1";
        } else if ((t.EcoreMask & bit) != 0) {
          type = "E";
          tooltip = $"Core {i}: E-Core (Efficient)";
        } else if (t.SmtEnabled && (t.Smt0Mask & bit) != 0) {
          type = "SMT0";
          tooltip = $"Core {i}: SMT Thread 0";
        } else {
          type = "P";
          tooltip = $"Core {i}: P-Core (Performance)";
        }
        list.Add(new CoreVisualItem { Index = i, CoreType = type, Tooltip = tooltip });
      }
      return list;
    }
  }

  // ══════════════════════════════════════
  //  自检（ponytail: 新逻辑留一个可运行检查 — App.exe --selftest）
  // ══════════════════════════════════════
  public static class SelfCheck {
    public static string Run() {
      var failures = new List<string>();

      // 内存优先级名 ↔ int 映射：CoreKeepService 与 EnforcementService 两处解析必须一致
      void CheckMem(string name, int expected) {
        int a = CoreKeepService.MemoryPriorityToInt(name);
        int b = EnforcementService.ParseMemoryPriority(name);
        if (a != expected || b != expected)
          failures.Add($"MemoryPriority '{name}' => {a}/{b}, want {expected}");
      }
      CheckMem("veryLow", 1);
      CheckMem("low", 2);
      CheckMem("medium", 3);
      CheckMem("belowNormal", 4);
      CheckMem("normal", 5);
      CheckMem("", -1);
      CheckMem("garbage", -1);

      // CPU% 纯计算：1 核满载 1 秒 = 100%，4 核满载 = 25%，半载 = 50%
      void CheckCpu(long delta100ns, long elapsedTicks, long freq, int cores, double expected) {
        double got = CoreKeepService.ComputeCpuPercent(delta100ns, elapsedTicks, freq, cores);
        if (Math.Abs(got - expected) > 0.5)
          failures.Add($"ComputeCpuPercent({delta100ns},{elapsedTicks},{freq},{cores}) = {got:F1}, want {expected:F1}");
      }
      long freq = 10_000_000; // 1 秒 = 1e7 拍（简化自 Stopwatch.Frequency 的单位自洽性）
      CheckCpu(10_000_000, freq, freq, 1, 100.0);
      CheckCpu(10_000_000, freq, freq, 4, 25.0);
      CheckCpu(5_000_000, freq, freq, 1, 50.0);
      CheckCpu(0, freq, freq, 4, 0.0);
      CheckCpu(-1, freq, freq, 4, 0.0);   // 采样失败路径
      CheckCpu(10_000_000, 0, freq, 4, 0.0); // elapsed=0 防护

      // 启动项命令有效性：真实存在的路径 → 有效(false)；明确不存在的绝对路径 → 无效(true)
      string windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
      void CheckCmd(string cmd, bool expectInvalid) {
        bool got = StartupItemOptimizer.IsInvalidCommand(cmd);
        if (got != expectInvalid)
          failures.Add($"IsInvalidCommand('{cmd}') = {got}, want {expectInvalid}");
      }
      CheckCmd("", true);
      CheckCmd("   ", true);
      CheckCmd(Path.Combine(windir, "explorer.exe"), false);
      CheckCmd($"\"{Path.Combine(windir, "explorer.exe")}\" --flag", false);
      CheckCmd(Path.Combine(windir, "notepad.exe"), false);
      CheckCmd(@"C:\Definitely\Not\A\Real\Path.exe", true);
      CheckCmd(@"explorer.exe", false);                    // 相对路径：无法判定 → 视为有效
      CheckCmd("%UNSET_VAR_XYZ%\\tool.exe", false);        // 含未展开变量 → 无法判定 → 视为有效
      CheckCmd(@"C:\Definitely\Not\A\Real\Dir\", true);    // 不存在目录

      // 启动项 ID 编解码往返（hku/hklm64/hklm32 各测一个）
      void CheckId(string locId, string name) {
        if (!StartupItemOptimizer.TryParseId(
            StartupItemOptimizer.MakeId(locId, name), out var loc, out string parsed) ||
            loc.Id != locId || parsed != name)
          failures.Add($"StartupItem id roundtrip failed for {locId}:{name}");
      }
      CheckId("hku", "OneDrive");
      CheckId("hklm64", "Steam Update");
      CheckId("hklm32", "安装程序");

      // 启动项禁用键名对称往返：Run↔RunDisabled、RunOnce↔RunOnceDisabled，且互逆还原
      {
        foreach (var baseKey in new[] {
          "Software\\Microsoft\\Windows\\CurrentVersion\\Run",
          "Software\\Microsoft\\Windows\\CurrentVersion\\RunOnce",
          "Software\\Wow6432Node\\Microsoft\\Windows\\CurrentVersion\\Run",
        }) {
          string dis = StartupItemOptimizer.DisabledSubKey(baseKey);
          string en  = StartupItemOptimizer.EnabledSubKey(dis);
          if (en != baseKey)
            failures.Add($"Startup subkey roundtrip failed: {baseKey} -> {dis} -> {en}");
        }
        // SetEnabled 的空守卫：Id 已是目标状态时返回 true 且不动注册表
        var noop = new StartupItem { Id = StartupItemOptimizer.MakeId("hku", "x"),
          Name = "x", Command = "x", IsEnabled = false, ItemType = StartupItemType.Registry };
        if (StartupItemOptimizer.SetEnabled(noop, enabled: false) != true)
          failures.Add("SetEnabled(false->false) short-circuit should return true");
        if (StartupItemOptimizer.SetEnabled(null, true))
          failures.Add("SetEnabled(null) should return false");
      }

      // 服务修改前置校验（不触碰 SCM 管理器，纯参数守卫）
      if (SystemServiceOptimizer.SetStartupType("", ServiceStartupType.Automatic))
        failures.Add("SetStartupType('') should fail");
      if (SystemServiceOptimizer.SetStartupType("svc", ServiceStartupType.Unknown))
        failures.Add("SetStartupType(Unknown) should fail");
      if (SystemServiceOptimizer.SetStartupType("svc", ServiceStartupType.Boot))
        failures.Add("SetStartupType(Boot) should fail");
      if (SystemServiceOptimizer.SetStartupType(@"a\b", ServiceStartupType.Manual))
        failures.Add("SetStartupType with '\\' should fail");

      // 一键优化预设完整性：非空、不重复、目标只能是 手动/禁用
      var preset = SystemServiceOptimizer.RecommendedPreset;
      var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      foreach (var (name, target) in preset) {
        if (string.IsNullOrWhiteSpace(name))
          failures.Add("RecommendedPreset contains empty name");
        if (!seenNames.Add(name))
          failures.Add($"RecommendedPreset duplicate service: {name}");
        if (target != ServiceStartupType.Manual && target != ServiceStartupType.Disabled)
          failures.Add($"RecommendedPreset '{name}' target must be Manual/Disabled, got {target}");
      }
      if (preset.Length == 0)
        failures.Add("RecommendedPreset is empty");

      // 恢复预设完整性：非空、不重复、目标只能是合法启动类型；且与推荐预设覆盖同一组服务
      var defPreset = SystemServiceOptimizer.DefaultPreset;
      var defNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      foreach (var (name, target) in defPreset) {
        if (string.IsNullOrWhiteSpace(name))
          failures.Add("DefaultPreset contains empty name");
        if (!defNames.Add(name))
          failures.Add($"DefaultPreset duplicate service: {name}");
        if (target != ServiceStartupType.Automatic && target != ServiceStartupType.Manual && target != ServiceStartupType.Disabled)
          failures.Add($"DefaultPreset '{name}' target must be Automatic/Manual/Disabled, got {target}");
      }
      if (defPreset.Length == 0)
        failures.Add("DefaultPreset is empty");
      foreach (var (name, _) in preset)
        if (!defNames.Contains(name))
          failures.Add($"DefaultPreset missing service covered by RecommendedPreset: {name}");
      foreach (var (name, _) in defPreset)
        if (!seenNames.Contains(name))
          failures.Add($"RecommendedPreset missing service covered by DefaultPreset: {name}");

      // 通用优化项定义完整性：Id 唯一、Edits 非空、每个注册表修改字段合法
      var tweaks = SystemTweaks.All;
      if (tweaks.Length == 0)
        failures.Add("SystemTweaks.All is empty");
      var seenTweakIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      foreach (var t in tweaks) {
        if (string.IsNullOrWhiteSpace(t.Id))
          failures.Add("Tweak has empty Id");
        if (!seenTweakIds.Add(t.Id))
          failures.Add($"Tweak duplicate Id: {t.Id}");
        if (t.Edits == null || t.Edits.Length == 0)
          failures.Add($"Tweak '{t.Id}' has no edits");
        else
          foreach (var edit in t.Edits) {
            if (string.IsNullOrWhiteSpace(edit.SubKey) || string.IsNullOrWhiteSpace(edit.ValueName))
              failures.Add($"Tweak '{t.Id}' edit missing SubKey/ValueName");
            if (edit.Kind != RegistryValueKind.DWord && edit.Kind != RegistryValueKind.String)
              failures.Add($"Tweak '{t.Id}' edit uses unsupported kind {edit.Kind}");
            if (edit.Kind == RegistryValueKind.DWord && !string.IsNullOrEmpty(edit.StringValue))
              failures.Add($"Tweak '{t.Id}' DWord edit has StringValue set");
            if (edit.Kind == RegistryValueKind.String && string.IsNullOrEmpty(edit.StringValue))
              failures.Add($"Tweak '{t.Id}' String edit missing StringValue");
            if (edit.Kind == RegistryValueKind.String && edit.HasDefault && string.IsNullOrEmpty(edit.DefaultString))
              failures.Add($"Tweak '{t.Id}' String edit missing DefaultString");
          }
      }

      if (failures.Count == 0)
        return "PASS: memory-priority mapping + cpu-percent math + sysopt parsing";
      return "FAIL:\n  " + string.Join("\n  ", failures);
    }
  }
}
