using System;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using OmenSuperHub.Services;

namespace OmenSuperHub.Pages
{
    public partial class OtherPage : Page
    {
        bool _loading;

        public OtherPage()
        {
            InitializeComponent();
            Loaded += (s, e) => { _loading = true; LoadState(); _loading = false; };
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
            var result = MessageBox.Show(Strings.BatteryChargeMyHpHint, Strings.BatteryChargeTitle, MessageBoxButton.OK, MessageBoxImage.Information);
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
