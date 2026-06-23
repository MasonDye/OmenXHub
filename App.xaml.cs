using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;

using OmenSuperHub.Services;
using Microsoft.Win32;
using static OmenSuperHub.OmenHardware;

namespace OmenSuperHub {
  public partial class App : System.Windows.Application {
    static Mutex _mutex;
    static int alreadyReadCode = 1000;

    protected override void OnStartup(StartupEventArgs e) {
      base.OnStartup(e);

      // Dispatcher exception handler — log and prevent crash loop
      this.DispatcherUnhandledException += (s, args) => {
        Logger.Error($"Dispatcher exception: {args.Exception}");
        args.Handled = true;
      };

      try {
        // Single instance check
        bool isNewInstance;
        _mutex = new Mutex(true, "MyUniqueAppMutex", out isNewInstance);
        if (!isNewInstance) {
          ShowExistingWindow();
          Shutdown();
          return;
        }

        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

        // Initialize Logger
        Logger.Info("OmenXHub starting...");

        // Load language config
        ConfigService.Load();
        CustomPresetNames.Load(); // file fallback for custom preset names
        if (!string.IsNullOrEmpty(ConfigService.Language)) {
          switch (ConfigService.Language) {
            case "TraditionalChinese": Strings.Current = AppLanguage.TraditionalChinese; break;
            case "English": Strings.Current = AppLanguage.English; break;
            default: Strings.Current = AppLanguage.SimplifiedChinese; break;
          }
        }

        // Preload NvidiaApi.dll for Hot Switch (DDS)
        if (HardwareService.PowerOnline) {
          try { OmenHardware.ExtractAndPreloadNativeDll("NvidiaApi.dll"); } catch { }
        }

        // Initialize System Theme integration
        ThemeService.Initialize();

        // Initialize power status
        HardwareService.PowerOnline = System.Windows.Forms.SystemInformation.PowerStatus.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online;
        HardwareService.MonitorQuery();

        // Set unleash mode — required before CPU power limit takes effect
        try { SetFanMode((byte)0x31); } catch { }

        // Version-based read code
        Version version = Assembly.GetExecutingAssembly().GetName().Version;
        string versionString = version.ToString().Replace(".", "");
        alreadyReadCode = new Random(int.Parse(versionString)).Next(1000, 10000);

        // Initialize tray icon (WinForms NotifyIcon + WPF ContextMenu)
        TrayService.InitTrayIcon();

        // Power change handler
        SystemEvents.PowerModeChanged += TrayService.OnPowerChange;

        // Show main window BEFORE heavy init (skip to tray if --tray flag)
        string[] cmdArgs = Environment.GetCommandLineArgs();
        if (cmdArgs.Length > 1 && cmdArgs[1] == "--tray") {
          Views.MainWindow.StartTrayOnly();
        } else {
          Views.MainWindow.ShowInstance();
        }

        // Init hardware and timers in background — window already visible
        System.Threading.ThreadPool.QueueUserWorkItem(_ => {
          HardwareService.LibreComputer.Open();
          System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() => {
            TrayService.StartTimers();
          }), System.Windows.Threading.DispatcherPriority.Background);
        });

        // Start HWiNFO64 integration if enabled
        HWiNFOService.StartStopIfNeeded();

        // Start local HTTP API server if enabled in settings
        if (ConfigService.HttpApiEnabled) {
          System.Threading.ThreadPool.QueueUserWorkItem(_ => HardwareApiService.Start());
        }

        // Start Omen Key listener
        TrayService.GetOmenKeyTask();

        // Defer non-critical startup work (RestoreConfig, Automation, Macro)
        Dispatcher.BeginInvoke(new Action(() => {
          TrayService.RestoreConfig();
          AutomationService.Initialize();
          AutomationProcessor.Start();
          MacroService.Initialize();
          MacroController.Start();
          Views.OsdWindow.StartLockKeyMonitor();
        }), System.Windows.Threading.DispatcherPriority.Background);

        // Floating window in separate BeginInvoke so it runs even if RestoreConfig throws
        Dispatcher.BeginInvoke(new Action(() => {
          if (ConfigService.FloatingBar == "on")
            Views.FloatingWindow.ShowInstances();
        }), System.Windows.Threading.DispatcherPriority.Background);

        // Show help for new version
        if (ConfigService.AlreadyRead != alreadyReadCode) {
          Views.HelpWindow.ShowInstance();
          ConfigService.AlreadyRead = alreadyReadCode;
          ConfigService.Save("AlreadyRead");
        }
      } catch (Exception ex) {
        System.Windows.MessageBox.Show(
          "Startup Error: " + ex.Message + "\n\n" + ex.ToString(),
          "OmenSuperHub Error", MessageBoxButton.OK, MessageBoxImage.Error);
      }
    }

    static void ShowExistingWindow() {
      var self = Process.GetCurrentProcess();
      foreach (var p in Process.GetProcessesByName(self.ProcessName)) {
        if (p.Id == self.Id) continue;
        p.WaitForInputIdle(3000);
        p.Refresh();
        IntPtr hWnd = p.MainWindowHandle;
        if (hWnd == IntPtr.Zero)
          hWnd = FindWindowForProcess(p.Id);
        if (hWnd != IntPtr.Zero) {
          // Tell the first instance to show its window via WPF ShowInstance(),
          // bypassing Win32 ShowWindow which breaks WPF internal state
          PostMessage(hWnd, WM_SHOW_MAIN, IntPtr.Zero, IntPtr.Zero);
          return;
        }
      }
    }

    static IntPtr FindWindowForProcess(int processId) {
      IntPtr found = IntPtr.Zero;
      EnumWindows((hWnd, lParam) => {
        GetWindowThreadProcessId(hWnd, out int pid);
        if (pid == processId) {
          found = hWnd;
          return false;
        }
        return true;
      }, IntPtr.Zero);
      return found;
    }

    internal static readonly uint WM_SHOW_MAIN = RegisterWindowMessage("OmenXHubShowMain");

    const int SW_SHOW = 5;
    const int SW_RESTORE = 9;

    delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);

    [DllImport("user32.dll")]
    static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

    [DllImport("user32.dll")]
    static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    static extern bool FlashWindow(IntPtr hWnd, bool bInvert);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    static extern uint RegisterWindowMessage(string lpString);

    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    protected override void OnExit(ExitEventArgs e) {
      try { MacroController.Stop(); } catch { }
      try { HardwareApiService.Stop(); } catch { }
      try { HWiNFOService.Stop(); } catch { }
      try { ThemeService.Cleanup(); } catch { }
      try { AutomationProcessor.Stop(); } catch { }
      try { SystemEvents.PowerModeChanged -= TrayService.OnPowerChange; } catch { }
      try { _mutex?.ReleaseMutex(); } catch { }
      try { _mutex?.Dispose(); } catch { }
      base.OnExit(e);
    }

    static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e) {
      Exception ex = e.ExceptionObject as Exception;
      System.Windows.MessageBox.Show(
        "Unhandled Exception: " + ex?.Message + "\n\n" + ex?.StackTrace,
        "OmenSuperHub Error", MessageBoxButton.OK, MessageBoxImage.Error);
    }
  }
}

