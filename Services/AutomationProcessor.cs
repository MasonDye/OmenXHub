// AutomationProcessor.cs - 自动化执行引擎
// 事件驱动触发器检测（进程/电源/会话/显示/温度/电池/计划），步骤执行（预设/风扇/功耗/WiFi/蓝牙等）
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
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

    public static bool IsExecuting => _executing;
    public static string CurrentPipelineName => _currentPipelineName;

    public static void Start() {
      if (_running) return;
      _running = true;

      SubscribeProcessEvents();
      SubscribePowerEvents();
      SubscribeSessionEvents();
      SubscribeBatteryEvents();
      SubscribeDisplayEvents();
      SubscribeLidEvents();
      StartScheduleTimer();
      StartTempPollTimer();

      FireTrigger("Startup", "");

      Logger.Info("AutomationProcessor started");
    }

    public static void Stop() {
      _running = false;
      if (_processStartWatcher != null) { _processStartWatcher.Stop(); _processStartWatcher.Dispose(); _processStartWatcher = null; }
      if (_processStopWatcher != null) { _processStopWatcher.Stop(); _processStopWatcher.Dispose(); _processStopWatcher = null; }
      if (_scheduleTimer != null) { _scheduleTimer.Dispose(); _scheduleTimer = null; }
      if (_tempPollTimer != null) { _tempPollTimer.Dispose(); _tempPollTimer = null; }
      SystemEvents.PowerModeChanged -= OnPowerModeChanged;
      if (_sessionSwitchHandler != null) { SystemEvents.SessionSwitch -= _sessionSwitchHandler; _sessionSwitchHandler = null; }
      if (_displaySettingsHandler != null) { SystemEvents.DisplaySettingsChanged -= _displaySettingsHandler; _displaySettingsHandler = null; }
      Logger.Info("AutomationProcessor stopped");
    }

    public static void ExecutePipeline(AutomationPipeline pipeline) {
      if (pipeline == null || pipeline.Steps == null || pipeline.Steps.Count == 0) return;
      lock (ExecLock) {
        if (_executing) return;
        _executing = true;
        _currentPipelineName = pipeline.Name;
      }
      ExecutionStatusChanged?.Invoke(pipeline.Name);
      Task.Run(async () => {
        try {
          foreach (var step in pipeline.Steps) {
            if (step.DelayMs > 0)
              await System.Threading.Tasks.Task.Delay(step.DelayMs);
            ExecuteStep(step);
          }
        } catch (Exception ex) {
          Logger.Error("AutomationProcessor.ExecutePipeline error: " + ex.Message);
        } finally {
          lock (ExecLock) { _executing = false; _currentPipelineName = null; }
          ExecutionStatusChanged?.Invoke(null);
        }
      });
    }

    private static void ExecuteStep(AutomationStep step) {
      if (step == null || string.IsNullOrEmpty(step.Type)) return;
      switch (step.Type) {
        case "SetPreset":
          ApplyPreset(step.Value);
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
          if (int.TryParse(step.Value, out int pm)) {
            Guid guid;
            if (pm == 0) guid = NativeMethods_Power.BEST_POWER_EFFICIENCY;
            else if (pm == 2) guid = NativeMethods_Power.BEST_PERFORMANCE;
            else guid = Guid.Empty;
            NativeMethods_Power.PowerSetActiveOverlayScheme(guid);
          }
          break;
        case "SetMaxFrameRate":
          if (int.TryParse(step.Value, out int fps) && OmenHardware.HasNvidiaGpu())
            HP.Omen.Core.Common.NVidiaApi.NvApiWrapper.NVAPI_SetMaxFrameRate(fps);
          break;
        case "SetCpuPower":
          if (step.Value == "max") {
            OmenHardware.SetCpuPowerLimit(254);
            Views.OsdWindow.ShowCpuPowerOsd("max");
          } else if (int.TryParse(step.Value, out int cpuVal) && cpuVal >= 10 && cpuVal <= 254) {
            OmenHardware.SetCpuPowerLimit((byte)cpuVal);
            Views.OsdWindow.ShowCpuPowerOsd(cpuVal + " W");
          }
          break;
        case "SetFanMode":
          if (step.Value == "silent") {
            ConfigService.FanTable = "silent";
            ConfigService.FanControl = "";
            FanService.LoadFanConfig("silent.txt");
            SetMaxFanSpeedOff();
            TrayService.fanControlTimer.Change(0, 1000);
            Views.OsdWindow.ShowFanModeOsd("silent");
          } else if (step.Value == "cool") {
            ConfigService.FanTable = "cool";
            ConfigService.FanControl = "";
            FanService.LoadFanConfig("cool.txt");
            SetMaxFanSpeedOff();
            TrayService.fanControlTimer.Change(0, 1000);
            Views.OsdWindow.ShowFanModeOsd("cool");
          } else if (step.Value == "custom") {
            ConfigService.FanControl = "custom";
            SetMaxFanSpeedOff();
            TrayService.fanControlTimer.Change(0, 1000);
            Views.OsdWindow.ShowFanModeOsd("custom");
          } else if (step.Value == "manual" && int.TryParse(step.IntValue.ToString(), out int pct) && pct >= 0 && pct <= 100) {
            ConfigService.FanControl = pct + "%";
            SetMaxFanSpeedOff();
            OmenHardware.SetFanLevel(pct, pct);
            Views.OsdWindow.ShowFanModeOsd(pct + "%");
            TrayService.fanControlTimer.Change(Timeout.Infinite, Timeout.Infinite);
          }
          break;
        case "RunProgram":
          if (!string.IsNullOrEmpty(step.Value)) {
            try {
              Process.Start(new ProcessStartInfo {
                FileName = step.Value,
                UseShellExecute = true
              });
            } catch (Exception ex) {
              Logger.Error("Automation RunProgram failed: " + ex.Message);
            }
          }
          break;
        case "Notification":
          if (TrayService.TrayIcon != null && !string.IsNullOrEmpty(step.Value)) {
            TrayService.TrayIcon.ShowBalloonTip("OmenXHub Automation", step.Value, 3000);
          }
          break;
        case "SetIccMax":
          if (int.TryParse(step.Value, out int iccVal) && iccVal > 0 && iccVal <= 255) {
            ConfigService.IccMax = iccVal;
            OmenHardware.SetIccMaxByWmi(iccVal);
          }
          break;
        case "SetAcLoadLine":
          if (int.TryParse(step.Value, out int acllVal) && acllVal > 0) {
            ConfigService.AcLoadLine = acllVal;
            OmenHardware.SetLoadLine(acllVal);
          }
          break;
        case "SetTpp":
          if (int.TryParse(step.Value, out int tppVal) && tppVal > 0 && tppVal <= 255) {
            ConfigService.Tpp = tppVal;
            OmenHardware.SetConcurrentTdp((byte)tppVal);
          }
          break;
        case "SetGpuPower":
          if (int.TryParse(step.Value, out int tgpVal) && tgpVal > 0) {
            ConfigService.TgpEnabled = true;
            ConfigService.PpabEnabled = true;
            ConfigService.DState = 1;
            OmenHardware.SetGpuPowerState(true, true, 1);
          }
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
        case "SetFanCurve":
          if (!string.IsNullOrEmpty(step.Value)) {
            var curve = FanService.LoadPresetCurve(step.Value, false);
            if (curve != null && curve.Count > 0) {
              ConfigService.FanControl = "custom";
              FanService.ApplyCustomCurve(curve);
              SetMaxFanSpeedOff();
              TrayService.fanControlTimer.Change(0, 1000);
            }
          }
          break;
        case "SetGPUHybridMode":
          if (!string.IsNullOrEmpty(step.Value))
            AutomationActions.SetGPUHybridMode(step.Value == "on");
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
            AutomationActions.SetWiFi(step.Value == "on");
          break;
        case "SetBluetooth":
          if (!string.IsNullOrEmpty(step.Value))
            AutomationActions.SetBluetooth(step.Value == "on");
          break;
        case "PlaySound":
          if (!string.IsNullOrEmpty(step.Value))
            AutomationActions.PlaySound(step.Value);
          break;
        case "RunMacro":
          if (!string.IsNullOrEmpty(step.Value)) {
            var macro = MacroService.Macros.Find(x => x.Name == step.Value && x.Enabled);
            if (macro != null) MacroController.PlayMacro(macro);
          }
          break;
      }
    }

    internal static void ApplyPreset(string preset) {
      if (string.IsNullOrEmpty(preset)) return;

      PresetManager.SwitchPreset(preset);

      // Apply hardware
      if (ConfigService.GpuClock > 0) TrayService.SetGPUClockLimit(ConfigService.GpuClock);
      OmenHardware.SetGpuPowerState(ConfigService.TgpEnabled, ConfigService.PpabEnabled,
          ConfigService.DState == 2 ? 2 : 1);
      if (ConfigService.Tpp > 0) OmenHardware.SetConcurrentTdp((byte)ConfigService.Tpp);
      if (ConfigService.IccMax > 0) OmenHardware.SetIccMaxByWmi(ConfigService.IccMax);
      if (ConfigService.AcLoadLine > 0) OmenHardware.SetLoadLine(ConfigService.AcLoadLine);
      if (OmenHardware.HasNvidiaGpu()) {
        int fps = ConfigService.MaxFrameRate;
        HP.Omen.Core.Common.NVidiaApi.NvApiWrapper.NVAPI_SetMaxFrameRate(fps <= 0 ? 0 : fps);
      }
      if (!string.IsNullOrEmpty(ConfigService.PowerPlanGuid)) {
        Guid g = Guid.Parse(ConfigService.PowerPlanGuid);
        NativeMethods_Power.PowerSetActiveScheme(IntPtr.Zero, ref g);
      }
      Guid pmGuid;
      if (ConfigService.PowerMode == 0) pmGuid = NativeMethods_Power.BEST_POWER_EFFICIENCY;
      else if (ConfigService.PowerMode == 2) pmGuid = NativeMethods_Power.BEST_PERFORMANCE;
      else pmGuid = Guid.Empty;
      NativeMethods_Power.PowerSetActiveOverlayScheme(pmGuid);
      if (ConfigService.CpuPower == "max") OmenHardware.SetCpuPowerLimit(254);
      else if (int.TryParse(ConfigService.CpuPower?.Replace(" W", ""), out int cpuVal) && cpuVal >= 10 && cpuVal <= 254)
        OmenHardware.SetCpuPowerLimit((byte)cpuVal);
      // Apply fan curves
      var cpuCurve = FanService.LoadPresetCurve(preset, false);
      if (cpuCurve != null && cpuCurve.Count > 0 && ConfigService.FanControl == "custom")
        FanService.ApplyCustomCurve(cpuCurve);
      var gpuCurve = FanService.LoadPresetCurve(preset, true);
      if (gpuCurve != null && gpuCurve.Count > 0 && ConfigService.FanControl == "custom")
        FanService.ApplyCustomCurveGPU(gpuCurve);
      // Apply refresh rate from preset config
      if (ConfigService.RefreshRate > 0)
        ApplyRefreshRate(ConfigService.RefreshRate);
      Views.OsdWindow.ShowPresetOsd(preset);
      ConfigService.FirePresetCycled(preset);
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

    private static void SubscribeLidEvents() {
      try {
        var query = new WqlEventQuery("SELECT * FROM Win32_SystemLaunchCondition");
        // Fallback: use power events to detect lid close via battery report
      } catch { }
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
          FireTrigger("CpuTempAbove", ((int)cpuTemp).ToString());
          FireTrigger("GpuTempAbove", ((int)gpuTemp).ToString());
        }

        int batPct = (int)(System.Windows.Forms.SystemInformation.PowerStatus.BatteryLifePercent * 100);
        if (batPct >= 0 && batPct <= 100 && batPct != _lastBatteryPercent) {
          _lastBatteryPercent = batPct;
          FireTrigger("BatteryAbove", batPct.ToString());
          FireTrigger("BatteryBelow", batPct.ToString());
        }
      } catch { }
    }

    private static void StartScheduleTimer() {
      RescheduleTimer();
    }

    static void RescheduleTimer() {
      if (_scheduleTimer != null) { _scheduleTimer.Dispose(); _scheduleTimer = null; }

      long minTicks = long.MaxValue;
      long nowTicks = DateTime.Now.Ticks;
      foreach (var p in AutomationService.GetEnabledPipelines()) {
        p.EnsureTriggers();
        foreach (var t in p.Triggers) {
          if (!t.Enabled || t.Type != "TimeSchedule" || string.IsNullOrEmpty(t.Value)) continue;
          if (TimeSpan.TryParse(t.Value, out var ts)) {
            long eventTicks = nowTicks - nowTicks % TimeSpan.TicksPerDay + ts.Ticks;
            if (eventTicks <= nowTicks) eventTicks += TimeSpan.TicksPerDay;
            long diff = (eventTicks - nowTicks) / TimeSpan.TicksPerMillisecond;
            if (diff > 0 && diff < minTicks) minTicks = diff;
          }
        }
      }
      int nextMs;
      if (minTicks < long.MaxValue)
        nextMs = (int)Math.Min(minTicks, 86400000);
      else
        return;
      _scheduleTimer = new Timer(CheckSchedule, null, nextMs, Timeout.Infinite);
    }

    private static void CheckSchedule(object state) {
      try {
        string now = DateTime.Now.ToString("HH:mm");
        FireTrigger("TimeSchedule", now);
      } finally {
        RescheduleTimer();
      }
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
      public static readonly Guid BEST_POWER_EFFICIENCY = Guid.Parse("961cc777-2547-4f9d-8174-7d86181b8a7a");
      public static readonly Guid BEST_PERFORMANCE = Guid.Parse("ded574b5-45a0-4f42-8737-46345c09c238");

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
              mo.InvokeMethod("WmiSetBrightness", new object[] { (uint)brightness, 1 });
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

      internal static async void SetWiFi(bool enable) {
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

      internal static async void SetBluetooth(bool enable) {
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
          // Core Audio API requires STA thread — dispatch to UI thread
          System.Windows.Application.Current.Dispatcher.Invoke(() => {
            var devEnumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            IMMDeviceCollection devices = null;
            try {
              devEnumerator.EnumAudioEndpoints(1, 1, out devices);
              if (devices != null) {
                devices.GetCount(out uint count);
                for (uint i = 0; i < count; i++) {
                  devices.Item(i, out IMMDevice device);
                  if (device != null) {
                    Guid iid = typeof(IAudioEndpointVolume).GUID;
                    device.Activate(ref iid, 0, IntPtr.Zero, out object epVolObj);
                    if (epVolObj is IAudioEndpointVolume epVol) {
                      Guid ctx = Guid.Empty;
                      epVol.SetMute(mute, ref ctx);
                    }
                    Marshal.ReleaseComObject(device);
                  }
                }
              }
            } finally {
              if (devices != null) Marshal.ReleaseComObject(devices);
              if (devEnumerator != null) Marshal.ReleaseComObject(devEnumerator);
            }
          });
        } catch (Exception ex) {
          Logger.Error("SetMicrophoneMute failed: " + ex.Message);
        }
      }

      internal static void PlaySound(string filePath) {
        try {
          if (!System.IO.File.Exists(filePath)) {
            Logger.Error("PlaySound: file not found: " + filePath);
            return;
          }
          using (var player = new System.Media.SoundPlayer(filePath)) {
            player.PlaySync();
          }
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
