using System;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using OmenSuperHub.Services;
using static OmenSuperHub.OmenHardware;
using static OmenSuperHub.OmenLighting;
using HP.Omen.Core.Model.Device.Models;
using HP.Omen.Core.Model.Device.Enums;
using LibreHardwareType = LibreHardwareMonitor.Hardware.HardwareType;
using LibreSensorType = LibreHardwareMonitor.Hardware.SensorType;

namespace OmenSuperHub.Pages
{
    public partial class SysInfoPage : Page
    {
        DispatcherTimer _refreshTimer;
        bool _loading;

        public SysInfoPage()
        {
            InitializeComponent();
            Loaded += (s, e) =>
            {
                _loading = true;
                LoadState();
                _loading = false;
                RefreshSysInfo();
                _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                _refreshTimer.Tick += (s2, e2) => RefreshSensors();
                _refreshTimer.Start();
                RefreshGpuAppList();
            };
            Unloaded += (s, e) => _refreshTimer?.Stop();
        }

        void LoadState()
        {
            MonCpuCombo.SelectedIndex = ConfigService.MonitorCPU ? 0 : 1;
            MonGpuCombo.SelectedIndex = ConfigService.MonitorGPU ? 0 : 1;
            MonFanCombo.SelectedIndex = ConfigService.MonitorFan ? 0 : 1;
            MonRefreshCombo.SelectedIndex = ConfigService.MonRefreshInterval <= 500 ? 0 : 1;
            TempDispCombo.SelectedIndex = ConfigService.DisplayMode == "raw" ? 1 : 0;
        }

        void RefreshSysInfo()
        {
            if (!string.IsNullOrEmpty(ConfigService.SysManufacturer))
            {
                SysManufacturerText.Text = Strings.SysManufacturer + ": " + ConfigService.SysManufacturer;
                SysModelText.Text = Strings.SysModel + ": " + ConfigService.SysModel;
                SysBiosText.Text = Strings.SysBiosVersion + ": " + ConfigService.SysBios;
                SysCpuText.Text = Strings.SysCpuModel + ": " + ConfigService.SysCpu;
                SysGpuText.Text = Strings.SysGpuList + ": " + ConfigService.SysGpu;
                SysAdapterText.Text = Strings.SysAdapterPower + ": " + ConfigService.SysAdapterPower + " W";
                SysProductNameText.Text = Strings.SysModelName + ": " + ConfigService.SysProductName;
                int v = ConfigService.SysValidation;
                SysValidationText.Text = Strings.SysModelValidation + ": " + (
                    v == 2 ? Strings.ValidationGamingProduct :
                    v == 1 ? Strings.ValidationUnsupported :
                    Strings.SysUnknown);
                SysBoardText.Text = Strings.SysBoardProduct + ": " + ConfigService.SysBoardProduct;
                SysCpuTjmaxText.Text = Strings.SysCpuTjMax + ": " + ConfigService.SysCpuTjmax + " °C";
                SysNvidiaTjmaxText.Text = ConfigService.SysNvidiaTjmax > 0
                    ? Strings.SysNvidiaTjMax + ": " + ConfigService.SysNvidiaTjmax + " °C"
                    : "";
                SysNvidiaPowerText.Text = !string.IsNullOrEmpty(ConfigService.SysNvidiaPowerMin)
                    ? Strings.SysNvidiaPowerLimitText(ConfigService.SysNvidiaPowerMin + " / " + ConfigService.SysNvidiaPowerMax)
                    : "";
                SysKbLightTypeText.Text = Strings.SysKbType + ": " + GetKeyboardTypeName((NbKeyboardLightingType)ConfigService.SysKbRaw);
                UpdateSysInfoFooter();
                return;
            }
            Task.Run(() =>
            {
                string mfr = null, model = null, bios = null, cpu = null, gpu = null;
                int adapterW = 0;
                try
                {
                    using (var searcher = new ManagementObjectSearcher("SELECT Manufacturer, Model FROM Win32_ComputerSystem"))
                    using (var col = searcher.Get())
                    {
                        foreach (ManagementBaseObject obj in col)
                        {
                            mfr = obj["Manufacturer"]?.ToString() ?? Strings.SysUnknown;
                            model = obj["Model"]?.ToString() ?? Strings.SysUnknown;
                        }
                    }
                    bios = GetBiosVersion();
                    cpu = GetCpuModel();
                    var gpuNames = GpuAppManager.GetAllGpuNamesList();
                    gpu = gpuNames.Count > 0 ? string.Join("; ", gpuNames) : Strings.SysUnknown;
                    adapterW = GetAdapterPower();
                }
                catch (Exception ex)
                {
                    Logger.Error("RefreshSysInfo WMI error: " + ex.Message);
                }
                var capturedMfr = mfr; var capturedModel = model; var capturedBios = bios;
                var capturedCpu = cpu; var capturedGpu = gpu; var capturedAdapterW = adapterW;
                Dispatcher.InvokeAsync(() =>
                {
                    if (capturedMfr != null)
                    {
                        SysManufacturerText.Text = Strings.SysManufacturer + ": " + capturedMfr;
                        SysModelText.Text = Strings.SysModel + ": " + capturedModel;
                        ConfigService.SysManufacturer = capturedMfr;
                        ConfigService.SysModel = capturedModel;
                        SysBiosText.Text = Strings.SysBiosVersion + ": " + capturedBios;
                        ConfigService.SysBios = capturedBios;
                        SysCpuText.Text = Strings.SysCpuModel + ": " + capturedCpu;
                        ConfigService.SysCpu = capturedCpu;
                        SysGpuText.Text = Strings.SysGpuList + ": " + capturedGpu;
                        ConfigService.SysGpu = capturedGpu;
                        SysAdapterText.Text = Strings.SysAdapterPower + ": " + capturedAdapterW + " W";
                        ConfigService.SysAdapterPower = capturedAdapterW;
                    }
                    string pn = null, board = null;
                    int validation = 0, tj = 0, nvidiaTj = 0;
                    try { pn = DeviceModel.OmenPlatform.DisplayName; } catch { }
                    try { board = DeviceModel.ThisSystemID; } catch { }
                    try { validation = Validation(); } catch { }
                    try { tj = GetCpuTjmax(); } catch { }
                    try { nvidiaTj = GpuAppManager.GetGpuTemperatureTarget(); } catch { }
                    var powerLimits = new float[2];
                    try { powerLimits = GpuAppManager.GetGpuPowerLimits(); } catch { }
                    string kb = null;
                    try { kb = GetKeyboardTypeName(GetKeyboardType()); } catch { }
                    SysProductNameText.Text = Strings.SysModelName + ": " + (pn ?? Strings.SysUnknown);
                    ConfigService.SysProductName = pn ?? Strings.SysUnknown;
                    SysValidationText.Text = Strings.SysModelValidation + ": " + (
                        validation >= 2 ? Strings.ValidationGamingProduct :
                        validation == 1 ? Strings.ValidationUnsupported : Strings.ValidationUnsupported);
                    ConfigService.SysValidation = validation;
                    SysBoardText.Text = Strings.SysBoardProduct + ": " + (board ?? Strings.SysUnknown);
                    ConfigService.SysBoardProduct = board ?? Strings.SysUnknown;
                    SysCpuTjmaxText.Text = Strings.SysCpuTjMax + ": " + tj + " °C";
                    ConfigService.SysCpuTjmax = tj;
                    SysNvidiaTjmaxText.Text = nvidiaTj > 0 ? Strings.SysNvidiaTjMax + ": " + nvidiaTj + " °C" : "";
                    ConfigService.SysNvidiaTjmax = nvidiaTj;
                    if (powerLimits[0] > 0)
                    {
                        SysNvidiaPowerText.Text = Strings.SysNvidiaPowerLimitText($"{powerLimits[0]:F0}W / {powerLimits[1]:F0}W");
                        ConfigService.SysNvidiaPowerMin = $"{powerLimits[0]:F0}W";
                        ConfigService.SysNvidiaPowerMax = $"{powerLimits[1]:F0}W";
                    }
                    SysKbLightTypeText.Text = Strings.SysKbType + ": " + (kb ?? Strings.SysUnknown);
                    ConfigService.SysKbType = kb ?? Strings.SysUnknown;
                    try { ConfigService.SysKbRaw = (int)GetKeyboardType(); } catch { }
                    ConfigService.Save();
                    UpdateSysInfoFooter();
                });
            });
        }

        void UpdateSysInfoFooter()
        {
            try
            {
                SysPawnIoText.Text = LibreHardwareMonitor.PawnIo.PawnIo.IsInstalled
                    ? "✔ " + Strings.SysPawnInstalled + " v" + LibreHardwareMonitor.PawnIo.PawnIo.Version().ToString()
                    : "✘ " + Strings.SysPawnMissing;
            }
            catch
            {
                SysPawnIoText.Text = "✘ " + Strings.SysPawnMissing;
            }
            try
            {
                int cpuT = (int)HardwareService.CPUTemp;
                int gpuT = (int)HardwareService.GPUTemp;
                SysCpuTempText.Text = Strings.SysCPUTemp + ": " + cpuT + " °C";
                SysGpuTempText.Text = Strings.SysGPUTemp + ": " + gpuT + " °C";
                SysIrSensorText.Text = Strings.SysIRSensor + ": " + GetSensorTemperature(0) + " °C";
                SysAmbientText.Text = Strings.SysAmbient + ": " + GetSensorTemperature(1) + " °C";
                SysPchText.Text = Strings.SysPCH + ": " + GetSensorTemperature(2) + " °C";
                SysVrText.Text = Strings.SysVR + ": " + GetSensorTemperature(3) + " °C";
            }
            catch { }
        }

        void RefreshSensors()
        {
            int cpuT = (int)HardwareService.CPUTemp;
            int gpuT = (int)HardwareService.GPUTemp;
            SysCpuTempText.Text = Strings.SysCPUTemp + ": " + cpuT + " °C";
            SysGpuTempText.Text = Strings.SysGPUTemp + ": " + gpuT + " °C";
            int ir = GetSensorTemperature(0);
            SysIrSensorText.Text = Strings.SysIRSensor + ": " + ir + " °C";
            int amb = GetSensorTemperature(1);
            SysAmbientText.Text = Strings.SysAmbient + ": " + amb + " °C";
            int pch = GetSensorTemperature(2);
            SysPchText.Text = Strings.SysPCH + ": " + pch + " °C";
            int vr = GetSensorTemperature(3);
            SysVrText.Text = Strings.SysVR + ": " + vr + " °C";
        }

        int GetCpuTjmax()
        {
            try
            {
                foreach (var hw in HardwareService.LibreComputer.Hardware)
                {
                    if (hw.HardwareType == LibreHardwareType.Cpu)
                    {
                        hw.Update();
                        foreach (var sensor in hw.Sensors)
                        {
                            if (sensor.SensorType == LibreSensorType.Temperature && sensor.Parameters.Count > 0)
                            {
                                return (int)sensor.Parameters[0].Value;
                            }
                        }
                    }
                }
            }
            catch { }
            return 100;
        }

        async Task RefreshNvidiaPowerLimitAsync()
        {
            try
            {
                await Task.Delay(500);
                var powerLimits = GpuAppManager.GetGpuPowerLimits();
                if (powerLimits[0] > 0)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        SysNvidiaPowerText.Text = Strings.SysNvidiaPowerLimitText($"{powerLimits[0]:F0}W / {powerLimits[1]:F0}W");
                        ConfigService.SysNvidiaPowerMin = $"{powerLimits[0]:F0}W";
                        ConfigService.SysNvidiaPowerMax = $"{powerLimits[1]:F0}W";
                    });
                }
            }
            catch { }
        }

        void SysInfoRefresh_Click(object sender, RoutedEventArgs e)
        {
            MachineInfoCache.Invalidate();
            ConfigService.SysManufacturer = "";
            RefreshSysInfo();
        }

        void MonCpu_SelectionChanged(object s, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            bool on = MonCpuCombo.SelectedIndex == 0;
            ConfigService.MonitorCPU = on;
            HardwareService.MonitorCPU = on;
            HardwareService.LibreComputer.IsCpuEnabled = on;
            ConfigService.Save("MonitorCPU");
            Views.FloatingWindow.UpdateAllText();
        }

        void MonGpu_SelectionChanged(object s, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            bool on = MonGpuCombo.SelectedIndex == 0;
            ConfigService.MonitorGPU = on;
            HardwareService.SetMonitorGPU(on);
            ConfigService.Save("MonitorGPU");
            Views.FloatingWindow.UpdateAllText();
        }

        void MonFan_SelectionChanged(object s, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            bool on = MonFanCombo.SelectedIndex == 0;
            ConfigService.MonitorFan = on;
            HardwareService.MonitorFan = on;
            ConfigService.Save("MonitorFan");
            Views.FloatingWindow.UpdateAllText();
        }

        void MonRefresh_SelectionChanged(object s, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            bool high = MonRefreshCombo.SelectedIndex == 0;
            int interval = high ? 250 : 1000;
            if (_refreshTimer != null)
                _refreshTimer.Interval = TimeSpan.FromMilliseconds(interval);
            ConfigService.MonRefreshInterval = interval;
            ConfigService.Save("MonRefreshInterval");
        }

        void TempDisplay_SelectionChanged(object s, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            string mode = TempDispCombo.SelectedIndex == 1 ? "raw" : "smoothed";
            ConfigService.DisplayMode = mode;
            ConfigService.Save("DisplayMode");
            HardwareService.ApplyDisplayMode();
        }

        void RefreshGpuAppList()
        {
            GpuAppList.Items.Clear();
            try
            {
                var apps = GpuAppManager.GetGpuApps();
                foreach (var app in apps)
                {
                    var item = new ListBoxItem
                    {
                        Content = app.ProcessName + " (" + app.FilePath + ")",
                        Tag = app
                    };
                    GpuAppList.Items.Add(item);
                }
            }
            catch { }
        }

        void GpuAppLocate_Click(object sender, RoutedEventArgs e)
        {
            var item = GpuAppList.SelectedItem as ListBoxItem;
            var app = item?.Tag as GpuAppManager.GpuAppInfo;
            if (app == null || string.IsNullOrEmpty(app.FilePath)) return;
            try { Process.Start("explorer.exe", $"/select,\"{app.FilePath}\""); } catch { }
        }

        void SetGpuPreference(int value)
        {
            var item = GpuAppList.SelectedItem as ListBoxItem;
            var app = item?.Tag as GpuAppManager.GpuAppInfo;
            if (app == null || string.IsNullOrEmpty(app.FilePath)) return;
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\GraphicsSettings"))
                {
                    key?.SetValue(app.FilePath, value, Microsoft.Win32.RegistryValueKind.DWord);
                }
            }
            catch { }
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
                    @"SOFTWARE\Microsoft\DirectX\UserGpuPreferences"))
                {
                    key?.SetValue(app.FilePath, value, Microsoft.Win32.RegistryValueKind.DWord);
                }
            }
            catch { }
        }

        void GpuAppPrefAuto_Click(object sender, RoutedEventArgs e) { SetGpuPreference(2); }
        void GpuAppPrefPowerSave_Click(object sender, RoutedEventArgs e) { SetGpuPreference(0); }
        void GpuAppPrefHighPerf_Click(object sender, RoutedEventArgs e) { SetGpuPreference(1); }

        void RefreshGpuApps_Click(object sender, RoutedEventArgs e) { RefreshGpuAppList(); }

        void RestartGpu_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(Strings.GpuRestartConfirmMsg, Strings.GpuRestartConfirmTitle,
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                GpuAppManager.RestartGpu();
                MessageBox.Show(Strings.GpuRestartSuccess, Strings.Hint,
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        void GpuAppList_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var scroller = FindVisualChild<ScrollViewer>(GpuAppList);
            if (scroller != null)
            {
                scroller.ScrollToVerticalOffset(scroller.VerticalOffset - e.Delta);
                e.Handled = true;
            }
        }

        static T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is T result) return result;
                var deeper = FindVisualChild<T>(child);
                if (deeper != null) return deeper;
            }
            return null;
        }

    }
}
