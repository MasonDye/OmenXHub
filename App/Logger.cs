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
      // R15/BUG-R15-9: 同 WriteToFile —— 日志写失败不得成为崩溃源。
      try {
        lock (FileLock) {
          File.AppendAllText(LogPath,
              $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [VERBOSE] {message}{Environment.NewLine}");
        }
      } catch { }
    }

    private static void WriteToFile(string line) {
      // R15/BUG-R15-9: 本方法被 DispatcherUnhandledException / UnhandledException 处理器调用。
      // 若日志文件被占用/目录只读,File.AppendAllText 抛异常会从异常处理器内再抛出 →
      // args.Handled 来不及置位 → 进程崩溃。日志是尽力而为的诊断手段,写失败必须静默降级,
      // 绝不能反过来成为崩溃源。
      try {
        lock (FileLock) {
          if (line == lastMessage &&
              (DateTime.Now - lastWriteTime).TotalSeconds < ThrottleSeconds)
            return;
          lastMessage = line;
          lastWriteTime = DateTime.Now;
          // ponytail: 单代滚动 —— 1s tick 的交替报错/Verbose 开关会让 append 无界增长
          // (30s 去重只拦连续相同消息)。超 2MB 归档 .old(覆盖旧档);竞争窗口无所谓,
          // 同进程持锁,外部挪动文件最坏只是该行写入失败被外层 catch 吃掉。
          try {
            var info = new FileInfo(LogPath);
            if (info.Exists && info.Length > 2 * 1024 * 1024) {
              string old = LogPath + ".old";
              if (File.Exists(old)) File.Delete(old);
              File.Move(LogPath, old);
            }
          } catch { }
          File.AppendAllText(LogPath,
              $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {line}{Environment.NewLine}");
        }
      } catch { /* 日志不可写:静默丢弃,不影响调用方(可能是崩溃处理器) */ }
    }
  }
}
