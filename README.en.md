<div align="center">

# OMEN X Hub

![OMEN X Hub](Preview/Dashboard.png)

**OMEN X Hub** — A lightweight, offline replacement for HP OMEN Gaming Hub.
No advertisements · No wallpapers · No network connections.

[简体中文](README.md) · [繁體中文](README.zh-Hant.md) · English

</div>

---

> A lightweight, offline replacement for HP OMEN Gaming Hub — no advertisements, no wallpapers, no network connections.

**OMEN X Hub** (formerly OmenSuperHub) is a WPF-based control center for HP OMEN / VICTUS gaming laptops. It provides comprehensive hardware monitoring, fan control, performance tuning, keyboard lighting, and system diagnostics — all without the bloat of the official OGH software.

## Features

### Dashboard

Real-time CPU/GPU temperature, usage, frequency, power, fan speed, RAM usage, and network speed. Color-coded progress bars (green → yellow → red) reflect load at a glance.

![Dashboard](Preview/Dashboard.png)

### Performance Control

CPU power limits (PL1/PL2), IccMax, AC Load Line, power plan/mode, EcoQoS efficiency mode, Core Keep. GPU frequency lock, core/memory overclock, TGP/PPAB, dState, DB version, graphics mode switching, hot switch, display refresh rate, max frame rate.

| CPU Control | GPU Control |
|:---:|:---:|
| ![CPU](Preview/Perf-CPU.png) | ![GPU](Preview/Perf-GPU.png) |

### Fan Control

Fan modes (Auto / Max / Fixed RPM), temperature sensitivity, custom fan curve (drag-and-drop), high-temp auto-protection, fan dust removal.

![Fan](Preview/Fan.png)

### Lighting Control

Keyboard / light bar devices, Basic/Dojo 4-zone protocols. 10 animation effects, per-zone colors, brightness and speed control.

![Lighting](Preview/Lighting.png)

### Automation

16 trigger types (process start/stop, session lock/unlock, AC/DC, display connect/disconnect, schedule, CPU/GPU temp, battery level) + 23 step types (preset, refresh rate, power, WiFi/Bluetooth, brightness, audio, macro, etc.). Quick actions triggerable from tray menu.

| Pipeline List | Edit Pipeline |
|:---:|:---:|
| ![Automation](Preview/Automation.png) | ![Edit](Preview/Automation-Edit.png) |

### Keyboard Macro

Record and replay keyboard sequences, with trigger hotkey support and event editing.

![Macro](Preview/Macro.png)

### Other

Smart charging, Num Lock, Caps Lock, touchpad lock, HWiNFO64 integration, HTTP API service.

![Other](Preview/Other.png)

### System Info

Hardware details, PawnIO driver status, sensor temperatures, GPU process management, monitoring options.

![SysInfo](Preview/SysInfo.png)

### Settings

Overlay display (position / font / opacity / multi-monitor), Omen Key (5 behaviors), OSD toast, tray icon (default / custom / dynamic), auto-start, custom main logo, theme (system / dark / light), language, custom background (opacity / Gaussian blur), data localization, debug log.

| Overlay & Omen Key | Theme & Background |
|:---:|:---:|
| ![Settings](Preview/Settings.png) | ![System](Preview/Settings-System.png) |

## Preset Management

| Preset | PL1 | PL2 | Fan | TGP/PPAB | GPU Freq Limit |
|--------|-----|-----|-----|----------|----------------|
| Extreme | 254W | 254W | Cool | 255W | Unlimited |
| GpuPriority | 45W | 45W | Cool | 255W | Unlimited |
| LightUse | 25W | 25W | Silent | Off | Unlimited |
| Custom 1-3 | User saved | User saved | User saved | User saved | User saved |

Only CPU power, power plan, GPU frequency limit, TGP+PPAB, and dState are bound to presets; lighting, macros, audio, and other params persist independently.

## Supported Hardware

| Status | Models |
|--------|--------|
| Confirmed | 暗影精灵 8 Plus · 8 Plus Plus · 9 · 9 Plus · 10 · 光影精灵 10 · 光影精灵 10 (Victus) · OMEN 16 (Ryzen) · OMEN 15 · OMEN Phantom Gaming |
| Not supported | 暗影精灵 6 |

> Primarily developed for **OMEN 10 Intel (i7-13650HX + RTX 4070)**. Compatibility not guaranteed on all platforms.

### Requirements

- HP OMEN / VICTUS gaming laptop with WMI BIOS interface
- Windows 10/11 64-bit · .NET Framework 4.8
- Administrative privileges (required for WMI, fan control, driver installation)

## Getting Started

1. **Close OGH** — shut down `OmenCommandCenterBackground.exe` or uninstall OGH to avoid conflicts.
2. **Run as Administrator** — all hardware control requires elevated privileges.
3. **Launch** `OmenXHub.exe` — the app runs from the system tray.
4. **Right-click tray icon** to switch performance modes or open the control panel.
5. **Enable auto-start** in settings for long-term OGH replacement.

> ⚠️ DB (Dynamic Boost) unlock requires NVIDIA driver version ≥ 537.42 and < 610.47. 50-series GPUs are not supported for unlock.

## Build

```cmd
dotnet restore OmenSuperHub.csproj
set MSBuildSDKsPath=C:\Program Files\dotnet\sdk\8.0.418\Sdks
set MSBuildEnableWorkloadResolver=false
MSBuild.exe OmenSuperHub.csproj /t:Build /p:Configuration=Release /p:Platform=x64
```

Output: `bin\x64\Release\OmenXHub.exe` (single-file — all DLLs embedded via Costura.Fody)

## Acknowledgments

- **MasonDye** — GUI design & WPF front-end development
- **One1turn** - WPF-UI Windows 11 style rewrite + performance optimization
- **breadeding** — [OmenSuperHub](https://github.com/breadeding/OmenSuperHub) (original framework & code)
- **GeographicCone** — [OmenMon](https://github.com/GeographicCone) / [OmenHwCtl](https://github.com/GeographicCone) (inspiration & OGH interaction research)
- **OpenHardwareMonitor** — [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) (hardware monitoring core)

## Disclaimer

OMEN X Hub is **not affiliated with HP or OMEN**. Brand names are used for reference only. This software interacts directly with hardware and may carry potential risks. **Use at your own risk.**
