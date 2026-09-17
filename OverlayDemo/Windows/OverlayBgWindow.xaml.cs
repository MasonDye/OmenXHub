using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using OverlayDemo.Core;

namespace OverlayDemo.Windows;

public partial class OverlayBgWindow : Window
{
    public event Action BgClicked;

    public IntPtr Handle => new WindowInteropHelper(this).Handle;

    public OverlayBgWindow()
    {
        InitializeComponent();
    }

    // 覆盖主屏工作区
    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        Left = 0;
        Top = 0;
        Width = SystemParameters.WorkArea.Width;
        Height = SystemParameters.WorkArea.Height;
    }

    private void OnSourceInitialized(object sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        int ex = Win32.GetWindowLong(hwnd, Win32.GWL_EXSTYLE);
        Win32.SetWindowLong(hwnd, Win32.GWL_EXSTYLE, ex | Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TOOLWINDOW);
    }

    public new void Show()
    {
        base.Show();
        Win32.AssertTopmost(new WindowInteropHelper(this).Handle);
    }

    // 点击遮罩收起 overlay
    private void Window_MouseDown(object sender, MouseButtonEventArgs e)
    {
        BgClicked?.Invoke();
        e.Handled = true;
    }
}
