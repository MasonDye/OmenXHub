<div align="center">

# OMEN X Hub

![OMEN X Hub](Preview/Dashboard.png)

**OMEN X Hub** — A lightweight, offline replacement for HP OMEN Gaming Hub.
No advertisements · No wallpapers · No network connections.

简体中文 · [繁體中文](README.zh-Hant.md) · [English](README.en.md)

</div>

---

> 轻量、离线的 HP OMEN Gaming Hub 替代品 —— 无广告、无壁纸、无联网。

**OMEN X Hub**（参考和主要功能来源于 OmenSuperHub）是一个基于 WPF 的 OMEN / VICTUS 游戏本控制中心，提供全面的硬件监控、风扇控制、性能调优、键盘灯光和系统诊断功能，无需安装臃肿的官方 OGH 软件。

## 功能一览

### 仪表板 (Dashboard)

实时显示 CPU/GPU 温度、使用率、频率、功耗、风扇转速、内存占用。颜色编码进度条（绿→黄→红）直观反映负载状态。

![仪表板](Preview/Dashboard.png)

### 性能控制 (Performance)

CPU 功率限制 (PL1/PL2)、IccMax、AC Load Line、电源模式/计划、EcoQoS 效率模式、Core Keep 核心保持。GPU 频率限制、核心超频、显存超频、TGP/PPAB、dState、DB 版本、图形模式切换、热切换、屏幕刷新率、最大帧率。

| CPU 控制 | GPU 控制 |
|:---:|:---:|
| ![CPU](Preview/Perf-CPU.png) | ![GPU](Preview/Perf-GPU.png) |

### 风扇控制 (Fan)

风扇模式（自动/最大/固定 RPM）、温度灵敏度、自定义风扇曲线（拖拽控制点）、高温自动保护、风扇除尘。

![风扇](Preview/Fan.png)

### 灯光控制 (Lighting)

键盘/灯条设备，Basic/Dojo 四分区协议。10 种动画效果、4 区独立颜色、亮度与速度控制。

![灯光](Preview/Lighting.png)

### 自动化 (Automation)

16 种触发条件（进程启动/停止、锁定/解锁、电源插拔、外接显示器、定时、CPU/GPU 温度、电池电量等）+ 23 种执行步骤（预设、刷新率、电源、WiFi/蓝牙、亮度、音频、宏等）。快捷操作可从托盘一键触发。

| 流水线列表 | 编辑管道 |
|:---:|:---:|
| ![自动化](Preview/Automation.png) | ![编辑](Preview/Automation-Edit.png) |

### 键盘宏 (Macro)

录制/回放键盘操作序列，支持触发快捷键、事件编辑。

![宏](Preview/Macro.png)

### 其他 (Other)

智能充电、数字锁定、大写锁定、触摸板锁定、HWiNFO64 集成、HTTP API 服务。

![其他](Preview/Other.png)

### 系统信息 (SysInfo)

系统硬件详情、PawnIO 驱动状态、传感器温度、GPU 进程管理、监控选项。

![系统信息](Preview/SysInfo.png)

### 设置 (Settings)

浮窗显示（位置/字体/透明度/多显示器）、Omen 键（5 种行为）、OSD 提示、托盘图标（原版/自定义/动态）、开机自启、自定义主界面 LOGO、主题（系统/深色/亮色）、语言、自定义背景（透明度/高斯模糊）、数据本地化、调试日志。

| 浮窗 & Omen 键 | 主题 & 背景 |
|:---:|:---:|
| ![设置](Preview/Settings.png) | ![系统](Preview/Settings-System.png) |

## 预设管理

| 预设 | PL1 | PL2 | 风扇 | TGP/PPAB | GPU 频率上限 |
|------|-----|-----|------|----------|-------------|
| 极致性能 (Extreme) | 254W | 254W | Cool | 255W | 无限制 |
| GPU 优先 (GpuPriority) | 45W | 45W | Cool | 255W | 无限制 |
| 轻度使用 (LightUse) | 25W | 25W | Silent | 关闭 | 无限制 |
| 自定义 (Custom 1-3) | 用户保存 | 用户保存 | 用户保存 | 用户保存 | 用户保存 |

仅 CPU 功率、电源计划、GPU 频率上限、TGP+PPAB、dState 跟随预设绑定；灯光、宏、音频等参数独立保存。

## 支持硬件

| 状态 | 型号 |
|------|------|
| 已确认 | 暗影精灵 8 Plus · 8 Plus Plus · 9 · 9 Plus · 10 · 光影精灵 10 · 光影精灵 10 (Victus) · OMEN 16 (Ryzen) · OMEN 15 · OMEN Phantom Gaming |
| 不支持 | 暗影精灵 6 |

> 主要针对 **OMEN 10 Intel (i7-13650HX + RTX 4070)** 开发，兼容性不保证适用于所有平台。

### 环境要求

- HP OMEN / VICTUS 游戏本，具有 WMI BIOS 接口
- Windows 10/11 64-bit · .NET Framework 4.8
- 管理员权限（WMI、风扇控制、驱动安装所需）

## 快速开始

1. **关闭 OGH** — 结束 `OmenCommandCenterBackground.exe` 或卸载 OGH 以避免冲突。
2. **以管理员身份运行** — 硬件控制需要提权。
3. **启动 `OmenXHub.exe`** — 程序在系统托盘运行。
4. **右键托盘图标** 切换性能模式或打开控制面板。
5. **在设置中启用开机自启** 以长期替代 OGH。

> ⚠️ DB (Dynamic Boost) 解锁需要 NVIDIA 驱动版本 ≥ 537.42 且 < 610.47。50 系列 GPU 不支持解锁。

## 构建

```cmd
dotnet restore OmenSuperHub.csproj
set MSBuildSDKsPath=C:\Program Files\dotnet\sdk\8.0.418\Sdks
set MSBuildEnableWorkloadResolver=false
MSBuild.exe OmenSuperHub.csproj /t:Build /p:Configuration=Release /p:Platform=x64
```

输出：`bin\x64\Release\OmenXHub.exe`（单文件 — 所有 DLL 通过 Costura.Fody 嵌入）

## 致谢

- **MasonDye** — GUI 设计与 WPF 前端开发
- **One1turn** - WPF-UI Windows 11 样式重写 + 性能优化
- **breadeding** — [OmenSuperHub](https://github.com/breadeding/OmenSuperHub)（原始框架与代码）
- **GeographicCone** — [OmenMon](https://github.com/GeographicCone) / [OmenHwCtl](https://github.com/GeographicCone)（灵感来源与 OGH 交互研究）
- **OpenHardwareMonitor** — [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor)（硬件监控核心库）

## 免责声明

OMEN X Hub **与 HP 或 OMEN 无关联**。品牌名称仅作参考。本软件直接与硬件交互，可能存在潜在风险。**使用风险自负。**
