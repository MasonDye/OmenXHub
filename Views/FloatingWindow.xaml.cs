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

    struct MEMORYSTATUSEX {
      public uint dwLength;
      public uint dwMemoryLoad;
      public ulong ullTotalPhys;
      public ulong ullAvailPhys;
      public ulong ullTotalPageFile;
      public ulong ullAvailPageFile;
      public ulong ullTotalVirtual;
      public ulong ullAvailVirtual;
      public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll")]
    static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    static MEMORYSTATUSEX GetMemoryStatus() {
      var mem = new MEMORYSTATUSEX();
      mem.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
      GlobalMemoryStatusEx(ref mem);
      return mem;
    }
    private static List<FloatingWindow> _instances = new List<FloatingWindow>();

    private string _deviceName;

    // ponytail: previous-string cache — skips TextBlock.Text assignment when value is unchanged,
    // which also avoids WPF's FormattedText layout pass for unchanged text
    string _lastCpuTemp, _lastCpuPower, _lastGpuTemp, _lastGpuPower, _lastFanSpeed;
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
      // ponytail: separators toggled per-cycle in DoUpdateText so collapsed rows don't leave orphaned `|`
    }

    private static void DoUpdateText(FloatingWindow w) {
      if (w == null) return;

      if (HardwareService.MonitorCPU) {
        w.CpuRow.Visibility = Visibility.Visible;
        float cpuTemp = HardwareService.CPUTemp;
        int idx = (int)Math.Max(0, Math.Min(100, cpuTemp));
        if (idx != w._lastCpuTempIdx) { w.CpuTempText.Foreground = TempBrushes[idx]; w._lastCpuTempIdx = idx; }
        SetIfChanged(w.CpuTempText, ref w._lastCpuTemp, $"{cpuTemp:F1}°C");
        SetIfChanged(w.CpuPowerText, ref w._lastCpuPower, $"{HardwareService.CPUPower:F1}W");
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
      } else {
        w.GpuRow.Visibility = Visibility.Collapsed;
      }

      if (HardwareService.MonitorFan) {
        w.FanRow.Visibility = Visibility.Visible;
        SetIfChanged(w.FanSpeedText, ref w._lastFanSpeed, $"{HardwareService.FanSpeedNow[0] * 100}, {HardwareService.FanSpeedNow[1] * 100}");
      } else {
        w.FanRow.Visibility = Visibility.Collapsed;
      }

      if (ConfigService.MonitorMemory) {
        w.MemRow.Visibility = Visibility.Visible;
        var mem = GetMemoryStatus();
        double memPct = mem.dwMemoryLoad;
        double usedGB = (mem.ullTotalPhys - mem.ullAvailPhys) / (1024.0 * 1024 * 1024);
        double totalGB = mem.ullTotalPhys / (1024.0 * 1024 * 1024);
        SetIfChanged(w.MemPctText, ref w._lastMemPct, $"{memPct:F0}%");
        SetIfChanged(w.MemUsedText, ref w._lastMemUsed, $"{usedGB:F1}/{totalGB:F1}G");
      } else {
        w.MemRow.Visibility = Visibility.Collapsed;
      }

      if (ConfigService.MonitorNetwork && NetworkSpeedService.IsAvailable) {
        w.NetRow.Visibility = Visibility.Visible;
        var (down, up) = NetworkSpeedService.GetSpeed();
        SetIfChanged(w.NetDownText, ref w._lastNetDown, $"↓{down:F0}KB/s");
        SetIfChanged(w.NetUpText, ref w._lastNetUp, $"↑{up:F0}KB/s");
      } else {
        w.NetRow.Visibility = Visibility.Collapsed;
      }

      if (ConfigService.MonitorFPS) {
        // ponytail: spawn PresentMon off the UI thread; first tick shows nothing, next tick after startup shows FPS
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
          w.FpsRow.Visibility = Visibility.Visible;
          SetIfChanged(w.FpsValueText, ref w._lastFps, fps.ToString());
          string appDisplay = string.IsNullOrWhiteSpace(app) ? "" : ShortAppName(app);
          SetIfChanged(w.FpsAppText, ref w._lastFpsApp, appDisplay);
        } else {
          w.FpsRow.Visibility = Visibility.Collapsed;
        }
      } else {
        w.FpsRow.Visibility = Visibility.Collapsed;
        if (_fpsMonitor != null) { DisposeFpsMonitorAsync(); }
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

    static void UpdateAllTextCore(bool forceLayout) {
      Application.Current?.Dispatcher.Invoke(() => {
        bool needLayout = forceLayout || _layoutDirty;
        bool needPosition = forceLayout || _positionDirty;
        foreach (var w in _instances.ToArray()) {
          if (w != null && w.IsLoaded) {
            if (needLayout) w.ApplyLayoutAndTextSize();
            if (needPosition) { w.UpdatePosition(); w.ApplyWindowStyles(); }
            DoUpdateText(w);
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
        foreach (string dev in selected) {
          if (!_instances.Any(w => w._deviceName == dev)) {
            var w = new FloatingWindow(dev);
            _instances.Add(w);
            w.Show();
            DoUpdateText(w);
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

    private void ApplyLayoutAndTextSize() {
      ApplyLayout();
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
      MemLabel.FontSize = fontSize;
      MemPctText.FontSize = fontSize;
      MemUsedText.FontSize = fontSize - 2;
      NetLabel.FontSize = fontSize;
      NetDownText.FontSize = fontSize;
      NetUpText.FontSize = fontSize - 2;
      FpsLabel.FontSize = fontSize;
      FpsValueText.FontSize = fontSize;
      FpsAppText.FontSize = Math.Max(8, fontSize - 4);
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
