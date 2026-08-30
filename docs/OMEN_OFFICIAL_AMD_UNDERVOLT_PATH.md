# AMD Curve Optimizer —— 官方通道与 UXTU 直写的对照

> 来源：`AMDRyzenSDK.dll`（HP.Omen.Core.Common.AMDSDK 的底层）反编译，24 文件
> 用途：说明官方 OMEN 的 AMD 超频真实路径，对照本项目的 `AmdUndervoltService.cs`（UXTU 直写路线）
> 日期：2026-08-26

## 1. 核心结论：官方走 RPC，不走本地 SMU

官方 `AmdRyzenHelper` 的所有调用都是：

```
OmenXHub / OGH → AmdRyzenSDK.dll → RPC → OmenCap 服务 (HPOmenRpcEndpoint, ncalrpc)
                                   → AmdRyzenOverclockingService 模块 → SMU 直写（在服务进程内）
```

- 端点：`HPOmenRpcEndpoint`（ALPC，同 Intel HSA——**与 `MEMORY_XTU_HSA_RE.md` 记录的 Intel 通道完全同构**）
- 服务：`AmdRyzenOverclockingService`（OmenCap.exe 内的模块）
- 消息：**JSON 字符串 + 命令号**，经 `RpcClientUtil.RunCustomCapabilityCommandAsync`
- 客户端不碰 SMU 邮箱——**你的 `AmdUndervoltService` 是绕过官方服务、自己在用户态直写 SMU（UXTU 路线），是独立实现**

## 2. 官方命令号全表（AmdRyzenCommands 枚举，73 个）

| cmd | 功能 | cmd | 功能 |
|---|---|---|---|
| 1 | GetCurrentFID | 33 | GetPMTableData |
| 2 | SetCurrentFID | 34 | GetCPUOCBiosLimits |
| 3 | GetCurrentVoltage | 36 | GetPBOScalar |
| 4 | SetCurrentVoltage | 37 | SetPBOScalar |
| 5 | GetCurrentFrequency | 38 | SetCurveOptimizerForAllCores |
| 6 | GetCurrentTemperature | 39 | SetCurveOptimizerForEachCore |
| 7 | GetDeviceCount | 40 | GetCurveOptimizerStatus |
| 10 | GetCurrentOCMode | 42 | SetEcoMode |
| 11 | EnableOverclocking | 46 | SetPPTLimit |
| 16 | SetOverclockCPUVID | 47 | SetEDCLimit |
| 17 | SetOverclockFreqAllCores | 48 | SetTDCLimit |
| 19 | GetCurrentCoreOrientation | 49-51 | *Limit_BIOS |
| 20 | GetCurrentPState | 58 | SetCurveOptimizer（每核） |
| 21 | GetVoltagebyVID | 72 | GetPBOScalarSpeed |
| 22 | GetVIDbyVoltage | 73 | GetCurrentCCDCount |
| 29 | IsPBOSupported | ... | 其余见枚举 |

## 3. 官方消息格式（JSON 载荷，已确认）

```csharp
// SetCurrentVoltage (cmd 4) — 单位：伏特（double）
{ "deviceIndex": 0, "pState": 0, "voltage": 1.325 }

// SetCurrentFID (cmd 2)
{ "deviceIndex": 0, "pState": 0, "fid": 0x3C }

// SetCurveOptimizerForAllCores (cmd 38) — 负压为负值 short
{ "sShort": -30 }

// SetCurveOptimizerForEachCore (cmd 39)
{ "fid": ccdIdx, "vid": coreIdx, "sShort": -30 }

// SetPBOScalar (cmd 37)
{ "bBoolstatus": true }

// SetPPTLimit (cmd 46)
{ "deviceIndex": 154 }   // PPT 瓦数
```

注意：**`fid`/`vid` 字段在 SetCurveOptimizerForEachCore 里是 ccdIdx/coreIdx**（不是真正的 FID/VID），这是 HP 复用了 AMDJsonObject 字段名的反直觉点。

## 4. 本项目（UXTU 直写路线）与官方的对照

| 维度 | 官方（OmenCap RPC） | 本项目（AmdUndervoltService） |
|---|---|---|
| 通道 | ncalrpc ALPC + JSON | PawnIO + RyzenSMU.bin 驱动直写 |
| SMU 邮箱 | 服务内，不可见 | MP1/PSMU 双邮箱（地址表按 Family） |
| Curve Optimizer 全核 | cmd 38, `sShort` | `SendMp1(0x55)+SendPsmu(0xB1)`（Socket 分派） |
| 负值编码 | short 负值直传 | `0x100000 - |value|`（UXTU 约定） |
| 限值检查 | 服务端（GetCPUOCBiosLimits） | 需自行实现 |
| PBO 开关 | cmd 37 `bBoolstatus` | 未见对应 |
| 功耗墙 | cmd 46/47/48 | 未见对应 |
| 与 OGH 并存 | — | 直写 SMU 与 OGH 服务并发有冲突风险 |

## 5. 给你的建议

1. **你的直写路线可自洽，但注意两点**：
   - 负值编码 `0x100000 - |value|` 是 UXTU 的 CO 偏移约定（0x100000=1.0 缩放基数），与官方 RPC 的 `sShort` 是两套——**不要混用**，也不要试图用官方 SDK 的返回值换算你的值
   - 直写 SMU 与 OGH（如果同时跑）会争抢邮箱：建议与官方共用 `Global\Access_PCI` 互斥，或检测 OmenCap 服务运行时不直写

2. **可借鉴官方的能力检查**（你缺的）：
   - `GetCPUOCBiosLimits`（cmd 34）→ `{IsOverClockingDisabled, MaxFrequency, MaxVoltage}`——写之前查，防超限
   - `GetPMTableData`（cmd 33）→ PPT/EDC/TDC 的 FusedLimit + CurrentLimit——功耗墙读写
   - `GetCurrentCoreOrientation`（cmd 19）→ 每 CCD 活跃核位图——**你的每核 CO 需要它**（官方 SetCurveOptimizerPerCore 用它解析核号）
   - 注意：这些是 OmenCap RPC 命令，你的直写实现里没有等价物——但消息号可用（如果将来你想走 RPC 的话）

3. **最有价值的移植点**：官方 `GetPerCoreNumber(ccdIdx)` 逻辑（从 `GetCurrentCoreOrientation` 位图解析活跃核）——你的每核 CO 如果用固定核号表，在部分核心禁用的平台上会写错核。官方这套位图解析是 ground truth。

## 6. 附带：官方支持的 Ryzen 家族（与本项目 RyzenFamily 枚举对照）

官方 `AMD_CPU_Type` 枚举含：Cezanne / Vermeer / Rembrandt / Raphael / Phoenix / Mendocino / StrixPoint / StrixHalo 等——本项目的 `RyzenFamily` 枚举（Zen1Plus..FireRange）与之对应但命名不同。PBO 白名单官方仅 `Cezanne, Vermeer`（见 AMDSDKHelper.IsCpuSupportedPBO），本项目如实现 PBO 开关应参考。
