using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace OmenSuperHub.Services {
  sealed class PresentMonFpsMonitor : IDisposable {
    const double SampleWindowSeconds = 1.5;
    static readonly TimeSpan CleanupInterval = TimeSpan.FromMilliseconds(500);
    static readonly TimeSpan StartFailureRetryInterval = TimeSpan.FromSeconds(10);

    struct FpsSample {
      public DateTime TimestampUtc;
      public double MsBetweenPresents;
    }

    sealed class FpsWindow {
      public string Application;
      public string NormalizedApplication;
      public DateTime LastSeenUtc;
      public Queue<FpsSample> Samples = new Queue<FpsSample>();
      public double SampleTotalMs;
    }

    readonly object syncRoot = new object();
    readonly Dictionary<int, FpsWindow> windowsByProcessId = new Dictionary<int, FpsWindow>();
    readonly Dictionary<string, FpsWindow> windowsByApplication = new Dictionary<string, FpsWindow>(StringComparer.OrdinalIgnoreCase);

    Process presentMonProcess;
    Thread stdoutThread;
    Thread stderrThread;
    bool disposed;
    bool headerParsed;
    int processIdColumnIndex = -1;
    int applicationColumnIndex = -1;
    int msBetweenPresentsColumnIndex = -1;
    string executablePath = string.Empty;
    string activeProcessNameFilter = string.Empty;
    int trackedProcessId = 0;
    string trackedProcessName = string.Empty;
    string statusText = "PresentMon 未启动";
    DateTime nextStartAttemptUtc = DateTime.MinValue;
    DateTime nextCleanupUtc = DateTime.MinValue;
    int _lastFps;
    string _lastApp;

    public int LastFps { get { lock (syncRoot) { return _lastFps; } } }
    public string LastApp { get { lock (syncRoot) { return _lastApp; } } }

    public bool IsRunning {
      get {
        lock (syncRoot) {
          return presentMonProcess != null && !presentMonProcess.HasExited;
        }
      }
    }

    public bool EnsureRunning(string processNameFilter, out string message) {
      string cachedExecutablePath = string.Empty;
      string normalizedProcessNameFilter = NormalizeProcessName(processNameFilter);
      bool restartRequired = false;
      lock (syncRoot) {
        if (disposed) { message = "已释放"; return false; }
        if (presentMonProcess != null && !presentMonProcess.HasExited) {
          if (string.Equals(activeProcessNameFilter, normalizedProcessNameFilter, StringComparison.OrdinalIgnoreCase)) {
            message = statusText; return true;
          }
          restartRequired = true;
        }
        if (!restartRequired && DateTime.UtcNow < nextStartAttemptUtc) { message = statusText; return false; }
        cachedExecutablePath = executablePath;
      }

      if (restartRequired) Stop();

      string resolvedPath = ResolveExecutablePath(cachedExecutablePath);
      if (string.IsNullOrWhiteSpace(resolvedPath)) {
        lock (syncRoot) {
          executablePath = string.Empty;
          statusText = "未找到 PresentMon.exe";
          nextStartAttemptUtc = DateTime.UtcNow + StartFailureRetryInterval;
        }
        message = StatusText; return false;
      }

      var process = new Process();
      process.StartInfo = new ProcessStartInfo {
        FileName = resolvedPath,
        Arguments = BuildArguments(normalizedProcessNameFilter),
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true,
        WorkingDirectory = Path.GetDirectoryName(resolvedPath)
      };
      process.EnableRaisingEvents = true;
      process.Exited += (s, e) => { lock (syncRoot) { if (!disposed) statusText = "PresentMon 已停止"; } };

      try {
        if (!process.Start()) {
          lock (syncRoot) { executablePath = resolvedPath; statusText = "启动失败"; nextStartAttemptUtc = DateTime.UtcNow + StartFailureRetryInterval; }
          message = StatusText; process.Dispose(); return false;
        }
      } catch (Exception ex) {
        lock (syncRoot) { executablePath = resolvedPath; statusText = "启动失败: " + ex.Message; nextStartAttemptUtc = DateTime.UtcNow + StartFailureRetryInterval; }
        message = StatusText; process.Dispose(); return false;
      }

      lock (syncRoot) {
        executablePath = resolvedPath;
        presentMonProcess = process;
        headerParsed = false;
        processIdColumnIndex = -1;
        applicationColumnIndex = -1;
        msBetweenPresentsColumnIndex = -1;
        windowsByProcessId.Clear();
        windowsByApplication.Clear();
        activeProcessNameFilter = normalizedProcessNameFilter;
        trackedProcessId = 0;
        trackedProcessName = normalizedProcessNameFilter;
        nextStartAttemptUtc = DateTime.MinValue;
        nextCleanupUtc = DateTime.MinValue;
        statusText = "采集中";
      }

      stdoutThread = new Thread(ReadStdoutLoop) { IsBackground = true, Name = "PMStdout" };
      stderrThread = new Thread(ReadStderrLoop) { IsBackground = true, Name = "PMStderr" };
      stdoutThread.Start(process);
      stderrThread.Start(process);
      message = StatusText;
      return true;
    }

    public void Stop() {
      Process processToStop = null;
      lock (syncRoot) {
        processToStop = presentMonProcess;
        presentMonProcess = null;
        windowsByProcessId.Clear();
        windowsByApplication.Clear();
        headerParsed = false;
        activeProcessNameFilter = string.Empty;
        trackedProcessId = 0;
        trackedProcessName = string.Empty;
        nextStartAttemptUtc = DateTime.MinValue;
        nextCleanupUtc = DateTime.MinValue;
        if (!disposed) statusText = "已停止";
      }
      if (processToStop == null) return;
      try {
        if (!processToStop.HasExited) { processToStop.Kill(); processToStop.WaitForExit(1000); }
      } catch { } finally { processToStop.Dispose(); }
    }

    /// <summary>Poll current FPS — must be called from timer (UI-safe).</summary>
    public void Poll() {
      lock (syncRoot) {
        MaybeCleanupStaleSamples(DateTime.UtcNow);
        // Foreground process mode: track active window
        if (string.IsNullOrEmpty(activeProcessNameFilter)) {
          int pid = GetForegroundPid();
          if (pid > 0 && pid != trackedProcessId) {
            trackedProcessId = pid;
            trackedProcessName = "";
          }
          if (trackedProcessId > 0 && windowsByProcessId.TryGetValue(trackedProcessId, out var w) && TryComputeFps(w, out float fps)) {
            _lastFps = (int)Math.Round(fps);
            _lastApp = w.Application ?? "";
            return;
          }
        } else {
          // Process name mode
          if (!string.IsNullOrWhiteSpace(trackedProcessName) && windowsByApplication.TryGetValue(trackedProcessName, out var w) && (DateTime.UtcNow - w.LastSeenUtc).TotalSeconds <= SampleWindowSeconds && TryComputeFps(w, out float fps)) {
            _lastFps = (int)Math.Round(fps);
            _lastApp = w.Application ?? "";
            return;
          }
        }
        _lastFps = 0;
      }
    }

    static int GetForegroundPid() {
      try {
        var h = NativeMethods.GetForegroundWindow();
        if (h == IntPtr.Zero) return 0;
        NativeMethods.GetWindowThreadProcessId(h, out uint pid);
        return (int)pid;
      } catch { return 0; }
    }

    static class NativeMethods {
      [System.Runtime.InteropServices.DllImport("user32.dll")]
      public static extern IntPtr GetForegroundWindow();
      [System.Runtime.InteropServices.DllImport("user32.dll")]
      public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    }

    public void Dispose() { if (!disposed) { disposed = true; Stop(); } }

    static string NormalizeProcessName(string name) {
      if (string.IsNullOrWhiteSpace(name)) return string.Empty;
      string n = name.Trim();
      if (!n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) n += ".exe";
      return n;
    }

    static string BuildArguments(string filter) {
      string args = "--output_stdout --exclude_dropped --no_console_stats --stop_existing_session --restart_as_admin";
      if (!string.IsNullOrWhiteSpace(filter)) {
        args += " --process_name " + QuoteArgument(filter);
        args += " --terminate_on_proc_exit";
      }
      return args;
    }

    static string QuoteArgument(string v) => string.IsNullOrEmpty(v) ? "\"\"" : "\"" + v.Replace("\"", "\\\"") + "\"";

    static string ResolveExecutablePath(string cached) {
      if (!string.IsNullOrWhiteSpace(cached) && File.Exists(cached)) return cached;
      string baseDir = AppDomain.CurrentDomain.BaseDirectory;
      string[] paths = {
        Path.Combine(baseDir, "PresentMon.exe"),
        Path.Combine(baseDir, "PresentMon64.exe"),
        Path.Combine(baseDir, "tools", "PresentMon.exe"),
        Path.Combine(baseDir, "tools", "PresentMon64.exe")
      };
      foreach (var p in paths) if (File.Exists(p)) return p;
      // Search PATH
      string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
      foreach (string dir in pathEnv.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)) {
        try {
          var found = Directory.GetFiles(dir.Trim(), "PresentMon*.exe", SearchOption.TopDirectoryOnly).FirstOrDefault();
          if (found != null) return found;
        } catch { }
      }
      return "";
    }

    string StatusText { get { lock (syncRoot) { return statusText; } } }

    void ReadStdoutLoop(object state) {
      var p = state as Process;
      if (p == null) return;
      try { string l; while ((l = p.StandardOutput.ReadLine()) != null) { if (!string.IsNullOrWhiteSpace(l)) HandleCsvLine(l); } }
      catch { lock (syncRoot) { if (!disposed) statusText = "读取异常"; } }
    }

    void ReadStderrLoop(object state) {
      var p = state as Process;
      if (p == null) return;
      try { string l; while ((l = p.StandardError.ReadLine()) != null) { if (!string.IsNullOrWhiteSpace(l)) { lock (syncRoot) { if (!disposed) statusText = l.Trim(); } } } } catch { }
    }

    void HandleCsvLine(string line) {
      lock (syncRoot) {
        if (!headerParsed) {
          var cols = ParseCsvLine(line);
          if (cols.Count == 0) return;
          processIdColumnIndex = FindIndex(cols, "ProcessID");
          applicationColumnIndex = FindIndex(cols, "Application");
          msBetweenPresentsColumnIndex = FindIndex(cols, "msBetweenPresents");
          if (processIdColumnIndex >= 0 && applicationColumnIndex >= 0 && msBetweenPresentsColumnIndex >= 0) headerParsed = true;
          return;
        }
        if (!TryExtract(line, out int pid, out string app, out double ms)) return;
        if (pid <= 0 || ms <= 0) return;
        string norm = NormalizeProcessName(app);
        if (!string.IsNullOrWhiteSpace(activeProcessNameFilter) && !string.Equals(activeProcessNameFilter, norm, StringComparison.OrdinalIgnoreCase)) return;
        var now = DateTime.UtcNow;
        if (!windowsByProcessId.TryGetValue(pid, out var w)) { w = new FpsWindow(); windowsByProcessId[pid] = w; }
        w.Application = app;
        w.NormalizedApplication = norm;
        w.LastSeenUtc = now;
        w.Samples.Enqueue(new FpsSample { TimestampUtc = now, MsBetweenPresents = ms });
        w.SampleTotalMs += ms;
        if (!string.IsNullOrWhiteSpace(norm)) windowsByApplication[norm] = w;
        CleanupWindow(w, now);
      }
    }

    bool TryExtract(string line, out int pid, out string app, out double ms) {
      pid = 0; app = ""; ms = 0;
      string pidStr = null, msStr = null;
      bool sawApp = false;
      int limit = Math.Max(processIdColumnIndex, Math.Max(applicationColumnIndex, msBetweenPresentsColumnIndex));
      bool inQ = false;
      int start = 0, col = 0;
      for (int i = 0; i <= line.Length; i++) {
        bool end = i == line.Length;
        char c = end ? '\0' : line[i];
        if (!end && c == '"') { inQ = !inQ; continue; }
        if (!end && (c != ',' || inQ)) continue;
        string val = col == processIdColumnIndex || col == applicationColumnIndex || col == msBetweenPresentsColumnIndex ? Unquote(line.Substring(start, i - start)) : null;
        if (col == processIdColumnIndex) pidStr = val;
        else if (col == applicationColumnIndex) { app = (val ?? "").Trim(); sawApp = true; }
        else if (col == msBetweenPresentsColumnIndex) msStr = val;
        if (col >= limit && pidStr != null && msStr != null && sawApp) break;
        col++; start = i + 1;
      }
      return pidStr != null && msStr != null && sawApp && int.TryParse(pidStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out pid) && double.TryParse(msStr, NumberStyles.Float, CultureInfo.InvariantCulture, out ms);
    }

    static int FindIndex(List<string> cols, string name) {
      for (int i = 0; i < cols.Count; i++) if (string.Equals(cols[i], name, StringComparison.OrdinalIgnoreCase)) return i;
      return -1;
    }

    void MaybeCleanupStaleSamples(DateTime now) {
      if (now < nextCleanupUtc) return;
      CleanupStaleSamples(now);
      nextCleanupUtc = now + CleanupInterval;
    }

    void CleanupStaleSamples(DateTime now) {
      List<int> remove = null;
      foreach (var kv in windowsByProcessId) {
        CleanupWindow(kv.Value, now);
        if (kv.Value.Samples.Count == 0 && (now - kv.Value.LastSeenUtc).TotalSeconds > SampleWindowSeconds) { if (remove == null) remove = new List<int>(); remove.Add(kv.Key); }
      }
      if (remove == null) return;
      foreach (int id in remove) {
        if (!windowsByProcessId.TryGetValue(id, out var w)) continue;
        windowsByProcessId.Remove(id);
        if (!string.IsNullOrWhiteSpace(w.NormalizedApplication) && windowsByApplication.TryGetValue(w.NormalizedApplication, out var cw) && ReferenceEquals(cw, w))
          windowsByApplication.Remove(w.NormalizedApplication);
      }
    }

    static void CleanupWindow(FpsWindow w, DateTime now) {
      while (w.Samples.Count > 0 && (now - w.Samples.Peek().TimestampUtc).TotalSeconds > SampleWindowSeconds) {
        var s = w.Samples.Dequeue(); w.SampleTotalMs -= s.MsBetweenPresents;
      }
      if (w.SampleTotalMs < 0) w.SampleTotalMs = 0;
    }

    static bool TryComputeFps(FpsWindow w, out float fps) {
      fps = 0;
      if (w == null || w.Samples.Count == 0) return false;
      double avg = w.SampleTotalMs / w.Samples.Count;
      if (avg <= 0) return false;
      fps = (float)(1000.0 / avg);
      return true;
    }

    static List<string> ParseCsvLine(string line) {
      var cols = new List<string>();
      if (line == null) return cols;
      bool inQ = false;
      int start = 0;
      for (int i = 0; i < line.Length; i++) {
        if (line[i] == '"') inQ = !inQ;
        else if (line[i] == ',' && !inQ) { cols.Add(Unquote(line.Substring(start, i - start))); start = i + 1; }
      }
      cols.Add(Unquote(line.Substring(start)));
      return cols;
    }

    static string Unquote(string v) {
      if (string.IsNullOrEmpty(v)) return "";
      v = v.Trim();
      if (v.Length >= 2 && v[0] == '"' && v[v.Length - 1] == '"') v = v.Substring(1, v.Length - 2).Replace("\"\"", "\"");
      return v;
    }
  }
}
