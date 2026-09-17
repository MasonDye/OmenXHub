# HP OMEN Overlay 逆向参考

> 对象：`C:\Program Files\HP\Overlay\OMENOverlay.exe`（OMEN Gaming Hub 的游戏内叠加层）
> 方法：该目录全是 **.NET Framework 4.8 WPF**（Prism + DryIoc），用 `ilspycmd` IL 反编译即可，不需要 IDA。
> 反编译产物：`%TEMP%\omen-re\src`（约 9.7k 行核心代码），重新生成命令：
> ```bash
> ilspycmd -p -o exe    "C:\Program Files\HP\Overlay\OMENOverlay.exe"
> ilspycmd -p -o lib    "C:\Program Files\HP\Overlay\HP.Omen.OMENOverlayLib.dll"
> ilspycmd -p -o helper "C:\Program Files\HP\Overlay\OverlayHelper.exe"
> ```
> 分析日期：2026-08-29

## 1. 总体架构：三进程 + 命名管道

```
OGH UWP (OMEN Gaming Hub 主程序, MSIX 包 AD2F1837.OMENCommandCenter)
  │  安装时把 Win32 组件铺到 C:\Program Files\HP\Overlay\，并注册计划任务
  ▼
OverlayHelper.exe            ← 常驻后台进程（每会话单实例，Mutex）
  ├─ WH_KEYBOARD_LL 全局键盘钩子 → 热键触发
  ├─ WH_MOUSE_LL 全局鼠标钩子   → "点击外部关闭" 判定
  ├─ PresentMon(ETW) FPS 采集   → 每秒经管道推送给各 widget
  └─ 管道服务端: HP.Omen.OverlayHelper.DesktopApp<SessionId>
  ▼ ShellExecute OMENOverlayLauncher.exe（热键触发且主程序未运行时）
OMENOverlay.exe              ← WPF 前端（选择器 + 所有 widget 宿主）
  ├─ MainWindowV2   选择器面板（挑 widget、改透明度/颜色/热键）
  ├─ OverlayBG      主屏全屏半透明遮罩（打开选择器时出现）
  └─ OverlayWindowManager: 每个 widget 一个 OverlayWindowV2 独立小窗
      └─ 插件: Overlay\Plugins\<Name>\<Name>.dll 实现 IOverlayWidgetPlugin
         （SystemVitals / SystemVitalsCompactPlugin / FanMonitorPlugin / PerformanceControlPlugin）
```

关键设计选择：**widget 不是画在一个大画布上，而是每个 widget 一个独立的 Topmost 小窗**，
由 `OverlayWindowManager`（单例）统一管理显示/隐藏/置顶。

## 2. IPC：命名管道协议

管道名全部带 `Process.GetCurrentProcess().SessionId` 后缀（多用户会话隔离）。
消息体统一是 `PerseusRevMsg { int FuncType; object SendParameter }`。

| 管道名 | 宿主 | 用途 |
|---|---|---|
| `HP.Omen.Overlay.UI<sid>` | OMENOverlay.exe | 总控：热键/显示/隐藏/设置/鼠标点击 |
| `HP.Omen.OverlayHelper.DesktopApp<sid>` | OverlayHelper.exe | widget 订阅 FPS / 接收指令 |
| `HP.Omen.Overlay.SystemVitals<sid>` | SystemVitals widget | 接收 FPS 推送 (FuncType=1) |
| `HP.Omen.Overlay.SystemVitalsCompactPlugin<sid>` | Compact widget | 同上 |
| `HP.Omen.Overlay.SystemVitals.PerformanceMonitor<sid>` | SystemVitals widget | 接收 CPU/GPU/NET 推送（来自 OGH 后台性能服务） |
| `HP.Omen.Overlay.BG.Hotkey<sid>` | OGH UWP 后台 | 主程序没开时由 UWP 侧接力启动 |
| `HP.Omen.Overlay.BG.GA<sid>` | OGH 后台 | 纯遥测（GA 打点），可直接无视 |

主程序 `HP.Omen.Overlay.UI` 收到的 FuncType（`MainWindowV2ViewModel.ReceiveMsg`）：

| FuncType | 参数 | 行为 |
|---|---|---|
| 1 | `"OGH UI"` / `"LaunchSelectorFromWidget"` / `"FirstLaunchSelectorFromHelper"` | 打开选择器 |
| 1 | `"OGH UI Close Overlay"` | 关闭选择器 |
| 1 | 热键索引 `"0"~"3"` | 与当前配置热键匹配则 toggle 整个 overlay |
| 2 | 鼠标点击坐标 JSON | 点击不在任何 widget 内 → 全部隐藏 |
| 4 | — | 隐藏全部（点击遮罩/游戏里按了别的） |
| 6 | widget 名 | 打开选择器并跳到该 widget 的设置 |
| 999 | `"Terminate"` | 退出（更新/卸载时 Helper 发来的） |

## 3. 叠加层窗口本体（最值得抄的部分）

### 3.1 窗口样式组合（`OverlayWindowV2`）

```csharp
Topmost = true;                                   // WPF 属性
AllowsTransparency = plugin.Setting.Transparency; // 每插件可选
// SourceInitialized 里追加：
extStyle |= WS_EX_NOACTIVATE;                     // 0x08000000，永不抢焦点
Title = "[Overlay]" + name;                       // 标题带前缀，供钩子逻辑自我识别
```

- 没用 `WS_EX_TRANSPARENT` 点击穿透 —— 他们的叠加层是**可交互**的（拖拽、悬停菜单、设置）。
- `WndProc` 吞掉 `WM_NCLBUTTONDOWN/DBLCLK/UP`(164/165/166) + `HTCAPTION`：禁掉系统标题栏拖拽，拖拽改由 `MouseMove` 里自己 `DragMove()` 实现（只在 `Plugin.IsActive` 时）。

### 3.2 置顶维持与显隐（`WindowHelper` + `OverlayWindowManager`）

```csharp
void ShowInactiveTopmost(IntPtr hWnd) {
    ShowWindow(hWnd, SW_SHOWNOACTIVATE);                       // 4
    GetWindowRect(...);
    SetWindowPos(hWnd, HWND_TOPMOST, rect..., SWP_NOACTIVATE); // 重新插回 topmost 带
}
void HideWindow(IntPtr hWnd) {
    ShowWindow(hWnd, SW_HIDE);
    SetWindowPos(hWnd, HWND_NOTOPMOST, rect..., SWP_NOACTIVATE);
}
```

**Z 序维持机制**：WPF Topmost 会被其他 topmost/全屏窗口压下去，HP 的做法是
`SetWinEventHook(EVENT_SYSTEM_FOREGROUND)`（`ActiveWindow` 类，WINEVENT_OUTOFCONTEXT）
监听前台窗口切换，每次切换就把所有"已锁定"的 widget `SetWindowPos(HWND_TOPMOST)` 重新顶上去。
同时识别标题含 `Windows Default Lock Screen` 时全部隐藏。

**锁屏/睡眠处理**（`OverlayWindowManager`）：
- `SystemEvents.SessionSwitch` SessionLock/Unlock → 隐藏/恢复；
- Modern Standby（S0 低功耗待机）事件 → 隐藏；恢复时**轮询等待 `logonui.exe` 退出**再显示（避免画在锁屏上）；
- 隐藏/恢复都只作用于对应状态的 widget（`ShowAllPinnedWidgets` 只恢复 IsLocked 的）。

### 3.3 Lock（钉住）模型 —— 注意不是点击穿透

- **Locked = 钉住**：选择器关闭后继续常驻显示，且每次前台切换被重新置顶；
- **Unlocked = 临时**：选择器一关就被 `HideAllWigets()` 隐藏（只隐藏未锁定的）；
- 解锁操作还会通过管道请求打开选择器（`LaunchSelectorFromWidget`）。
- widget 关闭时若配置了 `AlwaysShow`，则改为 Hide + `DispatcherTimer` 延时（默认 10s）后自动再 Show。

### 3.4 边界与屏幕适配

- `IsOutOfScreen()` 用 `SystemParameters.VirtualScreen*`（虚拟屏，支持负坐标多显示器）检查；
- 保存的位置出屏 → 回退 `DefaultSetting.Position`；
- `DisplaySettingsChanged`（分辨率/DPI 变化）后重新校验所有窗口位置；
- 拖到左边缘外 → 控制条改右对齐（`FlowDirection.RightToLeft`）防止菜单出屏；
- 窗口移动后 200ms 防抖（`locationChangeTimer`）再落盘。

### 3.5 悬停控制条

鼠标进入窗口 → 显示控制条（锁定框/菜单按钮）；离开 500ms 后（`mouseLeaveTimer`）隐藏。
窗口、控制条、内容区三个区域的 enter/leave 各自维护布尔位，`IsMousenEnter()` 取或。

### 3.6 选择器 + 遮罩（`MainWindowV2` + `OverlayBG`）

- `OverlayBG`：主屏 WorkingArea 大小的半透明遮罩窗，同样 `WS_EX_NOACTIVATE`，
  `SetWindowPos(HWND_TOPMOST, SWP_NOSIZE|SWP_NOACTIVATE)` 不激活置顶；点它 = 发 FuncType=4 关闭 overlay。
- 选择器本体也是 `Topmost + WS_EX_NOACTIVATE`。
- 点击外部关闭有**双保险**：BG 遮罩自己的 MouseDown + Helper 的 LL 鼠标钩子坐标判定
  （`OverlayWindowManager.IsClickInWidget(x,y)` 检查点是否落在某可见 widget 内）。

## 4. 热键链路（`OverlayHelper` + `KeyboardHook`）

- 热键**不在主程序注册**（没有 RegisterHotKey），而是常驻 Helper 里的 `WH_KEYBOARD_LL` 低级键盘钩子，
  钩子回调内用 `GetKeyState` 查修饰键状态，硬编码匹配 4 组组合，发出索引字符串：

| 索引 | 组合 | 实现判断 |
|---|---|---|
| 0 | Shift+F2 | Shift(160/161) 按下 && vk==113 && WM_KEYDOWN |
| 1 | Shift+F6 | vk==117 |
| 2 | Shift+F7 | vk==118 |
| 3 | Ctrl+Alt+F9 | Ctrl(162/163)+Alt(164/165) && vk==120 |

- 命中后经管道转发：主程序活着 → `HP.Omen.Overlay.UI`（FuncType=1）；
  没活着 → `HP.Omen.Overlay.BG.Hotkey`（OGH UWP 后台接力拉起 `OMENOverlayLauncher.exe`）。
- 钩子**不吞按键**，永远 `CallNextHookEx`（透传，不拦截游戏输入）。
- 用户只能在设置里从这 4 个固定组合里选一个（存索引 0-3，配置里 4 会被重置为 0）。

## 5. 数据链路

### FPS（PresentMon / ETW）
- 原生 DLL `HP.Omen.OMENPresentMonLib.dll`（非托管，Google PresentMon 的 HP 版），导出 4 个函数：
  `StartEtw() / StopEtw() / GenerateItems(out handle, out FPS_DATA_STRU*, out count) / ReleaseItems(handle)`，
  `FPS_DATA_STRU { uint pid; uint fps; uint runtime; }`。
- Helper 每秒一轮：取前台窗口进程（`GetForegroundWindow` → `GetWindowThreadProcessId`），
  排除 dwm 和自己，在 PresentMon 结果表里查该 pid 的 FPS；
  - 前台是 `ApplicationFrameHost`（UWP 游戏）→ 遍历表找父进程是 `svchost` 的呈现进程；
  - 前台是安卓模拟器（`Androws`）→ 取 `ABoxHeadless` 的帧率。
- **按需启停**：widget 经管道发 FuncType 3/4（订阅/退订）；没有任何订阅者就 `StopEtw()`，省 ETW 开销。

### CPU / GPU / NET
- widget 不在自己进程里采样，而是向 OGH 后台性能监控服务注册
  （`PerformanceMonitorHelper.RegisterPerformanceMonitoring(开关, 回调管道名, 类型, 1000ms)`），
  由后台服务每秒经管道推送 JSON（GPU=FuncType0 / CPU=1 / NET=6，含占用、温度、温度等级）。
- RAM 是 widget 本地取的（`RAM_USED/RAM_TOTAL`）。
- widget 端有个 1100ms 的低优先级后台线程把各项数据刷到 UI。
- 温度配色：白(正常)/绿#0FFA84(良好)/橙#FFA338(警告)/红#F9350F(危险)。

## 6. 插件系统

```
Overlay\Plugins\<Name>\<Name>.dll   ← 目录名必须等于程序集名
```
- `OverlayWidgetPluginManager.LoadPlugins`：扫目录 → `Assembly.LoadFrom` → 找导出类型里
  实现 `IOverlayWidgetPlugin` 的 → `Activator.CreateInstance`；
  挂 `AssemblyResolve` 先从插件目录、再从主程序目录找依赖；
  还要求 OGH UWP 包已安装（查 `%LocalAppData%\Packages\<UWPPFN>`）才加载。
- 接口核心成员：`ID / Category / Order / Icon / Setting(位置+透明+常驻+常锁) / DefaultSetting /
  OverlayWidget(UserControl)`，生命周期回调 `OnActived/OnDeactived/OnLocked/OnUnlocked/OnClosed`，
  外观回调 `OnBackgroundTransparencyChanged/OnTextTransparencyChanged/OnTextColorChanged`，
  配置读写 `ReadConfig/UpdateConfig/SaveConfig`（`Dictionary<string,bool>`），`ReadText` 做多语言。
- 窗口标题必须带 `[Overlay]` 前缀 —— 前台切换钩子靠它识别"这是自己人"。

## 7. 持久化

| 文件 | 内容 |
|---|---|
| `%LocalAppData%\Publishers\v10z8vjag6ke6\Overlay\Config.json` | 热键索引、背景/文字透明度、文字颜色、选择器位置 |
| 同目录 `WidgetState.json` | 每个 widget：Launched / Locked / PositionX / PositionY |
| `%LocalAppData%\HP\Overlay\` | 各 widget 自己的配置（显示哪些指标等） |
| `%LocalAppData%\OGH\OMENOverlay.log` | log4net 日志 |

透明度滑条 0-100 映射到实际不透明度：`(v * 0.9 + 10) / 100` → 0.10~1.00。

## 8. 与本项目（OmenSuperHub `Views/FloatingWindow`）对照

我们已经有的：`Topmost + AllowsTransparency + WS_EX_TOOLWINDOW + WS_EX_NOACTIVATE + WS_EX_TRANSPARENT`
（编辑/穿透切换）、DragMove、`WH_KEYBOARD_LL`、`RegisterHotKey`。HP 多出来的、值得借鉴的：

1. **前台切换重新置顶**（价值最高）：`SetWinEventHook(EVENT_SYSTEM_FOREGROUND)` +
   `SetWindowPos(HWND_TOPMOST, SWP_NOACTIVATE)`。我们的浮窗被游戏/其他 topmost 窗口压下去时
   目前没有恢复手段。注意：事件里要跳过自己的窗口（标题/句柄识别）。
2. **锁屏/睡眠隐藏**：`SessionSwitch` + Modern Standby + 恢复时等 `logonui` 退出，
   避免浮窗画到锁屏界面（安全+观感）。
3. **位置出屏回退**：虚拟屏范围校验 + `DisplaySettingsChanged` 重排，多显示器/换分辨率不丢窗。
4. **拖拽防抖落盘**（200ms Timer），别在 LocationChanged 里每帧写配置。
5. **热键放独立常驻进程**的取舍：HP 是因为 UWP 主程序不适合装全局钩子才拆出 Helper；
   我们是普通桌面进程，钩子装在自己进程里就行（现状已满足），不用学这个三进程架构。
6. **FPS 采集**：如果我们要做游戏内帧率显示，PresentMon（开源，github.com/GameTechDev/PresentMon）
   + ETW 就是标准答案；按需 `StartEtw/StopEtw` 的订阅制值得照抄。
7. **不建议抄的**：插件式架构（我们就几个固定卡片，YAGNI）、GA 遥测管道、
   每 widget 一个独立窗口（我们单浮窗 + 内部布局更简单）、UWP 包存在性检查。

### 可直接搬用的最小代码骨架（Z 序维持）

```csharp
// 前台切换钩子
[DllImport("user32.dll")]
static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr hmod,
    WinEventProc proc, uint pid, uint tid, uint flags); // WINEVENT_OUTOFCONTEXT=0

const uint EVENT_SYSTEM_FOREGROUND = 3;
SetWinEventHook(3, 3, IntPtr.Zero, (h, e, hwnd, o, c, t, ms) => {
    if (hwnd != 我的窗口句柄) ReassertTopmost();   // 忽略自己触发的事件
}, 0, 0, 0);  // 委托必须存静态引用防 GC！

void ReassertTopmost() {
    ShowWindow(hwnd, SW_SHOWNOACTIVATE);
    GetWindowRect(hwnd, out var r);
    SetWindowPos(hwnd, HWND_TOPMOST, r.Left, r.Top, r.Right-r.Left, r.Bottom-r.Top, SWP_NOACTIVATE);
}
```

## 9. 前端 UI 实现（XAML，从 BAML 反编译）

> 提取方法：`ilspycmd --resource "<程序集>.g.resources/<路径>.baml" <dll>`，BAML 直接反编译成 XAML。
> 提取产物在 `%TEMP%\omen-re\xaml\`。

### 9.1 通用窗口配方（三个窗口完全一致）

```
WindowStyle="None"  AllowsTransparency="True"  Background="#00FFFFFF"
ResizeMode="NoResize"  ShowInTaskbar="False"  SizeToContent="WidthAndHeight"
+ 代码里追加 WS_EX_NOACTIVATE
```
- **没有任务栏图标、没有系统边框、尺寸随内容自适应**（内容变更时代码手动同步 `Width/Height`）。
- 全部悬停反馈是 **Trigger 瞬时换色/换图**，整套叠加层**零动画**（没有 Storyboard）——游戏场景求稳不求炫。
- 字体：内嵌 Gotham Book/Bold OTF（商业字体，我们用开源替代即可）；图标字 Segoe MDL2 Assets；
  图标全是 PNG 双态资源（Normal/Hover 各一张）。
- 每个可交互控件都带 `AutomationProperties.AutomationId/Name`（他们的 UI 自动化测试基建，我们可省）。

### 9.2 widget 窗口布局（`OverlayWindowV2.xaml`）

```
Border(1px)
└─ Grid
   ├─ StackPanel 水平:  [ControlBar 控制条]  [AppContent → ContentRegion(插件 UserControl)]
   └─ Grid 浮动控制菜单(悬停菜单按钮弹出的设置面板，MinWidth=160, 圆角4, 2px边框)
       ├─ 标题行: 图标20×20 + 标题(GothamBold 14)
       ├─ 按钮行: 打开OGH / 打开选择器 / 设置（各 20×20 图标按钮 + ToolTip）
       └─ 按插件类型切换的设置区(ItemsControl + CheckBox 列表, 每行24高, 字号12)
```
- 控制条在内容**左侧**，竖排：锁按钮(24×24) + 菜单按钮(24×24)，圆角2、1px边框、悬停换底色。
- **设置菜单是同一窗口内的 Grid，不是 Popup** —— 避免 Popup 的激活/焦点问题（和窗口一起置顶、一起穿透），这个选择值得学。
- 菜单靠边时通过 `ControlMenuHorizontalAlignment` + 窗口 `FlowDirection` 翻转防止出屏（见第 3.4 节）。
- 内容容器 `ContentRegion` 圆角 8，插件 UserControl 直接塞进去，窗口随插件尺寸自适应。

### 9.3 数据卡片（`SystemVitals` 的观感，最值得抄的部分）

- 整窗宽 338；卡片在 `WrapPanel` 里两列流式排布：
  - 普通卡 **168×60**（一行两张）；奇数剩余项自动变 **336×60** 通栏（`_Basic_Extended` 样式，由 `IsLastExtendedStyle()` 计算）；
  - NET 卡固定 **334×60** 通栏（上传/下载双栏 + 18×18 箭头图标 + "Mbps" 单位）。
- 卡片背景：`LinearGradientBrush #FF323232 → #FF282828`（垂直），整体外层圆角 8、1px 边框。
- 文字层级（全部 GothamBold）：指标名 16 左上、**主数值 26**、副数值（温度等）18 右下、单位 12。
- **所有文字都挂 `DropShadowEffect`(黑, BlurRadius=4, ShadowDepth=1, Opacity=0.5)** ——
  游戏画面上保证可读性的关键，比描边便宜。
- 透明度是**双通道**的：背景不透明度、文字不透明度各一个滑条（0-100），分别绑到卡片背景
  和每个文字的 `Opacity`；温度颜色按等级 白→绿#0FFA84→橙#FFA338→红#F9350F。
- 冷知识：卡片模板挂在 `DataGrid` 的 `ControlTemplate` 上（纯粹拿它当带模板的容器用，没有表格语义），我们没必要学，用 ContentControl 即可。
- 装饰：两层透明 Border 做顶部高光边缘（`IsHitTestVisible="False"`）。

### 9.4 选择器面板（`MainWindowV2.xaml`）

- 屏幕**左下角**悬浮（三列 Grid 全部 `VerticalAlignment="Bottom"`），隐藏启动（`Visibility="Hidden"`）。
- 第 0 列：66 宽竖向工具条（双层 Border 66/60 制造嵌套感，圆角 7）：
  插件开关按钮列表（48 宽）→ 分隔图 → 热键/打开OGH/设置 图标按钮 → 分隔 → 关闭按钮。
- 第 1 列：设置面板 **327×356**（标题栏 48 高圆角 7-7-0-0 + 内容 308 高圆角 0-0-7-7，拼出分段卡片）：
  热键 ComboBox、背景不透明度 Slider、文字不透明度 Slider、文字颜色色板（WrapPanel 色块）。
- 第 2 列：热键快捷面板 **327×103**（只含热键选择）。
- Slider 都是 0-100、`IsSnapToTickEnabled` + `TickFrequency=1`，两侧放 0/100 刻度文字。

### 9.5 全屏遮罩（`OverlayBG.xaml`）

```xml
<Window WindowState="Maximized" WindowStyle="None" AllowsTransparency="True" Background="{x:Null}">
  <Rectangle Fill="#FF000000" Opacity="0.4">
    <Rectangle.Effect><DropShadowEffect BlurRadius="12" ShadowDepth="5" RenderingBias="Performance"/></Rectangle.Effect>
  </Rectangle>
</Window>
```
就这么多 —— 遮罩只是 40% 黑 + 一个性能优先的阴影，点击发管道消息关闭。

### 9.6 主题/换肤机制（`ThemeSelector`）

- 所有颜色全走 `DynamicResource`，主题 = 一个资源字典文件：
  `/HP.Omen.Core.UI.Common;component/Themes/{DefaultTheme|HighContrastDarkTheme|HighContrastLightTheme}.xaml`；
  换肤 = 从 `Application.Current.Resources.MergedDictionaries` 里移除旧的、加新的，外加浅色的
  `LightThemeColorsOverrides.xaml` 覆盖层。失败则回退 DefaultTheme。
- 主题来源三合一：
  1. 注册表 `HKCU\Software\HP\OMEN Ally\Settings\ThemePreference`（Dark/Light/FollowOS，默认 Dark）；
  2. FollowOS 时订阅 WinRT `UISettings.ColorValuesChanged` 跟随系统深浅色；
  3. 高对比度：WinRT `AccessibilitySettings.HighContrast` + 按 locale 的白底方案名表 → 切高对比主题。
- widget 侧监听 `SystemEvents.UserPreferenceChanged`（Accessibility 类别）重新套主题。
- 注：Overlay 目录下的 `HP.Omen.Core.UI.Common.dll` 是精简副本，不含这些主题字典资源
  （完整资源在 OGH UWP 包内），机制如上，配色值需从 UWP 包里再提。

### 9.7 对照本项目浮窗的可借鉴点（前端侧）

| 做法 | 我们现状 | 建议 |
|---|---|---|
| 文字全部带投影保证游戏画面上可读 | 无 | 值得加，一个 `DropShadowEffect` 的事 |
| 背景/文字透明度分开调 | 整体 Opacity | 看需求，双滑条体验更好但多一个配置项 |
| 悬停控制条 + 同窗设置菜单（非 Popup） | — | 若做卡片编辑菜单，用窗口内 Grid 别用 Popup |
| 固定尺寸卡片 + WrapPanel 流式 + 末位通栏 | — | 直接可用的排版套路 |
| 零动画、Trigger 瞬时态 | — | 叠加层场景合理，保持 |
| `SizeToContent` + 手动同步尺寸 | 已类似 | — |

## 10. 深挖：硬件控制与数据链路

> 核心结论：**叠加层本身从不直接读写硬件**。所有性能模式/风扇控制、所有传感器遥测，
> 都通过命名管道委托给 OGH（OMEN Gaming Hub）的后台服务。叠加层只是 UI 端点。

### 10.1 硬件控制路径（`PerformanceControlFg` 协议）

PerformanceControl/FanMonitor 插件通过管道 `PerformanceControlFg<SessionId>`（PipeClientV2）
向 OGH 后台发送 `PerformanceControlMsg { int Command; object Data }`：

| Command | 方向 | 含义 |
|---|---|---|
| 21 | → | 设置性能模式（Data = PerformanceMode 枚举） |
| 23 | → | 设置温控模式（Data = `PerformanceMode*1000 + ThermalControl`，两个值打包成一个数） |
| 25 | → | 设置传统风扇模式（Data = FanMode） |
| 27 | → | 请求风扇列表（仅台式机；回复经 `...FanMonitorPlugin.BG` 管道推回） |
| 29 | → | 初始化握手（插件每 5s 重试，直到后台回包） |

后台 → 插件的推送（插件自己开 `HP.Omen.Overlay.Plugin.PerformanceControlPlugin<sid>` 管道接收）：

| FuncType | 含义 |
|---|---|
| 29 | 初始化回复：`PerformanceControlInitial` JSON（当前模式、支持模式列表、Extreme 是否解锁、温控 UI 类型、legacy 风扇模式、是否可用） |
| 22 | 当前性能模式变更 |
| 24 / 26 | 温控模式 / legacy 风扇模式变更 |
| 7 / 11 | 自动切换 ECO / 自动切换 Performance 标志（电池/游戏场景联动） |
| 28 | 功能可用性（不可用时 UI 蒙灰） |

枚举值（反编译确认）：
- `PerformanceMode`：0=Default、1=Performance、2=Cool、3=Quiet、4=Extreme/Unleashed、**256=Eco**；
- UI 侧 `PerformanceModeOnUI`：5=Balanced（与 Default 同值显示）、6=Eco、7=Unleash；
- 温控 UI 五档：Max+Auto+Manual / Max+Auto / Auto+Manual / Quiet-Normal-Turbo / AutoOnly，
  按机型能力由初始化包决定显示哪一套；
- 规则细节：Eco 或自动切 Eco 时禁用温控调节；Performance/Unleashed + Quiet-Normal-Turbo 机型下切模式会联动把风扇拉到 Turbo。

**含义**：真正的 EC/固件写入发生在 OGH 后台（UWP 包内，本机无权限读取其程序集）。
叠加层换性能模式 = 发一条管道消息，界面状态全靠后台回推保持同步。

### 10.2 遥测订阅路径（`PerformanceMonitorHelper`）

CPU/GPU/NET/风扇/温度等数据不自己采样，而是向 OGH 的 PerformanceMonitorBg 服务**注册订阅**：

```
注册管道名: HP.Omen.Features.Hardware.PerformanceMonitorBg.<类型>.Registration<SessionId>
注销管道名: ...<类型>.Degegistration<SessionId>   // 原文如此，拼写错误是原样
消息: PerseusRevMsg { FuncType=(int)类型, SendParameter="<回调管道名>+period<毫秒>" }
```

`PerformanceMonitorRegisterType` 枚举即推送时的 FuncType：

| 值 | 类型 | 值 | 类型 |
|---|---|---|---|
| 0 | GPU 占用/温度 | 6 | 网络上下行 |
| 1 | CPU 占用/温度 | 7/8 | CPU 功耗 / 架构 |
| 2/3 | GPU/CPU 明细 | 10/11/12 | IR 温度 / 环境温度 / VR 温度 |
| 4/5 | 风扇(笔记本/台式) | | |

订阅后，后台每 1000ms 向调用方提供的回调管道推送一次
`PerformanceMonitorData_Simple`（占用/温度/温度等级）或 `PerformanceMonitorData_Fan`
（`FanSpeed` 单值或 `FanSpeedList` 按 FanIndex 数组）等 JSON。
FanMonitor 插件显示的风扇转速就是这么来的；0 转显示 "Inactive"，转速按百位补零对齐。
风扇的**名称/索引/类型列表**则走 10.1 的 Command=27 向 PerformanceControlFg 请求。

### 10.3 FPS：PresentMon ETW 机制（`HP.Omen.OMENPresentMonLib.dll` 原生）

- 确认是 **Google PresentMon 的 HP fork**（字符串里有全套 `PMTraceConsumer::HandleDXGKEvent/
  HandleWin32kEvent/HandleDxgkFlip/RuntimePresentStop/CompletePresent`、帧类型
  "Composed: Flip"/"Hardware: Independent Flip"/"Hardware: Legacy Flip"，以及 2.x 新增的
  GpuTrace DMA 包追踪与 Intel provider 支持）。
- 导入表即 ETW 消费链：`StartTraceW/ControlTraceW/OpenTraceW/EnableTraceEx2/ProcessTrace`
  + `tdh.dll` 事件解码。
- 二进制内 provider GUID 表（字节精确匹配）：**Microsoft-Windows-D3D9**、
  **Microsoft-Windows-Win32k**、**Microsoft-Windows-DxgKrnl**，另有一个与标准
  DXGI provider 仅末字节不同的 GUID（`CA11C036-0102-4A2D-A6AD-F03CFED5D3C9`，
  标准值为 …D3ED）——疑似 HP 改动或私有变体。
- 导出：`StartEtw / StopEtw / GenerateItems(out FPS_DATA_STRU{pid,fps,runtime}*) / ReleaseItems
  / PresentMonBridge_MarkSessionEnded`。
- OverlayHelper 的调度（前面已述）：有订阅者才 `StartEtw`；1 秒一轮
  `GenerateItems` → 按前台窗口 PID 取 FPS（UWP 游戏回退查父进程为 svchost 的呈现进程，
  安卓模拟器回退 Androws/ABoxHeadless）→ 管道分发给各 widget。

### 10.4 `OMENFUNC.dll` 实勘

只有 3 个导出：`GetAmdGpuTemperature / GetAmdGpuUsage / QueryAmdGpuInfoAll`，
导入表只有 KERNEL32（靠 `LoadLibraryW/GetProcAddress` 运行时动态挂 AMD 的库）。
**它是 AMD GPU 查询薄壳，不是风扇/性能后端**——风扇与性能模式的硬件通道在 OGH 服务内。

### 10.5 管道协议：安全与线上格式（V3）

- 传输：`NamedPipeServerStream`，InOut、**Message 模式**、10KB 缓冲，
  ACL 允许 `BUILTIN\Users` 读写。
- **对端验证**（`ConnectedPipeVerifier`）：取对端进程 PID → 读进程创建时间（防 PID 重用，
  与缓存的已验证表比对）→ `WinTrust.VerifyEmbeddedSignature` 校验对端 exe 的
  Authenticode 签名，且证书 Subject 必须含 **`CN=HP Inc.,`**。验证结果按 (PID+创建时间)
  缓存最多 100 条。**第三方进程接不进这些管道**。
- 载荷：消息对象 → JSON → `CryptUtilV2.EncryptString` → UTF-8 写入。
  加密是 `ProtectedData.Protect`（**DPAPI，CurrentUser 域**），entropy 取
  `SystemIdentification.GetSystemIdForUser()` 硬件 ID 的前 16 字节，输出 Base64。
  即"机器绑定 + 用户绑定"的防窥探层，不是强加密；访问控制靠上面的签名验证。
- V2 管道（如 `PerformanceControlFg`）无加密验证层，直接 JSON。
- 消息基类 `BasePipeMessage` 用 `[XmlInclude]` 声明了可携带的派生类型：
  `PerseusRevMsg`（FuncType+SendParameter 万能信封）、`PerformanceControlMsg`、
  `MessageDragonKB`、`DynamicCrosshairData`。

### 10.6 对本项目的含义

1. **想复用 OGH 的遥测（温度/占用/风扇转速）**：订阅 10.2 的 Registration 管道即可，
   但我们的进程没有 HP 签名，V3 验证管道会被拒；V2 管道（PerformanceControlFg 等）
   没有签名验证，理论上可连——前提是 OGH 后台在运行且管道 ACL 允许。自测时可用
   管道监视器（如 Sysinternals Pipelist + API Monitor）验证。
2. **想控制性能模式**：发 Command=21/23/25 到 `PerformanceControlFg<sid>`。同样受
   后台存活与管道可见性约束，且不同机型支持的模式集合以 Command=29 初始化包为准。
3. **风险**：与 OGH 同时写同一硬件通道可能互相打架（两个主控抢 EC）。若本项目
   已直接做风扇/性能控制（OmenHardware.cs/PresetManager），更稳的路线是保持自研通道，
   只把 HP 的**协议设计**当参考（订阅制遥测、命令/回推分离、初始化握手）。
4. **FPS 采集**：直接集成开源 PresentMon（ETW）即可，HP 的用法（按需启停 + 前台 PID
   匹配 + UWP/模拟器回退）就是完整范例。

## 11. 文件速查（反编译产物内）

| 关注点 | 文件 |
|---|---|
| 叠加窗口本体 | `lib/OMENOverlayLib.Views/OverlayWindowV2.cs` |
| 窗口管理/置顶/锁屏 | `lib/OMENOverlayLib.Models/OverlayWindowManager.cs` |
| Win32 帮助函数 | `lib/OMENOverlayLib.Utilities/WindowHelper.cs`、`exe/OMENOverlay.Utilities/WindowHelper.cs` |
| 前台切换钩子 | `lib/OMENOverlayLib.Utilities/ActiveWindow.cs` |
| 热键/鼠标钩子 + FPS | `helper/HP.Omen.Overlay.OverlayHelper/Program.cs`、`KeyboardHook.cs` |
| 热键/显隐/管道协议 | `exe/OMENOverlay.ViewModels/MainWindowV2ViewModel.cs` |
| 遮罩窗 | `exe/OMENOverlay.Views/OverlayBG.cs` |
| 插件接口 | `plugincommon/OMENOverlay.OverlayPluginCommon/IOverlayWidgetPlugin.cs` |
| 插件加载 | `lib/OMENOverlayLib.Models/OverlayWidgetPluginManager.cs` |
| 数据轮询示例 | `vitals/SystemVitals.ViewModels/SystemVitalsWidgetViewModel.cs` |
| 性能模式插件协议 | `plug_PerformanceControlPlugin/.../PerformanceControlWidgetViewModel.cs` |
| 风扇转速插件 | `plug_FanMonitorPlugin/.../FanMonitorWidgetViewModel.cs` |
| 软件风扇算法 SDK | `perfctrl/Hp.Bridge.Client.SDKs.PerformanceControl.Handlers/FanHandler.cs`（EWMA/滑条/Idle 三种策略） |
| 遥测订阅入口 | `HP.Omen.Core.Common.dll` → `PerformanceMonitorHelper`（`ilspycmd -t` 单类反编译） |
| 管道验证/加密 | `HP.Omen.Core.Common.dll` → `ConnectedPipeVerifier`、`SerializerJsonV2`；`HP.Omen.Core.Utilities.dll` → `CryptUtilV2` |
| 热键硬编码判断 | `helper/.../KeyboardHook.cs`（vk 113/117/118/120） |

XAML（`%TEMP%\omen-re\xaml\`，`ilspycmd --resource <程序集>.g.resources/<路径>.baml <dll>` 提取）：

| 关注点 | 文件 |
|---|---|
| widget 窗口布局 | `overlaywindowv2.xaml`（lib `views/overlaywindowv2.baml`） |
| 控制条/菜单/锁按钮样式 | `overlaywindowstyle.xaml`（lib `styles/overlaywindowstyle.baml`） |
| 选择器面板 | `mainwindowv2.xaml`（exe `views/mainwindowv2.baml`） |
| 全屏遮罩 | `overlaybg.xaml`（exe `views/overlaybg.baml`） |
| App 级资源合并 | `app.xaml`（exe `app.baml`） |
| 数据卡片样式 | `vitals_systemvitalsstyle.xaml`（SystemVitals.dll `styles/systemvitalsstyle.baml`） |
| 卡片容器 | `vitals_widget.xaml`（SystemVitals.dll `views/systemvitalswidget.baml`） |
| 换肤逻辑 | `%TEMP%\omen-re\themeselector.cs`（HP.Omen.Core.Utilities.dll 的 `ThemeSelector` 类） |
