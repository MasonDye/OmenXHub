using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;

namespace OmenSuperHub.Services.NetworkBoost {
  /// <summary>启动/停止 sing-box TUN 进程，并清理 TUN 残留默认路由。</summary>
  internal static class TunManager {
    public static string BinDir => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin");

    // ponytail: sing-box.exe 无 Authenticode 签名，不能走 ExtractAndPreloadNativeDll（会验签失败删文件）。
    // 单独提取到 bin\ 目录，wintun.dll 放同目录让 sing-box 加载。
    // ponytail: 嵌入资源为 .gz 压缩形式（sing-box 43MB→16MB），解压后写入 bin\。
    static readonly string[] _binaries = { "sing-box.exe", "wintun.dll" };

    public static void EnsureBinaries() {
      try {
        if (!Directory.Exists(BinDir)) Directory.CreateDirectory(BinDir);
        var asm = Assembly.GetExecutingAssembly();
        foreach (var name in _binaries) {
          string dest = Path.Combine(BinDir, name);
          if (File.Exists(dest)) continue;
          // ponytail: 资源名后缀 .gz；找不到 .gz 时回退原始未压缩资源（兼容旧构建产物）
          var rn = Array.Find(asm.GetManifestResourceNames(),
            r => r.EndsWith(name + ".gz", StringComparison.OrdinalIgnoreCase));
          bool isGz = rn != null;
          if (!isGz) {
            rn = Array.Find(asm.GetManifestResourceNames(),
              r => r.EndsWith(name, StringComparison.OrdinalIgnoreCase));
          }
          if (rn == null) continue;
          using (var s = asm.GetManifestResourceStream(rn))
          using (var fs = new FileStream(dest, FileMode.Create, FileAccess.Write)) {
            if (isGz) {
              using (var gz = new System.IO.Compression.GZipStream(s, System.IO.Compression.CompressionMode.Decompress))
                gz.CopyTo(fs);
            } else {
              s.CopyTo(fs);
            }
          }
        }
      } catch { }
    }

    public static string ConfigPath {
      get {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OmenXHub");
        return Path.Combine(dir, "singbox-config.json");
      }
    }

    static Process _proc;

    public static bool IsRunning {
      get { try { return _proc != null && !_proc.HasExited; } catch { return false; } }
    }

    public static bool Start(string configPath, out string error) {
      error = "";
      EnsureBinaries();
      string exe = Path.Combine(BinDir, "sing-box.exe");
      if (!File.Exists(exe)) { error = Strings.BoostSingboxMissing; return false; }
      try {
        var psi = new ProcessStartInfo {
          FileName = exe,
          Arguments = "run -c \"" + configPath + "\"",
          WorkingDirectory = BinDir,
          UseShellExecute = false,
          CreateNoWindow = true,
          WindowStyle = ProcessWindowStyle.Hidden
        };
        _proc = Process.Start(psi);
        Thread.Sleep(1500); // STARTUP_STABLE_DELAY
        if (_proc.HasExited) {
          error = "sing-box exited: code " + _proc.ExitCode;
          _proc = null;
          return false;
        }
        return true;
      } catch (Exception ex) {
        error = ex.Message;
        return false;
      }
    }

    public static void Stop() {
      try {
        if (_proc != null && !_proc.HasExited) {
          _proc.Kill();
          _proc.WaitForExit(3000);
        }
      } catch { }
      _proc = null;
      try {
        var psi = new ProcessStartInfo("route.exe", "delete 0.0.0.0 mask 0.0.0.0 " + SingboxConfigGenerator.TunGateway) {
          UseShellExecute = false, CreateNoWindow = true
        };
        using (var p = Process.Start(psi)) { if (p != null) p.WaitForExit(3000); }
      } catch { }
    }
  }
}
