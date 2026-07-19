// ConfigService.cs - 配置管理服务
// 100+ 静态配置字段，Windows 注册表持久化 (HKCU\Software\OmenXHub)，预设保存/加载
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;

namespace OmenSuperHub.Services {
  internal static class ConfigService {
    private const string RegistryPath = @"Software\OmenXHub";

    // Fired when Omen key cycles to a new preset (from background thread)
    public static event Action<string> OnPresetCycled;
    public static void FirePresetCycled(string preset) {
      // ponytail: marshal to UI thread — called from ThreadPool (Omen key handler, automation)
      try {
        var app = System.Windows.Application.Current;
        if (app != null && app.Dispatcher != null && !app.Dispatcher.CheckAccess())
          app.Dispatcher.Invoke(() => OnPresetCycled?.Invoke(preset));
        else
          OnPresetCycled?.Invoke(preset);
      } catch { }
    }

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
    public static string OmenKeyPresetCandidates = "LightUse;GpuPriority;Extreme";
    public static bool MonitorGPU = true;
    public static bool MonitorFan = true;
    public static bool MonitorMemory = true;
    public static bool MonitorNetwork = true;
    public static bool MonitorFPS = true;
    public static int TextSize = 48;
    public static string FloatingBarLoc = "left";
    public static string FloatingBarLayout = "row";
    public static string FloatingBarScreen = "";
    public static string FloatingBar = "off";
    // ponytail: volatile — FloatingPos* 在 UI 拖拽线程写、FloatingWindow 渲染线程读，
    // 没有内存屏障会读到陈旧坐标导致窗口跳回旧位置。仅单元素原子，复合更新仍可能混合。
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
    // ponytail: Direction/Theme only meaningful under Dojo anim — see docs/lighting-reverse-findings.md
    public static string LightingDirection = "Left";
    public static string LightingTheme = "Custom";
    // ponytail: PerKey RGB persisted state (only used when LightProto is PerKey or keyboard is Rgb).
    // Color/animation names mirror the ComboBox selection; Brightness is the byte scaled same
    // way as the 4-zone LightingBrightness (separate field because they apply to different devices).
    public static string PerKeyStaticColor = "Red";
    public static string PerKeyAnimation = "None";
    public static byte PerKeyBrightness = 100;
    public static string DisplayMode = "smoothed";
    public static int MonRefreshInterval = 1000;

    public static int IccMax = 0;
    public static int AcLoadLine = 0;
    public static int Tpp = 0;
    public static int DState = 1;
    public static bool TgpEnabled = true;
    public static bool PpabEnabled = true;
    public static string CustomPreset1Name = "Custom 1";  // ponytail: legacy fields kept for migration; replaced by CustomPresetNames dict
    public static string CustomPreset2Name = "Custom 2";
    public static string CustomPreset3Name = "Custom 3";
    // ponytail: dynamic custom preset names dict — key=preset file key, value=display name
    public static Dictionary<string, string> CustomPresetNames = new Dictionary<string, string>();
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
    public static string Resolution = "";   // "WxH" format, "" = don't restore
    public static int DpiScale = 0;          // 0 = don't restore, 100/125/150/...
    public static bool HdrEnabled = false;   // HDR state for custom presets
    public static bool BatteryChargeLimit = false;
    public static bool BatteryWmiUnsupported = false;
    public static bool HWiNFOEnabled = false;
    public static bool HWiNFOReadEnabled = false;    // 从 HWiNFO64 读取传感器数据
    public static bool HttpApiEnabled = false;
    public static bool AutomationEnabled = true;
    public static bool MacroEnabled = true;
    public static bool AdvancedTuningUnlocked = false; // 点击 logo 5 次解锁
    public static bool DebugShowAllUi = false;   // DEBUG: 强制显示所有 UI 卡片
    // Advanced CPU tuning
    public static int PboScalar = 0;         // 0=auto, 1-10
    public static int CoAllCoreOffset = 0;   // -50 to +30
    public static int FivrCoreOffset = 0;    // -250 to +250 mV
    public static int FivrCacheOffset = 0;
    public static int FivrIgpuOffset = 0;
    public static int FivrSaOffset = 0;
    public static int ClockRatio = 0;
    public static string PerCoreRatios = "";
    public static int PowerBalance = 16;     // 0-31, 16=balanced
    // Advanced GPU tuning
    public static int NvVoltCurveOffset = 0; // -1000 to +1000 mV (legacy simple offset)
    public static int NvPowerLimit = 0;      // 0=unset, 1..max (NVML)
    public static int NvMaxGpuClock = 0;     // 0=unlocked, 400..4000 MHz (NVML)
    public static int RtssFrameLimit = 0;    // 0=off, 1-999
    public static bool AutoOcEnabled = false;
    // UXTU / PawnIO MSR
    public static bool PawnTurboEnabled = true;
    public static int PawnProchotOffset = 0;
    public static int PawnHwpEpp = 128;
    public static int PawnCStateLimit = 0;
    public static int PawnIgpuPower = 0;
    public static int PawnIgpuRatio = 0;
    // AMD APU advanced tuning (SMU mailbox)
    public static int AmdStapmLimit = 0;       // mW, 0=unset
    public static int AmdFastLimit = 0;        // mW, 0=unset
    public static int AmdSlowLimit = 0;        // mW, 0=unset
    public static int AmdStapmTime = 0;        // seconds, 0=unset
    public static int AmdSlowTime = 0;         // seconds, 0=unset
    public static int AmdVrmCurrent = 0;       // mA, 0=unset
    public static int AmdVrmSocCurrent = 0;    // mA, 0=unset
    public static int AmdVrmMaxCurrent = 0;    // mA, 0=unset
    public static int AmdVrmSocMaxCurrent = 0; // mA, 0=unset
    public static int AmdTctlTemp = 0;         // °C, 0=unset
    public static int AmdSkinTempLimit = 0;    // mW, 0=unset
    public static int AmdApuSkinTemp = 0;      // °C, 0=unset
    public static int AmdDgpuSkinTemp = 0;     // °C, 0=unset
    public static int AmdGfxClk = 0;           // MHz, 0=unset
    // AMD CPU-level advanced tuning (independent of APU STAPM/Fast/Slow)
    public static int AmdCpuPpt = 0;           // mW, 0=unset  (AM5 CPU TDP)
    public static int AmdCpuTdc = 0;           // mA, 0=unset
    public static int AmdCpuEdc = 0;           // mA, 0=unset
    public static int AmdCpuTctl = 0;          // °C, 0=unset  (CPU hard throttle temp)
    // Curve Optimiser — iGPU offset + per-core offsets (CCD1: 0..11, CCD2: 12..23)
    public static int CoIGpuOffset = 0;        // -30 to +30
    // ponytail: array persisted as 24 reg keys "CoPc0".."CoPc23" to avoid collection-serialization overhead
    public static int[] CoPerCore = new int[24];

    // ── UXTU-style master toggles (per-card enable/disable) ──
    // Default=true preserves existing behaviour (sliders are active by default)
    public static bool FivrMasterEnabled = true;
    public static bool ApuPowerMasterEnabled = true;
    public static bool ApuVrmMasterEnabled = true;
    public static bool ApuTempMasterEnabled = true;
    public static bool ApuGfxClkMasterEnabled = true;
    public static bool AmdCpuPowerMasterEnabled = true;
    public static bool AmdCpuTempMasterEnabled = true;
    // ponytail: extended set — covers all advanced cards, added in batch so a
    // future "disable all" can flip them all in one place. Each card reads its
    // own flag in InitMasterToggle().
    // NOTE: advanced tuning cards default to DISABLED so users explicitly opt in.
    public static bool PboScalarMasterEnabled = false;
    public static bool CoMasterEnabled = false;
    public static bool CcdAffinityMasterEnabled = false;
    public static bool AutoOcMasterEnabled = false;
    public static bool ClockRatioMasterEnabled = false;
    public static bool PowerBalanceMasterEnabled = false;
    public static bool PawnTurboMasterEnabled = false;
    public static bool PawnProchotMasterEnabled = false;
    public static bool PawnHwpMasterEnabled = false;
    public static bool PawnCStateMasterEnabled = false;
    public static bool PawnIgpuPowerMasterEnabled = false;
    public static bool PawnIgpuRatioMasterEnabled = false;
    public static bool NvTuningMasterEnabled = false;
    public static bool RtssMasterEnabled = false;
    public static bool AdlxMasterEnabled = false;
    // ADLX / AMD GPU advanced
    public static bool AdlxRsrEnabled = false;     // Radeon Super Resolution
    public static int AdlxRsrSharpness = 50;       // 0..100
    public static bool AdlxAntiLagEnabled = false;
    public static bool AdlxEnhancedSyncEnabled = false;
    public static bool AdlxBoostEnabled = false;
    public static int AdlxBoostPercent = 0;        // -20..+20
    public static bool AdlxImageSharpEnabled = false;
    public static int AdlxImageSharpPercent = 50;  // 0..100

    public static bool FanSync = false;
    // ponytail: volatile — UI 写、ThreadPool(GetSmartFanSpeed) 读。
    // 不加屏障读到陈旧值会让 EMA 用旧 alpha/旧 hysteresis 算几轮才追上。
    // 上限：volatile 只保证单字段可见性，多字段复合更新仍可能混合，
    // 升级路径是把这三个字段搬到 FanService._fanLock 内的 snapshot 结构。
    public static volatile float SmartFanEmaAlpha = 0.3f;
    public static int SmartFanStepDownRate = 500;
    public static volatile float SmartFanHysteresis = 0.5f;

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
        Logger.Error($"Error batch saving configuration: {ex.Message}");
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
            case "MonitorMemory": key.SetValue("MonitorMemory", MonitorMemory); break;
            case "MonitorNetwork": key.SetValue("MonitorNetwork", MonitorNetwork); break;
            case "MonitorFPS": key.SetValue("MonitorFPS", MonitorFPS); break;
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
            case "LightingDirection": key.SetValue("LightingDirection", LightingDirection); break;
            case "LightingTheme": key.SetValue("LightingTheme", LightingTheme); break;
            case "PerKeyStaticColor": key.SetValue("PerKeyStaticColor", PerKeyStaticColor); break;
            case "PerKeyAnimation": key.SetValue("PerKeyAnimation", PerKeyAnimation); break;
            case "PerKeyBrightness": key.SetValue("PerKeyBrightness", PerKeyBrightness); break;
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
            case "HWiNFOReadEnabled": key.SetValue("HWiNFOReadEnabled", HWiNFOReadEnabled); break;
            case "HttpApiEnabled": key.SetValue("HttpApiEnabled", HttpApiEnabled); break;
            case "AutomationEnabled": key.SetValue("AutomationEnabled", AutomationEnabled); break;
            case "MacroEnabled": key.SetValue("MacroEnabled", MacroEnabled); break;
            case "AdvancedTuningUnlocked": key.SetValue("AdvancedTuningUnlocked", AdvancedTuningUnlocked ? 1 : 0); break;
            case "DebugShowAllUi": key.SetValue("DebugShowAllUi", DebugShowAllUi ? 1 : 0); break;
            case "PboScalar": key.SetValue("PboScalar", PboScalar); break;
            case "CoAllCoreOffset": key.SetValue("CoAllCoreOffset", CoAllCoreOffset); break;
            case "FivrCoreOffset": key.SetValue("FivrCoreOffset", FivrCoreOffset); break;
            case "FivrCacheOffset": key.SetValue("FivrCacheOffset", FivrCacheOffset); break;
            case "FivrIgpuOffset": key.SetValue("FivrIgpuOffset", FivrIgpuOffset); break;
            case "FivrSaOffset": key.SetValue("FivrSaOffset", FivrSaOffset); break;
            case "ClockRatio": key.SetValue("ClockRatio", ClockRatio); break;
            case "PerCoreRatios": key.SetValue("PerCoreRatios", PerCoreRatios); break;
            case "PowerBalance": key.SetValue("PowerBalance", PowerBalance); break;
            case "NvVoltCurveOffset": key.SetValue("NvVoltCurveOffset", NvVoltCurveOffset); break;
            case "NvPowerLimit": key.SetValue("NvPowerLimit", NvPowerLimit); break;
            case "NvMaxGpuClock": key.SetValue("NvMaxGpuClock", NvMaxGpuClock); break;
            case "RtssFrameLimit": key.SetValue("RtssFrameLimit", RtssFrameLimit); break;
            case "AutoOcEnabled": key.SetValue("AutoOcEnabled", AutoOcEnabled); break;
            case "PawnTurboEnabled": key.SetValue("PawnTurboEnabled", PawnTurboEnabled); break;
            case "PawnProchotOffset": key.SetValue("PawnProchotOffset", PawnProchotOffset); break;
            case "PawnHwpEpp": key.SetValue("PawnHwpEpp", PawnHwpEpp); break;
            case "PawnCStateLimit": key.SetValue("PawnCStateLimit", PawnCStateLimit); break;
            case "PawnIgpuPower": key.SetValue("PawnIgpuPower", PawnIgpuPower); break;
            case "PawnIgpuRatio": key.SetValue("PawnIgpuRatio", PawnIgpuRatio); break;
            case "AmdStapmLimit": key.SetValue("AmdStapmLimit", AmdStapmLimit); break;
            case "AmdFastLimit": key.SetValue("AmdFastLimit", AmdFastLimit); break;
            case "AmdSlowLimit": key.SetValue("AmdSlowLimit", AmdSlowLimit); break;
            case "AmdStapmTime": key.SetValue("AmdStapmTime", AmdStapmTime); break;
            case "AmdSlowTime": key.SetValue("AmdSlowTime", AmdSlowTime); break;
            case "AmdVrmCurrent": key.SetValue("AmdVrmCurrent", AmdVrmCurrent); break;
            case "AmdVrmSocCurrent": key.SetValue("AmdVrmSocCurrent", AmdVrmSocCurrent); break;
            case "AmdVrmMaxCurrent": key.SetValue("AmdVrmMaxCurrent", AmdVrmMaxCurrent); break;
            case "AmdVrmSocMaxCurrent": key.SetValue("AmdVrmSocMaxCurrent", AmdVrmSocMaxCurrent); break;
            case "AmdTctlTemp": key.SetValue("AmdTctlTemp", AmdTctlTemp); break;
            case "AmdSkinTempLimit": key.SetValue("AmdSkinTempLimit", AmdSkinTempLimit); break;
            case "AmdApuSkinTemp": key.SetValue("AmdApuSkinTemp", AmdApuSkinTemp); break;
            case "AmdDgpuSkinTemp": key.SetValue("AmdDgpuSkinTemp", AmdDgpuSkinTemp); break;
            case "AmdGfxClk": key.SetValue("AmdGfxClk", AmdGfxClk); break;
            case "AmdCpuPpt": key.SetValue("AmdCpuPpt", AmdCpuPpt); break;
            case "AmdCpuTdc": key.SetValue("AmdCpuTdc", AmdCpuTdc); break;
            case "AmdCpuEdc": key.SetValue("AmdCpuEdc", AmdCpuEdc); break;
            case "AmdCpuTctl": key.SetValue("AmdCpuTctl", AmdCpuTctl); break;
            case "CoIGpuOffset": key.SetValue("CoIGpuOffset", CoIGpuOffset); break;
            case "CoPerCore":
                // ponytail: 24 keys flat — keeps serialization one-liner, no JSON plumbing
                for (int i = 0; i < CoPerCore.Length; i++) key.SetValue("CoPc" + i, CoPerCore[i]);
                key.SetValue("CoPcCount", CoPerCore.Length); break;
            case "FivrMasterEnabled": key.SetValue("FivrMasterEnabled", FivrMasterEnabled); break;
            case "ApuPowerMasterEnabled": key.SetValue("ApuPowerMasterEnabled", ApuPowerMasterEnabled); break;
            case "ApuVrmMasterEnabled": key.SetValue("ApuVrmMasterEnabled", ApuVrmMasterEnabled); break;
            case "ApuTempMasterEnabled": key.SetValue("ApuTempMasterEnabled", ApuTempMasterEnabled); break;
            case "ApuGfxClkMasterEnabled": key.SetValue("ApuGfxClkMasterEnabled", ApuGfxClkMasterEnabled); break;
            case "AmdCpuPowerMasterEnabled": key.SetValue("AmdCpuPowerMasterEnabled", AmdCpuPowerMasterEnabled); break;
            case "AmdCpuTempMasterEnabled": key.SetValue("AmdCpuTempMasterEnabled", AmdCpuTempMasterEnabled); break;
            case "PboScalarMasterEnabled": key.SetValue("PboScalarMasterEnabled", PboScalarMasterEnabled); break;
            case "CoMasterEnabled": key.SetValue("CoMasterEnabled", CoMasterEnabled); break;
            case "CcdAffinityMasterEnabled": key.SetValue("CcdAffinityMasterEnabled", CcdAffinityMasterEnabled); break;
            case "AutoOcMasterEnabled": key.SetValue("AutoOcMasterEnabled", AutoOcMasterEnabled); break;
            case "ClockRatioMasterEnabled": key.SetValue("ClockRatioMasterEnabled", ClockRatioMasterEnabled); break;
            case "PowerBalanceMasterEnabled": key.SetValue("PowerBalanceMasterEnabled", PowerBalanceMasterEnabled); break;
            case "PawnTurboMasterEnabled": key.SetValue("PawnTurboMasterEnabled", PawnTurboMasterEnabled); break;
            case "PawnProchotMasterEnabled": key.SetValue("PawnProchotMasterEnabled", PawnProchotMasterEnabled); break;
            case "PawnHwpMasterEnabled": key.SetValue("PawnHwpMasterEnabled", PawnHwpMasterEnabled); break;
            case "PawnCStateMasterEnabled": key.SetValue("PawnCStateMasterEnabled", PawnCStateMasterEnabled); break;
            case "PawnIgpuPowerMasterEnabled": key.SetValue("PawnIgpuPowerMasterEnabled", PawnIgpuPowerMasterEnabled); break;
            case "PawnIgpuRatioMasterEnabled": key.SetValue("PawnIgpuRatioMasterEnabled", PawnIgpuRatioMasterEnabled); break;
            case "NvTuningMasterEnabled": key.SetValue("NvTuningMasterEnabled", NvTuningMasterEnabled); break;
            case "RtssMasterEnabled": key.SetValue("RtssMasterEnabled", RtssMasterEnabled); break;
            case "AdlxMasterEnabled": key.SetValue("AdlxMasterEnabled", AdlxMasterEnabled); break;
            case "AdlxRsrEnabled": key.SetValue("AdlxRsrEnabled", AdlxRsrEnabled ? 1 : 0); break;
            case "AdlxRsrSharpness": key.SetValue("AdlxRsrSharpness", AdlxRsrSharpness); break;
            case "AdlxAntiLagEnabled": key.SetValue("AdlxAntiLagEnabled", AdlxAntiLagEnabled ? 1 : 0); break;
            case "AdlxEnhancedSyncEnabled": key.SetValue("AdlxEnhancedSyncEnabled", AdlxEnhancedSyncEnabled ? 1 : 0); break;
            case "AdlxBoostEnabled": key.SetValue("AdlxBoostEnabled", AdlxBoostEnabled ? 1 : 0); break;
            case "AdlxBoostPercent": key.SetValue("AdlxBoostPercent", AdlxBoostPercent); break;
            case "AdlxImageSharpEnabled": key.SetValue("AdlxImageSharpEnabled", AdlxImageSharpEnabled ? 1 : 0); break;
            case "AdlxImageSharpPercent": key.SetValue("AdlxImageSharpPercent", AdlxImageSharpPercent); break;
            case "FanSync": key.SetValue("FanSync", FanSync); break;
            case "SmartFanEmaAlpha": key.SetValue("SmartFanEmaAlpha", SmartFanEmaAlpha); break;
            case "SmartFanStepDownRate": key.SetValue("SmartFanStepDownRate", SmartFanStepDownRate); break;
            case "SmartFanHysteresis": key.SetValue("SmartFanHysteresis", SmartFanHysteresis); break;
            case "ShowOsd": key.SetValue("ShowOsd", ShowOsd); break;
            case "Topmost": key.SetValue("Topmost", Topmost); break;
            case "ShowLockKeys": key.SetValue("ShowLockKeys", ShowLockKeys); break;
            case "EcoQosWhitelist": key.SetValue("EcoQosWhitelist", EcoQosWhitelist); break;
            case "EcoQosBlacklist": key.SetValue("EcoQosBlacklist", EcoQosBlacklist); break;
            case "CustomLogoPath": key.SetValue("CustomLogoPath", CustomLogoPath); break;
            case "CustomBgPath": key.SetValue("CustomBgPath", CustomBgPath); break;
            case "CustomBgOpacity": key.SetValue("CustomBgOpacity", CustomBgOpacity); break;
            case "CustomBgBlurEnabled": key.SetValue("CustomBgBlurEnabled", CustomBgBlurEnabled ? 1 : 0); break;
            case "DisableDynamicBoost": key.SetValue("DisableDynamicBoost", DisableDynamicBoost); break;
            case "Resolution": key.SetValue("Resolution", Resolution); break;
            case "DpiScale": key.SetValue("DpiScale", DpiScale); break;
            case "HdrEnabled": key.SetValue("HdrEnabled", HdrEnabled ? 1 : 0); break;
          }
        }
      } catch (Exception ex) {
        Logger.Error($"Error saving configuration: {ex.Message}");
      }
    }

    // ═══════════════════════════════════════════════════════
    // Preset Registry Subkey Helpers
    // ═══════════════════════════════════════════════════════
    static string PresetSubKey(string name) => $@"Software\OmenXHub\Presets\{name}";

    public static void InitBuiltInPresetDefaults(string preset) {
      // ponytail: per spec — only 1.1 global bound params for built-in presets.
      // DState/1.2 and 1.3 NOT touched. DState defaults to 1 (正常) independently.
      switch (preset) {
        case "Extreme":
          FanTable = "cool"; FanControl = "auto";
          CpuPower = "max"; TgpEnabled = true; PpabEnabled = true;
          PowerMode = 1; // 平衡
          CpuPowerPl1 = 254; CpuPowerPl2 = 254; GpuClock = 0; Tpp = 254;
          AmdCpuPpt = 254; AmdCpuTdc = 200; AmdCpuEdc = 300; AmdCpuTctl = 95; break;
        case "GpuPriority":
          FanTable = "cool"; FanControl = "auto";
          CpuPower = "55 W"; TgpEnabled = true; PpabEnabled = true;
          PowerMode = 1; // 平衡
          CpuPowerPl1 = 55; CpuPowerPl2 = 55; GpuClock = 0; Tpp = 254;
          AmdCpuPpt = 55; AmdCpuTdc = 80; AmdCpuEdc = 160; AmdCpuTctl = 95; break;
        case "LightUse":
          FanTable = "silent"; FanControl = "auto";
          CpuPower = "25 W"; TgpEnabled = false; PpabEnabled = false;
          PowerMode = 0; // 最佳能效
          CpuPowerPl1 = 25; CpuPowerPl2 = 25; GpuClock = 0; Tpp = 0;
          AmdCpuPpt = 30; AmdCpuTdc = 40; AmdCpuEdc = 80; AmdCpuTctl = 85; break;
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
          AmdCpuPpt = (int)key.GetValue("AmdCpuPpt", AmdCpuPpt);
          AmdCpuTdc = (int)key.GetValue("AmdCpuTdc", AmdCpuTdc);
          AmdCpuEdc = (int)key.GetValue("AmdCpuEdc", AmdCpuEdc);
          AmdCpuTctl = (int)key.GetValue("AmdCpuTctl", AmdCpuTctl);
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
          MonitorMemory = Convert.ToBoolean(key.GetValue("MonitorMemory", MonitorMemory));
          MonitorNetwork = Convert.ToBoolean(key.GetValue("MonitorNetwork", MonitorNetwork));
          MonitorFPS = Convert.ToBoolean(key.GetValue("MonitorFPS", MonitorFPS));
          MonitorCPU = Convert.ToBoolean(key.GetValue("MonitorCPU", MonitorCPU));
          AutoFanProtect = (string)key.GetValue("AutoFanProtect", AutoFanProtect);
          LightingDevice = (string)key.GetValue("LightingDevice", LightingDevice);
          LightingInterface = (string)key.GetValue("LightingInterface", LightingInterface);
          LightingBrightness = (byte)(int)key.GetValue("LightingBrightness", LightingBrightness);
          LightingColor = (string)key.GetValue("LightingColor", LightingColor);
          LightingAnimation = (string)key.GetValue("LightingAnimation", LightingAnimation);
          LightingDirection = (string)key.GetValue("LightingDirection", LightingDirection);
          LightingTheme = (string)key.GetValue("LightingTheme", LightingTheme);
          PerKeyStaticColor = (string)key.GetValue("PerKeyStaticColor", PerKeyStaticColor);
          PerKeyAnimation = (string)key.GetValue("PerKeyAnimation", PerKeyAnimation);
          PerKeyBrightness = (byte)(int)key.GetValue("PerKeyBrightness", PerKeyBrightness);
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
          key.SetValue("MonitorMemory", MonitorMemory);
          key.SetValue("MonitorNetwork", MonitorNetwork);
          key.SetValue("MonitorFPS", MonitorFPS);
          key.SetValue("MonitorCPU", MonitorCPU);
          key.SetValue("GpuPowerTgp", GpuPowerTgp);
          key.SetValue("GpuPowerPpab", GpuPowerPpab);
          key.SetValue("GpuPowerDState", GpuPowerDState);
          key.SetValue("GpuPowerTpp", GpuPowerTpp);
          key.SetValue("CpuPowerValue", CpuPowerValue);
          key.SetValue("CpuPowerPl1", CpuPowerPl1);
          key.SetValue("CpuPowerPl2", CpuPowerPl2);
          key.SetValue("AmdCpuPpt", AmdCpuPpt);
          key.SetValue("AmdCpuTdc", AmdCpuTdc);
          key.SetValue("AmdCpuEdc", AmdCpuEdc);
          key.SetValue("AmdCpuTctl", AmdCpuTctl);
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
          key.SetValue("LightingDirection", LightingDirection);
          key.SetValue("LightingTheme", LightingTheme);
          // ponytail: PerKey state also persisted into custom preset so the preset captures full lighting.
          key.SetValue("PerKeyStaticColor", PerKeyStaticColor);
          key.SetValue("PerKeyAnimation", PerKeyAnimation);
          key.SetValue("PerKeyBrightness", PerKeyBrightness);
          // Save custom preset name in preset subkey for extra persistence
          if (presetKey == "Custom1") key.SetValue("CustomPresetName", CustomPreset1Name);
          else if (presetKey == "Custom2") key.SetValue("CustomPresetName", CustomPreset2Name);
          else if (presetKey == "Custom3") key.SetValue("CustomPresetName", CustomPreset3Name);
        }
      } catch (Exception ex) {
        Logger.Error($"Error saving preset '{presetKey}': {ex.Message}");
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
          OmenKeyPresetCandidates = RegStr(key, "OmenKeyPresetCandidates", "LightUse;GpuPriority;Extreme");
          MonitorGPU = RegBool(key, "MonitorGPU", true);
          MonitorFan = RegBool(key, "MonitorFan", true);
          MonitorMemory = RegBool(key, "MonitorMemory", true);
          MonitorNetwork = RegBool(key, "MonitorNetwork", true);
          MonitorFPS = RegBool(key, "MonitorFPS", true);
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
          LightingDirection = RegStr(key, "LightingDirection", "Left");
          LightingTheme = RegStr(key, "LightingTheme", "Custom");
          PerKeyStaticColor = RegStr(key, "PerKeyStaticColor", "Red");
          PerKeyAnimation = RegStr(key, "PerKeyAnimation", "None");
          PerKeyBrightness = RegByte(key, "PerKeyBrightness", 100);
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
          HWiNFOReadEnabled = RegBool(key, "HWiNFOReadEnabled", false);
          HttpApiEnabled = RegBool(key, "HttpApiEnabled", false);
          AutomationEnabled = RegBool(key, "AutomationEnabled", true);
          MacroEnabled = RegBool(key, "MacroEnabled", true);
          AdvancedTuningUnlocked = RegInt(key, "AdvancedTuningUnlocked", 0) != 0;
          DebugShowAllUi = RegInt(key, "DebugShowAllUi", 0) != 0;
            PboScalar = RegInt(key, "PboScalar", 0);
            CoAllCoreOffset = RegInt(key, "CoAllCoreOffset", 0);
            FivrCoreOffset = RegInt(key, "FivrCoreOffset", 0);
            FivrCacheOffset = RegInt(key, "FivrCacheOffset", 0);
            FivrIgpuOffset = RegInt(key, "FivrIgpuOffset", 0);
            FivrSaOffset = RegInt(key, "FivrSaOffset", 0);
            ClockRatio = RegInt(key, "ClockRatio", 0);
            PerCoreRatios = RegStr(key, "PerCoreRatios", "");
            PowerBalance = RegInt(key, "PowerBalance", 16);
            NvVoltCurveOffset = RegInt(key, "NvVoltCurveOffset", 0);
            NvPowerLimit = RegInt(key, "NvPowerLimit", 0);
            NvMaxGpuClock = RegInt(key, "NvMaxGpuClock", 0);
            RtssFrameLimit = RegInt(key, "RtssFrameLimit", 0);
            AutoOcEnabled = RegBool(key, "AutoOcEnabled", false);
            PawnTurboEnabled = RegBool(key, "PawnTurboEnabled", true);
            PawnProchotOffset = RegInt(key, "PawnProchotOffset", 0);
            PawnHwpEpp = RegInt(key, "PawnHwpEpp", 128);
            PawnCStateLimit = RegInt(key, "PawnCStateLimit", 0);
            PawnIgpuPower = RegInt(key, "PawnIgpuPower", 0);
            PawnIgpuRatio = RegInt(key, "PawnIgpuRatio", 0);
            AmdStapmLimit = RegInt(key, "AmdStapmLimit", 0);
            AmdFastLimit = RegInt(key, "AmdFastLimit", 0);
            AmdSlowLimit = RegInt(key, "AmdSlowLimit", 0);
            AmdStapmTime = RegInt(key, "AmdStapmTime", 0);
            AmdSlowTime = RegInt(key, "AmdSlowTime", 0);
            AmdVrmCurrent = RegInt(key, "AmdVrmCurrent", 0);
            AmdVrmSocCurrent = RegInt(key, "AmdVrmSocCurrent", 0);
            AmdVrmMaxCurrent = RegInt(key, "AmdVrmMaxCurrent", 0);
            AmdVrmSocMaxCurrent = RegInt(key, "AmdVrmSocMaxCurrent", 0);
            AmdTctlTemp = RegInt(key, "AmdTctlTemp", 0);
            AmdSkinTempLimit = RegInt(key, "AmdSkinTempLimit", 0);
            AmdApuSkinTemp = RegInt(key, "AmdApuSkinTemp", 0);
            AmdDgpuSkinTemp = RegInt(key, "AmdDgpuSkinTemp", 0);
            AmdGfxClk = RegInt(key, "AmdGfxClk", 0);
            AmdCpuPpt = RegInt(key, "AmdCpuPpt", 0);
            AmdCpuTdc = RegInt(key, "AmdCpuTdc", 0);
            AmdCpuEdc = RegInt(key, "AmdCpuEdc", 0);
            AmdCpuTctl = RegInt(key, "AmdCpuTctl", 0);
            CoIGpuOffset = RegInt(key, "CoIGpuOffset", 0);
            {
              int cnt = RegInt(key, "CoPcCount", 24);
              if (cnt < 0 || cnt > CoPerCore.Length) cnt = CoPerCore.Length;
              for (int i = 0; i < CoPerCore.Length; i++) CoPerCore[i] = i < cnt ? RegInt(key, "CoPc" + i, 0) : 0;
            }
            FivrMasterEnabled = RegBool(key, "FivrMasterEnabled", true);
            ApuPowerMasterEnabled = RegBool(key, "ApuPowerMasterEnabled", true);
            ApuVrmMasterEnabled = RegBool(key, "ApuVrmMasterEnabled", true);
            ApuTempMasterEnabled = RegBool(key, "ApuTempMasterEnabled", true);
            ApuGfxClkMasterEnabled = RegBool(key, "ApuGfxClkMasterEnabled", true);
            AmdCpuPowerMasterEnabled = RegBool(key, "AmdCpuPowerMasterEnabled", true);
            AmdCpuTempMasterEnabled = RegBool(key, "AmdCpuTempMasterEnabled", true);
            PboScalarMasterEnabled = RegBool(key, "PboScalarMasterEnabled", false);
            CoMasterEnabled = RegBool(key, "CoMasterEnabled", false);
            CcdAffinityMasterEnabled = RegBool(key, "CcdAffinityMasterEnabled", false);
            AutoOcMasterEnabled = RegBool(key, "AutoOcMasterEnabled", false);
            ClockRatioMasterEnabled = RegBool(key, "ClockRatioMasterEnabled", false);
            PowerBalanceMasterEnabled = RegBool(key, "PowerBalanceMasterEnabled", false);
            PawnTurboMasterEnabled = RegBool(key, "PawnTurboMasterEnabled", false);
            PawnProchotMasterEnabled = RegBool(key, "PawnProchotMasterEnabled", false);
            PawnHwpMasterEnabled = RegBool(key, "PawnHwpMasterEnabled", false);
            PawnCStateMasterEnabled = RegBool(key, "PawnCStateMasterEnabled", false);
            PawnIgpuPowerMasterEnabled = RegBool(key, "PawnIgpuPowerMasterEnabled", false);
            PawnIgpuRatioMasterEnabled = RegBool(key, "PawnIgpuRatioMasterEnabled", false);
            NvTuningMasterEnabled = RegBool(key, "NvTuningMasterEnabled", false);
            RtssMasterEnabled = RegBool(key, "RtssMasterEnabled", false);
            AdlxMasterEnabled = RegBool(key, "AdlxMasterEnabled", false);
            AdlxRsrEnabled = RegBool(key, "AdlxRsrEnabled", false);
            AdlxRsrSharpness = RegInt(key, "AdlxRsrSharpness", 50);
            AdlxAntiLagEnabled = RegBool(key, "AdlxAntiLagEnabled", false);
            AdlxEnhancedSyncEnabled = RegBool(key, "AdlxEnhancedSyncEnabled", false);
            AdlxBoostEnabled = RegBool(key, "AdlxBoostEnabled", false);
            AdlxBoostPercent = RegInt(key, "AdlxBoostPercent", 0);
            AdlxImageSharpEnabled = RegBool(key, "AdlxImageSharpEnabled", false);
            AdlxImageSharpPercent = RegInt(key, "AdlxImageSharpPercent", 50);
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
          DisableDynamicBoost = RegInt(key, "DisableDynamicBoost", 0);
          Resolution = RegStr(key, "Resolution", "");
          DpiScale = RegInt(key, "DpiScale", 0);
          HdrEnabled = RegInt(key, "HdrEnabled", 0) == 1;
        }
      } catch (Exception ex) {
        Logger.Error($"Error loading configuration: {ex.Message}");
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

    // ponytail: get display name for a preset key. Checks the dict first,
    // then falls back to the legacy 3-field system for migration compatibility.
    public static string GetCustomPresetDisplayName(string presetKey) {
      if (CustomPresetNames.TryGetValue(presetKey, out var name) && !string.IsNullOrEmpty(name))
        return name;
      // legacy fallback for migration
      if (presetKey == "Custom1") return CustomPreset1Name;
      if (presetKey == "Custom2") return CustomPreset2Name;
      if (presetKey == "Custom3") return CustomPreset3Name;
      return presetKey;
    }

    public static void SetCustomPresetName(string presetKey, string displayName) {
      if (string.IsNullOrEmpty(presetKey) || string.IsNullOrEmpty(displayName)) return;
      CustomPresetNames[presetKey] = displayName;
      if (presetKey == "Custom1") CustomPreset1Name = displayName;
      else if (presetKey == "Custom2") CustomPreset2Name = displayName;
      else if (presetKey == "Custom3") CustomPreset3Name = displayName;
      try { CustomPresetNamesStore.Save(); } catch { }
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

  internal static class CustomPresetNamesStore {
    static string FilePath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OmenXHub", "preset_names.txt");

    public static void Save() {
      try {
        var dir = System.IO.Path.GetDirectoryName(FilePath);
        if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
        // ponytail: one line per key=value; skip empty
        var lines = ConfigService.CustomPresetNames
          .Where(kv => !string.IsNullOrEmpty(kv.Key) && !string.IsNullOrEmpty(kv.Value))
          .Select(kv => kv.Key + "=" + kv.Value)
          .ToArray();
        // also emit legacy 3-field lines for backward compat (first 3 lines = Custom1/2/3 names if present)
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(ConfigService.CustomPreset1Name);  // legacy line 1
        sb.AppendLine(ConfigService.CustomPreset2Name);  // legacy line 2
        sb.AppendLine(ConfigService.CustomPreset3Name);  // legacy line 3
        foreach (var l in lines) sb.AppendLine(l);
        System.IO.File.WriteAllText(FilePath, sb.ToString().TrimEnd());
      } catch (Exception ex) { Logger.Error("CustomPresetNamesStore.Save: " + ex.Message); }
    }

    public static void Load() {
      try {
        if (!System.IO.File.Exists(FilePath)) return;
        var lines = System.IO.File.ReadAllLines(FilePath);
        // legacy: first 3 lines (0-2) are Custom1/2/3 for backward compat
        if (lines.Length >= 1 && !string.IsNullOrEmpty(lines[0])) ConfigService.CustomPreset1Name = lines[0];
        if (lines.Length >= 2 && !string.IsNullOrEmpty(lines[1])) ConfigService.CustomPreset2Name = lines[1];
        if (lines.Length >= 3 && !string.IsNullOrEmpty(lines[2])) ConfigService.CustomPreset3Name = lines[2];
        // lines 3+ are key=value pairs for dynamic custom presets
        for (int i = 3; i < lines.Length; i++) {
          string line = lines[i];
          if (string.IsNullOrEmpty(line)) continue;
          int eq = line.IndexOf('=');
          if (eq <= 0) continue;
          string key = line.Substring(0, eq).Trim();
          string val = line.Substring(eq + 1).Trim();
          if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(val))
            ConfigService.CustomPresetNames[key] = val;
        }
      } catch { }
    }
  }
}