// CoreKeepPage.xaml.cs - 核心保持独立页面
// 从 PerfPage 迁移的 CoreKeep UI 逻辑，使用 CardExpander 折叠卡片
// 对齐 CpuAffinity 后端：拓扑可视化 + 进程枚举 + 右键快速操作 + 完整规则编辑
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OmenSuperHub.Services.CpuAffinity;
using OmenSuperHub.Utils;

namespace OmenSuperHub.Pages {
  public partial class CoreKeepPage : System.Windows.Controls.Page {
    CoreKeepEntry _currentSelectedEntry;
    List<ProcessItemView> _allProcesses = new List<ProcessItemView>();
    bool _sortByCpu = true;
    bool _loading; // ponytail: 页面初始化时屏蔽 IsChecked/Value 赋值触发的 Changed 事件,避免冗余 StartAutoApply/Save

    // ponytail: 强制级别常量 — 对齐 EnforcementService 4 级
    const string SoftCpuSets = "soft-cpu-sets";
    const string HardAffinity = "hard-affinity";
    const string JobEnforced = "job-enforced";
    const string JobLocked = "job-locked";

    public CoreKeepPage() {
      InitializeComponent();
      Loaded += CoreKeepPage_Loaded;
    }

    void CoreKeepPage_Loaded(object sender, RoutedEventArgs e) {
      _loading = true;
      try {
        InitCoreKeepUI();
        PopulateCoreVisualGrid();
      } finally { _loading = false; }
      UpdateLayout();
      Dispatcher.BeginInvoke(new Action(() => {
        RefreshProcessList();
      }), System.Windows.Threading.DispatcherPriority.Background);
    }

    void InitCoreKeepUI() {
      var data = CoreKeepService.Load();
      CoreKeepMasterToggle.IsChecked = data.MasterEnabled;
      CoreKeepList.ItemsSource = data.Entries;
      CoreKeepList.SelectionChanged -= CoreKeepList_SelectionChanged;
      CoreKeepList.SelectionChanged += CoreKeepList_SelectionChanged;
      UpdateTopologyText();
      UpdateEmptyHint(data.Entries.Count);
      CoreKeepGuardToggle.IsChecked = data.GuardIntervalMs > 0;
      CoreKeepGuardInterval.Value = Math.Max(1, Math.Min(10, Math.Max(1, data.GuardIntervalMs / 1000)));
      BuildPriorityCombo();
      BuildEnforcementCombo();
      BuildModeCombo();
      BuildBatchCombos();
      bool sub = data.MasterEnabled;
      CoreKeepList.IsEnabled = sub;
      CoreKeepAddBtn.IsEnabled = sub;
      CoreKeepRefreshBtn.IsEnabled = true;
      CoreKeepDeleteBtn.IsEnabled = sub;
      CoreKeepBenchBtn.IsEnabled = sub;
      CoreKeepGuardToggle.IsEnabled = sub;
      CoreKeepGuardInterval.IsEnabled = sub && CoreKeepGuardToggle.IsChecked == true;
      CoreKeepModeCombo.IsEnabled = false;
      CoreKeepPriorityCombo.IsEnabled = false;
      CoreKeepEnforcementCombo.IsEnabled = false;
      CoreKeepPathInput.IsEnabled = false;
      CoreKeepExcludeInput.IsEnabled = false;
      CoreKeepRuleEnabledToggle.IsEnabled = false;
      CoreKeepRuleNameInput.IsEnabled = false;
      CoreKeepProcessPatternInput.IsEnabled = false;
      CoreKeepCustomMaskInput.IsEnabled = false;
      CoreKeepLockToggle.IsEnabled = false;
      CoreKeepApplyNowBtn.IsEnabled = false;
      // ponytail: 若 CoreKeep 已在后台运行，只同步规则即可，不必重复 Stop→Start WMI watcher（避免进页面重连开销）
      if (sub) {
        if (CoreKeepService.IsRunning) CoreKeepService.SyncRules(data);
        else CoreKeepService.StartAutoApply(data);
      }
      InitReservedCpuSets(data);
      UpdateStats();
    }

    // ── 系统保留 CPU 核心集（ReservedCpuSets 注册表） ──

    void InitReservedCpuSets(CoreKeepData data) {
      bool supported = CoreKeepService.IsReservedCpuSetsSupported();
      ReservedUnsupportedHint.Visibility = supported ? Visibility.Collapsed : Visibility.Visible;
      ReservedMasterToggle.IsChecked = data.SystemReservedEnabled;
      ReservedApplyNowBtn.IsEnabled = supported;
      ReservedRefreshBtn.IsEnabled = supported;
      ReservedCoreList.IsEnabled = supported;
      PopulateReservedCoreCheckboxes(data.SystemReservedCores);
      RefreshReservedRegistryValue();
    }

    void PopulateReservedCoreCheckboxes(int[] selected) {
      var topo = CoreKeepService.GetTopologyInfo();
      var selSet = selected != null ? new HashSet<int>(selected) : new HashSet<int>();
      var items = new List<CoreCheckItem>();
      for (int i = 0; i < topo.TotalLogical; i++) {
        items.Add(new CoreCheckItem { CoreIndex = i, IsChecked = selSet.Contains(i) });
      }
      ReservedCoreList.ItemsSource = items;
    }

    int[] CollectReservedCores() {
      var selected = new List<int>();
      foreach (var item in ReservedCoreList.Items) {
        if (item is CoreCheckItem ci && ci.IsChecked) selected.Add(ci.CoreIndex);
      }
      return selected.ToArray();
    }

    void RefreshReservedRegistryValue() {
      ulong configured = CoreKeepService.ReadReservedCpuSetsRegistry();
      ulong effective = CoreKeepService.ReadEffectiveReservedMask();
      ReservedCurrentText.Text = Strings.CoreKeepReservedCurrent + (configured == 0 ? "0x0" : "0x" + configured.ToString("X"))
        + "   " + Strings.CoreKeepReservedEffective + (effective == 0 ? "0x0" : "0x" + effective.ToString("X"));
      // ponytail: 配置掩码 vs 生效掩码状态机
      if (configured == 0) {
        ReservedStatusText.Text = Strings.CoreKeepReservedStateNone;
      } else if (effective == configured) {
        ReservedStatusText.Text = Strings.CoreKeepReservedStateActive;
      } else {
        ReservedStatusText.Text = Strings.CoreKeepReservedStatePending;
      }
    }

    void ReservedMasterToggle_Changed(object sender, RoutedEventArgs e) {
      if (_loading) return;
      var data = CoreKeepService.Load();
      data.SystemReservedEnabled = ReservedMasterToggle.IsChecked == true;
      CoreKeepService.Save(data);
      ReservedApplyNowBtn.IsEnabled = data.SystemReservedEnabled && CoreKeepService.IsReservedCpuSetsSupported();
      ReservedCoreList.IsEnabled = data.SystemReservedEnabled && CoreKeepService.IsReservedCpuSetsSupported();
    }

    void ReservedCoreCheck_Click(object sender, RoutedEventArgs e) {
      if (_loading) return;
      var data = CoreKeepService.Load();
      data.SystemReservedCores = CollectReservedCores();
      CoreKeepService.Save(data);
    }

    void ReservedApplyNow_Click(object sender, RoutedEventArgs e) {
      if (!CoreKeepService.IsReservedCpuSetsSupported()) {
        ReservedStatusText.Text = Strings.CoreKeepReservedUnsupported;
        return;
      }
      var data = CoreKeepService.Load();
      data.SystemReservedCores = CollectReservedCores();
      CoreKeepService.Save(data);
      ulong mask = CoreKeepService.GetReservedMask();
      bool ok = CoreKeepService.WriteReservedCpuSetsRegistry(mask);
      if (ok) {
        // 写入成功：刷新三态（未设置/待重启生效/已生效）
        RefreshReservedRegistryValue();
      } else {
        ReservedStatusText.Text = Strings.CoreKeepReservedWriteFailed;
      }
    }

    void ReservedRefresh_Click(object sender, RoutedEventArgs e) {
      RefreshReservedRegistryValue();
    }

    // ── 拓扑可视化 ──

    void PopulateCoreVisualGrid() {
      var visuals = CoreKeepService.GetCoreVisuals();
      var items = new List<CoreVisualView>();
      foreach (var v in visuals) {
        items.Add(new CoreVisualView {
          Index = v.Index,
          Tooltip = v.Tooltip,
          ColorBrush = CoreTypeToBrush(v.CoreType)
        });
      }
      CoreVisualGrid.ItemsSource = items;
    }

    // ponytail: 画刷静态冻结 — 进程列表刷新/过滤时每行 new ×2 未冻结画刷造成 GC 洪峰;
    // 冻结后跨线程可用且系统可缓存渲染。颜色与图例一一对应,运行期不变。
    static readonly SolidColorBrush BrushP = Frozen(0xE7, 0x48, 0x56);   // 亮红
    static readonly SolidColorBrush BrushE = Frozen(0x00, 0x78, 0xD4);   // 蓝
    static readonly SolidColorBrush BrushSmt1 = Frozen(0x8B, 0x95, 0xA1); // 浅灰
    static readonly SolidColorBrush BrushDefault = Frozen(0x5B, 0x6B, 0x73); // 深灰 (SMT0)

    static SolidColorBrush Frozen(byte r, byte g, byte b) {
      var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
      brush.Freeze();
      return brush;
    }
    static SolidColorBrush FrozenAlpha(byte a, byte r, byte g, byte b) {
      var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
      brush.Freeze();
      return brush;
    }

    static SolidColorBrush CoreTypeToBrush(string type) {
      switch (type) {
        case "P":    return BrushP;
        case "E":    return BrushE;
        case "SMT1": return BrushSmt1;
        default:     return BrushDefault;
      }
    }

    void UpdateTopologyText() {
      var topo = CoreKeepService.GetTopologyInfo();
      string baseText;
      if (topo.IsHybrid)
        baseText = string.Format(Strings.CoreKeepTopologyHybrid, topo.TotalLogical, topo.PerformanceCores.Length, topo.EfficientCores.Length);
      else if (topo.IsDualCcd)
        baseText = string.Format(Strings.CoreKeepTopologyDualCcd, topo.TotalLogical, topo.Ccd0Count, topo.Ccd1Count);
      else
        baseText = string.Format(Strings.CoreKeepTopologyNormal, topo.TotalLogical);
      CoreKeepTopologyText.Text = baseText + (topo.HasSmt ? "  | SMT: ON" : "  | SMT: OFF");
    }

    void UpdateEmptyHint(int count) {
      CoreKeepEmptyHint.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ponytail: 统计概览卡片 — 进程数/规则数/P核/E核
    void UpdateStats() {
      var topo = CoreKeepService.GetTopologyInfo();
      StatPcoreCount.Text = topo.IsHybrid ? topo.PerformanceCores.Length.ToString() : topo.PhysicalCoreCount.ToString();
      StatEcoreCount.Text = topo.IsHybrid ? topo.EfficientCores.Length.ToString() : "0";
      var data = CoreKeepService.Load();
      StatRulesCount.Text = data.Entries.Count(e => e.Enabled).ToString();
      StatProcessCount.Text = _allProcesses.Count.ToString();
    }

    // ── 进程列表 ──

    void RefreshProcessList() {
      // ponytail: 后台线程枚举进程 + CPU 采样，避免阻塞 UI
      System.Threading.ThreadPool.QueueUserWorkItem(_ => {
        var procs = CoreKeepService.EnumerateProcessesWithCpu();
        Dispatcher.BeginInvoke(new Action(() => {
          _allProcesses = procs.Select(p => {
            var v = new ProcessItemView {
              Pid = p.Pid,
              Name = p.Name,
              Path = p.Path,
              PathText = p.Path,
              PidText = p.Pid.ToString(),
              AffinityText = p.AffinityHex,
              MatchedRule = p.MatchedRule,
              RuleLevel = p.RuleLevel,
              CpuPct = p.CpuUsagePercent,
              CpuText = p.CpuUsagePercent >= 0 ? p.CpuUsagePercent.ToString("F1") + "%" : "–",
              CanBatchManage = p.CanBatchManage
            };
            ApplyLevelColors(v);
            return v;
          }).ToList();
          if (_sortByCpu)
            _allProcesses = _allProcesses.OrderByDescending(p => p.CpuPct).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
          ApplyProcessFilter();
          UpdateStats();
        }), System.Windows.Threading.DispatcherPriority.Background);
      });
    }

    void ProcessSortByCpuToggle_Changed(object sender, RoutedEventArgs e) {
      _sortByCpu = ProcessSortByCpuToggle.IsChecked == true;
      if (_allProcesses.Count > 0) {
        if (_sortByCpu)
          _allProcesses = _allProcesses.OrderByDescending(p => p.CpuPct).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
        else
          _allProcesses = _allProcesses.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Pid).ToList();
        ApplyProcessFilter();
      }
    }

    void ApplyProcessFilter() {
      string query = (ProcessSearchBox?.Text ?? "").Trim();
      var filtered = string.IsNullOrEmpty(query)
        ? _allProcesses
        : _allProcesses.Where(p => ProcessSearchMatch(query, p.Name, p.Path, p.Pid)).ToList();
      ProcessListBox.ItemsSource = null;
      ProcessListBox.ItemsSource = filtered;
      ProcessListBox.UpdateLayout();
      UpdateBatchImpact();
    }

    void UpdateBatchImpact() {
      if (BatchImpactText == null) return;
      int count = GetCheckedTargets().Count;
      BatchImpactText.Text = count > 0 ? string.Format(Strings.CoreKeepBatchImpact, count) : "";
    }

    void ProcessCheck_Click(object sender, RoutedEventArgs e) {
      UpdateBatchImpact();
    }

    static bool ProcessSearchMatch(string query, string name, string path, int pid) {
      if (name != null && name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) return true;
      if (path != null && path.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) return true;
      return pid.ToString() == query;
    }

    // ponytail: 按强制级别设置进程列表标签颜色 — soft 蓝 / hard 黄 / enforced 橙 / locked 红
    static readonly SolidColorBrush BadgeSoft = FrozenAlpha(0x33, 0x00, 0x78, 0xD4);
    static readonly SolidColorBrush BadgeSoftFg = Frozen(0x00, 0x78, 0xD4);
    static readonly SolidColorBrush BadgeHard = FrozenAlpha(0x33, 0xFF, 0xB9, 0x00);
    static readonly SolidColorBrush BadgeHardFg = Frozen(0xFF, 0xB9, 0x00);
    static readonly SolidColorBrush BadgeEnforced = FrozenAlpha(0x33, 0xF7, 0x63, 0x0C);
    static readonly SolidColorBrush BadgeEnforcedFg = Frozen(0xF7, 0x63, 0x0C);
    static readonly SolidColorBrush BadgeLocked = FrozenAlpha(0x33, 0xE7, 0x48, 0x56);
    static readonly SolidColorBrush BadgeLockedFg = Frozen(0xE7, 0x48, 0x56);

    static void ApplyLevelColors(ProcessItemView v) {
      switch (v.RuleLevel) {
        case "soft-cpu-sets":
          v.RuleBadgeBrush = BadgeSoft;
          v.RuleBadgeForeground = BadgeSoftFg;
          break;
        case "hard-affinity":
          v.RuleBadgeBrush = BadgeHard;
          v.RuleBadgeForeground = BadgeHardFg;
          break;
        case "job-enforced":
          v.RuleBadgeBrush = BadgeEnforced;
          v.RuleBadgeForeground = BadgeEnforcedFg;
          break;
        case "job-locked":
          v.RuleBadgeBrush = BadgeLocked;
          v.RuleBadgeForeground = BadgeLockedFg;
          break;
      }
    }

    void ProcessSearch_TextChanged(object sender, TextChangedEventArgs e) {
      ApplyProcessFilter();
    }

    void ProcessRefresh_Click(object sender, RoutedEventArgs e) {
      RefreshProcessList();
    }

    ProcessItemView GetSelectedProcess() {
      return ProcessListBox.SelectedItem as ProcessItemView;
    }

    // ── 右键快速操作（参考 CpuAffinityManager ProcessListViewModel.ApplyQuick） ──

    void QuickSetPCores_Click(object sender, RoutedEventArgs e)       => ApplyQuickToSelected("p-cores|first-half", HardAffinity);
    void QuickSetECores_Click(object sender, RoutedEventArgs e)       => ApplyQuickToSelected("e-cores|second-half", HardAffinity);
    void QuickSetAll_Click(object sender, RoutedEventArgs e)          => ApplyQuickToSelected("all-cores", HardAffinity);
    void QuickSetFirstHalf_Click(object sender, RoutedEventArgs e)   => ApplyQuickToSelected("first-half", HardAffinity);
    void QuickSetSecondHalf_Click(object sender, RoutedEventArgs e)  => ApplyQuickToSelected("second-half", HardAffinity);
    void QuickJobEnforced_Click(object sender, RoutedEventArgs e)     => ApplyQuickToSelected("p-cores|all-cores", JobEnforced);
    void QuickJobLocked_Click(object sender, RoutedEventArgs e)       => ApplyQuickToSelected("all-cores", JobLocked);

    void QuickRelax_Click(object sender, RoutedEventArgs e) {
      var item = GetSelectedProcess();
      if (item == null) return;
      try { CoreKeepService.RelaxPid(item.Pid); } catch { }
      RefreshProcessList();
    }

    void QuickApplyRule_Click(object sender, RoutedEventArgs e) {
      var item = GetSelectedProcess();
      if (item == null) return;
      try {
        // ponytail: 通过名称匹配 CoreKeep.json 中已存在的规则并应用
        var data = CoreKeepService.Load();
        var entry = data.Entries.Find(x =>
          x.ProcessName != null &&
          x.ProcessName.Equals(item.Name, StringComparison.OrdinalIgnoreCase));
        if (entry != null && entry.Enabled) {
          CoreKeepService.ApplyToProcess(item.Name, entry);
        }
      } catch { }
      RefreshProcessList();
    }

    void ApplyQuickToSelected(string mode, string level) {
      var item = GetSelectedProcess();
      if (item == null) return;
      try {
        CoreKeepService.QuickApply(item.Pid, mode, level);
      } catch { }
      RefreshProcessList();
    }

    // ── 模式 ComboBox：按拓扑动态填充 ──

    void BuildModeCombo() {
      CoreKeepModeCombo.Items.Clear();
      var topo = CoreKeepService.GetTopologyInfo();
      AddMode(CoreKeepModeCombo, "all-cores", Strings.CoreKeepModeAll);
      if (topo.IsHybrid) {
        AddMode(CoreKeepModeCombo, "p-cores", Strings.CoreKeepModePCores);
        AddMode(CoreKeepModeCombo, "e-cores", Strings.CoreKeepModeECores);
        if (topo.HasSmt) {
          AddMode(CoreKeepModeCombo, "p-cores-smt", Strings.CoreKeepModePCoresSmt);
          AddMode(CoreKeepModeCombo, "p-cores-no-smt", Strings.CoreKeepModePCoresNoSmt);
        }
        AddMode(CoreKeepModeCombo, "p-cores-first", Strings.CoreKeepModePerformanceFirst);
      }
      if (topo.HasSmt)
        AddMode(CoreKeepModeCombo, "no-smt", Strings.CoreKeepModeNoSmt);
      AddMode(CoreKeepModeCombo, "first-half", Strings.CoreKeepModeFirstHalf);
      AddMode(CoreKeepModeCombo, "second-half", Strings.CoreKeepModeSecondHalf);
      if (topo.IsDualCcd) {
        AddMode(CoreKeepModeCombo, "ccd0", Strings.CoreKeepModeCcd0);
        AddMode(CoreKeepModeCombo, "ccd1", Strings.CoreKeepModeCcd1);
      }
      AddMode(CoreKeepModeCombo, "Manual", Strings.CoreKeepModeManual);
      AddMode(CoreKeepModeCombo, "custom", Strings.CoreKeepModeManual); // ponytail: custom 复用"手动选择"标签
    }

    static void AddMode(ComboBox combo, string tag, string label) {
      combo.Items.Add(new ComboBoxItem { Content = label, Tag = tag });
    }

    static string NormalizeMode(string mode) {
      if (string.IsNullOrEmpty(mode)) return "all-cores";
      switch (mode) {
        case "All": return "all-cores";
        case "Performance":
        case "Auto": return "p-cores";
        case "Manual":
        case "custom": return "Manual";
        case "PerformanceFirst": return "p-cores-first";
        case "NoSmt": return "no-smt";
        default: return mode;
      }
    }

    void SelectModeInCombo(string mode) {
      string normalized = NormalizeMode(mode);
      foreach (ComboBoxItem item in CoreKeepModeCombo.Items) {
        if (item.Tag is string tag && tag == normalized) { CoreKeepModeCombo.SelectedItem = item; return; }
      }
      if (CoreKeepModeCombo.Items.Count > 0)
        CoreKeepModeCombo.SelectedIndex = 0;
    }

    // ── 优先级 ComboBox ──

    void BuildPriorityCombo() {
      CoreKeepPriorityCombo.Items.Clear();
      CoreKeepPriorityCombo.Items.Add(new ComboBoxItem { Content = Strings.CoreKeepPriorityIdle, Tag = (uint)0x00000040 });
      CoreKeepPriorityCombo.Items.Add(new ComboBoxItem { Content = Strings.CoreKeepPriorityBelowNormal, Tag = (uint)0x00004000 });
      CoreKeepPriorityCombo.Items.Add(new ComboBoxItem { Content = Strings.CoreKeepPriorityNormal, Tag = (uint)0x00000020 });
      CoreKeepPriorityCombo.Items.Add(new ComboBoxItem { Content = Strings.CoreKeepPriorityAboveNormal, Tag = (uint)0x00008000 });
      CoreKeepPriorityCombo.Items.Add(new ComboBoxItem { Content = Strings.CoreKeepPriorityHigh, Tag = (uint)0x00000080 });
      CoreKeepPriorityCombo.Items.Add(new ComboBoxItem { Content = Strings.CoreKeepPriorityRealtime, Tag = (uint)0x00000100 });
    }

    void SelectPriorityInCombo(uint priorityClass) {
      foreach (ComboBoxItem item in CoreKeepPriorityCombo.Items) {
        if (item.Tag is uint tag && tag == priorityClass) { CoreKeepPriorityCombo.SelectedItem = item; return; }
      }
      CoreKeepPriorityCombo.SelectedIndex = -1;
    }

    // ── 强制级别 ComboBox：4 级 ──

    void BuildEnforcementCombo() {
      CoreKeepEnforcementCombo.Items.Clear();
      CoreKeepEnforcementCombo.Items.Add(new ComboBoxItem { Content = Strings.CoreKeepEnforcementSoft, Tag = SoftCpuSets });
      CoreKeepEnforcementCombo.Items.Add(new ComboBoxItem { Content = Strings.CoreKeepEnforcementHard, Tag = HardAffinity });
      CoreKeepEnforcementCombo.Items.Add(new ComboBoxItem { Content = Strings.CoreKeepEnforcementJob, Tag = JobEnforced });
      CoreKeepEnforcementCombo.Items.Add(new ComboBoxItem { Content = Strings.CoreKeepEnforcementLocked, Tag = JobLocked });
    }

    void SelectEnforcementInCombo(string level) {
      string lvl = string.IsNullOrEmpty(level) ? HardAffinity : level;
      foreach (ComboBoxItem item in CoreKeepEnforcementCombo.Items) {
        if (item.Tag is string tag && tag == lvl) { CoreKeepEnforcementCombo.SelectedItem = item; return; }
      }
      CoreKeepEnforcementCombo.SelectedIndex = 1;
    }

    // ── IFEO IO 优先级 ComboBox：XAML 静态项，Tag=int ──

    void SelectIoPriorityInCombo(int prio) {
      foreach (ComboBoxItem item in CoreKeepIoPriorityCombo.Items) {
        if (int.TryParse(item.Tag?.ToString(), out int tag) && tag == prio) {
          CoreKeepIoPriorityCombo.SelectedItem = item; return;
        }
      }
      CoreKeepIoPriorityCombo.SelectedIndex = 0; // 默认"不设置"
    }

    // ── 内存优先级 ComboBox：XAML 静态项，Tag=int（-1=不设置 1..5）──

    void SelectMemoryPriorityInCombo(int prio) {
      foreach (ComboBoxItem item in CoreKeepMemoryPriorityCombo.Items) {
        if (int.TryParse(item.Tag?.ToString(), out int tag) && tag == prio) {
          CoreKeepMemoryPriorityCombo.SelectedItem = item; return;
        }
      }
      CoreKeepMemoryPriorityCombo.SelectedIndex = 0; // 默认"不设置"
    }

    // ── 列表选中 ──

    void CoreKeepList_SelectionChanged(object sender, SelectionChangedEventArgs e) {
      _currentSelectedEntry = CoreKeepList.SelectedItem as CoreKeepEntry;
      if (_currentSelectedEntry == null) {
        CoreKeepProcInput.Text = "";
        CoreKeepPriorityText.Text = " -";
        CoreKeepAffinityText.Text = " -";
        CoreKeepStatusIcon.Text = "";
        CoreKeepLivePriorityText.Text = "";
        SetRuleConfigEnabled(false);
        CoreKeepCoreListPanel.Visibility = Visibility.Collapsed;
        CoreKeepCustomMaskPanel.Visibility = Visibility.Collapsed;
        return;
      }
      CoreKeepProcInput.Text = _currentSelectedEntry.ProcessName;
      CoreKeepPriorityText.Text = CoreKeepService.PriorityClassName(_currentSelectedEntry.PriorityClass);
      CoreKeepAffinityText.Text = "0x" + _currentSelectedEntry.AffinityMask.ToString("X");
      var state = CoreKeepService.QueryProcessState(_currentSelectedEntry.ProcessName, _currentSelectedEntry.ProcessId);
      if (!state.Running) {
        CoreKeepStatusIcon.Text = Strings.CoreKeepStatusNotRunning;
        CoreKeepLivePriorityText.Text = "";
      } else if (state.PriorityClass == _currentSelectedEntry.PriorityClass && state.AffinityMask == _currentSelectedEntry.AffinityMask) {
        CoreKeepStatusIcon.Text = Strings.CoreKeepStatusMatched;
        CoreKeepLivePriorityText.Text = "";
      } else {
        CoreKeepStatusIcon.Text = Strings.CoreKeepStatusMismatch;
        CoreKeepLivePriorityText.Text = $"({CoreKeepService.PriorityClassName(state.PriorityClass)}/0x{state.AffinityMask:X})";
      }
      bool on = CoreKeepMasterToggle.IsChecked == true;
      SetRuleConfigEnabled(on);
      SelectModeInCombo(_currentSelectedEntry.CoreMode);
      SelectPriorityInCombo(_currentSelectedEntry.PriorityClass);
      SelectEnforcementInCombo(_currentSelectedEntry.EnforcementLevel);
      SelectIoPriorityInCombo(_currentSelectedEntry.IoPriority);
      SelectMemoryPriorityInCombo(_currentSelectedEntry.MemoryPriority);
      CoreKeepMainThreadToggle.IsChecked = _currentSelectedEntry.MainThreadBind;
      CoreKeepRuleNameInput.Text = _currentSelectedEntry.ProcessName ?? "";
      CoreKeepRuleEnabledToggle.IsChecked = _currentSelectedEntry.Enabled;
      CoreKeepProcessPatternInput.Text = _currentSelectedEntry.ProcessName ?? "";
      CoreKeepPathInput.Text = _currentSelectedEntry.PathFilter ?? "";
      CoreKeepExcludeInput.Text = _currentSelectedEntry.ExcludePatterns != null
        ? string.Join(", ", _currentSelectedEntry.ExcludePatterns) : "";
      CoreKeepLockToggle.IsChecked = _currentSelectedEntry.EnforcementLevel == JobLocked;
      string nm = NormalizeMode(_currentSelectedEntry.CoreMode);
      bool isManual = nm == "Manual";
      bool isCustom = _currentSelectedEntry.CoreMode == "custom";
      CoreKeepCoreListPanel.Visibility = isManual ? Visibility.Visible : Visibility.Collapsed;
      CoreKeepCustomMaskPanel.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;
      if (isCustom)
        CoreKeepCustomMaskInput.Text = "0x" + _currentSelectedEntry.AffinityMask.ToString("X");
      if (isManual)
        PopulateCoreCheckboxes(_currentSelectedEntry.PreferredCores, _currentSelectedEntry.AffinityMask);
    }

    void SetRuleConfigEnabled(bool on) {
      CoreKeepModeCombo.IsEnabled = on;
      CoreKeepPriorityCombo.IsEnabled = on;
      CoreKeepEnforcementCombo.IsEnabled = on;
      CoreKeepIoPriorityCombo.IsEnabled = on;
      CoreKeepMemoryPriorityCombo.IsEnabled = on;
      CoreKeepMainThreadToggle.IsEnabled = on;
      CoreKeepPathInput.IsEnabled = on;
      CoreKeepExcludeInput.IsEnabled = on;
      CoreKeepRuleEnabledToggle.IsEnabled = on;
      CoreKeepRuleNameInput.IsEnabled = on;
      CoreKeepProcessPatternInput.IsEnabled = on;
      CoreKeepCustomMaskInput.IsEnabled = on;
      CoreKeepLockToggle.IsEnabled = on;
      CoreKeepApplyNowBtn.IsEnabled = on;
    }

    void PopulateCoreCheckboxes(int[] selected, long affinityMask = 0) {
      var topo = CoreKeepService.GetTopologyInfo();
      var items = new List<CoreCheckItem>();
      HashSet<int> selSet;
      if (selected != null && selected.Length > 0)
        selSet = new HashSet<int>(selected);
      else {
        selSet = new HashSet<int>();
        for (int i = 0; i < topo.TotalLogical; i++) {
          if ((affinityMask & (1L << i)) != 0) selSet.Add(i);
        }
      }
      for (int i = 0; i < topo.TotalLogical; i++) {
        items.Add(new CoreCheckItem { CoreIndex = i, IsChecked = selSet.Contains(i) });
      }
      CoreKeepCoreList.ItemsSource = items;
    }

    // ── 主开关 ──

    void CoreKeepMasterToggle_Changed(object sender, RoutedEventArgs e) {
      if (_loading || CoreKeepMasterToggle == null) return;
      bool on = CoreKeepMasterToggle.IsChecked == true;
      CoreKeepList.IsEnabled = on;
      CoreKeepAddBtn.IsEnabled = on;
      CoreKeepDeleteBtn.IsEnabled = on;
      CoreKeepBenchBtn.IsEnabled = on;
      CoreKeepGuardToggle.IsEnabled = on;
      CoreKeepGuardInterval.IsEnabled = on && CoreKeepGuardToggle.IsChecked == true;
      if (_currentSelectedEntry != null) SetRuleConfigEnabled(on);
      var data = CoreKeepService.Load();
      data.MasterEnabled = on;
      CoreKeepService.Save(data);
      if (on) CoreKeepService.StartAutoApply(data); else CoreKeepService.StopAutoApply();
    }

    // ── 进程操作（规则列表） ──

    void CoreKeepRefresh_Click(object sender, RoutedEventArgs e) {
      string raw = CoreKeepProcInput.Text?.Trim();
      if (string.IsNullOrEmpty(raw)) {
        RefreshCoreKeepList(CoreKeepService.Load());
        return;
      }
      ProcessAffinityState state;
      bool isPid = int.TryParse(raw, out int pid);
      if (isPid)
        state = CoreKeepService.QueryProcessState("", pid);
      else {
        string procName = raw.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? raw : raw + ".exe";
        state = CoreKeepService.QueryProcessState(procName);
      }
      if (!state.Running) {
        CoreKeepPriorityText.Text = " -";
        CoreKeepAffinityText.Text = " -";
        CoreKeepStatusIcon.Text = Strings.CoreKeepStatusNotRunning;
        CoreKeepLivePriorityText.Text = "";
        return;
      }
      CoreKeepPriorityText.Text = CoreKeepService.PriorityClassName(state.PriorityClass);
      CoreKeepAffinityText.Text = "0x" + state.AffinityMask.ToString("X");
      CoreKeepLivePriorityText.Text = "";
      var data = CoreKeepService.Load();
      var existing = isPid
        ? data.Entries.Find(x => x.ProcessId == pid)
        : data.Entries.Find(x => x.ProcessName.Equals(raw + (raw.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? "" : ".exe"), StringComparison.OrdinalIgnoreCase));
      if (existing != null) {
        if (state.PriorityClass == existing.PriorityClass && state.AffinityMask == existing.AffinityMask)
          CoreKeepStatusIcon.Text = Strings.CoreKeepStatusMatched;
        else
          CoreKeepStatusIcon.Text = Strings.CoreKeepStatusMismatch;
      } else {
        CoreKeepStatusIcon.Text = "";
      }
    }

    void CoreKeepDelete_Click(object sender, RoutedEventArgs e) {
      var selected = CoreKeepList.SelectedItem as CoreKeepEntry;
      if (selected == null) return;
      var data = CoreKeepService.Load();
      data.Entries.RemoveAll(x => x.ProcessName == selected.ProcessName);
      CoreKeepService.Save(data);
      _currentSelectedEntry = null;
      CoreKeepProcInput.Text = "";
      CoreKeepPriorityText.Text = " -";
      CoreKeepAffinityText.Text = " -";
      CoreKeepStatusIcon.Text = "";
      CoreKeepLivePriorityText.Text = "";
      SetRuleConfigEnabled(false);
      CoreKeepCoreListPanel.Visibility = Visibility.Collapsed;
      CoreKeepCustomMaskPanel.Visibility = Visibility.Collapsed;
      RefreshCoreKeepList(data);
    }

    void CoreKeepAdd_Click(object sender, RoutedEventArgs e) {
      string raw = CoreKeepProcInput.Text?.Trim();
      if (string.IsNullOrEmpty(raw)) return;
      var data = CoreKeepService.Load();
      CoreKeepEntry entry;
      bool isPid = int.TryParse(raw, out int pid);
      if (isPid) {
        entry = CoreKeepService.CaptureFromPid(pid);
        if (entry.AffinityMask == 0 && entry.PriorityClass == 0) return;
        if (data.Entries.Exists(x => x.ProcessId == pid)) return;
      } else {
        string procName = raw.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? raw : raw + ".exe";
        if (data.Entries.Exists(x => x.ProcessName.Equals(procName, StringComparison.OrdinalIgnoreCase))) return;
        entry = CoreKeepService.CaptureFromProcess(procName);
        if (entry.AffinityMask == 0 && entry.PriorityClass == 0) return;
      }
      entry.Enabled = true;
      entry.CoreMode = "all-cores";
      entry.EnforcementLevel = HardAffinity;
      entry.GuardEnabled = true;
      CoreKeepService.ApplyModeToEntry(entry, "all-cores");
      if (CoreKeepPriorityCombo.SelectedItem is ComboBoxItem pi && pi.Tag is uint prio)
        entry.PriorityClass = prio;
      data.Entries.Add(entry);
      CoreKeepService.Save(data);
      CoreKeepProcInput.Text = "";
      RefreshCoreKeepList(data);
      if (data.MasterEnabled) CoreKeepService.StartAutoApply(data);
    }

    // ── 模式/优先级/强制级别 ComboBox 变更 ──

    void CoreKeepModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) {
      if (_currentSelectedEntry == null || CoreKeepModeCombo.SelectedItem == null) return;
      if (CoreKeepModeCombo.SelectedItem is ComboBoxItem item && item.Tag is string mode) {
        if (NormalizeMode(_currentSelectedEntry.CoreMode) == mode) return;
        _currentSelectedEntry.CoreMode = mode;
        bool isManual = mode == "Manual";
        bool isCustom = mode == "custom";
        CoreKeepCoreListPanel.Visibility = isManual ? Visibility.Visible : Visibility.Collapsed;
        CoreKeepCustomMaskPanel.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;
        if (isManual) {
          PopulateCoreCheckboxes(_currentSelectedEntry.PreferredCores, _currentSelectedEntry.AffinityMask);
        } else if (!isCustom) {
          long mask = CoreKeepService.ModeToAffinityMask(mode, null);
          _currentSelectedEntry.AffinityMask = mask;
          CoreKeepAffinityText.Text = "0x" + mask.ToString("X");
        } else {
          // custom: 从输入框解析
          TryParseCustomMask();
        }
        PersistEntryChange();
        RefreshCoreKeepList(CoreKeepService.Load());
        if (CoreKeepMasterToggle.IsChecked == true && !isManual && !isCustom)
          CoreKeepService.ApplyToProcess(_currentSelectedEntry.ProcessName, _currentSelectedEntry);
      }
    }

    void CoreKeepPriorityCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) {
      if (_currentSelectedEntry == null || CoreKeepPriorityCombo.SelectedItem == null) return;
      if (CoreKeepPriorityCombo.SelectedItem is ComboBoxItem item && item.Tag is uint prio) {
        if (_currentSelectedEntry.PriorityClass == prio) return;
        _currentSelectedEntry.PriorityClass = prio;
        CoreKeepPriorityText.Text = CoreKeepService.PriorityClassName(prio);
        PersistEntryChange();
        if (CoreKeepMasterToggle.IsChecked == true)
          CoreKeepService.ApplyToProcess(_currentSelectedEntry.ProcessName, _currentSelectedEntry);
      }
    }

    void CoreKeepEnforcementCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) {
      if (_currentSelectedEntry == null || CoreKeepEnforcementCombo.SelectedItem == null) return;
      if (CoreKeepEnforcementCombo.SelectedItem is ComboBoxItem item && item.Tag is string lvl) {
        if (_currentSelectedEntry.EnforcementLevel == lvl) return;
        _currentSelectedEntry.EnforcementLevel = lvl;
        // 锁定子进程开关联动
        CoreKeepLockToggle.IsChecked = (lvl == JobLocked);
        PersistEntryChange();
        if (CoreKeepMasterToggle.IsChecked == true)
          CoreKeepService.ApplyToProcess(_currentSelectedEntry.ProcessName, _currentSelectedEntry);
      }
    }

    void CoreKeepIoPriorityCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) {
      if (_currentSelectedEntry == null || CoreKeepIoPriorityCombo.SelectedItem == null) return;
      // ponytail: IFEO IO 优先级 — Tag 是 int（-1=不设置 0=VeryLow 1=Low 3=High）
      if (CoreKeepIoPriorityCombo.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag?.ToString(), out int prio)) {
        if (_currentSelectedEntry.IoPriority == prio) return;
        _currentSelectedEntry.IoPriority = prio;
        PersistEntryChange();
        // IFEO 写注册表在 SyncRuleEngine 时统一完成，这里直接触发同步
        if (CoreKeepMasterToggle.IsChecked == true)
          CoreKeepService.SyncRules(CoreKeepService.Load());
      }
    }

    void CoreKeepMemoryPriorityCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) {
      if (_currentSelectedEntry == null || CoreKeepMemoryPriorityCombo.SelectedItem == null) return;
      // ponytail: 内存优先级 — Tag 是 int（-1=不设置 1=VeryLow 2=Low 3=Medium 4=BelowNormal 5=Normal）
      if (CoreKeepMemoryPriorityCombo.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag?.ToString(), out int prio)) {
        if (_currentSelectedEntry.MemoryPriority == prio) return;
        _currentSelectedEntry.MemoryPriority = prio;
        PersistEntryChange();
        if (CoreKeepMasterToggle.IsChecked == true)
          CoreKeepService.ApplyToProcess(_currentSelectedEntry.ProcessName, _currentSelectedEntry);
      }
    }

    void CoreKeepMainThreadToggle_Changed(object sender, RoutedEventArgs e) {
      if (_currentSelectedEntry == null) return;
      bool on = CoreKeepMainThreadToggle.IsChecked == true;
      if (_currentSelectedEntry.MainThreadBind == on) return;
      _currentSelectedEntry.MainThreadBind = on;
      PersistEntryChange();
      if (CoreKeepMasterToggle.IsChecked == true)
        CoreKeepService.ApplyToProcess(_currentSelectedEntry.ProcessName, _currentSelectedEntry);
    }

    // ── 规则配置面板：名称 / 启用 / 进程名 / 路径 / 排除 / 自定义掩码 / 锁定 / 立即应用 ──

    void CoreKeepRuleNameInput_LostFocus(object sender, RoutedEventArgs e) {
      if (_currentSelectedEntry == null) return;
      string name = CoreKeepRuleNameInput.Text?.Trim();
      if (string.IsNullOrEmpty(name) || _currentSelectedEntry.ProcessName == name) return;
      // ponytail: 规则名称修改同步到 ProcessName（CoreKeep 模型以 ProcessName 为标识）
      _currentSelectedEntry.ProcessName = name;
      PersistEntryChange();
      RefreshCoreKeepList(CoreKeepService.Load());
    }

    void CoreKeepRuleEnabled_Changed(object sender, RoutedEventArgs e) {
      if (_currentSelectedEntry == null) return;
      bool on = CoreKeepRuleEnabledToggle.IsChecked == true;
      if (_currentSelectedEntry.Enabled == on) return;
      _currentSelectedEntry.Enabled = on;
      PersistEntryChange();
      RefreshCoreKeepList(CoreKeepService.Load());
      if (CoreKeepMasterToggle.IsChecked == true && on)
        CoreKeepService.ApplyToProcess(_currentSelectedEntry.ProcessName, _currentSelectedEntry);
    }

    void CoreKeepProcessPatternInput_LostFocus(object sender, RoutedEventArgs e) {
      if (_currentSelectedEntry == null) return;
      string pattern = CoreKeepProcessPatternInput.Text?.Trim();
      if (_currentSelectedEntry.ProcessName == pattern) return;
      _currentSelectedEntry.ProcessName = string.IsNullOrEmpty(pattern) ? "" : pattern;
      PersistEntryChange();
      RefreshCoreKeepList(CoreKeepService.Load());
    }

    void CoreKeepPathInput_LostFocus(object sender, RoutedEventArgs e) {
      if (_currentSelectedEntry == null) return;
      string path = CoreKeepPathInput.Text?.Trim();
      if (_currentSelectedEntry.PathFilter == path) return;
      _currentSelectedEntry.PathFilter = string.IsNullOrEmpty(path) ? null : path;
      PersistEntryChange();
    }

    void CoreKeepExcludeInput_LostFocus(object sender, RoutedEventArgs e) {
      if (_currentSelectedEntry == null) return;
      string raw = CoreKeepExcludeInput.Text?.Trim();
      var patterns = string.IsNullOrEmpty(raw)
        ? null
        : raw.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
               .Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
      _currentSelectedEntry.ExcludePatterns = patterns;
      PersistEntryChange();
    }

    void CoreKeepCustomMaskInput_LostFocus(object sender, RoutedEventArgs e) {
      if (_currentSelectedEntry == null) return;
      if (TryParseCustomMask()) {
        PersistEntryChange();
        RefreshCoreKeepList(CoreKeepService.Load());
        CoreKeepAffinityText.Text = "0x" + _currentSelectedEntry.AffinityMask.ToString("X");
        if (CoreKeepMasterToggle.IsChecked == true)
          CoreKeepService.ApplyToProcess(_currentSelectedEntry.ProcessName, _currentSelectedEntry);
      }
    }

    bool TryParseCustomMask() {
      string raw = CoreKeepCustomMaskInput.Text?.Trim();
      if (string.IsNullOrEmpty(raw)) return false;
      // ponytail: 支持 0xFF、FF、0XFF 多种格式
      string hex = raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? raw.Substring(2) : raw;
      if (long.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out long mask)) {
        _currentSelectedEntry.AffinityMask = mask;
        return true;
      }
      return false;
    }

    void CoreKeepLockToggle_Changed(object sender, RoutedEventArgs e) {
      if (_currentSelectedEntry == null) return;
      bool locked = CoreKeepLockToggle.IsChecked == true;
      string newLevel = locked ? JobLocked : JobEnforced;
      if (_currentSelectedEntry.EnforcementLevel == newLevel) return;
      _currentSelectedEntry.EnforcementLevel = newLevel;
      SelectEnforcementInCombo(newLevel);
      PersistEntryChange();
      if (CoreKeepMasterToggle.IsChecked == true)
        CoreKeepService.ApplyToProcess(_currentSelectedEntry.ProcessName, _currentSelectedEntry);
    }

    void CoreKeepApplyNow_Click(object sender, RoutedEventArgs e) {
      if (_currentSelectedEntry == null) return;
      CoreKeepService.ApplyToProcess(_currentSelectedEntry.ProcessName, _currentSelectedEntry);
      // 刷新进程列表以反映新亲和性
      RefreshProcessList();
    }

    // ── 持久化辅助 ──

    void PersistEntryChange() {
      var data = CoreKeepService.Load();
      var existing = _currentSelectedEntry.ProcessId > 0
        ? data.Entries.Find(x => x.ProcessId == _currentSelectedEntry.ProcessId)
        : data.Entries.Find(x => x.ProcessName == _currentSelectedEntry.ProcessName);
      if (existing != null) {
        existing.CoreMode = _currentSelectedEntry.CoreMode;
        existing.AffinityMask = _currentSelectedEntry.AffinityMask;
        existing.PriorityClass = _currentSelectedEntry.PriorityClass;
        existing.EnforcementLevel = _currentSelectedEntry.EnforcementLevel;
        existing.PathFilter = _currentSelectedEntry.PathFilter;
        existing.ExcludePatterns = _currentSelectedEntry.ExcludePatterns;
        existing.PreferredCores = _currentSelectedEntry.PreferredCores;
        existing.Enabled = _currentSelectedEntry.Enabled;
        existing.ProcessName = _currentSelectedEntry.ProcessName;
        existing.IoPriority = _currentSelectedEntry.IoPriority;
        existing.MemoryPriority = _currentSelectedEntry.MemoryPriority;
        existing.MainThreadBind = _currentSelectedEntry.MainThreadBind;
        CoreKeepService.Save(data);
      }
    }

    // ── 手动模式核心选择 ──

    void CoreKeepCoreCheckChanged(object sender, RoutedEventArgs e) {
      if (_currentSelectedEntry == null || NormalizeMode(_currentSelectedEntry.CoreMode) != "Manual") return;
      var selected = new List<int>();
      foreach (var item in CoreKeepCoreList.Items) {
        var ci = item as CoreCheckItem;
        if (ci != null && ci.IsChecked) selected.Add(ci.CoreIndex);
      }
      if (selected.Count == 0) return;
      long mask = CoreKeepService.ModeToAffinityMask("Manual", selected.ToArray());
      _currentSelectedEntry.AffinityMask = mask;
      _currentSelectedEntry.PreferredCores = selected.ToArray();
      PersistEntryChange();
      RefreshCoreKeepList(CoreKeepService.Load());
      CoreKeepAffinityText.Text = "0x" + mask.ToString("X");
      if (CoreKeepMasterToggle.IsChecked == true)
        CoreKeepService.ApplyToProcess(_currentSelectedEntry.ProcessName, _currentSelectedEntry);
    }

    // ── 守护定时器 ──

    void CoreKeepGuardToggle_Changed(object sender, RoutedEventArgs e) {
      if (_loading || CoreKeepGuardToggle == null) return;
      bool on = CoreKeepGuardToggle.IsChecked == true;
      CoreKeepGuardInterval.IsEnabled = on;
      var data = CoreKeepService.Load();
      data.GuardIntervalMs = on ? (int)(CoreKeepGuardInterval.Value * 1000) : -1;
      CoreKeepService.Save(data);
      if (on) {
        CoreKeepService.UpdateGuardInterval(data.GuardIntervalMs);
        foreach (var entry in data.Entries) entry.GuardEnabled = true;
        CoreKeepService.Save(data);
      } else {
        foreach (var entry in data.Entries) entry.GuardEnabled = false;
        CoreKeepService.Save(data);
        CoreKeepService.StopAutoApply();
        if (CoreKeepMasterToggle.IsChecked == true)
          CoreKeepService.StartAutoApply(data);
      }
    }

    void CoreKeepGuardInterval_Changed(object s, RoutedEventArgs e) {
      if (_loading || CoreKeepGuardInterval == null) return;
      var data = CoreKeepService.Load();
      data.GuardIntervalMs = (int)(CoreKeepGuardInterval.Value * 1000);
      CoreKeepService.Save(data);
      CoreKeepService.UpdateGuardInterval(data.GuardIntervalMs);
    }

    // ── 核心竞速 ──

    void CoreKeepBenchmark_Click(object sender, RoutedEventArgs e) {
      CoreKeepBenchBtn.Content = Strings.CoreKeepBenchmarkRunning;
      CoreKeepBenchBtn.IsEnabled = false;
      System.Threading.ThreadPool.QueueUserWorkItem(_ => {
        try {
          var results = CoreKeepService.RunBenchmark(500);
          Dispatcher.BeginInvoke(new Action(() => {
            CoreKeepBenchBtn.Content = Strings.CoreKeepBenchmarkDone;
            CoreKeepBenchBtn.IsEnabled = true;
            var best = results.OrderBy(r => r.Score).Take(8).ToList();
            string msg = string.Join("\n", best.Select(r =>
              string.Format(Strings.CoreKeepBenchmarkResult, r.CoreIndex, r.Score, r.Relative)));
            DialogHelper.Info(msg, Strings.CoreKeepBenchmark);
          }));
        } catch {
          Dispatcher.BeginInvoke(new Action(() => {
            CoreKeepBenchBtn.Content = Strings.CoreKeepBenchBtn;
            CoreKeepBenchBtn.IsEnabled = true;
          }));
        }
      });
    }

    // ── 刷新列表 + 返回 ──

    void RefreshCoreKeepList(CoreKeepData data) {
      string selectedName = _currentSelectedEntry?.ProcessName;
      CoreKeepList.ItemsSource = data.Entries;
      UpdateEmptyHint(data.Entries.Count);
      UpdateStats();
      if (selectedName != null) {
        for (int i = 0; i < CoreKeepList.Items.Count; i++) {
          if ((CoreKeepList.Items[i] as CoreKeepEntry)?.ProcessName == selectedName) {
            CoreKeepList.SelectedIndex = i;
            break;
          }
        }
      }
    }

    void BackToPerf_Click(object sender, RoutedEventArgs e) {
      OmenSuperHub.Views.MainWindow.NavigateToPage("Perf");
    }

    // ── 批量操作 (对当前筛选结果生效) ──

    void BuildBatchCombos() {
      BatchModeCombo.Items.Clear();
      var topo = CoreKeepService.GetTopologyInfo();
      AddMode(BatchModeCombo, "all-cores", Strings.CoreKeepModeAll);
      if (topo.IsHybrid) {
        AddMode(BatchModeCombo, "p-cores", Strings.CoreKeepModePCores);
        AddMode(BatchModeCombo, "e-cores", Strings.CoreKeepModeECores);
        if (topo.HasSmt) {
          AddMode(BatchModeCombo, "p-cores-smt", Strings.CoreKeepModePCoresSmt);
          AddMode(BatchModeCombo, "p-cores-no-smt", Strings.CoreKeepModePCoresNoSmt);
        }
        AddMode(BatchModeCombo, "p-cores-first", Strings.CoreKeepModePerformanceFirst);
      }
      if (topo.HasSmt)
        AddMode(BatchModeCombo, "no-smt", Strings.CoreKeepModeNoSmt);
      AddMode(BatchModeCombo, "first-half", Strings.CoreKeepModeFirstHalf);
      AddMode(BatchModeCombo, "second-half", Strings.CoreKeepModeSecondHalf);
      if (topo.IsDualCcd) {
        AddMode(BatchModeCombo, "ccd0", Strings.CoreKeepModeCcd0);
        AddMode(BatchModeCombo, "ccd1", Strings.CoreKeepModeCcd1);
      }
      // ponytail: 批量不暴露 Manual/custom — 需要逐核交互，与批量语义冲突
      if (BatchModeCombo.Items.Count > 0) BatchModeCombo.SelectedIndex = 0;

      BatchLevelCombo.Items.Clear();
      BatchLevelCombo.Items.Add(new ComboBoxItem { Content = Strings.CoreKeepEnforcementSoft, Tag = SoftCpuSets });
      BatchLevelCombo.Items.Add(new ComboBoxItem { Content = Strings.CoreKeepEnforcementHard, Tag = HardAffinity });
      BatchLevelCombo.Items.Add(new ComboBoxItem { Content = Strings.CoreKeepEnforcementJob, Tag = JobEnforced });
      BatchLevelCombo.Items.Add(new ComboBoxItem { Content = Strings.CoreKeepEnforcementLocked, Tag = JobLocked });
      BatchLevelCombo.SelectedIndex = 1;
    }

    void BatchPanel_Expanded(object sender, RoutedEventArgs e) {
      // ponytail: 展开时同步影响范围（搜索框可能已变更）
      UpdateBatchImpact();
    }

    // 从批量 ComboBox 取当前 mode/level；mode 为 null 表示用户未选
    bool TryGetBatchSelection(out string mode, out string level) {
      mode = null; level = null;
      if (BatchModeCombo.SelectedItem is ComboBoxItem mi && mi.Tag is string m) mode = m;
      if (BatchLevelCombo.SelectedItem is ComboBoxItem li && li.Tag is string l) level = l;
      return !string.IsNullOrEmpty(mode) && !string.IsNullOrEmpty(level);
    }

    // ponytail: 批量目标 = 当前筛选结果中已勾选的进程；未勾选时回退到全部筛选结果
    List<ProcessItemView> GetCheckedTargets() {
      var list = new List<ProcessItemView>();
      foreach (var item in ProcessListBox.Items) {
        if (item is ProcessItemView v && v.IsChecked) list.Add(v);
      }
      return list;
    }

    List<ProcessItemView> GetBatchTargets() {
      var checkedList = GetCheckedTargets();
      return checkedList.Count > 0 ? checkedList : GetFilteredTargets();
    }

    List<ProcessItemView> GetFilteredTargets() {
      var list = new List<ProcessItemView>();
      foreach (var item in ProcessListBox.Items) {
        if (item is ProcessItemView v) list.Add(v);
      }
      return list;
    }

    void BatchSelectAll_Click(object sender, RoutedEventArgs e) {
      bool anyUnchecked = false;
      foreach (var item in ProcessListBox.Items) {
        if (item is ProcessItemView v && !v.IsChecked) { anyUnchecked = true; break; }
      }
      foreach (var item in ProcessListBox.Items) {
        if (item is ProcessItemView v) v.IsChecked = anyUnchecked;
      }
      // ponytail: 无 INPC，重设 ItemsSource 强制 CheckBox 模板读取新值
      var src = ProcessListBox.ItemsSource;
      ProcessListBox.ItemsSource = null;
      ProcessListBox.ItemsSource = src;
      UpdateBatchImpact();
    }

    void BatchApply_Click(object sender, RoutedEventArgs e) {
      if (!TryGetBatchSelection(out string mode, out string level)) return;
      var targets = GetBatchTargets();
      if (targets.Count == 0) return;
      SetBatchButtonsEnabled(false);
      System.Threading.ThreadPool.QueueUserWorkItem(_ => {
        int ok = 0, fail = 0;
        foreach (var t in targets) {
          try { if (CoreKeepService.QuickApply(t.Pid, mode, level)) ok++; else fail++; }
          catch { fail++; }
        }
        Dispatcher.BeginInvoke(new Action(() => {
          SetBatchButtonsEnabled(true);
          DialogHelper.Info(string.Format(Strings.CoreKeepBatchDone, ok, fail), Strings.CoreKeepBatchHeading);
          RefreshProcessList();
        }));
      });
    }

    void BatchRelax_Click(object sender, RoutedEventArgs e) {
      var targets = GetBatchTargets();
      if (targets.Count == 0) return;
      SetBatchButtonsEnabled(false);
      System.Threading.ThreadPool.QueueUserWorkItem(_ => {
        int ok = 0, fail = 0;
        foreach (var t in targets) {
          try { if (CoreKeepService.RelaxPid(t.Pid)) ok++; else fail++; }
          catch { fail++; }
        }
        Dispatcher.BeginInvoke(new Action(() => {
          SetBatchButtonsEnabled(true);
          DialogHelper.Info(string.Format(Strings.CoreKeepBatchDone, ok, fail), Strings.CoreKeepBatchHeading);
          RefreshProcessList();
        }));
      });
    }

    void BatchAddRules_Click(object sender, RoutedEventArgs e) {
      if (!TryGetBatchSelection(out string mode, out string level)) return;
      var targets = GetBatchTargets();
      if (targets.Count == 0) return;
      var data = CoreKeepService.Load();
      int added = 0, skipped = 0;
      var existingNames = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
      foreach (var en in data.Entries) {
        if (!string.IsNullOrEmpty(en.ProcessName)) existingNames.Add(en.ProcessName);
      }
      foreach (var t in targets) {
        if (string.IsNullOrEmpty(t.Name) || existingNames.Contains(t.Name)) { skipped++; continue; }
        var entry = CoreKeepService.CaptureFromProcess(t.Name);
        entry.Enabled = true;
        entry.CoreMode = mode;
        entry.EnforcementLevel = level;
        entry.GuardEnabled = true;
        CoreKeepService.ApplyModeToEntry(entry, mode);
        data.Entries.Add(entry);
        existingNames.Add(t.Name);
        added++;
      }
      if (added > 0) {
        CoreKeepService.Save(data);
        if (data.MasterEnabled) CoreKeepService.StartAutoApply(data);
        RefreshCoreKeepList(data);
      }
      DialogHelper.Info(string.Format(Strings.CoreKeepBatchDone, added, skipped), Strings.CoreKeepBatchHeading);
    }

    void SetBatchButtonsEnabled(bool on) {
      BatchApplyBtn.IsEnabled = on;
      BatchRelaxBtn.IsEnabled = on;
      BatchAddRulesBtn.IsEnabled = on;
    }
  }

  // ponytail: 进程列表项 ViewModel（对齐 XAML 绑定）
  public class ProcessItemView {
    public bool IsChecked { get; set; }
    public int Pid { get; set; }
    public string Name { get; set; }
    public string Path { get; set; }
    public string PathText { get; set; }
    public string PidText { get; set; }
    public string AffinityText { get; set; }
    public string MatchedRule { get; set; }
    public string RuleLevel { get; set; }
    /// <summary>CPU 占用率百分比（两拍差分采样），-1 = 未采样。</summary>
    public double CpuPct { get; set; }
    public string CpuText { get; set; }
    /// <summary>是否可批量路由（排除系统进程/自身）。</summary>
    public bool CanBatchManage { get; set; }
    public string ManageLockText => CanBatchManage ? "" : "🔒";
    public SolidColorBrush RuleBadgeBrush { get; set; }
    public SolidColorBrush RuleBadgeForeground { get; set; }
    public Visibility HasRule => string.IsNullOrEmpty(MatchedRule) ? Visibility.Collapsed : Visibility.Visible;
    public string RuleLevelText => string.IsNullOrEmpty(RuleLevel) ? "" : LevelToDisplay(RuleLevel);

    static string LevelToDisplay(string lvl) {
      switch (lvl) {
        case "soft-cpu-sets":  return "提示";
        case "hard-affinity": return "软强制";
        case "job-enforced":  return "硬强制";
        case "job-locked":    return "锁定";
        default:              return lvl;
      }
    }
  }

  // ponytail: CPU 拓扑核心可视化 ViewModel
  public class CoreVisualView {
    public int Index { get; set; }
    public string Tooltip { get; set; }
    public SolidColorBrush ColorBrush { get; set; }
  }

  // ponytail: 核心选择 CheckBox 的简易 ViewModel（与 PerfPage 解耦后定义在此处）
  public class CoreCheckItem {
    public int CoreIndex { get; set; }
    public bool IsChecked { get; set; }
  }
}
