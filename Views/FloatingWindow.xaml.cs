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
using static OmenSuperHub.Pages.NativeMethods_Proc;
using Forms = System.Windows.Forms;

namespace OmenSuperHub.Views {
  public partial class FloatingWindow : Window {
    static PresentMonFpsMonitor _fpsMonitor;
    static System.Windows.Threading.DispatcherTimer _refreshTimer;
    // ponytail: dirty flags so periodic ticks skip redundant layout/position/style recompute
    static bool _layoutDirty = true;   // font size / orientation changed → re-ApplyLayoutAndTextSize
    static bool _positionDirty = true;  // screen/loc/opacity changed → re-UpdatePosition + ApplyWindowStyles
    static bool _fpsStarting;           // PresentMon startup is async; avoid double-spawn on UI thread

    // ponytail: Dispose PresentMon off UI thread — Stop() does Kill+WaitForExit(1000) which would block the render
    static void DisposeFpsMonitorAsync() {
      var m = _fpsMonitor;
      _fpsMonitor = null;
      if (m != null) System.Threading.Tasks.Task.Run(() => { try { m.Dispose(); } catch { } });
    }

    // ponytail: interval follows config directly; clamp 250ms to avoid sub-frame burn
    static int IntervalMs() => Math.Max(250, ConfigService.MonRefreshInterval);

    static void EnsureTimer() {
      if (_refreshTimer != null) return;
      _refreshTimer = new System.Windows.Threading.DispatcherTimer {
        Interval = TimeSpan.FromMilliseconds(IntervalMs())
      };
      _refreshTimer.Tick += (_, __) => {
        if (_instances.Count == 0) return;
        // timer path: only re-layout/reposition when dirty, not every tick
        UpdateAllTextCore(forceLayout: false);
      };
      _refreshTimer.Start();
    }

    private static List<FloatingWindow> _instances = new List<FloatingWindow>();

    private string _deviceName;

    // ponytail: previous-string cache — skips TextBlock.Text assignment when value is unchanged,
    // which also avoids WPF's FormattedText layout pass for unchanged text
    string _lastCpuTemp, _lastCpuPower, _lastCpuUsage, _lastCpuFreq, _lastGpuTemp, _lastGpuPower, _lastGpuUsage, _lastGpuFreq, _lastFanSpeed0, _lastFanSpeed1;
    string _lastMemPct, _lastMemUsed, _lastNetDown, _lastNetUp, _lastFps, _lastFpsApp;
    int _lastCpuTempIdx = -1, _lastGpuTempIdx = -1;

    static void SetIfChanged(System.Windows.Controls.TextBlock tb, ref string cache, string value) {
      if (cache != value) { tb.Text = value; cache = value; }
    }

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
      this.ContentRendered += FloatingWindow_ContentRendered;
      ThemeService.ThemeChanged += OnThemeChanged;
    }

    private void OnThemeChanged() {
      if (IsLoaded) Dispatcher.BeginInvoke(new Action(() => ApplyOpacity()), System.Windows.Threading.DispatcherPriority.Background);
    }

    private IntPtr _hwnd;
    private bool _firstLoad = true;

    // ponytail: defer first position to ContentRendered — with SizeToContent=WidthAndHeight the
    // final ActualWidth is only reliable after the first render pass (DPI matrix is also ready).
    private void FloatingWindow_ContentRendered(object sender, EventArgs e) {
      if (_firstLoad) {
        _firstLoad = false;
        ContentRendered -= FloatingWindow_ContentRendered;
        ApplyLayoutAndTextSize();
        UpdatePosition();
        ApplyWindowStyles();
      }
    }

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

    private void ApplyLayout() {
      bool isCol = ConfigService.FloatingBarLayout == "col";
      DataPanel.Orientation = isCol
        ? System.Windows.Controls.Orientation.Horizontal
        : System.Windows.Controls.Orientation.Vertical;
      // ponytail: col mode → 2×2 grid (temp/power 上, usage/freq 下, 列对齐); row mode → 铺平一行, 4px 间距
      var dataOrient = isCol ? System.Windows.Controls.Orientation.Vertical
                             : System.Windows.Controls.Orientation.Horizontal;
      var lineGap = isCol ? new Thickness(0) : new Thickness(4, 0, 0, 0);
      CpuDataStack.Orientation = dataOrient;
      GpuDataStack.Orientation = dataOrient;
      NetDataStack.Orientation = dataOrient;
      MemDataStack.Orientation = dataOrient;
      FanDataStack.Orientation = dataOrient;
      FpsDataStack.Orientation = dataOrient;
      CpuLine2.Margin = lineGap;
      GpuLine2.Margin = lineGap;
      NetUpText.Margin = lineGap;
      MemUsedText.Margin = lineGap;
      FanSpeed1Text.Margin = lineGap;
      FpsAppText.Margin = lineGap;
      // ponytail: separators toggled per-cycle in DoUpdateText so collapsed rows don't leave orphaned `|`
    }

    // ponytail: 每 tick 只采样一次跨窗共享数据(内存/网络/FPS),再喂给每个浮窗。
    // 旧版在 DoUpdateText 里逐窗各自 GetMemoryStatus()/GetSpeed()/Poll() —— 多屏浮窗时
    // 快照被重复采样。其中 GetSpeed() 有状态(消费 _prevDown/_prevUp 推进时间),逐窗各调
    // 会让后采样的窗读到缩小的时间窗,速率失真;Poll() 带锁+字典遍历,也是纯浪费。
    // 上限:单屏浮窗省得有限,多屏/开启 FPS 监控时收益明显。
    struct TickSnapshot {
      public bool MemValid; public double MemPct, MemUsedGB, MemTotalGB;
      public bool NetValid; public double DownKBps, UpKBps;
      public bool FpsValid; public int Fps; public string FpsApp;
    }

    static TickSnapshot BuildTickSnapshot() {
      var s = new TickSnapshot();
      if (ConfigService.MonitorMemory) {
        var mem = GetMemoryStatus();
        s.MemValid = true;
        s.MemPct = mem.dwMemoryLoad;
        s.MemUsedGB = (mem.ullTotalPhys - mem.ullAvailPhys) / (1024.0 * 1024 * 1024);
        s.MemTotalGB = mem.ullTotalPhys / (1024.0 * 1024 * 1024);
      }
      if (ConfigService.MonitorNetwork && NetworkSpeedService.IsAvailable) {
        s.NetValid = true;
        (s.DownKBps, s.UpKBps) = NetworkSpeedService.GetSpeed();
      }
      if (ConfigService.MonitorFPS) {
        // ponytail: spawn presentMon off the UI thread; first tick shows nothing, next tick after startup shows FPS
        if (_fpsMonitor == null && !_fpsStarting) {
          _fpsStarting = true;
          var seed = new PresentMonFpsMonitor();
          System.Threading.Tasks.Task.Run(() => {
            try { seed.EnsureRunning("", out _); } catch { }
            _fpsMonitor = seed;
            _fpsStarting = false;
          });
        }
        _fpsMonitor?.Poll();
        int fps = _fpsMonitor?.LastFps ?? 0;
        string app = _fpsMonitor?.LastApp ?? "";
        if (fps > 0) {
          s.FpsValid = true;
          s.Fps = fps;
          s.FpsApp = string.IsNullOrWhiteSpace(app) ? "" : ShortAppName(app);
        }
      } else if (_fpsMonitor != null) {
        DisposeFpsMonitorAsync();
      }
      return s;
    }

    private static void DoUpdateText(FloatingWindow w, in TickSnapshot s) {
      if (w == null) return;

      if (HardwareService.MonitorCPU) {
        w.CpuRow.Visibility = Visibility.Visible;
        float cpuTemp = HardwareService.CPUTemp;
        int idx = (int)Math.Max(0, Math.Min(100, cpuTemp));
        if (idx != w._lastCpuTempIdx) { w.CpuTempText.Foreground = TempBrushes[idx]; w._lastCpuTempIdx = idx; }
        SetIfChanged(w.CpuTempText, ref w._lastCpuTemp, $"{cpuTemp:F1}°C");
        SetIfChanged(w.CpuPowerText, ref w._lastCpuPower, $"{HardwareService.CPUPower:F1}W");
        SetIfChanged(w.CpuUsageText, ref w._lastCpuUsage, $"{HardwareService.CPUUsage:F0}%");
        SetIfChanged(w.CpuFreqText, ref w._lastCpuFreq, $"{HardwareService.CPUClock:F0}M");
      } else {
        w.CpuRow.Visibility = Visibility.Collapsed;
      }

      if (HardwareService.MonitorGPU) {
        w.GpuRow.Visibility = Visibility.Visible;
        float gpuTemp = HardwareService.GPUTemp;
        int idx = (int)Math.Max(0, Math.Min(100, gpuTemp));
        if (idx != w._lastGpuTempIdx) { w.GpuTempText.Foreground = TempBrushes[idx]; w._lastGpuTempIdx = idx; }
        SetIfChanged(w.GpuTempText, ref w._lastGpuTemp, $"{gpuTemp:F1}°C");
        SetIfChanged(w.GpuPowerText, ref w._lastGpuPower, $"{HardwareService.GPUPower:F1}W");
        SetIfChanged(w.GpuUsageText, ref w._lastGpuUsage, $"{HardwareService.GPUUsage:F0}%");
        SetIfChanged(w.GpuFreqText, ref w._lastGpuFreq, $"{HardwareService.GPUClock:F0}M");
      } else {
        w.GpuRow.Visibility = Visibility.Collapsed;
      }

      if (HardwareService.MonitorFan) {
        w.FanRow.Visibility = Visibility.Visible;
        SetIfChanged(w.FanSpeed0Text, ref w._lastFanSpeed0, $"{HardwareService.FanSpeedNow[0] * 100}");
        SetIfChanged(w.FanSpeed1Text, ref w._lastFanSpeed1, $"{HardwareService.FanSpeedNow[1] * 100}");
      } else {
        w.FanRow.Visibility = Visibility.Collapsed;
      }

      if (ConfigService.MonitorMemory && s.MemValid) {
        w.MemRow.Visibility = Visibility.Visible;
        SetIfChanged(w.MemPctText, ref w._lastMemPct, $"{s.MemPct:F0}%");
        SetIfChanged(w.MemUsedText, ref w._lastMemUsed, $"{s.MemUsedGB:F1}/{s.MemTotalGB:F1}G");
      } else {
        w.MemRow.Visibility = Visibility.Collapsed;
      }

      if (ConfigService.MonitorNetwork && s.NetValid) {
        w.NetRow.Visibility = Visibility.Visible;
        SetIfChanged(w.NetDownText, ref w._lastNetDown, $"↓{s.DownKBps:F0}KB/s");
        SetIfChanged(w.NetUpText, ref w._lastNetUp, $"↑{s.UpKBps:F0}KB/s");
      } else {
        w.NetRow.Visibility = Visibility.Collapsed;
      }

      if (ConfigService.MonitorFPS) {
        if (s.FpsValid) {
          w.FpsRow.Visibility = Visibility.Visible;
          SetIfChanged(w.FpsValueText, ref w._lastFps, s.Fps.ToString());
          SetIfChanged(w.FpsAppText, ref w._lastFpsApp, s.FpsApp);
        } else {
          w.FpsRow.Visibility = Visibility.Collapsed;
        }
      } else {
        w.FpsRow.Visibility = Visibility.Collapsed;
      }

      UpdateSeparators(w);
    }

	    static void UpdateSeparators(FloatingWindow w) {
	      bool isCol = ConfigService.FloatingBarLayout == "col";
	      // ponytail: sep lives inside each row; only show in col mode when both this row and a following row are visible
	      var rows = new Tuple<UIElement, System.Windows.Controls.TextBlock>[] {
	        Tuple.Create((UIElement)w.CpuRow, w.Sep1),
	        Tuple.Create((UIElement)w.GpuRow, w.Sep2),
	        Tuple.Create((UIElement)w.MemRow, w.Sep3),
	        Tuple.Create((UIElement)w.NetRow, w.Sep4),
	        Tuple.Create((UIElement)w.FpsRow, w.Sep5),
	        Tuple.Create((UIElement)w.FanRow, (System.Windows.Controls.TextBlock)null),
	      };
	      for (int i = 0; i < rows.Length - 1; i++) {
	        var sep = rows[i].Item2;
	        if (sep == null) continue;
	        if (!isCol || rows[i].Item1.Visibility != Visibility.Visible) {
	          sep.Visibility = Visibility.Collapsed;
	          continue;
	        }
	        bool nextVisible = false;
	        for (int j = i + 1; j < rows.Length; j++) {
	          if (rows[j].Item1.Visibility == Visibility.Visible) { nextVisible = true; break; }
	        }
	        sep.Visibility = nextVisible ? Visibility.Visible : Visibility.Collapsed;
	      }
	    }

    static string ShortAppName(string app) {
      try {
        string name = System.IO.Path.GetFileNameWithoutExtension(app);
        return name.Length > 12 ? name.Substring(0, 12) + ".." : name;
      } catch { return app; }
    }

	    public static void UpdateAllText() {
      // external callers (SettingsPage/DashboardPage/TrayService) want a forced refresh after config changes
      UpdateAllTextCore(forceLayout: true);
    }

    // ponytail: 1Hz tray-tick entry — unforced (dirty-flag driven) and skips the UI-thread
    // marshalling entirely when no floating window exists. The old tick called the forced
    // UpdateAllText(), which unconditionally Dispatcher.Invoke'd even with _instances empty,
    // pinning the UI message pump awake every second after the main window was hidden — the
    // visible symptom was the working-set creeping up post-close and never settling back.
    public static void UpdateAllTextTicked() {
      if (_instances.Count == 0) return;
      UpdateAllTextCore(forceLayout: false);
    }

    static void UpdateAllTextCore(bool forceLayout) {
      // ponytail: zero floating windows → nothing to refresh. Early-out BEFORE the
      // Dispatcher.Invoke hop so the UI thread isn't woken just to loop over nothing.
      // Dirty flags stay set on purpose: the next real window gets laid out on its first tick.
      if (_instances.Count == 0) return;
      Application.Current?.Dispatcher.Invoke(() => {
        bool needLayout = forceLayout || _layoutDirty;
        bool needPosition = forceLayout || _positionDirty;
        var snap = BuildTickSnapshot();
        foreach (var w in _instances.ToArray()) {
          if (w != null && w.IsLoaded) {
            if (needLayout) w.ApplyLayoutAndTextSize();
            if (needPosition) { w.UpdatePosition(); w.ApplyWindowStyles(); }
            DoUpdateText(w, snap);
          }
        }
        if (needLayout) _layoutDirty = false;
        if (needPosition) _positionDirty = false;
      });
    }

    public static void ShowInstances() {
      EnsureTimer();
      Application.Current?.Dispatcher.BeginInvoke(new Action(() => {
        var selected = ParseSelectedDeviceNames();
        // Close instances for deselected screens
        foreach (var w in _instances.ToArray()) {
          if (!selected.Contains(w._deviceName)) {
            w.Close();
          }
        }
        _instances.RemoveAll(w => !selected.Contains(w._deviceName));
        // ponytail: first position is deferred to Loaded event where ActualWidth/DPI are ready;
        // layout + text are filled by the next timer tick (or immediately if already loaded)
        bool created = false;
        var snap = BuildTickSnapshot();
        foreach (string dev in selected) {
          if (!_instances.Any(w => w._deviceName == dev)) {
            var w = new FloatingWindow(dev);
            _instances.Add(w);
            w.Show();
            DoUpdateText(w, snap);
            created = true;
          }
        }
        // existing instances may need re-position if screens changed; force a layout pass next tick
        if (created) { _layoutDirty = true; _positionDirty = true; }
      }), System.Windows.Threading.DispatcherPriority.Background);
    }

    public static void CloseAll() {
      Application.Current?.Dispatcher.Invoke(() => {
        foreach (var w in _instances.ToArray()) {
          try { w.Close(); } catch { }
        }
        _instances.Clear();
      });
      // ponytail: stop the timer so hidden windows don't pay scheduler cost; EnsureTimer rebuilds on next Show
      if (_refreshTimer != null) {
        try { _refreshTimer.Stop(); } catch { }
        _refreshTimer = null;
      }
      // presentMon keeps running only while MonitorFPS is on; release it async to avoid UI-thread Kill/Wait
      DisposeFpsMonitorAsync();
    }

    public static void UpdateRefreshInterval() {
      if (_refreshTimer != null)
        _refreshTimer.Interval = TimeSpan.FromMilliseconds(IntervalMs());
    }

    public static List<string> ParseSelectedDeviceNames() {
      var result = new List<string>();
      var raw = ConfigService.FloatingBarScreen;
      // ponytail: 空 = 首次运行未配置 → 默认勾选所有显示器;用户改动后显式串接管(持久化已有)
      if (string.IsNullOrWhiteSpace(raw))
        return Forms.Screen.AllScreens.Select(s => s.DeviceName).ToList();
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

    private void ApplyLayoutAndTextSize() {
      ApplyLayout();
      double fontSize = ConfigService.TextSize;
      if (fontSize < 8) fontSize = 8;
      // ponytail: em ≈ FontSize * 0.6 for Consolas (DIP). Reserve fixed per-field widths so digit-count
      // changes don't squeeze the whole bar — NVIDIA-OSD-style stable window width.
      double EmW(double fs, int chars) => Math.Ceiling(fs * 0.6 * chars);
      CpuLabel.FontSize = fontSize;
      CpuTempText.FontSize = fontSize;
      CpuTempText.Width = EmW(fontSize, 7); CpuTempText.TextAlignment = TextAlignment.Right;
      CpuPowerText.FontSize = fontSize - 2;
      CpuPowerText.Width = EmW(fontSize - 2, 5); CpuPowerText.TextAlignment = TextAlignment.Right;
      // ponytail: usage mirrors temp column, freq mirrors power column so the two lines align as a 2x2 grid
      CpuUsageText.FontSize = fontSize;
      CpuUsageText.Width = EmW(fontSize, 7); CpuUsageText.TextAlignment = TextAlignment.Right;
      CpuFreqText.FontSize = fontSize - 2;
      CpuFreqText.Width = EmW(fontSize - 2, 5); CpuFreqText.TextAlignment = TextAlignment.Right;
      GpuLabel.FontSize = fontSize;
      GpuTempText.FontSize = fontSize;
      GpuTempText.Width = EmW(fontSize, 7); GpuTempText.TextAlignment = TextAlignment.Right;
      GpuPowerText.FontSize = fontSize - 2;
      GpuPowerText.Width = EmW(fontSize - 2, 5); GpuPowerText.TextAlignment = TextAlignment.Right;
      GpuUsageText.FontSize = fontSize;
      GpuUsageText.Width = EmW(fontSize, 7); GpuUsageText.TextAlignment = TextAlignment.Right;
      GpuFreqText.FontSize = fontSize - 2;
      GpuFreqText.Width = EmW(fontSize - 2, 5); GpuFreqText.TextAlignment = TextAlignment.Right;
      FanLabel.FontSize = fontSize;
      FanSpeed0Text.FontSize = fontSize - 2;
      FanSpeed0Text.Width = EmW(fontSize - 2, 5); FanSpeed0Text.TextAlignment = TextAlignment.Right;
      FanSpeed1Text.FontSize = fontSize - 2;
      FanSpeed1Text.Width = EmW(fontSize - 2, 5); FanSpeed1Text.TextAlignment = TextAlignment.Right;
      MemLabel.FontSize = fontSize;
      MemPctText.FontSize = fontSize;
      MemPctText.Width = EmW(fontSize, 4); MemPctText.TextAlignment = TextAlignment.Right;
      MemUsedText.FontSize = fontSize - 2;
      MemUsedText.Width = EmW(fontSize - 2, 10); MemUsedText.TextAlignment = TextAlignment.Right;
      NetLabel.FontSize = fontSize;
      NetDownText.FontSize = fontSize;
      NetDownText.Width = EmW(fontSize, 7); NetDownText.TextAlignment = TextAlignment.Right;
      NetUpText.FontSize = fontSize - 2;
      NetUpText.Width = EmW(fontSize - 2, 7); NetUpText.TextAlignment = TextAlignment.Right;
      FpsLabel.FontSize = fontSize;
      FpsValueText.FontSize = fontSize;
      FpsValueText.Width = EmW(fontSize, 4); FpsValueText.TextAlignment = TextAlignment.Right;
      double fpsAppFs = Math.Max(8, fontSize - 4);
      FpsAppText.FontSize = fpsAppFs;
      FpsAppText.Width = EmW(fpsAppFs, 14); FpsAppText.TextAlignment = TextAlignment.Left;
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
      } else if (ConfigService.FloatingBarLoc == "top") {
        this.Left = wpfLeft + (wpfRight - wpfLeft - Math.Max(this.ActualWidth, 100)) / 2;
      } else {
        this.Left = wpfLeft + 10;
      }
      this.Top = wpfTop + 10;
    }

  }
}
