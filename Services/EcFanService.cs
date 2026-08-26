// Services/EcFanService.cs - EC 直接读写风扇控制 (暗影精灵 6 适配)
// 使用项目已有的 PawnIO (LpcACPIEC.bin) 读写 EC 寄存器,无需 WinRing0
// 参考 OmenMon 项目: https://github.com/GeographicCone/OmenMon
// EC 寄存器表: OmenMon/Hardware/EcData.cs

using System;
using LibreHardwareMonitor.PawnIo;

namespace OmenSuperHub.Services {
  /// <summary>
  /// EC (Embedded Controller) 直接读写服务 — 暗影精灵 6 等老机型适配
  /// 当 WMI BIOS (hpqBIntM) 失败时,降级到 EC 寄存器直接读写
  /// </summary>
  public static class EcFanService {
    // ─── EC 寄存器定义 (来自 OmenMon EcData.cs,基于 HP 08A14 DSDT) ───
    private const byte REG_FAN_SET_SPEED_1 = 0x2C;  // XSS1 - 左风扇设定转速 [%]
    private const byte REG_FAN_SET_SPEED_2 = 0x2D;  // XSS2 - 右风扇设定转速 [%]
    private const byte REG_FAN_GET_SPEED_1 = 0x2E;  // XGS1 - 左风扇实际转速 [%]
    private const byte REG_FAN_GET_SPEED_2 = 0x2F;  // XGS2 - 右风扇实际转速 [%]
    private const byte REG_FAN_RPM_1_LO    = 0xB0;  // RPM1 - 左风扇 RPM 低字节
    private const byte REG_FAN_RPM_1_HI    = 0xB1;  // RPM2 - 左风扇 RPM 高字节
    private const byte REG_FAN_RPM_2_LO    = 0xB2;  // RPM3 - 右风扇 RPM 低字节
    private const byte REG_FAN_RPM_2_HI    = 0xB3;  // RPM4 - 右风扇 RPM 高字节
    private const byte REG_CPU_TEMP        = 0x57;  // CPUT - CPU 温度 [°C]
    private const byte REG_GPU_TEMP        = 0xB7;  // GPTM - GPU 温度 [°C]
    private const byte REG_MANUAL_CONTROL  = 0x62;  // OMCC - 手动风扇控制开关
    private const byte REG_MAX_FAN         = 0xEC;  // FFFF - 最大风扇转速开关
    private const byte REG_FAN_SWITCH      = 0xF4;  // SFAN - 风扇总开关

    // ─── ACPI EC 标准端口 ───
    private const byte EC_PORT_DATA    = 0x62;  // EC_DATA
    private const byte EC_PORT_COMMAND = 0x66;  // EC_SC

    // ─── ACPI EC 标准命令 ───
    private const byte EC_CMD_READ  = 0x80;  // RD_EC
    private const byte EC_CMD_WRITE = 0x81;  // WR_EC

    // ─── EC 状态位 ───
    private const byte EC_STATUS_OBF = 0x01;  // Output Buffer Full
    private const byte EC_STATUS_IBF = 0x02;  // Input Buffer Full

    private const int EC_WAIT_LIMIT  = 30;   // 等待重试次数
    private const int EC_RETRY_LIMIT = 3;    // 读写重试次数

    private static LpcAcpiEc _ec;
    private static readonly object _lock = new();
    private static bool _initFailed;

    /// <summary>EC 是否可用 (PawnIO 已安装且模块加载成功)</summary>
    public static bool IsAvailable {
      get {
        if (_initFailed) return false;
        if (_ec != null) return true;
        lock (_lock) {
          if (_ec != null) return true;
          if (_initFailed) return false;
          try {
            if (!PawnIo.IsInstalled) { _initFailed = true; return false; }
            _ec = new LpcAcpiEc();
            // ponytail: LpcAcpiEc 没有 IsLoaded 属性,通过读 EC 状态端口验证模块是否加载成功
            // 端口 0x66 在任何 x86 上都存在,读到全 0xFF 说明驱动未响应
            byte probe = _ec.ReadPort(EC_PORT_COMMAND);
            if (probe == 0xFF) { _initFailed = true; _ec.Close(); _ec = null; return false; }
            Logger.Info($"[EcFanService] PawnIO LpcACPIEC loaded, EC probe=0x{probe:X2}");
            return true;
          } catch (Exception ex) {
            Logger.Error($"[EcFanService] Init failed: {ex.Message}");
            _initFailed = true;
            return false;
          }
        }
      }
    }

    /// <summary>
    /// 通过 EC 设置风扇转速 (百分比 0-100)
    /// </summary>
    public static bool SetFanSpeed(int fan1Percent, int fan2Percent) {
      if (!IsAvailable) return false;
      lock (_lock) {
        try {
          // 1. 启用手动风扇控制 (OMCC = 1)
          if (!EcWriteByte(REG_MANUAL_CONTROL, 0x01)) return false;

          // 2. 写入风扇转速百分比 (XSS1/XSS2)
          if (!EcWriteByte(REG_FAN_SET_SPEED_1, (byte)Math.Max(0, Math.Min(100, fan1Percent)))) return false;
          if (!EcWriteByte(REG_FAN_SET_SPEED_2, (byte)Math.Max(0, Math.Min(100, fan2Percent)))) return false;

          Logger.Verbose($"[EcFanService] Set fan via EC: {fan1Percent}%, {fan2Percent}%");
          return true;
        } catch (Exception ex) {
          Logger.Error($"[EcFanService] SetFanSpeed failed: {ex.Message}");
          return false;
        }
      }
    }

    /// <summary>读取 CPU 温度 [°C] (-1 = 失败)</summary>
    public static float ReadCpuTemp() {
      if (!IsAvailable) return -1;
      lock (_lock) {
        try { return EcReadByte(REG_CPU_TEMP); }
        catch { return -1; }
      }
    }

    /// <summary>读取 GPU 温度 [°C] (-1 = 失败)</summary>
    public static float ReadGpuTemp() {
      if (!IsAvailable) return -1;
      lock (_lock) {
        try { return EcReadByte(REG_GPU_TEMP); }
        catch { return -1; }
      }
    }

    /// <summary>读取左风扇 RPM</summary>
    public static int ReadFan1Rpm() {
      if (!IsAvailable) return -1;
      lock (_lock) {
        try {
          int lo = EcReadByte(REG_FAN_RPM_1_LO);
          int hi = EcReadByte(REG_FAN_RPM_1_HI);
          return (hi << 8) | lo;
        } catch { return -1; }
      }
    }

    /// <summary>读取右风扇 RPM</summary>
    public static int ReadFan2Rpm() {
      if (!IsAvailable) return -1;
      lock (_lock) {
        try {
          int lo = EcReadByte(REG_FAN_RPM_2_LO);
          int hi = EcReadByte(REG_FAN_RPM_2_HI);
          return (hi << 8) | lo;
        } catch { return -1; }
      }
    }

    // ═══ EC 底层读写 (标准 ACPI 协议) ═══

    private static bool EcReadByte(byte register, out byte value) {
      value = 0;
      // 1. 等待输入缓冲区空
      if (!WaitEcReady(false)) return false;
      // 2. 发送读命令
      _ec.WritePort(EC_PORT_COMMAND, EC_CMD_READ);
      // 3. 等待输入缓冲区空
      if (!WaitEcReady(false)) return false;
      // 4. 发送寄存器地址
      _ec.WritePort(EC_PORT_DATA, register);
      // 5. 等待输出缓冲区满
      if (!WaitEcReady(true)) return false;
      // 6. 读取数据
      value = _ec.ReadPort(EC_PORT_DATA);
      return true;
    }

    private static byte EcReadByte(byte register) {
      for (int i = 0; i < EC_RETRY_LIMIT; i++) {
        if (EcReadByte(register, out byte value)) return value;
      }
      return 0;
    }

    private static bool EcWriteByte(byte register, byte value) {
      for (int i = 0; i < EC_RETRY_LIMIT; i++) {
        if (EcWriteByteImpl(register, value)) return true;
      }
      return false;
    }

    private static bool EcWriteByteImpl(byte register, byte value) {
      // 1. 等待输入缓冲区空
      if (!WaitEcReady(false)) return false;
      // 2. 发送写命令
      _ec.WritePort(EC_PORT_COMMAND, EC_CMD_WRITE);
      // 3. 等待输入缓冲区空
      if (!WaitEcReady(false)) return false;
      // 4. 发送寄存器地址
      _ec.WritePort(EC_PORT_DATA, register);
      // 5. 等待输入缓冲区空
      if (!WaitEcReady(false)) return false;
      // 6. 发送数据
      _ec.WritePort(EC_PORT_DATA, value);
      return true;
    }

    /// <summary>
    /// 等待 EC 就绪
    /// </summary>
    /// <param name="waitOutput">true=等待输出缓冲区满(OBF=1), false=等待输入缓冲区空(IBF=0)</param>
    private static bool WaitEcReady(bool waitOutput) {
      for (int i = 0; i < EC_WAIT_LIMIT; i++) {
        byte status = _ec.ReadPort(EC_PORT_COMMAND);
        if (waitOutput) {
          // 等待 OBF=1 (有数据可读)
          if ((status & EC_STATUS_OBF) != 0) return true;
        } else {
          // 等待 IBF=0 (可以写入)
          if ((status & EC_STATUS_IBF) == 0) return true;
        }
        System.Threading.Thread.SpinWait(100);
      }
      return false;
    }

    /// <summary>释放 EC 资源</summary>
    public static void Close() {
      lock (_lock) {
        _ec?.Close();
        _ec = null;
      }
    }
  }
}


