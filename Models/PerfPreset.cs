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
    [DataMember] public int PboScalarIndex { get; set; }
    [DataMember] public double Co { get; set; }
    [DataMember] public bool CcdAffinityOn { get; set; }
    [DataMember] public double FivrCore { get; set; }
    [DataMember] public double FivrCache { get; set; }
    [DataMember] public double FivrIgpu { get; set; }
    [DataMember] public double FivrSa { get; set; }
    [DataMember] public double PowerBalance { get; set; }
    [DataMember] public bool PawnTurboOn { get; set; }
    [DataMember] public double PawnProchot { get; set; }
    [DataMember] public double PawnHwp { get; set; }
    [DataMember] public int PawnCStateIndex { get; set; }
    [DataMember] public double ApuStapm { get; set; }
    [DataMember] public double ApuFast { get; set; }
    [DataMember] public double ApuSlow { get; set; }
    [DataMember] public double ApuStapmTime { get; set; }
    [DataMember] public double ApuSlowTime { get; set; }
    [DataMember] public double ApuVrmCurrent { get; set; }
    [DataMember] public double ApuVrmSocCurrent { get; set; }
    [DataMember] public double ApuVrmMax { get; set; }
    [DataMember] public double ApuVrmSocMax { get; set; }
    [DataMember] public double ApuTctl { get; set; }
    [DataMember] public double ApuSkinTemp { get; set; }
    [DataMember] public double ApuDgpuSkin { get; set; }
    [DataMember] public double ApuGfxClk { get; set; }
    [DataMember] public double NvPower { get; set; }
    [DataMember] public double NvClockLock { get; set; }
    [DataMember] public double Rtss { get; set; }
    [DataMember] public bool AutoOcOn { get; set; }
    [DataMember] public double PawnIgpuPower { get; set; }
    [DataMember] public double PawnIgpuRatio { get; set; }
  }
}
