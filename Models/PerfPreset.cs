using System.Runtime.Serialization;

namespace OmenSuperHub.Models {
  [DataContract]
  public class PerfPreset {
    [DataMember] public int CpuPowerIndex { get; set; }
    [DataMember] public double CpuPowerPL1 { get; set; }
    [DataMember] public double CpuPowerPL2 { get; set; }
    [DataMember] public int IccMaxIndex { get; set; }
    [DataMember] public double IccMax { get; set; }
    [DataMember] public int AcLoadLineIndex { get; set; }
    [DataMember] public int PowerModeIndex { get; set; }
    [DataMember] public int PowerPlanIndex { get; set; }
    [DataMember] public int PwrSourceIndex { get; set; }
    [DataMember] public int PwrClassIndex { get; set; }
    [DataMember] public int EppIndex { get; set; }
    [DataMember] public string EppText { get; set; }
    [DataMember] public int BoostModeIndex { get; set; }
    [DataMember] public int MaxProcStateIndex { get; set; }
    [DataMember] public string MaxProcStateText { get; set; }
    [DataMember] public int MaxFreqIndex { get; set; }
    [DataMember] public string MaxFreqText { get; set; }
    [DataMember] public int SmtPolicyIndex { get; set; }
    [DataMember] public bool EcoQosOn { get; set; }
    [DataMember] public bool EcoQosThrottlePlugged { get; set; }
    [DataMember] public int GpuClockIndex { get; set; }
    [DataMember] public double GpuClock { get; set; }
    [DataMember] public int GpuCoreOCIndex { get; set; }
    [DataMember] public double GpuCoreOC { get; set; }
    [DataMember] public int GpuMemoryOCIndex { get; set; }
    [DataMember] public double GpuMemoryOC { get; set; }
    [DataMember] public int GfxModeIndex { get; set; }
    [DataMember] public int DbVersionIndex { get; set; }
    [DataMember] public int CtgpIndex { get; set; }
    [DataMember] public bool PpabOn { get; set; }
    [DataMember] public double Tpp { get; set; }
    [DataMember] public int DStateIndex { get; set; }
    [DataMember] public int FpsIndex { get; set; }
    [DataMember] public double Fps { get; set; }
    [DataMember] public int RefreshRateIndex { get; set; }
    [DataMember] public double RefreshRate { get; set; }
    [DataMember] public int ResolutionIndex { get; set; }
    [DataMember] public int DpiIndex { get; set; }
    [DataMember] public bool HdrOn { get; set; }
    // ponytail: 高级调教字段已全数移除（机型不可用）。IccMax/AcLoadLine/CpuPower 等基础字段保留。
    // 旧 JSON 里的 advanced 键会被 DataContract 静默忽略，向后兼容。
  }
}
