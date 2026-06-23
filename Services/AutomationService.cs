// AutomationService.cs - 自动化数据模型与持久化
// Pipeline/Trigger/Step 数据契约定义，JSON 序列化存储，步骤类型注册表
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using OmenSuperHub.Services;

namespace OmenSuperHub.Services {
  [DataContract]
  public class AutomationStep {
    [DataMember] public string Type { get; set; }
    [DataMember] public string Value { get; set; }
    [DataMember] public int IntValue { get; set; }
    [DataMember] public int DelayMs { get; set; }
  }

  [DataContract]
  public class AutomationTrigger {
    [DataMember] public string Type { get; set; }
    [DataMember] public string Value { get; set; }
    [DataMember] public bool Enabled { get; set; }

    public AutomationTrigger() { Enabled = true; }
    public AutomationTrigger(string type, string value = "") {
      Type = type; Value = value ?? ""; Enabled = true;
    }
  }

  [DataContract]
  public class AutomationPipeline {
    [DataMember] public string Name { get; set; }
    [DataMember] public bool Enabled { get; set; }
    [DataMember] public string TriggerType { get; set; }
    [DataMember] public string TriggerValue { get; set; }
    [DataMember] public List<AutomationTrigger> Triggers { get; set; }
    [DataMember] public List<AutomationStep> Steps { get; set; }

    public AutomationPipeline() {
      Steps = new List<AutomationStep>();
      Triggers = new List<AutomationTrigger>();
      Enabled = true;
    }

    public void EnsureTriggers() {
      if (Triggers == null) Triggers = new List<AutomationTrigger>();
      if (Triggers.Count == 0 && !string.IsNullOrEmpty(TriggerType) && TriggerType != "QuickAction") {
        Triggers.Add(new AutomationTrigger(TriggerType, TriggerValue));
      }
    }

    public bool MatchesTrigger(string triggerType, string triggerValue) {
      EnsureTriggers();
      if (Triggers.Count == 0) return false;
      foreach (var t in Triggers) {
        if (!t.Enabled) continue;
        if (t.Type != triggerType) continue;
        if (!string.IsNullOrEmpty(t.Value) && !MatchTriggerValue(t.Type, t.Value, triggerValue)) continue;
        return true;
      }
      return false;
    }

    static bool MatchTriggerValue(string triggerType, string triggerDefValue, string firedValue) {
      switch (triggerType) {
        case "BatteryAbove":
          if (int.TryParse(triggerDefValue, out int batThresh) && int.TryParse(firedValue, out int batCur))
            return batCur >= batThresh;
          return false;
        case "BatteryBelow":
          if (int.TryParse(triggerDefValue, out int batThresh2) && int.TryParse(firedValue, out int batCur2))
            return batCur2 <= batThresh2;
          return false;
        case "CpuTempAbove":
          if (int.TryParse(triggerDefValue, out int cpuThresh) && int.TryParse(firedValue, out int cpuCur))
            return cpuCur >= cpuThresh;
          return false;
        case "GpuTempAbove":
          if (int.TryParse(triggerDefValue, out int gpuThresh) && int.TryParse(firedValue, out int gpuCur))
            return gpuCur >= gpuThresh;
          return false;
        default:
          return triggerDefValue == firedValue;
      }
    }

    public bool IsQuickAction {
      get {
        EnsureTriggers();
        return Triggers.Count == 1 && Triggers[0].Type == "QuickAction";
      }
    }
  }

  internal static class AutomationService {
    private static string FilePath;
    private static readonly object FileLock = new object();
    private static DataContractJsonSerializer _serializer;
    public static List<AutomationPipeline> Pipelines { get; private set; }

    public static void Initialize() {
      string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
      string dir = Path.Combine(appData, "OmenXHub");
      if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
      FilePath = Path.Combine(dir, "automation.json");
      _serializer = new DataContractJsonSerializer(typeof(List<AutomationPipeline>),
          new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
      Load();
    }

    public static void Load() {
      lock (FileLock) {
        try {
          if (File.Exists(FilePath)) {
            using (var fs = new FileStream(FilePath, FileMode.Open, FileAccess.Read))
              Pipelines = (List<AutomationPipeline>)_serializer.ReadObject(fs);
          } else {
            Pipelines = new List<AutomationPipeline>();
          }
        } catch (Exception ex) {
          Logger.Error("AutomationService.Load failed: " + ex.Message);
          Pipelines = new List<AutomationPipeline>();
        }
      }
      TrayService.RebuildMenu();
    }

    public static void Save() {
      lock (FileLock) {
        try {
          using (var ms = new MemoryStream()) {
            _serializer.WriteObject(ms, Pipelines);
            File.WriteAllBytes(FilePath, ms.ToArray());
          }
        } catch (Exception ex) {
          Logger.Error("AutomationService.Save failed: " + ex.Message);
        }
      }
    }

    public static void AddPipeline(AutomationPipeline pipeline) {
      Pipelines.Add(pipeline);
      Save();
    }

    public static void RemovePipeline(AutomationPipeline pipeline) {
      Pipelines.Remove(pipeline);
      Save();
    }

    public static void UpdatePipeline(AutomationPipeline pipeline) {
      Save();
    }

    public static List<AutomationPipeline> GetEnabledPipelines() {
      var result = new List<AutomationPipeline>();
      foreach (var p in Pipelines) {
        if (!p.Enabled) continue;
        p.EnsureTriggers();
        if (p.IsQuickAction) continue;
        result.Add(p);
      }
      return result;
    }

    public static List<AutomationPipeline> GetQuickActions() {
      var result = new List<AutomationPipeline>();
      if (Pipelines == null) return result;
      foreach (var p in Pipelines) {
        if (p.Enabled && p.IsQuickAction)
          result.Add(p);
      }
      return result;
    }
  }

  internal static class AutomationStepTypes {
    public static readonly string[] All = {
      "SetPreset", "SetRefreshRate", "SetPowerPlan", "SetPowerMode",
      "SetMaxFrameRate", "SetCpuPower", "SetGpuPower", "SetTpp",
      "SetIccMax", "SetAcLoadLine", "SetFanMode", "SetFanCurve",
      "SetTempSensitivity", "SetGPUHybridMode", "SetBrightness",
      "SetMicrophone", "SetWiFi", "SetBluetooth", "PlaySound",
      "RunProgram", "Delay", "Notification", "RunMacro"
    };

    public static string GetLabel(string type) {
      switch (type) {
        case "SetPreset": return Strings.AutomationStepSetPreset;
        case "SetRefreshRate": return Strings.AutomationStepSetRefreshRate;
        case "SetPowerPlan": return Strings.AutomationStepSetPowerPlan;
        case "SetPowerMode": return Strings.AutomationStepSetPowerMode;
        case "SetMaxFrameRate": return Strings.AutomationStepSetMaxFrameRate;
        case "SetCpuPower": return Strings.AutomationStepSetCpuPower;
        case "SetGpuPower": return Strings.AutomationStepSetGpuPower;
        case "SetTpp": return Strings.AutomationStepSetTpp;
        case "SetIccMax": return Strings.AutomationStepSetIccMax;
        case "SetAcLoadLine": return Strings.AutomationStepSetAcLoadLine;
        case "SetFanMode": return Strings.AutomationStepSetFanMode;
        case "SetFanCurve": return Strings.AutomationStepSetFanCurve;
        case "SetTempSensitivity": return Strings.AutomationStepSetTempSensitivity;
        case "SetGPUHybridMode": return Strings.AutomationStepSetGPUHybridMode;
        case "SetBrightness": return Strings.AutomationStepSetBrightness;
        case "SetMicrophone": return Strings.AutomationStepSetMicrophone;
        case "SetWiFi": return Strings.AutomationStepSetWiFi;
        case "SetBluetooth": return Strings.AutomationStepSetBluetooth;
        case "PlaySound": return Strings.AutomationStepPlaySound;
        case "RunProgram": return Strings.AutomationStepRunProgram;
        case "Delay": return Strings.AutomationStepDelay;
        case "Notification": return Strings.AutomationStepNotification;
        case "RunMacro": return Strings.AutomationStepRunMacro;
        default: return type;
      }
    }

    public static string GetValueHint(string type) {
      switch (type) {
        case "SetRefreshRate": return Strings.AutomationRefreshRate;
        case "SetPowerPlan": return Strings.AutomationPowerPlanGuid;
        case "SetPowerMode": return Strings.AutomationPowerModeValue;
        case "SetPreset": return Strings.AutomationPreset;
        case "SetMaxFrameRate": return Strings.AutomationMaxFrameRate;
        case "SetCpuPower": return Strings.AutomationCpuPowerValue;
        case "SetGpuPower": return Strings.AutomationGpuPowerValue;
        case "SetTpp": return Strings.AutomationTppValue;
        case "SetIccMax": return Strings.AutomationIccMaxValue;
        case "SetAcLoadLine": return Strings.AutomationAcLoadLineValue;
        case "SetFanMode": return Strings.AutomationFanModeValue;
        case "SetFanCurve": return Strings.AutomationFanCurveValue;
        case "SetTempSensitivity": return Strings.AutomationTempSensitivityValue;
        case "SetGPUHybridMode": return Strings.AutomationGPUHybridModeValue;
        case "SetBrightness": return Strings.AutomationBrightnessValue;
        case "SetMicrophone": return Strings.AutomationMicrophoneValue;
        case "SetWiFi": return Strings.AutomationWiFiValue;
        case "SetBluetooth": return Strings.AutomationBluetoothValue;
        case "PlaySound": return Strings.AutomationPlaySoundValue;
        case "RunProgram": return Strings.AutomationProgramPath;
        case "Notification": return Strings.AutomationMessage;
        case "RunMacro": return "Macro Name";
        default: return Strings.AutomationStepValue;
      }
    }
  }
}
