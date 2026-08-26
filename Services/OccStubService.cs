// OccStubService.cs — OMEN Light Studio 的 OGH 在场存根管理
// 背景: Light Studio 启动/进功能层时用 PackageManager 查 "AD2F1837.OMENCommandCenter"
// 包是否安装(CheckOCCModule.dll),卸载 OGH 后会弹"请安装 OCC"。本服务在 OXH 目录下
// 维护一个同名同发行者的空壳包并注册,枚举即命中,Light Studio 照常工作。
// 需要: 开发者模式(Add-AppxPackage -Register 免签名)。PowerShell 壳调用 — 设置页交互
// 频率低,秒级延迟可接受;升级路径 = WinRT PackageManager 直调。
using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace OmenSuperHub.Services {
  internal static class OccStubService {
    public static string StubDir => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "OCCStub");
    static string ManifestPath => Path.Combine(StubDir, "AppxManifest.xml");
    // 存根版本拉高 — 盖过 Light Studio 可能存在的 OCC 最低版本比较
    const string StubVersion = "9999.1.1.0";

    const string Manifest =
      "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
      "<Package xmlns=\"http://schemas.microsoft.com/appx/manifest/foundation/windows10\"" +
      " xmlns:uap=\"http://schemas.microsoft.com/appx/manifest/uap/windows10\"" +
      " xmlns:rescap=\"http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities\"" +
      " IgnorableNamespaces=\"uap rescap\">" +
      "<Identity Name=\"AD2F1837.OMENCommandCenter\" Publisher=\"CN=ED346674-0FA1-4272-85CE-3187C9C86E26\"" +
      " Version=\"" + StubVersion + "\" ProcessorArchitecture=\"x64\" />" +
      "<Properties><DisplayName>OMEN Command Center</DisplayName><PublisherDisplayName>HP Inc.</PublisherDisplayName>" +
      "<Logo>assets\\logo.png</Logo></Properties>" +
      "<Resources><Resource Language=\"en-US\" /></Resources>" +
      "<Dependencies><TargetDeviceFamily Name=\"Windows.Desktop\" MinVersion=\"10.0.17763.0\" MaxVersionTested=\"10.0.26100.0\" /></Dependencies>" +
      "<Capabilities><rescap:Capability Name=\"runFullTrust\" /></Capabilities>" +
      "<Applications><Application Id=\"OCCStub\" Executable=\"stub.exe\" EntryPoint=\"Windows.FullTrustApplication\">" +
      "<uap:VisualElements DisplayName=\"OMEN Command Center\" Description=\"Presence stub\" BackgroundColor=\"#1f1f1f\"" +
      " Square150x150Logo=\"assets\\logo.png\" Square44x44Logo=\"assets\\logo.png\" />" +
      "</Application></Applications></Package>";

    // 标准 1x1 透明 PNG(67 字节权威串) — 仅满足清单 Logo 校验,存根不进常用入口
    const string LogoB64 =
      "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAACklEQVR4nGMAAQAABQABDQottAAAAABJRU5ErkJggg==";

    /// <summary>确保存根目录三件套齐(幂等): 清单 / stub.exe(cmd 复制,占位) / logo.png</summary>
    static void EnsureFiles() {
      Directory.CreateDirectory(Path.Combine(StubDir, "assets"));
      File.WriteAllText(ManifestPath, Manifest);
      string stubExe = Path.Combine(StubDir, "stub.exe");
      if (!File.Exists(stubExe))
        File.Copy(Environment.SystemDirectory + "\\cmd.exe", stubExe, overwrite: true);
      string logo = Path.Combine(StubDir, "assets", "logo.png");
      File.WriteAllBytes(logo, Convert.FromBase64String(LogoB64));
    }

    static string RunPs(string command) {
      var psi = new ProcessStartInfo {
        FileName = "powershell.exe",
        Arguments = "-NoProfile -NonInteractive -Command \"" + command.Replace("\"", "'") + "\"",
        UseShellExecute = false, CreateNoWindow = true,
        RedirectStandardOutput = true, RedirectStandardError = true,
      };
      using (var p = Process.Start(psi)) {
        string output = p.StandardOutput.ReadToEnd();
        _ = p.StandardError.ReadToEnd();  // 读空 stderr,防缓冲区填满导致子进程卡死
        p.WaitForExit(30000);
        return output ?? "";
      }
    }

    public sealed class State {
      public bool LightStudioInstalled;
      public string LightStudioPath;     // LightStudio-ui.exe 全路径(未装为 null)
      public bool OccInstalled;          // OMENCommandCenter 在场(真包或存根)
      public bool OccIsStub;             // 在场的是我们的 9999 存根
    }

    /// <summary>一次 PS 查询双包状态(CSV 解析,空输出=两包皆无)</summary>
    public static State QueryState() {
      var st = new State();
      string csv = RunPs(
        " Get-AppxPackage -Name AD2F1837.* | " +
        "Select-Object Name,Version,InstallLocation | ConvertTo-Csv -NoTypeInformation");
      foreach (string line in csv.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)) {
        if (line.Length == 0 || line[0] != '"') continue;
        // 行格式: "Name","Version","InstallLocation"
        var f = line.Split(',');
        if (f.Length < 3) continue;
        string name = f[0].Trim('"'), ver = f[1].Trim('"'), loc = f[2].Trim('"');
        if (name.Contains("OMENLightStudio")) {
          st.LightStudioInstalled = true;
          string exe = Path.Combine(loc, "LightStudio-ui", "LightStudio-ui.exe");
          if (File.Exists(exe)) st.LightStudioPath = exe;
        } else if (name.Contains("OMENCommandCenter")) {
          st.OccInstalled = true;
          st.OccIsStub = ver.StartsWith("9999.");
        }
      }
      return st;
    }

    /// <summary>开发者模式是否已开启(AllowDevelopmentWithoutDevLicense)。</summary>
    public static bool IsDeveloperModeEnabled() {
      try {
        using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock"))
          return key != null && Convert.ToInt32(key.GetValue("AllowDevelopmentWithoutDevLicense", 0)) == 1;
      } catch { return false; }
    }

    /// <summary>开启开发者模式(写 HKLM,需管理员).false=写入失败。</summary>
    public static bool EnableDeveloperMode() {
      try {
        using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock")) {
          if (key == null) return false;
          key.SetValue("AllowDevelopmentWithoutDevLicense", 1, RegistryValueKind.DWord);
          key.SetValue("AllowAllTrustedApps", 1, RegistryValueKind.DWord);
        }
        return IsDeveloperModeEnabled();
      } catch { return false; }
    }

    /// <summary>注册存根(需开发者模式)。返回 null=成功,否则为错误文本。</summary>
    public static string Register() {
      try {
        EnsureFiles();
        string out8 = RunPs("Add-AppxPackage -Register '" + ManifestPath + "'");
        // Add-AppxPackage 失败信息走 stderr,成功无输出 — QueryState 复核
        return QueryState().OccIsStub ? null : Strings.OccStubRegFail;
      } catch (Exception ex) { return ex.Message; }
    }

    /// <summary>移除存根 — 仅当在场包是 9999 存根时动手,绝不动真 OGH。</summary>
    public static string Remove() {
      try {
        RunPs("Get-AppxPackage -Name AD2F1837.OMENCommandCenter | Where-Object { $_.Version -like '9999.*' } | Remove-AppxPackage");
        return QueryState().OccInstalled ? Strings.OccStubRmFail : null;
      } catch (Exception ex) { return ex.Message; }
    }

    /// <summary>启动 Light Studio(explorer 壳启动,不继承 OXH 的管理员令牌)。</summary>
    public static bool LaunchLightStudio() {
      var st = QueryState();
      if (st.LightStudioPath == null) return false;
      try { Process.Start("explorer.exe", "\"" + st.LightStudioPath + "\"")?.Dispose(); return true; }
      catch { return false; }
    }

    // ponytail: PFN 来自本机实包 Get-AppxPackage,OLS 包家族名稳定;深链比 ProductId 更不易猜错。
    const string LightStudioPFN = "AD2F1837.OMENLightStudio_v10z8vjag6ke6";

    /// <summary>拉起 Microsoft Store 到 Light Studio 详情页(explorer 壳,不继承管理员令牌)。
    /// ms-store 无法静默装,深链是唯一干净路径 — 用户在商店点"获取"完成安装。</summary>
    public static bool InstallLightStudio() {
      try { Process.Start("explorer.exe", "\"ms-windows-store://pdp/?PFN=" + LightStudioPFN + "\"")?.Dispose(); return true; }
      catch { return false; }
    }
  }
}
