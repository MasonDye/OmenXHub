// TrayHelper.cs - 托盘图标高级逻辑
// 管理右键上下文菜单（导航、快捷操作、语言切换）、悬停弹窗定位、工具提示更新
using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using OmenSuperHub.Services;
using OmenSuperHub.Views;
using Wpf.Ui.Controls;

namespace OmenSuperHub.Utils {
  public class TrayHelper : IDisposable {
    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hWnd);

    private readonly Action _bringToForeground;
    private System.Windows.Controls.ContextMenu _contextMenu;
    private CancellationTokenSource _popupCts = null;
    private TrayPopupWindow _popupWindow;
    private NativeTrayIcon _trayIcon;

    public TrayHelper(Action bringToForeground, NativeTrayIcon sharedIcon) {
      _bringToForeground = bringToForeground;
      _contextMenu = new System.Windows.Controls.ContextMenu();
      BuildContextMenu();

      // Create our own NativeTrayIcon for event handling (visible icon)
      _trayIcon = new NativeTrayIcon();
      if (sharedIcon.Icon != null)
        _trayIcon.SetIcon((Icon)sharedIcon.Icon.Clone());
      _trayIcon.MouseEnter += OnMouseEnter;
      _trayIcon.MouseLeave += OnMouseLeave;
      _trayIcon.Click += OnClick;
      _trayIcon.RightClick += OnRightClick;
    }

    void OnMouseEnter() {
      // ponytail: 用户可在设置页关闭悬停浮窗 — 关闭后不创建 TrayPopupWindow。
      if (!ConfigService.TrayHoverPopup) return;
      System.Windows.Application.Current?.Dispatcher.Invoke(() => {
        if (_popupWindow != null) return;
        try {
          var w = new TrayPopupWindow();
          w.UpdateContent();
          w.WindowStartupLocation = WindowStartupLocation.Manual;
          // Start off-screen to avoid flicker; reposition in Loaded after layout
          w.Left = -10000;
          w.Top = -10000;
          _popupWindow = w;
          // ponytail: position one idle tick after ContentRendered — SizeToContent="Height"
          // means ActualHeight isn't final until layout completes; positioning on Loaded
          // (or even synchronously in ContentRendered) used the under-sized height, so the
          // popup opened too low and its bottom sank past the tray icon.
          w.ContentRendered += (s, e) => w.Dispatcher.BeginInvoke(new Action(() => MoveToCursor(w)),
            System.Windows.Threading.DispatcherPriority.ApplicationIdle);
          w.Show();
        } catch { }
      });
    }

    void OnMouseLeave() {
      System.Windows.Application.Current?.Dispatcher.Invoke(() => {
        if (_popupWindow != null) {
          var w = _popupWindow;
          _popupWindow = null;
          w.Close();
        }
      });
    }

    void OnClick() {
      HidePopup();
      _bringToForeground();
    }

    void OnRightClick() {
      HidePopup();
      System.Windows.Application.Current?.Dispatcher.Invoke(() => {
        _contextMenu.IsOpen = false;
        var mw = System.Windows.Application.Current.MainWindow;
        if (mw != null) {
          // Ensure the main window has an HWND even when it was never shown
          // (tray-only boot mode via --tray). Without a host HWND the
          // ContextMenu popup has no PresentationSource and never displays —
          // this is why right-click showed nothing only when started at boot.
          var handle = new System.Windows.Interop.WindowInteropHelper(mw).EnsureHandle();
          _contextMenu.PlacementTarget = mw;
          // Tray context menus must own the foreground or Windows suppresses
          // the menu, so call this unconditionally — not only when visible.
          if (handle != IntPtr.Zero)
            SetForegroundWindow(handle);
        }
        _contextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        _contextMenu.IsOpen = true;
      });
    }

    void MoveToCursor(Window w) {
      // ponytail: keep positioning anchored to the live cursor (the tray icon's
      // physical location). WorkArea-based clamp so the popup stays on-screen
      // whether the icon sits in the taskbar corner or the Win11 overflow popup.
      // The previous catch-fallback hard-coded the main screen's bottom-right and
      // ignored the cursor — that's why overflow-area hover opened the popup in
      // the "old" corner position instead of above the icon.
      try {
        // ponytail: force a synchronous measure/arrange so ActualHeight is final
        // — without this, SizeToContent="Height" popups may still report a stale
        // (under-sized) height at ContentRendered/Idle and open too low.
        w.UpdateLayout();
        double popupW_wpf = w.ActualWidth > 0 ? w.ActualWidth : 280;
        double popupH_wpf = w.ActualHeight > 0 ? w.ActualHeight : 160;
        var mouse = Control.MousePosition;
        var screen = Screen.FromPoint(mouse).WorkingArea;

        float dpiX = 96f, dpiY = 96f;
        try {
          using (var g = Graphics.FromHwnd(IntPtr.Zero)) { dpiX = g.DpiX; dpiY = g.DpiY; }
        } catch { /* fall back to 96 dpi */ }

        // ponytail: do ALL math in PHYSICAL pixels (mouse coords and Screen are
        // physical; WPF/Actual sizes are 1/96" — failing to convert the popup
        // size to physical is the bug that made the popup open too low at scale):
        // mismatched units → bottom ended up *below* the cursor.
        double SX = dpiX / 96.0, SY = dpiY / 96.0;  // wpf→phys scale
        double popupW = popupW_wpf * SX;            // phys px
        double popupH = popupH_wpf * SY;            // phys px

        // ponytail: cursor hovers somewhere on the tray icon (~20px tall glyph),
        // not at its top edge. Lift the popup's bottom edge above the icon's TOP
        // edge: gap = visual_margin (~8px) + icon_height (~20px) = 28 phys px
        // below the cursor. Bottom edge stays a small distance above the icon.
        const double gap = 28;
        double bottomPhys = mouse.Y - gap;           // popup bottom edge (phys px)
        double topPhys = bottomPhys - popupH;        // popup top edge (phys px)
        double leftPhys = mouse.X - popupW / 2.0;   // horizontally center on cursor

        // clamp to working area in physical pixels
        double waLeft = screen.Left, waTop = screen.Top, waRight = screen.Right, waBottom = screen.Bottom;
        if (leftPhys < waLeft) leftPhys = waLeft;
        if (leftPhys + popupW > waRight) leftPhys = waRight - popupW;
        // not enough room above → place below cursor instead
        if (topPhys < waTop) { topPhys = mouse.Y + gap; }
        if (topPhys + popupH > waBottom) topPhys = waBottom - popupH;

        // back to WPF units for Window.Left/Top
        double left = leftPhys * 96.0 / dpiX;
        double top = topPhys * 96.0 / dpiY;

        System.Diagnostics.Debug.WriteLine($"[popup] mouse=({mouse.X},{mouse.Y}) popupW_wpf={popupW_wpf} popupH_wpf={popupH_wpf} popupH_phys={popupH:F0} dpi={dpiX}x{dpiY} gap={gap} left={left:F0} top={top:F0} bottomPhys={bottomPhys:F0} cursorPhys={mouse.Y}");
        w.Left = left;
        w.Top = top;
      } catch {
        // last-resort: above the cursor, clamped to the work area top
        try {
          var mouse = Control.MousePosition;
          var wa = Screen.FromPoint(mouse).WorkingArea;
          w.Left = Math.Max(wa.Left, mouse.X - (int)w.ActualWidth / 2);
          w.Top = Math.Max(wa.Top, mouse.Y - (int)w.ActualHeight - 8);
        } catch {
          var wa = SystemParameters.WorkArea;
          w.Left = wa.Right - 290;
          w.Top = wa.Bottom - 200;
        }
      }
    }

    void HidePopup() {
      _popupCts?.Cancel();
      var w = _popupWindow;
      if (w == null) return;
      _popupWindow = null;
      w.Close();
    }

    void BuildContextMenu() {
      _contextMenu.Items.Clear();
      // ponytail: Quick Actions 段受双门控 —
      // 1) 自动化总开关关 (AutomationEnabled=false) → 后端未 Start, 点了也不响应, 不上菜单
      // 2) 简洁模式把 Automation 页隐藏 → 右键菜单已不显示该页入口, 顶部的快捷操作也应跟着隐藏,
      //    否则用户从右键点了快捷操作却找不到对应的侧栏页, 体验割裂。两个条件任一不满足都跳过。
      bool quickActionsVisible = ConfigService.AutomationEnabled
        && Views.MainWindow.ShouldShowNavItem("Automation");
      if (quickActionsVisible) {
        var qas = AutomationService.GetQuickActions();
        if (qas.Count > 0) {
          foreach (var qa in qas)
            AddMenuItem(qa.Name, () => AutomationProcessor.ExecutePipeline(qa), SymbolRegular.Play24);
          _contextMenu.Items.Add(new System.Windows.Controls.Separator());
        }
      }
      // ponytail: 侧栏过滤共用规则 — 简洁模式白名单 / 普通键盘隐藏灯光 / Settings 常驻。
      // 用户开简洁模式把某页隐藏后,右键菜单还显示该页 = 跳过去发现侧栏没项,体验割裂。
      AddNavMenuItemIfVisible(Strings.SidebarDashboard, "Dashboard", new SymbolIcon(SymbolRegular.Home24));
      AddNavMenuItemIfVisible(Strings.SidebarFan, "Fan", new OmenSuperHub.Controls.FanIcon() { IconSize = 14 });
      AddNavMenuItemIfVisible(Strings.SidebarPerf, "Perf", new SymbolIcon(SymbolRegular.Gauge24));
      AddNavMenuItemIfVisible(Strings.SidebarLighting, "Lighting", new SymbolIcon(SymbolRegular.Lightbulb24));
      AddNavMenuItemIfVisible(Strings.SidebarAutomation, "Automation", new SymbolIcon(SymbolRegular.Rocket24));
      AddNavMenuItemIfVisible(Strings.SidebarMacro, "Macro", new SymbolIcon(SymbolRegular.Keyboard24));
      AddNavMenuItemIfVisible(Strings.SidebarNetworkBoost, "NetworkBoost", new SymbolIcon(SymbolRegular.PlugConnected24));
      AddNavMenuItemIfVisible(Strings.SidebarOther, "Other", new SymbolIcon(SymbolRegular.MoreHorizontal24));
      AddNavMenuItemIfVisible(Strings.SidebarSettings, "Settings", new SymbolIcon(SymbolRegular.Settings24));
      _contextMenu.Items.Add(new System.Windows.Controls.Separator());
      AddMenuItem(Strings.OmenKeyShowMain, () => _bringToForeground(), SymbolRegular.Window24);
      var langMenu = new System.Windows.Controls.MenuItem {
        Header = Strings.LanguageMenu,
        Icon = new SymbolIcon { Symbol = SymbolRegular.Globe24 }
      };
      langMenu.Items.Add(CreateLangMenuItem("简体中文", AppLanguage.SimplifiedChinese));
      langMenu.Items.Add(CreateLangMenuItem("繁體中文", AppLanguage.TraditionalChinese));
      langMenu.Items.Add(CreateLangMenuItem("English", AppLanguage.English));
      _contextMenu.Items.Add(langMenu);
      AddMenuItem(Strings.Help, () => Views.HelpWindow.ShowInstance(), SymbolRegular.QuestionCircle24);
      _contextMenu.Items.Add(new System.Windows.Controls.Separator());
      AddMenuItem(Strings.Exit, () => TrayService.Exit(), SymbolRegular.SignOut24);
    }

    // ponytail: 走 MainWindow.ShouldShowNavItem 的过滤规则;隐藏页不进菜单,跟侧栏一致。
    void AddNavMenuItemIfVisible(string header, string pageTag, object icon) {
      if (!Views.MainWindow.ShouldShowNavItem(pageTag)) return;
      AddNavMenuItem(header, pageTag, icon);
    }

    void AddNavMenuItem(string header, string pageTag, object icon) {
      var item = new System.Windows.Controls.MenuItem {
        Header = header,
        Icon = icon as UIElement
      };
      item.Click += (s, e) => { _contextMenu.IsOpen = false; Views.MainWindow.NavigateToPage(pageTag); };
      _contextMenu.Items.Add(item);
    }

    void AddMenuItem(string header, Action action, SymbolRegular? icon = null) {
      var item = new System.Windows.Controls.MenuItem { Header = header };
      if (icon.HasValue) item.Icon = new SymbolIcon { Symbol = icon.Value };
      item.Click += (s, e) => { _contextMenu.IsOpen = false; action(); };
      _contextMenu.Items.Add(item);
    }

    System.Windows.Controls.MenuItem CreateLangMenuItem(string header, AppLanguage lang) {
      var item = new System.Windows.Controls.MenuItem {
        Header = header, IsCheckable = true, IsChecked = Strings.Current == lang
      };
      item.Click += (s, e) => {
        Strings.SetLanguage(lang);
        ConfigService.Language = lang.ToString();
        ConfigService.Save("Language");
        BuildContextMenu();
        Views.MainWindow.ApplyLanguageToInstance();
      };
      return item;
    }

    internal void RebuildMenu() {
      System.Windows.Application.Current?.Dispatcher.Invoke(() => BuildContextMenu());
    }


    public void SetTooltip(string tip) {
      _trayIcon?.SetTip(tip);
    }

    public void MakeVisible() { _trayIcon.Show(); }
    public void SetIcon(Icon icon) { _trayIcon.Icon = (Icon)icon.Clone(); }
    public void StartTooltipTimer() {
      // Tooltip is now updated by TrayService.UpdateTooltip() timer (no duplicate timer)
    }

    public void ShowBalloonTip(string title, string text, int timeoutMs) {
      _trayIcon?.ShowBalloonTip(title, text, timeoutMs);
    }

    public void Dispose() {
      GC.SuppressFinalize(this);
      HidePopup();
      _popupCts?.Dispose();
      _trayIcon?.Dispose();
      _trayIcon = null;
    }
  }
}
