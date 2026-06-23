using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using OmenSuperHub.Services;
using OmenSuperHub.Views;
using Wpf.Ui.Controls;

namespace OmenSuperHub.Utils {
  public class TrayHelper : IDisposable {
    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr handle);

    private readonly Action _bringToForeground;
    private System.Windows.Controls.ContextMenu _contextMenu;
    private System.Timers.Timer _tooltipUpdateTimer;
    private CancellationTokenSource _popupCts;
    private TrayPopupWindow _popupWindow;
    private NativeTrayIcon _trayIcon;

    public TrayHelper(Action bringToForeground, NativeTrayIcon sharedIcon) {
      _bringToForeground = bringToForeground;
      _contextMenu = new System.Windows.Controls.ContextMenu();
      BuildContextMenu();

      // Create our own NativeTrayIcon for event handling (visible icon)
      _trayIcon = new NativeTrayIcon();
      _trayIcon.SetIcon(sharedIcon.Icon);
      _trayIcon.MouseEnter += OnMouseEnter;
      _trayIcon.MouseLeave += OnMouseLeave;
      _trayIcon.Click += OnClick;
      _trayIcon.RightClick += OnRightClick;

      _tooltipUpdateTimer = new System.Timers.Timer(1000) { AutoReset = true };
      _tooltipUpdateTimer.Elapsed += (_, _) => UpdateTooltip();
      _tooltipUpdateTimer.Start();
    }

    void OnMouseEnter() {
      System.Windows.Application.Current?.Dispatcher.Invoke(() => {
        if (_popupWindow != null) return;
        try {
          var w = new TrayPopupWindow();
          w.UpdateContent();
          w.WindowStartupLocation = WindowStartupLocation.Manual;
          MoveToCursor(w);
          _popupWindow = w;
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
        var mw = System.Windows.Application.Current.MainWindow;
        if (mw != null) {
          var handle = new System.Windows.Interop.WindowInteropHelper(mw).Handle;
          SetForegroundWindow(handle);
        }
        _contextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        _contextMenu.IsOpen = true;
      });
    }

    void MoveToCursor(Window w) {
      try {
        var mouse = Control.MousePosition;
        var screen = Screen.FromPoint(mouse).WorkingArea;
        int cw = 220, ch = 110;
        int gap = 30;
        int left = mouse.X - cw - gap;
        int top = mouse.Y - ch - 110;
        if (left < screen.Left) left = mouse.X;
        if (top < screen.Top) top = mouse.Y;
        if (left + cw > screen.Right) left = screen.Right - cw;
        if (top + ch > screen.Bottom) top = screen.Bottom - ch;

        float dpiX = 96f, dpiY = 96f;
        using (var g = Graphics.FromHwnd(IntPtr.Zero)) {
          dpiX = g.DpiX;
          dpiY = g.DpiY;
        }
        w.Left = left * 96.0 / dpiX;
        w.Top = top * 96.0 / dpiY;
      } catch {
        w.Left = SystemParameters.WorkArea.Right - 230;
        w.Top = SystemParameters.WorkArea.Bottom - 93;
      }
    }

    public void UpdatePopupIfOpen() {
      var w = _popupWindow;
      if (w != null && w.IsLoaded) {
        System.Windows.Application.Current?.Dispatcher.Invoke(() => w.UpdateContent());
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
      var qas = AutomationService.GetQuickActions();
      if (qas.Count > 0) {
        foreach (var qa in qas)
          AddMenuItem(qa.Name, () => AutomationProcessor.ExecutePipeline(qa), SymbolRegular.Play24);
        _contextMenu.Items.Add(new System.Windows.Controls.Separator());
      }
      AddNavMenuItem(Strings.SidebarDashboard, "Dashboard", SymbolRegular.Home24);
      AddNavMenuItem(Strings.SidebarFan, "Fan", SymbolRegular.ArrowSync24);
      AddNavMenuItem(Strings.SidebarPerf, "Perf", SymbolRegular.Gauge24);
      AddNavMenuItem(Strings.SidebarLighting, "Lighting", SymbolRegular.Lightbulb24);
      AddNavMenuItem(Strings.SidebarAutomation, "Automation", SymbolRegular.Rocket24);
      AddNavMenuItem(Strings.SidebarOther, "Other", SymbolRegular.MoreHorizontal24);
      AddNavMenuItem(Strings.SidebarSysInfo, "SysInfo", SymbolRegular.Info24);
      AddNavMenuItem(Strings.SidebarSettings, "Settings", SymbolRegular.Settings24);
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

    void AddNavMenuItem(string header, string pageTag, SymbolRegular icon) {
      var item = new System.Windows.Controls.MenuItem {
        Header = header,
        Icon = new SymbolIcon { Symbol = icon }
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

    void UpdateTooltip() {
      HardwareService.QueryHardware();
      if (HardwareService.MonitorFan)
        HardwareService.FanSpeedNow = OmenHardware.GetFanLevel();
      try {
        var tip = $"OMEN X Hub · CPU {(int)HardwareService.CPUTemp}°C";
        if (ConfigService.MonitorGPU)
          tip += $" · GPU {(int)HardwareService.GPUTemp}°C";
        _trayIcon?.SetTip(tip);
      } catch { }
    }

    public void MakeVisible() { _trayIcon.Show(); }

    public void Dispose() {
      GC.SuppressFinalize(this);
      HidePopup();
      _popupCts?.Dispose();
      _tooltipUpdateTimer?.Stop();
      _tooltipUpdateTimer?.Dispose();
      _tooltipUpdateTimer = null;
      _trayIcon?.Dispose();
      _trayIcon = null;
    }

    public static Icon LoadLogoIcon(int size = 0) {
      var asm = Assembly.GetExecutingAssembly();
      using (var stream = asm.GetManifestResourceStream("OmenSuperHub.Resources.fan.ico")) {
        if (stream != null) {
          if (size > 0) return new Icon(stream, size, size);
          return new Icon(stream);
        }
      }
      return CreateLogoIcon(size > 0 ? size : 32);
    }

    public static Icon CreateLogoIcon(int size) {
      using (var bitmap = new Bitmap(size, size)) {
        using (Graphics g = Graphics.FromImage(bitmap)) {
          g.Clear(Color.Transparent);
          g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
          float s = size / 100f;
          using (var brush = new System.Drawing.Drawing2D.LinearGradientBrush(
              new PointF(0, size * 0.79f), new PointF(size * 0.98f, size * 0.39f),
              Color.Transparent, Color.Transparent)) {
            brush.InterpolationColors = new System.Drawing.Drawing2D.ColorBlend {
              Colors = new[] { Color.FromArgb(0xFF, 0x55, 0xE1), Color.FromArgb(0xFF, 0x04, 0x02), Color.FromArgb(0xFF, 0xB4, 0x02) },
              Positions = new[] { 0f, 0.46078f, 1f }
            };
            var topV = new System.Drawing.Drawing2D.GraphicsPath();
            topV.AddPolygon(new PointF[] {
              new PointF(3*s, 47*s), new PointF(50*s, 3*s), new PointF(97*s, 47*s),
              new PointF(70*s, 47*s), new PointF(50*s, 30*s), new PointF(30*s, 47*s)
            });
            var bottomV = new System.Drawing.Drawing2D.GraphicsPath();
            bottomV.AddPolygon(new PointF[] {
              new PointF(3*s, 53*s), new PointF(50*s, 97*s), new PointF(97*s, 53*s),
              new PointF(70*s, 53*s), new PointF(50*s, 70*s), new PointF(30*s, 53*s)
            });
            g.FillPath(brush, topV);
            g.FillPath(brush, bottomV);
          }
          IntPtr hIcon = bitmap.GetHicon();
          using (var temp = Icon.FromHandle(hIcon)) {
            using (var ms = new MemoryStream()) {
              temp.Save(ms);
              ms.Position = 0;
              DestroyIcon(hIcon);
              return new Icon(ms);
            }
          }
        }
      }
    }
  }
}
