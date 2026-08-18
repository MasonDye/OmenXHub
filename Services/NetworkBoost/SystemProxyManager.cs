using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace OmenSuperHub.Services.NetworkBoost {
  /// <summary>WinINet 系统代理开关（HKCU Internet Settings + InternetSetOption 刷新）。</summary>
  internal static class SystemProxyManager {
    const string RegPath = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
    const int INTERNET_OPTION_SETTINGS_CHANGED = 39;
    const int INTERNET_OPTION_REFRESH = 37;

    [DllImport("wininet.dll", SetLastError = true)]
    static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);

    public static void Enable(string httpAddr, string socksAddr) {
      using (var key = Registry.CurrentUser.OpenSubKey(RegPath, true)) {
        if (key == null) return;
        key.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
        key.SetValue("ProxyServer", "http=" + httpAddr + ";https=" + httpAddr + ";socks=" + socksAddr, RegistryValueKind.String);
      }
      Refresh();
    }

    public static void Disable() {
      using (var key = Registry.CurrentUser.OpenSubKey(RegPath, true)) {
        if (key == null) return;
        key.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
      }
      Refresh();
    }

    static void Refresh() {
      InternetSetOption(IntPtr.Zero, INTERNET_OPTION_SETTINGS_CHANGED, IntPtr.Zero, 0);
      InternetSetOption(IntPtr.Zero, INTERNET_OPTION_REFRESH, IntPtr.Zero, 0);
    }
  }
}
