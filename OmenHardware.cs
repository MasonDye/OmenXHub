// OmenHardware.cs - HP OMEN 硬件通信抽象层
// WMI BIOS 通信、风扇控制、性能模式、CPU/GPU 功耗、灯效、BIOS 设置、GPU 检测
using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Runtime.InteropServices;
using HP.Omen.Core.Model.Device.Models;
using HP.Omen.Core.Common.PowerControl;

namespace OmenSuperHub {
  internal class OmenHardware {
    static readonly byte[] Sign = { 0x53, 0x45, 0x43, 0x55 };
    static ManagementScope _wmiScope;
    static readonly object _scopeLock = new object();
    // ponytail: 缓存 hpqBIntM 方法宿主对象 — 旧实现每次调用 new ManagementObjectSearcher
    // 全量枚举该类,是 WMI 单次延迟的主要来源(百毫秒级);缓存后静态色通道才撑得起
    // 软件渲染动画的 20 FPS(参考 OmenLinux/omen-rgb-keyboard 同接口帧率)。
    // ManagementException 时 Dispose+置 null 重探(与 scope 同生命周期,sleep/resume 断连)。
    static ManagementObject _biosMethods;

    // ── WMI result cache (static hardware info, never changes at runtime) ──
    static byte[] _cachedDesignData;
    static string _cachedBiosVersion;
    static string _cachedCpuModel;
    static bool? _cachedHasNvidia;
    static bool? _cachedHasIntelCpu;
    static bool? _cachedHasAmdCpu;
    static bool? _cachedHasAmdGpu;
    static bool? _cachedHasAmdDiscrete;

    // ponytail: cache ManagementScope to reuse WMI connection across calls
    static ManagementScope GetWmiScope() {
      if (_wmiScope == null) {
        lock (_scopeLock) {
          if (_wmiScope == null) {
            _wmiScope = new ManagementScope(@"root\wmi");
            _wmiScope.Connect();
          }
        }
      }
      return _wmiScope;
    }

    // ─── WMI Communication ────────────────────────────────────────────
    public static byte[] SendOmenBiosWmi(uint commandType, byte[] data, int outputSize, uint command = 0x20008) {
      const string className = "hpqBIntM";
      string methodName = "hpqBIOSInt" + outputSize.ToString();
      var scope = GetWmiScope();

      if (Services.ConfigService.VerboseLogging) {
        string dataHex = data != null ? BitConverter.ToString(data).Replace("-", " ") : "null";
        Logger.Verbose($"SendOmenBiosWmi: CmdType=0x{commandType:X2} Cmd=0x{command:X} Method={methodName} Data=[{dataHex}]");
      }

      try {
        // ponytail: _scopeLock 串行化整个 BIOS WMI 调用 — hpqBIntM 方法对象缓存后是共享
        // COM 状态,并发 Invoke 有竞态;BIOS 通道本身串行,锁不损失吞吐。
        lock (_scopeLock) {
          if (_biosMethods == null) {
            using (var searcher = new ManagementObjectSearcher(scope, new ObjectQuery($"SELECT * FROM {className}")))
            using (var collection = searcher.Get()) {
              // ponytail: 枚举出的 ManagementObject 独立持有 COM 引用,searcher/collection
              // 释放后仍可用 — 缓存它,后续调用直接 Invoke(仅首枚举一次)。
              _biosMethods = collection.Cast<ManagementObject>().FirstOrDefault();
            }
          }
          if (_biosMethods == null) {
            Logger.Error($"SendOmenBiosWmi: {className} WMI class not found!");
            return null;
          }

          using (var biosDataInClass = new ManagementClass(scope, new ManagementPath("hpqBDataIn"), null))
          using (var biosDataIn = biosDataInClass.CreateInstance()) {
            biosDataIn["Command"] = command;
            biosDataIn["CommandType"] = commandType;
            biosDataIn["Sign"] = Sign;
            if (data != null) {
              biosDataIn["hpqBData"] = data;
              biosDataIn["Size"] = (uint)data.Length;
            } else {
              biosDataIn["Size"] = (uint)0;
            }

            using (var inParams = _biosMethods.GetMethodParameters(methodName)) {
              inParams["InData"] = biosDataIn;

              using (var result = _biosMethods.InvokeMethod(methodName, inParams, null)) {
                using (var outData = result["OutData"] as ManagementBaseObject) {
                  uint returnCode = (uint)outData["rwReturnCode"];

                  if (returnCode == 0) {
                    Logger.Verbose($"SendOmenBiosWmi: SUCCESS CmdType=0x{commandType:X2} ReturnCode=0");
                    if (outputSize != 0)
                      return (byte[])outData["Data"];
                    else
                      return Array.Empty<byte>();
                  } else {
                    // ponytail: 暗影精灵 6 的 BIOS 在 SetFanLevel(0x2E) 时把 RPM 值写入 rwReturnCode
                    // (而非状态码 0),导致误报错误。检测到此情况时降级为 Verbose 日志。
                    if (commandType == 0x2E && returnCode == 0x2E) {
                      Logger.Verbose($"SendOmenBiosWmi: CmdType=0x{commandType:X2} executed, fan RPM={returnCode} (Omen 6 BIOS quirk)");
                      return Array.Empty<byte>();
                    }
                    string errorMessage = "";
                    switch (returnCode) {
                      case 0x03: errorMessage = "Command Not Available"; break;
                      case 0x05: errorMessage = "Input or Output Size Too Small"; break;
                    }
                    Logger.Error($"SendOmenBiosWmi: FAILED CmdType=0x{commandType:X2} ReturnCode=0x{returnCode:X8} {errorMessage}");
                  }
                }
              }
            }
          }
        }
      } catch (ManagementException ex) {
        Logger.Error($"SendOmenBiosWmi: WMI EXCEPTION CmdType=0x{commandType:X2}: {ex.ErrorCode} - {ex.Message}");
        // ponytail: invalidate cached scope+methods so next call reconnects (sleep/resume can break WMI)
        lock (_scopeLock) {
          _biosMethods?.Dispose();
          _biosMethods = null;
          _wmiScope = null;
        }
      } catch (Exception ex) {
        Logger.Error($"SendOmenBiosWmi: EXCEPTION CmdType=0x{commandType:X2}: {ex.Message}");
      }
      return null;
    }

    // ─── System Design Data ───────────────────────────────────────────
    public static byte[] GetSystemDesignData() {
      if (_cachedDesignData != null) return _cachedDesignData;
      _cachedDesignData = SendOmenBiosWmi(0x28, new byte[] { 0x00, 0x00, 0x00, 0x00 }, 128);
      return _cachedDesignData;
    }

    public static int GetAdapterPower() {
      byte[] data = GetSystemDesignData();
      if (data == null || data.Length < 2) return -1;
      return data[0] | (data[1] << 8);
    }

    // ─── Fan ──────────────────────────────────────────────────────────
    public static void GetFanCount() {
      SendOmenBiosWmi(0x10, new byte[] { 0x00, 0x00, 0x00, 0x00 }, 4);
    }

    public static bool GetFanCount(out bool ocp, out bool otp) {
      ocp = false; otp = false;
      byte[] result = SendOmenBiosWmi(0x10, new byte[] { 0x00, 0x00, 0x00, 0x00 }, 4);
      if (result == null || result.Length < 2) return false;
      otp = (result[1] & 0x02) != 0;
      ocp = (result[1] & 0x01) != 0;
      return true;
    }

    public static List<int> GetFanLevel() {
      List<int> fanSpeedNow = new List<int> { 0, 0, 0 };
      byte[] fanLevel = SendOmenBiosWmi(0x2D, new byte[] { 0x00, 0x00, 0x00, 0x00 }, 128);
      if (fanLevel != null) {
        if (fanLevel.Length >= 3) {
          fanSpeedNow[0] = fanLevel[0];
          fanSpeedNow[1] = fanLevel[1];
          fanSpeedNow[2] = fanLevel[2];
        }
      }
      return fanSpeedNow;
    }

    // ponytail: 标准机型风扇 RPM 查询,对照 OmenCtl hp-wmi.c:768-777。
    // oxh 现有 GetFanLevel() 走 0x2D 是 Victus_S 路径(outsize=128, 返回 3 个 level 0-255);
    // 0x11 是标准 Omen 机型路径(outsize=4, 返回单风扇 RPM u16, [2]<<8|[3])。
    // fanIndex 通常 0=CPU, 1=GPU。失败返回 -1。不替代 GetFanLevel,语义不同不能互换。
    public static int GetFanSpeedRpm(int fanIndex) {
      byte[] result = SendOmenBiosWmi(0x11, new byte[] { (byte)fanIndex, 0, 0, 0 }, 4);
      if (result == null || result.Length < 4) return -1;
      return (result[2] << 8) | result[3];
    }

    public static byte[] GetFanTable() {
      return SendOmenBiosWmi(0x2F, new byte[] { 0x00, 0x00, 0x00, 0x00 }, 128);
    }

    public enum FanType {
      Unsupported = 0, Cpu = 1, Gpu = 2, Exhaust = 3, Pump = 4, Intake = 5, Vrm = 6, LightingBoard = 100
    }

    public static void GetFanType(out List<FanType> types, out List<bool> capabilities) {
      types = new List<FanType>();
      capabilities = new List<bool>();
      byte[] sync = SendOmenBiosWmi(44, new byte[4] { 0, 0, 0, 0 }, 128, 0x20008);
      if (sync == null || sync.Length == 0) return;
      for (int i = 0; i < 4 && i < sync.Length; i++) {
        types.Add((FanType)(sync[i] & 0x0F));
        types.Add((FanType)((sync[i] & 0xF0) >> 4));
      }
      if (types.Count > 0) types.RemoveAt(types.Count - 1);
      for (int bit = 0; bit < 16; bit++) {
        int byteIndex = 8 + (bit / 8);
        if (byteIndex >= sync.Length) break;
        capabilities.Add(((sync[byteIndex] >> (bit % 8)) & 1) != 0);
      }
    }

    // ponytail: 三风扇缓存判定 —— IsThreeFanSupported() 每次发 WMI(0x2C)，心跳每秒调用
    // 会多一次 BIOS 往返。风扇数量是机器固定属性,进程内探测一次缓存。
    static bool _is3FanCached;
    static bool _is3Fan;
    public static bool IsThreeFan() {
      if (!_is3FanCached) {
        GetFanType(out var types, out _);
        // ponytail: GetFanType 返回空 = WMI 未就绪/命令失败 — 空结果不缓存,下个 fan tick 重探,
        // 与 GetSystemDesignData 失败不缓存同一惯例;否则冷启动首探失败会把三扇机型
        // 锁死成 false 整个会话(第三扇曲线/UI 永远消失)。
        if (types.Count == 0) return false;
        _is3Fan = types.Count > 2 && types[2] != FanType.Unsupported;
        _is3FanCached = true;
      }
      return _is3Fan;
    }

    public static bool IsCleanCreekSupported() {
      GetFanType(out var fanTypes, out var capabilities);
      if (capabilities.Count > fanTypes.Count)
        capabilities = capabilities.Take(fanTypes.Count).ToList();
      return capabilities.Any(supported => supported);
    }

    public static bool IsLegacyCleanCreekSupported() {
      if (IsCleanCreekSupported()) return false;
      byte[] result = SendOmenBiosWmi(44, null, 4, 1);
      if (result == null || result.Length < 1) return false;
      return (result[0] & 0x20) != 0;
    }

    public static bool SetLegacyCleanCreek(bool enable) {
      byte[] state = SendOmenBiosWmi(44, null, 4, 1);
      if (state == null || state.Length < 4) return false;
      if (enable) state[3] |= 0x80;
      else state[3] &= 0x7F;
      byte[] result = SendOmenBiosWmi(44, state, 0, 2);
      return result != null;
    }

    // ponytail: delegate to the 3-param overload — a single WMI write per call.
    // The previous 3-step sequence (SetMaxFanSpeedOff → {0,0} → target) was a
    // >6000 RPM runaway fix, but running it on every timer tick (once per second)
    // overwhelms the EC on AMD laptops, causing the fan to stay at zero or
    // stop responding.  Callers that need EC reset (manual mode switch, power
    // resume) already call SetMaxFanSpeedOff() explicitly before SetFanLevel.
    public static void SetFanLevel(int fanSpeed1, int fanSpeed2) {
      SetFanLevel(fanSpeed1, fanSpeed2, false, false);
    }

    public static void SetFanLevel(int fanSpeed1, int fanSpeed2, bool fan3 = false, bool fanClean = false) {
      SetFanLevelInternal(fanSpeed1, fanSpeed2, null, fanClean, fan3);
    }

    // ponytail: 独立第三扇 —— 第 3 字节写独立值而非均值。OSH 参考实现只用均值(无第三扇
    // 温度源时的保守策略),但 byte 槽本身接受任意 0-255,独立曲线经此重载下发。
    // fanSpeed3 为 null 时回退均值语义(与三参重载一致)。
    public static void SetFanLevel(int fanSpeed1, int fanSpeed2, int? fanSpeed3) {
      SetFanLevelInternal(fanSpeed1, fanSpeed2, fanSpeed3, false, fanSpeed3.HasValue);
    }

    // R15/BUG-R15-1: EC 风扇槽位是 byte,调用方传百分比(0-100)。旧实现直接 (byte) 强转,
    // 负值走补码截断(-1 → 0xFF=255,向 EC 写"最高速",与语义完全相反),>255 截断。
    // 纯防御钳位:合法 0-100 值原样通过,仅拦截越界输入,零回归。
    static byte ClampFanByte(int v) => (byte)(v < 0 ? 0 : v > 255 ? 255 : v);

    static void SetFanLevelInternal(int fanSpeed1, int fanSpeed2, int? fanSpeed3, bool fanClean, bool fan3) {
      byte[] data = new byte[fan3 ? 3 : 2];
      if (fanClean) {
        GetFanType(out var types, out var capabilities);
        var caps = capabilities.Take(types.Count).ToList();
        // R15/BUG: GetFanType 返回空(WMI 未就绪/失败)时 caps 可能不足 2-3 项,旧实现 caps[0..2]
        // 直接越界。边界安全索引:槽位缺失按"该扇不支持逆转"处理 → 写正常转速(比盲发 +128 安全)。
        bool Cap(int i) => i < caps.Count && caps[i];
        data[0] = ClampFanByte(Cap(0) ? fanSpeed1 + 128 : fanSpeed1);
        data[1] = ClampFanByte(Cap(1) ? fanSpeed2 + 128 : fanSpeed2);
        if (fan3) {
          int v = fanSpeed3 ?? (fanSpeed1 + fanSpeed2) / 2;
          data[2] = ClampFanByte(Cap(2) ? v + 128 : v);
        }
      } else {
        data[0] = ClampFanByte(fanSpeed1);
        data[1] = ClampFanByte(fanSpeed2);
        if (fan3) {
          int v = fanSpeed3 ?? (fanSpeed1 + fanSpeed2) / 2;
          data[2] = ClampFanByte(v);
        }
      }
      // ponytail: 暗影精灵 6 等老机型 WMI BIOS 可能失败,失败时降级到 EC 直接读写
      // (需 PawnIO 驱动; 参考 OmenMon 项目的 EC 寄存器表)
      var result = SendOmenBiosWmi(0x2E, data, 0);
      if (result == null && Services.EcFanService.IsAvailable) {
        Logger.Verbose($"[OmenHardware] WMI SetFanLevel failed, EC fallback: {fanSpeed1}%, {fanSpeed2}%");
        Services.EcFanService.SetFanSpeed(fanSpeed1, fanSpeed2);
      }
    }

    public static void SetMaxFanSpeedOn() { SendOmenBiosWmi(0x27, new byte[] { 0x01 }, 0); }
    public static void SetMaxFanSpeedOff() { SendOmenBiosWmi(0x27, new byte[] { 0x00 }, 0); }

    // ─── Performance Mode ─────────────────────────────────────────────
    public enum PerformanceModeOnUI {
      Default, Performance, Cool, Quiet, Extreme, Balance, Eco, Unleash
    }

    public static readonly Dictionary<PerformanceModeOnUI, string> ModeNames =
      new Dictionary<PerformanceModeOnUI, string> {
        { PerformanceModeOnUI.Default, "均衡模式" },
        { PerformanceModeOnUI.Performance, "狂暴模式" },
        { PerformanceModeOnUI.Cool, "酷冷模式" },
        { PerformanceModeOnUI.Quiet, "安静模式" },
        { PerformanceModeOnUI.Extreme, "极限模式" },
        { PerformanceModeOnUI.Balance, "平衡模式" },
        { PerformanceModeOnUI.Eco, "Eco（节能模式）" },
        { PerformanceModeOnUI.Unleash, "大师模式" }
      };

    public static readonly Dictionary<PerformanceModeOnUI, string> ModeDescriptions =
      new Dictionary<PerformanceModeOnUI, string> {
        { PerformanceModeOnUI.Default, "适合各种类型的任务。" },
        { PerformanceModeOnUI.Performance, "适合游戏和内容创作。可能提高温度和噪音水平。" },
        { PerformanceModeOnUI.Cool, "适合轻度任务。降低 CPU 和 GPU 温度。" },
        { PerformanceModeOnUI.Quiet, "通过降低性能将风扇噪音保持在最低限度。" },
        { PerformanceModeOnUI.Extreme, "解除功率限制以获得最高性能。" },
        { PerformanceModeOnUI.Balance, "适合常规任务。降低性能上限换取更低的噪音和温度。" },
        { PerformanceModeOnUI.Eco, "限制系统性能和功耗，以降低热量和噪音水平。" },
        { PerformanceModeOnUI.Unleash, "解除功率限制以获得最高性能。" }
      };

    public enum PerformanceMode {
      Default = 0, Performance = 1, Cool = 2, Quiet = 3, Extreme = 4, L8 = 4,
      L0 = 16, L5 = 17, L1 = 32, L6 = 33, L2 = 48, L7 = 49, L3 = 64, L4 = 80, Eco = 256
    }

    public enum ThermalPolicyVersion { V0 = 0, V1 = 1 }

    public static ThermalPolicyVersion GetThermalPolicyVersion() {
      byte[] data = GetSystemDesignData();
      if (data == null || data.Length < 4) return ThermalPolicyVersion.V0;
      ThermalPolicyVersion version = data[3] == 1 ? ThermalPolicyVersion.V1 : ThermalPolicyVersion.V0;
      string[] v0Blacklist = { "8607", "8746", "8747", "8749", "874A", "8748" };
      if (v0Blacklist.Contains(DeviceModel.ThisSystemID))
        version = ThermalPolicyVersion.V0;
      return version;
    }

    private static bool? _isSupported;
    public static bool IsSupported() {
      if (!_isSupported.HasValue) {
        _isSupported = false;
        try {
          _isSupported = GetThermalPolicyVersion() == ThermalPolicyVersion.V1;
        } catch (Exception ex) { Logger.Verbose($"[IsSupported] {ex.Message}"); _isSupported = false; }
      }
      return _isSupported.Value;
    }

    public static bool IsSwFanControlSupport() {
      byte[] data = GetSystemDesignData();
      return data != null && data.Length > 4 && (data[4] & 1) > 0;
    }

    public static List<PerformanceModeOnUI> GetSupportedPerformanceModes() {
      var modes = new List<PerformanceModeOnUI>();
      byte[] design = GetSystemDesignData();
      if (design == null || design.Length < 5) return modes;
      ThermalPolicyVersion version = GetThermalPolicyVersion();
      bool swFanControl = (design[4] & 0x01) != 0;
      bool turboSupport = (design[4] & 0x02) != 0;
      if (version == ThermalPolicyVersion.V1) {
        modes.Add(PerformanceModeOnUI.Eco);
        modes.Add(PerformanceModeOnUI.Balance);
        if (swFanControl) {
          modes.Add(PerformanceModeOnUI.Performance);
          if (turboSupport) modes.Add(PerformanceModeOnUI.Unleash);
        }
      } else {
        modes.Add(PerformanceModeOnUI.Eco);
        modes.Add(PerformanceModeOnUI.Default);
        modes.Add(PerformanceModeOnUI.Cool);
        if (turboSupport) modes.Add(PerformanceModeOnUI.Performance);
      }
      return modes;
    }

    public static void SetFanMode(PerformanceModeOnUI uiMode) {
      ThermalPolicyVersion version = GetThermalPolicyVersion();
      byte ecCommand = 0;
      switch (version) {
        case ThermalPolicyVersion.V0:
          ecCommand = (byte)(uiMode == PerformanceModeOnUI.Eco ? PerformanceModeOnUI.Default : uiMode);
          break;
        case ThermalPolicyVersion.V1:
          switch (uiMode) {
            case PerformanceModeOnUI.Default:
            case PerformanceModeOnUI.Balance:
            case PerformanceModeOnUI.Eco: ecCommand = (byte)PerformanceMode.L2; break;
            case PerformanceModeOnUI.Performance: ecCommand = (byte)PerformanceMode.L7; break;
            case PerformanceModeOnUI.Cool: ecCommand = (byte)PerformanceMode.L4; break;
            case PerformanceModeOnUI.Extreme:
            case PerformanceModeOnUI.Unleash: ecCommand = (byte)PerformanceMode.L7; break;
            default: ecCommand = (byte)PerformanceMode.L2; break;
          }
          break;
      }
      SendOmenBiosWmi(0x1A, new byte[] { 0xFF, ecCommand }, 0);
    }

    public static void SetFanMode(PerformanceMode mode) {
      SendOmenBiosWmi(0x1A, new byte[] { 0xFF, (byte)mode }, 0);
    }

    // Raw byte overload for backward compatibility (0x31=performance, 0x30=default)
    public static void SetFanMode(byte ecCommand) {
      SendOmenBiosWmi(0x1A, new byte[] { 0xFF, ecCommand }, 0);
    }

    // ponytail: V0/V1 兼容映射 —— 项目历史沿用裸字节 0x31(Unleash)/0x30(Default)，但这两值
    // 的语义按 ThermalPolicy 版本而异(OSH 已验证表)：V1 下 0x31=L7=Performance/Unleash；
    // V0 映射表无 L7 条目，黑名单老机(8607/8746/8747/8749/874A/8748)盲发 0x31 是未定义行为。
    // 统一经本辅助按机型版本映射成安全 EC 值；V1 机映射结果与原字节一致(零回归)，
    // 仅 V0 黑名单机被矫正到 V0 表的安全档。
    public static byte GetEcPerformanceCommand(byte legacyCommand) {
      // 0x31(Unleash/Performance) 与 0x30(Default/平衡) 是仅有的两个历史值
      bool unleash = legacyCommand == 0x31;
      ThermalPolicyVersion version = GetThermalPolicyVersion();
      if (version == ThermalPolicyVersion.V1)
        return unleash ? (byte)PerformanceMode.L7 : (byte)PerformanceMode.L2;
      // V0: OSH 表 —— Eco/Default 语义档直接用枚举值; Unleash 在 V0 无对应档,回退 Default。
      return unleash ? (byte)PerformanceMode.Default : (byte)PerformanceMode.Default;
    }

    /// <summary>按机型 ThermalPolicy 版本矫正后的风扇模式写入(全工程 SetFanMode((byte)) 调用应改走此入口)。</summary>
    public static void SetFanModeCompat(byte legacyCommand) {
      SendOmenBiosWmi(0x1A, new byte[] { 0xFF, GetEcPerformanceCommand(legacyCommand) }, 0);
    }

    // ─── CPU Power ────────────────────────────────────────────────────
    // ponytail: 0x29 (PL1/PL2/PL4/TPP) 在狂暴和平衡模式下都生效（参考 OSH 注释
    // "狂暴平衡都生效"），所以这里不耦合 SetUnleashMode()。OSH 参考实现里这些 setter
    // 都是纯 WMI 写，模式切换在 RestorePowerConfig 中独立完成且带 1000ms 延迟。
    //
    // 之前在每个 setter 里同步调 SetUnleashMode() 会触发竞态：0x1A(模式切换) 发出后
    // 0x29 立即同步发出，但 EC 异步处理模式切换——当 EC 完成 L7 切换时会按该模式的
    // BIOS 默认值重置 CPU 功耗限制，覆盖刚写入的 0x29 值。表现为 Omen Transcend 16
    // (8bb3 / i7-13700HX) 等机型 "power limits do not apply to CPU"。
    // Unleash 模式仍由 App.xamlcs 启动、PresetManager、TrayService 等独立设置。
    // ─── EC 模式双重写入（MSR 直写 + WMI 0x29）────────────────────────
    // EnableEcAccess 开启且 RAPL 0x610 可写时，功率墙两条通道都发：MSR 直写（带回读
    // 验证，权威）+ WMI 0x29 补发（同步 EC 侧状态）。WMI 半程延迟 DualSendIntervalMs
    // 在线程池发出：MSR 先落定再间隔补发，不阻塞 UI/预设回放线程。窗口内的 PL1/PL2
    // 变更合并进同一载荷（0x29 一次带 [PL2,PL1] 两个字段），拖滑杆等快速连续调用只发
    // 最新值且按提交序 —— 最终状态与 MSR 侧一致；EC 收到乱序旧值反而会改错最终状态。
    const int DualSendIntervalMs = 200;
    static readonly object _dualWmiLock = new object();
    static byte? _dualPl1, _dualPl2;
    static bool _dualWorkerRunning;

    /// <summary>0x29 载荷 [PL2, PL1, PL4, TPP]，null 字段用 0xFF"保持原值"哨兵。纯函数，SelfCheck 覆盖。</summary>
    internal static byte[] EncodeDualWmiPayload(byte? pl1, byte? pl2) {
      return new byte[] { pl2 ?? (byte)0xFF, pl1 ?? (byte)0xFF, 0xFF, 0xFF };
    }

    static void QueueDualWmiSend(byte? pl1, byte? pl2) {
      lock (_dualWmiLock) {
        // ponytail: null = 该字段不动，不清另一槽位 —— PL1Only/PL2Only 连发时两个
        // 变更合并进一次载荷，等价于两次独立发送且不丢字段。
        if (pl1.HasValue) _dualPl1 = pl1;
        if (pl2.HasValue) _dualPl2 = pl2;
        if (_dualWorkerRunning) return;
        _dualWorkerRunning = true;
      }
      System.Threading.ThreadPool.QueueUserWorkItem(_ => {
        while (true) {
          System.Threading.Thread.Sleep(DualSendIntervalMs);
          byte? p1, p2;
          lock (_dualWmiLock) {
            p1 = _dualPl1; p2 = _dualPl2;
            _dualPl1 = _dualPl2 = null;
            if (p1 == null && p2 == null) { _dualWorkerRunning = false; return; }
          }
          SendOmenBiosWmi(0x29, EncodeDualWmiPayload(p1, p2), 0);
        }
      });
    }

    /// <summary>MSR 写失败走 WMI 同步兜底前调用：清空待发槽，防止晚到的队列补发覆盖刚发出的兜底值。</summary>
    static void ClearDualWmiPending() {
      lock (_dualWmiLock) { _dualPl1 = _dualPl2 = null; }
    }

    /// <summary>Set both PL1 and PL2 to the same value (backward compat).</summary>
    public static bool SetCpuPowerLimit(byte value) {
      return SetCpuPowerLimit(value, value);
    }
    /// <summary>Set PL1 and PL2 independently.</summary>
    // EnableEcAccess 开启且 RAPL 0x610 可写时双重发送（见上方 EC 模式双重写入）；
    // MSR 被拒则立即同步走 WMI 兜底（原有行为，返回语义不变）。
    public static bool SetCpuPowerLimit(byte pl1, byte pl2) {
      if (Services.RaplPowerLimitService.ShouldHandlePowerLimit) {
        if (Services.RaplPowerLimitService.TrySetPowerLimits(pl1, pl2)) { QueueDualWmiSend(pl1, pl2); return true; }
        ClearDualWmiPending();
      }
      var result = SendOmenBiosWmi(0x29, new byte[] { pl2, pl1, 0xFF, 0xFF }, 0);
      return result != null;
    }

    /// <summary>Set PL1 only, PL2 unchanged.</summary>
    public static bool SetCpuPowerLimitPL1Only(byte pl1) {
      if (Services.RaplPowerLimitService.ShouldHandlePowerLimit) {
        if (Services.RaplPowerLimitService.TrySetPowerLimits(pl1, null)) { QueueDualWmiSend(pl1, null); return true; }
        ClearDualWmiPending();
      }
      var result = SendOmenBiosWmi(0x29, new byte[] { 0xFF, pl1, 0xFF, 0xFF }, 0);
      return result != null;
    }
    /// <summary>Set PL2 only, PL1 unchanged.</summary>
    public static bool SetCpuPowerLimitPL2Only(byte pl2) {
      if (Services.RaplPowerLimitService.ShouldHandlePowerLimit) {
        if (Services.RaplPowerLimitService.TrySetPowerLimits(null, pl2)) { QueueDualWmiSend(null, pl2); return true; }
        ClearDualWmiPending();
      }
      var result = SendOmenBiosWmi(0x29, new byte[] { pl2, 0xFF, 0xFF, 0xFF }, 0);
      return result != null;
    }

    public static bool SetCpuPowerLimit4(byte value) {
      var result = SendOmenBiosWmi(0x29, new byte[] { 0xFF, 0xFF, value, 0xFF }, 0);
      return result != null;
    }

    public static bool SetConcurrentTdp(byte value) {
      // ponytail: 0xFF 是 EC 的 0x29 "保持原值" 哨兵，所以 TPP 最大值不能用 255。
      // 255(0xFF) → EC 忽略当前字段，双烤 CPU 功耗将受限于 BIOS 默认 TPP(~155W)。
      // 钳位到 254(0xFE)，确保 EC 能识别并应用该值。
      if (value >= 255) value = 254;
      var result = SendOmenBiosWmi(0x29, new byte[] { 0xFF, 0xFF, 0xFF, value }, 0);
      return result != null;
    }

    public static bool IsTwoBytePL4Supported() {
      byte[] data = GetSystemDesignData();
      if (data == null || data.Length < 5) return false;
      return (data[4] & 0x10) != 0;
    }

    public static void SetPL4DoubleByte(ushort pl4Value) {
      byte[] data = new byte[128];
      data[0] = 0x20;
      data[2] = (byte)(pl4Value & 0xFF);
      data[3] = (byte)((pl4Value >> 8) & 0xFF);
      data[6] = 0xFF; data[7] = 0xFF;
      data[10] = 0xFF; data[11] = 0xFF;
      SendOmenBiosWmi(0x37, data, 0);
    }

    // ─── IccMax ───────────────────────────────────────────────────────
    public static void SetIccMaxByWmi(decimal iccMaxAmpere) {
      // R15/BUG-R15-2: EC 有效域是 {0} ∪ [160,255](PerfPage 注释:1-159 是死区,sub-160 触发
      // throttling/hang)。UI 路径已自行钳制,但 HardwareApiService 的 HTTP 门只校验 50-250,
      // 会把 50-159 原样下发。库边界统一钳制,覆盖所有调用方:负值丢弃,1-159 抬到 160,>255 截到 255。
      int amp = (int)iccMaxAmpere;
      if (amp < 0) return;
      if (amp > 0 && amp < 160) amp = 160;
      if (amp > 255) amp = 255;
      byte[] inputData = new byte[128];
      inputData[0] = 0;
      inputData[1] = 15;
      inputData[2] = (byte)(amp & 0xFF);
      inputData[3] = (byte)((amp >> 8) & 0xFF);
      SendOmenBiosWmi(0x37, inputData, 0);
    }

    // ─── AC Load Line ─────────────────────────────────────────────────
    public static bool IsLoadLineSupported() {
      byte[] data = GetSystemDesignData();
      if (data == null || data.Length < 10) return false;
      int levels = data[9] & 0x0F;
      int defaultLL = (data[9] >> 4) & 0x0F;
      return levels > 0 && defaultLL > 0;
    }

    public static int GetLoadLineSupportLevels() {
      byte[] data = GetSystemDesignData();
      if (data == null || data.Length < 10) return 0;
      return data[9] & 0x0F;
    }

    public static void SetLoadLine(int level) {
      // R15/BUG-R15-2: level 是档位索引(UI 只发 0-15,上限 GetLoadLineSupportLevels()=data[9]&0x0F)。
      // HardwareApiService 的 HTTP 门却按 80-130 校验(疑似把"80-130 mΩ"当档位),会把 80-130
      // 这种远超机型档数的值写进 EC。库边界按支持档数拦截:0 视为"不设置"直接透传(与 UI 一致),
      // 越界档位丢弃。
      if (level < 0) return;
      if (level > 0) {
        int max = GetLoadLineSupportLevels();
        if (max > 0 && level > max) return;
      }
      byte[] inputData = new byte[128];
      inputData[0] = 0;
      inputData[1] = 13;
      inputData[2] = (byte)level;
      SendOmenBiosWmi(0x37, inputData, 0);
    }

    public static int GetLoadLine() {
      byte[] inputData = new byte[4];
      inputData[0] = 0; inputData[1] = 13;
      byte[] result = SendOmenBiosWmi(0x37, inputData, 4);
      return (result != null && result.Length > 2) ? result[2] : -1;
    }

    // ─── GPU ──────────────────────────────────────────────────────────
    public static void SetGpuPowerState(bool enableTgp, bool enablePpab, int dState = 1, int gps = 0) {
      byte[] data = new byte[4] {
        Convert.ToByte(enableTgp), Convert.ToByte(enablePpab),
        Convert.ToByte(dState), Convert.ToByte(gps)
      };
      SendOmenBiosWmi(0x22, data, 0, 0x20008);
    }

    // ponytail: 回读 GPU 功耗策略当前 EC 状态,对照 OmenCtl hp-wmi.c:2126-2141。
    // 与现有 SetGpuPowerState (0x22) 对称,启动时同步 UI/ConfigService 用。
    // 返回结构:{ctgp_enable, ppab_enable, dstate, gpu_slowdown_temp}。
    // 失败返回 null(非 HP 机型或不支持)。
    public static (bool tgp, bool ppab, int dState, int gpuSlowdownTemp)? GetGpuPowerState() {
      byte[] result = SendOmenBiosWmi(0x21, new byte[] { 0, 0, 0, 0 }, 4);
      if (result == null || result.Length < 4) return null;
      return (
        (result[0] & 0x01) != 0,
        (result[1] & 0x01) != 0,
        result[2],
        result[3]
      );
    }

    public static void SetMaxGpuPower() { SetGpuPowerState(true, true, 1); }
    public static void SetMedGpuPower() { SetGpuPowerState(true, false, 1); }
    public static void SetMinGpuPower() { SetGpuPowerState(false, false, 1); }

    // ─── Graphics Mode ────────────────────────────────────────────────
    public static void GetGfxMode(out int mode) {
      byte[] result = SendOmenBiosWmi(82, new byte[4] { 0, 0, 0, 0 }, 4, 1);
      mode = (result != null && result.Length > 0) ? (result[0] & 0x7F) : -1;
    }

    public static bool SetGfxMode(int mode, bool dynamicSwitch = false) {
      byte modeByte = (byte)mode;
      if (dynamicSwitch) modeByte |= 0x80;
      byte[] result = SendOmenBiosWmi(82, new byte[4] { modeByte, 0, 0, 0 }, 0, 2);
      return result != null;
    }

    public static byte GetSupportedGfxModes() {
      byte[] designData = GetSystemDesignData();
      if (designData != null && designData.Length > 7 && designData[7] != 0)
        return designData[7];
      byte[] result = SendOmenBiosWmi(82, null, 4, 1);
      if (result != null && result.Length > 0) {
        int code = result[0];
        if (code != 3 && code != 4) return 6;
      }
      return 0;
    }

    // ─── Sensor ───────────────────────────────────────────────────────
    public static int GetSensorTemperature(byte sensorIndex) {
      byte[] result = SendOmenBiosWmi(0x23, new byte[4] { sensorIndex, 0, 0, 0 }, 4);
      return (result != null && result.Length > 0) ? result[0] : -1;
    }

    /// <summary>
    /// Estimate CPU temperature from ambient sensor when direct data is unavailable.
    /// </summary>
    public static float GetFittingTemperature() {
      float temp = GetSensorTemperature(1);
      if (temp < 25) return temp;
      return temp * 1.2f - 5;
    }

    // ─── PawnIO Driver Status ─────────────────────────────────────────
    /// <summary>从 Win 卸载注册表读版本号（跟"应用和功能"列表同一个来源）</summary>
    static string PawnIORegVersion => System.Convert.ToString(
      Microsoft.Win32.Registry.GetValue(
        @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO",
        "DisplayVersion", ""), System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>PawnIO 是否已安装（注册表有记录算已安装，不管服务跑没跑）</summary>
    public static bool IsPawnIOInstalled() => !string.IsNullOrEmpty(PawnIORegVersion);

    /// <summary>返回真实版本号 + 服务状态，如 "v1.3.0 (RUNNING)" 或 "驱动未加载"</summary>
    public static string GetPawnIOState() {
      string ver = PawnIORegVersion;
      if (string.IsNullOrEmpty(ver)) return "驱动未安装";

      var result = GpuAppManager.ExecuteCommand("sc query PawnIO");
      if (result.ExitCode != 0) return "v" + ver + " (驱动未加载)";

      var lines = result.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
      foreach (var line in lines) {
        if (line.IndexOf("STATE", StringComparison.OrdinalIgnoreCase) >= 0) {
          var parts = line.Split(new[] { ':' }, 2);
          if (parts.Length == 2) {
            var statePart = parts[1].Trim();
            var stateWords = statePart.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (stateWords.Length > 0)
              return "v" + ver + " (" + stateWords[stateWords.Length - 1] + ")";
          }
        }
      }
      return "v" + ver + " (Unknown)";
    }

    // ─── PawnIO 驱动安装 ────────────────────────────────────────────────
    /// <summary>
    /// 自带安装：从嵌入资源提取 PawnIO 安装程序到临时目录，以 -install 静默安装。
    /// 应用已 requireAdministrator 提权，安装进程继承管理员权限，无需二次 RunAs。
    /// 已安装则直接返回成功；安装后等待注册表落盘并回读确认结果，临时文件始终清理。
    /// </summary>
    public static bool InstallPawnIO() {
      if (IsPawnIOInstalled()) return true;
      // ponytail: 加固(非完整 TOCTOU 闭环) —— ①随机文件名,消除"固定名预置/残留劫持";
      // ②写后回读 SHA256 与资源流比对,捕获落盘被替换;③ACL 去继承+当前用户 FullControl
      // (注意:SetAccessRuleProtection 只断继承,不清显式规则;FullControl 也挡不住同用户
      // 进程替换 —— 对本机威胁模型,攻击者本就有同用户代码执行能力,这一层是纵深防御)。
      // ACL 设置失败继续执行是有意的降级路径。已知残余:回读校验与 CreateProcess 打开
      // 文件之间仍有毫秒级窗口,根治需要只有管理员/SYSTEM 可写的目录承载载荷。
      string tempExe = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
          $"OmenXHub_PawnIO_setup_{Guid.NewGuid():N}.exe");
      try {
        byte[] payload;
        using (var rs = Assembly.GetExecutingAssembly().GetManifestResourceStream("OmenSuperHub.Resources.PawnIO_setup.exe")) {
          if (rs == null) { Logger.Error("[PawnIO] 内嵌安装资源缺失，无法安装"); return false; }
          using (var ms = new System.IO.MemoryStream()) { rs.CopyTo(ms); payload = ms.ToArray(); }
        }
        string expectedHash;
        using (var sha = System.Security.Cryptography.SHA256.Create())
          expectedHash = BitConverter.ToString(sha.ComputeHash(payload));

        System.IO.File.WriteAllBytes(tempExe, payload);
        try {
          var sec = new System.IO.FileInfo(tempExe).GetAccessControl();
          sec.SetAccessRuleProtection(true, false);   // 断继承:不留 Everyone/其他账户写位
          sec.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
              System.Security.Principal.WindowsIdentity.GetCurrent().User,
              System.Security.AccessControl.FileSystemRights.FullControl,
              System.Security.AccessControl.AccessControlType.Allow));
          System.IO.File.SetAccessControl(tempExe, sec);
        } catch (Exception aclEx) {
          Logger.Warn($"[PawnIO] 临时文件 ACL 加固失败(继续安装): {aclEx.Message}");
        }

        string actualHash;
        using (var sha = System.Security.Cryptography.SHA256.Create())
          actualHash = BitConverter.ToString(sha.ComputeHash(System.IO.File.ReadAllBytes(tempExe)));
        if (actualHash != expectedHash) {
          Logger.Error("[PawnIO] 临时安装器回读哈希与内嵌资源不一致，拒绝执行");
          return false;
        }

        // ponytail: -install 静默安装；UseShellExecute=false 直接执行以继承本进程管理员权限。
        using (var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(tempExe, "-install") { UseShellExecute = false })) {
          p?.WaitForExit();
        }
        // 给驱动注册表写入留出落盘窗口（同安装器自身的异步收尾），再回读确认。
        System.Threading.Thread.Sleep(2000);
        return IsPawnIOInstalled();
      } catch (Exception ex) {
        Logger.Error("[PawnIO] 安装失败: " + ex.Message);
        return false;
      } finally {
        try { if (System.IO.File.Exists(tempExe)) System.IO.File.Delete(tempExe); } catch { }
      }
    }

    // ─── Keyboard Backlight (Basic WMI) ───────────────────────────────
    public static void BacklightOn() { SendOmenBiosWmi(0x05, new byte[] { 0xE4 }, 0, 0x20009); }
    public static void BacklightOff() { SendOmenBiosWmi(0x05, new byte[] { 0x64 }, 0, 0x20009); }

    public static void SetLightColor(byte[] inputData) {
      SendOmenBiosWmi(0x03, inputData, 4, 0x20009);
    }

    public static byte[] GetLightColor() {
      return SendOmenBiosWmi(0x02, new byte[1] { 0 }, 128, 0x20009);
    }

    public static bool SetBrightness(byte value) {
      byte[] inputData = new byte[128];
      inputData[0] = value;
      byte[] result = SendOmenBiosWmi(0x05, inputData, 4, 0x20009);
      return result != null;
    }

    public static int? GetLedAnimation() {
      byte[] result = SendOmenBiosWmi(0x06, new byte[1] { 0 }, 128, 0x20009);
      if (result != null && result.Length > 0) return result[0];
      return null;
    }

    public static bool SetLedAnimation(byte[] inputData) {
      byte[] result = SendOmenBiosWmi(0x07, inputData, 4, 0x20009);
      return result != null;
    }

    // ─── Win Lock (Gaming Key, 0x2000B 通道) ──────────────────────────
    // ponytail: HP 专属 EC 硬件锁,对照 OmenCtl hp-rgb-lighting.c:285-322。
    // commandType=0 + command=0x2000B。data[0]=0x01 锁 / 0x00 解锁,读时返回 bit0。
    // 非 HP 机型或不支持时 SendOmenBiosWmi 返回 null,调用方回退软件钩子。
    public static bool? GetWinLock() {
      byte[] result = SendOmenBiosWmi(0, new byte[] { 0 }, 4, 0x2000B);
      if (result == null || result.Length == 0) return null;
      return (result[0] & 0x01) != 0;
    }

    public static bool SetWinLock(bool enabled) {
      // outputSize=0 成功时返回 Array.Empty<byte>(),失败返回 null
      byte[] result = SendOmenBiosWmi(0, new byte[] { (byte)(enabled ? 0x01 : 0x00) }, 0, 0x2000B);
      return result != null;
    }

    // ─── Omen Key ─────────────────────────────────────────────────────
    public static void OmenKeyOff() {
      const string namespaceName = @"root\subscription";
      var scope = new ManagementScope(namespaceName);
      try {
        scope.Connect();
        foreach (ManagementObject mo in new ManagementObjectSearcher(scope,
          new ObjectQuery("SELECT * FROM __EventFilter WHERE Name='OmenKeyFilter'")).Get())
          using (mo) mo.Delete();
        foreach (ManagementObject mo in new ManagementObjectSearcher(scope,
          new ObjectQuery("SELECT * FROM CommandLineEventConsumer WHERE Name='OmenKeyConsumer'")).Get())
          using (mo) mo.Delete();
        foreach (ManagementObject mo in new ManagementObjectSearcher(scope,
          new ObjectQuery("SELECT * FROM __FilterToConsumerBinding WHERE Filter='__EventFilter.Name=\"OmenKeyFilter\"'")).Get())
          using (mo) mo.Delete();
      } catch (Exception ex) {
        Logger.Error("OmenKeyOff Error: " + ex.Message);
      }
      Logger.Info("OmenKeyOff: WMI subscription removed");
    }

    public static void OmenKeyOn(string method) {
      const string namespaceName = @"root\subscription";
      var scope = new ManagementScope(namespaceName);
      try {
        scope.Connect();
        var consumerClass = new ManagementClass(scope, new ManagementPath("CommandLineEventConsumer"), null);
        var consumer = consumerClass.CreateInstance();
        consumer["CommandLineTemplate"] = @"cmd /c echo OmenKeyTriggered > \\.\pipe\OmenXHubPipe";
        consumer["Name"] = "OmenKeyConsumer";
        consumer.Put();

        var filterClass = new ManagementClass(scope, new ManagementPath("__EventFilter"), null);
        var filter = filterClass.CreateInstance();
        filter["EventNameSpace"] = @"root\wmi";
        filter["Name"] = "OmenKeyFilter";
        filter["Query"] = "SELECT * FROM hpqBEvnt WHERE eventData = 8613 AND eventId = 29";
        filter["QueryLanguage"] = "WQL";
        filter.Put();

        var bindingClass = new ManagementClass(scope, new ManagementPath("__FilterToConsumerBinding"), null);
        var binding = bindingClass.CreateInstance();
        binding["Consumer"] = new ManagementPath(@"root\subscription:CommandLineEventConsumer.Name='OmenKeyConsumer'");
        binding["Filter"] = new ManagementPath(@"root\subscription:__EventFilter.Name='OmenKeyFilter'");
        binding.Put();

        Logger.Info("OmenKeyOn: WMI subscription created for mode=" + method);
      } catch (Exception ex) {
        Logger.Error("OmenKeyOn Error: " + ex.Message);
      }
    }

    // ─── NVIDIA Hot Switch (DDS) ───────────────────────────────────
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LoadLibrary(string lpFileName);

    [DllImport("wintrust.dll", SetLastError = true)]
    private static extern uint WinVerifyTrust(IntPtr hwnd, ref Guid pgActionID, IntPtr pWinTrustData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvidiaAPI_SYS_UIControl_Delegate(bool on);

    private struct WINTRUST_FILE_INFO {
      public uint cbStruct;
      public IntPtr pcwszFilePath;
      public IntPtr hFile;
      public IntPtr pgKnownSubject;
    }

    private struct WINTRUST_DATA {
      public uint cbStruct;
      public IntPtr pPolicyCallbackData;
      public IntPtr pSIPClientData;
      public uint dwUIChoice;
      public uint fdwRevocationChecks;
      public uint dwUnionChoice;
      public IntPtr pFile;
      public uint dwStateAction;
      public IntPtr hWVTStateData;
      public IntPtr pwszURLReference;
      public uint dwProvFlags;
      public uint dwUIContext;
    }

    static bool VerifyAuthenticodeSignature(string filePath) {
      try {
        var actionGuid = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
        var fileInfo = new WINTRUST_FILE_INFO {
          cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>(),
          pcwszFilePath = Marshal.StringToCoTaskMemAuto(filePath),
          hFile = IntPtr.Zero,
          pgKnownSubject = IntPtr.Zero
        };
        var data = new WINTRUST_DATA {
          cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>(),
          pPolicyCallbackData = IntPtr.Zero,
          pSIPClientData = IntPtr.Zero,
          dwUIChoice = 2,
          fdwRevocationChecks = 0,
          dwUnionChoice = 1,
          pFile = Marshal.AllocCoTaskMem(Marshal.SizeOf<WINTRUST_FILE_INFO>()),
          dwStateAction = 0,
          hWVTStateData = IntPtr.Zero,
          pwszURLReference = IntPtr.Zero,
          dwProvFlags = 0x00000010,
          dwUIContext = 0
        };
        Marshal.StructureToPtr(fileInfo, data.pFile, false);
        IntPtr pData = Marshal.AllocCoTaskMem(Marshal.SizeOf<WINTRUST_DATA>());
        Marshal.StructureToPtr(data, pData, false);
        uint result = WinVerifyTrust(IntPtr.Zero, ref actionGuid, pData);
        Marshal.FreeCoTaskMem(fileInfo.pcwszFilePath);
        Marshal.FreeCoTaskMem(data.pFile);
        Marshal.FreeCoTaskMem(pData);
        return result == 0;
      } catch (Exception ex) {
        Logger.Verbose($"[VerifyFileSignature] {ex.Message}");
        return false;
      }
    }

    public static void ExtractAndPreloadNativeDll(string dllName) {
      var currentAssembly = Assembly.GetExecutingAssembly();
      var resourceName = currentAssembly
          .GetManifestResourceNames()
          .FirstOrDefault(r => r.EndsWith(dllName, StringComparison.OrdinalIgnoreCase));
      if (resourceName == null) {
        Logger.Error($"资源中找不到 {dllName}");
        return;
      }
      string outputPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, dllName);
      if (!System.IO.File.Exists(outputPath)) {
        using (var stream = currentAssembly.GetManifestResourceStream(resourceName))
        using (var fs = new System.IO.FileStream(outputPath, System.IO.FileMode.Create, System.IO.FileAccess.Write)) {
          stream.CopyTo(fs);
        }
      }
      if (!VerifyAuthenticodeSignature(outputPath)) {
        Logger.Error($"DLL signature verification failed: {dllName}");
        if (System.IO.File.Exists(outputPath)) System.IO.File.Delete(outputPath);
        return;
      }
      IntPtr handle = LoadLibrary(outputPath);
      if (handle == IntPtr.Zero) {
        Logger.Error($"LoadLibrary 失败，错误码: {Marshal.GetLastWin32Error()}");
      }
    }

    public static int LaunchDDS() {
      IntPtr hModule = GetModuleHandle("NvidiaApi.dll");
      if (hModule == IntPtr.Zero) return -1;
      IntPtr proc = GetProcAddress(hModule, "NvidiaAPI_SYS_UIControl");
      if (proc == IntPtr.Zero) return -1;
      var fn = (NvidiaAPI_SYS_UIControl_Delegate)Marshal.GetDelegateForFunctionPointer(proc, typeof(NvidiaAPI_SYS_UIControl_Delegate));
      return fn(true);
    }

    // ─── GPU Detection ──────────────────────────────────────────────
    public static bool HasNvidiaGpu() {
      if (_cachedHasNvidia.HasValue) return _cachedHasNvidia.Value;
      using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController WHERE Name LIKE '%NVIDIA%'")) {
        foreach (ManagementObject obj in searcher.Get()) using (obj) { _cachedHasNvidia = true; return true; }
      }
      _cachedHasNvidia = false;
      return false;
    }

    // ─── BIOS / CPU / System Info ──────────────────────────────────
    public static string GetBiosVersion() {
      if (_cachedBiosVersion != null) return _cachedBiosVersion;
      try {
        using (var searcher = new ManagementObjectSearcher("SELECT SMBIOSBIOSVersion FROM Win32_BIOS"))
        using (var collection = searcher.Get())
          foreach (ManagementObject obj in collection) using (obj) {
            _cachedBiosVersion = obj["SMBIOSBIOSVersion"]?.ToString() ?? "未知";
            return _cachedBiosVersion;
          }
      } catch (Exception ex) { Logger.Verbose($"[GetBiosVersion] {ex.Message}"); }
      _cachedBiosVersion = "未知";
      return _cachedBiosVersion;
    }

    public static string GetCpuModel() {
      if (_cachedCpuModel != null) return _cachedCpuModel;
      try {
        using (var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor"))
        using (var collection = searcher.Get())
          foreach (ManagementObject obj in collection) using (obj) {
            _cachedCpuModel = obj["Name"]?.ToString()?.Trim() ?? "未知";
            return _cachedCpuModel;
          }
      } catch (Exception ex) { Logger.Verbose($"[GetCpuModel] {ex.Message}"); }
      _cachedCpuModel = "未知";
      return _cachedCpuModel;
    }

    public static bool HasIntelCpu() {
      if (_cachedHasIntelCpu.HasValue) return _cachedHasIntelCpu.Value;
      try {
        using (var searcher = new ManagementObjectSearcher(
            "root\\CIMV2", "SELECT Manufacturer, Name FROM Win32_Processor")) {
          foreach (ManagementObject obj in searcher.Get()) using (obj) {
            string manufacturer = obj["Manufacturer"]?.ToString() ?? "";
            string name = obj["Name"]?.ToString() ?? "";
            if (manufacturer.IndexOf("GenuineIntel", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Intel", StringComparison.OrdinalIgnoreCase) >= 0) {
              _cachedHasIntelCpu = true;
              return true;
            }
          }
        }
      } catch (Exception ex) { Logger.Verbose($"[HasIntelCpu] {ex.Message}"); }
      _cachedHasIntelCpu = false;
      return false;
    }

    public static bool HasAmdCpu() {
      if (_cachedHasAmdCpu.HasValue) return _cachedHasAmdCpu.Value;
      try {
        using (var searcher = new ManagementObjectSearcher(
            "root\\CIMV2", "SELECT Manufacturer, Name FROM Win32_Processor")) {
          foreach (ManagementObject obj in searcher.Get()) using (obj) {
            string manufacturer = obj["Manufacturer"]?.ToString() ?? "";
            string name = obj["Name"]?.ToString() ?? "";
            // WMI Manufacturer 对于 AMD CPU 返回 "AuthenticAMD"
            if (manufacturer.IndexOf("authenticamd", StringComparison.OrdinalIgnoreCase) >= 0 ||
                manufacturer.IndexOf("amd", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("AMD", StringComparison.OrdinalIgnoreCase) >= 0) {
              _cachedHasAmdCpu = true;
              return true;
            }
          }
        }
      } catch (Exception ex) { Logger.Verbose($"[HasAmdCpu] {ex.Message}"); }
      _cachedHasAmdCpu = false;
      return false;
    }

    public static bool HasAmdGpu() {
      if (_cachedHasAmdGpu.HasValue) return _cachedHasAmdGpu.Value;
      try {
        using (var searcher = new ManagementObjectSearcher(
            "root\\CIMV2", "SELECT Name FROM Win32_VideoController")) {
          foreach (ManagementObject obj in searcher.Get()) using (obj) {
            string name = obj["Name"]?.ToString() ?? "";
            if (name.IndexOf("AMD", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Radeon", StringComparison.OrdinalIgnoreCase) >= 0) {
              _cachedHasAmdGpu = true;
              return true;
            }
          }
        }
      } catch (Exception ex) { Logger.Verbose($"[HasAmdGpu] {ex.Message}"); }
      _cachedHasAmdGpu = false;
      return false;
    }

    public static bool HasAmdDiscreteGpu() {
      if (_cachedHasAmdDiscrete.HasValue) return _cachedHasAmdDiscrete.Value;
      try {
        using (var searcher = new ManagementObjectSearcher(
            "root\\CIMV2",
            "SELECT Name, AdapterCompatibility, VideoProcessor FROM Win32_VideoController")) {
          foreach (ManagementObject obj in searcher.Get()) using (obj) {
            string name = obj["Name"]?.ToString() ?? "";
            string vendor = obj["AdapterCompatibility"]?.ToString() ?? "";
            string processor = obj["VideoProcessor"]?.ToString() ?? "";
            bool isAmd = vendor.Contains("1002") || name.IndexOf("AMD", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!isAmd) continue;
            bool isIntegrated = name.Contains("Radeon Graphics") && !name.Contains("RX")
                               || name.Contains("AMD Radeon(TM) Graphics");
            if (!isIntegrated) { _cachedHasAmdDiscrete = true; return true; }
            if (!string.IsNullOrEmpty(processor) && !processor.Contains("Renoir") && !processor.Contains("Cezanne") && !processor.Contains("Rembrandt")) { _cachedHasAmdDiscrete = true; return true; }
          }
        }
      } catch (Exception ex) { Logger.Verbose($"[HasAmdDiscreteGpu] {ex.Message}"); }
      _cachedHasAmdDiscrete = false;
      return false;
    }

    // ─── Product Validation (mirrors OSH) ────────────────────────────
    // ponytail: displayName is obtained externally (DashboardPage fetches
    // it via DeviceModel.OmenPlatform with WMI fallback) and passed in,
    // avoiding the stale-cache problem where the property internally
    // queries the SDK before it's ready and caches false forever.
    private static bool? _isGamingProduct;
    public static bool IsGamingProduct(string displayName) {
      // R15/BUG-R15-3: displayName 来自 SDK/WMI,二者都取不到时可为 null —— 旧实现
      // displayName.Contains 直接 NRE,被调用方空 catch 吞掉,整机校验等级恒 0(产品徽标降级)。
      if (string.IsNullOrEmpty(displayName)) return false;
      if (!_isGamingProduct.HasValue) {
        _isGamingProduct = false;

        if (displayName.Contains("OMEN")) {
          _isGamingProduct = true;
        } else {
          if (DeviceModel.FeatureByte.Contains("7K") && DeviceModel.FeatureByte.Contains("fd")) {
            if (displayName.Contains("PAVILION") || displayName.Contains("VICTUS")) {
              _isGamingProduct = true;
            }
          } else if (displayName.Contains("VICTUS")) {
            _isGamingProduct = true;
          }
        }
      }
      return _isGamingProduct.Value;
    }

    // ─── Victus/光影 识别（EnableEcAccess 默认值用）────────────────────
    // 光影系列（Win32 model 含 "Victus"；老光影精灵为 "Pavilion Gaming"）的 WMI 0x29
    // 功率墙路径不可靠（docs/MEMORY_POWER_THERMAL_DIAGNOSTICS.md："WMI 返回成功 ≠ 实际
    // 生效"），该系列默认走 RAPL 直写。只查 WMI model，不依赖 HP SDK 初始化时机
    // （ConfigService.Load 在 SDK 就绪前运行）。结果缓存，进程内只查一次。
    static bool? _isVictusModel;
    public static bool IsVictusModel() {
      if (_isVictusModel.HasValue) return _isVictusModel.Value;
      try {
        using (var s = new ManagementObjectSearcher("SELECT Model FROM Win32_ComputerSystem"))
          foreach (ManagementObject o in s.Get())
            using (o) {
              string m = Convert.ToString(o["Model"]);
              _isVictusModel = !string.IsNullOrEmpty(m)
                && (m.IndexOf("Victus", StringComparison.OrdinalIgnoreCase) >= 0
                    || m.IndexOf("Pavilion Gaming", StringComparison.OrdinalIgnoreCase) >= 0);
              return _isVictusModel.Value;
            }
      } catch (Exception ex) { Logger.Verbose("[IsVictusModel] " + ex.Message); }
      _isVictusModel = false;
      return false;
    }

    /// <summary>纯逻辑自检：双重写入的 0x29 载荷编码（字段序/哨兵位）。不碰 WMI/MSR。</summary>
    public static string SelfCheck() {
      var fails = new List<string>();
      // 0x29 载荷字段序 = [PL2, PL1, PL4, TPP] —— 写反了在 PL1=PL2 的常见设置下不会暴露
      var both = EncodeDualWmiPayload(45, 80);
      if (both[0] != 80 || both[1] != 45 || both[2] != 0xFF || both[3] != 0xFF)
        fails.Add($"payload(45,80)=[{both[0]},{both[1]},{both[2]},{both[3]}] != [80,45,FF,FF]");
      var p1Only = EncodeDualWmiPayload(45, null);
      if (p1Only[0] != 0xFF || p1Only[1] != 45 || p1Only[2] != 0xFF || p1Only[3] != 0xFF)
        fails.Add("PL1Only payload must keep PL2/PL4/TPP sentinels");
      var p2Only = EncodeDualWmiPayload(null, 80);
      if (p2Only[0] != 80 || p2Only[1] != 0xFF || p2Only[2] != 0xFF || p2Only[3] != 0xFF)
        fails.Add("PL2Only payload must keep PL1/PL4/TPP sentinels");
      return fails.Count == 0 ? "OK" : "FAIL OmenHardware: " + string.Join(" | ", fails);
    }

    public static int Validation(string displayName) {
      try {
        if (IsGamingProduct(displayName)) return 2;
        // ponytail: each DeviceModel call wrapped separately. On non-HP
        // hardware individual calls can throw — we don't want one failure
        // to mask the other.
        try { if (DeviceModel.IsOldOmenProduct) return 1; } catch (Exception ex) { Logger.Verbose($"[Validation] IsOldOmenProduct: {ex.Message}"); }
        try { if (DeviceModel.IsHP) return 1; } catch (Exception ex) { Logger.Verbose($"[Validation] IsHP: {ex.Message}"); }
        return 0;
      } catch (Exception ex) { Logger.Verbose($"[Validation] {ex.Message}"); return 0; }
    }

    // ponytail: mirrors OSH InitMaxTemp — reads BIOS-set temperature throttling
    // limit from HP SDK PlatformSettings instead of hardware MSR TjMax.
    public static int GetCpuTempLimit() {
      // ponytail: SDK 路径天花板 — PerformanceControl.dll 按 4.1.1.0 强名请求 System.Threading.Tasks.Extensions，
      // 字节加载程序集不吃 app.config 重定向；而按 Costura 约定嵌入该程序集会让解析器在启动时
      // 递归栈溢出(c00000fd,已实测两种嵌入方式复现)。故 SDK 读取在真机必然失败 → 稳定走回退值 100。
      // 对 Raptor Lake HX(本机)TjMax 恰为 100,显示正确;换机型若不准,先解决上述程序集链。
      try {
        string sku = PerformanceControlHelper.GetPlatformSku(isInit: true);
        string devType = DeviceModel.OmenPlatform.Name.ToString();
        Logger.Info($"[GetCpuTempLimit] sku='{sku}', devType='{devType}'");
        var ps = PerformanceControlHelper.GetPlatformSettings(devType, sku);
        Logger.Info($"[GetCpuTempLimit] platformSettings={(ps == null ? "null" : "not null")}" +
            (ps != null ? $", tempThrottle={ps.temperatureThrottlingPerformance}" : ""));
        // ponytail: 合理范围外(垃圾/未初始化值)按未读到处理,回退默认 100
        if (ps != null && ps.temperatureThrottlingPerformance > 0 && ps.temperatureThrottlingPerformance < 200)
          return ps.temperatureThrottlingPerformance;
      } catch (Exception ex) {
        Logger.Verbose($"[GetCpuTempLimit] SDK read unavailable (known): {ex.Message}");
      }
      return 100;
    }

    // ponytail: IccMax 卡可见性 — 由 HP SDK PlatformSettings.UnleashedModeMaxIccMax 决定，
    // 而非仅按 CPU 厂商。封装在此处避免 PerfPage 直接依赖 HP.Omen 命名空间。
    public static bool IsIccMaxSupported() {
      try {
        string sku = PerformanceControlHelper.GetPlatformSku(isInit: false);
        var ps = PerformanceControlHelper.GetPlatformSettings(
            DeviceModel.OmenPlatform.Name.ToString(), sku);
        return ps != null && ps.UnleashedModeMaxIccMax > 0;
      } catch (Exception ex) { Logger.Verbose($"[IsIccMaxSupported] {ex.Message}"); return false; }
    }

    // ─── Convenience Mode Setters ─────────────────────────────────────
    public static void SetUnleashMode() {
      // ponytail: 0x64(100) 是错误的 EC 性能模式值, PerformanceMode.L7=0x31(49)
      // 才是正确的 "unleash/performance" 模式。错误值导致 EC 不遵守软件风扇控制,
      // 在 AMD 上表现为低温停转。对齐 OSH 参考实现。
      SetFanMode(PerformanceMode.L7);
    }

    public static void SetBalanceMode() {
      SendOmenBiosWmi(0x1A, new byte[] { 0xFF, 0x32 }, 0);
    }
    // (IsPowerControlForDeviceSupported 已删除: 零调用方死代码,且 case Gamora10 内
    //  `deviceType != Gamora10` 恒 false —— IsSupported() 求值纯属浪费的死逻辑。)
  }
}
