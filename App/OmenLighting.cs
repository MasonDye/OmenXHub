// OmenLighting.cs - 键盘灯效控制
// 封装 HP McuSDK2 实现逐键 RGB 和四区域灯效，支持 Basic/Dojo/PerKey 协议
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using Hp.Bridge.Client.SDKs.McuSDK2;
using Hp.Bridge.Client.SDKs.McuSDK2.Common.DataStructure;
using Hp.Bridge.Client.SDKs.McuSDK2.Common.Enums;
using Hp.Bridge.Client.SDKs.McuSDK2.General.Enums;
using Hp.Bridge.Client.SDKs.McuSDK2.General.Enums.Lighting;
using Hp.Bridge.Client.SDKs.McuSDK2.Keyboard;
using HP.Omen.Core.Model.Device.Enums;
using HP.Omen.Core.Model.Device.Models;
using HidSharp;
using OmenSuperHub.Services;
using static OmenSuperHub.OmenHardware;

namespace OmenSuperHub {
  internal class OmenLighting {
    public static string GetKeyboardTypeName(NbKeyboardLightingType type) {
      switch (type) {
        case NbKeyboardLightingType.Normal: return Strings.KbTypeNormal;
        case NbKeyboardLightingType.FourZoneWithNumpad: return Strings.KbTypeFourZoneWithNumpad;
        case NbKeyboardLightingType.FourZoneWithoutNumpad: return Strings.KbTypeFourZoneWithoutNumpad;
        case NbKeyboardLightingType.RgbPerKey: return Strings.KbTypeRgbPerKey;
        case NbKeyboardLightingType.OneZoneWithNumpad: return Strings.KbTypeOneZoneWithNumpad;
        case NbKeyboardLightingType.OneZoneWithoutNumpad: return Strings.KbTypeOneZoneWithoutNumpad;
        default: return Strings.KbTypeUnknown;
      }
    }

    public enum LightingDevice {
      Keyboard,
      LightBar
    }

    // ponytail: 键盘能力分类 — MainWindow(隐藏灯光页) 与 LightingPage(自适应布局) 共用。
    // LightBarOnly = 键盘无 RGB 但灯条存在；Conservative(探测失败) 按 FourZone 处理不隐藏。
    public enum KeyboardKind { Normal, OneZone, FourZone, PerKey, LightBarOnly }

    public sealed class KeyboardCapability {
      public KeyboardKind Kind;
      public bool AnimationSupported;
      public bool LightBarSupported;
      /// <summary>false = 探测失败走了保守降级(Kind=FourZone)，UI 不应据此隐藏灯光页</summary>
      public bool Detected;
    }

    static KeyboardCapability _kbCapability;
    static readonly object _kbCapLock = new();

    /// <summary>统一键盘能力探测 — 合并 WMI + HP SDK + 灯条探测，惰性求值缓存一次。
    /// 隐藏灯光页的判定必须用 Detected==true 且 Kind==Normal 才成立；探测异常一律保守 FourZone。
    /// DEBUG: ConfigService.DebugKbKind 非空时强制覆盖 Kind(模拟键盘类型,UI 预览用)。</summary>
    public static KeyboardCapability DetectKeyboardCapability() {
      lock (_kbCapLock) {
        if (_kbCapability != null) return _kbCapability;
        var cap = new KeyboardCapability { Kind = KeyboardKind.FourZone, Detected = false };
        try {
          // WMI 通道 (BIOS 权威值)
          NbKeyboardLightingType wmiType = GetKeyboardType();
          // HP SDK 通道 (Rgb 判定比 WMI 更可靠 — AutoDetectProtocol 优先级同理)
          Omen.OmenFourZoneLighting.KeyboardType sdkType = FourZoneHelper.GetKeyboardType();
          bool lightBar = false;
          try { lightBar = FourZoneHelper.IsLightBarSupported(); } catch (Exception ex) { Logger.Verbose($"[DetectKbCap] IsLightBarSupported: {ex.Message}"); }
          bool anim = false;
          try { anim = IsAnimationSupported(); } catch (Exception ex) { Logger.Verbose($"[DetectKbCap] IsAnimationSupported: {ex.Message}"); }

          // ponytail: WMI 返回 None 表示命令失败而非"确认无灯" — 保守 FourZone。
          // 只有 WMI 明确 Normal 且 HP SDK 也非 Rgb 才能定性 Normal。
          bool wmiFailed = wmiType == NbKeyboardLightingType.None;
          bool keyboardNormal = wmiType == NbKeyboardLightingType.Normal
            && sdkType != Omen.OmenFourZoneLighting.KeyboardType.Rgb;

          if (sdkType == Omen.OmenFourZoneLighting.KeyboardType.Rgb || wmiType == NbKeyboardLightingType.RgbPerKey)
            cap.Kind = KeyboardKind.PerKey;
          else if (keyboardNormal && lightBar)
            cap.Kind = KeyboardKind.LightBarOnly;
          else if (keyboardNormal && !wmiFailed)
            cap.Kind = KeyboardKind.Normal;
          else if (wmiType == NbKeyboardLightingType.OneZoneWithNumpad || wmiType == NbKeyboardLightingType.OneZoneWithoutNumpad
            || sdkType == Omen.OmenFourZoneLighting.KeyboardType.OneZoneWithNumpad || sdkType == Omen.OmenFourZoneLighting.KeyboardType.OneZoneWithoutNumpad)
            cap.Kind = KeyboardKind.OneZone;
          else
            cap.Kind = KeyboardKind.FourZone;

          cap.LightBarSupported = lightBar;
          cap.AnimationSupported = anim;
          cap.Detected = !wmiFailed;
        } catch (Exception ex) {
          // 全不可用 — 保守 FourZone, Detected=false 阻止隐藏灯光页
          Logger.Verbose($"[DetectKbCap] 全探测失败: {ex.Message}");
        }
        ApplyDebugKbKindOverride(cap);
        _kbCapability = cap;
        Logger.Info($"DetectKeyboardCapability: Kind={cap.Kind} Detected={cap.Detected} " +
                    $"Anim={cap.AnimationSupported} LightBar={cap.LightBarSupported}");
        return cap;
      }
    }

    // ponytail: DEBUG 模拟 — 设置页选择模拟键盘类型后覆盖 Kind 及派生能力,
    // 让灯光页布局/侧栏显隐按模拟值走(UI 预览无需真机)。派生值按类型合理合成:
    // Normal=全无 / OneZone=动画有 / FourZone=动画有 / PerKey=动画有+灯带(一体机型,选项已合并)。
    static void ApplyDebugKbKindOverride(KeyboardCapability cap) {
      string sim = ConfigService.DebugKbKind;
      if (string.IsNullOrEmpty(sim) || !Enum.TryParse<KeyboardKind>(sim, out var kind)) return;
      cap.Kind = kind;
      cap.Detected = true; // 模拟值视为"已确认",NavLighting 隐藏逻辑才能如实演练
      cap.AnimationSupported = kind != KeyboardKind.Normal;
      cap.LightBarSupported = kind == KeyboardKind.PerKey;
    }

    /// <summary>清能力缓存 — Debug 模拟类型变更后由设置页调用,下次探测重新求值</summary>
    public static void InvalidateKeyboardCapabilityCache() {
      lock (_kbCapLock) _kbCapability = null;
    }

    /// <summary>灯光页是否应显示在侧栏 — 仅"确认探测成功且为普通键盘无灯条"才隐藏</summary>
    public static bool IsLightingPageSupported() {
      var cap = DetectKeyboardCapability();
      return !(cap.Detected && cap.Kind == KeyboardKind.Normal && !cap.LightBarSupported);
    }

    public enum LightingControlInterface {
      None = 0,
      BasicFourZone,
      Dojo,
      PerKeyRGB
    }

    private enum TargetDevice : byte {
      LightBar = 0,
      FourZoneAni = 1
    }

    private const int WMI_COMMAND_ID = 131081;

    private static readonly Dictionary<LightingDevice, List<System.Windows.Media.Color>> _lastDeviceColors =
        new Dictionary<LightingDevice, List<System.Windows.Media.Color>>();

    public static int OpenHidDevice(int pid, int vid, string interfaceString = "") {
      try {
        // ponytail: .GetAwaiter().GetResult() 抛原始异常而非 AggregateException，
        // 且避免 task.Wait() 在 UI 线程上下文下可能的死锁
        // ponytail: OpenDevice is async without ConfigureAwait(false). Calling
        // .GetAwaiter().GetResult() on the UI thread deadlocks (continuation needs
        // UI context, but UI is blocked). Dispatch to ThreadPool so continuation
        // doesn't need UI thread. Ceiling: still sync-over-async; real fix is to
        // make OpenPerKeyKeyboard async.
        return System.Threading.Tasks.Task.Run(
            () => McuGeneralHelper.OpenDevice(pid, vid, interfaceString, "")
        ).GetAwaiter().GetResult();
      } catch (Exception ex) {
        Logger.Error($"OpenHidDevice Exception: {ex.Message}");
        return -1;
      }
    }

    public static async Task<bool> CloseDeviceAsync(int handle) => await McuGeneralHelper.CloseDevice(handle);

    public static int OpenPerKeyKeyboard() {
      // ponytail: 整函数包 try/catch — DeviceModel.DeviceType 在非匹配机型上走
      // Utf8Json 反序列化 OmenPlatformInfo,可能抛 FileNotFoundException(依赖 DLL 缺失)
      // 或其他平台访问异常。此函数被 EnsurePerKeyHandle→LightEnable_Changed 同步调用,
      // 未捕获会冒泡到 UI 事件处理并崩应用。所有失败路径统一返回 -1(与候选全 miss 一致),
      // 调用方按 -1 走诊断提示,不让平台访问异常穿透到 UI。
      try {
      DeviceEnums.DeviceType deviceType = DeviceModel.DeviceType;
      List<(int pid, int vid, string interfaceString)> candidates = new List<(int, int, string)>();

      switch (deviceType) {
        case DeviceEnums.DeviceType.Modena:
          candidates.Add((0x2238, 0x1FC9, ""));
          break;
        case DeviceEnums.DeviceType.Ralph:
          candidates.Add((0x4E9B, 0x0461, "mi_02"));
          break;
        case DeviceEnums.DeviceType.Cybug:
          candidates.Add((0x4E9A, 0x0461, "mi_02"));
          break;
        case DeviceEnums.DeviceType.Hendricks:
          candidates.Add((0x4F03, 0x0461, "mi_02"));
          break;
        case DeviceEnums.DeviceType.Brunobear:
        case DeviceEnums.DeviceType.Quaker:
          candidates.Add((0x4F11, 0x0461, "mi_02"));
          candidates.Add((0x4F1E, 0x0461, "mi_02"));
          break;
        case DeviceEnums.DeviceType.Voco:
          if (DeviceModel.ThisSystemID == "8E41")
            candidates.Add((0x36BA, 0x0D62, "mi_03"));
          else
            candidates.Add((0x1A32, 0x0D62, "mi_03"));
          break;
        case DeviceEnums.DeviceType.Dojo:
        case DeviceEnums.DeviceType.Vibrance:
          candidates.Add((0x54BF, 0x0D62, "mi_03"));
          candidates.Add((0x30BF, 0x0D62, "mi_03"));
          break;
        default:
          // ponytail: 未知机型 — 不直接 return -1,先尝试用 McuSDK 打开已知 HP per-key PID。
          // OMEN MAX 16 (2025) 等新机型的 DeviceType 可能不在枚举中,但 USB HID 接口已就绪。
          // 参考 OmenCore.HidPerKeyBackend 的 KnownPerKeyPids 表。
          candidates.Add((0x054E, 0x03F0, "")); // OMEN MAX 16 ah0xxx
          candidates.Add((0x054F, 0x03F0, "")); // OMEN MAX 16 ak0xxx
          candidates.Add((0x0547, 0x03F0, "")); // OMEN 16 (2024)
          candidates.Add((0x0549, 0x03F0, "")); // OMEN 17 (2024)
          candidates.Add((0x0538, 0x03F0, "")); // OMEN 16 (2023)
          break;
      }

      foreach (var (pid, vid, interfaceStr) in candidates) {
        int handle = OpenHidDevice(pid, vid, interfaceStr);
        if (handle > 0) {
          Logger.Info($"OpenPerKeyKeyboard: McuSDK opened PID=0x{pid:X4} VID=0x{vid:X4} iface='{interfaceStr}' handle={handle}");
          return handle;
        }
      }

      // ponytail: McuSDK 全部失败 — 记录诊断信息帮助定位问题。
      // 常见原因:OMEN 服务未运行、机型不在候选列表、HID 设备被其他进程(OMEN Light Studio)独占。
      Logger.Warn($"OpenPerKeyKeyboard: McuSDK failed for DeviceType={deviceType} SystemID={DeviceModel.ThisSystemID}");
      var hpHidDevices = ScanHpHidDevices();
      if (hpHidDevices.Count > 0) {
        Logger.Info($"OpenPerKeyKeyboard: detected {hpHidDevices.Count} HP HID device(s): " +
                    string.Join(", ", hpHidDevices.Select(d => $"PID=0x{d.Pid:X4}('{d.Name}')")));
      } else {
        Logger.Info("OpenPerKeyKeyboard: no HP HID devices (VID 0x03F0) found");
      }
      return -1;
      } catch (Exception ex) {
        // ponytail: DeviceModel 平台访问异常(FileNotFoundException/Json 反序列化失败等)。
        // 记录后返回 -1,与候选全 miss 路径一致 — EnsurePerKeyHandle 据此走诊断提示而非崩溃。
        Logger.Error($"OpenPerKeyKeyboard platform access exception: {ex.Message}");
        return -1;
      }
    }

    // ponytail: 扫描 HP USB HID 设备 — 参考 OmenCore.HidPerKeyBackend.InitializeAsync。
    // 用于 PerKey 失败时的诊断:列出所有 VID=0x03F0 的 HP HID 设备及其 PID,
    // 方便用户报告未知 PID 以扩展 KnownPerKeyPids 表。
    // 返回值:已识别的 per-key 键盘设备列表(可能为空)。
    public static List<(int Pid, string Name)> ScanHpHidDevices() {
      var result = new List<(int, string)>();
      try {
        const int HP_VID = 0x03F0;
        var knownPerKeyPids = new Dictionary<int, string> {
          { 0x0538, "OMEN 16 (2023) Intel/AMD keyboard" },
          { 0x053A, "OMEN Sequoia / external gaming keyboard" },
          { 0x0547, "OMEN 16 (2024) keyboard" },
          { 0x0549, "OMEN 17 (2024) keyboard" },
          { 0x054E, "OMEN MAX 16 (2025) ah0xxx keyboard" },
          { 0x054F, "OMEN MAX 16 (2025) ak0xxx keyboard" },
        };

        var hpDevices = DeviceList.Local.GetHidDevices().Where(d => d.VendorID == HP_VID).ToList();
        foreach (var d in hpDevices) {
          int pid = d.ProductID;
          string name = "";
          try { name = d.GetProductName(); } catch { }
          if (knownPerKeyPids.TryGetValue(pid, out var knownName)) {
            result.Add((pid, knownName));
            Logger.Info($"[HidScan] OK PID=0x{pid:X4} - {knownName} ('{name}')");
          } else {
            // 未知 PID — 记录日志,方便用户报告
            Logger.Info($"[HidScan] ? PID=0x{pid:X4} - '{name}' (not in per-key PID list; " +
                        "if this is an OMEN keyboard, report this PID for inclusion)");
          }
        }
      } catch (Exception ex) {
        Logger.Error($"ScanHpHidDevices: {ex.Message}");
      }
      return result;
    }

    // ponytail: PerKey 诊断信息字符串 — 供 UI 显示给用户,告知检测到的 HP HID 设备。
    // 当 McuSDK 打开失败时,LightingPage 调用此方法生成诊断提示。
    public static string GetPerKeyDiagnosticInfo() {
      var hpHidDevices = ScanHpHidDevices();
      if (hpHidDevices.Count == 0)
        return "";

      var sb = new System.Text.StringBuilder();
      sb.Append("检测到 HP HID 设备: ");
      sb.Append(string.Join(", ", hpHidDevices.Select(d => $"PID=0x{d.Pid:X4}({d.Name})")));
      return sb.ToString();
    }

    public static async Task<bool> SetPerKeyStaticColor(int handle, byte[] r, byte[] g, byte[] b) =>
        await McuGeneralHelper.SetKeyboardStaticLighting(handle, r, g, b);

    public static async Task<bool> SetPerKeyAnimation(int handle, LightingSetting setting) =>
        await McuGeneralHelper.SetLightingEffect(handle, setting, LightingEffectTarget.ALL_LED_AREA);

    public static async Task<bool> SetPerKeyAudioAnimation(int handle, LightingAudioEffectSetting setting) =>
        await McuKeyboardHelper.SetLightingAudioEffect(handle, setting);

    public static async Task<bool> SetPerKeyBrightness(int handle, byte level) =>
        await McuGeneralHelper.SetKeyboardBrightness(handle, level);

    public static async Task<bool> SetPerKeyLightingOn(int handle) =>
        await McuGeneralHelper.SetKeyboardLightingOn(handle);

    public static async Task<bool> SetPerKeyLightingOff(int handle) =>
        await McuGeneralHelper.SetKeyboardLightingOff(handle);

    public static async Task<bool> SetPerKeyLedOnOff(int handle, List<byte> allKeyStatus) =>
        await McuGeneralHelper.SetKeyboardIndividualLEDOnOff(handle, allKeyStatus);

    public static async Task<bool> StorePerKeyToFlash(int handle) =>
        await McuGeneralHelper.StoreLightingToFlash(handle, LightingEffectTarget.ALL_LED_AREA);

    public static async Task<bool> RestorePerKeyLightingToDefault(int handle) =>
        await McuGeneralHelper.RestoreLightingToDefault(handle);

    public static async Task<LightingSetting> GetPerKeyCurrentEffect(int handle) {
      var (success, setting) = await McuGeneralHelper.GetLightingEffect(handle, LightingEffectTarget.ALL_LED_AREA);
      return success ? setting : null;
    }

    public static async Task<KeyboardLanguage> GetPerKeyLanguage(int handle) {
      var (success, lang) = await McuGeneralHelper.GetKeyboardLanguage(handle);
      return success ? lang : KeyboardLanguage.LANGUAGE_US_ENGLISH;
    }

    public static async Task<Dictionary<KeyboardStatusType, CommonToggleEnum>> GetPerKeyKeyStatus(int handle) =>
        await McuGeneralHelper.GetKeyboardKeyStatus(handle);

    public static async Task<byte> GetPerKeyBrightness(int handle) {
      try {
        var (success, brightness) = await McuKeyboardHelper.GetAllKeyboardBrightness(handle);
        return success ? brightness : (byte)0;
      } catch (Exception ex) { Logger.Verbose($"[GetPerKeyBrightness] {ex.Message}"); return (byte)0; }
    }

    public static NbKeyboardLightingType GetKeyboardType() {
      byte[] result = SendOmenBiosWmi(43, new byte[0], 4, 0x20008);
      if (result != null && result.Length > 0)
        return (NbKeyboardLightingType)result[0];
      return NbKeyboardLightingType.None;
    }

    // ponytail: 零参包装 — LightingPage 不直接依赖 HP.Omen.Core.Model.Device.Models。
    // 注意 FourZoneSupportHelper.IsAnimationSupported 首次调用返回正确值，二次调用因
    // _isAnimationSupported.HasValue 短路返回 false（参考 WinForms 全局 supportAni 一次性
    // 赋值的行为）；本包装不缓存，调用方需自行缓存。
    public static bool IsAnimationSupported() {
      try {
        return FourZoneSupportHelper.IsAnimationSupported(GetKeyboardType(), DeviceModel.DeviceType);
      } catch (Exception ex) { Logger.Verbose($"[IsAnimationSupported] {ex.Message}"); return false; }
    }

    // ponytail: SystemID→protocol 映射表 — 提取自 OmenCore KeyboardModelDatabase。
    // PreferredMethod 映射: NewWmi2023→"Dojo", ColorTable2020→"BasicFourZone",
    // HidPerKey→"PerKey", BacklightOnly→空(表示无 RGB,只支持背光开关)。
    // 比 cycle>260 启发式更精确: 部分 2024+ 机型(8BCD/8D24/8E35)仍是 ColorTable2020 首选。
    // 未列出的已知 4-zone 机型走 ColorTable2020 → 此表仅记录"非默认"的机型。
    static readonly Dictionary<string, string> _protocolBySystemId = new(StringComparer.OrdinalIgnoreCase) {
      // === Dojo preferred (NewWmi2023) ===
      { "8E67", "Dojo" }, // OMEN 16 (2023) Intel
      { "8E68", "Dojo" }, // OMEN 16 (2023) AMD
      { "8E69", "Dojo" }, // OMEN 17-ck2xxx (2024)
      { "8E6A", "Dojo" }, // OMEN 45L Desktop (2023)

      // === PerKey (also caught by kbType check, explicit for robustness) ===
      { "8E41", "PerKey" }, // OMEN Transcend 14 (2024)
      { "8D41", "PerKey" }, // OMEN MAX 16-ah0xxx (2025)
      { "8D87", "PerKey" }, // OMEN MAX 16-ak0xxx (2025)

      // === BacklightOnly (single-color or on/off, no RGB zones) ===
      { "8574", "" }, // OMEN 15-dc1xxx (2019)
      { "8575", "" }, // OMEN 15 (2018)
      { "8600", "" }, // OMEN 15-dh0xxx (2019)
      { "860C", "" }, // OMEN 17 (2019)
      { "88EC", "" }, // Victus 16-e0xxx
      { "88EE", "" }, // Victus 16-e0194nw
      { "8A23", "" }, // HP Victus 15 (2021)
      { "8A3E", "" }, // HP Victus 15-fb0xxx (2022)
      { "8C30", "" }, // HP Victus 15-fb1xxx (2023)
      { "8BB4", "" }, // HP Victus 16 (2022)
    };

    // ponytail: 自动推荐灯光协议 — 优先查 SystemID 映射表(OmenCore 数据库),
    // 命中不到再用 kbType + cycle 数 + HP SDK 可用性降级。
    // 优先级链: PerKey(RGB键盘) > Dojo(查表/cycle>260) > HpSdk > BasicFourZone。
    // 返回值可能是空字符串 → 表示 BacklightOnly,UI 应做简化处理(当前项目无此模式,降级为 BasicFourZone)。
    public static string AutoDetectProtocol() {
      // 1. Per-key RGB keyboard → PerKey protocol (kbType check catches more models than the table)
      try {
        if (FourZoneHelper.GetKeyboardType() == Omen.OmenFourZoneLighting.KeyboardType.Rgb)
          return "PerKey";
      } catch (Exception ex) { Logger.Verbose($"[AutoDetectProtocol] PerKey probe: {ex.Message}"); }

      // 2. SystemID lookup → exact match from OmenCore database
      try {
        string sysId = DeviceModel.ThisSystemID;
        if (!string.IsNullOrEmpty(sysId) && _protocolBySystemId.TryGetValue(sysId, out string proto)) {
          if (!string.IsNullOrEmpty(proto)) return proto;
          // proto == "" → BacklightOnly, fall through to heuristic
        }
      } catch (Exception ex) { Logger.Verbose($"[AutoDetectProtocol] SystemID lookup: {ex.Message}"); }

      // 3. Animation-capable (cycle > 260) → Dojo (covers unknown 2023+ models not in table)
      try {
        if (IsAnimationSupported())
          return "Dojo";
      } catch (Exception ex) { Logger.Verbose($"[AutoDetectProtocol] Dojo probe: {ex.Message}"); }

      // 4. HP SDK available → safest official path for 4-zone
      if (FourZoneHelper.Available)
        return "HpSdk";

      // 5. Fallback
      return "BasicFourZone";
    }

    public static bool IsLightBarPlatform() {
      byte[] result = SendOmenBiosWmi(1, null, 4);
      if (result != null && result.Length > 0) {
        return ((result[0] >> 1) & 1) == 1;
      }
      return false;
    }

    internal static class FourZoneSupportHelper {
      private static bool? _isSupported;
      private static bool? _isAnimationSupported;

      public static bool IsSupported(NbKeyboardLightingType kbType, DeviceEnums.DeviceType device) {
        if (!_isSupported.HasValue) {
          if (device == DeviceEnums.DeviceType.Pirates11 || (uint)(device - 6) <= 2u || (uint)(device - 14) <= 1u) {
            _isSupported = GetLightingSupported() == 1;
          } else {
            _isSupported = kbType == NbKeyboardLightingType.FourZoneWithNumpad ||
              kbType == NbKeyboardLightingType.FourZoneWithoutNumpad ||
              kbType == NbKeyboardLightingType.OneZoneWithNumpad ||
              kbType == NbKeyboardLightingType.OneZoneWithoutNumpad;
          }
        }
        return _isSupported.Value;
      }

      public static bool IsAnimationSupported(NbKeyboardLightingType kbType, DeviceEnums.DeviceType device) {
        // ponytail: 原实现在第二次调用时 _isAnimationSupported.HasValue 短路直接 return false,
        // 即使首次调用返回 true。修复: 缓存 cycle>260 的判定结果,每次调用都走 IsSupported 取最终值。
        if (!_isAnimationSupported.HasValue) {
          try {
            _isAnimationSupported = DeviceModel.GetCycleNumber(
              DeviceModel.OmenPlatform.ProductNum.FirstOrDefault((SSIDInfo x) => x.SSID.Equals(DeviceModel.ThisSystemID)).Cycle) > 260;
          } catch (Exception ex) { Logger.Verbose($"[FourZoneSupportHelper] cycle 判定失败: {ex.Message}"); _isAnimationSupported = false; }
        }
        return _isAnimationSupported.Value && IsSupported(kbType, device);
      }

      public static int GetLightingSupported() {
        byte[] result = SendOmenBiosWmi(1, null, 128, 0x20009);
        if (result != null && result.Length > 0)
          return result[0] & 0x01;
        return -1;
      }
    }

    public static void SetZoneStaticColor(LightingDevice device, List<System.Windows.Media.Color> colors,
        byte brightness, LightingControlInterface controlInterface) {
      if (colors == null || colors.Count != 4)
        throw new ArgumentException("必须提供 4 个颜色");

      _lastDeviceColors[device] = new List<System.Windows.Media.Color>(colors);
      byte target = device == LightingDevice.LightBar ? (byte)TargetDevice.LightBar : (byte)TargetDevice.FourZoneAni;

      switch (controlInterface) {
        case LightingControlInterface.Dojo: {
            byte[] data = new byte[128];
            data[0] = target;
            data[1] = 0;
            data[3] = brightness;
            data[6] = 4;
            for (int i = 0; i < 4; i++) {
              data[7 + i * 3] = colors[i].R;
              data[8 + i * 3] = colors[i].G;
              data[9 + i * 3] = colors[i].B;
            }
            SendOmenBiosWmi(11, data, 0, WMI_COMMAND_ID);
            break;
          }
        case LightingControlInterface.BasicFourZone: {
            byte[] table = SendOmenBiosWmi(2, new byte[1] { 0 }, 128, WMI_COMMAND_ID);
            if (table == null || table.Length < 37) return;
            for (int i = 0; i < 4; i++) {
              int idx = 25 + i * 3;
              table[idx] = colors[i].R;
              table[idx + 1] = colors[i].G;
              table[idx + 2] = colors[i].B;
            }
            SendOmenBiosWmi(3, table, 0, WMI_COMMAND_ID);
            break;
          }
        default:
          throw new ArgumentOutOfRangeException(nameof(controlInterface));
      }
    }

    // ponytail: 4-zone static / light-bar static / brightness / WMI channel all 1:1 reverse-
    // verified against OMEN Light Studio's OmenFourZoneLighting.dll. The *animation* byte
    // layouts here (Dojo + BasicFourZone) are NOT in Light Studio — Aurora renders frames
    // on CPU and only sends static colors per frame. These bytes came from an older HP
    // gaming-hub era / community reverse; they are kept as-is here and just exposed via
    // SetZoneAnimation. Why supported-effects differ per interface:
    //   Dojo (CmdType=11): byte data[1] is effectId 1..9, all forwarded as-is by BIOS.
    //   BasicFourZone (CmdType=7): BIOS only honors effectId 2 (Starlight) and 4 (Wave),
    //                              swapping them to draxEffect 1/2 internally.
    // See docs/lighting-reverse-findings.md for the full byte tables and evidence trail.
    public static bool SetZoneAnimation(LightingDevice device, byte effectId, byte speed, byte direction,
        byte theme, List<System.Windows.Media.Color> customColors, byte brightness,
        LightingControlInterface controlInterface) {
      byte target = device == LightingDevice.LightBar ? (byte)TargetDevice.LightBar : (byte)TargetDevice.FourZoneAni;

      if (controlInterface == LightingControlInterface.Dojo) {
        byte[] data = new byte[128];
        data[0] = target;
        data[1] = effectId;
        // ponytail: data[2] starts as 0 (fresh array), so the &-masks below are no-ops
        // kept for clarity matching original community bit layout; speed(2) | dir(2) | theme(4)
        data[2] = (byte)(data[2] & 0xFC | (speed & 0x03));
        data[2] = (byte)(data[2] & 0xF3 | (direction == 1 ? 0x08 : 0x04));
        data[2] = (byte)(data[2] & 0x0F);
        switch (theme) {
          case 0: data[2] |= 0x10; break;
          case 1: data[2] |= 0x20; break;
          case 2: data[2] |= 0x30; break;
          case 3: data[2] |= 0x40; break;
          case 4: data[2] |= 0x50; break;
        }
        data[3] = brightness;
        if (theme == 4 && customColors != null) {
          int count = Math.Min(customColors.Count, 4);
          data[6] = (byte)count;
          for (int i = 0; i < count; i++) {
            data[7 + i * 3] = customColors[i].R;
            data[8 + i * 3] = customColors[i].G;
            data[9 + i * 3] = customColors[i].B;
          }
        }
        SendOmenBiosWmi(11, data, 0, WMI_COMMAND_ID);
        return true; // Dojo BIOS reports OK if WMI call returns; effect started on firmware side.
      } else {
        // ponytail: BasicFourZone/HP-SDK-protocol BIOS only honors Starlight(effectId=2 ->drax 2)
        // and Wave(effectId=4 ->drax 1). Every other effect is silently dropped by firmware —
        // report false so UI can warn the user instead of pretending the effect applied.
        if (effectId != 2 && effectId != 4) return false;
        byte draxEffect = (effectId == 2) ? (byte)2 : (byte)1;

        byte interval = speed == 0 ? (byte)10 : (speed == 1 ? (byte)5 : (byte)2);

        List<System.Windows.Media.Color> animColors;
        if (theme == 4 && customColors != null && customColors.Count > 0)
          animColors = customColors;
        else if (_lastDeviceColors.TryGetValue(device, out var last) && last.Count > 0)
          animColors = new List<System.Windows.Media.Color> { last[0] };
        else
          animColors = new List<System.Windows.Media.Color> { System.Windows.Media.Color.FromRgb(255, 255, 255) };

        byte[] data = new byte[5 + animColors.Count * 3];
        data[0] = 0;
        data[1] = draxEffect;
        data[2] = interval;
        data[3] = brightness;
        data[4] = (byte)animColors.Count;
        for (int i = 0; i < animColors.Count; i++) {
          data[5 + i * 3] = animColors[i].R;
          data[6 + i * 3] = animColors[i].G;
          data[7 + i * 3] = animColors[i].B;
        }

        SendOmenBiosWmi(7, data, 0, WMI_COMMAND_ID);
        return true;
      }
    }

    /// <summary>能力查询：协议是否下发指定 effectId。UI 用来选协议/效果之前提示用户。</summary>
    public static bool SupportsEffect(LightingControlInterface iface, byte effectId) {
      if (iface == LightingControlInterface.Dojo) return effectId >= 1 && effectId <= 9;
      // ponytail: BasicFourZone firmware only Starlight(2)/Wave(4). HP SDK path calls
      // FourZoneHelper.SetStaticColor instead — no animation at all there.
      return iface == LightingControlInterface.BasicFourZone && (effectId == 2 || effectId == 4);
    }

    // ponytail: 共享预设色表 —— LightingPage 的四区域、PerKey、ReplaySavedLighting 三处
    // 此前各持一份 switch，Pink 色值在 (255,0,0)↔(0xFF,0x69,0xB4) 间分歧过（PerKey 用
    // 品红、Zone 用真粉），这是 silent 行为分歧 root cause。统一于此，Pink 选 OMEN 官方
    // 真粉 (0xFF,0x69,0xB4)，与原 Zone 路径一致。
    public static readonly Dictionary<string, (byte r, byte g, byte b)> PresetColorRgb = new() {
      { "Red",    ((byte)255, (byte)0,   (byte)0)   },
      { "Green",  ((byte)0,   (byte)255, (byte)0)   },
      { "Blue",   ((byte)0,   (byte)0,   (byte)255) },
      { "White",  ((byte)255, (byte)255, (byte)255) },
      { "Cyan",   ((byte)0,   (byte)255, (byte)255) },
      { "Pink",   ((byte)0xFF, (byte)0x69, (byte)0xB4) },
      { "Yellow", ((byte)255, (byte)255, (byte)0)   },
      // 温度色系预设 — 从冷到热的渐变，覆盖 30°C→90°C 视觉映射
      { "IceBlue",    ((byte)0,   (byte)200, (byte)255) },  // ~30°C 冰蓝
      { "CoolGreen",  ((byte)0,   (byte)255, (byte)100) },  // ~45°C 冷绿
      { "WarmYellow", ((byte)255, (byte)200, (byte)0)   },  // ~60°C 暖黄
      { "FieryOrange",((byte)255, (byte)100, (byte)0)   },  // ~75°C 炽橙
      { "HotRed",     ((byte)255, (byte)20,  (byte)20)  },  // ~90°C 炽红
    };
    public static (byte r, byte g, byte b) LookupColor(string name) =>
      PresetColorRgb.TryGetValue(name, out var v) ? v : ((byte)255, (byte)255, (byte)255);

    // ponytail: 共享动画名→ID 映射。四区域与 PerKey 两套 ID；📺AnimNames 是 UI ComboBox
    // 公用显示顺序（与 LoadingPage.xaml 内 ComboBoxItem 顺序一致）。ZoneEffectId 走四区域
    // BIOS/HP SDK 斐道；PerKeyEffectId 走 McuSDK 单键 RGB 灯效字节。
    public static readonly string[] AnimNames = {
      "None", "ColorCycle", "Starlight", "Breathing", "Wave",
      "Raindrop", "AudioPulse", "Confetti", "Sun", "Swipe",
      "AudioBeat",  // PerKey 音频律动 — SetPerKeyAudioAnimation 通道
    };
    // ponytail: Dojo effectId 1..9 (SupportsEffect gates 1..9). Starlight=2/Wave=4 also
    // match BasicFourZone firmware's only-valid {2,4}. Old values {2..11} had 10/11 out
    // of range → ReplaySavedLighting silently fell back to static for Sun/Swipe.
    public static readonly Dictionary<string, byte> AnimNameToZoneId = new() {
      { "ColorCycle", 1 }, { "Starlight", 2 }, { "Breathing", 3 }, { "Wave", 4 },
      { "Raindrop", 5 }, { "AudioPulse", 6 }, { "Confetti", 7 }, { "Sun", 8 }, { "Swipe", 9 },
    };
    public static readonly Dictionary<string, byte> AnimNameToPerKeyId = new() {
      { "ColorCycle", 7 }, { "Starlight", 2 }, { "Breathing", 8 }, { "Wave", 10 },
      { "Raindrop", 13 }, { "AudioPulse", 9 }, { "Confetti", 14 }, { "Sun", 15 }, { "Swipe", 16 }, { "None", 4 },
      { "AudioBeat", 9 },  // 音频律动 — SetPerKeyAudioAnimation 通道,复用 AudioPulse effect ID
    };
    public static byte ZoneEffectId(string name) =>
      AnimNameToZoneId.TryGetValue(name, out var v) ? v : (byte)0;
    public static byte PerKeyEffectId(string name) =>
      AnimNameToPerKeyId.TryGetValue(name, out var v) ? v : (byte)4;
    public static int AnimIndex(string name) => Array.IndexOf(AnimNames, name);

    // ponytail: BIOS brightness byte range — reverse-verified against OmenCore WmiBiosBackend.
    // WMI CmdType=5 expects a raw byte: 0x64 (100) = OFF/minimum, 0xE4 (228) = ON/maximum.
    // HP SDK 的 SetBrightness(brightness) 把 0-100 直接当字节传给同一条 WMI 命令,
    // 于是 brightness=100 → 0x64 → OFF, brightness=50 → 0x32 → 仍低于 OFF 阈值 → 灯条变黑。
    // 此处统一把 UI 的 0-100% 百分比映射回 BIOS 期望的 100..228 区间; brightness=0 显式传 0x64 关闭。
    // Ceiling: 假设线性映射; 部分 BIOS 在 0x64..0xE4 之间是非线性感知,但与 OmenCore 对齐即可。
    static byte MapBrightnessToWmiLevel(byte brightness) {
      if (brightness == 0) return 0x64;        // OFF
      if (brightness >= 100) return 0xE4;      // ON/maximum
      return (byte)(100 + (brightness * 128 / 100)); // 100..228 线性映射
    }

    public static void SetZoneBrightness(LightingDevice device, byte brightness,
        LightingControlInterface controlInterface = LightingControlInterface.BasicFourZone) {
      switch (controlInterface) {
        case LightingControlInterface.Dojo: {
            byte target = device == LightingDevice.LightBar ? (byte)TargetDevice.LightBar : (byte)TargetDevice.FourZoneAni;
            byte[] data = new byte[128];
            data[0] = target;
            // ponytail: Dojo CmdType=11 的 data[3] 是 0-255 范围(Aurora 反编译写死 0xFF),
            // 不走 WMI CmdType=5 的 0x64-0xE4 映射,直接传 0-100 即可(虽然最大只有 40% 亮度,
            // 但 Dojo 高亮度有专门的 BtnBrightHigh_Click 走 128/228 字节)。
            data[3] = brightness;
            SendOmenBiosWmi(11, data, 0, WMI_COMMAND_ID);
            break;
          }
        case LightingControlInterface.BasicFourZone: {
            byte[] data = new byte[4] { MapBrightnessToWmiLevel(brightness), 0, 0, 0 };
            SendOmenBiosWmi(5, data, 0, WMI_COMMAND_ID);
            break;
          }
      }
    }

    public static void SetZoneOff(LightingDevice device, LightingControlInterface controlInterface) {
      switch (controlInterface) {
        default:
          SetZoneBrightness(device, 0, controlInterface);
          break;
      }
    }

    public static System.Windows.Media.Color[] GetZoneStaticColor() {
      byte[] result = SendOmenBiosWmi(2, new byte[1] { 0 }, 128, WMI_COMMAND_ID);
      if (result == null || result.Length < 22) return null;
      var colors = new System.Windows.Media.Color[4];
      for (int i = 0; i < 4; i++) {
        int idx = 25 + i * 3;
        colors[i] = System.Windows.Media.Color.FromRgb(result[idx], result[idx + 1], result[idx + 2]);
      }
      return colors;
    }

    public static byte GetZoneBrightness() {
      byte[] result = SendOmenBiosWmi(4, new byte[1] { 0 }, 128, WMI_COMMAND_ID);
      return (result != null && result.Length > 0) ? result[0] : (byte)0;
    }

    public static int GetCurrentAnimationEffect() {
      byte[] result = SendOmenBiosWmi(12, new byte[4] { 0, 0, 0, 0 }, 4, WMI_COMMAND_ID);
      return (result != null && result.Length > 0) ? result[0] : -1;
    }

    /// <summary>HP OmenFourZoneLighting SDK 包装，提供键盘类型检测和替代灯控</summary>
    internal static class FourZoneHelper {
      private static bool? _available;

      public static bool Available {
        get {
          if (!_available.HasValue)
            try { _available = Omen.OmenFourZoneLighting.FourZoneLighting.IsTurnOn(); }
            catch (Exception ex) { Logger.Verbose($"[FourZoneHelper] IsTurnOn probe: {ex.Message}"); _available = false; }
          return _available.Value;
        }
      }

      public static Omen.OmenFourZoneLighting.KeyboardType GetKeyboardType() {
        try { return Omen.OmenFourZoneLighting.FourZoneLighting.GetKeyboardType(); }
        catch (Exception ex) { Logger.Verbose($"[FourZoneHelper] GetKeyboardType: {ex.Message}"); return Omen.OmenFourZoneLighting.KeyboardType.Normal; }
      }

      public static string GetKeyboardTypeName() => GetKeyboardType() switch {
        Omen.OmenFourZoneLighting.KeyboardType.Rgb => Strings.KbTypeRgbPerKey,
        Omen.OmenFourZoneLighting.KeyboardType.WithNumpad or
          Omen.OmenFourZoneLighting.KeyboardType.OneZoneWithNumpad => Strings.KbTypeFourZoneWithNumpad,
        Omen.OmenFourZoneLighting.KeyboardType.WithoutNumpad or
          Omen.OmenFourZoneLighting.KeyboardType.OneZoneWithoutNumpad => Strings.KbTypeFourZoneWithoutNumpad,
        _ => Strings.KbTypeUnknown,
      };

      public static bool IsLightBarSupported() {
        try { return Omen.OmenFourZoneLighting.FourZoneLighting.GetLightBarSupport(); }
        catch (Exception ex) { Logger.Verbose($"[FourZoneHelper] GetLightBarSupport: {ex.Message}"); return false; }
      }

      public static bool IsTurnedOn() {
        try { return Omen.OmenFourZoneLighting.FourZoneLighting.IsTurnOn(); }
        catch (Exception ex) { Logger.Verbose($"[FourZoneHelper] IsTurnOn: {ex.Message}"); return false; }
      }

      public static void SetStaticColor(LightingDevice device, List<System.Windows.Media.Color> colors, byte brightness) {
        try {
          // ponytail: 顺序根因 — 参考 OmenCore KeyboardLightingServiceV2.ApplyProfileAsync:
          // "Some BIOS revisions (notably on 8BCD/F.31) can reset visible keyboard state
          //  when brightness is written after color-table updates."
          // 旧顺序(先颜色后亮度)会让 SetZoneBrightness 清掉刚写的颜色 → 灯条变黑。
          // 改为先写亮度(BasicFourZone 路径,正确映射 0x64..0xE4),再写颜色作为最终命令。
          OmenLighting.SetZoneBrightness(device, brightness, LightingControlInterface.BasicFourZone);
          var clrArray = colors.ConvertAll(c => System.Drawing.Color.FromArgb(c.R, c.G, c.B)).ToArray();
          if (device == LightingDevice.LightBar)
            Omen.OmenFourZoneLighting.FourZoneLighting.SetLightBarColors(clrArray);
          else
            Omen.OmenFourZoneLighting.FourZoneLighting.SetZoneColors(clrArray);
        } catch (Exception ex) { Logger.Error($"FourZoneHelper.SetStaticColor: {ex.Message}"); }
      }
    }

    /// <summary>OmenLightingSDK.dll 原生包装 — 支持键盘/鼠标/耳机/鼠标垫/音箱/灯条/显示器/ARGB</summary>
    internal static class NativeSdk {
      private static bool _loaded;
      private static readonly object _lock = new();

      public static bool EnsureLoaded() {
        if (_loaded) return true;
        lock (_lock) {
          if (_loaded) return true;
          try {
            // Try to open a test device to verify SDK works
            int h = OmenLightingNative.Keyboard_Open();
            if (h > 0) { OmenLightingNative.Keyboard_Close(h); }
            _loaded = true;
            Logger.Info("NativeSdk: OmenLightingSDK loaded successfully");
          } catch (Exception ex) {
            Logger.Error($"NativeSdk: failed to load OmenLightingSDK — {ex.Message}");
            _loaded = false;
          }
          return _loaded;
        }
      }

      public static string DetectDevices() {
        if (!EnsureLoaded()) return null;
        var sb = new System.Text.StringBuilder();
        foreach (OmenLightingNative.DeviceType dt in Enum.GetValues(typeof(OmenLightingNative.DeviceType))) {
          int h = OmenLightingNative.Open(dt);
          if (h > 0) {
            sb.AppendLine($"{dt}:OK");
            OmenLightingNative.Close(dt, h);
          }
        }
        return sb.Length > 0 ? sb.ToString() : null;
      }

      /// <summary>设置静态颜色 — 会自动打开/关闭设备</summary>
      public static bool SetStaticColor(OmenLightingNative.DeviceType type, byte r, byte g, byte b) {
        if (!EnsureLoaded()) return false;
        try {
          int h = OmenLightingNative.Open(type);
          if (h <= 0) return false;
          bool ok = OmenLightingNative.SetStatic(type, h, r, g, b);
          OmenLightingNative.Close(type, h);
          return ok;
        } catch (Exception ex) { Logger.Verbose($"[NativeSdk.SetStatic] {ex.Message}"); return false; }
      }
    }

#if DEBUG
    // ponytail: smallest thing that breaks if the Dojo data[2] bitfield layout drifts.
    // Reproduces SetZoneAnimation's Dojo branch bit-math for every (speed,dir,theme) combination
    // and asserts against a hand-computed golden table. If anyone changes 0xFC/0xF3/0x0F masks,
    // the 0x08-vs-0x04 direction pick, or the 0x10..0x50 theme ladder, this fires at startup in
    // debug builds. Release builds compile this out. Ceiling: catches layout drift only — does
    // not validate that the layout itself matches firmware (no ground truth; see
    // docs/lighting-reverse-findings.md).
    static OmenLighting() {
      // golden table indexed by speed(0..3), direction(0..1), theme(0..4)
      byte[,,] expected = new byte[,,] {
        // speed 0
        { { 0x14, 0x24, 0x34, 0x44, 0x54 },   // dir 0
          { 0x18, 0x28, 0x38, 0x48, 0x58 } }, // dir 1
        // speed 1
        { { 0x15, 0x25, 0x35, 0x45, 0x55 },
          { 0x19, 0x29, 0x39, 0x49, 0x59 } },
        // speed 2
        { { 0x16, 0x26, 0x36, 0x46, 0x56 },
          { 0x1A, 0x2A, 0x3A, 0x4A, 0x5A } },
        // speed 3
        { { 0x17, 0x27, 0x37, 0x47, 0x57 },
          { 0x1B, 0x2B, 0x3B, 0x4B, 0x5B } },
      };
      for (byte speed = 0; speed < 4; speed++) {
        for (byte dir = 0; dir < 2; dir++) {
          for (byte theme = 0; theme < 5; theme++) {
            byte b = 0;
            b = (byte)(b & 0xFC | (speed & 0x03));
            b = (byte)(b & 0xF3 | (dir == 1 ? 0x08 : 0x04));
            b = (byte)(b & 0x0F);
            switch (theme) {
              case 0: b |= 0x10; break;
              case 1: b |= 0x20; break;
              case 2: b |= 0x30; break;
              case 3: b |= 0x40; break;
              case 4: b |= 0x50; break;
            }
            System.Diagnostics.Debug.Assert(b == expected[speed, dir, theme],
              $"Dojo bitfield drift: speed={speed} dir={dir} theme={theme} got=0x{b:X2} want=0x{expected[speed, dir, theme]:X2}");
          }
        }
      }
      // ponytail: 一行可运行自检 —— Pink 色值曾发生过 PerKey/Zone 不一致 silent bug。
      // 断言共享表里 Pink 是 OMEN 官方真粉 (0xFF,0x69,0xB4)+7 色键全集存在，防止落入回 White。
      System.Diagnostics.Debug.Assert(PresetColorRgb["Pink"] == (0xFF, 0x69, 0xB4),
        "PresetColorRgb['Pink'] drifted from OMEN-pink (0xFF,0x69,0xB4)");
      System.Diagnostics.Debug.Assert(PresetColorRgb.Count == 12 &&
        AnimNameToZoneId.Count == 9 && AnimNameToPerKeyId.Count == 11 &&
        AnimNames.Length == 11, "Lighting table size drift");
    }
#endif
  }
}
