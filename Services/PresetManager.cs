// PresetManager.cs - 预设管理服务
// 内置默认预设 (Extreme/GpuPriority/LightUse)，自定义预设保存/加载，预设切换逻辑
using System;
using Microsoft.Win32;

namespace OmenSuperHub.Services {
  /// <summary>
  /// 预设绑定参数快照 — 切换预设时只改变这些参数。
  /// </summary>
  public class PresetData {
    // ── 绑定参数（随预设切换） ──
    public string CpuPower { get; set; } = "max";
    public int CpuPowerPl1 { get; set; } = -1;
    public int CpuPowerPl2 { get; set; } = -1;
    public string FanTable { get; set; } = "silent";
    public string FanControl { get; set; } = "auto";
    public string TempSensitivity { get; set; } = "medium";
    public int PowerMode { get; set; } = 1;
    public string PowerPlanGuid { get; set; } = "";
    public int GpuClock { get; set; } = 0;
    public bool TgpEnabled { get; set; } = true;
    public bool PpabEnabled { get; set; } = true;
    public int Tpp { get; set; } = 0;
    public int DState { get; set; } = 1;

    // ── 全局关联参数（仅随自定义预设保存切换） ──
    public int GpuCoreOverclock { get; set; } = -1;
    public int GpuMemoryOverclock { get; set; } = -1;
    public int DBVersion { get; set; } = 2;
    public int RefreshRate { get; set; } = 0;
    public int MaxFrameRate { get; set; } = -1;
    public int IccMax { get; set; } = 0;
    public int AcLoadLine { get; set; } = 0;
    public int DisableDynamicBoost { get; set; } = 0;
  }

  internal static class PresetManager {
    static readonly string[] BuiltInKeys = { "Extreme", "GpuPriority", "LightUse" };
    static readonly string[] CustomKeys = { "Custom1", "Custom2", "Custom3" };

    public static bool IsBuiltIn(string preset) => Array.IndexOf(BuiltInKeys, preset) >= 0;
    public static bool IsCustom(string preset) => Array.IndexOf(CustomKeys, preset) >= 0;

    // ═══════════════════════════════════════════════════════
    // 固定预设出厂默认值
    // ═══════════════════════════════════════════════════════
    public static PresetData GetBuiltInDefaults(string preset) {
      var d = new PresetData {
        PowerPlanGuid = "",
        IccMax = 0, AcLoadLine = 0,
        GpuCoreOverclock = -1, GpuMemoryOverclock = -1,
        DBVersion = 2, RefreshRate = 0,
      };
      switch (preset) {
        case "Extreme":
          d.CpuPower = "max"; d.CpuPowerPl1 = 254; d.CpuPowerPl2 = 254;
          d.FanTable = "cool"; d.FanControl = "auto"; d.TempSensitivity = "medium";
          d.PowerMode = 2;
          d.GpuClock = 0; // 还原默认（不限制）
          d.TgpEnabled = true; d.PpabEnabled = true; d.Tpp = 255;
          d.DState = 1;
          d.MaxFrameRate = -1; d.DisableDynamicBoost = 1;
          break;
        case "GpuPriority":
          d.CpuPower = "45 W"; d.CpuPowerPl1 = 45; d.CpuPowerPl2 = 45;
          d.FanTable = "cool"; d.FanControl = "auto"; d.TempSensitivity = "medium";
          d.PowerMode = 1;
          d.GpuClock = 0;
          d.TgpEnabled = true; d.PpabEnabled = true; d.Tpp = 255;
          d.DState = 1;
          d.MaxFrameRate = -1; d.DisableDynamicBoost = 0;
          break;
        case "LightUse":
          d.CpuPower = "25 W"; d.CpuPowerPl1 = 25; d.CpuPowerPl2 = 25;
          d.FanTable = "silent"; d.FanControl = "auto"; d.TempSensitivity = "medium";
          d.PowerMode = 0;
          d.GpuClock = 0;
          d.TgpEnabled = false; d.PpabEnabled = false; d.Tpp = 0;
          d.DState = 1;
          d.MaxFrameRate = 60; d.DisableDynamicBoost = 0;
          break;
      }
      return d;
    }

    // ═══════════════════════════════════════════════════════
    // 将 PresetData 写入 ConfigService 静态字段
    // ═══════════════════════════════════════════════════════
    public static void ApplyPresetData(PresetData d) {
      ConfigService.CpuPower = d.CpuPower;
      ConfigService.CpuPowerPl1 = d.CpuPowerPl1;
      ConfigService.CpuPowerPl2 = d.CpuPowerPl2;
      ConfigService.FanTable = d.FanTable;
      ConfigService.FanControl = d.FanControl;
      ConfigService.TempSensitivity = d.TempSensitivity;
      ConfigService.PowerMode = d.PowerMode;
      ConfigService.PowerPlanGuid = d.PowerPlanGuid;
      ConfigService.GpuClock = d.GpuClock;
      ConfigService.TgpEnabled = d.TgpEnabled;
      ConfigService.PpabEnabled = d.PpabEnabled;
      ConfigService.Tpp = d.Tpp;
      ConfigService.DState = d.DState;

      // 全局关联参数
      ConfigService.GpuCoreOverclock = d.GpuCoreOverclock;
      ConfigService.GpuMemoryOverclock = d.GpuMemoryOverclock;
      ConfigService.DBVersion = d.DBVersion;
      ConfigService.RefreshRate = d.RefreshRate;
      ConfigService.MaxFrameRate = d.MaxFrameRate;
      ConfigService.IccMax = d.IccMax;
      ConfigService.AcLoadLine = d.AcLoadLine;
      ConfigService.DisableDynamicBoost = d.DisableDynamicBoost;
    }

    // ═══════════════════════════════════════════════════════
    // 从 ConfigService 当前值捕获 PresetData
    // ═══════════════════════════════════════════════════════
    public static PresetData CaptureCurrent() {
      return new PresetData {
        CpuPower = ConfigService.CpuPower,
        CpuPowerPl1 = ConfigService.CpuPowerPl1,
        CpuPowerPl2 = ConfigService.CpuPowerPl2,
        FanTable = ConfigService.FanTable,
        FanControl = ConfigService.FanControl,
        TempSensitivity = ConfigService.TempSensitivity,
        PowerMode = ConfigService.PowerMode,
        PowerPlanGuid = ConfigService.PowerPlanGuid,
        GpuClock = ConfigService.GpuClock,
        TgpEnabled = ConfigService.TgpEnabled,
        PpabEnabled = ConfigService.PpabEnabled,
        Tpp = ConfigService.Tpp,
        DState = ConfigService.DState,
        GpuCoreOverclock = ConfigService.GpuCoreOverclock,
        GpuMemoryOverclock = ConfigService.GpuMemoryOverclock,
        DBVersion = ConfigService.DBVersion,
        RefreshRate = ConfigService.RefreshRate,
        MaxFrameRate = ConfigService.MaxFrameRate,
        IccMax = ConfigService.IccMax,
        AcLoadLine = ConfigService.AcLoadLine,
        DisableDynamicBoost = ConfigService.DisableDynamicBoost,
      };
    }

    // ═══════════════════════════════════════════════════════
    // 自定义预设持久化（仅保存绑定 + 全局关联参数）
    // ═══════════════════════════════════════════════════════
    static string PresetSubKey(string name) => $@"Software\OmenXHub\Presets\{name}";

    public static void SaveCustomPreset(string presetKey) {
      if (!IsCustom(presetKey)) return;
      var d = CaptureCurrent();
      try {
        using (RegistryKey key = Registry.CurrentUser.CreateSubKey(PresetSubKey(presetKey))) {
          if (key == null) return;
          key.SetValue("CpuPower", d.CpuPower);
          key.SetValue("CpuPowerPl1", d.CpuPowerPl1);
          key.SetValue("CpuPowerPl2", d.CpuPowerPl2);
          key.SetValue("FanTable", d.FanTable);
          key.SetValue("FanControl", d.FanControl);
          key.SetValue("TempSensitivity", d.TempSensitivity);
          key.SetValue("PowerMode", d.PowerMode);
          key.SetValue("PowerPlanGuid", d.PowerPlanGuid);
          key.SetValue("GpuClock", d.GpuClock);
          key.SetValue("TgpEnabled", d.TgpEnabled ? 1 : 0);
          key.SetValue("PpabEnabled", d.PpabEnabled ? 1 : 0);
          key.SetValue("Tpp", d.Tpp);
          key.SetValue("DState", d.DState);
          key.SetValue("GpuCoreOverclock", d.GpuCoreOverclock);
          key.SetValue("GpuMemoryOverclock", d.GpuMemoryOverclock);
          key.SetValue("DBVersion", d.DBVersion);
          key.SetValue("RefreshRate", d.RefreshRate);
          key.SetValue("MaxFrameRate", d.MaxFrameRate);
          key.SetValue("IccMax", d.IccMax);
          key.SetValue("AcLoadLine", d.AcLoadLine);
          key.SetValue("DisableDynamicBoost", d.DisableDynamicBoost);
          if (presetKey == "Custom1") key.SetValue("CustomPresetName", ConfigService.CustomPreset1Name);
          else if (presetKey == "Custom2") key.SetValue("CustomPresetName", ConfigService.CustomPreset2Name);
          else if (presetKey == "Custom3") key.SetValue("CustomPresetName", ConfigService.CustomPreset3Name);
        }
      } catch (Exception ex) {
        Console.WriteLine($"Error saving custom preset: {ex.Message}");
      }
    }

    public static PresetData LoadCustomPreset(string presetKey) {
      if (!IsCustom(presetKey)) return null;
      var d = new PresetData();
      try {
        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(PresetSubKey(presetKey))) {
          if (key == null) return null;
          d.CpuPower = (string)key.GetValue("CpuPower", d.CpuPower);
          d.CpuPowerPl1 = (int)key.GetValue("CpuPowerPl1", d.CpuPowerPl1);
          d.CpuPowerPl2 = (int)key.GetValue("CpuPowerPl2", d.CpuPowerPl2);
          d.FanTable = (string)key.GetValue("FanTable", d.FanTable);
          d.FanControl = (string)key.GetValue("FanControl", d.FanControl);
          d.TempSensitivity = (string)key.GetValue("TempSensitivity", d.TempSensitivity);
          d.PowerMode = (int)key.GetValue("PowerMode", d.PowerMode);
          d.PowerPlanGuid = (string)key.GetValue("PowerPlanGuid", d.PowerPlanGuid);
          d.GpuClock = (int)key.GetValue("GpuClock", d.GpuClock);
          d.TgpEnabled = Convert.ToInt32(key.GetValue("TgpEnabled", d.TgpEnabled ? 1 : 0)) == 1;
          d.PpabEnabled = Convert.ToInt32(key.GetValue("PpabEnabled", d.PpabEnabled ? 1 : 0)) == 1;
          d.Tpp = (int)key.GetValue("Tpp", d.Tpp);
          d.DState = (int)key.GetValue("DState", d.DState);
          d.GpuCoreOverclock = (int)key.GetValue("GpuCoreOverclock", d.GpuCoreOverclock);
          d.GpuMemoryOverclock = (int)key.GetValue("GpuMemoryOverclock", d.GpuMemoryOverclock);
          d.DBVersion = (int)key.GetValue("DBVersion", d.DBVersion);
          d.RefreshRate = (int)key.GetValue("RefreshRate", d.RefreshRate);
          d.MaxFrameRate = (int)key.GetValue("MaxFrameRate", d.MaxFrameRate);
          d.IccMax = (int)key.GetValue("IccMax", d.IccMax);
          d.AcLoadLine = (int)key.GetValue("AcLoadLine", d.AcLoadLine);
          d.DisableDynamicBoost = (int)key.GetValue("DisableDynamicBoost", d.DisableDynamicBoost);
        }
      } catch (Exception ex) {
        Console.WriteLine($"Error loading custom preset: {ex.Message}");
        return null;
      }
      return d;
    }

    // ═══════════════════════════════════════════════════════
    // 预设切换主入口
    // ═══════════════════════════════════════════════════════
    public static void SwitchPreset(string preset) {
      string prevPreset = ConfigService.Preset;

      // 从旧预设离开时：如果是自定义预设，保存当前绑定参数
      if (!string.IsNullOrEmpty(prevPreset) && prevPreset != preset && IsCustom(prevPreset))
        SaveCustomPreset(prevPreset);

      PresetData data;
      if (IsBuiltIn(preset))
        data = GetBuiltInDefaults(preset);
      else
        data = LoadCustomPreset(preset) ?? GetBuiltInDefaults("GpuPriority"); // fallback

      if (data == null) return;
      ApplyPresetData(data);
      ConfigService.Preset = preset;
      ConfigService.Save("Preset");
    }
  }
}
