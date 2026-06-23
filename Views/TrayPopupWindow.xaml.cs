using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OmenSuperHub.Services;
using Wpf.Ui.Controls;

namespace OmenSuperHub.Views {
  public partial class TrayPopupWindow : Window {
    public TrayPopupWindow() {
      InitializeComponent();
    }

    public void UpdateContent() {
      // ── Preset / performance mode header ──
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
      PresetText.Text = string.IsNullOrEmpty(display)
        ? $"{Strings.PerfModeLabel}: --"
        : $"{Strings.PerfModeLabel}: {display}";

      // ── Metric rows ──
      MetricPanel.Children.Clear();

      string cpuVal = HardwareService.CPUTemp > 0.01f
        ? $"{HardwareService.CPUTemp:F0}°C  ·  {HardwareService.CPUPower:F0}W"
        : "--";
      AddRow(SymbolRegular.DeveloperBoard24, Strings.MonitorCpuLabel, cpuVal);

      if (ConfigService.MonitorGPU) {
        string gpuVal = HardwareService.GPUPower >= 0.01f
          ? $"{HardwareService.GPUTemp:F0}°C  ·  {HardwareService.GPUPower:F0}W"
          : "--";
        AddRow(SymbolRegular.DeviceEq24, Strings.MonitorGpuLabel, gpuVal);
      }

      if (HardwareService.MonitorFan && HardwareService.FanSpeedNow != null) {
        string fanVal = $"{HardwareService.FanSpeedNow[0] * 100} / {HardwareService.FanSpeedNow[1] * 100} RPM";
        AddRow(SymbolRegular.ArrowSync24, Strings.MonitorFanLabel, fanVal);
      }
    }

    // Build a single metric row: [icon] [label] .... [value]
    void AddRow(SymbolRegular icon, string label, string value) {
      var secondary = Application.Current.TryFindResource("TextFillColorSecondaryBrush") as Brush ?? Brushes.Gray;
      var primary = Application.Current.TryFindResource("ContextMenuForeground") as Brush ?? Brushes.White;
      var font = new FontFamily("Microsoft YaHei UI, Segoe UI");

      var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
      row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
      row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
      row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
      row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

      var sym = new SymbolIcon {
        Symbol = icon, FontSize = 14,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, 0, 8, 0),
        Foreground = secondary
      };
      Grid.SetColumn(sym, 0);
      row.Children.Add(sym);

      var lbl = new System.Windows.Controls.TextBlock {
        Text = label, FontSize = 11, VerticalAlignment = VerticalAlignment.Center,
        Foreground = secondary, FontFamily = font
      };
      Grid.SetColumn(lbl, 1);
      row.Children.Add(lbl);

      var val = new System.Windows.Controls.TextBlock {
        Text = value, FontSize = 12, FontWeight = FontWeights.SemiBold,
        VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right,
        Foreground = primary, FontFamily = font
      };
      Grid.SetColumn(val, 3);
      row.Children.Add(val);

      MetricPanel.Children.Add(row);
    }
  }
}
