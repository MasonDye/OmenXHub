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
      } catch { }
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
        File.WriteAllText(PresetFilePath(presetKey), SerializePreset(d), Encoding.UTF8);
        // ponytail: 配套落盘按预设风扇曲线文件，避免首次重启回退到默认平衡曲线
        FanService.EnsurePresetCurveFile(presetKey);
      } catch (Exception ex) {
        Console.WriteLine($"Error saving custom preset to file: {ex.Message}");
        try { SaveCustomPresetToRegistry(presetKey, d); } catch { }
      }
    }

    // ponytail: delete custom preset file
    public static void DeleteCustomPreset(string presetKey) {
      if (IsBuiltIn(presetKey)) return;
      try {
        string path = PresetFilePath(presetKey);
        if (File.Exists(path)) File.Delete(path);
        // also clean registry fallback
        try { Registry.CurrentUser.DeleteSubKeyTree(PresetSubKey(presetKey)); } catch { }
      } catch { }
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
        try { d = LoadCustomPresetFromRegistry(presetKey); } catch { }
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
        d.CpuPower = (string)key.GetValue("CpuPower", d.CpuPower);
        d.CpuPowerPl1 = (int)key.GetValue("CpuPowerPl1", d.CpuPowerPl1);
        d.CpuPowerPl2 = (int)key.GetValue("CpuPowerPl2", d.CpuPowerPl2);
        d.FanTable = (string)key.GetValue("FanTable", d.FanTable);
        d.FanControl = (string)key.GetValue("FanControl", d.FanControl);
        d.PowerMode = (int)key.GetValue("PowerMode", d.PowerMode);
        d.GpuClock = (int)key.GetValue("GpuClock", d.GpuClock);
        d.TgpEnabled = Convert.ToInt32(key.GetValue("TgpEnabled", d.TgpEnabled ? 1 : 0)) == 1;
        d.PpabEnabled = Convert.ToInt32(key.GetValue("PpabEnabled", d.PpabEnabled ? 1 : 0)) == 1;
        d.Tpp = (int)key.GetValue("Tpp", d.Tpp);
        // 1.2
        d.DState = (int)key.GetValue("DState", d.DState);
        d.MaxFrameRate = (int)key.GetValue("MaxFrameRate", d.MaxFrameRate);
        d.RefreshRate = (int)key.GetValue("RefreshRate", d.RefreshRate);
        d.GpuCoreOverclock = (int)key.GetValue("GpuCoreOverclock", d.GpuCoreOverclock);
        d.GpuMemoryOverclock = (int)key.GetValue("GpuMemoryOverclock", d.GpuMemoryOverclock);
        d.PowerPlanGuid = (string)key.GetValue("PowerPlanGuid", d.PowerPlanGuid);
        d.CoreKeepEnabled = Convert.ToInt32(key.GetValue("CoreKeepEnabled", 0)) == 1;
        d.EcoQosEnabled = Convert.ToInt32(key.GetValue("EcoQosEnabled", 0)) == 1;
        d.EcoQosThrottlePlugged = Convert.ToInt32(key.GetValue("EcoQosThrottlePlugged", 0)) == 1;
        // ponytail: AMD CPU tuning — match SaveCustomPresetToRegistry. (TDC/EDC/Tctl removed.)
        d.AmdCpuPpt = (int)key.GetValue("AmdCpuPpt", d.AmdCpuPpt);
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
        } catch { }
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
    static void ApplyPowerModeOverlay(int powerMode) {
      try {
        Guid g;
        if (powerMode == 0) g = NativeMethods_Power.BEST_POWER_EFFICIENCY;
        else if (powerMode == 2) g = NativeMethods_Power.BEST_PERFORMANCE;
        else g = Guid.Empty;  // 1=平衡 → 默认
        NativeMethods_Power.PowerSetActiveOverlayScheme(g);
      } catch (Exception ex) { Logger.Verbose("ApplyPowerModeOverlay: " + ex.Message); }
    }

    // ═══════════════════════════════════════════════════════
    // ponytail: 高级调教已全数移除（机型不可用）。仅保留 AMD PPT 经 WMI 复写，
    // 作为预设切换 / PerfPage.Reload 后的状态恢复入口。
    // 上限：WMI 路径仅接受 0~255W；超过此范围的 PPT 由 PerfPage 滑条上限约束。
    internal static void ApplyAdvanced() {
      try {
        if (OmenHardware.HasAmdCpu() && ConfigService.AmdCpuPpt > 0 && ConfigService.AmdCpuPpt <= 255)
          OmenHardware.SetCpuPowerLimit((byte)ConfigService.AmdCpuPpt);
      } catch { }
      // ponytail: AMD Curve Optimizer 全核+分核降压 — SMU 写易失,每次预设切换重应用。
      // 仅需 PawnIO 驱动就绪,不依赖 EnableEcAccess 开关。
      try {
        var svc = Services.AmdUndervoltService.Instance;
        if (svc.IsAvailable) {
          if (ConfigService.AmdCpuUndervolt != 0) svc.SetAllCoreCO(ConfigService.AmdCpuUndervolt);
          var perCore = Services.AmdUndervoltService.ParsePerCoreOffsets(ConfigService.AmdCpuPerCoreOffsets);
          if (perCore.Count > 0) svc.ApplyPerCoreCO(perCore);
        }
      } catch { }
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
    public static System.Threading.Tasks.Task AwaitableApplyPresetHardware() {
      var tcs = new System.Threading.Tasks.TaskCompletionSource<bool>();
      int gpuClock = ConfigService.GpuClock;
      bool tgp = ConfigService.TgpEnabled;
      bool ppab = ConfigService.PpabEnabled;
      string cpuPwr = ConfigService.CpuPower;
      int powerMode = ConfigService.PowerMode;

      System.Threading.ThreadPool.QueueUserWorkItem(_ => {
        // ponytail: App.xaml.cs 启动时调的 SetFanMode(0x31) 可能在 EC/WMI 就绪前就跑，
        // 失败后没有重试；而 CPU 功率限制依赖 EC 处于 unleash mode 才会真正生效。
        // 在这里再补一刀，确保功率限不会被 EC 忽略。
        try { OmenHardware.SetFanMode((byte)0x31); } catch { }
        // ── 1.1 全局绑定参数 ──
        try { TrayService.SetGPUClockLimit(gpuClock); } catch { }
        try {
          // ponytail: apply PL1 and PL2 independently from ConfigService.
          int pl1 = ConfigService.CpuPowerPl1;
          int pl2 = ConfigService.CpuPowerPl2;
          if (cpuPwr == "max") OmenHardware.SetCpuPowerLimit(254, 254);
          else if (cpuPwr == "null") { /* keep BIOS default */ }
          else if (pl1 >= 10 && pl1 <= 254 && pl2 >= 10 && pl2 <= 254)
            OmenHardware.SetCpuPowerLimit((byte)pl1, (byte)pl2);
          else if (int.TryParse(cpuPwr?.Replace(" W", ""), out int cpuVal) && cpuVal >= 10 && cpuVal <= 254)
            OmenHardware.SetCpuPowerLimit((byte)cpuVal, (byte)cpuVal);
        } catch { }
        // ponytail: TPP (ConcurrentTDP) — total power budget for CPU+GPU combined.
        // Without this, EC uses a conservative default budget → dual-stress (CPU+GPU)
        // throttles because each component fights for a share of a capped total.
        // MUST be written BEFORE SetGpuPowerState — PPAB dynamic power sharing
        // reads the TPP budget to decide how much power to allocate to GPU, so
        // if TPP is still the BIOS default (~155W), PPAB caps GPU power within
        // that small budget and CPU doesn't get its share.
        try { if (ConfigService.Tpp >= 20) OmenHardware.SetConcurrentTdp((byte)ConfigService.Tpp); } catch { }
        try { OmenHardware.SetGpuPowerState(tgp, ppab, ConfigService.DState == 2 ? 2 : 1); } catch { }
        try { ApplyPowerModeOverlay(powerMode); } catch { }

        // ponytail: 高级调教已删除；ApplyAdvanced 现仅写 AMD PPT 经 WMI。
        try { ApplyAdvanced(); } catch { }

        // ── 风扇配置 ──
        try {
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
            OmenHardware.SetFanLevel(0, 0);
            OmenHardware.SetFanLevel(speed, speed);
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
        } catch { }

        // ── 1.2 自定义预设专属绑定参数 ──
        if (IsCustom(ConfigService.Preset)) {
          // GPU 超频
          try { GpuAppManager.SetCoreClockOffset(ConfigService.GpuCoreOverclock); } catch { }
          try { GpuAppManager.SetMemoryClockOffset(ConfigService.GpuMemoryOverclock); } catch { }
          // 最大帧率
          try {
            int fps = ConfigService.MaxFrameRate;
            if (fps > 0) HP.Omen.Core.Common.NVidiaApi.NvApiWrapper.NVAPI_SetMaxFrameRate(fps);
            else HP.Omen.Core.Common.NVidiaApi.NvApiWrapper.NVAPI_SetMaxFrameRate(0);
          } catch { }
          // 电源计划
          try {
            if (!string.IsNullOrEmpty(ConfigService.PowerPlanGuid)) {
              Guid g = Guid.Parse(ConfigService.PowerPlanGuid);
              NativeMethods_Power.PowerSetActiveScheme(IntPtr.Zero, ref g);
            }
          } catch { }
          // EcoQoS
          try {
            EcoQosService.SetEnabled(ConfigService.EcoQosEnabled);
            EcoQosService.SetThrottlePlugged(ConfigService.EcoQosThrottlePlugged);
          } catch { }
          // 刷新率
          try {
            if (ConfigService.RefreshRate > 0)
              TrayService.ApplyRefreshRate(ConfigService.RefreshRate);
          } catch { }
          // 风扇曲线 (自定义预设专属持久化)
          try {
            FanService.ApplyPresetCurve(ConfigService.Preset);
          } catch { }
        }
        // ponytail: 工作体所有写入都包在各自 try/catch 里, 这里 SetResult 永远触发,
        // 调用方 await 才能确定硬件写入已完成(就语义而言)再跑下一步骤。
        tcs.TrySetResult(true);
      });
      return tcs.Task;
    }
  }
}
