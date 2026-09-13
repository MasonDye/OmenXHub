// HWiNFOService.cs - HWiNFO64 传感器集成
// 将风扇/温度/功耗自定义传感器值写入注册表，供 HWiNFO64 读取显示
using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace OmenSuperHub.Services {
  public static class HWiNFOService {
    private const string CUSTOM_SENSOR_PATH = @"Software\HWiNFO64\Sensors\Custom";
    private const string CUSTOM_SENSOR_GROUP_NAME = "OmenXHub";
    private const string SENSOR_TYPE_FAN = "Fan";
    private const string SENSOR_TYPE_TEMP = "Temp";
    private const string SENSOR_TYPE_POWER = "Power";
    private const string CPU_FAN_SENSOR_NAME = "CPU Fan";
    private const string GPU_FAN_SENSOR_NAME = "GPU Fan";
    private const string CPU_TEMP_SENSOR_NAME = "CPU Temperature";
    private const string GPU_TEMP_SENSOR_NAME = "GPU Temperature";
    private const string CPU_POWER_SENSOR_NAME = "CPU Power";
    private const string GPU_POWER_SENSOR_NAME = "GPU Power";

    private static readonly TimeSpan _refreshInterval = TimeSpan.FromSeconds(1);
    private static CancellationTokenSource _cts;
    private static Task _refreshTask;

    public static void StartStopIfNeeded() {
      Stop();

      if (!ConfigService.HWiNFOEnabled)
        return;

      _cts = new CancellationTokenSource();
      _refreshTask = RefreshLoopAsync(_cts.Token);
    }

    public static void Stop() {
      if (_cts != null) {
        _cts.Cancel();
        try { _refreshTask?.Wait(2000); } catch { }
        _cts.Dispose();
        _cts = null;
      }
      ClearValues();
    }

    private static async Task RefreshLoopAsync(CancellationToken token) {
      try {
        SetSensorValues();
        while (!token.IsCancellationRequested) {
          await Task.Delay(_refreshInterval, token).ConfigureAwait(false);
          // ponytail: catch 在循环内 — 某轮注册表写入抛异常(如清理工具删键)只损失该轮;
          // 原实现包住整个 while,一次异常后写入循环静默死亡,HWiNFO 显示冻结到下次开关切换。
          try { SetSensorValues(); }
          catch (Exception ex) { Logger.Warn($"HWiNFOService: {ex.Message}"); }
        }
      } catch (OperationCanceledException) { }
    }

    private static void SetSensorValues() {
      // 风扇档位在首次 EC 读取前为 -1 哨兵(HardwareService.cs:52),外发前钳 0,避免写 "-100"。
      int cpuFan = HardwareService.FanSpeedNow.Count > 0 ? Math.Max(0, HardwareService.FanSpeedNow[0]) * 100 : 0;
      int gpuFan = HardwareService.FanSpeedNow.Count > 1 ? Math.Max(0, HardwareService.FanSpeedNow[1]) * 100 : 0;
      float cpuTemp = HardwareService.CPUTemp;
      float gpuTemp = HardwareService.TryGetFreshGpuTemp(out float freshGpuTemp) ? freshGpuTemp : 0;
      float cpuPower = HardwareService.CPUPower;
      float gpuPower = HardwareService.GpuPowerFresh ? HardwareService.GPUPower : 0;

      var nfi = new NumberFormatInfo { NumberDecimalSeparator = "." };

      SetValue(SENSOR_TYPE_FAN, 0, CPU_FAN_SENSOR_NAME, cpuFan.ToString());
      SetValue(SENSOR_TYPE_FAN, 1, GPU_FAN_SENSOR_NAME, gpuFan.ToString());
      SetValue(SENSOR_TYPE_TEMP, 0, CPU_TEMP_SENSOR_NAME, cpuTemp.ToString("F1", nfi));
      SetValue(SENSOR_TYPE_TEMP, 1, GPU_TEMP_SENSOR_NAME, gpuTemp.ToString("F1", nfi));
      SetValue(SENSOR_TYPE_POWER, 0, CPU_POWER_SENSOR_NAME, cpuPower.ToString("F1", nfi));
      SetValue(SENSOR_TYPE_POWER, 1, GPU_POWER_SENSOR_NAME, gpuPower.ToString("F1", nfi));
    }

    private static void SetValue(string type, int index, string name, string value) {
      string path = $@"{CUSTOM_SENSOR_PATH}\{CUSTOM_SENSOR_GROUP_NAME}\{type}{index}";
      using (var key = Registry.CurrentUser.CreateSubKey(path)) {
        if (key == null) return;
        key.SetValue("Value", value, RegistryValueKind.String);
        key.SetValue("Name", name, RegistryValueKind.String);
      }
    }

    private static void ClearValues() {
      try {
        using (var key = Registry.CurrentUser.OpenSubKey(CUSTOM_SENSOR_PATH, true)) {
          key?.DeleteSubKeyTree(CUSTOM_SENSOR_GROUP_NAME, false);
        }
      } catch { }
    }
  }
}
