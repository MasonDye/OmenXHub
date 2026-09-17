// PresetManager.cs - 预设管理服务
// 内置默认预设 (Extreme/GpuPriority/LightUse)，自定义预设保存/加载，预设切换逻辑
// 自定义预设持久化到 {BaseDir}\Presets\{name}.json，兼容旧版注册表回退
//
// ── 参数分类 (见规格) ──
// 1.1 全局绑定 (所有预设)   : CpuPower/PL1/PL2, FanTable/FanControl, PowerMode, GpuClock, TGP/PPAB/Tpp, DState
// 1.2 自定义专属 (Custom): MaxFrameRate, RefreshRate, GpuCoreOC, GpuMemOC, PowerPlanGuid, CoreKeepEnabled, EcoQos
// 1.3 独立保存 (不受预设影响): IccMax, AcLoadLine, DBVersion/DisableDynamicBoost, TempSensitivity, Resolution, Dpi, Hdr, 灯光, 宏, 音频
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using Microsoft.Win32;
using OmenSuperHub;
using OmenSuperHub.Pages;
using OmenSuperHub.Services.CpuAffinity;

namespace OmenSuperHub.Services {
  [DataContract]
  public class PresetData {
    // ── 1.1 全局绑定参数 ──
    [DataMember(Order = 1)] public string CpuPower { get; set; } = "max";
    [DataMember(Order = 2)] public int CpuPowerPl1 { get; set; } = -1;
    [DataMember(Order = 3)] public int CpuPowerPl2 { get; set; } = -1;
    [DataMember(Order = 4)] public string FanTable { get; set; } = "silent";
    [DataMember(Order = 5)] public string FanControl { get; set; } = "auto";
    [DataMember(Order = 6)] public int PowerMode { get; set; } = 1;  // 0=最佳能效 1=平衡 2=最佳性能
    [DataMember(Order = 7)] public int GpuClock { get; set; } = 0;
    [DataMember(Order = 8)] public bool TgpEnabled { get; set; } = true;
    [DataMember(Order = 9)] public bool PpabEnabled { get; set; } = true;
    [DataMember(Order = 10)] public int Tpp { get; set; } = 0;

    // ── 1.2 自定义预设专属绑定参数 ──
    [DataMember(Order = 11)] public int DState { get; set; } = 1;  // 默认正常；仅自定义预设绑定
    [DataMember(Order = 12)] public int MaxFrameRate { get; set; } = -1;
    [DataMember(Order = 13)] public int RefreshRate { get; set; } = 0;
    [DataMember(Order = 14)] public int GpuCoreOverclock { get; set; } = -1;
    [DataMember(Order = 15)] public int GpuMemoryOverclock { get; set; } = -1;
    [DataMember(Order = 16)] public string PowerPlanGuid { get; set; } = "";
    [DataMember(Order = 17)] public bool CoreKeepEnabled { get; set; } = false;
    [DataMember(Order = 18)] public bool EcoQosEnabled { get; set; } = false;
    [DataMember(Order = 19)] public bool EcoQosThrottlePlugged { get; set; } = false;

    [DataMember(Order = 20)] public string CustomPresetName { get; set; } = "";

    // ── AMD CPU 调校参数 ──
    // ponytail: TDC/EDC/Tctl 已随高级调教删除（依赖 SMU 服务，本机不可用）；仅保留 PPT 走 WMI。
    [DataMember(Order = 21)] public int AmdCpuPpt { get; set; }
    internal bool IsFromCustomSubkey { get; set; } = false;

    // ponytail: 全字段均为值类型/字符串 — MemberwiseClone 即完整拷贝,供 LoadCustomPreset 缓存安全返回
    public PresetData CloneShallow() => (PresetData)MemberwiseClone();
  }

  internal static class PresetManager {
    static readonly string[] BuiltInKeys = { "Extreme", "GpuPriority", "LightUse" };

    public static event Action<string> OnPresetChanged;

    public static bool IsBuiltIn(string preset) => Array.IndexOf(BuiltInKeys, preset) >= 0;
    // ponytail: dynamic — any preset not in BuiltInKeys is custom (discovered from filesystem)
    public static bool IsCustom(string preset) => !IsBuiltIn(preset);

    // ponytail: enumerate *.json files in Presets dir, exclude built-in names.
    // Returns display-name → file-key pairs (file key is filename without .json).
    public static List<(string DisplayName, string FileKey)> EnumerateCustomPresets() {
      var list = new List<(string, string)>();
      try {
        if (!Directory.Exists(PresetsDir)) return list;
        foreach (var f in Directory.GetFiles(PresetsDir, "*.json")) {
          string key = Path.GetFileNameWithoutExtension(f);
          if (string.IsNullOrEmpty(key) || IsBuiltIn(key)) continue;
          // try to read CustomPresetName from the file for display
          string display = key;
          try {
            var d = LoadCustomPreset(key);
            if (d != null && !string.IsNullOrEmpty(d.CustomPresetName))
              display = d.CustomPresetName;
          } catch { }
          list.Add((display, key));
        }
      } catch { }
      return list;
    }

    /// <summary>自定义预设目录当前是否存在 —— 不存在时 EnumerateCustomPresets 返回空表
    /// 是"暂不可读"而非"全部已删除",调用方(设置页剪除死键)不得据此清用户配置。</summary>
    public static bool CustomPresetsDirExists() => Directory.Exists(PresetsDir);

    // ponytail: convenience — ordered list (built-ins first, then customs) for combo building
    public static List<(string DisplayName, string Key)> EnumerateAllPresets() {
      var all = new List<(string, string)> {
        (Strings.PresetExtreme, "Extreme"),
        (Strings.PresetGpuPriority, "GpuPriority"),
        (Strings.PresetLightUse, "LightUse"),
      };
      all.AddRange(EnumerateCustomPresets());
      return all;
    }

    // ═══════════════════════════════════════════════════════
    // 内置预设出厂默认值 — 仅 1.1 全局绑定参数
    // ═══════════════════════════════════════════════════════
    public static PresetData GetBuiltInDefaults(string preset) {
      // ponytail: per spec — only 1.1 global bound params. DState/1.2 not included (independent for built-in).
      var d = new PresetData();
      switch (preset) {
        case "Extreme":
          d.CpuPower = "max"; d.CpuPowerPl1 = 254; d.CpuPowerPl2 = 254;
          d.FanTable = "cool"; d.FanControl = "auto";
          d.PowerMode = 1;  // 平衡
          d.GpuClock = 0;   // 无限制
          d.TgpEnabled = true; d.PpabEnabled = true; d.Tpp = 254;
          d.AmdCpuPpt = 254;
          break;
        case "GpuPriority":
          d.CpuPower = "55 W"; d.CpuPowerPl1 = 55; d.CpuPowerPl2 = 55;
          d.FanTable = "balanced"; d.FanControl = "auto";
          d.PowerMode = 0;  // 最佳能效
          d.GpuClock = 0;
          d.TgpEnabled = true; d.PpabEnabled = true; d.Tpp = 254;
          d.AmdCpuPpt = 55;
          break;
        case "LightUse":
          d.CpuPower = "25 W"; d.CpuPowerPl1 = 25; d.CpuPowerPl2 = 25;
          d.FanTable = "silent"; d.FanControl = "auto";
          d.PowerMode = 0;  // 最佳能效
          d.GpuClock = 0;
          d.TgpEnabled = false; d.PpabEnabled = false; d.Tpp = 0;
          d.AmdCpuPpt = 30;
          break;
      }
      return d;
    }

    // ═══════════════════════════════════════════════════════
    // 将 PresetData 写入 ConfigService
    // 1.1 全局参数始终写入；1.2 自定义专属参数仅在 IsFromCustomSubkey 时写入
    // ═══════════════════════════════════════════════════════
    public static void ApplyPresetData(PresetData d) {
      // ponytail: heal corrupted Pl1/Pl2 from NumberBox Minimum-clamp bug.
      // NumberBox ValueChanged fires during layout with Minimum (1), writing that to
      // ConfigService; CaptureCurrent() then serializes the corrupt value to JSON.
      // If CpuPower is a valid wattage but Pl1 or Pl2 < 10 (clearly below any
      // real Omen PL setting), derive both from CpuPower.
      if ((d.CpuPowerPl1 < 10 || d.CpuPowerPl2 < 10) && !string.IsNullOrEmpty(d.CpuPower)) {
        int fallback = -1;
        if (d.CpuPower == "max") fallback = 254;
        else if (int.TryParse(d.CpuPower.Replace(" W", ""), out int w) && w >= 10 && w <= 254) fallback = w;
        // ponytail: heal only the corrupted field, preserve the valid one.
        if (fallback >= 0) {
          if (d.CpuPowerPl1 < 10) d.CpuPowerPl1 = fallback;
          if (d.CpuPowerPl2 < 10) d.CpuPowerPl2 = d.CpuPowerPl1 >= 10 ? d.CpuPowerPl1 : fallback;
        }
      }
      // ── 1.1 全局绑定参数 (所有预设) ──
      ConfigService.CpuPower = d.CpuPower;
      ConfigService.CpuPowerPl1 = d.CpuPowerPl1;
      ConfigService.CpuPowerPl2 = d.CpuPowerPl2;
      ConfigService.FanTable = d.FanTable;
      ConfigService.FanControl = d.FanControl;
      ConfigService.PowerMode = d.PowerMode;
      ConfigService.GpuClock = d.GpuClock;
      ConfigService.TgpEnabled = d.TgpEnabled;
      ConfigService.PpabEnabled = d.PpabEnabled;
      ConfigService.Tpp = d.Tpp;

      // ── AMD CPU 调校（始终写入，内置预设也有意义） ──
      if (d.AmdCpuPpt > 0) ConfigService.AmdCpuPpt = d.AmdCpuPpt;

      // ── 1.2 自定义预设专属绑定参数 (仅自定义预设) ──
      // 内置预设不触碰这些参数，保持独立 (其他保持原值)。
      // DState 例外:内置预设 GetBuiltInDefaults 默认 1=正常,须随预设切换写回,
      // 否则用户在 PerfPage 手动设 2=低功耗后切回 Extreme/GpuPriority 仍残留 2 无法恢复。
      ConfigService.DState = d.DState;
      if (d.IsFromCustomSubkey) {
        ConfigService.MaxFrameRate = d.MaxFrameRate;
        ConfigService.RefreshRate = d.RefreshRate;
        ConfigService.GpuCoreOverclock = d.GpuCoreOverclock;
        ConfigService.GpuMemoryOverclock = d.GpuMemoryOverclock;
        ConfigService.PowerPlanGuid = d.PowerPlanGuid;
        ConfigService.EcoQosEnabled = d.EcoQosEnabled;
        ConfigService.EcoQosThrottlePlugged = d.EcoQosThrottlePlugged;
        // CoreKeep 由 SwitchPreset 单独处理 (需调 CoreKeepService)
      }
      // 1.3 独立参数不在 PresetData 中，永不受预设切换影响

      // ponytail: persist ALL preset-bound fields to registry immediately.
      // Without this, TrayService.RestoreConfig() → ConfigService.Load() re-reads STALE
      // registry values from the previous session, overwriting the preset values that
      // SwitchPreset just set. Then the exit save captures those stale values → JSON reset.
      try {
        ConfigService.Save("CpuPower"); ConfigService.Save("CpuPowerPl1"); ConfigService.Save("CpuPowerPl2");
        ConfigService.Save("FanTable"); ConfigService.Save("FanControl"); ConfigService.Save("PowerMode");
        ConfigService.Save("GpuClock"); ConfigService.Save("TgpEnabled"); ConfigService.Save("PpabEnabled");
        ConfigService.Save("Tpp");
        ConfigService.Save("AmdCpuPpt");
        ConfigService.Save("DState"); // 须与 line 172 赋值同步,否则重启 Load() 回读旧 2 复发 bug
        if (d.IsFromCustomSubkey) {
          ConfigService.Save("MaxFrameRate"); ConfigService.Save("RefreshRate");
          ConfigService.Save("GpuCoreOverclock"); ConfigService.Save("GpuMemoryOverclock");
          ConfigService.Save("PowerPlanGuid"); ConfigService.Save("EcoQosEnabled"); ConfigService.Save("EcoQosThrottlePlugged");
        }
      } catch (Exception ex) {
        Logger.Warn($"[PresetManager] ApplyPresetData: preset-bound registry save failed: {ex.Message}");
      }
    }

    // ═══════════════════════════════════════════════════════
    // 从 ConfigService 当前值捕获 PresetData (仅 1.1 + 1.2，不含 1.3)
    // ═══════════════════════════════════════════════════════
    public static PresetData CaptureCurrent() {
      var d = new PresetData {
        // 1.1
        CpuPower = ConfigService.CpuPower,
        CpuPowerPl1 = ConfigService.CpuPowerPl1,
        CpuPowerPl2 = ConfigService.CpuPowerPl2,
        FanTable = ConfigService.FanTable,
        FanControl = ConfigService.FanControl,
        PowerMode = ConfigService.PowerMode,
        GpuClock = ConfigService.GpuClock,
        TgpEnabled = ConfigService.TgpEnabled,
        PpabEnabled = ConfigService.PpabEnabled,
        Tpp = ConfigService.Tpp,
        // AMD CPU 调校
        AmdCpuPpt = ConfigService.AmdCpuPpt,
        // 1.2
        DState = ConfigService.DState,
        MaxFrameRate = ConfigService.MaxFrameRate,
        RefreshRate = ConfigService.RefreshRate,
        GpuCoreOverclock = ConfigService.GpuCoreOverclock,
        GpuMemoryOverclock = ConfigService.GpuMemoryOverclock,
        // ponytail: fall back to OS active power plan so new presets capture current state
        PowerPlanGuid = string.IsNullOrEmpty(ConfigService.PowerPlanGuid)
          ? GetActivePowerPlanGuid()
          : ConfigService.PowerPlanGuid,
        EcoQosEnabled = ConfigService.EcoQosEnabled,
        EcoQosThrottlePlugged = ConfigService.EcoQosThrottlePlugged,
      };
      // CoreKeep master toggle state
      try { d.CoreKeepEnabled = CoreKeepService.Load().MasterEnabled; } catch { }
      return d;
    }

    // ═══════════════════════════════════════════════════════
    // 自定义预设持久化 — JSON 文件 (同 FanCurves 模式)
    // ═══════════════════════════════════════════════════════
    static string PresetsDir => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Presets");
    static string PresetFilePath(string name) => Path.Combine(PresetsDir, $"{name}.json");

    static string SerializePreset(PresetData d) {
      using (var ms = new MemoryStream()) {
        var ser = new DataContractJsonSerializer(typeof(PresetData));
        ser.WriteObject(ms, d);
        return Encoding.UTF8.GetString(ms.ToArray());
      }
    }

    static PresetData DeserializePreset(string json) {
      using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json))) {
        var ser = new DataContractJsonSerializer(typeof(PresetData));
        return (PresetData)ser.ReadObject(ms);
      }
    }

    // ponytail: save under any custom name. If customPresetName is provided, use it
    // as the display name inside the file AND as the file key (sanitized).
    public static void SaveCustomPreset(string presetKey, string customPresetName = null) {
      if (IsBuiltIn(presetKey)) return;
      var d = CaptureCurrent();
      d.CustomPresetName = customPresetName ?? presetKey;
      try {
        Directory.CreateDirectory(PresetsDir);
        // ponytail: temp + Replace 原子换入(同 MacroService.Save / CoreKeepService.Save) ——
        // net481 无 File.Move(overwrite);直接 WriteAllText 若崩溃/断电于写一半,留下截断
        // JSON,下次 LoadCustomPreset 解析失败回退注册表,用户改动静默丢失。
        string path = PresetFilePath(presetKey);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, SerializePreset(d), Encoding.UTF8);
        if (File.Exists(path)) File.Replace(tmp, path, null);
        else File.Move(tmp, path);
        // ponytail: 配套落盘按预设风扇曲线文件，避免首次重启回退到默认平衡曲线
        FanService.EnsurePresetCurveFile(presetKey);
      } catch (Exception ex) {
        Console.WriteLine($"Error saving custom preset to file: {ex.Message}");
        try { SaveCustomPresetToRegistry(presetKey, d); }
        catch (Exception ex2) { Logger.Warn($"[PresetManager] SaveCustomPreset: registry fallback for '{presetKey}' also failed: {ex2.Message}"); }
      }
    }

    // ponytail: delete custom preset file
    public static void DeleteCustomPreset(string presetKey) {
      if (IsBuiltIn(presetKey)) return;
      try {
        string path = PresetFilePath(presetKey);
        if (File.Exists(path)) File.Delete(path);
        // also clean registry fallback
        try { Registry.CurrentUser.DeleteSubKeyTree(PresetSubKey(presetKey)); }
        catch (Exception ex) { Logger.Warn($"[PresetManager] DeleteCustomPreset: registry cleanup for '{presetKey}' failed: {ex.Message}"); }
      } catch (Exception ex) {
        Logger.Warn($"[PresetManager] DeleteCustomPreset '{presetKey}': {ex.Message}");
      }
    }

    // ponytail: LoadCustomPreset 被 Dashboard 雷达/圆环每 tick 调用 — 按 (路径, LastWriteTimeUtc)
    // memoize,文件未变时零 IO/零 JSON(mtime stat 由 OS 缓存,微秒级)。返回私有实例,调用方可改。
    // 上限:注册表回退结果也按文件 mtime 缓存 — 纯注册表预设(迁移场景)的改动要等文件出现才失效。
    static readonly Dictionary<string, (DateTime stamp, PresetData data)> _customPresetLoadCache
      = new Dictionary<string, (DateTime, PresetData)>();
    static readonly object _loadCacheLock = new object();

    public static PresetData LoadCustomPreset(string presetKey) {
      if (!IsCustom(presetKey)) return null;
      string path = PresetFilePath(presetKey);
      var stamp = File.GetLastWriteTimeUtc(path);  // 文件不存在返回 1601,常量可作缓存键
      lock (_loadCacheLock) {
        if (_customPresetLoadCache.TryGetValue(path, out var hit) && hit.stamp == stamp)
          return hit.data.CloneShallow();
      }
      PresetData d = null;
      if (File.Exists(path)) {
        try {
          d = DeserializePreset(File.ReadAllText(path, Encoding.UTF8));
        } catch (Exception ex) {
          Console.WriteLine($"Error loading custom preset from file: {ex.Message}");
        }
      }
      if (d == null) {
        try { d = LoadCustomPresetFromRegistry(presetKey); } catch (Exception ex) { Logger.Warn($"[PresetManager] LoadCustomPresetFromRegistry({presetKey}) failed: {ex.Message}"); }
      }
      if (d == null) return null;
      d.IsFromCustomSubkey = true;
      lock (_loadCacheLock) { _customPresetLoadCache[path] = (stamp, d.CloneShallow()); }
      return d;
    }

    // ── 旧注册表持久化 (回退/迁移用) ──
    static string PresetSubKey(string name) => $@"Software\OmenXHub\Presets\{name}";

    static void SaveCustomPresetToRegistry(string presetKey, PresetData d) {
      using (RegistryKey key = Registry.CurrentUser.CreateSubKey(PresetSubKey(presetKey))) {
        if (key == null) return;
        // 1.1
        key.SetValue("CpuPower", d.CpuPower);
        key.SetValue("CpuPowerPl1", d.CpuPowerPl1);
        key.SetValue("CpuPowerPl2", d.CpuPowerPl2);
        key.SetValue("FanTable", d.FanTable);
        key.SetValue("FanControl", d.FanControl);
        key.SetValue("PowerMode", d.PowerMode);
        key.SetValue("GpuClock", d.GpuClock);
        key.SetValue("TgpEnabled", d.TgpEnabled ? 1 : 0);
        key.SetValue("PpabEnabled", d.PpabEnabled ? 1 : 0);
        key.SetValue("Tpp", d.Tpp);
        // 1.2
        key.SetValue("DState", d.DState);
        key.SetValue("MaxFrameRate", d.MaxFrameRate);
        key.SetValue("RefreshRate", d.RefreshRate);
        key.SetValue("GpuCoreOverclock", d.GpuCoreOverclock);
        key.SetValue("GpuMemoryOverclock", d.GpuMemoryOverclock);
        key.SetValue("PowerPlanGuid", d.PowerPlanGuid);
        key.SetValue("CoreKeepEnabled", d.CoreKeepEnabled ? 1 : 0);
        key.SetValue("EcoQosEnabled", d.EcoQosEnabled ? 1 : 0);
        key.SetValue("EcoQosThrottlePlugged", d.EcoQosThrottlePlugged ? 1 : 0);
        // ponytail: AMD CPU tuning — JSON path stores these via DataMember, but the
        // registry fallback omitted them, so an AMD machine falling back to registry
        // silently dropped PPT. Keep in sync with the JSON member set. (TDC/EDC/Tctl removed.)
        key.SetValue("AmdCpuPpt", d.AmdCpuPpt);
        if (!string.IsNullOrEmpty(d.CustomPresetName)) key.SetValue("CustomPresetName", d.CustomPresetName);
      }
    }

    static PresetData LoadCustomPresetFromRegistry(string presetKey) {
      if (!IsCustom(presetKey)) return null;
      var d = new PresetData();
      using (RegistryKey key = Registry.CurrentUser.OpenSubKey(PresetSubKey(presetKey))) {
        if (key == null) return null;
        // 1.1
        d.CpuPower = ConfigService.RegStr(key, "CpuPower", d.CpuPower);
        d.CpuPowerPl1 = ConfigService.RegInt(key, "CpuPowerPl1", d.CpuPowerPl1);
        d.CpuPowerPl2 = ConfigService.RegInt(key, "CpuPowerPl2", d.CpuPowerPl2);
        d.FanTable = ConfigService.RegStr(key, "FanTable", d.FanTable);
        d.FanControl = ConfigService.RegStr(key, "FanControl", d.FanControl);
        d.PowerMode = ConfigService.RegInt(key, "PowerMode", d.PowerMode);
        d.GpuClock = ConfigService.RegInt(key, "GpuClock", d.GpuClock);
        d.TgpEnabled = ConfigService.RegBool(key, "TgpEnabled", d.TgpEnabled);
        d.PpabEnabled = ConfigService.RegBool(key, "PpabEnabled", d.PpabEnabled);
        d.Tpp = ConfigService.RegInt(key, "Tpp", d.Tpp);
        // 1.2
        d.DState = ConfigService.RegInt(key, "DState", d.DState);
        d.MaxFrameRate = ConfigService.RegInt(key, "MaxFrameRate", d.MaxFrameRate);
        d.RefreshRate = ConfigService.RegInt(key, "RefreshRate", d.RefreshRate);
        d.GpuCoreOverclock = ConfigService.RegInt(key, "GpuCoreOverclock", d.GpuCoreOverclock);
        d.GpuMemoryOverclock = ConfigService.RegInt(key, "GpuMemoryOverclock", d.GpuMemoryOverclock);
        d.PowerPlanGuid = ConfigService.RegStr(key, "PowerPlanGuid", d.PowerPlanGuid);
        d.CoreKeepEnabled = ConfigService.RegBool(key, "CoreKeepEnabled", false);
        d.EcoQosEnabled = ConfigService.RegBool(key, "EcoQosEnabled", false);
        d.EcoQosThrottlePlugged = ConfigService.RegBool(key, "EcoQosThrottlePlugged", false);
        // ponytail: AMD CPU tuning — match SaveCustomPresetToRegistry. (TDC/EDC/Tctl removed.)
        d.AmdCpuPpt = ConfigService.RegInt(key, "AmdCpuPpt", d.AmdCpuPpt);
      }
      return d;
    }

    // ═══════════════════════════════════════════════════════
    // 预设切换主入口 — 原子性：先写 ConfigService，再应用硬件，最后触发事件
    // ═══════════════════════════════════════════════════════
    public static void SwitchPreset(string preset) {
      string prevPreset = ConfigService.Preset;

      // 离开旧自定义预设时：保存当前绑定参数
      if (!string.IsNullOrEmpty(prevPreset) && prevPreset != preset && IsCustom(prevPreset))
        SaveCustomPreset(prevPreset);

      PresetData data;
      if (IsBuiltIn(preset))
        data = GetBuiltInDefaults(preset);
      else
        data = LoadCustomPreset(preset) ?? GetBuiltInDefaults("GpuPriority");

      if (data == null) return;

      // ponytail: 内置预设的风扇档(含手动/固定 RPM)是「临时绑定」——切走即丢、重启回到
      // 预设的 FanTable 语义默认(Extreme=cool / GpuPriority=balanced / LightUse=silent)。
      // 不再从 Presets\<preset> 子键读回任何 FanControl 覆盖硬编码 "auto"(旧逻辑会把用户
      // 手动选的 "4000 RPM" 跨切换/重启复活,导致"改其他档后仍回手动")。
      // 自定义预设的完整风扇绑定(含手动)走 LoadCustomPreset 的 JSON 恢复,不在此处。
      // 1. 写入 ConfigService (1.1 始终写入，1.2 仅自定义)
      ApplyPresetData(data);

      // 2. 同步自定义预设名到 ConfigService
      if (data.IsFromCustomSubkey && !string.IsNullOrEmpty(data.CustomPresetName)) {
        ConfigService.SetCustomPresetName(preset, data.CustomPresetName);
      }

      // 3. CoreKeep — 自定义预设恢复 CoreKeep 开关状态
      if (data.IsFromCustomSubkey) {
        try {
          var ckData = CoreKeepService.Load();
          if (ckData.MasterEnabled != data.CoreKeepEnabled) {
            ckData.MasterEnabled = data.CoreKeepEnabled;
            CoreKeepService.Save(ckData);
            if (data.CoreKeepEnabled) CoreKeepService.StartAutoApply(ckData);
            else CoreKeepService.StopAutoApply();
          }
        } catch (Exception ex) {
          Logger.Warn($"[PresetManager] SwitchPreset '{preset}': CoreKeep state sync failed: {ex.Message}");
        }
      }

      ConfigService.Preset = preset;
      ConfigService.Save("Preset");
      // ponytail: marshal to UI thread — called from ThreadPool (automation triggers)
      try {
        var app = System.Windows.Application.Current;
        if (app != null && app.Dispatcher != null && !app.Dispatcher.CheckAccess())
          app.Dispatcher.Invoke(() => OnPresetChanged?.Invoke(preset));
        else
          OnPresetChanged?.Invoke(preset);
      } catch { }
    }

    // ═══════════════════════════════════════════════════════
    // 电源计划 helper — P/Invoke 复用 Pages/NativeMethods.cs
    // ═══════════════════════════════════════════════════════
    static string GetActivePowerPlanGuid() {
      try {
        IntPtr ptr;
        if (NativeMethods_Power.PowerGetActiveScheme(IntPtr.Zero, out ptr) != 0 || ptr == IntPtr.Zero) return "";
        // ponytail: free HGlobal in finally. If PtrToStructure ever throws, the old
        // code leaked unmanaged memory; power APIs allocate repeatedly so it accumulates.
        try {
          return Marshal.PtrToStructure<Guid>(ptr).ToString();
        } finally {
          Marshal.FreeHGlobal(ptr);
        }
      } catch (Exception ex) { Logger.Verbose("GetActivePowerPlanGuid: " + ex.Message); }
      return "";
    }

    // ── 电源模式覆盖 (Power Mode overlay) ──
    // ponytail: 返回 null=成功,否则失败原因 —— 供 AwaitableApplyPresetHardware 聚合。
    // 原实现吞异常/返回码只写 Verbose,预设切换时 overlay 失败完全静默。
    static string ApplyPowerModeOverlay(int powerMode) {
      try {
        Guid g;
        if (powerMode == 0) g = NativeMethods_Power.BEST_POWER_EFFICIENCY;
        else if (powerMode == 2) g = NativeMethods_Power.BEST_PERFORMANCE;
        else g = Guid.Empty;  // 1=平衡 → 默认
        uint ret = NativeMethods_Power.PowerSetActiveOverlayScheme(g);
        if (ret != 0) Logger.Verbose($"ApplyPowerModeOverlay: ret={ret}");
        return ret != 0 ? $"PowerSetActiveOverlayScheme ret={ret}" : null;
      } catch (Exception ex) { Logger.Verbose("ApplyPowerModeOverlay: " + ex.Message); return ex.Message; }
    }

    // ═══════════════════════════════════════════════════════
    // ponytail: 高级调教已全数移除（机型不可用）。仅保留 AMD PPT 经 WMI 复写，
    // 作为预设切换 / PerfPage.Reload 后的状态恢复入口。
    // 上限：WMI 路径仅接受 0~255W；超过此范围的 PPT 由 PerfPage 滑条上限约束。
    // ponytail: 返回失败原因列表(空=成功)。WMI bool 返回值、SMU 状态码原本被丢弃,
    // 现在经 AwaitableApplyPresetHardware 聚合传播。本方法内部同时记一条 Error,
    // PerfPage 等忽略返回值的调用点也因此可见失败(与注释语义对齐,不再静默)。
    internal static System.Collections.Generic.List<string> ApplyAdvanced() {
      var failures = new System.Collections.Generic.List<string>();
      try {
        if (OmenHardware.HasAmdCpu() && ConfigService.AmdCpuPpt > 0 && ConfigService.AmdCpuPpt <= 255
            && !OmenHardware.SetCpuPowerLimit((byte)ConfigService.AmdCpuPpt))
          failures.Add($"AMD PPT WMI 写入被拒 (ppt={ConfigService.AmdCpuPpt})");
      } catch (Exception ex) { failures.Add("AMD PPT: " + ex.Message); }
      // ponytail: AMD Curve Optimizer 全核+分核降压 — SMU 写易失,每次预设切换重应用。
      // 仅需 PawnIO 驱动就绪,不依赖 EnableEcAccess 开关。
      try {
        var svc = Services.AmdUndervoltService.Instance;
        if (svc.IsAvailable) {
          if (ConfigService.AmdCpuUndervolt != 0) {
            var co = svc.SetAllCoreCO(ConfigService.AmdCpuUndervolt);
            if (co != Services.SmuStatus.Ok)
              failures.Add($"SetAllCoreCO({ConfigService.AmdCpuUndervolt}) 返回 {co}");
          }
          var perCore = Services.AmdUndervoltService.ParsePerCoreOffsets(ConfigService.AmdCpuPerCoreOffsets);
          if (perCore.Count > 0) {
            int okCount = svc.ApplyPerCoreCO(perCore);
            int expected = System.Linq.Enumerable.Count(perCore, kv => kv.Value != 0);
            if (okCount < expected)
              failures.Add($"分核 CO 仅 {okCount}/{expected} 核成功");
          }
        }
      } catch (Exception ex) { failures.Add("AMD CO: " + ex.Message); }
      if (failures.Count > 0)
        Logger.Error($"[ApplyAdvanced] {failures.Count} 项失败: {string.Join(" | ", failures)}");
      return failures;
    }

    // ═══════════════════════════════════════════════════════
    // 硬件应用 — 由 MainWindow.ApplyPresetHardware 调用
    // 1.1 始终应用；1.2 仅当当前预设为自定义时应用
    // ═══════════════════════════════════════════════════════
    public static void ApplyPresetHardware() => AwaitableApplyPresetHardware();

    // ponytail: 新增可等待版本 —— 旧版把工作甩到 ThreadPool 后立即返回,
    // AutomationProcessor 里多个步骤连发时无法保证"SetPreset 先把 GPU/CPU 功率写完,
    // 再跑下一个 SetGpuPower/SetCpuPower 步骤",后写者可能反向覆盖前面写入。
    // 这里只在原 QueueUserWorkItem 外套 TaskCompletionSource, 工作体逻辑不变。
    // ponytail: 结果携带失败步骤列表(步骤名+原因)。调用方 await 后若列表非空,
    // 必须当作"部分应用"处理:配置已持久化但硬件实态不完整,UI 不得静默显示成功。
    // 语义边界:空列表 = "未收集到失败" —— SetFanModeCompat/SetGpuPowerState/
    // SetMaxFanSpeedOff 等 void 包装(WMI 返回值在 OmenHardware 层被吞)与"CO 已配置
    // 但驱动不可用而跳过"不计入;也不代表读回验证。根治需改 OmenHardware 签名。
    public static System.Threading.Tasks.Task<string[]> AwaitableApplyPresetHardware() {
      var tcs = new System.Threading.Tasks.TaskCompletionSource<string[]>();
      int gpuClock = ConfigService.GpuClock;
      bool tgp = ConfigService.TgpEnabled;
      bool ppab = ConfigService.PpabEnabled;
      string cpuPwr = ConfigService.CpuPower;
      int powerMode = ConfigService.PowerMode;

      System.Threading.ThreadPool.QueueUserWorkItem(_ => {
        // ponytail: 聚合失败步骤 —— 每步失败只记录不中断(步骤间大多相互独立,
        // 中断会让后面的风扇配置整个丢失,比部分应用更糟)。
        var failed = new System.Collections.Generic.List<string>();
        void Step(string name, Action a) {
          try { a(); } catch (Exception ex) { failed.Add($"{name}: {ex.Message}"); }
        }
        // bool 返回值的 WMI/驱动调用:不抛异常仅返回 false 的失败也要计入
        void Ok(string name, bool r) { if (!r) failed.Add($"{name}: 驱动/BIOS 拒绝"); }
        void Check(string name, System.Collections.Generic.List<string> items) { failed.AddRange(items); }
        try {
        // ponytail: App.xaml.cs 启动时调的 SetFanMode(0x31) 可能在 EC/WMI 就绪前就跑，
        // 失败后没有重试；而 CPU 功率限制依赖 EC 处于 unleash mode 才会真正生效。
        // 在这里再补一刀，确保功率限不会被 EC 忽略。
        Step("SetFanMode", () => OmenHardware.SetFanModeCompat(0x31));
        // ── 1.1 全局绑定参数 ──
        Step("GpuClockLimit", () => TrayService.SetGPUClockLimit(gpuClock));
        Step("CpuPowerLimit", () => {
          // ponytail: apply PL1 and PL2 independently from ConfigService.
          int pl1 = ConfigService.CpuPowerPl1;
          int pl2 = ConfigService.CpuPowerPl2;
          if (cpuPwr == "max") Ok("CpuPowerLimit", OmenHardware.SetCpuPowerLimit(254, 254));
          else if (cpuPwr == "null") { /* keep BIOS default */ }
          else if (pl1 >= 10 && pl1 <= 254 && pl2 >= 10 && pl2 <= 254)
            Ok("CpuPowerLimit", OmenHardware.SetCpuPowerLimit((byte)pl1, (byte)pl2));
          else if (int.TryParse(cpuPwr?.Replace(" W", ""), out int cpuVal) && cpuVal >= 10 && cpuVal <= 254)
            Ok("CpuPowerLimit", OmenHardware.SetCpuPowerLimit((byte)cpuVal, (byte)cpuVal));
        });
        // ponytail: TPP (ConcurrentTDP) — total power budget for CPU+GPU combined.
        // Without this, EC uses a conservative default budget → dual-stress (CPU+GPU)
        // throttles because each component fights for a share of a capped total.
        // MUST be written BEFORE SetGpuPowerState — PPAB dynamic power sharing
        // reads the TPP budget to decide how much power to allocate to GPU, so
        // if TPP is still the BIOS default (~155W), PPAB caps GPU power within
        // that small budget and CPU doesn't get its share.
        Step("ConcurrentTdp", () => { if (ConfigService.Tpp >= 20) Ok("ConcurrentTdp", OmenHardware.SetConcurrentTdp((byte)ConfigService.Tpp)); });
        Step("GpuPowerState", () => OmenHardware.SetGpuPowerState(tgp, ppab, ConfigService.DState == 2 ? 2 : 1));
        Step("PowerModeOverlay", () => { var r = ApplyPowerModeOverlay(powerMode); if (r != null) failed.Add("PowerModeOverlay: " + r); });

        // ponytail: 高级调教已删除；ApplyAdvanced 现仅写 AMD PPT 经 WMI。
        Step("Advanced", () => Check("Advanced", ApplyAdvanced()));

        // ── 风扇配置 ──
        Step("FanConfig", () => {
          string fc = ConfigService.FanControl;
          string ft = ConfigService.FanTable;
          if (fc == "smart" || fc == "custom") {
            // ponytail: scrap the redundant LoadFanConfig(silent.txt) before ApplyPresetCurve.
            // ApplyPresetCurve clears CPUTempFanMap/GPUTempFanMap anyway, so the LoadFanConfig
            // only stuffs cool/silent.txt into the maps for a few EMA ticks before being thrown
            // away, skewing smart-fan startup. ApplyPresetCurve's internal GetDefaultPresetCurve
            // already gives per-preset fallback when no custom_{preset}.txt exists.
            FanService.InitSmartFanState(ConfigService.SmartFanEmaAlpha);
            FanService.ApplyPresetCurve(ConfigService.Preset);
            OmenHardware.SetMaxFanSpeedOff();
            TrayService.fanControlTimer?.Change(0, 1000);
          } else if (fc != null && fc.Contains(" RPM")) {
            int rpm = FanService.ParseFanRpm(fc);
            byte speed = (byte)(rpm / 100);
            if (speed < 0) speed = 0; if (speed > 100) speed = 100;
            OmenHardware.SetMaxFanSpeedOff();
            OmenHardware.SetFanLevel(0, 0, fan3: OmenHardware.IsThreeFan());
            OmenHardware.SetFanLevel(speed, speed, fan3: OmenHardware.IsThreeFan());
            TrayService.fanControlTimer?.Change(Timeout.Infinite, Timeout.Infinite);
          } else if (fc != null && fc.EndsWith("%") && int.TryParse(fc.TrimEnd('%'), out int pct)) {
            // ponytail: 固定百分比档 —— 与 RestoreFanSettings 的 % 分支同语义(停心跳 +
            // 直写 pct 档位)。旧版 "%" 落下方 else 被当 auto 曲线表处理,预设切换与
            // 重启恢复行为不一致。非法 % 值仍落 else 走 auto 表(有兜底的风扇控制)。
            pct = pct < 0 ? 0 : pct > 100 ? 100 : pct;
            OmenHardware.SetMaxFanSpeedOff();
            OmenHardware.SetFanLevel(pct, pct, fan3: OmenHardware.IsThreeFan());
            TrayService.fanControlTimer?.Change(Timeout.Infinite, Timeout.Infinite);
          } else {
            // ponytail: 按 FanTable 选用全局曲线文件 —— cool/silent/balanced 三档平级。
            // balanced.txt = G-Helper Balanced (主要给 GpuPriority 预设用)。
            string table = ft == "cool" ? "cool.txt"
                         : ft == "balanced" ? "balanced.txt"
                         : "silent.txt";
            FanService.LoadFanConfig(table);
            OmenHardware.SetMaxFanSpeedOff();
            TrayService.fanControlTimer?.Change(0, 1000);
          }
        });

        // ── 1.2 自定义预设专属绑定参数 ──
        if (IsCustom(ConfigService.Preset)) {
          // GPU 超频
          Step("GpuCoreOC", () => GpuAppManager.SetCoreClockOffset(ConfigService.GpuCoreOverclock));
          Step("GpuMemOC", () => GpuAppManager.SetMemoryClockOffset(ConfigService.GpuMemoryOverclock));
          // 最大帧率
          Step("MaxFrameRate", () => {
            int fps = ConfigService.MaxFrameRate;
            if (fps > 0) HP.Omen.Core.Common.NVidiaApi.NvApiWrapper.NVAPI_SetMaxFrameRate(fps);
            else HP.Omen.Core.Common.NVidiaApi.NvApiWrapper.NVAPI_SetMaxFrameRate(0);
          });
          // 电源计划
          Step("PowerPlan", () => {
            if (!string.IsNullOrEmpty(ConfigService.PowerPlanGuid)) {
              Guid g = Guid.Parse(ConfigService.PowerPlanGuid);
              uint r = NativeMethods_Power.PowerSetActiveScheme(IntPtr.Zero, ref g);
              if (r != 0) failed.Add($"PowerPlan: PowerSetActiveScheme ret={r}");
            }
          });
          // EcoQoS
          Step("EcoQos", () => {
            EcoQosService.SetEnabled(ConfigService.EcoQosEnabled);
            EcoQosService.SetThrottlePlugged(ConfigService.EcoQosThrottlePlugged);
          });
          // 刷新率
          Step("RefreshRate", () => {
            if (ConfigService.RefreshRate > 0)
              TrayService.ApplyRefreshRate(ConfigService.RefreshRate);
          });
          // 风扇曲线 (自定义预设专属持久化)
          Step("PresetFanCurve", () => FanService.ApplyPresetCurve(ConfigService.Preset));
        }
        } catch (Exception ex) {
          // 兜底:步骤体外任何抛出(含聚合器自身)不得吞掉任务完成 —— 否则 await 方永久悬挂
          failed.Add($"Fatal: {ex.Message}");
          Logger.Error($"[PresetApply] 工作体未捕获异常: {ex}");
        }
        // ponytail: SetResult 先于 OSD 与日志 —— ShowTextOsd 内部 Dispatcher.Invoke 可抛
        // (窗口创建失败等),若排在完成语句前会把异常吞进兜底外导致任务不完成。
        // 失败 OSD 单点弹给所有 fire-and-forget 调用方(MainWindow/TrayService/各页面
        // 共 11+ 处,旧行为是弹完"预设已应用"再静默吞失败)。
        var snapshot = failed.ToArray();
        tcs.TrySetResult(snapshot);
        if (snapshot.Length > 0) {
          Logger.Error($"[PresetApply] {ConfigService.Preset} 部分应用, {snapshot.Length} 步失败: {string.Join(" | ", snapshot)}");
          try { Views.OsdWindow.ShowTextOsd($"预设部分应用:{snapshot.Length} 步失败(详见日志)", force: true); }
          catch (Exception osdEx) { Logger.Error("[PresetApply] 失败 OSD 弹出异常: " + osdEx.Message); }
        }
      });
      return tcs.Task;
    }
  }
}
