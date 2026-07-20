// OtherPage.cs - 其他功能开关页面
// 电池充电限制、锁定键、触控板、HWiNFO 集成、HTTP API 等杂项开关
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
            BatteryChargeToggle.IsChecked = ConfigService.BatteryChargeLimit;
            if (ConfigService.BatteryWmiUnsupported)
            {
                BatteryChargeToggle.IsEnabled = false;
                BatteryChargeHint.Visibility = Visibility.Visible;
            }
            NumLockToggle.IsChecked = false;
            CapsLockToggle.IsChecked = false;
            TouchpadLockToggle.IsChecked = false;
            HWiNFOToggle.IsChecked = ConfigService.HWiNFOEnabled;
            HWiNFOReadToggle.IsChecked = ConfigService.HWiNFOReadEnabled;
            UpdateHWiNFOReadStatus();
            HttpApiToggle.IsChecked = ConfigService.HttpApiEnabled;
            UpdateHttpApiStatus();
        }

        async void BatteryChargeToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            bool enable = BatteryChargeToggle.IsChecked == true;
            if (ConfigService.BatteryWmiUnsupported)
            {
                BatteryChargeToggle.IsChecked = false;
                ConfigService.BatteryChargeLimit = false;
                ConfigService.Save("BatteryChargeLimit");
                if (enable) PromptOpenMyHP();
                return;
            }
            ConfigService.BatteryChargeLimit = enable;
            ConfigService.Save("BatteryChargeLimit");
            bool ok = await Task.Run(() => OmenHardware.SetBatteryConservation(enable));
            if (!ok)
            {
                BatteryChargeToggle.IsChecked = !enable;
                ConfigService.BatteryChargeLimit = !enable;
                ConfigService.Save("BatteryChargeLimit");
                ConfigService.BatteryWmiUnsupported = true;
                ConfigService.Save("BatteryWmiUnsupported");
                BatteryChargeToggle.IsEnabled = false;
                BatteryChargeHint.Visibility = Visibility.Visible;
                if (enable) PromptOpenMyHP();
            }
        }

        static void PromptOpenMyHP()
        {
            DialogHelper.Info(Strings.BatteryChargeMyHpHint, Strings.BatteryChargeTitle);
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
            try
            {
                using (var searcher = new ManagementObjectSearcher("root\\CIMV2",
                    "SELECT * FROM Win32_PointingDevice WHERE Name LIKE '%Touchpad%' OR Name LIKE '%Synaptics%' OR Name LIKE '%ELAN%'"))
                foreach (ManagementObject mo in searcher.Get())
                    mo.InvokeMethod(TouchpadLockToggle.IsChecked == true ? "Disable" : "Enable", null);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Touchpad lock failed: " + ex.Message);
            }
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
