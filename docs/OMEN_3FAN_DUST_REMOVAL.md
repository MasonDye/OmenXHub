# OMEN 三风扇 + 逆转除尘（Fan Cleaner / Clean Creek）官方实现还原

> 来源：OMEN Gaming Hub 反编译（PerformanceControl / AdaptivePowerControlV1 / ManualCleanCreek / PlatformSettings）
> 用途：为本项目 FanPage 的三风扇控制和"Fan Cleaner（逆转除尘）"提供官方 ground truth。
> 日期：2026-08-26

## 1. 总览

OMEN 的"逆转除尘"官方叫 **Fan Cleaner**（内部 `CleanCreek`）。三风扇机型（如 Articuno/Articice/Celsius）有 **CPU + GPU + 第3风扇（Intake/Exhaust 或 Fan3）**，除尘时风扇**反转**把灰尘吹出。

```
UI "Start Fan Clean" 按钮
  → FanControlModel.SendManualCleanCreek()
  → 管道消息 PerseusRevMsg{ PerformanceControlMsg{ Command = 19 } }   // 命令19 = ManualCleanCreek
  → PerformanceControl 后台 (AdaptivePowerControlV1.ProcessCleanCreek)
  → ManualCleanCreek 状态机: ReverseFanReady → ReverseFanInProgress → ReverseFanDone
  → PerformanceControlHelper.SetSwFanControlLevel(cpu, gpu, fan3)
  → 底层 WMI/EC 风扇控制
```

## 2. FanType 枚举（官方权威，本项目 OmenHardware.cs 已一致 ✓）

```csharp
public enum FanType {
  Unsupported = 0, Cpu = 1, Gpu = 2, Exhaust = 3, Pump = 4, Intake = 5, Vrm = 6, LightingBoard = 100
}
```

## 3. 逆转除尘核心命令（关键）

`AdaptivePowerControlV1.ProcessCleanCreek()` 反转时的下发：

```csharp
// 反转开始：
int cpuFan = CleanCreekCapabilities[0] ? (CleanCreekCpuFanSpeed + 128) : 255;  // 速度+128 = 反转！
int gpuFan = CleanCreekCapabilities[1] ? (CleanCreekGpuFanSpeed + 128) : 255;
int fan3   = Is3FanNb && CleanCreekCapabilities[2] ? (CleanCreekFan3Speed + 128) : 255;
PerformanceControlHelper.SetSwFanControlLevel(cpuFan, gpuFan, fan3);

// 反转停止（恢复正转）：
PerformanceControlHelper.SetSwFanControlLevel(127, 127, 127);  // 127 = 停止反转/恢复
```

**编码语义**：
| 值 | 含义 |
|---|---|
| `速度 + 128`（如 20+128=148） | **风扇反转**（除尘） |
| `127` | **停止反转，恢复正转** |
| `255` | 该风扇不参与（无此风扇/不支持） |

## 4. CleanCreek 状态机（ManualCleanCreek.cs）

```csharp
enum CleanCreekStatus { ReverseFanReady, ReverseFanInProgress, ReverseFanDone }

// 触发流程：
PrepareForReverse()   // 检查前置条件，状态→ReverseFanReady
StartReverse(duration) // 记录开始时间，状态→ReverseFanInProgress，下发反转命令
StopReverse(isDone)   // 停止监控，状态→ReverseFanDone，恢复正转(127)
IsReverseDone(ref progress) // 按 duration 算进度 0-100%
```

**前置条件（CleanCreekCriterion）**——任一失败则中止：
```
OK, ErrorHighCpuTemp, ErrorHighGpuTemp, ErrorHighIrTemp, ErrorDcMode(必须AC), ErrorEcoMode, ErrorInsufficientIdle
```

## 5. 参数配置（PlatformSettings.cs + 机型 JSON）

```csharp
CleanCreekCpuFanSpeed = 20   // CPU 反转转速
CleanCreekGpuFanSpeed = 20   // GPU 反转转速
CleanCreekFan3Speed   = 20   // 第3风扇反转转速
CleanCreekDuration    = 30000 // 反转持续 30 秒
CleanCreekCpuTemp     = 65   // CPU 温度门槛（超了不能除尘）
CleanCreekGpuTemp     = 80   // GPU 温度门槛
CleanCreekIrTemp      = 45   // IR 温度门槛
CleanCreekIdleTime    = 30000 // 空闲判定时间
```

实机例子（Celsius_DR8C_N22RX2X4X6.json）：`CleanCreekCpuFanSpeed=36, GpuFanSpeed=39, Duration=30000, CpuTemp=65, GpuTemp=80, IrTemp=45, IdleTime=30000`

## 6. 三风扇曲线（机型 JSON 完整结构）

三风扇机型（ArticunoA 示例）有 **5 条独立曲线**：

```json
{
  "DtSwFanControlCustomFanCurveAcsCPU": {  // CPU 高转曲线
    "FanTable": [20,20,20,25,30,35,40,45,55],
    "TemperatureTable": [50,55,60,65,70,75,80,85,90],
    "UpperBound": [100,...], "LowerBound": [20,...],
    "Lamda_Increase": 0.1, "Lamda_Decrease": 0.1
  },
  "DtSwFanControlCustomFanCurveLcsCPU": { ... },  // CPU 低转曲线
  "DtSwFanControlCustomFanCurveIntake": {          // 进风风扇
    "FanTable": [20,20,20,20,25,25,30,30,30],
    "TemperatureTable": [30,35,40,45,50,55,60,65,70]
  },
  "DtSwFanControlCustomFanCurveExhaust": {         // 排风风扇
    "FanTable": [20,20,25,25,30,30,35,40,40],
    "TemperatureTable": [30,35,40,45,50,55,60,65,70]
  },
  "DtSwFanControlCustomFanCurveVrm": { ... }       // VRM 风扇（部分机型）
}
```

`DtSwFanControlCustom` 结构：`FanTable`(转速) / `TemperatureTable`(温度) / `UpperBound` / `LowerBound` / `Lamda_Increase` / `Lamda_Decrease`。

## 7. FanSettingsDT（每风扇设置，DT 平台）

```csharp
class FanSettingsDT {
  FanType FanType;       // Cpu/Gpu/Intake/Exhaust/Pump/Vrm/LightingBoard
  int FanIndex;          // 风扇序号（对应 FanData.FanSpeedList 索引）
  int FanSpeed;
  string FanName;
  ReferenceTemp FanReferenceTemp;  // 参考温度源
  DtCustomFanCurve CustomFanCurve; // 自定义曲线
}
```

三风扇调度（DynamicFanCurve.cs）：CPU/GPU/Intake/Exhaust 各自查曲线取转速，`FanIndex` 对应 `FanData.FanSpeedList[FanIndex]` 下发。

## 8. 本项目对照与实现建议

| 项 | 官方 | 本项目 | 状态 |
|---|---|---|---|
| FanType 枚举 | Cpu=1..LightingBoard=100 | OmenHardware.FanType 相同 | ✅ 一致 |
| 三风扇检测 | IsThreeFanSupported（cmdType 44） | OmenHardware 已有 | 🟡 cmdType 44 需验证（见 CAPABILITY_MAP） |
| 反转命令 | SetSwFanControlLevel(速度+128, ..., 127停止) | 无 | 🔵 **可加** |
| CleanCreek 状态机 | ManualCleanCreek 三态 | 无 | 🔵 可加 |
| 前置条件 | 温度/AC/Eco/空闲检查 | 无 | 🔵 可加 |
| 三风扇曲线 | 5 条独立曲线（JSON） | FanService 单曲线 | 🔵 可扩展 |

**建议实现**（加入 OmenHardware.cs + FanPage）：

```csharp
// 1. 三风扇检测（已有 IsThreeFanSupported）

// 2. 逆转除尘（Fan Cleaner）—— 核心：
public static void StartFanCleaner(int cpuSpeed = 20, int gpuSpeed = 20, int fan3Speed = 20) {
  // 速度+128 = 反转；255 = 不参与
  SendOmenBiosWmi(0x2E, new byte[] { (byte)(cpuSpeed + 128), (byte)(gpuSpeed + 128), (byte)(fan3Speed + 128) }, 0);
}

public static void StopFanCleaner() {
  // 127 = 停止反转恢复正转
  SendOmenBiosWmi(0x2E, new byte[] { 127, 127, 127 }, 0);
}

// 3. 前置条件检查（参考官方 Criterion）：
//    - CPU/GPU/IR 温度 < 门槛（65/80/45）
//    - 必须 AC 供电（不能 DC）
//    - 不能 Eco 模式
//    - 系统空闲 30 秒

// 4. 状态机（简单版）：
//    StartFanCleaner → 等待 30 秒 → StopFanCleaner
```

**注意**：
- `0x2E` 是你项目已有的 SetFanLevel 命令（cmdType 0x2E），官方 SetSwFanControlLevel 走同一 EC 通道——具体 cmdType 需实测（官方在 PerformanceControlHelper 内部，可能是 0x2E 或专用）
- 反转速度建议用官方默认 20（低速反转，安全）
- 温度门槛：除尘前必须确认 CPU<65/GPU<80/IR<45，否则跳过并提示

## 9. 待验证项

- `SetSwFanControlLevel` 的底层 cmdType（官方 PerformanceControlHelper 未完全反编译，推测 0x2E 或 EC 直写）——可用探针测 0x2E 写 `速度+128` 观察风扇是否反转
- `CleanCreekCapabilities` 数组来源（每风扇是否支持反转）——本机 OMEN 16-am0xxx 需确认
- `Is3FanNb`（第3风扇是否存在）——本机是 3 风扇机型吗？

## 10. 本项目已实现情况（2026-08-26 实测确认）

**你的项目已经实现了逆转除尘的核心，与官方对照：**

| 方面 | 官方 | 你的项目 | 状态 |
|---|---|---|---|
| 触发 | ManualCleanCreek（命令19） | `SetFanLevel(0,0,false,true)` | ✅ 等效 |
| 反转编码 | `速度+128` | `速度+128`（FanPage→SetFanLevel fanClean） | ✅ 逐字节一致 |
| 反转时长 | `CleanCreekDuration=30000` | `Task.Delay(30000)` | ✅ 一致 |
| 恢复 | `SetSwFanControlLevel(127,127,127)` | `SetFanLevel(0,0)` | 🟡 官方用127，你用0 |
| 前置条件 | 温度<65/80/45 + AC + 非Eco + 空闲 | 无检查 | 🔵 可补 |
| Fan3独立速度 | `CleanCreekFan3Speed` | `(s1+s2)/2` | 🔵 可补 |

**探针实测（本机 OMEN 16-am0xxx）**：
- `0x20008 + cmdType 44`（GetFanType）**有效**，返回 `21 00...`
- **解码结果：Fan[0]=CPU(1), Fan[1]=GPU(2)，其余 Unsupported，capabilities(byte 8-11)=全 0**
- **结论：本机是双风扇（非三风扇），且 BIOS 未声明风扇反转能力位**

**对本机除尘的影响**：
- `IsThreeFanSupported()` → false（types[2]=Unsupported）—— 本机无第3风扇，正确
- `IsCleanCreekSupported()` → false（capabilities 全 0）—— **本机 BIOS 未启用 Fan Cleaner 能力位**
- `IsLegacyCleanCreekSupported()` → 需测 `cmd=1 + cmdType 44`（实测返回全 0，`result[0]&0x20`=0）→ **legacy 也不支持**
- **结论：这台机器的 BIOS 未开放逆转除尘功能**（命令通道存在、编码正确，但 BIOS 不支持）。除尘按钮会正确显示"不支持"。

**3 个改进建议**（对支持除尘的机型）：
1. **恢复用 127 而非 0**：官方 `SetSwFanControlLevel(127,127,127)`——127=停止反转但保持风扇正转。建议除尘结束改为支持 127 恢复。
2. **加前置条件检查**（参考官方 CleanCreekCriterion）：CPU<65°C / GPU<80°C / IR<45°C / AC 供电 / 非 Eco 模式 / 空闲 30 秒。
3. **Fan3 独立速度**：官方用 `CleanCreekFan3Speed`（独立配置，默认 20）。如目标机型是三风扇，建议支持独立 fan3 反转速度。
