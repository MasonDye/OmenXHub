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
                Loaded += (s, e) => { _loading = true; LoadState(); InitHeteroCpu(); _loading = false; };
                Unloaded += (s, e) => Strings.OnLanguageChanged -= RefreshHeteroLabels;
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

        // ════════════════════════════════════════════════════════════
        // Hetero CPU (AMD dual-CCD simulated hybrid scheduling)
        // ════════════════════════════════════════════════════════════
        static readonly int[] HeteroPolicyValues = { 0, 1, 2, 3, 4, 5, 6, 7 };
        static readonly int[] HeteroMaskValues = { 1, 2, 3, 4, 5, 6, 7 };

        void InitHeteroCpu() {
            HeteroCpuMaskBox.Text = ConfigService.HeteroCpuSmallMask;
            HeteroCpuRuntimeBox.Text = ConfigService.HeteroCpuExpectedRuntime.ToString();
            HeteroCpuPriorityBox.Text = ConfigService.HeteroCpuImportantPriority.ToString();
            RefreshHeteroLabels();
            // Restore saved selections
            SelectPolicy(HeteroCpuDefaultPolicyCombo, ConfigService.HeteroCpuDefaultPolicy, HeteroPolicyValues);
            SelectPolicy(HeteroCpuImportantPolicyCombo, ConfigService.HeteroCpuImportantPolicy, HeteroPolicyValues);
            SelectPolicy(HeteroCpuImportantShortCombo, ConfigService.HeteroCpuImportantShortPolicy, HeteroPolicyValues);
            SelectPolicy(HeteroCpuPolicyMaskCombo, ConfigService.HeteroCpuPolicyMask, HeteroMaskValues);
            Strings.OnLanguageChanged += RefreshHeteroLabels;
        }

        void RefreshHeteroLabels() {
            _loading = true;
            HeteroCpuDefaultPolicyCombo.ItemsSource = null;
            HeteroCpuDefaultPolicyCombo.ItemsSource = new[] {
                Strings.HeteroPolicyAny, Strings.HeteroPolicyBig, Strings.HeteroPolicyBigOrIdle,
                Strings.HeteroPolicySmall, Strings.HeteroPolicySmallOrIdle, Strings.HeteroPolicyAuto,
                Strings.HeteroPolicyPreferSmall, Strings.HeteroPolicyPreferBig
            };
            HeteroCpuImportantPolicyCombo.ItemsSource = null;
            HeteroCpuImportantPolicyCombo.ItemsSource = new[] {
                Strings.HeteroPolicyAny, Strings.HeteroPolicyBig, Strings.HeteroPolicyBigOrIdle,
                Strings.HeteroPolicySmall, Strings.HeteroPolicySmallOrIdle, Strings.HeteroPolicyAuto,
                Strings.HeteroPolicyPreferSmall, Strings.HeteroPolicyPreferBig
            };
            HeteroCpuImportantShortCombo.ItemsSource = null;
            HeteroCpuImportantShortCombo.ItemsSource = new[] {
                Strings.HeteroPolicyAny, Strings.HeteroPolicyBig, Strings.HeteroPolicyBigOrIdle,
                Strings.HeteroPolicySmall, Strings.HeteroPolicySmallOrIdle, Strings.HeteroPolicyAuto,
                Strings.HeteroPolicyPreferSmall, Strings.HeteroPolicyPreferBig
            };
            HeteroCpuPolicyMaskCombo.ItemsSource = null;
            HeteroCpuPolicyMaskCombo.ItemsSource = new[] {
                Strings.HeteroMaskForeground, Strings.HeteroMaskPriority, Strings.HeteroMaskFgPriority,
                Strings.HeteroMaskRuntime, Strings.HeteroMaskFgRuntime, Strings.HeteroMaskPriRuntime,
                Strings.HeteroMaskAll
            };
            // Restore selections from config
            SelectPolicy(HeteroCpuDefaultPolicyCombo, ConfigService.HeteroCpuDefaultPolicy, HeteroPolicyValues);
            SelectPolicy(HeteroCpuImportantPolicyCombo, ConfigService.HeteroCpuImportantPolicy, HeteroPolicyValues);
            SelectPolicy(HeteroCpuImportantShortCombo, ConfigService.HeteroCpuImportantShortPolicy, HeteroPolicyValues);
            SelectPolicy(HeteroCpuPolicyMaskCombo, ConfigService.HeteroCpuPolicyMask, HeteroMaskValues);
            _loading = false;
        }

        static void SelectPolicy(ComboBox cb, int val, int[] values) {
            int idx = Array.IndexOf(values, val);
            if (idx >= 0 && idx < (cb.ItemsSource as string[])?.Length)
                cb.SelectedIndex = idx;
        }

        void HeteroCpuToggle_Changed(object sender, RoutedEventArgs e) {
            if (_loading) return;
            bool enable = HeteroCpuToggle.IsChecked == true;
            HeteroCpuDetails.Visibility = enable ? Visibility.Visible : Visibility.Collapsed;
            if (enable) {
                // Write current values to HKLM
                HeteroCpuService.WriteSmallProcessorMask(ConfigService.HeteroCpuSmallMask);
                HeteroCpuService.WriteDefaultPolicy(ConfigService.HeteroCpuDefaultPolicy);
                HeteroCpuService.WriteExpectedRuntime(ConfigService.HeteroCpuExpectedRuntime);
                HeteroCpuService.WriteImportantPolicy(ConfigService.HeteroCpuImportantPolicy);
                HeteroCpuService.WriteImportantShortPolicy(ConfigService.HeteroCpuImportantShortPolicy);
                HeteroCpuService.WritePolicyMask(ConfigService.HeteroCpuPolicyMask);
                HeteroCpuService.WriteImportantPriority(ConfigService.HeteroCpuImportantPriority);
            } else {
                HeteroCpuService.RemoveAll();
            }
        }

        void HeteroCpuMask_TextChanged(object sender, TextChangedEventArgs e) {
            if (_loading) return;
            ConfigService.HeteroCpuSmallMask = HeteroCpuMaskBox.Text.Trim();
            ConfigService.Save("HeteroCpuSmallMask");
        }

        void HeteroCpuPolicy_Changed(object sender, SelectionChangedEventArgs e) {
            if (_loading) return;
            var combo = sender as ComboBox;
            if (combo == null || combo.SelectedIndex < 0) return;
            int val = HeteroPolicyValues[combo.SelectedIndex];
            string key = null;
            if (combo == HeteroCpuDefaultPolicyCombo) key = "HeteroCpuDefaultPolicy";
            else if (combo == HeteroCpuImportantPolicyCombo) key = "HeteroCpuImportantPolicy";
            else if (combo == HeteroCpuImportantShortCombo) key = "HeteroCpuImportantShortPolicy";
            else if (combo == HeteroCpuPolicyMaskCombo) { val = HeteroMaskValues[combo.SelectedIndex]; key = "HeteroCpuPolicyMask"; }
            if (key == null) return;
            var field = typeof(ConfigService).GetField(key);
            if (field != null) field.SetValue(null, val);
            ConfigService.Save(key);
        }

        void HeteroCpuRuntime_Changed(object sender, TextChangedEventArgs e) {
            if (_loading) return;
            if (int.TryParse(HeteroCpuRuntimeBox.Text, out var val)) {
                ConfigService.HeteroCpuExpectedRuntime = val;
                ConfigService.Save("HeteroCpuExpectedRuntime");
            }
        }

        void HeteroCpuPriority_Changed(object sender, TextChangedEventArgs e) {
            if (_loading) return;
            if (int.TryParse(HeteroCpuPriorityBox.Text, out var val)) {
                ConfigService.HeteroCpuImportantPriority = val;
                ConfigService.Save("HeteroCpuImportantPriority");
            }
        }

        void HeteroCpuApply_Click(object sender, RoutedEventArgs e) {
            if (HeteroCpuToggle.IsChecked != true) return;
            HeteroCpuService.WriteSmallProcessorMask(ConfigService.HeteroCpuSmallMask);
            HeteroCpuService.WriteDefaultPolicy(ConfigService.HeteroCpuDefaultPolicy);
            HeteroCpuService.WriteExpectedRuntime(ConfigService.HeteroCpuExpectedRuntime);
            HeteroCpuService.WriteImportantPolicy(ConfigService.HeteroCpuImportantPolicy);
            HeteroCpuService.WriteImportantShortPolicy(ConfigService.HeteroCpuImportantShortPolicy);
            HeteroCpuService.WritePolicyMask(ConfigService.HeteroCpuPolicyMask);
            HeteroCpuService.WriteImportantPriority(ConfigService.HeteroCpuImportantPriority);
            MessageBox.Show(Strings.HeteroCpuApplyResult, Strings.HeteroCpuApplyTitle, MessageBoxButton.OK, MessageBoxImage.Information);
        }

        void HeteroCpuRestore_Click(object sender, RoutedEventArgs e) {
            HeteroCpuService.RemoveAll();
            HeteroCpuToggle.IsChecked = false;
            HeteroCpuDetails.Visibility = Visibility.Collapsed;
            // Reset config defaults
            ConfigService.HeteroCpuSmallMask = "FFFF0000";
            ConfigService.HeteroCpuDefaultPolicy = 2;
            ConfigService.HeteroCpuExpectedRuntime = 1450;
            ConfigService.HeteroCpuImportantPolicy = 2;
            ConfigService.HeteroCpuImportantShortPolicy = 3;
            ConfigService.HeteroCpuPolicyMask = 7;
            ConfigService.HeteroCpuImportantPriority = 8;
            ConfigService.Save("HeteroCpuSmallMask");
            ConfigService.Save("HeteroCpuDefaultPolicy");
            ConfigService.Save("HeteroCpuExpectedRuntime");
            ConfigService.Save("HeteroCpuImportantPolicy");
            ConfigService.Save("HeteroCpuImportantShortPolicy");
            ConfigService.Save("HeteroCpuPolicyMask");
            ConfigService.Save("HeteroCpuImportantPriority");
            MessageBox.Show(Strings.HeteroCpuRestoreResult, Strings.HeteroCpuRestoreTitle, MessageBoxButton.OK, MessageBoxImage.Information);
        }

        void HeteroCpuDetect_Click(object sender, RoutedEventArgs e) {
            var (supported, totalLp, ccd0Lp, maskHex) = HeteroCpuService.DetectDualCcd();
            if (!supported) {
                MessageBox.Show(Strings.HeteroCpuNotDetected, Strings.HeteroCpuDetectTitle, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            string msg = Strings.HeteroCpuDetectConfirm(totalLp.ToString(), ccd0Lp.ToString(), (totalLp - ccd0Lp).ToString(), maskHex);
            if (MessageBox.Show(msg, Strings.HeteroCpuDetectTitle, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes) {
                HeteroCpuMaskBox.Text = maskHex;
                ConfigService.HeteroCpuSmallMask = maskHex;
                ConfigService.Save("HeteroCpuSmallMask");
                // Set policy defaults
                ConfigService.HeteroCpuDefaultPolicy = 2;
                ConfigService.HeteroCpuExpectedRuntime = 1450;
                ConfigService.HeteroCpuImportantPolicy = 2;
                ConfigService.HeteroCpuImportantShortPolicy = 3;
                ConfigService.HeteroCpuPolicyMask = 7;
                ConfigService.HeteroCpuImportantPriority = 8;
                ConfigService.Save("HeteroCpuDefaultPolicy");
                ConfigService.Save("HeteroCpuExpectedRuntime");
                ConfigService.Save("HeteroCpuImportantPolicy");
                ConfigService.Save("HeteroCpuImportantShortPolicy");
                ConfigService.Save("HeteroCpuPolicyMask");
                ConfigService.Save("HeteroCpuImportantPriority");
                _loading = true;
                SelectPolicy(HeteroCpuDefaultPolicyCombo, 2, HeteroPolicyValues);
                SelectPolicy(HeteroCpuImportantPolicyCombo, 2, HeteroPolicyValues);
                SelectPolicy(HeteroCpuImportantShortCombo, 3, HeteroPolicyValues);
                SelectPolicy(HeteroCpuPolicyMaskCombo, 7, HeteroMaskValues);
                HeteroCpuRuntimeBox.Text = "1450";
                HeteroCpuPriorityBox.Text = "8";
                _loading = false;
                // Apply to registry
                HeteroCpuService.WriteSmallProcessorMask(maskHex);
                HeteroCpuService.WriteDefaultPolicy(2);
                HeteroCpuService.WriteExpectedRuntime(1450);
                HeteroCpuService.WriteImportantPolicy(2);
                HeteroCpuService.WriteImportantShortPolicy(3);
                HeteroCpuService.WritePolicyMask(7);
                HeteroCpuService.WriteImportantPriority(8);
                HeteroCpuToggle.IsChecked = true;
                HeteroCpuDetails.Visibility = Visibility.Visible;
                MessageBox.Show(Strings.HeteroCpuDetectResult, Strings.HeteroCpuDetectTitle, MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

    }
}
