// WinKeyLockService.cs - Win 键全局禁用/启用
// 优先走 HP WMI 0x2000B 硬件锁 (EC 层,对照 OmenCtl hp-rgb-lighting.c:285-322)，
// 失败回退低级键盘钩子 (WH_KEYBOARD_LL) 拦截 Win 键 — 游戏防误触场景。
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OmenSuperHub.Services {
  public static class WinKeyLockService {
    const int WH_KEYBOARD_LL = 13;
    const int WM_KEYDOWN = 0x0100;
    const int WM_SYSKEYDOWN = 0x0104;
    const int VK_LWIN = 0x5B;
    const int VK_RWIN = 0x5C;

    delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr GetModuleHandle(string lpModuleName);

    static IntPtr _hook = IntPtr.Zero;
    static LowLevelKeyboardProc _proc;  // ponytail: 必须保留委托引用，否则 GC 会让原生回调崩进程
    static bool _enabled;

    public static bool IsEnabled => _enabled;

    // 查询 EC 当前锁状态,启动时同步 UI 用。null=查不到(非 HP 或不支持)。
    public static bool? QueryEcLockState() {
      try { return OmenHardware.GetWinLock(); }
      catch { return null; }
    }

    // 启动时调用:查 EC 锁状态并同步内部 _enabled,避免 UI 与 EC 状态不一致。
    // 返回 EC 状态(null=查不到,UI 显示关并走软件钩子路径)。
    public static bool? SyncFromEc() {
      bool? ec = QueryEcLockState();
      if (ec.HasValue) _enabled = ec.Value;
      return ec;
    }

    public static void SetEnabled(bool enabled) {
      if (enabled == _enabled) return;

      // ponytail: HP 机型优先 WMI 0x2000B 硬件锁(对照 OmenCtl hp-rgb-lighting.c:304-322)。
      // WMI 路径不依赖进程钩子,不受提权窗口/全屏独占影响。非 HP 机型或 WMI 失败时
      // 回退 WH_KEYBOARD_LL 软件钩子。
      bool wmiOk = false;
      try { wmiOk = OmenHardware.SetWinLock(enabled); }
      catch { wmiOk = false; }

      if (wmiOk) {
        // WMI 接管时,若之前装过钩子需卸载(否则会双重拦截)
        if (_hook != IntPtr.Zero) Uninstall();
        _enabled = enabled;
        return;
      }

      // WMI 失败:走软件钩子路径。启用时装钩子,禁用时卸钩子。
      if (enabled) Install();
      else Uninstall();
    }

    static void Install() {
      try {
        _proc = HookCallback;
        using (var cur = Process.GetCurrentProcess())
        using (var mod = cur.MainModule) {
          _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(mod.ModuleName), 0);
        }
        _enabled = _hook != IntPtr.Zero;
      } catch { _enabled = false; }
    }

    static void Uninstall() {
      if (_hook != IntPtr.Zero) {
        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
      }
      _proc = null;
      _enabled = false;
    }

    static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam) {
      if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN)) {
        int vk = Marshal.ReadInt32(lParam);
        if (vk == VK_LWIN || vk == VK_RWIN) {
          // ponytail: 返回非零值吃掉按键，不传给下一个钩子
          return (IntPtr)1;
        }
      }
      return CallNextHookEx(_hook, nCode, wParam, lParam);
    }
  }
}
