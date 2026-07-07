// AdlxGpuService.cs - ADLX / AMD GPU 驱动层调优
// Provides RSR, Anti-Lag, Boost, Enhanced Sync, Image Sharpening, Chill, FPS Limit
// for AMD Radeon GPUs (dGPU). Replaces the empty AdlxCard placeholder with real controls.
//
// DLL references (ADLX_AutoTuning.dll / ADLX_DisplaySettings.dll / ADLX_PerformanceMetrics.dll)
// come from AMD's ADLX SDK; copied as unmanaged DllImport targets. Calling conventions
// mirror UXTU's Cdecl-pinned ADLXBackend — see https://github.com/U-xTU/Universal-x86-Tuning-Utility.
using System;
using System.Runtime.InteropServices;

namespace OmenSuperHub.Services {

  /// <summary>
  /// AMD GPU driver-level controls via ADLX. Detection is lazy and graceful —
  /// every public method tolerates a missing/unsupported driver by returning -1
  /// (callers treat negative as "not supported" and skip silently).
  /// </summary>
  internal static class AdlxGpuService {

    public static bool IsAvailable {
      get {
        if (_discovered) return _rngOk;
        _discovered = true;
        try {
          // Probe with a harmless call. If the DLL or driver is missing,
          // the DllImport throws DLLNotFoundException / EntryPointNotFoundException —
          // we catch everything and mark the service unavailable.
          int gpu = GetPrimaryGpuIndex();
          _rngOk = gpu >= 0;
        } catch {
          _rngOk = false;
        }
        return _rngOk;
      }
    }

    static bool _discovered;
    static bool _rngOk;

    // ── FFI (mirrors ADLXBackend signatures from UXTU) ──

    const string ADLX_3D = "ADLX_DisplaySettings.dll";
    const string ADLX_PM = "ADLX_PerformanceMetrics.dll";
    const string ADLX_AT = "ADLX_AutoTuning.dll";

    [DllImport(ADLX_PM, CallingConvention = CallingConvention.Cdecl)]
    static extern int GetFPSData();

    [DllImport(ADLX_PM, CallingConvention = CallingConvention.Cdecl)]
    static extern int GetGPUMetrics(int gpu, int sensor);

    [DllImport(ADLX_3D, CallingConvention = CallingConvention.Cdecl)]
    static extern int SetFPSLimit(int gpu, bool enabled, int fps);

    [DllImport(ADLX_3D, CallingConvention = CallingConvention.Cdecl)]
    static extern int SetAntiLag(int gpu, bool enabled);

    [DllImport(ADLX_3D, CallingConvention = CallingConvention.Cdecl)]
    static extern int SetBoost(int gpu, bool enabled, int percent);

    [DllImport(ADLX_3D, CallingConvention = CallingConvention.Cdecl)]
    static extern int SetRSR(bool enabled);

    [DllImport(ADLX_3D, CallingConvention = CallingConvention.Cdecl)]
    static extern int GetRSRState();

    [DllImport(ADLX_3D, CallingConvention = CallingConvention.Cdecl)]
    static extern bool SetRSRSharpness(int sharpness);

    [DllImport(ADLX_3D, CallingConvention = CallingConvention.Cdecl)]
    static extern int GetRSRSharpness();

    [DllImport(ADLX_3D, CallingConvention = CallingConvention.Cdecl)]
    static extern int SetChill(int gpu, bool enabled, int maxFps, int minFps);

    [DllImport(ADLX_3D, CallingConvention = CallingConvention.Cdecl)]
    static extern int SetImageSharpning(int gpu, bool enabled, int percent);

    [DllImport(ADLX_3D, CallingConvention = CallingConvention.Cdecl)]
    static extern int SetEnhancedSync(int gpu, bool enabled);

    [DllImport(ADLX_AT, CallingConvention = CallingConvention.Cdecl)]
    static extern int GetPrimaryGpuIndex();

    // ── High-level wrappers (one-line, error-swallowing) ──

    static int SafeGpu() {
      try { return GetPrimaryGpuIndex(); } catch { return -1; }
    }

    public static int EnableRsr(bool on) {
      try { SetRSR(on); return GetRSRState(); } catch { return -1; }
    }

    public static int SetRsrSharpness(int sharpness) {
      try { return SetRSRSharpness(sharpness) ? GetRSRSharpness() : -1; } catch { return -1; }
    }

    public static int EnableAntiLag(bool on) {
      int g = SafeGpu(); if (g < 0) return -1;
      try { return SetAntiLag(g, on); } catch { return -1; }
    }

    public static int EnableBoost(bool on, int percent) {
      int g = SafeGpu(); if (g < 0) return -1;
      try { return SetBoost(g, on, percent); } catch { return -1; }
    }

    public static int EnableEnhancedSync(bool on) {
      int g = SafeGpu(); if (g < 0) return -1;
      try { return SetEnhancedSync(g, on); } catch { return -1; }
    }

    public static int EnableImageSharpening(bool on, int percent) {
      int g = SafeGpu(); if (g < 0) return -1;
      try { return SetImageSharpning(g, on, percent); } catch { return -1; }
    }

    public static int EnableChill(bool on, int maxFps, int minFps) {
      int g = SafeGpu(); if (g < 0) return -1;
      try { return SetChill(g, on, maxFps, minFps); } catch { return -1; }
    }

    public static int SetFpsLimit(bool on, int fps) {
      int g = SafeGpu(); if (g < 0) return -1;
      try { return SetFPSLimit(g, on, fps); } catch { return -1; }
    }

    public static int PollFps() {
      try { return GetFPSData(); } catch { return -1; }
    }

    // sensor: 0=Temperature, 1=Hotspot, 2=Power, 3=FanSpeed, ...
    public static int PollSensor(int sensor) {
      int g = SafeGpu(); if (g < 0) return -1;
      try { return GetGPUMetrics(g, sensor); } catch { return -1; }
    }
  }
}
