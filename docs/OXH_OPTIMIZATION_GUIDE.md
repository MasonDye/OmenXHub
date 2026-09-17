# OXH（OmenXHub）优化建议总纲

> 日期：2026-08-26（初版）
> 复核：第二轮（2026-08-26）逐条对照代码现状 + 标注落地状态；
> 第三轮（2026-09-11）补「〇、环境前提」、修正 §一/§3.6 措辞矛盾、补全文档索引
> 依据：对 OMEN Gaming Hub 官方逆向 + 本机（OMEN 16-am0xxx BIOS F.12）实测
> 原则：只给有官方 ground truth 依据的建议；每条标注「证据来源」「优先级」「当前状态」

## 状态图例（第二轮复核新增）

- ✅ **已落地** —— 代码中已实现，与建议一致（附文件/行为证据）
- 🔶 **部分落地** —— 实现了核心，仍有边界差异（附差异点）
- ⬜ **仍待办** —— 尚未实现
- ⛔ **已作废** —— 分支/架构变更后该建议不再适用（附原因）

> 复核基线：`feature/singbox-compress` HEAD = `94dfa16`
> 关键变更提交：`1034528 feat: IR 温度接入风扇 max、电池健康、音频律动灯效、三扇独立曲线与构建精简`

---

## 〇、环境前提（先读，决定哪些条目适用）

**本机画像**（多篇 docs 实测汇总，用于判断"条目是否适用本机"）：

| 维度 | 本机实况 | 影响 |
|---|---|---|
| 机型 | OMEN Gaming Laptop 16-am0xxx（8D3F，sku=RPLHXR_N22X2X4X6） | — |
| CPU | Intel i7-14650HX（8P+8E） | **AMD 条目（§3.3、§3.7）本机不可验证** |
| GPU | RTX 5060 Laptop | — |
| 风扇 | **双风扇**（FanType: CPU=1, GPU=2，其余 Unsupported） | **三扇条目（§3.6 Fan3）本机不可验证** |
| BIOS | F.12 | capabilities 全 0 → **除尘能力未开放**（§9） |
| 除尘能力 | `IsCleanCreekSupported()`=false、`IsLegacyCleanCreekSupported()`=false | 除尘按钮正确显示"不支持" |
| 运行时 | OmenXHub.exe **需提权**；Mutex 与 OmenSuperHub.exe 共享 | 部署/调试注意，见 `MEMORY_POWER_THERMAL_DIAGNOSTICS.md` §0 |

**读法**：§三 的 ⬜/🔶 条目多数**本机无法实测**（AMD/三扇/除尘），其价值面向**其他支持机型**；本机可验证的类型主要是 Intel 传感器、风扇曲线。

---

## 一、已确认正确（无需动，放心）

| 模块 | 结论 | 证据 |
|---|---|---|
| WMI 底座 `SendOmenBiosWmi` | SECU + hpqBDataIn + hpqBIOSInt{size} 与官方逐字节一致 | HP.SystemControl.BiosWmi / OmenHsaClient |
| 灯光 cmdType 2/3/5/6/7 | 与官方 Get/SetColor/Brightness/LEDAnimation 完全对应 | FourZoneLighting / Aurora 1:1 |
| FanType 枚举 | Cpu=1..LightingBoard=100 与官方一致 | PowerControl.Enum/FanType.cs |
| 逆转除尘**编码规则** | `速度+128` 反转 / 127 停止（官方规则，项目已掌握） | AdaptivePowerControlV1 |
| AMD SMU 邮箱协议 | UXTU 路线自洽 | AmdUndervoltService 对照 AMDSDKHelper |
| PawnIO 主驱动 | 已删冗余 OmenXHubDrv，架构干净 | — |

## 二、P0 复核结果（2026-08-26 两项均已闭环，P0 清零）

### 1. Intel 电压编码 —— **复核正确**（⛔ 第二轮复核：功能已移出本分支）
- **初判**：`(int)Math.Round(mv * 1.024) << 21` 缺 11-bit 二补码截断（undervolt 位模式错误）
- **复核（C# 全区间 -300..+300mV 实测 diffs=0）**：`x << 21` 是 mod-2^32 移位，负 int 的符号扩展高位全部溢出丢弃，**恒等价** `(x & 0x7FF) << 21` → 位模式正确（如 -100mV 两种写法均 0xF3400000）
- **误判根源**：`docs/INTEL_VOLTAGE_ENCODING_GROUND_TRUTH.md` 初版例算错误（`(-102)<<21` 误写 0xFFCCCC00、`-102&0x7FF` 误写 0x5FA），已更正
- **⛔ 现状（2026-08-26 二轮）**：`feature/singbox-compress` 分支的 `Services/XtuService.cs` **已不含电压/倍频/功耗平衡逻辑**——该 Intel 超频面整体移至 `feature/intel-ring0-oc` 分支（源码注释见 `XtuService.cs` 头部 ponytail 说明：PawnIO 写白名单不含 0x1AD、WinRing0 备选通道写入位级损坏待查）。
- **结论**：本分支无需任何电压相关动作；编码结论保留供 `feature/intel-ring0-oc` 参考。
- **证据**：`docs/INTEL_VOLTAGE_ENCODING_GROUND_TRUTH.md` §3.1；`Services/XtuService.cs:12-15`

### 2. Dojo 动画 —— **真机实测通过，保留现状**
- **位置**：`App/OmenLighting.cs` SetZoneAnimation（Dojo 分支，CmdType=11）
- **结论**：effectId/speed/direction/theme 位字段已在真机验证有效 → 以实测为 ground truth，不再视为"逆向推断值"
- **建议**：保留现有实现；UI 缺失项（Direction/Theme 下拉）按方案 A 继续补齐即可
- **证据**：`docs/lighting-reverse-findings.md` §2.3（已补实测注记）

## 三、可补强（P1）—— 第二轮逐条状态核查

### 3. AMD 每核 CO 位图解析 ⬜ 仍待办
- **现状**：`AmdUndervoltService.SetPerCoreCO(core, offset)` 接受**用户给定的核心索引 0..15**（内部拆 `ccd=core/8, coreInCcd=core%8`），未见 `GetCurrentCoreOrientation`(cmd 19) 位图解析
- **官方**：cmd 19 返回位图 → `GetPerCoreNumber` 解析活跃核
- **价值**：部分核心禁用的平台（如 8 核坏 2 核）会写错核
- **证据**：`docs/OMEN_OFFICIAL_AMD_UNDERVOLT_PATH.md` §5「最有价值的移植点」（`GetPerCoreNumber` 位图解析）；另见 `docs/OMEN_OFFICIAL_AMD_CO_STATE_MACHINE.md`；代码 `Services/AmdUndervoltService.cs:309-377`

### 4. 传感器交叉校验 ⬜ 仍待办
- **现状**：温度/功耗全依赖 LHM（`HardwareService` 走 `LibreHardwareMonitor-pawnio-squashed`）；独立公式未实现（`0x1B1/0xFF001700/IA32_THERM` 仅存在于 LHM 内部）
- **官方公式**：Intel RAPL 差分、AMD17 PCI 温度解码、IA32_THERM 位——可独立读取交叉验证
- **价值**：发现第三方库平台偏差；减少依赖
- **证据**：`docs/OMEN_OFFICIAL_SENSOR_FORMULAS.md`

### 5. 风扇三路取最大 + GPU 联动 ✅ 已落地（IR 路）
- **现状（更新）**：`FanService` 已在三处接入 IR 路 `Math.Max(t, HardwareService.RawIrTemp)`，受开关 `ConfigService.UseIrForFanCurve` 控制（见 `FanService.cs:73-85, 312-316, 503`）
- **差异点**：IR 路为**可选开关**而非默认三路取最大；GPU 联动 `GPU=CPU-2 或查表` 尚未见显式实现（当前 `max(cpu,gpu)`）
- **后续（可选）**：如需 100% 对齐官方 FanHandler，可将 IR 路改默认开启 + 补 GPU 查表联动
- **证据**：`docs/OMEN_OFFICIAL_FAN_ALGORITHM.md` §5；代码 `Services/FanService.cs`；接线链路见 `docs/FAN_CONTROL_PIPELINE.md`

### 6. 逆转除尘 3 处对齐 🔶 部分落地（Fan3 已做，127 恢复 + 前置条件未做）
- **Fan3 独立速度**：✅ `SetFanLevel(0, 0, OmenHardware.IsThreeFan(), true)` 已支持第 3 扇 +128 逆转（`Pages/FanPage.xaml.cs:~931`），三风机载荷正确扩展
- **恢复用 127**：⬜ 规则已知（§一）但**恢复路径未采用**——当前 `SetFanLevel(0, 0)`（即 `{0,0}`），官方用 `{127,127,127}`（127=停止反转保持正转）。改动仅一处：`Pages/FanPage.xaml.cs:935`。**风险低但本机无法验证**（本机不走此分支）。
- **前置条件**：⬜ 未加 CPU<65/GPU<80/IR<45 + AC + 非Eco + 空闲检查（官方 `CleanCreekCriterion`）
- **额外落地**：除尘期间已**暂停风扇心跳**（`fanControlTimer.Change(Infinite,Infinite)`），修复了 30s 清灰中秒级心跳覆盖 +128 逆转字节的打架问题
- **证据**：`docs/OMEN_3FAN_DUST_REMOVAL.md` §10；代码 `Pages/FanPage.xaml.cs:916-955`
- **注意**：本机双风扇且 capabilities 全 0，除尘按钮会正确显示"不支持"——不要为此改判断逻辑

### 7. AMD 功耗墙 + PBO 🔶 部分落地（PPT 已做，PBO/EDC/TDC 已删）
- **现状（更新）**：AMD PPT **已实现但走 WMI 通道**（`Pages/PerfPage.xaml.cs:327,370,1703`，注释"SMU 兜底已随高级调教删除"）
- **TDC/EDC/Tctl**：代码注释明确"三组控件已随高级调教删除"（`PerfPage.xaml.cs:1427`）——**主动裁剪，非缺失**
- **PBO Scalar**：未见实现
- **结论**：本条目已从"待补强"降级为"按需"；若需 PBO/EDC/TDC，需先恢复被删的高级调教 UI 并重新评估 SMU 通道
- **证据**：`docs/OMEN_OFFICIAL_AMD_UNDERVOLT_PATH.md` §2；代码 `Pages/PerfPage.xaml.cs`

## 四、认知校准（本机硬件限制，别浪费时间）

### 8. 充电限制
- **结论**：本机 BIOS F.12 支持（myHP features.json: `pcbatterymanager-x-core:true`），但：
  - 写入走 `\\.\QCOMBATTMGR` 驱动（IOCTL 0x80092044）——**本机无此驱动**
  - BEM-Intel 走 `hpqBIntM cmd=1/2 + cmdType=76`（已验证可读写，但 cmdType 76 是系统电源模式，非充电限制）
  - 第三方 RPC 连 HPSysInfoRpcEndpoint 被签名校验拒绝（rc=5）
- **建议**：确认 myHP UI 是否显示充电限制；若显示则硬件支持但需走 myHP 包身份，OXH 无法独立实现
- **证据**：`docs/BATTERY_CHARGE_LIMIT_FINDINGS.md`

### 9. 逆转除尘
- **结论**：本机双风扇 + capabilities 全 0 = **BIOS 未启用 Fan Cleaner**。命令通道和编码都对，硬件不支持
- **建议**：保留现有实现（支持机型可用），本机正常显示"不支持"

## 五、可选探索（P2）

### 10. 官方 cmdType 全表校准
- 你的 `GetFanType`(cmdType 44)、`GetGfxMode`(cmd 1+82) 等是推断值
- 官方 `HP.SystemControl.BiosWmi.dll` 只确认了 cmdType 76（SystemControl）
- **价值**：消除猜测命令，避免误发

### 11. OmenCap RPC 直连
- 本机 `HPOmenRpcEndpoint` 可用（OmenCap 服务运行中）
- 若走通，AMD 超频可切官方通道（含限值保护）
- **障碍**：端点签名校验（rc=5），需 HP 签名进程身份

---

## 六、实施顺序建议（第二轮复核后更新）

```
P0（已闭环，无需动作）：
  1. Intel 电压 —— 复核正确；功能已整体移至 feature/intel-ring0-oc，本分支无动作
  2. Dojo 动画 —— 真机实测通过，保留现状

已落地（第二轮核实，无需再做）：
  ✅ 风扇 IR 第三路      （FanService max(cpu,gpu,ir)，开关控制）
  ✅ 除尘 Fan3 独立逆转   （SetFanLevel(0,0,is3Fan,true)）
  ✅ 除尘暂停心跳         （修 +128 逆转被秒级心跳覆盖）
  ✅ AMD PPT（WMI 通道）  （TDC/EDC 为主动裁剪，非缺失）

仍待办（有精力就做，按性价比排序）：
  3. AMD 每核位图解析        （cmd 19 位图，防部分核心禁用平台写错核）
  4. 传感器交叉校验          （独立公式，减少对 LHM 依赖）
  5. 除尘两处对齐            （127 恢复 + CPU<65/GPU<80/IR<45 前置条件）

按需（P2）：
  6. PBO Scalar / EDC / TDC  （需先恢复被删的高级调教 UI）
  7. GPU 联动查表（GPU=CPU-2）（补全官方三路语义）
  8. cmdType 全表校准 / OmenCap RPC 直连
```

## 七、文档索引（全部在 docs/，共 13 篇）

### 官方逆向 ground truth（对照基准）

| 文档 | 内容 |
|---|---|
| CAPABILITY_MAP.md | 官方已还原 vs 项目现状 vs 可补强总览 |
| INTEL_VOLTAGE_ENCODING_GROUND_TRUTH.md | **Intel 电压编码权威答案（必读）** |
| OMEN_OFFICIAL_FAN_ALGORITHM.md | 风扇曲线/EWMA/迟滞算法 |
| OMEN_OFFICIAL_SENSOR_FORMULAS.md | CPU/GPU 温度功耗公式 |
| OMEN_OFFICIAL_AMD_CO_STATE_MACHINE.md | AMD Curve Optimizer 状态机 |
| OMEN_OFFICIAL_AMD_UNDERVOLT_PATH.md | AMD 官方 RPC vs UXTU 对照 |
| OMEN_3FAN_DUST_REMOVAL.md | 三风扇 + 逆转除尘 |

### 本项目实测 / 逆向取证

| 文档 | 内容 |
|---|---|
| FAN_CONTROL_PIPELINE.md | **风扇全链路接线地图**（用户调节→EC 写入；新增三扇/IR 功能从此定位插入点） |
| BATTERY_CHARGE_LIMIT_FINDINGS.md | 充电限制实测结论 |
| MEMORY_POWER_THERMAL_DIAGNOSTICS.md | 功耗钳制/温度墙/绑定陷阱 + 提权部署坑（2026-08-29） |
| MEMORY_XTU_HSA_RE.md | Intel XTU/HSA 通道逆向（客户端已通、服务器模块本机不可用） |
| OMEN_OVERLAY_RE.md | HP OMEN 游戏内叠加层逆向（ilspycmd 反编译产物地图） |
| lighting-reverse-findings.md | 灯光页逆向取证（Dojo 位域 + 实测注记） |

> 注：`MEMORY_POWER_THERMAL_DIAGNOSTICS.md`、`OMEN_OVERLAY_RE.md` 晚于本指南初版（08-29 新增），
> 已随第二轮复核补入索引。本指南自身即 `OXH_OPTIMIZATION_GUIDE.md`。
