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

    // ponytail: FanControl 字符串格式有 3 类: "smart"/"custom" → 智能曲线、""/"auto"/"silent"/"cool" → 自动表、
    // "<n> RPM" → 固定转速 / "<pct>%" → 固定百分比。之前 6 处重复 ".Replace(" RPM","")",
    // 分歧处理时容易漏边界。集中在这两个 helper 一处解析。
    // 升级路径: 字典表示法 (mode→value)，去掉字符串编码。
    /// <summary>解析 "1234 RPM" / "25%" 为 0..6000 的 RPM 整数。解析失败/越界返回 def。</summary>
    public static int ParseFanRpm(string fanControl, int def = 2500) {
      if (string.IsNullOrEmpty(fanControl)) return def;
      string s = fanControl.Trim();
      try {
        if (s.EndsWith(" RPM")) {
          if (int.TryParse(s.Substring(0, s.Length - 4).Trim(), out int rpm)) return ClampRpm(rpm);
        } else if (s.EndsWith("%")) {
          if (int.TryParse(s.TrimEnd('%'), out int pct)) return ClampRpm(pct * 100);
        }
      } catch { }
      return def;
    }
    /// <summary>判断 FanControl 是否为固定转速 (RPM 或 % 形式)。</summary>
    public static bool IsFixedRpm(string fanControl)
      => !string.IsNullOrEmpty(fanControl) && (fanControl.Contains(" RPM") || fanControl.EndsWith("%"));
    static int ClampRpm(int rpm) => rpm < 500 ? 500 : rpm > 6000 ? 6000 : rpm;

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
        // ponytail: 按文件名推断曲线档 — silent.txt → silent, balanced.txt → balanced,
        // 其它 (cool.txt/default) → default。cool.txt 历史上是 default 档的别名。
        string mode;
        if (filePath.IndexOf("silent", StringComparison.OrdinalIgnoreCase) >= 0) mode = "silent";
        else if (filePath.IndexOf("balanced", StringComparison.OrdinalIgnoreCase) >= 0) mode = "balanced";
        else mode = "default";
        parsed = GenerateDefaultDualCurve(mode);
        SaveDualCurve(absoluteFilePath, parsed.Value.cpu, parsed.Value.gpu);
      }
      // ponytail: 所有读路径（GetFanSpeedForTemperature/GetSmartFanSpeed）持有 _fanLock 读 map，
      // 写路径必须用同一锁，否则 Dictionary 并发读写会损坏内部状态。
      lock (_fanLock) {
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
      // ponytail: 兼容旧 bool 入口 — silent=true, 其它(0/true/false)=default。
      // 升级路径: 直接迁移所有调用点到 string 重载，删 bool 这层。
      return GenerateDefaultDualCurve(isSilent ? "silent" : "default");
    }

    // ponytail: 3 档全局曲线表，对齐 cool.txt/silent.txt/balanced.txt 文件方案。
    // 数据源: G-Helper app/AppConfig.cs GetDefaultCurve (16 byte = 8 温度 + 8 % )，
    // RPM = 6000 × % / 100 (用户要求最大转速设为 6000 按对应百分比设置)。
    //   silent   → G-Helper Silent CPU/GPU 8 点, 前 3 点 (G-Helper 0%→0/0/180) 抬到 700
    //               RPM floor 避开 Omen EC byte<10 (<500) 反弹风险 (OmenHardware.cs:205-208)。
    //   balanced → G-Helper Balanced CPU/GPU 分离 8 点, <58°C 回退到 700 floor。
    //   cool(default) → 参考 G-Helper 转速最高档 (Turbo CPU), CPU=GPU 共用。
    // 升级路径: 字典表示法 + PresetData 持久化曲线，去掉字符串 mode 三分支。
    static ((float temp, int rpm)[] cpu, (float temp, int rpm)[] gpu) GenerateDefaultDualCurve(string mode) {
      int[] cpuT, gpuT, cpuSpeeds, gpuSpeeds;
      if (mode == "silent") {
        // 用户定制 silent 曲线 9 点 (20~100°C), CPU/GPU 共用。前 5 个点 20~50°C 全部 600 RPM floor,
        // 60°C 起线性爬升, 100°C 峰值 3400 RPM (<6000 BIOS 上限的 57%)。给极安静轻度负载体验,
        // 高温端也保留散热余量。600 floor 严踩 Omen EC byte<10 (500 max) 安全区下限的边界, 安全。
        cpuT = gpuT = new[] { 20, 30, 40, 50, 60, 70, 80, 90, 100 };
        cpuSpeeds = gpuSpeeds = new[] { 600, 600, 600, 600, 1200, 2600, 2900, 3200, 3400 };
      } else if (mode == "balanced") {
        // 平衡档用户定制 8 点曲线 (30~100°C), CPU/GPU 共用。起点 600 RPM 极安静, 中段
        // 50~72°C 平缓爬升 (2000→2600→3000), 高温 80~100°C 明显加大散热 (3400→3700→4800)。
        // 100°C 峰值 4800 = 6000×80%, 留 20% 给 BIOS/EC 兜底, 符合 G-Helper "不冲到 100%" 哲学。
        cpuT = gpuT = new[] { 30, 50, 60, 64, 72, 80, 90, 100 };
        cpuSpeeds = gpuSpeeds = new[] { 600, 2000, 2400, 2600, 3000, 3400, 3700, 4800 };
      } else {
        // cool / default → 降温档用户定制 9 点曲线 (30~105°C), CPU=GPU 共用。
        // 30°C 起步 2400 RPM 保活 (高于 silent/balanced 起点以提供最小风量, 避免 EC 滞温),
        // 50~100°C 梯度爬升 (2400→5200), 105°C 兜底 6000 RPM (BIOS 上限, 硬件级保命点)。
        // 105°C 超出常见 CPU Tjmax (95~100), 是 CPU 传感器最后兜底档; 仅在散热严重失败时触发。
        cpuT = gpuT = new[] { 30, 50, 60, 70, 76, 80, 90, 100, 105 };
        cpuSpeeds = gpuSpeeds = new[] { 2400, 2800, 3000, 3400, 3800, 4400, 5000, 5200, 6000 };
      }
#if DEBUG
      // ponytail: 非平凡曲线生成必须留一个 runnable check。三条 assert 任意一条挂掉
      // 都意味着曲线被后续修改改坏 —— RPM floor 破坏会让 AMD EC 反弹 (OmenHardware.cs:205-208),
      // 单调破坏会让 GetFanSpeedForSpecificTemperature 插值在高温段往回跌。
      CheckCurveInvariants(mode, cpuT, cpuSpeeds);
      CheckCurveInvariants(mode, gpuT, gpuSpeeds);
      if (mode == "silent") {
        System.Diagnostics.Debug.Assert(cpuSpeeds[0] == 600 && cpuSpeeds[8] == 3400,
          "silent 曲线起点应为 600 RPM floor, 峰值 3400 RPM (用户定制 9 点曲线 20~100°C)");
      } else if (mode == "balanced") {
        System.Diagnostics.Debug.Assert(cpuSpeeds[0] == 600 && cpuSpeeds[7] == 4800,
          "balanced 曲线起点 600 RPM, 峰值 4800 RPM (用户定制 8 点 30~100°C, CPU=GPU 共用)");
      } else {
        System.Diagnostics.Debug.Assert(cpuSpeeds[0] == 2400 && cpuSpeeds[8] == 6000,
          "cool/default 曲线起点 2400 RPM, 105°C 兜底 6000 RPM (用户定制 9 点曲线)");
      }
#endif
      var cpu = cpuT.Select((t, i) => ((float)t, cpuSpeeds[i])).ToArray();
      var gpu = gpuT.Select((t, i) => ((float)t, gpuSpeeds[i])).ToArray();
      return (cpu, gpu);
    }

#if DEBUG
    static void CheckCurveInvariants(string mode, int[] temps, int[] speeds) {
      System.Diagnostics.Debug.Assert(speeds.All(r => r >= 500 && r <= 6000),
        $"{mode} 曲线 rpm 必须在 ClampRpm 区间 [500, 6000]");
      System.Diagnostics.Debug.Assert(speeds.Zip(speeds.Skip(1), (a, b) => a <= b).All(x => x),
        $"{mode} 曲线转速必须单调非递减，否则高温段插值会反向");
      System.Diagnostics.Debug.Assert(temps.Distinct().Count() == temps.Length,
        $"{mode} 曲线温度点必须唯一");
    }
#endif

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
    // ponytail: 旧 ParseOshCpuCurve/ParseOshGpuCurve 共享 ~90% 代码，仅 key 名不同。
    // 合并为参数化版本一处即可，CPU 走 temp+speed 主 key、GPU 走 GPU_Temperature/Speed。
    static List<(float temp, int rpm)> ParseOshCurve(string[] lines, string tempKey, string speedKey) {
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
      List<int> temps, speeds;
      if (!dict.TryGetValue(tempKey, out temps) || !dict.TryGetValue(speedKey, out speeds))
        return null;
      if (temps.Count != speeds.Count || temps.Count < 2) return null;
      var result = new List<(float, int)>();
      for (int i = 0; i < temps.Count; i++)
        result.Add((temps[i], speeds[i]));
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
      // ponytail: 先按 CPU keys 试，回退 GPU keys (既单一曲线文件兼容)
      var parsed = ParseOshCurve(lines, "Fan_Table_CPU_Temperature_List", "Fan_Table_CPU_Fan_Speed_List")
                ?? ParseOshCurve(lines, "Fan_Table_GPU_Temperature_List", "Fan_Table_GPU_Fan_Speed_List");
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
      lock (_fanLock) {
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
      lock (_fanLock) {
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

    // ponytail: 创建自定义预设时不预生成曲线文件的话，首次重启会回退到默认曲线，
    // 而默认曲线与用户预期不符。早期实现无条件落盘 GetDefaultPresetCurve(presetKey)
    // (balanced 档)，新建"Game"预设用户期望继承当前预设的曲线却拿到平衡曲线。
    // 改为优先克隆当前内存中的曲线（用户在 X 预设下建 Y 时看到的就是 X 的曲线，
    // 带进 Y 完全符合预期）；内存为空时才回退到 GetDefaultPresetCurve。
    // 升级路径: 将曲线存储在 PresetData JSON 内，省掉这套 per-file 补偿。
    public static void EnsurePresetCurveFile(string presetKey) {
      if (File.Exists(PresetCurvePath(presetKey, false)) &&
          File.Exists(PresetCurvePath(presetKey, true))) return;
      var (cpuPts, gpuPts) = DumpCurrentMaps();
      List<(float temp, int rpm)> cpu, gpu;
      if (cpuPts.Count >= 2) {
        cpu = cpuPts;
        gpu = gpuPts.Count >= 2 ? gpuPts : cpuPts;
      } else {
        var curve = GetDefaultPresetCurve(presetKey);
        cpu = curve.cpu.Select(p => (p.temp, p.rpm)).ToList();
        gpu = curve.gpu.Select(p => (p.temp, p.rpm)).ToList();
      }
      SavePresetCurve(presetKey, cpu, false);
      SavePresetCurve(presetKey, gpu, true);
    }

    // ponytail: snapshot current CPUTempFanMap / GPUTempFanMap as sorted (temp,rpm) lists.
    // Shared by EnsurePresetCurveFile (clone current curves into new preset).
    static (List<(float temp, int rpm)> cpu, List<(float temp, int rpm)> gpu) DumpCurrentMaps() {
      lock (_fanLock) {
        var cpu = CPUTempFanMap.OrderBy(p => p.Key)
            .Select(p => (p.Key, p.Value.Count > 0 ? p.Value[0] : 0)).ToList();
        var gpu = GPUTempFanMap.OrderBy(p => p.Key)
            .Select(p => (p.Key, p.Value.Count > 0 ? p.Value[0] : 0)).ToList();
        return (cpu, gpu);
      }
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
          // ponytail: Extreme 预设的自动档 (FanTable=cool) 走 cool.txt = GenerateDefaultDualCurve("default")
          // 用户定制 9 点曲线; 切到自定义曲线模式时 fallback 也同源, 不再分裂为 GenerateDefaultDualCurvePerf。
          // 否则用户切"极致性能→自定义"看到的曲线与"极致性能→降温自动档"不一致 (老 bug)。
          return GenerateDefaultDualCurve("default");
        case "GpuPriority":
          // ponytail: GpuPriority 预设默认走 G-Helper Balanced (AppConfig.cs:331-333)
          // — 与 Extreme(性能)、LightUse(静音) 形成三档梯度。GpuPriority 语义是
          // "GPU 占主导，CPU 让位"，G-Helper Balanced CPU/GPU 分离曲线刚好满足：
          // CPU 中段转速、GPU 显著高于 CPU 的散热 (4440 峰值 vs CPU 4140)。
          return GenerateDefaultDualCurve("balanced");
        // 自定义预设 keys → default 档（旧"平衡 0.8 缩放"哲学现已替换为高仿 silent）
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

      lock (_fanLock) {
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
