using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using OmenSuperHub.Services;
using Wpf.Ui.Controls;

namespace OmenSuperHub.Utils {
  public class TrayHelper : IDisposable {
    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr handle);

    private readonly Action _bringToForeground;
    private System.Windows.Controls.ContextMenu _contextMenu;
    private NotifyIcon _notifyIcon;
    private System.Timers.Timer _tooltipUpdateTimer;

    public TrayHelper(Action bringToForeground, NotifyIcon existingIcon = null) {
      _bringToForeground = bringToForeground;
      _contextMenu = new System.Windows.Controls.ContextMenu();
      BuildContextMenu();

      _notifyIcon = existingIcon ?? CreateDefaultIcon();
      _notifyIcon.MouseClick += OnMouseClick;
      _notifyIcon.MouseDoubleClick += (s, e) => _bringToForeground();

      _tooltipUpdateTimer = new System.Timers.Timer(1000) { AutoReset = true };
      _tooltipUpdateTimer.Elapsed += (_, _) => UpdateTooltip();
      _tooltipUpdateTimer.Start();
    }

    NotifyIcon CreateDefaultIcon() {
      var icon = new NotifyIcon { Icon = LoadLogoIcon(), Text = "OMEN X Hub" };
      return icon;
    }

    void OnMouseClick(object s, MouseEventArgs e) {
      if (e.Button == MouseButtons.Right) {
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
    }

    void BuildContextMenu() {
      _contextMenu.Items.Clear();

      // Quick actions (from enabled pipelines without triggers)
      var qas = AutomationService.GetQuickActions();
      if (qas.Count > 0) {
        // RemoveAll is needed because GetQuickActions may have changed since last build
        foreach (var qa in qas) {
          AddMenuItem(qa.Name, () => AutomationProcessor.ExecutePipeline(qa), SymbolRegular.Play24);
        }
        _contextMenu.Items.Add(new System.Windows.Controls.Separator());
      }

      // Navigation items (icons matching sidebar navigation)
      AddNavMenuItem(Strings.SidebarDashboard, "Dashboard", SymbolRegular.Home24);
      AddNavMenuItem(Strings.SidebarFan, "Fan", SymbolRegular.ArrowSync24);
      AddNavMenuItem(Strings.SidebarPerf, "Perf", SymbolRegular.Gauge24);
      AddNavMenuItem(Strings.SidebarLighting, "Lighting", SymbolRegular.Lightbulb24);
      AddNavMenuItem(Strings.SidebarAutomation, "Automation", SymbolRegular.Rocket24);
      AddNavMenuItem(Strings.SidebarOther, "Other", SymbolRegular.MoreHorizontal24);
      AddNavMenuItem(Strings.SidebarSysInfo, "SysInfo", SymbolRegular.Info24);
      AddNavMenuItem(Strings.SidebarSettings, "Settings", SymbolRegular.Settings24);

      _contextMenu.Items.Add(new System.Windows.Controls.Separator());

      // Open control panel
      AddMenuItem(Strings.OmenKeyShowMain, () => _bringToForeground(), SymbolRegular.Window24);

      // Language submenu
      var langMenu = new System.Windows.Controls.MenuItem {
        Header = Strings.LanguageMenu,
        Icon = new SymbolIcon { Symbol = SymbolRegular.Globe24 }
      };
      langMenu.Items.Add(CreateLangMenuItem("简体中文", AppLanguage.SimplifiedChinese));
      langMenu.Items.Add(CreateLangMenuItem("繁體中文", AppLanguage.TraditionalChinese));
      langMenu.Items.Add(CreateLangMenuItem("English", AppLanguage.English));
      _contextMenu.Items.Add(langMenu);

      // Help
      AddMenuItem(Strings.Help, () => Views.HelpWindow.ShowInstance(), SymbolRegular.QuestionCircle24);

      _contextMenu.Items.Add(new System.Windows.Controls.Separator());

      // Exit
      AddMenuItem(Strings.Exit, () => TrayService.Exit(), SymbolRegular.SignOut24);
    }

    void AddNavMenuItem(string header, string pageTag, SymbolRegular icon) {
      var item = new System.Windows.Controls.MenuItem {
        Header = header,
        Icon = new SymbolIcon { Symbol = icon }
      };
      item.Click += (s, e) => {
        _contextMenu.IsOpen = false;
        Views.MainWindow.NavigateToPage(pageTag);
      };
      _contextMenu.Items.Add(item);
    }

    void AddMenuItem(string header, Action action, SymbolRegular? icon = null) {
      var item = new System.Windows.Controls.MenuItem { Header = header };
      if (icon.HasValue)
        item.Icon = new SymbolIcon { Symbol = icon.Value };
      item.Click += (s, e) => { _contextMenu.IsOpen = false; action(); };
      _contextMenu.Items.Add(item);
    }

    System.Windows.Controls.MenuItem CreateLangMenuItem(string header, AppLanguage lang) {
      var item = new System.Windows.Controls.MenuItem {
        Header = header,
        IsCheckable = true,
        IsChecked = Strings.Current == lang
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
      string text = HardwareService.GetMonitorText();
      if (_notifyIcon != null) _notifyIcon.Text = text;
    }

    public void MakeVisible() {
      if (_notifyIcon != null) _notifyIcon.Visible = true;
    }

    public void Dispose() {
      GC.SuppressFinalize(this);
      _tooltipUpdateTimer?.Stop();
      _tooltipUpdateTimer?.Dispose();
      _tooltipUpdateTimer = null;
      _notifyIcon = null; // TrayService owns the icon lifetime
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
