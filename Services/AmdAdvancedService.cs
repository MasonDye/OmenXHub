using System;
using System.Threading.Tasks;

namespace OmenSuperHub.Services {
  /// <summary>AMD 高级调校服务 — 通过 SMU mailbox 直写（替换 AMDRyzenSDK.dll）</summary>
  internal static class AmdAdvancedService {

    public static bool IsAvailable => AmdSmuService.IsAvailable;
    public static bool IsPboSupported => AmdSmuService.IsAvailable; // SMU mailbox supports PBO on all known sockets
    public static string CpuFamilyName => AmdSmuService.CpuFamilyName;

    /// <summary>全核 Curve Optimizer 偏移 (-30 ~ +30)</summary>
    public static async Task<bool> SetCurveOptimizerAsync(short offset) {
      return await Task.Run(() => AmdSmuService.SetCurveOptimizerAllCore(offset));
    }

    /// <summary>每核 Curve Optimizer 偏移</summary>
    public static bool SetCurveOptimizerPerCore(int ccd, int core, int offset) {
      return AmdSmuService.SetCurveOptimizerPerCore(ccd, core, offset);
    }

    /// <summary>获取 CO 状态（简化：返回 0 表示可用，但不从 HW 读取当前值）</summary>
    public static async Task<int> GetCurveOptimizerStatusAsync() {
      return await Task.FromResult(IsAvailable ? 0 : -1);
    }

    /// <summary>PBO 开关</summary>
    public static async Task<bool> SetPboAsync(bool enabled) {
      return await Task.Run(() => AmdSmuService.SetPboScalar(enabled ? 1u : 0u));
    }

    /// <summary>获取 PBO 状态</summary>
    public static async Task<string> GetPboStatusAsync() {
      return await Task.FromResult(IsAvailable ? "PBO 通过 SMU mailbox 可用" : "不可用");
    }

    /// <summary>Auto OC 不通过 SMU mailbox 支持</summary>
    public static async Task<bool> SetAutoOcOffsetAsync(int offset) {
      return await Task.FromResult(false);
    }

    /// <summary>设置 PPT Limit (mW)</summary>
    public static bool SetPptLimit(uint mw) => AmdSmuService.SetPptLimit(mw);

    /// <summary>设置 TDC Limit (mA)</summary>
    public static bool SetTdcLimit(uint ma) => AmdSmuService.SetTdcLimit(ma);

    /// <summary>设置 EDC Limit (mA)</summary>
    public static bool SetEdcLimit(uint ma) => AmdSmuService.SetEdcLimit(ma);

    // ── APU 功耗调教 (STAPM / Fast / Slow PPT) ──

    /// <summary>STAPM 持续功耗限制 (mW)</summary>
    public static bool SetStapmLimit(uint mw) => AmdSmuService.SetStapmLimit(mw);

    /// <summary>Fast PPT 峰值功耗限制 (mW)</summary>
    public static bool SetFastLimit(uint mw) => AmdSmuService.SetFastLimit(mw);

    /// <summary>Slow PPT 平均功耗限制 (mW)</summary>
    public static bool SetSlowLimit(uint mw) => AmdSmuService.SetSlowLimit(mw);

    /// <summary>STAPM 持续时间窗口 (秒)</summary>
    public static bool SetStapmTime(uint seconds) => AmdSmuService.SetStapmTime(seconds);

    /// <summary>Slow PPT 持续时间窗口 (秒)</summary>
    public static bool SetSlowTime(uint seconds) => AmdSmuService.SetSlowTime(seconds);

    // ── VRM 电流限制 (TDC / EDC) ──

    /// <summary>CPU VRM TDC (mA)</summary>
    public static bool SetVrmCurrent(uint ma) => AmdSmuService.SetVrmCurrent(ma);

    /// <summary>SoC VRM TDC (mA)</summary>
    public static bool SetVrmSocCurrent(uint ma) => AmdSmuService.SetVrmSocCurrent(ma);

    /// <summary>CPU VRM EDC (mA)</summary>
    public static bool SetVrmMaxCurrent(uint ma) => AmdSmuService.SetVrmMaxCurrent(ma);

    /// <summary>SoC VRM EDC (mA)</summary>
    public static bool SetVrmSocMaxCurrent(uint ma) => AmdSmuService.SetVrmSocMaxCurrent(ma);

    // ── 温度限制 ──

    /// <summary>Tctl 温度限制 (°C)</summary>
    public static bool SetTctlTemp(uint tempC) => AmdSmuService.SetTctlTemp(tempC);

    /// <summary>皮肤温度功耗限制 (mW)</summary>
    public static bool SetSkinTempLimit(uint mw) => AmdSmuService.SetSkinTempLimit(mw);

    /// <summary>APU 皮肤温度限制 (°C)</summary>
    public static bool SetApuSkinTemp(uint tempC) => AmdSmuService.SetApuSkinTemp(tempC);

    /// <summary>dGPU 皮肤温度限制 (°C)</summary>
    public static bool SetDgpuSkinTemp(uint tempC) => AmdSmuService.SetDgpuSkinTemp(tempC);

    // ── iGPU 时钟 ──

    /// <summary>iGPU 时钟覆盖 (MHz)</summary>
    public static bool SetGfxClk(uint mhz) => AmdSmuService.SetGfxClk(mhz);
  }
}
