// Program.cs - [已弃用] WinForms 入口点
// 旧版 WinForms 启动代码，已被 App.xaml.cs 替代，不参与编译
using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using OmenSuperHub.Services;
using Microsoft.Win32;

namespace OmenSuperHub {
  static class Program {
    [DllImport("user32.dll")]
    static extern bool SetProcessDPIAware();

    static int alreadyReadCode = 1000;

    [STAThread]
    static void Main(string[] args) {
      bool isNewInstance;
      using (Mutex mutex = new Mutex(true, "MyUniqueAppMutex", out isNewInstance)) {
        if (!isNewInstance) {
          return;
        }

        if (Environment.OSVersion.Version.Major >= 6) {
          SetProcessDPIAware();
        }

        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

        // Initialize power status
        HardwareService.PowerOnline = SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online;
        HardwareService.MonitorQuery();

        // Version-based read code
        Version version = Assembly.GetExecutingAssembly().GetName().Version;
        string versionString = version.ToString().Replace(".", "");
        alreadyReadCode = new Random(int.Parse(versionString)).Next(1000, 10000);

        // Create and configure WPF Application
        var app = new App();
        app.InitializeComponent();

        // Initialize tray icon (uses WinForms NotifyIcon + WPF ContextMenu)
        TrayService.InitTrayIcon();

        // Initialize hardware monitoring
        HardwareService.LibreComputer.Open();

        // Start timers
        TrayService.StartTimers();

        // Start Omen Key listener
        TrayService.GetOmenKeyTask();

        // Restore last settings
        TrayService.RestoreConfig();

        // Show help for new version
        if (ConfigService.AlreadyRead != alreadyReadCode) {
          Views.HelpWindow.ShowInstance();
          ConfigService.AlreadyRead = alreadyReadCode;
          ConfigService.Save("AlreadyRead");
        }

        // Power change handler
        SystemEvents.PowerModeChanged += TrayService.OnPowerChange;

        // Run WPF message loop (replaces WinForms Application.Run())
        app.Run();
      }
    }

    static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e) {
      Exception ex = e.ExceptionObject as Exception;
      Console.WriteLine("Unhandled Exception: " + ex?.Message);
      Console.WriteLine(ex?.StackTrace);
    }
  }
}
