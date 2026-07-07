// TrayService.cs - 系统托盘核心服务
// 托盘图标管理、WPF 右键菜单、定时器更新、自动风扇保护、DB 解锁、电源恢复、Omen 键监听
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32.TaskScheduler;
using OmenSuperHub.Utils;
using OmenSuperHub.Views;
using static OmenSuperHub.OmenHardware;
using static OmenSuperHub.OmenLighting;

namespace OmenSuperHub.Services {
  internal static class TrayService {
    private static Utils.TrayHelper _trayHelperRef;
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    extern static bool DestroyIcon(IntPtr handle);

    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hWnd);

    // ══════════════════════════════════════════════════════
    // State
    // ══════════════════════════════════════════════════════
    public static NativeTrayIcon TrayIcon;
    static bool _trayIconInitialized;
    static string _savedFanControl;
    static string _savedFanTable;
    static bool _autoProtectActive;
    static string _dataLocalizeDir;
    public static void ResetAutoProtect() {
      _autoProtectActive = false;
      _savedFanControl = null;
      _savedFanTable = null;
    }
    static System.Windows.Controls.ContextMenu wpfContextMenu;
    public static int countDB = 0, countDBInit = 5, tryTimes = 0, CPULimitDB = 25;
    static int countRestore = 0;

    // Timers
    public static System.Threading.Timer fanControlTimer;
    public static System.Timers.Timer tooltipUpdateTimer;
    public static System.Windows.Threading.DispatcherTimer checkFloatingTimer, optimiseTimer;

    static bool checkFloating = false;
    static int flagStart = 0;

    // ══════════════════════════════════════════════════════
    // WPF Context Menu
    // ══════════════════════════════════════════════════════
    public static void InitTrayIcon() {
      if (_trayIconInitialized) return;
      _trayIconInitialized = true;

      // Read icon config early
      ConfigService.CustomIcon = ConfigService.ReadIconConfig();
      if (ConfigService.CustomIcon == "custom" && !CheckCustomIcon()) {
        ConfigService.CustomIcon = "original";
        ConfigService.Save("CustomIcon");
      }

      TrayIcon = new NativeTrayIcon();
      SetDefaultLogoIcon();
      // TrayHelper creates and shows its own visible NativeTrayIcon

      // Apply icon (TrayHelper will clone it when created)
      ApplyIconStyle();

      BuildWpfContextMenu(); // required by UpdateCheckedState / RestoreConfig

      // Right-click & double-click handled by TrayHelper (MainWindow)

      // GPU monitoring auto-change notifications
      HardwareService.OnGpuMonitoringChanged += (enabled, message) => {
        if (enabled) UpdateCheckedState("monitorGPUGroup", "开启GPU监控");
        else UpdateCheckedState("monitorGPUGroup", "关闭GPU监控");

        _trayHelperRef?.ShowBalloonTip("状态更改提示", message, 3000);
      };

      // Initialize timers
      tooltipUpdateTimer = new System.Timers.Timer(1000);
      tooltipUpdateTimer.Elapsed += (s, e) => UpdateTooltip();
      tooltipUpdateTimer.AutoReset = true;
      tooltipUpdateTimer.Start();
    }

    // ══════════════════════════════════════════════════════
    // Build WPF ContextMenu (simplified - controls moved to MainWindow)
    // ══════════════════════════════════════════════════════
    static void BuildWpfContextMenu() {
      wpfContextMenu = new System.Windows.Controls.ContextMenu();

      // Apply theme
      var themeDict = System.Windows.Application.Current?.Resources;
      if (themeDict != null) {
        var menuStyle = themeDict["OmenContextMenu"] as System.Windows.Style;
        if (menuStyle != null) wpfContextMenu.Style = menuStyle;
      }

      // ── 宏 ──
      wpfContextMenu.Items.Add(CreateMenuItem(Strings.SidebarMacro, null, () => {
        Views.MainWindow.NavigateToPage("Macro");
      }, false, Wpf.Ui.Controls.SymbolRegular.Keyboard24));

      // ── Language ──
      AddLanguageMenu();

      // ── 关于OXH ──
      wpfContextMenu.Items.Add(CreateMenuItem(Strings.Help, null, () => {
        Views.HelpWindow.ShowInstance();
      }, false, Wpf.Ui.Controls.SymbolRegular.QuestionCircle24));

      wpfContextMenu.Items.Add(new System.Windows.Controls.Separator() {
        Style = GetSeparatorStyle()
      });

      // ── 退出 ──
      wpfContextMenu.Items.Add(CreateMenuItem(Strings.Exit, null, () => Exit(), false, Wpf.Ui.Controls.SymbolRegular.SignOut24));
    }

    static void AddLanguageMenu() {
      var langMenu = CreateParentMenuItem(Strings.LanguageMenu);
      System.Action applyAll = () => {
        RebuildMenu();
        Views.MainWindow.ApplyLanguageToInstance();
      };
      langMenu.Items.Add(CreateMenuItem(Strings.LangSimplified, "languageGroup", () => {
        Strings.SetLanguage(AppLanguage.SimplifiedChinese);
        ConfigService.Language = "SimplifiedChinese";
        ConfigService.Save("Language");
        applyAll();
      }, Strings.Current == AppLanguage.SimplifiedChinese, Wpf.Ui.Controls.SymbolRegular.Globe24));
      langMenu.Items.Add(CreateMenuItem(Strings.LangTraditional, "languageGroup", () => {
        Strings.SetLanguage(AppLanguage.TraditionalChinese);
        ConfigService.Language = "TraditionalChinese";
        ConfigService.Save("Language");
        applyAll();
      }, Strings.Current == AppLanguage.TraditionalChinese, Wpf.Ui.Controls.SymbolRegular.Globe24));
      langMenu.Items.Add(CreateMenuItem(Strings.LangEnglish, "languageGroup", () => {
        Strings.SetLanguage(AppLanguage.English);
        ConfigService.Language = "English";
        ConfigService.Save("Language");
        applyAll();
      }, Strings.Current == AppLanguage.English, Wpf.Ui.Controls.SymbolRegular.Globe24));
      wpfContextMenu.Items.Add(langMenu);
    }

    internal static void RegisterTrayHelper(Utils.TrayHelper helper) {
      _trayHelperRef = helper;
    }

    internal static void StartTrayHelperTimers() {
      _trayHelperRef?.StartTooltipTimer();
    }

    internal static void ShowNotification(string title, string text, int timeoutMs = 3000) {
      _trayHelperRef?.ShowBalloonTip(title, text, timeoutMs);
    }

    internal static void RebuildMenu() {
      System.Windows.Application.Current?.Dispatcher.Invoke(() => {
        wpfContextMenu.Items.Clear();
        BuildWpfContextMenu();
        _trayHelperRef?.RebuildMenu();
      });
    }

    // ══════════════════════════════════════════════════════
    // Menu Item Helper
    // ══════════════════════════════════════════════════════
    static System.Windows.Controls.MenuItem CreateMenuItem(string header, string group, System.Action action, bool isChecked, Wpf.Ui.Controls.SymbolRegular? icon = null) {
      var item = new System.Windows.Controls.MenuItem {
        Header = header,
        Tag = group,
        IsChecked = isChecked,
        IsCheckable = group != null,
      };
      if (icon.HasValue) {
        item.Icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = icon.Value, FontSize = 14 };
      }

      // Apply theme style
      var themeDict = System.Windows.Application.Current?.Resources;
      if (themeDict != null) {
        var style = themeDict["OmenMenuItem"] as System.Windows.Style;
        if (style != null) item.Style = style;
      }

      item.Click += (s, e) => {
        // Pre-action checks (same as original)
        if (header == "解锁版本") {
          if (!HardwareService.PowerOnline) {
            DialogHelper.Warn("请连接交流电源", "提示");
            ConfigService.DBVersion = 2;
            countDB = 0;
            ConfigService.Save("DBVersion");
            UpdateCheckedState("DBGroup", "普通版本");
            return;
          }
          if (!CheckDBVersion(1)) {
            ConfigService.DBVersion = 2;
            countDB = 0;
            ConfigService.Save("DBVersion");
            UpdateCheckedState("DBGroup", "普通版本");
            return;
          }
        }
        if (header == "普通版本" && !CheckDBVersion(2))
          return;
        if (header == "自定义图标" && !CheckCustomIcon())
          return;

        action();
        if (group != null) {
          UpdateCheckedState(group, null, item);
        }
      };
      return item;
    }

    static System.Windows.Controls.MenuItem CreateParentMenuItem(string header) {
      var item = new System.Windows.Controls.MenuItem { Header = header };
      var themeDict = System.Windows.Application.Current?.Resources;
      if (themeDict != null) {
        var style = themeDict["OmenMenuItem"] as System.Windows.Style;
        if (style != null) item.Style = style;
      }
      return item;
    }

    static System.Windows.Style GetSeparatorStyle() {
      var themeDict = System.Windows.Application.Current?.Resources;
      if (themeDict != null) {
        return themeDict["OmenMenuSeparator"] as System.Windows.Style;
      }
      return null;
    }

    // ══════════════════════════════════════════════════════
    // Checked State Management
    // ══════════════════════════════════════════════════════
    public static void UpdateCheckedState(string group, string itemHeader = null, System.Windows.Controls.MenuItem menuItemToCheck = null) {
      System.Windows.Application.Current?.Dispatcher.Invoke(() => {
        if (menuItemToCheck == null && itemHeader != null) {
          menuItemToCheck = FindMenuItem(wpfContextMenu.Items, itemHeader);
          if (menuItemToCheck == null) return;
        }
        if (menuItemToCheck == null) return;

        UpdateMenuItemsCheckedState(wpfContextMenu.Items, group, menuItemToCheck);
      });
    }

    static void UpdateMenuItemsCheckedState(System.Windows.Controls.ItemCollection items, string group, System.Windows.Controls.MenuItem clicked) {
      foreach (var obj in items) {
        var menuItem = obj as System.Windows.Controls.MenuItem;
        if (menuItem == null) continue;

        if (menuItem.Tag as string == group) {
          menuItem.IsChecked = (menuItem == clicked);
        }
        if (menuItem.HasItems) {
          UpdateMenuItemsCheckedState(menuItem.Items, group, clicked);
        }
      }
    }

    static System.Windows.Controls.MenuItem FindMenuItem(System.Windows.Controls.ItemCollection items, string header) {
      foreach (var obj in items) {
        var menuItem = obj as System.Windows.Controls.MenuItem;
        if (menuItem == null) continue;

        if (menuItem.Header as string == header) {
          return menuItem;
        }
        if (menuItem.HasItems) {
          var found = FindMenuItem(menuItem.Items, header);
          if (found != null) return found;
        }
      }
      return null;
    }

    // ══════════════════════════════════════════════════════
    // Tooltip Update (timer callback)
    // ══════════════════════════════════════════════════════
    static void UpdateTooltip() {
      HardwareService.QueryHardware();
      if (HardwareService.MonitorFan)
        HardwareService.UpdateFanSpeed(GetFanLevel());
      // Update tray icon tooltip (consolidated from TrayHelper)
      try {
        var tip = "OMEN X Hub";
        if (HardwareService.MonitorCPU)
          tip += $" \u00b7 CPU {(int)HardwareService.CPUTemp}\u00b0C";
        if (ConfigService.MonitorGPU)
          tip += $" \u00b7 GPU {(int)HardwareService.GPUTemp}\u00b0C";
        _trayHelperRef?.SetTooltip(tip);
      } catch { }

      if (ConfigService.DataLocalize == "on") {
        try {
          if (_dataLocalizeDir == null)
            _dataLocalizeDir = System.IO.Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath);
          System.IO.File.WriteAllText(System.IO.Path.Combine(_dataLocalizeDir, "cpu_temp.txt"), $"{(int)HardwareService.CPUTemp}°C");
          System.IO.File.WriteAllText(System.IO.Path.Combine(_dataLocalizeDir, "gpu_temp.txt"), $"{(int)HardwareService.GPUTemp}°C");
        } catch { }
      }

      // Auto fan protect: if CPU >95°C and fans are fixed <75%, switch to auto+cool
      // Save previous fan state so it can be restored on cooldown
      // BUG FIX: Trigger requires toggle ON, but restore runs regardless so active sessions can unwind
      bool fanProtectOn = ConfigService.AutoFanProtect == "on";
      if (fanProtectOn && HardwareService.MonitorFan && HardwareService.CPUTemp > 0) {
        if (!_autoProtectActive && ConfigService.FanControl != "auto"
            && HardwareService.CPUTemp > 95 && HardwareService.FanSpeedNow != null) {
          int maxSpeed = 0;
          foreach (int s in HardwareService.FanSpeedNow) { if (s > maxSpeed) maxSpeed = s; }
          if (maxSpeed > 0 && maxSpeed < 75) {
            _savedFanControl = ConfigService.FanControl;
            _savedFanTable = ConfigService.FanTable;
            _autoProtectActive = true;
            SetMaxFanSpeedOff();
            fanControlTimer.Change(0, 1000);
            ConfigService.FanControl = "auto";
            ConfigService.FanTable = "cool";
            ConfigService.Save("FanControl");
            ConfigService.Save("FanTable");
            FanService.LoadFanConfig("cool.txt");
            Logger.Info("Auto fan protect: CPU>95°C with fixed fan, switched to auto+cool");
            _trayHelperRef?.ShowBalloonTip("高温自动保护", "CPU温度>95°C，已强制切换为降温曲线", 3000);
          }
        }
      }
      // Cooldown/restore: runs even if toggle was turned off mid-protection
      if (_autoProtectActive && (fanProtectOn == false || HardwareService.CPUTemp < 80)) {
        _autoProtectActive = false;
        if (!string.IsNullOrEmpty(_savedFanControl)) {
          ConfigService.FanControl = _savedFanControl;
          ConfigService.FanTable = _savedFanTable;
          ConfigService.Save("FanControl");
          // Re-apply the saved fan mode
          if (_savedFanControl == "silent" || _savedFanControl == "") {
            FanService.LoadFanConfig((_savedFanTable ?? "silent") + ".txt");
            SetMaxFanSpeedOff();
            fanControlTimer.Change(0, 1000);
          } else if (_savedFanControl == "smart" || _savedFanControl == "custom") {
            SetMaxFanSpeedOff();
            fanControlTimer.Change(0, 1000);
          } else if (_savedFanControl.EndsWith("%") || _savedFanControl.EndsWith(" RPM")) {
            int pct = _savedFanControl.EndsWith("%")
              ? int.Parse(_savedFanControl.TrimEnd('%'))
              : int.Parse(_savedFanControl.Replace(" RPM", "").Trim()) / 100;
            SetMaxFanSpeedOff();
            OmenHardware.SetFanLevel(pct, pct);
            fanControlTimer.Change(Timeout.Infinite, Timeout.Infinite);
          }
          _savedFanControl = null;
          _savedFanTable = null;
          Logger.Info("Auto fan protect: CPU cooled to <80°C (or protection disabled), restored fan config");
        }
      }

      Views.FloatingWindow.UpdateAllText();

      if (ConfigService.CustomIcon == "dynamic")
        GenerateDynamicIcon((int)HardwareService.CPUTemp);

      // DB unlock logic
      if (countDB > 0) {
        countDB--;
        if (countDB == 0) {
          string deviceId = "\"ACPI\\\\NVDA0820\\\\NPCF\"";
          string command = $"pnputil /disable-device {deviceId}";
          ExecuteCommand(command);

          float powerLimits = GPUPowerLimits();
          if (HardwareService.PowerOnline && powerLimits >= 0) {
            tryTimes++;
            if (tryTimes == 2) {
              tryTimes = 0;
              if (HardwareService.CPUPower > CPULimitDB + 10)
                DialogHelper.Warn("请在CPU低负载下解锁", "提示");
              else
                DialogHelper.Warn($"功耗异常，解锁失败，请重新尝试！\n当前显卡功耗限制为：{powerLimits:F2} W！", "提示");
              command = $"pnputil /enable-device {deviceId}";
              ExecuteCommand(command);
              ConfigService.DBVersion = 2;
              countDB = 0;
              ConfigService.Save("DBVersion");
              UpdateCheckedState("DBGroup", "普通版本");
            } else {
              SetFanMode((byte)0x31);
              SetMaxGpuPower();
              SetCpuPowerLimit((byte)CPULimitDB);
              countDB = countDBInit;
            }
          } else {
            tryTimes = 0;
            if (ConfigService.AutoStart == "off") {
              DialogHelper.Warn("解锁成功！但当前未设置开机自启，解锁后若重启电脑会导致功耗异常，需要重新解锁！", "提示");
            }
          }
          if (tryTimes == 0) {
            if (ConfigService.FanMode.Contains("performance")) {
              SetFanMode((byte)0x31);
            } else if (ConfigService.FanMode.Contains("default")) {
              SetFanMode((byte)0x30);
            }
            RestoreCPUPower();
          }
        } else if (countDB == countDBInit - 1) {
          string deviceId = "\"ACPI\\\\NVDA0820\\\\NPCF\"";
          string command = $"pnputil /enable-device {deviceId}";
          ExecuteCommand(command);
        }
      }

      // Restore from hibernation
      if (countRestore > 0) {
        countRestore--;
        if (countRestore == 0) {
          RestoreConfig();
        }
      }
    }

    // ══════════════════════════════════════════════════════
    // Floating Bar Toggle (Omen Key)
    // ══════════════════════════════════════════════════════
    public static void HandleFloatingBarToggle() {
      if (checkFloating) {
        checkFloating = false;
        try {
          if (ConfigService.FloatingBar == "on") {
            ConfigService.FloatingBar = "off";
            Views.FloatingWindow.CloseAll();
            UpdateCheckedState("floatingBarGroup", "关闭浮窗");
          } else {
            ConfigService.FloatingBar = "on";
            Views.FloatingWindow.ShowInstances();
            UpdateCheckedState("floatingBarGroup", "显示浮窗");
          }
          ConfigService.Save("FloatingBar");
        } catch (Exception ex) {
          Logger.Error($"HandleFloatingBarToggle error: {ex.Message}");
        }
      }
    }

    public static void SetCheckFloating() {
      checkFloating = true;
    }

    // ══════════════════════════════════════════════════════
    // Startup Timers & Lifecycle
    // ══════════════════════════════════════════════════════
    public static void StartTimers() {
      HardwareService.DetectAmbientSensor();
      HardwareService.RefreshPawnIOState();
      // Fan control timer
      fanControlTimer = new System.Threading.Timer((e) => {
        int fanSpeed1, fanSpeed2;
        if (ConfigService.FanControl == "smart" || ConfigService.FanControl == "custom") {
          fanSpeed1 = FanService.GetSmartFanSpeed(0) / 100;
          fanSpeed2 = ConfigService.FanSync ? fanSpeed1 : FanService.GetSmartFanSpeed(1) / 100;
        } else {
          fanSpeed1 = FanService.GetFanSpeedForTemperature(0) / 100;
          fanSpeed2 = ConfigService.FanSync ? fanSpeed1 : FanService.GetFanSpeedForTemperature(1) / 100;
        }
        if (HardwareService.MonitorFan) {
          if (fanSpeed1 != HardwareService.FanSpeedNow[0] || fanSpeed2 != HardwareService.FanSpeedNow[1]) {
            SetFanLevel(fanSpeed1, fanSpeed2);
          }
        } else
          SetFanLevel(fanSpeed1, fanSpeed2);
      }, null, 100, 1000);

      // Optimise timer (replaces WinForms Timer)
      optimiseTimer = new System.Windows.Threading.DispatcherTimer();
      optimiseTimer.Interval = TimeSpan.FromMilliseconds(30000);
      optimiseTimer.Tick += (s, e) => OptimiseSchedule();
      optimiseTimer.Start();

      // Check floating timer (poll registry for external toggle)
      checkFloatingTimer = new System.Windows.Threading.DispatcherTimer();
      checkFloatingTimer.Interval = TimeSpan.FromMilliseconds(2000);
      checkFloatingTimer.Tick += (s, e) => HandleFloatingBarToggle();
      checkFloatingTimer.Start();
    }

    static void OptimiseSchedule() {
      if (flagStart < 5) {
        flagStart++;
        if (ConfigService.FanControl.EndsWith("%")) {
          SetMaxFanSpeedOff();
          int pct = 100;
          int.TryParse(ConfigService.FanControl.TrimEnd('%'), out pct);
          SetFanLevel(pct, pct);
        } else if (ConfigService.FanControl.Contains("max")) {
          SetMaxFanSpeedOff();
          SetFanLevel(100, 100);
        } else if (ConfigService.FanControl == "" || ConfigService.FanControl == "auto") {
          SetMaxFanSpeedOff();
        } else if (ConfigService.FanControl == "smart" || ConfigService.FanControl == "custom") {
          var pts = FanService.LoadCustomCurve();
          if (pts.Count > 0) FanService.ApplyCustomCurve(pts);
        } else if (ConfigService.FanControl.Contains(" RPM")) {
          SetMaxFanSpeedOff();
          int rpmValue = int.Parse(ConfigService.FanControl.Replace(" RPM", "").Trim());
          SetFanLevel(rpmValue / 100, rpmValue / 100);
        }
      }
      GetFanCount();
      HardwareService.MonitorQuery();
    }

    // ══════════════════════════════════════════════════════
    // Restore Config (applied on startup)
    // ══════════════════════════════════════════════════════
    public static void RestoreConfig() {
      // ponytail: do NOT call ConfigService.Load() here on startup — it re-reads STALE
      // registry values that predate the preset apply, overwriting preset values.
      // App.xaml.cs already called ConfigService.Load() + SwitchPreset before us.
      // Only reload on power-resume (countRestore path), where fresh values are needed.
      if (countRestore == 0) {
        // startup path — ConfigService already loaded and preset applied by App.xaml.cs
      } else {
        ConfigService.Load();
      }

      // 重新应用预设，确保 ConfigService 字段反映预设值而非陈旧注册表独立值
      if (!string.IsNullOrEmpty(ConfigService.Preset)) {
        PresetManager.SwitchPreset(ConfigService.Preset);
      }

      // Fan table
      if (ConfigService.FanTable.Contains("cool")) {
        FanService.LoadFanConfig("cool.txt");
        UpdateCheckedState("fanTableGroup", "降温模式");
      } else {
        FanService.LoadFanConfig("silent.txt");
        UpdateCheckedState("fanTableGroup", "安静模式");
      }

      // Fan mode
      if (ConfigService.FanMode.Contains("performance")) {
        SetFanMode((byte)0x31);
        UpdateCheckedState("fanModeGroup", "狂暴模式");
      } else if (ConfigService.FanMode.Contains("default")) {
        SetFanMode((byte)0x30);
        UpdateCheckedState("fanModeGroup", "平衡模式");
      }

      // Fan control
      if (ConfigService.FanControl == "" || ConfigService.FanControl == "auto") {
        SetMaxFanSpeedOff();
        if (fanControlTimer != null) fanControlTimer.Change(0, 1000);
        UpdateCheckedState("fanControlGroup", ConfigService.FanTable == "cool" ? "降温模式" : "安静模式");
      } else if (ConfigService.FanControl == "smart" || ConfigService.FanControl == "custom") {
        string curveFile = ConfigService.FanTable == "cool" ? "cool.txt" : "silent.txt";
        FanService.LoadFanConfig(curveFile);
        FanService.InitSmartFanState(ConfigService.SmartFanEmaAlpha);
        SetMaxFanSpeedOff();
        string preset = ConfigService.Preset;
        if (PresetManager.IsCustom(preset)) {
          var cpuPts = FanService.LoadPresetCurve(preset, false);
          var gpuPts = FanService.LoadPresetCurve(preset, true);
          if (cpuPts.Count > 0) FanService.ApplyCustomCurve(cpuPts);
          if (gpuPts.Count > 0) FanService.ApplyCustomCurveGPU(gpuPts);
        } else {
          var pts = FanService.LoadCustomCurve();
          if (pts.Count > 0) FanService.ApplyCustomCurve(pts);
        }
        if (fanControlTimer != null) fanControlTimer.Change(0, 1000);
        UpdateCheckedState("fanControlGroup", "智能自定义曲线");
      } else if (ConfigService.FanControl.EndsWith("%")) {
        SetMaxFanSpeedOff();
        if (fanControlTimer != null) fanControlTimer.Change(Timeout.Infinite, Timeout.Infinite);
        int pct = int.Parse(ConfigService.FanControl.TrimEnd('%'));
        SetFanLevel(pct, pct);
        UpdateCheckedState("fanControlGroup", ConfigService.FanControl);
      } else if (ConfigService.FanControl.Contains("max")) {
        SetMaxFanSpeedOff();
        SetFanLevel(100, 100);
        if (fanControlTimer != null) fanControlTimer.Change(Timeout.Infinite, Timeout.Infinite);
        UpdateCheckedState("fanControlGroup", "手动: 100%");
      } else if (ConfigService.FanControl.Contains(" RPM")) {
        SetMaxFanSpeedOff();
        if (fanControlTimer != null) fanControlTimer.Change(Timeout.Infinite, Timeout.Infinite);
        int rpmValue = int.Parse(ConfigService.FanControl.Replace(" RPM", "").Trim());
        SetFanLevel(rpmValue / 100, rpmValue / 100);
        UpdateCheckedState("fanControlGroup", ConfigService.FanControl);
      }

      // Temp sensitivity
      switch (ConfigService.TempSensitivity) {
        case "realtime": HardwareService.RespondSpeed = 1; UpdateCheckedState("tempSensitivityGroup", "实时"); break;
        case "high": HardwareService.RespondSpeed = 0.4f; UpdateCheckedState("tempSensitivityGroup", "高"); break;
        case "medium": HardwareService.RespondSpeed = 0.1f; UpdateCheckedState("tempSensitivityGroup", "中"); break;
        case "low": HardwareService.RespondSpeed = 0.04f; UpdateCheckedState("tempSensitivityGroup", "低"); break;
      }

      // CPU power (WMI-based, no SDK dependency)
      if (ConfigService.CpuPower == "null") {
        // nothing — keep current BIOS limit
      } else if (ConfigService.CpuPower == "max") {
        SetCpuPowerLimit(254);
        UpdateCheckedState("cpuPowerGroup", "最大");
      } else if (ConfigService.CpuPower.Contains(" W")) {
        int value = int.Parse(ConfigService.CpuPower.Replace(" W", "").Trim());
        if (value >= 10 && value <= 254) {
          SetCpuPowerLimit((byte)value);
          UpdateCheckedState("cpuPowerGroup", ConfigService.CpuPower);
        }
      }

      // GPU power
      switch (ConfigService.GpuPower) {
        case "max": SetMaxGpuPower(); UpdateCheckedState("gpuPowerGroup", "CTGP开+DB开"); break;
        case "med": SetMedGpuPower(); UpdateCheckedState("gpuPowerGroup", "CTGP开+DB关"); break;
        case "min": SetMinGpuPower(); UpdateCheckedState("gpuPowerGroup", "CTGP关+DB关"); break;
      }

      // GPU clock
      if (SetGPUClockLimit(ConfigService.GpuClock)) {
        UpdateCheckedState("gpuClockGroup", ConfigService.GpuClock + " MHz");
      } else {
        UpdateCheckedState("gpuClockGroup", "还原");
      }

      // Max frame rate
      int[] frRates = { 0, 30, 60, 90, 120, 144, 165, 240, 300, 360, 480, 1000 };
      int frIdx = Array.IndexOf(frRates, ConfigService.MaxFrameRate);
      if (frIdx > 0 && frIdx < frRates.Length) {
        HP.Omen.Core.Common.NVidiaApi.NvApiWrapper.NVAPI_SetMaxFrameRate(frRates[frIdx]);
      } else {
        HP.Omen.Core.Common.NVidiaApi.NvApiWrapper.NVAPI_SetMaxFrameRate(0);
      }

      // DB version
      switch (ConfigService.DBVersion) {
        case 1:
          SetFanMode((byte)0x31);
          SetMaxGpuPower();
          SetCpuPowerLimit((byte)CPULimitDB);
          countDB = countDBInit;
          UpdateCheckedState("DBGroup", "解锁版本");
          break;
        case 2:
          string deviceId = "\"ACPI\\\\NVDA0820\\\\NPCF\"";
          string command = $"pnputil /enable-device {deviceId}";
          ExecuteCommand(command);
          UpdateCheckedState("DBGroup", "普通版本");
          break;
      }

      // Refresh rate — restore saved display refresh rate
      if (ConfigService.RefreshRate > 0)
        ApplyRefreshRate(ConfigService.RefreshRate);

      // Power plan — restore saved Windows power plan
      if (!string.IsNullOrEmpty(ConfigService.PowerPlanGuid)) {
        try {
          Guid g = Guid.Parse(ConfigService.PowerPlanGuid);
          NativeMethods.PowerSetActiveScheme(IntPtr.Zero, ref g);
        } catch { }
      }

      // Power mode overlay — restore saved Windows power mode
      try {
        Guid pmGuid;
        if (ConfigService.PowerMode == 0) pmGuid = NativeMethods.BEST_POWER_EFFICIENCY;
        else if (ConfigService.PowerMode == 2) pmGuid = NativeMethods.BEST_PERFORMANCE;
        else pmGuid = Guid.Empty;
        NativeMethods.PowerSetActiveOverlayScheme(pmGuid);
      } catch { }

      // EcoQoS — restore saved state
      try {
        EcoQosService.SetEnabled(ConfigService.EcoQosEnabled);
        EcoQosService.SetThrottlePlugged(ConfigService.EcoQosThrottlePlugged);
      } catch { }

      // GPU overclock — restore saved clock offsets
      if (ConfigService.GpuCoreOverclock > 0)
        System.Threading.ThreadPool.QueueUserWorkItem(_ => { try { GpuAppManager.SetCoreClockOffset(ConfigService.GpuCoreOverclock); } catch { } });
      if (ConfigService.GpuMemoryOverclock > 0)
        System.Threading.ThreadPool.QueueUserWorkItem(_ => { try { GpuAppManager.SetMemoryClockOffset(ConfigService.GpuMemoryOverclock); } catch { } });

      // Auto start
      if (ConfigService.AutoStart == "on") {
        AutoStartEnable();
        UpdateCheckedState("autoStartGroup", "开启");
      } else {
        UpdateCheckedState("autoStartGroup", "关闭");
      }

      // Icon
      ApplyIconStyle();
      switch (ConfigService.CustomIcon) {
        case "original": UpdateCheckedState("customIconGroup", "原版"); break;
        case "custom": UpdateCheckedState("customIconGroup", "自定义图标"); break;
        case "dynamic": UpdateCheckedState("customIconGroup", "动态图标"); break;
      }

      // Omen key
      switch (ConfigService.OmenKey) {
        case "custom":
          if (checkFloatingTimer != null) checkFloatingTimer.IsEnabled = true;
          OmenKeyOff();
          OmenKeyOn(ConfigService.OmenKey);
          UpdateCheckedState("omenKeyGroup", "切换浮窗显示");
          break;
        case "showMain":
          if (checkFloatingTimer != null) checkFloatingTimer.IsEnabled = false;
          OmenKeyOff();
          OmenKeyOn(ConfigService.OmenKey);
          UpdateCheckedState("omenKeyGroup", "显示主界面");
          break;
        case "cyclePresets":
          if (checkFloatingTimer != null) checkFloatingTimer.IsEnabled = false;
          OmenKeyOff();
          OmenKeyOn(ConfigService.OmenKey);
          UpdateCheckedState("omenKeyGroup", "循环预设");
          break;
        case "app":
          if (checkFloatingTimer != null) checkFloatingTimer.IsEnabled = true;
          OmenKeyOff();
          OmenKeyOn(ConfigService.OmenKey);
          UpdateCheckedState("omenKeyGroup", "打开应用");
          break;
        case "none":
          if (checkFloatingTimer != null) checkFloatingTimer.IsEnabled = false;
          OmenKeyOff();
          UpdateCheckedState("omenKeyGroup", "取消绑定");
          break;
      }

      // CPU monitor
      HardwareService.MonitorCPU = ConfigService.MonitorCPU;
      HardwareService.LibreComputer.IsCpuEnabled = ConfigService.MonitorCPU;

      // GPU monitor
      if (ConfigService.MonitorGPU) {
        HardwareService.LibreComputer.IsGpuEnabled = true;
        HardwareService.MonitorGPU = true;
        UpdateCheckedState("monitorGPUGroup", "开启GPU监控");
      } else {
        HardwareService.LibreComputer.IsGpuEnabled = false;
        HardwareService.MonitorGPU = false;
        UpdateCheckedState("monitorGPUGroup", "关闭GPU监控");
      }

      // Fan monitor
      HardwareService.MonitorFan = ConfigService.MonitorFan;
      if (ConfigService.MonitorFan) {
        UpdateCheckedState("monitorFanGroup", "开启风扇监控");
      } else {
        UpdateCheckedState("monitorFanGroup", "关闭风扇监控");
      }

      // Floating bar size
      Views.FloatingWindow.UpdateAllText();
      switch (ConfigService.TextSize) {
        case 24: UpdateCheckedState("floatingBarSizeGroup", "24点"); break;
        case 36: UpdateCheckedState("floatingBarSizeGroup", "36点"); break;
        case 48: UpdateCheckedState("floatingBarSizeGroup", "48点"); break;
      }

      // Floating bar loc
      if (ConfigService.FloatingBarLoc == "left") {
        UpdateCheckedState("floatingBarLocGroup", "左上角");
      } else {
        UpdateCheckedState("floatingBarLocGroup", "右上角");
      }

      // Floating bar on/off
      if (ConfigService.FloatingBar == "on") {
        Views.FloatingWindow.ShowInstances();
        UpdateCheckedState("floatingBarGroup", "显示浮窗");
      } else {
        Views.FloatingWindow.CloseAll();
        UpdateCheckedState("floatingBarGroup", "关闭浮窗");
      }
    }

    // ══════════════════════════════════════════════════════
    // Power change handler
    // ══════════════════════════════════════════════════════
    public static void OnPowerChange(object s, Microsoft.Win32.PowerModeChangedEventArgs e) {
      if (e.Mode == Microsoft.Win32.PowerModes.Resume) {
        SendOmenBiosWmi(0x10, new byte[] { 0x00, 0x00, 0x00, 0x00 }, 4);
        tooltipUpdateTimer.Start();
        countRestore = 3;
      }

      if (e.Mode == Microsoft.Win32.PowerModes.StatusChange) {
        var powerStatus = SystemInformation.PowerStatus;
        bool wasOffline = !HardwareService.PowerOnline;
        HardwareService.PowerOnline = powerStatus.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online;
        if (wasOffline != HardwareService.PowerOnline) {
          Views.OsdWindow.ShowPowerOsd(HardwareService.PowerOnline);
        }
        // Master: restore power config when AC adapter is plugged back in
        if (wasOffline && HardwareService.PowerOnline) {
          RestorePowerConfig();
        }
      }
    }

    // ══════════════════════════════════════════════════════
    // Restore Power Config (re-applies on AC plug-in)
    // ══════════════════════════════════════════════════════
    public static void RestorePowerConfig() {
      SetFanMode((byte)0x31); // Unleash mode
      System.Threading.Tasks.Task.Delay(1000).ContinueWith(_ => {
        RestoreCPUPower();
        SetGpuPowerState(ConfigService.TgpEnabled, ConfigService.PpabEnabled,
            ConfigService.DState == 2 ? 2 : 1);
        if (ConfigService.Tpp >= 20 && ConfigService.Tpp <= 254) {
          SetConcurrentTdp((byte)ConfigService.Tpp);
        }
      });
    }

    internal static void ApplyRefreshRate(int hz) {
      var dm = new NativeMethods.DEVMODE();
      dm.dmSize = (short)System.Runtime.InteropServices.Marshal.SizeOf(dm);
      NativeMethods.EnumDisplaySettings(null, NativeMethods.ENUM_CURRENT_SETTINGS, ref dm);
      dm.dmDisplayFrequency = hz;
      dm.dmFields = NativeMethods.DM_DISPLAYFREQUENCY | NativeMethods.DM_PELSWIDTH | NativeMethods.DM_PELSHEIGHT;
      NativeMethods.ChangeDisplaySettings(ref dm, NativeMethods.CDS_UPDATEREGISTRY);
    }

    // ══════════════════════════════════════════════════════
    // Helper methods (kept from original Program.cs)
    // ══════════════════════════════════════════════════════
    public static void RestoreCPUPower() {
      if (ConfigService.CpuPower == "max") {
        SetCpuPowerLimit(254);
      } else if (ConfigService.CpuPower.Contains(" W")) {
        int value = int.Parse(ConfigService.CpuPower.Replace(" W", "").Trim());
        if (value >= 10 && value <= 254) {
          SetCpuPowerLimit((byte)value);
        }
      }
    }

    static bool CheckCustomIcon() {
      string currentPath = AppDomain.CurrentDomain.BaseDirectory;
      string iconPath = Path.Combine(currentPath, "custom.ico");
      if (File.Exists(iconPath)) {
        return true;
      } else {
        DialogHelper.Warn("不存在自定义图标custom.ico", "提示");
        return false;
      }
    }

    public static void SetCustomIcon() {
      string currentPath = AppDomain.CurrentDomain.BaseDirectory;
      string iconPath = Path.Combine(currentPath, "custom.ico");
      if (File.Exists(iconPath)) {
        var old = TrayIcon.Icon;
        TrayIcon.Icon = new Icon(iconPath);
        old?.Dispose();
      } else {
        DialogHelper.Warn("不存在自定义图标custom.ico", "提示");
      }
    }

    internal static void ApplyIconStyle() {
      switch (ConfigService.CustomIcon) {
        case "custom": SetCustomIcon(); break;
        case "dynamic": GenerateDynamicIcon((int)HardwareService.CPUTemp); break;
        default: SetDefaultLogoIcon(); break;
      }
      _trayHelperRef?.SetIcon(TrayIcon.Icon);
    }

    // ── Cached GDI objects for dynamic icon (avoid per-tick allocation) ──
    static Bitmap _dynamicIconBitmap;
    static Graphics _dynamicIconGraphics;
    static System.Drawing.Font _dynamicIconFont;

    public static void GenerateDynamicIcon(int number) {
      if (_dynamicIconBitmap == null) {
        _dynamicIconBitmap = new Bitmap(128, 128);
        _dynamicIconGraphics = Graphics.FromImage(_dynamicIconBitmap);
        _dynamicIconFont = new System.Drawing.Font("Arial", 52, System.Drawing.FontStyle.Bold);
        _dynamicIconGraphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
      }

      _dynamicIconGraphics.Clear(System.Drawing.Color.Transparent);
      string text = number.ToString("00");
      SizeF textSize = _dynamicIconGraphics.MeasureString(text, _dynamicIconFont);
      float x = (_dynamicIconBitmap.Width - textSize.Width) / 2;
      float y = (_dynamicIconBitmap.Height - textSize.Height) / 8;
      _dynamicIconGraphics.DrawString(text, _dynamicIconFont, System.Drawing.Brushes.Tan, new PointF(x, y));

      IntPtr hIcon = _dynamicIconBitmap.GetHicon();
      using (var temp = Icon.FromHandle(hIcon)) {
        TrayIcon.Icon = (Icon)temp.Clone();
      }
      DestroyIcon(hIcon);
    }

    public static Icon CreateLogoIcon(int size) {
      using (var bitmap = new Bitmap(size, size)) {
        using (Graphics g = Graphics.FromImage(bitmap)) {
          g.Clear(System.Drawing.Color.Transparent);
          g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
          float s = size / 100f;
          using (var brush = new System.Drawing.Drawing2D.LinearGradientBrush(
              new PointF(0, size * 0.79f), new PointF(size * 0.98f, size * 0.39f),
              System.Drawing.Color.Transparent, System.Drawing.Color.Transparent)) {
            brush.InterpolationColors = new System.Drawing.Drawing2D.ColorBlend {
              Colors = new[] { System.Drawing.Color.FromArgb(0xFF, 0x55, 0xE1), System.Drawing.Color.FromArgb(0xFF, 0x04, 0x02), System.Drawing.Color.FromArgb(0xFF, 0xB4, 0x02) },
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

    public static void SetDefaultLogoIcon() {
      var old = TrayIcon.Icon;
      TrayIcon.Icon = LoadLogoIcon();
      old?.Dispose();
    }

    public static bool CheckDBVersion(int version) {
      return GpuAppManager.CheckDBVersion(version);
    }

    static string GetNVIDIAModel() {
      var result = ExecuteCommand("nvidia-smi --query-gpu=name --format=csv");
      if (result.ExitCode == 0) {
        string pattern = @"\b(\d[\w\d\-]*)\b";
        var match = Regex.Match(result.Output, pattern);
        if (match.Success) return match.Groups[1].Value;
      }
      return null;
    }

    public static bool SetGPUClockLimit(int freq) {
      if (freq < 210) {
        ExecuteCommand("nvidia-smi --reset-gpu-clocks");
        return false;
      } else {
        ExecuteCommand("nvidia-smi --lock-gpu-clocks=0," + freq);
        return true;
      }
    }

    static float GPUPowerLimits() {
      var result = ExecuteCommand("nvidia-smi --query-gpu=power.limit --format=csv,noheader,nounits");
      if (result.ExitCode == 0) {
        if (float.TryParse(result.Output.Trim(), out float limit)) {
          var result2 = ExecuteCommand("nvidia-smi --query-gpu=power.max_limit --format=csv,noheader,nounits");
          if (result2.ExitCode == 0) {
            if (float.TryParse(result2.Output.Trim(), out float maxLimit)) {
              if (Math.Abs(limit - maxLimit) > 1)
                return limit;
              else
                return -maxLimit;
            }
          }
        }
      }
      return -1;
    }

    public static void ChangeDBVersion(int version) {
      // Same as original
      string tempDir = Path.GetTempPath();
      string catPath = Path.Combine(tempDir, "nvpcf_cat.CAT");
      string infPath = Path.Combine(tempDir, "nvpcf_inf.inf");
      string sysPath = Path.Combine(tempDir, "nvpcf_sys.sys");

      ExtractResourceToFile("OmenSuperHub.Resources.nvpcf_cat.CAT", catPath);
      ExtractResourceToFile("OmenSuperHub.Resources.nvpcf_inf.inf", infPath);
      ExtractResourceToFile("OmenSuperHub.Resources.nvpcf_sys.sys", sysPath);

      string deviceId = "\"ACPI\\\\NVDA0820\\\\NPCF\"";

      if (version == 1) {
        // Disable existing, install custom
        string installCommand = $"pnputil /add-driver \"{infPath}\" /install /force";
        ExecuteCommand(installCommand);

        string disableCommand = $"pnputil /disable-device {deviceId}";
        ExecuteCommand(disableCommand);

        string enableCommand = $"pnputil /enable-device {deviceId}";
        ExecuteCommand(enableCommand);
      }

      DeleteExtractedFiles(catPath, infPath, sysPath);
    }

    static void ExtractResourceToFile(string resourceName, string filePath) {
      using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)) {
        if (stream != null) {
          using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write)) {
            stream.CopyTo(fileStream);
          }
        }
      }
    }

    static void DeleteExtractedFiles(params string[] paths) {
      foreach (var path in paths) {
        if (File.Exists(path)) File.Delete(path);
      }
    }

    // Task Scheduler
    public static void AutoStartEnable() {
      string currentPath = AppDomain.CurrentDomain.BaseDirectory;
      using (TaskService ts = new TaskService()) {
        TaskDefinition td = ts.NewTask();
        td.RegistrationInfo.Description = "Start OMEN X Hub with admin rights";
        td.Principal.RunLevel = TaskRunLevel.Highest;
        td.Actions.Add(new ExecAction(Path.Combine(currentPath, "OmenXHub.exe"), "--tray", null));
        LogonTrigger logonTrigger = new LogonTrigger();
        td.Triggers.Add(logonTrigger);
        td.Settings.DisallowStartIfOnBatteries = false;
        td.Settings.StopIfGoingOnBatteries = false;
        td.Settings.ExecutionTimeLimit = TimeSpan.Zero;
        td.Settings.AllowHardTerminate = false;
        ts.RootFolder.RegisterTaskDefinition(@"OmenXHub", td);
      }
      CleanUpAndRemoveTasks();
    }

    public static void AutoStartDisable() {
      using (TaskService ts = new TaskService()) {
        var existingTask = ts.FindTask("OmenXHub");
        if (existingTask != null) {
          ts.RootFolder.DeleteTask("OmenXHub");
        }
      }
    }

    static void CleanUpAndRemoveTasks() {
      string targetFolder = @"C:\Program Files\OmenXHub";
      string taskName = "Omen Boot";
      string file1 = @"C:\Windows\SysWOW64\silent.txt";
      string file2 = @"C:\Windows\SysWOW64\cool.txt";

      if (Directory.Exists(targetFolder)) ExecuteCommand($"rd /s /q \"{targetFolder}\"");
      if (File.Exists(file1)) ExecuteCommand($"del /f /q \"{file1}\"");
      if (File.Exists(file2)) ExecuteCommand($"del /f /q \"{file2}\"");

      var taskQueryResult = ExecuteCommand($"schtasks /query /tn \"{taskName}\"");
      if (taskQueryResult.ExitCode == 0) {
        ExecuteCommand($"schtasks /delete /tn \"{taskName}\" /f");
      }

      ExecuteCommand(@"reg delete ""HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run"" /v ""OmenXHub"" /f");
    }

    // ══════════════════════════════════════════════════════
    // Process Execution
    // ══════════════════════════════════════════════════════
    public class ProcessResult {
      public string Output;
      public string Error;
      public int ExitCode;
    }

    private static readonly HashSet<string> _allowedCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
      "nvidia-smi", "pnputil", "sc", "schtasks", "rd", "del", "reg", "cmd"
    };

    private static bool IsCommandSafe(string command) {
      string trimmed = command.TrimStart();
      if (trimmed.StartsWith("\"")) {
        int end = trimmed.IndexOf("\"", 1);
        if (end < 0) return false;
      }
      string exe = trimmed.Contains(" ") ? trimmed.Substring(0, trimmed.IndexOf(" ")) : trimmed;
      exe = exe.Trim('"');
      string exeName = System.IO.Path.GetFileName(exe);
      if (!_allowedCommands.Contains(exeName)) return false;
      if (command.Contains("&") || command.Contains("|") || command.Contains(";") ||
          command.Contains("`") || command.Contains("$(") || command.Contains("\n") || command.Contains("\r"))
        return false;
      return true;
    }

    public static ProcessResult ExecuteCommand(string command) {
      if (!IsCommandSafe(command)) {
        Logger.Error($"TrayService: blocked unsafe command: {command}");
        return new ProcessResult { Output = "", Error = "Blocked: unsafe command", ExitCode = -1 };
      }
      string trimmed = command.TrimStart();
      string exe = trimmed.Contains(" ") ? trimmed.Substring(0, trimmed.IndexOf(" ")) : trimmed;
      string args = trimmed.Contains(" ") ? trimmed.Substring(trimmed.IndexOf(" ") + 1) : "";
      exe = exe.Trim('"');
      if (exe.Equals("cmd", StringComparison.OrdinalIgnoreCase) || exe.EndsWith("\\cmd.exe", StringComparison.OrdinalIgnoreCase)) {
        var psi = new ProcessStartInfo {
          FileName = "cmd.exe",
          Arguments = args,
          RedirectStandardOutput = true,
          RedirectStandardError = true,
          UseShellExecute = false,
          CreateNoWindow = true
        };
        using (var process = new Process { StartInfo = psi }) {
          process.Start();
          string output = process.StandardOutput.ReadToEnd();
          string error = process.StandardError.ReadToEnd();
          process.WaitForExit();
          return new ProcessResult { Output = output, Error = error, ExitCode = process.ExitCode };
        }
      }
      var psi2 = new ProcessStartInfo {
        FileName = exe,
        Arguments = args,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
      };
      using (var process = new Process { StartInfo = psi2 }) {
        process.Start();
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new ProcessResult { Output = output, Error = error, ExitCode = process.ExitCode };
      }
    }

    // Named pipe for Omen Key
    static int _cyclePresetIndex = -1;
    static CancellationTokenSource _omenKeyCts;

    public static void GetOmenKeyTask() {
      _omenKeyCts?.Cancel();
      _omenKeyCts = new CancellationTokenSource();
      var token = _omenKeyCts.Token;
      System.Threading.Tasks.Task.Run(async () => {
        while (!token.IsCancellationRequested) {
          try {
            using (var pipeServer = new System.IO.Pipes.NamedPipeServerStream("OmenXHubPipe", System.IO.Pipes.PipeDirection.In)) {
              pipeServer.WaitForConnection();
              using (var reader = new StreamReader(pipeServer)) {
                string message = reader.ReadToEnd();
                if (message.Contains("OmenKeyTriggered")) {
                  if (ConfigService.OmenKey == "showMain") {
                    System.Windows.Application.Current?.Dispatcher.Invoke(() => Views.MainWindow.ShowInstance());
                  } else if (ConfigService.OmenKey == "cyclePresets") {
                    var candidates = ConfigService.OmenKeyPresetCandidates
                      .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                      .Distinct().ToList();
                    if (candidates.Count == 0) continue;
                    int idx = Math.Max(0, candidates.IndexOf(ConfigService.Preset));
                    _cyclePresetIndex = (idx + 1) % candidates.Count;
                    string preset = candidates[_cyclePresetIndex];
                    System.Windows.Application.Current?.Dispatcher.Invoke(() => {
                      PresetManager.SwitchPreset(preset);
                      if (System.Windows.Application.Current.MainWindow is Views.MainWindow mw)
                        mw.ApplyPresetHardware();
                      Views.OsdWindow.ShowPresetOsd(preset);
                      ConfigService.FirePresetCycled(preset);
                    });
                  } else if (ConfigService.OmenKey == "app") {
                    LaunchOmenKeyApp();
                  } else if (ConfigService.OmenKey == "custom") {
                    if (!checkFloating) checkFloating = true;
                  }
                  // "none" does nothing
                }
              }
            }
          } catch (Exception ex) {
            Logger.Error("OmenKey pipe error: " + ex.Message);
            await System.Threading.Tasks.Task.Delay(1000);
          }
        }
      });
    }

    static void LaunchOmenKeyApp() {
      string path = ConfigService.OmenKeyAppPath;
      if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path)) {
        Logger.Error("Omen key app not found: " + path);
        return;
      }
      try {
        var psi = new System.Diagnostics.ProcessStartInfo {
          FileName = path,
          UseShellExecute = true,
          WorkingDirectory = System.IO.Path.GetDirectoryName(path)
        };
        System.Diagnostics.Process.Start(psi)?.Dispose();
      } catch (Exception ex) {
        Logger.Error("LaunchOmenKeyApp error: " + ex.Message);
      }
    }

    // ══════════════════════════════════════════════════════
    // Exit
    // ══════════════════════════════════════════════════════
    public static void Exit() {
      if (wpfContextMenu != null)
        wpfContextMenu.IsOpen = false;
      AutomationProcessor.Stop();
      try { MacroController.Stop(); } catch { }
      _omenKeyCts?.Cancel();
      _omenKeyCts?.Dispose();
      _omenKeyCts = null;
      if (ConfigService.OmenKey == "custom" || ConfigService.OmenKey == "showMain" || ConfigService.OmenKey == "cyclePresets" || ConfigService.OmenKey == "app") {
        OmenKeyOff();
      }
      tooltipUpdateTimer?.Stop();
      tooltipUpdateTimer?.Dispose();
      fanControlTimer?.Dispose();
      optimiseTimer?.Stop();
      checkFloatingTimer?.Stop();
      HardwareService.Close();
      try { AmdSmuService.Shutdown(); } catch { }
      try {
        TrayIcon.Hide();
        TrayIcon.Dispose();
      } catch { }
      try {
        _trayHelperRef?.Dispose();
      } catch { }
      // Schedule shutdown after current event completes to avoid re-entrancy
      Views.MainWindow._allowClose = true;
      var app = System.Windows.Application.Current;
      if (app != null) {
        app.Dispatcher.BeginInvoke(new System.Action(() => {
          try { app.Shutdown(); } catch { }
          Environment.Exit(0);
        }), System.Windows.Threading.DispatcherPriority.Normal);
      } else {
        Environment.Exit(0);
      }
    }
  }

  static class NativeMethods {
    public const int ENUM_CURRENT_SETTINGS = -1;
    public const int DM_DISPLAYFREQUENCY = 0x400000;
    public const int DM_PELSWIDTH = 0x80000;
    public const int DM_PELSHEIGHT = 0x100000;
    public const int CDS_UPDATEREGISTRY = 0x00000001;

    // Power plan P/Invoke for startup restore
    public static readonly Guid BEST_POWER_EFFICIENCY = Guid.Parse("961cc777-2547-4f9d-8174-7d86181b8a7a");
    public static readonly Guid BEST_PERFORMANCE = Guid.Parse("ded574b5-45a0-4f42-8737-46345c09c238");

    [System.Runtime.InteropServices.DllImport("powrprof.dll")]
    public static extern uint PowerSetActiveScheme(IntPtr userPowerKey, ref Guid activePolicyGuid);

    [System.Runtime.InteropServices.DllImport("powrprof.dll")]
    public static extern uint PowerSetActiveOverlayScheme(Guid overlaySchemeGuid);

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
}
