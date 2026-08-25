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
      } catch (Exception ex) { Logger.Verbose($"[GetGpuApps] {ex.Message}"); }
      return apps;
    }

    static string GetProcessPath(int pid, string fallbackName) {
      try {
        using (var searcher = new ManagementObjectSearcher($"SELECT ExecutablePath FROM Win32_Process WHERE ProcessId = {pid}"))
        using (var results = searcher.Get()) {
          foreach (ManagementObject obj in results) {
            using (obj) {
              string path = obj["ExecutablePath"]?.ToString();
              if (!string.IsNullOrEmpty(path)) return path;
            }
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
            using (device) {
              string description = device["Description"]?.ToString();
              if (!string.IsNullOrEmpty(description) && description.IndexOf("nvidia", StringComparison.OrdinalIgnoreCase) >= 0) {
                instanceId = device["PNPDeviceID"]?.ToString();
                break;
              }
            }
          }
        }
        if (string.IsNullOrEmpty(instanceId)) return;
        ExecuteCommand($"pnputil /restart-device \"{instanceId}\"");
      } catch (Exception ex) { Logger.Verbose($"[RestartGpu] {ex.Message}"); }
    }

    public static List<string> GetAllGpuNamesList() {
      var gpuNames = new List<string>();
      try {
        using (var searcher = new ManagementObjectSearcher("SELECT Name, AdapterCompatibility FROM Win32_VideoController"))
        using (var collection = searcher.Get()) {
          foreach (ManagementObject obj in collection) {
            using (obj) {
              string name = obj["Name"]?.ToString() ?? "";
              string compatibility = obj["AdapterCompatibility"]?.ToString() ?? "";
              if (name.Contains("Microsoft") || compatibility.Contains("Microsoft")) continue;
              if (name.Contains("Display")) continue;
              if (!string.IsNullOrWhiteSpace(name)) gpuNames.Add(name.Trim());
            }
          }
        }
      } catch (Exception ex) { Logger.Verbose($"[GetAllGpuNamesList] {ex.Message}"); }
      return gpuNames;
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
      } catch (Exception ex) { Logger.Verbose($"[HasNvidiaGpu] {ex.Message}"); }
      return false;
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
      } catch (Exception ex) { Logger.Verbose($"[GetGpuPowerLimits] {ex.Message}"); }
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
      } catch (Exception ex) { Logger.Verbose($"[GetGpuTemperatureTarget] {ex.Message}"); }
      return limit;
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

    // ponytail: V-F 曲线死代码已删除
    //   删除项：VfPoint/VfPointCount/VoltageStepMv/RampStartMv/VfToleranceMHz 常量、
    //   _vfApiInited/_vfGetStatusPtr/_vfSetControlPtr 字段、InitVfApi/ReadLe32/WriteLe32、
    //   TryGetVfCurve/SetVfPointOffset/SetVoltageCurveOffset/SetUndervoltCurveFromDefault、
    //   CalculateDesiredPoint/AlignTo25Mv/AlignToSupportedVoltage/PreviewDesiredCurve、
    //   VerifyUndervoltCurve/ResetVfCurve/ApplyVfCurveFromUserEdits、
    //   GetNvidiaGpuInfoList/IsAbove50Series/GetGpuVRAM、
    //   GetGraphicsBoostClock/GetMemoryBoostClock、
    //   SetMaxGpuClock/GetMaxGpuClockLock、NvApiPrivate 嵌套类
    //   外部零调用，全为 V-F 降压子系统残留

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
  }
}
