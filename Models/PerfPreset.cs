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
    // AMD CPU 桌面调校 (PPT/TDC/EDC/Tctl)
    [DataMember] public double AmdCpuPpt { get; set; }
    [DataMember] public double AmdCpuTdc { get; set; }
    [DataMember] public double AmdCpuEdc { get; set; }
    [DataMember] public double AmdCpuTctl { get; set; }
    [DataMember] public double NvPower { get; set; }
    [DataMember] public double NvClockLock { get; set; }
    [DataMember] public double Rtss { get; set; }
    [DataMember] public bool AutoOcOn { get; set; }
    [DataMember] public double PawnIgpuPower { get; set; }
    [DataMember] public double PawnIgpuRatio { get; set; }
    // ── UXTU-style master toggles ──
    [DataMember] public bool FivrMasterOn { get; set; } = true;
    [DataMember] public bool ApuPowerMasterOn { get; set; } = true;
    [DataMember] public bool ApuVrmMasterOn { get; set; } = true;
    [DataMember] public bool ApuTempMasterOn { get; set; } = true;
    [DataMember] public bool ApuGfxClkMasterOn { get; set; } = true;
    [DataMember] public bool AmdCpuPowerMasterOn { get; set; } = true;
    [DataMember] public bool AmdCpuTempMasterOn { get; set; } = true;
    // ponytail: extended toggles + new ADLX GPU controls. Defaults match
    // ConfigService defaults (master=false so cards start disabled).
    [DataMember] public bool PboScalarMasterOn { get; set; }
    [DataMember] public bool CoMasterOn { get; set; }
    [DataMember] public bool CcdAffinityMasterOn { get; set; }
    [DataMember] public bool AutoOcMasterOn { get; set; }
    [DataMember] public bool ClockRatioMasterOn { get; set; }
    [DataMember] public bool PowerBalanceMasterOn { get; set; }
    [DataMember] public bool PawnTurboMasterOn { get; set; }
    [DataMember] public bool PawnProchotMasterOn { get; set; }
    [DataMember] public bool PawnHwpMasterOn { get; set; }
    [DataMember] public bool PawnCStateMasterOn { get; set; }
    [DataMember] public bool PawnIgpuPowerMasterOn { get; set; }
    [DataMember] public bool PawnIgpuRatioMasterOn { get; set; }
    [DataMember] public bool NvTuningMasterOn { get; set; }
    [DataMember] public bool RtssMasterOn { get; set; }
    // ADLX / AMD GPU state — captured/applyed together so preview mirrors real GPU
    [DataMember] public bool AdlxRsrOn { get; set; }
    [DataMember] public int AdlxRsrSharpness { get; set; } = 50;
    [DataMember] public bool AdlxAntiLagOn { get; set; }
    [DataMember] public bool AdlxEnhancedSyncOn { get; set; }
    [DataMember] public bool AdlxBoostOn { get; set; }
    [DataMember] public int AdlxBoostPercent { get; set; }
    [DataMember] public bool AdlxImageSharpOn { get; set; }
    [DataMember] public int AdlxImageSharpPercent { get; set; } = 50;
    // Curve Optimiser per-core array: 24 entries, comma-joined as a single string
    // for serialisation simplicity (UXTU path). CDATA pattern matches what
    // ConfigService.CoPerCore already stores.
    [DataMember] public string CoPerCoreCsv { get; set; } = "";
    [DataMember] public int CoIGpuOffsetSnapshot { get; set; }
  }
}
