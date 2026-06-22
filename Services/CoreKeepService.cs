using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace OmenSuperHub.Services {
  [DataContract]
  public class CoreKeepEntry {
    [DataMember] public bool Enabled { get; set; }
    [DataMember] public string ProcessName { get; set; }
    [DataMember] public uint PriorityClass { get; set; }
    [DataMember] public long AffinityMask { get; set; }
    [DataMember] public string CapturedAt { get; set; }
  }

  [DataContract]
  public class CoreKeepData {
    [DataMember] public bool MasterEnabled { get; set; }
    [DataMember] public List<CoreKeepEntry> Entries { get; set; } = new List<CoreKeepEntry>();
  }

  public static class CoreKeepService {
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool SetProcessAffinityMask(IntPtr hProcess, IntPtr dwProcessAffinityMask);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool SetPriorityClass(IntPtr hProcess, uint dwPriorityClass);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern uint GetPriorityClass(IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetProcessAffinityMask(IntPtr hProcess, out IntPtr lpProcessAffinityMask, out IntPtr lpSystemAffinityMask);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CloseHandle(IntPtr hObject);

    const uint PROCESS_QUERY_INFORMATION = 0x0400;
    const uint PROCESS_SET_INFORMATION = 0x0200;
    const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    static readonly string ConfigPath = Path.Combine(
      AppDomain.CurrentDomain.BaseDirectory, "CoreKeep.json");
    static ManagementEventWatcher _watcher;
    static Dictionary<string, CoreKeepEntry> _activeEntries = new Dictionary<string, CoreKeepEntry>();

    public static CoreKeepData Load() {
      try {
        if (!File.Exists(ConfigPath)) return new CoreKeepData();
        using (var ms = new MemoryStream(File.ReadAllBytes(ConfigPath))) {
          var ser = new DataContractJsonSerializer(typeof(CoreKeepData));
          var data = (CoreKeepData)ser.ReadObject(ms) ?? new CoreKeepData();
          if (data.Entries == null) data.Entries = new List<CoreKeepEntry>();
          return data;
        }
      } catch { return new CoreKeepData(); }
    }

    public static void Save(CoreKeepData data) {
      try {
        var ser = new DataContractJsonSerializer(typeof(CoreKeepData));
        using (var ms = new MemoryStream()) {
          ser.WriteObject(ms, data);
          File.WriteAllBytes(ConfigPath, ms.ToArray());
        }
      } catch { }
    }

    public static CoreKeepEntry CaptureFromProcess(string processName) {
      var entry = new CoreKeepEntry { ProcessName = processName, CapturedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") };
      try {
        Process[] procs = Process.GetProcessesByName(processName.Replace(".exe", ""));
        if (procs.Length == 0) return entry;
        int pid = procs[0].Id;
        IntPtr hProc = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (hProc == IntPtr.Zero) hProc = OpenProcess(PROCESS_QUERY_INFORMATION, false, pid);
        if (hProc != IntPtr.Zero) {
          try {
            entry.PriorityClass = GetPriorityClass(hProc);
            IntPtr affinity;
            if (GetProcessAffinityMask(hProc, out affinity, out _))
              entry.AffinityMask = affinity.ToInt64();
          } finally { CloseHandle(hProc); }
        }
      } catch { }
      return entry;
    }

    public static void ApplyToProcess(string processName, CoreKeepEntry entry) {
      if (string.IsNullOrEmpty(processName) || !entry.Enabled) return;
      try {
        Process[] procs = Process.GetProcessesByName(processName.Replace(".exe", ""));
        foreach (Process p in procs) {
          IntPtr hProc = OpenProcess(PROCESS_SET_INFORMATION, false, p.Id);
          if (hProc != IntPtr.Zero) {
            try {
              if (entry.PriorityClass != 0)
                SetPriorityClass(hProc, entry.PriorityClass);
              if (entry.AffinityMask != 0)
                SetProcessAffinityMask(hProc, new IntPtr(entry.AffinityMask));
            } finally { CloseHandle(hProc); }
          }
        }
      } catch { }
    }

    public static void StartAutoApply(CoreKeepData data) {
      StopAutoApply();
      if (!data.MasterEnabled) return;
      _activeEntries.Clear();
      foreach (var entry in data.Entries) {
        if (!entry.Enabled || string.IsNullOrEmpty(entry.ProcessName)) continue;
        string name = entry.ProcessName.Replace(".exe", "").ToLowerInvariant();
        if (!_activeEntries.ContainsKey(name))
          _activeEntries[name] = entry;
      }
      if (_activeEntries.Count == 0) return;
      // Apply to already running processes
      foreach (var kv in _activeEntries)
        ApplyToProcess(kv.Key, kv.Value);
      // Watch for new processes via WMI
      try {
        _watcher = new ManagementEventWatcher(
          new WqlEventQuery("SELECT * FROM Win32_ProcessStartTrace"));
        _watcher.EventArrived += (s, e) => {
          string name = e.NewEvent.Properties["ProcessName"]?.Value?.ToString()?.ToLowerInvariant();
          if (name != null && _activeEntries.ContainsKey(name))
            ApplyToProcess(name, _activeEntries[name]);
        };
        _watcher.Start();
      } catch { }
    }

    public static void StopAutoApply() {
      try { _watcher?.Stop(); _watcher?.Dispose(); } catch { }
      _watcher = null;
      _activeEntries.Clear();
    }

    public static string PriorityClassName(uint val) {
      switch (val) {
        case 0x00000040: return Strings.CoreKeepPriorityIdle;
        case 0x00004000: return Strings.CoreKeepPriorityBelowNormal;
        case 0x00000020: return Strings.CoreKeepPriorityNormal;
        case 0x00008000: return Strings.CoreKeepPriorityAboveNormal;
        case 0x00000080: return Strings.CoreKeepPriorityHigh;
        case 0x00000100: return Strings.CoreKeepPriorityRealtime;
        default: return Strings.CoreKeepPriorityUnknown;
      }
    }

    public static string PriorityClassShortName(uint val) {
      switch (val) {
        case 0x00000040: return "IDLE";
        case 0x00004000: return "BELOW_NORMAL";
        case 0x00000020: return "NORMAL";
        case 0x00008000: return "ABOVE_NORMAL";
        case 0x00000080: return "HIGH";
        case 0x00000100: return "REALTIME";
        default: return val.ToString();
      }
    }
  }
}
