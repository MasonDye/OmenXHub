// PerfPage.cs - 性能调优页面
// CPU 功耗 (PL1/PL2)、GPU 设置 (TGP/PPAB/dState/DB/时钟)、电源方案、热切换、刷新率
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using OmenSuperHub.Models;
using OmenSuperHub.Services;
using OmenSuperHub.Utils;
using static OmenSuperHub.OmenHardware;

namespace OmenSuperHub.Pages {
  public partial class PerfPage : System.Windows.Controls.Page {
    public static PerfPage Instance { get; private set; }
    bool _loading;
    bool _optionsBuilt;
    static void Log(string msg) {
      string line = "[PerfPage] " + msg;
      Debug.WriteLine(line);
      try { System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "OmenXHub-PerfPage.log"), line + Environment.NewLine); } catch { }
    }

    public PerfPage() {
      Instance = this;
      _loading = true;   // ponytail: suppress NumberBox ValueChanged during layout/template sync
      InitializeComponent();
      Loaded += PerfPage_Loaded;
    }

    void PerfPage_Loaded(object sender, RoutedEventArgs e) {
      Log("PerfPage_Loaded start");
      _loading = true;
      if (!_optionsBuilt) {
        BuildOptions();
        BuildPwrPlanOptions();
        _optionsBuilt = true;
      } else {
        BuildPowerPlanOptions();
      }
      LoadStateFast();
      // ponytail: DON'T set _loading=false here — NumberBox ValueChanged fires
      //          deferred after LoadStateFast() and would corrupt ConfigService
      //          with the Minimum-clamped value.  _loading is reset to false
      //          at ContextIdle inside the BeginInvoke(Loaded) callback below.
      // ponytail: remove-then-add so cached pages don't stack subscriptions
      PresetManager.OnPresetChanged -= OnPresetChanged;
      PresetManager.OnPresetChanged += OnPresetChanged;

      RefreshPresetList();

      Dispatcher.BeginInvoke(new Action(() => {
        _loading = true;
        LoadStateDeferred();
        BuildAdvancedCards();
        ApplyHardwareVisibility();
        LoadPwrPlanSettings();
        _loading = false;
        _perfExpanded = ActualWidth > PerfCollapseWidth;
        if (_perfExpanded) ExpandPerfGrids();
        else CollapsePerfGrids();
      }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    void OnPresetChanged(string preset) {
      _loading = true;
      LoadStateFast();
      try { LoadStateDeferred(); } catch { }
      // ponytail: dynamic — find index by tag in combo items
      int idx = -1;
      for (int i = 0; i < cbxPerfPreset.Items.Count; i++) {
        if (cbxPerfPreset.Items[i] is ComboBoxItem item && item.Tag as string == preset) { idx = i; break; }
      }
      if (idx >= 0 && cbxPerfPreset.SelectedIndex != idx)
        cbxPerfPreset.SelectedIndex = idx;
      // ponytail: defer _loading=false to ContextIdle so stray NumberBox
      //          ValueChanged (fired after programmatic value set) are suppressed
      Dispatcher.BeginInvoke(new Action(() => {
        _loading = false;
      }), System.Windows.Threading.DispatcherPriority.ContextIdle);
    }

    // ══════════════════════════════════════
    //   Native methods for Power & Display
    // ══════════════════════════════════════
    static class NativeMethods_Display {
      public const int ENUM_CURRENT_SETTINGS = -1;
      public const int DM_DISPLAYFREQUENCY = 0x400000;
      public const int DM_PELSWIDTH = 0x80000;
      public const int DM_PELSHEIGHT = 0x100000;
      public const int VREFRESH = 116;
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

    static class NativeMethods_Power {
      public static readonly Guid BEST_POWER_EFFICIENCY = Guid.Parse("961cc777-2547-4f9d-8174-7d86181b8a7a");
      public static readonly Guid BEST_PERFORMANCE = Guid.Parse("ded574b5-45a0-4f42-8737-46345c09c238");
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

    // ══════════════════════════════════════
    //   Option builders
    // ══════════════════════════════════════
    void BuildOptions() {
      CpuPowerCombo.Items.Clear();
      CpuPowerCombo.Items.Add(new ComboBoxItem { Content = Strings.NotSet, Tag = "null" });
      CpuPowerCombo.Items.Add(new ComboBoxItem { Content = Strings.Maximum, Tag = "max" });
      for (int w = 10; w <= 254; w++) CpuPowerCombo.Items.Add(new ComboBoxItem { Content = w + " W", Tag = w });

      AmdPptCombo.Items.Clear();
      AmdPptCombo.Items.Add(new ComboBoxItem { Content = Strings.NotSet, Tag = 0 });
      // ponytail: 1W 步进，跟 Intel 一致
      for (int w = 8; w <= 300; w++) AmdPptCombo.Items.Add(new ComboBoxItem { Content = w + " W", Tag = w });
	
      IccMaxCombo.Items.Clear();
      IccMaxCombo.Items.Add(new ComboBoxItem { Content = Strings.NotSet, Tag = 0 });
      for (int a = 160; a <= 255; a++) IccMaxCombo.Items.Add(new ComboBoxItem { Content = a + " A", Tag = a });

      AcLoadLineCombo.Items.Clear();
      AcLoadLineCombo.Items.Add(new ComboBoxItem { Content = Strings.NotSet, Tag = 0 });
      AcLoadLineCombo.Items.Add(new ComboBoxItem { Content = "170 mOhm", Tag = 1 });
      AcLoadLineCombo.Items.Add(new ComboBoxItem { Content = "160 mOhm", Tag = 2 });
      AcLoadLineCombo.Items.Add(new ComboBoxItem { Content = "150 mOhm", Tag = 3 });

      CtgpCombo.Items.Clear();
      CtgpCombo.Items.Add(new ComboBoxItem { Content = Strings.Enable, Tag = true });
      CtgpCombo.Items.Add(new ComboBoxItem { Content = Strings.Disable, Tag = false });

      // TPP presets removed — use TppNum + TppExtraSlider directly

      GpuClockCombo.Items.Clear();
      GpuClockCombo.Items.Add(new ComboBoxItem { Content = Strings.GpuClockRestore, Tag = 0 });
      int[] clockPresets = { 300, 600, 900, 1200, 1500, 1800, 2100, 2500 };
      foreach (int c in clockPresets) GpuClockCombo.Items.Add(new ComboBoxItem { Content = c + " MHz", Tag = c });

      GpuCoreOCCombo.Items.Clear();
      GpuCoreOCCombo.Items.Add(new ComboBoxItem { Content = Strings.NotSet, Tag = 0 });
      for (int o = -270; o <= 270; o += 15) {
        if (o == 0) continue;
        GpuCoreOCCombo.Items.Add(new ComboBoxItem { Content = (o >= 0 ? "+" : "") + o + " MHz", Tag = o });
      }

      GpuMemoryOCCombo.Items.Clear();
      GpuMemoryOCCombo.Items.Add(new ComboBoxItem { Content = Strings.NotSet, Tag = 0 });
      for (int m = 100; m <= 2000; m += 100)
        GpuMemoryOCCombo.Items.Add(new ComboBoxItem { Content = "+" + m + " MHz", Tag = m });

      FpsCombo.Items.Clear();
      FpsCombo.Items.Add(new ComboBoxItem { Content = Strings.Unlimited, Tag = 0 });
      foreach (int f in new[] { 30, 60, 90, 120, 144, 165, 240, 300, 360, 480, 1000 })
        FpsCombo.Items.Add(new ComboBoxItem { Content = f + " FPS", Tag = f });

      PowerModeCombo.Items.Clear();
      var modes = GetWindowsPowerModes();
      int savedPm = ConfigService.PowerMode;
      foreach (var m in modes)
        PowerModeCombo.Items.Add(new ComboBoxItem { Content = m.Name, Tag = m.Value, IsSelected = m.Value == savedPm });
      if (PowerModeCombo.SelectedIndex < 0 && modes.Count > 0) PowerModeCombo.SelectedIndex = 0;

      BuildPowerPlanOptions();
      BuildRefreshRateOptions();
      BuildResolutionOptions();
      BuildDpiOptions();
      InitHdr();
    }

    void LoadState() {
      LoadStateFast();
      LoadStateDeferred();
    }

    void LoadStateFast() {
      if (!string.IsNullOrEmpty(ConfigService.CpuPower)) {
        string cp = ConfigService.CpuPower;
        SelectCombo(CpuPowerCombo, cp == "max" ? Strings.Maximum : cp == "null" ? Strings.NotSet : cp);
        int pl1, pl2;
        if (cp == "max") { pl1 = 254; pl2 = 254; }
        else if (cp == "null") { pl1 = -1; pl2 = -1; }
        else if (int.TryParse(cp.Replace(" W", ""), out int w) && w >= 10 && w <= 254) {
          // ponytail: prefer independently stored PL1/PL2 over combo-derived wattage.
          pl1 = ConfigService.CpuPowerPl1 >= 10 ? ConfigService.CpuPowerPl1 : w;
          pl2 = ConfigService.CpuPowerPl2 >= 10 ? ConfigService.CpuPowerPl2 : w;
        }
        else { pl1 = ConfigService.CpuPowerPl1 > 0 ? ConfigService.CpuPowerPl1 : 254; pl2 = ConfigService.CpuPowerPl2 > 0 ? ConfigService.CpuPowerPl2 : 254; }
        CpuPowerPL1Slider.Value = pl1 > 0 ? pl1 : 254;
        CpuPowerPL2Slider.Value = pl2 > 0 ? pl2 : 254;
        CpuPowerPL1Num.Value = pl1 > 0 ? pl1 : 254;
        CpuPowerPL2Num.Value = pl2 > 0 ? pl2 : 254;
      }
      if (ConfigService.IccMax > 0) {
        SelectCombo(IccMaxCombo, ConfigService.IccMax + " A");
        IccMaxSlider.Value = ConfigService.IccMax;
        IccMaxNum.Value = ConfigService.IccMax;
      } else {
        SelectCombo(IccMaxCombo, Strings.NotSet);
        IccMaxSlider.Value = 0;
        IccMaxNum.Value = 0;
      }
      if (ConfigService.AcLoadLine > 0) {
        int mOhm = 180 - 10 * ConfigService.AcLoadLine;
        SelectCombo(AcLoadLineCombo, mOhm + " mOhm");
      } else SelectCombo(AcLoadLineCombo, Strings.NotSet);
      // ── AMD PPT Combo ──
      if (ConfigService.AmdCpuPpt > 0) SelectComboByTag(AmdPptCombo, ConfigService.AmdCpuPpt);
      else SelectCombo(AmdPptCombo, Strings.NotSet);
      // ── AMD 滑块同步（预设切换时通过 OnPresetChanged → LoadStateFast 带到这里） ──
      AmdCpuPptSlider.Value = ConfigService.AmdCpuPpt > 0 ? ConfigService.AmdCpuPpt : 105;
      AmdCpuPptNum.Value = AmdCpuPptSlider.Value;
      AmdCpuTdcSlider.Value = ConfigService.AmdCpuTdc > 0 ? ConfigService.AmdCpuTdc : 80;
      AmdCpuTdcNum.Value = AmdCpuTdcSlider.Value;
      AmdCpuEdcSlider.Value = ConfigService.AmdCpuEdc > 0 ? ConfigService.AmdCpuEdc : 160;
      AmdCpuEdcNum.Value = AmdCpuEdcSlider.Value;
      AmdCpuTctlSlider.Value = ConfigService.AmdCpuTctl > 0 ? ConfigService.AmdCpuTctl : 95;
      AmdCpuTctlNum.Value = AmdCpuTctlSlider.Value;
      // ── 电源模式：从 ConfigService 同步 ──
      SelectComboByTag(PowerModeCombo, ConfigService.PowerMode);

      PpabCheck.IsChecked = ConfigService.PpabEnabled;
      SelectCombo(CtgpCombo, ConfigService.TgpEnabled ? Strings.Enable : Strings.Disable);
      TppExtraSlider.Value = ConfigService.Tpp;
      TppNum.Value = ConfigService.Tpp;
      UpdateTppEnabled();
      DbVersionCombo.SelectedIndex = ConfigService.DBVersion == 1 ? 0 : 1;
      DStateCombo.SelectedIndex = ConfigService.DState == 2 ? 1 : 0;
      UpdateTgpStatus();
      if (ConfigService.MaxFrameRate <= 0) {
        SelectCombo(FpsCombo, Strings.Unlimited);
        FpsSlider.Value = 0;
        FpsNum.Value = 0;
      } else {
        SelectCombo(FpsCombo, ConfigService.MaxFrameRate + " FPS");
        FpsSlider.Value = ConfigService.MaxFrameRate;
        FpsNum.Value = ConfigService.MaxFrameRate;
      }
      if (ConfigService.GpuClock <= 0) {
        SelectCombo(GpuClockCombo, Strings.GpuClockRestore);
        GpuClockSlider.Value = 0;
        GpuClockNum.Value = 0;
      } else {
        SelectCombo(GpuClockCombo, ConfigService.GpuClock + " MHz");
        GpuClockSlider.Value = ConfigService.GpuClock;
        GpuClockNum.Value = ConfigService.GpuClock;
      }
      int coreOc = ConfigService.GpuCoreOverclock;
      if (coreOc < 0) {
        SelectComboByTag(GpuCoreOCCombo, 0);
        GpuCoreOCSlider.Value = 0;
        GpuCoreOCNum.Value = 0;
      } else {
        SelectComboByTag(GpuCoreOCCombo, coreOc);
        GpuCoreOCSlider.Value = coreOc;
        GpuCoreOCNum.Value = coreOc;
      }
      int memOc = ConfigService.GpuMemoryOverclock;
      if (memOc < 0) {
        SelectComboByTag(GpuMemoryOCCombo, 0);
        GpuMemoryOCSlider.Value = 0;
        GpuMemoryOCNum.Value = 0;
      } else {
        SelectComboByTag(GpuMemoryOCCombo, memOc);
        GpuMemoryOCSlider.Value = memOc;
        GpuMemoryOCNum.Value = memOc;
      }
      // ── 电源计划：从 ConfigService 同步 Combo 选中项 ──
      string syncPwr = ConfigService.PowerPlanGuid;
      if (!string.IsNullOrEmpty(syncPwr)) {
        foreach (ComboBoxItem item in PowerPlanCombo.Items) {
          if (item.Tag is string t && t.Equals(syncPwr, StringComparison.OrdinalIgnoreCase)) {
            PowerPlanCombo.SelectedItem = item;
            break;
          }
        }
      }

      EcoQosToggle.IsChecked = ConfigService.EcoQosEnabled;
      EcoQosThrottlePluggedToggle.IsChecked = ConfigService.EcoQosThrottlePlugged;
      UpdateEcoQosSubEnabled();
    }

    void LoadStateDeferred() {
      GetGfxMode(out int mode);
      GfxModeCombo.SelectedIndex = mode;
      UpdateHotSwitchVisibility(mode);
      InitCoreKeepUI();
      // 拓扑摘要
      UpdateTopologyText();
      // CCD 按钮可见性（仅 AMD 双 CCD）
      var cores = CpuTopologyService.GetCores();
      var ccds = cores.Where(c => c.CcdId >= 0).Select(c => c.CcdId).Distinct().ToList();
	    }

    void SelectCombo(ComboBox combo, string text) {
      foreach (ComboBoxItem item in combo.Items)
        if (string.Equals(item.Content?.ToString(), text, StringComparison.Ordinal)) { combo.SelectedItem = item; return; }
    }

    void SelectComboByTag(ComboBox combo, object tag) {
      foreach (ComboBoxItem item in combo.Items)
        if (item.Tag != null && item.Tag.Equals(tag)) { combo.SelectedItem = item; return; }
    }

    // ── CPU 功率 ──
    void SetCpuPowerStatus(bool ok, string mode = "WMI") {
      string color = ok ? "#1FAF5A" : "#E0463F";
      string text = ok ? $"{Strings.CpuPowerStatusOk} ({mode})" : Strings.CpuPowerStatusFail;
      try {
        CpuPowerStatusDot.Fill = new System.Windows.Media.SolidColorBrush(
          (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));
        CpuPowerStatusText.Text = text;
      } catch (Exception ex) { Log("SetCpuPowerStatus UI error: " + ex.Message); }
      Logger.Info($"PerfPage CPU power write => {(ok ? "ok" : "fail")} via {mode}");
    }

    // ── AMD PPT ComboBox ──
    void AmdPptCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) {
      if (_loading) return;
      _loading = true;
      try {
        var item = AmdPptCombo.SelectedItem as ComboBoxItem;
        if (item == null) return;
        int watts = (int)item.Tag;
        if (watts == 0) {
          AmdPptStatusDot.Fill = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#888888"));
          AmdPptStatusText.Text = "未设置";
          AmdCpuPptSlider.Value = 105;
          AmdCpuPptNum.Value = 105;
          ConfigService.AmdCpuPpt = 0; ConfigService.Save("AmdCpuPpt");
          return;
        }
        AmdCpuPptSlider.Value = watts;
        AmdCpuPptNum.Value = watts;
        ConfigService.AmdCpuPpt = watts; ConfigService.Save("AmdCpuPpt");
        bool ok = false;
        // ponytail: PPT 优先 WMI，降级 SMU（与 Intel CPU 功率一致）
        if (watts <= 255) ok = SetCpuPowerLimit((byte)watts);
        if (!ok && AmdAdvancedService.IsAvailable) ok = AmdAdvancedService.SetPptLimit((uint)watts * 1000);
        AmdPptStatusDot.Fill = new System.Windows.Media.SolidColorBrush(
          (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(ok ? "#1FAF5A" : "#E0463F"));
        AmdPptStatusText.Text = ok ? $"PPT={watts}W ✓" : "写入失败";
      } finally { _loading = false; }
    }

    void CpuPower_SelectionChanged(object sender, SelectionChangedEventArgs e) {
      if (_loading) return;
      _loading = true;
      try {
        var item = CpuPowerCombo.SelectedItem as ComboBoxItem;
        if (item == null) return;
        string tag = item.Tag.ToString();
        if (tag == "null") {
          ConfigService.CpuPower = "null";
          ConfigService.CpuPowerPl1 = -1;
          ConfigService.CpuPowerPl2 = -1;
          ConfigService.Save("CpuPower");
          return;
        }
        if (tag == "max") {
          ConfigService.CpuPower = "max";
          ConfigService.CpuPowerPl1 = 254;
          ConfigService.CpuPowerPl2 = 254;
          CpuPowerPL1Slider.Value = 254;
          CpuPowerPL2Slider.Value = 254;
          CpuPowerPL1Num.Value = 254;
          CpuPowerPL2Num.Value = 254;
          SetCpuPowerStatus(SetCpuPowerLimit(254));
          ConfigService.Save("CpuPower");
          return;
        }
        if (int.TryParse(tag, out int val) && val >= 10 && val <= 254) {
          ConfigService.CpuPower = val + " W";
          ConfigService.CpuPowerPl1 = val;
          ConfigService.CpuPowerPl2 = val;
          CpuPowerPL1Slider.Value = val;
          CpuPowerPL2Slider.Value = val;
          CpuPowerPL1Num.Value = val;
          CpuPowerPL2Num.Value = val;
          // ponytail: AMD 优先 WMI，降级 SMU PPT
          bool ok = SetCpuPowerLimit((byte)val);
          if (!ok && AmdAdvancedService.IsAvailable) {
            ok = AmdAdvancedService.SetPptLimit((uint)val * 1000);
            SetCpuPowerStatus(ok, "SMU");
          } else {
            SetCpuPowerStatus(ok, "WMI");
          }
          ConfigService.Save("CpuPower");
        }
      } finally { _loading = false; }
    }

    void CpuPowerPL1Num_ValueChanged(object s, RoutedEventArgs e) {
      if (_loading) return;
      _loading = true;
      try {
        double? val = CpuPowerPL1Num.Value;
        if (val == null || val < 1 || val > 254) return;
        int v = (int)val;
        if (v == ConfigService.CpuPowerPl1) return;
        // PL1 是 CPU 功率代表值，PL2 必须 >= PL1，否则 0x29 不生效。
        int pl2 = ConfigService.CpuPowerPl2 > 0 ? ConfigService.CpuPowerPl2 : 254;
        if (v > pl2) {
          // 抬高 PL1 越过 PL2：先把 PL2 拉到 PL1 才能生效，原地写 PL1 alone 无意义。
          if (!SetCpuPowerLimit((byte)v, (byte)v)) { SetCpuPowerStatus(false); return; }
          ConfigService.CpuPowerPl1 = v;
          ConfigService.CpuPowerPl2 = v;
          ConfigService.CpuPower = v + " W";
          CpuPowerPL2Slider.Value = v;
          CpuPowerPL2Num.Value = v;
          SelectCombo(CpuPowerCombo, v + " W");
          ConfigService.Save("CpuPower");
          SetCpuPowerStatus(true);
          return;
        }
        if (!SetCpuPowerLimitPL1Only((byte)v)) { SetCpuPowerStatus(false); return; }
        ConfigService.CpuPowerPl1 = v;
        ConfigService.CpuPower = v + " W";
        SelectCombo(CpuPowerCombo, v + " W");
        ConfigService.Save("CpuPower");
        SetCpuPowerStatus(true);
      } finally { _loading = false; }
    }

    void CpuPowerPL2Num_ValueChanged(object s, RoutedEventArgs e) {
      if (_loading) return;
      _loading = true;
      try {
        double? val = CpuPowerPL2Num.Value;
        if (val == null || val < 1 || val > 254) return;
        int v = (int)val;
        if (v == ConfigService.CpuPowerPl2) return;
        int pl1 = ConfigService.CpuPowerPl1 > 0 ? ConfigService.CpuPowerPl1 : 254;
        // PL2 必须 >= PL1，否则 PL1 持续压制，PL2 不生效。夹紧到 PL1，体现到 UI。
        if (v < pl1) {
          v = pl1;
          CpuPowerPL2Slider.Value = v;
          CpuPowerPL2Num.Value = v;
        }
        if (!SetCpuPowerLimitPL2Only((byte)v)) { SetCpuPowerStatus(false); return; }
        ConfigService.CpuPowerPl2 = v;
        ConfigService.Save("CpuPowerPl2");
        // CPU 功率 UI 值跟 PL1 走，PL2 单改不动它。
        SetCpuPowerStatus(true);
      } finally { _loading = false; }
    }

    // ── IccMax ──
    void IccMax_Changed(object sender, SelectionChangedEventArgs e) {
      if (_loading) return;
      _loading = true;
      try {
        var item = IccMaxCombo.SelectedItem as ComboBoxItem;
        if (item == null) return;
        int val = (int)item.Tag;
        if (val == ConfigService.IccMax) return;
        if (val == 0) { ConfigService.IccMax = 0; IccMaxSlider.Value = 0; ConfigService.Save("IccMax"); return; }
        ConfigService.IccMax = val; IccMaxSlider.Value = val;
        SetIccMaxByWmi(val);
        ConfigService.Save("IccMax");
      } finally { _loading = false; }
    }

    void IccMaxNum_ValueChanged(object s, RoutedEventArgs e) {
      if (_loading) return;
      _loading = true;
      try {
        double? val = IccMaxNum.Value;
        if (val == null || val < 0 || val > 255) return;
        int v = (int)val;
        // ponytail: valid set is {0} ∪ [160,255]; 1-159 is a dead band. Snap to 160
        // (safer floor — sub-160 causes throttling/hang) so the user sees a visible
        // jump instead of silently becoming 0.
        if (v > 0 && v < 160) v = 160;
        if (v == ConfigService.IccMax) return;
        IccMaxNum.Value = v; IccMaxSlider.Value = v;
        if (v > 0) SetIccMaxByWmi(v);
        ConfigService.IccMax = v; ConfigService.Save("IccMax");
        SelectCombo(IccMaxCombo, v == 0 ? Strings.NotSet : v + " A");
      } finally { _loading = false; }
    }

    // ── AC Load Line ──
    void AcLoadLine_Changed(object sender, SelectionChangedEventArgs e) {
      if (_loading) return;
      var item = AcLoadLineCombo.SelectedItem as ComboBoxItem;
      if (item == null) return;
      int level = (int)item.Tag;
      if (level == 0) { ConfigService.AcLoadLine = 0; ConfigService.Save("AcLoadLine"); return; }
      ConfigService.AcLoadLine = level;
      SetLoadLine(level);
      ConfigService.Save("AcLoadLine");
    }

    // ── 电源模式 ──
    void PowerMode_SelectionChanged(object sender, SelectionChangedEventArgs e) {
      if (_loading) return;
      var item = PowerModeCombo.SelectedItem as ComboBoxItem;
      if (item == null) return;
      int val = (int)item.Tag;
      ConfigService.PowerMode = val;
      ConfigService.Save("PowerMode");
      Guid guid;
      if (val == 0) guid = NativeMethods_Power.BEST_POWER_EFFICIENCY;
      else if (val == 2) guid = NativeMethods_Power.BEST_PERFORMANCE;
      else guid = Guid.Empty;
      NativeMethods_Power.PowerSetActiveOverlayScheme(guid);
    }

    // ── 电源计划 ──
    void BuildPowerPlanOptions() {
      PowerPlanCombo.Items.Clear();
      var plans = GetWindowsPowerPlans();
      string savedGuid = ConfigService.PowerPlanGuid;
      foreach (var p in plans) {
        bool isActive = string.IsNullOrEmpty(savedGuid) ? p.IsActive : p.Guid == savedGuid;
        PowerPlanCombo.Items.Add(new ComboBoxItem { Content = p.Name, Tag = p.Guid, IsSelected = isActive });
      }
      if (PowerPlanCombo.SelectedIndex < 0 && plans.Count > 0)
        PowerPlanCombo.SelectedIndex = 0;
    }

    void PowerPlan_SelectionChanged(object sender, SelectionChangedEventArgs e) {
      if (_loading) return;
      var item = PowerPlanCombo.SelectedItem as ComboBoxItem;
      if (item == null) return;
      string guid = (string)item.Tag;
      ConfigService.PowerPlanGuid = guid;
      ConfigService.Save("PowerPlanGuid");
      if (!string.IsNullOrEmpty(guid)) {
        var g = Guid.Parse(guid);
        NativeMethods_Power.PowerSetActiveScheme(IntPtr.Zero, ref g);
      }
      LoadPwrPlanSettings();
    }

    static List<(string Name, string Guid, bool IsActive)> GetWindowsPowerPlans() {
      var plans = new List<(string, string, bool)>();
      try {
        string activeGuid = "";
        IntPtr activePtr;
        if (NativeMethods_Power.PowerGetActiveScheme(IntPtr.Zero, out activePtr) == 0) {
          activeGuid = Marshal.PtrToStructure<Guid>(activePtr).ToString();
          Marshal.FreeHGlobal(activePtr);
        }
        uint index = 0;
        while (true) {
          uint bufSize = 0;
          uint ret = NativeMethods_Power.PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 16, index, IntPtr.Zero, ref bufSize);
          if (ret == 259) break;
          if (ret != 234) break;
          IntPtr buf = Marshal.AllocHGlobal((int)bufSize);
          try {
            ret = NativeMethods_Power.PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 16, index, buf, ref bufSize);
            if (ret != 0) break;
            var guid = Marshal.PtrToStructure<Guid>(buf);
            string name = GetPowerPlanName(guid);
            plans.Add((name, guid.ToString(), guid.ToString() == activeGuid));
          } finally { Marshal.FreeHGlobal(buf); }
          index++;
        }
      } catch { }
      return plans;
    }

    static string GetPowerPlanName(Guid guid) {
      string g = guid.ToString();
      switch (g) {
        case "381b4222-f694-41f0-9685-ff5bb260df2f": return "平衡";
        case "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c": return "高性能";
        case "a1841308-3541-4fab-bc81-f71556f20b4a": return "节能";
        case "e9a42b02-d5df-448d-aa00-03f14749eb61": return "卓越性能";
      }
      try {
        uint bufSize = 2048;
        IntPtr buf = Marshal.AllocHGlobal((int)bufSize);
        try {
          if (NativeMethods_Power.PowerReadFriendlyName(IntPtr.Zero, ref guid, IntPtr.Zero, IntPtr.Zero, buf, ref bufSize) == 0) {
            string name = Marshal.PtrToStringUni(buf);
            if (!string.IsNullOrEmpty(name)) return name;
          }
        } finally { Marshal.FreeHGlobal(buf); }
      } catch { }
      return guid.ToString();
    }

    static List<(string Name, int Value)> GetWindowsPowerModes() {
      return new List<(string, int)> {
        (Strings.PowerModeEfficiency, 0),
        (Strings.PowerModeBalanced, 1),
        (Strings.PowerModePerformance, 2)
      };
    }

    // ── EcoQoS ──
    void UpdateEcoQosSubEnabled() {
      bool on = EcoQosToggle.IsChecked == true;
      EcoQosExtra.Opacity = on ? 1.0 : 0.4;
      EcoQosExtra.IsEnabled = on;
    }

    void EcoQosToggle_Checked(object sender, RoutedEventArgs e) {
      UpdateEcoQosSubEnabled();
      ConfigService.EcoQosEnabled = true;
      ConfigService.Save("EcoQosEnabled");
      EcoQosService.SetEnabled(true);
    }

    void EcoQosToggle_Unchecked(object sender, RoutedEventArgs e) {
      UpdateEcoQosSubEnabled();
      ConfigService.EcoQosEnabled = false;
      ConfigService.Save("EcoQosEnabled");
      EcoQosService.SetEnabled(false);
    }

    void EcoQosThrottlePlugged_Changed(object sender, RoutedEventArgs e) {
      ConfigService.EcoQosThrottlePlugged = EcoQosThrottlePluggedToggle.IsChecked == true;
      ConfigService.Save("EcoQosThrottlePlugged");
      EcoQosService.SetThrottlePlugged(EcoQosThrottlePluggedToggle.IsChecked == true);
    }

    void EcoQosWhitelistEdit_Click(object sender, RoutedEventArgs e) { ShowEcoQosListDialog(true); }
    void EcoQosBlacklistEdit_Click(object sender, RoutedEventArgs e) { ShowEcoQosListDialog(false); }

    void ShowEcoQosListDialog(bool isWhitelist) {
      string title = isWhitelist ? "进程白名单" : "进程黑名单";
      string current = isWhitelist ? ConfigService.EcoQosWhitelist : ConfigService.EcoQosBlacklist;
      var dlg = new Window {
        Title = title, Width = 400, Height = 300,
        WindowStartupLocation = WindowStartupLocation.CenterOwner,
        Owner = Window.GetWindow(this)
      };
      var sp = new StackPanel { Margin = new Thickness(10) };
      var tb = new TextBox { Text = current, AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap, Height = 200,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
      sp.Children.Add(tb);
      var btnP = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
      var ok = new Button { Content = "确定", Width = 80, Height = 30 };
      ok.Click += (_, __) => { dlg.DialogResult = true; dlg.Close(); };
      btnP.Children.Add(ok);
      var cancel = new Button { Content = "取消", Width = 80, Height = 30, Margin = new Thickness(8, 0, 0, 0) };
      cancel.Click += (_, __) => { dlg.DialogResult = false; dlg.Close(); };
      btnP.Children.Add(cancel);
      sp.Children.Add(btnP);
      dlg.Content = sp;
      if (dlg.ShowDialog() == true) {
        string result = tb.Text;
        if (isWhitelist) { ConfigService.EcoQosWhitelist = result; ConfigService.Save("EcoQosWhitelist"); EcoQosService.SaveWhitelist(result); }
        else { ConfigService.EcoQosBlacklist = result; ConfigService.Save("EcoQosBlacklist"); EcoQosService.SaveBlacklist(result); }
      }
    }

    // ── Core Keep ──
    CoreKeepEntry _currentSelectedEntry;

    void InitCoreKeepUI() {
      var data = CoreKeepService.Load();
      CoreKeepMasterToggle.IsChecked = data.MasterEnabled;
      CoreKeepList.ItemsSource = data.Entries;
      CoreKeepList.DisplayMemberPath = "ProcessName";
      // ponytail: 只订阅一次，页面重载不叠加
      CoreKeepList.SelectionChanged -= CoreKeepList_SelectionChanged;
      CoreKeepList.SelectionChanged += CoreKeepList_SelectionChanged;
      // 拓扑摘要
      UpdateTopologyText();
      // 守护开关和间隔
      CoreKeepGuardToggle.IsChecked = data.GuardIntervalMs > 0;
      CoreKeepGuardInterval.Value = Math.Max(1, Math.Min(10, data.GuardIntervalMs / 1000));
      // 优先级 ComboBox
      BuildPriorityCombo();
      // 子控件状态
      bool sub = data.MasterEnabled;
      CoreKeepList.IsEnabled = sub;
      CoreKeepAddBtn.IsEnabled = sub;
      CoreKeepSaveBtn.IsEnabled = sub && _currentSelectedEntry != null;
      CoreKeepRefreshBtn.IsEnabled = true;
      CoreKeepDeleteBtn.IsEnabled = sub;
      CoreKeepBenchBtn.IsEnabled = sub;
      CoreKeepGuardToggle.IsEnabled = sub;
      CoreKeepGuardInterval.IsEnabled = sub && CoreKeepGuardToggle.IsChecked == true;
      SetCoreModeBtnEnabled(false); // 未选中条目时禁用模式按钮
      if (sub) CoreKeepService.StartAutoApply(data);
    }

    void UpdateTopologyText() {
      var topo = CoreKeepService.GetTopology();
      if (topo.IsHybrid)
        CoreKeepTopologyText.Text = string.Format(Strings.CoreKeepTopologyHybrid, topo.TotalLogical, topo.PerformanceCores.Length, topo.EfficientCores.Length);
      else if (topo.IsDualCcd)
        CoreKeepTopologyText.Text = string.Format(Strings.CoreKeepTopologyDualCcd, topo.TotalLogical, topo.Ccd0Count, topo.Ccd1Count);
      else
        CoreKeepTopologyText.Text = string.Format(Strings.CoreKeepTopologyNormal, topo.TotalLogical);
    }

    void BuildPriorityCombo() {
      CoreKeepPriorityCombo.Items.Clear();
      CoreKeepPriorityCombo.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = Strings.CoreKeepPriorityIdle, Tag = (uint)0x00000040 });
      CoreKeepPriorityCombo.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = Strings.CoreKeepPriorityBelowNormal, Tag = (uint)0x00004000 });
      CoreKeepPriorityCombo.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = Strings.CoreKeepPriorityNormal, Tag = (uint)0x00000020 });
      CoreKeepPriorityCombo.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = Strings.CoreKeepPriorityAboveNormal, Tag = (uint)0x00008000 });
      CoreKeepPriorityCombo.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = Strings.CoreKeepPriorityHigh, Tag = (uint)0x00000080 });
      CoreKeepPriorityCombo.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = Strings.CoreKeepPriorityRealtime, Tag = (uint)0x00000100 });
    }

    void SelectPriorityInCombo(uint priorityClass) {
      foreach (System.Windows.Controls.ComboBoxItem item in CoreKeepPriorityCombo.Items) {
        if (item.Tag is uint tag && tag == priorityClass) { CoreKeepPriorityCombo.SelectedItem = item; return; }
      }
      CoreKeepPriorityCombo.SelectedIndex = -1;
    }

    void SetCoreModeBtnEnabled(bool enabled) {
      CoreKeepModeAuto.IsEnabled = enabled;
      CoreKeepModeAll.IsEnabled = enabled;
      CoreKeepModeManual.IsEnabled = enabled;
    }

    void HighlightModeButton(string mode) {
      CoreKeepModeAuto.Appearance = mode == "Auto" ? Wpf.Ui.Controls.ControlAppearance.Primary : Wpf.Ui.Controls.ControlAppearance.Secondary;
      CoreKeepModeAll.Appearance = mode == "All" ? Wpf.Ui.Controls.ControlAppearance.Primary : Wpf.Ui.Controls.ControlAppearance.Secondary;
      CoreKeepModeManual.Appearance = mode == "Manual" ? Wpf.Ui.Controls.ControlAppearance.Primary : Wpf.Ui.Controls.ControlAppearance.Secondary;
    }

    void CoreKeepList_SelectionChanged(object sender, SelectionChangedEventArgs e) {
      _currentSelectedEntry = CoreKeepList.SelectedItem as CoreKeepEntry;
      if (_currentSelectedEntry == null) {
        CoreKeepProcInput.Text = "";
        CoreKeepPriorityText.Text = " -";
        CoreKeepAffinityText.Text = " -";
        CoreKeepStatusIcon.Text = "";
        CoreKeepLivePriorityText.Text = "";
        CoreKeepPriorityCombo.IsEnabled = false;
        CoreKeepPriorityCombo.SelectedIndex = -1;
        SetCoreModeBtnEnabled(false);
        CoreKeepCoreList.Visibility = Visibility.Collapsed;
        CoreKeepSaveBtn.IsEnabled = false;
        return;
      }
      CoreKeepProcInput.Text = _currentSelectedEntry.ProcessName;
      CoreKeepPriorityText.Text = CoreKeepService.PriorityClassName(_currentSelectedEntry.PriorityClass);
      CoreKeepAffinityText.Text = "0x" + _currentSelectedEntry.AffinityMask.ToString("X");
      // 查询当前实际状态（支持 PID）
      var state = CoreKeepService.QueryProcessState(_currentSelectedEntry.ProcessName, _currentSelectedEntry.ProcessId);
      if (!state.Running) {
        CoreKeepStatusIcon.Text = Strings.CoreKeepStatusNotRunning;
        CoreKeepLivePriorityText.Text = "";
      } else if (state.PriorityClass == _currentSelectedEntry.PriorityClass && state.AffinityMask == _currentSelectedEntry.AffinityMask) {
        CoreKeepStatusIcon.Text = Strings.CoreKeepStatusMatched;
        CoreKeepLivePriorityText.Text = "";
      } else {
        CoreKeepStatusIcon.Text = Strings.CoreKeepStatusMismatch;
        CoreKeepLivePriorityText.Text = $"({CoreKeepService.PriorityClassName(state.PriorityClass)}/0x{state.AffinityMask:X})";
      }
      // 模式按钮
      SetCoreModeBtnEnabled(true);
      HighlightModeButton(_currentSelectedEntry.CoreMode ?? "All");
      // 优先级 ComboBox
      CoreKeepPriorityCombo.IsEnabled = CoreKeepMasterToggle.IsChecked == true;
      SelectPriorityInCombo(_currentSelectedEntry.PriorityClass);
      CoreKeepSaveBtn.IsEnabled = CoreKeepMasterToggle.IsChecked == true;
      // 手动模式下显示核心选择
      CoreKeepCoreList.Visibility = (_currentSelectedEntry.CoreMode == "Manual") ? Visibility.Visible : Visibility.Collapsed;
      if (_currentSelectedEntry.CoreMode == "Manual")
        PopulateCoreCheckboxes(_currentSelectedEntry.PreferredCores, _currentSelectedEntry.AffinityMask);
    }

    void PopulateCoreCheckboxes(int[] selected, long affinityMask = 0) {
      var topo = CoreKeepService.GetTopology();
      var items = new List<CoreCheckItem>();
      // ponytail: 优先用 selected 数组；没有时从 AffinityMask 反推勾选的核心
      HashSet<int> selSet;
      if (selected != null && selected.Length > 0)
        selSet = new HashSet<int>(selected);
      else {
        selSet = new HashSet<int>();
        for (int i = 0; i < topo.TotalLogical; i++) {
          if ((affinityMask & (1L << i)) != 0) selSet.Add(i);
        }
      }
      for (int i = 0; i < topo.TotalLogical; i++) {
        items.Add(new CoreCheckItem { CoreIndex = i, IsChecked = selSet.Contains(i) });
      }
      CoreKeepCoreList.ItemsSource = items;
    }

    void CoreKeepMasterToggle_Changed(object sender, RoutedEventArgs e) {
      bool on = CoreKeepMasterToggle.IsChecked == true;
      CoreKeepList.IsEnabled = on;
      CoreKeepAddBtn.IsEnabled = on;
      CoreKeepSaveBtn.IsEnabled = on && _currentSelectedEntry != null;
      CoreKeepDeleteBtn.IsEnabled = on;
      CoreKeepBenchBtn.IsEnabled = on;
      CoreKeepGuardToggle.IsEnabled = on;
      CoreKeepGuardInterval.IsEnabled = on && CoreKeepGuardToggle.IsChecked == true;
      CoreKeepPriorityCombo.IsEnabled = on && _currentSelectedEntry != null;
      var data = CoreKeepService.Load();
      data.MasterEnabled = on;
      CoreKeepService.Save(data);
      if (on) CoreKeepService.StartAutoApply(data); else CoreKeepService.StopAutoApply();
    }

    void CoreKeepRefresh_Click(object sender, RoutedEventArgs e) {
      string raw = CoreKeepProcInput.Text?.Trim();
      if (string.IsNullOrEmpty(raw)) {
        RefreshCoreKeepList(CoreKeepService.Load());
        return;
      }
      // ponytail: Refresh 只查看当前运行状态，不修改保存的规则。支持 PID 输入
      ProcessAffinityState state;
      bool isPid = int.TryParse(raw, out int pid);
      if (isPid)
        state = CoreKeepService.QueryProcessState("", pid);
      else {
        string procName = raw.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? raw : raw + ".exe";
        state = CoreKeepService.QueryProcessState(procName);
      }
      if (!state.Running) {
        CoreKeepPriorityText.Text = " -";
        CoreKeepAffinityText.Text = " -";
        CoreKeepStatusIcon.Text = Strings.CoreKeepStatusNotRunning;
        CoreKeepLivePriorityText.Text = "";
        return;
      }
      CoreKeepPriorityText.Text = CoreKeepService.PriorityClassName(state.PriorityClass);
      CoreKeepAffinityText.Text = "0x" + state.AffinityMask.ToString("X");
      CoreKeepLivePriorityText.Text = "";
      // 如果有已保存规则，对比显示
      var data = CoreKeepService.Load();
      var existing = isPid
        ? data.Entries.Find(x => x.ProcessId == pid)
        : data.Entries.Find(x => x.ProcessName.Equals(raw + (raw.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? "" : ".exe"), StringComparison.OrdinalIgnoreCase));
      if (existing != null) {
        if (state.PriorityClass == existing.PriorityClass && state.AffinityMask == existing.AffinityMask)
          CoreKeepStatusIcon.Text = Strings.CoreKeepStatusMatched;
        else
          CoreKeepStatusIcon.Text = Strings.CoreKeepStatusMismatch;
      } else {
        CoreKeepStatusIcon.Text = "";
      }
    }

    void CoreKeepSave_Click(object sender, RoutedEventArgs e) {
      // ponytail: 用当前进程值更新保存的规则。支持 PID
      string raw = CoreKeepProcInput.Text?.Trim();
      if (string.IsNullOrEmpty(raw)) return;
      var data = CoreKeepService.Load();
      bool isPid = int.TryParse(raw, out int pid);
      CoreKeepEntry entry;
      if (isPid) {
        entry = CoreKeepService.CaptureFromPid(pid);
        if (entry.AffinityMask == 0 && entry.PriorityClass == 0) return;
        var existingPid = data.Entries.Find(x => x.ProcessId == pid);
        if (existingPid != null) {
          existingPid.PriorityClass = entry.PriorityClass;
          existingPid.AffinityMask = entry.AffinityMask;
          existingPid.CapturedAt = entry.CapturedAt;
          existingPid.ProcessName = entry.ProcessName; // 更新名称（可能变了）
          CoreKeepService.Save(data);
          RefreshCoreKeepList(data);
          ReselectByName(existingPid.ProcessName);
        }
      } else {
        string procName = raw.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? raw : raw + ".exe";
        entry = CoreKeepService.CaptureFromProcess(procName);
        var existing = data.Entries.Find(x => x.ProcessName.Equals(procName, StringComparison.OrdinalIgnoreCase));
        if (existing != null) {
          existing.PriorityClass = entry.PriorityClass;
          existing.AffinityMask = entry.AffinityMask;
          existing.CapturedAt = entry.CapturedAt;
          CoreKeepService.Save(data);
          RefreshCoreKeepList(data);
          ReselectByName(procName);
        }
      }
    }

    void ReselectByName(string name) {
      for (int i = 0; i < CoreKeepList.Items.Count; i++) {
        if ((CoreKeepList.Items[i] as CoreKeepEntry)?.ProcessName == name) {
          CoreKeepList.SelectedIndex = i; break;
        }
      }
    }

    void CoreKeepDelete_Click(object sender, RoutedEventArgs e) {
      var selected = CoreKeepList.SelectedItem as CoreKeepEntry;
      if (selected == null) return;
      var data = CoreKeepService.Load();
      data.Entries.RemoveAll(x => x.ProcessName == selected.ProcessName);
      CoreKeepService.Save(data);
      _currentSelectedEntry = null;
      CoreKeepProcInput.Text = "";
      CoreKeepPriorityText.Text = " -";
      CoreKeepAffinityText.Text = " -";
      CoreKeepStatusIcon.Text = "";
      CoreKeepLivePriorityText.Text = "";
      RefreshCoreKeepList(data);
    }

    /// <summary>添加进程：支持 PID（纯数字）或进程名</summary>
    void CoreKeepAdd_Click(object sender, RoutedEventArgs e) {
      string raw = CoreKeepProcInput.Text?.Trim();
      if (string.IsNullOrEmpty(raw)) return;
      var data = CoreKeepService.Load();
      CoreKeepEntry entry;
      bool isPid = int.TryParse(raw, out int pid);
      if (isPid) {
        entry = CoreKeepService.CaptureFromPid(pid);
        if (entry.AffinityMask == 0 && entry.PriorityClass == 0) return;
        if (data.Entries.Exists(x => x.ProcessId == pid)) return;
      } else {
        string procName = raw.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? raw : raw + ".exe";
        if (data.Entries.Exists(x => x.ProcessName.Equals(procName, StringComparison.OrdinalIgnoreCase))) return;
        entry = CoreKeepService.CaptureFromProcess(procName);
        if (entry.AffinityMask == 0 && entry.PriorityClass == 0) return;
      }
      entry.Enabled = true;
      entry.CoreMode = "All";
      entry.GuardEnabled = true;
      CoreKeepService.ApplyModeToEntry(entry, "All");
      if (CoreKeepPriorityCombo.SelectedItem is System.Windows.Controls.ComboBoxItem pi && pi.Tag is uint prio)
        entry.PriorityClass = prio;
      data.Entries.Add(entry);
      CoreKeepService.Save(data);
      CoreKeepProcInput.Text = "";
      RefreshCoreKeepList(data);
      if (data.MasterEnabled) CoreKeepService.StartAutoApply(data);
    }

    void CoreKeepPriorityCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) {
      if (_currentSelectedEntry == null || CoreKeepPriorityCombo.SelectedItem == null) return;
      if (CoreKeepPriorityCombo.SelectedItem is System.Windows.Controls.ComboBoxItem item && item.Tag is uint prio) {
        // ponytail: 值没变就跳过，防止 SelectPriorityInCombo 触发重入
        if (_currentSelectedEntry.PriorityClass == prio) return;
        _currentSelectedEntry.PriorityClass = prio;
        CoreKeepPriorityText.Text = CoreKeepService.PriorityClassName(prio);
        // 持久化
        var data = CoreKeepService.Load();
        var existing = _currentSelectedEntry.ProcessId > 0
          ? data.Entries.Find(x => x.ProcessId == _currentSelectedEntry.ProcessId)
          : data.Entries.Find(x => x.ProcessName == _currentSelectedEntry.ProcessName);
        if (existing != null) {
          existing.PriorityClass = prio;
          CoreKeepService.Save(data);
          // ponytail: 不 RefreshCoreKeepList —— 不替换 ItemsSource 避免清空 ListBox 选中
        }
        // 立即写入进程
        if (CoreKeepMasterToggle.IsChecked == true)
          CoreKeepService.ApplyToProcess(_currentSelectedEntry.ProcessName, _currentSelectedEntry);
      }
    }

    void CoreKeepMode_Click(object sender, RoutedEventArgs e) {
      var btn = sender as Wpf.Ui.Controls.Button;
      if (btn == null || _currentSelectedEntry == null) return;
      string mode = btn.Tag as string;
      if (string.IsNullOrEmpty(mode)) return;
      HighlightModeButton(mode);
      _currentSelectedEntry.CoreMode = mode;
      // 手动模式：显示核心选择
      CoreKeepCoreList.Visibility = (mode == "Manual") ? Visibility.Visible : Visibility.Collapsed;
      if (mode == "Manual") {
        PopulateCoreCheckboxes(_currentSelectedEntry.PreferredCores, _currentSelectedEntry.AffinityMask);
        var data = CoreKeepService.Load();
        var existing = _currentSelectedEntry.ProcessId > 0
          ? data.Entries.Find(x => x.ProcessId == _currentSelectedEntry.ProcessId)
          : data.Entries.Find(x => x.ProcessName == _currentSelectedEntry.ProcessName);
        if (existing != null) { existing.CoreMode = mode; CoreKeepService.Save(data); RefreshCoreKeepList(data); }
      } else {
        long mask = CoreKeepService.ModeToAffinityMask(mode, null);
        _currentSelectedEntry.AffinityMask = mask;
        var data = CoreKeepService.Load();
        var existing = _currentSelectedEntry.ProcessId > 0
          ? data.Entries.Find(x => x.ProcessId == _currentSelectedEntry.ProcessId)
          : data.Entries.Find(x => x.ProcessName == _currentSelectedEntry.ProcessName);
        if (existing != null) { existing.CoreMode = mode; existing.AffinityMask = mask; CoreKeepService.Save(data); RefreshCoreKeepList(data); }
        CoreKeepAffinityText.Text = "0x" + mask.ToString("X");
        if (CoreKeepMasterToggle.IsChecked == true)
          CoreKeepService.ApplyToProcess(_currentSelectedEntry.ProcessName, _currentSelectedEntry);
      }
    }

    void CoreKeepGuardToggle_Changed(object sender, RoutedEventArgs e) {
      bool on = CoreKeepGuardToggle.IsChecked == true;
      CoreKeepGuardInterval.IsEnabled = on;
      var data = CoreKeepService.Load();
      data.GuardIntervalMs = on ? (int)(CoreKeepGuardInterval.Value * 1000) : -1;
      CoreKeepService.Save(data);
      if (on) {
        CoreKeepService.UpdateGuardInterval(data.GuardIntervalMs);
        // 同时给所有条目启用守护
        foreach (var entry in data.Entries) entry.GuardEnabled = true;
        CoreKeepService.Save(data);
      } else {
        foreach (var entry in data.Entries) entry.GuardEnabled = false;
        CoreKeepService.Save(data);
        CoreKeepService.StopAutoApply();
        if (CoreKeepMasterToggle.IsChecked == true)
          CoreKeepService.StartAutoApply(data); // 重启不带守护的自动应用
      }
    }

    void CoreKeepGuardInterval_Changed(object s, RoutedEventArgs e) {
      var data = CoreKeepService.Load();
      data.GuardIntervalMs = (int)(CoreKeepGuardInterval.Value * 1000);
      CoreKeepService.Save(data);
      CoreKeepService.UpdateGuardInterval(data.GuardIntervalMs);
    }

    void CoreKeepBenchmark_Click(object sender, RoutedEventArgs e) {
      CoreKeepBenchBtn.Content = Strings.CoreKeepBenchmarkRunning;
      CoreKeepBenchBtn.IsEnabled = false;
      System.Threading.ThreadPool.QueueUserWorkItem(_ => {
        try {
          var results = CoreKeepService.RunBenchmark(500);
          Dispatcher.BeginInvoke(new Action(() => {
            CoreKeepBenchBtn.Content = Strings.CoreKeepBenchmarkDone;
            CoreKeepBenchBtn.IsEnabled = true;
            // 显示建议
            var best = results.OrderBy(r => r.Score).Take(8).ToList();
            string msg = string.Join("\n", best.Select(r =>
              string.Format(Strings.CoreKeepBenchmarkResult, r.CoreIndex, r.Score, r.Relative)));
            DialogHelper.Info(msg, Strings.CoreKeepBenchmark);
          }));
        } catch {
          Dispatcher.BeginInvoke(new Action(() => {
            CoreKeepBenchBtn.Content = Strings.CoreKeepBenchmark;
            CoreKeepBenchBtn.IsEnabled = true;
          }));
        }
      });
    }

    void RefreshCoreKeepList(CoreKeepData data) {
      // ponytail: 保存并恢复选中条目，避免替换 ItemsSource 清空选中
      string selectedName = _currentSelectedEntry?.ProcessName;
      CoreKeepList.ItemsSource = data.Entries;
      if (selectedName != null) {
        for (int i = 0; i < CoreKeepList.Items.Count; i++) {
          if ((CoreKeepList.Items[i] as CoreKeepEntry)?.ProcessName == selectedName) {
            CoreKeepList.SelectedIndex = i;
            break;
          }
        }
      }
    }

    void CoreKeepCoreCheckChanged(object sender, RoutedEventArgs e) {
      if (_currentSelectedEntry == null || _currentSelectedEntry.CoreMode != "Manual") return;
      // 收集勾选的核心，更新掩码
      var selected = new List<int>();
      foreach (var item in CoreKeepCoreList.Items) {
        var ci = item as CoreCheckItem;
        if (ci != null && ci.IsChecked) selected.Add(ci.CoreIndex);
      }
      if (selected.Count == 0) return;
      long mask = CoreKeepService.ModeToAffinityMask("Manual", selected.ToArray());
      _currentSelectedEntry.AffinityMask = mask;
      _currentSelectedEntry.PreferredCores = selected.ToArray();
      // 持久化
      var data = CoreKeepService.Load();
      var existing = _currentSelectedEntry.ProcessId > 0
        ? data.Entries.Find(x => x.ProcessId == _currentSelectedEntry.ProcessId)
        : data.Entries.Find(x => x.ProcessName == _currentSelectedEntry.ProcessName);
      if (existing != null) {
        existing.AffinityMask = mask;
        existing.PreferredCores = selected.ToArray();
        CoreKeepService.Save(data);
        RefreshCoreKeepList(data);
      }
      CoreKeepAffinityText.Text = "0x" + mask.ToString("X");
      if (CoreKeepMasterToggle.IsChecked == true)
        CoreKeepService.ApplyToProcess(_currentSelectedEntry.ProcessName, _currentSelectedEntry);
    }

    // ── GPU 频率限制 ──
    void GpuClock_SelectionChanged(object sender, SelectionChangedEventArgs e) {
      if (_loading) return;
      _loading = true;
      var item = GpuClockCombo.SelectedItem as ComboBoxItem;
      if (item == null) { _loading = false; return; }
      int val = (int)item.Tag;
      ConfigService.GpuClock = val;
      GpuClockSlider.Value = val;
      if (val > 0) TrayService.SetGPUClockLimit(val);
      ConfigService.Save("GpuClock");
      _loading = false;
    }

    void GpuClockNum_ValueChanged(object s, RoutedEventArgs e) {
      if (_loading) return;
      _loading = true;
      double? val = GpuClockNum.Value;
      if (val == null || val < 0 || val > 2500) { _loading = false; return; }
      int v = (int)val;
      if (v > 0) TrayService.SetGPUClockLimit(v);
      ConfigService.GpuClock = v; ConfigService.Save("GpuClock");
      SelectCombo(GpuClockCombo, v == 0 ? Strings.GpuClockRestore : v + " MHz");
      _loading = false;
    }

    // ── GPU 核心超频 ──
    void GpuCoreOC_SelectionChanged(object sender, SelectionChangedEventArgs e) {
      if (_loading) return;
      _loading = true;
      var item = GpuCoreOCCombo.SelectedItem as ComboBoxItem;
      if (item == null) { _loading = false; return; }
      int val = (int)item.Tag;
      ConfigService.GpuCoreOverclock = val;
      GpuCoreOCSlider.Value = val;
      System.Threading.ThreadPool.QueueUserWorkItem(_ => GpuAppManager.SetCoreClockOffset(val));
      ConfigService.Save("GpuCoreOverclock");
      _loading = false;
    }

    void GpuCoreOCNum_ValueChanged(object s, RoutedEventArgs e) {
      if (_loading) return;
      _loading = true;
      double? val = GpuCoreOCNum.Value;
      if (val == null || val < -270 || val > 270) { _loading = false; return; }
      int v = (int)val;
      ConfigService.GpuCoreOverclock = v;
      System.Threading.ThreadPool.QueueUserWorkItem(_ => GpuAppManager.SetCoreClockOffset(v));
      ConfigService.Save("GpuCoreOverclock");
      SelectCombo(GpuCoreOCCombo, (v >= 0 ? "+" : "") + v + " MHz");
      _loading = false;
    }

    // ── GPU 显存超频 ──
    void GpuMemoryOC_SelectionChanged(object sender, SelectionChangedEventArgs e) {
      if (_loading) return;
      _loading = true;
      var item = GpuMemoryOCCombo.SelectedItem as ComboBoxItem;
      if (item == null) { _loading = false; return; }
      int val = (int)item.Tag;
      ConfigService.GpuMemoryOverclock = val;
      GpuMemoryOCSlider.Value = val;
      System.Threading.ThreadPool.QueueUserWorkItem(_ => GpuAppManager.SetMemoryClockOffset(val));
      ConfigService.Save("GpuMemoryOverclock");
      _loading = false;
    }

    void GpuMemoryOCNum_ValueChanged(object s, RoutedEventArgs e) {
      if (_loading) return;
      _loading = true;
      double? val = GpuMemoryOCNum.Value;
      if (val == null || val < 0 || val > 2000) { _loading = false; return; }
      int v = (int)val;
      ConfigService.GpuMemoryOverclock = v;
      System.Threading.ThreadPool.QueueUserWorkItem(_ => GpuAppManager.SetMemoryClockOffset(v));
      ConfigService.Save("GpuMemoryOverclock");
      SelectCombo(GpuMemoryOCCombo, "+" + v + " MHz");
      _loading = false;
    }

    // ── 图形模式 ──
    void GfxMode_SelectionChanged(object s, SelectionChangedEventArgs e) {
      if (_loading) return;
      int mode = GfxModeCombo.SelectedIndex;
      if (mode == 3) {
        if (!DialogHelper.Confirm(Strings.GfxUMAConfirm, Strings.GfxUMATitle)) { LoadState(); return; }
      }
      if (mode >= 0 && SetGfxMode(mode)) {
        GetGfxMode(out int current);
        if (current == mode) {
          DialogHelper.Info(Strings.GfxSwitchedTo(
              mode == 0 ? "NVIDIA Advanced Optimus" :
              mode == 1 ? Strings.GfxDiscreteMode :
              mode == 2 ? Strings.GfxHybridMode : Strings.GfxUMALabel), Strings.Hint);
        } else {
          DialogHelper.Info(Strings.GfxSwitchedTo(
              mode == 0 ? "NVIDIA Advanced Optimus" :
              mode == 1 ? Strings.GfxDiscreteMode :
              mode == 2 ? Strings.GfxHybridMode : Strings.GfxUMALabel) +
              "\n" + Strings.PerfGfxReboot, Strings.Hint);
        }
      }
    }

    // ── 热切换 ──
    void UpdateHotSwitchVisibility(int mode = -1) {
      try {
        if (mode < 0) GetGfxMode(out mode);
        HotSwitchCard.Visibility = (mode == 0 || mode == 2) ? Visibility.Visible : Visibility.Collapsed;
      } catch { HotSwitchCard.Visibility = Visibility.Collapsed; }
    }

    void HotSwitch_Click(object sender, RoutedEventArgs e) {
      int result = LaunchDDS();
      if (result != 0)
        DialogHelper.Warn(Strings.DdsInitFail, Strings.Error);
    }

    // ── DB 版本 ──
    void DbVersion_SelectionChanged(object s, SelectionChangedEventArgs e) {
      if (_loading) return;
      if (!HardwareService.PowerOnline) {
        DialogHelper.Warn(Strings.PleaseConnectAC, Strings.Hint);
        LoadState(); return;
      }
      if (DbVersionCombo.SelectedIndex == 0) {
        if (!TrayService.CheckDBVersion(1)) {
          DialogHelper.Warn(Strings.DriverNotAllow + "\n" + Strings.DriverVersionRange, Strings.Error);
          LoadState(); return;
        }
        if (!DialogHelper.Confirm(Strings.PerfDbUnlockWarning, Strings.DbUnlockTitle)) { LoadState(); return; }
        ConfigService.DBVersion = 1; ConfigService.Save("DBVersion");
        TrayService.ChangeDBVersion(1);
      } else {
        ConfigService.DBVersion = 2; ConfigService.Save("DBVersion");
        TrayService.ChangeDBVersion(2);
      }
    }

    // ── TGP / PPAB ──
    void TppNum_ValueChanged(object s, RoutedEventArgs e) {
      if (_loading) return;
      _loading = true;
      double? val = TppNum.Value;
      if (val == null || val < 0 || val > 255) { _loading = false; return; }
      int v = (int)val;
      SetConcurrentTdp((byte)v);
      ConfigService.Tpp = v; ConfigService.Save("Tpp");
      if (v == 0) { PpabCheck.IsChecked = false; }
      UpdateTppEnabled();
      UpdateTgpStatus();
      _loading = false;
    }

    void UpdateTppEnabled() {
      bool tgpOn = ConfigService.TgpEnabled;
      bool ppabOn = PpabCheck.IsChecked == true;
      bool ppabAllowed = tgpOn && ConfigService.Tpp > 0;
      PpabCheck.IsEnabled = tgpOn;
      TppNum.IsEnabled = tgpOn && ppabOn;
      TppExtraSlider.IsEnabled = tgpOn && ppabOn;
    }

    void UpdateTgpStatus() {
      bool tgp = ConfigService.TgpEnabled;
      bool ppab = ConfigService.PpabEnabled && tgp;
      int dstate = ConfigService.DState;
      string tpp = ConfigService.Tpp > 0 ? $", TPP={ConfigService.Tpp}W" : "";
      TgpStatus.Text = $"TGP={(tgp ? "开" : "关")}, PPAB={(ppab ? "开" : "关")}, dState={(dstate == 2 ? "低功耗" : "标准")}{tpp}";
    }

    void CtgpCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) {
      if (_loading) return;
      var item = CtgpCombo.SelectedItem as ComboBoxItem;
      if (item == null) return;
      bool enabled = (bool)item.Tag;
      ConfigService.TgpEnabled = enabled;
      if (!enabled) PpabCheck.IsChecked = false;
      ConfigService.Save("TgpEnabled");
      SetGpuPowerState(enabled, PpabCheck.IsChecked == true, ConfigService.DState == 2 ? 2 : 1);
      UpdateTppEnabled();
      UpdateTgpStatus();
    }

    void Ppab_Changed(object sender, RoutedEventArgs e) {
      bool enabled = PpabCheck.IsChecked == true;
      ConfigService.PpabEnabled = enabled;
      ConfigService.Save("PpabEnabled");
      SetGpuPowerState(ConfigService.TgpEnabled, enabled, ConfigService.DState == 2 ? 2 : 1);
      UpdateTppEnabled();
      UpdateTgpStatus();
    }

    // ── dState ──
    void DState_SelectionChanged(object s, SelectionChangedEventArgs e) {
      if (_loading) return;
      ConfigService.DState = DStateCombo.SelectedIndex == 1 ? 2 : 1;
      ConfigService.Save("DState");
      SetGpuPowerState(ConfigService.TgpEnabled, ConfigService.PpabEnabled, ConfigService.DState);
      UpdateTgpStatus();
    }

    // ── 最大帧率 ──
    void Fps_SelectionChanged(object sender, SelectionChangedEventArgs e) {
      if (_loading) return;
      _loading = true;
      try {
        var item = FpsCombo.SelectedItem as ComboBoxItem;
        if (item == null) return;
        int val = (int)item.Tag;
        int configVal = val == 0 ? -1 : val;
        if (configVal == ConfigService.MaxFrameRate) return;
        FpsSlider.Value = val;
        ConfigService.MaxFrameRate = configVal;
        if (HasNvidiaGpu()) HP.Omen.Core.Common.NVidiaApi.NvApiWrapper.NVAPI_SetMaxFrameRate(val);
        ConfigService.Save("MaxFrameRate");
      } finally { _loading = false; }
    }

    void FpsNum_ValueChanged(object s, RoutedEventArgs e) {
      if (_loading) return;
      _loading = true;
      try {
        double? val = FpsNum.Value;
        if (val == null || val < 0) return;
        int v = (int)val;
        int[] presets = { 0, 30, 60, 90, 120, 144, 165, 240, 300, 360, 480, 1000 };
        int best = presets[0];
        foreach (int p in presets) { if (Math.Abs(p - v) < Math.Abs(best - v)) best = p; }
        if (best != v) { FpsNum.Value = best; FpsSlider.Value = best; v = best; }
        int configVal = v == 0 ? -1 : v;
        if (configVal == ConfigService.MaxFrameRate) return;
        ConfigService.MaxFrameRate = configVal;
        if (HasNvidiaGpu()) HP.Omen.Core.Common.NVidiaApi.NvApiWrapper.NVAPI_SetMaxFrameRate(v);
        ConfigService.Save("MaxFrameRate");
        SelectCombo(FpsCombo, v == 0 ? Strings.Unlimited : v + " FPS");
      } finally { _loading = false; }
    }

    // ── 屏幕刷新率 ──
    void BuildRefreshRateOptions() {
      RefreshRateCombo.Items.Clear();
      var rates = GetAvailableRefreshRates();
      foreach (int r in rates)
        RefreshRateCombo.Items.Add(new ComboBoxItem { Content = r + " Hz", Tag = r });
      // Select saved refresh rate if set, otherwise default to max available
      int targetHz = ConfigService.RefreshRate > 0 ? ConfigService.RefreshRate : (rates.Count > 0 ? rates.Max() : 60);
      SelectCombo(RefreshRateCombo, targetHz + " Hz");
      if (RefreshRateCombo.SelectedIndex < 0 && rates.Count > 0)
        RefreshRateCombo.SelectedIndex = rates.Count - 1;
      RefreshRateSlider.Value = (int)((RefreshRateCombo.SelectedItem as ComboBoxItem)?.Tag ?? targetHz);
    }

    void RefreshRate_SelectionChanged(object sender, SelectionChangedEventArgs e) {
      if (_loading) return;
      _loading = true;
      try {
        var item = RefreshRateCombo.SelectedItem as ComboBoxItem;
        if (item == null) return;
        int val = (int)item.Tag;
        if (val == ConfigService.RefreshRate) return;
        RefreshRateSlider.Value = val;
        ConfigService.RefreshRate = val;
        ApplyRefreshRate(val);
        ConfigService.Save("RefreshRate");
      } finally { _loading = false; }
    }

    void RefreshRateNum_ValueChanged(object s, RoutedEventArgs e) {
      if (_loading) return;
      _loading = true;
      try {
        double? val = RefreshRateNum.Value;
        if (val == null || val < 30 || val > 360) return;
        int v = (int)val;
        if (v == ConfigService.RefreshRate) return;
        ConfigService.RefreshRate = v;
        ApplyRefreshRate(v);
        ConfigService.Save("RefreshRate");
        SelectCombo(RefreshRateCombo, v + " Hz");
      } finally { _loading = false; }
    }

    static int GetCurrentRefreshRate() => GetAvailableRefreshRates().DefaultIfEmpty(60).Max();

    static List<int> _cachedRefreshRates;
    static List<int> GetAvailableRefreshRates() {
      if (_cachedRefreshRates != null) return _cachedRefreshRates;
      var seen = new HashSet<int>();
      var rates = new List<int>();
      var deviceName = GetInternalDisplayDeviceName();
      if (deviceName == null) return rates;
      var dm = new NativeMethods_Display.DEVMODE();
      dm.dmSize = (short)Marshal.SizeOf(typeof(NativeMethods_Display.DEVMODE));
      int mode = 0;
      while (NativeMethods_Display.EnumDisplaySettings(deviceName, mode, ref dm)) {
        if (dm.dmPelsWidth == 0 || dm.dmPelsHeight == 0) { mode++; continue; }
        if (seen.Add(dm.dmDisplayFrequency)) rates.Add(dm.dmDisplayFrequency);
        mode++;
      }
      rates.Sort();
      _cachedRefreshRates = rates;
      return rates;
    }

    static void ApplyRefreshRate(int hz) {
      var deviceName = GetInternalDisplayDeviceName();
      if (deviceName == null) { Log("ApplyRefreshRate: deviceName is null"); return; }
      Log($"ApplyRefreshRate: deviceName={deviceName} target={hz}Hz");
      var dm = new NativeMethods_Display.DEVMODE();
      dm.dmSize = (short)Marshal.SizeOf(typeof(NativeMethods_Display.DEVMODE));
      if (!NativeMethods_Display.EnumDisplaySettings(deviceName, NativeMethods_Display.ENUM_CURRENT_SETTINGS, ref dm)) { Log("ApplyRefreshRate: EnumDisplaySettings(ENUM_CURRENT_SETTINGS) failed"); return; }
      if (dm.dmDisplayFrequency == hz) { Log("ApplyRefreshRate: same freq, skipping"); return; }
      dm.dmDisplayFrequency = hz;
      dm.dmFields = NativeMethods_Display.DM_DISPLAYFREQUENCY;
      int result = NativeMethods_Display.ChangeDisplaySettingsEx(deviceName, ref dm, IntPtr.Zero, 0, IntPtr.Zero);
      Log($"ApplyRefreshRate: ChangeDisplaySettingsEx returned {result}");
    }

    // ── 查找内置显示器 ──
    static (NativeMethods_Display.LUID adapterId, uint sourceId, uint targetId) _cachedIds = (default, uint.MaxValue, uint.MaxValue);
    static (NativeMethods_Display.LUID adapterId, uint sourceId, uint targetId) FindInternalDisplayIds() {
      if (_cachedIds.sourceId != uint.MaxValue) return _cachedIds;
      uint[] flagsToTry = { NativeMethods_Display.QDC_ALL_PATHS, NativeMethods_Display.QDC_ONLY_ACTIVE_PATHS, NativeMethods_Display.QDC_DATABASE_CURRENT };
      (NativeMethods_Display.LUID, uint, uint)? firstActive = null;
      foreach (uint flag in flagsToTry) {
        uint pathCount, modeCount;
        if (NativeMethods_Display.GetDisplayConfigBufferSizes(flag, out pathCount, out modeCount) != 0) { Log($"flag={flag}: GetDisplayConfigBufferSizes failed"); continue; }
        if (pathCount == 0) { Log($"flag={flag}: no paths"); continue; }
        var paths = new NativeMethods_Display.DISPLAYCONFIG_PATH_INFO[pathCount];
        var modes = new NativeMethods_Display.DISPLAYCONFIG_MODE_INFO[modeCount];
        int qdcRet = NativeMethods_Display.QueryDisplayConfig(flag, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
        if (qdcRet != 0) {
          int gle = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
          Log($"flag={flag}: QDC failed: {qdcRet} gle={gle} (input pathCount={pathCount} modeCount={modeCount})");
          continue;
        }
        Log($"flag={flag}: QDC OK, {pathCount} paths, {modeCount} modes");
        for (int i = 0; i < pathCount; i++) {
          if ((paths[i].flags & NativeMethods_Display.DISPLAYCONFIG_PATH_ACTIVE) == 0) continue;
          var tgt = new NativeMethods_Display.DISPLAYCONFIG_TARGET_DEVICE_NAME();
          tgt.header.type = NativeMethods_Display.INFO_GET_TARGET_NAME;
          tgt.header.size = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods_Display.DISPLAYCONFIG_TARGET_DEVICE_NAME));
          tgt.header.adapterId = paths[i].targetInfo.adapterId;
          tgt.header.id = paths[i].targetInfo.id;
          // Remember first active path (even if external, even if GetDeviceInfo fails) as fallback
          if (firstActive == null)
            firstActive = (paths[i].sourceInfo.adapterId, paths[i].sourceInfo.id, paths[i].targetInfo.id);
          Log($"GetDeviceName(adapter={paths[i].targetInfo.adapterId.LowPart:X8},{paths[i].targetInfo.adapterId.HighPart:X8} targetId={paths[i].targetInfo.id} sourceId={paths[i].sourceInfo.id})");
          int ret = NativeMethods_Display.DisplayConfigGetDeviceInfo(ref tgt);
          if (ret != 0) { Log($"DisplayConfigGetDeviceInfo(path {i}) failed: {ret}"); continue; }
          bool isInternal = tgt.outputTechnology == NativeMethods_Display.DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.INTERNAL ||
                            tgt.outputTechnology == NativeMethods_Display.DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYPORT_EMBEDDED;
          Log($"path {i}: flag={flag} tech={tgt.outputTechnology} name={tgt.monitorFriendlyDeviceName} internal={isInternal}");
          if (isInternal) {
            _cachedIds = (paths[i].sourceInfo.adapterId, paths[i].sourceInfo.id, paths[i].targetInfo.id);
            return _cachedIds;
          }
        }
      }
      // No internal display found — use the first active path (external monitor) if available
      if (firstActive != null) {
        Log($"FindInternalDisplayIds: no internal display, using first active path (external)");
        _cachedIds = firstActive.Value;
        return _cachedIds;
      }
      _cachedIds = (default, 0, 0); // cache failure too
      // ── Fallback: Screen.AllScreens ──
      Log($"Screen.AllScreens: {System.Windows.Forms.Screen.AllScreens.Length} screens");
      foreach (var sc in System.Windows.Forms.Screen.AllScreens) {
        Log($"  Screen: '{sc.DeviceName}' primary={sc.Primary} bounds=({sc.Bounds.Width},{sc.Bounds.Height}) working=({sc.WorkingArea.Width},{sc.WorkingArea.Height})");
      }
      // ── Fallback: EnumDisplayDevices ──
      Log("EnumDisplayDevices enumeration:");
      for (uint dev = 0; ; dev++) {
        var dd = new NativeMethods_Display.DISPLAY_DEVICE();
        dd.cb = System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods_Display.DISPLAY_DEVICE));
        if (!NativeMethods_Display.EnumDisplayDevices(null, dev, ref dd, 0)) break;
        Log($"  ADAPTER[{dev}]: name='{dd.DeviceName}' str='{dd.DeviceString}' flags={dd.StateFlags:X}");
        for (uint mon = 0; ; mon++) {
          var md = new NativeMethods_Display.DISPLAY_DEVICE();
          md.cb = System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods_Display.DISPLAY_DEVICE));
          if (!NativeMethods_Display.EnumDisplayDevices(dd.DeviceName, mon, ref md, 0)) break;
          Log($"    MONITOR[{mon}]: name='{md.DeviceName}' str='{md.DeviceString}' flags={md.StateFlags:X}");
          // Check if this monitor is the built-in panel
          string monStr = md.DeviceString ?? "";
          // Common internal display identifiers in DeviceString
          if (monStr.IndexOf("LCD", StringComparison.OrdinalIgnoreCase) >= 0 ||
              monStr.IndexOf("eDP", StringComparison.OrdinalIgnoreCase) >= 0 ||
              monStr.IndexOf("Embedded", StringComparison.OrdinalIgnoreCase) >= 0 ||
              monStr.IndexOf("Built-in", StringComparison.OrdinalIgnoreCase) >= 0 ||
              monStr.IndexOf("Internal", StringComparison.OrdinalIgnoreCase) >= 0 ||
              monStr.IndexOf("Laptop", StringComparison.OrdinalIgnoreCase) >= 0) {
            Log($"    → matched as internal display via DeviceString '{monStr}'");
            _cachedDeviceName = md.DeviceName;
            return (default, 0, 0);
          }
          // Also check the adapter DeviceString for eDP (common for laptop panels)
          string adStr = dd.DeviceString ?? "";
          if (mon == 0 && adStr.IndexOf("eDP", StringComparison.OrdinalIgnoreCase) >= 0) {
            Log($"    → matched as internal display via adapter DeviceString '{adStr}'");
            _cachedDeviceName = md.DeviceName;
            return (default, 0, 0);
          }
        }
      }
      // ── Last resort: Screen.PrimaryScreen ──
      Log("FindInternalDisplayIds: all fallbacks failed, will use PrimaryScreen");
      _cachedDeviceName = System.Windows.Forms.Screen.PrimaryScreen?.DeviceName;
      return (default, 0, 0);
    }

    // ── 屏幕分辨率 ──
    static string _cachedDeviceName;
    static string GetInternalDisplayDeviceName() {
      if (_cachedDeviceName != null) return _cachedDeviceName;
      var ids = FindInternalDisplayIds();
      if (_cachedDeviceName != null) return _cachedDeviceName; // EnumDisplayDevices fallback may have set it
      if (ids.adapterId.LowPart == 0 && ids.adapterId.HighPart == 0 && ids.sourceId == 0) {
        string fallback = System.Windows.Forms.Screen.PrimaryScreen?.DeviceName;
        Log($"GetInternalDisplayDeviceName: no source ID, fallback to PrimaryScreen: {fallback}");
        _cachedDeviceName = fallback;
        return fallback;
      }
      var info = new NativeMethods_Display.DISPLAYCONFIG_SOURCE_DEVICE_NAME();
      info.header.type = NativeMethods_Display.INFO_GET_SOURCE_NAME;
      info.header.size = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods_Display.DISPLAYCONFIG_SOURCE_DEVICE_NAME));
      info.header.adapterId = ids.adapterId;
      info.header.id = ids.sourceId;
      int ret = NativeMethods_Display.DisplayConfigGetDeviceInfoEx(ref info);
      if (ret != 0) {
        string fallback = System.Windows.Forms.Screen.PrimaryScreen?.DeviceName;
        Log($"GetInternalDisplayDeviceName: DisplayConfigGetDeviceInfoEx failed: {ret}, fallback: {fallback}");
        return fallback;
      }
      _cachedDeviceName = info.viewGdiDeviceName;
      Log($"GetInternalDisplayDeviceName (from DisplayConfig): {_cachedDeviceName} ids=({ids.adapterId.LowPart:X8},{ids.adapterId.HighPart:X8}:{ids.sourceId})");
      return _cachedDeviceName;
    }

    static List<(int w, int h)> GetAvailableResolutions() {
      var deviceName = GetInternalDisplayDeviceName();
      if (deviceName == null) return new List<(int w, int h)>();
      uint curFreq = (uint)GetCurrentRefreshRate();
      var seen = new HashSet<string>();
      var result = new List<(int w, int h)>();
      var dm = new NativeMethods_Display.DEVMODE();
      dm.dmSize = (short)System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods_Display.DEVMODE));
      int modeIdx = 0;
      while (NativeMethods_Display.EnumDisplaySettings(deviceName, modeIdx, ref dm)) {
        if (dm.dmPelsWidth > 0 && dm.dmPelsHeight > 0 && dm.dmDisplayFrequency == curFreq) {
          int mx = Math.Max(dm.dmPelsWidth, dm.dmPelsHeight);
          // ponytail: filter tiny resolutions (< 1000px on the long edge)
          if (mx < 1000) { modeIdx++; continue; }
          string key = dm.dmPelsWidth + "x" + dm.dmPelsHeight;
          if (seen.Add(key)) result.Add((dm.dmPelsWidth, dm.dmPelsHeight));
        }
        modeIdx++;
      }
      result.Sort((a, b) => b.w.CompareTo(a.w));
      return result;
    }

    // ponytail: same pattern as ApplyRefreshRate — start from current DEVMODE, change only resolution
    static void ApplyResolution(int w, int h) {
      var deviceName = GetInternalDisplayDeviceName();
      if (deviceName == null) { Log("ApplyResolution: deviceName is null"); return; }
      Log($"ApplyResolution: deviceName={deviceName} target={w}x{h}");
      var dm = new NativeMethods_Display.DEVMODE();
      dm.dmSize = (short)System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods_Display.DEVMODE));
      if (!NativeMethods_Display.EnumDisplaySettings(deviceName, NativeMethods_Display.ENUM_CURRENT_SETTINGS, ref dm))
      { Log("ApplyResolution: EnumDisplaySettings(ENUM_CURRENT_SETTINGS) failed"); return; }
      dm.dmPelsWidth = w;
      dm.dmPelsHeight = h;
      dm.dmFields = NativeMethods_Display.DM_PELSWIDTH | NativeMethods_Display.DM_PELSHEIGHT;
      int result = NativeMethods_Display.ChangeDisplaySettingsEx(deviceName, ref dm, IntPtr.Zero, 0, IntPtr.Zero);
      Log($"ApplyResolution: ChangeDisplaySettingsEx returned {result} (0=OK)");
    }

    void BuildResolutionOptions() {
      ResolutionCombo.Items.Clear();
      // Confirm, then wipe registry/tasks/config/data and restart self.
            var res = GetAvailableResolutions();
      foreach (var r in res)
        ResolutionCombo.Items.Add(new ComboBoxItem { Content = $"{r.w} × {r.h}", Tag = r.w + "x" + r.h });
      if (!string.IsNullOrEmpty(ConfigService.Resolution)) {
        var parts = ConfigService.Resolution.Split('x');
        if (parts.Length == 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h))
          SelectCombo(ResolutionCombo, $"{w} × {h}");
      }
      if (ResolutionCombo.SelectedIndex < 0) {
        var deviceName = GetInternalDisplayDeviceName();
        if (deviceName != null) {
          var dm = new NativeMethods_Display.DEVMODE();
          dm.dmSize = (short)System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods_Display.DEVMODE));
          if (NativeMethods_Display.EnumDisplaySettings(deviceName, NativeMethods_Display.ENUM_CURRENT_SETTINGS, ref dm))
            SelectCombo(ResolutionCombo, $"{dm.dmPelsWidth} × {dm.dmPelsHeight}");
        }
      }
    }

    void Resolution_SelectionChanged(object sender, SelectionChangedEventArgs e) {
      if (_loading) return;
      var item = ResolutionCombo.SelectedItem as ComboBoxItem;
      if (item == null) return;
      string tag = (string)item.Tag;
      var parts = tag.Split('x');
      if (parts.Length == 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h)) {
        _cachedDeviceName = null; // force refresh for the target display
        ApplyResolution(w, h);
        ConfigService.Resolution = tag;
        ConfigService.Save("Resolution");
      }
    }

    // ── DPI 缩放 ──
    // ponytail: DPI API uses values relative to recommended (reverse-engineered, see SetDPI GitHub)
    static readonly int[] DpiScaleValues = { 100, 125, 140, 150, 175, 200, 225, 250, 300, 350, 400, 450, 500 };

    static int GetGdiDpi() {
      var deviceName = GetInternalDisplayDeviceName();
      if (deviceName == null) return 96;
      IntPtr dc = NativeMethods_Display.CreateDC(deviceName, null, null, IntPtr.Zero);
      if (dc == IntPtr.Zero) return 96;
      int dpiX = NativeMethods_Display.GetDeviceCaps(dc, NativeMethods_Display.LOGPIXELSX);
      NativeMethods_Display.DeleteDC(dc);
      return dpiX;
    }

    static (int cur, int max, int recommended) GetDpiScaleInfo() {
      var ids = FindInternalDisplayIds();
      if (ids.adapterId.LowPart != 0 || ids.adapterId.HighPart != 0 || ids.sourceId != 0) {
        var info = new NativeMethods_Display.DISPLAYCONFIG_SOURCE_DPI_SCALE_GET();
        info.header.type = NativeMethods_Display.INFO_GET_DPI_SCALE;
        info.header.size = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods_Display.DISPLAYCONFIG_SOURCE_DPI_SCALE_GET));
        info.header.adapterId = ids.adapterId;
        info.header.id = ids.sourceId;
        int ret = NativeMethods_Display.DisplayConfigGetDeviceInfoEx(ref info);
        if (ret == 0) {
          Log($"GetDpiScaleInfo: minRel={info.minScaleRel} curRel={info.curScaleRel} maxRel={info.maxScaleRel}");
          int minAbs = Math.Abs(info.minScaleRel);
          int recIdx = minAbs;
          int curIdx = Math.Max(0, Math.Min(minAbs + info.curScaleRel, DpiScaleValues.Length - 1));
          int maxIdx = Math.Max(0, Math.Min(minAbs + info.maxScaleRel, DpiScaleValues.Length - 1));
          recIdx = Math.Max(0, Math.Min(recIdx, DpiScaleValues.Length - 1));
          var result = (cur: DpiScaleValues[curIdx], max: DpiScaleValues[maxIdx], recommended: DpiScaleValues[recIdx]);
          Log($"GetDpiScaleInfo: cur={result.cur} max={result.max} rec={result.recommended}");
          return result;
        }
        Log($"GetDpiScaleInfo: DisplayConfigGetDeviceInfoEx failed: {ret}, falling back to GDI");
      }
      // GDI fallback
      int gdiDpi = GetGdiDpi();
      int pct = (int)Math.Round((double)gdiDpi / 96.0 * 100.0);
      // round to nearest DpiScaleValues entry
      int closest = DpiScaleValues.OrderBy(v => Math.Abs(v - pct)).First();
      Log($"GetDpiScaleInfo: GDI fallback gdiDpi={gdiDpi} pct={pct} closest={closest}");
      return (closest, DpiScaleValues.Last(), DpiScaleValues[0]);
    }

    static void ApplyDpiScale(int percent) {
      var ids = FindInternalDisplayIds();
      if (ids.adapterId.LowPart == 0 && ids.adapterId.HighPart == 0 && ids.sourceId == 0) { Log("ApplyDpiScale: no source ID"); return; }
      var (_, _, recommended) = GetDpiScaleInfo();
      int recIdx = Array.IndexOf(DpiScaleValues, recommended);
      int targetIdx = Array.IndexOf(DpiScaleValues, percent);
      if (recIdx < 0 || targetIdx < 0) { Log($"ApplyDpiScale: index lookup failed recIdx={recIdx} targetIdx={targetIdx}"); return; }
      int relVal = targetIdx - recIdx;
      Log($"ApplyDpiScale: target={percent}% recommended={recommended}% relVal={relVal}");
      var info = new NativeMethods_Display.DISPLAYCONFIG_SOURCE_DPI_SCALE_SET();
      info.header.type = NativeMethods_Display.INFO_SET_DPI_SCALE;
      info.header.size = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods_Display.DISPLAYCONFIG_SOURCE_DPI_SCALE_SET));
      info.header.adapterId = ids.adapterId;
      info.header.id = ids.sourceId;
      info.scaleRel = relVal;
      int ret = NativeMethods_Display.DisplayConfigSetDeviceInfo(ref info);
      Log($"ApplyDpiScale: DisplayConfigSetDeviceInfo returned {ret} (0 = success)");
    }

    void BuildDpiOptions() {
      DpiCombo.Items.Clear();
      var (_, maxScale, _) = GetDpiScaleInfo();
      foreach (int s in DpiScaleValues) {
        if (s > maxScale) break;
        DpiCombo.Items.Add(new ComboBoxItem { Content = s + "%", Tag = s });
      }
      int target = ConfigService.DpiScale > 0 ? ConfigService.DpiScale : GetDpiScaleInfo().cur;
      SelectCombo(DpiCombo, target + "%");
      if (DpiCombo.SelectedIndex < 0) SelectCombo(DpiCombo, "100%");
    }

    void DpiCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) {
      if (_loading) return;
      var item = DpiCombo.SelectedItem as ComboBoxItem;
      if (item == null) return;
      int scale = (int)item.Tag;
      var (cur, _, _) = GetDpiScaleInfo();
      if (scale == cur) return;
      ApplyDpiScale(scale);
      ConfigService.DpiScale = scale;
      ConfigService.Save("DpiScale");
    }

    // ── HDR ──
    (bool supported, bool enabled, bool forceDisabled) GetHdrInfo() {
      var ids = FindInternalDisplayIds();
      if (ids.targetId == 0) return (false, false, false);
      var info = new NativeMethods_Display.DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO();
      info.header.type = NativeMethods_Display.INFO_GET_ADVANCED_COLOR;
      info.header.size = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods_Display.DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO));
      info.header.adapterId = ids.adapterId;
      info.header.id = ids.targetId;
      if (NativeMethods_Display.DisplayConfigGetDeviceInfoEx(ref info) != 0)
        return (false, false, false);
      // bit 0 = AdvancedColorSupported, bit 1 = AdvancedColorEnabled, bit 2 = AdvancedColorForceDisabled
      return ((info.value & 1) != 0, (info.value & 2) != 0, (info.value & 4) != 0);
    }

    void SetHdrEnabled(bool enabled) {
      var ids = FindInternalDisplayIds();
      if (ids.targetId == 0) return;
      var info = new NativeMethods_Display.DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE();
      info.header.type = NativeMethods_Display.INFO_SET_ADVANCED_COLOR;
      info.header.size = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods_Display.DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE));
      info.header.adapterId = ids.adapterId;
      info.header.id = ids.targetId;
      info.value = (uint)(enabled ? 1 : 0);
      NativeMethods_Display.DisplayConfigSetDeviceInfo(ref info);
    }

    void InitHdr() {
      var hdr = GetHdrInfo();
      HdrCard.Visibility = hdr.supported ? Visibility.Visible : Visibility.Collapsed;
      HdrToggle.IsEnabled = !hdr.forceDisabled;
      HdrToggle.IsChecked = ConfigService.HdrEnabled;
      // If saved HDR preference exists and differs from actual system state, apply on load
      if (hdr.supported && !hdr.forceDisabled && ConfigService.HdrEnabled != hdr.enabled)
        SetHdrEnabled(ConfigService.HdrEnabled);
    }

    void HdrToggle_Click(object sender, RoutedEventArgs e) {
      if (_loading) return;
      bool enabled = HdrToggle.IsChecked == true;
      SetHdrEnabled(enabled);
      ConfigService.HdrEnabled = enabled;
      ConfigService.Save("HdrEnabled");
    }

    // ── 关闭显示器 ──
    void TurnOffDisplay_Click(object sender, RoutedEventArgs e) {
      var src = PresentationSource.FromVisual(this) as System.Windows.Interop.HwndSource;
      if (src != null)
        NativeMethods_Display.SendMessage(src.Handle, NativeMethods_Display.WM_SYSCOMMAND,
          (IntPtr)NativeMethods_Display.SC_MONITORPOWER, (IntPtr)2);
    }

    void PowerPlanSettings_Click(object sender, RoutedEventArgs e) {
      try { System.Diagnostics.Process.Start("control.exe", "powercfg.cpl"); } catch { }
    }

    bool _perfExpanded = true;
    const double PerfCollapseWidth = 1000;

    void PerfPage_SizeChanged(object sender, SizeChangedEventArgs e) {
      if (!e.WidthChanged) return;
      if (e.NewSize.Width > PerfCollapseWidth) {
        if (!_perfExpanded) { _perfExpanded = true; ExpandPerfGrids(); }
      } else {
        if (_perfExpanded) { _perfExpanded = false; CollapsePerfGrids(); }
      }
    }

    void ExpandPerfGrids() {
      LayoutPerfGrid(CpuPerfGrid, expand: true);
      LayoutPerfGrid(GpuPerfGrid, expand: true);
      if (CpuAdvGrid.Visibility == Visibility.Visible) LayoutPerfGrid(CpuAdvGrid, expand: true);
      if (GpuAdvGrid.Visibility == Visibility.Visible) LayoutPerfGrid(GpuAdvGrid, expand: true);
    }

    void CollapsePerfGrids() {
      LayoutPerfGrid(CpuPerfGrid, expand: false);
      LayoutPerfGrid(GpuPerfGrid, expand: false);
      if (CpuAdvGrid.Visibility == Visibility.Visible) LayoutPerfGrid(CpuAdvGrid, expand: false);
      if (GpuAdvGrid.Visibility == Visibility.Visible) LayoutPerfGrid(GpuAdvGrid, expand: false);
    }

    // ponytail: detect full-width by runtime Grid.ColumnSpan instead of name matching.
    // Any card with ColumnSpan >= 2 gets the full-row treatment — no name list to maintain.
    static bool IsFullWidthCard(FrameworkElement c) =>
      Grid.GetColumnSpan(c) >= 2;

    // ponytail: set of cards whose XAML ColumnSpan is 2.  This survives layout
    // toggling — after a collapsed→expand round-trip all runtime ColumnSpan
    // values are 2 and we'd lose the regular/fullWidth distinction without it.
    static readonly HashSet<string> _fwNames = new() {
      "FivrCard", "ClockRatioCard", "ApuPowerCard", "ApuVrmCard",
      "AmdCpuPowerCard", "AmdCpuTempCard", "Ccd1CoCard", "Ccd2CoCard"
    };
    /// <summary>Reset runtime ColumnSpan to XAML default (1) so the next
    /// categorization round starts clean.  Only cards whose name appears in
    /// _fwNames (true XAML full-width cards) are left alone — regular cards
    /// that got ColumnSpan=2 during a previous collapsed layout are reset.</summary>
    void ResetColumnSpans(Grid grid) {
      for (int i = 0; i < grid.Children.Count; i++) {
        if (grid.Children[i] is not FrameworkElement c) continue;
        // ponytail: DO NOT guard on runtime Grid.GetColumnSpan(c) >= 2 here.
        // After a collapsed pass ALL regular cards have ColumnSpan=2, so the
        // guard would skip them and IsFullWidthCard mis-classifies everything.
        if (c.Name != null && _fwNames.Contains(c.Name)) continue;
        Grid.SetColumnSpan(c, 1);
      }
    }

    void LayoutPerfGrid(Grid grid, bool expand) {
      int childCount = grid.Children.Count;
      if (childCount == 0) return;
      // Reset regular cards to ColumnSpan=1 before categorising, so a
      // previous collapsed→expand round-trip doesn't trick IsFullWidthCard.
      ResetColumnSpans(grid);
      grid.ColumnDefinitions[1].Width = expand
        ? new GridLength(1, GridUnitType.Star)
        : new GridLength(0, GridUnitType.Pixel);

      var fullWidth = new List<FrameworkElement>();
      var regular = new List<FrameworkElement>();
      for (int i = 0; i < childCount; i++) {
        if (grid.Children[i] is FrameworkElement c && c.Visibility == Visibility.Visible) {
          if (IsFullWidthCard(c)) fullWidth.Add(c);
          else regular.Add(c);
        }
      }

      int row = 0;
      foreach (var c in fullWidth) {
        Grid.SetRow(c, row); Grid.SetColumn(c, 0); Grid.SetColumnSpan(c, 2);
        c.Margin = new Thickness(0, 0, 0, 8);
        row++;
      }

      if (expand) {
        for (int i = 0; i < regular.Count; i++) {
          int col = i % 2;
          var c = regular[i];
          Grid.SetRow(c, row + i / 2); Grid.SetColumn(c, col); Grid.SetColumnSpan(c, 1);
          c.Margin = new Thickness(col == 1 ? 4 : 0, 0, col == 1 ? 0 : 4, 8);
        }
      } else {
        // ponytail: collapsed — each regular card spans full width to avoid
        // Column/ColumnSpan ambiguity. No second-column layout math to drift.
        foreach (var c in regular) {
          Grid.SetRow(c, row); Grid.SetColumn(c, 0); Grid.SetColumnSpan(c, 2);
          c.Margin = new Thickness(0, 0, 0, 8);
          row++;
        }
      }

      grid.InvalidateMeasure();
      grid.InvalidateArrange();
    }

    // ══════════════════════════════════════════════════════
    //   Advanced CPU / GPU tuning cards
    // ══════════════════════════════════════════════════════

    bool _hasAmdCpu => OmenHardware.HasAmdCpu();
    bool _hasIntelCpu => OmenHardware.HasIntelCpu();
    bool _hasNvidiaGpu => GpuAppManager.HasNvidiaGpu();
    bool _hasAmdGpu => OmenHardware.HasAmdGpu();

    // ════════════════════════════════════════════════════════════
    // Hetero CPU (AMD dual-CCD simulated hybrid scheduling)
    // ════════════════════════════════════════════════════════════
    static readonly int[] HeteroPolicyValues = { 0, 1, 2, 3, 4, 5, 6, 7 };
    static readonly int[] HeteroMaskValues = { 1, 2, 3, 4, 5, 6, 7 };

    void BuildAdvancedCards() {
      // PBO Scalar (AMD)
      bool amdSvc = AmdAdvancedService.IsAvailable;
      PboScalarStatus.Text = amdSvc ? "SDK 已连接" : Strings.FeaturePartialImpl;
      PboScalarCombo.Items.Clear();
      PboScalarCombo.Items.Add(new ComboBoxItem { Content = "Auto", Tag = 0 });
      for (int s = 1; s <= 10; s++)
        PboScalarCombo.Items.Add(new ComboBoxItem { Content = s + "x", Tag = s });
      if (ConfigService.PboScalar >= 0 && ConfigService.PboScalar <= 10)
        PboScalarCombo.SelectedIndex = ConfigService.PboScalar;

      // Curve Optimiser
      CoSlider.Value = ConfigService.CoAllCoreOffset;
      CoIGpuSlider.Value = ConfigService.CoIGpuOffset;

      // FIVR
      bool intelSvc = IntelAdvancedService.IsAvailable;
      FivrCoreSlider.Value = ConfigService.FivrCoreOffset;
      FivrCacheSlider.Value = ConfigService.FivrCacheOffset;
      FivrIgpuSlider.Value = ConfigService.FivrIgpuOffset;
      FivrSaSlider.Value = ConfigService.FivrSaOffset;
      FivrStatus.Text = intelSvc ? "XTU SDK 已连接" : Strings.FeaturePartialImpl;

      // Clock Ratio
      BuildClockRatioControls();

      // Power Balance
      PowerBalanceSlider.Value = ConfigService.PowerBalance;
      UpdatePowerBalanceLabel();

      // NVIDIA GPU Tuning
      NvPowerSlider.Value = ConfigService.NvPowerLimit;
      if (GpuAppManager.TryGetPowerLimitInfo(out var plInfo)) {
        NvPowerNum.Minimum = plInfo.Min;
        NvPowerNum.Maximum = plInfo.Max;
        NvPowerSlider.Minimum = plInfo.Min;
        NvPowerSlider.Maximum = plInfo.Max;
        NvPowerStatus.Text = $"当前: {plInfo.Current}W  默认: {plInfo.Default}W  范围: {plInfo.Min}-{plInfo.Max}W";
      } else {
        NvPowerNum.IsEnabled = false;
        NvPowerSlider.IsEnabled = false;
        NvPowerStatus.Text = Strings.HwNotSupported;
      }

      // ADLX status
      AdlxStatus.Text = _hasAmdGpu ? Strings.FeaturePartialImpl : Strings.HwNotDetected;

      // RTSS
      string rtssPath = Environment.ExpandEnvironmentVariables(@"%ProgramW6432%\RivaTuner Statistics Server\RTSS.exe");
      if (!System.IO.File.Exists(rtssPath))
        rtssPath = Environment.ExpandEnvironmentVariables(@"%ProgramFiles(x86)%\RivaTuner Statistics Server\RTSS.exe");
      bool rtssInstalled = System.IO.File.Exists(rtssPath);
      RtssSlider.Value = ConfigService.RtssFrameLimit;
      RtssStatus.Text = rtssInstalled ? Strings.FeaturePartialImpl : "RTSS 未安装 / RTSS not installed";

      // AutoOC
      AutoOcToggle.IsChecked = ConfigService.AutoOcEnabled;
      AutoOcStatus.Text = (amdSvc || intelSvc) ? "SDK 已连接" : Strings.FeaturePartialImpl;

      // UXTU / PawnIO MSR cards
      bool pawnSvc = IntelAdvancedService.IsAvailable;
      PawnTurboToggle.IsChecked = ConfigService.PawnTurboEnabled;
      PawnTurboStatus.Text = pawnSvc ? "PawnIO (MSR 0x1A0)" : Strings.FeaturePartialImpl;
      PawnProchotSlider.Value = ConfigService.PawnProchotOffset;
      PawnProchotStatus.Text = pawnSvc ? "PawnIO (MSR 0x1A2)" : Strings.FeaturePartialImpl;
      PawnHwpSlider.Value = ConfigService.PawnHwpEpp;
      PawnHwpStatus.Text = pawnSvc ? $"EPP={ConfigService.PawnHwpEpp}" : Strings.FeaturePartialImpl;
      BuildCStateCombo();
      PawnCStateStatus.Text = pawnSvc ? $"C-State Max={ConfigService.PawnCStateLimit}" : Strings.FeaturePartialImpl;
      PawnIgpuPowerSlider.Value = ConfigService.PawnIgpuPower;
      PawnIgpuPowerStatus.Text = pawnSvc ? "PawnIO (MSR 0x621)" : Strings.FeaturePartialImpl;
      PawnIgpuRatioSlider.Value = ConfigService.PawnIgpuRatio;
      PawnIgpuRatioStatus.Text = pawnSvc ? "PawnIO (MSR 0x1A2)" : Strings.FeaturePartialImpl;

      // AMD APU SMU tuning cards
      bool smuSvc = AmdAdvancedService.IsAvailable;
      ApuStapmSlider.Value = ConfigService.AmdStapmLimit > 0 ? ConfigService.AmdStapmLimit : 25;
      ApuFastSlider.Value = ConfigService.AmdFastLimit > 0 ? ConfigService.AmdFastLimit : 35;
      ApuSlowSlider.Value = ConfigService.AmdSlowLimit > 0 ? ConfigService.AmdSlowLimit : 25;
      ApuStapmTimeSlider.Value = ConfigService.AmdStapmTime > 0 ? ConfigService.AmdStapmTime : 8;
      ApuSlowTimeSlider.Value = ConfigService.AmdSlowTime > 0 ? ConfigService.AmdSlowTime : 16;
      ApuPowerStatus.Text = smuSvc ? $"SMU 已连接: {AmdAdvancedService.CpuFamilyName}" : Strings.FeaturePartialImpl;

      ApuVrmCurrentSlider.Value = ConfigService.AmdVrmCurrent > 0 ? ConfigService.AmdVrmCurrent : 150;
      ApuVrmSocCurrentSlider.Value = ConfigService.AmdVrmSocCurrent > 0 ? ConfigService.AmdVrmSocCurrent : 100;
      ApuVrmMaxSlider.Value = ConfigService.AmdVrmMaxCurrent > 0 ? ConfigService.AmdVrmMaxCurrent : 180;
      ApuVrmSocMaxSlider.Value = ConfigService.AmdVrmSocMaxCurrent > 0 ? ConfigService.AmdVrmSocMaxCurrent : 120;
      ApuVrmStatus.Text = smuSvc ? "SMU 已连接" : Strings.FeaturePartialImpl;

      ApuTctlSlider.Value = ConfigService.AmdTctlTemp > 0 ? ConfigService.AmdTctlTemp : 95;
      ApuSkinTempSlider.Value = ConfigService.AmdApuSkinTemp > 0 ? ConfigService.AmdApuSkinTemp : 65;
      ApuDgpuSkinSlider.Value = ConfigService.AmdDgpuSkinTemp > 0 ? ConfigService.AmdDgpuSkinTemp : 65;
      ApuTempStatus.Text = smuSvc ? "SMU 已连接" : Strings.FeaturePartialImpl;

      ApuGfxClkSlider.Value = ConfigService.AmdGfxClk;
      ApuGfxClkStatus.Text = smuSvc ? "SMU 已连接 (PSMU gfx-clk)" : Strings.FeaturePartialImpl;

      // AMD CPU Power Limits (PPT / TDC / EDC) — AM5 desktop
      AmdCpuPptSlider.Value = ConfigService.AmdCpuPpt > 0 ? ConfigService.AmdCpuPpt : 105;
      AmdCpuTdcSlider.Value = ConfigService.AmdCpuTdc > 0 ? ConfigService.AmdCpuTdc : 80;
      AmdCpuEdcSlider.Value = ConfigService.AmdCpuEdc > 0 ? ConfigService.AmdCpuEdc : 160;
      AmdCpuPowerStatus.Text = smuSvc ? "SMU 已连接" : Strings.FeaturePartialImpl;

      // AMD CPU Temperature Limit (Tctl) — AM5 desktop
      AmdCpuTctlSlider.Value = ConfigService.AmdCpuTctl > 0 ? ConfigService.AmdCpuTctl : 95;
      AmdCpuTempStatus.Text = smuSvc ? "SMU 已连接" : Strings.FeaturePartialImpl;

      // Per-Core Curve Optimiser panels built dynamically
      BuildPerCoreCoPanels();

      // ── Hetero CPU ──
      InitHeteroCpu();

      // ── Init master toggles (UXTU-style per-card enable/disable) ──
      InitMasterToggle(FivrMasterToggle, FivrCard, ConfigService.FivrMasterEnabled);
      InitMasterToggle(ApuPowerMasterToggle, ApuPowerCard, ConfigService.ApuPowerMasterEnabled);
      InitMasterToggle(ApuVrmMasterToggle, ApuVrmCard, ConfigService.ApuVrmMasterEnabled);
      InitMasterToggle(ApuTempMasterToggle, ApuTempCard, ConfigService.ApuTempMasterEnabled);
      InitMasterToggle(ApuGfxClkMasterToggle, ApuGfxClkCard, ConfigService.ApuGfxClkMasterEnabled);
      // ponytail: new master toggles — each card's master toggle gates its child UI controls.
      // For PCI/level toggles (AutoOC / single-toggle cards) we toggle only one
      // child control instead of all sliders — keeps the same helper API.
      InitMasterToggle(PboScalarMasterToggle, PboScalarCard, ConfigService.PboScalarMasterEnabled);
      InitMasterToggle(CoMasterToggle, CoCard, ConfigService.CoMasterEnabled);
      AutoOcMasterToggle.IsChecked = ConfigService.AutoOcMasterEnabled;
      AutoOcToggle.IsEnabled = ConfigService.AutoOcMasterEnabled;
      InitMasterToggle(ClockRatioMasterToggle, ClockRatioCard, ConfigService.ClockRatioMasterEnabled);
      InitMasterToggle(PowerBalanceMasterToggle, PowerBalanceCard, ConfigService.PowerBalanceMasterEnabled);
      PawnTurboMasterToggle.IsChecked = ConfigService.PawnTurboMasterEnabled;
      PawnTurboToggle.IsEnabled = ConfigService.PawnTurboMasterEnabled;
      InitMasterToggle(PawnProchotMasterToggle, PawnProchotCard, ConfigService.PawnProchotMasterEnabled);
      InitMasterToggle(PawnHwpMasterToggle, PawnHwpCard, ConfigService.PawnHwpMasterEnabled);
      PawnCStateMasterToggle.IsChecked = ConfigService.PawnCStateMasterEnabled;
      PawnCStateCombo.IsEnabled = ConfigService.PawnCStateMasterEnabled;
      InitMasterToggle(PawnIgpuPowerMasterToggle, PawnIgpuPowerCard, ConfigService.PawnIgpuPowerMasterEnabled);
      InitMasterToggle(PawnIgpuRatioMasterToggle, PawnIgpuRatioCard, ConfigService.PawnIgpuRatioMasterEnabled);
      InitMasterToggle(NvTuningMasterToggle, NvTuningCard, ConfigService.NvTuningMasterEnabled);
      InitMasterToggle(RtssMasterToggle, RtssCard, ConfigService.RtssMasterEnabled);
    }

    void BuildCStateCombo() {
      PawnCStateCombo.Items.Clear();
      string[] labels = { "无限制", "C1", "C2", "C3", "C4", "C5", "C6", "C7", "C8", "C9", "C10" };
      for (int i = 0; i < labels.Length; i++)
        PawnCStateCombo.Items.Add(new ComboBoxItem { Content = labels[i], Tag = i });
      int saved = ConfigService.PawnCStateLimit;
      if (saved >= 0 && saved < labels.Length)
        PawnCStateCombo.SelectedIndex = saved;
    }

    // ── Master Toggle helpers (UXTU-style per-card enable/disable) ──

    /// <summary>Enable/disable all Slider+NumberBox children inside a card's content StackPanel</summary>
    static void SetCardSlidersEnabled(UIElement card, bool enabled) {
      if (card == null) return;
      FrameworkElement contentRoot = null;
      if (card is Wpf.Ui.Controls.CardExpander ce && ce.Content is FrameworkElement fe) contentRoot = fe;
      else if (card is Wpf.Ui.Controls.CardControl cc && cc.Content is FrameworkElement fe2) contentRoot = fe2;
      if (contentRoot == null) return;
      foreach (var child in FindVisualChildren<Slider>(contentRoot)) child.IsEnabled = enabled;
      foreach (var child in FindVisualChildren<Wpf.Ui.Controls.NumberBox>(contentRoot)) child.IsEnabled = enabled;
    }

    static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject {
      if (parent == null) yield break;
      int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
      for (int i = 0; i < count; i++) {
        var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
        if (child is T t) yield return t;
        foreach (var grandChild in FindVisualChildren<T>(child)) yield return grandChild;
      }
    }

    void InitMasterToggle(Wpf.Ui.Controls.ToggleSwitch toggle, FrameworkElement card, bool savedValue) {
      toggle.IsChecked = savedValue;
      SetCardSlidersEnabled(card, savedValue);
    }

    // ── FIVR Master Toggle ──
    void FivrMasterToggle_Changed(object sender, RoutedEventArgs e) {
      if (_loading) return;
      bool on = FivrMasterToggle.IsChecked == true;
      ConfigService.FivrMasterEnabled = on;
      ConfigService.Save("FivrMasterEnabled");
      SetCardSlidersEnabled(FivrCard, on);
    }

    // ── APU Power Master Toggle ──
    void ApuPowerMasterToggle_Changed(object sender, RoutedEventArgs e) {
      if (_loading) return;
      bool on = ApuPowerMasterToggle.IsChecked == true;
      ConfigService.ApuPowerMasterEnabled = on;
      ConfigService.Save("ApuPowerMasterEnabled");
      SetCardSlidersEnabled(ApuPowerCard, on);
    }

    // ── APU VRM Master Toggle ──
    void ApuVrmMasterToggle_Changed(object sender, RoutedEventArgs e) {
      if (_loading) return;
      bool on = ApuVrmMasterToggle.IsChecked == true;
      ConfigService.ApuVrmMasterEnabled = on;
      ConfigService.Save("ApuVrmMasterEnabled");
      SetCardSlidersEnabled(ApuVrmCard, on);
    }

    // ── APU Temp Master Toggle ──
    void ApuTempMasterToggle_Changed(object sender, RoutedEventArgs e) {
      if (_loading) return;
      bool on = ApuTempMasterToggle.IsChecked == true;
      ConfigService.ApuTempMasterEnabled = on;
      ConfigService.Save("ApuTempMasterEnabled");
      SetCardSlidersEnabled(ApuTempCard, on);
    }

    // ── APU iGPU Clock Master Toggle ──
    void ApuGfxClkMasterToggle_Changed(object sender, RoutedEventArgs e) {
      if (_loading) return;
      bool on = ApuGfxClkMasterToggle.IsChecked == true;
      ConfigService.ApuGfxClkMasterEnabled = on;
      ConfigService.Save("ApuGfxClkMasterEnabled");
      SetCardSlidersEnabled(ApuGfxClkCard, on);
    }

    // ── New master toggles ──
    // PBO Scalar
    void PboScalarMasterToggle_Changed(object sender, RoutedEventArgs e) {
      if (_loading) return;
      bool on = PboScalarMasterToggle.IsChecked == true;
      ConfigService.PboScalarMasterEnabled = on;
      ConfigService.Save("PboScalarMasterEnabled");
      SetCardSlidersEnabled(PboScalarCard, on);
    }
    // Curve Optimiser
    void CoMasterToggle_Changed(object sender, RoutedEventArgs e) {
      if (_loading) return;
      bool on = CoMasterToggle.IsChecked == true;
      ConfigService.CoMasterEnabled = on;
      ConfigService.Save("CoMasterEnabled");
      SetCardSlidersEnabled(CoCard, on);
    }
    // AutoOC
    void AutoOcMasterToggle_Changed(object sender, RoutedEventArgs e) {
      if (_loading) return;
      bool on = AutoOcMasterToggle.IsChecked == true;
      ConfigService.AutoOcMasterEnabled = on;
      ConfigService.Save("AutoOcMasterEnabled");
      AutoOcToggle.IsEnabled = on;
    }
    // Clock Ratio (Intel)
    void ClockRatioMasterToggle_Changed(object sender, RoutedEventArgs e) {
      if (_loading) return;
      bool on = ClockRatioMasterToggle.IsChecked == true;
      ConfigService.ClockRatioMasterEnabled = on;
      ConfigService.Save("ClockRatioMasterEnabled");
      SetCardSlidersEnabled(ClockRatioCard, on);
    }
    // Power Balance (Intel)
    void PowerBalanceMasterToggle_Changed(object sender, RoutedEventArgs e) {
      if (_loading) return;
      bool on = PowerBalanceMasterToggle.IsChecked == true;
      ConfigService.PowerBalanceMasterEnabled = on;
      ConfigService.Save("PowerBalanceMasterEnabled");
      SetCardSlidersEnabled(PowerBalanceCard, on);
    }
    // PawnIO MSR cards
    void PawnTurboMasterToggle_Changed(object sender, RoutedEventArgs e) {
      if (_loading) return;
      bool on = PawnTurboMasterToggle.IsChecked == true;
      ConfigService.PawnTurboMasterEnabled = on;
      ConfigService.Save("PawnTurboMasterEnabled");
      PawnTurboToggle.IsEnabled = on;
    }
    void PawnProchotMasterToggle_Changed(object sender, RoutedEventArgs e) {
      if (_loading) return;
      bool on = PawnProchotMasterToggle.IsChecked == true;
      ConfigService.PawnProchotMasterEnabled = on;
      ConfigService.Save("PawnProchotMasterEnabled");
      SetCardSlidersEnabled(PawnProchotCard, on);
    }
    void PawnHwpMasterToggle_Changed(object sender, RoutedEventArgs e) {
      if (_loading) return;
      bool on = PawnHwpMasterToggle.IsChecked == true;
      ConfigService.PawnHwpMasterEnabled = on;
      ConfigService.Save("PawnHwpMasterEnabled");
      SetCardSlidersEnabled(PawnHwpCard, on);
    }
    void PawnCStateMasterToggle_Changed(object sender, RoutedEventArgs e) {
      if (_loading) return;
      bool on = PawnCStateMasterToggle.IsChecked == true;
      ConfigService.PawnCStateMasterEnabled = on;
      ConfigService.Save("PawnCStateMasterEnabled");
      PawnCStateCombo.IsEnabled = on;
    }
    void PawnIgpuPowerMasterToggle_Changed(object sender, RoutedEventArgs e) {
      if (_loading) return;
      bool on = PawnIgpuPowerMasterToggle.IsChecked == true;
      ConfigService.PawnIgpuPowerMasterEnabled = on;
      ConfigService.Save("PawnIgpuPowerMasterEnabled");
      SetCardSlidersEnabled(PawnIgpuPowerCard, on);
    }
    void PawnIgpuRatioMasterToggle_Changed(object sender, RoutedEventArgs e) {
      if (_loading) return;
      bool on = PawnIgpuRatioMasterToggle.IsChecked == true;
      ConfigService.PawnIgpuRatioMasterEnabled = on;
      ConfigService.Save("PawnIgpuRatioMasterEnabled");
      SetCardSlidersEnabled(PawnIgpuRatioCard, on);
    }
    // NVIDIA Tunings
    void NvTuningMasterToggle_Changed(object sender, RoutedEventArgs e) {
      if (_loading) return;
      bool on = NvTuningMasterToggle.IsChecked == true;
      ConfigService.NvTuningMasterEnabled = on;
      ConfigService.Save("NvTuningMasterEnabled");
      SetCardSlidersEnabled(NvTuningCard, on);
    }
    // RTSS
    void RtssMasterToggle_Changed(object sender, RoutedEventArgs e) {
      if (_loading) return;
      bool on = RtssMasterToggle.IsChecked == true;
      ConfigService.RtssMasterEnabled = on;
      ConfigService.Save("RtssMasterEnabled");
      SetCardSlidersEnabled(RtssCard, on);
    }

    // ── ADLX Master Toggle ──
    void AdlxMasterToggle_Changed(object sender, RoutedEventArgs e) {
      if (_loading) return;
      bool on = AdlxMasterToggle.IsChecked == true;
      ConfigService.AdlxMasterEnabled = on;
      ConfigService.Save("AdlxMasterEnabled");
      AdlxRsrToggle.IsEnabled = on;
      AdlxRsrSharpNum.IsEnabled = on;
      AdlxAntiLagToggle.IsEnabled = on;
      AdlxEnhancedSyncToggle.IsEnabled = on;
      AdlxBoostToggle.IsEnabled = on;
      AdlxBoostNum.IsEnabled = on;
      AdlxImageSharpToggle.IsEnabled = on;
      AdlxImageSharpNum.IsEnabled = on;
    }
    // ── ADLX individual controls ──
    void AdlxRsr_Changed(object sender, RoutedEventArgs e) {
      if (_loading) return;
      bool on = AdlxRsrToggle.IsChecked == true;
      ConfigService.AdlxRsrEnabled = on;
      ConfigService.Save("AdlxRsrEnabled");
      if (AdlxGpuService.IsAvailable) AdlxGpuService.EnableRsr(on);
    }
    void AdlxRsrSharp_Changed(object s, RoutedEventArgs e) {
      if (_loading) return;
      int v = (int)(AdlxRsrSharpNum.Value ?? 50);
      ConfigService.AdlxRsrSharpness = v;
      ConfigService.Save("AdlxRsrSharpness");
      if (AdlxGpuService.IsAvailable && AdlxRsrToggle.IsChecked == true) AdlxGpuService.SetRsrSharpness(v);
    }
    void AdlxAntiLag_Changed(object sender, RoutedEventArgs e) {
      if (_loading) return;
      bool on = AdlxAntiLagToggle.IsChecked == true;
      ConfigService.AdlxAntiLagEnabled = on;
      ConfigService.Save("AdlxAntiLagEnabled");
      if (AdlxGpuService.IsAvailable) AdlxGpuService.EnableAntiLag(on);
    }
    void AdlxEnhancedSync_Changed(object sender, RoutedEventArgs e) {
      if (_loading) return;
      bool on = AdlxEnhancedSyncToggle.IsChecked == true;
      ConfigService.AdlxEnhancedSyncEnabled = on;
      ConfigService.Save("AdlxEnhancedSyncEnabled");
      if (AdlxGpuService.IsAvailable) AdlxGpuService.EnableEnhancedSync(on);
    }
    void AdlxBoost_Changed(object sender, RoutedEventArgs e) {
      if (_loading) return;
      bool on = AdlxBoostToggle.IsChecked == true;
      int pct = (int)(AdlxBoostNum.Value ?? 0);
      ConfigService.AdlxBoostEnabled = on;
      ConfigService.AdlxBoostPercent = pct;
      ConfigService.Save("AdlxBoostEnabled");
      ConfigService.Save("AdlxBoostPercent");
      if (AdlxGpuService.IsAvailable) AdlxGpuService.EnableBoost(on, pct);
    }
    void AdlxImageSharp_Changed(object sender, RoutedEventArgs e) {
      if (_loading) return;
      bool on = AdlxImageSharpToggle.IsChecked == true;
      int pct = (int)(AdlxImageSharpNum.Value ?? 50);
      ConfigService.AdlxImageSharpEnabled = on;
      ConfigService.AdlxImageSharpPercent = pct;
      ConfigService.Save("AdlxImageSharpEnabled");
      ConfigService.Save("AdlxImageSharpPercent");
      if (AdlxGpuService.IsAvailable) AdlxGpuService.EnableImageSharpening(on, pct);
    }

    void ApplyHardwareVisibility() {
      // ── CpuPerfGrid per-vendor visibility (always applies) ──
      bool hasAmd = _hasAmdCpu;
      bool hasIntel = _hasIntelCpu;
      // DebugShowAllUi: 强制显示所有卡片（忽略 per-vendor 限制）
      if (ConfigService.DebugShowAllUi) {
        hasAmd = true;
        hasIntel = true;
      }
      IccMaxCard.Visibility = hasIntel ? Visibility.Visible : Visibility.Collapsed;
      AcLoadLineCard.Visibility = hasIntel ? Visibility.Visible : Visibility.Collapsed;
      HeteroCpuCard.Visibility = hasAmd ? Visibility.Visible : Visibility.Collapsed;

      // ponytail: AMD/Intel CPU power split
      bool amdSmu = false;
      try { amdSmu = hasAmd && AmdAdvancedService.IsAvailable; } catch { }
      // DebugShowAllUi 强制显示 AMD 卡片，跳过 SMU 可用性检测
      if (ConfigService.DebugShowAllUi) amdSmu = true;
      AmdCpuPowerCard.Visibility = amdSmu ? Visibility.Visible : Visibility.Collapsed;
      // ponytail: TDC/EDC 和 Tctl 按 SMU 可用性直接显示（不再需要点5次 logo）
      if (AmdCpuPowerCard != null) AmdCpuTdcEdcPanel.Visibility = amdSmu ? Visibility.Visible : Visibility.Collapsed;
      AmdCpuTempCard.Visibility = amdSmu ? Visibility.Visible : Visibility.Collapsed;
      CpuPowerCard.Visibility = hasIntel && !hasAmd ? Visibility.Visible : Visibility.Collapsed;

      // Gate: hide advanced tuning unless unlocked via logo 5-click (or DEBUG mode)
      if (!ConfigService.AdvancedTuningUnlocked && !ConfigService.DebugShowAllUi) {
        CpuAdvHeader.Visibility = Visibility.Collapsed;
        CpuAdvGrid.Visibility = Visibility.Collapsed;
        GpuAdvHeader.Visibility = Visibility.Collapsed;
        GpuAdvGrid.Visibility = Visibility.Collapsed;
        return;
      }

      bool hasNvidia = _hasNvidiaGpu;
      bool hasAdvCpu = hasAmd || hasIntel;
      bool hasAdvGpu = hasNvidia || _hasAmdGpu;
      CpuAdvHeader.Visibility = hasAdvCpu ? Visibility.Visible : Visibility.Collapsed;
      CpuAdvGrid.Visibility = hasAdvCpu ? Visibility.Visible : Visibility.Collapsed;
      PboScalarCard.Visibility = hasAmd ? Visibility.Visible : Visibility.Collapsed;
      CoCard.Visibility = hasAmd ? Visibility.Visible : Visibility.Collapsed;
      AutoOcCard.Visibility = hasAmd ? Visibility.Visible : Visibility.Collapsed;
      // AMD APU SMU tuning cards
      ApuPowerCard.Visibility = amdSmu ? Visibility.Visible : Visibility.Collapsed;
      ApuVrmCard.Visibility = amdSmu ? Visibility.Visible : Visibility.Collapsed;
      ApuTempCard.Visibility = amdSmu ? Visibility.Visible : Visibility.Collapsed;
      // Per-core CO: CCD1 always visible when SMU available; CCD2 only dual-CCD
      Ccd1CoCard.Visibility = amdSmu ? Visibility.Visible : Visibility.Collapsed;
      Ccd2CoCard.Visibility = (amdSmu && HeteroCpuService.DetectDualCcd().supported) ? Visibility.Visible : Visibility.Collapsed;

      // Intel cards
      FivrCard.Visibility = hasIntel ? Visibility.Visible : Visibility.Collapsed;
      ClockRatioCard.Visibility = hasIntel ? Visibility.Visible : Visibility.Collapsed;
      PowerBalanceCard.Visibility = hasIntel ? Visibility.Visible : Visibility.Collapsed;

      // PawnIO MSR cards visible when Intel + PawnIO available
      bool pawnHw = hasIntel && IntelAdvancedService.IsAvailable;
      PawnTurboCard.Visibility = pawnHw ? Visibility.Visible : Visibility.Collapsed;
      PawnProchotCard.Visibility = pawnHw ? Visibility.Visible : Visibility.Collapsed;
      PawnHwpCard.Visibility = pawnHw ? Visibility.Visible : Visibility.Collapsed;
      PawnCStateCard.Visibility = pawnHw ? Visibility.Visible : Visibility.Collapsed;

      GpuAdvHeader.Visibility = hasAdvGpu ? Visibility.Visible : Visibility.Collapsed;
      GpuAdvGrid.Visibility = hasAdvGpu ? Visibility.Visible : Visibility.Collapsed;

      // GPU cards
      NvTuningCard.Visibility = hasNvidia ? Visibility.Visible : Visibility.Collapsed;
      AdlxCard.Visibility = _hasAmdGpu && AdlxGpuService.IsAvailable ? Visibility.Visible : Visibility.Collapsed;
      if (_hasAmdGpu && AdlxGpuService.IsAvailable) {
        AdlxStatus.Text = "ADLX 已连接 - RSR/Anti-Lag/Enhanced Sync/Boost/Image Sharpening";
      } else {
        AdlxStatus.Text = AdlxGpuService.IsAvailable ? Strings.FeaturePartialImpl : Strings.HwNotDetected;
      }
      RtssCard.Visibility = hasNvidia ? Visibility.Visible : Visibility.Collapsed;
      // iGPU cards visible when Intel + PawnIO available (iGPU present)
      PawnIgpuPowerCard.Visibility = pawnHw ? Visibility.Visible : Visibility.Collapsed;
      PawnIgpuRatioCard.Visibility = pawnHw ? Visibility.Visible : Visibility.Collapsed;
      // iGPU Clock Override — visible when AMD SMU available (APU series)
      // ponytail: moved from CpuAdvGrid to GpuAdvGrid so iGPU controls live
      // under the GPU heading alongside other iGPU cards.
      ApuGfxClkCard.Visibility = (hasAmd && amdSmu) ? Visibility.Visible : Visibility.Collapsed;

      // DEBUG: 强制显示所有卡片
      if (ConfigService.DebugShowAllUi) {
        foreach (UIElement child in CpuAdvGrid.Children) child.Visibility = Visibility.Visible;
        foreach (UIElement child in GpuAdvGrid.Children) child.Visibility = Visibility.Visible;
        // ponytail: re-layout immediately so overlapping static Grid.Row
        // values don't cause visible overlap.  Skip the normal per-card
        // visibility logic below — DebugShowAllUi already set everything.
        bool exp = _perfExpanded;
        LayoutPerfGrid(CpuAdvGrid, exp);
        LayoutPerfGrid(GpuAdvGrid, exp);
        return;
      }

    }

    /// <summary>由 MainWindow 在解锁/锁定高级调校时调用</summary>
    public void RefreshAdvancedVisibility() {
      ApplyHardwareVisibility();
      // Re-layout performance grids
      if (_perfExpanded) {
        if (CpuAdvGrid.Visibility == Visibility.Visible) LayoutPerfGrid(CpuAdvGrid, expand: true);
        if (GpuAdvGrid.Visibility == Visibility.Visible) LayoutPerfGrid(GpuAdvGrid, expand: true);
      } else {
        if (CpuAdvGrid.Visibility == Visibility.Visible) LayoutPerfGrid(CpuAdvGrid, expand: false);
        if (GpuAdvGrid.Visibility == Visibility.Visible) LayoutPerfGrid(GpuAdvGrid, expand: false);
      }
    }

    // ── PBO Scalar ──
    void PboScalar_Changed(object sender, SelectionChangedEventArgs e) {
      if (_loading) return;
      var item = PboScalarCombo.SelectedItem as ComboBoxItem;
      if (item == null) return;
      int val = (int)item.Tag;
      ConfigService.PboScalar = val;
      ConfigService.Save("PboScalar");
      // ponytail: SDK SetPBOScalar(Bool) only toggles PBO on/off, doesn't set specific scalar
      if (AmdAdvancedService.IsAvailable) {
        _ = AmdAdvancedService.SetPboAsync(val != 0);
        PboScalarStatus.Text = val == 0 ? "PBO Auto (SDK)" : $"PBO {val}x (SDK)";
      }
    }

    // ── Curve Optimiser ──
    void CoNum_ValueChanged(object s, RoutedEventArgs e) {
      if (_loading) return;
      _loading = true;
      double? val = CoNum.Value;
      if (val == null || val < -50 || val > 30) { _loading = false; return; }
      int v = (int)val;
      ConfigService.CoAllCoreOffset = v;
      ConfigService.Save("CoAllCoreOffset");
      if (AmdAdvancedService.IsAvailable)
        _ = AmdAdvancedService.SetCurveOptimizerAsync((short)v);
      _loading = false;
    }
    void CoIGpuNum_ValueChanged(object s, RoutedEventArgs e) {
      if (_loading) return;
      double? val = CoIGpuNum.Value;
      if (val == null || val < -30 || val > 30) return;
      int v = (int)val;
      ConfigService.CoIGpuOffset = v;
      ConfigService.Save("CoIGpuOffset");
      if (AmdAdvancedService.IsAvailable && AmdAdvancedService.SetCurveOptimizerIGpu(v)) {
        // ponytail: silent success, no status label on CoCard's iGPU row
      }
    }

    // ════════════════════════════════════════════════════════════
    // Hetero CPU (AMD dual-CCD simulated hybrid scheduling)
    // ════════════════════════════════════════════════════════════

    void InitHeteroCpu() {
      HeteroCpuMaskBox.Text = ConfigService.HeteroCpuSmallMask;
      HeteroCpuRuntimeBox.Text = ConfigService.HeteroCpuExpectedRuntime.ToString();
      HeteroCpuPriorityBox.Text = ConfigService.HeteroCpuImportantPriority.ToString();
      RefreshHeteroLabels();
      // Restore saved selections
      SelectPolicy(HeteroCpuDefaultPolicyCombo, ConfigService.HeteroCpuDefaultPolicy, HeteroPolicyValues);
      SelectPolicy(HeteroCpuImportantPolicyCombo, ConfigService.HeteroCpuImportantPolicy, HeteroPolicyValues);
      SelectPolicy(HeteroCpuImportantShortCombo, ConfigService.HeteroCpuImportantShortPolicy, HeteroPolicyValues);
      SelectPolicy(HeteroCpuPolicyMaskCombo, ConfigService.HeteroCpuPolicyMask, HeteroMaskValues);
      // Load toggle state
      HeteroCpuToggle.IsChecked = HeteroCpuService.IsActive();
      HeteroCpuDetails.Visibility = HeteroCpuService.IsActive() ? Visibility.Visible : Visibility.Collapsed;
      // ponytail: register lang change once
      Strings.OnLanguageChanged -= RefreshHeteroLabels;
      Strings.OnLanguageChanged += RefreshHeteroLabels;
    }

    void RefreshHeteroLabels() {
      // ponytail: do NOT toggle _loading — called from BuildAdvancedCards
      // which is already inside a _loading=true block from BeginInvoke.
      HeteroCpuDefaultPolicyCombo.ItemsSource = null;
      HeteroCpuDefaultPolicyCombo.ItemsSource = new[] {
          Strings.HeteroPolicyAny, Strings.HeteroPolicyBig, Strings.HeteroPolicyBigOrIdle,
          Strings.HeteroPolicySmall, Strings.HeteroPolicySmallOrIdle, Strings.HeteroPolicyAuto,
          Strings.HeteroPolicyPreferSmall, Strings.HeteroPolicyPreferBig
      };
      HeteroCpuImportantPolicyCombo.ItemsSource = null;
      HeteroCpuImportantPolicyCombo.ItemsSource = new[] {
          Strings.HeteroPolicyAny, Strings.HeteroPolicyBig, Strings.HeteroPolicyBigOrIdle,
          Strings.HeteroPolicySmall, Strings.HeteroPolicySmallOrIdle, Strings.HeteroPolicyAuto,
          Strings.HeteroPolicyPreferSmall, Strings.HeteroPolicyPreferBig
      };
      HeteroCpuImportantShortCombo.ItemsSource = null;
      HeteroCpuImportantShortCombo.ItemsSource = new[] {
          Strings.HeteroPolicyAny, Strings.HeteroPolicyBig, Strings.HeteroPolicyBigOrIdle,
          Strings.HeteroPolicySmall, Strings.HeteroPolicySmallOrIdle, Strings.HeteroPolicyAuto,
          Strings.HeteroPolicyPreferSmall, Strings.HeteroPolicyPreferBig
      };
      HeteroCpuPolicyMaskCombo.ItemsSource = null;
      HeteroCpuPolicyMaskCombo.ItemsSource = new[] {
          Strings.HeteroMaskForeground, Strings.HeteroMaskPriority, Strings.HeteroMaskFgPriority,
          Strings.HeteroMaskRuntime, Strings.HeteroMaskFgRuntime, Strings.HeteroMaskPriRuntime,
          Strings.HeteroMaskAll
      };
      SelectPolicy(HeteroCpuDefaultPolicyCombo, ConfigService.HeteroCpuDefaultPolicy, HeteroPolicyValues);
      SelectPolicy(HeteroCpuImportantPolicyCombo, ConfigService.HeteroCpuImportantPolicy, HeteroPolicyValues);
      SelectPolicy(HeteroCpuImportantShortCombo, ConfigService.HeteroCpuImportantShortPolicy, HeteroPolicyValues);
      SelectPolicy(HeteroCpuPolicyMaskCombo, ConfigService.HeteroCpuPolicyMask, HeteroMaskValues);
    }

    static void SelectPolicy(ComboBox cb, int val, int[] values) {
      int idx = Array.IndexOf(values, val);
      if (idx >= 0 && idx < (cb.ItemsSource as string[])?.Length)
        cb.SelectedIndex = idx;
    }

    void HeteroCpuToggle_Changed(object sender, RoutedEventArgs e) {
      if (_loading) return;
      bool enable = HeteroCpuToggle.IsChecked == true;
      HeteroCpuDetails.Visibility = enable ? Visibility.Visible : Visibility.Collapsed;
	      if (enable) {
	        HeteroCpuService.WriteSmallProcessorMask(ConfigService.HeteroCpuSmallMask);
	        HeteroCpuService.WriteDefaultPolicy(ConfigService.HeteroCpuDefaultPolicy);
	        HeteroCpuService.WriteExpectedRuntime(ConfigService.HeteroCpuExpectedRuntime);
	        HeteroCpuService.WriteImportantPolicy(ConfigService.HeteroCpuImportantPolicy);
	        HeteroCpuService.WriteImportantShortPolicy(ConfigService.HeteroCpuImportantShortPolicy);
	        HeteroCpuService.WritePolicyMask(ConfigService.HeteroCpuPolicyMask);
	        HeteroCpuService.WriteImportantPriority(ConfigService.HeteroCpuImportantPriority);
	        // Also write per-power-plan hetero scheduling settings to active plan
	        if (GetActiveSchemeGuid() is Guid activeScheme) {
	          WritePwrValueBoth(activeScheme, GUID_HETERO_THREAD_SCHED, 2, 5);
	          WritePwrValueBoth(activeScheme, GUID_HETERO_SHORT_SCHED, 2, 5);
	          WritePwrValueBoth(activeScheme, GUID_HETERO_ACTIVE_POLICY, 0, 4);
	        }
	      } else {
	        HeteroCpuService.RemoveAll();
	        // Reset per-power-plan hetero settings to default (auto)
	        if (GetActiveSchemeGuid() is Guid activeScheme) {
	          WritePwrValueBoth(activeScheme, GUID_HETERO_THREAD_SCHED, 5, 5);
	          WritePwrValueBoth(activeScheme, GUID_HETERO_SHORT_SCHED, 5, 5);
	          WritePwrValueBoth(activeScheme, GUID_HETERO_ACTIVE_POLICY, 4, 4);
	        }
	      }
    }

    void HeteroCpuMask_TextChanged(object sender, TextChangedEventArgs e) {
      if (_loading) return;
      ConfigService.HeteroCpuSmallMask = HeteroCpuMaskBox.Text.Trim();
      ConfigService.Save("HeteroCpuSmallMask");
    }

    void HeteroCpuPolicy_Changed(object sender, SelectionChangedEventArgs e) {
      if (_loading) return;
      var combo = sender as ComboBox;
      if (combo == null || combo.SelectedIndex < 0) return;
      int val = HeteroPolicyValues[combo.SelectedIndex];
      string key = null;
      if (combo == HeteroCpuDefaultPolicyCombo) key = "HeteroCpuDefaultPolicy";
      else if (combo == HeteroCpuImportantPolicyCombo) key = "HeteroCpuImportantPolicy";
      else if (combo == HeteroCpuImportantShortCombo) key = "HeteroCpuImportantShortPolicy";
      else if (combo == HeteroCpuPolicyMaskCombo) { val = HeteroMaskValues[combo.SelectedIndex]; key = "HeteroCpuPolicyMask"; }
      if (key == null) return;
      var field = typeof(ConfigService).GetField(key);
      if (field != null) field.SetValue(null, val);
      ConfigService.Save(key);
    }

    void HeteroCpuRuntime_Changed(object sender, TextChangedEventArgs e) {
      if (_loading) return;
      if (int.TryParse(HeteroCpuRuntimeBox.Text, out var val)) {
        ConfigService.HeteroCpuExpectedRuntime = val;
        ConfigService.Save("HeteroCpuExpectedRuntime");
      }
    }

    void HeteroCpuPriority_Changed(object sender, TextChangedEventArgs e) {
      if (_loading) return;
      if (int.TryParse(HeteroCpuPriorityBox.Text, out var val)) {
        ConfigService.HeteroCpuImportantPriority = val;
        ConfigService.Save("HeteroCpuImportantPriority");
      }
    }

    void HeteroCpuApply_Click(object sender, RoutedEventArgs e) {
      if (HeteroCpuToggle.IsChecked != true) return;
      HeteroCpuService.WriteSmallProcessorMask(ConfigService.HeteroCpuSmallMask);
      HeteroCpuService.WriteDefaultPolicy(ConfigService.HeteroCpuDefaultPolicy);
      HeteroCpuService.WriteExpectedRuntime(ConfigService.HeteroCpuExpectedRuntime);
      HeteroCpuService.WriteImportantPolicy(ConfigService.HeteroCpuImportantPolicy);
      HeteroCpuService.WriteImportantShortPolicy(ConfigService.HeteroCpuImportantShortPolicy);
      HeteroCpuService.WritePolicyMask(ConfigService.HeteroCpuPolicyMask);
	      HeteroCpuService.WriteImportantPriority(ConfigService.HeteroCpuImportantPriority);
	      // Also write per-plan settings
	      if (GetActiveSchemeGuid() is Guid activeScheme) {
	        WritePwrValueBoth(activeScheme, GUID_HETERO_THREAD_SCHED, 2, 5);
	        WritePwrValueBoth(activeScheme, GUID_HETERO_SHORT_SCHED, 2, 5);
	        WritePwrValueBoth(activeScheme, GUID_HETERO_ACTIVE_POLICY, 0, 4);
	      }
	      DialogHelper.Info(Strings.HeteroCpuApplyResult, Strings.HeteroCpuApplyTitle);
    }

    void HeteroCpuRestore_Click(object sender, RoutedEventArgs e) {
      HeteroCpuService.RemoveAll();
      HeteroCpuToggle.IsChecked = false;
      HeteroCpuDetails.Visibility = Visibility.Collapsed;
      ConfigService.HeteroCpuSmallMask = "FFFF0000";
      ConfigService.HeteroCpuDefaultPolicy = 2;
      ConfigService.HeteroCpuExpectedRuntime = 1450;
      ConfigService.HeteroCpuImportantPolicy = 2;
      ConfigService.HeteroCpuImportantShortPolicy = 3;
      ConfigService.HeteroCpuPolicyMask = 7;
      ConfigService.HeteroCpuImportantPriority = 8;
      ConfigService.Save("HeteroCpuSmallMask");
      ConfigService.Save("HeteroCpuDefaultPolicy");
      ConfigService.Save("HeteroCpuExpectedRuntime");
      ConfigService.Save("HeteroCpuImportantPolicy");
      ConfigService.Save("HeteroCpuImportantShortPolicy");
      ConfigService.Save("HeteroCpuPolicyMask");
      ConfigService.Save("HeteroCpuImportantPriority");
      DialogHelper.Info(Strings.HeteroCpuRestoreResult, Strings.HeteroCpuRestoreTitle);
    }

    void HeteroCpuDetect_Click(object sender, RoutedEventArgs e) {
      var (supported, totalLp, ccd0Lp, maskHex) = HeteroCpuService.DetectDualCcd();
      if (!supported) {
        DialogHelper.Info(Strings.HeteroCpuNotDetected, Strings.HeteroCpuDetectTitle);
        return;
      }
      string msg = Strings.HeteroCpuDetectConfirm(totalLp.ToString(), ccd0Lp.ToString(), (totalLp - ccd0Lp).ToString(), maskHex);
      if (DialogHelper.Confirm(msg, Strings.HeteroCpuDetectTitle)) {
        HeteroCpuMaskBox.Text = maskHex;
        ConfigService.HeteroCpuSmallMask = maskHex;
        ConfigService.Save("HeteroCpuSmallMask");
        ConfigService.HeteroCpuDefaultPolicy = 2;
        ConfigService.HeteroCpuExpectedRuntime = 1450;
        ConfigService.HeteroCpuImportantPolicy = 2;
        ConfigService.HeteroCpuImportantShortPolicy = 3;
        ConfigService.HeteroCpuPolicyMask = 7;
        ConfigService.HeteroCpuImportantPriority = 8;
        ConfigService.Save("HeteroCpuDefaultPolicy");
        ConfigService.Save("HeteroCpuExpectedRuntime");
        ConfigService.Save("HeteroCpuImportantPolicy");
        ConfigService.Save("HeteroCpuImportantShortPolicy");
        ConfigService.Save("HeteroCpuPolicyMask");
        ConfigService.Save("HeteroCpuImportantPriority");
        _loading = true;
        SelectPolicy(HeteroCpuDefaultPolicyCombo, 2, HeteroPolicyValues);
        SelectPolicy(HeteroCpuImportantPolicyCombo, 2, HeteroPolicyValues);
        SelectPolicy(HeteroCpuImportantShortCombo, 3, HeteroPolicyValues);
        SelectPolicy(HeteroCpuPolicyMaskCombo, 7, HeteroMaskValues);
        HeteroCpuRuntimeBox.Text = "1450";
        HeteroCpuPriorityBox.Text = "8";
        _loading = false;
        HeteroCpuService.WriteSmallProcessorMask(maskHex);
        HeteroCpuService.WriteDefaultPolicy(2);
        HeteroCpuService.WriteExpectedRuntime(1450);
        HeteroCpuService.WriteImportantPolicy(2);
        HeteroCpuService.WriteImportantShortPolicy(3);
        HeteroCpuService.WritePolicyMask(7);
        HeteroCpuService.WriteImportantPriority(8);
        HeteroCpuToggle.IsChecked = true;
        HeteroCpuDetails.Visibility = Visibility.Visible;
        DialogHelper.Info(Strings.HeteroCpuDetectResult, Strings.HeteroCpuDetectTitle);
      }
    }

    // ── AMD APU Power Tuning (STAPM / Fast / Slow PPT) ──
    void ApuStapmNum_ValueChanged(object s, RoutedEventArgs e) {
      if (_loading) return;
      double? v = ApuStapmNum.Value;
      if (v == null) return;
      int watts = (int)v;
      ConfigService.AmdStapmLimit = watts;
      ConfigService.Save("AmdStapmLimit");
      if (AmdAdvancedService.IsAvailable) {
        bool ok = AmdAdvancedService.SetStapmLimit((uint)(watts * 1000));
        ApuPowerStatus.Text = ok ? $"STAPM={watts}W ✓" : "SMU 写入失败";
      }
    }
    void ApuFastNum_ValueChanged(object s, RoutedEventArgs e) {
      if (_loading) return;
      double? v = ApuFastNum.Value;
      if (v == null) return;
      int watts = (int)v;
      ConfigService.AmdFastLimit = watts;
      ConfigService.Save("AmdFastLimit");
      if (AmdAdvancedService.IsAvailable) {
        bool ok = AmdAdvancedService.SetFastLimit((uint)(watts * 1000));
        ApuPowerStatus.Text = ok ? $"Fast={watts}W ✓" : "SMU 写入失败";
      }
    }
    void ApuSlowNum_ValueChanged(object s, RoutedEventArgs e) {
      if (_loading) return;
      double? v = ApuSlowNum.Value;
      if (v == null) return;
      int watts = (int)v;
      ConfigService.AmdSlowLimit = watts;
      ConfigService.Save("AmdSlowLimit");
      if (AmdAdvancedService.IsAvailable) {
        bool ok = AmdAdvancedService.SetSlowLimit((uint)(watts * 1000));
        ApuPowerStatus.Text = ok ? $"Slow={watts}W ✓" : "SMU 写入失败";
      }
    }
    void ApuStapmTimeNum_ValueChanged(object s, RoutedEventArgs e) {
      if (_loading) return;
      double? v = ApuStapmTimeNum.Value;
      if (v == null) return;
      int sec = (int)v;
      ConfigService.AmdStapmTime = sec;
      ConfigService.Save("AmdStapmTime");
      if (AmdAdvancedService.IsAvailable)
        AmdAdvancedService.SetStapmTime((uint)sec);
    }
    void ApuSlowTimeNum_ValueChanged(object s, RoutedEventArgs e) {
      if (_loading) return;
      double? v = ApuSlowTimeNum.Value;
      if (v == null) return;
      int sec = (int)v;
      ConfigService.AmdSlowTime = sec;
      ConfigService.Save("AmdSlowTime");
      if (AmdAdvancedService.IsAvailable)
        AmdAdvancedService.SetSlowTime((uint)sec);
    }

    // ── AMD APU VRM Current Limits ──
    void ApuVrmCurrentNum_ValueChanged(object s, RoutedEventArgs e) {
      if (_loading) return;
      double? v = ApuVrmCurrentNum.Value; if (v == null) return;
      int a = (int)v;
      ConfigService.AmdVrmCurrent = a; ConfigService.Save("AmdVrmCurrent");
      if (AmdAdvancedService.IsAvailable) { bool ok = AmdAdvancedService.SetVrmCurrent((uint)(a * 1000)); ApuVrmStatus.Text = ok ? $"CPU TDC={a}A ✓" : "SMU 写入失败"; }
    }
    void ApuVrmSocCurrentNum_ValueChanged(object s, RoutedEventArgs e) {
      if (_loading) return;
      double? v = ApuVrmSocCurrentNum.Value; if (v == null) return;
      int a = (int)v;
      ConfigService.AmdVrmSocCurrent = a; ConfigService.Save("AmdVrmSocCurrent");
      if (AmdAdvancedService.IsAvailable) { bool ok = AmdAdvancedService.SetVrmSocCurrent((uint)(a * 1000)); ApuVrmStatus.Text = ok ? $"SoC TDC={a}A ✓" : "SMU 写入失败"; }
    }
    void ApuVrmMaxNum_ValueChanged(object s, RoutedEventArgs e) {
      if (_loading) return;
      double? v = ApuVrmMaxNum.Value; if (v == null) return;
      int a = (int)v;
      ConfigService.AmdVrmMaxCurrent = a; ConfigService.Save("AmdVrmMaxCurrent");
      if (AmdAdvancedService.IsAvailable) { bool ok = AmdAdvancedService.SetVrmMaxCurrent((uint)(a * 1000)); ApuVrmStatus.Text = ok ? $"CPU EDC={a}A ✓" : "SMU 写入失败"; }
    }
    void ApuVrmSocMaxNum_ValueChanged(object s, RoutedEventArgs e) {
      if (_loading) return;
      double? v = ApuVrmSocMaxNum.Value; if (v == null) return;
      int a = (int)v;
      ConfigService.AmdVrmSocMaxCurrent = a; ConfigService.Save("AmdVrmSocMaxCurrent");
      if (AmdAdvancedService.IsAvailable) { bool ok = AmdAdvancedService.SetVrmSocMaxCurrent((uint)(a * 1000)); ApuVrmStatus.Text = ok ? $"SoC EDC={a}A ✓" : "SMU 写入失败"; }
    }

    // ── AMD APU Temperature Limits ──
    void ApuTctlNum_ValueChanged(object s, RoutedEventArgs e) {
      if (_loading) return;
      double? v = ApuTctlNum.Value; if (v == null) return;
      int t = (int)v;
      ConfigService.AmdTctlTemp = t; ConfigService.Save("AmdTctlTemp");
      if (AmdAdvancedService.IsAvailable) { bool ok = AmdAdvancedService.SetTctlTemp((uint)t); ApuTempStatus.Text = ok ? $"Tctl={t}°C ✓" : "SMU 写入失败"; }
    }
    void ApuSkinTempNum_ValueChanged(object s, RoutedEventArgs e) {
      if (_loading) return;
      double? v = ApuSkinTempNum.Value; if (v == null) return;
      int t = (int)v;
      ConfigService.AmdApuSkinTemp = t; ConfigService.Save("AmdApuSkinTemp");
      if (AmdAdvancedService.IsAvailable) { bool ok = AmdAdvancedService.SetApuSkinTemp((uint)t); ApuTempStatus.Text = ok ? $"APU Skin={t}°C ✓" : "SMU 写入失败"; }
    }
    void ApuDgpuSkinNum_ValueChanged(object s, RoutedEventArgs e) {
      if (_loading) return;
      double? v = ApuDgpuSkinNum.Value; if (v == null) return;
      int t = (int)v;
      ConfigService.AmdDgpuSkinTemp = t; ConfigService.Save("AmdDgpuSkinTemp");
      if (AmdAdvancedService.IsAvailable) { bool ok = AmdAdvancedService.SetDgpuSkinTemp((uint)t); ApuTempStatus.Text = ok ? $"dGPU Skin={t}°C ✓" : "SMU 写入失败"; }
    }

    // ── AMD APU iGPU Clock Override ──
    void ApuGfxClkNum_ValueChanged(object s, RoutedEventArgs e) {
      if (_loading) return;
      double? v = ApuGfxClkNum.Value; if (v == null) return;
      int mhz = (int)v;
      ConfigService.AmdGfxClk = mhz; ConfigService.Save("AmdGfxClk");
      if (AmdAdvancedService.IsAvailable) {
        bool ok = mhz == 0 || AmdAdvancedService.SetGfxClk((uint)mhz);
        ApuGfxClkStatus.Text = mhz == 0 ? "自动 (未覆盖)" : (ok ? $"{mhz} MHz ✓" : "SMU 写入失败");
      }
    }

    // ── AMD CPU Power Limits (PPT / TDC / EDC) ──
    void AmdCpuPptNum_ValueChanged(object s, RoutedEventArgs e) {
      if (_loading) return;
      double? v = AmdCpuPptNum.Value; if (v == null) return;
      int watts = (int)v;
      ConfigService.AmdCpuPpt = watts; ConfigService.Save("AmdCpuPpt");
      // ponytail: PPT 优先 WMI，降级 SMU
      bool pptOk = false;
      if (watts <= 255) pptOk = SetCpuPowerLimit((byte)watts);
      if (!pptOk && AmdAdvancedService.IsAvailable) pptOk = AmdAdvancedService.SetPptLimit((uint)(watts * 1000));
      AmdCpuPowerStatus.Text = pptOk ? $"PPT={watts}W ✓" : "SMU 写入失败";
    }
    void AmdCpuTdcNum_ValueChanged(object s, RoutedEventArgs e) {
      if (_loading) return;
      double? v = AmdCpuTdcNum.Value; if (v == null) return;
      int amps = (int)v;
      ConfigService.AmdCpuTdc = amps; ConfigService.Save("AmdCpuTdc");
      if (AmdAdvancedService.IsAvailable) {
        bool ok = AmdAdvancedService.SetTdcLimit((uint)(amps * 1000));
        AmdCpuPowerStatus.Text = ok ? $"TDC={amps}A ✓" : "SMU 写入失败";
      }
    }
    void AmdCpuEdcNum_ValueChanged(object s, RoutedEventArgs e) {
      if (_loading) return;
      double? v = AmdCpuEdcNum.Value; if (v == null) return;
      int amps = (int)v;
      ConfigService.AmdCpuEdc = amps; ConfigService.Save("AmdCpuEdc");
      if (AmdAdvancedService.IsAvailable) {
        bool ok = AmdAdvancedService.SetEdcLimit((uint)(amps * 1000));
        AmdCpuPowerStatus.Text = ok ? $"EDC={amps}A ✓" : "SMU 写入失败";
      }
    }

    // ── AMD CPU Temperature Limit (Tctl hard throttle) ──
    void AmdCpuTctlNum_ValueChanged(object s, RoutedEventArgs e) {
      if (_loading) return;
      double? v = AmdCpuTctlNum.Value; if (v == null) return;
      int t = (int)v;
      ConfigService.AmdCpuTctl = t; ConfigService.Save("AmdCpuTctl");
      if (AmdAdvancedService.IsAvailable) {
        bool ok = AmdAdvancedService.SetTctlTemp((uint)t);
        AmdCpuTempStatus.Text = ok ? $"Tctl={t}°C ✓" : "SMU 写入失败";
      }
    }

    // ── Per-Core Curve Optimiser (CCD0/CCD1, Core 0-23) ──
    void CcdCo_ValueChanged(object s, RoutedEventArgs e) {
      if (_loading) return;
      var slider = s as Slider;
      if (slider == null) return;
      // Tag encodes: ccd * 100 + coreLocal (0-11)
      int tag = (int)((slider.Tag as int?) ?? 0);
      int ccd = tag / 100;
      int coreLocal = tag % 100;
      if (ccd < 0 || ccd > 1 || coreLocal < 0 || coreLocal > 11) return;
      int globalCore = ccd * 12 + coreLocal;
      int offset = (int)slider.Value;
      if (offset < -50 || offset > 30) return;
      ConfigService.CoPerCore[globalCore] = offset;
      ConfigService.Save("CoPerCore");
      if (AmdAdvancedService.IsAvailable) {
        bool ok = AmdAdvancedService.SetCurveOptimizerPerCore(ccd, coreLocal, offset);
        // Find status TextBlock in same rowPanel
        if (slider.Parent is System.Windows.Controls.Grid numSliderRow &&
            numSliderRow.Parent is StackPanel rowPanel) {
          foreach (var child in rowPanel.Children) {
            if (child is TextBlock tb && (tb.Name == $"co_status_{ccd}_{coreLocal}" ||
                tb.Text.Contains($"CCD{ccd}"))) {
              tb.Text = ok ? $"CCD{ccd} Core{globalCore}={offset} ✓" : "SMU 写入失败";
              break;
            }
          }
        }
      }
    }

    /// <summary>Dynamically build 12-core slider rows into Ccd1CoPanel / Ccd2CoPanel</summary>
    void BuildPerCoreCoPanels() {
      var specs = new (StackPanel panel, int ccd, int coreStart)[] {
        (Ccd1CoPanel, 0, 0),
        (Ccd2CoPanel, 1, 12)
      };
      foreach (var (panel, ccd, coreStart) in specs) {
        panel.Children.Clear();
        for (int ci = 0; ci < 12; ci++) {
          int globalCore = ccd * 12 + ci;
          int coreNumber = coreStart + ci;
          int savedOffset = globalCore < ConfigService.CoPerCore.Length ? ConfigService.CoPerCore[globalCore] : 0;

          var rowPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };

          // Label
          rowPanel.Children.Add(new TextBlock {
            Text = $"Core {coreNumber}", FontSize = 11,
            Foreground = (System.Windows.Media.Brush)FindResource("TextFillColorTertiaryBrush"),
            Margin = new Thickness(0, 4, 0, 2)
          });

          // NumberBox + Slider row
          var numSliderRow = new System.Windows.Controls.Grid();
          numSliderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
          numSliderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

          var sliderCtrl = new Slider {
            Minimum = -50, Maximum = 30, TickFrequency = 1,
            IsSnapToTickEnabled = true, Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center, Value = savedOffset,
            Tag = ccd * 100 + ci  // encode ccd:localCore in Tag
          };
          sliderCtrl.ValueChanged += CcdCo_ValueChanged;

          var numBox = new Wpf.Ui.Controls.NumberBox {
            Minimum = -50, Maximum = 30, SmallChange = 1, MaxDecimalPlaces = 0, Value = savedOffset
          };
          numBox.SetBinding(Wpf.Ui.Controls.NumberBox.ValueProperty,
            new System.Windows.Data.Binding("Value") {
              Source = sliderCtrl, Mode = System.Windows.Data.BindingMode.TwoWay
            });

          Grid.SetColumn(numBox, 0); Grid.SetColumn(sliderCtrl, 1);
          numSliderRow.Children.Add(numBox);
          numSliderRow.Children.Add(sliderCtrl);
          rowPanel.Children.Add(numSliderRow);

          // Status line
          rowPanel.Children.Add(new TextBlock {
            Name = $"co_status_{ccd}_{ci}",
            Text = AmdAdvancedService.IsAvailable ? "" : Strings.FeaturePartialImpl,
            FontSize = 10,
            Foreground = (System.Windows.Media.Brush)FindResource("TextFillColorTertiaryBrush"),
            Margin = new Thickness(0, 2, 0, 0)
          });

          panel.Children.Add(rowPanel);
        }
      }
    }

    // ── NVIDIA V-F Curve Interactive Editor (MSI Afterburner style) ──

    /// <summary>从 GPU 读取原始曲线，加载到图表编辑器</summary>
    void VfLoadBtn_Click(object sender, RoutedEventArgs e) {
      NvVfCurveStatus.Text = "正在读取 V-F 曲线...";
      System.Threading.ThreadPool.QueueUserWorkItem(_ => {
        if (GpuAppManager.TryGetVfCurve(out var curve)) {
          Dispatcher.Invoke(() => {
            VfChart.LoadOriginalCurve(curve);
            NvVfCurveStatus.Text = $"已加载 {curve.Count} 个曲线点 — 拖拽点调频、Ctrl+点击锁平高频段";
          });
        } else {
          Dispatcher.Invoke(() => {
            NvVfCurveStatus.Text = "无法读取 V-F 曲线，请确认 NVIDIA 驱动已加载";
          });
        }
      });
    }

    /// <summary>用户在图表上编辑了曲线 — 更新状态栏显示编辑点数</summary>
    void VfChart_CurveChanged() {
      if (!VfChart.HasData) return;
      var freqs = VfChart.GetDesiredFrequencies();
      int edited = freqs.Count(f => f.HasValue);
      NvVfCurveStatus.Text = edited > 0
          ? $"已编辑 {edited} 个点 — 点击 [应用曲线] 写入 GPU"
          : "曲线无修改";
    }

    /// <summary>将用户编辑的曲线逐点写回 GPU，写后自动回读验证并刷新图表</summary>
    void VfApplyBtn_Click(object sender, RoutedEventArgs e) {
      if (!VfChart.HasData) {
        NvVfCurveStatus.Text = "请先读取 V-F 曲线";
        return;
      }
      var freqs = VfChart.GetDesiredFrequencies();
      if (!freqs.Any(f => f.HasValue)) {
        NvVfCurveStatus.Text = "曲线无修改，无需应用";
        return;
      }
      NvVfCurveStatus.Text = "正在写入 V-F 曲线...";
      System.Threading.ThreadPool.QueueUserWorkItem(_ => {
        int result = GpuAppManager.ApplyVfCurveFromUserEdits(freqs, out int wrote, out int verified);
        Dispatcher.Invoke(() => {
          // Status based on verification level
          if (result == 2)
            NvVfCurveStatus.Text = $"应用成功 — 写入 {wrote} 点，回读验证 {verified}/{wrote} 点匹配";
          else if (result == 1)
            NvVfCurveStatus.Text = $"写入 {wrote} 点但回读验证仅 {verified} 点匹配 — GPU 可能不支持 V-F 曲线编辑（OEM 锁）";
          else if (result == 0)
            NvVfCurveStatus.Text = "曲线无修改";
          else
            NvVfCurveStatus.Text = "V-F 曲线写入失败，请检查 NVIDIA 驱动";

          // Always re-read the actual GPU curve to show ground truth
          RefreshVfChartFromGpu();
        });
      });
    }

    /// <summary>从 GPU 重新读取曲线刷新图表（保留用户编辑参考）</summary>
    void RefreshVfChartFromGpu() {
      if (GpuAppManager.TryGetVfCurve(out var curve) && curve.Count >= 2) {
        VfChart.LoadOriginalCurve(curve);
      }
    }

    /// <summary>恢复默认 V-F 曲线 — 所有偏移归零</summary>
    void VfResetBtn_Click(object sender, RoutedEventArgs e) {
      NvVfCurveStatus.Text = "正在恢复默认 V-F 曲线...";
      System.Threading.ThreadPool.QueueUserWorkItem(_ => {
        bool ok = GpuAppManager.ResetVfCurve();
        Dispatcher.Invoke(() => {
          if (ok) {
            VfChart.ResetEdits();
            NvVfCurveStatus.Text = "已恢复默认 V-F 曲线（所有偏移归零）";
          } else {
            NvVfCurveStatus.Text = "恢复默认失败，请检查 NVIDIA 驱动";
          }
        });
      });
    }

    // ── NVIDIA Power Limit (NVML) ──
    void NvPowerNum_ValueChanged(object s, RoutedEventArgs e) {
      if (_loading) return;
      _loading = true;
      int v = (int)NvPowerSlider.Value;
      ConfigService.NvPowerLimit = v;
      ConfigService.Save("NvPowerLimit");
      GpuAppManager.SetPowerLimit(v);
      NvPowerStatus.Text = $"功耗墙已设为 {v}W";
      _loading = false;
    }

    // ── FIVR Undervolt handlers ──
    void ApplyFivr() {
      if (!IntelAdvancedService.IsAvailable) return;
      _ = IntelAdvancedService.ApplyValuesAsync(new[] {
        (24u, (decimal)ConfigService.FivrCoreOffset),
        (25u, (decimal)ConfigService.FivrCacheOffset),
        (26u, (decimal)ConfigService.FivrIgpuOffset),
        (27u, (decimal)ConfigService.FivrSaOffset)
      });
    }
    void FivrCoreNum_ValueChanged(object s, RoutedEventArgs e) { _loading = true; var v = (int)(FivrCoreNum.Value ?? 0); ConfigService.FivrCoreOffset = v; ConfigService.Save("FivrCoreOffset"); ApplyFivr(); _loading = false; }
    void FivrCacheNum_ValueChanged(object s, RoutedEventArgs e) { _loading = true; var v = (int)(FivrCacheNum.Value ?? 0); ConfigService.FivrCacheOffset = v; ConfigService.Save("FivrCacheOffset"); ApplyFivr(); _loading = false; }
    void FivrIgpuNum_ValueChanged(object s, RoutedEventArgs e) { _loading = true; var v = (int)(FivrIgpuNum.Value ?? 0); ConfigService.FivrIgpuOffset = v; ConfigService.Save("FivrIgpuOffset"); ApplyFivr(); _loading = false; }
    void FivrSaNum_ValueChanged(object s, RoutedEventArgs e) { _loading = true; var v = (int)(FivrSaNum.Value ?? 0); ConfigService.FivrSaOffset = v; ConfigService.Save("FivrSaOffset"); ApplyFivr(); _loading = false; }

    // ── Clock Ratio (per-core) ──
    Slider[] _coreRatioSliders = Array.Empty<Slider>();
    bool _settingCoreRatios;

    void BuildClockRatioControls() {
      var panel = ClockRatioPanel;
      panel.Children.Clear();

      int coreCount = IntelAdvancedService.CoreCount;
      if (coreCount <= 1) {
        var sp = new StackPanel();
        var label = new TextBlock { Text = "Global", FontSize = 11, Margin = new Thickness(0, 0, 0, 2),
          Foreground = (System.Windows.Media.Brush)FindResource("TextFillColorTertiaryBrush") };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var numBox = new Wpf.Ui.Controls.NumberBox { Minimum = 4, Maximum = 83, SmallChange = 1, MaxDecimalPlaces = 0 };
        var slider = new Slider { Minimum = 4, Maximum = 83, TickFrequency = 1, IsSnapToTickEnabled = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        numBox.SetBinding(Wpf.Ui.Controls.NumberBox.ValueProperty, new System.Windows.Data.Binding("Value") { Source = slider, Mode = System.Windows.Data.BindingMode.TwoWay });
        slider.ValueChanged += (s, e) => {
          if (_loading) return;
          int v = (int)e.NewValue;
          ConfigService.ClockRatio = v;
          ConfigService.Save("ClockRatio");
          if (IntelAdvancedService.IsAvailable)
            _ = IntelAdvancedService.SetClockRatioAsync(v);
        };
        Grid.SetColumn(numBox, 0); Grid.SetColumn(slider, 1);
        grid.Children.Add(numBox); grid.Children.Add(slider);
        sp.Children.Add(label); sp.Children.Add(grid);
        panel.Children.Add(sp);
        slider.Value = ConfigService.ClockRatio;
        return;
      }

      var parts = ConfigService.PerCoreRatios.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
      int[] saved = parts.Length >= coreCount
        ? parts.Take(coreCount).Select(s => int.TryParse(s, out var x) ? x : ConfigService.ClockRatio).ToArray()
        : Enumerable.Repeat(ConfigService.ClockRatio, coreCount).ToArray();

      _coreRatioSliders = new Slider[coreCount];

      for (int i = 0; i < coreCount; i++) {
        int idx = i;
        var sp = new StackPanel { Margin = i < coreCount - 1 ? new Thickness(0, 0, 0, 8) : new Thickness(0) };
        var label = new TextBlock { Text = $"Core {i}", FontSize = 11, Margin = new Thickness(0, 0, 0, 2),
          Foreground = (System.Windows.Media.Brush)FindResource("TextFillColorTertiaryBrush") };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var numBox = new Wpf.Ui.Controls.NumberBox { Minimum = 4, Maximum = 83, SmallChange = 1, MaxDecimalPlaces = 0 };
        var slider = new Slider { Minimum = 4, Maximum = 83, TickFrequency = 1, IsSnapToTickEnabled = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        numBox.SetBinding(Wpf.Ui.Controls.NumberBox.ValueProperty, new System.Windows.Data.Binding("Value") { Source = slider, Mode = System.Windows.Data.BindingMode.TwoWay });
        slider.ValueChanged += (s, e) => CoreRatioSlider_ValueChanged(idx, (int)e.NewValue);
        Grid.SetColumn(numBox, 0); Grid.SetColumn(slider, 1);
        grid.Children.Add(numBox); grid.Children.Add(slider);
        sp.Children.Add(label); sp.Children.Add(grid);
        panel.Children.Add(sp);
        _coreRatioSliders[idx] = slider;
        slider.Value = saved[i];
      }
    }

    void CoreRatioSlider_ValueChanged(int coreIndex, int value) {
      if (_loading || _settingCoreRatios) return;
      _settingCoreRatios = true;
      int coreCount = _coreRatioSliders.Length;
      var all = new int[coreCount];
      for (int i = 0; i < coreCount; i++)
        all[i] = (int)_coreRatioSliders[i].Value;
      ConfigService.PerCoreRatios = string.Join(",", all);
      ConfigService.Save("PerCoreRatios");
      if (IntelAdvancedService.IsAvailable)
        _ = IntelAdvancedService.SetPerCoreRatiosAsync(all);
      _settingCoreRatios = false;
    }

    // ── Power Balance ──
    void UpdatePowerBalanceLabel() {
      int v = (int)PowerBalanceSlider.Value;
      PowerBalanceValue.Text = $"CPU={v}  GPU={31 - v}  (0=全GPU, 31=全CPU)";
    }
    void PowerBalanceSlider_ValueChanged(object s, RoutedPropertyChangedEventArgs<double> e) {
      if (_loading) return;
      int v = (int)e.NewValue;
      ConfigService.PowerBalance = v;
      ConfigService.Save("PowerBalance");
      UpdatePowerBalanceLabel();
      if (IntelAdvancedService.IsAvailable)
        _ = IntelAdvancedService.SetPowerBalanceAsync(v);
    }

    // ── RTSS Frame Limit ──
    void RtssNum_ValueChanged(object s, RoutedEventArgs e) {
      if (_loading) return;
      _loading = true;
      double? val = RtssNum.Value;
      if (val == null || val < 0 || val > 999) { _loading = false; return; }
      int v = (int)val;
      ConfigService.RtssFrameLimit = v;
      ConfigService.Save("RtssFrameLimit");
      // Fallback: use NvAPI FRL if NVIDIA GPU present (works without RTSS)
      if (HasNvidiaGpu())
        HP.Omen.Core.Common.NVidiaApi.NvApiWrapper.NVAPI_SetMaxFrameRate(v);
      _loading = false;
    }

    // ── AutoOC Adaptive Undervolt ──
    void AutoOcToggle_Changed(object sender, RoutedEventArgs e) {
      bool on = AutoOcToggle.IsChecked == true;
      ConfigService.AutoOcEnabled = on;
      ConfigService.Save("AutoOcEnabled");
      if (on) {
        if (AmdAdvancedService.IsAvailable)
          _ = AmdAdvancedService.SetAutoOcOffsetAsync(ConfigService.CoAllCoreOffset > 0 ? ConfigService.CoAllCoreOffset : -ConfigService.CoAllCoreOffset);
        AutoOcStatus.Text = "AutoOC 已启用 (SDK)";
      } else {
        AutoOcStatus.Text = "已禁用";
      }
    }

    // ══ UXTU / PawnIO MSR card handlers ══

    void PawnTurboToggle_Changed(object sender, RoutedEventArgs e) {
      if (_loading) return;
      bool on = PawnTurboToggle.IsChecked == true;
      ConfigService.PawnTurboEnabled = on;
      ConfigService.Save("PawnTurboEnabled");
      if (IntelAdvancedService.IsAvailable) {
        _ = IntelAdvancedService.SetTurboBoostAsync(on);
        PawnTurboStatus.Text = on ? "睿频已启用" : "睿频已关闭";
      }
    }

    void PawnProchotNum_ValueChanged(object s, RoutedEventArgs e) {
      if (_loading) return;
      _loading = true;
      int v = (int)PawnProchotSlider.Value;
      ConfigService.PawnProchotOffset = v;
      ConfigService.Save("PawnProchotOffset");
      if (IntelAdvancedService.IsAvailable)
        _ = IntelAdvancedService.SetProchotOffsetAsync(v);
      _loading = false;
    }

    // ── HWP EPP ──
    void PawnHwpNum_ValueChanged(object s, RoutedEventArgs e) {
      if (_loading) return;
      _loading = true;
      int v = (int)PawnHwpSlider.Value;
      ConfigService.PawnHwpEpp = v;
      ConfigService.Save("PawnHwpEpp");
      if (IntelAdvancedService.IsAvailable) {
        _ = IntelAdvancedService.SetHwpEppAsync(v);
        PawnHwpStatus.Text = $"EPP={v}";
      }
      _loading = false;
    }

    // ── C-State Limit ──
    void PawnCStateCombo_Changed(object s, SelectionChangedEventArgs e) {
      if (_loading) return;
      var item = PawnCStateCombo.SelectedItem as ComboBoxItem;
      if (item == null) return;
      int v = (int)item.Tag;
      ConfigService.PawnCStateLimit = v;
      ConfigService.Save("PawnCStateLimit");
      if (IntelAdvancedService.IsAvailable) {
        _ = IntelAdvancedService.SetCStateLimitAsync(v);
        PawnCStateStatus.Text = $"C-State Max={v}";
      }
    }

    // ── iGPU Power Limit ──
    void PawnIgpuPowerNum_ValueChanged(object s, RoutedEventArgs e) {
      if (_loading) return;
      _loading = true;
      int v = (int)PawnIgpuPowerSlider.Value;
      ConfigService.PawnIgpuPower = v;
      ConfigService.Save("PawnIgpuPower");
      if (IntelAdvancedService.IsAvailable)
        _ = IntelAdvancedService.SetIgpuPowerLimitAsync(v);
      _loading = false;
    }

    // ── iGPU Max Ratio ──
    void PawnIgpuRatioNum_ValueChanged(object s, RoutedEventArgs e) {
      if (_loading) return;
      _loading = true;
      int v = (int)PawnIgpuRatioSlider.Value;
      ConfigService.PawnIgpuRatio = v;
      ConfigService.Save("PawnIgpuRatio");
      if (IntelAdvancedService.IsAvailable)
        _ = IntelAdvancedService.SetIgpuMaxRatioAsync(v);
      _loading = false;
    }

    // ════════════════════════════════════════════════════════════
    // Power Plan Advanced Settings (EPP, BoostMode, MaxState, MaxFreq, SMT)
    // ════════════════════════════════════════════════════════════

    static readonly Guid SUB_PROCESSOR_GUID = new Guid("54533251-82be-4824-96c1-47b60b740d00");
    // General (all processor classes)
    static readonly Guid GUID_PERFEPP = new Guid("36687f9e-e3a5-4dbf-b1dc-15eb381c6863");
    static readonly Guid GUID_PERFBOOST = new Guid("be337238-0d82-4146-a960-4f3749d470c7");
    static readonly Guid GUID_PROCTHROTTLEMAX = new Guid("bc5038f7-23e0-4960-96da-33abaf5935ec");
    static readonly Guid GUID_PROCFREQMAX = new Guid("75b0ae3f-bce0-45a7-8c89-c9611c25e100");
    static readonly Guid GUID_SMTUNPARK = new Guid("b28a6829-c5f7-444e-8f61-10e24e85c532");
    // Hetero scheduling per-power-plan (AMD dual-CCD simulation)
    static readonly Guid GUID_HETERO_THREAD_SCHED = new Guid("93b8b6dc-0698-4d1c-9ee4-0644e900c85d");
    static readonly Guid GUID_HETERO_SHORT_SCHED = new Guid("bae08b81-2d5e-4688-ad6a-13243356654b");
    static readonly Guid GUID_HETERO_ACTIVE_POLICY = new Guid("7f2f5cfa-f10c-4823-b5e1-e93ae85f46b5");
    // Class 1 (P-cores / first efficiency class)
    static readonly Guid GUID_PERFEPP_CLS1 = new Guid("36687f9e-e3a5-4dbf-b1dc-15eb381c6864");
    static readonly Guid GUID_PROCTHROTTLEMAX_CLS1 = new Guid("bc5038f7-23e0-4960-96da-33abaf5935ed");
    static readonly Guid GUID_PROCFREQMAX_CLS1 = new Guid("75b0ae3f-bce0-45a7-8c89-c9611c25e101");
    bool _pwrPlanLoading;
    bool _pwrIsDC;
    bool _pwrIsClass1;

    int ReadPwrValue(Guid scheme, Guid setting) {
      try {
        Guid sub = SUB_PROCESSOR_GUID;
        uint ret = _pwrIsDC
          ? NativeMethods_Power.PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, out uint val)
          : NativeMethods_Power.PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, out val);
        if (ret == 0) return (int)val;
      } catch { }
      return -1;
    }

    void WritePwrValue(Guid scheme, Guid setting, int value) {
      Guid sub = SUB_PROCESSOR_GUID;
      uint v = (uint)value;
      if (_pwrIsDC)
        NativeMethods_Power.PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, v);
      else
        NativeMethods_Power.PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, v);
    }

    /// <summary>同时写入 AC 和 DC 值</summary>
    void WritePwrValueBoth(Guid scheme, Guid setting, int acVal, int dcVal) {
      Guid sub = SUB_PROCESSOR_GUID;
      NativeMethods_Power.PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, (uint)acVal);
      NativeMethods_Power.PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, (uint)dcVal);
    }

    Guid? GetActiveSchemeGuid() {
      try {
        if (NativeMethods_Power.PowerGetActiveScheme(IntPtr.Zero, out var ptr) == 0) {
          var guid = Marshal.PtrToStructure<Guid>(ptr);
          Marshal.FreeHGlobal(ptr);
          return guid;
        }
      } catch { }
      return null;
    }

    Guid GetSettingGuid(Guid general, Guid class1) {
      return _pwrIsClass1 ? class1 : general;
    }

    void BuildPwrPlanOptions() {
      // EPP presets
      EppCombo.Items.Clear();
      EppCombo.Items.Add(new ComboBoxItem { Content = "极速响应 (0)", Tag = 0 });
      EppCombo.Items.Add(new ComboBoxItem { Content = "偏向性能 (20)", Tag = 20 });
      EppCombo.Items.Add(new ComboBoxItem { Content = "平衡 (50)", Tag = 50 });
      EppCombo.Items.Add(new ComboBoxItem { Content = "偏向省电 (80)", Tag = 80 });
      EppCombo.Items.Add(new ComboBoxItem { Content = "极致省电 (100)", Tag = 100 });

      // Boost Mode presets
      BoostModeCombo.Items.Clear();
      string[] boostNames = { "已禁用 (关闭睿频)", "已启用 (适中)", "高性能 (积极)", "高效率",
                               "高性能高效率", "积极且有保障 (满血)", "高效积极且有保障" };
      for (int i = 0; i < boostNames.Length; i++)
        BoostModeCombo.Items.Add(new ComboBoxItem { Content = boostNames[i], Tag = i });

      // Max Processor State presets
      MaxProcStateCombo.Items.Clear();
      int[] stateVals = { 100, 99, 95, 90, 85, 80 };
      foreach (int v in stateVals)
        MaxProcStateCombo.Items.Add(new ComboBoxItem { Content = v + "%", Tag = v });

      // Max Frequency presets
      MaxFreqCombo.Items.Clear();
      MaxFreqCombo.Items.Add(new ComboBoxItem { Content = "不限制 (自动)", Tag = 0 });

      // SMT Policy presets
      SmtPolicyCombo.Items.Clear();
      string[] smtNames = { "核心 (优先物理核)", "每个线程的核心", "循环配置 (均衡负载)", "顺序" };
      for (int i = 0; i < smtNames.Length; i++)
        SmtPolicyCombo.Items.Add(new ComboBoxItem { Content = smtNames[i], Tag = i });
    }

    void LoadPwrPlanSettings() {
      if (PowerPlanCombo.SelectedItem is ComboBoxItem planItem) {
        string guidStr = planItem.Tag as string;
        if (string.IsNullOrEmpty(guidStr)) return;
        Guid scheme = Guid.Parse(guidStr);
        _pwrPlanLoading = true;

        // EPP — has class 1 variant
        LoadPwrSettingValue(scheme, GetSettingGuid(GUID_PERFEPP, GUID_PERFEPP_CLS1), EppCombo, EppCurrentText);

        // Boost Mode — no class 1 variant
        if (_pwrIsClass1) {
          BoostModeCombo.IsEnabled = false;
          BoostModeCurrentText.Text = "不可用";
        } else
          LoadPwrSettingValue(scheme, GUID_PERFBOOST, BoostModeCombo, BoostModeCurrentText);

        // Max Processor State — has class 1 variant
        LoadPwrSettingValue(scheme, GetSettingGuid(GUID_PROCTHROTTLEMAX, GUID_PROCTHROTTLEMAX_CLS1), MaxProcStateCombo, MaxProcStateCurrentText);

        // Max Frequency — has class 1 variant
        LoadPwrSettingValue(scheme, GetSettingGuid(GUID_PROCFREQMAX, GUID_PROCFREQMAX_CLS1), MaxFreqCombo, MaxFreqCurrentText);

        // SMT — no class 1 variant
        if (_pwrIsClass1) {
          SmtPolicyCombo.IsEnabled = false;
          SmtPolicyCurrentText.Text = "不可用";
        } else
          LoadPwrSettingValue(scheme, GUID_SMTUNPARK, SmtPolicyCombo, SmtPolicyCurrentText);

        _pwrPlanLoading = false;
      }
    }

    void LoadPwrSettingValue(Guid scheme, Guid settingGuid, ComboBox combo, TextBlock statusText) {
      int val = ReadPwrValue(scheme, settingGuid);
      if (val < 0) {
        statusText.Text = "不可用";
        combo.IsEnabled = false;
        return;
      }
      combo.IsEnabled = true;
      statusText.Text = "当前: " + val;
      bool found = false;
      foreach (ComboBoxItem item in combo.Items) {
        if (item.Tag is int tag && tag == val) {
          combo.SelectedItem = item;
          found = true;
          break;
        }
      }
      if (!found && combo.IsEditable) {
        combo.Text = val.ToString();
      }
    }

    void PwrSourceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) {
      if (_pwrPlanLoading) return;
      if (PwrSourceCombo.SelectedIndex >= 0)
        _pwrIsDC = PwrSourceCombo.SelectedIndex == 1;
      LoadPwrPlanSettings();
    }

    void PwrClassCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) {
      if (_pwrPlanLoading) return;
      if (PwrClassCombo.SelectedIndex >= 0)
        _pwrIsClass1 = PwrClassCombo.SelectedIndex == 1;
      LoadPwrPlanSettings();
    }

    void EppCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) {
      if (_pwrPlanLoading) return;
    }
    void BoostModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) {
      if (_pwrPlanLoading) return;
    }
    void MaxProcStateCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) {
      if (_pwrPlanLoading) return;
    }
    void MaxFreqCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) {
      if (_pwrPlanLoading) return;
    }
    void SmtPolicyCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) {
      if (_pwrPlanLoading) return;
    }

    void PwrPlanApply_Click(object sender, RoutedEventArgs e) {
      if (!(PowerPlanCombo.SelectedItem is ComboBoxItem planItem)) {
        PwrPlanStatus.Text = "请先选择电源计划";
        return;
      }
      string guidStr = planItem.Tag as string;
      if (string.IsNullOrEmpty(guidStr)) return;
      Guid scheme = Guid.Parse(guidStr);

      try {
        if (EppCombo.IsEnabled) {
          int eppVal = ParseComboValue(EppCombo, 0, 100);
          WritePwrValue(scheme, GetSettingGuid(GUID_PERFEPP, GUID_PERFEPP_CLS1), eppVal);
        }
        if (BoostModeCombo.IsEnabled && !_pwrIsClass1) {
          if (BoostModeCombo.SelectedItem is ComboBoxItem bi && bi.Tag is int bv)
            WritePwrValue(scheme, GUID_PERFBOOST, bv);
        }
        if (MaxProcStateCombo.IsEnabled) {
          int stateVal = ParseComboValue(MaxProcStateCombo, 0, 100);
          WritePwrValue(scheme, GetSettingGuid(GUID_PROCTHROTTLEMAX, GUID_PROCTHROTTLEMAX_CLS1), stateVal);
        }
        if (MaxFreqCombo.IsEnabled) {
          int freqVal = ParseComboValue(MaxFreqCombo, 0, 99999);
          WritePwrValue(scheme, GetSettingGuid(GUID_PROCFREQMAX, GUID_PROCFREQMAX_CLS1), freqVal);
        }
        if (SmtPolicyCombo.IsEnabled && !_pwrIsClass1) {
          if (SmtPolicyCombo.SelectedItem is ComboBoxItem si && si.Tag is int sv)
            WritePwrValue(scheme, GUID_SMTUNPARK, sv);
        }

        NativeMethods_Power.PowerSetActiveScheme(IntPtr.Zero, ref scheme);
        PwrPlanStatus.Text = "已应用" + (_pwrIsDC ? "直流" : "交流") + "设置";
        LoadPwrPlanSettings();
      } catch (Exception ex) {
        PwrPlanStatus.Text = "应用失败: " + ex.Message;
      }
    }

    static int ParseComboValue(ComboBox combo, int min, int max) {
      if (combo.SelectedItem is ComboBoxItem item && item.Tag is int tag)
        return Math.Max(min, Math.Min(max, tag));
      string text = combo.Text?.Trim().TrimEnd('%') ?? "";
      if (int.TryParse(text, out int val))
        return Math.Max(min, Math.Min(max, val));
      return min;
    }

    void PwrPlanView_Click(object sender, RoutedEventArgs e) {
      try { System.Diagnostics.Process.Start("control.exe", "/name Microsoft.PowerOptions"); } catch { }
    }

    // ──────── PerfPage Preset System ────────
    // ponytail: unified with Dashboard's PresetManager (Extreme/GpuPriority/LightUse/Custom1-3).
    // Switching/ saving here calls PresetManager, which fires OnPresetChanged → OnPresetChanged → LoadStateFast.
    // CapturePreset/ApplyPreset(PerfPreset) retained only for snapshot/undo (btnPerfUndo).

    Models.PerfPreset _snapshot;

    // ponytail: dynamic — built-ins + enumerated custom preset files
    void RefreshPresetList() {
      string current = ConfigService.Preset;
      if (string.IsNullOrEmpty(current)) current = "GpuPriority";
      cbxPerfPreset.Items.Clear();
      var all = PresetManager.EnumerateAllPresets();
      int idx = -1;
      for (int i = 0; i < all.Count; i++) {
        var (display, key) = all[i];
        cbxPerfPreset.Items.Add(new ComboBoxItem { Content = display, Tag = key });
        if (key == current) idx = i;
      }
      _loading = true;
      cbxPerfPreset.SelectedIndex = idx >= 0 ? idx : 1;
      _loading = false;
    }

    Models.PerfPreset CapturePreset() {
      return new Models.PerfPreset {
        CpuPowerIndex = CpuPowerCombo.SelectedIndex,
        CpuPowerPL1 = CpuPowerPL1Num.Value ?? 0,
        CpuPowerPL2 = CpuPowerPL2Num.Value ?? 0,
        IccMaxIndex = IccMaxCombo.SelectedIndex,
        IccMax = IccMaxNum.Value ?? 0,
        AcLoadLineIndex = AcLoadLineCombo.SelectedIndex,
        PowerModeIndex = PowerModeCombo.SelectedIndex,
        PowerPlanIndex = PowerPlanCombo.SelectedIndex,
        PwrSourceIndex = PwrSourceCombo.SelectedIndex,
        PwrClassIndex = PwrClassCombo.SelectedIndex,
        EppIndex = EppCombo.SelectedIndex,
        EppText = EppCombo.Text,
        BoostModeIndex = BoostModeCombo.SelectedIndex,
        MaxProcStateIndex = MaxProcStateCombo.SelectedIndex,
        MaxProcStateText = MaxProcStateCombo.Text,
        MaxFreqIndex = MaxFreqCombo.SelectedIndex,
        MaxFreqText = MaxFreqCombo.Text,
        SmtPolicyIndex = SmtPolicyCombo.SelectedIndex,
        EcoQosOn = EcoQosToggle.IsChecked ?? false,
        EcoQosThrottlePlugged = EcoQosThrottlePluggedToggle.IsChecked ?? false,
        GpuClockIndex = GpuClockCombo.SelectedIndex,
        GpuClock = GpuClockNum.Value ?? 0,
        GpuCoreOCIndex = GpuCoreOCCombo.SelectedIndex,
        GpuCoreOC = GpuCoreOCNum.Value ?? 0,
        GpuMemoryOCIndex = GpuMemoryOCCombo.SelectedIndex,
        GpuMemoryOC = GpuMemoryOCNum.Value ?? 0,
        GfxModeIndex = GfxModeCombo.SelectedIndex,
        DbVersionIndex = DbVersionCombo.SelectedIndex,
        CtgpIndex = CtgpCombo.SelectedIndex,
        PpabOn = PpabCheck.IsChecked ?? false,
        Tpp = TppNum.Value ?? 0,
        DStateIndex = DStateCombo.SelectedIndex,
        FpsIndex = FpsCombo.SelectedIndex,
        Fps = FpsNum.Value ?? 0,
        RefreshRateIndex = RefreshRateCombo.SelectedIndex,
        RefreshRate = RefreshRateNum.Value ?? 0,
        ResolutionIndex = ResolutionCombo.SelectedIndex,
        DpiIndex = DpiCombo.SelectedIndex,
        HdrOn = HdrToggle.IsChecked ?? false,
        PboScalarIndex = PboScalarCombo.SelectedIndex,
        Co = CoNum.Value ?? 0,
        FivrCore = FivrCoreNum.Value ?? 0,
        FivrCache = FivrCacheNum.Value ?? 0,
        FivrIgpu = FivrIgpuNum.Value ?? 0,
        FivrSa = FivrSaNum.Value ?? 0,
        PowerBalance = PowerBalanceSlider.Value,
        PawnTurboOn = PawnTurboToggle.IsChecked ?? false,
        PawnProchot = PawnProchotNum.Value ?? 0,
        PawnHwp = PawnHwpNum.Value ?? 0,
        PawnCStateIndex = PawnCStateCombo.SelectedIndex,
        ApuStapm = ApuStapmNum.Value ?? 0,
        ApuFast = ApuFastNum.Value ?? 0,
        ApuSlow = ApuSlowNum.Value ?? 0,
        ApuStapmTime = ApuStapmTimeNum.Value ?? 0,
        ApuSlowTime = ApuSlowTimeNum.Value ?? 0,
        ApuVrmCurrent = ApuVrmCurrentNum.Value ?? 0,
        ApuVrmSocCurrent = ApuVrmSocCurrentNum.Value ?? 0,
        ApuVrmMax = ApuVrmMaxNum.Value ?? 0,
        ApuVrmSocMax = ApuVrmSocMaxNum.Value ?? 0,
        ApuTctl = ApuTctlNum.Value ?? 0,
        ApuSkinTemp = ApuSkinTempNum.Value ?? 0,
        ApuDgpuSkin = ApuDgpuSkinNum.Value ?? 0,
        ApuGfxClk = ApuGfxClkNum.Value ?? 0,
        AmdCpuPpt = AmdCpuPptNum.Value ?? 0,
        AmdCpuTdc = AmdCpuTdcNum.Value ?? 0,
        AmdCpuEdc = AmdCpuEdcNum.Value ?? 0,
        AmdCpuTctl = AmdCpuTctlNum.Value ?? 0,
        NvPower = NvPowerNum.Value ?? 0,
        Rtss = RtssNum.Value ?? 0,
        AutoOcOn = AutoOcToggle.IsChecked ?? false,
        PawnIgpuPower = PawnIgpuPowerNum.Value ?? 0,
        PawnIgpuRatio = PawnIgpuRatioNum.Value ?? 0,
        // ── Master toggles (full set: existing 7 + new 14) ──
        FivrMasterOn = FivrMasterToggle.IsChecked ?? true,
        ApuPowerMasterOn = ApuPowerMasterToggle.IsChecked ?? true,
        ApuVrmMasterOn = ApuVrmMasterToggle.IsChecked ?? true,
        ApuTempMasterOn = ApuTempMasterToggle.IsChecked ?? true,
        ApuGfxClkMasterOn = ApuGfxClkMasterToggle.IsChecked ?? true,
        PboScalarMasterOn = PboScalarMasterToggle.IsChecked ?? true,
        CoMasterOn = CoMasterToggle.IsChecked ?? true,
        AutoOcMasterOn = AutoOcMasterToggle.IsChecked ?? true,
        ClockRatioMasterOn = ClockRatioMasterToggle.IsChecked ?? true,
        PowerBalanceMasterOn = PowerBalanceMasterToggle.IsChecked ?? true,
        PawnTurboMasterOn = PawnTurboMasterToggle.IsChecked ?? true,
        PawnProchotMasterOn = PawnProchotMasterToggle.IsChecked ?? true,
        PawnHwpMasterOn = PawnHwpMasterToggle.IsChecked ?? true,
        PawnCStateMasterOn = PawnCStateMasterToggle.IsChecked ?? true,
        PawnIgpuPowerMasterOn = PawnIgpuPowerMasterToggle.IsChecked ?? true,
        PawnIgpuRatioMasterOn = PawnIgpuRatioMasterToggle.IsChecked ?? true,
        NvTuningMasterOn = NvTuningMasterToggle.IsChecked ?? true,
        RtssMasterOn = RtssMasterToggle.IsChecked ?? true,
        // CO: per-core + iGPU snapshot
        CoPerCoreCsv = string.Join(",", ConfigService.CoPerCore),
        CoIGpuOffsetSnapshot = (int)(CoIGpuNum.Value ?? 0),
        // ADLX / AMD GPU state — captured as raw numbers, applied as service calls
        AdlxRsrOn = AdlxRsrToggle.IsChecked ?? false,
        AdlxRsrSharpness = (int)(AdlxRsrSharpNum.Value ?? 50),
        AdlxAntiLagOn = AdlxAntiLagToggle.IsChecked ?? false,
        AdlxEnhancedSyncOn = AdlxEnhancedSyncToggle.IsChecked ?? false,
        AdlxBoostOn = AdlxBoostToggle.IsChecked ?? false,
        AdlxBoostPercent = (int)(AdlxBoostNum.Value ?? 0),
        AdlxImageSharpOn = AdlxImageSharpToggle.IsChecked ?? false,
        AdlxImageSharpPercent = (int)(AdlxImageSharpNum.Value ?? 50),
      };
    }

    void ApplyPreset(Models.PerfPreset p) {
      _loading = true;
      CpuPowerCombo.SelectedIndex = Clamp(p.CpuPowerIndex, 0, CpuPowerCombo.Items.Count - 1);
      CpuPowerPL1Num.Value = p.CpuPowerPL1;
      CpuPowerPL2Num.Value = p.CpuPowerPL2;
      IccMaxCombo.SelectedIndex = Clamp(p.IccMaxIndex, 0, IccMaxCombo.Items.Count - 1);
      IccMaxNum.Value = p.IccMax;
      IccMaxSlider.Value = p.IccMax > 0 ? p.IccMax : 0;
      AcLoadLineCombo.SelectedIndex = Clamp(p.AcLoadLineIndex, 0, AcLoadLineCombo.Items.Count - 1);
      PowerModeCombo.SelectedIndex = Clamp(p.PowerModeIndex, 0, PowerModeCombo.Items.Count - 1);
      PowerPlanCombo.SelectedIndex = Clamp(p.PowerPlanIndex, 0, PowerPlanCombo.Items.Count - 1);
      PwrSourceCombo.SelectedIndex = Clamp(p.PwrSourceIndex, 0, PwrSourceCombo.Items.Count - 1);
      PwrClassCombo.SelectedIndex = Clamp(p.PwrClassIndex, 0, PwrClassCombo.Items.Count - 1);
      EppCombo.SelectedIndex = Clamp(p.EppIndex, 0, EppCombo.Items.Count - 1);
      if (!string.IsNullOrEmpty(p.EppText)) EppCombo.Text = p.EppText;
      BoostModeCombo.SelectedIndex = Clamp(p.BoostModeIndex, 0, BoostModeCombo.Items.Count - 1);
      MaxProcStateCombo.SelectedIndex = Clamp(p.MaxProcStateIndex, 0, MaxProcStateCombo.Items.Count - 1);
      if (!string.IsNullOrEmpty(p.MaxProcStateText)) MaxProcStateCombo.Text = p.MaxProcStateText;
      MaxFreqCombo.SelectedIndex = Clamp(p.MaxFreqIndex, 0, MaxFreqCombo.Items.Count - 1);
      if (!string.IsNullOrEmpty(p.MaxFreqText)) MaxFreqCombo.Text = p.MaxFreqText;
      SmtPolicyCombo.SelectedIndex = Clamp(p.SmtPolicyIndex, 0, SmtPolicyCombo.Items.Count - 1);
      if (EcoQosToggle.IsChecked != p.EcoQosOn) EcoQosToggle.IsChecked = p.EcoQosOn;
      if (EcoQosThrottlePluggedToggle.IsChecked != p.EcoQosThrottlePlugged) EcoQosThrottlePluggedToggle.IsChecked = p.EcoQosThrottlePlugged;
      GpuClockCombo.SelectedIndex = Clamp(p.GpuClockIndex, 0, GpuClockCombo.Items.Count - 1);
      GpuClockNum.Value = p.GpuClock;
      GpuClockSlider.Value = p.GpuClock > 0 ? p.GpuClock : 0;
      GpuCoreOCCombo.SelectedIndex = Clamp(p.GpuCoreOCIndex, 0, GpuCoreOCCombo.Items.Count - 1);
      GpuCoreOCNum.Value = p.GpuCoreOC;
      GpuCoreOCSlider.Value = p.GpuCoreOC;
      GpuMemoryOCCombo.SelectedIndex = Clamp(p.GpuMemoryOCIndex, 0, GpuMemoryOCCombo.Items.Count - 1);
      GpuMemoryOCNum.Value = p.GpuMemoryOC;
      GpuMemoryOCSlider.Value = p.GpuMemoryOC;
      GfxModeCombo.SelectedIndex = Clamp(p.GfxModeIndex, 0, GfxModeCombo.Items.Count - 1);
      DbVersionCombo.SelectedIndex = Clamp(p.DbVersionIndex, 0, DbVersionCombo.Items.Count - 1);
      CtgpCombo.SelectedIndex = Clamp(p.CtgpIndex, 0, CtgpCombo.Items.Count - 1);
      if (PpabCheck.IsChecked != p.PpabOn) PpabCheck.IsChecked = p.PpabOn;
      TppNum.Value = p.Tpp;
      TppExtraSlider.Value = p.Tpp > 0 ? p.Tpp : 0;
      DStateCombo.SelectedIndex = Clamp(p.DStateIndex, 0, DStateCombo.Items.Count - 1);
      FpsCombo.SelectedIndex = Clamp(p.FpsIndex, 0, FpsCombo.Items.Count - 1);
      FpsNum.Value = p.Fps;
      FpsSlider.Value = p.Fps > 0 ? p.Fps : 0;
      RefreshRateCombo.SelectedIndex = Clamp(p.RefreshRateIndex, 0, RefreshRateCombo.Items.Count - 1);
      RefreshRateNum.Value = p.RefreshRate;
      ResolutionCombo.SelectedIndex = Clamp(p.ResolutionIndex, 0, ResolutionCombo.Items.Count - 1);
      DpiCombo.SelectedIndex = Clamp(p.DpiIndex, 0, DpiCombo.Items.Count - 1);
      if (HdrToggle.IsChecked != p.HdrOn) HdrToggle.IsChecked = p.HdrOn;
      PboScalarCombo.SelectedIndex = Clamp(p.PboScalarIndex, 0, PboScalarCombo.Items.Count - 1);
      CoNum.Value = p.Co;
      FivrCoreNum.Value = p.FivrCore;
      FivrCacheNum.Value = p.FivrCache;
      FivrIgpuNum.Value = p.FivrIgpu;
      FivrSaNum.Value = p.FivrSa;
      PowerBalanceSlider.Value = p.PowerBalance;
      if (PawnTurboToggle.IsChecked != p.PawnTurboOn) PawnTurboToggle.IsChecked = p.PawnTurboOn;
      PawnProchotNum.Value = p.PawnProchot;
      PawnHwpNum.Value = p.PawnHwp;
      PawnCStateCombo.SelectedIndex = Clamp(p.PawnCStateIndex, 0, PawnCStateCombo.Items.Count - 1);
      ApuStapmNum.Value = p.ApuStapm;
      ApuFastNum.Value = p.ApuFast;
      ApuSlowNum.Value = p.ApuSlow;
      ApuStapmTimeNum.Value = p.ApuStapmTime;
      ApuSlowTimeNum.Value = p.ApuSlowTime;
      ApuVrmCurrentNum.Value = p.ApuVrmCurrent;
      ApuVrmSocCurrentNum.Value = p.ApuVrmSocCurrent;
      ApuVrmMaxNum.Value = p.ApuVrmMax;
      ApuVrmSocMaxNum.Value = p.ApuVrmSocMax;
      ApuTctlNum.Value = p.ApuTctl;
      ApuSkinTempNum.Value = p.ApuSkinTemp;
      ApuDgpuSkinNum.Value = p.ApuDgpuSkin;
      ApuGfxClkNum.Value = p.ApuGfxClk;
      AmdCpuPptNum.Value = p.AmdCpuPpt;
      AmdCpuPptSlider.Value = p.AmdCpuPpt > 0 ? p.AmdCpuPpt : 105;
      AmdCpuTdcNum.Value = p.AmdCpuTdc;
      AmdCpuTdcSlider.Value = p.AmdCpuTdc > 0 ? p.AmdCpuTdc : 80;
      AmdCpuEdcNum.Value = p.AmdCpuEdc;
      AmdCpuEdcSlider.Value = p.AmdCpuEdc > 0 ? p.AmdCpuEdc : 160;
      AmdCpuTctlNum.Value = p.AmdCpuTctl;
      AmdCpuTctlSlider.Value = p.AmdCpuTctl > 0 ? p.AmdCpuTctl : 95;
      NvPowerNum.Value = p.NvPower;
      RtssNum.Value = p.Rtss;
      if (AutoOcToggle.IsChecked != p.AutoOcOn) AutoOcToggle.IsChecked = p.AutoOcOn;
      PawnIgpuPowerNum.Value = p.PawnIgpuPower;
      PawnIgpuRatioNum.Value = p.PawnIgpuRatio;
      // ── Master toggles (full set) ──
      if (FivrMasterToggle.IsChecked != p.FivrMasterOn) FivrMasterToggle.IsChecked = p.FivrMasterOn;
      if (ApuPowerMasterToggle.IsChecked != p.ApuPowerMasterOn) ApuPowerMasterToggle.IsChecked = p.ApuPowerMasterOn;
      if (ApuVrmMasterToggle.IsChecked != p.ApuVrmMasterOn) ApuVrmMasterToggle.IsChecked = p.ApuVrmMasterOn;
      if (ApuTempMasterToggle.IsChecked != p.ApuTempMasterOn) ApuTempMasterToggle.IsChecked = p.ApuTempMasterOn;
      if (ApuGfxClkMasterToggle.IsChecked != p.ApuGfxClkMasterOn) ApuGfxClkMasterToggle.IsChecked = p.ApuGfxClkMasterOn;
      if (PboScalarMasterToggle.IsChecked != p.PboScalarMasterOn) PboScalarMasterToggle.IsChecked = p.PboScalarMasterOn;
      if (CoMasterToggle.IsChecked != p.CoMasterOn) CoMasterToggle.IsChecked = p.CoMasterOn;
      if (AutoOcMasterToggle.IsChecked != p.AutoOcMasterOn) AutoOcMasterToggle.IsChecked = p.AutoOcMasterOn;
      if (ClockRatioMasterToggle.IsChecked != p.ClockRatioMasterOn) ClockRatioMasterToggle.IsChecked = p.ClockRatioMasterOn;
      if (PowerBalanceMasterToggle.IsChecked != p.PowerBalanceMasterOn) PowerBalanceMasterToggle.IsChecked = p.PowerBalanceMasterOn;
      if (PawnTurboMasterToggle.IsChecked != p.PawnTurboMasterOn) PawnTurboMasterToggle.IsChecked = p.PawnTurboMasterOn;
      if (PawnProchotMasterToggle.IsChecked != p.PawnProchotMasterOn) PawnProchotMasterToggle.IsChecked = p.PawnProchotMasterOn;
      if (PawnHwpMasterToggle.IsChecked != p.PawnHwpMasterOn) PawnHwpMasterToggle.IsChecked = p.PawnHwpMasterOn;
      if (PawnCStateMasterToggle.IsChecked != p.PawnCStateMasterOn) PawnCStateMasterToggle.IsChecked = p.PawnCStateMasterOn;
      if (PawnIgpuPowerMasterToggle.IsChecked != p.PawnIgpuPowerMasterOn) PawnIgpuPowerMasterToggle.IsChecked = p.PawnIgpuPowerMasterOn;
      if (PawnIgpuRatioMasterToggle.IsChecked != p.PawnIgpuRatioMasterOn) PawnIgpuRatioMasterToggle.IsChecked = p.PawnIgpuRatioMasterOn;
      if (NvTuningMasterToggle.IsChecked != p.NvTuningMasterOn) NvTuningMasterToggle.IsChecked = p.NvTuningMasterOn;
      if (RtssMasterToggle.IsChecked != p.RtssMasterOn) RtssMasterToggle.IsChecked = p.RtssMasterOn;
      // CO iGPU + per-core restore — apply only if non-empty (legacy presets may not have these)
      if (!string.IsNullOrEmpty(p.CoPerCoreCsv)) {
        var parts = p.CoPerCoreCsv.Split(',');
        for (int i = 0; i < ConfigService.CoPerCore.Length && i < parts.Length; i++) {
          if (int.TryParse(parts[i].Trim(), out int v)) ConfigService.CoPerCore[i] = v;
        }
        // ponytail: rebuild per-core UI rows to reflect new values
        BuildPerCoreCoPanels();
      }
      if (p.CoIGpuOffsetSnapshot != 0) CoIGpuNum.Value = p.CoIGpuOffsetSnapshot;
      // ADLX UI restore — checked values only; service calls happen on user click
      if (AdlxRsrToggle.IsChecked != p.AdlxRsrOn) AdlxRsrToggle.IsChecked = p.AdlxRsrOn;
      AdlxRsrSharpNum.Value = p.AdlxRsrSharpness;
      if (AdlxAntiLagToggle.IsChecked != p.AdlxAntiLagOn) AdlxAntiLagToggle.IsChecked = p.AdlxAntiLagOn;
      if (AdlxEnhancedSyncToggle.IsChecked != p.AdlxEnhancedSyncOn) AdlxEnhancedSyncToggle.IsChecked = p.AdlxEnhancedSyncOn;
      if (AdlxBoostToggle.IsChecked != p.AdlxBoostOn) AdlxBoostToggle.IsChecked = p.AdlxBoostOn;
      AdlxBoostNum.Value = p.AdlxBoostPercent;
      if (AdlxImageSharpToggle.IsChecked != p.AdlxImageSharpOn) AdlxImageSharpToggle.IsChecked = p.AdlxImageSharpOn;
      AdlxImageSharpNum.Value = p.AdlxImageSharpPercent;
      _loading = false;
    }

    static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;

    void cbxPerfPreset_SelectionChanged(object sender, SelectionChangedEventArgs e) {
      if (_loading) return;
      var item = cbxPerfPreset.SelectedItem as ComboBoxItem;
      if (item == null) return;
      string preset = item.Tag as string;
      if (string.IsNullOrEmpty(preset)) return;
      try {
        PresetManager.SwitchPreset(preset);
        // SwitchPreset fires OnPresetChanged → LoadStateFast (UI sync).
        // Also apply hardware (1.1 always, 1.2 for custom) — matches Dashboard behavior.
        if (Application.Current.MainWindow is Views.MainWindow mainWindow)
          mainWindow.ApplyPresetHardware();
      } catch (Exception ex) { Log($"cbxPerfPreset_SelectionChanged: {ex.Message}"); }
    }

    void btnPerfSave_Click(object sender, RoutedEventArgs e) {
      // ponytail: snapshot before save so Undo can roll back to pre-save state
      // (absorbs the former btnPerfApply's snapshot duty — Apply button removed).
      _snapshot = CapturePreset();
      // ponytail: always saveable — creates a new custom preset from current settings
      string name = tbxPerfPresetName.Text?.Trim();
      string presetKey;
      if (!string.IsNullOrEmpty(name)) {
        // sanitize name for file system: replace invalid chars with underscores
        presetKey = string.Join("_", name.Split(System.IO.Path.GetInvalidFileNameChars()));
        if (string.IsNullOrEmpty(presetKey)) presetKey = "Custom";
      } else {
        // no name given — derive from current preset if custom, else auto-number
        string current = ConfigService.Preset;
        if (PresetManager.IsCustom(current) && !PresetManager.IsBuiltIn(current)) {
          presetKey = current;
          name = ConfigService.GetCustomPresetDisplayName(current);
        } else {
          // auto-number: find highest CustomN and increment
          int maxN = 0;
          foreach (var (_, key) in PresetManager.EnumerateCustomPresets()) {
            if (key.StartsWith("Custom") && int.TryParse(key.Substring(6), out int n) && n > maxN) maxN = n;
          }
          presetKey = "Custom" + (maxN + 1);
          name = presetKey;
        }
      }
      PresetManager.SaveCustomPreset(presetKey, name);
      // switch to the new/updated preset
      PresetManager.SwitchPreset(presetKey);
      if (Application.Current.MainWindow is Views.MainWindow mainWindow)
        mainWindow.ApplyPresetHardware();
      RefreshPresetList();
      Log($"btnPerfSave: saved to {presetKey} (display=\"{name}\")");
    }

    void btnPerfLoad_Click(object sender, RoutedEventArgs e) {
      _loading = true;
      LoadStateFast();
      try { LoadStateDeferred(); } catch { }
      _loading = false;
      Log($"btnPerfLoad: reloaded '{ConfigService.Preset}'");
    }

    void btnPerfDelete_Click(object sender, RoutedEventArgs e) {
      string preset = ConfigService.Preset;
      if (PresetManager.IsBuiltIn(preset)) {
        DialogHelper.Info("内置预设不可删除。请先切换到自定义预设。", "提示");
        return;
      }
      string displayName = ConfigService.GetCustomPresetDisplayName(preset);
      if (!DialogHelper.OkCancel(
        $"确认删除自定义预设「{displayName}」？此操作不可撤销。",
        "删除预设")) return;
      // save current state away from this preset (SwitchPreset auto-saves on leave)
      ConfigService.Preset = ""; // prevent SwitchPreset from re-saving to the deleted key
      PresetManager.DeleteCustomPreset(preset);
      PresetManager.SwitchPreset("GpuPriority");
      if (Application.Current.MainWindow is Views.MainWindow mainWindow)
        mainWindow.ApplyPresetHardware();
      Log($"btnPerfDelete: deleted {preset} → GpuPriority");
      RefreshPresetList();
    }

    void btnPerfUndo_Click(object sender, RoutedEventArgs e) {
      // 如果有 Apply 快照，优先回滚到快照；否则恢复到默认预设并清空自定义预设
      if (_snapshot != null) {
        if (!DialogHelper.OkCancel(
          "将撤销本次 Apply 操作，恢复为 Apply 之前的状态。确认继续？",
          "撤销应用")) return;
        ApplyPreset(_snapshot);
        _snapshot = null;
        Log("btnPerfUndo: reverted to snapshot");
        return;
      }

      if (!DialogHelper.OkCancel(
        "将恢复到默认性能预设 (GpuPriority) 并清空全部自定义预设 (Custom1/2/3)。确认继续？",
        "恢复默认预设")) return;

      // 1. 切换到 GpuPriority 内置预设
      PresetManager.SwitchPreset("GpuPriority");
      // 2. 删除所有自定义预设文件
      foreach (var (_, key) in PresetManager.EnumerateCustomPresets()) {
        PresetManager.DeleteCustomPreset(key);
      }
      // 3. 应用硬件
      if (Application.Current.MainWindow is Views.MainWindow mainWindow)
        mainWindow.ApplyPresetHardware();
      // 4. 刷新 UI
      RefreshPresetList();
      _loading = true;
      LoadStateFast();
      try { LoadStateDeferred(); } catch { }
      _loading = false;
      _snapshot = null;
      Log("btnPerfUndo: restored GpuPriority, cleared custom presets");
    }
  }

  // ponytail: 核心选择 CheckBox 的简易 ViewModel
  public class CoreCheckItem {
    public int CoreIndex { get; set; }
    public bool IsChecked { get; set; }
  }
}
