<div align="center">

# OMEN X Hub

![OMEN X Hub](Preview/Dashboard.png)

**OMEN X Hub** — A lightweight, offline replacement for HP OMEN Gaming Hub.
No advertisements · No wallpapers · No network connections.

[简体中文](README.md) · 繁體中文 · [English](README.en.md)

</div>

---

> 輕量、離線的 HP OMEN Gaming Hub 替代品 —— 無廣告、無牆紙、無聯網。

**OMEN X Hub**（原名 OmenSuperHub）是一個基於 WPF 的 OMEN / VICTUS 遊戲筆電控制中心，提供全面的硬體監控、風扇控制、效能調校、鍵盤燈光和系統診斷功能，無需安裝臃腫的官方 OGH 軟體。

## 功能一覽

### 儀表板 (Dashboard)

即時顯示 CPU/GPU 溫度、使用率、頻率、功耗、風扇轉速、記憶體佔用、網速。顏色編碼進度條（綠→黃→紅）直觀反映負載狀態。

![儀表板](Preview/Dashboard.png)

### 效能控制 (Performance)

CPU 功率限制 (PL1/PL2)、IccMax、AC Load Line、電源模式/計畫、EcoQoS 效率模式、Core Keep 核心保持。GPU 頻率限制、核心超頻、記憶體超頻、TGP/PPAB、dState、DB 版本、圖形模式切換、熱切換、螢幕更新率、最大幀率。

| CPU 控制 | GPU 控制 |
|:---:|:---:|
| ![CPU](Preview/Perf-CPU.png) | ![GPU](Preview/Perf-GPU.png) |

### 風扇控制 (Fan)

風扇模式（自動/最大/固定 RPM）、溫度靈敏度、自訂風扇曲線（拖曳控制點）、高溫自動保護、風扇除塵。

![風扇](Preview/Fan.png)

### 燈光控制 (Lighting)

鍵盤/燈條裝置，Basic/Dojo 四分區協議。10 種動畫效果、4 區獨立顏色、亮度與速度控制。

![燈光](Preview/Lighting.png)

### 自動化 (Automation)

16 種觸發條件（程序啟動/停止、鎖定/解鎖、電源插拔、外接顯示器、定時、CPU/GPU 溫度、電池電量等）+ 23 種執行步驟（預設、更新率、電源、WiFi/藍牙、亮度、音訊、巨集等）。快捷操作可從匣標（系統列）一鍵觸發。

| 流水線清單 | 編輯管道 |
|:---:|:---:|
| ![自動化](Preview/Automation.png) | ![編輯](Preview/Automation-Edit.png) |

### 鍵盤巨集 (Macro)

錄製/回放鍵盤操作序列，支援觸發快捷鍵、事件編輯。

![巨集](Preview/Macro.png)

### 其他 (Other)

智慧充電、數字鎖定、大寫鎖定、觸控板鎖定、HWiNFO64 整合、HTTP API 服務。

![其他](Preview/Other.png)

### 系統資訊 (SysInfo)

系統硬體詳情、PawnIO 驅動狀態、感測器溫度、GPU 程序管理、監控選項。

![系統資訊](Preview/SysInfo.png)

### 設定 (Settings)

浮窗顯示（位置/字型/透明度/多顯示器）、Omen 鍵（5 種行為）、OSD 提示、匣標圖示（原版/自訂/動態）、開機自啟、自訂主介面 LOGO、主題（跟隨系統/深色/亮色）、語言、自訂背景（透明度/高斯模糊）、資料本地化、除錯日誌。

| 浮窗 & Omen 鍵 | 主題 & 背景 |
|:---:|:---:|
| ![設定](Preview/Settings.png) | ![系統](Preview/Settings-System.png) |

## 預設管理

| 預設 | PL1 | PL2 | 風扇 | TGP/PPAB | GPU 頻率上限 |
|------|-----|-----|------|----------|-------------|
| 極致效能 (Extreme) | 254W | 254W | Cool | 255W | 無限制 |
| GPU 優先 (GpuPriority) | 45W | 45W | Cool | 255W | 無限制 |
| 輕度使用 (LightUse) | 25W | 25W | Silent | 關閉 | 無限制 |
| 自訂 (Custom 1-3) | 使用者儲存 | 使用者儲存 | 使用者儲存 | 使用者儲存 | 使用者儲存 |

僅 CPU 功率、電源計畫、GPU 頻率上限、TGP+PPAB、dState 跟隨預設綁定；燈光、巨集、音訊等參數獨立儲存。

## 支援硬體

| 狀態 | 型號 |
|------|------|
| 已確認 | 暗影精靈 8 Plus · 8 Plus Plus · 9 · 9 Plus · 10 · 光影精靈 10 · 光影精靈 10 (Victus) · OMEN 16 (Ryzen) · OMEN 15 · OMEN Phantom Gaming |
| 不支援 | 暗影精靈 6 |

> 主要針對 **OMEN 10 Intel (i7-13650HX + RTX 4070)** 開發，相容性不保證適用於所有平台。

### 環境要求

- HP OMEN / VICTUS 遊戲筆電，具有 WMI BIOS 介面
- Windows 10/11 64-bit · .NET Framework 4.8
- 管理員權限（WMI、風扇控制、驅動安裝所需）

## 快速開始

1. **關閉 OGH** — 結束 `OmenCommandCenterBackground.exe` 或解除安裝 OGH 以避免衝突。
2. **以管理員身份執行** — 硬體控制需要提權。
3. **啟動 `OmenXHub.exe`** — 程式在匣標（系統列）運行。
4. **右鍵匣標圖示** 切換效能模式或開啟控制面板。
5. **在設定中啟用開機自啟** 以長期替代 OGH。

> ⚠️ DB (Dynamic Boost) 解鎖需要 NVIDIA 驅動版本 ≥ 537.42 且 < 610.47。50 系列 GPU 不支援解鎖。

## 建置

```cmd
dotnet restore OmenSuperHub.csproj
set MSBuildSDKsPath=C:\Program Files\dotnet\sdk\8.0.418\Sdks
set MSBuildEnableWorkloadResolver=false
MSBuild.exe OmenSuperHub.csproj /t:Build /p:Configuration=Release /p:Platform=x64
```

輸出：`bin\x64\Release\OmenXHub.exe`（單一檔案 — 所有 DLL 透過 Costura.Fody 嵌入）

## 致謝

- **MasonDye** — GUI 設計與 WPF 前端開發
- **One1turn** - WPF-UI Windows 11 樣式重寫 + 性能優化
- **breadeding** — [OmenSuperHub](https://github.com/breadeding/OmenSuperHub)（原始框架與程式碼）
- **GeographicCone** — [OmenMon](https://github.com/GeographicCone) / [OmenHwCtl](https://github.com/GeographicCone)（靈感來源與 OGH 互動研究）
- **OpenHardwareMonitor** — [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor)（硬體監控核心庫）

## 免責聲明

OMEN X Hub **與 HP 或 OMEN 無關聯**。品牌名稱僅作參考。本軟體直接與硬體互動，可能存在潛在風險。**使用風險自負。**
