// TrayPopupWindow.xaml.cs - 托盘悬停弹窗
// 鼠标悬停托盘图标时显示 CPU/GPU/风扇简要数据的轻量级弹窗
using System;
using System.Windows;
using System.Windows.Controls;
using OmenSuperHub.Services;

namespace OmenSuperHub.Views {
  public partial class TrayPopupWindow : Window {
    public TrayPopupWindow() {
      InitializeComponent();
    }

    public void UpdateContent() {
      MetricPanel.Children.Clear();

      string preset = ConfigService.Preset;
      string display;
      switch (preset) {
        case "Extreme": display = Strings.PresetExtreme; break;
        case "GpuPriority": display = Strings.PresetGpuPriority; break;
        case "LightUse": display = Strings.PresetLightUse; break;
        case "Custom1": display = ConfigService.CustomPreset1Name; break;
        case "Custom2": display = ConfigService.CustomPreset2Name; break;
        case "Custom3": display = ConfigService.CustomPreset3Name; break;
        default: display = preset; break;
      }

      AddRow(MetricPanel, string.IsNullOrEmpty(display) ? $"{Strings.PerfModeLabel}: --" : $"{Strings.PerfModeLabel}: {display}");

      if (HardwareService.CPUTemp > 0.01f)
        AddRow(MetricPanel, $"CPU: {HardwareService.CPUTemp:F1}°C, {HardwareService.CPUPower:F1}W");
      else
        AddRow(MetricPanel, "CPU: --");

      if (ConfigService.MonitorGPU) {
        AddRow(MetricPanel, HardwareService.GPUPower >= 0.01f
          ? $"GPU: {HardwareService.GPUTemp:F1}°C, {HardwareService.GPUPower:F1}W"
          : "GPU: --");
      }

      if (HardwareService.MonitorFan && HardwareService.FanSpeedNow != null) {
        AddRow(MetricPanel, $"{Strings.FanLabel}: {HardwareService.FanSpeedNow[0] * 100}, {HardwareService.FanSpeedNow[1] * 100} RPM");
      }
    }

    void AddRow(Panel parent, string text) {
      parent.Children.Add(new TextBlock {
        Text = text,
        FontSize = 12,
        Foreground = TryFindResource("ContextMenuForeground") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.White,
        Margin = new Thickness(0, 1, 0, 1)
      });
    }
  }
}
