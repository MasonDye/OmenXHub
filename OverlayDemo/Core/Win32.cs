using System;
using System.Runtime.InteropServices;
using System.Text;

namespace OverlayDemo.Core;

// Win32 窗口辅助：非激活置顶 / 扩展样式 / 前台切换钩子 / 全局热键
internal static class Win32
{
    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_TRANSPARENT = 0x00000020;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_NOACTIVATE = 0x08000000;
    public const int SW_HIDE = 0;
    public const int SW_SHOWNOACTIVATE = 4;
    public const int HWND_TOPMOST = -1;
    public const int HWND_NOTOPMOST = -2;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint EVENT_SYSTEM_FOREGROUND = 3;
    public const uint WINEVENT_OUTOFCONTEXT = 0;

    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int cmd);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] internal static extern int GetWindowLong(IntPtr hWnd, int index);
    [DllImport("user32.dll")] internal static extern int SetWindowLong(IntPtr hWnd, int index, int value);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr hmod, WinEventProc proc, uint pid, uint tid, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);

    public struct RECT { public int Left, Top, Right, Bottom; }
    private delegate void WinEventProc(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

    // 显示但不激活，并重新插回 topmost 层级
    // SWP_NOSIZE|SWP_NOMOVE：窗口 WPF 布局未完成时 rect 可能无效，只调 z 序不动几何
    public static void ShowInactiveTopmost(IntPtr hWnd)
    {
        ShowWindow(hWnd, SW_SHOWNOACTIVATE);
        SetWindowPos(hWnd, (IntPtr)HWND_TOPMOST, 0, 0, 0, 0, SWP_NOACTIVATE | SWP_NOSIZE | SWP_NOMOVE);
    }

    public static void HideWindow(IntPtr hWnd)
    {
        ShowWindow(hWnd, SW_HIDE);
        SetWindowPos(hWnd, (IntPtr)HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    public static void AssertTopmost(IntPtr hWnd) =>
        SetWindowPos(hWnd, (IntPtr)HWND_TOPMOST, 0, 0, 0, 0, SWP_NOACTIVATE | SWP_NOSIZE | SWP_NOMOVE);

    // 前台切换钩子（EVENT_SYSTEM_FOREGROUND）
    public sealed class ForegroundHook : IDisposable
    {
        private readonly WinEventProc _proc;   // 静态引用防 GC
        private IntPtr _hook;
        public event Action Activated;

        public ForegroundHook()
        {
            _proc = (hook, evt, hwnd, o, c, t, ms) =>
            {
                if (evt == EVENT_SYSTEM_FOREGROUND) Activated?.Invoke();
            };
            _hook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _proc, 0, 0, WINEVENT_OUTOFCONTEXT);
        }

        public void Dispose() { if (_hook != IntPtr.Zero) { UnhookWinEvent(_hook); _hook = IntPtr.Zero; } }
    }

    // 全局热键固定 4 组合
    public static readonly (uint mods, uint vk)[] HotkeyCombos =
    {
        (0x0004, 0x71),           // Shift + F2
        (0x0004, 0x75),           // Shift + F6
        (0x0004, 0x76),           // Shift + F7
        (0x0002 | 0x0001, 0x78),  // Ctrl + Alt + F9
    };
    public const int WM_HOTKEY = 0x0312;
    public const int HOTKEY_ID = 0xA11C;
}
