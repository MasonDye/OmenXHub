// TrayPopupWindow.xaml.cs - 托盘悬停弹窗
// 鼠标悬停托盘图标时显示 CPU/GPU/风扇简要数据的轻量级弹窗
using System.Windows;
using System.Windows.Threading;
using OmenSuperHub.Services;

namespace OmenSuperHub.Views {
  public partial class TrayPopupWindow : Window {
    private DispatcherTimer _refreshTimer;

    public TrayPopupWindow() {
      InitializeComponent();
      _refreshTimer = new DispatcherTimer {
        Interval = System.TimeSpan.FromSeconds(1)
      };
      _refreshTimer.Tick += (s, e) => UpdateContent();
    }

    protected override void OnContentRendered(System.EventArgs e) {
      base.OnContentRendered(e);
      _refreshTimer.Start();
    }

    protected override void OnClosed(System.EventArgs e) {
      _refreshTimer.Stop();
      base.OnClosed(e);
    }

    public void UpdateContent() {
      // ── Preset ──
      string preset = ConfigService.Preset;
      string display = PresetDisplayName(preset);

      PresetText.Text = string.IsNullOrEmpty(display) ? "OMEN X HUB" : display;
      PerfModeLabel.Text = Strings.PerfModeLabel;
      PresetValueText.Text = string.IsNullOrEmpty(display) ? "--" : display;

      // ── CPU ──
      bool showCpu = HardwareService.MonitorCPU;
      CpuIcon.Visibility = showCpu ? Visibility.Visible : Visibility.Collapsed;
      CpuLabel.Visibility = showCpu ? Visibility.Visible : Visibility.Collapsed;
      CpuValueText.Visibility = showCpu ? Visibility.Visible : Visibility.Collapsed;
      if (showCpu) {
        CpuValueText.Text = HardwareService.CPUTemp > 0.01f
          ? $"{HardwareService.CPUTemp:F1}°C  {HardwareService.CPUPower:F1}W"
          : "--";
      }

      // ── GPU ──
      bool showGpu = ConfigService.MonitorGPU;
      GpuIcon.Visibility = showGpu ? Visibility.Visible : Visibility.Collapsed;
      GpuLabel.Visibility = showGpu ? Visibility.Visible : Visibility.Collapsed;
      GpuValueText.Visibility = showGpu ? Visibility.Visible : Visibility.Collapsed;
      if (showGpu) {
        GpuValueText.Text = HardwareService.GPUPower >= 0.01f
          ? $"{HardwareService.GPUTemp:F1}°C  {HardwareService.GPUPower:F1}W"
          : "--";
      }

      // ── Fan ──
      bool showFan = HardwareService.MonitorFan && HardwareService.FanSpeedNow != null;
      FanIcon.Visibility = showFan ? Visibility.Visible : Visibility.Collapsed;
      FanLabelText.Visibility = showFan ? Visibility.Visible : Visibility.Collapsed;
      FanValueText.Visibility = showFan ? Visibility.Visible : Visibility.Collapsed;
      if (showFan) {
        FanLabelText.Text = Strings.FanLabel.TrimEnd(':', ' ', '\u3001');
        FanValueText.Text = $"{HardwareService.FanSpeedNow[0] * 100}, {HardwareService.FanSpeedNow[1] * 100} RPM";
      }
    }  // UpdateContent

    static string PresetDisplayName(string key) {
      if (key == "Extreme") return Strings.PresetExtreme;
      if (key == "GpuPriority") return Strings.PresetGpuPriority;
      if (key == "LightUse") return Strings.PresetLightUse;
      return ConfigService.GetCustomPresetDisplayName(key);
    }
  }  // class TrayPopupWindow
}
