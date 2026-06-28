// ConfigService.cs - 配置管理服务
// 100+ 静态配置字段，Windows 注册表持久化 (HKCU\Software\OmenXHub)，预设保存/加载
using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace OmenSuperHub.Services {
  internal static class ConfigService {
    private const string RegistryPath = @"Software\OmenXHub";

    // Fired when Omen key cycles to a new preset (from background thread)
    public static event Action<string> OnPresetCycled;
    public static void FirePresetCycled(string preset) => OnPresetCycled?.Invoke(preset);

    // ═══════════════════════════════════════════════════════
    // Configuration State
    // ═══════════════════════════════════════════════════════
    public static string FanTable = "silent";
    public static string FanMode = "performance";
    public static string FanControl = "auto";
    public static string TempSensitivity = "medium";
    public static string CpuPower = "max";
    public static string GpuPower = "max";
    public static int GpuClock = 0;
    public static int DBVersion = 2;
    public static string AutoStart = "off";
    public static int AlreadyRead = 0;
    public static string CustomIcon = "original";
    public static string OmenKey = "none";
    public static string OmenKeyAppPath = "";
    public static string OmenKeyPresetCandidates = "LightUse;GpuPriority;Extreme;Custom1;Custom2;Custom3";
    public static bool MonitorGPU = true;
    public static bool MonitorFan = true;
    public static int TextSize = 48;
    public static string FloatingBarLoc = "left";
    public static string FloatingBarLayout = "row";
    public static string FloatingBarScreen = "";
    public static string FloatingBar = "off";
    public static double FloatingPosLeft = 100;
    public static double FloatingPosTop = 100;

    // New features from OmenSuperHub-master merge
    public static string Preset = "GpuPriority";
    public static string Language = "SimplifiedChinese";
    public static string DataLocalize = "off";
    public static string LightingDevice = "keyboard";
    public static string LightingInterface = "BasicFourZone";
    public static byte LightingBrightness = 100;
    public static string LightingColor = "Red";
    public static string LightingAnimation = "None";
    public static string DisplayMode = "smoothed";
    public static int MonRefreshInterval = 1000;

    public static int IccMax = 0;
    public static int AcLoadLine = 0;
    public static int Tpp = 0;
    public static int DState = 1;
    public static bool TgpEnabled = true;
    public static bool PpabEnabled = true;
    public static string CustomPreset1Name = "Custom 1";
    public static string CustomPreset2Name = "Custom 2";
    public static string CustomPreset3Name = "Custom 3";
    public static double FloatingOpacity = 0.85;
    public static double FloatingTextOpacity = 1.0;
    public static bool VerboseLogging = false;

    // Hetero CPU (AMD dual-CCD simulated hybrid scheduling)
    public static string HeteroCpuSmallMask = "FFFF0000";
    public static int HeteroCpuDefaultPolicy = 2;
    public static int HeteroCpuExpectedRuntime = 1450;
    public static int HeteroCpuImportantPolicy = 2;
    public static int HeteroCpuImportantShortPolicy = 3;
    public static int HeteroCpuPolicyMask = 7;
    public static int HeteroCpuImportantPriority = 8;
    public static string AutoFanProtect = "on";
    public static string CustomLogoPath = "";
    public static string CustomBgPath = "";
    public static double CustomBgOpacity = 0.5;
    public static bool CustomBgBlurEnabled = true;
    public static string GpuPowerTgp = "on";
    public static string GpuPowerPpab = "on";
    public static string GpuPowerDState = "normal";
    public static string GpuPowerTpp = "null";
    public static string GpuPowerIccMax = "null";
    public static string GpuPowerAcLoadLine = "null";
    public static string CpuPowerValue = "null";
    public static int CpuPowerPl1 = -1;
    public static int CpuPowerPl2 = -1;
    public static int GpuCoreOverclock = -1;
    public static int GpuMemoryOverclock = -1;
    public static int MaxFrameRate = -1;
    public static int RefreshRate = 0;
    public static string PowerPlanGuid = "";
    public static int PowerMode = 1;
    public static bool MonitorCPU = true;
    public static string Theme = "system";
    public static string AccentColorSource = "system";
    public static string AccentColor = "#FFFFFFFF";
    public static bool Topmost = true;
    public static bool ShowOsd = true;
    public static bool ShowLockKeys = true;
    public static bool EcoQosEnabled = false;
    public static bool EcoQosThrottlePlugged = false;
    public static string EcoQosWhitelist = "";
    public static string EcoQosBlacklist = "";
    public static int DisableDynamicBoost = 0;
    public static bool BatteryChargeLimit = false;
    public static bool BatteryWmiUnsupported = false;
    public static bool HWiNFOEnabled = false;
    public static bool HttpApiEnabled = false;
    public static bool AutomationEnabled = true;
    public static bool MacroEnabled = true;
    public static bool FanSync = false;
    public static float SmartFanEmaAlpha = 0.3f;
    public static int SmartFanStepDownRate = 500;
    public static float SmartFanHysteresis = 0.5f;

    // Cached machine info (no WMI re-query on each SysInfo refresh)
    public static string SysManufacturer = "";
    public static string SysModel = "";
    public static string SysBios = "";
    public static string SysCpu = "";
    public static string SysGpu = "";
    public static int SysAdapterPower = 0;
    public static string SysProductName = "";
    public static string SysBoardProduct = "";
    public static int SysCpuTjmax = 100;
    public static int SysNvidiaTjmax = 0;
    public static string SysNvidiaPowerMin = "";
    public static string SysNvidiaPowerMax = "";
    public static string SysKbType = "";
    public static int SysValidation = 0; // 0=unknown, 1=unsupported, 2=gaming
    public static int SysKbRaw = 0;
    public static string SysPawnIoText = "";

    // ═══════════════════════════════════════════════════════
    // Save Configuration
    // ═══════════════════════════════════════════════════════
    public static void BatchSave(Dictionary<string, object> updates) {
      if (updates == null || updates.Count == 0) return;
      try {
        using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryPath)) {
          if (key == null) return;
          foreach (var kv in updates) {
            key.SetValue(kv.Key, kv.Value);
          }
        }
      } catch (Exception ex) {
        Console.WriteLine($"Error batch saving configuration: {ex.Message}");
      }
    }

    public static void Save(string setting = null) {
      try {
        using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryPath)) {
          if (key == null) return;
          if (string.IsNullOrEmpty(setting)) {
            key.SetValue("Preset", Preset);
            key.SetValue("ShowOsd", ShowOsd);
            key.SetValue("ShowLockKeys", ShowLockKeys);
            key.SetValue("Topmost", Topmost);
            key.SetValue("SysManufacturer", SysManufacturer);
            key.SetValue("SysModel", SysModel);
            key.SetValue("SysBios", SysBios);
            key.SetValue("SysCpu", SysCpu);
            key.SetValue("SysGpu", SysGpu);
            key.SetValue("SysAdapterPower", SysAdapterPower);
            key.SetValue("SysProductName", SysProductName);
            key.SetValue("SysBoardProduct", SysBoardProduct);
            key.SetValue("SysCpuTjmax", SysCpuTjmax);
            key.SetValue("SysNvidiaTjmax", SysNvidiaTjmax);
            key.SetValue("SysNvidiaPowerMin", SysNvidiaPowerMin);
            key.SetValue("SysNvidiaPowerMax", SysNvidiaPowerMax);
            key.SetValue("SysKbType", SysKbType);
            key.SetValue("SysValidation", SysValidation);
            key.SetValue("SysPawnIoText", SysPawnIoText);
            key.SetValue("CustomLogoPath", CustomLogoPath);
            key.SetValue("CustomBgPath", CustomBgPath);
            key.SetValue("CustomBgOpacity", CustomBgOpacity);
            key.SetValue("CustomBgBlurEnabled", CustomBgBlurEnabled ? 1 : 0);
            return;
          }
          switch (setting) {
            case "FanTable": key.SetValue("FanTable", FanTable); break;
            case "FanMode": key.SetValue("FanMode", FanMode); break;
            case "FanControl": key.SetValue("FanControl", FanControl); break;
            case "TempSensitivity": key.SetValue("TempSensitivity", TempSensitivity); break;
            case "CpuPower": key.SetValue("CpuPower", CpuPower); break;
            case "GpuPower": key.SetValue("GpuPower", GpuPower); break;
            case "GpuClock": key.SetValue("GpuClock", GpuClock); break;
            case "DBVersion": key.SetValue("DBVersion", DBVersion); break;
            case "AutoStart": key.SetValue("AutoStart", AutoStart); break;
            case "AlreadyRead": key.SetValue("AlreadyRead", AlreadyRead); break;
            case "CustomIcon": key.SetValue("CustomIcon", CustomIcon); break;
            case "OmenKey": key.SetValue("OmenKey", OmenKey); break;
            case "OmenKeyAppPath": key.SetValue("OmenKeyAppPath", OmenKeyAppPath); break;
            case "OmenKeyPresetCandidates": key.SetValue("OmenKeyPresetCandidates", OmenKeyPresetCandidates); break;
            case "MonitorGPU": key.SetValue("MonitorGPU", MonitorGPU); break;
            case "MonitorFan": key.SetValue("MonitorFan", MonitorFan); break;
            case "FloatingBarSize": key.SetValue("FloatingBarSize", TextSize); break;
            case "FloatingBarLoc": key.SetValue("FloatingBarLoc", FloatingBarLoc); break;
            case "FloatingBarScreen": key.SetValue("FloatingBarScreen", FloatingBarScreen); break;
            case "FloatingBarLayout": key.SetValue("FloatingBarLayout", FloatingBarLayout); break;
            case "FloatingBar": key.SetValue("FloatingBar", FloatingBar); break;
            case "FloatingPosLeft": key.SetValue("FloatingPosLeft", FloatingPosLeft); break;
            case "FloatingPosTop": key.SetValue("FloatingPosTop", FloatingPosTop); break;
            case "MonitorCPU": key.SetValue("MonitorCPU", MonitorCPU); break;
            case "Preset": key.SetValue("Preset", Preset); break;
            case "Language": key.SetValue("Language", Language); break;
            case "DataLocalize": key.SetValue("DataLocalize", DataLocalize); break;
            case "LightingDevice": key.SetValue("LightingDevice", LightingDevice); break;
            case "LightingInterface": key.SetValue("LightingInterface", LightingInterface); break;
            case "LightingBrightness": key.SetValue("LightingBrightness", LightingBrightness); break;
            case "LightingColor": key.SetValue("LightingColor", LightingColor); break;
            case "LightingAnimation": key.SetValue("LightingAnimation", LightingAnimation); break;
            case "DisplayMode": key.SetValue("DisplayMode", DisplayMode); break;
            case "MonRefreshInterval": key.SetValue("MonRefreshInterval", MonRefreshInterval); break;
            case "IccMax": key.SetValue("IccMax", IccMax); break;
            case "AcLoadLine": key.SetValue("AcLoadLine", AcLoadLine); break;
            case "Tpp": key.SetValue("Tpp", Tpp); break;
            case "DState": key.SetValue("DState", DState); break;
            case "TgpEnabled": key.SetValue("TgpEnabled", TgpEnabled); break;
            case "PpabEnabled": key.SetValue("PpabEnabled", PpabEnabled); break;
            case "CustomPreset1Name": key.SetValue("CustomPreset1Name", CustomPreset1Name); break;
            case "CustomPreset2Name": key.SetValue("CustomPreset2Name", CustomPreset2Name); break;
            case "CustomPreset3Name": key.SetValue("CustomPreset3Name", CustomPreset3Name); break;
            case "FloatingOpacity": key.SetValue("FloatingOpacity", FloatingOpacity); break;
            case "FloatingBarOpacity": key.SetValue("FloatingOpacity", FloatingOpacity); break;
            case "FloatingTextOpacity": key.SetValue("FloatingTextOpacity", FloatingTextOpacity); break;
            case "VerboseLogging": key.SetValue("VerboseLogging", VerboseLogging); break;
            case "HeteroCpuSmallMask": key.SetValue("HeteroCpuSmallMask", HeteroCpuSmallMask); break;
            case "HeteroCpuDefaultPolicy": key.SetValue("HeteroCpuDefaultPolicy", HeteroCpuDefaultPolicy); break;
            case "HeteroCpuExpectedRuntime": key.SetValue("HeteroCpuExpectedRuntime", HeteroCpuExpectedRuntime); break;
            case "HeteroCpuImportantPolicy": key.SetValue("HeteroCpuImportantPolicy", HeteroCpuImportantPolicy); break;
            case "HeteroCpuImportantShortPolicy": key.SetValue("HeteroCpuImportantShortPolicy", HeteroCpuImportantShortPolicy); break;
            case "HeteroCpuPolicyMask": key.SetValue("HeteroCpuPolicyMask", HeteroCpuPolicyMask); break;
            case "HeteroCpuImportantPriority": key.SetValue("HeteroCpuImportantPriority", HeteroCpuImportantPriority); break;
            case "AutoFanProtect": key.SetValue("AutoFanProtect", AutoFanProtect); break;
            case "GpuPowerTgp": key.SetValue("GpuPowerTgp", GpuPowerTgp); break;
            case "GpuPowerPpab": key.SetValue("GpuPowerPpab", GpuPowerPpab); break;
            case "GpuPowerDState": key.SetValue("GpuPowerDState", GpuPowerDState); break;
            case "GpuPowerTpp": key.SetValue("GpuPowerTpp", GpuPowerTpp); break;
            case "GpuPowerIccMax": key.SetValue("GpuPowerIccMax", GpuPowerIccMax); break;
            case "GpuPowerAcLoadLine": key.SetValue("GpuPowerAcLoadLine", GpuPowerAcLoadLine); break;
            case "CpuPowerValue": key.SetValue("CpuPowerValue", CpuPowerValue); break;
            case "CpuPowerPl1": key.SetValue("CpuPowerPl1", CpuPowerPl1); break;
            case "CpuPowerPl2": key.SetValue("CpuPowerPl2", CpuPowerPl2); break;
            case "GpuCoreOverclock": key.SetValue("GpuCoreOverclock", GpuCoreOverclock); break;
            case "GpuMemoryOverclock": key.SetValue("GpuMemoryOverclock", GpuMemoryOverclock); break;
            case "MaxFrameRate": key.SetValue("MaxFrameRate", MaxFrameRate); break;
            case "RefreshRate": key.SetValue("RefreshRate", RefreshRate); break;
            case "PowerPlanGuid": key.SetValue("PowerPlanGuid", PowerPlanGuid); break;
            case "PowerMode": key.SetValue("PowerMode", PowerMode); break;
            case "Theme": key.SetValue("Theme", Theme); break;
            case "AccentColorSource": key.SetValue("AccentColorSource", AccentColorSource); break;
            case "AccentColor": key.SetValue("AccentColor", AccentColor); break;
            case "EcoQosEnabled": key.SetValue("EcoQosEnabled", EcoQosEnabled); break;
            case "EcoQosThrottlePlugged": key.SetValue("EcoQosThrottlePlugged", EcoQosThrottlePlugged); break;
            case "BatteryChargeLimit": key.SetValue("BatteryChargeLimit", BatteryChargeLimit); break;
            case "BatteryWmiUnsupported": key.SetValue("BatteryWmiUnsupported", BatteryWmiUnsupported); break;
            case "HWiNFOEnabled": key.SetValue("HWiNFOEnabled", HWiNFOEnabled); break;
            case "HttpApiEnabled": key.SetValue("HttpApiEnabled", HttpApiEnabled); break;
            case "AutomationEnabled": key.SetValue("AutomationEnabled", AutomationEnabled); break;
            case "MacroEnabled": key.SetValue("MacroEnabled", MacroEnabled); break;
            case "FanSync": key.SetValue("FanSync", FanSync); break;
            case "SmartFanEmaAlpha": key.SetValue("SmartFanEmaAlpha", SmartFanEmaAlpha); break;
            case "SmartFanStepDownRate": key.SetValue("SmartFanStepDownRate", SmartFanStepDownRate); break;
            case "SmartFanHysteresis": key.SetValue("SmartFanHysteresis", SmartFanHysteresis); break;
            case "EcoQosWhitelist": key.SetValue("EcoQosWhitelist", EcoQosWhitelist); break;
            case "EcoQosBlacklist": key.SetValue("EcoQosBlacklist", EcoQosBlacklist); break;
            case "CustomLogoPath": key.SetValue("CustomLogoPath", CustomLogoPath); break;
            case "CustomBgPath": key.SetValue("CustomBgPath", CustomBgPath); break;
            case "CustomBgOpacity": key.SetValue("CustomBgOpacity", CustomBgOpacity); break;
            case "CustomBgBlurEnabled": key.SetValue("CustomBgBlurEnabled", CustomBgBlurEnabled ? 1 : 0); break;
          }
        }
      } catch (Exception ex) {
        Console.WriteLine($"Error saving configuration: {ex.Message}");
      }
    }

    // ═══════════════════════════════════════════════════════
    // Preset Registry Subkey Helpers
    // ═══════════════════════════════════════════════════════
    static string PresetSubKey(string name) => $@"Software\OmenXHub\Presets\{name}";

    public static void InitBuiltInPresetDefaults(string preset) {
      switch (preset) {
        case "Extreme":
          FanTable = "cool"; FanControl = "auto"; TempSensitivity = "medium";
          CpuPower = "max"; DState = 1; TgpEnabled = true; PpabEnabled = true;
          MaxFrameRate = -1; PowerMode = 2; DisableDynamicBoost = 1;
          CpuPowerPl1 = 254; CpuPowerPl2 = 254; GpuClock = 0; Tpp = 255; break;
        case "GpuPriority":
          FanTable = "cool"; FanControl = "auto"; TempSensitivity = "medium";
          CpuPower = "45 W"; DState = 1; TgpEnabled = true; PpabEnabled = true;
          MaxFrameRate = -1; PowerMode = 1; DisableDynamicBoost = 0;
          CpuPowerPl1 = 45; CpuPowerPl2 = 45; GpuClock = 0; Tpp = 255; break;
        case "LightUse":
          FanTable = "silent"; FanControl = "auto"; TempSensitivity = "medium";
          CpuPower = "25 W"; DState = 1; TgpEnabled = false; PpabEnabled = false;
          MaxFrameRate = 60; PowerMode = 0; DisableDynamicBoost = 0;
          CpuPowerPl1 = 25; CpuPowerPl2 = 25; GpuClock = 0; Tpp = 0; break;
      }
    }

    public static void LoadPresetFromRegistry(string presetKey) {
      try {
        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(PresetSubKey(presetKey))) {
          if (key == null) return;
          FanTable = (string)key.GetValue("FanTable", FanTable);
          FanControl = (string)key.GetValue("FanControl", FanControl);
          TempSensitivity = (string)key.GetValue("TempSensitivity", TempSensitivity);
          CpuPower = (string)key.GetValue("CpuPower", CpuPower);
          TgpEnabled = Convert.ToBoolean(key.GetValue("TgpEnabled", TgpEnabled));
          PpabEnabled = Convert.ToBoolean(key.GetValue("PpabEnabled", PpabEnabled));
          DState = (int)key.GetValue("DState", DState);
          GpuClock = (int)key.GetValue("GpuClock", GpuClock);
          Tpp = (int)key.GetValue("Tpp", Tpp);
          DisplayMode = (string)key.GetValue("DisplayMode", DisplayMode);
          GpuPowerTgp = (string)key.GetValue("GpuPowerTgp", GpuPowerTgp);
          GpuPowerPpab = (string)key.GetValue("GpuPowerPpab", GpuPowerPpab);
          GpuPowerDState = (string)key.GetValue("GpuPowerDState", GpuPowerDState);
          GpuPowerTpp = (string)key.GetValue("GpuPowerTpp", GpuPowerTpp);
          CpuPowerValue = (string)key.GetValue("CpuPowerValue", CpuPowerValue);
          MaxFrameRate = (int)key.GetValue("MaxFrameRate", MaxFrameRate);
          RefreshRate = (int)key.GetValue("RefreshRate", RefreshRate);
          PowerPlanGuid = (string)key.GetValue("PowerPlanGuid", PowerPlanGuid);
          PowerMode = (int)key.GetValue("PowerMode", PowerMode);
          MonitorGPU = Convert.ToBoolean(key.GetValue("MonitorGPU", MonitorGPU));
          MonitorFan = Convert.ToBoolean(key.GetValue("MonitorFan", MonitorFan));
          MonitorCPU = Convert.ToBoolean(key.GetValue("MonitorCPU", MonitorCPU));
          AutoFanProtect = (string)key.GetValue("AutoFanProtect", AutoFanProtect);
          LightingDevice = (string)key.GetValue("LightingDevice", LightingDevice);
          LightingInterface = (string)key.GetValue("LightingInterface", LightingInterface);
          LightingBrightness = (byte)(int)key.GetValue("LightingBrightness", LightingBrightness);
          LightingColor = (string)key.GetValue("LightingColor", LightingColor);
          LightingAnimation = (string)key.GetValue("LightingAnimation", LightingAnimation);
          // Restore custom preset name from preset subkey
          string savedName = (string)key.GetValue("CustomPresetName", null);
          if (savedName != null) {
            if (presetKey == "Custom1") CustomPreset1Name = savedName;
            else if (presetKey == "Custom2") CustomPreset2Name = savedName;
            else if (presetKey == "Custom3") CustomPreset3Name = savedName;
          }
        }
      } catch { }
    }

    public static void SavePresetToRegistry(string presetKey) {
      // Save ALL presets (built-in and custom) to registry
      try {
        using (RegistryKey key = Registry.CurrentUser.CreateSubKey(PresetSubKey(presetKey))) {
          if (key == null) return;
          key.SetValue("FanTable", FanTable);
          key.SetValue("FanControl", FanControl);
          key.SetValue("TempSensitivity", TempSensitivity);
          key.SetValue("CpuPower", CpuPower);
          key.SetValue("TgpEnabled", TgpEnabled);
          key.SetValue("PpabEnabled", PpabEnabled);
          key.SetValue("DState", DState);
          key.SetValue("GpuClock", GpuClock);
          key.SetValue("Tpp", Tpp);
          key.SetValue("DisplayMode", DisplayMode);
          key.SetValue("MonRefreshInterval", MonRefreshInterval);
          key.SetValue("MonitorGPU", MonitorGPU);
          key.SetValue("MonitorFan", MonitorFan);
          key.SetValue("MonitorCPU", MonitorCPU);
          key.SetValue("GpuPowerTgp", GpuPowerTgp);
          key.SetValue("GpuPowerPpab", GpuPowerPpab);
          key.SetValue("GpuPowerDState", GpuPowerDState);
          key.SetValue("GpuPowerTpp", GpuPowerTpp);
          key.SetValue("CpuPowerValue", CpuPowerValue);
          key.SetValue("CpuPowerPl1", CpuPowerPl1);
          key.SetValue("CpuPowerPl2", CpuPowerPl2);
          key.SetValue("MaxFrameRate", MaxFrameRate);
          key.SetValue("RefreshRate", RefreshRate);
          key.SetValue("PowerPlanGuid", PowerPlanGuid);
          key.SetValue("PowerMode", PowerMode);
          key.SetValue("AutoFanProtect", AutoFanProtect);
          key.SetValue("LightingDevice", LightingDevice);
          key.SetValue("LightingInterface", LightingInterface);
          key.SetValue("LightingBrightness", LightingBrightness);
          key.SetValue("LightingColor", LightingColor);
          key.SetValue("LightingAnimation", LightingAnimation);
          // Save custom preset name in preset subkey for extra persistence
          if (presetKey == "Custom1") key.SetValue("CustomPresetName", CustomPreset1Name);
          else if (presetKey == "Custom2") key.SetValue("CustomPresetName", CustomPreset2Name);
          else if (presetKey == "Custom3") key.SetValue("CustomPresetName", CustomPreset3Name);
        }
      } catch (Exception ex) {
        Console.WriteLine($"Error saving configuration: {ex.Message}");
      }
    }

    // ═══════════════════════════════════════════════════════
    // Load Configuration (reads values only, does not apply)
    // ═══════════════════════════════════════════════════════
    static int RegInt(Microsoft.Win32.RegistryKey key, string name, int def) {
      try { return Convert.ToInt32(key.GetValue(name, def)); } catch { return def; }
    }
    static string RegStr(Microsoft.Win32.RegistryKey key, string name, string def) {
      try { return (string)key.GetValue(name, def) ?? def; } catch { return def; }
    }
    static bool RegBool(Microsoft.Win32.RegistryKey key, string name, bool def) {
      try { return Convert.ToBoolean(key.GetValue(name, def ? 1 : 0)); } catch { return def; }
    }
    static double RegDouble(Microsoft.Win32.RegistryKey key, string name, double def) {
      try { return Convert.ToDouble(key.GetValue(name, def)); } catch { return def; }
    }
    static byte RegByte(Microsoft.Win32.RegistryKey key, string name, byte def) {
      try { return Convert.ToByte(key.GetValue(name, (int)def)); } catch { return def; }
    }

    public static void Load() {
      try {
        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryPath)) {
          if (key == null) return;

          FanTable = RegStr(key, "FanTable", "silent");
          FanMode = RegStr(key, "FanMode", "performance");
          FanControl = RegStr(key, "FanControl", "auto");
          TempSensitivity = RegStr(key, "TempSensitivity", "medium");
          CpuPower = RegStr(key, "CpuPower", "max");
          GpuPower = RegStr(key, "GpuPower", "max");
          GpuClock = RegInt(key, "GpuClock", 0);
          DBVersion = RegInt(key, "DBVersion", 2);
          AutoStart = RegStr(key, "AutoStart", "off");
          AlreadyRead = RegInt(key, "AlreadyRead", 0);
          CustomIcon = RegStr(key, "CustomIcon", "original");
          OmenKey = RegStr(key, "OmenKey", "none");
          OmenKeyAppPath = RegStr(key, "OmenKeyAppPath", "");
          OmenKeyPresetCandidates = RegStr(key, "OmenKeyPresetCandidates", "LightUse;GpuPriority;Extreme;Custom1;Custom2;Custom3");
          MonitorGPU = RegBool(key, "MonitorGPU", true);
          MonitorFan = RegBool(key, "MonitorFan", true);
          TextSize = RegInt(key, "FloatingBarSize", 48);
          FloatingBarLoc = RegStr(key, "FloatingBarLoc", "left");
          FloatingBarScreen = RegStr(key, "FloatingBarScreen", "");
          FloatingBarLayout = RegStr(key, "FloatingBarLayout", "row");
          FloatingBar = RegStr(key, "FloatingBar", "off");
          FloatingPosLeft = RegDouble(key, "FloatingPosLeft", 100);
          FloatingPosTop = RegDouble(key, "FloatingPosTop", 100);
          Preset = RegStr(key, "Preset", "GpuPriority");
          Language = RegStr(key, "Language", "SimplifiedChinese");
          DataLocalize = RegStr(key, "DataLocalize", "off");
          LightingDevice = RegStr(key, "LightingDevice", "keyboard");
          LightingInterface = RegStr(key, "LightingInterface", "BasicFourZone");
          LightingBrightness = RegByte(key, "LightingBrightness", 100);
          LightingColor = RegStr(key, "LightingColor", "Red");
          LightingAnimation = RegStr(key, "LightingAnimation", "None");
          DisplayMode = RegStr(key, "DisplayMode", "smoothed");
          MonRefreshInterval = RegInt(key, "MonRefreshInterval", 1000);
          IccMax = RegInt(key, "IccMax", 0);
          AcLoadLine = RegInt(key, "AcLoadLine", 0);
          Tpp = RegInt(key, "Tpp", 0);
          DState = RegInt(key, "DState", 1);
          TgpEnabled = RegBool(key, "TgpEnabled", true);
          PpabEnabled = RegBool(key, "PpabEnabled", true);
          CustomPreset1Name = RegStr(key, "CustomPreset1Name", "Custom 1");
          CustomPreset2Name = RegStr(key, "CustomPreset2Name", "Custom 2");
          CustomPreset3Name = RegStr(key, "CustomPreset3Name", "Custom 3");
          FloatingOpacity = RegDouble(key, "FloatingOpacity", 0.85);
          FloatingTextOpacity = RegDouble(key, "FloatingTextOpacity", 1.0);
          VerboseLogging = RegBool(key, "VerboseLogging", false);
          HeteroCpuSmallMask = RegStr(key, "HeteroCpuSmallMask", "FFFF0000");
          HeteroCpuDefaultPolicy = RegInt(key, "HeteroCpuDefaultPolicy", 2);
          HeteroCpuExpectedRuntime = RegInt(key, "HeteroCpuExpectedRuntime", 1450);
          HeteroCpuImportantPolicy = RegInt(key, "HeteroCpuImportantPolicy", 2);
          HeteroCpuImportantShortPolicy = RegInt(key, "HeteroCpuImportantShortPolicy", 3);
          HeteroCpuPolicyMask = RegInt(key, "HeteroCpuPolicyMask", 7);
          HeteroCpuImportantPriority = RegInt(key, "HeteroCpuImportantPriority", 8);
          AutoFanProtect = RegStr(key, "AutoFanProtect", "on");
          GpuPowerTgp = RegStr(key, "GpuPowerTgp", "on");
          GpuPowerPpab = RegStr(key, "GpuPowerPpab", "on");
          GpuPowerDState = RegStr(key, "GpuPowerDState", "normal");
          GpuPowerTpp = RegStr(key, "GpuPowerTpp", "null");
          GpuPowerIccMax = RegStr(key, "GpuPowerIccMax", "null");
          GpuPowerAcLoadLine = RegStr(key, "GpuPowerAcLoadLine", "null");
          CpuPowerValue = RegStr(key, "CpuPowerValue", "null");
          CpuPowerPl1 = RegInt(key, "CpuPowerPl1", -1);
          CpuPowerPl2 = RegInt(key, "CpuPowerPl2", -1);
          GpuCoreOverclock = RegInt(key, "GpuCoreOverclock", -1);
          GpuMemoryOverclock = RegInt(key, "GpuMemoryOverclock", -1);
          MaxFrameRate = RegInt(key, "MaxFrameRate", -1);
          RefreshRate = RegInt(key, "RefreshRate", 0);
          PowerPlanGuid = RegStr(key, "PowerPlanGuid", "");
          PowerMode = RegInt(key, "PowerMode", 1);
          MonitorCPU = RegBool(key, "MonitorCPU", true);
          Theme = RegStr(key, "Theme", "system");
          AccentColorSource = RegStr(key, "AccentColorSource", "system");
          AccentColor = RegStr(key, "AccentColor", "#FFFFFFFF");
          Topmost = RegBool(key, "Topmost", true);
          ShowOsd = RegBool(key, "ShowOsd", true);
          ShowLockKeys = RegBool(key, "ShowLockKeys", true);
          EcoQosEnabled = RegBool(key, "EcoQosEnabled", false);
          EcoQosThrottlePlugged = RegBool(key, "EcoQosThrottlePlugged", false);
          BatteryChargeLimit = RegBool(key, "BatteryChargeLimit", false);
          BatteryWmiUnsupported = RegBool(key, "BatteryWmiUnsupported", false);
          HWiNFOEnabled = RegBool(key, "HWiNFOEnabled", false);
          HttpApiEnabled = RegBool(key, "HttpApiEnabled", false);
          AutomationEnabled = RegBool(key, "AutomationEnabled", true);
          MacroEnabled = RegBool(key, "MacroEnabled", true);
          FanSync = RegBool(key, "FanSync", false);
          SmartFanEmaAlpha = (float)RegDouble(key, "SmartFanEmaAlpha", 0.3);
          SmartFanStepDownRate = RegInt(key, "SmartFanStepDownRate", 500);
          SmartFanHysteresis = (float)RegDouble(key, "SmartFanHysteresis", 0.5);
          EcoQosWhitelist = RegStr(key, "EcoQosWhitelist", "");
          EcoQosBlacklist = RegStr(key, "EcoQosBlacklist", "");
          SysManufacturer = RegStr(key, "SysManufacturer", "");
          SysModel = RegStr(key, "SysModel", "");
          SysBios = RegStr(key, "SysBios", "");
          SysCpu = RegStr(key, "SysCpu", "");
          SysGpu = RegStr(key, "SysGpu", "");
          SysAdapterPower = RegInt(key, "SysAdapterPower", 0);
          SysProductName = RegStr(key, "SysProductName", "");
          SysBoardProduct = RegStr(key, "SysBoardProduct", "");
          SysCpuTjmax = RegInt(key, "SysCpuTjmax", 100);
          SysNvidiaTjmax = RegInt(key, "SysNvidiaTjmax", 0);
          SysNvidiaPowerMin = RegStr(key, "SysNvidiaPowerMin", "");
          SysNvidiaPowerMax = RegStr(key, "SysNvidiaPowerMax", "");
          SysKbType = RegStr(key, "SysKbType", "");
          SysValidation = RegInt(key, "SysValidation", 0);
          SysPawnIoText = RegStr(key, "SysPawnIoText", "").TrimStart('✔', '✓', '\u2713', '\u2714', '\u2705');
          CustomLogoPath = RegStr(key, "CustomLogoPath", "");
          CustomBgPath = RegStr(key, "CustomBgPath", "");
          CustomBgOpacity = RegDouble(key, "CustomBgOpacity", 0.5);
          CustomBgBlurEnabled = RegInt(key, "CustomBgBlurEnabled", 1) == 1;
        }
      } catch (Exception ex) {
        Console.WriteLine($"Error loading configuration: {ex.Message}");
      }
    }

    /// <summary>
    /// Read a single icon config value (used early in startup before full load).
    /// </summary>
    public static string ReadIconConfig() {
      try {
        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryPath)) {
          if (key != null) {
            return (string)key.GetValue("CustomIcon", "original");
          }
        }
      } catch { }
      return "original";
    }
  }

  internal static class MachineInfoCache {
    public static bool HasData => !string.IsNullOrEmpty(ConfigService.SysManufacturer);

    public static void Invalidate() {
      ConfigService.SysManufacturer = "";
      ConfigService.SysModel = "";
      ConfigService.SysBios = "";
      ConfigService.SysCpu = "";
      ConfigService.SysGpu = "";
      ConfigService.SysAdapterPower = 0;
      ConfigService.SysProductName = "";
      ConfigService.SysBoardProduct = "";
      ConfigService.SysCpuTjmax = 100;
      ConfigService.SysNvidiaTjmax = 0;
      ConfigService.SysNvidiaPowerMin = "";
      ConfigService.SysNvidiaPowerMax = "";
      ConfigService.SysKbType = "";
      ConfigService.SysValidation = 0;
      ConfigService.SysKbRaw = 0;
      ConfigService.SysPawnIoText = "";
    }
  }

  internal static class CustomPresetNames {
    static string FilePath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OmenXHub", "preset_names.txt");

    public static void Save() {
      try {
        var dir = System.IO.Path.GetDirectoryName(FilePath);
        if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
        System.IO.File.WriteAllText(FilePath,
            ConfigService.CustomPreset1Name + "\n" +
            ConfigService.CustomPreset2Name + "\n" +
            ConfigService.CustomPreset3Name);
      } catch { }
    }

    public static void Load() {
      try {
        if (!System.IO.File.Exists(FilePath)) return;
        var lines = System.IO.File.ReadAllLines(FilePath);
        if (lines.Length >= 1 && !string.IsNullOrEmpty(lines[0])) ConfigService.CustomPreset1Name = lines[0];
        if (lines.Length >= 2 && !string.IsNullOrEmpty(lines[1])) ConfigService.CustomPreset2Name = lines[1];
        if (lines.Length >= 3 && !string.IsNullOrEmpty(lines[2])) ConfigService.CustomPreset3Name = lines[2];
      } catch { }
    }
  }
}