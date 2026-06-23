// FanService.cs - 风扇曲线管理
// 加载/保存 OSH 兼容双曲线文件，温度-转速插值，按预设持久化曲线
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static OmenSuperHub.OmenHardware;

namespace OmenSuperHub.Services {
  internal static class FanService {
    static readonly string FanCurvesDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "FanCurves");
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
      if (CPUTempFanMap.Count == 0 || GPUTempFanMap.Count == 0) return 0;

      // Sensor fitting: in auto mode, CPU-only monitor with no real data → use ambient sensor
      if (HardwareService.MonitorCPU && !HardwareService.MonitorGPU
          && HardwareService.CPUPower < 0.01f && HardwareService.IsAmbientSensorSupported) {
        float fitted = OmenHardware.GetFittingTemperature();
        int speed = GetFanSpeedForSpecificTemperature(fitted, CPUTempFanMap, fanIndex);
        return speed;
      }

      if (fanIndex == 0) {
        // Fan 0 (CPU fan): follow CPU curve based on CPU temperature
        return GetFanSpeedForSpecificTemperature(HardwareService.CPUTemp, CPUTempFanMap, fanIndex);
      } else {
        // Fan 1 (GPU fan): follow GPU curve based on GPU temp if monitoring is on
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

      int lowerSpeed = tempFanMap[lowerBound][fanIndex];
      int upperSpeed = tempFanMap[upperBound][fanIndex];
      float interpolatedSpeed = lowerSpeed + (upperSpeed - lowerSpeed) * (temperature - lowerBound) / (upperBound - lowerBound);
      return (int)interpolatedSpeed;
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
  }
}
