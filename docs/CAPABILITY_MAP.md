# 能力地图 —— 官方已还原 vs 本项目现状 vs 可补强

> 日期：2026-08-26
> 用途：基于 OMEN Gaming Hub 官方逆向（ground truth）与本项目（OmenXHub-optimized）现状，
> 列出"能实现什么、已实现什么、还差什么"。供路线规划。

## 0. 图例

- ✅ 已实现且被官方 ground truth 验证正确
- 🟡 已实现但路径与官方不同（可用/自洽，注意差异）
- ⚠️ 已实现但可能有错（见对应文档）
- 🔵 官方能力已还原、本项目未实现（可补强）
- ⬜ 官方/项目均无（非目标）

---

## 1. 硬件访问底座

| 能力 | 官方（已还原） | 本项目 | 状态 |
|---|---|---|---|
| BIOS WMI 命令 | SECU+hpqBDataIn+hpqBIOSInt{size} | SendOmenBiosWmi 同源 | ✅ |
| 内核驱动 | HpReadHWData.sys (IOCTL 0x9C419008) | **PawnIO**（主驱动，EC/SMU/MSR 均走它） | 🟡 独立，注意无白名单风险 |
| MSR/PCI 直读 | Rdmsr/Rdpci 封装 | PawnIO（IntelMsr/SmuReg） | 🟡 |
| 外设 HID | HidMailroomBg 管道 + 65B 报告 | 键盘 HID 直连（VID 0x03F0 表） | 🟡 无管道，直连设备 |
| 外设 SCSI 桥 | HbProtocol + UstorVendorScsiCmd | 无 | ⬜ 少用 |
| 命名管道 IPC | HP.Omen.Background.* + SecurePipe | OmenXHubPipe（托盘） | 🟡 形态不同 |

## 2. 风扇控制

| 能力 | 官方（已还原） | 本项目 | 状态 |
|---|---|---|---|
| EC 风扇命令 | cmdType 0x2E/0x2D/0x27/0x1A | SendOmenBiosWmi 同通道 | ✅ |
| 自定义曲线 | FanHandler: FanTable 迟滞 + EWMA | FanService: GetSmartFanSpeed（EMA+插值） | 🟡 算法不同但自洽 |
| 滑杆模式 | RPM=值/100×档位+MinRpm | 见 FanPage | 🟡 |
| 空闲自动 | IDLE_AUTO 固定最低档 | 见 FanService | 🟡 |
| **迟滞降档** | FanTuningV1（升看高表/降看低表） | 见 FanService 注释（有单调破坏防护） | 🔵 可对照 OMEN_OFFICIAL_FAN_ALGORITHM.md 精修 |
| **三路(CPU/GPU/IR)取最大** | max(cpu,gpu,ir) | 仅 CPU/GPU 两路 | 🔵 可加 IR 路 |
| GPU 联动 | GPU=CPU-2 或查表 | ? | 🔵 可补 |

## 3. 温度/功耗传感器

| 能力 | 官方（已还原） | 本项目 | 状态 |
|---|---|---|---|
| CPU 温度 | Intel 0x1B1 / AMD PCI 0xFF001700 | LHM + HWiNFO 双源 | 🟡 依赖第三方 |
| RAPL 功耗 | ΔEnergy÷Δt×单位 | LHM | 🟡 |
| 独立公式 | MSR_INDEX/PCI_INDEX 全表 | 未用 | 🔵 可做交叉校验（见 OMEN_OFFICIAL_SENSOR_FORMULAS.md） |
| 传感器互斥 | Global\Access_PCI | ? | 🔵 建议加，避免与 OGH 冲突 |

## 4. Intel 超频

| 能力 | 官方（已还原） | 本项目 | 状态 |
|---|---|---|---|
| 电压偏移 | S11.0.10, 11-bit 补码, bit21 | XtuService <<21 | ✅ 复核正确（C# 移位 mod-2^32 天然等价补码截断，全区间实测） |
| P/E 核倍频 | 0x1AD/0x1AE 或 HP 槽位 | XtuService 直写 | 🟡 与官方 HP 槽位协议不同 |
| OC Mailbox | 0x150, cmd 0x11 | 已用 | 🟡 |
| **官方控制 ID 表** | 34=CpuVoltageOffset(高层), 3/11/12 字段 | 用 0xFF 哨兵 | 🟡 哨兵无碍，但别用 34/79 当字段 ID（是别的） |
| XTU 服务 RPC | HPOmenRpcEndpoint + IOCBIOS2 | 无 | ⬜ 本机 HSA 不通（见 MEMORY_XTU_HSA_RE.md） |

## 5. AMD 超频

| 能力 | 官方（已还原） | 本项目 | 状态 |
|---|---|---|---|
| Curve Optimizer | OmenCap RPC (cmd 38/39) | 直写 SMU（UXTU 路线） | 🟡 独立自洽 |
| 负值编码 | sShort 直传 | 0x100000-|v| | 🟡 两套别混用 |
| PBO 开关 | cmd 37 | 无 | 🔵 可补（直写 SMU 有对应消息号） |
| 功耗墙 PPT/EDC/TDC | cmd 46/47/48 | 无 | 🔵 可补 |
| 核位图解析 | GetCurrentCoreOrientation | 固定核号表 | 🔵 可补（部分核心禁用平台会写错核） |
| 限值检查 | GetCPUOCBiosLimits | 无 | 🔵 可补（写前防超限） |
| 官方共存 | 注册表 AMDCurveOptimizer | 无 | 🔵 可对齐（见 OMEN_OFFICIAL_AMD_CO_STATE_MACHINE.md） |

## 6. 灯光

| 能力 | 官方（已还原） | 本项目 | 状态 |
|---|---|---|---|
| 4 区静态色 | cmdType 2/3, array[25+i*3] | 同布局 | ✅ |
| 灯条静态 | cmdType 11, [0]=target/[6]=4 | 同布局 | ✅ |
| 亮度 | cmdType 5 | BacklightOn/Off | ✅ |
| LED 动画查询 | cmdType 6/7 | 有 | ✅ |
| **Dojo 动画字节** | 官方 Aurora 不下发（逐帧 SetStatic） | SetZoneAnimation（CmdType=11） | ✅ 真机实测通过（2026-08-26），以实测为 ground truth |
| 音频律动 | AudioMonitor WASAPI 环回 | LightingAnimationService | 🟡 通道不同，效果等效 |
| 虚拟摄像头 | EffectEngine(NvVFX/NvAR) | 无 | ⬜ 与超频无关 |

## 7. 网络 / 系统

| 能力 | 官方（已还原） | 本项目 | 状态 |
|---|---|---|---|
| 网络加速 | QoS 按进程优先级 | NetworkBoostPage + NetworkSpeedService | 🟡 通道不同 |
| 性能模式 | FanMode→PerformanceMode L0-L8 | PerfPage 电源计划 | 🟡 |
| EcoQoS | ProcessPowerThrottling | EcoQosService | ✅ |
| 宏 | McuSDK2 MacroCommand | MacroService + MacroController | 🟡 |
| 固件更新 | FwUpdateHelper + FwUpdate.dll | 无 | ⬜ ODM 闭源 |
| MQTT 遥测 | omenmqtt (AWS IoT) | 无 | ⬜ 云端，本地项目不需要 |

## 8. 可补强优先级（建议路线）

### P0 —— 复核结论（2026-08-26，均已闭环）
1. **XtuService 电压编码** → 复核**正确**（`int<<21` 天然等价补码截断），已加显式掩码+注释，无功能修改
2. **Dojo 动画字节** → 真机**实测通过**，保留现状，无需决策

### P1 —— 低成本高价值（官方 ground truth 现成）
3. **AMD 每核 CO 位图解析**（GetPerCoreNumber 逻辑，防写错核）
4. **传感器交叉校验**（用官方公式独立读温度，对比 LHM/HWiNFO）
5. **风扇三路取最大 + GPU 联动**（FanHandler 移植）

### P2 —— 功能补全（需较多工作）
6. **AMD PBO 开关 + 功耗墙**（官方 cmd 37/46-48 语义参考）
7. **官方注册表共存**（AMDCurveOptimizer 状态机对齐）
8. **Global\Access_PCI 互斥**（与 OGH 并发安全）

### P3 —— 可选探索
9. **OmenCap RPC 直连**（HPOmenRpcEndpoint：若本机 OmenCap 可用，AMD 超频可切官方通道）
10. **官方 cmdType 全表校准**（OmenHsaClient 里所有 command/cmdType 对照，消除 44/82 等猜测）

## 9. 一句话总结

**你项目的骨架（WMI 通道、风扇、灯光静态、宏、网络）已与官方同源或自洽，放心用；**
**P0 已清零：Intel 电压编码复核正确、Dojo 动画实测通过；**
**三个可补：AMD 核位图/功耗墙、传感器交叉校验、风扇三路算法。**
