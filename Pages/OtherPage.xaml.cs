// OtherPage.cs - 其他功能开关页面
// 锁定键、触控板、HWiNFO 集成、HTTP API 等杂项开关
using System;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using OmenSuperHub.Services;
using OmenSuperHub.Utils;
using System.Windows.Threading;

namespace OmenSuperHub.Pages
{
    public partial class OtherPage : Page
    {
        bool _loading;

        DispatcherTimer _hwinfoTimer;

        public OtherPage()
        {
            InitializeComponent();
                Loaded += (s, e) => { _loading = true; LoadState(); _loading = false; };
                Loaded += (s, e) => { _hwinfoTimer?.Start(); };
                // ponytail: dispatcher timer 必须在 Unloaded 停止，否则页面被导航离开后
                // timer 仍每 2s 触发 UpdateHWiNFOReadStatus()（注册表读 + WMI 探测），
                // 而 OtherPage 已脱离可视树，调用对分离元素无效且占 UI 线程。
                Unloaded += (s, e) => { _hwinfoTimer?.Stop(); };
            _hwinfoTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _hwinfoTimer.Tick += (s, e) => UpdateHWiNFOReadStatus();
        }

        void LoadState()
        {
            NumLockToggle.IsChecked = false;
            CapsLockToggle.IsChecked = false;
            TouchpadLockToggle.IsChecked = false;
            // ponytail: 启动时查 EC 锁状态同步 UI(HP 机型走 WMI 0x2000B),
            // 避免上次锁了重启后 UI 显示关但实际 EC 还锁着。查不到则默认关。
            WinLockToggle.IsChecked = WinKeyLockService.SyncFromEc() ?? false;
            HWiNFOToggle.IsChecked = ConfigService.HWiNFOEnabled;
            HWiNFOReadToggle.IsChecked = ConfigService.HWiNFOReadEnabled;
            UpdateHWiNFOReadStatus();
            HttpApiToggle.IsChecked = ConfigService.HttpApiEnabled;
            UpdateHttpApiStatus();
        }

        void NumLockToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            ToggleKey(VK_NUMLOCK);
        }

        void CapsLockToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            ToggleKey(VK_CAPITAL);
        }

        void TouchpadLockToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            // ponytail: 用 SetupAPI 禁用/启用触摸板设备。
            // 之前用 PrecisionTouchPad 注册表键 + WM_SETTINGCHANGE 广播，
            // 但 Windows Shell 不会立即重读该键（需注销重登或重启 explorer.exe）。
            // SetupAPI 禁用设备即时生效，对所有触摸板类型通用。
            try
            {
                int changed = TouchpadLockService.SetEnabled(TouchpadLockToggle.IsChecked != true);
                System.Diagnostics.Debug.WriteLine($"Touchpad lock: {changed} devices affected");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Touchpad lock failed: " + ex.Message);
            }
        }

        void WinLockToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            WinKeyLockService.SetEnabled(WinLockToggle.IsChecked == true);
        }

        void HWiNFOToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            ConfigService.HWiNFOEnabled = HWiNFOToggle.IsChecked == true;
            ConfigService.Save("HWiNFOEnabled");
            HWiNFOService.StartStopIfNeeded();
        }

        void HWiNFOReadToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            ConfigService.HWiNFOReadEnabled = HWiNFOReadToggle.IsChecked == true;
            ConfigService.Save("HWiNFOReadEnabled");
            HWiNFOReaderService.StartStopIfNeeded();
            UpdateHWiNFOReadStatus();
        }

        void UpdateHWiNFOReadStatus()
        {
            if (HWiNFOReadStatusText == null) return;
            if (ConfigService.HWiNFOReadEnabled && HWiNFOReaderService.IsRunning)
            {
                HWiNFOReadStatusText.Text = HWiNFOReaderService.StatusText;
                HWiNFOReadStatusText.Foreground = System.Windows.Media.Brushes.LimeGreen;
            }
            else
            {
                HWiNFOReadStatusText.Text = HWiNFOReaderService.StatusText;
                HWiNFOReadStatusText.Foreground = System.Windows.Media.Brushes.Gray;
            }
        }

        void HttpApiToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            ConfigService.HttpApiEnabled = HttpApiToggle.IsChecked == true;
            ConfigService.Save("HttpApiEnabled");
            if (ConfigService.HttpApiEnabled)
            {
                System.Threading.ThreadPool.QueueUserWorkItem(_ =>
                {
                    HardwareApiService.Start();
                    Dispatcher.BeginInvoke(new Action(UpdateHttpApiStatus));
                });
            }
            else
            {
                HardwareApiService.Stop();
                UpdateHttpApiStatus();
            }
        }

        void UpdateHttpApiStatus()
        {
            if (HttpApiStatusText == null) return;
            if (HardwareApiService.IsRunning)
            {
                HttpApiStatusText.Text = Strings.HttpApiRunning;
                HttpApiStatusText.Foreground = System.Windows.Media.Brushes.LimeGreen;
            }
            else
            {
                HttpApiStatusText.Text = Strings.HttpApiStopped;
                HttpApiStatusText.Foreground = System.Windows.Media.Brushes.Gray;
            }
        }

        [DllImport("user32.dll")]
        static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, IntPtr dwExtraInfo);
        const uint KEYEVENTF_KEYUP = 0x02;
        const byte VK_NUMLOCK = 0x90;
        const byte VK_CAPITAL = 0x14;

        static void ToggleKey(byte vk)
        {
            keybd_event(vk, 0, 0, IntPtr.Zero);
            keybd_event(vk, 0, KEYEVENTF_KEYUP, IntPtr.Zero);
        }

    }
}
