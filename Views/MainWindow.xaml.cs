// MainWindow.xaml.cs - 主窗口逻辑
// WPF-UI NavigationView 侧边栏导航、鼠标滚轮处理、页面切换动画、窗口拖拽
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
    static TrayHelper _trayHelper;
    internal static bool _allowClose;
    Page _activePage;

    static void EnsureWindow() {
      if (_instance != null && _instance.IsLoaded) return;
      _instance = new MainWindow();
    }

    public static void ShowInstance() {
      bool wasLoaded = _instance != null && _instance.IsLoaded;
      EnsureWindow();
      _instance.BeginAnimation(UIElement.OpacityProperty, null);
      _instance.Opacity = 1;
      if (!wasLoaded || _trayHidden) {
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
      // The window must be Show()'d once even in tray-only boot mode so that:
      //  1) Application.Current.MainWindow is set — the tray context menu host.
      //     Without it, Application.Current.MainWindow is null, OnRightClick
      //     skips setting PlacementTarget, and the menu never appears.
      //  2) Loaded fires → Mica backdrop init, NavigationView, status timer.
      //     Without it, switching dark/light theme has no visible effect
      //     (ApplicationThemeManager.Apply has no shown window to update,
      //      and WindowBackgroundManager.UpdateBackground was never called).
      //  3) A PresentationSource exists — required by the WPF ContextMenu
      //     popup to render.
      // Shown with Opacity=0 + ShowActivated=false + no taskbar → invisible.
      _instance.ShowInTaskbar = false;
      _instance.ShowActivated = false;
      _instance.Opacity = 0;
      _instance.Show();   // fires Loaded synchronously, creates HWND + PresentationSource
      _instance.Hide();
      _instance.ShowInTaskbar = true;
      _instance.Opacity = 1;
    }

    public static void ApplyLanguageToInstance() {
      if (_instance == null) return;
    }

    public static void NavigateToPage(string pageTag) {
      bool wasLoaded = _instance != null && _instance.IsLoaded;
      EnsureWindow();
      if (!wasLoaded || _instance.Visibility != Visibility.Visible || _instance.WindowState == WindowState.Minimized) {
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
        if (_pageTypeMap.TryGetValue(pageTag, out var type))
          _instance.NavigationView.Navigate(type);
        _instance.Activate();
      }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    public MainWindow() {
      InitializeComponent();
      _instance = this;
      // ponytail: restore persisted topmost state — XAML defaults to False
      Topmost = ConfigService.Topmost;
      PinIcon.Symbol = Topmost ? SymbolRegular.Pin24 : SymbolRegular.PinOff24;
      PinButton.ToolTip = Topmost ? Strings.MainWindowPinTooltipOn : Strings.MainWindowPinTooltipOff;
      NavigationView.SetPageService(new Services.CachedPageService());

      ThemeService.ThemeChanged += OnThemeChanged;

      Closing += (s, e) => {
        if (_allowClose) {
          ThemeService.ThemeChanged -= OnThemeChanged;
          if (_wheelHandler != null && _wheelRoot != null)
            _wheelRoot.RemoveHandler(UIElement.PreviewMouseWheelEvent, _wheelHandler);
          _wheelHandler = null; _wheelRoot = null;
          StopStatusTimer();
          _instance = null;
          return;
        }
        e.Cancel = true;
        Hide();
      };

      StateChanged += (s, e) => {
        if (WindowState == WindowState.Minimized) {
          ShowInTaskbar = true;
        }
      };

      // Init tray immediately (not inside Loaded) so --tray mode works
      if (_trayHelper == null) {
        _trayHelper = new TrayHelper(BringToForeground, TrayService.TrayIcon);
        TrayService.RegisterTrayHelper(_trayHelper);
        _trayHelper.MakeVisible();
      }

      Loaded += (s, e) => {
        // Initialize Mica backdrop based on current theme
        WindowBackgroundManager.UpdateBackground(this, ApplicationThemeManager.GetAppTheme(), WindowBackdropType.Mica);

        LoadDeviceInfo();
        ApplyPresetHardware();
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
      // ponytail: was Task.Run + Dispatcher.Invoke for a single Visibility setter — pointless threadhop
      try { DeviceInfoBadge.Visibility = Visibility.Collapsed; } catch { }
    }

    void DeviceInfoBadge_Click(object sender, MouseButtonEventArgs e) => NavigateToPage("Dashboard");
    void LogBadge_Click(object sender, MouseButtonEventArgs e) {
      try {
        string logDir = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
        Process.Start("explorer", System.IO.Path.Combine(logDir, "logs"))?.Dispose();
      } catch { }
    }

    // ══════════════════════════════════════════════════════
    // Page Navigation (handled by NavigationView + TargetPageType)
    // ══════════════════════════════════════════════════════
    void NavigationView_Navigated(object sender, NavigatedEventArgs e) {
      // ponytail: keep last 3 pages to avoid re-creation cost for frequently visited pages
      KeepNavJournal(NavigationView, 3);
      if (e.Page is Page page) {
        _activePage = page;
        UpdateTitleBar(page);
        if (page.IsLoaded)
          AttachWheelHandler(page);
        else
          page.Loaded += PageOnLoaded;

      void PageOnLoaded(object s, RoutedEventArgs args) {
        if (s is Page p) p.Loaded -= PageOnLoaded;
        AttachWheelHandler(page);
      }
      }
    }

    static void KeepNavJournal(System.Windows.DependencyObject root, int maxEntries) {
      for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++) {
        var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
        if (child is System.Windows.Controls.Frame f) {
          int cnt = 0;
          foreach (var _ in f.BackStack) cnt++;
          while (cnt > maxEntries) { f.RemoveBackEntry(); cnt--; }
          return;
        }
        KeepNavJournal(child, maxEntries);
      }
    }

    System.Windows.Input.MouseWheelEventHandler _wheelHandler;
    System.Windows.UIElement _wheelRoot;

    void AttachWheelHandler(Page page) {
      // Remove previous handler to prevent accumulation on navigation
      if (_wheelHandler != null && _wheelRoot != null)
        _wheelRoot.RemoveHandler(UIElement.PreviewMouseWheelEvent, _wheelHandler);

      // ponytail: walk up visual tree to find the root UIElement.
      // VisualTreeHelper.GetParent throws on ContentElement nodes (e.g. Run, TextElement),
      // so skip any non-UIElement along the way.
      DependencyObject root = page;
      try { root = System.Windows.Media.VisualTreeHelper.GetParent(page); } catch { }
      while (root != null) {
        try {
          var parent = System.Windows.Media.VisualTreeHelper.GetParent(root);
          if (parent == null) break;
          root = parent;
        } catch {
          // ponytail: ContentElement in the visual tree ancestry — skip it,
          // the real visual parent is higher up.
          break;
        }
      }
      if (root is System.Windows.UIElement uiRoot) {
        _wheelRoot = uiRoot;
        _wheelHandler = new System.Windows.Input.MouseWheelEventHandler((s, ev) => {
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
          });
        _wheelRoot.AddHandler(UIElement.PreviewMouseWheelEvent, _wheelHandler, true);
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
      { "Automation", typeof(AutomationPage) },
      { "Macro", typeof(MacroPage) },
      { "Other", typeof(OtherPage) }, { "Settings", typeof(SettingsPage) }
    };

    struct PageInfo { public Type pageType; public string title; public SymbolRegular icon; }

    static readonly Dictionary<string, PageInfo> _pageInfos = new Dictionary<string, PageInfo> {
      { "Dashboard", new PageInfo { pageType = typeof(DashboardPage), title = Strings.PageDashboard, icon = SymbolRegular.Home24 } },
      { "Fan", new PageInfo { pageType = typeof(FanPage), title = Strings.PageFan, icon = SymbolRegular.ArrowSync24 } },
      { "Perf", new PageInfo { pageType = typeof(PerfPage), title = Strings.PagePerf, icon = SymbolRegular.Gauge24 } },
      { "Lighting", new PageInfo { pageType = typeof(LightingPage), title = Strings.PageLighting, icon = SymbolRegular.Lightbulb24 } },
      { "Automation", new PageInfo { pageType = typeof(AutomationPage), title = Strings.PageAutomation, icon = SymbolRegular.Rocket24 } },
      { "Macro", new PageInfo { pageType = typeof(MacroPage), title = Strings.PageMacro, icon = SymbolRegular.Keyboard24 } },
      { "Other", new PageInfo { pageType = typeof(OtherPage), title = Strings.PageOther, icon = SymbolRegular.MoreHorizontal24 } },
      { "Settings", new PageInfo { pageType = typeof(SettingsPage), title = Strings.PageSettings, icon = SymbolRegular.Settings24 } }
    };

    // ══════════════════════════════════════════════════════
    // Preset / Hardware (called from pages)
    // ══════════════════════════════════════════════════════
    public void ApplyPresetHardware() {
      // ponytail: delegated to PresetManager — applies 1.1 always, 1.2 only for custom presets.
      // Atomic: all params dispatched on a single thread pool work item.
      PresetManager.ApplyPresetHardware();
    }

    // ══════════════════════════════════════════════════════
    // Tray Integration
    // ══════════════════════════════════════════════════════
    static void BringToForeground() {
      System.Windows.Application.Current.Dispatcher.Invoke(() => {
        ShowInstance();
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
      _statusTimer.Elapsed += OnStatusTimerTick;
      _statusTimer.AutoReset = true;
      _statusTimer.Start();
    }

    void OnStatusTimerTick(object sender, System.Timers.ElapsedEventArgs e) {
      Dispatcher.InvokeAsync(() => UpdateStatusBar());
    }

    void StopStatusTimer() {
      if (_statusTimer != null) {
        _statusTimer.Stop();
        _statusTimer.Elapsed -= OnStatusTimerTick;
        _statusTimer.Dispose();
        _statusTimer = null;
      }
    }

    void OnThemeChanged() {
      Dispatcher.InvokeAsync(() => {
        WindowBackgroundManager.UpdateBackground(this, ApplicationThemeManager.GetAppTheme(), WindowBackdropType.Mica);
      });
    }

	    void UpdateStatusBar() {
	      string icon = HardwareService.PowerOnline ? "\U0001f50c" : "\U0001f50b";
	      StatusBarIcon.Text = icon;
	      StatusBarText.Text = Strings.MainWindowStatusBarFormat(HardwareService.CPUTemp, HardwareService.GPUTemp);
	    }

    void PinToggle_Click(object sender, RoutedEventArgs e) {
      Topmost = !Topmost;
      ConfigService.Topmost = Topmost;
      ConfigService.Save("Topmost");
      PinIcon.Symbol = Topmost ? SymbolRegular.Pin24 : SymbolRegular.PinOff24;
	      PinButton.ToolTip = Topmost ? Strings.MainWindowPinTooltipOn : Strings.MainWindowPinTooltipOff;
    }
  }
}
