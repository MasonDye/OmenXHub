// FanPage.cs - 风扇控制页面
// 风扇模式/灵敏度/曲线选择，自定义风扇曲线编辑，自动保护和除尘功能
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using OmenSuperHub.Services;
using OmenSuperHub.Utils;
using static OmenSuperHub.OmenHardware;

namespace OmenSuperHub.Pages {
  public partial class FanPage : System.Windows.Controls.Page {
    const int CurvePointRadius = 8;
    const float MinTemp = 20;
    const float MaxTemp = 105;
    const float MaxRPM = 6400;
    const double CurvePadL = 32, CurvePadR = 16, CurvePadT = 22, CurvePadB = 28;

    bool _loading;
    bool _showGpuCurve;
    bool _optionsBuilt;
    int _draggingIndex = -1;
    // ponytail: cache curve visual elements so MouseMove doesn't rebuild the
    // entire canvas (~30 WPF objects) every frame — that causes PPT-level stutter.
    Polyline _polylineElement;
    List<Ellipse> _circleElements;
    List<(float temp, int rpm)> _curvePoints;
    List<(float temp, int rpm)> _curvePointsGPU;
    // ponytail: keep in sync with PresetManager.BuiltInKeys + ConfigService.Preset default ("GpuPriority").
    int _initRpm;
    string _currentPresetKey = "GpuPriority";

    public FanPage() {
      try { InitializeComponent(); } catch (Exception ex) {
        DialogHelper.Info("FanPage Init: " + ex.GetType().Name + "\n" + ex.Message + "\n" + (ex.InnerException?.Message ?? ""));
      }
      FanCurveCanvas.SizeChanged += (s, e) => { if (_curvePoints != null) DrawFanCurve(); };
      Loaded += FanPage_Loaded;
      // ponytail: 见 PerfPage.Unloaded 同理 — CachedPageService 缓存导致 Loaded 多次触发
      // 而订阅永不去订阅；Unloaded 取消以让页面可 GC。
      Unloaded += FanPage_Unloaded;
      // ponytail: 第三扇转速展示(仅三扇机型) — 低频 2s 刷新,窗口不可见即停(NetworkBoostPage 同款守卫)。
      _fan3Timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
      _fan3Timer.Tick += (s, e) => {
        if (Window.GetWindow(this)?.IsVisible != true) return;
        UpdateFan3RpmText();
      };
    }

    readonly System.Windows.Threading.DispatcherTimer _fan3Timer;

    // ponytail: 第三扇显示行 —— 仅 IsThreeFan() 时可见;DebugShowAllUi 开启后双风机也强制
    // 显示(带 DEBUG 标注),用于无三扇机预览/排错 UI(与 PerfPage 强显卡片同款开关)。
    void UpdateFan3RpmText() {
      if (Fan3RpmText == null) return;
      bool is3 = OmenHardware.IsThreeFan();
      bool debugForce = ConfigService.DebugShowAllUi && !is3;
      Fan3RpmText.Visibility = (is3 || debugForce) ? Visibility.Visible : Visibility.Collapsed;
      if (!is3 && !debugForce) return;
      int lv = HardwareService.FanSpeedNow.Count > 2 ? HardwareService.FanSpeedNow[2] : -1;
      string prefix = debugForce ? "[DEBUG] " : "";
      // 双风机第 3 路 level 恒 -1(EC 不回),DEBUG 态按当前 CPU/GPU 均值模拟固件跟随行为。
      if (lv < 0) {
        int simLv = (int)Math.Round(
          (HardwareService.FanSpeedNow[0] + HardwareService.FanSpeedNow[1]) / 2.0);
        if (simLv < 0) { Fan3RpmText.Text = prefix + Strings.Fan3FollowLabel; return; }
        Fan3RpmText.Text = $"{prefix}{Strings.Fan3FollowRpm((simLv * 100).ToString())}";
        return;
      }
      Fan3RpmText.Text = $"{prefix}{Strings.Fan3FollowRpm((lv * 100).ToString())}";
    }

    void FanPage_Unloaded(object sender, RoutedEventArgs e) {
      PresetManager.OnPresetChanged -= OnPresetChanged;
      _fan3Timer?.Stop();
      // ponytail: CachedPageService 缓存 Page 但 Canvas 子控件 (Polyline/Ellipse) 一旦被
      // _polylineElement/_circleElements 持有就阻止 GC。卸载时同时清空 Canvas 子控件与引用。
      FanCurveCanvas.Children.Clear();
      _polylineElement = null;
      _circleElements = null;
    }

    void OnPresetChanged(string preset) {
      // ponytail: dynamic — find index by tag in combo items
      int idx = -1;
      for (int i = 0; i < cbxFanPreset.Items.Count; i++) {
        if (cbxFanPreset.Items[i] is ComboBoxItem item && item.Tag as string == preset) { idx = i; break; }
      }
      if (idx >= 0 && cbxFanPreset.SelectedIndex != idx) {
        _loading = true;
        cbxFanPreset.SelectedIndex = idx;
        _loading = false;
      }
      // ponytail: keep _currentPresetKey in sync regardless of fan mode — previously
      // this was only set in the mode==2 branch, so editing a curve while the fan
      // was in mode 0/1/3 saved to a stale key (often the bogus "balanced").
      // fan mode is part of the preset snapshot (FanControl/FanTable), so when the
      // preset changes we also re-sync FanModeCombo from ConfigService.
      _currentPresetKey = preset;
      _fan3PointsLoaded = false;   // 换预设后 fan3 曲线需重新加载
      if (!IsLoaded) return;
      // re-sync fan mode combo from the freshly-applied ConfigService values so the
      // UI reflects the preset's FanControl/FanTable (Extreme→酷冷, LightUse→静音, 自定义→it stored that).
      // ALSO apply the fan configuration to hardware immediately — without this,
      // changing presets only scheduled it on a ThreadPool work item and the
      // fan could lag one cycle or silently stay at the old level.
      _loading = true;
      LoadConfigState();
      UpdateFanModeUI();
      _loading = false;
      ApplyPresetFanConfig();
      // only the curve workspace (mode 3 = 自定义曲线) shows preset curves;
      // modes 0=静音/1=降温/2=平衡/4=手动 have no UI surface for per-preset curve files.
      if (FanModeCombo.SelectedIndex != 3) return;
      LoadPresetCurvePoints(_currentPresetKey);
    }

    // ponytail: synced to PerfPage — use PresetManager.EnumerateAllPresets. Upgrade path = share via PresetManager.
    void RefreshPresetList() {
      string current = ConfigService.Preset;
      if (string.IsNullOrEmpty(current)) current = "GpuPriority";
      cbxFanPreset.Items.Clear();
      var all = PresetManager.EnumerateAllPresets();
      int idx = -1;
      for (int i = 0; i < all.Count; i++) {
        var (display, key) = all[i];
        cbxFanPreset.Items.Add(new ComboBoxItem { Content = display, Tag = key });
        if (key == current) idx = i;
      }
      _loading = true;
      cbxFanPreset.SelectedIndex = idx >= 0 ? idx : 1;
      _loading = false;
    }

    void cbxFanPreset_SelectionChanged(object sender, SelectionChangedEventArgs e) {
      if (_loading) return;
      var item = cbxFanPreset.SelectedItem as ComboBoxItem;
      if (item == null) return;
      string preset = item.Tag as string;
      if (string.IsNullOrEmpty(preset)) return;
      try {
        PresetManager.SwitchPreset(preset);
        if (Application.Current.MainWindow is Views.MainWindow mainWindow)
          mainWindow.ApplyPresetHardware();
      } catch (Exception ex) { System.Diagnostics.Debug.WriteLine("cbxFanPreset_SelectionChanged: " + ex.Message); }
    }

    private void FanPage_Loaded(object sender, RoutedEventArgs e) {
      try {
      PresetManager.OnPresetChanged -= OnPresetChanged;
      PresetManager.OnPresetChanged += OnPresetChanged;
      // ponytail: 第三扇显示 —— Loaded 时先刷一拍并启 2s timer(仅三扇机可见该行)。
      UpdateFan3RpmText();
      _fan3Timer?.Start();
      // ponytail: 三扇机显示第三扇曲线 tab(双扇机 Collapsed);DebugShowAllUi 开启后
      // 双风机也强显,与 UpdateFan3RpmText 同款开关 —— 编辑/保存走 Fan3TempFanMap 与
      // custom_<preset>_fan3.txt,不碰硬件;下发仍由 TrayService IsThreeFan() 门控。
      if ((OmenHardware.IsThreeFan() || ConfigService.DebugShowAllUi)
          && FanCurveSel.Items.Count > 2
          && FanCurveSel.Items[2] is System.Windows.Controls.ComboBoxItem fan3Item)
        fan3Item.Visibility = Visibility.Visible;
      _fan3PointsLoaded = false;
      if (!_optionsBuilt) { BuildFanRpmOptions(); _optionsBuilt = true; }
      RefreshPresetList();
      LoadCurvePoints();
      // ponytail: 必须在 LoadConfigState 之前同步 _currentPresetKey，
      // 否则 smart 参数会用默认值 "GpuPriority" 加载，与当前实际预设脱钩。
      _currentPresetKey = ConfigService.Preset;
      _loading = true;
      LoadConfigState();
      UpdateFanModeUI();
      if (FanModeCombo.SelectedIndex == 3) {
        LoadPresetCurvePoints(_currentPresetKey);
      }
      if (_initRpm > 0) { SelectRpmComboItem(_initRpm); FanRpmSlider.Value = _initRpm; }
      else FanRpmSlider.Value = 2500;
      _loading = false;
      } catch (Exception ex) {
        DialogHelper.Info("FanPage error: " + ex.GetType().Name + "\n" + ex.Message + "\n" + (ex.InnerException?.Message ?? ""));
      }
    }

    void LoadCurvePoints() {
      var existing = FanService.LoadCustomCurve();
      _curvePoints = (existing != null && existing.Count > 0) ? existing :
        new List<(float, int)> { (20f, 0), (40f, 1600), (55f, 2200), (70f, 3400), (85f, 4800), (100f, 6400) };
      var existingGpu = FanService.LoadCustomCurveGPU();
      _curvePointsGPU = (existingGpu != null && existingGpu.Count > 0) ? existingGpu :
        new List<(float, int)> { (20f, 0), (40f, 1600), (55f, 2200), (70f, 3400), (85f, 4800), (100f, 6400) };
      DrawFanCurve();
    }

    void LoadPresetCurvePoints(string presetKey) {
      var (cpu, gpu) = FanService.ApplyPresetCurve(presetKey);
      _curvePoints = cpu;
      _curvePointsGPU = gpu;
      DrawFanCurve();
    }

    void LoadConfigState() {
      try {
      string fc = ConfigService.FanControl;
      if (fc == "smart" || fc == "custom") FanModeCombo.SelectedIndex = 3;
      else if (fc == "" || fc == "auto" || fc == "silent" || fc == "cool" || fc == "balanced") {
        // FanModeCombo 现映射 5 档: 0=静音 / 1=降温 / 2=平衡 / 3=自定义曲线 / 4=手动。
        // 平衡档对应 FanTable=="balanced"，是 GpuPriority 等内置预设的默认曲线。
        switch (ConfigService.FanTable) {
          case "cool": FanModeCombo.SelectedIndex = 1; break;
          case "balanced": FanModeCombo.SelectedIndex = 2; break;
          default: FanModeCombo.SelectedIndex = 0; break; // silent / 空
        }
      } else if (fc.Contains(" RPM")) {
        FanModeCombo.SelectedIndex = 4;
        _initRpm = FanService.ParseFanRpm(fc);
        FanRpmSlider.Value = _initRpm;
      } else if (fc.EndsWith("%")) {
        FanModeCombo.SelectedIndex = 4;
        _initRpm = FanService.ParseFanRpm(fc);
        FanRpmSlider.Value = _initRpm;
      } else {
        FanModeCombo.SelectedIndex = 4;
        _initRpm = 2500;
        FanRpmSlider.Value = _initRpm;
      }
      switch (ConfigService.TempSensitivity) {
        case "realtime": SensitivityCombo.SelectedIndex = 0; break;
        case "high": SensitivityCombo.SelectedIndex = 1; break;
        case "medium": SensitivityCombo.SelectedIndex = 2; break;
        case "low": SensitivityCombo.SelectedIndex = 3; break;
        default: SensitivityCombo.SelectedIndex = 2; break;
      }
      AutoFanProtectToggle.IsChecked = ConfigService.AutoFanProtect == "on";
      FanSyncToggle.IsChecked = ConfigService.FanSync;
      IrFanCurveToggle.IsChecked = ConfigService.UseIrForFanCurve;
      SmartIdleLambdaToggle.IsChecked = ConfigService.SmartFanIdleLambda;
      // ponytail: smart 参数按预设从 FanCurves/custom_<preset>_smart.txt 加载。
      // 文件不存在时保留 ConfigService 字段当前值（继承上一预设，对齐 EnsurePresetCurveFile 的克隆语义）。
      var sp = FanService.LoadPresetSmartParams(_currentPresetKey);
      if (sp.HasValue) {
        ConfigService.SmartFanEmaAlpha = sp.Value.emaAlpha;
        ConfigService.SmartFanStepDownRate = sp.Value.stepDown;
        ConfigService.SmartFanHysteresis = sp.Value.hysteresis;
      }
      float ea = ConfigService.SmartFanEmaAlpha;
      int eaIdx = ea <= 0.15f ? 0 : ea <= 0.4f ? 1 : 2;
      SmartEmaAlphaCombo.SelectedIndex = eaIdx;
      int sd = ConfigService.SmartFanStepDownRate;
      SmartStepDownCombo.SelectedIndex = sd <= 100 ? 0 : sd <= 300 ? 1 : sd <= 500 ? 2 : 3;
      float hy = ConfigService.SmartFanHysteresis;
      SmartHysteresisCombo.SelectedIndex = hy <= 0.2f ? 0 : hy <= 0.5f ? 1 : 2;
      // ponytail: 除尘卡显隐接入 —— 原先 XAML Collapsed 后从未被任何代码置 Visible,
      // 功能等于"藏在代码里"。按能力探测显示(legacy 或标准 CleanCreek 任一支持即可);
      // DebugShowAllUi 开启后强制显示以利无能力机型预览/排错(与 PerfPage 强显卡片同款);
      // 三扇机在描述里注明第三扇也参与逆转。
      bool cleanSupported = OmenHardware.IsLegacyCleanCreekSupported() || OmenHardware.IsCleanCreekSupported();
      if (cleanSupported && OmenHardware.IsThreeFan())
        CleanCreekDesc.Text = Strings.DustCleanDesc + " " + Strings.DustCleanFan3Note;
      if (ConfigService.DebugShowAllUi) {
        CleanCreekCard.Visibility = Visibility.Visible;
        if (!cleanSupported) {
          // 能力探测不通过的机器:DEBUG 强显但标注不可用,按钮点了会走 Unsupported 提示分支。
          CleanCreekDesc.Text = Strings.DustCleanDesc + " [DEBUG] " + Strings.CleanCreekUnsupported;
        }
      } else {
        CleanCreekCard.Visibility = cleanSupported ? Visibility.Visible : Visibility.Collapsed;
      }
      } catch { }
    }

    void UpdateFanModeUI() {
      int mode = FanModeCombo.SelectedIndex;
      bool isSmartCurve = mode == 3;
      bool isManual = mode == 4;
      bool isAuto = (mode == 0 || mode == 1 || mode == 2); // 静音/降温/平衡 都走温度敏感自动档
      FanCurveCard.Visibility = isSmartCurve ? Visibility.Visible : Visibility.Collapsed;
      SmartFanCard.Visibility = isSmartCurve ? Visibility.Visible : Visibility.Collapsed;
      ManualControlCard.Visibility = isManual ? Visibility.Visible : Visibility.Collapsed;
      TempSensCard.Visibility = isAuto ? Visibility.Visible : Visibility.Collapsed;
    }

    void FanMode_SelectionChanged(object s, SelectionChangedEventArgs e) {
      if (_loading) return;
      TrayService.ResetAutoProtect();
      int mode = FanModeCombo.SelectedIndex;
      if (mode == 0) {
        ConfigService.FanControl = "";
        ConfigService.FanTable = "silent";
      } else if (mode == 1) {
        ConfigService.FanControl = "";
        ConfigService.FanTable = "cool";
      } else if (mode == 2) {
        ConfigService.FanControl = "";
        ConfigService.FanTable = "balanced";
      } else if (mode == 3) {
        ConfigService.FanControl = "smart";
        // ponytail: ApplyPresetCurve (called by LoadPresetCurvePoints below) is the sole
        // curve source. The old LoadFanConfig(cool/silent.txt) here only pre-stuffed the
        // maps with the wrong curve for a couple ticks before LoadPresetCurvePoints cleared
        // them — pure noise + startup skitter. InitSmartFanState is still needed to reset EMA.
        _currentPresetKey = ConfigService.Preset;
        // ponytail: 切到 mode 3 时先重新加载该预设的 smart 参数，再用对应 EmaAlpha 初始化 EMA。
        var sp = FanService.LoadPresetSmartParams(_currentPresetKey);
        if (sp.HasValue) {
          ConfigService.SmartFanEmaAlpha = sp.Value.emaAlpha;
          ConfigService.SmartFanStepDownRate = sp.Value.stepDown;
          ConfigService.SmartFanHysteresis = sp.Value.hysteresis;
        }
        FanService.InitSmartFanState(ConfigService.SmartFanEmaAlpha);
        LoadPresetCurvePoints(_currentPresetKey);
        SetMaxFanSpeedOff();
        TrayService.fanControlTimer.Change(0, 1000);
} else if (mode == 4) {
      // ponytail: parse existing FanControl for the current manual RPM instead of
      // hardcoding 2500 — otherwise switching back to manual mode always resets
      // the slider to 2500, regardless of what the user set before or what the
      // current preset stores.
      int rpm = FanService.ParseFanRpm(ConfigService.FanControl);
      ConfigService.FanControl = rpm + " RPM";
      SetMaxFanSpeedOff();
      TrayService.fanControlTimer.Change(Timeout.Infinite, Timeout.Infinite);
      _initRpm = rpm;
    }
    _loading = true;
    if (_initRpm > 0) {
      FanRpmSlider.Value = _initRpm;
      _initRpm = 0;
    }
    UpdateFanModeUI();
    _loading = false;
    ConfigService.Save("FanControl");
    ConfigService.Save("FanTable");
      // ponytail: fan mode switch applies immediately — no BeginInvoke deferral.
      // The old code deferred LoadFanConfig/SetMaxFanSpeedOff/timer-Change to
      // Dispatcher.Background, which meant the first tick after a mode switch had
      // no map loaded yet, and the timer's "fanSpeedNow hasn't changed" guard
      // silently skipped SetFanLevel.  Switching fan modes appeared to have no
      // effect until the next preset switch kicked ApplyPresetHardware.
      if (mode == 0) {
        Views.OsdWindow.ShowFanModeOsd("silent");
        FanService.LoadFanConfig("silent.txt");
        SetMaxFanSpeedOff();
        TrayService.fanControlTimer.Change(0, 1000);
      } else if (mode == 1) {
        Views.OsdWindow.ShowFanModeOsd("cool");
        FanService.LoadFanConfig("cool.txt");
        SetMaxFanSpeedOff();
        TrayService.fanControlTimer.Change(0, 1000);
      } else if (mode == 2) {
        Views.OsdWindow.ShowFanModeOsd("balanced");
        FanService.LoadFanConfig("balanced.txt");
        SetMaxFanSpeedOff();
        TrayService.fanControlTimer.Change(0, 1000);
      } else if (mode == 3) {
        Views.OsdWindow.ShowFanModeOsd("smart");
        SetMaxFanSpeedOff();
        FanService.ApplyCustomCurve(_curvePoints);
        if (_curvePointsGPU != null) FanService.ApplyCustomCurveGPU(_curvePointsGPU);
        TrayService.fanControlTimer.Change(0, 1000);
      } else if (mode == 4) {
        Views.OsdWindow.ShowFanModeOsd(ConfigService.FanControl);
        TrayService.fanControlTimer.Change(Timeout.Infinite, Timeout.Infinite);
        int rpm = FanService.ParseFanRpm(ConfigService.FanControl);
        SetFanLevel(0, 0, fan3: OmenHardware.IsThreeFan());
        SetFanLevel(rpm / 100, rpm / 100, fan3: OmenHardware.IsThreeFan());
      }
      // ponytail: 内置预设的风扇档(含手动/固定 RPM)是临时绑定,不持久化到预设子键——
      // 切走/重启回到预设 FanTable 默认(Extreme=cool/GpuPriority=balanced/LightUse=silent)。
      // 只有自定义预设才完整绑定并保存风扇配置(SaveCustomPreset 写 JSON)。
      if (PresetManager.IsCustom(ConfigService.Preset)) {
        PresetManager.SaveCustomPreset(ConfigService.Preset);
      }
    }

    void Sensitivity_SelectionChanged(object s, SelectionChangedEventArgs e) {
      if (_loading) return;
      string[] vals = { "realtime", "high", "medium", "low" };
      int idx = SensitivityCombo.SelectedIndex;
      if (idx < 0) return;
      string val = vals[idx];
      ConfigService.TempSensitivity = val;
      ConfigService.Save("TempSensitivity");
      switch (val) {
        case "realtime": HardwareService.RespondSpeed = 1; break;
        case "high": HardwareService.RespondSpeed = 0.4f; break;
        case "medium": HardwareService.RespondSpeed = 0.1f; break;
        case "low": HardwareService.RespondSpeed = 0.04f; break;
      }
    }

    void AutoFanProtectToggle_Changed(object sender, RoutedEventArgs e) {
      ConfigService.AutoFanProtect = AutoFanProtectToggle.IsChecked == true ? "on" : "off";
      ConfigService.Save("AutoFanProtect");
      // When turning off, do NOT call ResetAutoProtect() here — that would clear
      // saved fan/GPU state before the timer can restore it. The timer's restore
      // logic detects fanProtectOn==false and unwinds the active session naturally.
    }

    void FanSyncToggle_Changed(object sender, RoutedEventArgs e) {
      ConfigService.FanSync = FanSyncToggle.IsChecked == true;
      ConfigService.Save("FanSync");
    }

    void IrFanCurveToggle_Changed(object sender, RoutedEventArgs e) {
      ConfigService.UseIrForFanCurve = IrFanCurveToggle.IsChecked == true;
      ConfigService.Save("UseIrForFanCurve");
    }

    void SmartIdleLambdaToggle_Changed(object sender, RoutedEventArgs e) {
      ConfigService.SmartFanIdleLambda = SmartIdleLambdaToggle.IsChecked == true;
      ConfigService.Save("SmartFanIdleLambda");
    }

    void SmartEmaAlpha_Changed(object s, SelectionChangedEventArgs e) {
      if (_loading) return;
      float[] vals = { 0.1f, 0.3f, 0.5f };
      int idx = SmartEmaAlphaCombo.SelectedIndex;
      if (idx >= 0) {
        ConfigService.SmartFanEmaAlpha = vals[idx];
        FanService.InitSmartFanState(ConfigService.SmartFanEmaAlpha);
        FanService.SavePresetSmartParams(_currentPresetKey, ConfigService.SmartFanEmaAlpha, ConfigService.SmartFanStepDownRate, ConfigService.SmartFanHysteresis);
      }
    }

    void SmartStepDown_Changed(object s, SelectionChangedEventArgs e) {
      if (_loading) return;
      int[] vals = { 100, 300, 500, 1000 };
      int idx = SmartStepDownCombo.SelectedIndex;
      if (idx >= 0) {
        ConfigService.SmartFanStepDownRate = vals[idx];
        FanService.SavePresetSmartParams(_currentPresetKey, ConfigService.SmartFanEmaAlpha, ConfigService.SmartFanStepDownRate, ConfigService.SmartFanHysteresis);
      }
    }

    void SmartHysteresis_Changed(object s, SelectionChangedEventArgs e) {
      if (_loading) return;
      float[] vals = { 0.2f, 0.5f, 1.0f };
      int idx = SmartHysteresisCombo.SelectedIndex;
      if (idx >= 0) {
        ConfigService.SmartFanHysteresis = vals[idx];
        FanService.SavePresetSmartParams(_currentPresetKey, ConfigService.SmartFanEmaAlpha, ConfigService.SmartFanStepDownRate, ConfigService.SmartFanHysteresis);
      }
    }

    void BuildFanRpmOptions() {
      FanRpmCombo.Items.Clear();
      int[] rpms = { 1500, 2000, 2500, 3000, 3500, 4000, 4500, 5000, 5500, 6000 };
      foreach (int r in rpms)
        FanRpmCombo.Items.Add(new ComboBoxItem { Content = r + " RPM", Tag = r });
    }

    void FanRpmCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) {
      if (_loading) return;
      if (FanModeCombo.SelectedIndex != 4) return;
      _loading = true;
      var item = FanRpmCombo.SelectedItem as ComboBoxItem;
      if (item == null) { _loading = false; return; }
      int rpm = (int)item.Tag;
      FanRpmSlider.Value = rpm;
      SetMaxFanSpeedOff();
      SetFanLevel(0, 0, fan3: OmenHardware.IsThreeFan());
      SetFanLevel(rpm / 100, rpm / 100, fan3: OmenHardware.IsThreeFan());
      ConfigService.FanControl = rpm + " RPM";
      ConfigService.Save("FanControl");
      // ponytail: 内置预设的手动 RPM 不持久化到预设子键(临时绑定,切走/重启回 FanTable 默认);
      // 仅自定义预设完整绑定风扇配置。
      if (PresetManager.IsCustom(ConfigService.Preset)) {
        PresetManager.SaveCustomPreset(ConfigService.Preset);
      }
      _loading = false;
    }

    void FanRpmNum_ValueChanged(object s, RoutedEventArgs e) {
      if (_loading) return;
      if (FanModeCombo.SelectedIndex != 4) return;
      _loading = true;
      double? val = FanRpmNum.Value;
      if (val == null || val < 500 || val > 6000) { _loading = false; return; }
      int rpm = (int)val;
      SetMaxFanSpeedOff();
      SetFanLevel(0, 0, fan3: OmenHardware.IsThreeFan());
      SetFanLevel(rpm / 100, rpm / 100, fan3: OmenHardware.IsThreeFan());
      ConfigService.FanControl = rpm + " RPM";
      ConfigService.Save("FanControl");
      SelectComboItem(FanRpmCombo, rpm + " RPM");
      // ponytail: 与 FanRpmCombo_SelectionChanged 一致 —— 内置预设手动不持久化,仅自定义预设绑定。
      if (PresetManager.IsCustom(ConfigService.Preset)) {
        PresetManager.SaveCustomPreset(ConfigService.Preset);
      }
      _loading = false;
    }

    // ponytail: 曲线编辑器三态 —— false=CPU tab, gpuTab=true=GPU tab, fan3 tab 仅三扇机显示。
    // fan3 选中时 _showGpuCurve 置 false 且 _fan3CurveActive=true,编辑/保存/绘制走 Fan3TempFanMap。
    bool _fan3CurveActive;
    bool _fan3PointsLoaded;

    void FanCurveSel_Changed(object s, SelectionChangedEventArgs e) {
      if (!IsLoaded) return;
      _fan3CurveActive = FanCurveSel.SelectedIndex == 2;
      _showGpuCurve = FanCurveSel.SelectedIndex == 1;
      if (_fan3CurveActive) {
        // 首次切到第三扇 tab: 加载该 preset 的 fan3 曲线(文件不存在则预置默认四点,
        // 否则空表无法用拖拽创建第一个点);保存时写 custom_<preset>_fan3.txt。
        if (!_fan3PointsLoaded) {
          FanService.LoadFan3CurveIntoMap(_currentPresetKey);
          _fan3PointsLoaded = true;
        }
        if (FanService.Fan3TempFanMap.Count == 0) {
          foreach (var (temp, rpm) in new[] { ((float)40, 1500), ((float)60, 2200), ((float)80, 3400), ((float)95, 4800) })
            FanService.Fan3TempFanMap[temp] = new List<int> { rpm };
        }
        _curvePoints = FanService.Fan3TempFanMap
          .OrderBy(kv => kv.Key)
          .SelectMany(kv => kv.Value.Select(rpm => (kv.Key, rpm)))
          .ToList();
      } else {
        // ponytail: 从 fan3 tab 切回 CPU/GPU 时重载对应曲线 —— _curvePoints 已被
        // fan3 编辑快照占用,不重载会把 fan3 数据保存进 CPU/GPU 通道。
        var saved = FanService.LoadPresetCurve(_currentPresetKey, _showGpuCurve);
        var fallback = (_showGpuCurve ? _curvePointsGPU : _curvePoints);
        if (_showGpuCurve) _curvePointsGPU = (saved != null && saved.Count >= 2) ? saved : fallback;
        else _curvePoints = (saved != null && saved.Count >= 2) ? saved : fallback;
      }
      if (_curvePoints != null || _curvePointsGPU != null || _fan3CurveActive) DrawFanCurve();
    }

    // ponytail: 「应用」按钮 —— 解决 smart 模式下新曲线"切配置才生效"的问题:
    // GetSmartFanSpeed 的迟滞(温度变化<Hysteresis && lastApplied>0 → 锁旧转速)在
    // 温度未变时锁死旧值。此 handler 保存曲线 + 重置智能状态(EMA/lastApplied 清零)
    // + 立即触发一拍心跳 → 新曲线按当前温度立刻算出转速写入 EC。fan3 tab 时
    // SaveCurve 走 fan3 通道并刷新 Fan3TempFanMap,下一拍同样立即生效。
    void FanCurveApply_Click(object sender, RoutedEventArgs e) {
      SaveCurve();
      if (FanModeCombo.SelectedIndex == 3)
        FanService.InitSmartFanState(ConfigService.SmartFanEmaAlpha);
      // 立即一拍: Change(0,1000) 触发即时回调并保持 1s 周期(对正在运行的 timer 幂等)。
      TrayService.fanControlTimer?.Change(0, 1000);
    }

    void ApplyCustomCurve() {
      FanService.ApplyCustomCurve(_curvePoints);
      if (_curvePointsGPU != null)
        FanService.ApplyCustomCurveGPU(_curvePointsGPU);
    }

    void DrawFanCurve() {
      FanCurveCanvas.Children.Clear();
      double w = FanCurveCanvas.ActualWidth;
      double h = FanCurveCanvas.ActualHeight;
      if (w <= 0 || h <= 0) {
        FanCurveCanvas.Dispatcher.BeginInvoke(new Action(() => {
          FanCurveCanvas.UpdateLayout();
          w = FanCurveCanvas.ActualWidth;
          h = FanCurveCanvas.ActualHeight;
          if (w > 0 && h > 0) DrawFanCurveInternal(w, h);
        }), DispatcherPriority.Loaded);
        return;
      }
      DrawFanCurveInternal(w, h);
    }

    void DrawFanCurveInternal(double w, double h) {
      FanCurveCanvas.Children.Clear();
      _polylineElement = null;
      _circleElements = null;
      var gridBrush = TryFindResource("ControlStrokeColorDefaultBrush") as Brush ?? Brushes.Gray;
      var lineBrush = TryFindResource("TextFillColorPrimaryBrush") as Brush ?? Brushes.White;
      var accentBrush = TryFindResource("SystemAccentColor") as Brush ?? Brushes.White;
      var mutedBrush = TryFindResource("TextFillColorSecondaryBrush") as Brush ?? Brushes.Gray;

      var points = _showGpuCurve ? _curvePointsGPU : _curvePoints;
      float currentTemp = _showGpuCurve ? HardwareService.GPUTemp : HardwareService.CPUTemp;

      double padL = CurvePadL, padR = CurvePadR, padT = CurvePadT, padB = CurvePadB;
      double chartW = w - padL - padR;
      double chartH = h - padT - padB;

      // ponytail: 刻度尺只画到 100°；105°C 是 cool 预设的兜底保命点，不画刻度线/标签。
      // 旧版从 t=0 起步，第一根虚线和 "0°" label 实际落在 padL 左侧画外（-0.25*chartW）。
      const float ScaleMaxTemp = 100;
      for (int t = (int)MinTemp; t <= (int)ScaleMaxTemp; t += 10) {
        double x = padL + (t - MinTemp) / (MaxTemp - MinTemp) * chartW;
        FanCurveCanvas.Children.Add(new Line { X1 = x, Y1 = padT, X2 = x, Y2 = padT + chartH, Stroke = gridBrush, StrokeThickness = 0.5, StrokeDashArray = new DoubleCollection { 4, 4 } });
        var label = new TextBlock { Text = t + "\u00b0", FontSize = 10, Foreground = mutedBrush };
        Canvas.SetLeft(label, x - 10); Canvas.SetTop(label, padT + chartH + 3);
        FanCurveCanvas.Children.Add(label);
      }
      for (int rpm = 0; rpm <= (int)MaxRPM; rpm += 1600) {
        double y = padT + chartH - (rpm / MaxRPM) * chartH;
        FanCurveCanvas.Children.Add(new Line { X1 = padL, Y1 = y, X2 = padL + chartW, Y2 = y, Stroke = gridBrush, StrokeThickness = 0.5, StrokeDashArray = new DoubleCollection { 4, 4 } });
        var label = new TextBlock { Text = rpm.ToString(), FontSize = 9, Foreground = mutedBrush };
        Canvas.SetLeft(label, padL - 3); Canvas.SetTop(label, y - 12);
        FanCurveCanvas.Children.Add(label);
      }

      if (points == null || points.Count == 0) return;
      var sorted = points.OrderBy(p => p.temp).ToList();
      _polylineElement = new Polyline { Stroke = lineBrush, StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round };
      foreach (var pt in sorted) {
        double x = padL + (pt.temp - MinTemp) / (MaxTemp - MinTemp) * chartW;
        double y = padT + chartH - (pt.rpm / MaxRPM) * chartH;
        _polylineElement.Points.Add(new Point(x, y));
      }
      FanCurveCanvas.Children.Add(_polylineElement);

      _circleElements = new List<Ellipse>();
      for (int i = 0; i < sorted.Count; i++) {
        double x = padL + (sorted[i].temp - MinTemp) / (MaxTemp - MinTemp) * chartW;
        double y = padT + chartH - (sorted[i].rpm / MaxRPM) * chartH;
        var circle = new Ellipse { Width = CurvePointRadius * 2, Height = CurvePointRadius * 2, Fill = accentBrush, Stroke = lineBrush, StrokeThickness = 1.5, Cursor = Cursors.Hand, Tag = i };
        Canvas.SetLeft(circle, x - CurvePointRadius); Canvas.SetTop(circle, y - CurvePointRadius);
        FanCurveCanvas.Children.Add(circle);
        _circleElements.Add(circle);
      }

      if (currentTemp >= MinTemp && currentTemp <= MaxTemp) {
        double tx = padL + (currentTemp - MinTemp) / (MaxTemp - MinTemp) * chartW;
        FanCurveCanvas.Children.Add(new Line { X1 = tx, Y1 = padT, X2 = tx, Y2 = padT + chartH, Stroke = lineBrush, StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 2, 2 }, Opacity = 0.7 });
      }
    }

    void FanCurveCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
      var pos = e.GetPosition(FanCurveCanvas);
      double w = FanCurveCanvas.ActualWidth, h = FanCurveCanvas.ActualHeight;
      double padL = CurvePadL, padR = CurvePadR, padT = CurvePadT, padB = CurvePadB;
      double chartW = w - padL - padR, chartH = h - padT - padB;
      var points = _showGpuCurve ? _curvePointsGPU : _curvePoints;
      if (points == null) return;
      var sorted = points.OrderBy(p => p.temp).ToList();
      for (int i = 0; i < sorted.Count; i++) {
        double px = padL + (sorted[i].temp - MinTemp) / (MaxTemp - MinTemp) * chartW;
        double py = padT + chartH - (sorted[i].rpm / MaxRPM) * chartH;
        if (Math.Abs(pos.X - px) < 15 && Math.Abs(pos.Y - py) < 15) {
          _draggingIndex = i;
          FanCurveCanvas.CaptureMouse();
          e.Handled = true;
          return;
        }
      }
    }

    void FanCurveCanvas_MouseMove(object sender, MouseEventArgs e) {
      var points = _showGpuCurve ? _curvePointsGPU : _curvePoints;
      if (_draggingIndex < 0 || points == null) return;
      var pos = e.GetPosition(FanCurveCanvas);
      double w = FanCurveCanvas.ActualWidth, h = FanCurveCanvas.ActualHeight;
      double padL = CurvePadL, padR = CurvePadR, padT = CurvePadT, padB = CurvePadB;
      double chartW = w - padL - padR, chartH = h - padT - padB;
      var sorted = points.OrderBy(p => p.temp).ToList();
      float newTemp = (float)((pos.X - padL) / chartW * (MaxTemp - MinTemp) + MinTemp);
      float newRpm = (float)((padT + chartH - pos.Y) / chartH * MaxRPM);
      float minT = _draggingIndex > 0 ? sorted[_draggingIndex - 1].temp + 1 : MinTemp;
      float maxT = _draggingIndex < sorted.Count - 1 ? sorted[_draggingIndex + 1].temp - 1 : MaxTemp;
      newTemp = Math.Max(minT, Math.Min(maxT, newTemp));
      newRpm = Math.Max(500, Math.Min(MaxRPM, newRpm));
      newTemp = (float)Math.Round(newTemp);
      newRpm = (float)(Math.Round(newRpm / 100) * 100);
      sorted[_draggingIndex] = ((float)newTemp, (int)newRpm);
      if (_showGpuCurve) _curvePointsGPU = sorted; else _curvePoints = sorted;
      // ponytail: 拖拽时只更新 UI 元素，不写硬件和 fan maps。
      // ApplyCustomCurve + GetSmartFanSpeed 在每帧 ~60 次鼠标事件下
      // 累积成秒级延迟（EMA 计算 + 锁 + WMI 调用）。硬件写入交给
      // MouseUp 一次性完成。
      if (_polylineElement != null && _circleElements != null && _draggingIndex < _circleElements.Count) {
        double px = padL + (newTemp - MinTemp) / (MaxTemp - MinTemp) * chartW;
        double py = padT + chartH - (newRpm / MaxRPM) * chartH;
        _polylineElement.Points[_draggingIndex] = new Point(px, py);
        Canvas.SetLeft(_circleElements[_draggingIndex], px - CurvePointRadius);
        Canvas.SetTop(_circleElements[_draggingIndex], py - CurvePointRadius);
      }
    }

    void FanCurveCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) {
      if (_draggingIndex >= 0) {
        _draggingIndex = -1;
        FanCurveCanvas.ReleaseMouseCapture();
        // ponytail: 编辑期间不自动保存/应用 —— 统一走「应用」按钮(SaveCurve),
        // 否则 smart 迟滞锁旧转速,用户误以为新曲线无效(切配置才生效的由来)。
      }
    }

    void FanCurveCanvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e) {
      var points = _showGpuCurve ? _curvePointsGPU : _curvePoints;
      if (points == null) return;
      var pos = e.GetPosition(FanCurveCanvas);
      double w = FanCurveCanvas.ActualWidth, h = FanCurveCanvas.ActualHeight;
      double padL = CurvePadL, padR = CurvePadR, padT = CurvePadT, padB = CurvePadB;
      double chartW = w - padL - padR, chartH = h - padT - padB;
      if (chartW <= 0 || chartH <= 0) return;
      var sorted = points.OrderBy(p => p.temp).ToList();
      for (int i = 0; i < sorted.Count; i++) {
        double px = padL + (sorted[i].temp - MinTemp) / (MaxTemp - MinTemp) * chartW;
        double py = padT + chartH - (sorted[i].rpm / MaxRPM) * chartH;
        if (Math.Abs(pos.X - px) < 15 && Math.Abs(pos.Y - py) < 15) {
          if (sorted.Count <= 2) return;
          sorted.RemoveAt(i);
          if (_showGpuCurve) _curvePointsGPU = sorted; else _curvePoints = sorted;
          DrawFanCurve(); e.Handled = true; return;   // ponytail: 删除不自动保存,走「应用」按钮
        }
      }
      float newTemp = (float)((pos.X - padL) / chartW * (MaxTemp - MinTemp) + MinTemp);
      float newRpm = (float)((padT + chartH - pos.Y) / chartH * MaxRPM);
      newTemp = (float)Math.Round(Math.Max(MinTemp, Math.Min(MaxTemp, newTemp)));
      newRpm = (float)(Math.Round(Math.Max(500, Math.Min(MaxRPM, newRpm)) / 100) * 100);
      for (int i = 0; i < sorted.Count; i++) { if (Math.Abs(sorted[i].temp - newTemp) < 3) return; }
      int insertIdx = 0;
      while (insertIdx < sorted.Count && sorted[insertIdx].temp < newTemp) insertIdx++;
      float minT2 = insertIdx > 0 ? sorted[insertIdx - 1].temp + 1 : MinTemp;
      float maxT2 = insertIdx < sorted.Count ? sorted[insertIdx].temp - 1 : MaxTemp;
      if (minT2 > maxT2) return;
      newTemp = Math.Max(minT2, Math.Min(maxT2, newTemp));
      sorted.Insert(insertIdx, (newTemp, (int)newRpm));
      if (_showGpuCurve) _curvePointsGPU = sorted; else _curvePoints = sorted;
      DrawFanCurve(); e.Handled = true;   // ponytail: 插入不自动保存,走「应用」按钮
    }

    void SaveCurve() {
      // ponytail: 第三扇 tab 激活时 —— _curvePoints 承载的是 fan3 编辑快照,
      // 写回 fan3 文件并刷新 Fan3TempFanMap(心跳 GetFan3Speed 消费),不碰 CPU/GPU 通道。
      if (_fan3CurveActive) {
        if (_curvePoints == null) return;
        FanService.SavePresetCurve(_currentPresetKey, _curvePoints, gpu: false, fan3: true);
        FanService.LoadFan3CurveIntoMap(_currentPresetKey);   // 重载查表
        return;
      }
      FanService.SavePresetCurve(_currentPresetKey, _curvePoints, false);
      FanService.SavePresetCurve(_currentPresetKey, _curvePointsGPU, true);
      if (_curvePoints != null) FanService.ApplyCustomCurve(_curvePoints);
      if (_curvePointsGPU != null) FanService.ApplyCustomCurveGPU(_curvePointsGPU);
    }

    // ── Import / Export / Share ──
    void FanExportBtn_Click(object sender, RoutedEventArgs e) {
      var points = _showGpuCurve ? _curvePointsGPU : _curvePoints;
      if (points == null || points.Count < 2) {
	        DialogHelper.Info(Strings.FanShareNoData, Strings.Hint);
        return;
      }
      var dlg = new Microsoft.Win32.SaveFileDialog {
        Title = Strings.FanCurveExportTitle,
        Filter = Strings.FanCurveFileFilter,
        DefaultExt = ".json",
        FileName = $"FanCurve_{( _showGpuCurve ? "GPU" : "CPU")}_{DateTime.Now:yyyyMMdd}.json"
      };
      if (dlg.ShowDialog() == true) {
        string name = _showGpuCurve ? "GPU Fan Curve" : "CPU Fan Curve";
        string json = FanService.ExportCurveToJson(points, name);
        if (!string.IsNullOrEmpty(json)) {
          try {
            File.WriteAllText(dlg.FileName, json, System.Text.Encoding.UTF8);
		        DialogHelper.Info(Strings.FanCurveExportSuccess + "\n" + dlg.FileName, Strings.HelpWindowTitleBar);
          } catch (Exception ex) {
            // ponytail: 引用代理3 报告 — 之前 File.WriteAllText 无保护，磁盘满/无权限/路径过长会崩溃
            DialogHelper.Error(Strings.FanCurveExportFailed + "\n" + ex.Message, Strings.HelpWindowTitleBar);
          }
		        } else {
		          DialogHelper.Error(Strings.FanCurveExportFailed, Strings.HelpWindowTitleBar);
        }
      }
    }

    void FanImportBtn_Click(object sender, RoutedEventArgs e) {
      // First try clipboard (share code check)
      string clip = null;
      try { clip = System.Windows.Clipboard.GetText(); } catch { }
      bool hasShareCode = !string.IsNullOrEmpty(clip) && clip.StartsWith("OXFC:", StringComparison.OrdinalIgnoreCase);

      if (hasShareCode) {
	        int r = DialogHelper.YesNoCancel(
	            Strings.FanShareCodeDetected(clip.Substring(0, Math.Min(clip.Length, 40)) + "..."),
	            Strings.FanCurveImportTitle);
        if (r == 1) {
          ImportFromCode(clip);
          return;
        } else if (r == 0) {
          return;
        }
      }

      // File import
      var dlg = new Microsoft.Win32.OpenFileDialog {
        Title = Strings.FanCurveImportTitle,
        Filter = Strings.FanCurveFileFilter,
        DefaultExt = ".json",
        Multiselect = false
      };
      if (dlg.ShowDialog() == true) {
        try {
          string json = File.ReadAllText(dlg.FileName, System.Text.Encoding.UTF8);
          ImportFromJson(json);
        } catch {
          DialogHelper.Error(Strings.FanCurveImportFailed, "OMEN X Hub");
        }
      }
    }

    void FanShareBtn_Click(object sender, RoutedEventArgs e) {
      var points = _showGpuCurve ? _curvePointsGPU : _curvePoints;
      if (points == null || points.Count < 2) {
	        DialogHelper.Info(Strings.FanShareNoDataToShare, Strings.Hint);
        return;
      }
      string name = _showGpuCurve ? "GPU" : "CPU";
      string code = FanService.GenerateShareCode(points, name);
      if (string.IsNullOrEmpty(code)) {
	        DialogHelper.Error(Strings.FanShareGenerateFail, Strings.HelpWindowTitleBar);
        return;
      }
      try {
        System.Windows.Clipboard.SetText(code);
        DialogHelper.Info(Strings.FanCurveShareCopied + "\n\n" + Strings.FanCurveShareGuide, "OMEN X Hub");
      } catch {
        // Clipboard may fail, show dialog with manual copy
        var dlg = new System.Windows.Window {
	          Title = Strings.FanShareWindowTitle,
          Width = 500, Height = 200,
          WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner,
          Owner = System.Windows.Window.GetWindow(this),
          Content = new System.Windows.Controls.StackPanel { Margin = new System.Windows.Thickness(16) }
        };
        // ponytail: 关闭前断开 Owner,避免 owned window 关闭把主窗口误最小化(通用弹窗 bug)
        OmenSuperHub.Utils.WindowHelper.DetachOwnerOnClose(dlg);
        var stack = dlg.Content as System.Windows.Controls.StackPanel;
	        stack.Children.Add(new System.Windows.Controls.TextBlock {
	          Text = Strings.FanShareCopyInstruction, FontSize = 13, Margin = new System.Windows.Thickness(0, 0, 0, 8)
        });
        var box = new System.Windows.Controls.TextBox {
          Text = code, IsReadOnly = true, FontSize = 11,
          FontFamily = new System.Windows.Media.FontFamily("Consolas"),
          TextWrapping = System.Windows.TextWrapping.Wrap
        };
        stack.Children.Add(box);
        var btn = new System.Windows.Controls.Button {
	          Content = Strings.FanShareClose, Width = 60, Height = 28, Margin = new System.Windows.Thickness(0, 8, 0, 0),
          HorizontalAlignment = System.Windows.HorizontalAlignment.Right
        };
        btn.Click += (s2, e2) => dlg.Close();
        stack.Children.Add(btn);
        dlg.ShowDialog();
      }
    }

    void ImportFromCode(string code) {
      var parsed = FanService.ParseShareCode(code);
      if (parsed == null) {
	        DialogHelper.Error(Strings.FanShareInvalidCode, Strings.HelpWindowTitleBar);
        return;
      }
      ApplyImportedCurve(parsed.Value.points, parsed.Value.name);
    }

	    void ImportFromJson(string json) {
	      var parsed = FanService.ImportCurveFromJson(json);
	      if (parsed == null) {
	        DialogHelper.Error(Strings.FanCurveImportFailed, Strings.HelpWindowTitleBar);
	        return;
	      }
	      ApplyImportedCurve(parsed.Value.points, parsed.Value.name);
	    }

    void ApplyImportedCurve(List<(float temp, int rpm)> points, string name) {
      if (_showGpuCurve) {
        _curvePointsGPU = points;
        FanService.SavePresetCurve(_currentPresetKey, _curvePointsGPU, true);
        FanService.ApplyCustomCurveGPU(_curvePointsGPU);
      } else {
        _curvePoints = points;
        FanService.SavePresetCurve(_currentPresetKey, _curvePoints, false);
        FanService.ApplyCustomCurve(_curvePoints);
      }
      DrawFanCurve();
      DialogHelper.Info(
          Strings.FanCurveImportSuccess + name + $" ({points.Count} 点)\n" +
          "拖拽控制点可进一步微调", "OMEN X Hub");
    }

    void CleanCreekBtn_Click(object sender, RoutedEventArgs e) {
      if (OmenHardware.IsLegacyCleanCreekSupported()) {
        if (DialogHelper.OkCancel(Strings.CleanCreekConfirmMessage, Strings.CleanCreekTitle)) {
          System.Threading.Tasks.Task.Run(async () => {
            OmenHardware.SetLegacyCleanCreek(true);
            await RunDustCleaningCountdownAsync();
            OmenHardware.SetLegacyCleanCreek(false);
          });
        }
      } else if (OmenHardware.IsCleanCreekSupported()) {
        if (DialogHelper.OkCancel(Strings.CleanCreekConfirmMessage, Strings.CleanCreekTitle)) {
          System.Threading.Tasks.Task.Run(async () => {
            // ponytail: 除尘期间暂停风扇心跳 —— 心跳每秒写正常转速,会覆盖 +128 逆转字节,
            // 30s 清灰流程里两者互相打架导致逆转时断时续。结束/异常均恢复心跳。
            TrayService.fanControlTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            try {
              // 三扇机第 3 扇(caps[2] 支持时)同样 +128 逆转;双风机 IsThreeFan()=false 载荷不变。
              SetFanLevel(0, 0, OmenHardware.IsThreeFan(), true);
              await RunDustCleaningCountdownAsync();
              SetFanLevel(0, 0);
            } finally {
              TrayService.fanControlTimer?.Change(0, 1000);
            }
          });
        }
      } else {
        DialogHelper.Info(Strings.CleanCreekUnsupported, Strings.Hint);
      }
    }

    // ponytail: 除尘 UI 反馈 —— 按钮禁用 + 状态文案每秒倒计时(UI 线程 Dispatcher 后台优先级),
    // 完成后恢复按钮与描述。除尘期间用户不再面对无响应的 30 秒黑盒。
    async System.Threading.Tasks.Task RunDustCleaningCountdownAsync() {
      await Dispatcher.InvokeAsync(() => {
        CleanCreekBtn.IsEnabled = false;
        CleanCreekDesc.Text = Strings.DustCleanRunning(30);
      });
      for (int remain = 29; remain >= 1; remain--) {
        await System.Threading.Tasks.Task.Delay(1000);
        int r = remain;
        await Dispatcher.InvokeAsync(() => CleanCreekDesc.Text = Strings.DustCleanRunning(r));
      }
      await Dispatcher.InvokeAsync(() => {
        CleanCreekBtn.IsEnabled = true;
        CleanCreekDesc.Text = Strings.DustCleanDesc;
      });
    }

    void SelectComboItem(ComboBox combo, string text) {
      foreach (ComboBoxItem item in combo.Items) {
        if (string.Equals(item.Content?.ToString(), text, StringComparison.Ordinal)) {
          item.IsSelected = true;
          return;
        }
      }
    }

    void SelectRpmComboItem(int rpm) {
      foreach (ComboBoxItem item in FanRpmCombo.Items) {
        if (item.Tag is int tagVal && tagVal == rpm) {
          item.IsSelected = true;
          return;
        }
      }
    }

    // ── 预设切换时同步应用风扇到硬件（不依赖 ThreadPool 异步 PresetManager.ApplyPresetHardware） ──
    void ApplyPresetFanConfig() {
      try {
        string fc = ConfigService.FanControl;
        string ft = ConfigService.FanTable;
        if (fc == "smart" || fc == "custom") {
          FanService.LoadFanConfig(
            ft == "cool" ? "cool.txt"
            : ft == "balanced" ? "balanced.txt"
            : "silent.txt");
          FanService.InitSmartFanState(ConfigService.SmartFanEmaAlpha);
          FanService.ApplyPresetCurve(ConfigService.Preset);
          SetMaxFanSpeedOff();
          TrayService.fanControlTimer.Change(0, 1000);
        } else if (fc != null && fc.Contains(" RPM")) {
          int rpm = FanService.ParseFanRpm(fc);
          SetMaxFanSpeedOff();
          SetFanLevel(0, 0, fan3: OmenHardware.IsThreeFan());
          SetFanLevel(rpm / 100, rpm / 100, fan3: OmenHardware.IsThreeFan());
          TrayService.fanControlTimer.Change(Timeout.Infinite, Timeout.Infinite);
        } else {
          string table = ft == "cool" ? "cool.txt"
                       : ft == "balanced" ? "balanced.txt"
                       : "silent.txt";
          FanService.LoadFanConfig(table);
          SetMaxFanSpeedOff();
          TrayService.fanControlTimer.Change(0, 1000);
        }
      } catch { }
    }

  }
}