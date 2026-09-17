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
          // ponytail: 旧实现 `if (File.Exists(dest)) continue` 会把解压中途被杀留下的截断
          // exe 永久粘住(之后每次启动都跳过重提取)。改为 sidecar 记录完整落盘的字节数,
          // 尺寸不符/无 sidecar 即重新提取;提取写 temp 后原子换入,dest 路径上只可能出现
          // 完整文件。上限: 只防截断不防篡改(资源哈希比对见 InstallPawnIO,43MB 每次全
          // 提取代价不成比例)。
          if (File.Exists(dest) && SizeMatches(dest)) continue;
          var rn = Array.Find(asm.GetManifestResourceNames(),
            r => r.EndsWith(name + ".gz", StringComparison.OrdinalIgnoreCase));
          bool isGz = rn != null;
          if (!isGz) {
            rn = Array.Find(asm.GetManifestResourceNames(),
              r => r.EndsWith(name, StringComparison.OrdinalIgnoreCase));
          }
          if (rn == null) continue;
          string tmp = dest + ".tmp";
          try {
            using (var s = asm.GetManifestResourceStream(rn))
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write)) {
              if (isGz) {
                using (var gz = new System.IO.Compression.GZipStream(s, System.IO.Compression.CompressionMode.Decompress))
                  gz.CopyTo(fs);
              } else {
                s.CopyTo(fs);
              }
            }
            long written = new FileInfo(tmp).Length;
            if (File.Exists(dest)) File.Replace(tmp, dest, null);
            else File.Move(tmp, dest);
            // sidecar 最后写: 换入成功但 sidecar 失败时,下次启动重提取一次即可(幂等,不粘滞)
            File.WriteAllText(dest + ".size", written.ToString());
          } catch {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            throw;
          }
        }
      } catch (Exception ex) { Logger.Warn("[TunManager] EnsureBinaries: " + ex.Message); }
    }

    static bool SizeMatches(string dest) {
      try {
        return long.TryParse(File.ReadAllText(dest + ".size"), out long sz)
               && sz > 0 && new FileInfo(dest).Length == sz;
      } catch { return false; }
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
          try { _proc.Dispose(); } catch { }
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
      try { _proc?.Dispose(); } catch { }
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
