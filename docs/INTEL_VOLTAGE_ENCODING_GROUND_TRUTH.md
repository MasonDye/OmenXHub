# Intel 电压偏移编码 —— 官方 ground truth（XtuCommon.dll 运行时验证）

> 来源：`Auto-OC\XtuCommon.dll`（Intel XTU 内核）反编译 + **.NET 运行时反射实测**
> 日期：2026-08-26
> 结论：本项目 `XtuService.cs` 的 `round(mv*1.024) << 21` **位模式正确**（C# `int` 左移 21 是 mod-2^32 运算，天然等价 11-bit 补码截断，见 §3.1 全区间实测）；`MEMORY_XTU_HSA_RE.md` 的 `v<0? v+2000 : v` 是 **HP 私有通道**编码，与 XTU mailbox 无关。

## 1. 权威字段布局（MailboxFieldResolver 解密，1606 字段）

所有电压偏移字段统一格式：

| 字段 | 官方 ID | Param1 | DataFormat | Offset | 含义 |
|---|---|---|---|---|---|
| IA Core Voltage Offset | 3 | 0 | **S11.0.10** | **21** | 核心电压偏移 |
| Graphics Core Voltage Offset | 7 | 1 | S11.0.10 | 21 | 核显电压偏移 |
| Ring Voltage Offset | 11 | 2 | S11.0.10 | 21 | 环形总线 |
| System Agent Voltage Offset | 12 | 4 | S11.0.10 | 21 | SA |
| Efficient Cores Cache Voltage Offset | 38 | 5 | S11.0.10 | 21 | E 核缓存 |
| Processor Core N Voltage Offset | 3240+N | 0 | S11.0.10 | 21 | 每核（N=0..） |
| ...Floor 系列 | +600 | 17 | S11.0.10 | 21 | 下限 |

**`S11.0.10` 语义**：有符号(signed) 11 位宽、0 整数位、10 小数位 → **1/1024 伏特单位，11-bit 二进制补码**。

## 2. 编码算法（运行时实测，非推断）

对 `FixedPointDecimalOperations.ConvertDecimaltoFixedPointDecimal(Decimal)` 反射调用：

| 输入 (V) | 输出 (uint) | 二进制解释 |
|---|---|---|
| 1.000 | 0x400 (1024) | 1024 = 1.0 × 1024 |
| 0.500 | 0x200 (512) | 512 |
| 0.250 | 0x100 (256) | 256 |
| 0.001 | 0x001 (1) | 1 |
| 0.000 | 0x000 (0) | 0 |
| **-0.001** | **0x7FF (2047)** | **11-bit 补码：4096 - 1** |
| 1.500 | 0x600 (1536) | 1536 |
| 2.000 | 0x800 (2048) | 边界（11-bit 最大正 1023 后即符号位） |

**公式**：
```
// 正值：enc = round(V × 1024)             （V 单位伏特）
// 负值：enc = (1 << 11) + round(V × 1024)  （11-bit 二进制补码）
//     即 enc = round(V × 1024) & 0x7FF，再按 11-bit 有符号解释
// 换算 mV：enc = round(mV × 1.024)
```

**写入 mailbox 时放在 bit 21 起**（Offset=21）：
```
mailbox_data = ((uint)enc & 0x7FF) << 21
// 或合并写命令：
msr_value = (0x80000011UL << 32) | (((uint)enc & 0x7FF) << 21)   // bit63=RUN_BUSY, cmd=0x11
```

## 3. 与本项目现有代码的对照

### 3.1 `XtuService.cs:173`：`round(mv*1.024) << 21` —— **已实测复核，位模式正确**

```csharp
uint data = unchecked((uint)((int)Math.Round(mv * 1.024) << 21));  // 正确；掩码 & 0x7FF 仅为可读性，行为不变
```

- ✅ `mv × 1.024` 正确（1/1024 单位）
- ✅ `<< 21` 正确（官方 Offset=21）
- ✅ **负值补码天然正确**：C# `int` 左移是 mod-2^32 运算，`x << 21` 只把 bits 10..0 移到 31..21，bits 11..31 全部溢出丢弃——数学上恒等于 `(x & 0x7FF) << 21`。负 int 的符号扩展高位恰好全部移出，无需显式截断。
  **实测（C# 全区间 -300..+300mV，`diffs=0`）**：
  | mv | round | 现有 `x << 21` | 掩码 `(x & 0x7FF) << 21` |
  |---|---|---|---|
  | -200 | -205 | 0xE6600000 | 0xE6600000 |
  | -100 | -102 | 0xF3400000 | 0xF3400000 |
  | -50  | -51  | 0xF9A00000 | 0xF9A00000 |
  | -10  | -10  | 0xFEC00000 | 0xFEC00000 |
  | +100 | +102 | 0x0CC00000 | 0x0CC00000 |

  ⚠️ **初版文档此处例算有误**，特此更正：`(-102) << 21` 实为 **0xF3400000**（非 0xFFCCCC00）；`-102 & 0x7FF` 实为 **0x79A**（非 0x5FA）。两个正确值恰好相等——所谓"缺失补码"不成立。
- ⚠️ `VoltageWriteCmdCore = 0x80000011`：cmd 位 bits[39:32]=0x11 正确；bit63(RUN_BUSY) 由写入触发、`WaitMailboxIdle` 轮询清零，与 UXTU 用法一致。此点与编码无关，真机验证即可。

### 3.2 `MEMORY_XTU_HSA_RE.md:47`：`v<0 ? v+2000 : v` ×1.024+0.5

- 这是 **HP 私有通道（HPCPUMSRCommand 槽位）** 的编码，不是 XTU OC Mailbox 的。
- `+2000` 是 HP 槽位协议的偏移约定（可能把 -1000..+1000mV 映射到 1000..3000 区间），与 XTU 的 11-bit 补码是**两套独立编码**。
- 文档把它和 XTU 混在一起记，是来源混淆——两者不能混用。

## 4. 参考实现（与现有代码等价，可选重构）

```csharp
// Intel OC Mailbox 电压偏移编码（官方 S11.0.10，11-bit 补码，bit21 起）
const int MAILBOX_FIELD_OFFSET = 21;
const uint MAILBOX_11BIT_MASK = 0x7FF;

uint EncodeVoltageOffsetMv(int mv) {
    int raw = (int)Math.Round(mv * 1.024);          // 1/1024 V 单位
    uint enc = (uint)(raw & MAILBOX_11BIT_MASK);    // 11-bit 补码截断
    return enc << MAILBOX_FIELD_OFFSET;             // 移到 bit21
}

// 写入（含 RUN_BUSY 触发）
ulong msrValue = (0x80000011UL << 32) | EncodeVoltageOffsetMv(mv);
_msr.WriteMsr(0x150, msrValue);
// 之后 WaitMailboxIdle()（轮询 bit63 清零）——本项目已有，保持
```

**验证**：-100mV → `raw=-102` → `(-102)&0x7FF=0x79A` → `0x79A<<21=0xF3400000`（与现有 `(-102)<<21` 结果一致）；回读校验时按 `(val>>21)&0x7FF` 再符号扩展 11 位还原 mV。

## 5. 附带确认（顺带修正文档错误）

- **id=79 不是 Ring Voltage Offset**：官方字段表里 79 = `Thermal Velocity Boost`（ResidencyStateRegulation 通道）；Ring Voltage Offset 的字段 ID 是 **11**（mailbox 表）/ 常量名 `RingVoltageOffset=79` 只是 ControlIdHelper 的高层控制 ID，实际映射按平台分派到字段 11。
- **id=34 也不是 CpuVoltageOffset**（在 MSR 表里 34 = `TURBO_POWER_LIMIT: POWER_LIMIT_1_TIME`）；`CpuVoltageOffset=34` 常量同理是高层 ID。**本项目 XtuService 用 `CpuVoltageOffsetId=0xFF` 自定义哨兵没问题，但不要指望 34 就是电压字段。**

## 6. 结论

| 项 | 判定 |
|---|---|
| `round(mv*1.024)` 数值 | ✅ 正确 |
| `<<21` 位偏移 | ✅ 正确（官方 Offset=21） |
| **负值补码** | ✅ **正确**——`int` 左移 21 为 mod-2^32 运算，天然等价 `& 0x7FF` 截断（全区间实测 diffs=0） |
| `0x80000011` cmd | ⚠️ 与 UXTU 一致，真机验证 |
| HP `v+2000` 编码 | 另一通道，勿混用 |
