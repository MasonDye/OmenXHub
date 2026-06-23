// Logger.cs - 日志记录器
// 写入 OmenXHub.log 文件，支持 Info/Warn/Error/Verbose 级别，30秒去重节流
using System;
using System.IO;

namespace OmenSuperHub {
  public static class Logger {
    public static readonly string logFileName = "OmenXHub.log";
    private static readonly string LogPath
        = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, logFileName);
    private static readonly object FileLock = new object();
    private static string lastMessage = "";
    private static DateTime lastWriteTime = DateTime.MinValue;
    private const int ThrottleSeconds = 30;

    public static void Info(string message) {
      Console.WriteLine(message);
      WriteToFile(message);
    }

    public static void Warn(string message) {
      Console.WriteLine("[WARN] " + message);
      WriteToFile($"[WARN] {message}");
    }

    public static void Error(string message) {
      Console.WriteLine(message);
      WriteToFile($"[ERROR] {message}");
    }

    public static void Verbose(string message) {
      if (!Services.ConfigService.VerboseLogging) return;
      Console.WriteLine("[VERBOSE] " + message);
      lock (FileLock) {
        File.AppendAllText(LogPath,
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [VERBOSE] {message}{Environment.NewLine}");
      }
    }

    private static void WriteToFile(string line) {
      if (line == lastMessage &&
          (DateTime.Now - lastWriteTime).TotalSeconds < ThrottleSeconds)
        return;
      lastMessage = line;
      lastWriteTime = DateTime.Now;
      lock (FileLock) {
        File.AppendAllText(LogPath,
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {line}{Environment.NewLine}");
      }
    }
  }
}
