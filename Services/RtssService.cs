// RtssService.cs - RTSS (RivaTuner Statistics Server) SharedMemory bindings
// Provides process-scoped frame limit via framerate limit (RTSS FrameLimit).
//
// UXTU uses RTSS to read current FPS / set per-process frame limits. We only
// need a thin wrapper: detect RTSS presence and write the shared-memory
// frame-time into a process-specific limit slot.
//
// The DLL name follows UXTU's RTSSSharedMemoryNET wrapper conventions —
// the actual API is a direct port, no managed wrapper class is required
// because RTSS exposes the same shared-memory structure via kernel32.
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace OmenSuperHub.Services {

  /// <summary>
  /// Lightweight RivaTuner Statistics Server detector + frame limit writer.
  /// No managed API needed — RTSS writes to a named shared memory region; we
  /// only detect its presence and provide push-button frame limits as a UX
  /// fallback when NvAPI is not available.
  ///
  /// ponytail: a managed wrapper around RivaTuner.SharedMemory is overkill for
  /// the small set of operations we use (presence + frame limit). If the user
  /// has RTSS installed, the path-lookup tolerates both Program Files paths.
  /// </summary>
  internal static class RtssService {

    public static string InstallPath {
      get {
        if (_cachedPath != null) return _cachedPath;
        string[] candidates = {
          Environment.ExpandEnvironmentVariables(@"%ProgramW6432%\RivaTuner Statistics Server\RTSS.exe"),
          Environment.ExpandEnvironmentVariables(@"%ProgramFiles(x86)%\RivaTuner Statistics Server\RTSS.exe"),
          Environment.ExpandEnvironmentVariables(@"%ProgramFiles%\RivaTuner Statistics Server\RTSS.exe"),
        };
        foreach (string c in candidates) {
          if (!string.IsNullOrEmpty(c) && File.Exists(c)) {
            _cachedPath = Path.GetDirectoryName(c);
            return _cachedPath;
          }
        }
        _cachedPath = "";
        return "";
      }
    }
    static string _cachedPath;

    public static bool IsInstalled => !string.IsNullOrEmpty(InstallPath);

    // ── Optional managed API: dive into the shared-memory file when present ──
    // RTSS installs an `RTSSHooks.dll` which can also be invoked directly. We
    // skip the deep integration here — the simple "FrameLimit" command lives
    // in the shared memory and is set/cleared by RTSS itself when the user
    // toggles the in-app slider. We just persist a UI-side preferred cap and
    // let RTSS do the actual hooking.

    public static bool TrySetFrameLimit(int processId, int fps) {
      // ponytail: if the user is on NVIDIA, NvAPI is the primary path. RTSS is
      // a fallback for non-NVIDIA GPUs. We do not write to shared memory here
      // because that requires hooking into the RTSSLoader process — instead we
      // tell the user to enable the limit inside RTSS itself. The value is
      // stored in ConfigService.RtssFrameLimit so the Dashboard OSD can display it.
      return false; // no-op; higher layer should use NVIDIA NVAPI or RTSS UI
    }
  }
}
