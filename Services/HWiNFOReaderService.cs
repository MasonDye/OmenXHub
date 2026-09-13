// HWiNFOReaderService.cs - 从 HWiNFO64 VSB 注册表读取 CPU/GPU 传感器数据
// 替代 LibreHardwareMonitor 数据源，需手动在设置中启用
// 参考 FanControl.HWInfo 项目的注册表读取模式
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace OmenSuperHub.Services {
  internal static class HWiNFOReaderService {
    private const string MAIN_KEY = @"SOFTWARE\HWiNFO64\VSB";
    private const string SECOND_KEY = @"SOFTWARE\HWiNFO32\VSB";
    private const string SENSOR = "Sensor";
    private const string LABEL = "Label";
    private const string VALUE = "Value";
    private const string VALUE_RAW = "ValueRaw";

    private static readonly TimeSpan _refreshInterval = TimeSpan.FromSeconds(1);
    private static readonly CultureInfo _format = new CultureInfo("en-us");
    private static CancellationTokenSource _cts;
    private static Task _refreshTask;

    // 传感器索引缓存：key=our field name, value=HWiNFO registry index
    private static Dictionary<string, int> _sensorIndex = new Dictionary<string, int>();
    // 上一次读到的索引数，用于检测 HWiNFO 传感器增减
    private static int _lastValueCount;
    // 是否首次加载（触发一次完整重索引）
    private static bool _firstRun = true;

    // ═══════════════════════════════════════════════════════
    // Public status (for UI feedback)
    // ═══════════════════════════════════════════════════════
    /// <summary>HWiNFO 读取器是否正在运行（后台轮询中）。</summary>
    public static bool IsRunning => _cts != null && !_cts.IsCancellationRequested;

    /// <summary>HWiNFO 是否正在运行且暴露传感器。</summary>
    public static bool IsHWiNFOConnected => FindActiveKeyPath() != null;

    /// <summary>当前匹配到的传感器数量。0 表示 HWiNFO 运行但未找到支持的传感器。</summary>
    public static int MatchedSensorCount => _sensorIndex.Count;

    /// <summary>UI 状态文本。</summary>
    public static string StatusText {
      get {
        if (!ConfigService.HWiNFOReadEnabled) return "未启用";
        if (!IsHWiNFOConnected) return "HWiNFO64 未运行或未启用 Gadget 输出";
        if (_sensorIndex.Count == 0) return "已连接，但未找到 CPU/GPU 传感器（检查 HWiNFO Gadget 勾选）";
        return $"已连接，匹配 {_sensorIndex.Count} 个传感器";
      }
    }

    public static void StartStopIfNeeded() {
      Stop();
      if (!ConfigService.HWiNFOReadEnabled) return;

      // 检查 HWiNFO 是否在运行且 Gadget 输出已启用
      if (FindActiveKeyPath() == null) return;

      _cts = new CancellationTokenSource();
      _refreshTask = RefreshLoopAsync(_cts.Token);
    }

    public static void Stop() {
      if (_cts != null) {
        _cts.Cancel();
        // ponytail: 不同步 Wait(2000) —— 该路径从 OtherPage 切 Toggle 的 UI 线程调用,同步等最多 2s
        // 会冻结界面。RefreshLoopAsync 用 token,Task.Delay(token) 在 Cancel 后立即返回,循环自然退出,
        // 无需等待。Dispose 已 Cancel 的 CTS 安全。
        _cts.Dispose();
        _cts = null;
        _refreshTask = null;
      }
      _sensorIndex.Clear();
      _lastValueCount = 0;
      _firstRun = true;
      HardwareService.InvalidateGpuSamples();
    }

    /// <summary>返回当前可用的注册表路径，无 key 表示 HWiNFO 未运行/未启用 Gadget。</summary>
    private static string FindActiveKeyPath() {
      using (var k = Registry.CurrentUser.OpenSubKey(MAIN_KEY))
        if (k?.ValueCount > 0) return MAIN_KEY;
      using (var k = Registry.CurrentUser.OpenSubKey(SECOND_KEY))
        if (k?.ValueCount > 0) return SECOND_KEY;
      return null;
    }

    private static async Task RefreshLoopAsync(CancellationToken token) {
      // ponytail: catch 在循环内 — 读一轮失败只跳过本轮;原实现包住整个 while,
      // 一次异常后整个读取循环死亡(HWiNFOReadEnabled 时 CPU 温度冻结 → 风扇曲线吃旧值)。
      while (!token.IsCancellationRequested) {
        try { RefreshSensors(); }
        catch (Exception ex) { Logger.Warn($"HWiNFOReader: {ex.Message}"); }
        try { await Task.Delay(_refreshInterval, token).ConfigureAwait(false); }
        catch (OperationCanceledException) { break; }
      }
    }

    private static void RefreshSensors() {
      string regKey = FindActiveKeyPath();
      if (regKey == null) return;

      using (var key = Registry.CurrentUser.OpenSubKey(regKey)) {
        if (key == null) return;
        int valueCount = key.ValueCount;
        if (valueCount == 0) return;

        // 传感器数量变化或首次运行时重新索引
        if (_firstRun || valueCount != _lastValueCount) {
          _firstRun = false;
          _lastValueCount = valueCount;
          RebuildIndex(key);
        }

        // 没有匹配的传感器则跳过
        if (_sensorIndex.Count == 0) return;

        // 读取并映射到 HardwareService
        foreach (var kv in _sensorIndex) {
          string raw = key.GetValue(VALUE_RAW + kv.Value) as string;
          if (string.IsNullOrEmpty(raw)) continue;
          if (float.TryParse(raw, NumberStyles.Float, _format, out float val))
            ApplyField(kv.Key, val);
        }
      }
    }

    /// <summary>扫描注册表，按 Label（分类）和传感器名称建立字段名→索引的映射。</summary>
    private static void RebuildIndex(RegistryKey key) {
      var names = key.GetValueNames();
      var sensorNames = names.Where(n => n.StartsWith(SENSOR, StringComparison.InvariantCultureIgnoreCase));

      // 先按 Label 分组收集所有传感器，以便同一类目下有多个时做优选
      var cpuCandidates = new List<(int index, string name, string valStr)>();
      var gpuCandidates = new List<(int index, string name, string valStr)>();

      foreach (var sn in sensorNames) {
        if (!int.TryParse(sn.Replace(SENSOR, ""), out int i)) continue;

        // HWiNFO VSB: SensorN is the device/group, LabelN is the metric name.
        string sensorGroup = (key.GetValue(SENSOR + i) as string) ?? "";
        string metricName = (key.GetValue(LABEL + i) as string) ?? "";
        string valStr = key.GetValue(VALUE + i) as string ?? "";

        var cat = ClassifyLabel(sensorGroup);
        if (cat == "cpu") cpuCandidates.Add((i, metricName, valStr));
        else if (cat == "gpu") gpuCandidates.Add((i, metricName, valStr));
      }

      var idx = new Dictionary<string, int>();

      // CPU 传感器匹配（按重要度降序）
      MatchGroup(cpuCandidates, "CPU", idx);
      // GPU 传感器匹配
      MatchGroup(gpuCandidates, "GPU", idx);

      _sensorIndex = idx;
    }

    /// <summary>根据 Label 值判断归属 CPU/GPU/其他。</summary>
    private static string ClassifyLabel(string label) {
      var l = label.ToUpperInvariant();
      // Specific GPU markers must win over broad CPU words such as CORE/PACKAGE.
      if (l.Contains("GPU") || l == "D3D")
        return "gpu";
      // CPU 类目: "CPU", "Package", "Core #1", "DIE avg" 等
      if (l.Contains("CPU") || l.Contains("DIE") || l.Contains("CORE") || l.Contains("PACKAGE") || l.Contains("CLOCK"))
        return "cpu";
      return "other";
    }

    /// <summary>从一组候选传感器中挑唯一的核心温度和总功耗，避免误选热点/显存/供电轨。</summary>
    private static void MatchGroup(List<(int i, string name, string valStr)> candidates, string prefix, Dictionary<string, int> idx) {
      string p = prefix.ToLowerInvariant();
      int bestTempRank = int.MaxValue, bestPowerRank = int.MaxValue;

      foreach (var (i, name, valStr) in candidates) {
        string unit = valStr.Trim().Split(' ').Skip(1).FirstOrDefault() ?? "";
        string n = name.ToUpperInvariant();

        // ponytail: ValueRaw 没有单位元数据可供换算；非摄氏/非瓦特时宁可不匹配，也不把 °F/mW 当 °C/W。
        if (unit == "°C" || unit == "℃") {
          int rank = p == "gpu"
            ? (n == "GPU TEMPERATURE" ? 0 : n == "GPU CORE" ? 1
              : n.Contains("TEMPERATURE") && !n.Contains("HOT") && !n.Contains("MEMORY") ? 2 : int.MaxValue)
            : (n.Contains("CPU PACKAGE") || n == "PACKAGE" ? 0
              : n.Contains("TCTL/TDIE") || n.Contains("TDIE") ? 1 : int.MaxValue);
          if (rank < bestTempRank) { bestTempRank = rank; idx[p + "Temp"] = i; }
        } else if (unit == "W") {
          int rank = p == "gpu"
            ? (n.Contains("TOTAL GPU POWER") ? 0 : n == "GPU POWER" ? 1
              : n.Contains("GPU PACKAGE") ? 2 : n.Contains("BOARD POWER") ? 3 : int.MaxValue)
            : (n.Contains("CPU PACKAGE POWER") || n == "PACKAGE POWER" ? 0
              : n.Contains("CPU TOTAL POWER") ? 1 : int.MaxValue);
          if (rank < bestPowerRank) { bestPowerRank = rank; idx[p + "Power"] = i; }
        }
      }
    }

    /// <summary>将解析后的值写入 HardwareService 对应字段。</summary>
    private static void ApplyField(string field, float val) {
      switch (field) {
        case "cpuTemp":
          HardwareService.CPUTemp = val * HardwareService.RespondSpeed + HardwareService.CPUTemp * (1.0f - HardwareService.RespondSpeed);
          break;
        case "gpuTemp":
          HardwareService.UpdateGpuTempSample(val, HardwareService.GpuTemperatureSource.HWiNFO);
          break;
        case "cpuPower":
          HardwareService.CPUPower = val;
          break;
        case "gpuPower":
          HardwareService.UpdateGpuPowerSample(val);
          break;
      }
    }
  }
}