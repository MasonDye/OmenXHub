// DiskCleaner.cs - 储存清理引擎 (参考 Dism++ 空间回收的分类项模型)
// 数据驱动的清理项定义 + 扫描(体积统计) + 执行(逐文件跳过占用),全部为安全通用系统垃圾类目
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace OmenSuperHub.Services {
  internal class CleanItem {
    public string Name;
    public string Description;
    public bool IsSelected;              // UI 勾选后由调用方写回, Clean() 依此过滤
    public bool IsRecycleBin;          // 特殊路径: SHEmptyRecycleBin
    // (根目录, 通配模式, 是否递归) 列表; null 模式 = 整个目录内容
    public List<Tuple<string, string, bool>> Targets = new();
    public long SizeBytes;
  }

  internal static class DiskCleaner {
    // ── 清理项定义 ── 参考 Dism++ Data.xml CleanCollection4 的系统级条目 ──
    internal static List<CleanItem> BuildItems() {
      var list = new List<CleanItem>();
      string WinDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
      string ProgData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
      string LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

      list.Add(new CleanItem {
        Name = Strings.StorageCleanTempSys,
        Description = Strings.StorageCleanTempSysDesc,
        Targets = { T(Path.Combine(WinDir, "Temp"), null, true) }
      });
      list.Add(new CleanItem {
        Name = Strings.StorageCleanTempUser,
        Description = Strings.StorageCleanTempUserDesc,
        Targets = { T(Path.GetTempPath(), null, true) }
      });
      list.Add(new CleanItem {
        Name = Strings.StorageCleanRecycleBin,
        Description = Strings.StorageCleanRecycleBinDesc,
        IsRecycleBin = true
      });
      list.Add(new CleanItem {
        Name = Strings.StorageCleanWuCache,
        Description = Strings.StorageCleanWuCacheDesc,
        Targets = { T(Path.Combine(WinDir, "SoftwareDistribution", "Download"), null, true) }
      });
      list.Add(new CleanItem {
        Name = Strings.StorageCleanDosCache,
        Description = Strings.StorageCleanDosCacheDesc,
        Targets = { T(Path.Combine(WinDir, "ServiceProfiles", "NetworkService",
            "AppData", "Local", "Microsoft", "Windows", "DeliveryOptimization", "Cache"), null, true) }
      });
      list.Add(new CleanItem {
        Name = Strings.StorageCleanDumps,
        Description = Strings.StorageCleanDumpsDesc,
        Targets = {
          T(Path.Combine(LocalAppData, "CrashDumps"), null, true),
          T(Path.Combine(WinDir, "Minidump"), null, true),
          T(WinDir, "MEMORY.DMP", false),
        }
      });
      list.Add(new CleanItem {
        Name = Strings.StorageCleanWer,
        Description = Strings.StorageCleanWerDesc,
        Targets = { T(Path.Combine(ProgData, "Microsoft", "Windows", "WER"), null, true) }
      });
      list.Add(new CleanItem {
        Name = Strings.StorageCleanThumb,
        Description = Strings.StorageCleanThumbDesc,
        Targets = {
          T(Path.Combine(LocalAppData, "Microsoft", "Windows", "Explorer"), "thumbcache_*.db", false),
          T(Path.Combine(LocalAppData, "Microsoft", "Windows", "Explorer"), "iconcache_*.db", false),
        }
      });
      return list;
    }

    static Tuple<string, string, bool> T(string root, string pattern, bool recursive)
      => Tuple.Create(root ?? "", pattern, recursive);

    /// <summary>扫描各项目体积。后台线程调用。</summary>
    internal static void Scan(List<CleanItem> items) {
      foreach (var it in items) {
        long total = 0;
        if (it.IsRecycleBin) {
          foreach (DriveInfo d in SafeDrives()) total += DirSize(Path.Combine(d.Name, "$Recycle.Bin"));
        } else {
          foreach (var t in it.Targets) total += MatchedSize(t.Item1, t.Item2, t.Item3);
        }
        it.SizeBytes = total;
      }
    }

    /// <summary>执行清理,返回释放字节数。后台线程调用。占用中的文件自动跳过。</summary>
    internal static long Clean(IEnumerable<CleanItem> items) {
      long freed = 0;
      foreach (var it in items) {
        if (!it.IsSelected) continue;
        try {
          if (it.IsRecycleBin) {
            freed += EmptyRecycleBins();
            continue;
          }
          foreach (var t in it.Targets) {
            string root = Guard(t.Item1);
            if (root == null) continue;
            freed += DeleteChildren(root, t.Item2, t.Item3);
          }
        } catch { }
      }
      return freed;
    }

    // ── 内部实现 ──

    static IEnumerable<DriveInfo> SafeDrives() {
      try { return DriveInfo.GetDrives(); } catch { return Array.Empty<DriveInfo>(); }
    }

    static long EmptyRecycleBins() {
      long before = 0;
      foreach (DriveInfo d in SafeDrives()) before += DirSize(Path.Combine(d.Name, "$Recycle.Bin"));
      // SHERB_NOCONFIRMATION|NOPROGRESSUI|NOSOUND — 静默清空所有盘回收站
      int hr = SHEmptyRecycleBin(IntPtr.Zero, null, 0x1 | 0x2 | 0x4);
      if (hr != 0) return 0;
      long after = 0;
      foreach (DriveInfo d in SafeDrives()) after += DirSize(Path.Combine(d.Name, "$Recycle.Bin"));
      return Math.Max(0, before - after);
    }

    [DllImport("shell32.dll")]
    static extern int SHEmptyRecycleBin(IntPtr hwnd, string rootPath, uint flags);

    // ponytail: 路径守卫 — 只允许清理解析后长度足够且非盘根的目录,防误删。
    // 升级路径: 若未来加入用户自定义规则,这里需追加白名单校验。
    static string Guard(string path) {
      try {
        string full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path ?? ""))
                          .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (full.Length < 6) return null;                       // 排除 "C:\" 类根目录
        if (Directory.Exists(full)) return full;
        return null;
      } catch { return null; }
    }

    static long MatchedSize(string root, string pattern, bool recursive) {
      string r = Guard(root);
      if (r == null) return 0;
      long sum = 0;
      var di = new DirectoryInfo(r);
      try {
        if (pattern != null) {
          foreach (FileInfo f in di.EnumerateFiles(pattern, recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly))
            sum += TryLen(f);
        } else if (recursive) {
          sum += DirSize(r);
        } else {
          foreach (FileInfo f in di.EnumerateFiles()) sum += TryLen(f);
        }
      } catch { }
      return sum;
    }

    static long DeleteChildren(string root, string pattern, bool recursive) {
      long freed = 0;
      var di = new DirectoryInfo(root);
      // 文件通配模式: 只删匹配文件
      if (pattern != null) {
        try {
          foreach (FileInfo f in di.EnumerateFiles(pattern, recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)) {
            long len = TryLen(f);
            if (TryDelete(f.FullName)) freed += len;
          }
        } catch { }
        return freed;
      }
      // 目录内容模式: 先删子目录树再删散文件,保留根目录本身(避免运行中程序句柄失效)
      try {
        foreach (DirectoryInfo sub in di.EnumerateDirectories())
          freed += DeleteTree(sub);
      } catch { }
      try {
        foreach (FileInfo f in di.EnumerateFiles()) {
          long len = TryLen(f);
          if (TryDelete(f.FullName)) freed += len;
        }
      } catch { }
      return freed;
    }

    static long DeleteTree(DirectoryInfo dir) {
      long freed = 0;
      try {
        foreach (DirectoryInfo sub in dir.EnumerateDirectories())
          freed += DeleteTree(sub);
      } catch { }
      try {
        foreach (FileInfo f in dir.EnumerateFiles()) {
          long len = TryLen(f);
          if (TryDelete(f.FullName)) freed += len;
        }
      } catch { }
      try { if (dir.EnumerateFileSystemInfoCount() == 0) dir.Delete(); } catch { }
      return freed;
    }

    static long TryLen(FileInfo f) { try { return f.Length; } catch { return 0; } }
    static bool TryDelete(string path) {
      try { File.Delete(path); return true; } catch { return false; }
    }

    static long DirSize(string dir) {
      if (string.IsNullOrEmpty(dir)) return 0;
      long sum = 0;
      try {
        var di = new DirectoryInfo(dir);
        foreach (FileInfo f in di.EnumerateFiles("*", SearchOption.AllDirectories))
          sum += TryLen(f);
      } catch { }
      return sum;
    }

    internal static string FmtBytes(long b) {
      double v = b; string unit = " B";
      if (v >= 1024) { v /= 1024; unit = " KB"; }
      if (v >= 1024) { v /= 1024; unit = " MB"; }
      if (v >= 1024) { v /= 1024; unit = " GB"; }
      return $"{v:0.#}{unit}";
    }

    // ── 自检 (--selftest 链路): 守卫逻辑 + 字节格式化 ──
    public static string SelfCheck() {
      string ok = "[DiskCleaner] PASS";
      try {
        // 盘根必须被拒
        if (Guard("C:\\") != null) return "[DiskCleaner] FAIL: drive root not rejected";
        // 不存在的目录必须被拒
        if (Guard(@"C:\this_dir_should_not_exist_xyz\") != null) return "[DiskCleaner] FAIL: missing dir not rejected";
        // 存在的真实目录应通过
        string tmp = Path.GetTempPath().TrimEnd('\\');
        if (Guard(tmp + "\\") == null) return "[DiskCleaner] FAIL: real dir rejected";
        // 格式化边界
        if (FmtBytes(1023) != "1023 B") return "[DiskCleaner] FAIL: fmt 1023";
        if (FmtBytes(1024) != "1 KB") return "[DiskCleaner] FAIL: fmt 1KB";
        if (FmtBytes((long)(1.5 * 1024 * 1024)) != "1.5 MB") return "[DiskCleaner] FAIL: fmt 1.5MB";
      } catch (Exception ex) { return "[DiskCleaner] FAIL: " + ex.Message; }
      return ok;
    }
  }

  internal static class DirectoryInfoExt {
    public static int EnumerateFileSystemInfoCount(this DirectoryInfo d) {
      int n = 0;
      foreach (var _ in d.EnumerateFileSystemInfos()) n++;
      return n;
    }
  }
}
