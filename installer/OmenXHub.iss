#define MyAppName "OmenXHub"
#define MyAppPublisher "OmenXHub"
#define MyAppExeName "OmenXHub.exe"
#define BuildDir "..\bin\x64\Release\net481"
#define MyAppVersion GetFileVersion(AddBackslash(BuildDir) + "OmenXHub.exe")

[Setup]
AppId={{7F4B6A40-2E6A-4F7E-9D5E-040040040040}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\OmenXHub
DefaultGroupName=OmenXHub
OutputDir=..\dist
OutputBaseFilename=OmenXHub-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupIconFile=..\Resources\fan.ico
LicenseFile=License.zh-Hans.txt

[Languages]
Name: "chinesesimplified"; MessagesFile: "ChineseSimplified.isl"
Name: "chinesetraditional"; MessagesFile: "ChineseTraditional.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
english.RunApp=Launch OmenXHub
chinesesimplified.RunApp=启动 OmenXHub
chinesetraditional.RunApp=啟動 OmenXHub
english.Subscribe=Subscribe to this project on GitHub
chinesesimplified.Subscribe=订阅本项目（打开 GitHub 仓库）
chinesetraditional.Subscribe=訂閱本項目（開啟 GitHub 儲存庫）

[Files]
Source: "{#BuildDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "OmenXHub.log,*.log,selftest_result.txt,cpu_temp.txt,gpu_temp.txt,custom.ico,CoreKeep.json,preset_names.txt,*.pdb,nul,FanCurves\*"
Source: "License.zh-Hans.txt"; DestDir: "{tmp}"; Flags: dontcopy
Source: "License.zh-Hant.txt"; DestDir: "{tmp}"; Flags: dontcopy
Source: "License.en.txt"; DestDir: "{tmp}"; Flags: dontcopy
; FPS 监控外部工具,程序在安装根目录查找
Source: "..\PresentMon.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\OmenXHub"; Filename: "{app}\{#MyAppExeName}"; Parameters: "--tray"
Name: "{autodesktop}\OmenXHub"; Filename: "{app}\{#MyAppExeName}"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:RunApp}"; Flags: nowait postinstall skipifsilent shellexec
Filename: "https://github.com/MasonDye/OmenXHub/"; Description: "{cm:Subscribe}"; Flags: nowait postinstall skipifsilent unchecked shellexec

[UninstallRun]
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /TN ""OmenXHub"" /F"; Flags: runhidden waituntilterminated; RunOnceId: "DelAutoStartTask"
Filename: "{sys}\taskkill.exe"; Parameters: "/IM ""OmenXHub.exe"" /F"; Flags: runhidden waituntilterminated; RunOnceId: "KillApp"

[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM "OmenXHub.exe" /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := '';
end;

// 安装器语言 → 程序语言设定(HKCU\Software\OmenXHub\Language)。
// ponytail: 每次安装都以安装器所选语言覆盖,保证安装后程序语言与安装器一致;
// 安装后在程序内改的语言不受影响,直到下次重装。
procedure CurStepChanged(CurStep: TSetupStep);
var
  AppLang: String;
begin
  if CurStep = ssPostInstall then begin
    if ActiveLanguage = 'english' then
      AppLang := 'English'
    else if ActiveLanguage = 'chinesetraditional' then
      AppLang := 'TraditionalChinese'
    else
      AppLang := 'SimplifiedChinese';
    RegWriteStringValue(HKEY_CURRENT_USER, 'Software\OmenXHub', 'Language', AppLang);
  end;
end;

// 许可页按安装语言显示对应协议文本。
procedure CurPageChanged(CurPageID: Integer);
var
  LicenseName: String;
begin
  if CurPageID = wpLicense then begin
    if ActiveLanguage = 'english' then
      LicenseName := 'License.en.txt'
    else if ActiveLanguage = 'chinesetraditional' then
      LicenseName := 'License.zh-Hant.txt'
    else
      LicenseName := 'License.zh-Hans.txt';
    ExtractTemporaryFile(LicenseName);
    WizardForm.LicenseMemo.Lines.LoadFromFile(ExpandConstant('{tmp}\') + LicenseName);
  end;
end;
