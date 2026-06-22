using System;
using System.Collections.Generic;
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
        Task<int> task = McuGeneralHelper.OpenDevice(pid, vid, interfaceString, "");
        task.Wait();
        return task.Result;
      } catch (AggregateException ae) {
        foreach (var inner in ae.InnerExceptions)
          Logger.Error($"OpenHidDevice AggregateException: {inner.Message}");
        return -1;
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

    public static void SetZoneAnimation(LightingDevice device, byte effectId, byte speed, byte direction,
        byte theme, List<System.Windows.Media.Color> customColors, byte brightness,
        LightingControlInterface controlInterface) {
      byte target = device == LightingDevice.LightBar ? (byte)TargetDevice.LightBar : (byte)TargetDevice.FourZoneAni;

      if (controlInterface == LightingControlInterface.Dojo) {
        byte[] data = new byte[128];
        data[0] = target;
        data[1] = effectId;
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
      } else {
        byte draxEffect;
        if (effectId == 2) draxEffect = 2;
        else if (effectId == 4) draxEffect = 1;
        else return;

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
      }
    }

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
  }
}
