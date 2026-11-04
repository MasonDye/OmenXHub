// KeyRemapService.cs - 物理键改键 (OmenXHub 版)
// 把 OMEN 键 / 计算器键 / Insert / PrtSc 改成 Home / End / PageUp / PageDown 等常用键。
//
// 两条通道:
//   1. OMEN 键: 不产生键盘扫描码(BIOS 走 WMI 事件),由 OmenHardware.OmenKeyPressed 驱动注入;
//      与 Omen 键功能绑定互斥 —— ConfigService.OmenKey 不是 "none" 时本项自动失效。
//   2. 计算器键 / Insert / PrtSc: 正常键码,用 WH_KEYBOARD_LL 低级键盘钩子拦截后改发。
//
// ponytail: 注入用 keybd_event(与本项目 MacroController 同一条路),不用 SendInput ——
// SendInput 要求 INPUT 结构体在 x64 下正好 40 字节(联合体必须含 MOUSEINPUT),
// 尺寸算错时它会静默返回 0、一个键都发不出去,是踩过的坑。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OmenSuperHub.Services {
  internal static class KeyRemapService {
    const int WH_KEYBOARD_LL = 13;
    const int WM_KEYDOWN = 0x0100;
    const int WM_SYSKEYDOWN = 0x0104;
    const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    const uint KEYEVENTF_KEYUP = 0x0002;
    // KBDLLHOOKSTRUCT.flags bit4: 事件由程序注入(非真实按键)
    const int LLKHF_INJECTED = 0x10;

    // 走键盘钩子的物理键
    const uint VK_CALC = 0xB7;   // 计算器键 (VK_LAUNCH_APP2)
    const uint VK_INSERT = 0x2D;
    const uint VK_PRTSC = 0x2C;

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

    [DllImport("user32.dll")]
    static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    // 可选目标键(顺序即设置页下拉框顺序),值存键名到注册表
    internal static readonly string[] TargetKeyNames = {
      "Home", "End", "PageUp", "PageDown", "Insert", "Delete", "PrintScreen",
      "Esc", "Up", "Down", "Left", "Right"
    };

    static readonly Dictionary<string, uint> TargetKeyVk = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase) {
      {"HOME",0x24},{"END",0x23},{"PAGEUP",0x21},{"PGUP",0x21},{"PAGEDOWN",0x22},{"PGDN",0x22},
      {"INSERT",0x2D},{"INS",0x2D},{"DELETE",0x2E},{"DEL",0x2E},
      {"PRINTSCREEN",0x2C},{"PRTSC",0x2C},{"ESC",0x1B},{"ESCAPE",0x1B},
      {"UP",0x26},{"DOWN",0x28},{"LEFT",0x25},{"RIGHT",0x27},
    };

    static IntPtr _hook = IntPtr.Zero;
    static LowLevelKeyboardProc _proc;   // ponytail: 必须保留委托引用,否则 GC 回收后原生回调崩进程

    // 生效中的映射目标 VK,0 = 不替换
    static uint _calcVk, _insertVk, _prtscVk, _omenVk;

    public static bool HookActive => _hook != IntPtr.Zero;

    // OMEN 键是否处于"改键生效"状态(调用方据此决定是否需要订阅 BIOS 事件)
    public static bool OmenRemapActive => _omenVk != 0;

    // 从配置重算映射并装卸钩子。任何相关设置变更后都要调一次。
    public static void Apply() {
      bool on = ConfigService.KeyRemapEnabled;
      _calcVk = on ? ResolveTarget(ConfigService.RemapCalc) : 0;
      _insertVk = on ? ResolveTarget(ConfigService.RemapInsert) : 0;
      _prtscVk = on ? ResolveTarget(ConfigService.RemapPrtSc) : 0;
      // 功能互斥: Omen 键已绑定其它功能("showMain"/"cyclePresets"/"app"/"custom")时改键不生效
      _omenVk = (on && ConfigService.OmenKey == "none") ? ResolveTarget(ConfigService.RemapOmen) : 0;

      if (_calcVk != 0 || _insertVk != 0 || _prtscVk != 0) Install();
      else Uninstall();
    }

    // 键名 -> VK,空值/未知键名都返回 0(不替换)
    public static uint ResolveTarget(string keyName) {
      if (string.IsNullOrWhiteSpace(keyName)) return 0;
      uint vk;
      return TargetKeyVk.TryGetValue(keyName.Trim(), out vk) ? vk : 0;
    }

    // OMEN 键按下时由 TrayService 调用
    public static void HandleOmenKey() {
      if (_omenVk == 0) return;
      InjectKey(_omenVk);
    }

    static void Install() {
      if (_hook != IntPtr.Zero) return;
      try {
        _proc = HookCallback;
        using (var cur = Process.GetCurrentProcess())
        using (var mod = cur.MainModule) {
          _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(mod.ModuleName), 0);
        }
        if (_hook == IntPtr.Zero) Logger.Error("KeyRemap: SetWindowsHookEx failed, err=" + Marshal.GetLastWin32Error());
      } catch (Exception ex) {
        _hook = IntPtr.Zero;
        Logger.Error("KeyRemap Install: " + ex.Message);
      }
    }

    static void Uninstall() {
      if (_hook != IntPtr.Zero) {
        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
      }
      _proc = null;
    }

    static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam) {
      if (nCode >= 0) {
        // 跳过自己注入的事件:否则 A->B、B->A 这类映射会互相触发成死循环
        int flags = Marshal.ReadInt32(lParam, 8);
        if ((flags & LLKHF_INJECTED) == 0) {
          uint vk = (uint)Marshal.ReadInt32(lParam);
          uint target = 0;
          if (vk == VK_CALC) target = _calcVk;
          else if (vk == VK_INSERT) target = _insertVk;
          else if (vk == VK_PRTSC) target = _prtscVk;

          if (target != 0) {
            if (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN)
              InjectKey(target);
            // ponytail: keydown/keyup 一起吞掉,只吞一半会让目标键残留按下状态
            return (IntPtr)1;
          }
        }
      }
      return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    static void InjectKey(uint vk) {
      // 方向键/翻页/Insert 等 E0 前缀键必须带 EXTENDEDKEY,否则被当成小键盘数字
      uint flags = MacroController.IsExtendedKey(vk) ? KEYEVENTF_EXTENDEDKEY : 0;
      keybd_event((byte)vk, 0, flags, UIntPtr.Zero);
      keybd_event((byte)vk, 0, flags | KEYEVENTF_KEYUP, UIntPtr.Zero);
    }
  }
}
