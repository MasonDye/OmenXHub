using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;
using OverlayDemo.Core;
using OverlayDemo.Windows;
using OverlayDemo.Widgets;

namespace OverlayDemo;

public partial class App : Application
{
    private AppConfig _cfg;
    private OverlayWidgetWindow[] _windows = new OverlayWidgetWindow[4];
    private VitalsWidget _vitals;
    private SelectorWindow _selector;
    private OverlayBgWindow _bg;
    private Win32.ForegroundHook _fgHook;
    private bool _hotkeyRegistered;
    private bool _overlayShown;
    private System.Threading.Mutex _mutex;

    [DllImport("kernel32.dll")] private static extern bool AttachConsole(int pid);
    private const int ATTACH_PARENT_PROCESS = -1;

    private static readonly string[] WidgetTitles =
        { "System Vitals", "System Vitals Compact", "Fan Monitor", "Performance Control" };

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // 单实例 Mutex（按会话隔离）
        _mutex = new System.Threading.Mutex(true, "OverlayDemo-session-" + System.Diagnostics.Process.GetCurrentProcess().SessionId, out bool createdNew);
        if (!createdNew) { Shutdown(0); return; }

        if (Array.IndexOf(e.Args, "--selftest") >= 0)
        {
            AttachConsole(ATTACH_PARENT_PROCESS);
            bool ok = SelfTest.Run();
            Console.WriteLine(ok ? "OK" : "FAILED");
            Shutdown(ok ? 0 : 1);
            return;
        }
        RunDemo();
    }

    private void RunDemo()
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _cfg = AppConfig.Load();
        _vitals = new VitalsWidget();
        _vitals.ViewModel.ApplySettings(_cfg);

        // 每个 widget 一个独立置顶小窗
        _windows[0] = new OverlayWidgetWindow(_cfg, 0, _cfg.State(0), WidgetTitles[0], _vitals, vitalsToggles: true);
        _windows[1] = new OverlayWidgetWindow(_cfg, 1, _cfg.State(1), WidgetTitles[1], new CompactWidget(), vitalsToggles: false);
        _windows[2] = new OverlayWidgetWindow(_cfg, 2, _cfg.State(2), WidgetTitles[2], new FanWidget(), vitalsToggles: false);
        _windows[3] = new OverlayWidgetWindow(_cfg, 3, _cfg.State(3), WidgetTitles[3], new PerfWidget(), vitalsToggles: false);
        foreach (var w in _windows)
        {
            w.LockChanged += _ => AssertOverlaysTopmost();
            w.SelectorRequested += _ => ShowOverlay(showSettings: false);
            w.SettingsRequested += _ => ShowOverlay(showSettings: true);
            w.CloseRequested += win => CloseWidget(win.WidgetIndex);
            w.VitalsSettingsChanged += ApplyAllSettings;
        }

        _selector = new SelectorWindow(_cfg);
        _selector.WidgetToggleRequested += ToggleWidget;
        _selector.SettingsChanged += ApplyAllSettings;
        _selector.HotkeyChanged += ReRegisterHotkey;
        _selector.CollapseRequested += HideOverlay;

        _bg = new OverlayBgWindow();
        _bg.BgClicked += HideOverlay;

        ApplyAllSettings();   // 启动时四个 widget 都要应用一次透明度/颜色（否则背景保持默认透明）

        // 前台切换钩子：把所有应显示的 overlay 重新插回 topmost 层级
        // （否则任何同样置顶的应用在前台时都会把遮罩/选择器压下去）
        _fgHook = new Win32.ForegroundHook();
        _fgHook.Activated += AssertOverlaysTopmost;

        // 锁屏隐藏全部，解锁恢复
        SystemEvents.SessionSwitch += (_, e) =>
        {
            if (e.Reason == SessionSwitchReason.SessionLock) HideOverlay();
            else if (e.Reason == SessionSwitchReason.SessionUnlock && _overlayShown) ShowOverlay(showSettings: false);
        };

        ShowOverlay(showSettings: false);
    }

    private void ApplyAllSettings()
    {
        _vitals.ViewModel.ApplySettings(_cfg);
        foreach (var w in _windows)
        {
            var content = w.ContentRegion.Content;
            switch (content)
            {
                case CompactWidget c: c.ViewModel.ApplySettings(_cfg); break;
                case FanWidget f: f.ViewModel.ApplySettings(_cfg); break;
                case PerfWidget p: p.ViewModel.ApplySettings(_cfg); break;
            }
            w.SyncVitalsToggles();
        }
    }

    private void ShowOverlay(bool showSettings)
    {
        _bg.Show();
        _selector.Show();
        if (showSettings) _selector.IsSettingVisible = true;
        RegisterHotkeyOnce();
        for (int i = 0; i < _windows.Length; i++)
            if (_cfg.State(i).Launched) _windows[i].ShowInactiveTopmost();
        _selector.SyncRailChecks();
        AssertOverlaysTopmost();
        _overlayShown = true;
    }

    // 收起：选择器/遮罩隐藏，未锁定的 widget 一并隐藏，锁定的保留
    private void HideOverlay()
    {
        _selector.Hide();
        _bg.Hide();
        for (int i = 0; i < _windows.Length; i++)
            if (!_cfg.State(i).Locked) _windows[i].Hide();
        _overlayShown = false;
    }

    private void ToggleOverlay()
    {
        if (_overlayShown) HideOverlay();
        else ShowOverlay(showSettings: false);
    }

    private void ToggleWidget(int index)
    {
        var s = _cfg.State(index);
        if (s.Launched)
        {
            s.Launched = false;
            _windows[index].Hide();
        }
        else
        {
            s.Launched = true;
            _windows[index].ShowInactiveTopmost();
        }
        _cfg.Save();
        _selector.SyncRailChecks();
        AssertOverlaysTopmost();
    }

    private void CloseWidget(int index)
    {
        _cfg.State(index).Launched = false;
        _cfg.Save();
        _windows[index].Hide();
        _selector.SyncRailChecks();
    }

    // 置顶顺序：遮罩最底 → 选择器 → 各 widget
    private void AssertOverlaysTopmost()
    {
        if (_bg.IsVisible) Win32.AssertTopmost(_bg.Handle);
        if (_selector.IsVisible) Win32.AssertTopmost(_selector.Handle);
        for (int i = _windows.Length - 1; i >= 0; i--)
            if (_windows[i].IsVisible) Win32.AssertTopmost(_windows[i].Handle);
    }

    // 全局热键，固定 4 组合
    private void RegisterHotkeyOnce()
    {
        if (_hotkeyRegistered) return;
        _hotkeyRegistered = true;
        ReRegisterHotkey();
        var src = HwndSource.FromHwnd(_selector.Handle);
        src.AddHook((IntPtr hwnd, int msg, IntPtr wp, IntPtr lp, ref bool handled) =>
        {
            if (msg == Win32.WM_HOTKEY && wp.ToInt32() == Win32.HOTKEY_ID) { ToggleOverlay(); handled = true; }
            return IntPtr.Zero;
        });
    }

    private void ReRegisterHotkey()
    {
        if (!_hotkeyRegistered) return;
        var (mods, vk) = Win32.HotkeyCombos[Math.Max(0, Math.Min(3, _cfg.HotkeyIndex))];
        Win32.UnregisterHotKey(_selector.Handle, Win32.HOTKEY_ID);
        Win32.RegisterHotKey(_selector.Handle, Win32.HOTKEY_ID, mods, vk);
    }
}
