// GpuAppManager.cs - GPU 进程与超频管理
// 使用 nvidia-smi 和 NvAPIWrapper.Net 查询 GPU 应用、时钟偏移、功耗限制，支持超频调节
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using NvAPIWrapper;
using NvAPIWrapper.GPU;
using NvAPIWrapper.Native;
using NvAPIWrapper.Native.GPU;
using NvAPIWrapper.Native.GPU.Structures;

namespace OmenSuperHub {
  public static class GpuAppManager {
    public class GpuAppInfo {
      public int ProcessId { get; set; }
      public string ProcessName { get; set; }
      public string FilePath { get; set; }
    }

    public static List<GpuAppInfo> GetGpuApps() {
      var apps = new List<GpuAppInfo>();
      try {
        // ponytail: try --query-compute-apps first, fall back to parsing standard output
        string command = "nvidia-smi --query-compute-apps=pid,process_name --format=csv,noheader";
        var result = ExecuteCommand(command);
        if (result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.Output)) {
          string[] lines = result.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
          foreach (string line in lines) {
            string[] parts = line.Split(',');
            if (parts.Length >= 2 && int.TryParse(parts[0].Trim(), out int pid)) {
              try { using (var p = Process.GetProcessById(pid)) { } } catch { continue; }
              string fullPath = parts[1].Trim();
              apps.Add(new GpuAppInfo {
                ProcessId = pid,
                ProcessName = System.IO.Path.GetFileName(fullPath),
                FilePath = GetProcessPath(pid, fullPath) ?? fullPath
              });
            }
          }
        }
        if (apps.Count == 0) {
          // fallback: parse standard nvidia-smi Processes section
          result = ExecuteCommand("nvidia-smi");
          if (result.ExitCode == 0) {
            var m = Regex.Match(result.Output, @"\|(\s+\d+\s+\S+\s+\S+\s+)(\d+)(\s+)(\S+)(\s+\S[^|]*)\|");
            // simpler: find all "PID" lines
            foreach (Match match in Regex.Matches(result.Output, @"^\|\s+\d+\s+N/A\s+N/A\s+(\d+)\s+\S+\s+(\S[^|]*?)\s+\S+\s*\|", RegexOptions.Multiline)) {
              if (int.TryParse(match.Groups[1].Value, out int pid)) {
                try { using (var p = Process.GetProcessById(pid)) { } } catch { continue; }
                string name = match.Groups[2].Value.Trim();
                if (!string.IsNullOrEmpty(name) && !apps.Any(a => a.ProcessId == pid))
                  apps.Add(new GpuAppInfo { ProcessId = pid, ProcessName = name, FilePath = GetProcessPath(pid, name) });
              }
            }
          }
        }
      } catch { }
      return apps;
    }

    static string GetProcessPath(int pid, string fallbackName) {
      try {
        using (var searcher = new ManagementObjectSearcher($"SELECT ExecutablePath FROM Win32_Process WHERE ProcessId = {pid}"))
        using (var results = searcher.Get()) {
          foreach (ManagementObject obj in results) {
            string path = obj["ExecutablePath"]?.ToString();
            if (!string.IsNullOrEmpty(path)) return path;
          }
        }
      } catch { }
      try {
        using (var proc = Process.GetProcessById(pid)) {
          return proc.MainModule?.FileName;
        }
      } catch { }
      return null;
    }

    public static void RestartGpu() {
      try {
        string instanceId = null;
        string query = "SELECT * FROM Win32_PnPEntity WHERE PNPClass = 'Display'";
        using (var searcher = new ManagementObjectSearcher(query)) {
          foreach (ManagementObject device in searcher.Get()) {
            string description = device["Description"]?.ToString();
            if (!string.IsNullOrEmpty(description) && description.IndexOf("nvidia", StringComparison.OrdinalIgnoreCase) >= 0) {
              instanceId = device["PNPDeviceID"]?.ToString();
              break;
            }
          }
        }
        if (string.IsNullOrEmpty(instanceId)) return;
        ExecuteCommand($"pnputil /restart-device \"{instanceId}\"");
      } catch { }
    }

    public static List<string> GetAllGpuNamesList() {
      var gpuNames = new List<string>();
      try {
        using (var searcher = new ManagementObjectSearcher("SELECT Name, AdapterCompatibility FROM Win32_VideoController"))
        using (var collection = searcher.Get()) {
          foreach (ManagementObject obj in collection) {
            string name = obj["Name"]?.ToString() ?? "";
            string compatibility = obj["AdapterCompatibility"]?.ToString() ?? "";
            if (name.Contains("Microsoft") || compatibility.Contains("Microsoft")) continue;
            if (name.Contains("Display")) continue;
            if (!string.IsNullOrWhiteSpace(name)) gpuNames.Add(name.Trim());
          }
        }
      } catch { }
      return gpuNames;
    }

    public static List<(string Name, int ModelNum)> GetNvidiaGpuInfoList() {
      var result = new List<(string Name, int ModelNum)>();
      try {
        using (var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController WHERE Name LIKE '%NVIDIA%'")) {
          foreach (ManagementObject obj in searcher.Get()) {
            string name = obj["Name"]?.ToString() ?? "";
            var m = Regex.Match(name, @"\b(\d{3,})\b");
            int modelNum = m.Success ? int.Parse(m.Value) : -1;
            result.Add((name, modelNum));
          }
        }
      } catch { }
      return result;
    }

    public static bool HasNvidiaGpu() {
      try {
        using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
            @"SYSTEM\CurrentControlSet\Enum\PCI")) {
          foreach (string device in key.GetSubKeyNames()) {
            if (device.StartsWith("VEN_10DE", StringComparison.OrdinalIgnoreCase))
              return true;
          }
        }
      } catch { }
      return false;
    }

    public static bool IsAbove50Series() {
      var gpus = GetNvidiaGpuInfoList();
      if (gpus.Count == 0) return true;
      return gpus.All(g => g.ModelNum >= 5000);
    }

    public static float[] GetGpuPowerLimits() {
      var limits = new float[2] { -2f, -2f };
      try {
        var result = ExecuteCommand("nvidia-smi -q -d POWER");
        if (result.ExitCode == 0) {
          string currentPattern = @"Current Power Limit\s+:\s+([\d.]+)\s+W";
          string maxPattern = @"Max Power Limit\s+:\s+([\d.]+)\s+W";
          var currentMatch = Regex.Match(result.Output, currentPattern);
          var maxMatch = Regex.Match(result.Output, maxPattern);
          if (currentMatch.Success && maxMatch.Success) {
            limits[0] = float.Parse(currentMatch.Groups[1].Value);
            limits[1] = float.Parse(maxMatch.Groups[1].Value);
          }
        }
      } catch { }
      return limits;
    }

    public static int GetGpuTemperatureTarget() {
      int limit = -2;
      try {
        var result = ExecuteCommand("nvidia-smi -q -d TEMPERATURE");
        if (result.ExitCode == 0) {
          string targetPattern = @"GPU Target Temperature\s+:\s+(\d+)\s+C";
          var targetMatch = Regex.Match(result.Output, targetPattern);
          if (targetMatch.Success) limit = int.Parse(targetMatch.Groups[1].Value);
        }
      } catch { }
      return limit;
    }

    public static string GetGpuVRAM() {
      try {
        var result = ExecuteCommand("nvidia-smi --query-gpu=memory.total --format=csv,noheader,nounits");
        if (result.ExitCode == 0 && float.TryParse(result.Output.Trim(), out float mb)) {
          return (mb / 1024).ToString("F0");
        }
      } catch { }
      return null;
    }

    public static bool CheckDBVersion(int kind) {
      var result = ExecuteCommand("nvidia-smi");
      if (result.ExitCode == 0) {
        string pattern = @"NVIDIA-SMI\s+(\d+\.\d+)";
        Match match = Regex.Match(result.Output, pattern);
        string version = match.Success ? match.Groups[1].Value : null;
        if (version != null) {
          Version v1 = new Version(version);
          Version v2 = new Version("537.42");
          Version v3 = new Version("610.47");
          if (v1 >= v2 && v1 < v3) return true;
        }
      }
      return false;
    }

    public static void ChangeDBVersion(int kind) {
      string currentPath = AppDomain.CurrentDomain.BaseDirectory;
      string extractedInfFilePath = Path.Combine(currentPath, "nvpcf.inf");
      string extractedSysFilePath = Path.Combine(currentPath, "nvpcf.sys");
      string extractedCatFilePath = Path.Combine(currentPath, "nvpcf.CAT");
      ExtractResourceToFile("OmenSuperHub.Resources.nvpcf_inf.inf", extractedInfFilePath);
      ExtractResourceToFile("OmenSuperHub.Resources.nvpcf_sys.sys", extractedSysFilePath);
      ExtractResourceToFile("OmenSuperHub.Resources.nvpcf_cat.CAT", extractedCatFilePath);
      string targetVersion = "08/28/2023 31.0.15.3730";
      string driverFile = Path.Combine(currentPath, "nvpcf.inf");
      bool hasVersion = false;
      string command = "pnputil /enum-drivers";
      var result = ExecuteCommand(command);
      string output = result.Output;
      var lines = output.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
      var namesToDelete = new List<string>();
      for (int i = 0; i < lines.Length; i++) {
        if (lines[i].Contains(":      nvpcf.inf")) {
          if (i > 0 && lines[i - 1].Contains(":")) {
            string publishedName = lines[i - 1].Split(':')[1].Trim();
            if (i + 4 < lines.Length && lines[i + 4].Contains(":")) {
              string driverVersion = lines[i + 4].Split(':')[1].Trim();
              if (driverVersion != targetVersion)
                namesToDelete.Add(publishedName);
              else
                hasVersion = true;
            }
          }
        }
      }
      if (!hasVersion)
        ExecuteCommand($"pnputil /add-driver \"{driverFile}\" /install /force");
      foreach (var name in namesToDelete)
        ExecuteCommand($"pnputil /delete-driver \"{name}\" /uninstall /force");
      DeleteExtractedFiles(extractedInfFilePath, extractedSysFilePath, extractedCatFilePath);
    }

    static void ExtractResourceToFile(string resourceName, string outputFilePath) {
      using (Stream resourceStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)) {
        if (resourceStream != null) {
          using (FileStream fileStream = new FileStream(outputFilePath, FileMode.Create)) {
            resourceStream.CopyTo(fileStream);
          }
        }
      }
    }

    static void DeleteExtractedFiles(params string[] paths) {
      foreach (var path in paths) {
        if (File.Exists(path)) File.Delete(path);
      }
    }

    public static void SetCoreClockOffset(int offsetMhz) {
      NVIDIA.Initialize();
      try {
        PhysicalGPU[] gpus = PhysicalGPU.GetPhysicalGPUs();
        if (gpus.Length == 0) return;
        PhysicalGPU gpu = gpus[0];
        var clockDelta = new PerformanceStates20ClockEntryV1(
            PublicClockDomain.Graphics,
            new PerformanceStates20ParameterDelta(offsetMhz * 1000));
        var pState = new PerformanceStates20InfoV1.PerformanceState20(
            PerformanceStateId.P0_3DPerformance,
            new PerformanceStates20ClockEntryV1[] { clockDelta },
            new PerformanceStates20BaseVoltageEntryV1[0]);
        var writeInfo = new PerformanceStates20InfoV1(
            new PerformanceStates20InfoV1.PerformanceState20[] { pState },
            1u, 0u);
        GPUApi.SetPerformanceStates20(gpu.Handle, writeInfo);
      } finally {
        NVIDIA.Unload();
      }
    }

    public static void SetMemoryClockOffset(int offsetMhz) {
      NVIDIA.Initialize();
      try {
        PhysicalGPU[] gpus = PhysicalGPU.GetPhysicalGPUs();
        if (gpus.Length == 0) return;
        PhysicalGPU gpu = gpus[0];
        var clockDelta = new PerformanceStates20ClockEntryV1(
            PublicClockDomain.Memory,
            new PerformanceStates20ParameterDelta(offsetMhz * 1000));
        var pState = new PerformanceStates20InfoV1.PerformanceState20(
            PerformanceStateId.P0_3DPerformance,
            new PerformanceStates20ClockEntryV1[] { clockDelta },
            new PerformanceStates20BaseVoltageEntryV1[0]);
        var writeInfo = new PerformanceStates20InfoV1(
            new PerformanceStates20InfoV1.PerformanceState20[] { pState },
            1u, 0u);
        GPUApi.SetPerformanceStates20(gpu.Handle, writeInfo);
      } finally {
        NVIDIA.Unload();
      }
    }

    public static int GetCoreClockOffset() {
      NVIDIA.Initialize();
      try {
        PhysicalGPU gpu = PhysicalGPU.GetPhysicalGPUs()[0];
        var pstatesInfo = GPUApi.GetPerformanceStates20(gpu.Handle);
        if (pstatesInfo.Clocks.TryGetValue(PerformanceStateId.P0_3DPerformance, out var clockEntries)) {
          foreach (var clock in clockEntries) {
            if (clock.DomainId == PublicClockDomain.Graphics) {
              return clock.FrequencyDeltaInkHz.DeltaValue / 1000;
            }
          }
        }
        return 0;
      } finally {
        NVIDIA.Unload();
      }
    }

    public static int GetMemoryClockOffset() {
      NVIDIA.Initialize();
      try {
        PhysicalGPU gpu = PhysicalGPU.GetPhysicalGPUs()[0];
        var pstatesInfo = GPUApi.GetPerformanceStates20(gpu.Handle);
        if (pstatesInfo.Clocks.TryGetValue(PerformanceStateId.P0_3DPerformance, out var clockEntries)) {
          foreach (var clock in clockEntries) {
            if (clock.DomainId == PublicClockDomain.Memory) {
              return clock.FrequencyDeltaInkHz.DeltaValue / 1000;
            }
          }
        }
        return 0;
      } finally {
        NVIDIA.Unload();
      }
    }

    public static int GetGraphicsBoostClock() {
      try {
        PhysicalGPU[] gpus = PhysicalGPU.GetPhysicalGPUs();
        if (gpus.Length == 0) return 0;
        PhysicalGPU gpu = gpus[0];
        var info = GPUApi.GetAllClockFrequencies(gpu.Handle, new ClockFrequenciesV2(ClockType.BoostClock));
        foreach (var kvp in info.Clocks) {
          if (kvp.Key == PublicClockDomain.Graphics && kvp.Value.IsPresent) {
            return (int)(kvp.Value.Frequency / 1000);
          }
        }
      } catch { }
      return 0;
    }

    // ─── NVIDIA V-F Curve (via private NVAPI, like UXTU) ───

    /// <summary>V-F 曲线点: 索引/电压(mV)/频率(MHz)</summary>
    public struct VfPoint {
      public int Index, VoltageMv, FrequencyMHz;
      public VfPoint(int idx, int mv, int mhz) { Index = idx; VoltageMv = mv; FrequencyMHz = mhz; }
    }

    public const int VfPointCount = 127;
    const int VoltageStepMv = 25;      // 电压步进 25mV
    const int RampStartMv = 725;       // ramp 起始电压阈值
    const int VfToleranceMHz = 35;     // 验证容差 ±35MHz

    static bool _vfApiInited;
    static IntPtr _vfGetStatusPtr;
    static IntPtr _vfSetControlPtr;

    static bool InitVfApi() {
      if (_vfApiInited) return _vfGetStatusPtr != IntPtr.Zero;
      _vfApiInited = true;
      try {
        _vfGetStatusPtr = NvApiPrivate.QueryInterface(0x21537AD4);
        _vfSetControlPtr = NvApiPrivate.QueryInterface(0x0733E009);
      } catch { }
      return _vfGetStatusPtr != IntPtr.Zero && _vfSetControlPtr != IntPtr.Zero;
    }

    static int ReadLe32(byte[] buf, int off) => buf[off] | (buf[off + 1] << 8) | (buf[off + 2] << 16) | (buf[off + 3] << 24);
    static void WriteLe32(byte[] buf, int off, int val) {
      buf[off] = (byte)val; buf[off + 1] = (byte)(val >> 8); buf[off + 2] = (byte)(val >> 16); buf[off + 3] = (byte)(val >> 24);
    }

    public static bool TryGetVfCurve(out List<VfPoint> points) {
      points = new List<VfPoint>();
      if (!InitVfApi()) return false;
      if (!NvApiPrivate.TryGetFirstGpuHandle(out IntPtr gpu)) return false;
      var getStatus = Marshal.GetDelegateForFunctionPointer<NvApiPrivate.GpuBufferDelegate>(_vfGetStatusPtr);
      const int statusSize = 0x1C28, statusVersion = (1 << 16) | statusSize, statusMaskOffset = 0x04;
      const int statusNumClocksOffset = 0x14, statusEntriesOffset = 0x48, statusEntryStride = 0x1C;
      byte[] buffer = new byte[statusSize];
      WriteLe32(buffer, 0, statusVersion);
      for (int i = 0; i < 16; i++) buffer[statusMaskOffset + i] = 0xFF;
      WriteLe32(buffer, statusNumClocksOffset, 15);
      if (getStatus(gpu, buffer) != 0) return false;
      for (int i = 0; i < VfPointCount; i++) {
        int off = statusEntriesOffset + i * statusEntryStride;
        uint freqKhz = (uint)ReadLe32(buffer, off);
        uint voltUv = (uint)ReadLe32(buffer, off + 4);
        if (freqKhz > 0 && voltUv > 0)
          points.Add(new VfPoint(i, (int)(voltUv / 1000), (int)(freqKhz / 1000)));
      }
      return points.Count > 0;
    }

    static bool SetVfPointOffset(int pointIndex, int freqOffsetKhz, int voltOffsetUv) {
      if (!InitVfApi()) return false;
      if (pointIndex < 0 || pointIndex >= VfPointCount) return false;
      if (!NvApiPrivate.TryGetFirstGpuHandle(out IntPtr gpu)) return false;
      var setControl = Marshal.GetDelegateForFunctionPointer<NvApiPrivate.GpuBufferDelegate>(_vfSetControlPtr);
      const int controlSize = 0x2420, controlVersion = (1 << 16) | controlSize;
      const int controlMaskOffset = 0x04, controlEntriesOffset = 0x20, controlEntryStride = 0x48;
      byte[] buffer = new byte[controlSize];
      WriteLe32(buffer, 0, controlVersion);
      buffer[controlMaskOffset + pointIndex / 8] = (byte)(1 << (pointIndex % 8));
      int entryOff = controlEntriesOffset + pointIndex * controlEntryStride;
      WriteLe32(buffer, entryOff, freqOffsetKhz);
      WriteLe32(buffer, entryOff + 4, voltOffsetUv);
      return setControl(gpu, buffer) == 0;
    }

    // ─── Legacy simple offset (kept for backward compat) ───
    public static bool SetVoltageCurveOffset(int offsetMv) {
      try {
        if (!TryGetVfCurve(out var curve)) return false;
        int ok = 0;
        foreach (var pt in curve)
          if (SetVfPointOffset(pt.Index, 0, offsetMv * 1000)) ok++;
        return ok > 0;
      } catch { return false; }
    }

    // ─── V-F Curve Remapping (UXTU-style, true undervolt) ───

    /// <summary>
    /// 真正的 V-F 曲线降压：指定目标电压和频率，压平高频段曲线。
    /// 返回: 1=成功验证, 0=写入但验证不通过, -1=失败
    /// </summary>
    public static int SetUndervoltCurveFromDefault(int maxVoltageMv, int maxClockMhz) {
      if (maxVoltageMv < 600 || maxVoltageMv > 1300) return -1;
      if (maxClockMhz < 300 || maxClockMhz > 4500) return -1;
      try {
        if (!TryGetVfCurve(out var curve)) return -1;
        var usable = curve
            .Where(p => p.Index >= 0 && p.Index < VfPointCount)
            .OrderBy(p => p.VoltageMv).ThenBy(p => p.Index).ToList();
        if (usable.Count == 0) return -1;

        int alignedMaxVoltageMv = AlignToSupportedVoltage(usable, maxVoltageMv);

        var pivot = usable
            .OrderBy(p => Math.Abs(p.VoltageMv - alignedMaxVoltageMv))
            .ThenBy(p => Math.Abs(p.FrequencyMHz - maxClockMhz))
            .First();

        var rampStart = usable
            .Where(p => p.VoltageMv <= RampStartMv)
            .OrderByDescending(p => p.VoltageMv)
            .FirstOrDefault();
        if (rampStart.Equals(default(VfPoint))) rampStart = usable.First();

        int ok = 0, fail = 0;
        foreach (var pt in usable) {
          CalculateDesiredPoint(pt, rampStart, pivot,
              alignedMaxVoltageMv, maxClockMhz,
              out int desiredVoltageMv, out int desiredFrequencyMhz);
          int freqOffKhz = (desiredFrequencyMhz - pt.FrequencyMHz) * 1000;
          int voltOffUv = (desiredVoltageMv - pt.VoltageMv) * 1000;
          if (SetVfPointOffset(pt.Index, freqOffKhz, voltOffUv)) ok++; else fail++;
        }
        if (ok == 0) return -1;
        return VerifyUndervoltCurve(alignedMaxVoltageMv, maxClockMhz) ? 1 : 0;
      } catch { return -1; }
    }

    /// <summary>逐点计算降压目标值: rampStart以下保持, rampStart~pivot线性插值, pivot以上压平</summary>
    static void CalculateDesiredPoint(VfPoint pt, VfPoint rampStart, VfPoint pivot,
        int targetVoltageMv, int targetClockMhz, out int desiredVoltageMv, out int desiredFrequencyMhz) {
      if (pt.VoltageMv <= rampStart.VoltageMv) {
        desiredVoltageMv = pt.VoltageMv;
        desiredFrequencyMhz = pt.FrequencyMHz;
        return;
      }
      if (pt.VoltageMv < pivot.VoltageMv) {
        double denom = Math.Max(1, pivot.VoltageMv - rampStart.VoltageMv);
        double progress = (pt.VoltageMv - rampStart.VoltageMv) / denom;
        desiredVoltageMv = AlignTo25Mv(
            (int)Math.Round(rampStart.VoltageMv + (targetVoltageMv - rampStart.VoltageMv) * progress));
        desiredFrequencyMhz = (int)Math.Round(
            rampStart.FrequencyMHz + (targetClockMhz - rampStart.FrequencyMHz) * progress);
        return;
      }
      desiredVoltageMv = targetVoltageMv;
      desiredFrequencyMhz = targetClockMhz;
    }

    static int AlignTo25Mv(int mv) => (int)Math.Round(mv / (double)VoltageStepMv) * VoltageStepMv;

    static int AlignToSupportedVoltage(List<VfPoint> points, int requestedMv) {
      int aligned = AlignTo25Mv(requestedMv);
      return points.OrderBy(p => Math.Abs(p.VoltageMv - aligned))
          .ThenBy(p => Math.Abs(p.VoltageMv - requestedMv)).First().VoltageMv;
    }

    /// <summary>
    /// 预览降压曲线（只计算不写入硬件）。
    /// 返回原始曲线点列表和降压后的目标点列表，供 UI 绘制曲线图。
    /// </summary>
    public static bool PreviewDesiredCurve(int maxVoltageMv, int maxClockMhz,
        out List<VfPoint> original, out List<VfPoint> desired,
        out VfPoint pivot, out VfPoint rampStart) {
      original = null; desired = null;
      pivot = default; rampStart = default;
      if (maxVoltageMv < 600 || maxVoltageMv > 1300) return false;
      if (maxClockMhz < 300 || maxClockMhz > 4500) return false;
      try {
        if (!TryGetVfCurve(out var curve)) return false;
        original = curve.Where(p => p.Index >= 0 && p.Index < VfPointCount)
            .OrderBy(p => p.VoltageMv).ThenBy(p => p.Index).ToList();
        if (original.Count == 0) return false;

        int alignedMaxVoltageMv = AlignToSupportedVoltage(original, maxVoltageMv);

        pivot = original
            .OrderBy(p => Math.Abs(p.VoltageMv - alignedMaxVoltageMv))
            .ThenBy(p => Math.Abs(p.FrequencyMHz - maxClockMhz))
            .First();

        rampStart = original
            .Where(p => p.VoltageMv <= RampStartMv)
            .OrderByDescending(p => p.VoltageMv)
            .FirstOrDefault();
        if (rampStart.Equals(default(VfPoint))) rampStart = original.First();

        desired = new List<VfPoint>(original.Count);
        foreach (var pt in original) {
          CalculateDesiredPoint(pt, rampStart, pivot,
              alignedMaxVoltageMv, maxClockMhz,
              out int dMv, out int dMhz);
          desired.Add(new VfPoint(pt.Index, dMv, dMhz));
        }
        return true;
      } catch { return false; }
    }

    /// <summary>验证降压结果: 电压 ≥ maxVoltageMv 的点中，多数频率应在 targetClock ±35MHz 内</summary>
    static bool VerifyUndervoltCurve(int maxVoltageMv, int targetClockMhz) {
      if (!TryGetVfCurve(out var verified)) return false;
      var upper = verified
          .Where(p => p.Index >= 0 && p.Index < VfPointCount && p.VoltageMv >= maxVoltageMv)
          .OrderBy(p => p.VoltageMv).ThenBy(p => p.Index).ToList();
      if (upper.Count == 0) return false;
      int matchClock = upper.Count(p => Math.Abs(p.FrequencyMHz - targetClockMhz) <= VfToleranceMHz);
      int matchVolt = upper.Count(p => Math.Abs(p.VoltageMv - maxVoltageMv) <= VoltageStepMv);
      return matchClock >= Math.Max(1, upper.Count / 2) &&
             matchVolt >= Math.Max(1, upper.Count / 2);
    }

    /// <summary>恢复默认 V-F 曲线 —— 将所有 127 个点的偏移归零</summary>
    public static bool ResetVfCurve() {
      try {
        if (!InitVfApi()) return false;
        int ok = 0;
        for (int i = 0; i < VfPointCount; i++)
          if (SetVfPointOffset(i, 0, 0)) ok++;
        return ok > 0;
      } catch { return false; }
    }

    /// <summary>
    /// 按用户编辑逐点写回 GPU —— 微星小飞机风格
    /// desiredFreq[127]: null=不修改, 非null=目标频率MHz
    /// 写入后立即回读验证。
    /// 返回: 2=成功写入且回读验证通过, 1=NVAPI返回成功但回读未匹配(可能GPU/驱动不支持V-F编辑), 0=无任何点被写入, -1=失败
    /// </summary>
    public static int ApplyVfCurveFromUserEdits(int?[] desiredFreq, out int wrote, out int verified) {
      wrote = 0; verified = 0;
      try {
        if (desiredFreq == null || !InitVfApi()) return -1;
        if (!TryGetVfCurve(out var before)) return -1;
        if (before.Count == 0) return -1;

        // Build index → frequency map from the pre-write read
        var beforeMap = new Dictionary<int, int>();
        foreach (var p in before) beforeMap[p.Index] = p.FrequencyMHz;

        // Collect all offsets first, then write each
        var toWrite = new List<(int idx, int deltaKhz)>();
        for (int i = 0; i < VfPointCount; i++) {
          if (!desiredFreq[i].HasValue) continue;
          if (!beforeMap.TryGetValue(i, out int curFreq)) continue;
          int desiredMhz = desiredFreq[i].Value;
          int deltaKhz = (desiredMhz - curFreq) * 1000;
          if (deltaKhz == 0) continue;
          toWrite.Add((i, deltaKhz));
        }
        if (toWrite.Count == 0) return 0;

        // Write each offset
        foreach (var (idx, dKhz) in toWrite)
          if (SetVfPointOffset(idx, dKhz, 0))
            wrote++;

        if (wrote == 0) return -1;

        // ── Verify by re-reading the curve (same approach as UXTU) ──
        if (!TryGetVfCurve(out var after)) return 1; // wrote but can't verify
        var afterMap = new Dictionary<int, int>();
        foreach (var p in after) afterMap[p.Index] = p.FrequencyMHz;

        foreach (var (idx, dKhz) in toWrite) {
          if (!afterMap.TryGetValue(idx, out int afterFreq)) continue;
          if (!beforeMap.TryGetValue(idx, out int beforeFreq)) continue;
          int expectedFreq = beforeFreq + dKhz / 1000;
          if (Math.Abs(afterFreq - expectedFreq) <= 2) // ±2 MHz tolerance (rounding)
            verified++;
        }

        return verified == toWrite.Count ? 2 : (verified > 0 ? 1 : 1);
      } catch { return -1; }
    }

    public static int GetMemoryBoostClock() {
      try {
        PhysicalGPU[] gpus = PhysicalGPU.GetPhysicalGPUs();
        if (gpus.Length == 0) return 0;
        PhysicalGPU gpu = gpus[0];
        var info = GPUApi.GetAllClockFrequencies(gpu.Handle, new ClockFrequenciesV2(ClockType.BoostClock));
        foreach (var kvp in info.Clocks) {
          if (kvp.Key == PublicClockDomain.Memory && kvp.Value.IsPresent) {
            return (int)(kvp.Value.Frequency / 1000);
          }
        }
      } catch { }
      return 0;
    }

    // ─── NVML Power Limit ───
    // Direct NVML P/Invoke (no CLI parsing, instant apply)
    public static bool SetPowerLimit(int watts) {
      try {
        if (!Nvml.TryGetGpu(out IntPtr gpu)) return false;
        if (!TryGetPowerLimitInfo(out var info)) return false;
        if (watts < info.Min || watts > info.Max) return false;
        return Nvml.nvmlDeviceSetPowerManagementLimit(gpu, (uint)watts * 1000) == 0;
      } catch { return false; }
    }

    public static bool TryGetPowerLimitInfo(out (int Min, int Current, int Default, int Max) info) {
      info = default;
      try {
        if (!Nvml.TryGetGpu(out IntPtr gpu)) return false;
        int r1 = Nvml.nvmlDeviceGetPowerManagementLimitConstraints(gpu, out uint minMw, out uint maxMw);
        int r2 = Nvml.nvmlDeviceGetPowerManagementLimit(gpu, out uint curMw);
        int r3 = Nvml.nvmlDeviceGetPowerManagementDefaultLimit(gpu, out uint defMw);
        if (r1 != 0 || r2 != 0 || r3 != 0) return false;
        info = ((int)(minMw / 1000), (int)(curMw / 1000), (int)(defMw / 1000), (int)(maxMw / 1000));
        return true;
      } catch { return false; }
    }

    // ─── NVIDIA Max GPU Clock Lock (NVML) ───
    public static bool SetMaxGpuClock(int clockMHz) {
      try {
        if (!Nvml.TryGetGpu(out IntPtr gpu)) return false;
        // clockMHz=0 → unlock, otherwise lock to [0, clockMHz]
        int ret = clockMHz > 0
            ? Nvml.nvmlDeviceSetGpuLockedClocks(gpu, 0, (uint)clockMHz)
            : Nvml.nvmlDeviceResetGpuLockedClocks(gpu);
        return ret == 0;
      } catch { return false; }
    }

    public static int GetMaxGpuClockLock() {
      // ponytail: uses NVAPI (faster than NVML for this query)
      try {
        NVIDIA.Initialize();
        PhysicalGPU gpu = PhysicalGPU.GetPhysicalGPUs()[0];
        var data = GPUApi.GetClockBoostLock(gpu.Handle);
        return (int)data.ClockBoostLocks[0].VoltageInMicroV / 1000;
      } catch { return 0; }
      finally { NVIDIA.Unload(); }
    }

    // ─── NVML P/Invoke wrapper (like UXTU) ───
    static class Nvml {
      public const string Dll = "nvml.dll";
      public const int SUCCESS = 0;
      static bool _init;
      static bool _ok;
      public static bool EnsureInit() {
        if (_init) return _ok;
        _init = true;
        _ok = nvmlInit_v2() == SUCCESS;
        return _ok;
      }
      public static bool TryGetGpu(out IntPtr gpu) {
        gpu = IntPtr.Zero;
        return EnsureInit() && nvmlDeviceGetHandleByIndex_v2(0, out gpu) == SUCCESS;
      }
      [DllImport(Dll)] public static extern int nvmlInit_v2();
      [DllImport(Dll)] public static extern int nvmlShutdown();
      [DllImport(Dll)] public static extern int nvmlDeviceGetHandleByIndex_v2(uint index, out IntPtr device);
      [DllImport(Dll)] public static extern int nvmlDeviceGetPowerManagementLimit(IntPtr device, out uint limitMw);
      [DllImport(Dll)] public static extern int nvmlDeviceSetPowerManagementLimit(IntPtr device, uint limitMw);
      [DllImport(Dll)] public static extern int nvmlDeviceGetPowerManagementLimitConstraints(IntPtr device, out uint minMw, out uint maxMw);
      [DllImport(Dll)] public static extern int nvmlDeviceGetPowerManagementDefaultLimit(IntPtr device, out uint limitMw);
      [DllImport(Dll)] public static extern int nvmlDeviceSetGpuLockedClocks(IntPtr device, uint minGpuMHz, uint maxGpuMHz);
      [DllImport(Dll)] public static extern int nvmlDeviceResetGpuLockedClocks(IntPtr device);
      [DllImport(Dll, CharSet = CharSet.Ansi)] public static extern int nvmlDeviceGetName(IntPtr device, System.Text.StringBuilder name, uint length);
    }

    private static readonly HashSet<string> _allowedCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
      "nvidia-smi", "pnputil", "sc", "schtasks", "rd", "del", "reg", "cmd", "taskkill"
    };

    private static bool IsCommandSafe(string command) {
      string trimmed = command.TrimStart();
      if (trimmed.StartsWith("\"")) {
        int end = trimmed.IndexOf("\"", 1);
        if (end < 0) return false;
      }
      string exe = trimmed.Contains(" ") ? trimmed.Substring(0, trimmed.IndexOf(" ")) : trimmed;
      exe = exe.Trim('"');
      string exeName = System.IO.Path.GetFileName(exe);
      if (!_allowedCommands.Contains(exeName)) return false;
      if (command.Contains("&") || command.Contains("|") || command.Contains(";") ||
          command.Contains("`") || command.Contains("$(") || command.Contains("\n") || command.Contains("\r"))
        return false;
      return true;
    }

    public static ProcessResult ExecuteCommand(string command) {
      if (!IsCommandSafe(command)) {
        Logger.Error($"GpuAppManager: blocked unsafe command");
        return new ProcessResult { ExitCode = -1, Output = "", Error = "Blocked: unsafe command" };
      }
      string exe = command.TrimStart().Contains(" ") ? command.TrimStart().Substring(0, command.TrimStart().IndexOf(" ")) : command.TrimStart();
      string args = command.TrimStart().Contains(" ") ? command.TrimStart().Substring(command.TrimStart().IndexOf(" ") + 1) : "";
      exe = exe.Trim('"');
      if (exe.Equals("cmd", StringComparison.OrdinalIgnoreCase) || exe.EndsWith("\\cmd.exe", StringComparison.OrdinalIgnoreCase)) {
        var processStartInfo = new ProcessStartInfo {
          FileName = "cmd.exe",
          Arguments = args,
          RedirectStandardOutput = true,
          RedirectStandardError = true,
          UseShellExecute = false,
          CreateNoWindow = true,
          WindowStyle = ProcessWindowStyle.Hidden
        };
        using (var process = new Process { StartInfo = processStartInfo }) {
          process.Start();
          string output = process.StandardOutput.ReadToEnd();
          string error = process.StandardError.ReadToEnd();
          process.WaitForExit();
          return new ProcessResult { ExitCode = process.ExitCode, Output = output, Error = error };
        }
      }
      var psi = new ProcessStartInfo {
        FileName = exe,
        Arguments = args,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
        WindowStyle = ProcessWindowStyle.Hidden
      };
      using (var process = new Process { StartInfo = psi }) {
        process.Start();
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new ProcessResult { ExitCode = process.ExitCode, Output = output, Error = error };
      }
    }

    public class ProcessResult {
      public int ExitCode { get; set; }
      public string Output { get; set; }
      public string Error { get; set; }
    }

    // ─── Private NVAPI (undocumented function IDs, like UXTU) ───
    static class NvApiPrivate {
      [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
      public static extern IntPtr QueryInterface(uint functionId);
      [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
      public delegate int GpuBufferDelegate(IntPtr gpuHandle, [In, Out] byte[] buffer);
      public static bool TryGetFirstGpuHandle(out IntPtr gpu) {
        gpu = IntPtr.Zero;
        IntPtr initPtr = QueryInterface(0x0150E828);
        IntPtr enumPtr = QueryInterface(0xE5AC921F);
        if (initPtr == IntPtr.Zero || enumPtr == IntPtr.Zero) return false;
        var init = Marshal.GetDelegateForFunctionPointer<Action>(initPtr);
        var enumGpus = Marshal.GetDelegateForFunctionPointer<EnumGpusDelegate>(enumPtr);
        init();
        IntPtr[] handles = new IntPtr[64];
        if (enumGpus(handles, out int count) != 0 || count <= 0) return false;
        gpu = handles[0];
        return true;
      }
      [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
      delegate int EnumGpusDelegate([Out] IntPtr[] gpuHandles, out int gpuCount);
    }
  }
}
