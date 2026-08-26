# OMEN 官方传感器公式还原（CPU/GPU 温度·功耗·频率）

> 来源：`HP.Omen.Core.Common.dll` 反编译
> （HP.Omen.Core.Common.Utilities.SystemAdvPerformanceHelper.*）
> 用途：独立于 LibreHardwareMonitor/HWiNFO 的 ground-truth 公式，可做交叉校验或自实现。
> 日期：2026-08-26

## 0. 硬件访问通道

- 全部经内核驱动 `\\.\HpReadHWData`（`C:\Windows\System32\drivers\HpReadHWData.sys`）
- IOCTL = `0x9C419008`（deviceType 40001=0x9C41, function 2306, Method.Buffered, Access.Any）
- 用户态封装：`ReadHWData.Rdmsr(index, out eax, out edx)` / `ReadHWData.Rdpci(index, out ulong)`
- 跨进程串行化：命名互斥 `Global\Access_PCI`（每个调用包互斥，本项目 PawnIO 可复用同一名字避免冲突）

## 1. MSR 索引表（MSR_INDEX.cs 全量）

| 名称 | 值 | 用途 |
|---|---|---|
| IA32_PLATFORM_INFO | 0xCE (206) | 倍频上限 `(eax>>8)&0xFF` |
| IA32_PERF (PERF_STATUS) | 0x198 (408) | 当前倍频（位域） |
| IA32_THERM (温度) | 0x19C (412) | 每核温度 |
| IA32_ARCH_CAPABILITY | 0x10A (266) | 架构能力 bit23 |
| IA32_TEMPERATURE_TARGET | 0x1A2 (418) | TjMax |
| IA32_PACKAGE_THERM | 0x1B1 (433) | 封装温度 |
| IA32_RAPL_POWER_UNIT | 0x606 (1542) | RAPL 单位 |
| IA32_PKG_POWER_LIMIT | 0x610 (1552) | PL1/PL2 |
| IA32_PKG_ENERY | 0x611 (1553) | 封装能量计数器 |
| IA32_DRAM_ENERY | 0x619 (1561) | 内存能量 |
| IA32_PP0_ENERY | 0x639 (1593) | 核能量 |
| IA32_PP1_ENERY | 0x641 (1601) | 图形能量 |
| AMD_MPERF_RO | 0xC00000E7 | 频率基准 |
| AMD_APERF_RO | 0xC00000E8 | 实际频率 |
| AMD_PERF_CTL_0 | 0xC0010000 | |
| AMD_PERF_CTR_0 | 0xC0010004 | |
| AMD_HWCR | 0xC0010015 | |
| AMD_FIDVID | 0xC0010042 | |
| AMD_P_STATE_0 | 0xC0010064 | |
| AMD_COFVID | 0xC0010071 | 当前倍频 |
| AMD_FAMILY_17H_P_STATE | 0xC0010063 | |
| AMD_RAPL_PWR_UNIT | 0xC0010299 | |
| AMD_CORE_ENERGY | 0xC001029A | 单核能量 |
| AMD_PKG_ENERGY | 0xC001029B | 封装能量 |

## 2. Intel 温度（IntelCPU.cs）

**封装温度**（`IA32_PACKAGE_THERM` 0x1B1）：

```csharp
if (RdmsrTx(0x1B1, out eax, out _, affinity) && (eax & 0x80000000u) != 0)  // bit31 有效位
    temp = tjMax[0] - ((eax & 0x7F0000) >> 16);   // 温度 = TjMax - 数字读数
```

**每核温度**（`IA32_THERM` 0x19C）：

```csharp
if (RdmsrTx(0x19C, out eax, out _, affinity) && (eax & 0x80000000u) != 0)
    temp = tjMax[k] - ((eax & 0x7F0000) >> 16);
```

**TjMax 表**（按微架构默认，IntelCPU 构造时）：

```csharp
// 90/100/95 三档，按 microarchitecture 选择（Core2→90, Nehalem/SandyBridge→100, Haswell 之后多数 100/95）
// 优先读 IA32_TEMPERATURE_TARGET (0x1A2)：tjMax = (eax>>16)&0xFF
```

**温度有效位判断**：`eax & 0x80000000` 非零才有效（否则读数无意义，官方返回 0）。

## 3. Intel 功耗（RAPL 能量差分）

```csharp
// 初始化：
Rdmsr(0x606, out eax, out _);   // IA32_RAPL_POWER_UNIT
if (microarch == Silvermont || microarch == Airmont)
    energyUnitMultiplier = 1e-6 * (1 << ((eax>>8) & 0x1F));
else
    energyUnitMultiplier = 1.0 / (1 << (eax>>8));    // 标准：单位 = 1/2^ESU

// 每秒更新（对每个能量 MSR）：
DateTime utcNow = DateTime.UtcNow;
float dt = (float)(utcNow - lastEnergyTime[i]).TotalSeconds;
if (dt > 0.01) {
    power = energyUnitMultiplier * (eax - lastEnergyConsumed[i]) / dt;  // 瓦
    lastEnergyTime[i] = utcNow;
    lastEnergyConsumed[i] = eax;
}
```

能量 MSR 数组（IntelCPU.GetMSRs）：`{0xCE, 0x198, 0x19C, 0x1A2, 0x1B1, 0x606, 0x611, 0x619, 0x639, 0x641}`
输出标签：PKG/DRAM/PP0/PP1 → `CPU_POWER_PACKAGE` 等。

**PL1/PL2 boost 周期**（`IA32_PKG_POWER_LIMIT` 0x610）：

```csharp
ulong v = ((ulong)edx << 32) | eax;
// PL1: bits[21:17]=vals, bits[23:22]=bitS
long vals = (long)(v >> 17) & 0x1F;
long bitS = (long)(v >> 22) & 3;
double pl1 = Math.Pow(2, vals) * (1 + bitS/4.0) * Math.Pow(0.5, 10);
// PL2: bits[53:49], bits[55:54] 同上
```

## 4. Intel 频率/电压

```csharp
// 当前频率（每核，0x198 位域，按微架构）：
if (microarch == Nehalem)           freq = (eax & 0xFF) * baseFreq;
else if (microarch < SandyBridge)   freq = (((eax>>8)&0x1F) + 0.5*((eax>>14)&1)) * baseFreq;
else                                freq = ((eax>>8) & 0xFF) * baseFreq;
// baseFreq = TSC频率 / timeStampCounterMultiplier
// timeStampCounterMultiplier：读 0xCE (eax>>8)&0xFF

// 核心电压（SandyBridge+，0x198 的 edx）：
voltage = (edx & 0xFFFF) / 8192.0;   // 伏特
```

## 5. AMD17 温度（AMD17CPU.cs）

**封装温度走 PCI**（非 MSR！）：

```csharp
// PCI 地址 AMD_PCI_17_TEMP_PACKAGE = 0xFF001700
if (Rdpci(0xFF001700, out ulong value)) {
    double temp = (double)((value >> 21) & 0x7FF) / 8.0;
    if ((value & 0x80000) != 0)         // bit19 = RANGE_SEL（高温范围）
        temp -= 49.0;                   // 偏移 49°C
}
```

**CCD 温度**：`FAMILY_17H_M70H_CCD_TEMP(i) = 0x59954 + i*4`（M70H+ 步进）

**AMD10 温度**（Zen1/Zen2，PCI）：`AMD_PCI_10_TEMP_SMU=0xFF000001` 等，同 21-bit 编码

**AMD17 功耗**（RAPL）：

```csharp
Rdmsr(0xC0010299, out eax, out _);              // AMD_RAPL_PWR_UNIT
energyUnitMultiplier = 1f / (1 << ((eax>>8) & 0x1F));
// 每秒：
Rdmsr(0xC001029B, out eax, out _);              // AMD_PKG_ENERGY
power = energyUnitMultiplier * (eax - lastEnergyConsumed) / dt;
// 每核能量：0xC001029A (AMD_CORE_ENERGY)，累加得 CPU_POWER_CORE_ALL
```

## 6. AMD 频率

```csharp
// COFVID 0xC0010071
Rdmsr(0xC0010071, out eax, out _);
uint fid = eax & 0xFF;
uint did = (eax >> 8) & 0x3F;
freq = 2.0 * fid / did;    // × 100 MHz
// 或 MPERF/APERF 差分（0xC00000E7/E8）算实际负载频率
```

## 7. 移植到本项目的建议

1. **交叉校验**：用这些公式独立读温度，与 LHM/HWiNFO 读数对比（同一传感器应一致），可发现 LHM 在特定平台上的偏差。
2. **自实现路径**：若想减少第三方依赖，`Rdmsr/Rdpci` 可用本项目主驱动 **PawnIO**（IntelMsr 的 ReadMsr / SmuReg 的 PCI 读取）替代官方驱动通道。
3. **互斥**：官方用 `Global\Access_PCI`——本项目如与 OGH 并存，建议同名互斥，避免 MSR 并发读造成冲突。
4. **能量差分注意**：首次采样需"初始化 lastEnergy"，否则第一秒功耗为 0/错误——官方在构造时预读一次。
