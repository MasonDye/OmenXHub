// OmenLightingNative.cs - OmenLightingSDK.dll 原生 P/Invoke 包装
// 支持 OMEN 键盘/鼠标/耳机/鼠标垫/音箱/灯条/显示器/ARGB 灯效
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace OmenSuperHub.Services;

internal static class OmenLightingNative {
  private const string DLL = "OmenLightingSDK.dll";

  // ── 键盘 ──────────────────────────────────────────
  [DllImport(DLL, EntryPoint = "OmenLighting_Keyboard_Open")]
  public static extern int Keyboard_Open();
  [DllImport(DLL, EntryPoint = "OmenLighting_Keyboard_OpenByName")]
  public static extern int Keyboard_OpenByName([MarshalAs(UnmanagedType.LPStr)] string name);
  [DllImport(DLL, EntryPoint = "OmenLighting_Keyboard_Close")]
  public static extern void Keyboard_Close(int handle);

  [DllImport(DLL, EntryPoint = "OmenLighting_Keyboard_SetStatic")]
  public static extern void Keyboard_SetStatic(int handle, byte r, byte g, byte b);
  [DllImport(DLL, EntryPoint = "OmenLighting_Keyboard_SetSingleColorAnimation")]
  public static extern void Keyboard_SetSingleColorAnimation(int handle, int effectId, byte r, byte g, byte b, int speed);
  [DllImport(DLL, EntryPoint = "OmenLighting_Keyboard_SetMultiColorAnimation")]
  public static extern void Keyboard_SetMultiColorAnimation(int handle, int effectId, [In] byte[] colors, int colorCount, int speed);
  [DllImport(DLL, EntryPoint = "OmenLighting_Keyboard_SetPresetColorAnimation")]
  public static extern void Keyboard_SetPresetColorAnimation(int handle, int presetId, int speed);

  [DllImport(DLL, EntryPoint = "OmenLighting_Keyboard_GetAvailableKeys")]
  public static extern int Keyboard_GetAvailableKeys(int handle, [Out] byte[] buffer, int bufferSize);
  [DllImport(DLL, EntryPoint = "OmenLighting_Keyboard_GetKeyByChar")]
  public static extern int Keyboard_GetKeyByChar(int handle, byte keyChar);
  [DllImport(DLL, EntryPoint = "OmenLighting_Keyboard_GetKeyboardLanguage")]
  public static extern int Keyboard_GetKeyboardLanguage(int handle);

  // ── 鼠标 ──────────────────────────────────────────
  [DllImport(DLL, EntryPoint = "OmenLighting_Mouse_Open")]
  public static extern int Mouse_Open();
  [DllImport(DLL, EntryPoint = "OmenLighting_Mouse_OpenByName")]
  public static extern int Mouse_OpenByName([MarshalAs(UnmanagedType.LPStr)] string name);
  [DllImport(DLL, EntryPoint = "OmenLighting_Mouse_Close")]
  public static extern void Mouse_Close(int handle);

  [DllImport(DLL, EntryPoint = "OmenLighting_Mouse_SetStatic")]
  public static extern void Mouse_SetStatic(int handle, byte r, byte g, byte b);
  [DllImport(DLL, EntryPoint = "OmenLighting_Mouse_SetSingleColorAnimation")]
  public static extern void Mouse_SetSingleColorAnimation(int handle, int effectId, byte r, byte g, byte b, int speed);
  [DllImport(DLL, EntryPoint = "OmenLighting_Mouse_SetMultiColorAnimation")]
  public static extern void Mouse_SetMultiColorAnimation(int handle, int effectId, [In] byte[] colors, int colorCount, int speed);
  [DllImport(DLL, EntryPoint = "OmenLighting_Mouse_SetPresetColorAnimation")]
  public static extern void Mouse_SetPresetColorAnimation(int handle, int presetId, int speed);

  // ── 耳机 ──────────────────────────────────────────
  [DllImport(DLL, EntryPoint = "OmenLighting_Headset_Open")]
  public static extern int Headset_Open();
  [DllImport(DLL, EntryPoint = "OmenLighting_Headset_OpenByName")]
  public static extern int Headset_OpenByName([MarshalAs(UnmanagedType.LPStr)] string name);
  [DllImport(DLL, EntryPoint = "OmenLighting_Headset_Close")]
  public static extern void Headset_Close(int handle);

  [DllImport(DLL, EntryPoint = "OmenLighting_Headset_SetStatic")]
  public static extern void Headset_SetStatic(int handle, byte r, byte g, byte b);
  [DllImport(DLL, EntryPoint = "OmenLighting_Headset_SetSingleColorAnimation")]
  public static extern void Headset_SetSingleColorAnimation(int handle, int effectId, byte r, byte g, byte b, int speed);
  [DllImport(DLL, EntryPoint = "OmenLighting_Headset_SetMultiColorAnimation")]
  public static extern void Headset_SetMultiColorAnimation(int handle, int effectId, [In] byte[] colors, int colorCount, int speed);
  [DllImport(DLL, EntryPoint = "OmenLighting_Headset_SetPresetColorAnimation")]
  public static extern void Headset_SetPresetColorAnimation(int handle, int presetId, int speed);

  // ── 鼠标垫 ────────────────────────────────────────
  [DllImport(DLL, EntryPoint = "OmenLighting_MousePad_Open")]
  public static extern int MousePad_Open();
  [DllImport(DLL, EntryPoint = "OmenLighting_MousePad_OpenByName")]
  public static extern int MousePad_OpenByName([MarshalAs(UnmanagedType.LPStr)] string name);
  [DllImport(DLL, EntryPoint = "OmenLighting_MousePad_Close")]
  public static extern void MousePad_Close(int handle);

  [DllImport(DLL, EntryPoint = "OmenLighting_MousePad_SetStatic")]
  public static extern void MousePad_SetStatic(int handle, byte r, byte g, byte b);
  [DllImport(DLL, EntryPoint = "OmenLighting_MousePad_SetSingleColorAnimation")]
  public static extern void MousePad_SetSingleColorAnimation(int handle, int effectId, byte r, byte g, byte b, int speed);
  [DllImport(DLL, EntryPoint = "OmenLighting_MousePad_SetMultiColorAnimation")]
  public static extern void MousePad_SetMultiColorAnimation(int handle, int effectId, [In] byte[] colors, int colorCount, int speed);
  [DllImport(DLL, EntryPoint = "OmenLighting_MousePad_SetPresetColorAnimation")]
  public static extern void MousePad_SetPresetColorAnimation(int handle, int presetId, int speed);
  [DllImport(DLL, EntryPoint = "OmenLighting_MousePad_GetZoneCount")]
  public static extern int MousePad_GetZoneCount(int handle);

  // ── 音箱 ──────────────────────────────────────────
  [DllImport(DLL, EntryPoint = "OmenLighting_Speaker_Open")]
  public static extern int Speaker_Open();
  [DllImport(DLL, EntryPoint = "OmenLighting_Speaker_OpenByName")]
  public static extern int Speaker_OpenByName([MarshalAs(UnmanagedType.LPStr)] string name);
  [DllImport(DLL, EntryPoint = "OmenLighting_Speaker_Close")]
  public static extern void Speaker_Close(int handle);

  [DllImport(DLL, EntryPoint = "OmenLighting_Speaker_SetStatic")]
  public static extern void Speaker_SetStatic(int handle, byte r, byte g, byte b);
  [DllImport(DLL, EntryPoint = "OmenLighting_Speaker_SetSingleColorAnimation")]
  public static extern void Speaker_SetSingleColorAnimation(int handle, int effectId, byte r, byte g, byte b, int speed);
  [DllImport(DLL, EntryPoint = "OmenLighting_Speaker_SetMultiColorAnimation")]
  public static extern void Speaker_SetMultiColorAnimation(int handle, int effectId, [In] byte[] colors, int colorCount, int speed);
  [DllImport(DLL, EntryPoint = "OmenLighting_Speaker_SetPresetColorAnimation")]
  public static extern void Speaker_SetPresetColorAnimation(int handle, int presetId, int speed);

  // ── 机箱灯条 ──────────────────────────────────────
  [DllImport(DLL, EntryPoint = "OmenLighting_Chassis_Open")]
  public static extern int Chassis_Open();
  [DllImport(DLL, EntryPoint = "OmenLighting_Chassis_OpenByName")]
  public static extern int Chassis_OpenByName([MarshalAs(UnmanagedType.LPStr)] string name);
  [DllImport(DLL, EntryPoint = "OmenLighting_Chassis_Close")]
  public static extern void Chassis_Close(int handle);

  [DllImport(DLL, EntryPoint = "OmenLighting_Chassis_SetStatic")]
  public static extern void Chassis_SetStatic(int handle, byte r, byte g, byte b);
  [DllImport(DLL, EntryPoint = "OmenLighting_Chassis_SetSingleColorAnimation")]
  public static extern void Chassis_SetSingleColorAnimation(int handle, int effectId, byte r, byte g, byte b, int speed);
  [DllImport(DLL, EntryPoint = "OmenLighting_Chassis_SetMultiColorAnimation")]
  public static extern void Chassis_SetMultiColorAnimation(int handle, int effectId, [In] byte[] colors, int colorCount, int speed);
  [DllImport(DLL, EntryPoint = "OmenLighting_Chassis_SetPresetColorAnimation")]
  public static extern void Chassis_SetPresetColorAnimation(int handle, int presetId, int speed);

  // ── 显示器 ────────────────────────────────────────
  [DllImport(DLL, EntryPoint = "OmenLighting_Display_Open")]
  public static extern int Display_Open();
  [DllImport(DLL, EntryPoint = "OmenLighting_Display_OpenByName")]
  public static extern int Display_OpenByName([MarshalAs(UnmanagedType.LPStr)] string name);
  [DllImport(DLL, EntryPoint = "OmenLighting_Display_Close")]
  public static extern void Display_Close(int handle);

  [DllImport(DLL, EntryPoint = "OmenLighting_Display_SetStatic")]
  public static extern void Display_SetStatic(int handle, byte r, byte g, byte b);
  [DllImport(DLL, EntryPoint = "OmenLighting_Display_GetAvailableLeds")]
  public static extern int Display_GetAvailableLeds(int handle, [Out] byte[] buffer, int bufferSize);

  // ── ARGB (第一代) ─────────────────────────────────
  [DllImport(DLL, EntryPoint = "OmenLighting_Argb_Open")]
  public static extern int Argb_Open();
  [DllImport(DLL, EntryPoint = "OmenLighting_Argb_OpenByName")]
  public static extern int Argb_OpenByName([MarshalAs(UnmanagedType.LPStr)] string name);
  [DllImport(DLL, EntryPoint = "OmenLighting_Argb_Close")]
  public static extern void Argb_Close(int handle);

  [DllImport(DLL, EntryPoint = "OmenLighting_Argb_SetStatic")]
  public static extern void Argb_SetStatic(int handle, byte r, byte g, byte b);
  [DllImport(DLL, EntryPoint = "OmenLighting_Argb_GetArticunoFWSupport")]
  public static extern int Argb_GetArticunoFWSupport(int handle);

  // ── ARGB G2 (第二代) ───────────────────────────────
  [DllImport(DLL, EntryPoint = "OmenLighting_ArgbG2_Open")]
  public static extern int ArgbG2_Open();
  [DllImport(DLL, EntryPoint = "OmenLighting_ArgbG2_Close")]
  public static extern void ArgbG2_Close(int handle);
  [DllImport(DLL, EntryPoint = "OmenLighting_ArgbG2_SetStatic")]
  public static extern void ArgbG2_SetStatic(int handle, byte r, byte g, byte b);

  // ── 便捷封装 ──────────────────────────────────────
  public enum DeviceType { Keyboard, Mouse, Headset, MousePad, Speaker, Chassis, Display, Argb, ArgbG2 }

  public static int Open(DeviceType type, string name = null) {
    if (name != null) {
      return type switch {
        DeviceType.Keyboard => Keyboard_OpenByName(name),
        DeviceType.Mouse => Mouse_OpenByName(name),
        DeviceType.Headset => Headset_OpenByName(name),
        DeviceType.MousePad => MousePad_OpenByName(name),
        DeviceType.Speaker => Speaker_OpenByName(name),
        DeviceType.Chassis => Chassis_OpenByName(name),
        DeviceType.Display => Display_OpenByName(name),
        DeviceType.Argb => Argb_OpenByName(name),
        _ => -1,
      };
    }
    return type switch {
      DeviceType.Keyboard => Keyboard_Open(),
      DeviceType.Mouse => Mouse_Open(),
      DeviceType.Headset => Headset_Open(),
      DeviceType.MousePad => MousePad_Open(),
      DeviceType.Speaker => Speaker_Open(),
      DeviceType.Chassis => Chassis_Open(),
      DeviceType.Display => Display_Open(),
      DeviceType.Argb => Argb_Open(),
      DeviceType.ArgbG2 => ArgbG2_Open(),
      _ => -1,
    };
  }

  public static void Close(DeviceType type, int handle) {
    if (handle <= 0) return;
    switch (type) {
      case DeviceType.Keyboard: Keyboard_Close(handle); break;
      case DeviceType.Mouse: Mouse_Close(handle); break;
      case DeviceType.Headset: Headset_Close(handle); break;
      case DeviceType.MousePad: MousePad_Close(handle); break;
      case DeviceType.Speaker: Speaker_Close(handle); break;
      case DeviceType.Chassis: Chassis_Close(handle); break;
      case DeviceType.Display: Display_Close(handle); break;
      case DeviceType.Argb: Argb_Close(handle); break;
      case DeviceType.ArgbG2: ArgbG2_Close(handle); break;
    }
  }

  public static bool SetStatic(DeviceType type, int handle, byte r, byte g, byte b) {
    if (handle <= 0) return false;
    try {
      switch (type) {
        case DeviceType.Keyboard: Keyboard_SetStatic(handle, r, g, b); break;
        case DeviceType.Mouse: Mouse_SetStatic(handle, r, g, b); break;
        case DeviceType.Headset: Headset_SetStatic(handle, r, g, b); break;
        case DeviceType.MousePad: MousePad_SetStatic(handle, r, g, b); break;
        case DeviceType.Speaker: Speaker_SetStatic(handle, r, g, b); break;
        case DeviceType.Chassis: Chassis_SetStatic(handle, r, g, b); break;
        case DeviceType.Display: Display_SetStatic(handle, r, g, b); break;
        case DeviceType.Argb: Argb_SetStatic(handle, r, g, b); break;
        case DeviceType.ArgbG2: ArgbG2_SetStatic(handle, r, g, b); break;
      }
      return true;
    } catch { return false; }
  }

  public static string TryProbe(DeviceType type) {
    int h = Open(type);
    if (h <= 0) return null;
    Close(type, h);
    return $"{type}:OK";
  }

  /// <summary>自动探测所有设备类型，返回可用设备列表</summary>
  public static string ProbeAll() {
    var sb = new StringBuilder();
    foreach (DeviceType t in Enum.GetValues(typeof(DeviceType))) {
      string r = TryProbe(t);
      if (r != null) sb.AppendLine(r);
    }
    return sb.ToString();
  }
}
