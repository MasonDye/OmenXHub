// StartupItemOptimizer.cs - 启动项管理
// 禁用即物理搬迁：Run/RunOnce → RunDisabled/RunOnceDisabled；Startup 文件夹 → Startup\Disabled。
// 启用反向。被搬走的项再枚举时 is_disabled=true，UI 与实际一致。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace OmenSuperHub.Services.SystemOptimization {

  public enum StartupRegistryValueKind {
    String = 0,
    ExpandString = 1
  }

  public sealed class StartupItem {
    public string Id { get; set; }
    public string Name { get; set; }
    public string Command { get; set; }
    /// <summary>来源显示名（当前用户 / 本机 64 位 / 本机 32 位 / 启动文件夹 / …）。</summary>
    public string Location { get; set; }
    public bool IsEnabled { get; set; }
    public StartupRegistryValueKind ValueKind { get; set; }
    // 搬迁式禁用需要标记来源类型，启用时原路写回。
    public StartupItemType ItemType { get; set; }
    // Registry 项的原始 + 禁用子键路径（相对根），启用时按对称规则反推。
    public string RegKeyPath { get; set; }
    // Folder 项的文件绝对路径（已搬迁后指向 Disabled\ 下的副本）。
    public string FolderPath { get; set; }
    public string HiveId { get; set; }   // hku / hklm64 / hklm32（仅 Registry 项）
  }

  public enum StartupItemType { Registry, Folder }

  public static class StartupItemOptimizer {

    const string RunSubKey     = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
    const string RunOnceSubKey = "Software\\Microsoft\\Windows\\CurrentVersion\\RunOnce";
    const string WowPrefix     = "Software\\Wow6432Node\\Microsoft\\Windows\\CurrentVersion\\";

    // ── RunLocation ──
    // DisplayName 同时承担 UI 徽标和禁用子键命名；Enumerate 直接显示。
    public sealed class RunLocation {
      public string Id;
      public RegistryHive Hive;
      public RegistryView View;
      public string SubKey;
      public string DisplayName;
      public RunLocation(string id, RegistryHive hive, RegistryView view, string subKey, string display) {
        Id = id; Hive = hive; View = view; SubKey = subKey; DisplayName = display;
      }
    }

    static readonly RunLocation[] RegistryLocations = {
      new RunLocation("hku",    RegistryHive.CurrentUser,  RegistryView.Default,    RunSubKey,     "当前用户 Run"),
      new RunLocation("hkuOnce",RegistryHive.CurrentUser,  RegistryView.Default,    RunOnceSubKey, "当前用户 RunOnce"),
      new RunLocation("hklm64", RegistryHive.LocalMachine, RegistryView.Registry64, RunSubKey,     "本机 (64 位) Run"),
      new RunLocation("hklm64Once", RegistryHive.LocalMachine, RegistryView.Registry64, RunOnceSubKey, "本机 (64 位) RunOnce"),
      new RunLocation("hklm32", RegistryHive.LocalMachine, RegistryView.Registry32, RunSubKey,     "本机 (32 位) Run"),
      new RunLocation("hklm32Once", RegistryHive.LocalMachine, RegistryView.Registry32, RunOnceSubKey, "本机 (32 位) RunOnce"),
    };

    internal static string DisabledSubKey(string subKey) =>
      subKey.Contains("RunOnce") ? subKey.Replace("RunOnce", "RunOnceDisabled")
           : subKey.Replace("Run", "RunDisabled");
    internal static string EnabledSubKey(string subKey) =>
      subKey.Contains("RunOnceDisabled") ? subKey.Replace("RunOnceDisabled", "RunOnce")
           : subKey.Replace("RunDisabled", "Run");

    static string[] StartupFolders() {
      var list = new List<string>(2);
      try {
        string cu = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        if (!string.IsNullOrEmpty(cu)) list.Add(cu);
      } catch { }
      try {
        string lm = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup);
        if (!string.IsNullOrEmpty(lm)) list.Add(lm);
      } catch { }
      return list.ToArray();
    }

    static readonly string[] FolderItemExts = { ".exe", ".bat", ".cmd", ".lnk" };

    // ── 枚举 ──

    public static List<StartupItem> Enumerate() {
      var dict = new Dictionary<string, StartupItem>(StringComparer.OrdinalIgnoreCase);

      foreach (var loc in RegistryLocations) {
        using (var baseKey = RegistryKey.OpenBaseKey(loc.Hive, loc.View))
        using (var runKey = baseKey.OpenSubKey(loc.SubKey, false)) {
          if (runKey != null)
            foreach (string valueName in runKey.GetValueNames()) {
              var item = ReadRunValue(runKey, loc, valueName);
              if (item != null && !IsInvalidCommand(item.Command))
                dict[item.Id] = item;
            }
        }
        // 同源的禁用子键：搬过去的项，is_disabled=true，登记时仍用原 location 名以保持 UI 一致
        string disSubKey = DisabledSubKey(loc.SubKey);
        using (var baseKey = RegistryKey.OpenBaseKey(loc.Hive, loc.View))
        using (var disKey = baseKey.OpenSubKey(disSubKey, false)) {
          if (disKey != null)
            foreach (string valueName in disKey.GetValueNames()) {
              var item = ReadRunValue(disKey, loc, valueName, isDisabled: true, effectiveSubKey: loc.SubKey);
              if (item != null && !IsInvalidCommand(item.Command) && !dict.ContainsKey(item.Id))
                dict[item.Id] = item;
            }
        }
      }

      // 两个 Startup 文件夹（活动 + Disabled\ 子目录）
      foreach (string folder in StartupFolders()) {
        ScanFolder(folder, fmtFolderDisplay(folder, disabled: false), dict);
        ScanFolder(Path.Combine(folder, "Disabled"), fmtFolderDisplay(folder, disabled: true), dict);
      }

      return dict.Values.OrderBy(i =>
        (i.ItemType == StartupItemType.Registry ? "0" : "1") + i.Name,
        StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    static string fmtFolderDisplay(string folder, bool disabled) {
      string scope = folder.IndexOf("CommonStartup", StringComparison.OrdinalIgnoreCase) >= 0 ? "所有用户" : "当前用户";
      return scope + (disabled ? " 启动文件夹 (已禁用)" : " 启动文件夹");
    }

    static void ScanFolder(string dir, string display, Dictionary<string, StartupItem> dict) {
      var files = SafeEnumerateFiles(dir);
      foreach (string f in files) {
        string ext = Path.GetExtension(f) ?? "";
        if (Array.IndexOf(FolderItemExts, ext.ToLowerInvariant()) < 0) continue;
        string name = Path.GetFileName(f);
        var item = new StartupItem {
          Id = FolderId(f),
          Name = name,
          Command = ShortcutResolve.RootTargetOrDefault(f, displayFn: null),
          Location = display,
          IsEnabled = CallbackPathDisablingIsEnabled(display),
          ItemType = StartupItemType.Folder,
          FolderPath = f,
        };
        if (item.IsEnabled && IsInvalidCommand(item.Command)) continue;
        if (!dict.ContainsKey(item.Id)) dict[item.Id] = item;
      }
    }

    static bool CallbackPathDisablingIsEnabled(string display) =>
      !display.Contains("(已禁用)");

    static string[] SafeEnumerateFiles(string dir) {
      try { return Directory.GetFiles(dir); } catch { return Array.Empty<string>(); }
    }

    static StartupItem ReadRunValue(RegistryKey key, RunLocation loc, string valueName)
      => ReadRunValue(key, loc, valueName, isDisabled: false, effectiveSubKey: loc.SubKey);

    static StartupItem ReadRunValue(RegistryKey key, RunLocation loc, string valueName, bool isDisabled, string effectiveSubKey) {
      string real = key.GetValueNames().FirstOrDefault(n => string.Equals(n, valueName, StringComparison.OrdinalIgnoreCase));
      if (real == null) return null;
      RegistryValueKind kind = key.GetValueKind(real);
      if (kind != RegistryValueKind.String && kind != RegistryValueKind.ExpandString) return null;
      string command = key.GetValue(real, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
      if (command == null) return null;
      return new StartupItem {
        Id = MakeId(loc.Id, real),
        Name = real,
        Command = command,
        Location = loc.DisplayName,
        IsEnabled = !isDisabled,
        ValueKind = kind == RegistryValueKind.ExpandString ? StartupRegistryValueKind.ExpandString : StartupRegistryValueKind.String,
        ItemType = StartupItemType.Registry,
        RegKeyPath = effectiveSubKey,
        HiveId = loc.Id,
      };
    }

    // ── 启停 ──

    /// <summary>启用/禁用启动项。Registry 项在 Run↔RunDisabled / RunOnce↔RunOnceDisabled 间搬迁；Folder 项在 Startup↔Startup\Disabled 间搬迁。</summary>
    public static bool SetEnabled(StartupItem item, bool enabled) {
      if (item == null || string.IsNullOrEmpty(item.Id)) return false;
      // ponytail: 不能用 item.IsEnabled == enabled 短路 — UI 的 IsChecked 是 Mode=TwoWay 绑定,
      // 用户切 Toggle 时绑定先在事件触发前把 item.IsEnabled 写成目标值,这里会误判"无需操作"
      // 而直接 return,导致"关闭启动项失效"。幂等交给 RegDisable/RegEnable 依据源键真实存在性判断。
      if (item.ItemType == StartupItemType.Folder) return SetFolderEnabled(item, enabled);
      if (!TryParseId(item.Id, out RunLocation loc, out string name) ||
          !string.Equals(name, item.Name, StringComparison.OrdinalIgnoreCase)) return false;
      return enabled ? RegEnable(item, loc) : RegDisable(item, loc);
    }

    // 搬走：原 Run/RunOnce → 同源的 RunDisabled/RunOnceDisabled；类型原样保留。
    static bool RegDisable(StartupItem item, RunLocation loc) {
      try {
        using (var baseKey = RegistryKey.OpenBaseKey(loc.Hive, loc.View))
        using (var src = baseKey.OpenSubKey(loc.SubKey, true)) {
          // ponytail: 源键不存在 → 没有可禁用的活动项,视为已禁用(幂等成功),不再 return false。
          if (src == null) return true;
          var current = ReadRunValue(src, loc, item.Name);
          // ponytail: current==null 表示 Run 键里已无此值(已搬到 Disabled 或本就不在) → 禁用已达成。
          // 仅当源键还持有该值但命令不匹配(错配)时才失败;不再比较 current.IsEnabled,
          // 那从活动键读恒为 true,且 item.IsEnabled 已被 TwoWay 绑定污染,比较不稳定。
          if (current == null) return true;
          if (current.Command != item.Command) return false;
          RegistryValueKind kind = src.GetValueKind(item.Name);
          object raw = src.GetValue(item.Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);

          string disSub = DisabledSubKey(loc.SubKey);
          using (var dis = baseKey.CreateSubKey(disSub, true)) {
            dis.SetValue(item.Name, raw, kind);
          }
          src.DeleteValue(item.Name, false);
        }
        return true;
      } catch { return false; }
    }

    // 还回：RunDisabled/RunOnceDisabled → 原 Run/RunOnce。
    static bool RegEnable(StartupItem item, RunLocation loc) {
      try {
        string disSub = DisabledSubKey(loc.SubKey);
        using (var baseKey = RegistryKey.OpenBaseKey(loc.Hive, loc.View))
        using (var dis = baseKey.OpenSubKey(disSub, true)) {
          // ponytail: 禁用键不存在 → 没有待启用的项,视为已启用(幂等成功)。
          if (dis == null) return true;
          // 找回原始值与类型
          string real = dis.GetValueNames().FirstOrDefault(n => string.Equals(n, item.Name, StringComparison.OrdinalIgnoreCase));
          if (real == null) return true;   // 禁用键里无此值 → 已启用,幂等
          RegistryValueKind kind = dis.GetValueKind(real);
          object raw = dis.GetValue(real, null, RegistryValueOptions.DoNotExpandEnvironmentNames);

          using (var runKey = baseKey.CreateSubKey(loc.SubKey, true)) {
            runKey.SetValue(item.Name, raw, kind);
          }
          dis.DeleteValue(real, false);
        }
        return true;
      } catch { return false; }
    }

    // ── Folder 搬迁 ──

    static bool SetFolderEnabled(StartupItem item, bool enabled) {
      try {
        string src = item.FolderPath;
        if (string.IsNullOrEmpty(src) || !File.Exists(src)) return false;
        var srcName = Path.GetFileName(src);
        // 当前在活动目录 → 搬到 Disabled\；当前在 Disabled\ → 搬回活动目录。
        string parent = Path.GetDirectoryName(src);
        bool inDisabled = Path.GetFileName(parent).Equals("Disabled", StringComparison.OrdinalIgnoreCase);
        bool wantDisabled = !enabled;
        if (inDisabled == wantDisabled) return true; // 已就位

        string dstParent = wantDisabled
          ? Directory.CreateDirectory(Path.Combine(parent, "Disabled")).FullName   // 活动 → Disabled\
          : Path.GetDirectoryName(parent);                                            // Disabled\ → 上级活动目录
        string dst = Path.Combine(dstParent, srcName);
        if (File.Exists(dst)) { try { File.Delete(dst); } catch { return false; } }
        File.Move(src, dst);
        item.IsEnabled = enabled;
        return true;
      } catch { return false; }
    }

    // ── ID 编解码 ──
    // Registry 项：{loc}:{base64url(name)}；Folder 项：folder:{base64url(path)}。

    public static string MakeId(string locId, string name) =>
      locId + ":" + Convert.ToBase64String(Encoding.UTF8.GetBytes(name)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    static string MakeId(RunLocation loc, string name) => MakeId(loc.Id, name);

    static string FolderId(string path) => "folder:" +
      Convert.ToBase64String(Encoding.UTF8.GetBytes(path.ToLowerInvariant())).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static bool TryParseId(string id, out RunLocation loc, out string name) {
      loc = null; name = "";
      if (string.IsNullOrWhiteSpace(id)) return false;
      int colon = id.IndexOf(':');
      if (colon <= 0) return false;
      loc = RegistryLocations.FirstOrDefault(l => l.Id == id.Substring(0, colon));
      if (loc == null) return false;
      try {
        string b64 = id.Substring(colon + 1).Replace('-', '+').Replace('_', '/');
        b64 = b64.PadRight((b64.Length + 3) / 4 * 4, '=');
        name = Encoding.UTF8.GetString(Convert.FromBase64String(b64));
        return !string.IsNullOrWhiteSpace(name) && name.IndexOf('\0') < 0;
      } catch (FormatException) {
        return false;
      }
    }

    /// <summary>命令是否指向一个明确不存在的绝对路径（true = 视为失效项，不显示）。</summary>
    public static bool IsInvalidCommand(string command) {
      if (string.IsNullOrWhiteSpace(command)) return true;
      string text;
      try { text = Environment.ExpandEnvironmentVariables(command.Trim()); }
      catch { return true; }
      string path;
      if (text.StartsWith("\"")) {
        int end = text.IndexOf('"', 1);
        if (end <= 1) return false;
        path = text.Substring(1, end - 1);
      } else {
        string[] parts = text.Split(new char[] { ' ', '\t' }, 2, StringSplitOptions.RemoveEmptyEntries);
        path = parts.Length > 0 ? parts[0] : text;
      }
      path = path.Trim().Trim('"');
      if (path.Length == 0 || path.Contains('%') || !IsFullyQualified(path)) return false;
      try {
        if (!File.Exists(path)) return !Directory.Exists(path);
      } catch { return true; }
      return false;
    }

    /// <summary>net481 无 Path.IsPathFullyQualified 的等价实现（盘符或 UNC）。</summary>
    static bool IsFullyQualified(string path) {
      if (string.IsNullOrEmpty(path)) return false;
      if (path.StartsWith("\\\\") || path.StartsWith("//")) return true;
      return path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':' &&
             (path[2] == '\\' || path[2] == '/');
    }
  }

  // ── 快捷方式目标解析（最小实现，不依赖 WshShell/COM）─
  // Alt: WshShell 在 net481 也可用，但 COM 组件初始化代价不划算；这里手写 ShellLink 头解析。
  internal static class ShortcutResolve {
    static readonly byte[] ShellLinkClsid = {
      0x01,0x00,0x02,0x00,0x00,0x00,0x00,0x00,0xC0,0x00,0x00,0x00,0x00,0x00,0x00,0x46 };

    public static string RootTargetOrDefault(string path, Func<string, string> displayFn) {
      if (!string.Equals(Path.GetExtension(path), ".lnk", StringComparison.OrdinalIgnoreCase))
        return path; // 非 lnk：直接返回路径本身（exe/bat/cmd）
      try {
        string t = ParseLocalBasePath(path);
        return t ?? path;
      } catch {
        return path;
      }
    }

    // 解析 .lnk 的 LocalBasePath（Unicode 优先）。失败返回 null。
    static string ParseLocalBasePath(string path) {
      byte[] data;
      using (var fs = File.OpenRead(path)) {
        if (fs.Length < 0x4C) return null;
        data = new byte[Math.Min((int)fs.Length, 64 * 1024)];
        int read = fs.Read(data, 0, data.Length);
        if (read < 0x4C) return null;
      }
      // Header CLSID 校验
      for (int i = 0; i < 16; i++)
        if (data[0x04 + i] != ShellLinkClsid[i]) return null;
      uint flags = BitConverter.ToUInt32(data, 0x14);
      int offset = 0x4C;
      // HasLinkTargetIDList (0x01) → 跳过 IDList
      if ((flags & 0x01) != 0) {
        if (offset + 2 > data.Length) return null;
        short idSize = BitConverter.ToInt16(data, offset);
        offset += 2 + idSize;
      }
      if ((flags & 0x02) == 0) return null; // 有 LinkInfo
      if (offset + 4 > data.Length) return null;
      int linkInfoSize = BitConverter.ToInt32(data, offset);
      int linkInfoStart = offset;
      int headerSize = BitConverter.ToInt32(data, offset + 4);
      if (headerSize < 0x1C || offset + linkInfoSize > data.Length) return null;
      int unicodeOffset = BitConverter.ToInt32(data, offset + 0x18);
      if (unicodeOffset > 0 && linkInfoStart + unicodeOffset < offset + linkInfoSize)
        return ReadNullTerminatedUtf16(data, linkInfoStart + unicodeOffset);
      int ansiOffset = BitConverter.ToInt32(data, offset + 0x10);
      if (ansiOffset > 0)
        return ReadNullTerminatedAnsi(data, linkInfoStart + ansiOffset);
      return null;
    }

    static string ReadNullTerminatedUtf16(byte[] data, int start) {
      var sb = new StringBuilder();
      int i = start;
      while (i + 1 < data.Length) {
        char c = (char)(data[i] | (data[i + 1] << 8));
        if (c == 0) break;
        sb.Append(c); i += 2;
      }
      return sb.ToString();
    }

    static string ReadNullTerminatedAnsi(byte[] data, int start) {
      var sb = new StringBuilder();
      for (int i = start; i < data.Length; i++) {
        if (data[i] == 0) break;
        sb.Append((char)data[i]);
      }
      return sb.ToString();
    }
  }
}
