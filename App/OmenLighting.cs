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
          return -1;
      }

      foreach (var (pid, vid, interfaceStr) in candidates) {
        int handle = OpenHidDevice(pid, vid, interfaceStr);
        if (handle > 0)
          return handle;
      }
      return -1;
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
      } catch { return 0; }
    }

    public static NbKeyboardLightingType GetKeyboardType() {
      byte[] result = SendOmenBiosWmi(43, new byte[0], 4, 0x20008);
      if (result != null && result.Length > 0)
        return (NbKeyboardLightingType)result[0];
      return NbKeyboardLightingType.None;
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
        if (!_isAnimationSupported.HasValue) {
          _isAnimationSupported = false;
          if (DeviceModel.GetCycleNumber(DeviceModel.OmenPlatform.ProductNum.FirstOrDefault((SSIDInfo x) => x.SSID.Equals(DeviceModel.ThisSystemID)).Cycle) > 260) {
            _isAnimationSupported = true;
          }
          if (_isAnimationSupported.Value) {
            return IsSupported(kbType, device);
          }
        }
        return false;
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
    };
    public static (byte r, byte g, byte b) LookupColor(string name) =>
      PresetColorRgb.TryGetValue(name, out var v) ? v : ((byte)255, (byte)255, (byte)255);

    // ponytail: 共享动画名→ID 映射。四区域与 PerKey 两套 ID；📺AnimNames 是 UI ComboBox
    // 公用显示顺序（与 LoadingPage.xaml 内 ComboBoxItem 顺序一致）。ZoneEffectId 走四区域
    // BIOS/HP SDK 斐道；PerKeyEffectId 走 McuSDK 单键 RGB 灯效字节。
    public static readonly string[] AnimNames = {
      "None", "ColorCycle", "Starlight", "Breathing", "Wave",
      "Raindrop", "AudioPulse", "Confetti", "Sun", "Swipe"
    };
    public static readonly Dictionary<string, byte> AnimNameToZoneId = new() {
      { "ColorCycle", 2 }, { "Starlight", 3 }, { "Breathing", 4 }, { "Wave", 6 },
      { "Raindrop", 7 }, { "AudioPulse", 8 }, { "Confetti", 9 }, { "Sun", 10 }, { "Swipe", 11 },
    };
    public static readonly Dictionary<string, byte> AnimNameToPerKeyId = new() {
      { "ColorCycle", 7 }, { "Starlight", 2 }, { "Breathing", 8 }, { "Wave", 10 },
      { "Raindrop", 13 }, { "Confetti", 14 }, { "Sun", 15 }, { "Swipe", 16 }, { "None", 4 },
    };
    public static byte ZoneEffectId(string name) =>
      AnimNameToZoneId.TryGetValue(name, out var v) ? v : (byte)0;
    public static byte PerKeyEffectId(string name) =>
      AnimNameToPerKeyId.TryGetValue(name, out var v) ? v : (byte)4;
    public static int AnimIndex(string name) => Array.IndexOf(AnimNames, name);

    public static void SetZoneBrightness(LightingDevice device, byte brightness,
        LightingControlInterface controlInterface = LightingControlInterface.BasicFourZone) {
      switch (controlInterface) {
        case LightingControlInterface.Dojo: {
            byte target = device == LightingDevice.LightBar ? (byte)TargetDevice.LightBar : (byte)TargetDevice.FourZoneAni;
            byte[] data = new byte[128];
            data[0] = target;
            data[3] = brightness;
            SendOmenBiosWmi(11, data, 0, WMI_COMMAND_ID);
            break;
          }
        case LightingControlInterface.BasicFourZone: {
            byte[] data = new byte[4] { brightness, 0, 0, 0 };
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
            try { _available = Omen.OmenFourZoneLighting.FourZoneLighting.IsTurnOn() || true; }
            catch { _available = false; }
          return _available.Value;
        }
      }

      public static Omen.OmenFourZoneLighting.KeyboardType GetKeyboardType() {
        try { return Omen.OmenFourZoneLighting.FourZoneLighting.GetKeyboardType(); }
        catch { return Omen.OmenFourZoneLighting.KeyboardType.Normal; }
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
        catch { return false; }
      }

      public static bool IsTurnedOn() {
        try { return Omen.OmenFourZoneLighting.FourZoneLighting.IsTurnOn(); }
        catch { return false; }
      }

      public static void SetStaticColor(LightingDevice device, List<System.Windows.Media.Color> colors, byte brightness) {
        try {
          var clrArray = colors.ConvertAll(c => System.Drawing.Color.FromArgb(c.R, c.G, c.B)).ToArray();
          if (device == LightingDevice.LightBar)
            Omen.OmenFourZoneLighting.FourZoneLighting.SetLightBarColors(clrArray);
          else
            Omen.OmenFourZoneLighting.FourZoneLighting.SetZoneColors(clrArray);
          Omen.OmenFourZoneLighting.FourZoneLighting.SetBrightness(brightness);
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
        } catch { return false; }
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
      System.Diagnostics.Debug.Assert(PresetColorRgb.Count == 7 &&
        AnimNameToZoneId.Count == 9 && AnimNameToPerKeyId.Count == 9 &&
        AnimNames.Length == 10, "Lighting table size drift");
    }
#endif
  }
}
