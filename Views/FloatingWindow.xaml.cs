// FloatingWindow.xaml.cs - 硬件浮窗
// 透明置顶窗口显示 CPU/GPU/风扇实时数据，支持拖拽和鼠标穿透
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
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
      if (ConfigService.FloatingBarLoc == "free") {
        ConfigService.FloatingPosLeft = this.Left;
        ConfigService.FloatingPosTop = this.Top;
        ConfigService.Save("FloatingPosLeft");
        ConfigService.Save("FloatingPosTop");
      }
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
      DataPanel.Opacity = ConfigService.FloatingTextOpacity;
      ApplyWindowStyles();
    }

    public static void ApplyAllOpacity() {
      foreach (var w in _instances.ToArray()) {
        if (w != null && w.IsLoaded) {
          try {
            w.DataPanel.Opacity = ConfigService.FloatingTextOpacity;
            w.ApplyWindowStyles();
          } catch { }
        }
      }
    }

    // ponytail: pre-built frozen brush palette avoids per-update SolidColorBrush allocation
    static readonly SolidColorBrush[] TempBrushes = Enumerable.Range(0, 101).Select(i => {
      float t = i;
      Color c = t < 40f ? Color.FromRgb(255, 255, 255)
          : t < 55f ? LerpColor(Color.FromRgb(255, 255, 255), Color.FromRgb(102, 187, 106), (t - 40f) / 15f)
          : t < 70f ? LerpColor(Color.FromRgb(102, 187, 106), Color.FromRgb(255, 235, 59), (t - 55f) / 15f)
          : t < 85f ? LerpColor(Color.FromRgb(255, 235, 59), Color.FromRgb(255, 107, 107), (t - 70f) / 15f)
          : t < 95f ? LerpColor(Color.FromRgb(255, 107, 107), Color.FromRgb(180, 0, 0), (t - 85f) / 10f)
          : Color.FromRgb(0, 0, 0);
      var b = new SolidColorBrush(c); b.Freeze(); return b;
    }).ToArray();

    static Color LerpColor(Color a, Color b, float t) {
      if (t < 0f) t = 0f;
      if (t > 1f) t = 1f;
      return Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t),
        (byte)(a.G + (b.G - a.G) * t),
        (byte)(a.B + (b.B - a.B) * t));
    }

    private static void DoUpdateText(FloatingWindow w) {
      if (w == null) return;

      if (HardwareService.MonitorCPU) {
        w.CpuRow.Visibility = Visibility.Visible;
        float cpuTemp = HardwareService.CPUTemp;
        int idx = (int)Math.Max(0, Math.Min(100, cpuTemp));
        w.CpuTempText.Foreground = TempBrushes[idx];
        w.CpuTempText.Text = $"{cpuTemp:F1}°C";
        w.CpuPowerText.Text = $"{HardwareService.CPUPower:F1}W";
      } else {
        w.CpuRow.Visibility = Visibility.Collapsed;
      }

      if (HardwareService.MonitorGPU) {
        w.GpuRow.Visibility = Visibility.Visible;
        float gpuTemp = HardwareService.GPUTemp;
        int idx = (int)Math.Max(0, Math.Min(100, gpuTemp));
        w.GpuTempText.Foreground = TempBrushes[idx];
        w.GpuTempText.Text = $"{gpuTemp:F1}°C";
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
      if (ConfigService.FloatingBarLoc == "free") {
        this.Left = ConfigService.FloatingPosLeft;
        this.Top = ConfigService.FloatingPosTop;
        return;
      }
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

  }
}
