// NativeMethods.cs - Windows P/Invoke 声明
// 显示配置 (DisplayConfig API) + 电源管理 (Power API)
// 从 PerfPage.xaml.cs 提取, 原嵌套类改为 internal 供全程序集复用
using System;
using System.Runtime.InteropServices;
using System.Diagnostics;
using OmenSuperHub.Services;

namespace OmenSuperHub.Pages {
  // ══════════════════════════════════════
  //   Native methods for Power & Display
  // ══════════════════════════════════════
  internal static class NativeMethods_Display {
    public const int ENUM_CURRENT_SETTINGS = -1;
    public const int DM_DISPLAYFREQUENCY = 0x400000;
    public const int DM_PELSWIDTH = 0x80000;
    public const int DM_PELSHEIGHT = 0x100000;
    public const int VREFRESH = 116;
    public const int CDS_UPDATEREGISTRY = 0x01;
    [DllImport("user32.dll", CharSet = CharSet.Auto)] public static extern bool EnumDisplaySettings(string lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] public static extern int ChangeDisplaySettings(ref DEVMODE lpDevMode, int dwFlags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] public static extern int ChangeDisplaySettingsEx(string lpszDeviceName, ref DEVMODE lpDevMode, IntPtr hwnd, int dwFlags, IntPtr lParam);
    [DllImport("user32.dll")] public static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);
    [DllImport("user32.dll", SetLastError = true)] public static extern int QueryDisplayConfig(uint flags, ref uint numPathArrayElements, [In, Out] DISPLAYCONFIG_PATH_INFO[] pathArray, ref uint numModeInfoArrayElements, [In, Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray, IntPtr pCurrentTopologyId);
    [DllImport("user32.dll")] public static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_TARGET_DEVICE_NAME deviceName);
    [DllImport("gdi32.dll", CharSet = CharSet.Auto)] public static extern IntPtr CreateDC(string lpszDriver, string lpszDevice, string lpszOutput, IntPtr lpInitData);
    [DllImport("gdi32.dll")] public static extern int GetDeviceCaps(IntPtr hdc, int nIndex);
    [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);
    public const int LOGPIXELSX = 88;
    public const int LOGPIXELSY = 90;
    [StructLayout(LayoutKind.Sequential)]
    public struct LUID { public uint LowPart; public int HighPart; }
    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_PATH_INFO {
      public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
      public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
      public uint flags;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_2DREGION { public uint cx; public uint cy; }
    // ponytail: layout MUST match Win32 byte-for-byte. QueryDisplayConfig writes an
    // array of PATH_INFO into a marshaler-allocated buffer sized by Marshal.SizeOf(this).
    // An undersized struct makes the API overflow the buffer → STATUS_HEAP_CORRUPTION
    // (0xc0000374) crash in ntdll. Real sizes: SOURCE=20, TARGET=48, PATH=72 bytes.
    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_PATH_SOURCE_INFO {
      public LUID adapterId;
      public uint id;
      public uint modeInfoIdx;   // union: modeInfoIdx | {cloneGroupId, sourceModeInfoIdx}
      public uint statusFlags;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_PATH_TARGET_INFO {
      public LUID adapterId;
      public uint id;
      public uint modeInfoIdx;   // union: modeInfoIdx | {desktopModeInfoIdx, targetModeInfoIdx}
      public DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY outputTechnology;
      public uint rotation;      // DISPLAYCONFIG_ROTATION
      public uint scaling;       // DISPLAYCONFIG_SCALING
      public DISPLAYCONFIG_RATIONAL refreshRate;
      public uint scanLineOrdering; // DISPLAYCONFIG_SCANLINE_ORDERING
      public uint targetAvailable;  // BOOL
      public uint statusFlags;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_MODE_INFO {
      public uint infoType;
      public uint id;
      public LUID adapterId;
      public DISPLAYCONFIG_VIDEO_SIGNAL_INFO targetMode;
    }
    // ponytail: self-check — sizes must match Win32 or QueryDisplayConfig overflows the heap.
    // Fires in Debug the moment someone shrinks a struct again, instead of silently corrupting memory.
    static NativeMethods_Display() {
      Debug.Assert(Marshal.SizeOf(typeof(DISPLAYCONFIG_PATH_SOURCE_INFO)) == 20, "PATH_SOURCE_INFO must be 20 bytes");
      Debug.Assert(Marshal.SizeOf(typeof(DISPLAYCONFIG_PATH_TARGET_INFO)) == 48, "PATH_TARGET_INFO must be 48 bytes");
      Debug.Assert(Marshal.SizeOf(typeof(DISPLAYCONFIG_PATH_INFO)) == 72, "PATH_INFO must be 72 bytes");
      Debug.Assert(Marshal.SizeOf(typeof(DISPLAYCONFIG_MODE_INFO)) == 64, "MODE_INFO must be 64 bytes");
    }
    // ponytail: SDK has union {videoStandard; AdditionalSignalInfo{bitfield}} — C# uses uint for the 4-byte union
    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_VIDEO_SIGNAL_INFO {
      public ulong pixelRate;
      public DISPLAYCONFIG_RATIONAL hSyncFreq;
      public DISPLAYCONFIG_RATIONAL vSyncFreq;
      public DISPLAYCONFIG_RATIONAL activeSize;
      public DISPLAYCONFIG_RATIONAL totalSize;
      public uint videoStandardAndSyncDivider;
      public uint scanLineOrdering;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_RATIONAL { public uint Numerator; public uint Denominator; }
    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_TARGET_DEVICE_NAME_FLAGS { public uint value; }

    public enum DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY : uint {
      OTHER = 0xFFFFFFFF, HD15 = 0, SVIDEO = 1, COMPOSITE_VIDEO = 2,
      COMPONENT_VIDEO = 3, DVI = 4, HDMI = 5, LVDS = 6, DJPN_DVI = 8,
      DJPN_HDMI = 10, DJPN_SDI = 11, DISPLAYPORT_EXTERNAL = 12,
      DISPLAYPORT_EMBEDDED = 13, UDI_EXTERNAL = 14, UDI_EMBEDDED = 15,
      SDI = 16, MICRODISPLAY = 18, INTERNAL = 0x80000000,
      FORCE_UINT32 = 0xFFFFFFFF
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DISPLAYCONFIG_TARGET_DEVICE_NAME {
      public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
      public DISPLAYCONFIG_TARGET_DEVICE_NAME_FLAGS flags;
      public DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY outputTechnology;
      public ushort edidManufactureId;
      public ushort edidProductCodeId;
      public uint connectorInstance;
      [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string monitorFriendlyDeviceName;
      [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string monitorDevicePath;
      // ponytail: Win10 RS3 (1709) added this field — struct goes from 420→424 bytes
      public uint baseOutputTechnology;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_DEVICE_INFO_HEADER {
      public uint type;
      public uint size;
      public LUID adapterId;
      public uint id;
    }
    public enum DISPLAYCONFIG_TOPOLOGY_ID : uint {
      DISPLAYCONFIG_TOPOLOGY_INTERNAL = 0x00000001,
      DISPLAYCONFIG_TOPOLOGY_EXTERNAL = 0x00000002,
      DISPLAYCONFIG_TOPOLOGY_MIRROR = 0x00000004,
      DISPLAYCONFIG_TOPOLOGY_FORCE_UINT32 = 0xFFFFFFFF
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO {
      public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
      public uint value;
      public uint colorEncoding;
      public uint bitsPerColorChannel;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE {
      public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
      public uint value;
    }
    // GET variant: returns min/cur/max relative to recommended
    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_SOURCE_DPI_SCALE_GET {
      public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
      public int minScaleRel;
      public int curScaleRel;
      public int maxScaleRel;
    }
    // SET variant: scaleRel is offset from recommended (e.g. -1 = one step below recommended)
    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAYCONFIG_SOURCE_DPI_SCALE_SET {
      public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
      public int scaleRel;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DISPLAYCONFIG_SOURCE_DEVICE_NAME {
      public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
      [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string viewGdiDeviceName;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct DISPLAY_DEVICE {
      public int cb;
      [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
      [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
      public int StateFlags;
      [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
      [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }
    // ponytail: type-specific overloads, NOT ref DISPLAYCONFIG_DEVICE_INFO_HEADER.
    // The base header is 20 bytes; the marshaler allocates exactly that. But callers set
    // header.size to the real derived struct (e.g. SOURCE_DEVICE_NAME=84, ADVANCED_COLOR=32),
    // and the API writes header.size bytes — overflowing the 20-byte buffer and corrupting
    // the adjacent native heap (we saw _cachedIds get overwritten with "DISPLAY1" bytes,
    // then a 0xc0000005 AV). Each overload makes the marshaler allocate the correct size.
    [DllImport("user32.dll", EntryPoint = "DisplayConfigSetDeviceInfo")] public static extern int DisplayConfigSetDeviceInfo(ref DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE deviceInfo);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigSetDeviceInfo")] public static extern int DisplayConfigSetDeviceInfo(ref DISPLAYCONFIG_SOURCE_DPI_SCALE_SET deviceInfo);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] public static extern int DisplayConfigGetDeviceInfoEx(ref DISPLAYCONFIG_SOURCE_DEVICE_NAME deviceInfo);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] public static extern int DisplayConfigGetDeviceInfoEx(ref DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO deviceInfo);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] public static extern int DisplayConfigGetDeviceInfoEx(ref DISPLAYCONFIG_SOURCE_DPI_SCALE_GET deviceInfo);
    [DllImport("user32.dll")] public static extern int SetDisplayConfig(uint numPathArrayElements, DISPLAYCONFIG_PATH_INFO[] pathArray, uint numModeInfoArrayElements, DISPLAYCONFIG_MODE_INFO[] modeArray, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] public static extern bool EnumDisplayDevices(string lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);
    public const uint QDC_ALL_PATHS = 0x00000001;
    public const uint QDC_ONLY_ACTIVE_PATHS = 0x00000002;
    public const uint QDC_DATABASE_CURRENT = 0x00000004;
    public const uint DISPLAYCONFIG_PATH_ACTIVE = 0x00000001;
    public const uint SDC_APPLY = 0x00000080;
    public const uint SDC_USE_SUPPLIED_DISPLAY_CONFIG = 0x00000020;
    public const uint SDC_SAVE_TO_DATABASE = 0x00000200;
    public const uint SDC_ALLOW_CHANGES = 0x00000400;
    public const uint WM_SYSCOMMAND = 0x0112;
    public const uint SC_MONITORPOWER = 0xF170;
    public const uint INFO_GET_SOURCE_NAME = 1;
    public const uint INFO_GET_TARGET_NAME = 2;
    public const uint INFO_GET_ADVANCED_COLOR = 9;
    public const uint INFO_SET_ADVANCED_COLOR = 10;
    public const uint INFO_GET_DPI_SCALE = unchecked((uint)-3);
    public const uint INFO_SET_DPI_SCALE = unchecked((uint)-4);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct DEVMODE {
      [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
      public short dmSpecVersion; public short dmDriverVersion; public short dmSize; public short dmDriverExtra;
      public int dmFields; public short dmOrientation; public short dmPaperSize; public short dmPaperLength;
      public short dmPaperWidth; public short dmScale; public short dmCopies; public short dmDefaultSource;
      public short dmPrintQuality; public short dmColor; public short dmDuplex; public short dmYResolution;
      public short dmTTOption; public short dmCollate;
      [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
      public short dmLogPixels; public int dmBitsPerPel; public int dmPelsWidth; public int dmPelsHeight;
      public int dmDisplayFlags; public int dmDisplayFrequency;
      public int dmICMMethod; public int dmICMIntent; public int dmMediaType; public int dmDitherType;
      public int dmReserved1; public int dmReserved2; public int dmPanningWidth; public int dmPanningHeight;
    }
  }

  internal static class NativeMethods_Power {
    // ponytail: GUID 提取到共享 PowerOverlay (Services/NativeDefs.cs)
    public static readonly Guid BEST_POWER_EFFICIENCY = PowerOverlay.BestPowerEfficiency;
    public static readonly Guid BEST_PERFORMANCE = PowerOverlay.BestPerformance;
    [DllImport("powrprof.dll")] public static extern uint PowerSetActiveScheme(IntPtr userPowerKey, ref Guid activePolicyGuid);
    [DllImport("powrprof.dll")] public static extern uint PowerSetActiveOverlayScheme(Guid overlaySchemeGuid);
    [DllImport("powrprof.dll")] public static extern uint PowerGetActiveScheme(IntPtr userPowerKey, out IntPtr activePolicyGuid);
    [DllImport("powrprof.dll")] public static extern uint PowerEnumerate(IntPtr rootPowerKey, IntPtr schemeGuid, IntPtr subGroupOfPowerSettings, uint accessFlags, uint index, IntPtr buffer, ref uint bufferSize);
    [DllImport("powrprof.dll")] public static extern uint PowerReadFriendlyName(IntPtr rootPowerKey, ref Guid schemeGuid, IntPtr subGroupOfPowerSettings, IntPtr powerSetting, IntPtr buffer, ref uint bufferSize);
    [DllImport("powrprof.dll")] public static extern uint PowerReadACValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subGroupOfPowerSettings, ref Guid powerSetting, out uint acValueIndex);
    [DllImport("powrprof.dll")] public static extern uint PowerReadDCValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subGroupOfPowerSettings, ref Guid powerSetting, out uint dcValueIndex);
    [DllImport("powrprof.dll")] public static extern uint PowerWriteACValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subGroupOfPowerSettings, ref Guid powerSetting, uint acValueIndex);
    [DllImport("powrprof.dll")] public static extern uint PowerWriteDCValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subGroupOfPowerSettings, ref Guid powerSetting, uint dcValueIndex);
  }
  internal static class NativeMethods_Proc {
    [DllImport("psapi.dll")] public static extern bool EmptyWorkingSet(IntPtr hProcess);

    [StructLayout(LayoutKind.Sequential)]
    public struct MEMORYSTATUSEX {
      public uint dwLength;
      public uint dwMemoryLoad;
      public ulong ullTotalPhys;
      public ulong ullAvailPhys;
      public ulong ullTotalPageFile;
      public ulong ullAvailPageFile;
      public ulong ullTotalVirtual;
      public ulong ullAvailVirtual;
      public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll")]
    public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    public static MEMORYSTATUSEX GetMemoryStatus() {
      var mem = new MEMORYSTATUSEX();
      mem.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
      GlobalMemoryStatusEx(ref mem);
      return mem;
    }
  }

}
