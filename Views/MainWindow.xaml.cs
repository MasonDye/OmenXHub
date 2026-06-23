// MainWindow.xaml.cs - 主窗口逻辑
// WPF-UI NavigationView 侧边栏导航、鼠标滚轮处理、页面切换动画、窗口拖拽
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using OmenSuperHub.Pages;
using OmenSuperHub.Services;
using OmenSuperHub.Utils;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace OmenSuperHub.Views {
  public partial class MainWindow : OmenSuperHub.Windows.BaseWindow {
    static MainWindow _instance;
    static bool _trayHidden;
    TrayHelper _trayHelper;
    Page _activePage;

    static void EnsureWindow() {
      if (_instance != null && _instance.IsLoaded) return;
      _instance = new MainWindow();
    }

    public static void ShowInstance() {
      EnsureWindow();
      _instance.BeginAnimation(UIElement.OpacityProperty, null);
      _instance.Opacity = 1;
      if (_trayHidden) {
        _trayHidden = false;
        _instance.Show();
        FadeIn(_instance, () => _instance.Activate());
      } else if (_instance.Visibility == Visibility.Visible && _instance.WindowState != WindowState.Minimized) {
        FadeOut(_instance, () => _instance.Hide());
        return;
      } else {
        _instance.Opacity = 0;
        _instance.Show();
        FadeIn(_instance, () => _instance.Activate());
      }
    }

    public static void StartTrayOnly() {
      if (_instance != null && _instance.IsLoaded) return;
      _instance = new MainWindow();
      _trayHidden = true;
    }

    public static void ApplyLanguageToInstance() {
      if (_instance == null) return;
    }

    public static void NavigateToSysInfo() {
      EnsureWindow();
      _instance.Dispatcher.BeginInvoke(() =>
        _instance.NavigationView.Navigate(typeof(SysInfoPage)),
        System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    public static void NavigateToPage(string pageTag) {
      EnsureWindow();
      if (_instance.Visibility != Visibility.Visible || _instance.WindowState == WindowState.Minimized) {
        _instance.BeginAnimation(UIElement.OpacityProperty, null);
        _instance.Opacity = 0;
        _instance.Show();
        if (_instance.WindowState == WindowState.Minimized)
          _instance.WindowState = WindowState.Normal;
        FadeIn(_instance, () => _instance.Activate());
      } else {
        _instance.Activate();
      }
      _instance.Dispatcher.BeginInvoke(new Action(() => {
        if (_pageTypeMap.TryGetValue(pageTag, out var type)) {
          _instance.NavigationView.Navigate(type);
          _instance.Activate();
        }
      }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    public MainWindow() {
      InitializeComponent();
      _instance = this;

      ThemeService.ThemeChanged += () => Dispatcher.InvokeAsync(() => {
        WindowBackgroundManager.UpdateBackground(this, ApplicationThemeManager.GetAppTheme(), WindowBackdropType.Mica);
      });

      Closing += (s, e) => {
        e.Cancel = true;
        FadeOut(this, () => Hide());
      };

      StateChanged += (s, e) => {
        if (WindowState == WindowState.Minimized) {
          ShowInTaskbar = true;
        }
      };

      Loaded += (s, e) => {
        // Initialize Mica backdrop based on current theme
        WindowBackgroundManager.UpdateBackground(this, ApplicationThemeManager.GetAppTheme(), WindowBackdropType.Mica);

        LoadDeviceInfo();
        _trayHelper = new TrayHelper(BringToForeground, TrayService.TrayIcon);
        TrayService.RegisterTrayHelper(_trayHelper);
        _trayHelper.MakeVisible();
        NavigationView.Navigate(typeof(DashboardPage));
        StartStatusTimer();
        ApplyCustomLogo();
        ApplyCustomBg();
        Dispatcher.BeginInvoke(new Action(() => {
          HidePaneScrollBar(NavigationView);
        }), System.Windows.Threading.DispatcherPriority.Background);
      };
    }

    public static void ApplyCustomLogoToInstance() {
      if (_instance != null && _instance.IsLoaded)
        _instance.ApplyCustomLogo();
    }

    void ApplyCustomLogo() {
      try {
        string path = ConfigService.CustomLogoPath;
        if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path)) {
          var uri = new Uri(path, UriKind.Absolute);
          var bmp = new System.Windows.Media.Imaging.BitmapImage();
          bmp.BeginInit();
          bmp.UriSource = uri;
          bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
          bmp.EndInit();
          bmp.Freeze();
          CustomLogoImage.Source = bmp;
          CustomLogoImage.Visibility = Visibility.Visible;
        } else {
          CustomLogoImage.Source = null;
          CustomLogoImage.Visibility = Visibility.Collapsed;
        }
      } catch {
        CustomLogoImage.Source = null;
        CustomLogoImage.Visibility = Visibility.Collapsed;
      }
    }

    public static void ApplyCustomBgToInstance() {
      if (_instance != null && _instance.IsLoaded)
        _instance.ApplyCustomBg();
    }

    void ApplyCustomBg() {
      try {
        string path = ConfigService.CustomBgPath;
        if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path)) {
          var uri = new Uri(path, UriKind.Absolute);
          var bmp = new System.Windows.Media.Imaging.BitmapImage();
          bmp.BeginInit();
          bmp.UriSource = uri;
          bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
          bmp.EndInit();
          bmp.Freeze();
          CustomBgImage.Source = bmp;
          CustomBgImage.Visibility = Visibility.Visible;
          CustomBgImage.Opacity = ConfigService.CustomBgOpacity;
          if (ConfigService.CustomBgBlurEnabled)
            CustomBgImage.Effect = new BlurEffect { Radius = 24, KernelType = KernelType.Gaussian };
          else
            CustomBgImage.Effect = null;
        } else {
          CustomBgImage.Source = null;
          CustomBgImage.Visibility = Visibility.Collapsed;
          CustomBgImage.Effect = null;
        }
      } catch {
        CustomBgImage.Source = null;
        CustomBgImage.Visibility = Visibility.Collapsed;
        CustomBgImage.Effect = null;
      }
    }

    void LoadDeviceInfo() {
      System.Threading.Tasks.Task.Run(() => {
        try {
          Dispatcher.Invoke(() => {
            DeviceInfoBadge.Visibility = Visibility.Collapsed;
          });
        } catch { }
      });
    }

    void DeviceInfoBadge_Click(object sender, MouseButtonEventArgs e) => NavigateToSysInfo();
    void LogBadge_Click(object sender, MouseButtonEventArgs e) {
      try {
        string logDir = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
        Process.Start("explorer", System.IO.Path.Combine(logDir, "logs"));
      } catch { }
    }

    // ══════════════════════════════════════════════════════
    // Page Navigation (handled by NavigationView + TargetPageType)
    // ══════════════════════════════════════════════════════
    void NavigationView_Navigated(object sender, NavigatedEventArgs e) {
      if (e.Page is Page page) {
        _activePage = page;
        UpdateTitleBar(page);
        if (page.IsLoaded)
          AttachWheelHandler(page);
        else
          page.Loaded += (s, args) => AttachWheelHandler(page);
      }
    }

    void AttachWheelHandler(Page page) {
      var root = System.Windows.Media.VisualTreeHelper.GetParent(page);
      while (root != null) {
        var parent = System.Windows.Media.VisualTreeHelper.GetParent(root);
        if (parent == null) break;
        root = parent;
      }
      if (root is System.Windows.UIElement uiRoot) {
        uiRoot.AddHandler(UIElement.PreviewMouseWheelEvent,
          new System.Windows.Input.MouseWheelEventHandler((s, ev) => {
            if (ev.Handled) return;
            // Only handle events from within the page's visual tree
            var src = ev.OriginalSource as System.Windows.DependencyObject;
            bool inPage = false;
            while (src != null) {
              if (src == page) { inPage = true; break; }
              src = System.Windows.Media.VisualTreeHelper.GetParent(src);
            }
            if (!inPage) return;
            // Don't intercept when any ComboBox drop-down is open
            if (HasOpenComboBox(page)) return;
            var dsv = FindScrollHost(page);
            if (dsv == null || dsv.ScrollableHeight <= 0) return;
            if (ev.Delta > 0)
              dsv.ScrollToVerticalOffset(Math.Max(0, dsv.VerticalOffset - 60));
            else
              dsv.ScrollToVerticalOffset(Math.Min(dsv.ScrollableHeight, dsv.VerticalOffset + 60));
            ev.Handled = true;
          }), true);
      }
    }

    static System.Windows.Controls.ScrollViewer FindScrollHost(System.Windows.DependencyObject child) {
      var c = child;
      while (c != null) {
        if (c is System.Windows.Controls.ScrollViewer sv && sv.ScrollableHeight > 0) return sv;
        c = System.Windows.Media.VisualTreeHelper.GetParent(c);
      }
      return null;
    }

    static bool HasOpenComboBox(System.Windows.DependencyObject root) {
      if (root is System.Windows.Controls.ComboBox cb && cb.IsDropDownOpen) return true;
      int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
      for (int i = 0; i < count; i++) {
        if (HasOpenComboBox(System.Windows.Media.VisualTreeHelper.GetChild(root, i))) return true;
      }
      return false;
    }

    static void HidePaneScrollBar(System.Windows.DependencyObject root) {
      if (root is System.Windows.Controls.ScrollViewer sv && sv.VerticalScrollBarVisibility != System.Windows.Controls.ScrollBarVisibility.Hidden) {
        sv.VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Hidden;
        return;
      }
      int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
      for (int i = 0; i < count; i++) {
        HidePaneScrollBar(System.Windows.Media.VisualTreeHelper.GetChild(root, i));
      }
    }



    void UpdateTitleBar(Page page) {
      foreach (var kvp in _pageInfos) {
        if (kvp.Value.pageType == page.GetType()) {
          TitleText.Text = kvp.Value.title;
          NavIcon.Symbol = kvp.Value.icon;
          return;
        }
      }
    }

    static readonly Dictionary<string, Type> _pageTypeMap = new Dictionary<string, Type> {
      { "Dashboard", typeof(DashboardPage) }, { "Fan", typeof(FanPage) },
      { "Perf", typeof(PerfPage) }, { "Lighting", typeof(LightingPage) },
      { "SysInfo", typeof(SysInfoPage) }, { "Automation", typeof(AutomationPage) },
      { "Macro", typeof(MacroPage) },
      { "Other", typeof(OtherPage) }, { "Settings", typeof(SettingsPage) }
    };

    struct PageInfo { public Type pageType; public string title; public SymbolRegular icon; }

    static readonly Dictionary<string, PageInfo> _pageInfos = new Dictionary<string, PageInfo> {
      { "Dashboard", new PageInfo { pageType = typeof(DashboardPage), title = Strings.PageDashboard, icon = SymbolRegular.Home24 } },
      { "Fan", new PageInfo { pageType = typeof(FanPage), title = Strings.PageFan, icon = SymbolRegular.ArrowSync24 } },
      { "Perf", new PageInfo { pageType = typeof(PerfPage), title = Strings.PagePerf, icon = SymbolRegular.Gauge24 } },
      { "Lighting", new PageInfo { pageType = typeof(LightingPage), title = Strings.PageLighting, icon = SymbolRegular.Lightbulb24 } },
      { "SysInfo", new PageInfo { pageType = typeof(SysInfoPage), title = Strings.PageSysInfo, icon = SymbolRegular.Info24 } },
      { "Automation", new PageInfo { pageType = typeof(AutomationPage), title = Strings.PageAutomation, icon = SymbolRegular.Rocket24 } },
      { "Macro", new PageInfo { pageType = typeof(MacroPage), title = Strings.PageMacro, icon = SymbolRegular.Keyboard24 } },
      { "Other", new PageInfo { pageType = typeof(OtherPage), title = Strings.PageOther, icon = SymbolRegular.MoreHorizontal24 } },
      { "Settings", new PageInfo { pageType = typeof(SettingsPage), title = Strings.PageSettings, icon = SymbolRegular.Settings24 } }
    };

    // ══════════════════════════════════════════════════════
    // Preset / Hardware (called from pages)
    // ══════════════════════════════════════════════════════
    public void ApplyPresetHardware() {
      int gpuClock = ConfigService.GpuClock;
      bool tgp = ConfigService.TgpEnabled;
      bool ppab = ConfigService.PpabEnabled;
      int dState = ConfigService.DState == 2 ? 2 : 1;
      string cpuPwr = ConfigService.CpuPower;
      System.Threading.ThreadPool.QueueUserWorkItem(_ => {
        // Always call: SetGPUClockLimit(<210) resets/releases the lock, so a
        // preset with GpuClock=0 ("no limit") correctly clears a prior lock.
        TrayService.SetGPUClockLimit(gpuClock);
        OmenHardware.SetGpuPowerState(tgp, ppab, dState);
        if (cpuPwr == "max") OmenHardware.SetCpuPowerLimit(254);
        else if (int.TryParse(cpuPwr?.Replace(" W", ""), out int cpuVal) && cpuVal >= 10 && cpuVal <= 254)
          OmenHardware.SetCpuPowerLimit((byte)cpuVal);
      });
    }

    // ══════════════════════════════════════════════════════
    // Tray Integration
    // ══════════════════════════════════════════════════════
    void BringToForeground() {
      Dispatcher.Invoke(() => {
        _instance.BeginAnimation(UIElement.OpacityProperty, null);
        if (Visibility != Visibility.Visible || _instance.WindowState == WindowState.Minimized) {
          _instance.Opacity = 0;
          Show();
          if (_instance.WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
          FadeIn(this, () => Activate());
        } else {
          Activate();
        }
      });
    }

    // ══════════════════════════════════════════════════════
    // Fade Animations
    // ══════════════════════════════════════════════════════
    static void FadeOut(UIElement element, Action onDone = null) {
      element.BeginAnimation(UIElement.OpacityProperty, null);
      element.Opacity = 1;
      var fade = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.15)) {
        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn },
        FillBehavior = FillBehavior.HoldEnd
      };
      fade.Completed += (s, a) => {
        element.BeginAnimation(UIElement.OpacityProperty, null);
        element.Opacity = 1;
        onDone?.Invoke();
      };
      element.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    static void FadeIn(UIElement element, Action onDone = null) {
      element.BeginAnimation(UIElement.OpacityProperty, null);
      element.Opacity = 0;
      var fade = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.15)) {
        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        FillBehavior = FillBehavior.HoldEnd
      };
      fade.Completed += (s, a) => {
        element.BeginAnimation(UIElement.OpacityProperty, null);
        element.Opacity = 1;
        onDone?.Invoke();
      };
      element.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    System.Timers.Timer _statusTimer;
    void StartStatusTimer() {
      _statusTimer = new System.Timers.Timer(2000);
      _statusTimer.Elapsed += (s, e) => Dispatcher.InvokeAsync(() => UpdateStatusBar());
      _statusTimer.AutoReset = true;
      _statusTimer.Start();
    }

    void UpdateStatusBar() {
      string online = HardwareService.PowerOnline ? "\U0001f50c" : "\U0001f50b";
      StatusBarText.Text = $"{online} CPU {HardwareService.CPUTemp:F0}\u00b0C \u00b7 GPU {HardwareService.GPUTemp:F0}\u00b0C";
    }

    void PinToggle_Click(object sender, RoutedEventArgs e) {
      Topmost = !Topmost;
      PinIcon.Symbol = Topmost ? SymbolRegular.Pin24 : SymbolRegular.PinOff24;
      PinButton.ToolTip = Topmost ? "取消顶置" : "顶置";
    }
  }
}
