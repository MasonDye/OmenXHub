// OmenHardware.cs - HP OMEN 硬件通信抽象层
// WMI BIOS 通信、风扇控制、性能模式、CPU/GPU 功耗、灯效、BIOS 设置、GPU 检测
using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Runtime.InteropServices;
using HP.Omen.Core.Model.Device.Models;
using HP.Omen.Core.Model.Device.Enums;

namespace OmenSuperHub {
  internal class OmenHardware {
    static readonly byte[] Sign = { 0x53, 0x45, 0x43, 0x55 };
    static ManagementScope _wmiScope;
    static readonly object _scopeLock = new object();

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

          using (var localSearcher = new ManagementObjectSearcher(scope, new ObjectQuery($"SELECT * FROM {className}")))
          using (var collection = localSearcher.Get()) {
            ManagementObject biosMethods = null;
            foreach (ManagementObject mo in collection) { biosMethods = mo; break; }
            if (biosMethods == null) {
              Logger.Error($"SendOmenBiosWmi: {className} WMI class not found!");
              return null;
            }

            using (biosMethods)
            using (var inParams = biosMethods.GetMethodParameters(methodName)) {
              inParams["InData"] = biosDataIn;

              using (var result = biosMethods.InvokeMethod(methodName, inParams, null)) {
                using (var outData = result["OutData"] as ManagementBaseObject) {
                  uint returnCode = (uint)outData["rwReturnCode"];

                  if (returnCode == 0) {
                    Logger.Verbose($"SendOmenBiosWmi: SUCCESS CmdType=0x{commandType:X2} ReturnCode=0");
                    if (outputSize != 0)
                      return (byte[])outData["Data"];
                    else
                      return Array.Empty<byte>();
                  } else {
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

    public static bool IsThreeFanSupported() {
      GetFanType(out var types, out _);
      return types.Count > 2 && types[2] != FanType.Unsupported;
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

    public static void SetFanLevel(int fanSpeed1, int fanSpeed2) {
      SendOmenBiosWmi(0x2E, new byte[] { (byte)fanSpeed1, (byte)fanSpeed2 }, 0);
    }

    public static void SetFanLevel(int fanSpeed1, int fanSpeed2, bool fan3 = false, bool fanClean = false) {
      byte[] data = new byte[fan3 ? 3 : 2];
      if (fanClean) {
        GetFanType(out var types, out var capabilities);
        var caps = capabilities.Take(types.Count).ToList();
        data[0] = (byte)(caps[0] ? fanSpeed1 + 128 : fanSpeed1);
        data[1] = (byte)(caps[1] ? fanSpeed2 + 128 : fanSpeed2);
        if (fan3) {
          data[2] = (byte)(caps[2] ? (fanSpeed1 + fanSpeed2) / 2 + 128 : (fanSpeed1 + fanSpeed2) / 2);
        }
      } else {
        data[0] = (byte)fanSpeed1;
        data[1] = (byte)fanSpeed2;
        if (fan3) data[2] = (byte)((fanSpeed1 + fanSpeed2) / 2);
      }
      SendOmenBiosWmi(0x2E, data, 0);
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

    // ─── Diagnostics ──────────────────────────────────────────────────
    public static void PrintSystemDesignData() {
      byte[] data = GetSystemDesignData();
      if (data == null || data.Length < 12) { Logger.Error("[ERROR] SystemDesignData 获取失败或长度不足"); return; }
      Console.WriteLine("========== System Design Data ==========");
      Console.WriteLine($"完整数据: {BitConverter.ToString(data)}");
      int adapterPower = data[0] | (data[1] << 8);
      Console.WriteLine($"[0]-[1] 适配器功率 = {adapterPower} W");
      Console.WriteLine($"[3] ThermalPolicyVersion = {data[3]}");
      byte b4 = data[4];
      Console.WriteLine($"[4] 平台特性 = 0x{b4:X2}  Bit0(SwFanControl)={(b4 & 0x01) != 0} Bit1(TurboMode)={(b4 & 0x02) != 0}");
      Console.WriteLine($"[5] PL4_Default = {data[5]}W");
      Console.WriteLine($"[8] DefaultConcurrentTdp = {data[8]}");
      byte b9 = data[9];
      Console.WriteLine($"[9] LoadLine 支持级别={b9 & 0x0F} 默认级别={(b9 >> 4) & 0x0F}");
      byte b10 = data[10];
      Console.WriteLine($"[10] 传感器: IR={(b10 & 0x01) != 0} Ambient={(b10 & 0x02) != 0} PCH={(b10 & 0x04) != 0} VR={(b10 & 0x08) != 0}");
    }

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
        } catch { _isSupported = false; }
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

    // ─── CPU Power ────────────────────────────────────────────────────
    /// <summary>Set both PL1 and PL2 to the same value (backward compat).</summary>
    public static bool SetCpuPowerLimit(byte value) {
      return SetCpuPowerLimit(value, value);
    }
    /// <summary>Set PL1 and PL2 independently.</summary>
    public static bool SetCpuPowerLimit(byte pl1, byte pl2) {
      SetUnleashMode();
      var result = SendOmenBiosWmi(0x29, new byte[] { pl2, pl1, 0xFF, 0xFF }, 0);
      return result != null;
    }

    /// <summary>Set PL1 only, PL2 unchanged.</summary>
    public static bool SetCpuPowerLimitPL1Only(byte pl1) {
      SetUnleashMode();
      var result = SendOmenBiosWmi(0x29, new byte[] { 0xFF, pl1, 0xFF, 0xFF }, 0);
      return result != null;
    }
    /// <summary>Set PL2 only, PL1 unchanged.</summary>
    public static bool SetCpuPowerLimitPL2Only(byte pl2) {
      SetUnleashMode();
      var result = SendOmenBiosWmi(0x29, new byte[] { pl2, 0xFF, 0xFF, 0xFF }, 0);
      return result != null;
    }

    public static bool SetCpuPowerLimit4(byte value) {
      SetUnleashMode();
      var result = SendOmenBiosWmi(0x29, new byte[] { 0xFF, 0xFF, value, 0xFF }, 0);
      return result != null;
    }

    public static bool SetConcurrentTdp(byte value) {
      SetUnleashMode();
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
      byte[] inputData = new byte[128];
      inputData[0] = 0;
      inputData[1] = 15;
      inputData[2] = (byte)((int)iccMaxAmpere & 0xFF);
      inputData[3] = (byte)(((int)iccMaxAmpere >> 8) & 0xFF);
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
    public static bool IsPawnIOInstalled() {
      var result = GpuAppManager.ExecuteCommand("sc query PawnIO");
      return result.ExitCode == 0;
    }

    public static string GetPawnIOState() {
      var result = GpuAppManager.ExecuteCommand("sc query PawnIO");
      if (result.ExitCode != 0) return "Not found";
      var lines = result.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
      foreach (var line in lines) {
        if (line.IndexOf("STATE", StringComparison.OrdinalIgnoreCase) >= 0) {
          var parts = line.Split(new[] { ':' }, 2);
          if (parts.Length == 2) {
            var statePart = parts[1].Trim();
            var stateWords = statePart.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (stateWords.Length > 0)
              return stateWords[stateWords.Length - 1];
          }
        }
      }
      return "Unknown";
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

    // ─── Omen Key ─────────────────────────────────────────────────────
    public static void OmenKeyOff() {
      const string namespaceName = @"root\subscription";
      var scope = new ManagementScope(namespaceName);
      try {
        scope.Connect();
        foreach (ManagementObject mo in new ManagementObjectSearcher(scope,
          new ObjectQuery("SELECT * FROM __EventFilter WHERE Name='OmenKeyFilter'")).Get())
          mo.Delete();
        foreach (ManagementObject mo in new ManagementObjectSearcher(scope,
          new ObjectQuery("SELECT * FROM CommandLineEventConsumer WHERE Name='OmenKeyConsumer'")).Get())
          mo.Delete();
        foreach (ManagementObject mo in new ManagementObjectSearcher(scope,
          new ObjectQuery("SELECT * FROM __FilterToConsumerBinding WHERE Filter='__EventFilter.Name=\"OmenKeyFilter\"'")).Get())
          mo.Delete();
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
      } catch {
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
        foreach (var obj in searcher.Get()) { _cachedHasNvidia = true; return true; }
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
          foreach (ManagementObject obj in collection) {
            _cachedBiosVersion = obj["SMBIOSBIOSVersion"]?.ToString() ?? "未知";
            return _cachedBiosVersion;
          }
      } catch { }
      _cachedBiosVersion = "未知";
      return _cachedBiosVersion;
    }

    public static string GetCpuModel() {
      if (_cachedCpuModel != null) return _cachedCpuModel;
      try {
        using (var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor"))
        using (var collection = searcher.Get())
          foreach (ManagementObject obj in collection) {
            _cachedCpuModel = obj["Name"]?.ToString()?.Trim() ?? "未知";
            return _cachedCpuModel;
          }
      } catch { }
      _cachedCpuModel = "未知";
      return _cachedCpuModel;
    }

    public static bool HasIntelCpu() {
      if (_cachedHasIntelCpu.HasValue) return _cachedHasIntelCpu.Value;
      try {
        using (var searcher = new ManagementObjectSearcher(
            "root\\CIMV2", "SELECT Manufacturer, Name FROM Win32_Processor")) {
          foreach (var obj in searcher.Get()) {
            string manufacturer = obj["Manufacturer"]?.ToString() ?? "";
            string name = obj["Name"]?.ToString() ?? "";
            if (manufacturer.IndexOf("GenuineIntel", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Intel", StringComparison.OrdinalIgnoreCase) >= 0) {
              _cachedHasIntelCpu = true;
              return true;
            }
          }
        }
      } catch { }
      _cachedHasIntelCpu = false;
      return false;
    }

    public static bool HasAmdCpu() {
      if (_cachedHasAmdCpu.HasValue) return _cachedHasAmdCpu.Value;
      try {
        using (var searcher = new ManagementObjectSearcher(
            "root\\CIMV2", "SELECT Manufacturer, Name FROM Win32_Processor")) {
          foreach (var obj in searcher.Get()) {
            string manufacturer = obj["Manufacturer"]?.ToString() ?? "";
            string name = obj["Name"]?.ToString() ?? "";
            if (manufacturer.IndexOf("Advanced Micro Devices", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("AMD", StringComparison.OrdinalIgnoreCase) >= 0) {
              _cachedHasAmdCpu = true;
              return true;
            }
          }
        }
      } catch { }
      _cachedHasAmdCpu = false;
      return false;
    }

    public static bool HasAmdGpu() {
      if (_cachedHasAmdGpu.HasValue) return _cachedHasAmdGpu.Value;
      try {
        using (var searcher = new ManagementObjectSearcher(
            "root\\CIMV2", "SELECT Name FROM Win32_VideoController")) {
          foreach (var obj in searcher.Get()) {
            string name = obj["Name"]?.ToString() ?? "";
            if (name.IndexOf("AMD", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Radeon", StringComparison.OrdinalIgnoreCase) >= 0) {
              _cachedHasAmdGpu = true;
              return true;
            }
          }
        }
      } catch { }
      _cachedHasAmdGpu = false;
      return false;
    }

    public static bool HasAmdDiscreteGpu() {
      if (_cachedHasAmdDiscrete.HasValue) return _cachedHasAmdDiscrete.Value;
      try {
        using (var searcher = new ManagementObjectSearcher(
            "root\\CIMV2",
            "SELECT Name, AdapterCompatibility, VideoProcessor FROM Win32_VideoController")) {
          foreach (var obj in searcher.Get()) {
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
      } catch { }
      _cachedHasAmdDiscrete = false;
      return false;
    }

    // ─── Product Validation (mirrors master) ─────────────────────────
    private static bool? _isGamingProduct;
    public static bool IsGamingProduct {
      get {
        if (!_isGamingProduct.HasValue) {
          _isGamingProduct = false;
          try {
            // ponytail: DeviceModel.OmenPlatform can throw when the SDK
            // doesn't recognise this platform.  Gracefully fall back to
            // WMI Win32_ComputerSystem.Model so we still get a model name
            // for partial support UX (VICTUS / PAVILION fans, etc.).
            string displayName = null;
            try { displayName = DeviceModel.OmenPlatform.DisplayName; } catch { }
            if (string.IsNullOrEmpty(displayName)) {
              try {
                using (var searcher = new ManagementObjectSearcher(
                  "SELECT Model FROM Win32_ComputerSystem"))
                using (var col = searcher.Get()) {
                  foreach (ManagementBaseObject obj in col) {
                    displayName = obj["Model"]?.ToString() ?? "";
                    break;
                  }
                }
              } catch { }
            }
            if (string.IsNullOrEmpty(displayName)) return false;

            if (displayName.Contains("OMEN")) {
              _isGamingProduct = true;
            } else {
              string fb = null;
              try {
                var fbytes = DeviceModel.FeatureByte;
                if (fbytes != null) fb = string.Join("", fbytes);
              } catch { }
              if (fb != null && fb.Contains("7K") && fb.Contains("fd")) {
                if (displayName.Contains("PAVILION") || displayName.Contains("VICTUS"))
                  _isGamingProduct = true;
              } else if (displayName.Contains("VICTUS")) {
                _isGamingProduct = true;
              }
            }
          } catch { }
        }
        return _isGamingProduct.Value;
      }
    }

    public static int Validation() {
      try {
        if (IsGamingProduct) return 2;
        // ponytail: each DeviceModel call wrapped separately. On non-HP
        // hardware individual calls can throw — we don't want one failure
        // to mask the other.
        try { if (DeviceModel.IsOldOmenProduct) return 1; } catch { }
        try { if (DeviceModel.IsHP) return 1; } catch { }
        return 0;
      } catch { return 0; }
    }

    // ─── BIOS Settings ────────────────────────────────────────────────
    const string BiosNs = @"root\HP\InstrumentedBIOS";
    static ManagementScope _biosScope;

    static ManagementScope GetBiosScope() {
      if (_biosScope == null) {
        _biosScope = new ManagementScope($@"\\.\{BiosNs}");
        _biosScope.Options.Impersonation = ImpersonationLevel.Impersonate;
        _biosScope.Connect();
      }
      return _biosScope;
    }

    public static bool SetHpBiosSetting(string name, string value) {
      return CallSetBiosSetting(name, value);
    }

    static bool CallSetBiosSetting(string settingName, string settingValue) {
      // Probe the actual WMI class to discover InParameter property names
      try {
        var scope = GetBiosScope();
        using (var cls = new ManagementClass(scope, new ManagementPath { Server = ".", NamespacePath = BiosNs, ClassName = "HPBIOS_BIOSSettingInterface" }, null)) {
          var inParams = cls.GetMethodParameters("SetBIOSSetting");
          var names = new List<string>();
          foreach (PropertyData pd in inParams.Properties) {
            names.Add(pd.Name);
            Logger.Verbose($"SetBIOSSetting param: {pd.Name} ({pd.Type})");
          }
          // Build a name-to-value map based on heuristics
          var args = new Dictionary<string, string>();
          foreach (string pn in names) {
            string lower = pn.ToLowerInvariant();
            if (lower.Contains("name") && !lower.Contains("password"))
              args[pn] = settingName;
            else if (lower.Contains("value"))
              args[pn] = settingValue;
            else if (lower.Contains("password") || lower.Contains("pass") || lower.Contains("auth"))
              args[pn] = "";
            else
              args[pn] = ""; // unknown param — set empty
          }
          if (args.Count >= 2) {
            foreach (var kv in args)
              inParams[kv.Key] = kv.Value;
            Logger.Verbose($"SetBIOSSetting args: [{string.Join(", ", args.Select(kv => $"{kv.Key}={kv.Value}"))}]");
            var result = cls.InvokeMethod("SetBIOSSetting", inParams, new InvokeMethodOptions());
            var ret = result?["ReturnValue"];
            if (ret != null && ret.ToString() == "0") {
              Logger.Verbose($"SetBIOSSetting('{settingName}') OK");
              return true;
            }
            if (ret != null) {
              Logger.Warn($"SetBIOSSetting('{settingName}') ReturnValue={ret}");
              return false;
            }
          }
        }
      } catch (ManagementException ex) when ((int)ex.ErrorCode == (int)ManagementStatus.InvalidMethodParameters) {
        Logger.Verbose("SetBIOSSetting: invalid params even after heuristics");
      } catch (Exception ex) {
        Logger.Verbose($"SetBIOSSetting probe: {ex.Message}");
      }

      // Fallback: try with different Password strategies
      foreach (bool setPassword in new[] { true, false }) {
        try {
          var scope = GetBiosScope();
          using (var cls = new ManagementClass(scope, new ManagementPath { Server = ".", NamespacePath = BiosNs, ClassName = "HPBIOS_BIOSSettingInterface" }, null)) {
            var inParams = cls.GetMethodParameters("SetBIOSSetting");
            foreach (PropertyData pd in inParams.Properties) {
              string lower = pd.Name.ToLowerInvariant();
              try {
                if (lower.Contains("name") && !lower.Contains("password")) inParams[pd.Name] = settingName;
                else if (lower.Contains("value")) inParams[pd.Name] = settingValue;
                else if (setPassword && lower.Contains("password")) inParams[pd.Name] = "";
              } catch (Exception ex) { Logger.Verbose($"SetBIOSSetting param assign: {ex.Message}"); }
            }
            var result = cls.InvokeMethod("SetBIOSSetting", inParams, new InvokeMethodOptions());
            var ret = result?["ReturnValue"];
            if (ret != null && ret.ToString() == "0") { Logger.Verbose($"SetBIOSSetting OK (fallback, pw={setPassword})"); return true; }
            if (ret != null) { Logger.Warn($"SetBIOSSetting ReturnValue={ret} (pw={setPassword})"); return false; }
          }
        } catch { }
      }
      return false;
    }

    public static bool SetBatteryConservation(bool enable) {
      string val = enable ? "Enabled" : "Disabled";
      if (CallSetBiosSetting("Adaptive Battery Optimizer", val)) return true;
      foreach (var name in new[] { "Battery Health Manager", "Battery Charge Limit",
                                    "Battery Care", "Smart Battery" }) {
        foreach (var v in new[] { enable ? "Enabled" : "Disabled", enable ? "1" : "0",
                                   enable ? "Enable" : "Disable" }) {
          if (CallSetBiosSetting(name, v)) return true;
        }
      }
      Logger.Warn("Battery conservation: all setting names failed");
      return false;
    }

    static string QueryBiosSettingValue(string settingName) {
      if (string.IsNullOrEmpty(settingName)) return null;
      if (!System.Text.RegularExpressions.Regex.IsMatch(settingName, @"^[a-zA-Z0-9 _\-\.]+$")) {
        Logger.Error($"QueryBiosSettingValue: invalid setting name blocked");
        return null;
      }
      try {
        var scope = GetBiosScope();
        foreach (string table in new[] { "HP_BIOSSetting", "HPBIOS_BIOSSetting" }) {
          if (table != "HP_BIOSSetting" && table != "HPBIOS_BIOSSetting") continue;
          try {
            var query = new ObjectQuery($"SELECT * FROM {table} WHERE Name='{settingName.Replace("'", "''")}'");
            using (var searcher = new ManagementObjectSearcher(scope, query))
            using (var results = searcher.Get()) {
              foreach (ManagementObject mo in results) {
                var val = mo["Value"];
                if (val != null) return val.ToString();
              }
            }
          } catch (Exception ex) { Logger.Verbose($"QueryBiosSettingValue table {table}: {ex.Message}"); }
        }
      } catch (Exception ex) { Logger.Verbose($"QueryBiosSettingValue: {ex.Message}"); }
      return null;
    }

    public static bool? GetBatteryConservation() {
      foreach (var name in new[] { "Adaptive Battery Optimizer", "Battery Health Manager", "Battery Charge Limit" }) {
        string val = QueryBiosSettingValue(name);
        if (val != null)
          return val.Equals("Enabled", StringComparison.OrdinalIgnoreCase) || val == "1" || val.Equals("true", StringComparison.OrdinalIgnoreCase);
      }
      return null;
    }

    // ─── Convenience Mode Setters ─────────────────────────────────────
    public static void SetUnleashMode() {
      SendOmenBiosWmi(0x1A, new byte[] { 0xFF, 0x64 }, 0);
    }

    public static void SetBalanceMode() {
      SendOmenBiosWmi(0x1A, new byte[] { 0xFF, 0x32 }, 0);
    }

    public static bool IsPowerControlForDeviceSupported(DeviceEnums.DeviceType deviceType) {
      switch (deviceType) {
        case DeviceEnums.DeviceType.Gamora10:
          return IsSupported() && deviceType != DeviceEnums.DeviceType.Gamora10;
        default:
          return IsSwFanControlSupport();
      }
    }
  }
}
