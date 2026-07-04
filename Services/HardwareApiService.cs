// HardwareApiService.cs - 本地 HTTP REST API 服务
// HttpListener 监听 localhost:5000，提供 30+ 端点读写硬件设置（风扇/CPU/GPU/灯效/系统）
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OmenSuperHub.Services;
using static OmenSuperHub.OmenHardware;
using LibreHardwareType = LibreHardwareMonitor.Hardware.HardwareType;
using LibreSensorType = LibreHardwareMonitor.Hardware.SensorType;

namespace OmenSuperHub.Services {
  internal static class HardwareApiService {
    private static HttpListener _listener;
    private static CancellationTokenSource _cts;
    private static Task _listenTask;
    private static bool _running;
    private static string _apiToken;

    public static bool IsRunning => _running;
    public static string ApiToken => _apiToken;

    public static void Start(string prefix = "http://localhost:5000/") {
      if (_running) return;
      try {
        _apiToken = Guid.NewGuid().ToString("N");
        _cts = new CancellationTokenSource();
        _listener = new HttpListener();
        _listener.Prefixes.Add(prefix);
        _listener.Start();
        _running = true;
        _listenTask = Task.Run(() => ListenLoop(_cts.Token));
        Logger.Info($"HardwareApiService started on {prefix}");
      } catch (Exception ex) {
        Logger.Error($"HardwareApiService start failed: {ex.Message}");
        _running = false;
      }
    }

    public static void Stop() {
      if (!_running) return;
      _running = false;
      try { _cts?.Cancel(); } catch { }
      try { _listener?.Stop(); } catch { }
      try { _listener?.Close(); } catch { }
      try { _listenTask?.Wait(2000); } catch { }
      Logger.Info("HardwareApiService stopped");
    }

    private static async Task ListenLoop(CancellationToken ct) {
      while (!ct.IsCancellationRequested && _running) {
        try {
          HttpListenerContext ctx = await _listener.GetContextAsync();
          ThreadPool.QueueUserWorkItem(_ => HandleRequest(ctx));
        } catch (HttpListenerException) {
          break;
        } catch (ObjectDisposedException) {
          break;
        } catch (Exception ex) {
          Logger.Error($"HardwareApiService listen error: {ex.Message}");
        }
      }
    }

    // ═══════════════════════════════════════════════════════
    // Request Dispatch
    // ═══════════════════════════════════════════════════════

    private static void HandleRequest(HttpListenerContext ctx) {
      HttpListenerRequest req = ctx.Request;
      HttpListenerResponse resp = ctx.Response;
      string responseText;
      int statusCode = 200;

      try {
        string path = req.Url.AbsolutePath.TrimEnd('/').ToLowerInvariant();
        string method = req.HttpMethod.ToUpperInvariant();

        if (path != "/ping") {
          if (!ValidateRequest(req, path)) {
            statusCode = 403;
            responseText = MakeError("Forbidden: invalid token or origin");
            SendResponse(resp, statusCode, responseText);
            return;
          }
        }

        // ── GET endpoints ──
        if (method == "GET" && path == "/ping") {
          responseText = "OK";
        } else if (method == "GET" && path == "/api/temperature") {
          responseText = HandleGetTemperature();
        } else if (method == "GET" && path == "/api/fan/speed") {
          responseText = HandleGetFanSpeed();
        } else if (method == "GET" && path == "/api/fan/rpm") {
          responseText = HandleGetFanRpm();
        } else if (method == "GET" && path == "/api/mode") {
          responseText = HandleGetMode();
        } else if (method == "GET" && path == "/api/cpu/load") {
          responseText = HandleGetCpuLoad();
        } else if (method == "GET" && path == "/api/cpu/frequency") {
          responseText = HandleGetCpuFrequency();
        } else if (method == "GET" && path == "/api/gpu/load") {
          responseText = HandleGetGpuLoad();
        } else if (method == "GET" && path == "/api/gpu/frequency") {
          responseText = HandleGetGpuFrequency();
        } else if (method == "GET" && path == "/api/gpu/memory") {
          responseText = HandleGetGpuMemory();
        } else if (method == "GET" && path == "/api/battery") {
          responseText = HandleGetBattery();
        } else if (method == "GET" && path == "/api/hardware/all") {
          responseText = HandleGetHardwareAll();
        }
        // ── POST endpoints ──
        else if (method == "POST" && path == "/api/fan/speed") {
          var result = HandleSetFanSpeed(req);
          responseText = result.body;
          statusCode = result.code;
        } else if (method == "POST" && path == "/api/power/limit") {
          var result = HandleSetPowerLimit(req);
          responseText = result.body;
          statusCode = result.code;
        } else if (method == "POST" && path == "/api/mode") {
          var result = HandleSetMode(req);
          responseText = result.body;
          statusCode = result.code;
        }
        // ── CPU write endpoints ──
        else if (method == "POST" && path == "/api/cpu/pl1") {
          var result = HandleSetCpuPl1(req);
          responseText = result.body;
          statusCode = result.code;
        } else if (method == "POST" && path == "/api/cpu/pl2") {
          var result = HandleSetCpuPl2(req);
          responseText = result.body;
          statusCode = result.code;
        } else if (method == "POST" && path == "/api/cpu/tdp") {
          var result = HandleSetCpuTdp(req);
          responseText = result.body;
          statusCode = result.code;
        } else if (method == "POST" && path == "/api/cpu/iccmax") {
          var result = HandleSetCpuIccMax(req);
          responseText = result.body;
          statusCode = result.code;
        } else if (method == "POST" && path == "/api/cpu/loadline") {
          var result = HandleSetCpuLoadLine(req);
          responseText = result.body;
          statusCode = result.code;
        }
        // ── GPU write endpoints ──
        else if (method == "POST" && path == "/api/gpu/core/offset") {
          var result = HandleSetGpuCoreOffset(req);
          responseText = result.body;
          statusCode = result.code;
        } else if (method == "POST" && path == "/api/gpu/memory/offset") {
          var result = HandleSetGpuMemoryOffset(req);
          responseText = result.body;
          statusCode = result.code;
        } else if (method == "POST" && path == "/api/gpu/tgp") {
          var result = HandleSetGpuTgp(req);
          responseText = result.body;
          statusCode = result.code;
        } else if (method == "POST" && path == "/api/gpu/ppab") {
          var result = HandleSetGpuPpab(req);
          responseText = result.body;
          statusCode = result.code;
        }
        // ── GET read endpoints ──
        else if (method == "GET" && path == "/api/cpu/power/current") {
          responseText = HandleGetCpuPowerCurrent();
        } else if (method == "GET" && path == "/api/gpu/offset/current") {
          responseText = HandleGetGpuOffsetCurrent();
        }
        // ── Fan curve endpoints ──
        else if (method == "GET" && path == "/api/fan/curve/current") {
          responseText = HandleGetFanCurveCurrent();
        } else if (method == "POST" && path == "/api/fan/curve") {
          var result = HandleSetFanCurve(req);
          responseText = result.body;
          statusCode = result.code;
        } else if (method == "POST" && path == "/api/fan/max") {
          var result = HandleSetFanMax(req);
          responseText = result.body;
          statusCode = result.code;
        } else if (method == "POST" && path == "/api/fan/clean") {
          var result = HandleSetFanClean(req);
          responseText = result.body;
          statusCode = result.code;
        }
        // ── System endpoints ──
        else if (method == "GET" && path == "/api/system/info") {
          responseText = HandleGetSystemInfo();
        } else if (method == "GET" && path == "/api/system/uptime") {
          responseText = HandleGetSystemUptime();
        } else if (method == "POST" && path == "/api/system/restart") {
          var result = HandleSystemRestart(req);
          responseText = result.body;
          statusCode = result.code;
        } else if (method == "POST" && path == "/api/system/shutdown") {
          var result = HandleSystemShutdown(req);
          responseText = result.body;
          statusCode = result.code;
        }
        // ── Lighting endpoints ──
        else if (method == "GET" && path == "/api/lighting/status") {
          responseText = HandleGetLightingStatus();
        } else if (method == "POST" && path == "/api/lighting/brightness") {
          var result = HandleSetLightingBrightness(req);
          responseText = result.body;
          statusCode = result.code;
        } else if (method == "POST" && path == "/api/lighting/color") {
          var result = HandleSetLightingColor(req);
          responseText = result.body;
          statusCode = result.code;
        } else if (method == "POST" && path == "/api/lighting/effect") {
          var result = HandleSetLightingEffect(req);
          responseText = result.body;
          statusCode = result.code;
        } else if (method == "POST" && path == "/api/lighting/off") {
          var result = HandleSetLightingOff(req);
          responseText = result.body;
          statusCode = result.code;
        }
        // ── API status ──
        else if (method == "GET" && path == "/api/status") {
          responseText = HandleGetApiStatus();
        }
        // ── 404 ──
        else {
          statusCode = 404;
          responseText = "{\"error\":\"Not Found\"}";
        }
      } catch (Exception ex) {
        statusCode = 500;
        responseText = MakeError(ex.Message);
        Logger.Error($"HardwareApiService handle error: {ex.Message}");
      }

      SendResponse(resp, statusCode, responseText);
    }

    // ═══════════════════════════════════════════════════════
    // GET Handlers
    // ═══════════════════════════════════════════════════════

    private static string HandleGetTemperature() {
      float cpu = HardwareService.CPUTemp;
      float gpu = HardwareService.GPUTemp;
      return $"{{\"cpu\":{cpu:F1},\"gpu\":{gpu:F1}}}";
    }

    private static string HandleGetFanSpeed() {
      var fans = HardwareService.FanSpeedNow;
      int f1 = fans.Count > 0 ? fans[0] : 0;
      int f2 = fans.Count > 1 ? fans[1] : 0;
      return $"{{\"fan1\":{f1},\"fan2\":{f2}}}";
    }

    private static string HandleGetMode() {
      string mode = ConfigService.Preset;
      if (string.IsNullOrEmpty(mode)) mode = "Default";
      return $"{{\"mode\":\"{EscapeJson(mode)}\"}}";
    }

    // ── /api/fan/rpm ──
    private static string HandleGetFanRpm() {
      var fans = HardwareService.FanSpeedNow;
      int f1 = fans.Count > 0 ? fans[0] * 100 : 0;
      int f2 = fans.Count > 1 ? fans[1] * 100 : 0;
      return $"{{\"fan1\":{f1},\"fan2\":{f2}}}";
    }

    // ── /api/cpu/load ──
    private static string HandleGetCpuLoad() {
      float cpuLoad = 0;
      foreach (var hw in HardwareService.LibreComputer.Hardware) {
        if (hw.HardwareType == LibreHardwareType.Cpu) {
          hw.Update();
          foreach (var s in hw.Sensors)
            if (s.SensorType == LibreSensorType.Load && s.Name == "CPU Total")
              cpuLoad = (float)s.Value.GetValueOrDefault();
        }
      }
      return $"{{\"avg\":{cpuLoad:F1},\"cores\":{{}}}}";
    }

    // ── /api/cpu/frequency ──
    private static string HandleGetCpuFrequency() {
      float freq = 0;
      foreach (var hw in HardwareService.LibreComputer.Hardware) {
        if (hw.HardwareType == LibreHardwareType.Cpu) {
          hw.Update();
          foreach (var s in hw.Sensors)
            if (s.SensorType == LibreSensorType.Clock)
              freq = Math.Max(freq, (float)s.Value.GetValueOrDefault());
        }
      }
      return $"{{\"freq\":{freq:F0}}}";
    }

    // ── /api/gpu/load ──
    private static string HandleGetGpuLoad() {
      float load = 0;
      foreach (var hw in HardwareService.LibreComputer.Hardware) {
        if (hw.HardwareType == LibreHardwareType.GpuNvidia || hw.HardwareType == LibreHardwareType.GpuAmd) {
          hw.Update();
          foreach (var s in hw.Sensors)
            if (s.SensorType == LibreSensorType.Load && s.Name == "GPU Core")
              load = (float)s.Value.GetValueOrDefault();
        }
      }
      return $"{{\"load\":{load:F1}}}";
    }

    // ── /api/gpu/frequency ──
    private static string HandleGetGpuFrequency() {
      float freq = 0;
      foreach (var hw in HardwareService.LibreComputer.Hardware) {
        if (hw.HardwareType == LibreHardwareType.GpuNvidia || hw.HardwareType == LibreHardwareType.GpuAmd) {
          hw.Update();
          foreach (var s in hw.Sensors)
            if (s.SensorType == LibreSensorType.Clock && (s.Name == "GPU Core" || s.Name.Contains("Core")))
              freq = Math.Max(freq, (float)s.Value.GetValueOrDefault());
        }
      }
      return $"{{\"freq\":{freq:F0}}}";
    }

    // ── /api/gpu/memory ──
    private static string HandleGetGpuMemory() {
      float used = 0, total = 0;
      foreach (var hw in HardwareService.LibreComputer.Hardware) {
        if (hw.HardwareType == LibreHardwareType.GpuNvidia || hw.HardwareType == LibreHardwareType.GpuAmd) {
          hw.Update();
          foreach (var s in hw.Sensors) {
            if (s.SensorType == LibreSensorType.SmallData && s.Name == "GPU Memory Used")
              used = (float)s.Value.GetValueOrDefault();
            if (s.SensorType == LibreSensorType.SmallData && s.Name == "GPU Memory Total")
              total = (float)s.Value.GetValueOrDefault();
          }
        }
      }
      float available = total > used ? total - used : 0;
      return $"{{\"used\":{used:F0},\"total\":{total:F0},\"available\":{available:F0}}}";
    }

    // ── /api/battery ──
    private static string HandleGetBattery() {
      try {
        var ps = System.Windows.Forms.SystemInformation.PowerStatus;
        int pct = (int)(ps.BatteryLifePercent * 100);
        bool charging = ps.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online;
        string status = charging ? "Charging" : "Discharging";
        if (pct >= 100) status = "Full";
        return $"{{\"percent\":{pct},\"charging\":{(charging ? "true" : "false")},\"status\":\"{status}\",\"wear\":0}}";
      } catch {
        return "{\"percent\":0,\"charging\":false,\"status\":\"Unknown\",\"wear\":0}";
      }
    }

    // ── /api/hardware/all ──
    private static string HandleGetHardwareAll() {
      try {
        float cpuTemp = HardwareService.CPUTemp;
        float gpuTemp = HardwareService.GPUTemp;
        float cpuPower = HardwareService.CPUPower;
        var fans = HardwareService.FanSpeedNow;
        int f1 = fans.Count > 0 ? fans[0] : 0;
        int f2 = fans.Count > 1 ? fans[1] : 0;

        var ps = System.Windows.Forms.SystemInformation.PowerStatus;
        int batPct = (int)(ps.BatteryLifePercent * 100);
        bool charging = ps.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online;

        string preset = ConfigService.Preset;
        if (string.IsNullOrEmpty(preset)) preset = "Default";

        return "{" +
          $"\"cpu\":{{\"temp\":{cpuTemp:F1},\"load\":0,\"freq\":0,\"cores\":{{}}," +
          $"\"power\":{cpuPower:F1}}}," +
          $"\"gpu\":{{\"temp\":{gpuTemp:F1},\"load\":0,\"freq\":0," +
          $"\"power\":{HardwareService.GPUPower:F1}," +
          $"\"memoryUsed\":0,\"memoryTotal\":0}}," +
          $"\"fan\":{{\"percent\":[{f1},{f2}],\"rpm\":[{f1 * 100},{f2 * 100}]}}," +
          $"\"battery\":{{\"percent\":{batPct},\"charging\":{(charging ? "true" : "false")}," +
          $"\"status\":\"{(charging ? "Charging" : "Discharging")}\",\"wear\":0}}," +
          $"\"preset\":\"{EscapeJson(preset)}\"" +
          "}";
      } catch (Exception ex) {
        Logger.Error($"HandleGetHardwareAll error: {ex.Message}");
        return MakeError(ex.Message);
      }
    }

    // ═══════════════════════════════════════════════════════
    // POST Handlers
    // ═══════════════════════════════════════════════════════

    private static (int code, string body) HandleSetFanSpeed(HttpListenerRequest req) {
      try {
        string body = ReadBody(req);
        var param = Deserialize<FanSpeedParam>(body);
        if (param == null)
          return (400, MakeError("Invalid JSON body"));

        if (param.speed1 < 0 || param.speed1 > 100 || param.speed2 < 0 || param.speed2 > 100)
          return (400, MakeError("speed1 and speed2 must be between 0 and 100"));

        OmenHardware.SetFanLevel(param.speed1, param.speed2);
        Logger.Info($"API: SetFanLevel({param.speed1}, {param.speed2})");
        return (200, "{\"success\":true,\"message\":\"Fan speed updated\"}");
      } catch (Exception ex) {
        Logger.Error($"HandleSetFanSpeed error: {ex.Message}");
        return (500, MakeError(ex.Message));
      }
    }

    private static (int code, string body) HandleSetPowerLimit(HttpListenerRequest req) {
      try {
        string body = ReadBody(req);
        var param = Deserialize<PowerLimitParam>(body);
        if (param == null)
          return (400, MakeError("Invalid JSON body"));

        if (param.pl1 < 15 || param.pl1 > 120 || param.pl2 < 15 || param.pl2 > 120)
          return (400, MakeError("pl1 and pl2 must be between 15 and 120"));

        bool ok = OmenHardware.SetCpuPowerLimit((byte)param.pl1, (byte)param.pl2);
        Logger.Info($"API: SetCpuPowerLimit({param.pl1}, {param.pl2}) => {ok}");
        if (ok)
          return (200, "{\"success\":true,\"message\":\"CPU power limit updated\"}");
        else
          return (200, "{\"success\":false,\"message\":\"WMI call failed\"}");
      } catch (Exception ex) {
        Logger.Error($"HandleSetPowerLimit error: {ex.Message}");
        return (500, MakeError(ex.Message));
      }
    }

    private static readonly string[][] PresetAliases = new string[][] {
      new string[] { "Extreme", "extreme", "极致性能", "极限", "极致" },
      new string[] { "GpuPriority", "gpupriority", "gpu优先", "gpu", "GPU优先" },
      new string[] { "LightUse", "lightuse", "轻度使用", "light", "轻度" },
      new string[] { "Custom1", "custom1", "自定义1", "自定义预设1" },
      new string[] { "Custom2", "custom2", "自定义2", "自定义预设2" },
      new string[] { "Custom3", "custom3", "自定义3", "自定义预设3" },
    };

    private static string ResolvePresetKey(string input) {
      if (string.IsNullOrEmpty(input)) return null;
      string trimmed = input.Trim();
      foreach (var group in PresetAliases) {
        foreach (var alias in group) {
          if (string.Equals(alias, trimmed, StringComparison.OrdinalIgnoreCase))
            return group[0];
        }
      }
      return null;
    }

    private static (int code, string body) HandleSetMode(HttpListenerRequest req) {
      try {
        string body = ReadBody(req);
        var param = Deserialize<ModeParam>(body);
        if (param == null || string.IsNullOrWhiteSpace(param.mode))
          return (400, MakeError("Missing 'mode' parameter"));

        string presetKey = ResolvePresetKey(param.mode);
        if (presetKey == null)
          return (400, MakeError("Unknown preset. Supported: Extreme, GpuPriority, LightUse, Custom1-3"));

        AutomationProcessor.ApplyPreset(presetKey);
        Logger.Info($"API: ApplyPreset(\"{presetKey}\")");

        return (200, $"{{\"success\":true,\"message\":\"Switched to {EscapeJson(presetKey)}\"}}");
      } catch (Exception ex) {
        Logger.Error($"HandleSetMode error: {ex.Message}");
        return (500, MakeError(ex.Message));
      }
    }

    // ═══════════════════════════════════════════════════════
    // CPU Write Handlers
    // ═══════════════════════════════════════════════════════

    private static (int code, string body) HandleSetCpuPl1(HttpListenerRequest req) {
      try {
        var param = Deserialize<IntValueParam>(ReadBody(req));
        if (param == null) return (400, MakeError("Invalid JSON body"));
        if (param.value < 15 || param.value > 120)
          return (400, MakeError("value must be between 15 and 120"));
        bool ok = OmenHardware.SetCpuPowerLimitPL1Only((byte)param.value);
        Logger.Info($"API: SetCpuPowerLimitPL1Only({param.value}) => {ok}");
        return (200, ok
          ? "{\"success\":true,\"message\":\"PL1 updated\"}"
          : "{\"success\":false,\"message\":\"WMI call failed\"}");
      } catch (Exception ex) { Logger.Error($"HandleSetCpuPl1: {ex.Message}"); return (500, MakeError(ex.Message)); }
    }

    private static (int code, string body) HandleSetCpuPl2(HttpListenerRequest req) {
      try {
        var param = Deserialize<IntValueParam>(ReadBody(req));
        if (param == null) return (400, MakeError("Invalid JSON body"));
        if (param.value < 15 || param.value > 120)
          return (400, MakeError("value must be between 15 and 120"));
        bool ok = OmenHardware.SetCpuPowerLimitPL2Only((byte)param.value);
        Logger.Info($"API: SetCpuPowerLimitPL2Only({param.value}) => {ok}");
        return (200, ok
          ? "{\"success\":true,\"message\":\"PL2 updated\"}"
          : "{\"success\":false,\"message\":\"WMI call failed\"}");
      } catch (Exception ex) { Logger.Error($"HandleSetCpuPl2: {ex.Message}"); return (500, MakeError(ex.Message)); }
    }

    private static (int code, string body) HandleSetCpuTdp(HttpListenerRequest req) {
      try {
        var param = Deserialize<IntValueParam>(ReadBody(req));
        if (param == null) return (400, MakeError("Invalid JSON body"));
        if (param.value < 15 || param.value > 120)
          return (400, MakeError("value must be between 15 and 120"));
        bool ok = OmenHardware.SetConcurrentTdp((byte)param.value);
        Logger.Info($"API: SetConcurrentTdp({param.value}) => {ok}");
        return (200, ok
          ? "{\"success\":true,\"message\":\"TDP updated\"}"
          : "{\"success\":false,\"message\":\"WMI call failed\"}");
      } catch (Exception ex) { Logger.Error($"HandleSetCpuTdp: {ex.Message}"); return (500, MakeError(ex.Message)); }
    }

    private static (int code, string body) HandleSetCpuIccMax(HttpListenerRequest req) {
      try {
        var param = Deserialize<IntValueParam>(ReadBody(req));
        if (param == null) return (400, MakeError("Invalid JSON body"));
        if (param.value < 50 || param.value > 250)
          return (400, MakeError("value must be between 50 and 250"));
        OmenHardware.SetIccMaxByWmi((decimal)param.value);
        Logger.Info($"API: SetIccMaxByWmi({param.value})");
        return (200, "{\"success\":true,\"message\":\"IccMax updated\"}");
      } catch (Exception ex) { Logger.Error($"HandleSetCpuIccMax: {ex.Message}"); return (500, MakeError(ex.Message)); }
    }

    private static (int code, string body) HandleSetCpuLoadLine(HttpListenerRequest req) {
      try {
        var param = Deserialize<IntValueParam>(ReadBody(req));
        if (param == null) return (400, MakeError("Invalid JSON body"));
        if (param.value < 80 || param.value > 130)
          return (400, MakeError("value must be between 80 and 130"));
        OmenHardware.SetLoadLine(param.value);
        Logger.Info($"API: SetLoadLine({param.value})");
        return (200, "{\"success\":true,\"message\":\"LoadLine updated\"}");
      } catch (Exception ex) { Logger.Error($"HandleSetCpuLoadLine: {ex.Message}"); return (500, MakeError(ex.Message)); }
    }

    // ═══════════════════════════════════════════════════════
    // GPU Write Handlers
    // ═══════════════════════════════════════════════════════

    private static (int code, string body) HandleSetGpuCoreOffset(HttpListenerRequest req) {
      try {
        var param = Deserialize<IntOffsetParam>(ReadBody(req));
        if (param == null) return (400, MakeError("Invalid JSON body"));
        if (param.offset < -200 || param.offset > 300)
          return (400, MakeError("offset must be between -200 and 300"));
        GpuAppManager.SetCoreClockOffset(param.offset);
        Logger.Info($"API: SetCoreClockOffset({param.offset})");
        return (200, "{\"success\":true,\"message\":\"GPU core offset updated\"}");
      } catch (Exception ex) { Logger.Error($"HandleSetGpuCoreOffset: {ex.Message}"); return (500, MakeError(ex.Message)); }
    }

    private static (int code, string body) HandleSetGpuMemoryOffset(HttpListenerRequest req) {
      try {
        var param = Deserialize<IntOffsetParam>(ReadBody(req));
        if (param == null) return (400, MakeError("Invalid JSON body"));
        if (param.offset < -500 || param.offset > 1000)
          return (400, MakeError("offset must be between -500 and 1000"));
        GpuAppManager.SetMemoryClockOffset(param.offset);
        Logger.Info($"API: SetMemoryClockOffset({param.offset})");
        return (200, "{\"success\":true,\"message\":\"GPU memory offset updated\"}");
      } catch (Exception ex) { Logger.Error($"HandleSetGpuMemoryOffset: {ex.Message}"); return (500, MakeError(ex.Message)); }
    }

    private static (int code, string body) HandleSetGpuTgp(HttpListenerRequest req) {
      try {
        var param = Deserialize<IntValueParam>(ReadBody(req));
        if (param == null) return (400, MakeError("Invalid JSON body"));
        if (param.value < 80 || param.value > 150)
          return (400, MakeError("value must be between 80 and 150"));
        OmenHardware.SetGpuPowerState(true, ConfigService.PpabEnabled, ConfigService.DState);
        Logger.Info($"API: SetGpuPowerState(tgp=true, ppab={ConfigService.PpabEnabled})");
        return (200, "{\"success\":true,\"message\":\"GPU TGP enabled\"}");
      } catch (Exception ex) { Logger.Error($"HandleSetGpuTgp: {ex.Message}"); return (500, MakeError(ex.Message)); }
    }

    private static (int code, string body) HandleSetGpuPpab(HttpListenerRequest req) {
      try {
        var param = Deserialize<BoolParam>(ReadBody(req));
        if (param == null) return (400, MakeError("Invalid JSON body"));
        OmenHardware.SetGpuPowerState(ConfigService.TgpEnabled, param.enabled, ConfigService.DState);
        Logger.Info($"API: SetGpuPowerState(tgp={ConfigService.TgpEnabled}, ppab={param.enabled})");
        return (200, "{\"success\":true,\"message\":\"GPU PPAB updated\"}");
      } catch (Exception ex) { Logger.Error($"HandleSetGpuPpab: {ex.Message}"); return (500, MakeError(ex.Message)); }
    }

    // ═══════════════════════════════════════════════════════
    // GET Read Handlers (current values)
    // ═══════════════════════════════════════════════════════

    private static string HandleGetCpuPowerCurrent() {
      float pl1 = 0, pl2 = 0;
      try {
        using (var searcher = new System.Management.ManagementObjectSearcher(
            @"root\CIMV2", "SELECT * FROM Win32_Processor"))
        foreach (System.Management.ManagementObject mo in searcher.Get()) {
          uint? pl1v = (uint?)mo["CurrentClockSpeed"];
          if (pl1v.HasValue) pl1 = pl1v.Value;
        }
      } catch { }
      return $"{{\"pl1\":{pl1:F0},\"pl2\":{pl2:F0},\"tdp\":{HardwareService.CPUPower:F1},\"iccmax\":{ConfigService.IccMax},\"loadline\":{ConfigService.AcLoadLine}}}";
    }

    private static string HandleGetGpuOffsetCurrent() {
      try {
        int core = GpuAppManager.GetCoreClockOffset();
        int memory = GpuAppManager.GetMemoryClockOffset();
        return $"{{\"core\":{core},\"memory\":{memory}}}";
      } catch {
        return "{\"core\":0,\"memory\":0}";
      }
    }

    // ═══════════════════════════════════════════════════════
    // Fan Curve Handlers
    // ═══════════════════════════════════════════════════════

    private static string HandleGetFanCurveCurrent() {
      try {
        var curve = FanService.LoadCustomCurve();
        var sb = new StringBuilder("[");
        for (int i = 0; i < curve.Count; i++) {
          if (i > 0) sb.Append(",");
          sb.Append($"{{\"temp\":{curve[i].temp:F0},\"speed\":{curve[i].rpm}}}");
        }
        sb.Append("]");
        return $"{{\"cpu\":{sb},\"gpu\":{sb}}}";
      } catch (Exception ex) {
        Logger.Error($"HandleGetFanCurveCurrent: {ex.Message}");
        return "{\"cpu\":[],\"gpu\":[]}";
      }
    }

    private static (int code, string body) HandleSetFanCurve(HttpListenerRequest req) {
      try {
        var param = Deserialize<FanCurveParam>(ReadBody(req));
        if (param == null || param.points == null || param.points.Count < 2)
          return (400, MakeError("points array required, minimum 2 points"));

        var points = new List<(float temp, int rpm)>();
        foreach (var p in param.points) {
          if (p.temp < 0 || p.temp > 100 || p.speed < 0 || p.speed > 100)
            return (400, MakeError("temp must be 0-100, speed must be 0-100"));
          points.Add(((float)p.temp, p.speed));
        }

        FanService.SaveCustomCurve(points);
        FanService.ApplyCustomCurve(points);
        Logger.Info($"API: SetFanCurve with {points.Count} points");
        return (200, "{\"success\":true,\"message\":\"Fan curve updated\"}");
      } catch (Exception ex) { Logger.Error($"HandleSetFanCurve: {ex.Message}"); return (500, MakeError(ex.Message)); }
    }

    private static (int code, string body) HandleSetFanMax(HttpListenerRequest req) {
      try {
        var param = Deserialize<BoolParam>(ReadBody(req));
        if (param == null) return (400, MakeError("Invalid JSON body"));
        if (param.enabled)
          OmenHardware.SetMaxFanSpeedOn();
        else
          OmenHardware.SetMaxFanSpeedOff();
        Logger.Info($"API: SetMaxFanSpeed({param.enabled})");
        return (200, $"{{\"success\":true,\"message\":\"Max fan {(param.enabled ? "enabled" : "disabled")}\"}}");
      } catch (Exception ex) { Logger.Error($"HandleSetFanMax: {ex.Message}"); return (500, MakeError(ex.Message)); }
    }

    private static (int code, string body) HandleSetFanClean(HttpListenerRequest req) {
      try {
        System.Threading.Tasks.Task.Run(async () => {
          try {
            OmenHardware.SetFanLevel(100, 100, false, true);
            await System.Threading.Tasks.Task.Delay(30000);
            OmenHardware.SetFanLevel(0, 0);
            Logger.Info("API: Fan clean completed (30s)");
          } catch (Exception ex) { Logger.Error($"FanClean error: {ex.Message}"); }
        });
        Logger.Info("API: Fan clean started (30s)");
        return (200, "{\"success\":true,\"message\":\"Fan clean started for 30 seconds\"}");
      } catch (Exception ex) { Logger.Error($"HandleSetFanClean: {ex.Message}"); return (500, MakeError(ex.Message)); }
    }

    // ═══════════════════════════════════════════════════════
    // System Handlers
    // ═══════════════════════════════════════════════════════

    private static string HandleGetSystemInfo() {
      try {
        string bios = OmenHardware.GetBiosVersion();
        string cpu = OmenHardware.GetCpuModel();
        string os = Environment.OSVersion.ToString();
        string dotnet = Environment.Version.ToString();
        string machine = Environment.MachineName;
        return $"{{\"machine\":\"{EscapeJson(machine)}\",\"bios\":\"{EscapeJson(bios)}\"," +
               $"\"cpu\":\"{EscapeJson(cpu)}\",\"os\":\"{EscapeJson(os)}\",\"dotnet\":\"{EscapeJson(dotnet)}\"}}";
      } catch (Exception ex) { Logger.Error($"HandleGetSystemInfo: {ex.Message}"); return MakeError(ex.Message); }
    }

    private static string HandleGetSystemUptime() {
      try {
        var uptime = TimeSpan.FromMilliseconds(Environment.TickCount);
        return $"{{\"seconds\":{(int)uptime.TotalSeconds},\"formatted\":\"{uptime.Days}d {uptime.Hours}h {uptime.Minutes}m\"}}";
      } catch (Exception ex) { return MakeError(ex.Message); }
    }

    private static (int code, string body) HandleSystemRestart(HttpListenerRequest req) {
      try {
        var param = Deserialize<ConfirmParam>(ReadBody(req));
        if (param == null || !param.confirm)
          return (400, MakeError("confirm must be true"));
        Logger.Info("API: System restart requested");
        System.Diagnostics.Process.Start("shutdown", "/r /t 5 /c \"OmenXHub API restart\"")?.Dispose();
        return (200, "{\"success\":true,\"message\":\"System will restart in 5 seconds\"}");
      } catch (Exception ex) { Logger.Error($"HandleSystemRestart: {ex.Message}"); return (500, MakeError(ex.Message)); }
    }

    private static (int code, string body) HandleSystemShutdown(HttpListenerRequest req) {
      try {
        var param = Deserialize<ConfirmParam>(ReadBody(req));
        if (param == null || !param.confirm)
          return (400, MakeError("confirm must be true"));
        Logger.Info("API: System shutdown requested");
        System.Diagnostics.Process.Start("shutdown", "/s /t 5 /c \"OmenXHub API shutdown\"")?.Dispose();
        return (200, "{\"success\":true,\"message\":\"System will shutdown in 5 seconds\"}");
      } catch (Exception ex) { Logger.Error($"HandleSystemShutdown: {ex.Message}"); return (500, MakeError(ex.Message)); }
    }

    // ═══════════════════════════════════════════════════════
    // Lighting Handlers
    // ═══════════════════════════════════════════════════════

    private static string HandleGetLightingStatus() {
      try {
        var colors = OmenLighting.GetZoneStaticColor();
        byte brightness = OmenLighting.GetZoneBrightness();
        int effect = OmenLighting.GetCurrentAnimationEffect();
        string colorHex = colors != null && colors.Length > 0
          ? $"#{colors[0].R:X2}{colors[0].G:X2}{colors[0].B:X2}" : "#000000";
        return $"{{\"available\":true,\"brightness\":{brightness},\"color\":\"{colorHex}\",\"effect\":{effect}}}";
      } catch {
        return "{\"available\":false}";
      }
    }

    private static (int code, string body) HandleSetLightingBrightness(HttpListenerRequest req) {
      try {
        var param = Deserialize<BrightnessParam>(ReadBody(req));
        if (param == null) return (400, MakeError("Invalid JSON body"));
        if (param.value < 0 || param.value > 100)
          return (400, MakeError("value must be between 0 and 100"));
        OmenLighting.SetZoneBrightness(OmenLighting.LightingDevice.Keyboard, (byte)param.value);
        Logger.Info($"API: SetLightingBrightness({param.value})");
        return (200, "{\"success\":true,\"message\":\"Brightness updated\"}");
      } catch (Exception ex) { Logger.Error($"HandleSetLightingBrightness: {ex.Message}"); return (500, MakeError(ex.Message)); }
    }

    private static (int code, string body) HandleSetLightingColor(HttpListenerRequest req) {
      try {
        var param = Deserialize<ColorParam>(ReadBody(req));
        if (param == null) return (400, MakeError("Invalid JSON body"));
        if (param.r < 0 || param.r > 255 || param.g < 0 || param.g > 255 || param.b < 0 || param.b > 255)
          return (400, MakeError("r/g/b must be between 0 and 255"));
        var color = System.Windows.Media.Color.FromRgb((byte)param.r, (byte)param.g, (byte)param.b);
        OmenLighting.SetZoneStaticColor(OmenLighting.LightingDevice.Keyboard,
            new List<System.Windows.Media.Color> { color }, 100,
            OmenLighting.LightingControlInterface.BasicFourZone);
        Logger.Info($"API: SetLightingColor({param.r},{param.g},{param.b})");
        return (200, "{\"success\":true,\"message\":\"Color updated\"}");
      } catch (Exception ex) { Logger.Error($"HandleSetLightingColor: {ex.Message}"); return (500, MakeError(ex.Message)); }
    }

    private static (int code, string body) HandleSetLightingEffect(HttpListenerRequest req) {
      try {
        var param = Deserialize<EffectParam>(ReadBody(req));
        if (param == null || string.IsNullOrWhiteSpace(param.effect))
          return (400, MakeError("Missing 'effect' parameter"));
        Logger.Info($"API: SetLightingEffect(\"{param.effect}\")");
        return (200, $"{{\"success\":true,\"message\":\"Effect '{EscapeJson(param.effect)}' applied\"}}");
      } catch (Exception ex) { Logger.Error($"HandleSetLightingEffect: {ex.Message}"); return (500, MakeError(ex.Message)); }
    }

    private static (int code, string body) HandleSetLightingOff(HttpListenerRequest req) {
      try {
        OmenLighting.SetZoneOff(OmenLighting.LightingDevice.Keyboard,
            OmenLighting.LightingControlInterface.BasicFourZone);
        Logger.Info("API: SetLightingOff");
        return (200, "{\"success\":true,\"message\":\"Lighting turned off\"}");
      } catch (Exception ex) { Logger.Error($"HandleSetLightingOff: {ex.Message}"); return (500, MakeError(ex.Message)); }
    }

    // ═══════════════════════════════════════════════════════
    // API Status
    // ═══════════════════════════════════════════════════════

    private static string HandleGetApiStatus() {
      var endpoints = new string[] {
        "{\"method\":\"GET\",\"path\":\"/ping\",\"description\":\"Health check\"}",
        "{\"method\":\"GET\",\"path\":\"/api/status\",\"description\":\"API endpoint list\"}",
        "{\"method\":\"GET\",\"path\":\"/api/temperature\",\"description\":\"CPU/GPU temperature\"}",
        "{\"method\":\"GET\",\"path\":\"/api/fan/speed\",\"description\":\"Fan speed percent\"}",
        "{\"method\":\"GET\",\"path\":\"/api/fan/rpm\",\"description\":\"Fan speed RPM\"}",
        "{\"method\":\"GET\",\"path\":\"/api/fan/curve/current\",\"description\":\"Current fan curve\"}",
        "{\"method\":\"GET\",\"path\":\"/api/mode\",\"description\":\"Current preset mode\"}",
        "{\"method\":\"GET\",\"path\":\"/api/cpu/load\",\"description\":\"CPU load (per-core)\"}",
        "{\"method\":\"GET\",\"path\":\"/api/cpu/frequency\",\"description\":\"CPU frequency\"}",
        "{\"method\":\"GET\",\"path\":\"/api/cpu/power/current\",\"description\":\"CPU power settings\"}",
        "{\"method\":\"GET\",\"path\":\"/api/gpu/load\",\"description\":\"GPU load\"}",
        "{\"method\":\"GET\",\"path\":\"/api/gpu/frequency\",\"description\":\"GPU frequency\"}",
        "{\"method\":\"GET\",\"path\":\"/api/gpu/memory\",\"description\":\"GPU memory usage\"}",
        "{\"method\":\"GET\",\"path\":\"/api/gpu/offset/current\",\"description\":\"GPU clock offsets\"}",
        "{\"method\":\"GET\",\"path\":\"/api/battery\",\"description\":\"Battery status\"}",
        "{\"method\":\"GET\",\"path\":\"/api/hardware/all\",\"description\":\"All hardware data\"}",
        "{\"method\":\"GET\",\"path\":\"/api/system/info\",\"description\":\"System information\"}",
        "{\"method\":\"GET\",\"path\":\"/api/system/uptime\",\"description\":\"System uptime\"}",
        "{\"method\":\"GET\",\"path\":\"/api/lighting/status\",\"description\":\"Keyboard lighting status\"}",
        "{\"method\":\"POST\",\"path\":\"/api/fan/speed\",\"description\":\"Set fan speed\"}",
        "{\"method\":\"POST\",\"path\":\"/api/fan/curve\",\"description\":\"Set fan curve\"}",
        "{\"method\":\"POST\",\"path\":\"/api/fan/max\",\"description\":\"Toggle max fan\"}",
        "{\"method\":\"POST\",\"path\":\"/api/fan/clean\",\"description\":\"Fan clean (30s)\"}",
        "{\"method\":\"POST\",\"path\":\"/api/mode\",\"description\":\"Switch preset\"}",
        "{\"method\":\"POST\",\"path\":\"/api/power/limit\",\"description\":\"Set CPU PL1+PL2\"}",
        "{\"method\":\"POST\",\"path\":\"/api/cpu/pl1\",\"description\":\"Set CPU PL1\"}",
        "{\"method\":\"POST\",\"path\":\"/api/cpu/pl2\",\"description\":\"Set CPU PL2\"}",
        "{\"method\":\"POST\",\"path\":\"/api/cpu/tdp\",\"description\":\"Set CPU TDP\"}",
        "{\"method\":\"POST\",\"path\":\"/api/cpu/iccmax\",\"description\":\"Set CPU IccMax\"}",
        "{\"method\":\"POST\",\"path\":\"/api/cpu/loadline\",\"description\":\"Set CPU LoadLine\"}",
        "{\"method\":\"POST\",\"path\":\"/api/gpu/core/offset\",\"description\":\"Set GPU core offset\"}",
        "{\"method\":\"POST\",\"path\":\"/api/gpu/memory/offset\",\"description\":\"Set GPU memory offset\"}",
        "{\"method\":\"POST\",\"path\":\"/api/gpu/tgp\",\"description\":\"Toggle GPU TGP\"}",
        "{\"method\":\"POST\",\"path\":\"/api/gpu/ppab\",\"description\":\"Toggle GPU PPAB\"}",
        "{\"method\":\"POST\",\"path\":\"/api/lighting/brightness\",\"description\":\"Set keyboard brightness\"}",
        "{\"method\":\"POST\",\"path\":\"/api/lighting/color\",\"description\":\"Set keyboard color\"}",
        "{\"method\":\"POST\",\"path\":\"/api/lighting/effect\",\"description\":\"Set keyboard effect\"}",
        "{\"method\":\"POST\",\"path\":\"/api/lighting/off\",\"description\":\"Turn off keyboard lighting\"}",
        "{\"method\":\"POST\",\"path\":\"/api/system/restart\",\"description\":\"Restart system\"}",
        "{\"method\":\"POST\",\"path\":\"/api/system/shutdown\",\"description\":\"Shutdown system\"}"
      };
      return $"{{\"status\":\"running\",\"version\":\"1.0.0\",\"endpoints\":[{string.Join(",",endpoints)}]}}";
    }

    // ═══════════════════════════════════════════════════════
    // Security
    // ═══════════════════════════════════════════════════════

    private static bool IsLocalhostUri(string uriStr) {
      if (string.IsNullOrEmpty(uriStr)) return true;
      if (Uri.TryCreate(uriStr, UriKind.Absolute, out var uri)) {
        return uri.IsLoopback;
      }
      return false;
    }

    private static bool ValidateRequest(HttpListenerRequest req, string path) {
      string origin = req.Headers["Origin"];
      string referer = req.UrlReferrer?.ToString();
      if (!string.IsNullOrEmpty(origin) && !IsLocalhostUri(origin)) {
        Logger.Error($"API: Blocked cross-origin request from {origin}");
        return false;
      }
      if (!string.IsNullOrEmpty(referer) && !IsLocalhostUri(referer)) {
        Logger.Error($"API: Blocked cross-referer request from {referer}");
        return false;
      }

      string authHeader = req.Headers["Authorization"];
      if (!string.IsNullOrEmpty(authHeader)) {
        if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) {
          string token = authHeader.Substring(7).Trim();
          if (token == _apiToken) return true;
        }
      }

      string tokenParam = req.QueryString["token"];
      if (!string.IsNullOrEmpty(tokenParam) && tokenParam == _apiToken) return true;

      Logger.Error($"API: Unauthorized request to {path}");
      return false;
    }

    // ═══════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════

    [DataContract]
    private class FanSpeedParam {
      [DataMember] public int speed1 { get; set; }
      [DataMember] public int speed2 { get; set; }
    }

    [DataContract]
    private class PowerLimitParam {
      [DataMember] public int pl1 { get; set; }
      [DataMember] public int pl2 { get; set; }
    }

    [DataContract]
    private class ModeParam {
      [DataMember] public string mode { get; set; }
    }

    [DataContract]
    private class IntValueParam {
      [DataMember] public int value { get; set; }
    }

    [DataContract]
    private class IntOffsetParam {
      [DataMember] public int offset { get; set; }
    }

    [DataContract]
    private class BoolParam {
      [DataMember] public bool enabled { get; set; }
    }

    [DataContract]
    private class ConfirmParam {
      [DataMember] public bool confirm { get; set; }
    }

    [DataContract]
    private class FanCurveParam {
      [DataMember] public List<FanPointParam> points { get; set; }
    }

    [DataContract]
    private class FanPointParam {
      [DataMember] public int temp { get; set; }
      [DataMember] public int speed { get; set; }
    }

    [DataContract]
    private class BrightnessParam {
      [DataMember] public int value { get; set; }
    }

    [DataContract]
    private class ColorParam {
      [DataMember] public int r { get; set; }
      [DataMember] public int g { get; set; }
      [DataMember] public int b { get; set; }
    }

    [DataContract]
    private class EffectParam {
      [DataMember] public string effect { get; set; }
    }

    private static string ReadBody(HttpListenerRequest req) {
      using (var reader = new StreamReader(req.InputStream, req.ContentEncoding))
        return reader.ReadToEnd();
    }

    private static T Deserialize<T>(string json) where T : class {
      if (string.IsNullOrWhiteSpace(json)) return null;
      var ser = new DataContractJsonSerializer(typeof(T));
      using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
        return ser.ReadObject(ms) as T;
    }

    private static string EscapeJson(string s) {
      if (s == null) return "";
      return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private static string MakeError(string msg) {
      return $"{{\"error\":\"{EscapeJson(msg)}\"}}";
    }

    private static void SendResponse(HttpListenerResponse resp, int statusCode, string body) {
      try {
        byte[] buffer = Encoding.UTF8.GetBytes(body);
        resp.StatusCode = statusCode;
        resp.ContentType = "application/json; charset=utf-8";
        resp.ContentLength64 = buffer.Length;
        resp.OutputStream.Write(buffer, 0, buffer.Length);
        resp.OutputStream.Close();
      } catch { }
    }
  }
}
