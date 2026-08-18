# OmenXHub 架构总览

按本仓 `AGENTS.md` 的 ponytail 准则,本文件只画核心数据流和真实的 call graph,不重复 README 已有内容、不写营销话术。所有引用都给到 file:line。

## 1. 进程启动 (`App.OnStartup`, App.xaml.cs:25)

```mermaid
flowchart TD
  A0["exe 启动\n--selftest?"] -->|是| S0["SelfCheck.*\n写 selftest_result.txt\nShutdown\n退出"]
  A0 -->|否| A1["Mutex 单实例校验\nMyUniqueAppMutex"]
  A1 -->|没拿到| A2["PostMessage(WM_SHOW_MAIN)\n唤醒已存在实例\nShutdown 本进程"]
  A1 -->|拿到| A3["ConfigService.Load()\n+ PresetManager.SwitchPreset"]
  A3 --> A4["ExtractAndPreloadNativeDll\nNvidiaApi.dll / OmenLightingSDK.dll"]
  A4 --> A5["ThreadPool×2 后台\n  · 5s 后 LightingPage.ReplaySavedLighting\n  · OmenLighting.DetectKeyboardCapability → UpdateNavigationItems"]
  A5 --> A6["ThemeService.Initialize\nHardwareService.MonitorQuery\nSetFanMode(0x31)  注:unleash 模式前置"]
  A6 --> A7["TrayService.InitTrayIcon\n注册 SystemEvents.PowerModeChanged"]
  A7 --> A8{"cmdLine[1] == --tray?"}
  A8 -->|是| A9["MainWindow.StartTrayOnly()\n最小化创建HWND→Hide,\n仅为保留托盘菜单宿主"]
  A8 -->|否| A10["MainWindow.ShowInstance()"]
  A9 --> A11
  A10 --> A11["ThreadPool:\nHardwareService.LibreComputer.Open\n→ Dispatcher: TrayService.StartTimers + StartTrayHelperTimers"]
  A11 --> A12["HWiNFOService / HWiNFOReaderService.StartStopIfNeeded"]
  A12 --> A13["ConfigService.HttpApiEnabled? → HardwareApiService.Start (本地 5000 port)"]
  A13 --> A14["TrayService.GetOmenKeyTask  (NamedPipe 'OmenXHubPipe' 监听)"]
  A14 --> A15["Dispatcher BeginInvoke:\n  TrayService.RestoreConfig\n  AutomationService.Initialize (+ Start if enabled)\n  MacroService.Initialize (+ MacroController.Start if enabled)\n  OsdWindow.StartLockKeyMonitor"]
  A15 --> A16["FloatingWindow.ShowInstances if FloatingBar==on\nHelpWindow if 新版本"]
```

## 2. 运行时主循环 / 定时器矩阵

```mermaid
flowchart LR
  subgraph UI["UI 层 (Dispatcher 线程)"]
    MW["MainWindow\nIsVisibleChanged → Start/Stop\n  _statusTimer 2s: 状态栏 CPU/GPU 温度 (MainWindow.xaml.cs:510+)"]
    OSD["OsdWindow\n  _lockKeyTimer 200ms: Caps/Numlock 轮询\n  _fadeTimer 1.5s: toast 短淡出 (OsdWindow.xaml.cs:35+)"]
    FW["FloatingWindow\n  _refreshTimer = MonRefreshInterval 250ms+\n  每实例一窗,跨越多屏 (FloatingWindow.xaml.cs:33+)"]
    TPW["TrayPopupWindow\n  悬停托盘图标时显示 1s 刷新定时器"]
  end

  subgraph BG["后端服务 (大量 ThreadPool + Timer)"]
    TS["TrayService"]
    TS -->|1s System.Threading.Timer| FCT["fanControlTimer\n读温度 → SetMaxFanSpeedOff + SetFanLevel\n  (AMD EC 保活, 1s 周期硬写) (TrayService.cs:497+)"]
    TS -->|1s System.Timers.Timer| TUT["tooltipUpdateTimer\nHardwareService.QueryHardware\nUpdateTooltip / CheckAutoFanProtect (/cool 触发回切)\nHandleDbUnlockCountdown / HandleRestoreCountdown"]
    TS -->|30s DispatcherTimer| OPT["optimiseTimer\nAMD SetFanMode(0x31) 重复强调\nflagStart<5 时重申风扇配态"]
    TS -->|2s DispatcherTimer| CFT["checkFloatingTimer\n注册表 FloatingBar 外部切换轮询"]

    HS["HardwareService.QueryHardware\n800ms cache\nLibreComputer sensors → CPUTemp/Power/Util/Clock"]
    AML["LightingAnimationService\n_timer 50ms (20 FPS)\n6 渲染器 → OmenLighting.SetZoneStaticColor 软渲染"]
    LSS["LightingSceneService\n_scheduleTimer 60s\n按时间表触发 scene (LightingSceneService.cs:252+)"]
    AP["AutomationProcessor\n  _processStartWatcher/_processStopWatcher (WMI 事件)\n  温度 2s / 电池 5s / 调度 15s 三定时器\n  RegisterHotKey  WM_HOTKEY"]
    EQ["EcoQosService\n  _throttleTimer 2s\n  ProcessPowerThrottling API"]
    HW["HwiNfo 双向:\n  HWiNFOService 1s 写到注册表 Custom\\OmenXHub\n  HWiNFOReaderService 1s 从 VSB 读,EMA 平滑"]
    LAS["BoostService\n  ProxyEngine (SOCKS5/HTTP) | TunManager (sing-box)\n  RateLimiter token-bucket"]
  end

  UI -.读 HardwareService 状态.-> HS
  UI -.config 变更触发.-> TS
```

## 3. 硬件控制底座 (Hardware Layer, `OmenHardware.cs`)

**所有 EC / 风扇 / 功耗 / 灯效控制都通过同一条 WMI 通道,而非 native IOCTL/PawnIO**(PawnIO 只用于 AMD undervolt 的 SMU):

```mermaid
flowchart TD
  subgraph Entry["WMI 入口 (唯一)"]
    SendWmi["OmenHardware.SendOmenBiosWmi\n(cmdType, data, outputSize,\n command = 0x20008 | 0x20009 | 0x2000B)"]
  end

  subgraph WMI["WMI 命名空间 root\wmi"]
    HBI["hpqBIntM (ManagementObject, 缓存为 _biosMethods)"]
    HDI["hpqBDataIn {Command, CommandType, Sign=[SECU], hpqBData, Size}"]
    HBM["hpqBIOSInt{outputSize}  方法返回 OutData{rwReturnCode, Data}"]
    HBI --> HDI --> HBM --> SendWmi
  end

  subgraph Cmd["commandType 真实动作表"]
    C10["0x10 命令\nUnleash 模式前置\nResume 后 EC 唤醒"]
    C1A["0x1A\nSetFanMode(0x31 perf/0x30 default)"]
    C27["0x27\nSetMaxFanSpeedOn/Off\nAMD EC 保活关键"]
    C2E["0x2E\nSetFanLevel(pct1,pct2) 写入目标RPM"]
    C29["0x29\nSetCpuPowerLimit PL1/PL2\nSetConcurrentTdp (TPP)"]
    C37["0x37\nSetCpuPowerLimitPL4 (双字节)\nIccMax, LoadLine"]
    C22["0x22\nSetGpuPowerState CTGP/PPAB/dState\nGetGpuPowerState"]
    C21["0x21\nGetGpuPowerState(读)"]
    C23["0x23\nGetSensorTemperature IR/Amb/PCH/VR"]
    C28["0x28\nGetSystemDesignData(缓存) 机型能力位"]
  end

  subgraph LED["灯效 channel 0x20009 (hpagx BIOS)"]
    L02["0x02 GetZoneStaticColor (BasicFourZone)"]
    L03["0x03 SetLightColor 写色表 (BasicFourZone)"]
    L05["0x05 Backlight on/off, SetBrightness, SetZoneBrightness"]
    L07["0x07 SetZoneAnimation BasicFourZone effectId 限 2/4"]
    L0B["0x0B=11 Dojo 写 SetZoneStaticColor/Animation/Brightness\neffectId 接受 1..9"]
    L0C["0x0C=12 GetCurrentAnimationEffect (Dojo)"]
    L2B["0x2B GetKeyboardType → NbKeyboardLightingType"]
  end

  subgraph Win["command 0x2000B (Win/Gaming 键)"]
    W00["GetWinLock/SetWinLock EC Win/Gaming 键硬件锁"]
  end

  SendWmi --> C10 & C1A & C27 & C2E & C29 & C37 & C22 & C21 & C23 & C28
  SendWmi --> L02 & L03 & L05 & L07 & L0B & L0C & L2B
  SendWmi --> W00
```

## 4. 灯光控制双路径 (`App/OmenLighting.cs`)

```mermaid
flowchart TD
  Cap["OmenLighting.DetectKeyboardCapability()\n枚举 HID 设备 + 探测键盘类型\n(capability cache)"]

  Cap -->|Kind == FourZone 或 LightBarOnly| Wmi["WMI 四区/Dojo\n通道 0x20009 (BasicFourZone) 或 131081 (Dojo)\nOmenLighting.SetZoneStaticColor/SetZoneAnimation/SetZoneBrightness\n(Alt source of animation: LightingAnimationService 对每帧软渲染)"]
  Cap -->|Kind == PerKey 且是 HP HID 设备| Sdk["Native SDK: OmenLightingSDK.dll\nOmenLightingNative.cs P/Invoke\nKeyboard/Mouse/Headset/...SetStatic\nKeyboard_SetMultiColorAnimation"]
  Cap -->|HP McuSDK2 兼容 PID/VID| Mcu["McuSDK2.dll (HidSharp)\nMcuKeyboardHelper / McuGeneralHelper\nPerKeyStaticColor/Animation/Audio/Brightness\n  · Modena, Ralph, Cybug, Hendricks, Brunobear,\n    Quaker, Voco, Dojo, Vibrance 等 PID\n  · HP_VID=0x03F0 (App/OmenLighting.cs:248)"]

  Wmi --> Effect["Effect ID 区分:\n  BasicFourZone: 只接受 2/4\n  Dojo: 接受 1..9\n  LightBarOnly: data[0]=0 (灯带), =1 (四区)"]
  Sdk --> Perkey["Native per-key API\nKey 数支持查询 (Keyboard_GetAvailableKeys)"]
  Mcu --> HpFlash["McuSDK2 通道\nStorePerKeyToFlash 持久化\nRestorePerKeyLightingToDefault"]
```

## 5. 预设 / 配置链 (`PresetManager` + `ConfigService`)

```mermaid
flowchart LR
  Trigger["触发点:\n  · 用户托盘菜单 "循环预设" → OmenKey pipe\n  · SettingsPage 用户切换\n  · AutomationProcessor 自动条件触发\n  · Resume 后 OnPowerChange → RestorePowerConfig"]
  Trigger --> Switch["PresetManager.SwitchPreset(preset)\n· 1.1 用 ApplyPresetData 同步 ConfigService 字段\n· 1.2 仅 custom preset 再覆盖\n· 发出 OnPresetChanged (Dispatcher.Invoke)"]

  Switch --> Apply["PresetManager.ApplyPresetHardware()\n单 ThreadPool.QueueUserWorkItem\n原子化硬件写入"]

  Apply --> A1["OmenHardware.SetFanMode(0x31)"]
  Apply --> A2["OmenHardware.SetCpuPowerLimit +\n  SetPL4DoubleByte +\n  AmdUndervoltService.SetAllCoreCO/ApplyPerCoreCO"]
  Apply --> A3["OmenHardware.SetConcurrentTdp (TPP)\n  ⚠ 必须 GPU power 前,否则 PPAB 读旧预算"]
  Apply --> A4["OmenHardware.SetGpuPowerState CTGP/PPAB/dState"]
  Apply --> A5["OmenHardware.SetMaxFanSpeedOff + SetFanLevel\n  FanService.ApplyPresetCurve (custom_<preset>.txt)\n  fanControlTimer.Change"]
  Apply --> A6["GpuAppManager.Set[Core/Memory]ClockOffset\n  NVAPI_SetMaxFrameRate\n  PowerSetActiveScheme + PowerSetActiveOverlayScheme"]
  Apply --> A7["EcoQosService.SetEnabled/SetThrottlePlugged\n  CoreKeepService.StartAutoApply/StopAutoApply"]
  Apply --> A8["TrayService.ApplyRefreshRate (DEVMODE)"]

  Switch -.幂等发火.-> Lss["PresetManager.OnPresetChanged →\nLightingSceneService.NotifyPresetChanged → 可能改场景"]
  Switch -.-> CFC["ConfigService.FirePresetCycled →\nDashboardPage 更新 preset 标\n(关面板时已断订阅 ReleaseFrontend)"]
```

## 6. 跨进程 Omen Key 触发 (E.1)

```mermaid
flowchart LR
  Fw["Omen Key 物理按下"] --> Ec["BIOS EC 产生 WMI 事件"]
  Ec --> WmiFilter["OmenHardware.OmenKeyOn 装的 WMI:\n  __EventFilter + CommandLineEventConsumer + __FilterToConsumerBinding\n  CommandLineTemplate = 'cmd /c echo OmenKeyTriggered > \\.\pipe\OmenXHubPipe'"]
  WmiFilter --> Cmd["Win32 启动 cmd.exe 写入命名管道"]
  Cmd --> Pipe["\\\\.\\pipe\\OmenXHubPipe"]
  Pipe --> Reader["TrayService.GetOmenKeyTask:\nNamedPipeServerStream.WaitForConnection\nStreamReader.ReadToEnd"]

  Reader -->|"OmenKeyTriggered"| Routing{"ConfigService.OmenKey"}
  Routing -->|showMain| Rm["MainWindow.ShowInstance"]
  Routing -->|cyclePresets| Rc["取下一候选 → PresetManager.SwitchPreset\n+ ApplyPresetHardware + OsdWindow.ShowPresetOsd\n+ FirePresetCycled"]
  Routing -->|app| Ra["LaunchOmenKeyApp (process start)"]
  Routing -->|custom| Rf["checkFloating = true\n(下个 2s tick 由 checkFloatingTimer 切换浮窗)"]
  Routing -->|none| Rn["no-op"]
```

## 7. 关闭主面板的前端释放流程 (F.1-F.3,本次补丁新增)

```mermaid
flowchart TD
  Close["点关闭按钮 / Closing 事件"] --> Allow{"_allowClose?"}
  Allow -->|"true 只在 TrayService.Exit 翻"| Real["真关: ThemeService.ThemeChanged -=, 拆 wheel handler,\nStopStatusTimer, _instance=null\n(后端统一由 TrayService.Exit 清)"]
  Allow -->|"false (用户按 X)"| Hide["e.Cancel = true; Hide();"]
  Hide --> Vis["IsVisibleChanged false 分支\n  else { StopStatusTimer(); ReleaseFrontend(); }"]

  Vis --> RF["ReleaseFrontend (MainWindow.xaml.cs:549+)"]
  RF --> R1["反射取 NavigationView 的 protected\n  NavigationViewContentPresenter (实际类型 = Frame)\n  cast 到 ContentControl, 置 Content=null\n  ↓ 同步触发当前 Page 的 Unloaded"]
  RF --> R2["nav.ClearJournal() — 清后退栈强引用\n  (Wpf.Ui 公开 API)"]
  RF --> R3["_pageService.Clear() — 清 Dictionary 引用"]
  RF --> R4["_activePage = null"]
  RF --> R5["GC.Collect / WaitForPendingFinalizers / GC.Collect"]
  RF --> R6["psapi!EmptyWorkingSet\n  (复用 DashboardPage.cs:60 同一 P/Invoke)"]

  R1 -->|"Page 各自的 Unloaded"| PU["解订阅/停 timer:\n  PerfPage: PresetManager.OnPresetChanged -= &\n           Strings.OnLanguageChanged -= & Instance=null\n  DashboardPage: ConfigService.OnPresetCycled -= &\n                _presetCycledHandler -= & _refreshTimer Stop\n  FanPage/LightingPage/AutomationPage/MacroPage/\n  NetworkBoostPage/OtherPage/RoutingRulesPage: 各自对称清理"]

  R5 --> GC["Page 实例 + XAML 可视树 → GC 回收"]
  R6 --> Mem["任务管理器可见工作集回落"]
```

注意:**后端进程链路不动** —— HardwareService / fanControlTimer / tooltipUpdateTimer / AutomationProcessor / MacroController / OmenKey pipe / TrayService timer / SystemEvents.PowerModeChanged 全部按 TrayService/App 持有,不受窗口可见性影响。从最小化唤回(不经过 Hide,只在窗口可见+最小化态点托盘)不会触发本流程,保留热缓存。

## 8. 配置 / 持久化

```mermaid
flowchart LR
  subgraph Settings["ConfigService 静态状态 (HKCU\Software\OmenXHub)"]
    Common["根级:\n  Preset, FanControl, FanTable, FanMode,\n  MonitorCPU/GPU/Fan, Theme, Language,\n  OmenKey, Floating*, Lighting*, ..."]
    PresetSub["Preset 子键:\n  HKCU\Software\OmenXHub\Presets\<preset>\n  per-preset 数据"]
    CustomPresetNames["%APPDATA%\OmenXHub\preset_names.txt\nfile fallback"]
    SavedState["SavedState:\n  %APPDATA%\OmenXHub\automation.json\n  macros.json\n  lighting_scenes.json\n  lightstudio_occ_stub 等装载在自定义位置"]
  end

  SettingsEvent["ConfigService.OnPresetCycled (event)\n  · DashboardPage 订阅(关面板时已断订阅)\n  · anyone 可 Subscribe"]
  SettingsEvent -.-> Settings
```

## 9. 进程退出 (`App.OnExit`, App.xaml.cs:285)

```mermaid
flowchart TD
  Exit["托盘 'Exit' → TrayService.Exit()"] --> T0["网关:_allowClose = true\napp.Dispatcher.BeginInvoke → app.Shutdown()\n5s 兜底 Environment.Exit(0)"]
  T0 --> OnExit["App.OnExit  SafeShutdown 每步独立 try-catch"]
  OnExit --> E1["PresetManager.SaveCustomPreset (若是 custom)"]
  OnExit --> E2["BoostService.Stop\n停代理/停 TUN/清路由, 留 json"]
  OnExit --> E3["MacroController.Stop (UnhookWindowsHookEx)"]
  OnExit --> E4["HardwareApiService.Stop (停 HttpListener)"]
  OnExit --> E5["HWiNFOService/Reader Stop"]
  OnExit --> E6["ThemeService.Cleanup"]
  OnExit --> E7["EcoQosService.Cleanup (free Marshal.AllocHGlobal)"]
  OnExit --> E8["CoreKeepService.StopAutoApply (JobObject ⊥ WMI watcher)"]
  OnExit --> E9["AutomationProcessor.Stop (dispose watchers/timers,\n              unregister hotkeys, unsubscribe SystemEvents)"]
  OnExit --> E10["LightingSceneService.StopScheduler (60s Timer)"]
  OnExit --> E11["SystemEvents.PowerModeChanged -= OnPowerChange"]
  OnExit --> E12["HardwareService.Close (LibreComputer.Close)"]
  OnExit --> E13["_mutex ReleaseMutex/Dispose"]
```

## 10. 自检机制 (`--selftest`, App.xaml.cs:30 + RunFrontendReleaseSelfCheck)

`OmenXHub.exe --selftest` → 不启 UI → 跑四个 SelfCheck 函数 + 关面板释放前端内存自检 → 写 `selftest_result.txt` → `Environment.ExitCode = result.Contains("FAIL") ? 1 : 0`。

新增的 `[FrontendRelease]` 段反射驱动 CachedPageService / PerfPage.Unloaded / DashboardPage.Unloaded,断言:
- Patch 3 `CachedPageService._cache` Clear 后 Count == 0
- Patch 1 `PerfPage.Instance` Unloaded 后 == null
- Patch 2 `ConfigService.OnPresetCycled` 中已无 DashboardPage 实例的订阅

## 附录 — 文件 → 责任对照

| 文件 | 责任概要 |
|---|---|
| `App.xaml.cs` | 启动入口、Mutex 单实例、`--selftest`、OnExit 顺序清场 |
| `OmenHardware.cs` | HP BIOS WMI 通道 (`hpqBIntM`) + 灯效 WMI 相关 + GPU/系统检测 |
| `App/OmenLighting.cs` | 灯效双路径(WMI / Native SDK / McuSDK2 HID)+ 键盘能力探测 |
| `Services/OmenLightingNative.cs` | OmenLightingSDK.dll P/Invoke wrap |
| `Services/HardwareService.cs` | LibreHardwareMonitor 传感器 + 800ms 缓存 + GPU AC 检测 |
| `Services/TrayService.cs` | 托盘图标 + 多个 timer 进 + WMI hook + OmenKey pipe + Exit() 清场 |
| `Services/PresetManager.cs` | 内置/自定义预设 + SwitchPreset/ApplyPresetHardware 原子硬件写入 |
| `Services/FanService.cs` | 风扇曲线引擎 (smart EMA + hysteresis + 文件持久化) |
| `Services/AutomationProcessor.cs` | 事件驱动自动化管道 + 3 timer + WM_HOTKEY + WMI watcher |
| `Services/AutomationService.cs` | AutomationPipeline 数据模型 + JSON |
| `Services/MacroController.cs` | WH_KEYBOARD_LL/WH_MOUSE_LL 全局钩子 + 录制回放 |
| `Services/MacroService.cs` | MacroSequence 数据模型 + JSON + trigger-key index |
| `Services/LightingSceneService.cs` | 灯光场景 CRUD + 60s 调度 + 预设联动 |
| `Services/LightingAnimationService.cs` | 4 区软件渲染帧引擎 (20 FPS) |
| `Services/ThemeService.cs` | Dark/Light 系统 + Mica + DWM 口味 |
| `Services/EcoQosService.cs` | ProcessPowerThrottling (后台进程节流) |
| `Services/BoostService.cs` + NetworkBoost/ | 多 NIC 代理 + tun (sing-box.exe) + 速率限制 |
| `Services/HardwareApiService.cs` | 本地 HTTP API (port 5000, X-Auth-Token Guid) |
| `Services/HWiNFOService.cs`/`HWiNFOReaderService.cs` | HWiNFO64 双向 (写自定义传感器 / 读 VSB) |
| `Services/AmdUndervoltService.cs` | AMD Ryzen Curve Optimizer (PawnIO SMU) |
| `Services/CpuAffinity/*` | CoreKeep 持久进程亲和性 (JobObject + WMI watcher) |
| `Views/MainWindow.xaml.cs` | 导航 + 关面板释放 (ReleaseFrontend) + 托盘唤起 WndProc hook |
| `Services/CachedPageService.cs` | Wpf.Ui IPageService + Clear() for ReleaseFrontend |
| `Windows/BaseWindow.cs` | Mica + DPI FluentWindow 基类 |
| `Views/FloatingWindow.xaml.cs` | 多屏浮窗 + PresentMon FPS + 单计时器 |
| `Views/OsdWindow.xaml.cs` | Caps/NumLock 轮询 + preset OSD toast |
| `Views/HelpWindow.xaml.cs` | 关于窗 (singleton) |
| `Views/TrayPopupWindow.xaml.cs` | 托盘悬停弹窗 (1s 刷新) |
| `Pages/*` | 各功能页 (Dashboard / Fan / Perf / Lighting / Automation / Macro / NetworkBoost / CoreKeep / RoutingRules / Other / Settings) + 各页 Loaded/Unloaded 对称清理 |
```
