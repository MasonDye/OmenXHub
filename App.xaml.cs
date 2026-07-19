// App.xaml.cs - 应用程序入口
// 互斥锁单实例、Logger 初始化、ConfigService 加载、主题/托盘/HWiNFO/API 启动、窗口管理
using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

using OmenSuperHub.Services;
using OmenSuperHub.Utils;
using Microsoft.Win32;
using static OmenSuperHub.OmenHardware;

namespace OmenSuperHub {
  public partial class App : System.Windows.Application {
    static Mutex _mutex;
    static int alreadyReadCode = 1000;

    protected override void OnStartup(StartupEventArgs e) {
      RenderOptions.ProcessRenderMode = RenderMode.Default;
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
        CustomPresetNamesStore.Load(); // file fallback for custom preset names
        // Re-apply saved preset so its values populate ConfigService fields before RestoreConfig
        if (!string.IsNullOrEmpty(ConfigService.Preset)) {
          PresetManager.SwitchPreset(ConfigService.Preset);
          // ponytail: SetGpuPowerState removed here — TPP (ConcurrentTDP) must be
          // written BEFORE GPU power state for PPAB to use the right power budget.
          // ApplyPresetHardware in MainWindow.Loaded already does them in the
          // correct order: CPU power → TPP → GPU power state.
        }
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

        // Preload OmenLightingSDK.dll for native lighting control
        try { OmenHardware.ExtractAndPreloadNativeDll("OmenLightingSDK.dll"); } catch { }

        // ponytail: persisted lighting state wasn't reapplied on boot — user had to hit
        // "Apply Lighting" manually after every reboot. Replay it 5s after launch (off-UI
        // thread so cold-boot window doesn't block on slow WMI/HID open). PerKey HID path
        // skipped here (device probe may fail at cold boot; user can hit the PerKey card's
        // Apply button to re-establish).
        System.Threading.ThreadPool.QueueUserWorkItem(_ => {
          try {
            System.Threading.Thread.Sleep(5000);
            OmenSuperHub.Pages.LightingPage.ReplaySavedLighting();
          } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"ReplaySavedLighting: {ex.Message}"); }
        });

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
            TrayService.StartTrayHelperTimers();
          }), System.Windows.Threading.DispatcherPriority.Background);
        });

        // Start HWiNFO64 integration if enabled
        HWiNFOService.StartStopIfNeeded();

        // Start HWiNFO64 reader if enabled
        HWiNFOReaderService.StartStopIfNeeded();

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
        DialogHelper.Error("Startup Error: " + ex.Message + "\n\n" + ex.ToString(),
          "OmenSuperHub Error");
      }
    }

    static void ShowExistingWindow() {
      using (var self = Process.GetCurrentProcess()) {
        foreach (var p in Process.GetProcessesByName(self.ProcessName)) {
          if (p.Id == self.Id) continue;
          p.WaitForInputIdle(3000);
          p.Refresh();
          IntPtr hWnd = p.MainWindowHandle;
          if (hWnd == IntPtr.Zero)
            hWnd = FindWindowForProcess(p.Id);
          if (hWnd != IntPtr.Zero) {
            PostMessage(hWnd, WM_SHOW_MAIN, IntPtr.Zero, IntPtr.Zero);
            return;
          }
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
      // ponytail: 每个 close 操作都独立 try-catch — 任何一个抛出不能阻断后续关闭。
      // SafeShutdown 顺序与原 OnExit 一致。
      void SafeShutdown(Action a) { try { a(); } catch (Exception ex) { Logger.Error("OnExit step failed: " + ex.Message); } }
      if (PresetManager.IsCustom(ConfigService.Preset)) SafeShutdown(() => PresetManager.SaveCustomPreset(ConfigService.Preset));
      SafeShutdown(MacroController.Stop);
      SafeShutdown(HardwareApiService.Stop);
      SafeShutdown(HWiNFOService.Stop);
      SafeShutdown(HWiNFOReaderService.Stop);
      SafeShutdown(ThemeService.Cleanup);
      SafeShutdown(EcoQosService.Cleanup);
      SafeShutdown(AutomationProcessor.Stop);
      SafeShutdown(() => SystemEvents.PowerModeChanged -= TrayService.OnPowerChange);
      SafeShutdown(HardwareService.Close);
      SafeShutdown(AmdSmuService.Shutdown);
      SafeShutdown(() => _mutex?.ReleaseMutex());
      SafeShutdown(() => _mutex?.Dispose());
      base.OnExit(e);
    }

    static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e) {
      Exception ex = e.ExceptionObject as Exception;
      DialogHelper.Error("Unhandled Exception: " + ex?.Message + "\n\n" + ex?.StackTrace,
        "OmenSuperHub Error");
    }
  }
}

