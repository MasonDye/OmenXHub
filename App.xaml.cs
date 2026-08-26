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
using OmenSuperHub.Services.NetworkBoost;
using OmenSuperHub.Utils;
using Microsoft.Win32;
using static OmenSuperHub.OmenHardware;

namespace OmenSuperHub {
  public partial class App : System.Windows.Application {
    static Mutex _mutex;
    static bool _ownsMutex;  // ponytail: 仅在 createdNew==true 时 ReleaseMutex,否则 SynchronizationLockException
    static int alreadyReadCode = 1000;

    [DllImport("kernel32.dll")]
    static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")]
    static extern bool SetProcessWorkingSetSize(IntPtr hProcess, IntPtr dwMinimumWorkingSetSize, IntPtr dwMaximumWorkingSetSize);

    protected override void OnStartup(StartupEventArgs e) {
      RenderOptions.ProcessRenderMode = RenderMode.Default;
      base.OnStartup(e);

      // ponytail: --selftest 运行新逻辑自检断言后退出，不启动 UI。用法：OmenXHub.exe --selftest
      if (e.Args.Length > 0 && e.Args[0] == "--selftest") {
        string result = OmenSuperHub.Services.CpuAffinity.SelfCheck.Run()
          + "\n" + OmenSuperHub.Services.LightingSceneService.SelfCheck()
          + "\n" + OmenSuperHub.Pages.LightingPage.LightBarAnimsSelfCheck()
          + "\n" + OmenSuperHub.Services.LightingAnimationService.SelfCheck()
          + "\n" + OmenSuperHub.Services.DiskCleaner.SelfCheck();
        // ponytail: 关面板释放前端内存的自检 —— 跑完静态自检后启动一次主窗 → 导航 Dashboard
        // (热缓存 +订阅 OnPresetCycled) → Hide 触发 IsVisibleChanged→ReleaseFrontend → 反射断言
        // 三条:页面缓存清空 / PerfPage.Instance 断开 / OnPresetCycled 订阅归零。任一不成立写 FAIL。
        result += "\n[FrontendRelease] " + RunFrontendReleaseSelfCheck();
        Console.WriteLine(result);
        try {
          // WinExe 无控制台时输出不可见，同时落盘方便验证
          System.IO.File.WriteAllText(
            System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "selftest_result.txt"), result);
        } catch { }
        Environment.ExitCode = result.Contains("FAIL") ? 1 : 0;
        Shutdown();
        return;
      }

      // Dispatcher exception handler — log and prevent crash loop
      this.DispatcherUnhandledException += (s, args) => {
        Logger.Error($"Dispatcher exception: {args.Exception}");
        args.Handled = true;
      };

      try {
        // Single instance check
        _mutex = new Mutex(true, "MyUniqueAppMutex", out _ownsMutex);
        if (!_ownsMutex) {
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
        // ponytail: 持久化 FanSync 首次启动的默认 true。若用户已关掉并保存 (注册表有 FanSync=false),
        // Load 读到 false, Save("FanSync") 把 false 写回, 行为不变。首次安装则 true 落地。
        try { ConfigService.Save("FanSync"); } catch { }
        // Re-apply saved preset so its values populate ConfigService fields before RestoreConfig
        if (!string.IsNullOrEmpty(ConfigService.Preset)) {
          PresetManager.SwitchPreset(ConfigService.Preset);
          // ponytail: SetGpuPowerState removed here — TPP (ConcurrentTDP) must be
          // written BEFORE GPU power state for PPAB to use the right power budget.
          // ApplyPresetHardware in MainWindow.Loaded already does them in the
          // correct order: CPU power → TPP → GPU power state.
        }
        // 灯光场景联动 — 预设切换时自动切换灯光场景 (已实现但未接入的 OnPerformanceModeChanged)
        PresetManager.OnPresetChanged += (preset) => LightingSceneService.NotifyPresetChanged(preset);
        // ponytail: 灯光 60s 定时调度器不再在 App 启动时无条件启动 — 此时 _file 还是 null,
        // 跑也是空 tick。改为在 LightingSceneService.Initialize() 内首次加载完 _file 后
        // 按真值 _file.Enabled 决定是否 StartScheduler。页面总开关 → Enabled setter 也会
        // 双向联动调度器启停 (见 LightingSceneService.cs)。
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

        // ponytail: 键盘能力探测 — 后台跑(WMI/SDK 冷启动可达数秒),完成后刷新侧栏。
        // 普通键盘(确认探测)时 NavLighting 被 Collapsed;探测失败保守显示,不误隐藏。
        System.Threading.ThreadPool.QueueUserWorkItem(_ => {
          try {
            OmenLighting.DetectKeyboardCapability();
            Dispatcher.BeginInvoke(new Action(() => Views.MainWindow.UpdateNavigationItems()));
          } catch (Exception ex) { Logger.Error($"KeyboardCapability probe: {ex.Message}"); }
        });

        // ponytail: 初次占用整理 —— LibreComputer.Open(驱动+全部传感器)、原生 SDK 预加载、首帧渲染
        // 完成后堆里遗留大量启动期临时对象。延迟 10s(等 LHM Open + 首轮硬件采样跑完)后做一次
        // 全量 GC + 修剪工作集,任务管理器里的初始占用显著下降。
        // 天花板: 只压"初次"峰值 — 页面导航/持续轮询触碰内存后会回到真实水位。
        System.Threading.ThreadPool.QueueUserWorkItem(_ => {
          try {
            System.Threading.Thread.Sleep(10000);
            GC.Collect(2, GCCollectionMode.Optimized);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Optimized);
            SetProcessWorkingSetSize(GetCurrentProcess(), (IntPtr)(-1), (IntPtr)(-1));
            Logger.Info("Startup memory trim done");
          } catch { }
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
          // ponytail: QueueUserWorkItem 会静默吞异常 — HTTP 监听失败必须留下痕迹
          System.Threading.ThreadPool.QueueUserWorkItem(_ => {
            try { HardwareApiService.Start(); }
            catch (Exception ex) { Logger.Error("HardwareApiService.Start: " + ex.Message); }
          });
        }

        // Start Omen Key listener
        TrayService.GetOmenKeyTask();

        // Defer non-critical startup work (RestoreConfig, Automation, Macro)
        Dispatcher.BeginInvoke(new Action(() => {
          TrayService.RestoreConfig();
          AutomationService.Initialize();
          // ponytail: 启动门控走中央入口 ReevaluateBackendNeeded —— 双门控 (总开关 + 简洁模式可见性)
          // 任一不满足后端不 Start(WMI watcher/系统事件订阅/全局热键订阅都不上)。
          // 页面总开关/简洁模式 toggle/白名单 cb 切换时由各自回调再次调用本入口真启停。
          AutomationProcessor.ReevaluateBackendNeeded();
          MacroService.Initialize();
          // ponytail: 启动门控 — MacroEnabled=false 时不装全局键盘钩子 (SetWindowsHookEx
          // WH_KEYBOARD_LL 装上后兜所有按键事件)。MacroController.Start 已加幂等守卫,
          // 已 Start 时再调是 no-op。
          if (ConfigService.MacroEnabled) MacroController.Start();
          Views.OsdWindow.StartLockKeyMonitor();
        }), System.Windows.Threading.DispatcherPriority.Background);

        // Floating window in separate BeginInvoke so it runs even if RestoreConfig throws
        Dispatcher.BeginInvoke(new Action(() => {
          if (ConfigService.FloatingBar == "on")
            Views.FloatingWindow.ShowInstances();
        }), System.Windows.Threading.DispatcherPriority.Background);

        // Show help for new version
        if (ConfigService.AlreadyRead != alreadyReadCode) {
          Views.HelpWindow.ShowInstance(isFirstRun: true);
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
      // ponytail: 主窗口隐藏到托盘时 MainWindowHandle 为 0,EnumWindows 按 Z 序取
      // 第一个窗口 — 预设切换刚弹过 OSD 浮窗时拿到的是它,唤醒消息被丢弃(#20)。
      // 先按主窗口标题精确匹配,匹配不到再退回第一个。
      IntPtr byTitle = IntPtr.Zero, fallback = IntPtr.Zero;
      string mainTitle = Strings.WindowTitle;
      EnumWindows((hWnd, lParam) => {
        GetWindowThreadProcessId(hWnd, out int pid);
        if (pid != processId) return true;
        if (fallback == IntPtr.Zero) fallback = hWnd;
        if (byTitle == IntPtr.Zero) {
          var sb = new System.Text.StringBuilder(256);
          if (GetWindowText(hWnd, sb, 256) > 0 && sb.ToString() == mainTitle)
            byTitle = hWnd;
        }
        return byTitle == IntPtr.Zero;  // 找到标题匹配即停止枚举
      }, IntPtr.Zero);
      return byTitle != IntPtr.Zero ? byTitle : fallback;
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
      SafeShutdown(BoostService.Stop); // 关闭时自动重置加速（停代理/停 TUN/清路由），规则留在本地 json
      SafeShutdown(MacroController.Stop);
      SafeShutdown(HardwareApiService.Stop);
      SafeShutdown(HWiNFOService.Stop);
      SafeShutdown(HWiNFOReaderService.Stop);
      SafeShutdown(ThemeService.Cleanup);
      SafeShutdown(EcoQosService.Cleanup);
      // ponytail: CoreKeep 现状是惰性启动 (默认 false、进 CoreKeepPage 才 StartAutoApply),
      // 但以前没在 OnExit 统一 Cleanup → 用户开过后再退出程序,ManagementEventWatcher 与
      // Timer 由 GC 兜底。StopAutoApply 已是幂等(null 守卫),退出时没启也 ran-nothing。
      SafeShutdown(OmenSuperHub.Services.CpuAffinity.CoreKeepService.StopAutoApply);
      SafeShutdown(AutomationProcessor.Stop);
      SafeShutdown(LightingSceneService.StopScheduler);
      SafeShutdown(() => SystemEvents.PowerModeChanged -= TrayService.OnPowerChange);
      SafeShutdown(HardwareService.Close);
      SafeShutdown(() => { if (_ownsMutex) _mutex?.ReleaseMutex(); });
      SafeShutdown(() => _mutex?.Dispose());
      base.OnExit(e);
    }

    static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e) {
      Exception ex = e.ExceptionObject as Exception;
      DialogHelper.Error("Unhandled Exception: " + ex?.Message + "\n\n" + ex?.StackTrace,
        "OmenSuperHub Error");
    }

    // ponytail: 关主面板释放前端内存的自检。直接驱动 Services.CachedPageService + 各页 Unloaded,
    // 不启 UI（避免拖累 HardwareService 等后端）。任意 FAIL 写到 selftest_result.txt 并通过
    // 现有 Environment.ExitCode = result.Contains("FAIL") ? 1 : 0 让退出码反映。
    // 三段对应三个补丁：
    //   Patch 3 — CachedPageService.Clear() 清空 _cache（hasClass field 反射验证）
    //   Patch 1 — PerfPage.Unloaded 把 static Instance 置 null
    //   Patch 2 — DashboardPage.Unloaded 解订阅 ConfigService.OnPresetCycled
    static string RunFrontendReleaseSelfCheck() {
      var fails = new System.Collections.Generic.List<string>();
      var Npub = System.Reflection.BindingFlags.NonPublic;
      var Inst = System.Reflection.BindingFlags.Instance;
      var Stat = System.Reflection.BindingFlags.Static;

      // ── Patch 3: CachedPageService.Clear() ──────────────────────────────
      try {
        var svc = new Services.CachedPageService();
        var cacheField = typeof(Services.CachedPageService).GetField("_cache", Npub | Inst);
        if (cacheField == null) fails.Add("CachedPageService._cache field missing");
        else {
          var cache = (System.Collections.IDictionary)cacheField.GetValue(svc);
          svc.GetPage(typeof(Pages.PerfPage));
          if (cache.Count != 1) fails.Add($"cache.Count after GetPage(Perf) = {cache.Count}, expected 1");
          svc.Clear();
          if (cache.Count != 0) fails.Add($"cache.Count after Clear = {cache.Count}, expected 0");
        }
      } catch (System.Exception ex) { fails.Add($"Patch3 threw: {ex.GetType().Name}: {ex.Message}"); }

      // ── Patch 1: PerfPage.Unloaded → Instance = null ────────────────────
      try {
        var p = new Pages.PerfPage();
        if (Pages.PerfPage.Instance == null) fails.Add("PerfPage.Instance null right after ctor — Patch 1 untestable");
        else {
          var unloaded = typeof(Pages.PerfPage).GetMethod("PerfPage_Unloaded", Npub | Inst);
          unloaded?.Invoke(p, new object[] { p, new System.Windows.RoutedEventArgs() });
          if (Pages.PerfPage.Instance != null)
            fails.Add($"PerfPage.Instance still set after Unloaded (Patch 1 broken)");
        }
      } catch (System.Exception ex) { fails.Add($"Patch1 threw: {ex.GetType().Name}: {ex.Message}"); }

      // ── Patch 2: DashboardPage.Unloaded detaches OnPresetCycled ─────────
      try {
        var evtField = typeof(ConfigService).GetField("OnPresetCycled", Npub | Stat);
        if (evtField == null) fails.Add("ConfigService.OnPresetCycled backing field missing");
        else {
          var dashboard = new Pages.DashboardPage();
          var onPresetMethod = typeof(Pages.DashboardPage).GetMethod("OnPresetCycled", Npub | Inst);
          var handlerField = typeof(Pages.DashboardPage).GetField("_presetCycledHandler", Npub | Inst);

          // 镜像 Loaded 行为:把 OnPresetCycled 方法 + _presetCycledHandler 字段订阅上去
          var current = (System.Action<string>)evtField.GetValue(null);
          if (onPresetMethod != null)
            current += (System.Action<string>)System.Delegate.CreateDelegate(typeof(System.Action<string>), dashboard, onPresetMethod);
          if (handlerField != null)
            current += (System.Action<string>)handlerField.GetValue(dashboard);
          evtField.SetValue(null, current);

          System.Delegate beforeDel = (System.Delegate)evtField.GetValue(null);
          int subsBefore = beforeDel?.GetInvocationList().Length ?? 0;
          if (subsBefore < 2) fails.Add($"OnPresetCycled subs before Unloaded = {subsBefore}, expected ≥2");

          // 触发真实的 Unloaded 路由事件 — Page 通过 `Unloaded += lambda` 注册的处理器由
          // RaiseEvent 调度执行,与 Cache 清空时的真实路径完全一致。
          dashboard.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.FrameworkElement.UnloadedEvent, dashboard));

          System.Delegate afterDel = (System.Delegate)evtField.GetValue(null);
          if (afterDel != null) {
            int leftover = 0;
            foreach (System.Delegate d in afterDel.GetInvocationList())
              if (d.Target == dashboard) leftover++;
            if (leftover != 0) fails.Add($"DashboardPage still has {leftover} OnPresetCycled subscription(s) after Unloaded (Patch 2 broken)");
          }
        }
      } catch (System.Exception ex) { fails.Add($"Patch2 threw: {ex.GetType().Name}: {ex.Message}"); }

      return fails.Count == 0 ? "OK" : "FAIL: " + string.Join(" | ", fails);
    }
  }
}

