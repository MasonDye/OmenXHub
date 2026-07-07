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
    const float MaxTemp = 100;
    const float MaxRPM = 6400;
    const double CurvePadL = 32, CurvePadR = 16, CurvePadT = 22, CurvePadB = 28;

    bool _loading;
    bool _showGpuCurve;
    bool _optionsBuilt;
    int _draggingIndex = -1;
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
      // only the curve workspace (mode 2) shows preset curves; modes 0/1/3 have no UI
      // surface for per-preset curve files.
      if (FanModeCombo.SelectedIndex != 2) return;
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
      if (!_optionsBuilt) { BuildFanRpmOptions(); _optionsBuilt = true; }
      RefreshPresetList();
      LoadCurvePoints();
      _loading = true;
      LoadConfigState();
      UpdateFanModeUI();
      if (FanModeCombo.SelectedIndex == 2) {
        _currentPresetKey = ConfigService.Preset;
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
      if (fc == "smart" || fc == "custom") FanModeCombo.SelectedIndex = 2;
      else if (fc == "" || fc == "auto" || fc == "silent" || fc == "cool") {
        FanModeCombo.SelectedIndex = ConfigService.FanTable == "cool" ? 1 : 0;
      } else if (fc.Contains(" RPM")) {
        FanModeCombo.SelectedIndex = 3;
        _initRpm = int.Parse(fc.Replace(" RPM", "").Trim());
        FanRpmSlider.Value = _initRpm;
      } else if (fc.EndsWith("%")) {
        FanModeCombo.SelectedIndex = 3;
        int pct = int.Parse(fc.TrimEnd('%'));
        _initRpm = pct * 100;
        FanRpmSlider.Value = _initRpm;
      } else {
        FanModeCombo.SelectedIndex = 3;
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
      float ea = ConfigService.SmartFanEmaAlpha;
      int eaIdx = ea <= 0.15f ? 0 : ea <= 0.4f ? 1 : 2;
      SmartEmaAlphaCombo.SelectedIndex = eaIdx;
      int sd = ConfigService.SmartFanStepDownRate;
      SmartStepDownCombo.SelectedIndex = sd <= 100 ? 0 : sd <= 300 ? 1 : sd <= 500 ? 2 : 3;
      float hy = ConfigService.SmartFanHysteresis;
      SmartHysteresisCombo.SelectedIndex = hy <= 0.2f ? 0 : hy <= 0.5f ? 1 : 2;
      } catch { }
    }

    void UpdateFanModeUI() {
      int mode = FanModeCombo.SelectedIndex;
      bool isSmartCurve = mode == 2;
      bool isManual = mode == 3;
      bool isAuto = (mode == 0 || mode == 1);
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
        ConfigService.FanControl = "smart";
        string curveFile = ConfigService.FanTable == "cool" ? "cool.txt" : "silent.txt";
        FanService.LoadFanConfig(curveFile);
        FanService.InitSmartFanState(ConfigService.SmartFanEmaAlpha);
        _currentPresetKey = ConfigService.Preset;
        LoadPresetCurvePoints(_currentPresetKey);
        SetMaxFanSpeedOff();
        TrayService.fanControlTimer.Change(0, 1000);
} else if (mode == 3) {
      // ponytail: parse existing FanControl for the current manual RPM instead of
      // hardcoding 2500 — otherwise switching back to manual mode always resets
      // the slider to 2500, regardless of what the user set before or what the
      // current preset stores.
      if (!int.TryParse(ConfigService.FanControl?.Replace(" RPM", "").Trim(), out int rpm) || rpm < 500 || rpm > 6000)
        rpm = 2500;
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
        Views.OsdWindow.ShowFanModeOsd("smart");
        SetMaxFanSpeedOff();
        FanService.ApplyCustomCurve(_curvePoints);
        if (_curvePointsGPU != null) FanService.ApplyCustomCurveGPU(_curvePointsGPU);
        TrayService.fanControlTimer.Change(0, 1000);
      } else if (mode == 3) {
        Views.OsdWindow.ShowFanModeOsd(ConfigService.FanControl);
        TrayService.fanControlTimer.Change(Timeout.Infinite, Timeout.Infinite);
        int rpm = 2500;
        int.TryParse(ConfigService.FanControl.Replace(" RPM", "").Trim(), out rpm);
        SetFanLevel(0, 0);
        SetFanLevel(rpm / 100, rpm / 100);
      }
      // ponytail: fan mode is part of the preset snapshot (FanControl/FanTable).
      // If the current preset is a custom preset, write the new fan mode back into
      // its JSON/registry so that switching away and back doesn't revert to the
      // old value.  Without this, changing the fan mode in the fan page felt like
      // it only took effect after the next preset switch kicked ApplyPresetHardware,
      // and switching presets could silently restore a stale fan mode.
      // Built-in presets (Extreme/GpuPriority/LightUse) are fixed — don't overwrite.
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

    void SmartEmaAlpha_Changed(object s, SelectionChangedEventArgs e) {
      if (_loading) return;
      float[] vals = { 0.1f, 0.3f, 0.5f };
      int idx = SmartEmaAlphaCombo.SelectedIndex;
      if (idx >= 0) { ConfigService.SmartFanEmaAlpha = vals[idx]; ConfigService.Save("SmartFanEmaAlpha"); FanService.InitSmartFanState(ConfigService.SmartFanEmaAlpha); }
    }

    void SmartStepDown_Changed(object s, SelectionChangedEventArgs e) {
      if (_loading) return;
      int[] vals = { 100, 300, 500, 1000 };
      int idx = SmartStepDownCombo.SelectedIndex;
      if (idx >= 0) { ConfigService.SmartFanStepDownRate = vals[idx]; ConfigService.Save("SmartFanStepDownRate"); }
    }

    void SmartHysteresis_Changed(object s, SelectionChangedEventArgs e) {
      if (_loading) return;
      float[] vals = { 0.2f, 0.5f, 1.0f };
      int idx = SmartHysteresisCombo.SelectedIndex;
      if (idx >= 0) { ConfigService.SmartFanHysteresis = vals[idx]; ConfigService.Save("SmartFanHysteresis"); }
    }

    void BuildFanRpmOptions() {
      FanRpmCombo.Items.Clear();
      int[] rpms = { 1500, 2000, 2500, 3000, 3500, 4000, 4500, 5000, 5500, 6000 };
      foreach (int r in rpms)
        FanRpmCombo.Items.Add(new ComboBoxItem { Content = r + " RPM", Tag = r });
    }

    void FanRpmCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) {
      if (_loading) return;
      if (FanModeCombo.SelectedIndex != 3) return;
      _loading = true;
      var item = FanRpmCombo.SelectedItem as ComboBoxItem;
      if (item == null) { _loading = false; return; }
      int rpm = (int)item.Tag;
      FanRpmSlider.Value = rpm;
      SetMaxFanSpeedOff();
      SetFanLevel(0, 0);
      SetFanLevel(rpm / 100, rpm / 100);
      ConfigService.FanControl = rpm + " RPM";
      ConfigService.Save("FanControl");
      // ponytail: per-mode-value writeback for custom presets (see FanMode_SelectionChanged)
      if (PresetManager.IsCustom(ConfigService.Preset)) {
        PresetManager.SaveCustomPreset(ConfigService.Preset);
      }
      _loading = false;
    }

    void FanRpmNum_ValueChanged(object s, RoutedEventArgs e) {
      if (_loading) return;
      if (FanModeCombo.SelectedIndex != 3) return;
      _loading = true;
      double? val = FanRpmNum.Value;
      if (val == null || val < 500 || val > 6000) { _loading = false; return; }
      int rpm = (int)val;
      SetMaxFanSpeedOff();
      SetFanLevel(0, 0);
      SetFanLevel(rpm / 100, rpm / 100);
      ConfigService.FanControl = rpm + " RPM";
      ConfigService.Save("FanControl");
      SelectComboItem(FanRpmCombo, rpm + " RPM");
      // ponytail: per-mode-value writeback for custom presets
      if (PresetManager.IsCustom(ConfigService.Preset)) {
        PresetManager.SaveCustomPreset(ConfigService.Preset);
      }
      _loading = false;
    }

    void FanCurveSel_Changed(object s, SelectionChangedEventArgs e) {
      if (!IsLoaded) return;
      _showGpuCurve = FanCurveSel.SelectedIndex == 1;
      if (_curvePoints != null || _curvePointsGPU != null) DrawFanCurve();
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
      var gridBrush = TryFindResource("ControlStrokeColorDefaultBrush") as Brush ?? Brushes.Gray;
      var lineBrush = TryFindResource("TextFillColorPrimaryBrush") as Brush ?? Brushes.White;
      var accentBrush = TryFindResource("SystemAccentColor") as Brush ?? Brushes.White;
      var mutedBrush = TryFindResource("TextFillColorSecondaryBrush") as Brush ?? Brushes.Gray;

      var points = _showGpuCurve ? _curvePointsGPU : _curvePoints;
      float currentTemp = _showGpuCurve ? HardwareService.GPUTemp : HardwareService.CPUTemp;

      double padL = CurvePadL, padR = CurvePadR, padT = CurvePadT, padB = CurvePadB;
      double chartW = w - padL - padR;
      double chartH = h - padT - padB;

      for (int t = 0; t <= 100; t += 20) {
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
      var polyline = new Polyline { Stroke = lineBrush, StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round };
      foreach (var pt in sorted) {
        double x = padL + (pt.temp - MinTemp) / (MaxTemp - MinTemp) * chartW;
        double y = padT + chartH - (pt.rpm / MaxRPM) * chartH;
        polyline.Points.Add(new Point(x, y));
      }
      FanCurveCanvas.Children.Add(polyline);

      for (int i = 0; i < sorted.Count; i++) {
        double x = padL + (sorted[i].temp - MinTemp) / (MaxTemp - MinTemp) * chartW;
        double y = padT + chartH - (sorted[i].rpm / MaxRPM) * chartH;
        var circle = new Ellipse { Width = CurvePointRadius * 2, Height = CurvePointRadius * 2, Fill = accentBrush, Stroke = lineBrush, StrokeThickness = 1.5, Cursor = Cursors.Hand, Tag = i };
        Canvas.SetLeft(circle, x - CurvePointRadius); Canvas.SetTop(circle, y - CurvePointRadius);
        FanCurveCanvas.Children.Add(circle);
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
      newRpm = Math.Max(0, Math.Min(MaxRPM, newRpm));
      newTemp = (float)Math.Round(newTemp);
      newRpm = (float)(Math.Round(newRpm / 100) * 100);
      sorted[_draggingIndex] = ((float)newTemp, (int)newRpm);
      if (_showGpuCurve) _curvePointsGPU = sorted; else _curvePoints = sorted;
      DrawFanCurve();
    }

    void FanCurveCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) {
      if (_draggingIndex >= 0) {
        _draggingIndex = -1;
        FanCurveCanvas.ReleaseMouseCapture();
        SaveCurve();
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
          DrawFanCurve(); SaveCurve(); e.Handled = true; return;
        }
      }
      float newTemp = (float)((pos.X - padL) / chartW * (MaxTemp - MinTemp) + MinTemp);
      float newRpm = (float)((padT + chartH - pos.Y) / chartH * MaxRPM);
      newTemp = (float)Math.Round(Math.Max(MinTemp, Math.Min(MaxTemp, newTemp)));
      newRpm = (float)(Math.Round(Math.Max(0, Math.Min(MaxRPM, newRpm)) / 100) * 100);
      for (int i = 0; i < sorted.Count; i++) { if (Math.Abs(sorted[i].temp - newTemp) < 3) return; }
      int insertIdx = 0;
      while (insertIdx < sorted.Count && sorted[insertIdx].temp < newTemp) insertIdx++;
      float minT2 = insertIdx > 0 ? sorted[insertIdx - 1].temp + 1 : MinTemp;
      float maxT2 = insertIdx < sorted.Count ? sorted[insertIdx].temp - 1 : MaxTemp;
      if (minT2 > maxT2) return;
      newTemp = Math.Max(minT2, Math.Min(maxT2, newTemp));
      sorted.Insert(insertIdx, (newTemp, (int)newRpm));
      if (_showGpuCurve) _curvePointsGPU = sorted; else _curvePoints = sorted;
      DrawFanCurve(); SaveCurve(); e.Handled = true;
    }

    void SaveCurve() {
      FanService.SavePresetCurve(_currentPresetKey, _curvePoints, false);
      FanService.SavePresetCurve(_currentPresetKey, _curvePointsGPU, true);
      if (_curvePoints != null) FanService.ApplyCustomCurve(_curvePoints);
      if (_curvePointsGPU != null) FanService.ApplyCustomCurveGPU(_curvePointsGPU);
    }

    // ── Import / Export / Share ──
    void FanExportBtn_Click(object sender, RoutedEventArgs e) {
      var points = _showGpuCurve ? _curvePointsGPU : _curvePoints;
      if (points == null || points.Count < 2) {
        DialogHelper.Info("当前无可导出的曲线数据", "提示");
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
          File.WriteAllText(dlg.FileName, json, System.Text.Encoding.UTF8);
          DialogHelper.Info(Strings.FanCurveExportSuccess + "\n" + dlg.FileName, "OMEN X Hub");
        } else {
          DialogHelper.Error(Strings.FanCurveExportFailed, "OMEN X Hub");
        }
      }
    }

    void FanImportBtn_Click(object sender, RoutedEventArgs e) {
      // First try clipboard (share code check)
      string clip = null;
      try { clip = System.Windows.Clipboard.GetText(); } catch { }
      bool hasShareCode = !string.IsNullOrEmpty(clip) && clip.StartsWith("OXFC:", StringComparison.OrdinalIgnoreCase);

      if (hasShareCode) {
        var result = System.Windows.MessageBox.Show(
            "检测到剪贴板中有分享码：\n" + clip.Substring(0, Math.Min(clip.Length, 40)) + "...\n\n点击「是」从分享码导入\n点击「否」选择文件导入",
            Strings.FanCurveImportTitle, MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (result == MessageBoxResult.Yes) {
          ImportFromCode(clip);
          return;
        } else if (result == MessageBoxResult.Cancel) {
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
        DialogHelper.Info("当前无可分享的曲线数据", "提示");
        return;
      }
      string name = _showGpuCurve ? "GPU" : "CPU";
      string code = FanService.GenerateShareCode(points, name);
      if (string.IsNullOrEmpty(code)) {
        DialogHelper.Error("生成分享码失败", "OMEN X Hub");
        return;
      }
      try {
        System.Windows.Clipboard.SetText(code);
        DialogHelper.Info(Strings.FanCurveShareCopied + "\n\n" + Strings.FanCurveShareGuide, "OMEN X Hub");
      } catch {
        // Clipboard may fail, show dialog with manual copy
        var dlg = new System.Windows.Window {
          Title = "分享码",
          Width = 500, Height = 200,
          WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner,
          Owner = System.Windows.Window.GetWindow(this),
          Content = new System.Windows.Controls.StackPanel { Margin = new System.Windows.Thickness(16) }
        };
        var stack = dlg.Content as System.Windows.Controls.StackPanel;
        stack.Children.Add(new System.Windows.Controls.TextBlock {
          Text = "手动复制以下分享码：", FontSize = 13, Margin = new System.Windows.Thickness(0, 0, 0, 8)
        });
        var box = new System.Windows.Controls.TextBox {
          Text = code, IsReadOnly = true, FontSize = 11,
          FontFamily = new System.Windows.Media.FontFamily("Consolas"),
          TextWrapping = System.Windows.TextWrapping.Wrap
        };
        stack.Children.Add(box);
        var btn = new System.Windows.Controls.Button {
          Content = "关闭", Width = 60, Height = 28, Margin = new System.Windows.Thickness(0, 8, 0, 0),
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
        DialogHelper.Error("无效的分享码", "OMEN X Hub");
        return;
      }
      ApplyImportedCurve(parsed.Value.points, parsed.Value.name);
    }

    void ImportFromJson(string json) {
      var parsed = FanService.ImportCurveFromJson(json);
      if (parsed == null) {
        DialogHelper.Error(Strings.FanCurveImportFailed, "OMEN X Hub");
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
            await System.Threading.Tasks.Task.Delay(30000);
            OmenHardware.SetLegacyCleanCreek(false);
          });
        }
      } else if (OmenHardware.IsCleanCreekSupported()) {
        if (DialogHelper.OkCancel(Strings.CleanCreekConfirmMessage, Strings.CleanCreekTitle)) {
          System.Threading.Tasks.Task.Run(async () => {
            SetFanLevel(0, 0, false, true);
            await System.Threading.Tasks.Task.Delay(30000);
            SetFanLevel(0, 0);
          });
        }
      } else {
        DialogHelper.Info(Strings.CleanCreekUnsupported, Strings.Hint);
      }
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
          FanService.LoadFanConfig(ft == "cool" ? "cool.txt" : "silent.txt");
          FanService.InitSmartFanState(ConfigService.SmartFanEmaAlpha);
          FanService.ApplyPresetCurve(ConfigService.Preset);
          SetMaxFanSpeedOff();
          TrayService.fanControlTimer.Change(0, 1000);
        } else if (fc != null && fc.Contains(" RPM")) {
          int rpm = 2500;
          int.TryParse(fc.Replace(" RPM", "").Trim(), out rpm);
          if (rpm < 500) rpm = 500; if (rpm > 6000) rpm = 6000;
          SetMaxFanSpeedOff();
          SetFanLevel(0, 0);
          SetFanLevel(rpm / 100, rpm / 100);
          TrayService.fanControlTimer.Change(Timeout.Infinite, Timeout.Infinite);
        } else {
          string table = ft == "cool" ? "cool.txt" : "silent.txt";
          FanService.LoadFanConfig(table);
          SetMaxFanSpeedOff();
          TrayService.fanControlTimer.Change(0, 1000);
        }
      } catch { }
    }

  }
}