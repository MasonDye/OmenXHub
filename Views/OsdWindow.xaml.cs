// OsdWindow.xaml.cs - 屏幕提示窗口
// 预设切换、功耗变化、锁定键、刷新率等的 Toast 通知显示
using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using OmenSuperHub.Services;
using Wpf.Ui.Controls;

namespace OmenSuperHub.Views {
  public partial class OsdWindow : Window {
    private static OsdWindow _instance;
    private static DispatcherTimer _fadeTimer;
    private static DispatcherTimer _lockKeyTimer;
    private static bool _lastCapsLock;
    private static bool _lastNumLock;
    private static bool _monitoringStarted;

    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int GWL_EXSTYLE = -20;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    public static void StartLockKeyMonitor() {
      if (_monitoringStarted) return;
      _monitoringStarted = true;
      _lastCapsLock = Console.CapsLock;
      _lastNumLock = Console.NumberLock;
      _lockKeyTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
      _lockKeyTimer.Tick += (s, e) => {
        if (!ConfigService.ShowOsd) return;
        if (Console.CapsLock != _lastCapsLock) {
          _lastCapsLock = Console.CapsLock;
          ShowOsd(_lastCapsLock ? Strings.CapsLockOn : Strings.CapsLockOff,
                  _lastCapsLock ? SymbolRegular.Keyboard24 : SymbolRegular.Keyboard24);
        }
        if (Console.NumberLock != _lastNumLock) {
          _lastNumLock = Console.NumberLock;
          ShowOsd(_lastNumLock ? Strings.NumLockOn : Strings.NumLockOff,
                  _lastNumLock ? SymbolRegular.Keyboard24 : SymbolRegular.Keyboard24);
        }
      };
      _lockKeyTimer.Start();
    }

    public static void StopLockKeyMonitor() {
      if (_lockKeyTimer != null) {
        _lockKeyTimer.Stop();
        _lockKeyTimer = null;
      }
    }

    public static void Dismiss() {
      _lastCapsLock = Console.CapsLock;
      _lastNumLock = Console.NumberLock;
      Application.Current?.Dispatcher.Invoke(() => {
        if (_fadeTimer != null) { _fadeTimer.Stop(); _fadeTimer = null; }
        if (_instance != null && _instance.IsLoaded) {
          _instance.Close();
        }
      });
    }

    public static void ShowPresetOsd(string presetKey) {
      if (!ConfigService.ShowOsd) return;
      string text;
      SymbolRegular icon = SymbolRegular.Gauge24;
      switch (presetKey) {
        case "Extreme": text = Strings.PresetExtreme; icon = SymbolRegular.Rocket24; break;
        case "GpuPriority": text = Strings.PresetGpuPriority; icon = SymbolRegular.Gauge24; break;
        case "LightUse": text = Strings.PresetLightUse; icon = SymbolRegular.WeatherMoon24; break;
        default:
          // ponytail: custom preset — use dynamic display name, star icon
          text = ConfigService.GetCustomPresetDisplayName(presetKey);
          icon = SymbolRegular.Star24;
          break;
      }
      ShowOsd(text, icon);
    }

    public static void ShowFanModeOsd(string mode) {
      if (!ConfigService.ShowOsd) return;
      string text;
      SymbolRegular icon = SymbolRegular.ArrowSync24;
      switch (mode) {
        case "silent": text = Strings.FanSilentMode; icon = SymbolRegular.WeatherMoon24; break;
        case "cool": text = Strings.FanCoolMode; icon = SymbolRegular.WeatherSunny24; break;
        case "smart":
        case "custom": text = Strings.FanCustomCurve; icon = SymbolRegular.Bot24; break;
        default:
          if (mode.EndsWith(" RPM")) { text = Strings.FanManualMode + ": " + mode; icon = SymbolRegular.Gauge24; }
          else if (mode.EndsWith("%")) { text = Strings.FanManualMode + ": " + mode; icon = SymbolRegular.Gauge24; }
          else { text = mode; break; }
          break;
      }
      ShowOsd(text, icon);
    }

    public static void ShowFanModeHardwareOsd(string mode) {
      if (!ConfigService.ShowOsd) return;
      string text;
      SymbolRegular icon = SymbolRegular.ArrowSync24;
      switch (mode) {
        case "performance": text = Strings.FanModePerformance; icon = SymbolRegular.Rocket24; break;
        case "default": text = Strings.FanModeDefault; icon = SymbolRegular.ArrowSync24; break;
        default: text = mode; break;
      }
      ShowOsd(text, icon);
    }

    public static void ShowPowerOsd(bool isOnline) {
      if (!ConfigService.ShowOsd) return;
      ShowOsd(isOnline ? Strings.PowerStatusAC : Strings.PowerStatusDC,
              isOnline ? SymbolRegular.PlugConnected24 : SymbolRegular.BatteryCharge24);
    }

    public static void ShowRefreshRateOsd(int hz) {
      if (!ConfigService.ShowOsd) return;
      ShowOsd(hz + " Hz", SymbolRegular.ArrowClockwise24);
    }

    public static void ShowCpuPowerOsd(string power) {
      if (!ConfigService.ShowOsd) return;
      ShowOsd("CPU: " + power, SymbolRegular.Gauge24);
    }

    public static void ShowGpuClockOsd(int mhz) {
      if (!ConfigService.ShowOsd) return;
      if (mhz <= 0)
        ShowOsd(Strings.GpuClockReset, SymbolRegular.ArrowClockwise24);
      else
        ShowOsd("GPU: " + mhz + " MHz", SymbolRegular.Gauge24);
    }

    public static void ShowTextOsd(string text, SymbolRegular icon = SymbolRegular.Info24, bool force = false) {
      if (!force && !ConfigService.ShowOsd) return;
      ShowOsd(text, icon);
    }

    private static void ShowOsd(string text, SymbolRegular icon) {
      Application.Current.Dispatcher.Invoke(() => {
        if (_fadeTimer != null) { _fadeTimer.Stop(); _fadeTimer = null; }
        if (_instance == null) {
          _instance = new OsdWindow();
          _instance.Closed += (s, e) => _instance = null;
          _instance.OsdText.Text = text;
          _instance.OsdIcon.Symbol = icon;
          _instance.OsdIcon.Visibility = Visibility.Visible;
          _instance.Show();
          _instance.Dispatcher.BeginInvoke(new Action(() => {
            _instance.UpdatePosition();
            _instance.BeginAnimate();
          }), DispatcherPriority.Loaded);
        } else {
          _instance.OsdText.Text = text;
          _instance.OsdIcon.Symbol = icon;
          _instance.OsdIcon.Visibility = Visibility.Visible;
          _instance.UpdatePosition();
          _instance.Show();
          _instance.BeginAnimate();
        }
      });
    }

    private OsdWindow() {
      InitializeComponent();
      SourceInitialized += (s, e) => {
        var hwnd = new WindowInteropHelper(this).Handle;
        int ext = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, ext | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT);
      };
    }

    private void UpdatePosition() {
      double waW = SystemParameters.WorkArea.Width;
      double waH = SystemParameters.WorkArea.Height;
      double w = ActualWidth > 0 ? ActualWidth : 300;
      double h = ActualHeight > 0 ? ActualHeight : 60;
      Left = (waW - w) / 2;
      Top = waH - h - 120;
    }

    private void BeginAnimate() {
      BeginAnimation(OpacityProperty, null);
      Opacity = 0;
      var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)) {
        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
      };
      BeginAnimation(OpacityProperty, fadeIn);

      _fadeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
      _fadeTimer.Tick += (s, e) => {
        _fadeTimer.Stop();
        _fadeTimer = null;
        var fadeOut = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(300)) {
          EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };
        fadeOut.Completed += (s2, e2) => { if (IsLoaded) Close(); };
        BeginAnimation(OpacityProperty, fadeOut);
      };
      _fadeTimer.Start();
    }
  }
}
