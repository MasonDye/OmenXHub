# OMEN 官方 AMD Curve Optimizer 状态机还原

> 来源：`HP.Omen.Core.Common.dll` → `HP.Omen.Core.Common.AMDSDK.AMDSDKHelper`（713 行）
> 底层：`AmdRyzenHelper`（AmdRyzenSDK.dll，托管的 Ryzen Master SDK 封装）
> 用途：让本项目 AmdUndervoltService 与官方 OGH 共存而不互相覆盖。
> 日期：2026-08-26

## 1. 持久化位置（唯一）

```
HKCU\SOFTWARE\HP\OMEN Ally\AMDCurveOptimizer
```

| 键 | 类型 | 含义 |
|---|---|---|
| PBOEnable | DWORD(bool) | PBO 是否应开启 |
| CoreMode | DWORD | 0 = AllCore，1 = PerCore |
| AllCoreValue | DWORD(short) | 全核偏移（正值=负压量，写入时取负） |
| PerCoreValue | String | 逗号分隔的每核偏移（如 `30,25,20,...`） |
| CoAlive | DWORD | 0 = 上次开机 PBO 没生效，需重设 |

## 2. 状态机：开机恢复（Bg_InitCurveOptimizer）

```csharp
void Bg_InitCurveOptimizer() {
  // 仅 AC 电源下执行（笔记本电池下不折腾）
  if (ACLineStatus != 1) return;

  bool pboStatus = GetPBOStatus();           // 读当前 PBO 是否生效
  var reg = HKCU\SOFTWARE\HP\OMEN Ally\AMDCurveOptimizer;

  if (reg.PBOEnable 存在 && PBOEnable==true && !pboStatus) {
    // 期望开但实际没开 → 上次没成功，标记 CoAlive=0
    reg.CoAlive = 0;
  }
  // 若 PBO 已生效，不动 CoAlive（保持 1）
}
```

## 3. 状态机：恢复执行（Bg_SetPBOStatusByRegistry）

```csharp
void Bg_SetPBOStatusByRegistry() {
  Thread.Sleep(100);   // 等系统稳定
  lock (_setAMDSDKLock) {
    var reg = HKCU\SOFTWARE\HP\OMEN Ally\AMDCurveOptimizer;

    if (reg.CoAlive 存在 && CoAlive == 0) {
      // 上次失败标记 → 什么都不做（避免无限重试循环）
      return;
    }

    bool pboStatus = GetPBOStatus();
    if (reg.PBOEnable 存在 && PBOEnable==true && !pboStatus) {
      uint rc = SetPBOStatus(true);          // 开启 PBO（SetPBOScalar）
      if (rc == 0) {                          // 成功
        switch (reg.CoreMode) {
          case 0:  // AllCore
            short val = (short)reg.AllCoreValue;
            for (retry=0; rc!=0 && retry<5; retry++) {
              Thread.Sleep(200);
              rc = SetCurveOptimizerForAllCores((short)-val);   // 负压
            }
            break;
          case 1:  // PerCore
            short[] list = Parse(reg.PerCoreValue);  // CSV → short[]
            if (list.Length == CSV项数) {
              for (retry=0; rc!=0 && retry<5; retry++) {
                rc = SetCurveOptimizerPerCore(list);  // 内部逐核取负
              }
            }
            break;
        }
      }
    }
  }
}
```

## 4. 状态机：清空（Bg_SetDefaultCurveOptimizer）

```csharp
void Bg_SetDefaultCurveOptimizer() {
  SetCurveOptimizerForAllCores(0);   // 偏移归零
  SetPBOStatus(false);               // 关闭 PBO
}
```

## 5. 关键写入语义（SetCurveOptimizerPerCore）

```csharp
uint SetCurveOptimizerPerCore(IReadOnlyList<short> perCoreValues) {
  // 每核偏移全部取负（正值=负压）
  for (ccdIdx = 0; ccdIdx < ceil(核数/8); ccdIdx++) {
    List<uint> cores = GetPerCoreNumber(ccdIdx);   // 从 core orientation 位图解析
    for (i = 0; i < cores.Count; i++) {
      int idx = i + ccdIdx * cores.Count;
      rc = SetCurveOptimizerForEachCore(ccdIdx, cores[i], (short)-perCoreValues[idx]);
      if (rc != 0) return rc;   // 任一失败即中止
    }
  }
}
```

`GetPerCoreNumber`：读 `GetCurrentCoreOrientation(ccdIdx)`（bitmask），逐位解析出活跃核号。

## 6. 官方支持矩阵（IsCpuSupportedPBO）

```csharp
bool IsCpuSupportedPBO() {
  // CPU 型号表（内嵌 AmdCpuInfos.json）
  return cpuType is Cezanne or Vermeer;   // 官方白名单
}
```

## 7. 官方能力/限值查询（防超限）

```csharp
// 全部在写之前查询，超限直接返回 rc=99：
GetCPUOCBiosLimits()    // { IsOverClockingDisabled, MaxFrequency, MaxVoltage }
GetPMTableData()        // PPT/EDC/TDC 的 FusedLimit + CurrentLimit + CurrentValue
GetPBOScalarSpeed()     // PBO 倍率
```

## 8. 与官方并存的移植建议

1. **共用同一注册表键**：本项目如也用 `HKCU\SOFTWARE\HP\OMEN Ally\AMDCurveOptimizer`，写入前先读 `CoAlive`——官方开机恢复会重设，本项目若检测到 CoAlive==0 应让位（避免与官方抢写）。
2. **写入前置检查**：抄官方的 `IsOverClockable()/IsPBOSupported()` + `GetCPUBIOSLimits()` 限值检查（本项目 RyzenFamily 枚举已覆盖，缺的是限值表）。
3. **取负语义**：UI 显示"负压 30mV" → 存 `AllCoreValue=30` → 写入 `SetCurveOptimizerForAllCores(-30)`。**不要存负值再取负**（会变正压！）。
4. **AC 限制**：官方只在插电时执行 CO 恢复——本项目也应同（电池下写 SMU 可能失败）。
5. **底层通道差异**：官方走 `AmdRyzenHelper`（Ryzen Master SDK，SmuDll）；本项目走 UXTU 式 SMU 邮箱（MP1/PSMU）。同一 SMU 邮箱不同消息号——并存时用 `Global\Access_PCI` 互斥避免并发。

## 9. 本项目需要的对照表

| 官方（AMDSDKHelper） | 本项目（AmdUndervoltService） | 关系 |
|---|---|---|
| SetCurveOptimizerForAllCores(offset) | 全核 CO 写入 | 语义相同，通道不同 |
| SetCurveOptimizerForEachCore(ccd, core, offset) | 每核 CO 写入 | 需确认核心位图解析一致 |
| SetPBOScalar(bool) | PBO 开关 | 本项目未实现 → 可补 |
| SetPPTLimit/SetEDCLimit/SetTDCLimit | 功耗墙写入 | 本项目已有类似 → 对照消息号 |
| GetPMTableData | 温度/功耗读取 | 可复用官方字段名 |
