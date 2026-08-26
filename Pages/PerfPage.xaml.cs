// PerfPage.cs - 性能调优页面
// CPU 功耗 (PL1/PL2)、GPU 设置 (TGP/PPAB/dState/DB/时钟)、电源方案、热切换、刷新率
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using OmenSuperHub.Models;
using OmenSuperHub.Services;
using OmenSuperHub.Utils;
using static OmenSuperHub.OmenHardware;

namespace OmenSuperHub.Pages {
  public partial class PerfPage : System.Windows.Controls.Page {
    public static PerfPage Instance { get; private set; }
    bool _loading;
    bool _optionsBuilt;
    // ponytail: 原临时写 %TEMP%/OmenXHub-PerfPage.log 的调试日志,统一收敛到全局 Logger.Verbose
    // (受 ConfigService.VerboseLogging 开关控制,默认不落盘;开 verbose 才记录)。
    static void Log(string msg) => Logger.Verbose($"[PerfPage] {msg}");

    public PerfPage() {
      Instance = this;
      _loading = true;   // ponytail: suppress NumberBox ValueChanged during layout/template sync
      InitializeComponent();
      Loaded += PerfPage_Loaded;
      // ponytail: CachedPageService keeps last 3 pages alive; OnPresetChanged / OnLanguageChanged
      // 订阅在 Loaded 中、不取消则保留强引用让本页无法 GC。Unloaded 时统一取消，pages
      // 在被缓存驱逐前会先 Unload。Loaded 内仍用 -= 保证幂等。
      Unloaded += PerfPage_Unloaded;
    }

    void PerfPage_Unloaded(object sender, RoutedEventArgs e) {
      PresetManager.OnPresetChanged -= OnPresetChanged;
      Strings.OnLanguageChanged -= RefreshHeteroLabels;
      // ponytail: 断静态强引用 — CachedPageService 在 ReleaseFrontend 时会清掉本页字典引用,
      // 但 Instance 静态字段会持续钉住旧实例。Unloaded 是唯一对称的清理点；下次 ctor 重赋值。
      Instance = null;
    }

    void PerfPage_Loaded(object sender, RoutedEventArgs e) {
      Log("PerfPage_Loaded start");
      _loading = true;
      if (!_optionsBuilt) {
        BuildOptions();
        BuildPwrPlanOptions();
        _optionsBuilt = true;
      } else {
        BuildPowerPlanOptions();
      }
      LoadStateFast();
      // ponytail: DON'T set _loading=false here — NumberBox ValueChanged fires
      //          deferred after LoadStateFast() and would corrupt ConfigService
      //          with the Minimum-clamped value.  _loading is reset to false
      //          at ContextIdle inside the BeginInvoke(Loaded) callback below.
      // ponytail: remove-then-add so cached pages don't stack subscriptions
      PresetManager.OnPresetChanged -= OnPresetChanged;
      PresetManager.OnPresetChanged += OnPresetChanged;

      RefreshPresetList();

      Dispatcher.BeginInvoke(new Action(() => {
        _loading = true;
        LoadStateDeferred();
        InitHeteroCpu();
        ApplyHardwareVisibility();
        LoadPwrPlanSettings();
        _loading = false;
        _perfExpanded = ActualWidth > PerfCollapseWidth;
        if (_perfExpanded) ExpandPerfGrids();
        else CollapsePerfGrids();
      }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    void OnPresetChanged(string preset) {
      _loading = true;
      LoadStateFast();
      try { LoadStateDeferred(); } catch { }
      // ponytail: dynamic — find index by tag in combo items
      int idx = -1;
      for (int i = 0; i < cbxPerfPreset.Items.Count; i++) {
        if (cbxPerfPreset.Items[i] is ComboBoxItem item && item.Tag as string == preset) { idx = i; break; }
      }
      if (idx >= 0 && cbxPerfPreset.SelectedIndex != idx)
        cbxPerfPreset.SelectedIndex = idx;
      // ponytail: defer _loading=false to ContextIdle so stray NumberBox
      //          ValueChanged (fired after programmatic value set) are suppressed
      Dispatcher.BeginInvoke(new Action(() => {
        _loading = false;
      }), System.Windows.Threading.DispatcherPriority.ContextIdle);
    }

    // ══════════════════════════════════════
    //   Native methods for Power & Display
    //   → 已提取到 NativeMethods.cs
    // ══════════════════════════════════════

    // ══════════════════════════════════════
    //   Option builders
    // ══════════════════════════════════════
    void BuildOptions() {
      CpuPowerCombo.Items.Clear();
      CpuPowerCombo.Items.Add(new ComboBoxItem { Content = Strings.NotSet, Tag = "null" });
      CpuPowerCombo.Items.Add(new ComboBoxItem { Content = Strings.Maximum, Tag = "max" });
      for (int w = 10; w <= 254; w++) CpuPowerCombo.Items.Add(new ComboBoxItem { Content = w + " W", Tag = w });

      AmdPptCombo.Items.Clear();
      AmdPptCombo.Items.Add(new ComboBoxItem { Content = Strings.NotSet, Tag = 0 });
      // ponytail: 1W 步进，跟 Intel 一致
      for (int w = 8; w <= 300; w++) AmdPptCombo.Items.Add(new ComboBoxItem { Content = w + " W", Tag = w });
	
      IccMaxCombo.Items.Clear();
      IccMaxCombo.Items.Add(new ComboBoxItem { Content = Strings.NotSet, Tag = 0 });
      for (int a = 160; a <= 255; a++) IccMaxCombo.Items.Add(new ComboBoxItem { Content = a + " A", Tag = a });

      AcLoadLineCombo.Items.Clear();
      AcLoadLineCombo.Items.Add(new ComboBoxItem { Content = Strings.NotSet, Tag = 0 });
      AcLoadLineCombo.Items.Add(new ComboBoxItem { Content = "170 mOhm", Tag = 1 });
      AcLoadLineCombo.Items.Add(new ComboBoxItem { Content = "160 mOhm", Tag = 2 });
      AcLoadLineCombo.Items.Add(new ComboBoxItem { Content = "150 mOhm", Tag = 3 });

      CtgpCombo.Items.Clear();
      CtgpCombo.Items.Add(new ComboBoxItem { Content = Strings.Enable, Tag = true });
      CtgpCombo.Items.Add(new ComboBoxItem { Content = Strings.Disable, Tag = false });

      // TPP presets removed — use TppNum + TppExtraSlider directly

      GpuClockCombo.Items.Clear();
      GpuClockCombo.Items.Add(new ComboBoxItem { Content = Strings.GpuClockRestore, Tag = 0 });
      int[] clockPresets = { 300, 600, 900, 1200, 1500, 1800, 2100, 2500 };
      foreach (int c in clockPresets) GpuClockCombo.Items.Add(new ComboBoxItem { Content = c + " MHz", Tag = c });

      GpuCoreOCCombo.Items.Clear();
      GpuCoreOCCombo.Items.Add(new ComboBoxItem { Content = Strings.NotSet, Tag = 0 });
      for (int o = -270; o <= 270; o += 15) {
        if (o == 0) continue;
        GpuCoreOCCombo.Items.Add(new ComboBoxItem { Content = (o >= 0 ? "+" : "") + o + " MHz", Tag = o });
      }

      GpuMemoryOCCombo.Items.Clear();
      GpuMemoryOCCombo.Items.Add(new ComboBoxItem { Content = Strings.NotSet, Tag = 0 });
      for (int m = 100; m <= 2000; m += 100)
        GpuMemoryOCCombo.Items.Add(new ComboBoxItem { Content = "+" + m + " MHz", Tag = m });

      FpsCombo.Items.Clear();
      FpsCombo.Items.Add(new ComboBoxItem { Content = Strings.Unlimited, Tag = 0 });
      foreach (int f in new[] { 30, 60, 90, 120, 144, 165, 240, 300, 360, 480, 1000 })
        FpsCombo.Items.Add(new ComboBoxItem { Content = f + " FPS", Tag = f });

      PowerModeCombo.Items.Clear();
      var modes = GetWindowsPowerModes();
      int savedPm = ConfigService.PowerMode;
      foreach (var m in modes)
        PowerModeCombo.Items.Add(new ComboBoxItem { Content = m.Name, Tag = m.Value, IsSelected = m.Value == savedPm });
      if (PowerModeCombo.SelectedIndex < 0 && modes.Count > 0) PowerModeCombo.SelectedIndex = 0;

      BuildPowerPlanOptions();
      BuildRefreshRateOptions();
      BuildResolutionOptions();
      BuildDpiOptions();
      InitHdr();
    }

    void LoadState() {
      LoadStateFast();
      LoadStateDeferred();
    }

    void LoadStateFast() {
      if (!string.IsNullOrEmpty(ConfigService.CpuPower)) {
        string cp = ConfigService.CpuPower;
        SelectCombo(CpuPowerCombo, cp == "max" ? Strings.Maximum : cp == "null" ? Strings.NotSet : cp);
        int pl1, pl2;
        if (cp == "max") { pl1 = 254; pl2 = 254; }
        else if (cp == "null") { pl1 = -1; pl2 = -1; }
        else if (int.TryParse(cp.Replace(" W", ""), out int w) && w >= 10 && w <= 254) {
          // ponytail: prefer independently stored PL1/PL2 over combo-derived wattage.
          pl1 = ConfigService.CpuPowerPl1 >= 10 ? ConfigService.CpuPowerPl1 : w;
          pl2 = ConfigService.CpuPowerPl2 >= 10 ? ConfigService.CpuPowerPl2 : w;
        }
        else { pl1 = ConfigService.CpuPowerPl1 > 0 ? ConfigService.CpuPowerPl1 : 254; pl2 = ConfigService.CpuPowerPl2 > 0 ? ConfigService.CpuPowerPl2 : 254; }
        CpuPowerPL1Slider.Value = pl1 > 0 ? pl1 : 254;
        CpuPowerPL2Slider.Value = pl2 > 0 ? pl2 : 254;
        CpuPowerPL1Num.Value = pl1 > 0 ? pl1 : 254;
        CpuPowerPL2Num.Value = pl2 > 0 ? pl2 : 254;
      }
      if (ConfigService.IccMax > 0) {
        SelectCombo(IccMaxCombo, ConfigService.IccMax + " A");
        IccMaxSlider.Value = ConfigService.IccMax;
        IccMaxNum.Value = ConfigService.IccMax;
      } else {
        SelectCombo(IccMaxCombo, Strings.NotSet);
        IccMaxSlider.Value = 0;
        IccMaxNum.Value = 0;
      }
      if (ConfigService.AcLoadLine > 0) {
        int mOhm = 180 - 10 * ConfigService.AcLoadLine;
        SelectCombo(AcLoadLineCombo, mOhm + " mOhm");
      } else SelectCombo(AcLoadLineCombo, Strings.NotSet);
      // ── AMD PPT Combo ──
      if (ConfigService.AmdCpuPpt > 0) SelectComboByTag(AmdPptCombo, ConfigService.AmdCpuPpt);
      else SelectCombo(AmdPptCombo, Strings.NotSet);
      // ── AMD 滑块同步（预设切换时通过 OnPresetChanged → LoadStateFast 带到这里） ──
      AmdCpuPptSlider.Value = ConfigService.AmdCpuPpt > 0 ? ConfigService.AmdCpuPpt : 105;
      AmdCpuPptNum.Value = AmdCpuPptSlider.Value;
      // ── AMD Curve Optimizer 降压滑块同步 ──
      AmdUndervoltSlider.Value = ConfigService.AmdCpuUndervolt;
      AmdUndervoltNum.Value = ConfigService.AmdCpuUndervolt;
      // ── 电源模式：从 ConfigService 同步 ──
      SelectComboByTag(PowerModeCombo, ConfigService.PowerMode);

      PpabCheck.IsChecked = ConfigService.PpabEnabled;
      SelectCombo(CtgpCombo, ConfigService.TgpEnabled ? Strings.Enable : Strings.Disable);
      TppExtraSlider.Value = ConfigService.Tpp;
      TppNum.Value = ConfigService.Tpp;
      UpdateTppEnabled();
      DbVersionCombo.SelectedIndex = ConfigService.DBVersion == 1 ? 0 : 1;
      DStateCombo.SelectedIndex = ConfigService.DState == 2 ? 1 : 0;
      UpdateTgpStatus();
      if (ConfigService.MaxFrameRate <= 0) {
        SelectCombo(FpsCombo, Strings.Unlimited);
        FpsSlider.Value = 0;
        FpsNum.Value = 0;
      } else {
        SelectCombo(FpsCombo, ConfigService.MaxFrameRate + " FPS");
        FpsSlider.Value = ConfigService.MaxFrameRate;
        FpsNum.Value = ConfigService.MaxFrameRate;
      }
      if (ConfigService.GpuClock <= 0) {
        SelectCombo(GpuClockCombo, Strings.GpuClockRestore);
        GpuClockSlider.Value = 0;
        GpuClockNum.Value = 0;
      } else {
        SelectCombo(GpuClockCombo, ConfigService.GpuClock + " MHz");
        GpuClockSlider.Value = ConfigService.GpuClock;
        GpuClockNum.Value = ConfigService.GpuClock;
      }
      int coreOc = ConfigService.GpuCoreOverclock;
      if (coreOc < 0) {
        SelectComboByTag(GpuCoreOCCombo, 0);
        GpuCoreOCSlider.Value = 0;
        GpuCoreOCNum.Value = 0;
      } else {
        SelectComboByTag(GpuCoreOCCombo, coreOc);
        GpuCoreOCSlider.Value = coreOc;
        GpuCoreOCNum.Value = coreOc;
      }
      int memOc = ConfigService.GpuMemoryOverclock;
      if (memOc < 0) {
        SelectComboByTag(GpuMemoryOCCombo, 0);
        GpuMemoryOCSlider.Value = 0;
        GpuMemoryOCNum.Value = 0;
      } else {
        SelectComboByTag(GpuMemoryOCCombo, memOc);
        GpuMemoryOCSlider.Value = memOc;
        GpuMemoryOCNum.Value = memOc;
      }
      // ── 电源计划：从 ConfigService 同步 Combo 选中项 ──
      string syncPwr = ConfigService.PowerPlanGuid;
      if (!string.IsNullOrEmpty(syncPwr)) {
        foreach (ComboBoxItem item in PowerPlanCombo.Items) {
          if (item.Tag is string t && t.Equals(syncPwr, StringComparison.OrdinalIgnoreCase)) {
            PowerPlanCombo.SelectedItem = item;
            break;
          }
        }
      }

      EcoQosToggle.IsChecked = ConfigService.EcoQosEnabled;
      EcoQosThrottlePluggedToggle.IsChecked = ConfigService.EcoQosThrottlePlugged;
      UpdateEcoQosSubEnabled();
    }

    void LoadStateDeferred() {
      GetGfxMode(out int mode);
      GfxModeCombo.SelectedIndex = mode;
      UpdateHotSwitchVisibility(mode);
      // ponytail: CoreKeep UI 已迁移到 CoreKeepPage 二级菜单，PerfPage 不再初始化
    }

    void SelectCombo(ComboBox combo, string text) {
      foreach (ComboBoxItem item in combo.Items)
        if (string.Equals(item.Content?.ToString(), text, StringComparison.Ordinal)) { combo.SelectedItem = item; return; }
    }

    void SelectComboByTag(ComboBox combo, object tag) {
      foreach (ComboBoxItem item in combo.Items)
        if (item.Tag != null && item.Tag.Equals(tag)) { combo.SelectedItem = item; return; }
    }

    // ── AMD PPT ComboBox ──
    void AmdPptCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) {
      if (_loading) return;
      _loading = true;
      try {
        var item = AmdPptCombo.SelectedItem as ComboBoxItem;
        if (item == null) return;
        int watts = (int)item.Tag;
        if (watts == 0) {
          AmdCpuPptSlider.Value = 105;
          AmdCpuPptNum.Value = 105;
          ConfigService.AmdCpuPpt = 0; ConfigService.Save("AmdCpuPpt");
          return;
        }
        AmdCpuPptSlider.Value = watts;
        AmdCpuPptNum.Value = watts;
        ConfigService.AmdCpuPpt = watts; ConfigService.Save("AmdCpuPpt");
        // ponytail: PPT 走 WMI（SMU 兜底已随高级调教删除；本机不可用）
        if (watts <= 255) SetCpuPowerLimit((byte)watts);
      } finally { _loading = false; }
    }

    void CpuPower_SelectionChanged(object sender, SelectionChangedEventArgs e) {
      if (_loading) return;
      _loading = true;
      try {
        var item = CpuPowerCombo.SelectedItem as ComboBoxItem;
        if (item == null) return;
        string tag = item.Tag.ToString();
        if (tag == "null") {
          ConfigService.CpuPower = "null";
          ConfigService.CpuPowerPl1 = -1;
          ConfigService.CpuPowerPl2 = -1;
          ConfigService.Save("CpuPower");
          ConfigService.Save("CpuPowerPl1");
          ConfigService.Save("CpuPowerPl2");
          return;
        }
        if (tag == "max") {
          ConfigService.CpuPower = "max";
          ConfigService.CpuPowerPl1 = 254;
          ConfigService.CpuPowerPl2 = 254;
          CpuPowerPL1Slider.Value = 254;
          CpuPowerPL2Slider.Value = 254;
          CpuPowerPL1Num.Value = 254;
          CpuPowerPL2Num.Value = 254;
          SetCpuPowerLimit(254);
          ConfigService.Save("CpuPower");
          ConfigService.Save("CpuPowerPl1");
          ConfigService.Save("CpuPowerPl2");
          return;
        }
        if (int.TryParse(tag, out int val) && val >= 10 && val <= 254) {
          ConfigService.CpuPower = val + " W";
          ConfigService.CpuPowerPl1 = val;
          ConfigService.CpuPowerPl2 = val;
          CpuPowerPL1Slider.Value = val;
          CpuPowerPL2Slider.Value = val;
          CpuPowerPL1Num.Value = val;
          CpuPowerPL2Num.Value = val;
          // ponytail: AMD PPT 走 WMI（SMU 兜底已随高级调教删除）
          SetCpuPowerLimit((byte)val);
          ConfigService.Save("CpuPower");
          ConfigService.Save("CpuPowerPl1");
          ConfigService.Save("CpuPowerPl2");
        }
      } finally { _loading = false; }
    }

    void CpuPowerPL1Num_ValueChanged(object s, RoutedEventArgs e) {
      if (_loading) return;
      _loading = true;
      try {
        double? val = CpuPowerPL1Num.Value;
        if (val == null || val < 1 || val > 254) return;
        int v = (int)val;
        if (v == ConfigService.CpuPowerPl1) return;
        // PL1 是 CPU 功率代表值，PL2 必须 >= PL1，否则 0x29 不生效。
        int pl2 = ConfigService.CpuPowerPl2 > 0 ? ConfigService.CpuPowerPl2 : 254;
        if (v > pl2) {
          // 抬高 PL1 越过 PL2：先把 PL2 拉到 PL1 才能生效，原地写 PL1 alone 无意义。
          if (!SetCpuPowerLimit((byte)v, (byte)v)) return;
          ConfigService.CpuPowerPl1 = v;
          ConfigService.CpuPowerPl2 = v;
          ConfigService.CpuPower = v + " W";
          CpuPowerPL2Slider.Value = v;
          CpuPowerPL2Num.Value = v;
          SelectCombo(CpuPowerCombo, v + " W");
          ConfigService.Save("CpuPower");
          return;
        }
        if (!SetCpuPowerLimitPL1Only((byte)v)) return;
        ConfigService.CpuPowerPl1 = v;
        ConfigService.CpuPower = v + " W";
        SelectCombo(CpuPowerCombo, v + " W");
        ConfigService.Save("CpuPower");
      } finally { _loading = false; }
    }

    void CpuPowerPL2Num_ValueChanged(object s, RoutedEventArgs e) {
      if (_loading) return;
      _loading = true;
      try {
        double? val = CpuPowerPL2Num.Value;
        if (val == null || val < 1 || val > 254) return;
        int v = (int)val;
        if (v == ConfigService.CpuPowerPl2) return;
        int pl1 = ConfigService.CpuPowerPl1 > 0 ? ConfigService.CpuPowerPl1 : 254;
        // PL2 必须 >= PL1，否则 PL1 持续压制，PL2 不生效。夹紧到 PL1，体现到 UI。
        if (v < pl1) {
          v = pl1;
          CpuPowerPL2Slider.Value = v;
          CpuPowerPL2Num.Value = v;
        }
        if (!SetCpuPowerLimitPL2Only((byte)v)) return;
        ConfigService.CpuPowerPl2 = v;
        ConfigService.Save("CpuPowerPl2");
        // CPU 功率 UI 值跟 PL1 走，PL2 单改不动它。
      } finally { _loading = false; }
    }

    // ── IccMax ──
    void IccMax_Changed(object sender, SelectionChangedEventArgs e) {
      if (_loading) return;
      _loading = true;
      try {
        var item = IccMaxCombo.SelectedItem as ComboBoxItem;
        if (item == null) return;
        int val = (int)item.Tag;
        if (val == ConfigService.IccMax) return;
        if (val == 0) { ConfigService.IccMax = 0; IccMaxSlider.Value = 0; ConfigService.Save("IccMax"); return; }
        ConfigService.IccMax = val; IccMaxSlider.Value = val;
        SetIccMaxByWmi(val);
        ConfigService.Save("IccMax");
      } finally { _loading = false; }
    }

    void IccMaxNum_ValueChanged(object s, RoutedEventArgs e) {
      if (_loading) return;
      _loading = true;
      try {
        double? val = IccMaxNum.Value;
        if (val == null || val < 0 || val > 255) return;
        int v = (int)val;
        // ponytail: valid set is {0} ∪ [160,255]; 1-159 is a dead band. Snap to 160
        // (safer floor — sub-160 causes throttling/hang) so the user sees a visible
        // jump instead of silently becoming 0.
        if (v > 0 && v < 160) v = 160;
        if (v == ConfigService.IccMax) return;
        IccMaxNum.Value = v; IccMaxSlider.Value = v;
        if (v > 0) SetIccMaxByWmi(v);
        ConfigService.IccMax = v; ConfigService.Save("IccMax");
        SelectCombo(IccMaxCombo, v == 0 ? Strings.NotSet : v + " A");
      } finally { _loading = false; }
    }

    // ── AC Load Line ──
    void AcLoadLine_Changed(object sender, SelectionChangedEventArgs e) {
      if (_loading) return;
      var item = AcLoadLineCombo.SelectedItem as ComboBoxItem;
      if (item == null) return;
      int level = (int)item.Tag;
      if (level == 0) { ConfigService.AcLoadLine = 0; ConfigService.Save("AcLoadLine"); return; }
      ConfigService.AcLoadLine = level;
      SetLoadLine(level);
      ConfigService.Save("AcLoadLine");
    }

    // ── 电源模式 ──
    
    void CpuOcSettings_Click(object sender, RoutedEventArgs e) {
      var dialog = new CpuOcDialog { Owner = Window.GetWindow(this) }; dialog.ShowDialog();
    }

    void PowerMode_SelectionChanged(object sender, SelectionChangedEventArgs e) {
      if (_loading) return;
      var item = PowerModeCombo.SelectedItem as ComboBoxItem;
      if (item == null) return;
      int val = (int)item.Tag;
      ConfigService.PowerMode = val;
      ConfigService.Save("PowerMode");
      Guid guid;
      if (val == 0) guid = NativeMethods_Power.BEST_POWER_EFFICIENCY;
      else if (val == 2) guid = NativeMethods_Power.BEST_PERFORMANCE;
      else guid = Guid.Empty;
      NativeMethods_Power.PowerSetActiveOverlayScheme(guid);
    }

    // ── 电源计划 ──
    void BuildPowerPlanOptions() {
      PowerPlanCombo.Items.Clear();
      var plans = GetWindowsPowerPlans();
      string savedGuid = ConfigService.PowerPlanGuid;
      foreach (var p in plans) {
        bool isActive = string.IsNullOrEmpty(savedGuid) ? p.IsActive : p.Guid == savedGuid;
        PowerPlanCombo.Items.Add(new ComboBoxItem { Content = p.Name, Tag = p.Guid, IsSelected = isActive });
      }
      if (PowerPlanCombo.SelectedIndex < 0 && plans.Count > 0)
        PowerPlanCombo.SelectedIndex = 0;
    }

    void PowerPlan_SelectionChanged(object sender, SelectionChangedEventArgs e) {
      if (_loading) return;
      var item = PowerPlanCombo.SelectedItem as ComboBoxItem;
      if (item == null) return;
      string guid = (string)item.Tag;
      ConfigService.PowerPlanGuid = guid;
      ConfigService.Save("PowerPlanGuid");
      if (!string.IsNullOrEmpty(guid)) {
        var g = Guid.Parse(guid);
        NativeMethods_Power.PowerSetActiveScheme(IntPtr.Zero, ref g);
      }
      LoadPwrPlanSettings();
    }

    static List<(string Name, string Guid, bool IsActive)> GetWindowsPowerPlans() {
      var plans = new List<(string, string, bool)>();
      try {
        string activeGuid = "";
        IntPtr activePtr;
        if (NativeMethods_Power.PowerGetActiveScheme(IntPtr.Zero, out activePtr) == 0) {
          activeGuid = Marshal.PtrToStructure<Guid>(activePtr).ToString();
          Marshal.FreeHGlobal(activePtr);
        }
        uint index = 0;
        while (true) {
          uint bufSize = 0;
          uint ret = NativeMethods_Power.PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 16, index, IntPtr.Zero, ref bufSize);
          if (ret == 259) break;
          if (ret != 234) break;
          IntPtr buf = Marshal.AllocHGlobal((int)bufSize);
          try {
            ret = NativeMethods_Power.PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 16, index, buf, ref bufSize);
            if (ret != 0) break;
            var guid = Marshal.PtrToStructure<Guid>(buf);
            string name = GetPowerPlanName(guid);
            plans.Add((name, guid.ToString(), guid.ToString() == activeGuid));
          } finally { Marshal.FreeHGlobal(buf); }
          index++;
        }
      } catch { }
      return plans;
    }

    static string GetPowerPlanName(Guid guid) {
      string g = guid.ToString();
      switch (g) {
        case "381b4222-f694-41f0-9685-ff5bb260df2f": return "平衡";
        case "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c": return "高性能";
        case "a1841308-3541-4fab-bc81-f71556f20b4a": return "节能";
        case "e9a42b02-d5df-448d-aa00-03f14749eb61": return "卓越性能";
      }
      try {
        uint bufSize = 2048;
        IntPtr buf = Marshal.AllocHGlobal((int)bufSize);
        try {
          if (NativeMethods_Power.PowerReadFriendlyName(IntPtr.Zero, ref guid, IntPtr.Zero, IntPtr.Zero, buf, ref bufSize) == 0) {
            string name = Marshal.PtrToStringUni(buf);
            if (!string.IsNullOrEmpty(name)) return name;
          }
        } finally { Marshal.FreeHGlobal(buf); }
      } catch { }
      return guid.ToString();
    }

    static List<(string Name, int Value)> GetWindowsPowerModes() {
      return new List<(string, int)> {
        (Strings.PowerModeEfficiency, 0),
        (Strings.PowerModeBalanced, 1),
        (Strings.PowerModePerformance, 2)
      };
    }

    // ── EcoQoS ──
    void UpdateEcoQosSubEnabled() {
      bool on = EcoQosToggle.IsChecked == true;
      EcoQosExtra.Opacity = on ? 1.0 : 0.4;
      EcoQosExtra.IsEnabled = on;
    }

    void EcoQosToggle_Checked(object sender, RoutedEventArgs e) {
      UpdateEcoQosSubEnabled();
      ConfigService.EcoQosEnabled = true;
      ConfigService.Save("EcoQosEnabled");
      EcoQosService.SetEnabled(true);
    }

    void EcoQosToggle_Unchecked(object sender, RoutedEventArgs e) {
      UpdateEcoQosSubEnabled();
      ConfigService.EcoQosEnabled = false;
      ConfigService.Save("EcoQosEnabled");
      EcoQosService.SetEnabled(false);
    }

    void EcoQosThrottlePlugged_Changed(object sender, RoutedEventArgs e) {
      ConfigService.EcoQosThrottlePlugged = EcoQosThrottlePluggedToggle.IsChecked == true;
      ConfigService.Save("EcoQosThrottlePlugged");
      EcoQosService.SetThrottlePlugged(EcoQosThrottlePluggedToggle.IsChecked == true);
    }

    void EcoQosWhitelistEdit_Click(object sender, RoutedEventArgs e) { ShowEcoQosListDialog(true); }
    void EcoQosBlacklistEdit_Click(object sender, RoutedEventArgs e) { ShowEcoQosListDialog(false); }

    void ShowEcoQosListDialog(bool isWhitelist) {
	      string title = isWhitelist ? Strings.EcoQosWhitelist : Strings.EcoQosBlacklist;
      string current = isWhitelist ? ConfigService.EcoQosWhitelist : ConfigService.EcoQosBlacklist;
      var dlg = new Window {
        Title = title, Width = 400, Height = 300,
        WindowStartupLocation = WindowStartupLocation.CenterOwner,
        Owner = Window.GetWindow(this)
      };
      var sp = new StackPanel { Margin = new Thickness(10) };
      var tb = new TextBox { Text = current, AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap, Height = 200,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
      sp.Children.Add(tb);
      var btnP = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
	      var ok = new Button { Content = Strings.ButtonOK, Width = 80, Height = 30 };
      ok.Click += (_, __) => { dlg.DialogResult = true; dlg.Close(); };
      btnP.Children.Add(ok);
	      var cancel = new Button { Content = Strings.ButtonCancel, Width = 80, Height = 30, Margin = new Thickness(8, 0, 0, 0) };
      cancel.Click += (_, __) => { dlg.DialogResult = false; dlg.Close(); };
      btnP.Children.Add(cancel);
      sp.Children.Add(btnP);
      dlg.Content = sp;
      if (dlg.ShowDialog() == true) {
        string result = tb.Text;
        if (isWhitelist) { ConfigService.EcoQosWhitelist = result; ConfigService.Save("EcoQosWhitelist"); EcoQosService.SaveWhitelist(result); }
        else { ConfigService.EcoQosBlacklist = result; ConfigService.Save("EcoQosBlacklist"); EcoQosService.SaveBlacklist(result); }
      }
    }

    // ponytail: CoreKeep 已迁移到独立 CoreKeepPage 二级菜单，PerfPage 仅保留跳转入口
    void CoreKeepGoto_Click(object sender, RoutedEventArgs e) {
      Views.MainWindow.NavigateToPage("CoreKeep");
    }

    // ── GPU 频率限制 ──
    void GpuClock_SelectionChanged(object sender, SelectionChangedEventArgs e) {
      if (_loading) return;
      _loading = true;
      var item = GpuClockCombo.SelectedItem as ComboBoxItem;
      if (item == null) { _loading = false; return; }
      int val = (int)item.Tag;
      ConfigService.GpuClock = val;
      GpuClockSlider.Value = val;
      if (val > 0) TrayService.SetGPUClockLimit(val);
      ConfigService.Save("GpuClock");
      _loading = false;
    }

    void GpuClockNum_ValueChanged(object s, RoutedEventArgs e) {
      if (_loading) return;
      _loading = true;
      double? val = GpuClockNum.Value;
      if (val == null || val < 0 || val > 2500) { _loading = false; return; }
      int v = (int)val;
      if (v > 0) TrayService.SetGPUClockLimit(v);
      ConfigService.GpuClock = v; ConfigService.Save("GpuClock");
      SelectCombo(GpuClockCombo, v == 0 ? Strings.GpuClockRestore : v + " MHz");
      _loading = false;
    }

    // ── GPU 核心超频 ──
    void GpuCoreOC_SelectionChanged(object sender, SelectionChangedEventArgs e) {
      if (_loading) return;
      _loading = true;
      var item = GpuCoreOCCombo.SelectedItem as ComboBoxItem;
      if (item == null) { _loading = false; return; }
      int val = (int)item.Tag;
      ConfigService.GpuCoreOverclock = val;
      GpuCoreOCSlider.Value = val;
      System.Threading.ThreadPool.QueueUserWorkItem(_ => GpuAppManager.SetCoreClockOffset(val));
      ConfigService.Save("GpuCoreOverclock");
      _loading = false;
    }

    void GpuCoreOCNum_ValueChanged(object s, RoutedEventArgs e) {
      if (_loading) return;
      _loading = true;
      double? val = GpuCoreOCNum.Value;
      if (val == null || val < -270 || val > 270) { _loading = false; return; }
      int v = (int)val;
      ConfigService.GpuCoreOverclock = v;
      System.Threading.ThreadPool.QueueUserWorkItem(_ => GpuAppManager.SetCoreClockOffset(v));
      ConfigService.Save("GpuCoreOverclock");
      SelectCombo(GpuCoreOCCombo, (v >= 0 ? "+" : "") + v + " MHz");
      _loading = false;
    }

    // ── GPU 显存超频 ──
    void GpuMemoryOC_SelectionChanged(object sender, SelectionChangedEventArgs e) {
      if (_loading) return;
      _loading = true;
      var item = GpuMemoryOCCombo.SelectedItem as ComboBoxItem;
      if (item == null) { _loading = false; return; }
      int val = (int)item.Tag;
      ConfigService.GpuMemoryOverclock = val;
      GpuMemoryOCSlider.Value = val;
      System.Threading.ThreadPool.QueueUserWorkItem(_ => GpuAppManager.SetMemoryClockOffset(val));
      ConfigService.Save("GpuMemoryOverclock");
      _loading = false;
    }

    void GpuMemoryOCNum_ValueChanged(object s, RoutedEventArgs e) {
      if (_loading) return;
      _loading = true;
      double? val = GpuMemoryOCNum.Value;
      if (val == null || val < 0 || val > 2000) { _loading = false; return; }
      int v = (int)val;
      ConfigService.GpuMemoryOverclock = v;
      System.Threading.ThreadPool.QueueUserWorkItem(_ => GpuAppManager.SetMemoryClockOffset(v));
      ConfigService.Save("GpuMemoryOverclock");
      SelectCombo(GpuMemoryOCCombo, "+" + v + " MHz");
      _loading = false;
    }

    // ── 图形模式 ──
    void GfxMode_SelectionChanged(object s, SelectionChangedEventArgs e) {
      if (_loading) return;
      int mode = GfxModeCombo.SelectedIndex;
      if (mode == 3) {
        if (!DialogHelper.Confirm(Strings.GfxUMAConfirm, Strings.GfxUMATitle)) { LoadState(); return; }
      }
      if (mode >= 0 && SetGfxMode(mode)) {
        GetGfxMode(out int current);
        if (current == mode) {
          DialogHelper.Info(Strings.GfxSwitchedTo(
              mode == 0 ? "NVIDIA Advanced Optimus" :
              mode == 1 ? Strings.GfxDiscreteMode :
              mode == 2 ? Strings.GfxHybridMode : Strings.GfxUMALabel), Strings.Hint);
        } else {
          DialogHelper.Info(Strings.GfxSwitchedTo(
              mode == 0 ? "NVIDIA Advanced Optimus" :
              mode == 1 ? Strings.GfxDiscreteMode :
              mode == 2 ? Strings.GfxHybridMode : Strings.GfxUMALabel) +
              "\n" + Strings.PerfGfxReboot, Strings.Hint);
        }
      }
    }

    // ── 热切换 ──
    void UpdateHotSwitchVisibility(int mode = -1) {
      try {
        if (mode < 0) GetGfxMode(out mode);
        HotSwitchCard.Visibility = (mode == 0 || mode == 2) ? Visibility.Visible : Visibility.Collapsed;
      } catch { HotSwitchCard.Visibility = Visibility.Collapsed; }
    }

    void HotSwitch_Click(object sender, RoutedEventArgs e) {
      int result = LaunchDDS();
      if (result != 0)
        DialogHelper.Warn(Strings.DdsInitFail, Strings.Error);
    }

    // ── DB 版本 ──
    void DbVersion_SelectionChanged(object s, SelectionChangedEventArgs e) {
      if (_loading) return;
      if (!HardwareService.PowerOnline) {
        DialogHelper.Warn(Strings.PleaseConnectAC, Strings.Hint);
        LoadState(); return;
      }
      if (DbVersionCombo.SelectedIndex == 0) {
        if (!TrayService.CheckDBVersion(1)) {
          DialogHelper.Warn(Strings.DriverNotAllow + "\n" + Strings.DriverVersionRange, Strings.Error);
          LoadState(); return;
        }
        if (!DialogHelper.Confirm(Strings.PerfDbUnlockWarning, Strings.DbUnlockTitle)) { LoadState(); return; }
        ConfigService.DBVersion = 1; ConfigService.Save("DBVersion");
        TrayService.ChangeDBVersion(1);
      } else {
        ConfigService.DBVersion = 2; ConfigService.Save("DBVersion");
        TrayService.ChangeDBVersion(2);
      }
    }

    // ── TGP / PPAB ──
    void TppNum_ValueChanged(object s, RoutedEventArgs e) {
      if (_loading) return;
      _loading = true;
      double? val = TppNum.Value;
      if (val == null || val < 0 || val > 254) { _loading = false; return; }
      int v = (int)val;
      SetConcurrentTdp((byte)v);
      ConfigService.Tpp = v; ConfigService.Save("Tpp");
      if (v == 0) { PpabCheck.IsChecked = false; }
      UpdateTppEnabled();
      UpdateTgpStatus();
      _loading = false;
    }

    void UpdateTppEnabled() {
      bool tgpOn = ConfigService.TgpEnabled;
      bool ppabOn = PpabCheck.IsChecked == true;
      bool ppabAllowed = tgpOn && ConfigService.Tpp > 0;
      PpabCheck.IsEnabled = tgpOn;
      TppNum.IsEnabled = tgpOn && ppabOn;
      TppExtraSlider.IsEnabled = tgpOn && ppabOn;
    }

    void UpdateTgpStatus() {
      bool tgp = ConfigService.TgpEnabled;
      bool ppab = ConfigService.PpabEnabled && tgp;
      int dstate = ConfigService.DState;
      string tpp = ConfigService.Tpp > 0 ? $", TPP={ConfigService.Tpp}W" : "";
	      TgpStatus.Text = Strings.PerfTgpStatusFormat(tgp, ppab, dstate, tpp);
    }

    void CtgpCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) {
      if (_loading) return;
      var item = CtgpCombo.SelectedItem as ComboBoxItem;
      if (item == null) return;
      bool enabled = (bool)item.Tag;
      ConfigService.TgpEnabled = enabled;
      if (!enabled) PpabCheck.IsChecked = false;
      ConfigService.Save("TgpEnabled");
      SetGpuPowerState(enabled, PpabCheck.IsChecked == true, ConfigService.DState == 2 ? 2 : 1);
      ConfigService.FirePresetCycled(ConfigService.Preset);
      UpdateTppEnabled();
      UpdateTgpStatus();
    }

    void Ppab_Changed(object sender, RoutedEventArgs e) {
      bool enabled = PpabCheck.IsChecked == true;
      ConfigService.PpabEnabled = enabled;
      ConfigService.Save("PpabEnabled");
      SetGpuPowerState(ConfigService.TgpEnabled, enabled, ConfigService.DState == 2 ? 2 : 1);
      ConfigService.FirePresetCycled(ConfigService.Preset);
      UpdateTppEnabled();
      UpdateTgpStatus();
    }

    // ── dState ──
    void DState_SelectionChanged(object s, SelectionChangedEventArgs e) {
      if (_loading) return;
      ConfigService.DState = DStateCombo.SelectedIndex == 1 ? 2 : 1;
      ConfigService.Save("DState");
      SetGpuPowerState(ConfigService.TgpEnabled, ConfigService.PpabEnabled, ConfigService.DState);
      ConfigService.FirePresetCycled(ConfigService.Preset);
      UpdateTgpStatus();
    }

    // ── 最大帧率 ──
    void Fps_SelectionChanged(object sender, SelectionChangedEventArgs e) {
      if (_loading) return;
      _loading = true;
      try {
        var item = FpsCombo.SelectedItem as ComboBoxItem;
        if (item == null) return;
        int val = (int)item.Tag;
        int configVal = val == 0 ? -1 : val;
        if (configVal == ConfigService.MaxFrameRate) return;
        FpsSlider.Value = val;
        ConfigService.MaxFrameRate = configVal;
        if (HasNvidiaGpu()) HP.Omen.Core.Common.NVidiaApi.NvApiWrapper.NVAPI_SetMaxFrameRate(val);
        ConfigService.Save("MaxFrameRate");
      } finally { _loading = false; }
    }

    void FpsNum_ValueChanged(object s, RoutedEventArgs e) {
      if (_loading) return;
      _loading = true;
      try {
        double? val = FpsNum.Value;
        if (val == null || val < 0) return;
        int v = (int)val;
        int[] presets = { 0, 30, 60, 90, 120, 144, 165, 240, 300, 360, 480, 1000 };
        int best = presets[0];
        foreach (int p in presets) { if (Math.Abs(p - v) < Math.Abs(best - v)) best = p; }
        if (best != v) { FpsNum.Value = best; FpsSlider.Value = best; v = best; }
        int configVal = v == 0 ? -1 : v;
        if (configVal == ConfigService.MaxFrameRate) return;
        ConfigService.MaxFrameRate = configVal;
        if (HasNvidiaGpu()) HP.Omen.Core.Common.NVidiaApi.NvApiWrapper.NVAPI_SetMaxFrameRate(v);
        ConfigService.Save("MaxFrameRate");
        SelectCombo(FpsCombo, v == 0 ? Strings.Unlimited : v + " FPS");
      } finally { _loading = false; }
    }

    // ── 屏幕刷新率 ──
    void BuildRefreshRateOptions() {
      RefreshRateCombo.Items.Clear();
      var rates = GetAvailableRefreshRates();
      foreach (int r in rates)
        RefreshRateCombo.Items.Add(new ComboBoxItem { Content = r + " Hz", Tag = r });
      // Select saved refresh rate if set, otherwise default to max available
      int targetHz = ConfigService.RefreshRate > 0 ? ConfigService.RefreshRate : (rates.Count > 0 ? rates.Max() : 60);
      SelectCombo(RefreshRateCombo, targetHz + " Hz");
      if (RefreshRateCombo.SelectedIndex < 0 && rates.Count > 0)
        RefreshRateCombo.SelectedIndex = rates.Count - 1;
      RefreshRateSlider.Value = (int)((RefreshRateCombo.SelectedItem as ComboBoxItem)?.Tag ?? targetHz);
    }

    void RefreshRate_SelectionChanged(object sender, SelectionChangedEventArgs e) {
      if (_loading) return;
      _loading = true;
      try {
        var item = RefreshRateCombo.SelectedItem as ComboBoxItem;
        if (item == null) return;
        int val = (int)item.Tag;
        if (val == ConfigService.RefreshRate) return;
        RefreshRateSlider.Value = val;
        ConfigService.RefreshRate = val;
        ApplyRefreshRate(val);
        ConfigService.Save("RefreshRate");
      } finally { _loading = false; }
    }

    void RefreshRateNum_ValueChanged(object s, RoutedEventArgs e) {
      if (_loading) return;
      _loading = true;
      try {
        double? val = RefreshRateNum.Value;
        if (val == null || val < 30 || val > 360) return;
        int v = (int)val;
        if (v == ConfigService.RefreshRate) return;
        ConfigService.RefreshRate = v;
        ApplyRefreshRate(v);
        ConfigService.Save("RefreshRate");
        SelectCombo(RefreshRateCombo, v + " Hz");
      } finally { _loading = false; }
    }

    static int GetCurrentRefreshRate() => GetAvailableRefreshRates().DefaultIfEmpty(60).Max();

    static List<int> _cachedRefreshRates;
    static List<int> GetAvailableRefreshRates() {
      if (_cachedRefreshRates != null) return _cachedRefreshRates;
      var seen = new HashSet<int>();
      var rates = new List<int>();
      var deviceName = GetInternalDisplayDeviceName();
      if (deviceName == null) return rates;
      var dm = new NativeMethods_Display.DEVMODE();
      dm.dmSize = (short)Marshal.SizeOf(typeof(NativeMethods_Display.DEVMODE));
      int mode = 0;
      while (NativeMethods_Display.EnumDisplaySettings(deviceName, mode, ref dm)) {
        if (dm.dmPelsWidth == 0 || dm.dmPelsHeight == 0) { mode++; continue; }
        if (seen.Add(dm.dmDisplayFrequency)) rates.Add(dm.dmDisplayFrequency);
        mode++;
      }
      rates.Sort();
      _cachedRefreshRates = rates;
      return rates;
    }

    static void ApplyRefreshRate(int hz) {
      var deviceName = GetInternalDisplayDeviceName();
      if (deviceName == null) { Log("ApplyRefreshRate: deviceName is null"); return; }
      Log($"ApplyRefreshRate: deviceName={deviceName} target={hz}Hz");
      var dm = new NativeMethods_Display.DEVMODE();
      dm.dmSize = (short)Marshal.SizeOf(typeof(NativeMethods_Display.DEVMODE));
      if (!NativeMethods_Display.EnumDisplaySettings(deviceName, NativeMethods_Display.ENUM_CURRENT_SETTINGS, ref dm)) { Log("ApplyRefreshRate: EnumDisplaySettings(ENUM_CURRENT_SETTINGS) failed"); return; }
      if (dm.dmDisplayFrequency == hz) { Log("ApplyRefreshRate: same freq, skipping"); return; }
      dm.dmDisplayFrequency = hz;
      dm.dmFields = NativeMethods_Display.DM_DISPLAYFREQUENCY;
      int result = NativeMethods_Display.ChangeDisplaySettingsEx(deviceName, ref dm, IntPtr.Zero, 0, IntPtr.Zero);
      Log($"ApplyRefreshRate: ChangeDisplaySettingsEx returned {result}");
    }

    // ── 查找内置显示器 ──
    static (NativeMethods_Display.LUID adapterId, uint sourceId, uint targetId) _cachedIds = (default, uint.MaxValue, uint.MaxValue);
    static (NativeMethods_Display.LUID adapterId, uint sourceId, uint targetId) FindInternalDisplayIds() {
      if (_cachedIds.sourceId != uint.MaxValue) return _cachedIds;
      uint[] flagsToTry = { NativeMethods_Display.QDC_ALL_PATHS, NativeMethods_Display.QDC_ONLY_ACTIVE_PATHS, NativeMethods_Display.QDC_DATABASE_CURRENT };
      (NativeMethods_Display.LUID, uint, uint)? firstActive = null;
      foreach (uint flag in flagsToTry) {
        uint pathCount, modeCount;
        if (NativeMethods_Display.GetDisplayConfigBufferSizes(flag, out pathCount, out modeCount) != 0) { Log($"flag={flag}: GetDisplayConfigBufferSizes failed"); continue; }
        if (pathCount == 0) { Log($"flag={flag}: no paths"); continue; }
        var paths = new NativeMethods_Display.DISPLAYCONFIG_PATH_INFO[pathCount];
        var modes = new NativeMethods_Display.DISPLAYCONFIG_MODE_INFO[modeCount];
        int qdcRet = NativeMethods_Display.QueryDisplayConfig(flag, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
        if (qdcRet != 0) {
          int gle = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
          Log($"flag={flag}: QDC failed: {qdcRet} gle={gle} (input pathCount={pathCount} modeCount={modeCount})");
          continue;
        }
        Log($"flag={flag}: QDC OK, {pathCount} paths, {modeCount} modes");
        for (int i = 0; i < pathCount; i++) {
          if ((paths[i].flags & NativeMethods_Display.DISPLAYCONFIG_PATH_ACTIVE) == 0) continue;
          var tgt = new NativeMethods_Display.DISPLAYCONFIG_TARGET_DEVICE_NAME();
          tgt.header.type = NativeMethods_Display.INFO_GET_TARGET_NAME;
          tgt.header.size = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods_Display.DISPLAYCONFIG_TARGET_DEVICE_NAME));
          tgt.header.adapterId = paths[i].targetInfo.adapterId;
          tgt.header.id = paths[i].targetInfo.id;
          // Remember first active path (even if external, even if GetDeviceInfo fails) as fallback
          if (firstActive == null)
            firstActive = (paths[i].sourceInfo.adapterId, paths[i].sourceInfo.id, paths[i].targetInfo.id);
          Log($"GetDeviceName(adapter={paths[i].targetInfo.adapterId.LowPart:X8},{paths[i].targetInfo.adapterId.HighPart:X8} targetId={paths[i].targetInfo.id} sourceId={paths[i].sourceInfo.id})");
          int ret = NativeMethods_Display.DisplayConfigGetDeviceInfo(ref tgt);
          if (ret != 0) { Log($"DisplayConfigGetDeviceInfo(path {i}) failed: {ret}"); continue; }
          bool isInternal = tgt.outputTechnology == NativeMethods_Display.DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.INTERNAL ||
                            tgt.outputTechnology == NativeMethods_Display.DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYPORT_EMBEDDED;
          Log($"path {i}: flag={flag} tech={tgt.outputTechnology} name={tgt.monitorFriendlyDeviceName} internal={isInternal}");
          if (isInternal) {
            _cachedIds = (paths[i].sourceInfo.adapterId, paths[i].sourceInfo.id, paths[i].targetInfo.id);
            return _cachedIds;
          }
        }
      }
      // No internal display found — use the first active path (external monitor) if available
      if (firstActive != null) {
        Log($"FindInternalDisplayIds: no internal display, using first active path (external)");
        _cachedIds = firstActive.Value;
        return _cachedIds;
      }
      _cachedIds = (default, 0, 0); // cache failure too
      // ── Fallback: Screen.AllScreens ──
      Log($"Screen.AllScreens: {System.Windows.Forms.Screen.AllScreens.Length} screens");
      foreach (var sc in System.Windows.Forms.Screen.AllScreens) {
        Log($"  Screen: '{sc.DeviceName}' primary={sc.Primary} bounds=({sc.Bounds.Width},{sc.Bounds.Height}) working=({sc.WorkingArea.Width},{sc.WorkingArea.Height})");
      }
      // ── Fallback: EnumDisplayDevices ──
      Log("EnumDisplayDevices enumeration:");
      for (uint dev = 0; ; dev++) {
        var dd = new NativeMethods_Display.DISPLAY_DEVICE();
        dd.cb = System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods_Display.DISPLAY_DEVICE));
        if (!NativeMethods_Display.EnumDisplayDevices(null, dev, ref dd, 0)) break;
        Log($"  ADAPTER[{dev}]: name='{dd.DeviceName}' str='{dd.DeviceString}' flags={dd.StateFlags:X}");
        for (uint mon = 0; ; mon++) {
          var md = new NativeMethods_Display.DISPLAY_DEVICE();
          md.cb = System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods_Display.DISPLAY_DEVICE));
          if (!NativeMethods_Display.EnumDisplayDevices(dd.DeviceName, mon, ref md, 0)) break;
          Log($"    MONITOR[{mon}]: name='{md.DeviceName}' str='{md.DeviceString}' flags={md.StateFlags:X}");
          // Check if this monitor is the built-in panel
          string monStr = md.DeviceString ?? "";
          // Common internal display identifiers in DeviceString
          if (monStr.IndexOf("LCD", StringComparison.OrdinalIgnoreCase) >= 0 ||
              monStr.IndexOf("eDP", StringComparison.OrdinalIgnoreCase) >= 0 ||
              monStr.IndexOf("Embedded", StringComparison.OrdinalIgnoreCase) >= 0 ||
              monStr.IndexOf("Built-in", StringComparison.OrdinalIgnoreCase) >= 0 ||
              monStr.IndexOf("Internal", StringComparison.OrdinalIgnoreCase) >= 0 ||
              monStr.IndexOf("Laptop", StringComparison.OrdinalIgnoreCase) >= 0) {
            Log($"    → matched as internal display via DeviceString '{monStr}'");
            _cachedDeviceName = md.DeviceName;
            return (default, 0, 0);
          }
          // Also check the adapter DeviceString for eDP (common for laptop panels)
          string adStr = dd.DeviceString ?? "";
          if (mon == 0 && adStr.IndexOf("eDP", StringComparison.OrdinalIgnoreCase) >= 0) {
            Log($"    → matched as internal display via adapter DeviceString '{adStr}'");
            _cachedDeviceName = md.DeviceName;
            return (default, 0, 0);
          }
        }
      }
      // ── Last resort: Screen.PrimaryScreen ──
      Log("FindInternalDisplayIds: all fallbacks failed, will use PrimaryScreen");
      _cachedDeviceName = System.Windows.Forms.Screen.PrimaryScreen?.DeviceName;
      return (default, 0, 0);
    }

    // ── 屏幕分辨率 ──
    static string _cachedDeviceName;
    static string GetInternalDisplayDeviceName() {
      if (_cachedDeviceName != null) return _cachedDeviceName;
      var ids = FindInternalDisplayIds();
      if (_cachedDeviceName != null) return _cachedDeviceName; // EnumDisplayDevices fallback may have set it
      if (ids.adapterId.LowPart == 0 && ids.adapterId.HighPart == 0 && ids.sourceId == 0) {
        string fallback = System.Windows.Forms.Screen.PrimaryScreen?.DeviceName;
        Log($"GetInternalDisplayDeviceName: no source ID, fallback to PrimaryScreen: {fallback}");
        _cachedDeviceName = fallback;
        return fallback;
      }
      var info = new NativeMethods_Display.DISPLAYCONFIG_SOURCE_DEVICE_NAME();
      info.header.type = NativeMethods_Display.INFO_GET_SOURCE_NAME;
      info.header.size = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods_Display.DISPLAYCONFIG_SOURCE_DEVICE_NAME));
      info.header.adapterId = ids.adapterId;
      info.header.id = ids.sourceId;
      int ret = NativeMethods_Display.DisplayConfigGetDeviceInfoEx(ref info);
      if (ret != 0) {
        string fallback = System.Windows.Forms.Screen.PrimaryScreen?.DeviceName;
        Log($"GetInternalDisplayDeviceName: DisplayConfigGetDeviceInfoEx failed: {ret}, fallback: {fallback}");
        return fallback;
      }
      _cachedDeviceName = info.viewGdiDeviceName;
      Log($"GetInternalDisplayDeviceName (from DisplayConfig): {_cachedDeviceName} ids=({ids.adapterId.LowPart:X8},{ids.adapterId.HighPart:X8}:{ids.sourceId})");
      return _cachedDeviceName;
    }

    static List<(int w, int h)> GetAvailableResolutions() {
      var deviceName = GetInternalDisplayDeviceName();
      if (deviceName == null) return new List<(int w, int h)>();
      uint curFreq = (uint)GetCurrentRefreshRate();
      var seen = new HashSet<string>();
      var result = new List<(int w, int h)>();
      var dm = new NativeMethods_Display.DEVMODE();
      dm.dmSize = (short)System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods_Display.DEVMODE));
      int modeIdx = 0;
      while (NativeMethods_Display.EnumDisplaySettings(deviceName, modeIdx, ref dm)) {
        if (dm.dmPelsWidth > 0 && dm.dmPelsHeight > 0 && dm.dmDisplayFrequency == curFreq) {
          int mx = Math.Max(dm.dmPelsWidth, dm.dmPelsHeight);
          // ponytail: filter tiny resolutions (< 1000px on the long edge)
          if (mx < 1000) { modeIdx++; continue; }
          string key = dm.dmPelsWidth + "x" + dm.dmPelsHeight;
          if (seen.Add(key)) result.Add((dm.dmPelsWidth, dm.dmPelsHeight));
        }
        modeIdx++;
      }
      result.Sort((a, b) => b.w.CompareTo(a.w));
      return result;
    }

    // ponytail: same pattern as ApplyRefreshRate — start from current DEVMODE, change only resolution.
    // CDS_UPDATEREGISTRY persists the change to the registry so it survives reboots and stays
    // in sync with Windows Settings — otherwise Windows' own CDS_UPDATEREGISTRY write wins
    // once the user changes resolution there, and a second switch from here is silently ignored.
    internal static void ApplyResolution(int w, int h) {
      var deviceName = GetInternalDisplayDeviceName();
      if (deviceName == null) { Log("ApplyResolution: deviceName is null"); return; }
      Log($"ApplyResolution: deviceName={deviceName} target={w}x{h}");
      var dm = new NativeMethods_Display.DEVMODE();
      dm.dmSize = (short)System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods_Display.DEVMODE));
      if (!NativeMethods_Display.EnumDisplaySettings(deviceName, NativeMethods_Display.ENUM_CURRENT_SETTINGS, ref dm))
      { Log("ApplyResolution: EnumDisplaySettings(ENUM_CURRENT_SETTINGS) failed"); return; }
      dm.dmPelsWidth = w;
      dm.dmPelsHeight = h;
      dm.dmFields = NativeMethods_Display.DM_PELSWIDTH | NativeMethods_Display.DM_PELSHEIGHT;
      int result = NativeMethods_Display.ChangeDisplaySettingsEx(deviceName, ref dm, IntPtr.Zero, NativeMethods_Display.CDS_UPDATEREGISTRY, IntPtr.Zero);
      Log($"ApplyResolution: ChangeDisplaySettingsEx returned {result} (0=OK)");
    }

    void BuildResolutionOptions() {
      ResolutionCombo.Items.Clear();
      // Confirm, then wipe registry/tasks/config/data and restart self.
            var res = GetAvailableResolutions();
      foreach (var r in res)
        ResolutionCombo.Items.Add(new ComboBoxItem { Content = $"{r.w} × {r.h}", Tag = r.w + "x" + r.h });
      if (!string.IsNullOrEmpty(ConfigService.Resolution)) {
        var parts = ConfigService.Resolution.Split('x');
        if (parts.Length == 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h))
          SelectCombo(ResolutionCombo, $"{w} × {h}");
      }
      if (ResolutionCombo.SelectedIndex < 0) {
        var deviceName = GetInternalDisplayDeviceName();
        if (deviceName != null) {
          var dm = new NativeMethods_Display.DEVMODE();
          dm.dmSize = (short)System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods_Display.DEVMODE));
          if (NativeMethods_Display.EnumDisplaySettings(deviceName, NativeMethods_Display.ENUM_CURRENT_SETTINGS, ref dm))
            SelectCombo(ResolutionCombo, $"{dm.dmPelsWidth} × {dm.dmPelsHeight}");
        }
      }
    }

    void Resolution_SelectionChanged(object sender, SelectionChangedEventArgs e) {
      if (_loading) return;
      var item = ResolutionCombo.SelectedItem as ComboBoxItem;
      if (item == null) return;
      string tag = (string)item.Tag;
      var parts = tag.Split('x');
      if (parts.Length == 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h)) {
        _cachedDeviceName = null; // force refresh for the target display
        ApplyResolution(w, h);
        ConfigService.Resolution = tag;
        ConfigService.Save("Resolution");
      }
    }

    // ── DPI 缩放 ──
    // ponytail: DPI API uses values relative to recommended (reverse-engineered, see SetDPI GitHub)
    static readonly int[] DpiScaleValues = { 100, 125, 140, 150, 175, 200, 225, 250, 300, 350, 400, 450, 500 };

    static int GetGdiDpi() {
      var deviceName = GetInternalDisplayDeviceName();
      if (deviceName == null) return 96;
      IntPtr dc = NativeMethods_Display.CreateDC(deviceName, null, null, IntPtr.Zero);
      if (dc == IntPtr.Zero) return 96;
      int dpiX = NativeMethods_Display.GetDeviceCaps(dc, NativeMethods_Display.LOGPIXELSX);
      NativeMethods_Display.DeleteDC(dc);
      return dpiX;
    }

    static (int cur, int max, int recommended) GetDpiScaleInfo() {
      var ids = FindInternalDisplayIds();
      if (ids.adapterId.LowPart != 0 || ids.adapterId.HighPart != 0 || ids.sourceId != 0) {
        var info = new NativeMethods_Display.DISPLAYCONFIG_SOURCE_DPI_SCALE_GET();
        info.header.type = NativeMethods_Display.INFO_GET_DPI_SCALE;
        info.header.size = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods_Display.DISPLAYCONFIG_SOURCE_DPI_SCALE_GET));
        info.header.adapterId = ids.adapterId;
        info.header.id = ids.sourceId;
        int ret = NativeMethods_Display.DisplayConfigGetDeviceInfoEx(ref info);
        if (ret == 0) {
          Log($"GetDpiScaleInfo: minRel={info.minScaleRel} curRel={info.curScaleRel} maxRel={info.maxScaleRel}");
          int minAbs = Math.Abs(info.minScaleRel);
          int recIdx = minAbs;
          int curIdx = Math.Max(0, Math.Min(minAbs + info.curScaleRel, DpiScaleValues.Length - 1));
          int maxIdx = Math.Max(0, Math.Min(minAbs + info.maxScaleRel, DpiScaleValues.Length - 1));
          recIdx = Math.Max(0, Math.Min(recIdx, DpiScaleValues.Length - 1));
          var result = (cur: DpiScaleValues[curIdx], max: DpiScaleValues[maxIdx], recommended: DpiScaleValues[recIdx]);
          Log($"GetDpiScaleInfo: cur={result.cur} max={result.max} rec={result.recommended}");
          return result;
        }
        Log($"GetDpiScaleInfo: DisplayConfigGetDeviceInfoEx failed: {ret}, falling back to GDI");
      }
      // GDI fallback
      int gdiDpi = GetGdiDpi();
      int pct = (int)Math.Round((double)gdiDpi / 96.0 * 100.0);
      // round to nearest DpiScaleValues entry
      int closest = DpiScaleValues.OrderBy(v => Math.Abs(v - pct)).First();
      Log($"GetDpiScaleInfo: GDI fallback gdiDpi={gdiDpi} pct={pct} closest={closest}");
      return (closest, DpiScaleValues.Last(), DpiScaleValues[0]);
    }

    static void ApplyDpiScale(int percent) {
      var ids = FindInternalDisplayIds();
      if (ids.adapterId.LowPart == 0 && ids.adapterId.HighPart == 0 && ids.sourceId == 0) { Log("ApplyDpiScale: no source ID"); return; }
      var (_, _, recommended) = GetDpiScaleInfo();
      int recIdx = Array.IndexOf(DpiScaleValues, recommended);
      int targetIdx = Array.IndexOf(DpiScaleValues, percent);
      if (recIdx < 0 || targetIdx < 0) { Log($"ApplyDpiScale: index lookup failed recIdx={recIdx} targetIdx={targetIdx}"); return; }
      int relVal = targetIdx - recIdx;
      Log($"ApplyDpiScale: target={percent}% recommended={recommended}% relVal={relVal}");
      var info = new NativeMethods_Display.DISPLAYCONFIG_SOURCE_DPI_SCALE_SET();
      info.header.type = NativeMethods_Display.INFO_SET_DPI_SCALE;
      info.header.size = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods_Display.DISPLAYCONFIG_SOURCE_DPI_SCALE_SET));
      info.header.adapterId = ids.adapterId;
      info.header.id = ids.sourceId;
      info.scaleRel = relVal;
      int ret = NativeMethods_Display.DisplayConfigSetDeviceInfo(ref info);
      Log($"ApplyDpiScale: DisplayConfigSetDeviceInfo returned {ret} (0 = success)");
    }

    void BuildDpiOptions() {
      DpiCombo.Items.Clear();
      var (_, maxScale, _) = GetDpiScaleInfo();
      foreach (int s in DpiScaleValues) {
        if (s > maxScale) break;
        DpiCombo.Items.Add(new ComboBoxItem { Content = s + "%", Tag = s });
      }
      int target = ConfigService.DpiScale > 0 ? ConfigService.DpiScale : GetDpiScaleInfo().cur;
      SelectCombo(DpiCombo, target + "%");
      if (DpiCombo.SelectedIndex < 0) SelectCombo(DpiCombo, "100%");
    }

    void DpiCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) {
      if (_loading) return;
      var item = DpiCombo.SelectedItem as ComboBoxItem;
      if (item == null) return;
      int scale = (int)item.Tag;
      var (cur, _, _) = GetDpiScaleInfo();
      if (scale == cur) return;
      ApplyDpiScale(scale);
      ConfigService.DpiScale = scale;
      ConfigService.Save("DpiScale");
    }

    // ── HDR ──
    (bool supported, bool enabled, bool forceDisabled) GetHdrInfo() {
      var ids = FindInternalDisplayIds();
      if (ids.targetId == 0) return (false, false, false);
      var info = new NativeMethods_Display.DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO();
      info.header.type = NativeMethods_Display.INFO_GET_ADVANCED_COLOR;
      info.header.size = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods_Display.DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO));
      info.header.adapterId = ids.adapterId;
      info.header.id = ids.targetId;
      if (NativeMethods_Display.DisplayConfigGetDeviceInfoEx(ref info) != 0)
        return (false, false, false);
      // bit 0 = AdvancedColorSupported, bit 1 = AdvancedColorEnabled, bit 2 = AdvancedColorForceDisabled
      return ((info.value & 1) != 0, (info.value & 2) != 0, (info.value & 4) != 0);
    }

    void SetHdrEnabled(bool enabled) {
      var ids = FindInternalDisplayIds();
      if (ids.targetId == 0) return;
      var info = new NativeMethods_Display.DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE();
      info.header.type = NativeMethods_Display.INFO_SET_ADVANCED_COLOR;
      info.header.size = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods_Display.DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE));
      info.header.adapterId = ids.adapterId;
      info.header.id = ids.targetId;
      info.value = (uint)(enabled ? 1 : 0);
      NativeMethods_Display.DisplayConfigSetDeviceInfo(ref info);
    }

    void InitHdr() {
      var hdr = GetHdrInfo();
      HdrCard.Visibility = hdr.supported ? Visibility.Visible : Visibility.Collapsed;
      HdrToggle.IsEnabled = !hdr.forceDisabled;
      HdrToggle.IsChecked = ConfigService.HdrEnabled;
      // If saved HDR preference exists and differs from actual system state, apply on load
      if (hdr.supported && !hdr.forceDisabled && ConfigService.HdrEnabled != hdr.enabled)
        SetHdrEnabled(ConfigService.HdrEnabled);
    }

    void HdrToggle_Click(object sender, RoutedEventArgs e) {
      if (_loading) return;
      bool enabled = HdrToggle.IsChecked == true;
      SetHdrEnabled(enabled);
      ConfigService.HdrEnabled = enabled;
      ConfigService.Save("HdrEnabled");
    }

    // ── 关闭显示器 ──
    void TurnOffDisplay_Click(object sender, RoutedEventArgs e) {
      var src = PresentationSource.FromVisual(this) as System.Windows.Interop.HwndSource;
      if (src != null)
        NativeMethods_Display.SendMessage(src.Handle, NativeMethods_Display.WM_SYSCOMMAND,
          (IntPtr)NativeMethods_Display.SC_MONITORPOWER, (IntPtr)2);
    }

    void PowerPlanSettings_Click(object sender, RoutedEventArgs e) {
      try { System.Diagnostics.Process.Start("control.exe", "powercfg.cpl"); } catch { }
    }

    bool _perfExpanded = true;
    const double PerfCollapseWidth = 1000;

    void PerfPage_SizeChanged(object sender, SizeChangedEventArgs e) {
      if (!e.WidthChanged) return;
      if (e.NewSize.Width > PerfCollapseWidth) {
        if (!_perfExpanded) { _perfExpanded = true; ExpandPerfGrids(); }
      } else {
        if (_perfExpanded) { _perfExpanded = false; CollapsePerfGrids(); }
      }
    }

    void ExpandPerfGrids() {
      LayoutPerfGrid(CpuPerfGrid, expand: true);
      LayoutPerfGrid(GpuPerfGrid, expand: true);
    }

    void CollapsePerfGrids() {
      LayoutPerfGrid(CpuPerfGrid, expand: false);
      LayoutPerfGrid(GpuPerfGrid, expand: false);
    }

    // ponytail: detect full-width by runtime Grid.ColumnSpan instead of name matching.
    // Any card with ColumnSpan >= 2 gets the full-row treatment — no name list to maintain.
    static bool IsFullWidthCard(FrameworkElement c) =>
      Grid.GetColumnSpan(c) >= 2;

    // ponytail: set of cards whose XAML ColumnSpan is 2.  This survives layout
    // toggling — after a collapsed→expand round-trip all runtime ColumnSpan
    // values are 2 and we'd lose the regular/fullWidth distinction without it.
    static readonly HashSet<string> _fwNames = new() {
      "AmdCpuPowerCard"
    };
    /// <summary>Reset runtime ColumnSpan to XAML default (1) so the next
    /// categorization round starts clean.  Only cards whose name appears in
    /// _fwNames (true XAML full-width cards) are left alone — regular cards
    /// that got ColumnSpan=2 during a previous collapsed layout are reset.</summary>
    void ResetColumnSpans(Grid grid) {
      for (int i = 0; i < grid.Children.Count; i++) {
        if (grid.Children[i] is not FrameworkElement c) continue;
        // ponytail: DO NOT guard on runtime Grid.GetColumnSpan(c) >= 2 here.
        // After a collapsed pass ALL regular cards have ColumnSpan=2, so the
        // guard would skip them and IsFullWidthCard mis-classifies everything.
        if (c.Name != null && _fwNames.Contains(c.Name)) continue;
        Grid.SetColumnSpan(c, 1);
      }
    }

    void LayoutPerfGrid(Grid grid, bool expand) {
      int childCount = grid.Children.Count;
      if (childCount == 0) return;
      // Reset regular cards to ColumnSpan=1 before categorising, so a
      // previous collapsed→expand round-trip doesn't trick IsFullWidthCard.
      ResetColumnSpans(grid);
      grid.ColumnDefinitions[1].Width = expand
        ? new GridLength(1, GridUnitType.Star)
        : new GridLength(0, GridUnitType.Pixel);

      var fullWidth = new List<FrameworkElement>();
      var regular = new List<FrameworkElement>();
      for (int i = 0; i < childCount; i++) {
        if (grid.Children[i] is FrameworkElement c && c.Visibility == Visibility.Visible) {
          if (IsFullWidthCard(c)) fullWidth.Add(c);
          else regular.Add(c);
        }
      }

      int row = 0;
      foreach (var c in fullWidth) {
        Grid.SetRow(c, row); Grid.SetColumn(c, 0); Grid.SetColumnSpan(c, 2);
        c.Margin = new Thickness(0, 0, 0, 8);
        row++;
      }

      if (expand) {
        for (int i = 0; i < regular.Count; i++) {
          int col = i % 2;
          var c = regular[i];
          Grid.SetRow(c, row + i / 2); Grid.SetColumn(c, col); Grid.SetColumnSpan(c, 1);
          c.Margin = new Thickness(col == 1 ? 4 : 0, 0, col == 1 ? 0 : 4, 8);
        }
      } else {
        // ponytail: collapsed — each regular card spans full width to avoid
        // Column/ColumnSpan ambiguity. No second-column layout math to drift.
        foreach (var c in regular) {
          Grid.SetRow(c, row); Grid.SetColumn(c, 0); Grid.SetColumnSpan(c, 2);
          c.Margin = new Thickness(0, 0, 0, 8);
          row++;
        }
      }

      grid.InvalidateMeasure();
      grid.InvalidateArrange();
    }

    bool _hasAmdCpu => OmenHardware.HasAmdCpu();
    bool _hasIntelCpu => OmenHardware.HasIntelCpu();
    bool _hasNvidiaGpu => GpuAppManager.HasNvidiaGpu();
    bool _hasAmdGpu => OmenHardware.HasAmdGpu();

    // ════════════════════════════════════════════════════════════
    // Hetero CPU (AMD dual-CCD simulated hybrid scheduling)
    // ════════════════════════════════════════════════════════════
    static readonly int[] HeteroPolicyValues = { 0, 1, 2, 3, 4, 5, 6, 7 };
    static readonly int[] HeteroMaskValues = { 1, 2, 3, 4, 5, 6, 7 };

    void InitHeteroCpu() {
      HeteroCpuMaskBox.Text = ConfigService.HeteroCpuSmallMask;
      HeteroCpuRuntimeBox.Text = ConfigService.HeteroCpuExpectedRuntime.ToString();
      HeteroCpuPriorityBox.Text = ConfigService.HeteroCpuImportantPriority.ToString();
      RefreshHeteroLabels();
      // Restore saved selections
      SelectPolicy(HeteroCpuDefaultPolicyCombo, ConfigService.HeteroCpuDefaultPolicy, HeteroPolicyValues);
      SelectPolicy(HeteroCpuImportantPolicyCombo, ConfigService.HeteroCpuImportantPolicy, HeteroPolicyValues);
      SelectPolicy(HeteroCpuImportantShortCombo, ConfigService.HeteroCpuImportantShortPolicy, HeteroPolicyValues);
      SelectPolicy(HeteroCpuPolicyMaskCombo, ConfigService.HeteroCpuPolicyMask, HeteroMaskValues);
      // Load toggle state
      HeteroCpuToggle.IsChecked = HeteroCpuService.IsActive();
      HeteroCpuDetails.Visibility = HeteroCpuService.IsActive() ? Visibility.Visible : Visibility.Collapsed;
      // ponytail: register lang change once
      Strings.OnLanguageChanged -= RefreshHeteroLabels;
      Strings.OnLanguageChanged += RefreshHeteroLabels;
    }

    public void ApplyHardwareVisibility() {
      // ── CpuPerfGrid per-vendor visibility (always applies) ──
      bool hasAmd = _hasAmdCpu;
      bool hasIntel = _hasIntelCpu;
      // DebugShowAllUi: 强制显示所有卡片（忽略 per-vendor 限制）
      if (ConfigService.DebugShowAllUi) {
        hasAmd = true;
        hasIntel = true;
      }
      // ponytail: 机型能力据 SystemDesignData + platformSettings 决定 IccMax/ACLL 卡是否出现，
      // 而非仅按 CPU 厂商。DebugShowAllUi 仍强显以利排错。
      bool iccMaxCapable, acLlCapable;
      try { iccMaxCapable = IsIccMaxSupported(); } catch { iccMaxCapable = false; }
      try { acLlCapable = IsLoadLineSupported(); } catch { acLlCapable = false; }
      if (!ConfigService.DebugShowAllUi) {
        IccMaxCard.Visibility   = (hasIntel && iccMaxCapable) ? Visibility.Visible : Visibility.Collapsed;
        AcLoadLineCard.Visibility = (hasIntel && acLlCapable)  ? Visibility.Visible : Visibility.Collapsed;
      } else {
        IccMaxCard.Visibility   = Visibility.Visible;
        AcLoadLineCard.Visibility = Visibility.Visible;
      }
      HeteroCpuCard.Visibility = hasAmd ? Visibility.Visible : Visibility.Collapsed;

      // ponytail: AMD 基础卡 — PPT 走 WMI 始终可用。TDC/EDC/Tctl 三组控件已随高级调教删除。
      AmdCpuPowerCard.Visibility = hasAmd ? Visibility.Visible : Visibility.Collapsed;
      CpuPowerCard.Visibility = hasIntel && !hasAmd ? Visibility.Visible : Visibility.Collapsed;
      // ponytail: AMD 降压卡 — 仅需 PawnIO 驱动 + 受支持家族,不依赖 EnableEcAccess 开关。
      // 首次访问 AmdUndervoltService.Instance 会触发 WMI CPU 检测(~100ms,后续缓存)。
      bool uvCapable = hasAmd;
      if (uvCapable && !ConfigService.DebugShowAllUi) {
        try { uvCapable = Services.AmdUndervoltService.Instance.IsAvailable; } catch { uvCapable = false; }
      }
      AmdUndervoltCard.Visibility = uvCapable ? Visibility.Visible : Visibility.Collapsed;
      // Intel XTU 超频卡 — 仅 Intel 机型可见(经 XTU3SERVICE 服务控制每核倍频/电压偏移)
      CpuOcCard.Visibility = hasIntel ? Visibility.Visible : Visibility.Collapsed;
    }

    void RefreshHeteroLabels() {
      // ponytail: do NOT toggle _loading — called from InitHeteroCpu
      // which is already inside a _loading=true block from BeginInvoke.
      HeteroCpuDefaultPolicyCombo.ItemsSource = null;
      HeteroCpuDefaultPolicyCombo.ItemsSource = new[] {
          Strings.HeteroPolicyAny, Strings.HeteroPolicyBig, Strings.HeteroPolicyBigOrIdle,
          Strings.HeteroPolicySmall, Strings.HeteroPolicySmallOrIdle, Strings.HeteroPolicyAuto,
          Strings.HeteroPolicyPreferSmall, Strings.HeteroPolicyPreferBig
      };
      HeteroCpuImportantPolicyCombo.ItemsSource = null;
      HeteroCpuImportantPolicyCombo.ItemsSource = new[] {
          Strings.HeteroPolicyAny, Strings.HeteroPolicyBig, Strings.HeteroPolicyBigOrIdle,
          Strings.HeteroPolicySmall, Strings.HeteroPolicySmallOrIdle, Strings.HeteroPolicyAuto,
          Strings.HeteroPolicyPreferSmall, Strings.HeteroPolicyPreferBig
      };
      HeteroCpuImportantShortCombo.ItemsSource = null;
      HeteroCpuImportantShortCombo.ItemsSource = new[] {
          Strings.HeteroPolicyAny, Strings.HeteroPolicyBig, Strings.HeteroPolicyBigOrIdle,
          Strings.HeteroPolicySmall, Strings.HeteroPolicySmallOrIdle, Strings.HeteroPolicyAuto,
          Strings.HeteroPolicyPreferSmall, Strings.HeteroPolicyPreferBig
      };
      HeteroCpuPolicyMaskCombo.ItemsSource = null;
      HeteroCpuPolicyMaskCombo.ItemsSource = new[] {
          Strings.HeteroMaskForeground, Strings.HeteroMaskPriority, Strings.HeteroMaskFgPriority,
          Strings.HeteroMaskRuntime, Strings.HeteroMaskFgRuntime, Strings.HeteroMaskPriRuntime,
          Strings.HeteroMaskAll
      };
      SelectPolicy(HeteroCpuDefaultPolicyCombo, ConfigService.HeteroCpuDefaultPolicy, HeteroPolicyValues);
      SelectPolicy(HeteroCpuImportantPolicyCombo, ConfigService.HeteroCpuImportantPolicy, HeteroPolicyValues);
      SelectPolicy(HeteroCpuImportantShortCombo, ConfigService.HeteroCpuImportantShortPolicy, HeteroPolicyValues);
      SelectPolicy(HeteroCpuPolicyMaskCombo, ConfigService.HeteroCpuPolicyMask, HeteroMaskValues);
    }

    static void SelectPolicy(ComboBox cb, int val, int[] values) {
      int idx = Array.IndexOf(values, val);
      if (idx >= 0 && idx < (cb.ItemsSource as string[])?.Length)
        cb.SelectedIndex = idx;
    }

    void HeteroCpuToggle_Changed(object sender, RoutedEventArgs e) {
      if (_loading) return;
      bool enable = HeteroCpuToggle.IsChecked == true;
      HeteroCpuDetails.Visibility = enable ? Visibility.Visible : Visibility.Collapsed;
	      if (enable) {
	        HeteroCpuService.WriteSmallProcessorMask(ConfigService.HeteroCpuSmallMask);
	        HeteroCpuService.WriteDefaultPolicy(ConfigService.HeteroCpuDefaultPolicy);
	        HeteroCpuService.WriteExpectedRuntime(ConfigService.HeteroCpuExpectedRuntime);
	        HeteroCpuService.WriteImportantPolicy(ConfigService.HeteroCpuImportantPolicy);
	        HeteroCpuService.WriteImportantShortPolicy(ConfigService.HeteroCpuImportantShortPolicy);
	        HeteroCpuService.WritePolicyMask(ConfigService.HeteroCpuPolicyMask);
	        HeteroCpuService.WriteImportantPriority(ConfigService.HeteroCpuImportantPriority);
	        // Also write per-power-plan hetero scheduling settings to active plan
	        if (GetActiveSchemeGuid() is Guid activeScheme) {
	          WritePwrValueBoth(activeScheme, GUID_HETERO_THREAD_SCHED, 2, 5);
	          WritePwrValueBoth(activeScheme, GUID_HETERO_SHORT_SCHED, 2, 5);
	          WritePwrValueBoth(activeScheme, GUID_HETERO_ACTIVE_POLICY, 0, 4);
	        }
	      } else {
	        HeteroCpuService.RemoveAll();
	        // Reset per-power-plan hetero settings to default (auto)
	        if (GetActiveSchemeGuid() is Guid activeScheme) {
	          WritePwrValueBoth(activeScheme, GUID_HETERO_THREAD_SCHED, 5, 5);
	          WritePwrValueBoth(activeScheme, GUID_HETERO_SHORT_SCHED, 5, 5);
	          WritePwrValueBoth(activeScheme, GUID_HETERO_ACTIVE_POLICY, 4, 4);
	        }
	      }
    }

    void HeteroCpuMask_TextChanged(object sender, TextChangedEventArgs e) {
      if (_loading) return;
      ConfigService.HeteroCpuSmallMask = HeteroCpuMaskBox.Text.Trim();
      ConfigService.Save("HeteroCpuSmallMask");
    }

    void HeteroCpuPolicy_Changed(object sender, SelectionChangedEventArgs e) {
      if (_loading) return;
      var combo = sender as ComboBox;
      if (combo == null || combo.SelectedIndex < 0) return;
      int val = HeteroPolicyValues[combo.SelectedIndex];
      string key = null;
      if (combo == HeteroCpuDefaultPolicyCombo) key = "HeteroCpuDefaultPolicy";
      else if (combo == HeteroCpuImportantPolicyCombo) key = "HeteroCpuImportantPolicy";
      else if (combo == HeteroCpuImportantShortCombo) key = "HeteroCpuImportantShortPolicy";
      else if (combo == HeteroCpuPolicyMaskCombo) { val = HeteroMaskValues[combo.SelectedIndex]; key = "HeteroCpuPolicyMask"; }
      if (key == null) return;
      var field = typeof(ConfigService).GetField(key);
      if (field != null) field.SetValue(null, val);
      ConfigService.Save(key);
    }

    void HeteroCpuRuntime_Changed(object sender, TextChangedEventArgs e) {
      if (_loading) return;
      if (int.TryParse(HeteroCpuRuntimeBox.Text, out var val)) {
        ConfigService.HeteroCpuExpectedRuntime = val;
        ConfigService.Save("HeteroCpuExpectedRuntime");
      }
    }

    void HeteroCpuPriority_Changed(object sender, TextChangedEventArgs e) {
      if (_loading) return;
      if (int.TryParse(HeteroCpuPriorityBox.Text, out var val)) {
        ConfigService.HeteroCpuImportantPriority = val;
        ConfigService.Save("HeteroCpuImportantPriority");
      }
    }

    void HeteroCpuApply_Click(object sender, RoutedEventArgs e) {
      if (HeteroCpuToggle.IsChecked != true) return;
      HeteroCpuService.WriteSmallProcessorMask(ConfigService.HeteroCpuSmallMask);
      HeteroCpuService.WriteDefaultPolicy(ConfigService.HeteroCpuDefaultPolicy);
      HeteroCpuService.WriteExpectedRuntime(ConfigService.HeteroCpuExpectedRuntime);
      HeteroCpuService.WriteImportantPolicy(ConfigService.HeteroCpuImportantPolicy);
      HeteroCpuService.WriteImportantShortPolicy(ConfigService.HeteroCpuImportantShortPolicy);
      HeteroCpuService.WritePolicyMask(ConfigService.HeteroCpuPolicyMask);
	      HeteroCpuService.WriteImportantPriority(ConfigService.HeteroCpuImportantPriority);
	      // Also write per-plan settings
	      if (GetActiveSchemeGuid() is Guid activeScheme) {
	        WritePwrValueBoth(activeScheme, GUID_HETERO_THREAD_SCHED, 2, 5);
	        WritePwrValueBoth(activeScheme, GUID_HETERO_SHORT_SCHED, 2, 5);
	        WritePwrValueBoth(activeScheme, GUID_HETERO_ACTIVE_POLICY, 0, 4);
	      }
	      DialogHelper.Info(Strings.HeteroCpuApplyResult, Strings.HeteroCpuApplyTitle);
    }

    void HeteroCpuRestore_Click(object sender, RoutedEventArgs e) {
      HeteroCpuService.RemoveAll();
      HeteroCpuToggle.IsChecked = false;
      HeteroCpuDetails.Visibility = Visibility.Collapsed;
      ConfigService.HeteroCpuSmallMask = "FFFF0000";
      ConfigService.HeteroCpuDefaultPolicy = 2;
      ConfigService.HeteroCpuExpectedRuntime = 1450;
      ConfigService.HeteroCpuImportantPolicy = 2;
      ConfigService.HeteroCpuImportantShortPolicy = 3;
      ConfigService.HeteroCpuPolicyMask = 7;
      ConfigService.HeteroCpuImportantPriority = 8;
      ConfigService.Save("HeteroCpuSmallMask");
      ConfigService.Save("HeteroCpuDefaultPolicy");
      ConfigService.Save("HeteroCpuExpectedRuntime");
      ConfigService.Save("HeteroCpuImportantPolicy");
      ConfigService.Save("HeteroCpuImportantShortPolicy");
      ConfigService.Save("HeteroCpuPolicyMask");
      ConfigService.Save("HeteroCpuImportantPriority");
      DialogHelper.Info(Strings.HeteroCpuRestoreResult, Strings.HeteroCpuRestoreTitle);
    }

    void HeteroCpuDetect_Click(object sender, RoutedEventArgs e) {
      var (supported, totalLp, ccd0Lp, maskHex) = HeteroCpuService.DetectDualCcd();
      if (!supported) {
        DialogHelper.Info(Strings.HeteroCpuNotDetected, Strings.HeteroCpuDetectTitle);
        return;
      }
      string msg = Strings.HeteroCpuDetectConfirm(totalLp.ToString(), ccd0Lp.ToString(), (totalLp - ccd0Lp).ToString(), maskHex);
      if (DialogHelper.Confirm(msg, Strings.HeteroCpuDetectTitle)) {
        HeteroCpuMaskBox.Text = maskHex;
        ConfigService.HeteroCpuSmallMask = maskHex;
        ConfigService.Save("HeteroCpuSmallMask");
        ConfigService.HeteroCpuDefaultPolicy = 2;
        ConfigService.HeteroCpuExpectedRuntime = 1450;
        ConfigService.HeteroCpuImportantPolicy = 2;
        ConfigService.HeteroCpuImportantShortPolicy = 3;
        ConfigService.HeteroCpuPolicyMask = 7;
        ConfigService.HeteroCpuImportantPriority = 8;
        ConfigService.Save("HeteroCpuDefaultPolicy");
        ConfigService.Save("HeteroCpuExpectedRuntime");
        ConfigService.Save("HeteroCpuImportantPolicy");
        ConfigService.Save("HeteroCpuImportantShortPolicy");
        ConfigService.Save("HeteroCpuPolicyMask");
        ConfigService.Save("HeteroCpuImportantPriority");
        _loading = true;
        SelectPolicy(HeteroCpuDefaultPolicyCombo, 2, HeteroPolicyValues);
        SelectPolicy(HeteroCpuImportantPolicyCombo, 2, HeteroPolicyValues);
        SelectPolicy(HeteroCpuImportantShortCombo, 3, HeteroPolicyValues);
        SelectPolicy(HeteroCpuPolicyMaskCombo, 7, HeteroMaskValues);
        HeteroCpuRuntimeBox.Text = "1450";
        HeteroCpuPriorityBox.Text = "8";
        _loading = false;
        HeteroCpuService.WriteSmallProcessorMask(maskHex);
        HeteroCpuService.WriteDefaultPolicy(2);
        HeteroCpuService.WriteExpectedRuntime(1450);
        HeteroCpuService.WriteImportantPolicy(2);
        HeteroCpuService.WriteImportantShortPolicy(3);
        HeteroCpuService.WritePolicyMask(7);
        HeteroCpuService.WriteImportantPriority(8);
        HeteroCpuToggle.IsChecked = true;
        HeteroCpuDetails.Visibility = Visibility.Visible;
        DialogHelper.Info(Strings.HeteroCpuDetectResult, Strings.HeteroCpuDetectTitle);
      }
    }

    // ── AMD APU Power Tuning (STAPM / Fast / Slow PPT) ──

    // ── AMD CPU Power Limits (PPT / TDC / EDC) ──
    void AmdCpuPptNum_ValueChanged(object s, RoutedEventArgs e) {
      if (_loading) return;
      double? v = AmdCpuPptNum.Value; if (v == null) return;
      int watts = (int)v;
      ConfigService.AmdCpuPpt = watts; ConfigService.Save("AmdCpuPpt");
      // ponytail: PPT 走 WMI（SMU 兜底已随高级调教删除；本机不可用）
      bool pptOk = watts <= 255 && SetCpuPowerLimit((byte)watts);
      AmdCpuPowerStatus.Text = pptOk ? $"PPT={watts}W ✓" : "WMI 写入失败";
    }

    // ── AMD Curve Optimizer 全核降压 (SMU 直写) ──
    // ponytail: SMU 写走 Global\Access_PCI mutex,可能因 LHM 持锁而短暂阻塞,
    // 故在 ThreadPool 执行,避免 UI 冻结。预设切换时由 PresetManager.ApplyAdvanced 重应用。
    void AmdUndervoltNum_ValueChanged(object s, RoutedEventArgs e) {
      if (_loading) return;
      double? v = AmdUndervoltNum.Value; if (v == null) return;
      int offset = (int)v;
      ConfigService.AmdCpuUndervolt = offset; ConfigService.Save("AmdCpuUndervolt");
      AmdUndervoltStatus.Text = $"CO={offset} …";
      System.Threading.ThreadPool.QueueUserWorkItem(_ => {
        var svc = Services.AmdUndervoltService.Instance;
        if (!svc.IsAvailable) {
          Dispatcher.Invoke(() => AmdUndervoltStatus.Text = "SMU 不可用");
          return;
        }
        var st = svc.SetAllCoreCO(offset);
        Dispatcher.Invoke(() => AmdUndervoltStatus.Text = st == Services.SmuStatus.Ok
          ? $"CO={offset} ✓" : $"CO={offset} 失败 ({st})");
      });
    }

    // ── AMD Curve Optimizer 分核降压弹窗 ──
    // ponytail: FluentWindow + Mica 风格,对齐项目 DialogHelper/HelpWindow 约定。
    // 双列 UniformGrid 呈现核心,卡片式行布局,加全核批量设置。
    // 应用后不关闭弹窗,允许用户继续调整后再应用。
    void AmdUndervoltMore_Click(object sender, RoutedEventArgs e) {
      int coreCount = Math.Min(Environment.ProcessorCount, 16);
      if (coreCount < 1) coreCount = 8;
      var existing = Services.AmdUndervoltService.ParsePerCoreOffsets(ConfigService.AmdCpuPerCoreOffsets);
      var tertiaryBrush = (System.Windows.Media.Brush)FindResource("TextFillColorTertiaryBrush");
      var cardBrush = (System.Windows.Media.Brush)FindResource("ControlFillColorDefaultBrush");
      var bgDeep = (System.Windows.Media.Brush)FindResource("BgDeepBrush");
      var borderSubtle = (System.Windows.Media.Brush)FindResource("BorderSubtleBrush");
      var accentOmen = (System.Windows.Media.Brush)FindResource("AccentOmenBrush");

      var dlg = new Wpf.Ui.Controls.FluentWindow {
        Title = Strings.AmdUndervoltPerCoreTitle,
        Width = 640, Height = 780,
        WindowStartupLocation = WindowStartupLocation.CenterOwner,
        Owner = Window.GetWindow(this),
        ExtendsContentIntoTitleBar = true,
        WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType.Mica,
        Background = System.Windows.Media.Brushes.Transparent,
        ResizeMode = ResizeMode.CanResize,
        MinWidth = 520, MinHeight = 600
      };
      var outer = new Grid();
      outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // 标题栏
      outer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 内容卡片
      outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // 按钮栏

      // ── 标题栏 (对齐 DashboardPage 错误弹窗: BgDeep + 下边框 + 图标 + 标题) ──
      var titleBar = new Border {
        Background = bgDeep, BorderBrush = borderSubtle,
        BorderThickness = new Thickness(0, 0, 0, 1),
        Padding = new Thickness(16, 12, 16, 12)
      };
      var titlePanel = new StackPanel { Orientation = Orientation.Horizontal };
      titlePanel.Children.Add(new Wpf.Ui.Controls.SymbolIcon {
        Symbol = Wpf.Ui.Controls.SymbolRegular.DeveloperBoard24, FontSize = 18,
        Foreground = accentOmen, Margin = new Thickness(0, 0, 8, 0),
        VerticalAlignment = VerticalAlignment.Center
      });
      titlePanel.Children.Add(new TextBlock {
        Text = Strings.AmdUndervoltPerCoreTitle, FontSize = 14, FontWeight = FontWeights.SemiBold,
        VerticalAlignment = VerticalAlignment.Center
      });
      titleBar.Child = titlePanel;
      Grid.SetRow(titleBar, 0); outer.Children.Add(titleBar);

      // ── 内容卡片 (对齐 DashboardPage 错误弹窗: CardBg + CornerRadius) ──
      var contentCard = new Border {
        Background = cardBrush,
        CornerRadius = new CornerRadius(6),
        Padding = new Thickness(12, 14, 12, 14),
        Margin = new Thickness(12, 8, 12, 8)
      };
      var root = new Grid();
      root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // 描述
      root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // 批量栏
      root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 核心列表(占满)
      root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // 状态栏
      contentCard.Child = root;
      Grid.SetRow(contentCard, 1); outer.Children.Add(contentCard);
      dlg.Content = outer;

      // ── 描述 ──
      var descTb = new TextBlock {
        Text = Strings.AmdUndervoltPerCoreDesc, FontSize = 12,
        Foreground = tertiaryBrush, TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 14)
      };
      Grid.SetRow(descTb, 0); root.Children.Add(descTb);

      // ── 全核批量设置栏 ──
      var batchRow = new Grid { Margin = new Thickness(0, 0, 0, 14) };
      batchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
      batchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
      batchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
      var batchLbl = new TextBlock {
        Text = "全核批量:", VerticalAlignment = VerticalAlignment.Center,
        FontSize = 13, FontWeight = FontWeights.Medium, Margin = new Thickness(0, 0, 8, 0)
      };
      Grid.SetColumn(batchLbl, 0); batchRow.Children.Add(batchLbl);
      var batchNum = new Wpf.Ui.Controls.NumberBox {
        Minimum = -100, Maximum = 0, SmallChange = 1, MaxDecimalPlaces = 0,
        PlaceholderText = "0", Width = 100
      };
      Grid.SetColumn(batchNum, 1); batchRow.Children.Add(batchNum);
      var batchApplyBtn = new Wpf.Ui.Controls.Button {
        Content = "应用到所有核", Margin = new Thickness(8, 0, 0, 0),
        Height = 30, MinWidth = 120, Padding = new Thickness(16, 4, 16, 4),
        VerticalAlignment = VerticalAlignment.Center
      };
      Grid.SetColumn(batchApplyBtn, 2); batchRow.Children.Add(batchApplyBtn);
      Grid.SetRow(batchRow, 1); root.Children.Add(batchRow);

      // ── 核心列表(双列 UniformGrid + 滚动,占满剩余空间) ──
      var scroll = new ScrollViewer {
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        Margin = new Thickness(0, 0, 0, 12),
        Padding = new Thickness(4)
      };
      // ponytail: UniformGrid 双列自动排列,核心数奇数时最后一格独占一行右半
      var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2 };
      var numBoxes = new System.Collections.Generic.List<Wpf.Ui.Controls.NumberBox>();
      for (int i = 0; i < coreCount; i++) {
        // 单核卡片(浅底色 + 圆角)
        var card = new Border {
          Background = cardBrush,
          CornerRadius = new CornerRadius(6),
          Padding = new Thickness(12, 8, 12, 8),
          Margin = new Thickness(4)
        };
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var lbl = new TextBlock {
          Text = $"{Strings.AmdUndervoltPerCoreLabel} {i}",
          VerticalAlignment = VerticalAlignment.Center,
          FontSize = 13, FontWeight = FontWeights.SemiBold
        };
        Grid.SetColumn(lbl, 0); row.Children.Add(lbl);
        var num = new Wpf.Ui.Controls.NumberBox {
          Minimum = -100, Maximum = 0, SmallChange = 1, MaxDecimalPlaces = 0,
          ClearButtonEnabled = true, PlaceholderText = "0",
          HorizontalAlignment = HorizontalAlignment.Stretch
        };
        if (existing.TryGetValue(i, out int v) && v != 0) num.Value = v;
        Grid.SetColumn(num, 1); row.Children.Add(num);
        numBoxes.Add(num);
        card.Child = row;
        grid.Children.Add(card);
      }
      scroll.Content = grid;
      Grid.SetRow(scroll, 2); root.Children.Add(scroll);

      // ── 状态栏 ──
      var status = new TextBlock {
        FontSize = 12, Margin = new Thickness(0, 0, 0, 10),
        Foreground = tertiaryBrush
      };
      Grid.SetRow(status, 3); root.Children.Add(status);

      // ── 按钮栏 (对齐 DashboardPage 错误弹窗: 上边框分隔) ──
      var btnRow = new Grid();
      btnRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
      btnRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
      btnRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
      var resetBtn = new Wpf.Ui.Controls.Button {
        Content = "重置全部", Height = 30, MinWidth = 80,
        Padding = new Thickness(16, 4, 16, 4),
        HorizontalAlignment = HorizontalAlignment.Left
      };
      Grid.SetColumn(resetBtn, 0); btnRow.Children.Add(resetBtn);
      var cancelBtn = new Wpf.Ui.Controls.Button {
        Content = Strings.AmdUndervoltClosePage, Height = 30, MinWidth = 80,
        Padding = new Thickness(16, 4, 16, 4),
        Margin = new Thickness(0, 0, 8, 0)
      };
      cancelBtn.Click += (_, __) => dlg.Close();
      Grid.SetColumn(cancelBtn, 1); btnRow.Children.Add(cancelBtn);
      var applyBtn = new Wpf.Ui.Controls.Button {
        Content = Strings.ButtonOK, Height = 30, MinWidth = 80,
        Padding = new Thickness(16, 4, 16, 4)
      };
      Grid.SetColumn(applyBtn, 2); btnRow.Children.Add(applyBtn);
      var btnBorder = new Border {
        BorderBrush = borderSubtle,
        BorderThickness = new Thickness(0, 1, 0, 0),
        Padding = new Thickness(16, 8, 16, 8),
        Child = btnRow
      };
      Grid.SetRow(btnBorder, 2); outer.Children.Add(btnBorder);

      // ── 事件 ──
      batchApplyBtn.Click += (_, __) => {
        double? bv = batchNum.Value;
        if (!bv.HasValue) { status.Text = "请先输入批量值"; return; }
        int bo = (int)bv.Value;
        foreach (var nb in numBoxes) nb.Value = bo;
        status.Text = $"已将 {numBoxes.Count} 核全部设为 {bo}";
      };
      resetBtn.Click += (_, __) => {
        foreach (var nb in numBoxes) nb.Value = null;
        status.Text = "已清空所有核心偏移";
      };
      applyBtn.Click += (_, __) => {
        // 收集非零偏移
        var offsets = new System.Collections.Generic.Dictionary<int, int>();
        for (int i = 0; i < numBoxes.Count; i++) {
          var v = numBoxes[i].Value;
          if (v.HasValue && v.Value != 0) offsets[i] = (int)v.Value;
        }
        // 持久化(空字典存空串)
        var sb = new System.Text.StringBuilder();
        foreach (var kv in offsets) {
          if (sb.Length > 0) sb.Append(',');
          sb.Append(kv.Key).Append(':').Append(kv.Value);
        }
        ConfigService.AmdCpuPerCoreOffsets = sb.ToString();
        ConfigService.Save("AmdCpuPerCoreOffsets");

        if (offsets.Count == 0) {
          status.Text = "已清空分核偏移(持久化)";
          return;
        }
        status.Text = $"应用中 ({offsets.Count} 核)…";
        applyBtn.IsEnabled = false; resetBtn.IsEnabled = false; batchApplyBtn.IsEnabled = false;
        // 后台线程逐核写 SMU,完成后保持弹窗打开
        System.Threading.ThreadPool.QueueUserWorkItem(_ => {
          var svc = Services.AmdUndervoltService.Instance;
          if (!svc.IsAvailable) {
            Dispatcher.Invoke(() => {
              status.Text = "SMU 不可用,配置已保存";
              applyBtn.IsEnabled = true; resetBtn.IsEnabled = true; batchApplyBtn.IsEnabled = true;
            });
            return;
          }
          int ok = svc.ApplyPerCoreCO(offsets);
          Dispatcher.Invoke(() => {
            status.Text = ok == offsets.Count ? $"已应用 {ok}/{offsets.Count} 核 ✓ (可继续调整)"
                                              : $"部分失败 {ok}/{offsets.Count} (可重试)";
            applyBtn.IsEnabled = true; resetBtn.IsEnabled = true; batchApplyBtn.IsEnabled = true;
          });
        });
      };

      dlg.ShowDialog();
    }

    /// <summary>Dynamically build 12-core slider rows into Ccd1CoPanel / Ccd2CoPanel</summary>

    // ── NVIDIA Power Limit (NVML) ──

    // ════════════════════════════════════════════════════════════
    // Power Plan Advanced Settings (EPP, BoostMode, MaxState, MaxFreq, SMT)
    // ════════════════════════════════════════════════════════════

    static readonly Guid SUB_PROCESSOR_GUID = new Guid("54533251-82be-4824-96c1-47b60b740d00");
    // General (all processor classes)
    static readonly Guid GUID_PERFEPP = new Guid("36687f9e-e3a5-4dbf-b1dc-15eb381c6863");
    static readonly Guid GUID_PERFBOOST = new Guid("be337238-0d82-4146-a960-4f3749d470c7");
    static readonly Guid GUID_PROCTHROTTLEMAX = new Guid("bc5038f7-23e0-4960-96da-33abaf5935ec");
    static readonly Guid GUID_PROCTHROTTLEMIN = new Guid("893dee8e-2bef-41e0-89c6-b55d0929964c");
    static readonly Guid GUID_PROCFREQMAX = new Guid("75b0ae3f-bce0-45a7-8c89-c9611c25e100");
    static readonly Guid GUID_SMTUNPARK = new Guid("b28a6829-c5f7-444e-8f61-10e24e85c532");
    // Hetero scheduling per-power-plan (AMD dual-CCD simulation)
    static readonly Guid GUID_HETERO_THREAD_SCHED = new Guid("93b8b6dc-0698-4d1c-9ee4-0644e900c85d");
    static readonly Guid GUID_HETERO_SHORT_SCHED = new Guid("bae08b81-2d5e-4688-ad6a-13243356654b");
    static readonly Guid GUID_HETERO_ACTIVE_POLICY = new Guid("7f2f5cfa-f10c-4823-b5e1-e93ae85f46b5");
    // Class 1 (P-cores / first efficiency class)
    static readonly Guid GUID_PERFEPP_CLS1 = new Guid("36687f9e-e3a5-4dbf-b1dc-15eb381c6864");
    static readonly Guid GUID_PROCTHROTTLEMAX_CLS1 = new Guid("bc5038f7-23e0-4960-96da-33abaf5935ed");
    static readonly Guid GUID_PROCTHROTTLEMIN_CLS1 = new Guid("893dee8e-2bef-41e0-89c6-b55d0929964d");
    static readonly Guid GUID_PROCFREQMAX_CLS1 = new Guid("75b0ae3f-bce0-45a7-8c89-c9611c25e101");
    bool _pwrPlanLoading;
    bool _pwrIsDC;
    bool _pwrIsClass1;

    int ReadPwrValue(Guid scheme, Guid setting) {
      try {
        Guid sub = SUB_PROCESSOR_GUID;
        uint ret = _pwrIsDC
          ? NativeMethods_Power.PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, out uint val)
          : NativeMethods_Power.PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, out val);
        if (ret == 0) return (int)val;
      } catch { }
      return -1;
    }

    void WritePwrValue(Guid scheme, Guid setting, int value) {
      Guid sub = SUB_PROCESSOR_GUID;
      uint v = (uint)value;
      if (_pwrIsDC)
        NativeMethods_Power.PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, v);
      else
        NativeMethods_Power.PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, v);
    }

    /// <summary>同时写入 AC 和 DC 值</summary>
    void WritePwrValueBoth(Guid scheme, Guid setting, int acVal, int dcVal) {
      Guid sub = SUB_PROCESSOR_GUID;
      NativeMethods_Power.PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, (uint)acVal);
      NativeMethods_Power.PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, (uint)dcVal);
    }

    Guid? GetActiveSchemeGuid() {
      try {
        if (NativeMethods_Power.PowerGetActiveScheme(IntPtr.Zero, out var ptr) == 0) {
          var guid = Marshal.PtrToStructure<Guid>(ptr);
          Marshal.FreeHGlobal(ptr);
          return guid;
        }
      } catch { }
      return null;
    }

    Guid GetSettingGuid(Guid general, Guid class1) {
      return _pwrIsClass1 ? class1 : general;
    }

    void BuildPwrPlanOptions() {
      // EPP presets
	      EppCombo.Items.Clear();
	      EppCombo.Items.Add(new ComboBoxItem { Content = Strings.PerfEppInstant, Tag = 0 });
	      EppCombo.Items.Add(new ComboBoxItem { Content = Strings.PerfEppPerf, Tag = 20 });
	      EppCombo.Items.Add(new ComboBoxItem { Content = Strings.PerfEppBalanced, Tag = 50 });
	      EppCombo.Items.Add(new ComboBoxItem { Content = Strings.PerfEppPowerSave, Tag = 80 });
	      EppCombo.Items.Add(new ComboBoxItem { Content = Strings.PerfEppMaxSave, Tag = 100 });

      // Boost Mode presets
      BoostModeCombo.Items.Clear();
	      string[] boostNames = { Strings.PerfBoostDisabled, Strings.PerfBoostEnabled, Strings.PerfBoostHighPerf, Strings.PerfBoostHighEff,
	                               Strings.PerfBoostHighPerfEff, Strings.PerfBoostAggressive, Strings.PerfBoostEffAggressive };
      for (int i = 0; i < boostNames.Length; i++)
        BoostModeCombo.Items.Add(new ComboBoxItem { Content = boostNames[i], Tag = i });

      // Max Processor State presets
      MaxProcStateCombo.Items.Clear();
      int[] stateVals = { 100, 99, 95, 90, 85, 80 };
      foreach (int v in stateVals)
        MaxProcStateCombo.Items.Add(new ComboBoxItem { Content = v + "%", Tag = v });

      // Min Processor State presets
      MinProcStateCombo.Items.Clear();
      int[] minStateVals = { 5, 10, 20, 50, 80, 100 };
      foreach (int v in minStateVals)
        MinProcStateCombo.Items.Add(new ComboBoxItem { Content = v + "%", Tag = v });

      // Max Frequency presets
      MaxFreqCombo.Items.Clear();
	      MaxFreqCombo.Items.Add(new ComboBoxItem { Content = Strings.PerfMaxFreqAuto, Tag = 0 });

      // SMT Policy presets
      SmtPolicyCombo.Items.Clear();
	      string[] smtNames = { Strings.PerfSmtCore, Strings.PerfSmtPerThread, Strings.PerfSmtRoundRobin, Strings.PerfSmtSequential };
      for (int i = 0; i < smtNames.Length; i++)
        SmtPolicyCombo.Items.Add(new ComboBoxItem { Content = smtNames[i], Tag = i });
    }

    void LoadPwrPlanSettings() {
      if (PowerPlanCombo.SelectedItem is ComboBoxItem planItem) {
        string guidStr = planItem.Tag as string;
        if (string.IsNullOrEmpty(guidStr)) return;
        Guid scheme = Guid.Parse(guidStr);
        _pwrPlanLoading = true;

        // EPP — has class 1 variant
        LoadPwrSettingValue(scheme, GetSettingGuid(GUID_PERFEPP, GUID_PERFEPP_CLS1), EppCombo, EppCurrentText);

        // Boost Mode — no class 1 variant
        if (_pwrIsClass1) {
          BoostModeCombo.IsEnabled = false;
	          BoostModeCurrentText.Text = Strings.PerfStatusUnavailable;
        } else
          LoadPwrSettingValue(scheme, GUID_PERFBOOST, BoostModeCombo, BoostModeCurrentText);

        // Max Processor State — has class 1 variant
        LoadPwrSettingValue(scheme, GetSettingGuid(GUID_PROCTHROTTLEMAX, GUID_PROCTHROTTLEMAX_CLS1), MaxProcStateCombo, MaxProcStateCurrentText);

        // Min Processor State — has class 1 variant
        LoadPwrSettingValue(scheme, GetSettingGuid(GUID_PROCTHROTTLEMIN, GUID_PROCTHROTTLEMIN_CLS1), MinProcStateCombo, MinProcStateCurrentText);

        // Max Frequency — has class 1 variant
        LoadPwrSettingValue(scheme, GetSettingGuid(GUID_PROCFREQMAX, GUID_PROCFREQMAX_CLS1), MaxFreqCombo, MaxFreqCurrentText);

        // SMT — no class 1 variant
        if (_pwrIsClass1) {
          SmtPolicyCombo.IsEnabled = false;
	          SmtPolicyCurrentText.Text = Strings.PerfStatusUnavailable;
        } else
          LoadPwrSettingValue(scheme, GUID_SMTUNPARK, SmtPolicyCombo, SmtPolicyCurrentText);

        _pwrPlanLoading = false;
      }
    }

    void LoadPwrSettingValue(Guid scheme, Guid settingGuid, ComboBox combo, TextBlock statusText) {
      int val = ReadPwrValue(scheme, settingGuid);
      if (val < 0) {
	        statusText.Text = Strings.PerfStatusUnavailable;
        combo.IsEnabled = false;
        return;
      }
      combo.IsEnabled = true;
	      statusText.Text = Strings.PerfStatusCurrent + val;
      bool found = false;
      foreach (ComboBoxItem item in combo.Items) {
        if (item.Tag is int tag && tag == val) {
          combo.SelectedItem = item;
          found = true;
          break;
        }
      }
      if (!found && combo.IsEditable) {
        combo.Text = val.ToString();
      }
    }

    void PwrSourceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) {
      if (_pwrPlanLoading) return;
      if (PwrSourceCombo.SelectedIndex >= 0)
        _pwrIsDC = PwrSourceCombo.SelectedIndex == 1;
      LoadPwrPlanSettings();
    }

    void PwrClassCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) {
      if (_pwrPlanLoading) return;
      if (PwrClassCombo.SelectedIndex >= 0)
        _pwrIsClass1 = PwrClassCombo.SelectedIndex == 1;
      LoadPwrPlanSettings();
    }

    void EppCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) {
      if (_pwrPlanLoading) return;
    }
    void BoostModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) {
      if (_pwrPlanLoading) return;
    }
    void MaxProcStateCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) {
      if (_pwrPlanLoading) return;
    }
    void MinProcStateCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) {
      if (_pwrPlanLoading) return;
    }
    void MaxFreqCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) {
      if (_pwrPlanLoading) return;
    }
    void SmtPolicyCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) {
      if (_pwrPlanLoading) return;
    }

    void PwrPlanApply_Click(object sender, RoutedEventArgs e) {
	      if (!(PowerPlanCombo.SelectedItem is ComboBoxItem planItem)) {
	        PwrPlanStatus.Text = Strings.PerfPowerPlanSelectFirst;
        return;
      }
      string guidStr = planItem.Tag as string;
      if (string.IsNullOrEmpty(guidStr)) return;
      Guid scheme = Guid.Parse(guidStr);

      try {
        if (EppCombo.IsEnabled) {
          int eppVal = ParseComboValue(EppCombo, 0, 100);
          WritePwrValue(scheme, GetSettingGuid(GUID_PERFEPP, GUID_PERFEPP_CLS1), eppVal);
        }
        if (BoostModeCombo.IsEnabled && !_pwrIsClass1) {
          if (BoostModeCombo.SelectedItem is ComboBoxItem bi && bi.Tag is int bv)
            WritePwrValue(scheme, GUID_PERFBOOST, bv);
        }
        if (MaxProcStateCombo.IsEnabled) {
          int stateVal = ParseComboValue(MaxProcStateCombo, 0, 100);
          WritePwrValue(scheme, GetSettingGuid(GUID_PROCTHROTTLEMAX, GUID_PROCTHROTTLEMAX_CLS1), stateVal);
        }
        if (MinProcStateCombo.IsEnabled) {
          int minStateVal = ParseComboValue(MinProcStateCombo, 0, 100);
          WritePwrValue(scheme, GetSettingGuid(GUID_PROCTHROTTLEMIN, GUID_PROCTHROTTLEMIN_CLS1), minStateVal);
        }
        if (MaxFreqCombo.IsEnabled) {
          int freqVal = ParseComboValue(MaxFreqCombo, 0, 99999);
          WritePwrValue(scheme, GetSettingGuid(GUID_PROCFREQMAX, GUID_PROCFREQMAX_CLS1), freqVal);
        }
        if (SmtPolicyCombo.IsEnabled && !_pwrIsClass1) {
          if (SmtPolicyCombo.SelectedItem is ComboBoxItem si && si.Tag is int sv)
            WritePwrValue(scheme, GUID_SMTUNPARK, sv);
        }

        NativeMethods_Power.PowerSetActiveScheme(IntPtr.Zero, ref scheme);
	        PwrPlanStatus.Text = Strings.PerfPowerPlanApplied(_pwrIsDC ? Strings.PwrSourceDc : Strings.PwrSourceAc);
        LoadPwrPlanSettings();
      } catch (Exception ex) {
	        PwrPlanStatus.Text = Strings.PerfPowerPlanApplyFailed(ex.Message);
      }
    }

    static int ParseComboValue(ComboBox combo, int min, int max) {
      if (combo.SelectedItem is ComboBoxItem item && item.Tag is int tag)
        return Math.Max(min, Math.Min(max, tag));
      string text = combo.Text?.Trim().TrimEnd('%') ?? "";
      if (int.TryParse(text, out int val))
        return Math.Max(min, Math.Min(max, val));
      return min;
    }

    void PwrPlanView_Click(object sender, RoutedEventArgs e) {
      try { System.Diagnostics.Process.Start("control.exe", "/name Microsoft.PowerOptions"); } catch { }
    }

    // ──────── PerfPage Preset System ────────
    // ponytail: unified with Dashboard's PresetManager (Extreme/GpuPriority/LightUse/Custom1-3).
    // Switching/ saving here calls PresetManager, which fires OnPresetChanged → OnPresetChanged → LoadStateFast.
    // CapturePreset/ApplyPreset(PerfPreset) retained only for snapshot/undo (btnPerfUndo).

    Models.PerfPreset _snapshot;

    // ponytail: dynamic — built-ins + enumerated custom preset files
    void RefreshPresetList() {
      string current = ConfigService.Preset;
      if (string.IsNullOrEmpty(current)) current = "GpuPriority";
      cbxPerfPreset.Items.Clear();
      var all = PresetManager.EnumerateAllPresets();
      int idx = -1;
      for (int i = 0; i < all.Count; i++) {
        var (display, key) = all[i];
        cbxPerfPreset.Items.Add(new ComboBoxItem { Content = display, Tag = key });
        if (key == current) idx = i;
      }
      _loading = true;
      cbxPerfPreset.SelectedIndex = idx >= 0 ? idx : 1;
      _loading = false;
    }

    Models.PerfPreset CapturePreset() {
      return new Models.PerfPreset {
        CpuPowerIndex = CpuPowerCombo.SelectedIndex,
        CpuPowerPL1 = CpuPowerPL1Num.Value ?? 0,
        CpuPowerPL2 = CpuPowerPL2Num.Value ?? 0,
        IccMaxIndex = IccMaxCombo.SelectedIndex,
        IccMax = IccMaxNum.Value ?? 0,
        AcLoadLineIndex = AcLoadLineCombo.SelectedIndex,
        PowerModeIndex = PowerModeCombo.SelectedIndex,
        PowerPlanIndex = PowerPlanCombo.SelectedIndex,
        PwrSourceIndex = PwrSourceCombo.SelectedIndex,
        PwrClassIndex = PwrClassCombo.SelectedIndex,
        EppIndex = EppCombo.SelectedIndex,
        EppText = EppCombo.Text,
        BoostModeIndex = BoostModeCombo.SelectedIndex,
        MaxProcStateIndex = MaxProcStateCombo.SelectedIndex,
        MaxProcStateText = MaxProcStateCombo.Text,
        MinProcStateIndex = MinProcStateCombo.SelectedIndex,
        MinProcStateText = MinProcStateCombo.Text,
        MaxFreqIndex = MaxFreqCombo.SelectedIndex,
        MaxFreqText = MaxFreqCombo.Text,
        SmtPolicyIndex = SmtPolicyCombo.SelectedIndex,
        EcoQosOn = EcoQosToggle.IsChecked ?? false,
        EcoQosThrottlePlugged = EcoQosThrottlePluggedToggle.IsChecked ?? false,
        GpuClockIndex = GpuClockCombo.SelectedIndex,
        GpuClock = GpuClockNum.Value ?? 0,
        GpuCoreOCIndex = GpuCoreOCCombo.SelectedIndex,
        GpuCoreOC = GpuCoreOCNum.Value ?? 0,
        GpuMemoryOCIndex = GpuMemoryOCCombo.SelectedIndex,
        GpuMemoryOC = GpuMemoryOCNum.Value ?? 0,
        GfxModeIndex = GfxModeCombo.SelectedIndex,
        DbVersionIndex = DbVersionCombo.SelectedIndex,
        CtgpIndex = CtgpCombo.SelectedIndex,
        PpabOn = PpabCheck.IsChecked ?? false,
        Tpp = TppNum.Value ?? 0,
        DStateIndex = DStateCombo.SelectedIndex,
        FpsIndex = FpsCombo.SelectedIndex,
        Fps = FpsNum.Value ?? 0,
        RefreshRateIndex = RefreshRateCombo.SelectedIndex,
        RefreshRate = RefreshRateNum.Value ?? 0,
        ResolutionIndex = ResolutionCombo.SelectedIndex,
        DpiIndex = DpiCombo.SelectedIndex,
        HdrOn = HdrToggle.IsChecked ?? false,
      };
    }

    void ApplyPreset(Models.PerfPreset p) {
      _loading = true;
      CpuPowerCombo.SelectedIndex = Clamp(p.CpuPowerIndex, 0, CpuPowerCombo.Items.Count - 1);
      CpuPowerPL1Num.Value = p.CpuPowerPL1;
      CpuPowerPL2Num.Value = p.CpuPowerPL2;
      IccMaxCombo.SelectedIndex = Clamp(p.IccMaxIndex, 0, IccMaxCombo.Items.Count - 1);
      IccMaxNum.Value = p.IccMax;
      IccMaxSlider.Value = p.IccMax > 0 ? p.IccMax : 0;
      AcLoadLineCombo.SelectedIndex = Clamp(p.AcLoadLineIndex, 0, AcLoadLineCombo.Items.Count - 1);
      PowerModeCombo.SelectedIndex = Clamp(p.PowerModeIndex, 0, PowerModeCombo.Items.Count - 1);
      PowerPlanCombo.SelectedIndex = Clamp(p.PowerPlanIndex, 0, PowerPlanCombo.Items.Count - 1);
      PwrSourceCombo.SelectedIndex = Clamp(p.PwrSourceIndex, 0, PwrSourceCombo.Items.Count - 1);
      PwrClassCombo.SelectedIndex = Clamp(p.PwrClassIndex, 0, PwrClassCombo.Items.Count - 1);
      EppCombo.SelectedIndex = Clamp(p.EppIndex, 0, EppCombo.Items.Count - 1);
      if (!string.IsNullOrEmpty(p.EppText)) EppCombo.Text = p.EppText;
      BoostModeCombo.SelectedIndex = Clamp(p.BoostModeIndex, 0, BoostModeCombo.Items.Count - 1);
      MaxProcStateCombo.SelectedIndex = Clamp(p.MaxProcStateIndex, 0, MaxProcStateCombo.Items.Count - 1);
      if (!string.IsNullOrEmpty(p.MaxProcStateText)) MaxProcStateCombo.Text = p.MaxProcStateText;
      MinProcStateCombo.SelectedIndex = Clamp(p.MinProcStateIndex, 0, MinProcStateCombo.Items.Count - 1);
      if (!string.IsNullOrEmpty(p.MinProcStateText)) MinProcStateCombo.Text = p.MinProcStateText;
      MaxFreqCombo.SelectedIndex = Clamp(p.MaxFreqIndex, 0, MaxFreqCombo.Items.Count - 1);
      if (!string.IsNullOrEmpty(p.MaxFreqText)) MaxFreqCombo.Text = p.MaxFreqText;
      SmtPolicyCombo.SelectedIndex = Clamp(p.SmtPolicyIndex, 0, SmtPolicyCombo.Items.Count - 1);
      if (EcoQosToggle.IsChecked != p.EcoQosOn) EcoQosToggle.IsChecked = p.EcoQosOn;
      if (EcoQosThrottlePluggedToggle.IsChecked != p.EcoQosThrottlePlugged) EcoQosThrottlePluggedToggle.IsChecked = p.EcoQosThrottlePlugged;
      GpuClockCombo.SelectedIndex = Clamp(p.GpuClockIndex, 0, GpuClockCombo.Items.Count - 1);
      GpuClockNum.Value = p.GpuClock;
      GpuClockSlider.Value = p.GpuClock > 0 ? p.GpuClock : 0;
      GpuCoreOCCombo.SelectedIndex = Clamp(p.GpuCoreOCIndex, 0, GpuCoreOCCombo.Items.Count - 1);
      GpuCoreOCNum.Value = p.GpuCoreOC;
      GpuCoreOCSlider.Value = p.GpuCoreOC;
      GpuMemoryOCCombo.SelectedIndex = Clamp(p.GpuMemoryOCIndex, 0, GpuMemoryOCCombo.Items.Count - 1);
      GpuMemoryOCNum.Value = p.GpuMemoryOC;
      GpuMemoryOCSlider.Value = p.GpuMemoryOC;
      GfxModeCombo.SelectedIndex = Clamp(p.GfxModeIndex, 0, GfxModeCombo.Items.Count - 1);
      DbVersionCombo.SelectedIndex = Clamp(p.DbVersionIndex, 0, DbVersionCombo.Items.Count - 1);
      CtgpCombo.SelectedIndex = Clamp(p.CtgpIndex, 0, CtgpCombo.Items.Count - 1);
      if (PpabCheck.IsChecked != p.PpabOn) PpabCheck.IsChecked = p.PpabOn;
      TppNum.Value = p.Tpp;
      TppExtraSlider.Value = p.Tpp > 0 ? p.Tpp : 0;
      DStateCombo.SelectedIndex = Clamp(p.DStateIndex, 0, DStateCombo.Items.Count - 1);
      FpsCombo.SelectedIndex = Clamp(p.FpsIndex, 0, FpsCombo.Items.Count - 1);
      FpsNum.Value = p.Fps;
      FpsSlider.Value = p.Fps > 0 ? p.Fps : 0;
      RefreshRateCombo.SelectedIndex = Clamp(p.RefreshRateIndex, 0, RefreshRateCombo.Items.Count - 1);
      RefreshRateNum.Value = p.RefreshRate;
      ResolutionCombo.SelectedIndex = Clamp(p.ResolutionIndex, 0, ResolutionCombo.Items.Count - 1);
      DpiCombo.SelectedIndex = Clamp(p.DpiIndex, 0, DpiCombo.Items.Count - 1);
      if (HdrToggle.IsChecked != p.HdrOn) HdrToggle.IsChecked = p.HdrOn;
      _loading = false;
    }

    static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;

    void cbxPerfPreset_SelectionChanged(object sender, SelectionChangedEventArgs e) {
      if (_loading) return;
      var item = cbxPerfPreset.SelectedItem as ComboBoxItem;
      if (item == null) return;
      string preset = item.Tag as string;
      if (string.IsNullOrEmpty(preset)) return;
      try {
        PresetManager.SwitchPreset(preset);
        // SwitchPreset fires OnPresetChanged → LoadStateFast (UI sync).
        // Also apply hardware (1.1 always, 1.2 for custom) — matches Dashboard behavior.
        if (Application.Current.MainWindow is Views.MainWindow mainWindow)
          mainWindow.ApplyPresetHardware();
      } catch (Exception ex) { Log($"cbxPerfPreset_SelectionChanged: {ex.Message}"); }
    }

    void btnPerfSave_Click(object sender, RoutedEventArgs e) {
      // ponytail: snapshot before save so Undo can roll back to pre-save state
      // (absorbs the former btnPerfApply's snapshot duty — Apply button removed).
      _snapshot = CapturePreset();
      // ponytail: always saveable — creates a new custom preset from current settings
      string name = tbxPerfPresetName.Text?.Trim();
      string presetKey;
      if (!string.IsNullOrEmpty(name)) {
        // sanitize name for file system: replace invalid chars with underscores
        presetKey = string.Join("_", name.Split(System.IO.Path.GetInvalidFileNameChars()));
        if (string.IsNullOrEmpty(presetKey)) presetKey = "Custom";
      } else {
        // no name given — derive from current preset if custom, else auto-number
        string current = ConfigService.Preset;
        if (PresetManager.IsCustom(current) && !PresetManager.IsBuiltIn(current)) {
          presetKey = current;
          name = ConfigService.GetCustomPresetDisplayName(current);
        } else {
          // auto-number: find highest CustomN and increment
          int maxN = 0;
          foreach (var (_, key) in PresetManager.EnumerateCustomPresets()) {
            if (key.StartsWith("Custom") && int.TryParse(key.Substring(6), out int n) && n > maxN) maxN = n;
          }
          presetKey = "Custom" + (maxN + 1);
          name = presetKey;
        }
      }
      PresetManager.SaveCustomPreset(presetKey, name);
      // switch to the new/updated preset
      PresetManager.SwitchPreset(presetKey);
      if (Application.Current.MainWindow is Views.MainWindow mainWindow)
        mainWindow.ApplyPresetHardware();
      RefreshPresetList();
      Log($"btnPerfSave: saved to {presetKey} (display=\"{name}\")");
    }

    void btnPerfLoad_Click(object sender, RoutedEventArgs e) {
      _loading = true;
      LoadStateFast();
      try { LoadStateDeferred(); } catch { }
      _loading = false;
      // ponytail: previously Reload only updated UI sliders while _loading=true
      // suppressed every *_ValueChanged handler → sliders moved, hardware
      // never got the new values. Reapply via PresetManager so the load button
      // actually pushes to MSR/SMU. Runs on a worker thread to keep UI snappy.
      System.Threading.ThreadPool.QueueUserWorkItem(_ => {
        try { PresetManager.ApplyAdvanced(); } catch { }
      });
      Log($"btnPerfLoad: reloaded '{ConfigService.Preset}' + advanced settings reapplied");
    }

    void btnPerfDelete_Click(object sender, RoutedEventArgs e) {
      string preset = ConfigService.Preset;
      if (PresetManager.IsBuiltIn(preset)) {
	        DialogHelper.Info(Strings.PerfDeleteBuiltinPreset, Strings.Hint);
        return;
      }
      string displayName = ConfigService.GetCustomPresetDisplayName(preset);
	      if (!DialogHelper.OkCancel(
	        Strings.PerfDeleteConfirmMsg($"「{displayName}」"), Strings.PerfDeleteConfirmTitle)) return;
      // save current state away from this preset (SwitchPreset auto-saves on leave)
      ConfigService.Preset = ""; // prevent SwitchPreset from re-saving to the deleted key
      PresetManager.DeleteCustomPreset(preset);
      PresetManager.SwitchPreset("GpuPriority");
      if (Application.Current.MainWindow is Views.MainWindow mainWindow)
        mainWindow.ApplyPresetHardware();
      Log($"btnPerfDelete: deleted {preset} → GpuPriority");
      RefreshPresetList();
    }

    void btnPerfUndo_Click(object sender, RoutedEventArgs e) {
      // 如果有 Apply 快照，优先回滚到快照；否则恢复到默认预设并清空自定义预设
      if (_snapshot != null) {
	        if (!DialogHelper.OkCancel(
	          Strings.PerfUndoApplyMsg, Strings.PerfUndoApplyTitle)) return;
        ApplyPreset(_snapshot);
        _snapshot = null;
        Log("btnPerfUndo: reverted to snapshot");
        return;
      }

	      if (!DialogHelper.OkCancel(
	        Strings.PerfResetDefaultsMsg, Strings.PerfResetDefaultsTitle)) return;

      // 1. 切换到 GpuPriority 内置预设
      PresetManager.SwitchPreset("GpuPriority");
      // 2. 删除所有自定义预设文件
      foreach (var (_, key) in PresetManager.EnumerateCustomPresets()) {
        PresetManager.DeleteCustomPreset(key);
      }
      // 3. 应用硬件
      if (Application.Current.MainWindow is Views.MainWindow mainWindow)
        mainWindow.ApplyPresetHardware();
      // 4. 刷新 UI
      RefreshPresetList();
      _loading = true;
      LoadStateFast();
      try { LoadStateDeferred(); } catch { }
      _loading = false;
      _snapshot = null;
      Log("btnPerfUndo: restored GpuPriority, cleared custom presets");
    }
  }
  // ponytail: CoreCheckItem 已迁移到 CoreKeepPage.xaml.cs，PerfPage 不再需要
}




