# OverlayDemo — 游戏内系统信息叠加层（WPF）

桌面常驻叠加层演示：置顶显示系统指标卡片，可拖拽、钉住、换色、调透明度，
热键呼出/收起。目标框架 net481 WPF（与主程序 OmenSuperHub 一致，便于日后合并）。

## 运行

```bash
dotnet run --project OverlayDemo                 # 正常启动
dotnet run --project OverlayDemo -- --selftest   # 30 项逻辑自检
```

单实例（Mutex，按会话隔离）。配置存 `%LocalAppData%\OverlayDemo\config.xaml`（XamlWriter 序列化，零依赖）。

## 界面与操作

### Widget（每个独立一个小窗，可同时开多个）

| Widget | 内容 | 操作 |
|---|---|---|
| System Vitals | CPU/GPU/RAM/FPS/NET 大卡片，WrapPanel 两列流式，奇数选中项自动通栏 | 左键拖动；悬停左侧控制条（锁/菜单） |
| System Vitals Compact | 同款指标卡纵向堆叠（168×60 独立卡） | 同上 |
| Fan Monitor | 风扇行（185×24 圆角6）+ 右侧 CPU/GPU 温度面板（115×40），转速按百位补零，停转显示 Inactive | 同上 |
| Performance Control | 316×126 两段面板：上段 ECO/默认/性能/极致 模式按钮（140×24 圆角4 图标+文字），下段 CPU/GPU 温度 | 同上；按钮组互斥选中 |

悬停控制条菜单：打开选择器 / 设置 / 关闭 widget；System Vitals 菜单内含五个指标的显示开关。

### 选择器（左下角 rail）

外框 66 圆角7 嵌内框 60：四个 48×48 widget 开关（图标字形，选中亮边）、热键按钮（显示当前组合）、设置、收起。
点击遮罩空白处也可整体收起。**锁定（钉住）的 widget 收起后仍然常驻**。

### 设置面板

热键（Shift+F2 / Shift+F6 / Shift+F7 / Ctrl+Alt+F9）、背景不透明度滑条、文字不透明度滑条、9 色文字色板。

## 关键机制

- `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`：永不抢焦点、不进 Alt+Tab
- `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)`：前台每次切换把所有应显示的 overlay
  重新 `SetWindowPos(HWND_TOPMOST)`（遮罩 → 选择器 → widget，保持层叠顺序）
- 锁屏（`SessionSwitch`）隐藏全部，解锁恢复
- 拖动结束 clamp 进虚拟屏 + 200ms 防抖落盘；贴屏幕左缘时菜单自动右对齐
- 控制条悬停显示、离开 500ms 延迟收起
- 卡片规格：168×60 基础卡、336×60 通栏、334×60 网络双列；垂直渐变 `#323232→#282828`；
  全部文字带黑投影（`BlurRadius=4, ShadowDepth=1, Opacity=0.5`）保证游戏画面上可读
- 设置面板两段拼接（标题 48 圆角 7,7,0,0 + 内容 308 圆角 0,0,7,7），滑条为 8 高圆角轨道 + 16 圆 thumb，
  色板为 18/16 双层圆（选中外圈变白）
- 温度配色：`<45 绿 #0FFA84 / <65 白 #EEEEEE / <80 橙 #FFA338 / ≥80 红 #F9350F`
- 透明度映射：`(滑条值 × 0.9 + 10) / 100`（0→0.10，100→1.00）
- 控制菜单是同窗 Grid 而非 Popup（跟随窗口置顶，无焦点问题）

## 数据源

`MockTelemetry`：单一全局数据流，随机游走模拟传感器，1100ms 刷新；
转速跟随负载。各 widget VM 订阅 `Updated` 事件读取快照。
后续接真实数据时，只需替换 `MockTelemetry.Tick` 的取数逻辑（如 LibreHardwareMonitor / PresentMon），UI 层不变。

## 与生产版的有意取舍

| 项 | 现状 | 升级路径 |
|---|---|---|
| 数据 | 随机游走模拟 | 接 LibreHardwareMonitor / PresentMon（ETW） |
| 热键 | `RegisterHotKey` | 需要游戏全屏下也生效时改 `WH_KEYBOARD_LL` 钩子 |
| 性能模式 | 仅选中态切换 | 接真实模式切换命令 |
| 字体/图标 | Segoe UI / Segoe MDL2 Assets | 可换品牌资产 |
| 退出 | 任务管理器结束进程 | 需要时加托盘图标 |

## 文件结构

```
OverlayDemo/
├─ OverlayDemo.csproj          net481 WPF
├─ App.xaml / App.xaml.cs      装配：多 widget 编排、热键、前台钩子、锁屏、自检入口
├─ Styles.xaml                 视觉规格（卡片/控制条/模式按钮/色板/Tooltip）
├─ Core/
│   ├─ Win32.cs                非激活置顶 / 扩展样式 / 前台切换钩子 / 热键
│   ├─ AppConfig.cs            配置（per-widget 状态）+ XamlWriter 持久化
│   ├─ CardItem.cs             卡片 VM + 通栏卡逻辑 + 温度色
│   ├─ MockTelemetry.cs        全局模拟数据流
│   └─ SelfTest.cs             --selftest（30 断言）
├─ Widgets/                    Vitals / Compact / Fan / Perf 四个 widget
└─ Windows/
    ├─ OverlayWidgetWindow.*   widget 窗口（控制条/悬停菜单/拖拽/锁定，按标题与设置区参数化）
    ├─ SelectorWindow.*        左下 rail + 设置面板
    └─ OverlayBgWindow.*       全屏 40% 遮罩
```
