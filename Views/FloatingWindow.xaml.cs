using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using OmenSuperHub.Services;
using Forms = System.Windows.Forms;

namespace OmenSuperHub.Views {
  public partial class FloatingWindow : Window {
    private static List<FloatingWindow> _instances = new List<FloatingWindow>();

    private string _deviceName;

    // Win32 constants for click-through window
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll", SetLastError = true)]
    static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    public FloatingWindow(string deviceName) {
      _deviceName = deviceName;
      InitializeComponent();
      this.SourceInitialized += FloatingWindow_SourceInitialized;
      ThemeService.ThemeChanged += OnThemeChanged;
    }

    private void OnThemeChanged() {
      if (IsLoaded) Dispatcher.BeginInvoke(new Action(() => ApplyOpacity()), System.Windows.Threading.DispatcherPriority.Background);
    }

    private IntPtr _hwnd;

    private void FloatingWindow_SourceInitialized(object sender, EventArgs e) {
      _hwnd = new WindowInteropHelper(this).Handle;
      ApplyWindowStyles();
      ApplyOpacity();
    }

    private void ContentBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
      this.DragMove();
    }

    private void ApplyWindowStyles() {
      int extStyle = GetWindowLong(_hwnd, GWL_EXSTYLE);
      if (ConfigService.FloatingBarLoc == "free") {
        extStyle &= ~WS_EX_TRANSPARENT;
      } else {
        extStyle |= WS_EX_TRANSPARENT;
      }
      SetWindowLong(_hwnd, GWL_EXSTYLE, extStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
    }

    private void ApplyOpacity() {
      double opacity = ConfigService.FloatingOpacity;
      if (opacity <= 0) {
        ContentBorder.Background = Brushes.Transparent;
        ContentBorder.BorderThickness = new Thickness(0);
        ContentBorder.Effect = null;
      } else {
        var bgBrush = TryFindResource("BgElevatedBrush") as SolidColorBrush;
        if (bgBrush != null) {
          var c = bgBrush.Color;
          ContentBorder.Background = new SolidColorBrush(Color.FromArgb((byte)(opacity * 255), c.R, c.G, c.B));
        }
        ContentBorder.BorderThickness = new Thickness(1);
        ContentBorder.Effect = new DropShadowEffect { BlurRadius = 12, ShadowDepth = 2, Direction = 270, Color = Color.FromRgb(0x60, 0x00, 0x00), Opacity = 0.4 };
      }
      DataPanel.Opacity = ConfigService.FloatingTextOpacity;
      ApplyWindowStyles();
    }

    public static void ApplyAllOpacity() {
      foreach (var w in _instances.ToArray()) {
        if (w != null && w.IsLoaded) {
          try {
            double opacity = ConfigService.FloatingOpacity;
            if (opacity <= 0) {
              w.ContentBorder.Background = Brushes.Transparent;
              w.ContentBorder.BorderThickness = new Thickness(0);
              w.ContentBorder.Effect = null;
            } else {
              var bgBrush = w.TryFindResource("BgElevatedBrush") as SolidColorBrush;
              if (bgBrush != null) {
                var c = bgBrush.Color;
                w.ContentBorder.Background = new SolidColorBrush(Color.FromArgb((byte)(opacity * 255), c.R, c.G, c.B));
              }
              w.ContentBorder.BorderThickness = new Thickness(1);
              w.ContentBorder.Effect = new DropShadowEffect { BlurRadius = 12, ShadowDepth = 2, Direction = 270, Color = Color.FromRgb(0x60, 0x00, 0x00), Opacity = 0.4 };
            }
            w.DataPanel.Opacity = ConfigService.FloatingTextOpacity;
            w.ApplyWindowStyles();
          } catch { }
        }
      }
    }

    private static void DoUpdateText(FloatingWindow w) {
      if (w == null) return;

      if (HardwareService.MonitorCPU) {
        w.CpuRow.Visibility = Visibility.Visible;
        float cpuTemp = HardwareService.CPUTemp;
        w.CpuTempText.Text = $"{cpuTemp:F1}°C";
        w.CpuTempText.Foreground = GetTempBrush(cpuTemp);
        w.CpuPowerText.Text = $"{HardwareService.CPUPower:F1}W";
      } else {
        w.CpuRow.Visibility = Visibility.Collapsed;
      }

      if (HardwareService.MonitorGPU) {
        w.GpuRow.Visibility = Visibility.Visible;
        float gpuTemp = HardwareService.GPUTemp;
        w.GpuTempText.Text = $"{gpuTemp:F1}°C";
        w.GpuTempText.Foreground = GetTempBrush(gpuTemp);
        w.GpuPowerText.Text = $"{HardwareService.GPUPower:F1}W";
      } else {
        w.GpuRow.Visibility = Visibility.Collapsed;
      }

      if (HardwareService.MonitorFan) {
        w.FanRow.Visibility = Visibility.Visible;
        w.FanSpeedText.Text = $"{HardwareService.FanSpeedNow[0] * 100}, {HardwareService.FanSpeedNow[1] * 100}";
      } else {
        w.FanRow.Visibility = Visibility.Collapsed;
      }

      w.UpdatePosition();
      w.ApplyWindowStyles();
    }

    public static void UpdateAllText() {
      Application.Current?.Dispatcher.Invoke(() => {
        foreach (var w in _instances.ToArray()) {
          if (w != null && w.IsLoaded) {
            w.ApplyTextSize();
            DoUpdateText(w);
          }
        }
      });
    }

    public static void ShowInstances() {
      Application.Current?.Dispatcher.Invoke(() => {
        var selected = ParseSelectedDeviceNames();
        // Close instances for deselected screens
        foreach (var w in _instances.ToArray()) {
          if (!selected.Contains(w._deviceName)) {
            w.Close();
          }
        }
        _instances.RemoveAll(w => !selected.Contains(w._deviceName));
        // Create new instances for missing screens
        foreach (string dev in selected) {
          if (!_instances.Any(w => w._deviceName == dev)) {
            var w = new FloatingWindow(dev);
            w.ApplyTextSize();
            _instances.Add(w);
            w.Show();
            w.UpdatePosition();
            DoUpdateText(w);
          }
        }
      });
    }

    public static void CloseAll() {
      Application.Current?.Dispatcher.Invoke(() => {
        foreach (var w in _instances.ToArray()) {
          try { w.Close(); } catch { }
        }
        _instances.Clear();
      });
    }

    public static List<string> ParseSelectedDeviceNames() {
      var result = new List<string>();
      var raw = ConfigService.FloatingBarScreen;
      if (string.IsNullOrWhiteSpace(raw)) return result;
      var parts = raw.Split(',');
      foreach (var p in parts) {
        var trimmed = p.Trim();
        if (string.IsNullOrWhiteSpace(trimmed)) continue;
        if (trimmed.StartsWith("\\")) {
          result.Add(trimmed);
        } else if (int.TryParse(trimmed, out var idx)) {
          var all = Forms.Screen.AllScreens;
          if (idx >= 0 && idx < all.Length)
            result.Add(all[idx].DeviceName);
        }
      }
      return result;
    }

    protected override void OnClosed(EventArgs e) {
      ThemeService.ThemeChanged -= OnThemeChanged;
      base.OnClosed(e);
      _instances.Remove(this);
    }

    private void ApplyTextSize() {
      double fontSize = ConfigService.TextSize;
      if (fontSize < 8) fontSize = 8;
      CpuLabel.FontSize = fontSize;
      CpuTempText.FontSize = fontSize;
      CpuPowerText.FontSize = fontSize - 2;
      GpuLabel.FontSize = fontSize;
      GpuTempText.FontSize = fontSize;
      GpuPowerText.FontSize = fontSize - 2;
      FanLabel.FontSize = fontSize;
      FanSpeedText.FontSize = fontSize - 2;
    }

    private void UpdatePosition() {
      if (ConfigService.FloatingBarLoc == "free") return;
      var match = Forms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName == _deviceName);
      var wa = match?.WorkingArea ?? Forms.Screen.PrimaryScreen.WorkingArea;
      // Convert physical pixels to WPF device-independent pixels
      double scaleX = 1.0, scaleY = 1.0;
      if (PresentationSource.FromVisual(this) is PresentationSource source &&
          source.CompositionTarget != null) {
        scaleX = source.CompositionTarget.TransformToDevice.M11;
        scaleY = source.CompositionTarget.TransformToDevice.M22;
      }
      double wpfLeft = wa.Left / scaleX;
      double wpfTop = wa.Top / scaleY;
      double wpfRight = wa.Right / scaleX;
      if (ConfigService.FloatingBarLoc == "right") {
        this.Left = wpfRight - Math.Max(this.ActualWidth, 100) - 10;
      } else {
        this.Left = wpfLeft + 10;
      }
      this.Top = wpfTop + 10;
    }

    private static SolidColorBrush GetTempBrush(float temp) {
      if (temp < 50) {
        return new SolidColorBrush(Color.FromRgb(0x00, 0xC8, 0x53));
      } else if (temp < 70) {
        double ratio = (temp - 50) / 20.0;
        byte r = (byte)(0x00 + ratio * 0xFF);
        byte g = (byte)(0xC8 + ratio * (0xC1 - 0xC8));
        byte b = (byte)(0x53 - ratio * 0x53);
        return new SolidColorBrush(Color.FromRgb(r, g, b));
      } else if (temp < 85) {
        double ratio = (temp - 70) / 15.0;
        byte r = (byte)0xFF;
        byte g = (byte)(0xC1 - ratio * 0xC1);
        byte b = (byte)(0x07 - ratio * 0x07);
        return new SolidColorBrush(Color.FromRgb(r, g, b));
      } else {
        return new SolidColorBrush(Color.FromRgb(0xFF, 0x44, 0x44));
      }
    }
  }
}
