# OXH（OmenXHub）优化建议总纲

> 日期：2026-08-26
> 依据：本次会话对 OMEN Gaming Hub 官方逆向 + 本机（OMEN 16-am0xxx BIOS F.12）实测
> 原则：只给有官方 ground truth 依据的建议；每条标注「证据来源」和「优先级」

---

## 一、已确认正确（无需动，放心）

| 模块 | 结论 | 证据 |
|---|---|---|
| WMI 底座 `SendOmenBiosWmi` | SECU + hpqBDataIn + hpqBIOSInt{size} 与官方逐字节一致 | HP.SystemControl.BiosWmi / OmenHsaClient |
| 灯光 cmdType 2/3/5/6/7 | 与官方 Get/SetColor/Brightness/LEDAnimation 完全对应 | FourZoneLighting / Aurora 1:1 |
| FanType 枚举 | Cpu=1..LightingBoard=100 与官方一致 | PowerControl.Enum/FanType.cs |
| 逆转除尘编码 | `速度+128` 反转 / 127 停止 与官方一致 | AdaptivePowerControlV1 |
| AMD SMU 邮箱协议 | UXTU 路线自洽 | AmdUndervoltService 对照 AMDSDKHelper |
| PawnIO 主驱动 | 已删冗余 OmenXHubDrv，架构干净 | — |

## 二、P0 复核结果（2026-08-26 两项均已闭环，P0 清零）

### 1. Intel 电压编码 —— **复核正确，无需功能修改**
- **初判**：`(int)Math.Round(mv * 1.024) << 21` 缺 11-bit 二补码截断（undervolt 位模式错误）
- **复核（C# 全区间 -300..+300mV 实测 diffs=0）**：`x << 21` 是 mod-2^32 移位，负 int 的符号扩展高位全部溢出丢弃，**恒等价** `(x & 0x7FF) << 21` → 现有代码位模式正确（如 -100mV 两种写法均 0xF3400000）
- **误判根源**：`docs/INTEL_VOLTAGE_ENCODING_GROUND_TRUTH.md` 初版例算错误（`(-102)<<21` 误写 0xFFCCCC00、`-102&0x7FF` 误写 0x5FA），已更正
- **动作**：仅补显式 `& 0x7FF` 掩码 + 注释（行为零变化，防再误判）
- **证据**：`docs/INTEL_VOLTAGE_ENCODING_GROUND_TRUTH.md` §3.1

### 2. Dojo 动画 —— **真机实测通过，保留现状**
- **位置**：`App/OmenLighting.cs` SetZoneAnimation（Dojo 分支，CmdType=11）
- **结论**：effectId/speed/direction/theme 位字段已在真机验证有效 → 以实测为 ground truth，不再视为"逆向推断值"
- **建议**：保留现有实现；UI 缺失项（Direction/Theme 下拉）按方案 A 继续补齐即可
- **证据**：`docs/lighting-reverse-findings.md` §2.3（已补实测注记）

## 三、可补强（P1，官方 ground truth 现成，低成本高价值）

### 3. AMD 每核 CO 位图解析
- **现状**：`AmdUndervoltService` 每核 CO 可能用固定核号表
- **官方**：`GetCurrentCoreOrientation`(cmd 19) 返回位图 → `GetPerCoreNumber` 解析活跃核
- **价值**：部分核心禁用的平台（如 8 核坏 2 核）会写错核
- **证据**：`docs/OMEN_OFFICIAL_AMD_UNDERVOLT_PATH.md` §5.2

### 4. 传感器交叉校验
- **现状**：温度/功耗全依赖 LHM/HWiNFO
- **官方公式**：Intel RAPL 差分、AMD17 PCI 温度解码、IA32_THERM 位——可独立读取交叉验证
- **价值**：发现第三方库平台偏差；减少依赖
- **证据**：`docs/OMEN_OFFICIAL_SENSOR_FORMULAS.md`

### 5. 风扇三路取最大 + GPU 联动
- **现状**：FanService 只 CPU/GPU 两路
- **官方**：FanHandler 取 max(cpu, gpu, ir)，GPU=CPU-2 或查表
- **价值**：有 IR 温度传感器时更准
- **证据**：`docs/OMEN_OFFICIAL_FAN_ALGORITHM.md` §5

### 6. 逆转除尘 3 处对齐官方
- **恢复用 127**：`SetFanLevel(0,0)` → 支持 127 恢复（官方 SetSwFanControlLevel(127,127,127)）
- **前置条件**：加 CPU<65/GPU<80/IR<45 + AC + 非Eco + 空闲检查
- **Fan3 独立速度**：`CleanCreekFan3Speed` 独立配置
- **证据**：`docs/OMEN_3FAN_DUST_REMOVAL.md` §10
- **注意**：本机双风扇且 capabilities 全 0，除尘按钮会正确显示"不支持"——不要为此改判断逻辑

### 7. AMD 功耗墙 + PBO 开关
- **现状**：AmdUndervoltService 无 PBO/PPT/EDC/TDC
- **官方**：SetPBOScalar(cmd 37)、SetPPTLimit(cmd 46)/EDC(47)/TDC(48)
- **价值**：完整 AMD 超频面
- **证据**：`docs/OMEN_OFFICIAL_AMD_UNDERVOLT_PATH.md` §2

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

## 六、实施顺序建议

```
P0 复核结论（2026-08-26）：
  1. Intel 电压 —— 复核正确，已加显式掩码+注释（行为不变），无功能修改
  2. Dojo 动画 —— 真机实测通过，保留现状，无需决策
  → 当前没有必须马上改的功能项

第一优先级（有精力就做）：
  3. AMD 每核位图解析
  4. 逆转除尘 3 处对齐
  5. 传感器交叉校验

第三优先级（按需）：
  6. AMD 功耗墙/PBO
  7. 风扇三路算法
  8. cmdType 校准
```

## 七、文档索引（本次会话产出，全部在 docs/）

| 文档 | 内容 |
|---|---|
| CAPABILITY_MAP.md | 官方已还原 vs 项目现状 vs 可补强总览 |
| INTEL_VOLTAGE_ENCODING_GROUND_TRUTH.md | **Intel 电压编码权威答案（必读）** |
| OMEN_OFFICIAL_FAN_ALGORITHM.md | 风扇曲线/EWMA/迟滞算法 |
| OMEN_OFFICIAL_SENSOR_FORMULAS.md | CPU/GPU 温度功耗公式 |
| OMEN_OFFICIAL_AMD_CO_STATE_MACHINE.md | AMD Curve Optimizer 状态机 |
| OMEN_OFFICIAL_AMD_UNDERVOLT_PATH.md | AMD 官方 RPC vs UXTU 对照 |
| OMEN_3FAN_DUST_REMOVAL.md | 三风扇 + 逆转除尘 |
| BATTERY_CHARGE_LIMIT_FINDINGS.md | 充电限制实测结论 |
