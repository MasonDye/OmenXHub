// FanService.cs - 风扇曲线管理
// 加载/保存 OSH 兼容双曲线文件，温度-转速插值，按预设持久化曲线
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using static OmenSuperHub.OmenHardware;

namespace OmenSuperHub.Services {
  internal static class FanService {
    static readonly string FanCurvesDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "FanCurves");
    static readonly object _fanLock = new object();

    // ═══════════════════════════════════════════════════════
    // Smart Fan State (EMA smoothing, step-down protection)
    // ═══════════════════════════════════════════════════════
    static float _smartEmaCpuTemp = -1f;
    static float _smartEmaGpuTemp = -1f;
    static int _smartLastAppliedRpmCpu;
    static int _smartLastAppliedRpmGpu;
    static uint _smartLastTick;
    static float _smartAlpha = 0.3f;

    public static void InitSmartFanState(float alpha) {
      _smartAlpha = alpha;
      _smartEmaCpuTemp = -1f;
      _smartEmaGpuTemp = -1f;
      _smartLastAppliedRpmCpu = 0;
      _smartLastAppliedRpmGpu = 0;
      _smartLastTick = (uint)Environment.TickCount;
    }

    public static int GetSmartFanSpeed(int fanIndex) {
      lock (_fanLock) {
        float rawTemp = (fanIndex == 0) ? HardwareService.CPUTemp : HardwareService.GPUTemp;
        if (rawTemp <= 0) rawTemp = OmenHardware.GetFittingTemperature();
        if (rawTemp <= 0) return _smartLastAppliedRpmCpu > 0 ? _smartLastAppliedRpmCpu : 2000;

        if (_smartEmaCpuTemp < 0) {
          _smartEmaCpuTemp = HardwareService.CPUTemp;
          _smartEmaGpuTemp = HardwareService.GPUTemp;
          _smartLastTick = (uint)Environment.TickCount;
        }
        float prevEma = (fanIndex == 0) ? _smartEmaCpuTemp : _smartEmaGpuTemp;
        if (fanIndex == 0)
          _smartEmaCpuTemp = _smartAlpha * rawTemp + (1f - _smartAlpha) * _smartEmaCpuTemp;
        else
          _smartEmaGpuTemp = _smartAlpha * rawTemp + (1f - _smartAlpha) * _smartEmaGpuTemp;
        float emaTemp = (fanIndex == 0) ? _smartEmaCpuTemp : _smartEmaGpuTemp;

        float hysteresis = ConfigService.SmartFanHysteresis;
        int lastApplied = (fanIndex == 0) ? _smartLastAppliedRpmCpu : _smartLastAppliedRpmGpu;
        if (Math.Abs(emaTemp - prevEma) < hysteresis && lastApplied > 0) {
          return lastApplied;
        }

        var map = (fanIndex == 0) ? CPUTempFanMap : GPUTempFanMap;
        if (map.Count == 0) return lastApplied > 0 ? lastApplied : 2000;
        int rawSpeed = GetFanSpeedForSpecificTemperature(emaTemp, map, fanIndex);

        uint now = (uint)Environment.TickCount;
        uint dt = now - _smartLastTick; // unsigned subtraction handles wraparound
        _smartLastTick = now;
        if (dt > 0 && rawSpeed < lastApplied) {
          int maxDrop = (int)(ConfigService.SmartFanStepDownRate * dt / 1000f);
          rawSpeed = Math.Max(lastApplied - maxDrop, rawSpeed);
        }

        if (fanIndex == 0) _smartLastAppliedRpmCpu = rawSpeed;
        else _smartLastAppliedRpmGpu = rawSpeed;
        return rawSpeed;
      }
    }
    // ═══════════════════════════════════════════════════════
    // Temperature-Fan Speed Mappings
    // ═══════════════════════════════════════════════════════
    public static Dictionary<float, List<int>> CPUTempFanMap = new Dictionary<float, List<int>>();
    public static Dictionary<float, List<int>> GPUTempFanMap = new Dictionary<float, List<int>>();

    // ═══════════════════════════════════════════════════════
    // Load Fan Configuration from file (cool.txt / silent.txt)
    // OSH key=value format: Fan_Table_CPU_Temperature_List=...
    // ═══════════════════════════════════════════════════════
    public static void LoadFanConfig(string filePath) {
      string absoluteFilePath = Path.Combine(FanCurvesDir, filePath);
      var parsed = TryParseOshDualCurve(absoluteFilePath);
      if (parsed == null) {
        bool isSilent = filePath.IndexOf("silent", StringComparison.OrdinalIgnoreCase) >= 0;
        parsed = GenerateDefaultDualCurve(isSilent);
        SaveDualCurve(absoluteFilePath, parsed.Value.cpu, parsed.Value.gpu);
      }
      lock (CPUTempFanMap) {
        CPUTempFanMap.Clear();
        GPUTempFanMap.Clear();
        foreach (var (t, r) in parsed.Value.cpu) CPUTempFanMap[t] = new List<int> { r, r };
        foreach (var (t, r) in parsed.Value.gpu) GPUTempFanMap[t] = new List<int> { r, r };
      }
    }

    static ((float temp, int rpm)[] cpu, (float temp, int rpm)[] gpu)? TryParseOshDualCurve(string path) {
      if (!File.Exists(path)) return null;
      var lines = File.ReadAllLines(path);
      if (lines.Length == 0) return null;
      var dict = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
      foreach (var rawLine in lines) {
        if (string.IsNullOrWhiteSpace(rawLine)) continue;
        int eq = rawLine.IndexOf('=');
        if (eq < 0) continue;
        string key = rawLine.Substring(0, eq).Trim();
        string val = rawLine.Substring(eq + 1).Trim();
        dict[key] = val.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => int.TryParse(s.Trim(), out int n) ? n : -1)
            .Where(n => n >= 0).ToList();
      }
      List<int> cpuT, cpuS, gpuT, gpuS;
      if (!dict.TryGetValue("Fan_Table_CPU_Temperature_List", out cpuT) ||
          !dict.TryGetValue("Fan_Table_CPU_Fan_Speed_List", out cpuS) ||
          !dict.TryGetValue("Fan_Table_GPU_Temperature_List", out gpuT) ||
          !dict.TryGetValue("Fan_Table_GPU_Fan_Speed_List", out gpuS))
        return null;
      if (cpuT.Count != cpuS.Count || cpuT.Count < 2 ||
          gpuT.Count != gpuS.Count || gpuT.Count < 2)
        return null;
      var cpu = cpuT.Select((t, i) => ((float)t, cpuS[i])).ToArray();
      var gpu = gpuT.Select((t, i) => ((float)t, gpuS[i])).ToArray();
      if (!ValidateCurve(cpu.ToList()) || !ValidateCurve(gpu.ToList())) return null;
      return (cpu, gpu);
    }

    static ((float temp, int rpm)[] cpu, (float temp, int rpm)[] gpu) GenerateDefaultDualCurve(bool isSilent) {
      float scale = isSilent ? 0.8f : 1f;
      int[] cpuT = { 40, 50, 60, 68, 78, 85, 95 };
      int[] speeds = { 1200, 2000, 3000, 3600, 4600, 5000, 5200 };
      int[] gpuT = { 35, 45, 55, 63, 73, 82, 92 };
      var cpu = cpuT.Select((t, i) => ((float)t, (int)(speeds[i] * scale))).ToArray();
      var gpu = gpuT.Select((t, i) => ((float)t, (int)(speeds[i] * scale))).ToArray();
      return (cpu, gpu);
    }

    /// <summary>Generate aggressive performance dual curve</summary>
    static ((float temp, int rpm)[] cpu, (float temp, int rpm)[] gpu) GenerateDefaultDualCurvePerf() {
      int[] cpuT = { 35, 45, 55, 63, 73, 82, 92 };
      int[] speeds = { 1600, 2500, 3400, 4000, 4800, 5600, 6000 };
      int[] gpuT = { 30, 40, 50, 58, 68, 77, 87 };
      var cpu = cpuT.Select((t, i) => ((float)t, speeds[i])).ToArray();
      var gpu = gpuT.Select((t, i) => ((float)t, speeds[i])).ToArray();
      return (cpu, gpu);
    }

    static void SaveDualCurve(string path, (float temp, int rpm)[] cpu, (float temp, int rpm)[] gpu) {
      Directory.CreateDirectory(Path.GetDirectoryName(path));
      var lines = new List<string> {
        $"Fan_Table_CPU_Temperature_List={string.Join(",", cpu.Select(p => p.temp.ToString("F0")))}",
        $"Fan_Table_CPU_Fan_Speed_List={string.Join(",", cpu.Select(p => p.rpm))}",
        $"Fan_Table_GPU_Temperature_List={string.Join(",", gpu.Select(p => p.temp.ToString("F0")))}",
        $"Fan_Table_GPU_Fan_Speed_List={string.Join(",", gpu.Select(p => p.rpm))}",
      };
      File.WriteAllLines(path, lines);
    }

    // ═══════════════════════════════════════════════════════
    // Fan Speed Calculation with Interpolation
    // ═══════════════════════════════════════════════════════
    public static int GetFanSpeedForTemperature(int fanIndex) {
      lock (_fanLock) {
        if (CPUTempFanMap.Count == 0 || GPUTempFanMap.Count == 0) return 0;

        if (HardwareService.MonitorCPU && !HardwareService.MonitorGPU
            && HardwareService.CPUPower < 0.01f && HardwareService.IsAmbientSensorSupported) {
          float fitted = OmenHardware.GetFittingTemperature();
          return GetFanSpeedForSpecificTemperature(fitted, CPUTempFanMap, fanIndex);
        }

        if (fanIndex == 0)
          return GetFanSpeedForSpecificTemperature(HardwareService.CPUTemp, CPUTempFanMap, fanIndex);
        if (HardwareService.MonitorGPU)
          return GetFanSpeedForSpecificTemperature(HardwareService.GPUTemp, GPUTempFanMap, fanIndex);
        return GetFanSpeedForSpecificTemperature(HardwareService.CPUTemp, CPUTempFanMap, fanIndex);
      }
    }

    public static int GetFanSpeedForSpecificTemperature(float temperature, Dictionary<float, List<int>> tempFanMap, int fanIndex) {
      float lowerBound = float.MinValue, upperBound = float.MaxValue;
      foreach (float t in tempFanMap.Keys) {
        if (t <= temperature && t > lowerBound) lowerBound = t;
        if (t > temperature && t < upperBound) upperBound = t;
      }
      if (lowerBound == float.MinValue) lowerBound = upperBound;
      if (upperBound == float.MaxValue) upperBound = lowerBound;

      if (lowerBound == upperBound)
        return tempFanMap[lowerBound][fanIndex];

      float lowerSpeed = tempFanMap[lowerBound][fanIndex];
      float upperSpeed = tempFanMap[upperBound][fanIndex];
      return (int)(lowerSpeed + (upperSpeed - lowerSpeed) * (temperature - lowerBound) / (upperBound - lowerBound));
    }

    // ═══════════════════════════════════════════════════════
    // OSH-compatible curve helpers (key=value format)
    // ═══════════════════════════════════════════════════════
    static List<(float temp, int rpm)> ParseOshCpuCurve(string[] lines) {
      var dict = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
      foreach (var rawLine in lines) {
        if (string.IsNullOrWhiteSpace(rawLine)) continue;
        int eq = rawLine.IndexOf('=');
        if (eq < 0) continue;
        string key = rawLine.Substring(0, eq).Trim();
        string val = rawLine.Substring(eq + 1).Trim();
        dict[key] = val.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => int.TryParse(s.Trim(), out int n) ? n : -1)
            .Where(n => n >= 0).ToList();
      }
      List<int> cpuTemps, cpuSpeeds;
      if (!dict.TryGetValue("Fan_Table_CPU_Temperature_List", out cpuTemps) ||
          !dict.TryGetValue("Fan_Table_CPU_Fan_Speed_List", out cpuSpeeds))
        return null;
      if (cpuTemps.Count != cpuSpeeds.Count || cpuTemps.Count < 2) return null;
      var result = new List<(float, int)>();
      for (int i = 0; i < cpuTemps.Count; i++)
        result.Add((cpuTemps[i], cpuSpeeds[i]));
      return ValidateCurve(result) ? result : null;
    }

    static List<(float temp, int rpm)> ParseOshGpuCurve(string[] lines) {
      var dict = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
      foreach (var rawLine in lines) {
        if (string.IsNullOrWhiteSpace(rawLine)) continue;
        int eq = rawLine.IndexOf('=');
        if (eq < 0) continue;
        string key = rawLine.Substring(0, eq).Trim();
        string val = rawLine.Substring(eq + 1).Trim();
        dict[key] = val.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => int.TryParse(s.Trim(), out int n) ? n : -1)
            .Where(n => n >= 0).ToList();
      }
      List<int> gpuTemps, gpuSpeeds;
      if (!dict.TryGetValue("Fan_Table_GPU_Temperature_List", out gpuTemps) ||
          !dict.TryGetValue("Fan_Table_GPU_Fan_Speed_List", out gpuSpeeds))
        return null;
      if (gpuTemps.Count != gpuSpeeds.Count || gpuTemps.Count < 2) return null;
      var result = new List<(float, int)>();
      for (int i = 0; i < gpuTemps.Count; i++)
        result.Add((gpuTemps[i], gpuSpeeds[i]));
      return ValidateCurve(result) ? result : null;
    }

    static bool ValidateCurve(List<(float temp, int rpm)> points) {
      if (points == null || points.Count < 2) return false;
      if (points.Any(p => p.rpm < 0)) return false;
      if (points.GroupBy(p => p.temp).Any(g => g.Count() > 1)) return false;
      return true;
    }

    static List<(float temp, int rpm)> LoadCurveFromFile(string filePath) {
      var result = new List<(float, int)>();
      if (!File.Exists(filePath)) return result;
      var lines = File.ReadAllLines(filePath);
      if (lines.Length == 0) return result;
      var parsed = ParseOshCpuCurve(lines) ?? ParseOshGpuCurve(lines);
      return parsed ?? result;
    }

    static void SaveCurveToFile(string filePath, string keyTemp, string keySpeed, List<(float temp, int rpm)> points) {
      Directory.CreateDirectory(Path.GetDirectoryName(filePath));
      var sorted = points.OrderBy(p => p.temp).ToList();
      var lines = new List<string> {
        $"{keyTemp}={string.Join(",", sorted.Select(p => p.temp.ToString("F0")))}",
        $"{keySpeed}={string.Join(",", sorted.Select(p => p.rpm))}"
      };
      File.WriteAllLines(filePath, lines);
    }

    // ═══════════════════════════════════════════════════════
    // Custom Fan Curve Persistence (OSH-compatible)
    // ═══════════════════════════════════════════════════════
    public static List<(float temp, int rpm)> LoadCustomCurve() {
      string filePath = Path.Combine(FanCurvesDir, "custom.txt");
      return LoadCurveFromFile(filePath);
    }

    public static void SaveCustomCurve(List<(float temp, int rpm)> points) {
      if (points == null || points.Count == 0) return;
      string filePath = Path.Combine(FanCurvesDir, "custom.txt");
      SaveCurveToFile(filePath, "Fan_Table_CPU_Temperature_List", "Fan_Table_CPU_Fan_Speed_List", points);
    }

    public static void ApplyCustomCurve(List<(float temp, int rpm)> points) {
      var sorted = points.OrderBy(p => p.temp).ToList();
      lock (CPUTempFanMap) {
        CPUTempFanMap.Clear();
        GPUTempFanMap.Clear();
        foreach (var pt in sorted) {
          CPUTempFanMap[pt.temp] = new List<int> { pt.rpm, pt.rpm };
          float gpuTemp = pt.temp - 10f;
          if (gpuTemp < 0) gpuTemp = 0;
          GPUTempFanMap[gpuTemp] = new List<int> { pt.rpm, pt.rpm };
        }
      }
    }

    // ═══════════════════════════════════════════════════════
    // GPU Custom Fan Curve Persistence (OSH-compatible)
    // ═══════════════════════════════════════════════════════
    public static List<(float temp, int rpm)> LoadCustomCurveGPU() {
      string filePath = Path.Combine(FanCurvesDir, "custom_gpu.txt");
      return LoadCurveFromFile(filePath);
    }

    public static void SaveCustomCurveGPU(List<(float temp, int rpm)> points) {
      if (points == null || points.Count == 0) return;
      string filePath = Path.Combine(FanCurvesDir, "custom_gpu.txt");
      SaveCurveToFile(filePath, "Fan_Table_GPU_Temperature_List", "Fan_Table_GPU_Fan_Speed_List", points);
    }

    public static void ApplyCustomCurveGPU(List<(float temp, int rpm)> points) {
      var sorted = points.OrderBy(p => p.temp).ToList();
      lock (GPUTempFanMap) {
        GPUTempFanMap.Clear();
        foreach (var pt in sorted) {
          GPUTempFanMap[pt.temp] = new List<int> { pt.rpm, pt.rpm };
        }
      }
    }

    // ═══════════════════════════════════════════════════════
    // Per-Preset Custom Curve Persistence (OSH-compatible)
    // ═══════════════════════════════════════════════════════
    public static string SerializeCurve(List<(float temp, int rpm)> points) {
      if (points == null || points.Count == 0) return "";
      var sorted = points.OrderBy(p => p.temp).ToList();
      return string.Join(";", sorted.Select(p => $"{p.temp:F0},{p.rpm}"));
    }

    public static List<(float temp, int rpm)> DeserializeCurve(string data) {
      var result = new List<(float, int)>();
      if (string.IsNullOrEmpty(data)) return result;
      foreach (var part in data.Split(';')) {
        var vals = part.Split(',');
        if (vals.Length == 2 && float.TryParse(vals[0], out float t) && int.TryParse(vals[1], out int r))
          result.Add((t, r));
      }
      return result;
    }

    public static string PresetCurvePath(string presetKey, bool gpu) =>
        Path.Combine(FanCurvesDir, $"custom_{presetKey}{(gpu ? "_gpu" : "")}.txt");

    public static void SavePresetCurve(string presetKey, List<(float temp, int rpm)> points, bool gpu) {
      if (points == null || points.Count == 0) return;
      string path = PresetCurvePath(presetKey, gpu);
      if (gpu)
        SaveCurveToFile(path, "Fan_Table_GPU_Temperature_List", "Fan_Table_GPU_Fan_Speed_List", points);
      else
        SaveCurveToFile(path, "Fan_Table_CPU_Temperature_List", "Fan_Table_CPU_Fan_Speed_List", points);
    }

    public static List<(float temp, int rpm)> LoadPresetCurve(string presetKey, bool gpu) {
      string path = PresetCurvePath(presetKey, gpu);
      return LoadCurveFromFile(path);
    }

    // ponytail: fan-curve preset namespace lives on the real PresetManager keys
    // (Extreme/GpuPriority/LightUse/<custom>). The legacy quiet/performance/balanced
    // bucket names map to the same generator columns. GpuPriority + unknown custom
    // keys fall through to the balanced column so we never silently create orphan
    // curve files for a non-existent preset like "balanced".
    public static ((float temp, int rpm)[] cpu, (float temp, int rpm)[] gpu) GetDefaultPresetCurve(string presetKey) {
      switch (presetKey) {
        case "quiet":
        case "LightUse":
          return GenerateDefaultDualCurve(true);
        case "performance":
        case "Extreme":
          return GenerateDefaultDualCurvePerf();
        // balanced / GpuPriority / 自定义预设 keys → balanced 档
        default:
          return GenerateDefaultDualCurve(false);
      }
    }

    /// <summary>
    /// Load preset curve from saved file (or generate default), populate CPUTempFanMap / GPUTempFanMap,
    /// and return the (cpu, gpu) curve lists for UI display.
    /// </summary>
    public static (List<(float temp, int rpm)> cpu, List<(float temp, int rpm)> gpu) ApplyPresetCurve(string presetKey) {
      var defaultCurve = GetDefaultPresetCurve(presetKey);
      var cpuSaved = LoadPresetCurve(presetKey, false);
      var gpuSaved = LoadPresetCurve(presetKey, true);
      var cpuPoints = (cpuSaved != null && cpuSaved.Count >= 2) ? cpuSaved : defaultCurve.cpu.Select(p => (p.temp, p.rpm)).ToList();
      var gpuPoints = (gpuSaved != null && gpuSaved.Count >= 2) ? gpuSaved : defaultCurve.gpu.Select(p => (p.temp, p.rpm)).ToList();

      lock (CPUTempFanMap) {
        CPUTempFanMap.Clear();
        GPUTempFanMap.Clear();
        foreach (var pt in cpuPoints) CPUTempFanMap[pt.temp] = new List<int> { pt.rpm, pt.rpm };
        foreach (var pt in gpuPoints) GPUTempFanMap[pt.temp] = new List<int> { pt.rpm, pt.rpm };
      }

      return (cpuPoints, gpuPoints);
    }

    // ═══════════════════════════════════════════════════════
    // Import / Export / Share — JSON file & share code
    // ═══════════════════════════════════════════════════════

    [DataContract]
    public class FanCurveExportData {
      [DataMember(Order = 1)] public int Version { get; set; } = 1;
      [DataMember(Order = 2)] public string Name { get; set; } = "";
      [DataMember(Order = 3)] public string Description { get; set; } = "";
      [DataMember(Order = 4)] public string Device { get; set; } = "";
      [DataMember(Order = 5)] public string Date { get; set; } = "";
      [DataMember(Order = 6)] public List<FanCurvePoint> Points { get; set; } = new List<FanCurvePoint>();
    }

    [DataContract]
    public class FanCurvePoint {
      [DataMember(Order = 1)] public float Temp { get; set; }
      [DataMember(Order = 2)] public int Rpm { get; set; }
    }

    /// <summary>导出曲线为 JSON 字符串</summary>
    public static string ExportCurveToJson(List<(float temp, int rpm)> points, string curveName, string description = "", string device = "") {
      try {
        if (points == null || points.Count < 2) return null;
        var sorted = points.OrderBy(p => p.temp).ToList();
        var data = new FanCurveExportData {
          Name = string.IsNullOrEmpty(curveName) ? "Custom Fan Curve" : curveName,
          Description = description ?? "",
          Device = string.IsNullOrEmpty(device) ? "OMEN X Hub" : device,
          Date = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
          Points = sorted.Select(p => new FanCurvePoint { Temp = p.temp, Rpm = p.rpm }).ToList()
        };
        using (var ms = new MemoryStream()) {
          var serializer = new DataContractJsonSerializer(typeof(FanCurveExportData));
          serializer.WriteObject(ms, data);
          ms.Position = 0;
          using (var reader = new StreamReader(ms, Encoding.UTF8))
            return reader.ReadToEnd();
        }
      } catch { return null; }
    }

    /// <summary>从 JSON 字符串导入曲线，返回 (points, name, description)</summary>
    public static (List<(float temp, int rpm)> points, string name, string desc)? ImportCurveFromJson(string json) {
      try {
        if (string.IsNullOrWhiteSpace(json)) return null;
        using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json))) {
          var serializer = new DataContractJsonSerializer(typeof(FanCurveExportData));
          var data = (FanCurveExportData)serializer.ReadObject(ms);
          if (data == null || data.Points == null || data.Points.Count < 2) return null;
          var points = data.Points.Select(p => ((float)p.Temp, p.Rpm)).ToList();
          if (!ValidateCurve(points)) return null;
          // remap to int temp
          for (int i = 0; i < points.Count; i++)
            points[i] = ((float)Math.Round(points[i].Item1), points[i].Item2);
          return (points, data.Name ?? "", data.Description ?? "");
        }
      } catch { return null; }
    }

    /// <summary>生成分享码（压缩：名称|日期|序列化曲线 → Base64）</summary>
    public static string GenerateShareCode(List<(float temp, int rpm)> points, string curveName = "") {
      try {
        if (points == null || points.Count < 2) return null;
        string serialized = SerializeCurve(points);
        string device = "OMEN";
        string date = DateTime.Now.ToString("yyyyMMdd");
        string name = string.IsNullOrEmpty(curveName) ? "Curve" : curveName.Replace('|', '-');
        string payload = $"{name}|{device}|{date}|{serialized}";
        byte[] bytes = Encoding.UTF8.GetBytes(payload);
        return "OXFC:" + Convert.ToBase64String(bytes);
      } catch { return null; }
    }

    /// <summary>解析分享码，返回 (points, name)</summary>
    public static (List<(float temp, int rpm)> points, string name)? ParseShareCode(string code) {
      try {
        if (string.IsNullOrWhiteSpace(code)) return null;
        // Strip "OXFC:" prefix if present
        if (code.StartsWith("OXFC:", StringComparison.OrdinalIgnoreCase))
          code = code.Substring(5);
        byte[] bytes = Convert.FromBase64String(code.Trim());
        string payload = Encoding.UTF8.GetString(bytes);
        var parts = payload.Split('|');
        if (parts.Length < 4) return null;
        string name = parts[0];
        string serialized = string.Join("|", parts.Skip(3)); // in case serialized contains '|'
        var points = DeserializeCurve(serialized);
        if (points == null || points.Count < 2) return null;
        return (points, name);
      } catch { return null; }
    }
  }
}
