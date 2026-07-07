// DashboardPage.cs - 主仪表盘页面 + 系统信息卡片
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using OmenSuperHub.Services;
using OmenSuperHub.Utils;
using static OmenSuperHub.OmenHardware;
using static OmenSuperHub.OmenLighting;
using HP.Omen.Core.Model.Device.Models;
using HP.Omen.Core.Model.Device.Enums;
using LibreHardwareType = LibreHardwareMonitor.Hardware.HardwareType;
using LibreSensorType = LibreHardwareMonitor.Hardware.SensorType;

namespace OmenSuperHub.Pages {
  public partial class DashboardPage : System.Windows.Controls.Page {
    bool _loading;
    bool _optionsBuilt;
    bool _gpuAppsLoaded;
    DispatcherTimer _refreshTimer;
    Brush _brushTextPrimary, _brushAccentGreen, _brushAccentYellow, _brushAccentRed, _brushAccentOmen;
    Brush _brushWhite, _brushBlack;
    Action<string> _presetCycledHandler;

    static readonly string IntelSvgPath = "m4.7 5.2h28.1v28.1h-28.1z m27.4 146.4v-101.2h-26.6v101.2zm176.8 1v-24.8c-3.9 0-7.2-.2-9.6-.6-2.8-.4-4.9-1.4-6.3-2.8s-2.3-3.4-2.8-6c-.4-2.5-.6-5.8-.6-9.8v-35.4h19.3v-22.8h-19.3v-39.5h-26.7v97.9c0 8.3.7 15.3 2.1 20.9 1.4 5.5 3.8 10 7.1 13.4s7.7 5.8 13 7.3c5.4 1.5 12.2 2.2 20.3 2.2zm152.8-1v-148.5h-26.7v148.5zm-224.5-91.3c-7.4-8-17.8-12-31-12-6.4 0-12.2 1.3-17.5 3.9-5.2 2.6-9.7 6.2-13.2 10.8l-1.5 1.9v-14.5h-26.3v101.2h26.5v-53.9 1.9c.3-9.5 2.6-16.5 7-21 4.7-4.8 10.4-7.2 16.9-7.2 7.7 0 13.6 2.4 17.5 7 3.8 4.6 5.8 11.1 5.8 19.4v53.7h26.9v-57.4c.1-14.4-3.7-25.8-11.1-33.8zm184 40.5c0-7.3-1.3-14.1-3.8-20.5-2.6-6.3-6.2-11.9-10.7-16.7-4.6-4.8-10.1-8.5-16.5-11.2s-13.5-4-21.2-4c-7.3 0-14.2 1.4-20.6 4.1-6.4 2.8-12 6.5-16.7 11.2s-8.5 10.3-11.2 16.7c-2.8 6.4-4.1 13.3-4.1 20.6s1.3 14.2 3.9 20.6 6.3 12 10.9 16.7 10.3 8.5 16.9 11.2c6.6 2.8 13.9 4.2 21.7 4.2 22.6 0 36.6-10.3 45-19.9l-19.2-14.6c-4 4.8-13.6 11.3-25.6 11.3-7.5 0-13.7-1.7-18.4-5.2-4.7-3.4-7.9-8.2-9.6-14.1l-.3-.9h79.5zm-79.3-9.3c0-7.4 8.5-20.3 26.8-20.4 18.3 0 26.9 12.9 26.9 20.3zm150.2 46.9c-.5-1.2-1.2-2.2-2.1-3.1s-1.9-1.6-3.1-2.1-2.5-.8-3.8-.8c-1.4 0-2.6.3-3.8.8s-2.2 1.2-3.1 2.1-1.6 1.9-2.1 3.1-.8 2.5-.8 3.8c0 1.4.3 2.6.8 3.8s1.2 2.2 2.1 3.1 1.9 1.6 3.1 2.1 2.5.8 3.8.8c1.4 0 2.6-.3 3.8-.8s2.2-1.2 3.1-2.1 1.6-1.9 2.1-3.1.8-2.5.8-3.8-.3-2.6-.8-3.8zm-1.6 7c-.4 1-1 1.9-1.7 2.6s-1.6 1.3-2.6 1.7-2 .6-3.2.6c-1.1 0-2.2-.2-3.2-.6s-1.9-1-2.6-1.7-1.3-1.6-1.7-2.6-.6-2-.6-3.2c0-1.1.2-2.2.6-3.2s1-1.9 1.7-2.6 1.6-1.3 2.6-1.7 2-.6 3.2-.6c1.1 0 2.2.2 3.2.6s1.9 1 2.6 1.7 1.3 1.6 1.7 2.6.6 2 .6 3.2c.1 1.2-.2 2.2-.6 3.2zm-5.6-2.4c.8-.1 1.4-.4 1.9-.9s.8-1.2.8-2.2c0-1.1-.3-1.9-1-2.5-.6-.6-1.7-.9-3-.9h-4.4v11.3h2.1v-4.6h1.5l2.8 4.6h2.2zm-1.1-1.6h-2.5v-3.2h2.5c.3 0 .6.1.9.2s.5.3.6.5.2.5.2.9-.1.7-.2.9c-.2.2-.4.4-.6.5-.3.1-.6.2-.9.2z";
    static readonly string AmdSvgPath = "M187.888 178.122H143.52l-13.573-32.738H56.003l-12.366 32.738H0L66.667 12.776h47.761zM91.155 52.286L66.912 116.53h50.913zM349.056 12.776h35.88v165.346h-41.219V74.842l-44.608 51.877h-6.301l-44.605-51.877V178.12h-41.219V12.776h35.88l53.092 61.336zM489.375 12.776c60.364 0 91.391 37.573 91.391 82.909 0 47.517-30.058 82.437-96 82.437h-68.369V12.776zm-31.762 135.041h26.906c41.457 0 53.823-28.129 53.823-52.377 0-28.368-15.276-52.363-54.308-52.363h-26.422v104.74zM662.769 51.981L610.797 0H800v189.21l-51.972-51.975V51.981zM662.708 62.397L609.2 115.903v74.899h74.889l53.505-53.506h-74.886z";
    static readonly string NvidiaSvgPath = "M384.195 282.109c0 3.771-2.769 6.302-6.047 6.302v-.023c-3.371.023-6.089-2.508-6.089-6.278 0-3.769 2.718-6.293 6.089-6.293 3.279-.001 6.047 2.523 6.047 6.292zm2.453 0c0-5.176-4.02-8.18-8.5-8.18-4.511 0-8.531 3.004-8.531 8.18 0 5.172 4.021 8.188 8.531 8.188 4.48 0 8.5-3.016 8.5-8.188m-9.91.692h.91l2.109 3.703h2.315l-2.336-3.859c1.207-.086 2.2-.66 2.2-2.285 0-2.02-1.393-2.668-3.75-2.668h-3.411v8.812h1.961l.002-3.703m0-1.492v-2.121h1.364c.742 0 1.753.06 1.753.965 0 .984-.523 1.156-1.398 1.156h-1.719M329.406 237.027l10.598 28.992H318.48l10.926-28.992zm-11.35-11.289l-24.423 61.88h17.245l3.863-10.935h28.903l3.656 10.935h18.722l-24.605-61.888-23.361.008zm-49.033 61.903h17.497v-61.922l-17.5-.004.003 61.926zm-121.467-61.926l-14.598 49.078-13.984-49.074-18.879-.004 19.972 61.926h25.207l20.133-61.926h-17.851zm70.725 13.484h7.521c10.909 0 17.966 4.898 17.966 17.609 0 12.713-7.057 17.612-17.966 17.612h-7.521v-35.221zm-17.35-13.484v61.926h28.365c15.113 0 20.049-2.512 25.385-8.147 3.769-3.957 6.207-12.642 6.207-22.134 0-8.707-2.063-16.469-5.66-21.305-6.48-8.648-15.816-10.34-29.75-10.34h-24.547zm-165.743-.086v62.012h17.645v-47.086l13.672.004c4.527 0 7.754 1.129 9.934 3.457 2.765 2.945 3.894 7.699 3.894 16.396v27.229h17.098v-34.262c0-24.453-15.586-27.75-30.836-27.75H35.188zm137.583.086l.007 61.926h17.489v-61.926h-17.496zM82.211 102.414s22.504-33.203 67.437-36.638V53.73c-49.769 3.997-92.867 46.149-92.867 46.149s24.41 70.564 92.867 77.026v-12.804c-50.237-6.32-67.437-61.687-67.437-61.687zm67.437 36.223v11.727c-37.968-6.77-48.507-46.237-48.507-46.237s18.23-20.195 48.507-23.47v12.867c-.023 0-.039-.007-.058-.007-15.891-1.907-28.305 12.938-28.305 12.938s6.958 24.99 28.363 32.182m0-107.125V53.73c1.461-.112 2.922-.207 4.391-.257 56.582-1.907 93.449 46.406 93.449 46.406s-42.343 51.488-86.457 51.488c-4.043 0-7.828-.375-11.383-1.005v13.739a75.04 75.04 0 0 0 9.481.612c41.051 0 70.738-20.965 99.484-45.778 4.766 3.817 24.278 13.103 28.289 17.167-27.332 22.92 8.438 33.352 24.821 41.848l-6.805 15.332s-40.469-17.383-68.703-6.379c-28.234 11.004-70.516 46.738-70.516 46.738s-7.676-32.867-42.668-50.648c-27.927-14.195-59.352-12.7-78.909-4.039L0 185.375s37.531 33.531 91.246 41.051l-5.883 12.973s-37.016-4.273-73.937 14.234c-36.926 18.508-63.535 57.355-77.355 91.403 0 0 26.039 40.011 82.227 49.88l-4.676 10.242s-60.598-3.441-100.754 15.5c-40.156 18.941-54.563 42.801-54.563 42.801l87.035-80.433s70.148-58.586 113.902-22.375c43.754 36.215 68.957 85.246 68.957 85.246l47.902-42.027s-35.039-66.816-85.43-102.637c-39.602-28.164-91.652-34.285-91.652-34.285l2.97-6.508c17.945-1.953 37.621-6.679 48.676-13.105 16.148-9.375 26.137-21.261 26.137-21.261s9.676 34.082 37.996 41.602c37.926 10.094 71.832-7.844 80.617-18.383 2.04-2.445-17.652-6.754-45.148-6.086l2.621-6.586c24.59-3.571 49.855-1.813 64.18 15.812 0 0-4.793-26.25-28.121-40.875-23.328-14.625-53.512-10.871-72.758-1.035l2.586-6.375c25.293-6.902 53.676-3.094 72.691 11.25 0 0-1.438-28.031-23.805-44.777-22.367-16.746-52.324-18.703-71.043-10.695l2.648-6.484c23.156-7.277 53.625-4.172 74.156 12.949 0 0-3.453-28.899-28.008-48.613-24.559-19.714-57.195-22.496-78.738-15.637l1.898-6.219c20.047-6.113 73.504-12.937 107.969 28.418 0 0 .008.004.012.004 4.808 5.75 12.511 9.562 24.516 9.562 16.172 0 31.281-11.422 36.617-20.242 9.812-16.227 5.28-37.804-7.914-49.992-3.457-3.187-14.953-12.614-25.402-14.957l2.926-6.441c13.617 3.429 30.363 16.441 34.067 29.066 4.894 16.672 3.105 36.61-9.515 51.524-9.441 11.168-25.48 21.027-44.246 16.48-11.027-2.672-19.73-9.113-25.828-17.156-18.586-24.543-37.082-43.051-73.246-37.602l1.586-5.883c19.332 2.734 38.93 11.274 52.558 25.637 14.762 15.578 21.707 33 23.012 39.258 4.43 21.164-4.625 42.844-20.25 56.5-11.352 9.918-27.617 16.398-44.93 14.414-6.863-.785-13.203-3.188-18.516-6.883 0 0-35.125 14.367-72.016 10.578-36.89-3.789-60.421-30.125-60.421-30.125s19.683-18.203 62.98-12.125c29.28 4.102 49.391 22.52 49.391 22.52s5.203-21.402-10.375-46.328c-11.281-18.039-30.09-31.258-52.132-35.894l2.024-4.445c27.031 6.488 51.188 38.078 51.188 38.078s23.308-4.308 27.449-21.239c2.301-9.406 1.832-21.34-3.602-30.559 0 0 39.722 14.402 64.852 55.851 25.125 41.453 38.722 101.809 38.722 101.809l81.718-17.867s-16.036-46.001-47.852-85.258c-25.792-31.829-55.262-47.716-55.262-47.716l3.691-8.105c10.605 4.946 20.826 7.125 33.277 7.125 24.903 0 45.634-12.844 49.363-17.527l19.96 28.813s1.527 8.059 7.668 11.637c3.757 2.188 7.938 2.25 12.398.691 3.844-1.344 7.48-4.446 9.219-9.895 1.543-4.836.097-10.703-4.425-14.891-4.062-3.758-8.195-5.734-14.406-5.734-1.68 0-3.32.195-4.926.574l1.129-2.484";

    [StructLayout(LayoutKind.Sequential)]
    struct MEMORYSTATUSEX {
      public uint dwLength;
      public uint dwMemoryLoad;
      public ulong ullTotalPhys;
      public ulong ullAvailPhys;
      public ulong ullTotalPageFile;
      public ulong ullAvailPageFile;
      public ulong ullTotalVirtual;
      public ulong ullAvailVirtual;
      public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll")]
    static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [DllImport("psapi.dll")]
    static extern bool EmptyWorkingSet(IntPtr hProcess);

    static MEMORYSTATUSEX GetMemoryStatus() {
      var mem = new MEMORYSTATUSEX();
      mem.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
      GlobalMemoryStatusEx(ref mem);
      return mem;
    }

    public DashboardPage() {
      InitializeComponent();
      _presetCycledHandler = presetName => { _ = RefreshNvidiaPowerLimitAsync(); };
      Loaded += (s, e) => {
        if (!_optionsBuilt) {
          _brushTextPrimary = FindResource("TextPrimaryBrush") as Brush;
          _brushAccentGreen = FindResource("AccentGreenBrush") as Brush;
          _brushAccentYellow = FindResource("AccentYellowBrush") as Brush;
          _brushAccentRed = FindResource("AccentRedBrush") as Brush;
          _brushAccentOmen = FindResource("AccentOmenBrush") as Brush;
          _brushWhite = new SolidColorBrush(Colors.White);
          _brushBlack = new SolidColorBrush(Colors.Black);
          _optionsBuilt = true;
        }

        _loading = true;
        LoadPresetState();
        LoadSysInfoState();
        _loading = false;
        RefreshDashboard();
        RefreshSysInfo();
        Billboard();
        if (!_gpuAppsLoaded) {
          Dispatcher.BeginInvoke(new Action(RefreshGpuAppList), DispatcherPriority.Background);
          _ = RefreshNvidiaPowerLimitAsync();
          _gpuAppsLoaded = true;
        }
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ConfigService.MonRefreshInterval) };
        _refreshTimer.Tick += (s2, e2) => {
          // ponytail: move ALL hardware queries (WMI/SMBIOS/LibreHW) to a
          // background thread so window dragging never blocks on them.
          // Only read ConfigService (registry-backed, <1 µs) on the UI thread.
          bool cpuOn = ConfigService.MonitorCPU;
          bool gpuOn = ConfigService.MonitorGPU;
          bool memOn = ConfigService.MonitorMemory;
          string presetKey = ConfigService.Preset;
          string fc = ConfigService.FanControl;
          string ft = ConfigService.FanTable;
          Task.Run(() => {
            var mem = memOn ? GetMemoryStatus() : default;
            int cpuTemp = cpuOn ? (int)HardwareService.GetDisplayCpuTemp() : 0;
            double cpuUtil = cpuOn ? HardwareService.CPUUsage : 0;
            double cpuFan = cpuOn ? HardwareService.FanSpeedNow[0] * 100 : 0;
            double cpuPower = cpuOn ? HardwareService.CPUPower : 0;
            int gpuTemp = gpuOn ? (int)HardwareService.GetDisplayGpuTemp() : 0;
            double gpuUtil = gpuOn ? HardwareService.GPUUsage : 0;
            double gpuFan = gpuOn ? HardwareService.FanSpeedNow[1] * 100 : 0;
            double gpuPower = gpuOn ? HardwareService.GPUPower : 0;
            int ir = GetSensorTemperature(0);
            int amb = GetSensorTemperature(1);
            int pch = GetSensorTemperature(2);
            int vr = GetSensorTemperature(3);
            // Push results back to UI thread
            Dispatcher.BeginInvoke(new Action(() =>
              RefreshDashboardCore(cpuOn, gpuOn, memOn, mem, cpuTemp, cpuUtil, cpuFan, cpuPower,
                  gpuTemp, gpuUtil, gpuFan, gpuPower, presetKey, fc, ft)
            ), DispatcherPriority.Background);
            Dispatcher.BeginInvoke(new Action(() =>
              RefreshSensorsCore(gpuOn ? gpuTemp : 0, cpuTemp, ir, amb, pch, vr)
            ), DispatcherPriority.Background);
            // ponytail: refresh floating window on same timer so network speed & other data stay live
            Dispatcher.BeginInvoke(new Action(() =>
              Views.FloatingWindow.UpdateAllText()
            ), DispatcherPriority.Background);
          });
        };
        _refreshTimer.Start();
        ConfigService.OnPresetCycled -= OnPresetCycled;
        ConfigService.OnPresetCycled -= _presetCycledHandler;
        ConfigService.OnPresetCycled += OnPresetCycled;
        ConfigService.OnPresetCycled += _presetCycledHandler;
      };
      Unloaded += (s, e) => {
        _refreshTimer?.Stop();
        _refreshTimer = null;
      };
    }

    void RefreshDashboard() {
      bool cpuOn = ConfigService.MonitorCPU;
      if (cpuOn) {
        int cpuTemp = (int)HardwareService.GetDisplayCpuTemp();
        CpuTempText.Text = cpuTemp.ToString();
        CpuTempBar.Foreground = GetGradientBrush(cpuTemp, 100);
        AnimateBar(CpuTempBar, cpuTemp);
        
        CpuUtilText.Text = HardwareService.CPUUsage.ToString("F0") + "%";
        CpuUtilBar.Foreground = GetGradientBrush(HardwareService.CPUUsage, 100);
        AnimateBar(CpuUtilBar, HardwareService.CPUUsage);
        
        CpuFanText.Text = (HardwareService.FanSpeedNow[0] * 100) + " RPM";
        CpuFanBar.Foreground = GetGradientBrush(HardwareService.FanSpeedNow[0] * 100, 6400);
        AnimateBar(CpuFanBar, HardwareService.FanSpeedNow[0] * 100);
        
        CpuPowerText.Text = HardwareService.CPUPower.ToString("F1") + " W";
        CpuPowerBar.Foreground = GetGradientBrush(HardwareService.CPUPower, 150);
        AnimateBar(CpuPowerBar, HardwareService.CPUPower);
      }
      CpuDetailPanel.Visibility = cpuOn ? Visibility.Visible : Visibility.Collapsed;
      CpuOffMessage.Visibility = cpuOn ? Visibility.Collapsed : Visibility.Visible;

      bool gpuOn = ConfigService.MonitorGPU;
      if (gpuOn) {
        int gpuTemp = (int)HardwareService.GetDisplayGpuTemp();
        GpuTempText.Text = gpuTemp.ToString();
        GpuTempBar.Foreground = GetGradientBrush(gpuTemp, 100);
        AnimateBar(GpuTempBar, gpuTemp);
        
        GpuUtilText.Text = HardwareService.GPUUsage.ToString("F0") + "%";
        GpuUtilBar.Foreground = GetGradientBrush(HardwareService.GPUUsage, 100);
        AnimateBar(GpuUtilBar, HardwareService.GPUUsage);
        
        GpuFanText.Text = (HardwareService.FanSpeedNow[1] * 100) + " RPM";
        GpuFanBar.Foreground = GetGradientBrush(HardwareService.FanSpeedNow[1] * 100, 6400);
        AnimateBar(GpuFanBar, HardwareService.FanSpeedNow[1] * 100);
        
        GpuPowerText.Text = HardwareService.GPUPower.ToString("F1") + " W";
        GpuPowerBar.Foreground = GetGradientBrush(HardwareService.GPUPower, 300);
        AnimateBar(GpuPowerBar, HardwareService.GPUPower);
      }
      GpuDetailPanel.Visibility = gpuOn ? Visibility.Visible : Visibility.Collapsed;
      GpuOffMessage.Visibility = gpuOn ? Visibility.Collapsed : Visibility.Visible;

      // Memory
      bool memOn = ConfigService.MonitorMemory;
      try {
        if (memOn) {
          var mem = GetMemoryStatus();
          double memPct = mem.dwMemoryLoad;
          double usedGB = (mem.ullTotalPhys - mem.ullAvailPhys) / (1024.0 * 1024 * 1024);
          double totalGB = mem.ullTotalPhys / (1024.0 * 1024 * 1024);
          double pageUsedGB = (mem.ullTotalPageFile - mem.ullAvailPageFile) / (1024.0 * 1024 * 1024);
          double pageTotalGB = mem.ullTotalPageFile / (1024.0 * 1024 * 1024);
          RamUsageText.Text = memPct.ToString("F0") + "%";
          RamUsageBar.Foreground = GetGradientBrush(memPct, 100);
          AnimateBar(RamUsageBar, memPct);
          RamDetailText.Text = $"{usedGB:F1} GB / {totalGB:F1} GB";
          RamVirtualText.Text = $"{pageUsedGB:F1} GB / {pageTotalGB:F1} GB";
          CleanMemBtn.IsEnabled = true;
        } else {
          RamUsageText.Text = "-";
          RamDetailText.Text = "-";
          RamVirtualText.Text = "-";
          CleanMemBtn.IsEnabled = false;
        }
      } catch { }

      // Storage
      try {
        RefreshStorage();
      } catch { }

      CurrentModeText.Text = PresetDisplayName(ConfigService.Preset);
      // ponytail: keep PresetCombo in sync with ConfigService the same way CurrentModeText does.
      // Without this, the combo's item content (set once in LoadPresetState) goes stale when a
      // custom preset name changes mid-session (rename in PerfPage, automation, etc.) and only
      // recovers after a page switch reloads LoadPresetState.
      SyncPresetComboDisplay();

      string fc = ConfigService.FanControl;
      string ft = ConfigService.FanTable;
      if (fc == "custom")
        CurrentFanText.Text = Strings.FanCustomCurve;
      else if (fc == "" || fc == "auto")
        CurrentFanText.Text = ft == "cool" ? Strings.FanCoolMode : Strings.FanSilentMode;
      else if (fc.EndsWith("%"))
        CurrentFanText.Text = Strings.FanManualMode + ": " + fc;
      else if (fc.Contains(" RPM"))
        CurrentFanText.Text = Strings.FanManualMode + ": " + fc;
      else
        CurrentFanText.Text = fc == "max" ? Strings.FanManualMode + ": 100%" : fc;

      PowerStatusText.Text = HardwareService.PowerOnline ? Strings.PowerStatusAC : Strings.PowerStatusDC;
      PowerStatusText.Foreground = HardwareService.PowerOnline ? _brushAccentGreen : _brushAccentYellow;
    }

    /// <summary>UI-only update from pre-fetched data (called from timer background thread).</summary>
    void RefreshDashboardCore(bool cpuOn, bool gpuOn, bool memOn, MEMORYSTATUSEX mem,
        int cpuTemp, double cpuUtil, double cpuFan, double cpuPower,
        int gpuTemp, double gpuUtil, double gpuFan, double gpuPower,
        string presetKey, string fc, string ft) {
      if (cpuOn) {
        CpuTempText.Text = cpuTemp.ToString();
        CpuTempBar.Foreground = GetGradientBrush(cpuTemp, 100);
        AnimateBar(CpuTempBar, cpuTemp);
        CpuUtilText.Text = cpuUtil.ToString("F0") + "%";
        CpuUtilBar.Foreground = GetGradientBrush(cpuUtil, 100);
        AnimateBar(CpuUtilBar, cpuUtil);
        CpuFanText.Text = cpuFan.ToString("F0") + " RPM";
        CpuFanBar.Foreground = GetGradientBrush(cpuFan, 6400);
        AnimateBar(CpuFanBar, cpuFan);
        CpuPowerText.Text = cpuPower.ToString("F1") + " W";
        CpuPowerBar.Foreground = GetGradientBrush(cpuPower, 150);
        AnimateBar(CpuPowerBar, cpuPower);
      }
      CpuDetailPanel.Visibility = cpuOn ? Visibility.Visible : Visibility.Collapsed;
      CpuOffMessage.Visibility = cpuOn ? Visibility.Collapsed : Visibility.Visible;
      if (gpuOn) {
        GpuTempText.Text = gpuTemp.ToString();
        GpuTempBar.Foreground = GetGradientBrush(gpuTemp, 100);
        AnimateBar(GpuTempBar, gpuTemp);
        GpuUtilText.Text = gpuUtil.ToString("F0") + "%";
        GpuUtilBar.Foreground = GetGradientBrush(gpuUtil, 100);
        AnimateBar(GpuUtilBar, gpuUtil);
        GpuFanText.Text = gpuFan.ToString("F0") + " RPM";
        GpuFanBar.Foreground = GetGradientBrush(gpuFan, 6400);
        AnimateBar(GpuFanBar, gpuFan);
        GpuPowerText.Text = gpuPower.ToString("F1") + " W";
        GpuPowerBar.Foreground = GetGradientBrush(gpuPower, 300);
        AnimateBar(GpuPowerBar, gpuPower);
      }
      GpuDetailPanel.Visibility = gpuOn ? Visibility.Visible : Visibility.Collapsed;
      GpuOffMessage.Visibility = gpuOn ? Visibility.Collapsed : Visibility.Visible;
      // Memory
      try {
        if (memOn) {
          double memPct = mem.dwMemoryLoad;
          double usedGB = (mem.ullTotalPhys - mem.ullAvailPhys) / (1024.0 * 1024 * 1024);
          double totalGB = mem.ullTotalPhys / (1024.0 * 1024 * 1024);
          double pageUsedGB = (mem.ullTotalPageFile - mem.ullAvailPageFile) / (1024.0 * 1024 * 1024);
          double pageTotalGB = mem.ullTotalPageFile / (1024.0 * 1024 * 1024);
          RamUsageText.Text = memPct.ToString("F0") + "%";
          RamUsageBar.Foreground = GetGradientBrush(memPct, 100);
          AnimateBar(RamUsageBar, memPct);
          RamDetailText.Text = $"{usedGB:F1} GB / {totalGB:F1} GB";
          RamVirtualText.Text = $"{pageUsedGB:F1} GB / {pageTotalGB:F1} GB";
          CleanMemBtn.IsEnabled = true;
        } else {
          RamUsageText.Text = "-";
          RamDetailText.Text = "-";
          RamVirtualText.Text = "-";
          CleanMemBtn.IsEnabled = false;
        }
      } catch { }
      CurrentModeText.Text = PresetDisplayName(presetKey);
      if (fc == "custom")
        CurrentFanText.Text = Strings.FanCustomCurve;
      else if (fc == "" || fc == "auto")
        CurrentFanText.Text = ft == "cool" ? Strings.FanCoolMode : Strings.FanSilentMode;
      else if (fc.EndsWith("%"))
        CurrentFanText.Text = Strings.FanManualMode + ": " + fc;
      else if (fc.Contains(" RPM"))
        CurrentFanText.Text = Strings.FanManualMode + ": " + fc;
      else
        CurrentFanText.Text = fc == "max" ? Strings.FanManualMode + ": 100%" : fc;
      PowerStatusText.Text = HardwareService.PowerOnline ? Strings.PowerStatusAC : Strings.PowerStatusDC;
      PowerStatusText.Foreground = HardwareService.PowerOnline ? _brushAccentGreen : _brushAccentYellow;
    }

    /// <summary>UI-only sensor temperature update from pre-fetched data.</summary>
    void RefreshSensorsCore(int cpuT, int gpuT, int ir, int amb, int pch, int vr) {
      SysCpuTempText.Text = Strings.SysCPUTemp + ": " + cpuT + " °C";
      SysGpuTempText.Text = Strings.SysGPUTemp + ": " + gpuT + " °C";
      SysIrSensorText.Text = Strings.SysIRSensor + ": " + ir + " °C";
      SysAmbientText.Text = Strings.SysAmbient + ": " + amb + " °C";
      SysPchText.Text = Strings.SysPCH + ": " + pch + " °C";
      SysVrText.Text = Strings.SysVR + ": " + vr + " °C";
    }

    void AnimateBar(ProgressBar bar, double newVal) {
      if (double.IsNaN(newVal) || double.IsInfinity(newVal)) newVal = 0;
      bar.Value = newVal;
    }

    void RefreshStorage() {
      StoragePanel.Children.Clear();
      int count = 0;
      foreach (var drive in DriveInfo.GetDrives()) {
        if (drive.DriveType != DriveType.Fixed || !drive.IsReady) continue;
        double totalGB = drive.TotalSize / (1024.0 * 1024 * 1024);
        double freeGB = drive.TotalFreeSpace / (1024.0 * 1024 * 1024);
        double usedGB = totalGB - freeGB;
        if (totalGB <= 0) continue;
        count++;
        double pct = usedGB / totalGB * 100;

        string label = drive.Name.TrimEnd('\\');
        string sizeStr = totalGB >= 1000 ? $"{usedGB / 1024:F1}TB/{totalGB / 1024:F1}TB" : $"{usedGB:F0}GB/{totalGB:F0}GB";

        var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) });

        grid.Children.Add(new TextBlock {
          Text = label, FontSize = 11,
          Foreground = TryFindResource("TextFillColorTertiaryBrush") as Brush,
          VerticalAlignment = VerticalAlignment.Center,
          TextTrimming = TextTrimming.CharacterEllipsis
        });

        var bar = new ProgressBar {
          Height = 8, VerticalAlignment = VerticalAlignment.Center,
          Margin = new Thickness(8, 0, 8, 0), Minimum = 0, Maximum = 100,
          Value = pct, Foreground = GetGradientBrush(pct, 100)
        };
        Grid.SetColumn(bar, 1);
        grid.Children.Add(bar);

        grid.Children.Add(new TextBlock {
          Text = sizeStr, FontSize = 11,
          Foreground = TryFindResource("TextFillColorSecondaryBrush") as Brush,
          VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right
        });
        Grid.SetColumn(grid.Children[grid.Children.Count - 1], 2);

        StoragePanel.Children.Add(grid);
      }
      if (count == 0) {
        StoragePanel.Children.Add(new TextBlock {
          Text = "-", FontSize = 11,
          Foreground = TryFindResource("TextFillColorDisabledBrush") as Brush,
          VerticalAlignment = VerticalAlignment.Center
        });
      }
    }

    void CleanMemory_Click(object sender, RoutedEventArgs e) {
      try {
        var memBefore = GetMemoryStatus();
        ulong usedBefore = memBefore.ullTotalPhys - memBefore.ullAvailPhys;

        CleanMemBtn.IsEnabled = false;
        CleanMemBtn.Content = "清理中...";

        foreach (var proc in Process.GetProcesses()) {
          try { using (proc) EmptyWorkingSet(proc.Handle); } catch { }
        }

        var memAfter = GetMemoryStatus();
        ulong usedAfter = memAfter.ullTotalPhys - memAfter.ullAvailPhys;
        long freed = (long)(usedBefore - usedAfter);
        if (freed < 0) freed = 0;

        string saved = RamDetailText.Text;
        RamDetailText.Text = freed > 0 ? $"已释放 {FormatBytes((ulong)freed)}" : "无需清理";
        RamDetailText.Foreground = freed > 0 ? _brushAccentGreen : _brushAccentYellow;

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        timer.Tick += (s, a) => {
          timer.Stop();
          RamDetailText.Foreground = _brushTextPrimary;
          CleanMemBtn.IsEnabled = true;
          CleanMemBtn.Content = Strings.DashboardMemoryCleanBtn;
        };
        timer.Start();
      } catch (Exception ex) {
        RamDetailText.Text = $"清理失败: {ex.Message}";
        RamDetailText.Foreground = _brushAccentRed;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        timer.Tick += (s, a) => {
          timer.Stop();
          RamDetailText.Foreground = _brushTextPrimary;
          CleanMemBtn.IsEnabled = true;
          CleanMemBtn.Content = Strings.DashboardMemoryCleanBtn;
        };
        timer.Start();
      }
    }

    static string FormatBytes(ulong bytes) {
      if (bytes >= 1024L * 1024 * 1024)
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
      if (bytes >= 1024 * 1024)
        return $"{bytes / (1024.0 * 1024):F1} MB";
      return $"{bytes / 1024} KB";
    }

    Brush GetGradientBrush(double val, double max) {
      double pct = max > 0 ? Math.Min(100, Math.Max(0, val / max * 100)) : 0;
      byte r, g, b;
      if (pct <= 35) {
        // white → green
        double t = pct / 35.0;
        r = (byte)Math.Round(255 + (74 - 255) * t);
        g = (byte)Math.Round(255 + (222 - 255) * t);
        b = (byte)Math.Round(255 + (128 - 255) * t);
      } else if (pct <= 60) {
        // green (constant, no yellow below 60%)
        r = 74; g = 222; b = 128;
      } else if (pct <= 82) {
        // green → yellow
        double t = (pct - 60) / 22.0;
        r = (byte)Math.Round(74 + (251 - 74) * t);
        g = (byte)Math.Round(222 + (191 - 222) * t);
        b = (byte)Math.Round(128 + (36 - 128) * t);
      } else if (pct <= 92) {
        // yellow → red
        double t = (pct - 82) / 10.0;
        r = (byte)Math.Round(251 + (239 - 251) * t);
        g = (byte)Math.Round(191 + (68 - 191) * t);
        b = (byte)Math.Round(36 + (68 - 36) * t);
      } else {
        // red → dark
        double t = Math.Min(1, (pct - 92) / 8.0);
        r = (byte)Math.Round(239 + (26 - 239) * t);
        g = (byte)Math.Round(68 + (26 - 68) * t);
        b = (byte)Math.Round(68 + (26 - 68) * t);
      }
      return new SolidColorBrush(Color.FromRgb(r, g, b));
    }


    void SetBrandLogos() {
      string cpuPath = null;
      Color cpuColor = Colors.Transparent;
      if (OmenHardware.HasIntelCpu()) {
        cpuPath = IntelSvgPath;
        cpuColor = Color.FromRgb(0, 0x71, 0xC5);
      } else if (OmenHardware.HasAmdCpu()) {
        cpuPath = AmdSvgPath;
        cpuColor = Color.FromRgb(0xED, 0x1C, 0x24);
      }
      if (cpuPath != null) {
        CpuBrandLogo.Data = Geometry.Parse(cpuPath);
        CpuBrandLogo.Fill = new SolidColorBrush(cpuColor);
      } else {
        CpuBrandLogo.Visibility = Visibility.Collapsed;
      }

      string gpuPath = null;
      Color gpuColor = Colors.Transparent;
      if (OmenHardware.HasNvidiaGpu()) {
        gpuPath = NvidiaSvgPath;
        gpuColor = Color.FromRgb(0x77, 0xB9, 0x00);
      } else if (OmenHardware.HasAmdGpu()) {
        gpuPath = AmdSvgPath;
        gpuColor = Color.FromRgb(0xED, 0x1C, 0x24);
      }
      if (gpuPath != null) {
        GpuBrandLogo.Data = Geometry.Parse(gpuPath);
        GpuBrandLogo.Fill = new SolidColorBrush(gpuColor);
      } else {
        GpuBrandLogo.Visibility = Visibility.Collapsed;
      }
    }

    void LoadPresetState() {
      PresetCombo.Items.Clear();
      var all = PresetManager.EnumerateAllPresets();
      int idx = -1;
      string current = ConfigService.Preset;
      if (string.IsNullOrEmpty(current)) current = "GpuPriority";
      for (int i = 0; i < all.Count; i++) {
        var (display, key) = all[i];
        PresetCombo.Items.Add(new ComboBoxItem { Content = display, Tag = key });
        if (key == current) idx = i;
      }
      bool prevLoading = _loading;
      _loading = true;
      if (idx >= 0) PresetCombo.SelectedIndex = idx;
      else if (PresetCombo.Items.Count > 1) PresetCombo.SelectedIndex = 1;
      _loading = prevLoading;
      UpdatePresetButtons();
    }

    static string PresetDisplayName(string key) {
      if (key == "Extreme") return Strings.PresetExtreme;
      if (key == "GpuPriority") return Strings.PresetGpuPriority;
      if (key == "LightUse") return Strings.PresetLightUse;
      return ConfigService.GetCustomPresetDisplayName(key);
    }

    // ponytail: lightweight in-place sync of the combo's item text + selection.
    // Called from RefreshDashboard so the combo reflects current ConfigService state
    // without a full LoadPresetState rebuild (which would flicker and reset dropdown state).
  void SyncPresetComboDisplay() {
    if (PresetCombo.Items.Count == 0) return;
    // ponytail: dynamic — sync each item's label to the live preset name, selection by Tag.
    var customs = PresetManager.EnumerateCustomPresets();
    var nameByKey = new Dictionary<string, string>(StringComparer.Ordinal)
    {
      { "Extreme", Strings.PresetExtreme },
      { "GpuPriority", Strings.PresetGpuPriority },
      { "LightUse", Strings.PresetLightUse },
    };
    foreach (var (display, key) in customs) nameByKey[key] = display;
    for (int i = 0; i < PresetCombo.Items.Count; i++) {
      if (PresetCombo.Items[i] is ComboBoxItem ci && ci.Tag as string is string k) {
        if (nameByKey.TryGetValue(k, out var nm) && !string.Equals(ci.Content as string, nm, StringComparison.Ordinal))
          ci.Content = nm;
      }
    }
    // sync selection to current preset
    string preset = ConfigService.Preset;
    if (string.IsNullOrEmpty(preset)) preset = "GpuPriority";
    int idx = -1;
    for (int i = 0; i < PresetCombo.Items.Count; i++) {
      if (PresetCombo.Items[i] is ComboBoxItem ci && ci.Tag as string == preset) { idx = i; break; }
    }
    if (idx >= 0 && PresetCombo.SelectedIndex != idx) {
      bool prev = _loading;
      _loading = true;
      PresetCombo.SelectedIndex = idx;
      _loading = false;
      UpdatePresetButtons();
    }
  }

    void Preset_SelectionChanged(object s, SelectionChangedEventArgs e) {
      if (_loading) return;
      var item = PresetCombo.SelectedItem as ComboBoxItem;
      if (item == null) return;
      string preset = item.Tag as string;
      if (string.IsNullOrEmpty(preset)) return;
      UpdatePresetButtons();
      // ponytail: set the display text immediately so it never goes stale
      CurrentModeText.Text = PresetDisplayName(preset);

      try { PresetManager.SwitchPreset(preset); } catch { }

      _loading = true;
      if (Application.Current.MainWindow is Views.MainWindow mainWindow)
        mainWindow.ApplyPresetHardware();
      _loading = false;
      Views.OsdWindow.ShowPresetOsd(preset);
      RefreshDashboard();
    }

    void OnPresetCycled(string preset) {
      Dispatcher.Invoke(() => {
        _loading = true;
        CurrentModeText.Text = PresetDisplayName(preset);
        try { LoadPresetState(); } catch { }
        // ponytail: dynamic — find index by tag in combo items
        int idx = -1;
        for (int i = 0; i < PresetCombo.Items.Count; i++) {
          if (PresetCombo.Items[i] is ComboBoxItem item && item.Tag as string == preset) { idx = i; break; }
        }
        if (idx >= 0) PresetCombo.SelectedIndex = idx;
        _loading = false;
        UpdatePresetButtons();
        try { RefreshDashboard(); } catch { }
      });
    }

    void UpdatePresetButtons() {
      var item = PresetCombo.SelectedItem as ComboBoxItem;
      string preset = item?.Tag as string;
      bool isCustom = !string.IsNullOrEmpty(preset) && PresetManager.IsCustom(preset);
      PresetRenameBtn.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;
      PresetSaveBtn.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;
    }

    void PresetRename_Click(object sender, RoutedEventArgs e) {
      var item = PresetCombo.SelectedItem as ComboBoxItem;
      string preset = item?.Tag as string;
      if (string.IsNullOrEmpty(preset) || PresetManager.IsBuiltIn(preset)) return;
      string currentName = ConfigService.GetCustomPresetDisplayName(preset);

      var dialog = new Wpf.Ui.Controls.FluentWindow {
        Title = Strings.RenamePresetTitle,
        Width = 340, Height = 200,
        WindowStartupLocation = WindowStartupLocation.CenterOwner,
        Owner = Window.GetWindow(this),
        ResizeMode = ResizeMode.NoResize,
        ShowInTaskbar = false,
        WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType.Mica,
        ExtendsContentIntoTitleBar = true,
        Background = Brushes.Transparent
      };

      var root = new Grid();
      root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
      root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
      root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

      root.Children.Add(new Border {
        BorderBrush = TryFindResource("BorderSubtleBrush") as Brush,
        BorderThickness = new Thickness(0, 0, 0, 1),
        Padding = new Thickness(16, 12, 16, 12),
        Child = new TextBlock {
          Text = Strings.RenamePresetTitle,
          FontSize = 14, FontWeight = FontWeights.SemiBold
        }
      });

      var card = new Border {
        Background = TryFindResource("CardBackgroundFillColorDefaultBrush") as Brush,
        CornerRadius = new CornerRadius(6),
        Padding = new Thickness(12, 14, 12, 14),
        Margin = new Thickness(12, 8, 12, 8),
        VerticalAlignment = VerticalAlignment.Top
      };
      var cardStack = new StackPanel();
      cardStack.Children.Add(new TextBlock {
        Text = Strings.RenamePresetPrompt,
        FontSize = 13,
        Margin = new Thickness(0, 0, 0, 8),
        Foreground = TryFindResource("TextPrimaryBrush") as Brush
      });
      var tb = new TextBox {
        Text = currentName,
        Height = 34,
        FontSize = 14,
        VerticalContentAlignment = VerticalAlignment.Center
      };
      cardStack.Children.Add(tb);
      card.Child = cardStack;

      var contentArea = new Grid();
      contentArea.Children.Add(card);
      Grid.SetRow(contentArea, 1);
      root.Children.Add(contentArea);

      var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
      var renameBtn = new Button {
        Content = Strings.CustomRename, MinWidth = 80, Height = 30,
        Padding = new Thickness(16, 4, 16, 4), Margin = new Thickness(0, 0, 8, 0)
      };
      renameBtn.Click += (s, a) => { dialog.DialogResult = true; };
      btnPanel.Children.Add(renameBtn);
      var cancelBtn = new Button {
        Content = Strings.AutomationCancel, MinWidth = 80, Height = 30,
        Padding = new Thickness(16, 4, 16, 4)
      };
      cancelBtn.Click += (s, a) => dialog.Close();
      btnPanel.Children.Add(cancelBtn);

      root.Children.Add(new Border {
        BorderBrush = TryFindResource("BorderSubtleBrush") as Brush,
        BorderThickness = new Thickness(0, 1, 0, 0),
        Padding = new Thickness(16, 8, 16, 8),
        Child = btnPanel
      });
      Grid.SetRow(root.Children[root.Children.Count - 1], 2);

      dialog.Content = root;

      if (dialog.ShowDialog() == true) {
        string newName = tb.Text.Trim();
        if (string.IsNullOrEmpty(newName)) {
          var errDialog = new Wpf.Ui.Controls.FluentWindow {
            Title = Strings.Error,
            Width = 340, Height = 180,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = dialog,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType.Mica,
            ExtendsContentIntoTitleBar = true,
            Background = Brushes.Transparent
          };
          var errRoot = new Grid();
          errRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
          errRoot.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
          errRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

          errRoot.Children.Add(new Border {
            BorderBrush = TryFindResource("BorderSubtleBrush") as Brush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(16, 12, 16, 12),
            Child = new TextBlock { Text = Strings.Error, FontSize = 14, FontWeight = FontWeights.SemiBold }
          });

          var errCard = new Border {
            Background = TryFindResource("CardBackgroundFillColorDefaultBrush") as Brush,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 14, 12, 14),
            Margin = new Thickness(12, 8, 12, 8),
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock {
              Text = Strings.RenamePresetError,
              FontSize = 13,
              Foreground = TryFindResource("TextPrimaryBrush") as Brush,
              TextWrapping = TextWrapping.Wrap
            }
          };
          var errContent = new Grid();
          errContent.Children.Add(errCard);
          Grid.SetRow(errContent, 1);
          errRoot.Children.Add(errContent);

          var okBtn = new Button {
            Content = Strings.ButtonOK, MinWidth = 80, Height = 30,
            Padding = new Thickness(16, 4, 16, 4),
            HorizontalAlignment = HorizontalAlignment.Right
          };
          okBtn.Click += (s2, a2) => errDialog.DialogResult = true;
          errRoot.Children.Add(new Border {
            BorderBrush = TryFindResource("BorderSubtleBrush") as Brush,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(16, 8, 16, 8),
            Child = okBtn
          });
          Grid.SetRow(errRoot.Children[errRoot.Children.Count - 1], 2);

          errDialog.Content = errRoot;
          errDialog.ShowDialog();
          return;
        }
        ConfigService.SetCustomPresetName(preset, newName);
        LoadPresetState();
      }
    }

    void PresetSave_Click(object sender, RoutedEventArgs e) {
      var item = PresetCombo.SelectedItem as ComboBoxItem;
      string preset = item?.Tag as string;
      if (string.IsNullOrEmpty(preset) || PresetManager.IsBuiltIn(preset)) return;

      PresetManager.SaveCustomPreset(preset);
      ConfigService.Save("Preset");

      _loading = true;
      int idx = PresetCombo.SelectedIndex;
      PresetCombo.SelectedIndex = idx;
      _loading = false;
    }

    void Billboard() { }

    bool _dashExpanded = true;
    const double DashCollapseWidth = 1000;

    void DashboardPage_SizeChanged(object sender, SizeChangedEventArgs e) {
      if (!e.WidthChanged) return;
      if (e.NewSize.Width > DashCollapseWidth) {
        if (!_dashExpanded) { _dashExpanded = true; ExpandDashGrids(); }
      } else {
        if (_dashExpanded) { _dashExpanded = false; CollapseDashGrids(); }
      }
    }

    void ExpandDashGrids() {
      LayoutDashGrid(MetricsGrid, 0, 2);
      LayoutDashGrid(StatusGrid, 0, 2);
      ExpandSysInfoGrid();
    }

    void CollapseDashGrids() {
      LayoutDashGrid(MetricsGrid, 0, 1);
      LayoutDashGrid(StatusGrid, 0, 1);
      CollapseSysInfoGrid();
    }

    void ExpandSysInfoGrid() {
      if (SysInfoGrid == null) return;
      var col1 = SysInfoGrid.ColumnDefinitions[1];
      col1.Width = new GridLength(1, GridUnitType.Star);
      var left = SysInfoGrid.Children[0] as FrameworkElement;
      var right = SysInfoGrid.Children[1] as FrameworkElement;
      if (left != null) { Grid.SetRow(left, 0); Grid.SetColumn(left, 0); Grid.SetColumnSpan(left, 1); left.Margin = new Thickness(0, 0, 4, 0); }
      if (right != null) { Grid.SetRow(right, 0); Grid.SetColumn(right, 1); Grid.SetColumnSpan(right, 1); right.Margin = new Thickness(4, 0, 0, 0); }
    }

    void CollapseSysInfoGrid() {
      if (SysInfoGrid == null) return;
      var col1 = SysInfoGrid.ColumnDefinitions[1];
      col1.Width = new GridLength(0, GridUnitType.Pixel);
      var left = SysInfoGrid.Children[0] as FrameworkElement;
      var right = SysInfoGrid.Children[1] as FrameworkElement;
      if (left != null) { Grid.SetRow(left, 0); Grid.SetColumn(left, 0); Grid.SetColumnSpan(left, 2); left.Margin = new Thickness(0); }
      if (right != null) { Grid.SetRow(right, 1); Grid.SetColumn(right, 0); Grid.SetColumnSpan(right, 2); right.Margin = new Thickness(0); }
    }

    void LayoutDashGrid(Grid grid, int col1, int col2) {
      var gapDef = grid.ColumnDefinitions[1];
      var gpuDef = grid.ColumnDefinitions[2];
      var cpu = grid.Children[0] as FrameworkElement;
      var gpu = grid.Children[1] as FrameworkElement;
      var mem = grid.Children.Count > 2 ? grid.Children[2] as FrameworkElement : null;
      var storage = grid.Children.Count > 3 ? grid.Children[3] as FrameworkElement : null;
      if (col1 == 0 && col2 == 2) {
        gapDef.Width = new GridLength(12, GridUnitType.Pixel);
        gpuDef.Width = new GridLength(1, GridUnitType.Star);
        if (cpu != null) { Grid.SetRow(cpu, 0); Grid.SetColumn(cpu, 0); Grid.SetColumnSpan(cpu, 1); cpu.Margin = new Thickness(0); }
        if (gpu != null) { Grid.SetRow(gpu, 0); Grid.SetColumn(gpu, 2); Grid.SetColumnSpan(gpu, 1); gpu.Margin = new Thickness(0); }
        if (mem != null) { Grid.SetRow(mem, 1); Grid.SetColumn(mem, 0); Grid.SetColumnSpan(mem, 1); mem.Margin = new Thickness(0, 12, 0, 0); }
        if (storage != null) { Grid.SetRow(storage, 1); Grid.SetColumn(storage, 2); Grid.SetColumnSpan(storage, 1); storage.Margin = new Thickness(0, 12, 0, 0); }
      } else {
        gapDef.Width = new GridLength(0, GridUnitType.Pixel);
        gpuDef.Width = new GridLength(0, GridUnitType.Pixel);
        if (cpu != null) { Grid.SetRow(cpu, 0); Grid.SetColumn(cpu, 0); Grid.SetColumnSpan(cpu, 3); cpu.Margin = new Thickness(0, 0, 0, 8); }
        if (gpu != null) { Grid.SetRow(gpu, 1); Grid.SetColumn(gpu, 0); Grid.SetColumnSpan(gpu, 3); gpu.Margin = new Thickness(0); }
        if (mem != null) { Grid.SetRow(mem, 2); Grid.SetColumn(mem, 0); Grid.SetColumnSpan(mem, 3); mem.Margin = new Thickness(0, 12, 0, 0); }
        if (storage != null) { Grid.SetRow(storage, 3); Grid.SetColumn(storage, 0); Grid.SetColumnSpan(storage, 3); storage.Margin = new Thickness(0, 12, 0, 0); }
      }
    }

    // ══════ SysInfoPage merged methods ══════

    void LoadSysInfoState() {
      MonCpuCombo.SelectedIndex = ConfigService.MonitorCPU ? 0 : 1;
      MonGpuCombo.SelectedIndex = ConfigService.MonitorGPU ? 0 : 1;
      MonFanCombo.SelectedIndex = ConfigService.MonitorFan ? 0 : 1;
      MonMemoryCombo.SelectedIndex = ConfigService.MonitorMemory ? 0 : 1;
      MonNetworkCombo.SelectedIndex = ConfigService.MonitorNetwork ? 0 : 1;
      MonFpsCombo.SelectedIndex = ConfigService.MonitorFPS ? 0 : 1;
      MonRefreshCombo.SelectedIndex = ConfigService.MonRefreshInterval <= 500 ? 0 : 1;
      TempDispCombo.SelectedIndex = ConfigService.DisplayMode == "raw" ? 1 : 0;
    }

    void RefreshSysInfo() {
      // ponytail: stale-cache detection — if the cached product name is
      // empty/unknown the initial query (possibly before PerformanceControl.dll
      // was present) returned garbage.  Force a re-fetch so the fix takes
      // effect automatically once.
      bool staleCache = !string.IsNullOrEmpty(ConfigService.SysManufacturer)
          && (string.IsNullOrEmpty(ConfigService.SysProductName)
              || ConfigService.SysProductName == Strings.SysUnknown
              || ConfigService.SysProductName == "未知");
      if (!string.IsNullOrEmpty(ConfigService.SysManufacturer) && !staleCache) {
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
	            Strings.ValidationUnsupported);
        SysBoardText.Text = Strings.SysBoardProduct + ": " + ConfigService.SysBoardProduct;
        SysCpuTjmaxText.Text = Strings.SysCpuTjMax + ": " + ConfigService.SysCpuTjmax + " °C";
        SysNvidiaTjmaxText.Text = ConfigService.SysNvidiaTjmax > 0
            ? Strings.SysNvidiaTjMax + ": " + ConfigService.SysNvidiaTjmax + " °C"
            : "";
        SysNvidiaPowerText.Text = !string.IsNullOrEmpty(ConfigService.SysNvidiaPowerMin)
            ? Strings.SysNvidiaPowerLimitText(ConfigService.SysNvidiaPowerMin + " / " + ConfigService.SysNvidiaPowerMax)
            : "";
        SysKbLightTypeText.Text = Strings.SysKbType + ": " + GetKeyboardTypeName((NbKeyboardLightingType)ConfigService.SysKbRaw);
        SysPawnIoText.Text = ConfigService.SysPawnIoText;
        return;
      }
      Task.Run(() => {
        string mfr = null, model = null, bios = null, cpu = null, gpu = null;
        int adapterW = 0;
        string pn = null, board = null;
        int validation = 0, tj = 0, nvidiaTj = 0;
        float[] powerLimits = null;
        string kb = null;
        string pawnIoText = "", cpuTemp = "", gpuTemp = "", irTemp = "", ambTemp = "", pchTemp = "", vrTemp = "";
        try {
          using (var searcher = new ManagementObjectSearcher("SELECT Manufacturer, Model FROM Win32_ComputerSystem"))
          using (var col = searcher.Get()) {
            foreach (ManagementBaseObject obj in col) {
              mfr = obj["Manufacturer"]?.ToString() ?? Strings.SysUnknown;
              model = obj["Model"]?.ToString() ?? Strings.SysUnknown;
            }
          }
          bios = GetBiosVersion();
          cpu = GetCpuModel();
          var gpuNames = GpuAppManager.GetAllGpuNamesList();
          gpu = gpuNames.Count > 0 ? string.Join("; ", gpuNames) : Strings.SysUnknown;
          adapterW = GetAdapterPower();
        } catch (Exception ex) {
          Logger.Error("RefreshSysInfo WMI error: " + ex.Message);
        }
        try {
          // ponytail: DeviceModel.OmenPlatform is a struct — the getter
          // itself never throws, but platform.DisplayName may return null
          // when the SDK doesn't know this platform.  Fallback to the WMI
          // model name (already fetched) so "机型名称" is never blank.
          var platform = DeviceModel.OmenPlatform;
          pn = platform.DisplayName;
          if (string.IsNullOrEmpty(pn)) pn = model;
        } catch {
          if (string.IsNullOrEmpty(pn)) pn = model;
        }
        try { board = DeviceModel.ThisSystemID; } catch { }
        try { validation = Validation(); } catch { }
        try { tj = GetCpuTjmax(); } catch { }
        try { nvidiaTj = GpuAppManager.GetGpuTemperatureTarget(); } catch { }
        try { powerLimits = GpuAppManager.GetGpuPowerLimits(); } catch { }
        int kbRaw = 0;
        try { kb = GetKeyboardTypeName((NbKeyboardLightingType)(kbRaw = (int)GetKeyboardType())); } catch { }
        try {
          pawnIoText = LibreHardwareMonitor.PawnIo.PawnIo.IsInstalled
              ? Strings.SysPawnInstalled + "v" + LibreHardwareMonitor.PawnIo.PawnIo.Version().ToString()
              : Strings.SysPawnMissing;
        } catch { pawnIoText = Strings.SysPawnMissing; }
        try {
          cpuTemp = Strings.SysCPUTemp + ": " + (int)HardwareService.CPUTemp + " °C";
          gpuTemp = Strings.SysGPUTemp + ": " + (int)HardwareService.GPUTemp + " °C";
          irTemp = Strings.SysIRSensor + ": " + GetSensorTemperature(0) + " °C";
          ambTemp = Strings.SysAmbient + ": " + GetSensorTemperature(1) + " °C";
          pchTemp = Strings.SysPCH + ": " + GetSensorTemperature(2) + " °C";
          vrTemp = Strings.SysVR + ": " + GetSensorTemperature(3) + " °C";
        } catch { }
        string _pn = pn, _board = board;
        int _validation = validation, _tj = tj, _nvidiaTj = nvidiaTj, _kbRaw = kbRaw;
        string _kb = kb;
        float[] _powerLimits = powerLimits;
        Dispatcher.InvokeAsync(() => {
          var updates = new Dictionary<string, object>();
          if (mfr != null) {
            SysManufacturerText.Text = Strings.SysManufacturer + ": " + mfr;
            SysModelText.Text = Strings.SysModel + ": " + model;
            if (ConfigService.SysManufacturer != mfr) { ConfigService.SysManufacturer = mfr; updates["SysManufacturer"] = mfr; }
            if (ConfigService.SysModel != model) { ConfigService.SysModel = model; updates["SysModel"] = model; }
            SysBiosText.Text = Strings.SysBiosVersion + ": " + bios;
            if (ConfigService.SysBios != bios) { ConfigService.SysBios = bios; updates["SysBios"] = bios; }
            SysCpuText.Text = Strings.SysCpuModel + ": " + cpu;
            if (ConfigService.SysCpu != cpu) { ConfigService.SysCpu = cpu; updates["SysCpu"] = cpu; }
            SysGpuText.Text = Strings.SysGpuList + ": " + gpu;
            if (ConfigService.SysGpu != gpu) { ConfigService.SysGpu = gpu; updates["SysGpu"] = gpu; }
            SysAdapterText.Text = Strings.SysAdapterPower + ": " + adapterW + " W";
            if (ConfigService.SysAdapterPower != adapterW) { ConfigService.SysAdapterPower = adapterW; updates["SysAdapterPower"] = adapterW; }
            SysDriverModelText.Text = Strings.SysModel + ": " + model;
          }
          SysProductNameText.Text = Strings.SysModelName + ": " + (_pn ?? Strings.SysUnknown);
          if (ConfigService.SysProductName != (_pn ?? Strings.SysUnknown)) { ConfigService.SysProductName = _pn ?? Strings.SysUnknown; updates["SysProductName"] = _pn ?? Strings.SysUnknown; }
          SysValidationText.Text = Strings.SysModelValidation + ": " + (
              _validation >= 2 ? Strings.ValidationGamingProduct :
              _validation == 1 ? Strings.ValidationUnsupported : Strings.ValidationUnsupported);
          if (ConfigService.SysValidation != _validation) { ConfigService.SysValidation = _validation; updates["SysValidation"] = _validation; }
          SysBoardText.Text = Strings.SysBoardProduct + ": " + (_board ?? Strings.SysUnknown);
          if (ConfigService.SysBoardProduct != (_board ?? Strings.SysUnknown)) { ConfigService.SysBoardProduct = _board ?? Strings.SysUnknown; updates["SysBoardProduct"] = _board ?? Strings.SysUnknown; }
          SysCpuTjmaxText.Text = Strings.SysCpuTjMax + ": " + _tj + " °C";
          if (ConfigService.SysCpuTjmax != _tj) { ConfigService.SysCpuTjmax = _tj; updates["SysCpuTjmax"] = _tj; }
          SysNvidiaTjmaxText.Text = _nvidiaTj > 0 ? Strings.SysNvidiaTjMax + ": " + _nvidiaTj + " °C" : "";
          if (ConfigService.SysNvidiaTjmax != _nvidiaTj) { ConfigService.SysNvidiaTjmax = _nvidiaTj; updates["SysNvidiaTjmax"] = _nvidiaTj; }
          if (_powerLimits != null && _powerLimits[0] > 0) {
            SysNvidiaPowerText.Text = Strings.SysNvidiaPowerLimitText($"{_powerLimits[0]:F0}W / {_powerLimits[1]:F0}W");
            string minStr = $"{_powerLimits[0]:F0}W";
            string maxStr = $"{_powerLimits[1]:F0}W";
            if (ConfigService.SysNvidiaPowerMin != minStr) { ConfigService.SysNvidiaPowerMin = minStr; updates["SysNvidiaPowerMin"] = minStr; }
            if (ConfigService.SysNvidiaPowerMax != maxStr) { ConfigService.SysNvidiaPowerMax = maxStr; updates["SysNvidiaPowerMax"] = maxStr; }
          }
          SysKbLightTypeText.Text = Strings.SysKbType + ": " + (_kb ?? Strings.SysUnknown);
          if (ConfigService.SysKbType != (_kb ?? Strings.SysUnknown)) {
            ConfigService.SysKbType = _kb ?? Strings.SysUnknown;
            updates["SysKbType"] = _kb ?? Strings.SysUnknown;
          }
          if (ConfigService.SysKbRaw != _kbRaw) {
            ConfigService.SysKbRaw = _kbRaw;
            updates["SysKbRaw"] = _kbRaw;
          }
          SysPawnIoText.Text = pawnIoText;
          if (ConfigService.SysPawnIoText != pawnIoText) {
            ConfigService.SysPawnIoText = pawnIoText;
            updates["SysPawnIoText"] = pawnIoText;
          }
          if (updates.Count > 0) ConfigService.BatchSave(updates);
          SysCpuTempText.Text = cpuTemp;
          SysGpuTempText.Text = gpuTemp;
          SysIrSensorText.Text = irTemp;
          SysAmbientText.Text = ambTemp;
          SysPchText.Text = pchTemp;
          SysVrText.Text = vrTemp;
        }, DispatcherPriority.Background);
      });
    }

    void RefreshSensors() {
      int cpuT = (int)HardwareService.GetDisplayCpuTemp();
      int gpuT = (int)HardwareService.GetDisplayGpuTemp();
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
      _ = RefreshNvidiaPowerLimitAsync();
    }

    int GetCpuTjmax() {
      try {
        foreach (var hw in HardwareService.LibreComputer.Hardware) {
          if (hw.HardwareType == LibreHardwareType.Cpu) {
            hw.Update();
            foreach (var sensor in hw.Sensors) {
              if (sensor.SensorType == LibreSensorType.Temperature && sensor.Parameters.Count > 0)
                return (int)sensor.Parameters[0].Value;
            }
          }
        }
      } catch { }
      return 100;
    }

    async Task RefreshNvidiaPowerLimitAsync() {
      try {
        await Task.Delay(500);
        var powerLimits = GpuAppManager.GetGpuPowerLimits();
        if (powerLimits[0] > 0) {
          await Dispatcher.InvokeAsync(() => {
            SysNvidiaPowerText.Text = Strings.SysNvidiaPowerLimitText($"{powerLimits[0]:F0}W / {powerLimits[1]:F0}W");
            ConfigService.SysNvidiaPowerMin = $"{powerLimits[0]:F0}W";
            ConfigService.SysNvidiaPowerMax = $"{powerLimits[1]:F0}W";
          });
        }
      } catch { }
    }

    void SysInfoRefresh_Click(object sender, RoutedEventArgs e) { RefreshSensors(); }

    bool FanNeedsTemperature() {
      string fc = ConfigService.FanControl;
      return !fc.EndsWith("%") && !fc.Contains(" RPM");
    }

    void MonCpu_SelectionChanged(object s, SelectionChangedEventArgs e) {
      if (_loading) return;
      bool on = MonCpuCombo.SelectedIndex == 0;
      if (!on && !ConfigService.MonitorGPU && FanNeedsTemperature()) {
        DialogHelper.Warn(Strings.MonitorAutoFanWarning, Strings.Hint);
        _loading = true; MonCpuCombo.SelectedIndex = 0; _loading = false;
        return;
      }
      ConfigService.MonitorCPU = on;
      HardwareService.MonitorCPU = on;
      HardwareService.LibreComputer.IsCpuEnabled = on;
      ConfigService.Save("MonitorCPU");
      Views.FloatingWindow.UpdateAllText();
    }

    void MonGpu_SelectionChanged(object s, SelectionChangedEventArgs e) {
      if (_loading) return;
      bool on = MonGpuCombo.SelectedIndex == 0;
      if (!on && !ConfigService.MonitorCPU && FanNeedsTemperature()) {
        DialogHelper.Warn(Strings.MonitorAutoFanWarning, Strings.Hint);
        _loading = true; MonGpuCombo.SelectedIndex = 0; _loading = false;
        return;
      }
      ConfigService.MonitorGPU = on;
      HardwareService.SetMonitorGPU(on);
      ConfigService.Save("MonitorGPU");
      Views.FloatingWindow.UpdateAllText();
    }

    void MonFan_SelectionChanged(object s, SelectionChangedEventArgs e) {
      if (_loading) return;
      bool on = MonFanCombo.SelectedIndex == 0;
      ConfigService.MonitorFan = on;
      HardwareService.MonitorFan = on;
      ConfigService.Save("MonitorFan");
      Views.FloatingWindow.UpdateAllText();
    }

    void MonMemory_SelectionChanged(object s, SelectionChangedEventArgs e) {
      if (_loading) return;
      bool on = MonMemoryCombo.SelectedIndex == 0;
      ConfigService.MonitorMemory = on;
      ConfigService.Save("MonitorMemory");
    }

    void MonNetwork_SelectionChanged(object s, SelectionChangedEventArgs e) {
      if (_loading) return;
      bool on = MonNetworkCombo.SelectedIndex == 0;
      ConfigService.MonitorNetwork = on;
      ConfigService.Save("MonitorNetwork");
    }

    void MonFps_SelectionChanged(object s, SelectionChangedEventArgs e) {
      if (_loading) return;
      bool on = MonFpsCombo.SelectedIndex == 0;
      ConfigService.MonitorFPS = on;
      ConfigService.Save("MonitorFPS");
    }

    void MonRefresh_SelectionChanged(object s, SelectionChangedEventArgs e) {
      if (_loading) return;
      int interval = MonRefreshCombo.SelectedIndex == 0 ? 250 : 2000;
      ConfigService.MonRefreshInterval = interval;
      ConfigService.Save("MonRefreshInterval");
      // ponytail: also update the live timer so the change takes effect immediately
      if (_refreshTimer != null) _refreshTimer.Interval = TimeSpan.FromMilliseconds(interval);
      Views.FloatingWindow.UpdateRefreshInterval();
    }

    void TempDisplay_SelectionChanged(object s, SelectionChangedEventArgs e) {
      if (_loading) return;
      string mode = TempDispCombo.SelectedIndex == 1 ? "raw" : "smoothed";
      ConfigService.DisplayMode = mode;
      ConfigService.Save("DisplayMode");
      HardwareService.ApplyDisplayMode();
    }

    void RefreshGpuAppList() {
      GpuAppList.Items.Clear();
      try {
        var apps = GpuAppManager.GetGpuApps();
        foreach (var app in apps) {
          var item = new ListBoxItem { Content = app.ProcessName + " (" + app.FilePath + ")", Tag = app };
          GpuAppList.Items.Add(item);
        }
      } catch { }
    }

    void GpuAppLocate_Click(object sender, RoutedEventArgs e) {
      var item = GpuAppList.SelectedItem as ListBoxItem;
      var app = item?.Tag as GpuAppManager.GpuAppInfo;
      if (app == null || string.IsNullOrEmpty(app.FilePath)) return;
        try { Process.Start("explorer.exe", $"/select,\"{app.FilePath}\"")?.Dispose(); } catch { }
    }

    void GpuAppEndTask_Click(object sender, RoutedEventArgs e) {
      var item = GpuAppList.SelectedItem as ListBoxItem;
      var app = item?.Tag as GpuAppManager.GpuAppInfo;
      if (app == null || app.ProcessId <= 0) return;
      if (!DialogHelper.Confirm($"{Strings.GpuAppEndTask} '{app.ProcessName}' (PID {app.ProcessId})?", Strings.Hint))
        return;
      bool ok = false;
      try {
        // ponytail: try PID first, then fall back to image name (same as manual taskkill /F /IM)
        var psi = new ProcessStartInfo("taskkill", $"/F /PID {app.ProcessId}") {
          UseShellExecute = false, CreateNoWindow = true
        };
        using (var p = Process.Start(psi)) {
          if (p != null) { p.WaitForExit(2000); ok = p.ExitCode == 0; }
        }
        if (!ok && !string.IsNullOrEmpty(app.ProcessName)) {
          string imageName = System.IO.Path.GetFileName(app.ProcessName);
          psi = new ProcessStartInfo("taskkill", $"/F /IM {imageName}") {
            UseShellExecute = false, CreateNoWindow = true
          };
          using (var p = Process.Start(psi)) {
            if (p != null) { p.WaitForExit(2000); ok = p.ExitCode == 0; }
          }
        }
        if (ok)
          DialogHelper.Info($"进程 '{app.ProcessName}' 已终止", Strings.Hint);
        else
          DialogHelper.Warn($"进程 '{app.ProcessName}' 终止失败，PID可能已过期或权限不足", Strings.Hint);
      } catch (Exception ex) {
        DialogHelper.Error($"结束进程失败: {ex.Message}", Strings.Hint);
        Logger.Error($"结束进程失败: {ex.Message}");
      }
      RefreshGpuAppList();
    }

    void SetGpuPreference(int value) {
      var item = GpuAppList.SelectedItem as ListBoxItem;
      var app = item?.Tag as GpuAppManager.GpuAppInfo;
      if (app == null || string.IsNullOrEmpty(app.FilePath)) return;
      try {
        using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
            @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\GraphicsSettings"))
          key?.SetValue(app.FilePath, value, Microsoft.Win32.RegistryValueKind.DWord);
      } catch { }
      try {
        using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
            @"SOFTWARE\Microsoft\DirectX\UserGpuPreferences"))
          key?.SetValue(app.FilePath, value, Microsoft.Win32.RegistryValueKind.DWord);
      } catch { }
    }

    void GpuAppPrefAuto_Click(object sender, RoutedEventArgs e) { SetGpuPreference(2); }
    void GpuAppPrefPowerSave_Click(object sender, RoutedEventArgs e) { SetGpuPreference(0); }
    void GpuAppPrefHighPerf_Click(object sender, RoutedEventArgs e) { SetGpuPreference(1); }
    void RefreshGpuApps_Click(object sender, RoutedEventArgs e) { RefreshGpuAppList(); }

    void RestartGpu_Click(object sender, RoutedEventArgs e) {
      if (DialogHelper.Confirm(Strings.GpuRestartConfirmMsg, Strings.GpuRestartConfirmTitle)) {
        GpuAppManager.RestartGpu();
        DialogHelper.Info(Strings.GpuRestartSuccess, Strings.Hint);
      }
    }

    void GpuAppList_PreviewMouseWheel(object sender, MouseWheelEventArgs e) {
      var scroller = FindVisualChild<ScrollViewer>(GpuAppList);
      if (scroller != null) {
        scroller.ScrollToVerticalOffset(scroller.VerticalOffset - e.Delta);
        e.Handled = true;
      }
    }

    void GpuAppList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e) {
      var item = GpuAppList.ContainerFromElement((DependencyObject)e.OriginalSource) as ListBoxItem;
      if (item != null) {
        item.IsSelected = true;
        item.Focus();
      }
    }

    static T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject {
      int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
      for (int i = 0; i < count; i++) {
        var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
        if (child is T result) return result;
        var deeper = FindVisualChild<T>(child);
        if (deeper != null) return deeper;
      }
      return null;
    }

    void HpDriverSearch_Click(object sender, RoutedEventArgs e) {
      try { Process.Start(new ProcessStartInfo("https://support.hp.com/cn-zh/product/detect?source=swd") { UseShellExecute = true })?.Dispose(); } catch { }
    }
  }
}
