// FanPage.cs - 风扇控制页面
// 风扇模式/灵敏度/曲线选择，自定义风扇曲线编辑，自动保护和除尘功能
using System;
using System.Collections.Generic;
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
    int _draggingIndex = -1;
    List<(float temp, int rpm)> _curvePoints;
    List<(float temp, int rpm)> _curvePointsGPU;
    int _initRpm;

    public FanPage() {
      try { InitializeComponent(); } catch (Exception ex) {
        System.Windows.MessageBox.Show("FanPage Init: " + ex.GetType().Name + "\n" + ex.Message + "\n" + (ex.InnerException?.Message ?? ""));
      }
      FanCurveCanvas.SizeChanged += (s, e) => { if (_curvePoints != null) DrawFanCurve(); };
      Loaded += FanPage_Loaded;
    }

    private void FanPage_Loaded(object sender, RoutedEventArgs e) {
      try {
      BuildFanRpmOptions();
      LoadCurvePoints();
      _loading = true;
      LoadConfigState();
      _loading = false;
      UpdateFanModeUI();
      if (_initRpm > 0 && FanModeCombo.SelectedIndex == 3) SelectRpmComboItem(_initRpm);
      } catch (Exception ex) {
        System.Windows.MessageBox.Show("FanPage error: " + ex.GetType().Name + "\n" + ex.Message + "\n" + (ex.InnerException?.Message ?? ""));
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

    void LoadConfigState() {
      try {
      string fc = ConfigService.FanControl;
      if (fc == "custom") FanModeCombo.SelectedIndex = 2;
      else if (fc == "smart") FanModeCombo.SelectedIndex = 3;
      else if (fc == "" || fc == "auto" || fc == "silent" || fc == "cool") {
        FanModeCombo.SelectedIndex = ConfigService.FanTable == "cool" ? 1 : 0;
      } else if (fc.Contains(" RPM")) {
        FanModeCombo.SelectedIndex = 4;
        _initRpm = int.Parse(fc.Replace(" RPM", "").Trim());
        FanRpmSlider.Value = _initRpm;
        if (FanRpmInput != null) FanRpmInput.Text = _initRpm.ToString();
        if (FanRpmSliderVal != null) FanRpmSliderVal.Text = _initRpm + " RPM";
      } else if (fc.EndsWith("%")) {
        FanModeCombo.SelectedIndex = 4;
        int pct = int.Parse(fc.TrimEnd('%'));
        _initRpm = pct * 100;
        FanRpmSlider.Value = _initRpm;
        if (FanRpmInput != null) FanRpmInput.Text = _initRpm.ToString();
        if (FanRpmSliderVal != null) FanRpmSliderVal.Text = _initRpm + " RPM";
      } else {
        FanModeCombo.SelectedIndex = 4;
        _initRpm = 2500;
        FanRpmSlider.Value = _initRpm;
        if (FanRpmInput != null) FanRpmInput.Text = _initRpm.ToString();
        if (FanRpmSliderVal != null) FanRpmSliderVal.Text = _initRpm + " RPM";
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
      SmartCurvePreset.SelectedIndex = eaIdx;
      } catch { }
    }

    void UpdateFanModeUI() {
      int mode = FanModeCombo.SelectedIndex;
      bool isCustom = mode == 2;
      bool isSmart = mode == 3;
      bool isManual = mode == 4;
      bool isAuto = (mode == 0 || mode == 1);
      FanCurveCard.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;
      SmartFanCard.Visibility = isSmart ? Visibility.Visible : Visibility.Collapsed;
      ManualControlCard.Visibility = isManual ? Visibility.Visible : Visibility.Collapsed;
      TempSensCard.Visibility = (isAuto || isCustom) ? Visibility.Visible : Visibility.Collapsed;
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
        ConfigService.FanControl = "custom";
      } else if (mode == 3) {
        ConfigService.FanControl = "smart";
        string curveFile = ConfigService.FanTable == "cool" ? "cool.txt" : "silent.txt";
        FanService.LoadFanConfig(curveFile);
        FanService.InitSmartFanState(ConfigService.SmartFanEmaAlpha);
        SetMaxFanSpeedOff();
        TrayService.fanControlTimer.Change(0, 1000);
      } else if (mode == 4) {
        ConfigService.FanControl = "2500 RPM";
        SetMaxFanSpeedOff();
        TrayService.fanControlTimer.Change(Timeout.Infinite, Timeout.Infinite);
        FanRpmSlider.Value = 2500;
      }
      _loading = true;
      UpdateFanModeUI();
      _loading = false;
      ConfigService.Save("FanControl");
      ConfigService.Save("FanTable");
      if (mode == 0) {
        Views.OsdWindow.ShowFanModeOsd("silent");
        Dispatcher.BeginInvoke(new Action(() => {
          FanService.LoadFanConfig("silent.txt");
          SetMaxFanSpeedOff();
          TrayService.fanControlTimer.Change(0, 1000);
        }), DispatcherPriority.Background);
      } else if (mode == 1) {
        Views.OsdWindow.ShowFanModeOsd("cool");
        Dispatcher.BeginInvoke(new Action(() => {
          FanService.LoadFanConfig("cool.txt");
          SetMaxFanSpeedOff();
          TrayService.fanControlTimer.Change(0, 1000);
        }), DispatcherPriority.Background);
      } else if (mode == 2) {
        Views.OsdWindow.ShowFanModeOsd("custom");
        SetMaxFanSpeedOff();
        Dispatcher.BeginInvoke(new Action(() => {
          LoadCurvePoints();
          ApplyCustomCurve();
          TrayService.fanControlTimer.Change(0, 1000);
        }), DispatcherPriority.Background);
      } else if (mode == 3) {
        Views.OsdWindow.ShowFanModeOsd("smart");
      } else if (mode == 4) {
        Views.OsdWindow.ShowFanModeOsd(ConfigService.FanControl);
        TrayService.fanControlTimer.Change(Timeout.Infinite, Timeout.Infinite);
        int rpm = 2500;
        int.TryParse(ConfigService.FanControl.Replace(" RPM", "").Trim(), out rpm);
        SetFanLevel(0, 0);
        SetFanLevel(rpm / 100, rpm / 100);
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
    }

    void FanSyncToggle_Changed(object sender, RoutedEventArgs e) {
      ConfigService.FanSync = FanSyncToggle.IsChecked == true;
      ConfigService.Save("FanSync");
    }

    void SmartCurvePreset_Changed(object s, SelectionChangedEventArgs e) {
      if (_loading) return;
      int idx = SmartCurvePreset.SelectedIndex;
      float[] alphas = { 0.1f, 0.3f, 0.5f };
      int[] stepDowns = { 100, 300, 500 };
      float[] hysts = { 0.2f, 0.5f, 1.0f };
      if (idx >= 0 && idx < alphas.Length) {
        ConfigService.SmartFanEmaAlpha = alphas[idx];
        ConfigService.SmartFanStepDownRate = stepDowns[idx];
        ConfigService.SmartFanHysteresis = hysts[idx];
        ConfigService.Save("SmartFanEmaAlpha");
        ConfigService.Save("SmartFanStepDownRate");
        ConfigService.Save("SmartFanHysteresis");
        FanService.InitSmartFanState(ConfigService.SmartFanEmaAlpha);
        _loading = true;
        SmartEmaAlphaCombo.SelectedIndex = idx;
        SmartStepDownCombo.SelectedIndex = idx;
        SmartHysteresisCombo.SelectedIndex = idx;
        _loading = false;
      }
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

    void FanRpmExpand_Changed(object s, RoutedEventArgs e) {
      AnimatedExpand(FanRpmExtra, FanRpmExpand.IsChecked == true);
    }

    void FanRpmCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) {
      if (_loading) return;
      var item = FanRpmCombo.SelectedItem as ComboBoxItem;
      if (item == null) return;
      int rpm = (int)item.Tag;
      FanRpmSlider.Value = rpm;
      FanRpmInput.Text = rpm.ToString();
      SetMaxFanSpeedOff();
      SetFanLevel(0, 0);
      SetFanLevel(rpm / 100, rpm / 100);
      ConfigService.FanControl = rpm + " RPM";
      ConfigService.Save("FanControl");
    }

    void FanRpmSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) {
      if (_loading) return;
      int rpm = (int)e.NewValue;
      if (FanRpmSliderVal != null) FanRpmSliderVal.Text = rpm + " RPM";
      if (FanRpmInput != null) FanRpmInput.Text = rpm.ToString();
    }

    void FanRpmApply_Click(object sender, RoutedEventArgs e) {
      if (!int.TryParse(FanRpmInput.Text, out int rpm) || rpm < 500 || rpm > 6000) return;
      FanRpmSlider.Value = rpm;
      ConfigService.FanControl = rpm + " RPM";
      SetMaxFanSpeedOff();
      SetFanLevel(0, 0);
      SetFanLevel(rpm / 100, rpm / 100);
      ConfigService.Save("FanControl");
      SelectComboItem(FanRpmCombo, rpm + " RPM");
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
        FanCurveCanvas.Children.Add(new Line { X1 = tx, Y1 = padT, X2 = tx, Y2 = padT + chartH, Stroke = accentBrush, StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 2, 2 }, Opacity = 0.7 });
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
      if (_showGpuCurve) FanService.SaveCustomCurveGPU(_curvePointsGPU);
      else FanService.SaveCustomCurve(_curvePoints);
      if (_curvePoints != null) FanService.ApplyCustomCurve(_curvePoints);
      if (_curvePointsGPU != null) FanService.ApplyCustomCurveGPU(_curvePointsGPU);
    }

    void CleanCreekBtn_Click(object sender, RoutedEventArgs e) {
      if (OmenHardware.IsLegacyCleanCreekSupported()) {
        var confirm = System.Windows.MessageBox.Show(Strings.CleanCreekConfirmMessage, Strings.CleanCreekTitle,
             MessageBoxButton.OKCancel, MessageBoxImage.Information);
        if (confirm == MessageBoxResult.OK) {
          System.Threading.Tasks.Task.Run(async () => {
            OmenHardware.SetLegacyCleanCreek(true);
            await System.Threading.Tasks.Task.Delay(30000);
            OmenHardware.SetLegacyCleanCreek(false);
          });
        }
      } else if (OmenHardware.IsCleanCreekSupported()) {
        var confirm = System.Windows.MessageBox.Show(Strings.CleanCreekConfirmMessage, Strings.CleanCreekTitle,
             MessageBoxButton.OKCancel, MessageBoxImage.Information);
        if (confirm == MessageBoxResult.OK) {
          System.Threading.Tasks.Task.Run(async () => {
            SetFanLevel(0, 0, false, true);
            await System.Threading.Tasks.Task.Delay(30000);
            SetFanLevel(0, 0);
          });
        }
      } else {
        System.Windows.MessageBox.Show(Strings.CleanCreekUnsupported, Strings.Hint,
            MessageBoxButton.OK, MessageBoxImage.Information);
      }
    }

    void AnimatedExpand(FrameworkElement panel, bool expand) {
      panel.BeginAnimation(UIElement.OpacityProperty, null);
      double dur = 0.2;
      if (expand) {
        panel.Visibility = Visibility.Visible;
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(dur)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        fadeIn.FillBehavior = FillBehavior.HoldEnd;
        panel.BeginAnimation(UIElement.OpacityProperty, fadeIn);
      } else {
        panel.Opacity = 1;
        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(dur)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
        fadeOut.FillBehavior = FillBehavior.HoldEnd;
        fadeOut.Completed += (s, a) => {
          panel.Visibility = Visibility.Collapsed;
          panel.BeginAnimation(UIElement.OpacityProperty, null);
          panel.Opacity = 1;
        };
        panel.BeginAnimation(UIElement.OpacityProperty, fadeOut);
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

  }
}