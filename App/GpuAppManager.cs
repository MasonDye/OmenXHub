using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Reflection;
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
        string command = "nvidia-smi --query-compute-apps=pid,process_name --format=csv,noheader";
        var result = ExecuteCommand(command);
        if (result.ExitCode == 0) {
          string[] lines = result.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
          foreach (string line in lines) {
            string[] parts = line.Split(',');
            if (parts.Length >= 2 && int.TryParse(parts[0].Trim(), out int pid)) {
              string procName = parts[1].Trim();
              apps.Add(new GpuAppInfo {
                ProcessId = pid,
                ProcessName = procName,
                FilePath = GetProcessPath(pid, procName)
              });
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

    public static ProcessResult ExecuteCommand(string command) {
      var processStartInfo = new ProcessStartInfo {
        FileName = "cmd.exe",
        Arguments = $"/c {command}",
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

    public class ProcessResult {
      public int ExitCode { get; set; }
      public string Output { get; set; }
      public string Error { get; set; }
    }
  }
}
