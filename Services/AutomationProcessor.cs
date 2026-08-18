// AutomationProcessor.cs - 自动化执行引擎
// 事件驱动触发器检测（进程/电源/会话/显示/温度/电池/计划），步骤执行（预设/风扇/功耗/WiFi/蓝牙等）
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Interop;
using Microsoft.Win32;
using Windows.Devices.Radios;
using static OmenSuperHub.OmenHardware;

namespace OmenSuperHub.Services {
  internal static class AutomationProcessor {
    public static event Action<string> ExecutionStatusChanged;
    private static bool _running;
    private static ManagementEventWatcher _processStartWatcher;
    private static ManagementEventWatcher _processStopWatcher;
    private static Timer _scheduleTimer;
    private static Timer _tempPollTimer;
    private static Microsoft.Win32.SessionSwitchEventHandler _sessionSwitchHandler;
    private static EventHandler _displaySettingsHandler;
    private static readonly object ExecLock = new object();
    private static bool _executing;
    private static string _currentPipelineName;
    private static float _lastCpuTemp;
    private static float _lastGpuTemp;
    private static int _lastBatteryPercent = -1;
    private static readonly HashSet<string> _tempTriggerFired = new HashSet<string>();

    // Hotkey support
    [DllImport("user32.dll")]
    static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")]
    static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    const int WM_HOTKEY = 0x0312;
    const uint MOD_ALT = 0x0001;
    const uint MOD_CONTROL = 0x0002;
    const uint MOD_SHIFT = 0x0004;
    const uint MOD_WIN = 0x0008;
    static HwndSource _hotkeyHwndSource;
    static System.Collections.Concurrent.ConcurrentDictionary<int, string> _registeredHotkeys;

    public static bool IsExecuting => _executing;
    public static string CurrentPipelineName => _currentPipelineName;

    // ponytail: "自动化后端是否应该运行"的统一判定 —— 两个条件都满足才允许运行:
    //   1) 总开关 AutomationEnabled 打开;
    //   2) Automation 页对用户可见(简洁模式没把它隐藏)。
    // 旧版只看总开关,简洁模式隐藏 Automation 页时后端仍在空转跑 WMI watcher/定时器/热键。
    // 注意:这个方法只在用户操作 (总开关 / 简洁模式 / 白名单) 后调用,不会影响性能/风扇服务。
    static bool IsBackendWanted()
      => ConfigService.AutomationEnabled
         && Views.MainWindow.ShouldShowNavItem("Automation");

    // ponytail: 让后端运行状态对齐 IsBackendWanted —— 总开关切 / 简洁模式 toggle / 白名单变更都调它,
    // 内部幂等 (_running 守卫),该启启该停停。性能/风扇服务与本类无关,本方法不碰。
    public static void ReevaluateBackendNeeded() {
      if (IsBackendWanted()) Start();
      else Stop();
    }

    public static void Start() {
      if (_running) return;
      _running = true;

	      SubscribeProcessEvents();
	      SubscribePowerEvents();
	      SubscribeSessionEvents();
	      // ponytail: 只有实际存在对应轮询触发器的管道才启定时器,空载用户不再每 2/5/15s 空转。
	      ReevaluateTimers();
      SubscribeDisplayEvents();

      // Init hotkey message window
      InitHotkeyHwnd();
      RefreshHotkeys();

      FireTrigger("Startup", "");

      Logger.Info("AutomationProcessor started");
    }

    // ponytail: 根据当前已启用管道的真实触发器集 Start/Stop 三个轮询定时器。
    // 增删/启用/禁用管道后由 AutomationService.Save() 触发。Stop() 已会 Dispose,这里只在运行中调用。
    public static void ReevaluateTimers() {
      if (!_running) return;
      bool needTemp = HasPolledTriggerType(t => t == "CpuTempAbove" || t == "GpuTempAbove");
      bool needBattery = HasPolledTriggerType(t => t == "BatteryAbove" || t == "BatteryBelow");
      bool needSchedule = HasPolledTriggerType(t => t == "TimeSchedule");

      if (needTemp && _tempPollTimer == null) StartTempPollTimer();
      else if (!needTemp && _tempPollTimer != null) { _tempPollTimer.Dispose(); _tempPollTimer = null; }

      if (needBattery && _batteryPollTimer == null) SubscribeBatteryEvents();
      else if (!needBattery && _batteryPollTimer != null) { _batteryPollTimer.Dispose(); _batteryPollTimer = null; }

      if (needSchedule && _scheduleTimer == null) StartScheduleTimer();
      else if (!needSchedule && _scheduleTimer != null) { _scheduleTimer.Dispose(); _scheduleTimer = null; }
    }

    static bool HasPolledTriggerType(Func<string, bool> matcher) {
      foreach (var p in AutomationService.GetEnabledPipelines()) {
        foreach (var t in p.Triggers) {
          if (t.Enabled && matcher(t.Type)) return true;
        }
      }
      return false;
    }

    public static void Stop() {
      _running = false;
      if (_processStartWatcher != null) { _processStartWatcher.Stop(); _processStartWatcher.Dispose(); _processStartWatcher = null; }
      if (_processStopWatcher != null) { _processStopWatcher.Stop(); _processStopWatcher.Dispose(); _processStopWatcher = null; }
      if (_scheduleTimer != null) { _scheduleTimer.Dispose(); _scheduleTimer = null; }
      if (_tempPollTimer != null) { _tempPollTimer.Dispose(); _tempPollTimer = null; }
      if (_batteryPollTimer != null) { _batteryPollTimer.Dispose(); _batteryPollTimer = null; }
	      SystemEvents.PowerModeChanged -= OnPowerModeChanged;
	      if (_sessionSwitchHandler != null) { SystemEvents.SessionSwitch -= _sessionSwitchHandler; _sessionSwitchHandler = null; }
      if (_displaySettingsHandler != null) { SystemEvents.DisplaySettingsChanged -= _displaySettingsHandler; _displaySettingsHandler = null; }
      _tempTriggerFired.Clear();
      UnregisterAllHotkeys();
      // ponytail: 修复 bug — 之前这里 _hotkeyHwndSource.Dispose(), 但 _hotkeyHwndSource
      // 是 MainWindow 自己的 HwndSource(InitHotkeyHwnd 里 HwndSource.FromHwnd(MainWindow.Handle),
      // 注释还写"use main window handle instead of a hidden HwndSource")。对一个顶级 WPF 窗口的
      // HwndSource 调 Dispose 等于拔掉它的底层 HWND → 关总开关时主界面被连根关掉。
      // 主窗口的 HwndSource 生命周期由窗口自己管, 这里只拆钩子、置 null 让下次 Start 重 AddHook。
      if (_hotkeyHwndSource != null) {
        try { _hotkeyHwndSource.RemoveHook(HotkeyWndProc); } catch { }
        _hotkeyHwndSource = null;
      }
      Logger.Info("AutomationProcessor stopped");
    }

    // ponytail: 触发改为串行排队 — 进程启动类触发是突发的(游戏+启动器+反作弊几秒内连发),
    // 旧 _executing 门在另一管道执行中(含 Delay 步骤)时直接静默丢弃后续触发,
    // 表现为"自动化大多失效"(issue #20)。同名管道去重防止重复排队刷屏。
    static readonly System.Collections.Concurrent.ConcurrentQueue<AutomationPipeline> _pendingPipelines = new();
    static int _drainRunning;

    public static void ExecutePipeline(AutomationPipeline pipeline) {
      if (pipeline == null || pipeline.Steps == null || pipeline.Steps.Count == 0) return;
      if (!_pendingPipelines.IsEmpty) {
        foreach (var p in _pendingPipelines)
          if (p.Name == pipeline.Name) return;
      }
      _pendingPipelines.Enqueue(pipeline);
      if (Interlocked.CompareExchange(ref _drainRunning, 1, 0) == 0)
        _ = Task.Run(DrainPipelines);
    }

    static async System.Threading.Tasks.Task DrainPipelines() {
      try {
        while (_running && _pendingPipelines.TryDequeue(out var pipeline)) {
          lock (ExecLock) { _executing = true; _currentPipelineName = pipeline.Name; }
          ExecutionStatusChanged?.Invoke(pipeline.Name);
          try {
            foreach (var step in pipeline.Steps) {
              if (step.DelayMs > 0)
                await System.Threading.Tasks.Task.Delay(step.DelayMs);
              await ExecuteStep(step);
            }
          } catch (Exception ex) {
            Logger.Error("AutomationProcessor.ExecutePipeline error: " + ex.Message);
          } finally {
            lock (ExecLock) { _executing = false; _currentPipelineName = null; }
            // ponytail: marshal to UI thread for anyone subscribing from XAML/code-behind
            try {
              var app = System.Windows.Application.Current;
              if (app != null && app.Dispatcher != null && !app.Dispatcher.CheckAccess())
                app.Dispatcher.Invoke(() => ExecutionStatusChanged?.Invoke(null));
              else
                ExecutionStatusChanged?.Invoke(null);
            } catch { }
          }
        }
      } finally {
        _drainRunning = 0;
        // ponytail: TryDequeue 失败与 _drainRunning 清零之间入队的管道需要重新拉起 drain
        if (!_pendingPipelines.IsEmpty && Interlocked.CompareExchange(ref _drainRunning, 1, 0) == 0)
          _ = Task.Run(DrainPipelines);
      }
    }

    private static async System.Threading.Tasks.Task ExecuteStep(AutomationStep step) {
      if (step == null || string.IsNullOrEmpty(step.Type)) return;
      switch (step.Type) {
        case "SetPreset":
          await ApplyPresetAsync(step.Value);
          break;
        case "SetRefreshRate":
          if (int.TryParse(step.Value, out int rr) && rr >= 30 && rr <= 360) {
            ApplyRefreshRate(rr);
            Views.OsdWindow.ShowRefreshRateOsd(rr);
          }
          break;
        case "SetPowerPlan":
          if (!string.IsNullOrEmpty(step.Value)) {
            Guid g = Guid.Parse(step.Value);
            NativeMethods_Power.PowerSetActiveScheme(IntPtr.Zero, ref g);
          }
          break;
        case "SetPowerMode":
          ExecuteSetPowerMode(step.Value);
          break;
        case "SetMaxFrameRate":
          if (int.TryParse(step.Value, out int fps) && OmenHardware.HasNvidiaGpu())
            HP.Omen.Core.Common.NVidiaApi.NvApiWrapper.NVAPI_SetMaxFrameRate(fps);
          break;
        case "SetCpuPower":
          ExecuteSetCpuPower(step.Value);
          break;
        case "SetFanMode":
          ExecuteSetFanMode(step.Value);
          break;
        case "RunProgram":
          StartShellProcess(step.Value, "Automation RunProgram");
          break;
        case "Notification":
          if (!string.IsNullOrEmpty(step.Value))
            Views.OsdWindow.ShowTextOsd(step.Value, force: true);
          break;
        case "SetGpuPower":
          ExecuteSetGpuPower(step.Value);
          break;
        case "SetTempSensitivity":
          if (!string.IsNullOrEmpty(step.Value)) {
            ConfigService.TempSensitivity = step.Value;
            switch (step.Value) {
              case "realtime": HardwareService.RespondSpeed = 1f; break;
              case "high": HardwareService.RespondSpeed = 0.4f; break;
              case "medium": HardwareService.RespondSpeed = 0.1f; break;
              case "low": HardwareService.RespondSpeed = 0.04f; break;
            }
          }
          break;
        case "SetGPUHybridMode":
          if (!string.IsNullOrEmpty(step.Value))
            AutomationActions.SetGPUHybridMode(step.Value == "enable");
          break;
        case "SetBrightness":
          if (int.TryParse(step.Value, out int brightness))
            AutomationActions.SetBrightness((byte)Math.Max(0, Math.Min(100, brightness)));
          break;
        case "SetMicrophone":
          if (!string.IsNullOrEmpty(step.Value))
            AutomationActions.SetMicrophoneMute(step.Value == "mute" || step.Value == "off");
          break;
        case "SetWiFi":
          if (!string.IsNullOrEmpty(step.Value))
            await AutomationActions.SetWiFi(step.Value == "on");
          break;
        case "SetBluetooth":
          if (!string.IsNullOrEmpty(step.Value))
            await AutomationActions.SetBluetooth(step.Value == "on");
          break;
        case "PlaySound":
          StartShellProcess(step.Value, "PlaySound");
          break;
        case "RunMacro":
          if (!string.IsNullOrEmpty(step.Value)) {
            var macro = MacroService.Macros.Find(x => x.Name == step.Value && x.Enabled);
            if (macro != null) MacroController.PlayMacro(macro);
          }
          break;
        case "Delay":
          // ponytail: DelayMs 已在 ExecutePipeline 循环开头 await Task.Delay(step.DelayMs),
          // "Delay" 步骤本身只是个占位/分隔器——这里显式 break 表明是 no-op 而非 switch 漏处理。
          break;
      }
    }

    static void ExecuteSetPowerMode(string value) {
      if (!int.TryParse(value, out int pm)) return;
      Guid guid;
      if (pm == 0) guid = NativeMethods_Power.BEST_POWER_EFFICIENCY;
      else if (pm == 2) guid = NativeMethods_Power.BEST_PERFORMANCE;
      else guid = Guid.Empty;
      NativeMethods_Power.PowerSetActiveOverlayScheme(guid);
    }

    static void ExecuteSetCpuPower(string value) {
      if (value == "max") {
        OmenHardware.SetCpuPowerLimit(254);
        Views.OsdWindow.ShowCpuPowerOsd("max");
      } else if (int.TryParse(value, out int cpuVal) && cpuVal >= 10 && cpuVal <= 254) {
        OmenHardware.SetCpuPowerLimit((byte)cpuVal);
        Views.OsdWindow.ShowCpuPowerOsd(cpuVal + " W");
      }
    }

    static void ExecuteSetFanMode(string value) {
      if (value == "silent" || value == "cool" || value == "balanced") {
        ConfigService.FanTable = value;
        ConfigService.FanControl = "";
        FanService.LoadFanConfig(value + ".txt");
        SetMaxFanSpeedOff();
        TrayService.fanControlTimer.Change(0, 1000);
        Views.OsdWindow.ShowFanModeOsd(value);
      } else if (value == "smart" || value == "custom") {
        ConfigService.FanControl = "smart";
        FanService.LoadFanConfig(
          ConfigService.FanTable == "cool" ? "cool.txt"
          : ConfigService.FanTable == "balanced" ? "balanced.txt"
          : "silent.txt");
        FanService.InitSmartFanState(ConfigService.SmartFanEmaAlpha);
        SetMaxFanSpeedOff();
        TrayService.fanControlTimer.Change(0, 1000);
        Views.OsdWindow.ShowFanModeOsd("smart");
      } else if (value != null && value.StartsWith("json:")) {
        string json = value.Substring(5);
        var result = FanService.ImportCurveFromJson(json);
        if (result.HasValue) {
          ConfigService.FanControl = "smart";
          FanService.ApplyCustomCurve(result.Value.points);
          SetMaxFanSpeedOff();
          TrayService.fanControlTimer.Change(0, 1000);
          Views.OsdWindow.ShowFanModeOsd("custom");
        }
      } else if (value != null && value.StartsWith("manual")) {
        int pct = -1;
        if (value.Contains(":")) {
          string pctStr = value.Split(':')[1].Trim().TrimEnd('%');
          int.TryParse(pctStr, out pct);
        }
        if (pct >= 0 && pct <= 100) {
          ConfigService.FanControl = pct + "%";
          SetMaxFanSpeedOff();
          OmenHardware.SetFanLevel(pct, pct);
          Views.OsdWindow.ShowFanModeOsd(pct + "%");
          TrayService.fanControlTimer.Change(Timeout.Infinite, Timeout.Infinite);
        }
      }
    }

    static void ExecuteSetGpuPower(string value) {
      if (value == "max") {
        ConfigService.GpuPower = "max";
        ConfigService.TgpEnabled = true; ConfigService.PpabEnabled = true; ConfigService.DState = 1;
        OmenHardware.SetGpuPowerState(true, true, 1);
        Views.OsdWindow.ShowTextOsd("GPU: CTGP开+DB开");
      } else if (value == "med") {
        ConfigService.GpuPower = "med";
        ConfigService.TgpEnabled = true; ConfigService.PpabEnabled = false; ConfigService.DState = 1;
        OmenHardware.SetGpuPowerState(true, false, 1);
        Views.OsdWindow.ShowTextOsd("GPU: CTGP开+DB关");
      } else if (value == "min") {
        ConfigService.GpuPower = "min";
        ConfigService.TgpEnabled = false; ConfigService.PpabEnabled = false; ConfigService.DState = 1;
        OmenHardware.SetGpuPowerState(false, false, 1);
        Views.OsdWindow.ShowTextOsd("GPU: CTGP关+DB关");
      }
    }

    // ponytail: inline — shared by RunProgram and PlaySound
    static void StartShellProcess(string path, string errorTag) {
      if (string.IsNullOrEmpty(path)) return;
      try {
        Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true })?.Dispose();
      } catch (Exception ex) {
        Logger.Error(errorTag + " failed: " + ex.Message);
      }
    }

    internal static async System.Threading.Tasks.Task ApplyPresetAsync(string preset) {
      if (string.IsNullOrEmpty(preset)) return;

      PresetManager.SwitchPreset(preset);

      // ponytail: 硬件应用委托给 PresetManager.ApplyPresetHardware —— 与 OMEN 键 /
      // 手动切换预设走同一条经过验证的路径。此处旧的内联副本有两个缺陷(issue #20
      // "通知弹了但预设/风扇/CPU 功率没生效"):
      //   1) 缺 SetFanMode(0x31) unleash 前置,部分机型 EC 会忽略后续 0x29 功率写入;
      //   2) TPP 写在 SetGpuPowerState 之后,PPAB 读到的仍是 BIOS 默认总功率预算。
      // 规范路径还覆盖 PL1/PL2 独立写入、自定义预设 1.2 参数与按预设风扇曲线。
      // ponytail: 改为 await —— 多步骤 pipeline 连发时,确保 SetPreset 把 GPU/CPU 功率
      // 真正写入完,再跑下一步的 SetGpuPower/SetCpuPower,否则后写者可能反向覆盖前者。
      await PresetManager.AwaitableApplyPresetHardware();

      Views.OsdWindow.ShowPresetOsd(preset);
      ConfigService.FirePresetCycled(preset);
    }

    // ponytail: 保留旧入口给 HardwareApiService 等非 async 调用点 —— fire-and-forget,
    // 不阻塞 HTTP 处理线。语义与旧 void 一致:触发后不等硬件写完即返回。
    internal static void ApplyPreset(string preset) {
      _ = ApplyPresetAsync(preset);
    }

    private static void ApplyRefreshRate(int hz) {
      var dm = new NativeMethods_Display.DEVMODE();
      dm.dmSize = (short)System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods_Display.DEVMODE));
      if (!NativeMethods_Display.EnumDisplaySettings(null, NativeMethods_Display.ENUM_CURRENT_SETTINGS, ref dm)) return;
      int prevHz = dm.dmDisplayFrequency;
      dm.dmDisplayFrequency = hz;
      dm.dmFields = NativeMethods_Display.DM_DISPLAYFREQUENCY;
      int ret = NativeMethods_Display.ChangeDisplaySettings(ref dm, 0);
      if (ret != 0) {
        dm.dmDisplayFrequency = prevHz;
        NativeMethods_Display.ChangeDisplaySettings(ref dm, 0);
      }
    }

    // ── Hotkey support ──

    static void InitHotkeyHwnd() {
      if (_hotkeyHwndSource != null) return;
      var win = System.Windows.Application.Current?.MainWindow;
      if (win == null) return;
      // ponytail: use main window handle instead of a hidden HwndSource
      var helper = new System.Windows.Interop.WindowInteropHelper(win);
      var hwnd = helper.Handle;
      if (hwnd == IntPtr.Zero) return;
      var source = System.Windows.Interop.HwndSource.FromHwnd(hwnd);
      if (source == null) return;
      source.AddHook(HotkeyWndProc);
      _hotkeyHwndSource = source;
    }

    static IntPtr HotkeyWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) {
      try {
        if (msg == WM_HOTKEY) {
          int id = wParam.ToInt32();
          if (_registeredHotkeys != null && _registeredHotkeys.TryGetValue(id, out string value)) {
            FireTrigger("Hotkey", value);
            handled = true;
          }
        }
      } catch (Exception ex) { Logger.Error("HotkeyWndProc error: " + ex.Message); }
      return IntPtr.Zero;
    }

    static uint ParseHotkeyModifiers(string[] parts, out string keyPart) {
      uint mods = 0;
      keyPart = parts[parts.Length - 1];
      for (int i = 0; i < parts.Length - 1; i++) {
        switch (parts[i].ToLowerInvariant()) {
          case "alt": mods |= MOD_ALT; break;
          case "ctrl": case "control": mods |= MOD_CONTROL; break;
          case "shift": mods |= MOD_SHIFT; break;
          case "win": case "windows": mods |= MOD_WIN; break;
        }
      }
      return mods;
    }

    static Key HotkeyStringToKey(string hotkey) {
      string[] parts = hotkey.Split('+');
      string keyName = parts[parts.Length - 1].Trim();
      try { return (Key)Enum.Parse(typeof(Key), keyName, ignoreCase: true); } catch { }
      return FriendlyToKey(keyName);
    }

    static Key FriendlyToKey(string name) {
      if (name.Length == 1 && name[0] >= '0' && name[0] <= '9')
        return Key.D0 + (name[0] - '0');
      switch (name) {
        case ".": return Key.OemPeriod;    case ",": return Key.OemComma;
        case "-": return Key.OemMinus;     case "+": return Key.OemPlus;
        case "/": return Key.OemQuestion;  case ";": return Key.OemSemicolon;
        case "'": return Key.OemQuotes;    case "[": return Key.OemOpenBrackets;
        case "]": return Key.OemCloseBrackets;
        case "\\": return Key.OemPipe;     case "`": return Key.OemTilde;
        default: return Key.None;
      }
    }

    static void UnregisterAllHotkeys() {
      if (_registeredHotkeys == null) return;
      IntPtr hwnd = _hotkeyHwndSource?.Handle ?? IntPtr.Zero;
      foreach (int id in _registeredHotkeys.Keys)
        UnregisterHotKey(hwnd, id);
      _registeredHotkeys.Clear();
    }

    public static void RefreshHotkeys() {
      if (!_running) return;
      InitHotkeyHwnd();
      UnregisterAllHotkeys();
      if (_registeredHotkeys == null) _registeredHotkeys = new System.Collections.Concurrent.ConcurrentDictionary<int, string>();
      IntPtr hwnd = _hotkeyHwndSource.Handle;
      int nextId = 1;
      var pipelines = AutomationService.GetEnabledPipelines();
      foreach (var p in pipelines) {
        foreach (var t in p.Triggers) {
          if (!t.Enabled || t.Type != "Hotkey" || string.IsNullOrEmpty(t.Value)) continue;
          Key key = HotkeyStringToKey(t.Value);
          if (key == Key.None) continue;
          string[] parts = t.Value.Split('+');
          if (parts.Length < 2) continue;
          uint mods = ParseHotkeyModifiers(parts, out _);
          uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
          if (RegisterHotKey(hwnd, nextId, mods, vk))
            _registeredHotkeys.TryAdd(nextId, t.Value);
          else
            Logger.Warn($"RegisterHotKey failed for {t.Value}");
          nextId++;
        }
      }
      if (nextId > 1) Logger.Info($"Hotkeys: registered {_registeredHotkeys.Count} shortcut(s)");
    }

    // ── Trigger detection ──

    private static void FireTrigger(string triggerType, string triggerValue) {
      if (!_running) return;
      var pipelines = AutomationService.GetEnabledPipelines();
      foreach (var p in pipelines) {
        if (p.MatchesTrigger(triggerType, triggerValue))
          ExecutePipeline(p);
      }
    }

    private static void SubscribeProcessEvents() {
      try {
        var startQuery = new WqlEventQuery("WIN32_ProcessStartTrace", TimeSpan.FromSeconds(1), "ProcessName LIKE '%'");
        _processStartWatcher = new ManagementEventWatcher(startQuery);
        _processStartWatcher.EventArrived += (s, e) => {
          string name = e.NewEvent.Properties["ProcessName"]?.Value?.ToString() ?? "";
          FireTrigger("ProcessStart", name);
        };
        _processStartWatcher.Start();
      } catch (Exception ex) {
        Logger.Error("Automation: ProcessStart watcher failed: " + ex.Message);
      }

      try {
        var stopQuery = new WqlEventQuery("WIN32_ProcessStopTrace", TimeSpan.FromSeconds(1), "ProcessName LIKE '%'");
        _processStopWatcher = new ManagementEventWatcher(stopQuery);
        _processStopWatcher.EventArrived += (s, e) => {
          string name = e.NewEvent.Properties["ProcessName"]?.Value?.ToString() ?? "";
          FireTrigger("ProcessStop", name);
        };
        _processStopWatcher.Start();
      } catch (Exception ex) {
        Logger.Error("Automation: ProcessStop watcher failed: " + ex.Message);
      }
    }

    private static void SubscribePowerEvents() {
      SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

	    private static void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e) {
	      if (e.Mode == PowerModes.Resume) {
	        FireTrigger("Resume", "");
	      } else if (e.Mode == PowerModes.StatusChange) {
	        bool online = System.Windows.Forms.SystemInformation.PowerStatus.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online;
	        FireTrigger(online ? "PowerAC" : "PowerDC", "");
	      }
	    }

    private static void SubscribeSessionEvents() {
      _sessionSwitchHandler = (s, e) => {
        if (e.Reason == SessionSwitchReason.SessionLock)
          FireTrigger("SessionLock", "");
        else if (e.Reason == SessionSwitchReason.SessionUnlock)
          FireTrigger("SessionUnlock", "");
      };
      SystemEvents.SessionSwitch += _sessionSwitchHandler;
    }

    private static void SubscribeBatteryEvents() {
      try {
        _batteryPollTimer = new Timer(PollBatteryTrigger, null, 5000, 5000);
      } catch { }
    }

    private static Timer _batteryPollTimer;
    private static void PollBatteryTrigger(object state) {
      if (!_running) return;
      try {
        int pct = (int)(System.Windows.Forms.SystemInformation.PowerStatus.BatteryLifePercent * 100);
        if (pct == _lastBatteryPercent) return;
        _lastBatteryPercent = pct;
        foreach (var p in AutomationService.GetEnabledPipelines()) {
          foreach (var t in p.Triggers) {
            if (!t.Enabled) continue;
            // ponytail: A1 之后 _lastBatteryPercent 仅由本 5s timer 维护,
            // 上面 pct==_lastBatteryPercent 提前 return 意味着只有电量真实变化才走到这里。
            // 阈值 latch 通过 _tempTriggerFired 防止电量在阈值上下反复抖动时重复执行 preset 切换。
            string latchKey = (p.Name ?? "") + ":" + t.Type;
            bool matched = false;
            if (t.Type == "BatteryAbove" && int.TryParse(t.Value, out int above) && pct >= above) matched = true;
            else if (t.Type == "BatteryBelow" && int.TryParse(t.Value, out int below) && pct <= below) matched = true;
            if (matched && !_tempTriggerFired.Contains(latchKey)) {
              _tempTriggerFired.Add(latchKey);
              ExecutePipeline(p);
            } else if (!matched) {
              _tempTriggerFired.Remove(latchKey);
            }
          }
        }
      } catch { }
    }

    private static void SubscribeDisplayEvents() {
      try {
        _displaySettingsHandler = (s, e) => {
          bool connected = System.Windows.Forms.Screen.AllScreens.Length > 1;
          FireTrigger(connected ? "DisplayConnect" : "DisplayDisconnect", "");
        };
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += _displaySettingsHandler;
      } catch (Exception ex) {
        Logger.Error("Automation: DisplayEvent subscribe failed: " + ex.Message);
      }
    }

    private static void StartTempPollTimer() {
      _tempPollTimer = new Timer(PollTemperatureTriggers, null, 2000, 2000);
    }

    private static void PollTemperatureTriggers(object state) {
      if (!_running) return;
      try {
        float cpuTemp = HardwareService.CPUTemp;
        float gpuTemp = HardwareService.GPUTemp;
        bool tempChanged = Math.Abs(cpuTemp - _lastCpuTemp) > 0.5f || Math.Abs(gpuTemp - _lastGpuTemp) > 0.5f;
        _lastCpuTemp = cpuTemp;
        _lastGpuTemp = gpuTemp;
        if (tempChanged) {
          foreach (var p in AutomationService.GetEnabledPipelines()) {
            // ponytail: GetEnabledPipelines() 已对每个 pipeline 调 EnsureTriggers()，无需重复。
            // inner loop 只看 CpuTempAbove/GpuTempAbove， latch 防 CPU/GPU 在阈值上下抖动重复执行。
            foreach (var t in p.Triggers) {
              if (!t.Enabled) continue;
              string latchKey = (p.Name ?? "") + ":" + t.Type;
              if (t.Type == "CpuTempAbove" && int.TryParse(t.Value, out int cpuThresh)) {
                bool above = cpuTemp >= cpuThresh;
                if (above && !_tempTriggerFired.Contains(latchKey)) {
                  _tempTriggerFired.Add(latchKey);
                  ExecutePipeline(p);
                } else if (!above && cpuTemp < cpuThresh - 2) {
                  _tempTriggerFired.Remove(latchKey);
                }
              } else if (t.Type == "GpuTempAbove" && int.TryParse(t.Value, out int gpuThresh)) {
                bool above = gpuTemp >= gpuThresh;
                if (above && !_tempTriggerFired.Contains(latchKey)) {
                  _tempTriggerFired.Add(latchKey);
                  ExecutePipeline(p);
                } else if (!above && gpuTemp < gpuThresh - 2) {
                  _tempTriggerFired.Remove(latchKey);
                }
              }
            }
          }
        }
        // ponytail: 电池触发只走专用的 _batteryPollTimer（PollBatteryTrigger，已有 latch）。
        // 之前这里每 2s 用无 latch 的 FireTrigger 触发 BatteryAbove/Below，并写入 _lastBatteryPercent，
        // 让 5s 周期的 PollBatteryTrigger 几乎永远 pct==_lastBatteryPercent 提前 return，
        // 直接废掉了阈值触发的 "首次触发 latch" —— 电池反复在阈值上下抖动会重复执行 preset/refresh 切换。
      } catch { }
    }

	internal static void PollSchedule(object state) {
	      if (!_running) return;
	      try {
	        // ponytail: 15s 轮询但只对该分钟第一次触发发火——MatchTriggerValue 用 "HH:mm" 精确相等，
	        // 同一分钟内最多 4 个 tick 都会匹配并重复执行 preset/refresh 切换等副作用步骤。
	        string minute = DateTime.Now.ToString("HH:mm");
	        if (minute != _lastScheduleMinute) {
	          _lastScheduleMinute = minute;
	          FireTrigger("TimeSchedule", minute);
	        }
	      } catch { }
	    }

	    private static string _lastScheduleMinute;

	    private static void StartScheduleTimer() {
	      _lastScheduleMinute = null;
	      _scheduleTimer = new Timer(PollSchedule, null, 0, 15000);
	    }

    // ── Native methods for display ──

    private static class NativeMethods_Display {
      public const int ENUM_CURRENT_SETTINGS = -1;
      public const int DM_DISPLAYFREQUENCY = 0x400000;
      public const int DM_PELSWIDTH = 0x80000;
      public const int DM_PELSHEIGHT = 0x100000;
      public const int DM_BITSPERPEL = 0x40000;

      [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
      public static extern bool EnumDisplaySettings(string lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);

      [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
      public static extern int ChangeDisplaySettings(ref DEVMODE lpDevMode, int dwFlags);

      [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Auto)]
      public struct DEVMODE {
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmDeviceName;
        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int dmFields;
        public short dmOrientation;
        public short dmPaperSize;
        public short dmPaperLength;
        public short dmPaperWidth;
        public short dmScale;
        public short dmCopies;
        public short dmDefaultSource;
        public short dmPrintQuality;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
      }
    }

    private static class NativeMethods_Power {
      // ponytail: GUID 提取到共享 PowerOverlay (Services/NativeDefs.cs) — 4 处不再重复定义
      public static readonly Guid BEST_POWER_EFFICIENCY = PowerOverlay.BestPowerEfficiency;
      public static readonly Guid BEST_PERFORMANCE = PowerOverlay.BestPerformance;

      [System.Runtime.InteropServices.DllImport("powrprof.dll")]
      public static extern uint PowerSetActiveScheme(IntPtr userPowerKey, ref Guid activePolicyGuid);

      [System.Runtime.InteropServices.DllImport("powrprof.dll")]
      public static extern uint PowerSetActiveOverlayScheme(Guid overlaySchemeGuid);
    }

    // ── New automation step actions ──

    private static class AutomationActions {
      internal static void SetBrightness(byte brightness) {
        try {
          using (var searcher = new ManagementObjectSearcher(@"root\WMI",
              "SELECT * FROM WmiMonitorBrightnessMethods")) {
            foreach (ManagementObject mo in searcher.Get()) {
              mo.InvokeMethod("WmiSetBrightness", new object[] { 1, (uint)brightness });
            }
          }
        } catch (Exception ex) {
          Logger.Error("SetBrightness failed: " + ex.Message);
        }
      }

      internal static void SetGPUHybridMode(bool enable) {
        try {
          string pnpId = null;
          using (var searcher = new ManagementObjectSearcher(@"root\CIMV2",
              "SELECT PNPDeviceID FROM Win32_VideoController WHERE Name LIKE '%NVIDIA%'")) {
            foreach (ManagementObject mo in searcher.Get()) {
              pnpId = mo["PNPDeviceID"]?.ToString();
              break;
            }
          }
          if (string.IsNullOrEmpty(pnpId)) return;
          string escaped = pnpId.Replace("\\", "\\\\").Replace("'", "\\'");
          using (var searcher = new ManagementObjectSearcher(@"root\CIMV2",
              "SELECT * FROM Win32_PnPEntity WHERE DeviceID = '" + escaped + "'")) {
            foreach (ManagementObject mo in searcher.Get()) {
              mo.InvokeMethod(enable ? "Enable" : "Disable", null);
            }
          }
        } catch (Exception ex) {
          Logger.Error("SetGPUHybridMode failed: " + ex.Message);
        }
      }

      internal static async System.Threading.Tasks.Task SetWiFi(bool enable) {
        try {
          var access = await Radio.RequestAccessAsync();
          if (access != RadioAccessStatus.Allowed) return;
          var radios = await Radio.GetRadiosAsync();
          foreach (var r in radios) {
            if (r.Kind == RadioKind.WiFi)
              await r.SetStateAsync(enable ? RadioState.On : RadioState.Off);
          }
        } catch (Exception ex) {
          Logger.Error("SetWiFi failed: " + ex.Message);
        }
      }

      internal static async System.Threading.Tasks.Task SetBluetooth(bool enable) {
        try {
          var access = await Radio.RequestAccessAsync();
          if (access != RadioAccessStatus.Allowed) return;
          var radios = await Radio.GetRadiosAsync();
          foreach (var r in radios) {
            if (r.Kind == RadioKind.Bluetooth)
              await r.SetStateAsync(enable ? RadioState.On : RadioState.Off);
          }
        } catch (Exception ex) {
          Logger.Error("SetBluetooth failed: " + ex.Message);
        }
      }

      internal static void SetMicrophoneMute(bool mute) {
        try {
          System.Windows.Application.Current.Dispatcher.Invoke(() => {
            var devEnumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            IMMDevice defaultDev = null;
            try {
              devEnumerator.GetDefaultAudioEndpoint(1, 0, out defaultDev);
              if (defaultDev != null) {
                Guid iid = typeof(IAudioEndpointVolume).GUID;
                defaultDev.Activate(ref iid, 23, IntPtr.Zero, out object epVolObj);
                if (epVolObj is IAudioEndpointVolume epVol) {
                  Guid ctx = Guid.Empty;
                  epVol.SetMute(mute, ref ctx);
                  Marshal.ReleaseComObject(epVolObj);
                }
                Marshal.ReleaseComObject(defaultDev);
              }
            } catch {
              // try enumerating all devices as fallback
              IMMDeviceCollection devices = null;
              try {
                devEnumerator.EnumAudioEndpoints(2, 0x1F, out devices);
                if (devices != null) {
                  devices.GetCount(out uint count);
                  for (uint i = 0; i < count; i++) {
                    try {
                      devices.Item(i, out IMMDevice device);
                      if (device != null) {
                        Guid iid2 = typeof(IAudioEndpointVolume).GUID;
                        device.Activate(ref iid2, 23, IntPtr.Zero, out object epv);
                        if (epv is IAudioEndpointVolume ev) {
                          Guid ctx2 = Guid.Empty;
                          ev.SetMute(mute, ref ctx2);
                          Marshal.ReleaseComObject(epv);
                        }
                        Marshal.ReleaseComObject(device);
                      }
                    } catch { }
                  }
                }
              } finally {
                if (devices != null) Marshal.ReleaseComObject(devices);
              }
            } finally {
              if (devEnumerator != null) Marshal.ReleaseComObject(devEnumerator);
            }
          });
        } catch (Exception ex) {
          Logger.Error("SetMicrophoneMute failed: " + ex.Message);
        }
      }

      internal static void PlaySound(string filePath) {
        try {
          Logger.Info("PlaySound: " + (filePath ?? "(null)"));
          if (string.IsNullOrEmpty(filePath) || !System.IO.File.Exists(filePath)) {
            Logger.Error("PlaySound: file not found"); return;
          }
          // ponytail: shell-open via default player — same as RunProgram, works for any format
          Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true })?.Dispose();
        } catch (Exception ex) {
          Logger.Error("PlaySound failed: " + ex.Message);
        }
      }
    }

    // ── Core Audio COM interfaces ──

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator { }

    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator {
      void EnumAudioEndpoints(int dataFlow, int dwStateMask, out IMMDeviceCollection ppDevices);
      void GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppDevice);
    }

    [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection {
      void GetCount(out uint pcDevices);
      void Item(uint nDevice, out IMMDevice ppDevice);
    }

    [Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice {
      void Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
    }

    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume {
      void RegisterControlChangeNotify(IntPtr pNotify);
      void UnregisterControlChangeNotify(IntPtr pNotify);
      void GetChannelCount(out uint pnChannelCount);
      void SetMasterVolumeLevel(float fLevelDB, ref Guid pguidEventContext);
      void GetMasterVolumeLevel(out float pfLevelDB);
      void SetChannelVolumeLevel(uint nChannel, float fLevelDB, ref Guid pguidEventContext);
      void GetChannelVolumeLevel(uint nChannel, out float pfLevelDB);
      void SetMute(bool bMute, ref Guid pguidEventContext);
      void GetMute(out bool pbMute);
      void GetVolumeStepInfo(out uint pnStep, out uint pnStepCount);
      void VolumeStepUp(ref Guid pguidEventContext);
      void VolumeStepDown(ref Guid pguidEventContext);
      void QueryHardwareSupport(out uint pdwHardwareSupportMask);
      void GetVolumeRange(out float pflMinVolumeDB, out float pflMaxVolumeDB, out float pflVolumeIncrementDB);
    }
  }
}
