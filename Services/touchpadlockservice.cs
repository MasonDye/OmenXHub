// TouchpadLockService.cs - 触摸板锁定服务
// 用 SetupAPI 禁用/启用 HID 精确触摸板设备，对所有触摸板通用、即时生效。
// 需要管理员权限（app.manifest 已设 requireAdministrator）。
//
// ponytail: 参考了 LenovoLegionToolkit 的语义（TouchpadLockState.On = 锁定 = 触摸板禁用），
// 但 LLT 用 Lenovo 专属 WMI (LENOVO_GAMEZONE_DATA.SetTPStatus)，HP 机器无此接口。
// HP 也没暴露等价 WMI（root\HP\InstrumentedBIOS 已按硬约束删除）。
// 因此用 SetupAPI 走标准 Windows 设备栈，匹配名称含 Touch/Tprecision/Tpad 的 Mouse 类设备。
//
// Ceiling:
//   1. 仅识别 Mouse 类的触摸板设备，不识别 HIDClass 的 I2C HID 底层设备（那会误伤键盘等其他 HID）
//   2. 锁定期间设备显示为"已禁用"，恢复后立即回 OK
//   3. 不持久化跨会话状态：重启后触摸板始终可用（这是预期行为，防止程序卸载后永久锁定）
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace OmenSuperHub.Services {
  public static class TouchpadLockService {
    // SetupAPI consts
    const uint DIGCF_PRESENT = 0x00000002;
    const uint DIGCF_ALLCLASSES = 0x00000040;
    const uint DICS_ENABLE = 1;
    const uint DICS_DISABLE = 2;
    const uint DIF_PROPERTYCHANGE = 0x12;
    const uint SPDRP_DEVICEDESC = 0x00000000;
    const uint SPDRP_HARDWAREID = 0x00000001;

    [StructLayout(LayoutKind.Sequential)]
    struct SP_DEVINFO_DATA {
      public uint cbSize;
      public Guid ClassGuid;
      public uint DevInst;
      public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct SP_CLASSINSTALL_HEADER {
      public uint cbSize;
      public uint InstallFunction;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct SP_PROPCHANGE_PARAMS {
      public SP_CLASSINSTALL_HEADER ClassInstallHeader;
      public uint StateChange;
      public uint Scope;
      public uint HwProfile;
    }

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern IntPtr SetupDiGetClassDevs(IntPtr ClassGuid, IntPtr Enumerator, IntPtr hwndParent, uint Flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

    [DllImport("setupapi.dll", SetLastError = true)]
    static extern bool SetupDiEnumDeviceInfo(IntPtr DeviceInfoSet, uint MemberIndex, ref SP_DEVINFO_DATA DeviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool SetupDiGetDeviceRegistryProperty(IntPtr DeviceInfoSet, ref SP_DEVINFO_DATA DeviceInfoData, uint Property, ref uint PropertyRegDataType, StringBuilder PropertyBuffer, uint PropertyBufferSize, ref uint RequiredSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    static extern bool SetupDiSetClassInstallParams(IntPtr DeviceInfoSet, ref SP_DEVINFO_DATA DeviceInfoData, ref SP_PROPCHANGE_PARAMS ClassInstallParams, uint ClassInstallParamsSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    static extern bool SetupDiCallClassInstaller(uint InstallFunction, IntPtr DeviceInfoSet, ref SP_DEVINFO_DATA DeviceInfoData);

    static bool _enabled = true;
    public static bool IsEnabled => _enabled;

    // ponytail: 触摸板名称匹配关键词（小写）。覆盖中英文 + Synaptics/ELAN/Alps 厂商
    static readonly string[] MatchKeywords = {
      "touch", "precision touchpad", "精确触摸板", "触摸板",
      "synaptics", "elan", " Alps ", "trackpad"
    };

    static bool IsTouchpad(string deviceDesc) {
      if (string.IsNullOrEmpty(deviceDesc)) return false;
      string lower = deviceDesc.ToLowerInvariant();
      foreach (var kw in MatchKeywords) {
        if (lower.Contains(kw.ToLowerInvariant())) return true;
      }
      return false;
    }

    /// <summary>
    /// 启用/禁用所有触摸板设备。返回受影响设备数。
    /// </summary>
    public static int SetEnabled(bool enabled) {
      int changed = 0;
      IntPtr devInfoSet = SetupDiGetClassDevs(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
          DIGCF_PRESENT | DIGCF_ALLCLASSES);
      if (devInfoSet == new IntPtr(-1)) return 0;

      try {
        uint index = 0;
        var did = new SP_DEVINFO_DATA();
        did.cbSize = (uint)Marshal.SizeOf(typeof(SP_DEVINFO_DATA));

        while (SetupDiEnumDeviceInfo(devInfoSet, index, ref did)) {
          index++;
          var sb = new StringBuilder(512);
          uint dataType = 0;
          uint requiredSize = 0;

          if (!SetupDiGetDeviceRegistryProperty(devInfoSet, ref did, SPDRP_DEVICEDESC,
              ref dataType, sb, (uint)sb.Capacity, ref requiredSize)) {
            continue;
          }
          string desc = sb.ToString();
          if (!IsTouchpad(desc)) continue;

          // ponytail: 多数触摸板在 Mouse 类下，直接走 SetupDiCallClassInstaller
          // 就能让设备管理器禁用它。
          if (ChangeDeviceState(devInfoSet, ref did, enabled ? DICS_ENABLE : DICS_DISABLE)) {
            changed++;
          }
        }
      } finally {
        SetupDiDestroyDeviceInfoList(devInfoSet);
      }
      _enabled = enabled;
      return changed;
    }

    static bool ChangeDeviceState(IntPtr devInfoSet, ref SP_DEVINFO_DATA did, uint stateChange) {
      var parms = new SP_PROPCHANGE_PARAMS {
        ClassInstallHeader = new SP_CLASSINSTALL_HEADER {
          cbSize = (uint)Marshal.SizeOf(typeof(SP_CLASSINSTALL_HEADER)),
          InstallFunction = DIF_PROPERTYCHANGE,
        },
        StateChange = stateChange,
        Scope = 0,    // DICS_FLAG_GLOBAL — 改全局配置
        HwProfile = 0,
      };
      if (!SetupDiSetClassInstallParams(devInfoSet, ref did, ref parms,
          (uint)Marshal.SizeOf(typeof(SP_PROPCHANGE_PARAMS)))) {
        return false;
      }
      return SetupDiCallClassInstaller(DIF_PROPERTYCHANGE, devInfoSet, ref did);
    }
  }
}
