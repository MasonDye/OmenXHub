// OsdWindow.xaml.cs - 屏幕提示窗口
// 预设切换、功耗变化、锁定键、刷新率等的 Toast 通知显示
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using OmenSuperHub.Services;
using Wpf.Ui.Controls;

namespace OmenSuperHub.Views {
  public partial class OsdWindow : Window {
    // ponytail: OSD 改为多实例堆叠 —— 旧版只有单例 _instance,自动化 pipeline 里多个步骤
    // 在毫秒级连发各自 ShowXxxOsd 时每条都覆盖前一条文字并重置 1.5s 淡出计时,结果用户
    // 只看得到最后一条 ("OSD 看来只跑到最后一个步骤")。现在每个 ShowOsd 建独立窗口,
    // 按 OsdPosition 锚点向 stack 方向排开,每条独立淡出,关窗后从列表摘除并重排其余窗口。
    private static readonly List<OsdWindow> _instances = new List<OsdWindow>();
    private static readonly object _stackLock = new object();
    private DispatcherTimer _fadeTimer;  // per-instance now
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

    // ponytail: 200 ms Polling caps/numlock is the most frequent timer in the app.
    // Start symmetric Stop via RefreshMonitorState driven by SettingsPage. Tick lambda
    // pulled to a named method so Unload path can detach and the DispatcherTimer becomes GC-eligible.
    public static void StartLockKeyMonitor() {
      // ponytail: ShowOsd=false 用户已显式关闭 OSD —— 键状态轮询无消费方,不启动。
      if (!ConfigService.ShowOsd) return;
      if (_monitoringStarted) return;
      _monitoringStarted = true;
      _lastCapsLock = Console.CapsLock;
      _lastNumLock = Console.NumberLock;
      _lockKeyTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
      _lockKeyTimer.Tick += _lockKeyTick;
      _lockKeyTimer.Start();
    }

    static void _lockKeyTick(object s, EventArgs e) {
      if (!ConfigService.ShowOsd) {
        // ponytail: 设置页关掉 OSD 后由 RefreshMonitorState 走 Stop 路径,这里仅兜底。
        RefreshMonitorState();
        return;
      }
      if (Console.CapsLock != _lastCapsLock) {
        _lastCapsLock = Console.CapsLock;
        ShowOsd(_lastCapsLock ? Strings.CapsLockOn : Strings.CapsLockOff,
                SymbolRegular.Keyboard24);
      }
      if (Console.NumberLock != _lastNumLock) {
        _lastNumLock = Console.NumberLock;
        ShowOsd(_lastNumLock ? Strings.NumLockOn : Strings.NumLockOff,
                SymbolRegular.Keyboard24);
      }
    }

    public static void StopLockKeyMonitor() {
      if (_lockKeyTimer != null) {
        _lockKeyTimer.Tick -= _lockKeyTick;
        _lockKeyTimer.Stop();
        _lockKeyTimer = null;
      }
      _monitoringStarted = false;
    }

    // ponytail: SettingsPage 在 ShowOsd 切换后调用,把定时器对齐到当前 ShowOsd 状态。
    // 之前 _lockKeyTimer 启动后从不停,即使 ShowOsd=false 也每分钟 300 次读 Console.CapsLock。
    public static void RefreshMonitorState() {
      if (ConfigService.ShowOsd) StartLockKeyMonitor();
      else StopLockKeyMonitor();
    }

    public static void Dismiss() {
      _lastCapsLock = Console.CapsLock;
      _lastNumLock = Console.NumberLock;
      Application.Current?.Dispatcher.Invoke(() => {
        // ponytail: 关掉所有堆叠中的 OSD —— 旧版只关单例,关 OSD 后残留的窗口现在能一起清掉
        lock (_stackLock) {
          foreach (var w in _instances) {
            if (w._fadeTimer != null) { w._fadeTimer.Stop(); w._fadeTimer = null; }
            if (w.IsLoaded) w.Close();
          }
          _instances.Clear();
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
        case "balanced": text = Strings.FanModeDefault; icon = SymbolRegular.WeatherSunny24; break;
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
        // ponytail: 每个 ShowOsd 建独立窗口并加入堆叠。旧版覆盖单例 → 多步骤只看到最后一条。
        var win = new OsdWindow();
        win.OsdText.Text = text;
        win.OsdIcon.Symbol = icon;
        win.OsdIcon.Visibility = Visibility.Visible;
        win.Closed += (s, e) => RemoveAndRepack(win);
        lock (_stackLock) _instances.Add(win);
        win.Show();
        win.Dispatcher.BeginInvoke(new Action(() => {
          RepositionAll();
          win.BeginAnimate();
        }), DispatcherPriority.Loaded);
      });
    }

    // ponytail: 关窗后从 _instances 摘除并把其余窗口重排,避免留间隙或重叠。
    static void RemoveAndRepack(OsdWindow win) {
      lock (_stackLock) _instances.Remove(win);
      RepositionAll();
    }

    private OsdWindow() {
      InitializeComponent();
      SourceInitialized += (s, e) => {
        var hwnd = new WindowInteropHelper(this).Handle;
        int ext = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, ext | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT);
      };
    }

    // ponytail: 静态重排 —— 按 OsdPosition 锚点把所有堆叠中的 OSD 顺序排开。
    // 顶部锚点(top*)首条贴边向下堆; 底部锚点(bottom*)首条贴边向上堆,下一条让出上一条高度+gap。
    // 用户要"向上堆叠",对应底部锚点;顶部则对称向下。每条尺寸用 ActualWidth/Height 测量,
    // 因为 OsdWindow 是 SizeToContent,首帧可能为 0,用 fallback 估算再 Reposition 兜底。
    static void RepositionAll() {
      lock (_stackLock) {
        double waW = SystemParameters.WorkArea.Width;
        double waH = SystemParameters.WorkArea.Height;
        const double margin = 24;
        const double gap = 8;
        double acc = 0;  // 累计已占用的高度偏移
        string pos = ConfigService.OsdPosition ?? "bottomCenter";
        bool top = pos.StartsWith("top");
        foreach (var w in _instances) {
          double width = w.ActualWidth > 0 ? w.ActualWidth : 300;
          double height = w.ActualHeight > 0 ? w.ActualHeight : 60;

          // 锚点决定 Left/Top 基线
          switch (pos) {
            case "topLeft":
              w.Left = margin;
              w.Top = margin + acc;
              break;
            case "topRight":
              w.Left = waW - width - margin;
              w.Top = margin + acc;
              break;
            case "topCenter":
              w.Left = (waW - width) / 2;
              w.Top = margin + acc;
              break;
            case "bottomLeft":
              w.Left = margin;
              w.Top = waH - height - margin - acc;
              break;
            case "bottomRight":
              w.Left = waW - width - margin;
              w.Top = waH - height - margin - acc;
              break;
            default:  // bottomCenter — 原始默认位置
              w.Left = (waW - width) / 2;
              w.Top = waH - height - 120 - acc;
              break;
          }
          // ponytail: 顶部向下累加; 底部向上累加(后来者排得更靠上),方向与"向上堆叠"一致。
          acc += (top ? height : height) + gap;
        }
      }
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
