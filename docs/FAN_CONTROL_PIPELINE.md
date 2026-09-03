# 风扇配置调节 — 全链路实现文档

> 基于源码完整追踪（FanPage / FanService / PresetManager / TrayService / AutomationProcessor /
> OmenHardware / EcFanService）。目标：一条从「用户调节」到「EC 写入」的可查链路地图，
> 新功能接线（如三扇/IR）从此文档定位插入点。

---

## 一、数据模型：三个核心字段

| 字段 | 类型 | 语义 | 持久化 |
|---|---|---|---|
| `ConfigService.FanControl` | string | **控制方式**：`""`/`"auto"`=自动表 / `"smart"`/`"custom"`=智能曲线 / `"XXXX RPM"`/`"XX%"`/`"max"`=手动固定 | 全局键（内置预设改手动不写子键=临时绑定） |
| `ConfigService.FanTable` | string | **自动档曲线**：`silent`/`cool`/`balanced` → `FanCurves/*.txt` | 全局键 |
| `FanTempFanMap`（FanService） | `Dictionary<float, List<int>>` | 温度→转速查表（CPU/GPU 两张），运行时由曲线文件加载 | 文件 |

设计要点：**控制方式与曲线内容分离** —— FanControl 决定「怎么算转速」，曲线文件决定「什么温度转多少」。

### 预设绑定语义（重要，多次返工过的点）

- **内置预设**（Extreme/GpuPriority/LightUse）：风扇是**临时绑定**——任何档（含手动 RPM）
  切走即丢、重启回预设 FanTable 默认（Extreme=cool / GpuPriority=balanced / LightUse=silent）。
  SwitchPreset 不再读回子键 FanControl；FanPage 也不为内置预设写子键。
- **自定义预设**：风扇配置（含手动）**完整绑定**，经 `SaveCustomPreset` 落 JSON。

---

## 二、UI 层（FanPage）— 用户调节入口

### 模式切换 `FanMode_SelectionChanged`（mode 0-4）

| mode | 设置 | 立即生效动作 |
|---|---|---|
| 0 静音 | `FanControl=""`, `FanTable="silent"` | `LoadFanConfig("silent.txt")` + timer 启动 |
| 1 降温 | → `"cool"` | `LoadFanConfig("cool.txt")` |
| 2 平衡 | → `"balanced"` | `LoadFanConfig("balanced.txt")` |
| 3 智能 | `FanControl="smart"` | `InitSmartFanState(EMA)` + `ApplyPresetCurve(preset)` |
| 4 手动 | `FanControl="XXXX RPM"` | timer 停 + 直接 `SetFanLevel(rpm/100, rpm/100)` |

持久化统一尾部：
```csharp
ConfigService.Save("FanControl"); ConfigService.Save("FanTable");
if (IsCustom(preset)) SaveCustomPreset(...);   // 仅自定义预设写 JSON
```

### 手动 RPM
三个入口（下拉/滑条/数字框）汇聚同动作：写 `FanControl="N RPM"` → `SetMaxFanSpeedOff()`
→ `SetFanLevel(rpm/100, …)` → 心跳 timer `Change(Infinite)`（固定值无需心跳）。

---

## 三、计算层（FanService）— 温度到转速

### A. 自动档 `GetFanSpeedForTemperature(fanIndex)`
温度对查表线性插值。输入来源优先级：
1. 仅 CPU 监控 + 有环境传感器 → `GetFittingTemperature()`（环境温×1.2−5）
2. FanSync + GPU 监控 → `max(CPU, GPU)`（开关开启时再 max IR，官方三路算法）
3. 否则按 fanIndex 各自温度

### B. 智能曲线 `GetSmartFanSpeed(fanIndex)`
A 的输入之上叠加三重平滑：
```
EMA(α 可调) → 迟滞(< Hysteresis °C 不动) → 降速限率(每秒最多降 StepDownRate)
```
smart 参数存 `FanCurves/custom_<preset>_smart.txt`。

曲线文件三层兜底：用户拖拽自定义(`custom_<preset>.txt`) → per-preset 默认 → 出厂生成。

### IR 三路（UseIrForFanCurve 开关，默认关）
- `HardwareService.IrTemp`：OMEN WMI 0x23 sensorIndex 0 读数缓存（1~120°C 钳位+EMA）。
- 开启时 `max(CPU,GPU,IR)`（docs/OMEN_OFFICIAL_FAN_ALGORITHM.md 官方三路）；IR 未读到（负值）被 max 忽略。
- VR 无官方依据，未接入。

---

## 四、执行层 — 1 秒心跳写 EC

`TrayService.fanControlTimer`（1000ms）是唯一执行心脏：

```csharp
if (FanControl == smart/custom)
    speed = GetSmartFanSpeed(i) / 100;
else
    speed = GetFanSpeedForTemperature(i) / 100;
SetMaxFanSpeedOff();                 // 0x27 EC 保活: AMD ~3s 不写就回退 BIOS 表
SetFanLevel(s1, s2, fan3: IsThreeFan());
```

- 手动固定档心跳 `Change(Infinite)` 停搏。
- 硬件通道两套：**OMEN WMI** (`hpqBIntM`, 0x2C/0x2D/0x2E/0x2F/0x27/0x11) 与 **PawnIO EC 直写**
  (`EcFanService`: XSS1/XSS2=0x2C/0x2D, OMCC=0x62, RPM@0xB0-B2 —— 来自 OmenMon，暗影精灵 6 等
  WMI 失败机型的降级路径)。OSH 无 EC 层，此段为本项目独有。
- 高温保护旁路：`CheckAutoFanProtect()` CPU>95°C 且手动<75% → 强制 auto+cool 并冷却后还原。

### 三扇接线（移植 OSH 验证实现）
- 探测：`OmenHardware.IsThreeFan()` —— GetFanType(0x2C, command 0x20008) 解析 types[2]!=Unsupported，
  进程内探测一次缓存（`IsThreeFan()`，避免心跳每秒 WMI 往返）。
- 写入：所有 SetFanLevel 调用点（心跳/唤醒重发/恢复/FanPage 手动×5/自动化/PresetManager/API 共 12 处）
  统一 `fan3: IsThreeFan()`。第 3 字节 = `(f1+f2)/2` 均值跟随（无独立转速）。
- 读数：`FanSpeedNow` int[3]，GetFanLevel(0x2D) 三元组经 UpdateTooltip 流入第 3 路；
  双风机恒 -1。UI：FanPage 手动卡第三扇显示行（仅三扇或 DebugShowAllUi 可见，
  DEBUG 态带 `[DEBUG]` 前缀并模拟均值转速）。

### 独立第三扇曲线（三扇机型）
- **协议事实**：0x2E 载荷第 3 字节是独立 byte 槽，OSH 的 `(f1+f2)/2` 是无温度源时的保守策略，
  非固件强制 —— 可写任意 0-255 独立值。
- **写入**：`SetFanLevel(f1, f2, int? fanSpeed3)` 重载 —— fanSpeed3 非空写独立值，null 回退均值。
- **计算**：`FanService.Fan3TempFanMap` 第三张查表 + `GetFan3Speed()`，
  温度源 `max(CPU,GPU[,IR])`（第三扇多为 VRM/排气，跟随机内主热源）。
- **持久化**：`custom_<preset>_fan3.txt`（与曲线文件同格式），随 `ApplyPresetCurve` 按预设切换加载，
  预设删除/清空时随查表清空 → 回退均值跟随。
- **UI**：曲线编辑器第三 tab（仅三扇机 Visible）；文件不存在时预置默认四点曲线（40/60/80/95°C）。
  fan3 tab 激活时 `_curvePoints` 承载编辑快照，`SaveCurve` 检测 `_fan3CurveActive` 写回 fan3 通道；
  切回 CPU/GPU tab 时从文件重载，防 fan3 快照污染双扇通道。
- **降级**：未配曲线 → `GetFan3Speed()=-1` → 心跳传 null → 固件均值跟随（OSH 行为）。

### V0/V1 ThermalPolicy 兼容分支
- **背景**：HP EC 热策略协议两代不兼容。同字节值语义随代际而异 —— 项目历史裸字节 0x31(Unleash)
  在 V1 机=L7=Performance，但 **V0 黑名单机（8607/8746/8747/8749/874A/8748）映射表无 L7 条目**，
  盲发是未定义行为。
- **实现**：`OmenHardware.GetEcPerformanceCommand(byte legacy)` 按版本映射：
  V1 → unleash?L7:L2（与历史字节 bit 级一致，零回归）；V0 → 统一回退 Default(L0)。
  `SetFanModeCompat(byte)` 为矫正后写入入口，全工程 12 处 `SetFanMode((byte))` 已改走。
- **探针**：启动日志 `[ThermalPolicy] Version=… SystemID=…` 确认本机代别。
- **性能模式 UI 组**（PerformanceModeOnUI/ModeNames/GetSupportedPerformanceModes/SetFanMode(OnUI)
  等）保留未删 —— 是这套兼容分支的未来 UI 接线点。

---

## 五、联动层 — 预设/自动化改变风扇

**预设切换**：`SwitchPreset → ApplyPresetData → AwaitableApplyPresetHardware`
先 `SetFanMode(0x31)` Unleash 前置（否则部分机型 EC 忽略后续 0x29 功率写入），
再按 FanControl 分支应用（smart→ApplyPresetCurve / RPM→SetFanLevel / 其它→LoadFanConfig）。

**自动化步骤** `ExecuteSetFanMode` 四分支（silent-cool-balanced / smart / json:导入曲线 / manual:XX%）
尾部统一持久化（全局键 + 子键/JSON）。

**温度灵敏度**（TempSensitivity → RespondSpeed EMA α）:
realtime=1.0 / high=0.4 / medium=0.1(默认) / low=0.04，
消费覆盖全部温度源（CPU/GPU/IR/Extra/HWiNFO 读数），α 越小越圆滑迟滞。
写入端三入口（FanPage/自动化/启动 RestoreTempSensitivity）齐备，链路闭环。
启动初期存在短暂 0.4 默认窗口（字段初值先于 RestoreConfig），影响可忽略。

---

## 六、Debug 预览（DebugShowAllUi 全局开关）

设置页「强制显示所有 UI」开启时：
- FanPage 第三扇显示行双风机可见（`[DEBUG]` 前缀 + 模拟均值转速；本机不发第 3 字节）。
- 除尘卡不支持机型也显示（描述带 `[DEBUG]` 标注，点击走 Unsupported 提示分支）。

---

## 七、历史 bug 备忘（防复发）

| bug | 根因 | 修复 |
|---|---|---|
| 内置预设手动复活 | 子键残留被 SwitchPreset 读回 | 删读回+写入（临时绑定语义） |
| TwoWay 绑定污染短路 | 启动项 Toggle 绑定先写 model 使 SetEnabled 误判已就位 | 删短路，按源键真实状态幂等 |
| 自动化风扇不持久 | ExecuteSetFanMode 只写内存 | 补 Save+子键/JSON |
| 除尘与心跳打架 | 心跳每秒正常写覆盖 +128 逆转字节 | 除尘期间 Change(Infinite)，finally 恢复 |
| GPU 功耗徘徊误导 | GPUPower 无 EMA | GetDisplayGpuPower 平滑+clamp 400W |
| 启动项关闭失效类 | 通用教训：**局部状态变化勿整表重建；持久化读写两端必须对称成对改** | — |
