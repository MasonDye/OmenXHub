// CleanupService.cs - 出厂重置：清理注册表/计划任务/配置文件/数据目录
// 由 PerfPage「恢复出厂」按钮调用。语义对齐 clean_oxh.bat，但跳过对自身进程的 taskkill。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using Microsoft.Win32.TaskScheduler;

namespace OmenSuperHub.Services {
  internal static class CleanupService {
    const string AppRegKey = @"Software\OmenXHub";
    const string AppDirName = "OmenXHub";

    // 执行全量清理。返回清理报告（每行一项）。
    public static List<string> RunAll() {
      var report = new List<string>();
      DeleteRegKey(CurrentUser, AppRegKey, report);                       // 含 Presets 子键
      DeleteRunValue(CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", AppDirName, report);
      DeleteRunValue(LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", AppDirName, report);
      DeleteRunValue(LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", AppDirName, report);
      DeleteRegKey(CurrentUser, @"Software\HWiNFO64\Sensors\Custom", report);
      DeleteTask("OmenXHub", report);
      DeleteTask("Omen Boot", report);
      DeleteGeneratedFiles(report);
      DeleteDir(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppDirName), report);
      DeleteDir(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppDirName), report);
      DeleteDir(@"C:\Program Files\OmenXHub", report);
      return report;
    }

    // ── 注册表 ──
    static RegistryKey CurrentUser => Registry.CurrentUser;
    static RegistryKey LocalMachine => Registry.LocalMachine;

    static void DeleteRegKey(RegistryKey root, string path, List<string> report) {
      try {
        using (var k = root.OpenSubKey(path))
          if (k == null) { report.Add($"  - 不存在：{root.Name}\\{path}，跳过。"); return; }
        root.DeleteSubKeyTree(path, false);
        report.Add($"  - 已删除：{root.Name}\\{path}");
      } catch (Exception ex) { report.Add($"  - [警告] 删除失败 {root.Name}\\{path}: {ex.Message}"); }
    }

    static void DeleteRunValue(RegistryKey root, string path, string value, List<string> report) {
      try {
        using (var k = root.OpenSubKey(path, writable: true)) {
          if (k?.GetValue(value) == null) { report.Add($"  - 不存在：{root.Name}\\...\\Run\\{value}，跳过。"); return; }
          k.DeleteValue(value, false);
        }
        report.Add($"  - 已删除：{root.Name}\\...\\Run\\{value}");
      } catch (Exception ex) { report.Add($"  - [警告] 删除 Run 值失败 {value}: {ex.Message}"); }
    }

    // ── 计划任务 ──
    static void DeleteTask(string name, List<string> report) {
      try {
        using (var ts = new TaskService()) {
          if (ts.FindTask(name) == null) { report.Add($"  - 不存在计划任务：{name}，跳过。"); return; }
          ts.RootFolder.DeleteTask(name);
          report.Add($"  - 已删除计划任务：{name}");
        }
      } catch (Exception ex) { report.Add($"  - [警告] 删除计划任务失败 {name}: {ex.Message}"); }
    }

    // ── 生成文件 ──
    static void DeleteGeneratedFiles(List<string> report) {
      string appDir = AppContext.BaseDirectory;
      foreach (var f in new[] { "silent.txt", "cool.txt", "balanced.txt", "error.log", "custom_icon.ico", "CoreKeep.json", "preset_names.txt" })
        DeleteFile(Path.Combine(appDir, f), report);
      DeletePattern(Path.Combine(appDir, "Presets"), "*.json", report);
      DeletePattern(Path.Combine(appDir, "FanCurves"), "custom*.txt", report);
      foreach (var f in new[] { "silent.txt", "cool.txt", "balanced.txt" })
        DeleteFile(Path.Combine(@"C:\Windows\SysWOW64", f), report);
    }

    static void DeleteFile(string path, List<string> report) {
      try {
        if (!File.Exists(path)) { report.Add($"  - 不存在：{path}，跳过。"); return; }
        File.Delete(path);
        report.Add($"  - 已删除：{path}");
      } catch (Exception ex) { report.Add($"  - [警告] 删除文件失败 {path}: {ex.Message}"); }
    }

    static void DeletePattern(string dir, string pattern, List<string> report) {
      try {
        if (!Directory.Exists(dir)) { report.Add($"  - 不存在：{dir}，跳过。"); return; }
        foreach (var f in Directory.GetFiles(dir, pattern)) {
          File.Delete(f);
        }
        report.Add($"  - 已清空：{dir}\\{pattern}");
      } catch (Exception ex) { report.Add($"  - [警告] 清空目录失败 {dir}: {ex.Message}"); }
    }

    static void DeleteDir(string path, List<string> report) {
      try {
        if (!Directory.Exists(path)) { report.Add($"  - 不存在目录：{path}，跳过。"); return; }
        Directory.Delete(path, recursive: true);
        report.Add($"  - 已删除目录：{path}");
      } catch (Exception ex) { report.Add($"  - [警告] 删除目录失败 {path}: {ex.Message}"); }
    }
  }
}
