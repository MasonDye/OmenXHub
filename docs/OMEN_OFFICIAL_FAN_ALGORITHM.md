# OMEN 官方风扇算法完整还原（FanHandler 参考实现）

> 来源：`PerformanceControl.dll` 反编译（Hp.Bridge.Client.SDKs.PerformanceControl.Handlers.FanHandler，328 行）
> 用途：为本项目 FanPage 的"自定义风扇曲线"提供官方 ground-truth 参考。
> 日期：2026-08-26

## 1. 算法总览

```
温度传感器 (CPU EWMA / GPU temp / IR temp)   ← 每秒
    │
    ▼
FanSetScheme 选择 (根据 ThermalControl 模式 + 是否空闲)
    ├── Manual + 滑杆      → SLIDER       (RPM = 滑杆/100 × 档位 + MinRpm)
    ├── Manual + 自定义曲线 → EWMA_ALGO    (温度查表 + 迟滞)
    ├── Auto + 非空闲       → EWMA_ALGO
    ├── Auto + 空闲         → IDLE_AUTO    (固定取最低转速)
    └── Max                → NO_SET       (不动风扇，交给 BIOS)
    │
    ▼
目标 RPM (CPU/GPU 两路) → 写回 FanData
```

## 2. FanTable 数据结构（JSON 反序列化目标）

每个设备一份，含 **三路独立温度-转速表**，每路有"高温表"和"低温表"（迟滞用）：

```csharp
class FanTable {
  // CPU 路
  List<int> Fan_Table_CPU_Temperature_List;       // 高温阈值表（升档用）
  List<int> Fan_Table_CPU_Temperature_Low_List;   // 低温阈值表（降档用）
  List<int> Fan_Table_CPU_Fan_Speed_List;         // 转速表 (RPM)
  List<int> Fan_Table_CPU_Fan_Speed_List_UI;      // UI 显示用（百分比）
  // GPU 路（同构）
  List<int> Fan_Table_GPU_Temperature_List;
  List<int> Fan_Table_GPU_Temperature_Low_List;
  List<int> Fan_Table_GPU_Fan_Speed_List;
  List<int> Fan_Table_GPU_Fan_Speed_List_UI;
  // IR 路（同构）
  List<int> Fan_Table_IR_Temperature_List;
  List<int> Fan_Table_IR_Temperature_Low_List;
  List<int> Fan_Table_IR_Fan_Speed_List;
  List<int> Fan_Table_IR_Fan_Speed_List_UI;
}
```

表语义（V1 版本，带迟滞）：
- `Temperature_List[i]` = 第 i 档的**升档**温度
- `Temperature_Low_List[i]` = 第 i 档的**降档**温度（≤ 它才降）
- `Fan_Speed_List[i]` = 第 i 档的 RPM
- 步进式：`stepRef` 记录当前档位，温度升过高温阈值 → `stepRef++`；温度降到低温阈值以下 → `stepRef--`

## 3. FanSetScheme 选择（FanHandler.GetFanSetScheme）

```csharp
static FanSetScheme GetFanSetScheme(UiAdapter ui, bool isIdle) {
  switch (ui.CurrentThermalMode) {
    case ThermalControl.Manual:
      return ui.IsCustomFanCurve ? FanSetScheme.EWMA_ALGO : FanSetScheme.SLIDER;
    case ThermalControl.Auto:
      return isIdle ? FanSetScheme.IDLE_AUTO : FanSetScheme.EWMA_ALGO;
    default: // Max
      return FanSetScheme.NO_SET;
  }
}
```

## 4. EWMA 温度平滑（EWMA.cs，27 行，完整）

```csharp
class EWMA {
  public const double LAMBDA_IDLE = 0.1;
  private double _lambdaIncrease;   // 升温系数（升得快）
  private double _lambdaDecrease;   // 降温系数（降得慢）
  private bool _isIdle;
  public double Value { get; set; }

  public void Update(bool isIdle, double cpuTemp) {
    _isIdle = isIdle;
    double lambda = _isIdle ? 0.1
                 : (cpuTemp >= Value ? _lambdaIncrease : _lambdaDecrease);
    Value = lambda * cpuTemp + (1.0 - lambda) * Value;
  }
}
```

关键点：**升温系数 ≠ 降温系数**（官方默认升快降慢），空闲时强制 λ=0.1。

## 5. 目标转速计算（三种 scheme）

### 5.1 SLIDER（手动滑杆）

```csharp
int GetSliderFanSpeed() {
  int fanSpeed = ui.FanSpeed;                      // 0..100
  var tbl = _init.Info.SwFanControlTable;
  return (int)((double)fanSpeed / 100.0 * tbl.NumOfLevels + tbl.MinRpm);
}
```

### 5.2 EWMA_ALGO（温度查表，核心）

```csharp
int GetAlgoFanSpeed() {
  // 三路分别查表，取最大
  int cpu = GetFanTuning(valid, TuningDevice.Cpu);   // 输入 = EWMA.Value
  int gpu = GetFanTuning(valid, TuningDevice.Gpu);   // 输入 = GpuTemp
  int ir  = GetFanTuning(valid, TuningDevice.Ir);    // 输入 = IrTemp
  return max(cpu, gpu, ir);
}

// V1（有低温表）—— 带步进迟滞
int FanTuningV1(TuningDevice device) {
  var high = Fan_Table_CPU_Temperature_List;      // 升档阈值
  var low  = Fan_Table_CPU_Temperature_Low_List;  // 降档阈值
  var rpm  = Fan_Table_CPU_Fan_Speed_List;
  int maxStep = rpm.Count - 1;

  if (stepRef == -1) {  // 首次
    if (temp < high[0])          { stepRef = 0; return rpm[0]; }
    if (temp >= high[maxStep])   { stepRef = maxStep; return rpm[maxStep]; }
    for (i = 0; i < maxStep; i++)
      if (temp >= high[i] && temp < high[i+1]) { stepRef = i+1; return rpm[stepRef]; }
    stepRef = 0; return rpm[0];
  }
  // 已确定档位：迟滞
  if (stepRef != 0 && temp <= low[stepRef]) { stepRef--; return rpm[stepRef]; }  // 降温降档
  if (stepRef != maxStep && temp >= high[stepRef]) { stepRef++; return rpm[stepRef]; }  // 升温升档
  return rpm[stepRef];  // 保持
}
```

**迟滞本质**：升档看 `high[i]`，降档看 `low[i]`（low < high），避免在阈值附近来回跳档。V0（无低温表）则是简单插值 + 半程防抖（`temp >= (high[i]+high[i+1])/2` 才升到下一档）。

### 5.3 IDLE_AUTO（空闲自动）

```csharp
int GetIdleModeAutoFanSpeed() {
  return _init.Json.SwFanControlCustomDefault.FanTable
         .Fan_Table_CPU_Fan_Speed_List[0];   // 固定最低档
}
```

## 6. GPU 风扇联动（GetSwFancontrol）

```csharp
(int cpuFan, int gpuFan) GetSwFancontrol(int targetFanSpeed) {
  int gpu = targetFanSpeed - 2;   // 默认 GPU 比 CPU 低 2
  var t = _init.Info.SwFanControlTable;
  int? v = t?.GetGpuFan(targetFanSpeed);
  if (v.HasValue && v.Value >= 0) gpu = v.Value;
  return (cpuFan: targetFanSpeed, gpuFan: Math.Max(gpu, 0));
}
```

## 7. 下发前置条件

```csharp
bool canSet = !systemStatus.IsNbBiosFanControl   // BIOS 接管时不动
           && !systemStatus.IsCleanCreekInProgress
           && scheme != FanSetScheme.NO_SET
           && (scheme == SLIDER || syncCounter.IsReachCycle(5));  // EWMA 每 5 周期才写一次
```

## 8. 移植到本项目的建议

1. **FanTable 用 JSON 配置**：官方从 `Json` 反序列化（`SwFanControlCustom.Get(ui, json).FanTable`），本项目可照做——把每机型曲线放 JSON。
2. **EWMA 类直接照抄**（§4，27 行）——官方就是它。
3. **迟滞算法照抄 FanTuningV1**（§5.2）——这是"自定义风扇曲线"的正确打开方式。
4. **滑杆公式照抄**（§5.1）。
5. 温度输入：CPU 用 EWMA 平滑值、GPU 用当前温度、IR 用红外温度——本项目已有 HardwareService 温度源，直接对接。
6. 写入端：官方最终写 `FanData.SetFan(cpu, gpu)`，再经 EC/WMI 下发——本项目已有 `SendOmenBiosWmi(0x2E/0x2D...)` 通道，替换目标值即可。
