// MainWindow.xaml.cs - 主窗口逻辑
// WPF-UI NavigationView 侧边栏导航、鼠标滚轮处理、页面切换动画、窗口拖拽
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using OmenSuperHub.Controls;
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
    // ponytail: 保护引用 — 主面板 Hide 时 ReleaseFrontend 需要 Clear 它,
    // 否则 _cache 永久钉住所有进过的页面。原代码 new 完丢引用,只能等进程结束回收。
    static CachedPageService _pageService;
    // ponytail: ReleaseFrontend 把 ContentPresenter.Content 清空 + ClearJournal 后,窗口外观
    // 上当前页可视树不再存在。Show 唤回时必须 Navigate 回某页,否则用户看到一片白板,
    // 只有点别的侧栏项再点回来才渲染(原 Fix 副作用)。_frontendReleased=true 表示下一次任何
    // "显示"路径需要 re-navigate。托盘双击/二次启动 → 回 Dashboard 总览页。
    // _pendingNavigateTag:托盘右键菜单 NavigateToPage("Fan") 这种"用户指明目标"路径会先贴上
    // 该字段,ResyncFrontendIfReleased 看见就走该目标页而不是 Dashboard(同时清掉,一次性)。
    static bool _frontendReleased;
    static string _pendingNavigateTag;
    Page _activePage;

    static void EnsureWindow() {
      if (_instance != null && _instance.IsLoaded) return;
      _instance = new MainWindow();
    }

    public static void ShowInstance() {
      bool wasLoaded = _instance != null && _instance.IsLoaded;
      EnsureWindow();
      _instance.BeginAnimation(UIElement.OpacityProperty, null);
      if (!wasLoaded || _trayHidden) {
        // ponytail: 首次显示 / 从托盘恢复 — Show() 前必须 Opacity=0,否则 WPF 在 Show()
        // 后到 FadeIn 设 Opacity=0 之间会渲染一帧 Opacity=1 的窗口,此时 Mica 背景尚未
        // 初始化完成,呈现白色窗口闪一下。FadeIn 内部也会设 0,但 Show() 和 FadeIn 之间
        // 已有一帧渲染间隔。参考 StartTrayOnly() 用 Opacity=0 隐藏首帧的写法。
        _trayHidden = false;
        _instance.Opacity = 0;
        _instance.Show();
        _instance.ResyncFrontendIfReleased();
        FadeIn(_instance, () => _instance.Activate());
      } else if (_instance.Visibility == Visibility.Visible && _instance.WindowState != WindowState.Minimized) {
        _instance.Opacity = 1;
        FadeOut(_instance, () => _instance.Hide());
        return;
      } else {
        _instance.Opacity = 0;
        // ponytail: 窗口可见但最小化时点托盘图标会走到这里 — Show() 对已可见窗口是
        // no-op,窗口会一直停在最小化,看起来"托盘唤不回窗口"。先恢复 Normal 再 Show。
        if (_instance.WindowState == WindowState.Minimized)
          _instance.WindowState = WindowState.Normal;
        _instance.Show();
        _instance.ResyncFrontendIfReleased();
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
      // Shown minimized + ShowActivated=false + no taskbar → minimized HWNDs are
      // never composited on screen, so nothing can flash regardless of DWM timing.
      _instance.ShowInTaskbar = false;
      _instance.ShowActivated = false;
      _instance.Opacity = 0;
      // ponytail: Opacity=0 的分层属性是 HWND 显示之后才生效的,冷启动时 DWM 会先
      // 合成一帧尚未渲染的窗口 → 白屏闪一下再被 Hide。最小化创建的 HWND 不上屏,
      // 从根上消除首帧可见性。
      _instance.WindowState = WindowState.Minimized;
      _instance.Show();   // fires Loaded synchronously, creates HWND + PresentationSource
      _instance.Hide();
      // WPF 对隐藏窗口设 WindowState=Normal 内部走 SW_RESTORE,会把窗口重新显示
      // 出来 — 趁 Opacity=0 且无任务栏按钮时归位 Normal 再补一次 Hide,让托盘
      // 唤起路径看到的状态与旧实现完全一致 (Hidden + Normal + Opacity=1)。
      _instance.WindowState = WindowState.Normal;
      _instance.Hide();
      _instance.ShowInTaskbar = true;
      _instance.Opacity = 1;
      // ponytail: 消除首帧白屏的 Minimized→Show 序列会在窗口尚未完成布局时把
      // RestoreBounds 写成 0×0/左下角脏值(issue #25 自启首点托盘小窗)。此处趁
      // Loaded 已跑、布局已完成,显式重置到 XAML 默认尺寸并居中,覆盖脏 RestoreBounds,
      // 首次 ShowInstance 才能以正常尺寸唤出。
      _instance.Width = 1150;   // 与 MainWindow.xaml 的 Width/Height 一致
      _instance.Height = 750;
      _instance.Left = (System.Windows.SystemParameters.WorkArea.Width - _instance.Width) / 2;
      _instance.Top = (System.Windows.SystemParameters.WorkArea.Height - _instance.Height) / 2;
      _instance.WindowState = WindowState.Normal;
    }

    public static void ApplyLanguageToInstance() {
      if (_instance == null) return;
    }

    public static void NavigateToPage(string pageTag) {
      bool wasLoaded = _instance != null && _instance.IsLoaded;
      // ponytail: 告诉将来的 ResyncFrontendIfReleased "我要去 pageTag,别抢着 Navigate(Dashboard)"。
      // 仅在窗口隐藏/未加载(走 Show 路径会触发 IsVisibleChanged→Resync) 时才有意义 — 但一次性置上,
      // _pendingNavigateTag 由 Resync 自己消费并清零;可见路径中 NavigateToPage 置了也无碍
      // (Resync 看见 _frontendReleased=false 直接早退,根本不读这字段)。
      _pendingNavigateTag = pageTag;
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
      _pageService = new Services.CachedPageService();
      NavigationView.SetPageService(_pageService);

      // ponytail: 二次启动唤醒 — App.ShowExistingWindow 用 PostMessage(WM_SHOW_MAIN) 通知
      // 已运行实例,但之前没有任何窗口挂过该消息的 hook:窗口隐藏到托盘时 MainWindowHandle
      // 为 0,EnumWindows 兜底会拿到 OSD 浮窗等无标题工具窗口,消息被静默丢弃 →
      // "重启/双击 exe 软件打不开"(issue #20)。
      SourceInitialized += (s, e) => {
        var source = System.Windows.Interop.HwndSource.FromHwnd(
          new System.Windows.Interop.WindowInteropHelper(this).Handle);
        source?.AddHook(WakeUpWndProc);
      };

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
        // ponytail: !_trayHidden — 托盘静默启动的最小化 Show() 也会触发本事件,
        // 不加守卫会把刚关掉的任务栏按钮又打开,开机时闪一个任务栏图标。
        if (WindowState == WindowState.Minimized && !_trayHidden) {
          ShowInTaskbar = true;
        }
      };

      // ponytail: 关闭主窗口 = Hide() (Closing handler 里 e.Cancel=true)。隐藏后状态栏
      // 标签无可见输出,2s 的 _statusTimer 仍每 tick marshal 到 UI 线程刷新分离元素 — 纯开销。
      // 进程、托盘轮询、风扇/灯光/自动化等后端服务全部保持运行 (它们由 TrayService/App 拥有,
      // 不受窗口可见性影响)。显示时再 Start,必要时补一拍立即刷新。
      // ponytail: Hide 不仅是停 timer — 还要释放前端缓存(CachedPageService._cache 永驻)与
      // 各页通过 Loaded 订阅的静态事件引用(否则 GC 不收)。ReleaseFrontend 强制当前页 Unloaded,
      // 清后退栈,清页面缓存。后端链路不动。下次 Show 重建页面,与首访问一致。
      IsVisibleChanged += (s, e) => {
        if (IsVisible) {
          StartStatusTimer();
          OnStatusTimerTick(null, null);
          ResyncFrontendIfReleased();
        }
        else { StopStatusTimer(); ReleaseFrontend(); }
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
        UpdateNavItemsInternal();
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

    // ponytail: 显式"唤起"语义 — 不能复用 ShowInstance(它在窗口可见时是开/关切换,
    // 二次启动会把已显示的窗口藏起来)。
    IntPtr WakeUpWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) {
      if (msg == (int)App.WM_SHOW_MAIN) {
        handled = true;
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        BeginAnimation(UIElement.OpacityProperty, null);
        Opacity = 1;  // 可能停在淡出动画中途的半透明值
        Show();
        ResyncFrontendIfReleased();
        Activate();
      }
      return IntPtr.Zero;
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
          NavIcon.Content = kvp.Value.icon;
          return;
        }
      }
    }

    static readonly Dictionary<string, Type> _pageTypeMap = new Dictionary<string, Type> {
      { "Dashboard", typeof(DashboardPage) }, { "Fan", typeof(FanPage) },
      { "Perf", typeof(PerfPage) }, { "Lighting", typeof(LightingPage) },
      { "Automation", typeof(AutomationPage) },
      { "Macro", typeof(MacroPage) },
      { "Other", typeof(OtherPage) }, { "Settings", typeof(SettingsPage) },
      { "NetworkBoost", typeof(NetworkBoostPage) },
      // ponytail: 二级菜单——仅从 PerfPage 跳转按钮进入，不显示在侧边栏
      { "CoreKeep", typeof(CoreKeepPage) },
      { "RoutingRules", typeof(RoutingRulesPage) }
    };

    struct PageInfo { public Type pageType; public string title; public object icon; }

    static readonly Dictionary<string, PageInfo> _pageInfos = new Dictionary<string, PageInfo> {
      { "Dashboard", new PageInfo { pageType = typeof(DashboardPage), title = Strings.PageDashboard, icon = new SymbolIcon(SymbolRegular.Home24) { FontSize = 14 } } },
      { "Fan", new PageInfo { pageType = typeof(FanPage), title = Strings.PageFan, icon = new FanIcon() { IconSize = 14 } } },
      { "Perf", new PageInfo { pageType = typeof(PerfPage), title = Strings.PagePerf, icon = new SymbolIcon(SymbolRegular.Gauge24) { FontSize = 14 } } },
      { "Lighting", new PageInfo { pageType = typeof(LightingPage), title = Strings.PageLighting, icon = new SymbolIcon(SymbolRegular.Lightbulb24) { FontSize = 14 } } },
      { "Automation", new PageInfo { pageType = typeof(AutomationPage), title = Strings.PageAutomation, icon = new SymbolIcon(SymbolRegular.Rocket24) { FontSize = 14 } } },
      { "Macro", new PageInfo { pageType = typeof(MacroPage), title = Strings.PageMacro, icon = new SymbolIcon(SymbolRegular.Keyboard24) { FontSize = 14 } } },
      { "Other", new PageInfo { pageType = typeof(OtherPage), title = Strings.PageOther, icon = new SymbolIcon(SymbolRegular.MoreHorizontal24) { FontSize = 14 } } },
      { "Settings", new PageInfo { pageType = typeof(SettingsPage), title = Strings.PageSettings, icon = new SymbolIcon(SymbolRegular.Settings24) { FontSize = 14 } } },
      { "NetworkBoost", new PageInfo { pageType = typeof(NetworkBoostPage), title = Strings.PageNetworkBoost, icon = new SymbolIcon(SymbolRegular.PlugConnected24) { FontSize = 14 } } },
      // ponytail: 二级菜单——仅从 PerfPage 跳转按钮进入，不显示在侧边栏
      { "CoreKeep", new PageInfo { pageType = typeof(CoreKeepPage), title = Strings.PageCoreKeep, icon = new SymbolIcon(SymbolRegular.AppsList24) { FontSize = 14 } } },
      { "RoutingRules", new PageInfo { pageType = typeof(RoutingRulesPage), title = Strings.PageRoutingRules, icon = new SymbolIcon(SymbolRegular.Router24) { FontSize = 14 } } }
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
      // ponytail: 褪色卡死看门狗 — Show() 后立刻再次唤醒的竞态会把进行中的动画移除
      // (BeginAnimation(null))而本地 Opacity 停在 0 → "HWND 可见但全透明":tasklist 有
      // 窗口标题、屏幕上无窗口(#20"软件打不开"同族)。350ms(褪色 150ms 已结束)后仍
      // 可见且透明则强制拉回 1;正常褪色中被提前置 1 无害(动画值优先,Completed 亦置 1)。
      var watchdog = new System.Windows.Threading.DispatcherTimer {
        Interval = TimeSpan.FromMilliseconds(350)
      };
      watchdog.Tick += (s, a) => {
        watchdog.Stop();
        if (element is Window w && w.IsLoaded && w.Visibility == Visibility.Visible && w.Opacity < 0.99)
          w.Opacity = 1;
      };
      watchdog.Start();
    }

    System.Timers.Timer _statusTimer;
    void StartStatusTimer() {
      // ponytail: 幂等 — IsVisibleChanged 在 Loaded 与每次 FadeIn 显示时都会触发 Start,
      // 重入时复用已有 timer 即可,避免旧 timer 泄漏叠加多个 Elapsed 订阅。
      if (_statusTimer != null) return;
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

    // ponytail: 关主面板时的"真释放"。后端服务不动,只回收前端:
    //  1) 摘出当前页 — Wpf.Ui 的内容承载控件 NavigationViewContentPresenter 不是 ContentPresenter,
    //     是 Frame(ContentControl 的子类;实测基类链 Frame→ContentControl→Control→FrameworkElement)。
    //     所以 getter 拿到的是 Frame,这里 cast 到 ContentControl 置 Content=null。WPF 同步触发该页
    //     Unloaded — 各页已在 Unloaded 解订阅/停 timer (PerfPage/DashboardPage/FanPage/LightingPage/
    //     AutomationPage/MacroPage/NetworkBoostPage/OtherPage/RoutingRulesPage)。
    //  2) ClearJournal — Wpf.Ui 公开 API,清后退栈避免残留旧页实例引用。
    //  3) _pageService.Clear — 清 Dictionary 引用,Page 此时可被 GC。
    //  4) GC + EmptyWorkingSet — 仅清引用不会立刻缩工作集;Wpf.Ui Page 大多是 100KB+ 的 XAML 可视树,
    //     显式回收一次让任务管理器看得见内存回落。第 2+ 次关面板时上述清理已是 no-op、无 GC 必要时也省。
    // 下次 Show 跳转走 Navigate → CachedPageService 重新 ctor + Loaded,与首访问体验一致。
    // 注意:wakeUpWndProc/ShowInstance 的"可见但最小化"路径不走 Hide,不会触发本方法 —
    // 那种情况下页面应保持热缓存 (用户体验:从最小化唤回不重建页面)。
    // ponytail: 反射元数据缓存 — GetProperty/GetSetMethod 结果按类型固定,缓存后每次 Hide
    // 免 3+n 次反射查找(原每 Hide 都重新查找)。
    static readonly System.Reflection.PropertyInfo _navPresenterProp = typeof(Wpf.Ui.Controls.NavigationView).GetProperty(
        "NavigationViewContentPresenter",
        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
    static readonly System.Reflection.PropertyInfo _navStackProp = typeof(Wpf.Ui.Controls.NavigationView).GetProperty(
        "NavigationStack",
        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
    static readonly System.Reflection.MethodInfo _navSelectedSetter = typeof(Wpf.Ui.Controls.NavigationView).GetProperty(
        "SelectedItem",
        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)?.GetSetMethod(nonPublic: true);
    static readonly System.Collections.Generic.Dictionary<System.Type, System.Reflection.MethodInfo> _deactivateCache
        = new System.Collections.Generic.Dictionary<System.Type, System.Reflection.MethodInfo>();

    void ReleaseFrontend() {
      var nav = NavigationView;
      try {
        if (nav != null) {
          var presenter = _navPresenterProp?.GetValue(nav) as System.Windows.Controls.ContentControl;
          if (presenter != null) presenter.Content = null;
          nav.ClearJournal();
          // ponytail: 真正的短路源 — Wpf.Ui NavigateInternal (decompiled 3.1.1:1321-1327):
          //   if (NavigationStack.Count > 0 && NavigationStack[last] == viewItem) return false;
          // 引用相等就跳过 UpdateContent → 窗口停在 ReleaseFrontend 留下的空 ContentPresenter = 白板。
          // ClearJournal() 只清 Journal + _complexNavigationStackHistory,不动 NavigationStack;
          // NavigationView.Unloaded 在窗口 Hide 时不触发(可视树仍在,OnUnloaded:845 只在模板拆除才跑),
          // 所以 NavigationStack 带着关闭前那页的 viewItem 跨 hide/show 存活。
          //   关面板停在总览 → NavigationStack=[dashboardItem] → 重开 Navigate(Dashboard) 字典取到
          //   同一个 dashboardItem 引用 → last==viewItem → return false → 白板。
          //   关面板停在别的页 → last≠dashboardItem → 不短路 → 正常重建。
          // 这就是"如果在总览页关闭就不能"的根因。修法:反射取 protected NavigationStack 并 Clear()。
          // NavigationStack 是 protected get (Family 可见性,非 public),与 SelectedItem(public get)相反 —
          // NonPublic binding 能匹配它,GetProperty(NonPublic|Instance) 返回 PropertyInfo,GetValue 可用。
          // ponytail: 清栈前先逐项 Deactivate。NavigationStackOnCollectionChanged 的 Reset
          // 分支(decompiled:1123-1146)只动面包屑栏 _breadcrumbBarItems,不 Deactivate 被移除项。
          // 不主动清 → 旧页 item 的 Activated 视觉态跨 hide/show 存活 = 关前那页侧栏按钮继续亮、
          // 重开进 Dashboard 后 Dashboard 按钮反而不亮(旧高亮盖新的)。逐项 Deactivate(nav) 后再
          // Clear(),两段症状一起修。
          // 反射 duck-type 调 Deactivate(nav) — INavigationViewItem 接口在 Wpf.Ui 3.1.1 不在
          // Wpf.Ui.Controls.Interfaces 命名空间(CS0234),与其猜命名空间不如直接在运行时类型上 Invoke,
          // 与上方 presenterProp/selectedSetter 同款反射模式(元数据已缓存,Invoke 才是每次 Hide 的成本)。
          // 上限:逐项 Invoke 是 O(n),n = 已进过的页数(≤ 侧栏项数 + 子页),量级可忽略。
          var navStack = _navStackProp?.GetValue(nav) as System.Collections.IList;
          if (navStack != null) {
            var items = new System.Collections.Generic.List<object>();
            foreach (var it in navStack) if (it != null) items.Add(it);
            foreach (var it in items) {
              try {
                var t = it.GetType();
                if (!_deactivateCache.TryGetValue(t, out var deact)) {
                  deact = t.GetMethod("Deactivate",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                  _deactivateCache[t] = deact;
                }
                deact?.Invoke(it, new object[] { nav });
              } catch { }
            }
            navStack.Clear();
          }
          // ponytail: 次要 — 清 SelectedItem 让重开时 sidebar 高亮刷新 (NavigateInternal:1339
          // 检查 SelectedItem != NavigationStack[0] 后 OnSelectionChanged)。SelectedItem 是
          // public get + protected set;GetProperty(NonPublic|Instance) 因 public getter 返回 null
          // (前两轮的坑),必须 GetProperty(Public|Instance).GetSetMethod(nonPublic:true).Invoke。
          _navSelectedSetter?.Invoke(nav, new object[] { null });
        }
      } catch { /* ponytail: 导航控件尚未应用模板时成员可能 null — 此时本就无页可清 */ }
      _pageService?.Clear();
      _activePage = null;
      // ponytail: 解耦 —— 断开鼠标滚轮 handler 对旧页 UIElement 的引用。_wheelHandler 是捕获旧 page
      // 的 lambda,_wheelRoot 指向旧 page 的祖先 UIElement;MainWindow 常驻持有这两个字段会让旧 page 无法 GC。
      // 下次 Show 走 AttachWheelHandler 时会重新指向新页(它内部先 RemoveHandler 旧的)。
      if (_wheelRoot != null && _wheelHandler != null)
        _wheelRoot.RemoveHandler(UIElement.PreviewMouseWheelEvent, _wheelHandler);
      _wheelHandler = null; _wheelRoot = null;
      // ponytail: 仅清引用只是允许被回收,真正缩工作集需要 GC + 工作集修剪。Wpf.Ui Page 是
      // 100KB+ XAML 可视树,显式回收一次让任务管理器看得见内存回落。一次 Hide 的渲染开销远高于
      // 这 200µs。和 DashboardPage:492 用的是同一个 PSAPI 调用。
      System.GC.Collect();
      System.GC.WaitForPendingFinalizers();
      System.GC.Collect();
      try { NativeMethods_Proc.EmptyWorkingSet(System.Diagnostics.Process.GetCurrentProcess().Handle); } catch { }
      // ponytail: 标记需要 re-navigate。下次任何 Show 路径看到此标志会 Navigate 回 Dashboard,
      // 重建可视树。否则窗口会停在"ContentPresenter 已清空"的白板上。
      _frontendReleased = true;
    }

    // ponytail: ReleaseFrontend 把 ContentPresenter.Content 清空并 ClearJournal,所以窗口
    // 被再次显示时可视树是空的 — 必须重新挂上一个 Page 才不是白板。
    //
    // 目标页选择:
    //   - 托盘右键菜单 NavigateToPage("Fan/Perf/..."):用户指明目标 -> _pendingNavigateTag 已贴上,
    //     直接去该目标页(并清零一次性)。NavigateToPage 自己随后还会 Navigate 一次同页 — 撞两次
    //     Navigate 不影响结果,Resync 提前一帧搭好可视树,NavigateToPage 那次 Navigate 在更晚的
    //     DispatcherPriority.Loaded 跑,Wpf.Ui 同一 SelectedItem 上的第二次 Navigate 是幂等。
    //   - 托盘双击 / 二次启动 CallEvent(WM_SHOW_MAIN):无 pending -> 默认 Dashboard 总览页。
    //
    // 用 Navigate(targetType) 而非 ReplaceContent(targetType):
    //   - ReplaceContent 只替换 Presenter 内容、完全不动 NavigationView 的 selection,
    //     侧栏高亮会留在关面板前那页(如 Fan),内容已是 Dashboard 而高亮还是 Fan —— 视觉割裂,
    //     即用户报的"点开程序正常刷新回到总览页但导航栏没回到"。
    //   - Navigate 走 Wpf.Ui 原生 selection 驱动路径:按 TargetPageType 匹配侧栏项并刷新高亮,
    //     内容与高亮一起归位。ReleaseFrontend 已清掉 SelectedItem 与缓存,不命中"同项短路径"。
    // ponytail: BeginInvoke(Background) 延迟一帧 — 直接同步 Navigate 会与 Show 的窗口
    // 可见性切换 + FadeIn Opacity 动画争抢布局,VisualTree 未到位时仍渲染空白一帧;Background 在
    // Loaded 之后、Render 之前,让 Page 可视树先 build 起来再让 FadeIn 透出。
    void ResyncFrontendIfReleased() {
      if (!_frontendReleased) return;
      _frontendReleased = false;
      // 托盘右键菜单 / 跨页跳转指明的目标优先于默认 Dashboard
      string tag = _pendingNavigateTag ?? "Dashboard";
      _pendingNavigateTag = null;
      if (!_pageTypeMap.TryGetValue(tag, out var targetType)) targetType = typeof(DashboardPage);
      Dispatcher.BeginInvoke(new Action(() => {
        try {
          NavigationView.Navigate(targetType);
        } catch { /* ponytail: 导航失败不该阻塞窗口显示,用户可手动点侧栏重建 */ }
      }), System.Windows.Threading.DispatcherPriority.Background);
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

    // ══════════════════════════════════════════════════════
    // Simple Mode — 简洁模式下隐藏未勾选的导航项
    // ══════════════════════════════════════════════════════
    public static void UpdateNavigationItems() {
      if (_instance == null || !_instance.IsLoaded) return;
      _instance.UpdateNavItemsInternal();
    }

    void UpdateNavItemsInternal() {
      // ponytail: 过滤规则集中到 ShouldShowNavItem,与 TrayHelper.BuildContextMenu 共用一份,
      // 否则侧栏隐藏后托盘右键菜单仍显示该页 = 不一致。
      foreach (var item in NavigationView.MenuItems.OfType<NavigationViewItem>()) {
        var tag = item.TargetPageTag?.ToString();
        item.Visibility = ShouldShowNavItem(tag) ? Visibility.Visible : Visibility.Collapsed;
      }
    }

    // ponytail: 侧栏与托盘右键菜单共用的可见性规则。
    //   - Settings 始终可见 —— 否则用户开简洁模式后再也回不到设置页关掉它;
    //   - Lighting 两条独立隐藏路径(任一条命中即隐藏,两者都不能由简洁模式白名单反过来放回):
    //       (a) 硬件确认普通键盘(无 RGB 无灯条) IsLightingPageSupported==false;探测失败保守返回 true 不隐藏;
    //       (b) 用户主动选 LightingUseOfficial="使用官方灯效软件"后持久隐藏,设置页存根卡可撤回;
    //   - SimpleMode=false → 默认全可见(除 Lighting 上述例外)。
    public static bool ShouldShowNavItem(string tag) {
      if (string.IsNullOrEmpty(tag)) return true;
      if (tag == "Settings") return true;
      if (tag == "Lighting") {
        bool lightingSupported = OmenLighting.IsLightingPageSupported() && !ConfigService.LightingUseOfficial;
        if (!lightingSupported) return false;
      }
      if (ConfigService.EnableSimpleMode) {
        var allowed = new HashSet<string>((ConfigService.SimpleModeNavItems ?? "")
          .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
        return allowed.Contains(tag);
      }
      return true;
    }
  }
}
