# OmenXHub 缺陷探测报告

> 日期：2026-09-11
> 方法：静态扫描 432 个 .cs（高风险模式 + 边界/健壮性 + 并发 + 持久化原子性）
> 基线：`feature/singbox-compress` HEAD = `94dfa16`

## 摘要

| 严重度 | 数量 | 状态 |
|---|---|---|
| 🔴 高 BUG-2（Timer 回调无 catch → **进程终止**） | 1 处 | ✅ 已修复 |
| 🔴 高 BUG-1（配置未校验 → 启动/恢复失败） | 4 处 | ✅ 已修复 |
| 🟠 中高 BUG-6（跨线程弹窗 → 静默失败） | 2 处 | ✅ 已修复 |
| 🟡 中 BUG-7（预设加载裸类型转换 → 静默中断） | 34 处 | ✅ 已修复 |
| 🟡 中 BUG-8（自定义预设注册表回退同型缺陷） | 21 处 | ✅ 已修复 |
| 🟡 中 BUG-3（tick 静默失败） | 1 处 | ✅ 已修复（R13） |
| 🟡 中 BUG-4（注册表读取无类型守卫） | — | ✅ 已并入 BUG-7 |
| 🟡 中 BUG-9（HardwareService 时钟回拨 → 监控冻结） | 1 处 | ✅ 已修复 |
| 🟠 中 BUG-14（启动项搬迁直删目标文件 → 数据丢失） | 1 处 | ✅ 已修复 |
| 🟢 低 BUG-12（SystemTweaks RegistryKey 句柄泄漏） | 1 处 | ✅ 已修复 |
| 🟢 低 BUG-11（TunManager Process 句柄未 Dispose） | 2 处 | ✅ 已修复 |
| 🟢 低 BUG-5（配置写入非原子） | 1 处 | ✅ 已修复（R13） |
| ⚪ 观察 BUG-10（事件 marshal 风格不一致） | 1 处 | ✅ 已加固（R14） |
| ⚪ 观察 BUG-13（DisabledSubKey 全局 Replace 脆弱性） | 1 处 | ✅ 已加固（R14） |
| ⚪ 观察 BUG-15（CleanupService 死代码含危险递归删除） | 1 类 | 记录（不可达） |
| 🟠 中高 BUG-18（**自有修复引入的语法错误** CS0139） | 1 处 | ✅ 已修复 |
| 🟠 中高 BUG-19（**自有修复引入的歧义错误** CS0104） | 1 处 | ✅ 已修复 |
| 🟡 中 BUG-17（热键录制 handler 跨触发器不互斥） | 1 处 | ✅ 已修复 |
| ⚪ 观察 BUG-16（FanService 注释行号漂移） | 3 处 | ✅ 已修正 |
| 🔴 高 BUG-23（R15：GPU 超频 NVAPI 异常线程池裸奔 → **进程终止**） | 4 处 | ✅ 已修复 |
| 🟠 中高 BUG-20（R15：SystemOptimize 绑定激活守卫失效 → 开页误弹/静默写） | 3 列表 | ✅ 已修复 |
| 🟡 中 BUG-21（R15：OsdWindow.Dismiss 枚举中改集合 → 中断） | 1 处 | ✅ 已修复 |
| 🟡 中 BUG-22（R15：线程池工作项无 catch → 进程终止面） | 7 处 | ✅ 已修复 |
| 🟡 中 BUG-24（R15：AMD CO 回调线程池无 catch） | 2 处 | ✅ 已修复 |
| 🟡 中 BUG-25（R15：FanPage 定时器裸调 NRE 竞态 + legacy 除尘无 finally） | 9 处 | ✅ 已修复 |
| 🟡 中 BUG-26（R15：静态集合并发读写 → 结构损坏/下发丢失） | 2 处 | ✅ 已修复 |
| 🟡 中 BUG-27（R15：EC 写入输入域防御缺失 + HTTP API 门域错误） | 5 处 | ✅ 已修复 |
| 🟢 低 BUG-28（R15：崩溃处理器跨线程弹窗自抛 + Logger 自抛成崩溃源） | 3 处 | ✅ 已修复 |
| 🟢 低 BUG-29（R15：NumberBox 可空裸强转 + ComboBox -1 裸转 255） | 3 处 | ✅ 已修复 |
| 🟢 低 BUG-30（R15：UI 状态机/泄漏杂项 5 项） | 5 处 | ✅ 已修复 |
| 🟢 低 BUG-31（R15：防御性杂项 6 项，含 1 观察项 DashboardPage FindResource 未改） | 5 处修复 +1 观察 | ✅ 已修复（5 处）+1 观察 |
| ⚪ 观察 R15 存疑项（V0 EC 三元/死分支/静态竞态等 9 项） | 9 项 | 记录（见 R15 节） |

---

## 🔴 BUG-2：fanControlTimer 回调无 catch → 异常终止进程

**严重度**：高 —— 可致**整个应用崩溃**（比 BUG-1 更严重，触发面更大）

### 位置

`Services/TrayService.cs:547-576`，`fanControlTimer = new System.Threading.Timer(...)`

```csharp
fanControlTimer = new System.Threading.Timer((e) => {
  if (Interlocked.Exchange(ref _fanTickRunning, 1) != 0) return;
  try {
    ...GetSmartFanSpeed / SetMaxFanSpeedOff / SetFanLevel / IsThreeFan...
  } finally {                       // ← 只有 finally，没有 catch！
    Interlocked.Exchange(ref _fanTickRunning, 0);
  }
}, null, 100, 1000);
```

### 根因

`try...finally` **没有 catch** → 回调内任何异常都会向上抛出。
`System.Threading.Timer` 的回调在**线程池线程**执行，未捕获异常 →
`AppDomain.UnhandledException` → **进程终止**（.NET Framework 中该事件无法阻止退出）。

回调每 **1 秒**运行，内部含 WMI/EC 写入（`SetMaxFanSpeedOff`、`SetFanLevel`、`IsThreeFan`→`GetFanType`）、
配置读取（`ConfigService.FanControl`）、`HardwareService` 访问——任一处抛异常即崩。

### 对照证据（同项目同场景已加 catch）

`Services/LightingTemperatureService.cs:40` 的 `Tick()`（同样是 `System.Threading.Timer` 回调）：
```csharp
static void Tick() {
  if (!_running) return;
  try { ... } catch (Exception ex) { Logger.Error($"...Tick: {ex.Message}"); }
}
```
→ 证明 fanControlTimer 缺 catch 是**遗漏**。

### 修复（✅ 已实施 2026-09-11）

在 `finally` 前插入 `catch`（与 `LightingTemperatureService.Tick` 同惯例）：
```csharp
} catch (Exception ex) {
  Logger.Error($"[TrayService] fanControlTimer tick: {ex.Message}");
} finally { Interlocked.Exchange(ref _fanTickRunning, 0); }
```
`Logger` 有 30s 去重节流，1s 周期不会刷屏。

---

## 🟠 BUG-6：tick 线程池调 DialogHelper → 跨线程创建 WPF 窗口（已修复）
- **位置**：`Services/TrayService.cs` `HandleDbUnlockCountdown()` 内 2 处 `DialogHelper.Warn`
- **根因**：`HandleDbUnlockCountdown` 由 `UpdateTooltip`（341）调用，而 `UpdateTooltip` 由
  `tooltipUpdateTimer.Elapsed`（`System.Timers.Timer`，**未设 SynchronizingObject**）在**线程池线程**触发。
  `DialogHelper._Show` 会 `new DialogResultWindow()` + `ShowDialog()` —— WPF 窗口需 STA 线程，
  线程池线程调用必然抛 `InvalidOperationException`。
- **影响**：DB 解锁失败/需接电源等提示**静默丢失**（异常被 Timers.Timer 吞），且 `UpdateTooltip`
  后续代码（含 `HandleRestoreCountdown`）被中断。
- **对照**：项目已有 `Application.Current.Dispatcher.CheckAccess()` 惯例
  （`ConfigService.cs:19`、`PresetManager.cs:449`、`AutomationProcessor.cs:183`）→ 此处是遗漏。
- **修复（✅ 2026-09-11）**：新增 `DispatchWarn(string)` 辅助，`CheckAccess()` 判断后用
  `Dispatcher.BeginInvoke` 回 UI 线程（异步不阻塞 tick，与 `ConfigService.FirePresetCycled` 同惯例）。

## 🔴 BUG-1：TrayService 配置字符串未校验 → int.Parse 崩溃

**严重度**：高 —— 可致**启动失败**与**电源恢复失败**

### 位置（4 处，均在 `Services/TrayService.cs`）

| 行 | 方法 | 触发路径 |
|---|---|---|
| 435 | `HandleRestoreCountdown` 恢复分支 | 启动后恢复 |
| 713 | `RestoreFanSettings` | **冷启动**（App.xaml.cs:232 经 Dispatcher） |
| 752 | `RestoreCpuPower` | **冷启动** / 电源恢复 |
| 1045 | `RestoreCPUPower` | **AC 电源插拔**（RestorePowerConfig） |

### 根因

`ConfigService.FanControl` / `CpuPower` 的值来自**注册表**，可为任意字符串：
- `ConfigService.cs:424` `FanControl = (string)key.GetValue("FanControl", FanControl)`
- `ConfigService.cs:426` `CpuPower = (string)key.GetValue("CpuPower", CpuPower)`
- `ConfigService.cs:567` `CpuPower = RegStr(key, "CpuPower", "max")`

正常路径写入的是 `"55 W"` / `"80%"`；但注册表被手工改坏、旧版本残留、或数据损坏时可能是 `"fast W"` / `"high%"`。此时：
```csharp
int.Parse(ConfigService.CpuPower.Replace(" W", "").Trim())  // "fast" → FormatException
int.Parse(ConfigService.FanControl.TrimEnd('%'))            // "high" → FormatException
```

### 影响

`RestoreConfig()`（App.xaml.cs:231，`Dispatcher.BeginInvoke` 内**无 try 包裹**）顺序调用
`RestoreFanSettings()` → `RestoreTempSensitivity()` → `RestoreCpuPower()` → … → `RestoreFloatingBar()`。
一旦 `RestoreFanSettings` 抛出 `FormatException`：
1. **启动路径**：Dispatcher 回调未处理异常 → 应用异常；且其后的 `AutomationService.Initialize()`、
   `MacroService.Initialize()` 等**全部跳过**
2. **AC 插拔路径**：`RestoreCPUPower` 抛异常，功耗配置未恢复

### 对照证据（同文件已用安全写法）

`HandleRestoreCountdown` 第 598-601 行处理同样语义时用的是：
```csharp
int.TryParse(ConfigService.FanControl.TrimEnd('%'), out pct);
```
→ 证明 435/713/752/1045 是**遗漏**，非刻意设计。

### 修复（✅ 已实施 2026-09-11）

统一改为 `int.TryParse`（正常值行为不变，异常值降级为跳过）：

| 行（修复后） | 改动 |
|---|---|
| 436 | `int.TryParse(_savedFanControl.TrimEnd('%'), out pct)`（失败 pct=0，不退硬件） |
| 717 | `if (!int.TryParse(..., out int pct)) break;`（失败跳过该分支硬件写入） |
| 756 | `if (int.TryParse(..., out int value) && value>=10 && value<=254) ...` |
| 1049 | 同 756 |

**兼容性佐证**：`out int x` 内联声明项目已大量使用（`AmdUndervoltService.cs:383` 等）；
`if (!int.TryParse(...)) break;` 与 `HWiNFOReaderService.cs:142` 同风格。零兼容风险。

**验证**：大括号平衡 = 0；diff 精确 4 处；（R15 更新）Windows 真机 `dotnet build` 全量编译
0 错误 0 警告，语法/语义/构建三级验证均已通过（见文末"验证体系"）。

---

## 🟡 中优先级（明细）

### BUG-7（已修复）：`LoadPresetFromRegistry` 34 处裸类型转换 → 静默中断

**严重度**：中 —— 不崩进程，但**静默数据损坏 + 无日志**（难排查）

- **位置**：`Services/ConfigService.cs` `LoadPresetFromRegistry(presetKey)`（预设子键加载）
- **根因**：34 处裸转换 `(string)key.GetValue(...)` / `(int)key.GetValue(...)` /
  `Convert.ToBoolean(...)` / `(byte)(int)key.GetValue(...)`。若预设子键被外部/旧版本写成异类型，
  **首个抛出的 `InvalidCastException` 被方法级 `catch { }` 静默吞掉**，
  其**后全部字段不再加载**（保留旧值）→ 用户表现为"切换预设后部分设置没生效"。
- **对照**：同文件主键加载 `Load()` 早已统一用 `RegStr/RegInt/RegBool/RegByte`（540-552 行，
  各自 try/catch + 默认值），预设子键加载是**遗漏**。
- **修复（✅ 2026-09-11）**：34 处裸转换统一为对应 `Reg*` 辅助（逐个字段类型严格对应
  `SavePresetToRegistry` 的写入类型）；`catch { }` 改为记录 `Logger.Warn`。
  `RegStr(..., null)` 对不存在的键返回 null，与原行为一致。

### BUG-8（已修复）：`LoadCustomPresetFromRegistry` 同型裸转换（21 处）

**严重度**：中（与 BUG-7 同源）

- **位置**：`Services/PresetManager.cs` `LoadCustomPresetFromRegistry(presetKey)`
- **根因**：21 处裸转换 `(string)/(int)key.GetValue` 与 `Convert.ToInt32(...)==1`。
  该方法**自身无 try**，调用点（第 323 行）为 `try { ... } catch { }` **空 catch** ——
  任一转换抛异常即静默失败，整个自定义预设回退加载中止（返回 null）。
- **场景**：文件式自定义预设读取失败后回退到注册表子键；子键损坏即静默失联。
- **修复（✅ 2026-09-11）**：
  - `ConfigService` 的 `RegStr/RegInt/RegBool/RegDouble/RegByte` 由 `private static` 提为
    `internal static`（消除重复，供同程序集复用）
  - `PresetManager` 21 处裸转换统一为 `ConfigService.Reg*`
  - 第 323 行空 catch 加 `Logger.Warn`

### BUG-9（已修复）：`QueryHardware` 用 `DateTime.Now` 做缓存间隔 → DST/时钟回拨会冻结监控

**严重度**：中（触发条件罕见，但后果是**硬件监控完全停更且无日志**）

- **位置**：`Services/HardwareService.cs:229-230` `QueryHardware()`
- **根因**：早退判断用 `(DateTime.Now - _lastQueryTime) < _cacheInterval`。
  `DateTime.Now` 受**夏令时切换 / NTP 校时 / 用户改时钟**影响，时钟回拨时差值为负 →
  条件恒真 → `QueryHardware` **永久早退**，温度/功耗/风扇全部停止更新。
- **对照（同文件已用 UtcNow）**：`GpuTempFresh`（188）、`GpuPowerFresh`（189）用 `DateTime.UtcNow`；
  `TrayService:347/351`、`PresentMonFpsMonitor` 亦用 `UtcNow` → 此处 `Now` 是不一致。
- **修复（✅ 2026-09-11）**：改为 `DateTime.UtcNow`（读写两处），与同文件其它新鲜度判断统一。

### BUG-11（已修复）：`TunManager` 的 sing-box Process 句柄未 Dispose

- **位置**：`Services/NetworkBoost/TunManager.cs` `_proc`（第 77 行启动失败路径、第 94 行 `Stop`）
- **根因**：`Process.Start()` 得到 `_proc` 后，Kill/退出路径仅 `_proc = null`，**未 `Dispose()`** →
  Process 句柄泄漏（反复启停 Boost 会累积）。
- **对照**：同项目 `GpuAppManager`/`TrayService` 一律 `using (var process = ...)`。
- **修复（✅ 2026-09-11）**：失败路径 `_proc.Dispose()`；`Stop` 路径 `_proc?.Dispose()`（都在 try 内）。

### BUG-10（已加固，R14）：`ExecutionStatusChanged` marshal 不一致 → 收口为统一 helper

- **位置**：`Services/AutomationProcessor.cs` —— 原第 169 行在线程池**直接 Invoke**，
  第 184 行**却经 `Dispatcher.Invoke`**（同事件两种契约）。
- **现状安全**（唯一订阅者自行 marshal），但"订阅者须自 marshal"是隐式契约，
  新增假定 UI 线程的订阅者即中招。
- **修复（✅ R14）**：照 `ConfigService.FirePresetCycled` 既有范本新增
  `FireExecutionStatus(name)`（`CheckAccess()` → `BeginInvoke` 异步 marshal，避免嵌套死锁），
  三处调用点全部收口 —— 契约从"订阅者自觉"变为"发布者结构保证"。

### BUG-14（已修复）：`StartupItemOptimizer.SetFolderEnabled` 直删目标文件 → 数据丢失

**严重度**：中（**删除用户文件**）

- **位置**：`Services/SystemOptimization/StartupItemOptimizer.cs` `SetFolderEnabled`
  （原第 265 行 `if (File.Exists(dst)) { try { File.Delete(dst); } catch { return false; } } File.Move(src, dst);`）
- **根因**：活动 Startup 目录与 `Startup\Disabled\` 被 `Enumerate` 当作**两个独立条目**
  （`FolderId` 基于完整路径），各自可单独启停。当把 `Disabled\foo.lnk` 启用回活动目录时，
  若活动目录已有同名 `foo.lnk`（用户自行放置的**不同文件**），旧的 `File.Delete(dst)` 会**静默删除它**；
  且删后若 `File.Move` 再失败，用户的原文件已不可恢复。
- **修复（✅ 2026-09-11）**：目标存在时**先改名为 `.bakN`（带序号避撞）**，`Move` 成功后再删备份；
  `Move` 失败则把备份**还原回目标**。任何路径都不静默丢文件。

### BUG-12（已修复）：`SystemTweaks.Open` 的 RegistryKey 句柄泄漏

- **位置**：`Services/SystemOptimization/SystemTweaks.cs:515`
  `var baseKey = RegistryKey.OpenBaseKey(hive, view);` —— **未 using**
- **对照**：项目其它 5 处 `OpenBaseKey`（`CoreKeepService:237`、`StartupItemOptimizer:94/105/199/226`）
  **全部 using** → 此处遗漏，`baseKey` 句柄泄漏。
- **修复（✅ 2026-09-11）**：改为 `using (var baseKey = ...) return ...`（返回的子键持有独立句柄，
  baseKey 用后可安全释放）。

### BUG-13（已加固，R14）：`DisabledSubKey/EnabledSubKey` 全局 Replace → 末段精确匹配

- 旧实现 `subKey.Replace("Run","RunDisabled")` 为**全串替换**，正确性依赖"常量路径恰好只含
  一个 Run 子串"的巧合。仿真实证：`Software\Runtime\Run` 会被旧实现损坏为
  `Software\RunDisabledtime\RunDisabled`（`Runtime` 里的 `Run` 也被替换）。
- **修复（✅ R14）**：改为仅对**最后一个路径段**做**精确名匹配**变换
  （`Run↔RunDisabled`、`RunOnce↔RunOnceDisabled`，其余名原样）。
- **等价性证明**：三条常量（Run/RunOnce/WOW6432Node\Run）新旧输出逐字节一致、往返互逆；
  无匹配路径幂等；对假想路径新实现严格更强。`CoreKeepService.SelfCheck` 的往返断言继续看门。

### BUG-15（观察，死代码，含危险操作）：`CleanupService` 全类无调用

- **位置**：`Services/CleanupService.cs`（103 行，注入为"恢复出厂"清理）
- **现状**：全项目**无任何调用**（含反射/动态调用已排查）→ **不可达**，故当前无风险。
  注释称"由 PerfPage「恢复出厂」按钮调用"，但 PerfPage 只有"重置为默认值"（走 PresetManager），
  并无 CleanupService 接线。
- **若将来接线的风险**：
  1. `DeleteDir(@"C:\Program Files\OmenXHub")` **递归删除安装目录**（正在运行的程序）——语义可疑，
     且 `DeleteDir` **无路径校验**（对比 `DiskCleaner.Guard()` 的 `full.Length<6` 根目录防护）
  2. 无二次确认逻辑（该类内）；触发即执行破坏性删除
- **建议**：接线前补路径白名单校验 + 二次确认；或若为卸载脚本专用则标注清楚。**本轮未改**（不可达，改动无收益）。

### BUG-16（已修正，文档）：`FanService` 注释引用过时行号

- `Services/FanService.cs:217/226/245` 注释指向 `OmenHardware.cs:205-208` 的"EC byte<10 (<500) 反弹风险"，
  但当前 `OmenHardware.cs:205-208` 已是 `IsThreeFanSupported`（行号随代码变动漂移）。
- **非代码 bug**：`ClampRpm` 的 `[500,6000]` 与默认曲线 600 floor 逻辑自洽（曲线更保守）。
- **修正（✅ 2026-09-11）**：3 处注释去掉失效的 `(OmenHardware.cs:205-208)` 行号引用，
  改为描述性引用（"EC 速度字节 <10 即 <500 RPM 时可能反弹"），消除行号漂移误导。

### BUG-17（已修复）：热键录制 handler 跨触发器不互斥 → 按键同时写入多个触发器

**严重度**：中（静默错误赋值）

- **位置**：`Views/PipelineEditorWindow.xaml.cs` `BuildTriggerValueControl` 的 Hotkey 分支（录制按钮）
- **根因**：每个 Hotkey 触发器的"录制"按钮各自 `win.PreviewKeyDown += handler`，handler 挂在**同一个编辑窗口**上。
  代码只防了"同按钮连点"（`btn.IsEnabled = false`），**未防跨触发器**：
  点 A 录制（未按键完成）再点 B 录制 → **A 的 handler 仍挂载** → 按一个组合键会**同时触发 A/B 两个 handler**
  → A 的 `committed` 被意外赋值（用户以为 A 已取消）。
- **修复（✅ 2026-09-11）**：新增窗口级录制会话字段 `_hkRecWin/_hkRecHandler/_hkRecCancel` +
  `CancelActiveHotkeyRecording()`；启动新录制前先取消旧会话（移除 handler + 恢复旧按钮/文案）；
  正常结束（Escape/成功）时清空会话字段。编辑器为 `ShowDialog` 模态 → 同一时刻仅一个窗口，静态字段安全。

### BUG-18（已修复）：BUG-1 的修复引入 `break` 语法错误（**自我复核发现**）

**严重度**：中高（**编译不过 → 整个项目无法构建**）

- **来源**：Round 4 修复 BUG-1 时，将 `RestoreFanSettings` 内的
  `int.Parse(FanControl.TrimEnd('%'))` 改为 `if (!int.TryParse(..., out int pct)) break;`。
- **错误**：`break;` 出现在 **`if-else if` 链内**（`RestoreFanSettings` 方法体内**无任何循环/switch**）
  → C# **CS0139**（没有要中断或继续的封闭循环）→ **编译失败**。
- **发现方式**：Round 11 自我复核（逐方法确认 `break` 的封闭控制结构）+
  **Roslyn 语法解析验证**（下节）。此前 7 轮因无编译环境未暴露。
- **修复（✅ 2026-09-11）**：改为包裹式
  `if (int.TryParse(..., out int pct)) { SetFanLevel(...); UpdateCheckedState(...); } else { Logger.Warn(...); }`
  —— 语义与意图一致（失败不写硬件、不抛异常），且合法。

### ✅ 语法验证（Roslyn，本轮新增）

用 Roslyn（`Microsoft.CodeAnalysis.CSharp`）对全项目做**纯语法解析**（无需 net481 引用）：

```
389 个 .cs 文件  →  OK 389  /  FAIL 0   （exit=0）
```

覆盖全部改动文件（TrayService/ConfigService/PresetManager/HardwareService/TunManager/
SystemTweaks/StartupItemOptimizer/FanService/PipelineEditorWindow）与其余 380 个源文件。
→ **确认所有修复语法合法**（BUG-18 已消除）。

> 说明：语法解析 ≠ 完整编译（不校验类型/引用），但能捕获 `break`/括号/等号等结构性错误。
> 环境：`/opt/dotnet9/dotnet` + Roslyn 4.9.2。

### BUG-19（已修复）：BUG-6 的修复引入 `Action` 歧义（**语义验证发现**）

**严重度**：中高（**编译不过**）

- **来源**：Round 4 修复 BUG-6 时新增 `DispatchWarn`，其中
  `app.Dispatcher.BeginInvoke(new Action(() => ...))` 用了**裸 `Action`**。
- **错误**：`TrayService.cs` 第 18 行 `using Microsoft.Win32.TaskScheduler;` 该命名空间也有 `Action` 类
  → **CS0104**（`Action` 在 `Microsoft.Win32.TaskScheduler.Action` 与 `System.Action` 间歧义）→ **编译失败**。
- **对照**：同文件其它 3 处（160/225/1561 行）**一律用 `System.Action`** → 我的新代码违反项目惯例。
- **发现方式**：Round 12 **Roslyn 语义编译**（见下节）。
- **修复（✅ 2026-09-11）**：`new Action(...)` → `new System.Action(...)`。

### ✅✅ 语义编译验证（Roslyn + net48 参考集 + 本地 NuGet，本轮新增）

在语法解析之上，进一步用 **Roslyn `CSharpCompilation`** 做**语义编译**：

- **引用**：net48 参考程序集（264 个）+ `bin/Release/net481` 构建产物 + `Resources/` + `lib/`（共 303 个引用 dll）
- **源**：389 个 .cs
- **结果**：1751 个诊断，**经分类全部为环境噪音**：
  | 类别 | 数量 | 性质 |
  |---|---|---|
  | CS0103 `InitializeComponent`/XAML 命名元素 | 1569 | XAML `.g.cs` 未参与编译（预期） |
  | CS1061 XAML 元素成员（`DataPanel`/`CpuRow`…） | 51 | 同上 |
  | CS0246 `Windows.Devices.Radios`（WinRT 契约未引用） | 1 | 缺 `Microsoft.Windows.SDK.Contracts` |
  | CS0009/CS1509 某 dll 非托管程序集 | 7 | 环境 |
  | 其余（`NavigationView` 等 XAML 基类） | 123 | XAML `.g.cs` 缺失 |
  | **项目 `Services/` 真实错误** | **0** | ✅ |
- **发现并修复**：BUG-19（CS0104）—— 修复后该项目错误消失（1752→1751）。
- **结论**：除 XAML 生成代码缺失导致的预期噪音外，**项目自有 C# 代码无语义错误**。

### BUG-3（已修复，R13）：`tooltipUpdateTimer.Elapsed → UpdateTooltip()` 无顶层 try

- `Services/TrayService.cs:100` `tooltipUpdateTimer.Elapsed += (s, e) => UpdateTooltip();`
- `UpdateTooltip()` 是 tick 主循环，调用 `QueryHardware`/`CheckAutoFanProtect`/`GenerateDynamicIcon`（GDI）/
  `HandleDbUnlockCountdown`/`HandleRestoreCountdown` 等，**均无 try/catch**。
- net481 `System.Timers.Timer` 会**吞掉** Elapsed 异常：不崩进程，但整拍**静默中断且无日志**
  （若 `QueryHardware` 持续性抛错，恢复/解锁倒计时状态机**永久停摆**而无人知晓）。
- **修复（✅ 2026-09-11 R13）**：两段式兜底 —— `UpdateTooltip` 改为调度器：
  显示/保护段整体平移为 `UpdateTooltipDisplay()`（原主体逐行不变）+ `try/catch → Logger.Warn`；
  倒计时段（DB 解锁/恢复状态机）独立 `try/catch → Logger.Error`。
  显示段故障不再连带跳过倒计时推进；两类故障均留痕。
- **验证**：Roslyn 语法 OK；语义编译错误数与基线一致（1751，Services 层零错误）。

### BUG-4（已并入 BUG-7 修复）：注册表读取的类型守卫
- 全局 `ConfigService.Load()`（主键）**已**用 `RegStr/RegInt/RegBool/RegDouble/RegByte` 辅助（540-552 行,各自 try/catch+默认值）✅
- 唯 `LoadPresetFromRegistry`（预设子键）遗漏 → 见 BUG-7

### BUG-5（已修复，R13）：自定义预设写入非原子
- `PresetManager.SaveCustomPreset` 原 `File.WriteAllText(...)` 直接覆写（同文件/同项目
  `MacroService.Save`、`CoreKeepService.Save` 均已是 `.tmp`+`File.Replace` 原子换入）→ 又一处遗漏。
- 崩溃/断电于写一半会留截断 JSON；下次 `LoadCustomPreset` 解析失败静默回退注册表旧值，
  用户改动丢失。
- **修复（✅ 2026-09-11 R13）**：照搬项目范本 —— `WriteAllText(path+".tmp")` →
  存在则 `File.Replace(tmp, path, null)`，否则 `File.Move(tmp, path)`；异常仍走原注册表回退 catch。

---

## 🟢 已排查、确认无问题的"疑似"项

| 疑似点 | 结论 |
|---|---|
| `PresentMonFpsMonitor.cs:293` `for (i = 0; i <= line.Length; i++)` | ✅ 刻意设计（`end` 哨兵处理末字段），无越界 |
| `App/GpuAppManager.cs:474/492` `.Result` | ✅ 在 `WaitForExit()` 后，且控制台无同步上下文，安全 |
| `GpuAppManager.cs:208/210` `Split(':')[1]` | ✅ 前置 `Contains(":")` / `i>0` 保护 |
| `AutomationProcessor.cs:373` `Split(':')[1]` | ✅ 前置 `value.Contains(":")` 保护 |
| `OmenLighting.cs` / `LightingPage` 的 `.Wait()` | ✅ 注释表明已识别 UI 死锁风险并规避 |
| `FanService.ParseShareCode` `parts[0]` | ✅ 前置 `parts.Length < 4` 保护 |
| `CoreKeepService` 41 处空 catch | ✅ 多数是防御性 `try{}catch{}`（进程/句柄操作），非 bug |
| `FloatingWindow`/`LightingPage` 等页面订阅静态事件 | ✅ 均在 `OnClosed`/`Unloaded` 对称 `-=`；LightingPage 还先 `-=` 再 `+=` 防重复 |
| `HardwareService` 静态状态字段 | ✅ 核心状态用 `lock (_lock)` 属性封装（读写均加锁） |
| `SendOmenBiosWmi` COM 并发 | ✅ `lock (_scopeLock)` 串行化 |
| `Logger.WriteToFile` 节流字段 | ✅ 在 `lock (FileLock)` 内 |
| `MainWindow._statusTimer` | ✅ 幂等 Start、Stop 时 `-=`+`Dispose`、Elapsed 经 `Dispatcher.InvokeAsync` |
| `GetOmenKeyTask` 命名管道循环 | ✅ 完整 `catch`(OperationCanceled/Exception)+日志+退避 |
| `OmenHardware` 唯一空 catch | ✅ `finally` 内删临时文件（合理防御） |
| `MacroController._pressedKeys` 线程安全 | ✅ WH_KEYBOARD_LL 回调在安装钩子的 UI 线程执行，`Clear/Add/Remove` 同线程无竞态 |
| `MacroController` 录制 `delay` 时钟回拨 | ✅ 回放侧 `if (delay > 0)` 过滤负值，无异常 |
| `OsdWindow.Show*Osd` 跨线程调用 | ✅ `ShowOsd` 内部 `Dispatcher.Invoke`，线程池调用安全 |
| `DiskCleaner` 删除操作 | ✅ `Guard()` 用 `full.Length < 6` 排除根目录 + 自检 |
| `AutomationProcessor` drain 线程 | ✅ `ConcurrentQueue` + `Interlocked` CAS + 重新拉起 drain（注释处理竞态） |
| `RaplPowerLimitService` 除零/溢出 | ✅ `powerUnit<=0→8`、`WattToRaw` 钳制 + 溢出保护 + 读回验证 |
| `HWiNFOReaderService` 解析 | ✅ `TryParse` + `Split().Skip().FirstOrDefault() ?? ""` + 单位严格校验 |
| `SystemTweaks` 系统级注册表写入 | ✅ 仅操作 `RegEdit` 数据表内的键值；失败由调用方回滚提示 |
| `StartupItemOptimizer` UI 失败处理 | ✅ `SystemOptimizeWindow` 对失败回滚 Toggle + `DialogHelper.Warn` |
| `SystemServiceOptimizer` 服务修改 | ✅ 输入校验(防路径注入) + 改后读回校验 + 失败回滚 + 句柄全 finally 释放 |
| `XtuService` MSR 预取器 | ✅ 每核亲和 + `finally` 恢复 + 掩码钳制 + 回读校验 |
| `ConfigService` 键名一致性 | ✅ 136 写入键 / 130 读取键，**无"读取但无写入"**（无拼写错误） |
| `FanService` RPM 边界 | ✅ `ClampRpm[500,6000]` + 曲线 600 floor + DEBUG assert 校验单调/区间 |
| `FloatingWindow` 多实例管理 | ✅ `_instances` 增删全在 `Dispatcher.Invoke`；遍历用 `.ToArray()` 快照；`OnClosed` Remove 幂等 |

---

## 复现方法

**BUG-1**（配置未校验）：
```powershell
$k = "HKCU:\Software\OmenXHub"        # ConfigService.RegistryPath
Set-ItemProperty $k CpuPower "fast W"  # 模拟注册表损坏/旧残留
# 重启 OmenXHub（修复前）→ 启动 Dispatcher 回调抛 FormatException → 后续初始化跳过
```

**BUG-2**（Timer 无 catch）：
- 令 `fanControlTimer` 回调内任一硬件调用抛异常（如 WMI 瞬时失败）
- （修复前）→ 线程池未捕获异常 → AppDomain.UnhandledException 弹窗 → 进程终止

**BUG-6**（跨线程弹窗）：
- 触发 DB 解锁失败路径（断开 AC 电源时点解锁）→ `HandleDbUnlockCountdown` 走 Warn 分支
- （修复前）→ 线程池线程 `new DialogResultWindow()` 抛异常被 Timer 吞 → 提示不显示

---

## 修复汇总（本轮）

| BUG | 文件 | 改动 |
|---|---|---|
| BUG-1 | `Services/TrayService.cs` | 4 处 `int.Parse` → `int.TryParse` |
| BUG-2 | `Services/TrayService.cs` | `fanControlTimer` 回调补 `catch` + Logger |
| BUG-6 | `Services/TrayService.cs` | 新增 `DispatchWarn()`，2 处弹窗回 UI 线程 |
| BUG-7 | `Services/ConfigService.cs` | `LoadPresetFromRegistry` 34 处裸转换 → `Reg*` + catch 加日志 |
| BUG-8 | `Services/ConfigService.cs` + `Services/PresetManager.cs` | `Reg*` 提为 internal；`LoadCustomPresetFromRegistry` 21 处裸转换 → `Reg*`；空 catch 加日志 |
| BUG-9 | `Services/HardwareService.cs` | `QueryHardware` 缓存间隔 `DateTime.Now` → `UtcNow` |
| BUG-11 | `Services/NetworkBoost/TunManager.cs` | `_proc` 两处路径补 `Dispose()` |
| BUG-12 | `Services/SystemOptimization/SystemTweaks.cs` | `OpenBaseKey` 补 `using` |
| BUG-14 | `Services/SystemOptimization/StartupItemOptimizer.cs` | 目标同名文件先备份 `.bakN` 再 Move，失败还原 |
| BUG-16 | `Services/FanService.cs` | 3 处注释去掉失效行号引用，改描述性 |
| BUG-17 | `Views/PipelineEditorWindow.xaml.cs` | 热键录制加窗口级会话互斥（`CancelActiveHotkeyRecording`） |
| BUG-18 | `Services/TrayService.cs` | 修正 BUG-1 引入的 `break`（CS0139）→ 包裹式 TryParse |
| BUG-19 | `Services/TrayService.cs` | 修正 BUG-6 引入的 `Action` 歧义（CS0104）→ `System.Action` |
| BUG-3 | `Services/TrayService.cs` | （R13）`UpdateTooltip` 两段式 try/catch 兜底 + 日志 |
| BUG-5 | `Services/PresetManager.cs` | （R13）`SaveCustomPreset` 改 temp+Replace 原子写 |
| BUG-10 | `Services/AutomationProcessor.cs` | （R14）新增 `FireExecutionStatus` 统一 marshal，三处收口 |
| BUG-13 | `Services/SystemOptimization/StartupItemOptimizer.cs` | （R14）子键变换改末段精确匹配（含等价性仿真证明） |

**验证（Round 13 收口时）**：
- 语法解析：全项目 389 个 .cs → **OK 389 / FAIL 0**
- 语义编译（Roslyn + net48 参考集 + 本地 NuGet，303 引用）：错误总数与基线一致
  （1751，全部为 XAML `.g.cs` 缺失 / WinRT 契约等环境噪音），`Services/` 自有代码
  **零错误、零警告**；BUG-3/5/10/13 修复均经此双级验证后合入（防 BUG-18/19 型回归）
- ~~真机运行验证仍受环境限制（net481 WPF 无法在 Linux 执行）~~ → **R15 已解除该限制**：
  本轮在 Windows 真机 `dotnet build`（SDK 8.0.420）全量编译 **0 错误 0 警告**，并运行
  `OmenXHub.exe --selftest` 回归 **ExitCode=0、全部 PASS**（含 FrontendRelease 三项断言）。
  上会话的 Roslyn 模拟（1751 环境噪音）已被真机构建取代。

---

## Round 15（R15）：未覆盖区域增量探测 + 真机验证收口

> 探测范围（前 14 轮集中在 `Services/`，本轮专攻浅覆盖区）：
> `OmenHardware.cs`(1129) + `Models/`(59) + `Controls/`(63) + `Windows/`(21) + `Properties/`(56)
> + `Views/` 全部 9 文件(3277) + `App/` 4 文件(2832) + `Pages/` 全部 12 文件(10265)
> ≈ **17,700 行逐文件完整读取**（四路并行子代理，覆盖度自证见各节）。
> 复核方式：主代理对每条高危发现**直接读源码验证**后才动手；两处独立报告同一问题
> （PerfPage GPU 超频线程池裸调 NVAPI）互为交叉印证。

### 🔴 BUG-23（已修复，R15）：GPU 超频 NVAPI 异常线程池裸奔 → 进程终止

**严重度**：高（**进程级崩溃**，可在无 N 卡机器稳定触发）
- **位置**：`Pages/PerfPage.xaml.cs:683/696/712/725` 四处
  `ThreadPool.QueueUserWorkItem(_ => GpuAppManager.SetCoreClockOffset(val))` 无 try/catch；
  根因在 `App/GpuAppManager.cs`：`NVIDIA.Initialize()` 在 try 块**之外**，
  `GetCoreClockOffset()/GetMemoryClockOffset()` 还有 `GetPhysicalGPUs()[0]` 空数组越界。
- **触发条件**：无 NVIDIA 独显/驱动异常机器上拖动 GPU Core/Memory 超频滑条或 NumberBox。
  NVAPI 初始化失败抛 `NVIDIAResultException` → .NET 4.8 线程池未捕获异常**直接终止进程**
  （`AppDomain.UnhandledException` 只能弹窗不能阻止；与 UI 线程 `DispatcherUnhandledException`
  Handled=true 可存活不同）。
- **对照证据**：同项目 `Services/TrayService.cs:910/912` 调同类 API 显式包了 try/catch。
- **修复**：库边界收口 —— `GpuAppManager` 四个方法整体 try/catch + `Logger.Verbose`，
  `Initialize()` 移入 try 内，getter 补 `gpus.Length == 0` 防护返回 0。所有调用方
  （UI 线程池 / HTTP API）一次性变安全。

### 🟠 BUG-20（已修复，R15）：SystemOptimizeWindow 绑定激活守卫失效 → 开页误弹/静默写

**严重度**：中高
- **位置**：`Views/SystemOptimizeWindow.xaml.cs` ReloadServices/ReloadStartup/ReloadTweaks
  三处 `_loading* = true; List.ItemsSource = items; _loading* = false;`
- **根因**：WPF 容器 realize 与绑定激活发生在**随后的 layout 遍**（Render 优先级），
  同步复位守卫时 `Checked`/`SelectionChanged` 尚未触发 → 守卫形同虚设。
  子代理用独立 WPF 探针实测证实（事件到达时 `_loading=False`）。
- **后果**：打开服务页时，不可改服务（Boot/System，startType<2）的 ComboBox 绑定激活为
  index 0 → 误调 `SetStartupType` 失败 → **连续弹多条"服务修改失败"模态框**；
  已应用 tweak 被重复 Apply（Partial 被静默补全）；启动项被重复 SetEnabled。
- **修复**（双保险）：①三处守卫复位改 `Dispatcher.BeginInvoke(..., DispatcherPriority.Loaded)`
  推迟到 layout 遍之后；②`ServiceType_SelectionChanged` 补"值未变即返回"
  （`combo.SelectedIndex == vm.StartupTypeIndex` 直接吞掉，程序化激活必等值）。

### 🟡 BUG-21（已修复，R15）：OsdWindow.Dismiss 枚举中修改集合 → 中断

- **位置**：`Views/OsdWindow.xaml.cs:93` `foreach (var w in _instances)` 内 `w.Close()`
  → 同步触发 `Closed` → `RemoveAndRepack` → `_instances.Remove(win)` → `MoveNext` 抛
  `InvalidOperationException`，Dismiss 提前中断（后续窗口未关、`Clear()` 未执行）。
- **触发条件**：关闭 OSD 开关瞬间屏幕上有淡出期 OSD（1.5s 窗口期）。
- **修复**：快照遍历 `_instances.ToArray()`；顺带 `ShowOsd` 的 `Application.Current.Dispatcher`
  补 `?.` 判空（存疑-C 一并处置）。

### 🟡 BUG-22（已修复，R15）：线程池工作项无 catch → 进程终止面（7 处）

- **位置**：`Views/SystemOptimizeWindow.xaml.cs`（OneClickOptimize/Restore 两按钮 +
  ReloadServices/Startup/Tweaks 三枚举 + TweakToggle worker）、
  `Views/ProcessSelectDialog.xaml.cs` LoadProcesses。
- **根因**：`Enumerate()` 系（SCM/注册表/P-Invoke）可抛，线程池裸奔即终止进程；
  且异常路径下按钮 `IsEnabled` 永不复位。
- **修复**：worker 体整体 try/catch + Logger，异常时 BeginInvoke 复位按钮并 Warn 弹窗。

### 🟡 BUG-24（已修复，R15）：AMD Curve Optimizer 回调线程池无异常保护（2 处）

- **位置**：`Pages/PerfPage.xaml.cs:1718`（全核 CO）与 `:1947`（分核 CO 弹窗应用）。
  `AmdUndervoltService` 的 PawnIO ioctl 与窗口关闭后的 `Dispatcher.Invoke` 均可抛。
- **修复**：worker 体兜底 + UI 回写段各自防"页面已卸载"。

### 🟡 BUG-25（已修复，R15）：FanPage 定时器裸调 NRE 竞态 + legacy 除尘无 finally

- **裸调**：`Pages/FanPage.xaml.cs` 9 处 `TrayService.fanControlTimer.Change(...)` 无 `?.`。
  该字段在 `StartTimers()`（`LibreComputer.Open()` 秒级慢初始化完成后）才赋值，
  而主窗口**先显示** → 启动竞态窗口内切风扇模式 NRE（模式已存但硬件未动作，"切了没反应"）。
  同文件 567 行已有 `?.` 写法，证明作者知情。→ 全部统一 `?.`。
- **除尘**：legacy CleanCreek 路径 `SetLegacyCleanCreek(true) → await → (false)` 无 try/finally，
  中途抛异常风扇**停在逆转状态**且异常静默。→ 补 finally（对照标准路径 927-939 的既有写法）。

### 🟡 BUG-26（已修复，R15）：静态集合并发读写 → 结构损坏/下发丢失（2 处）

- `App/OmenLighting.cs:141` `_lastDeviceColors`：UI 线程/动画定时器线程池/温度联动/HTTP API
  四路并发读写无锁 Dictionary → 经典结构损坏。→ 改 `ConcurrentDictionary`（写侧本就存副本、
  读侧仅 TryGetValue，零语义变化）。
- `Pages/LightingPage.xaml.cs:902` `_perKeyColors`：UI 写 vs 线程池 `foreach` 枚举 →
  "Collection was modified" 该次下发丢失。→ 补静态锁（实例锁 `_perKeyLock` 锁不住静态字段），
  枚举侧取快照，三处写侧加锁。

### 🟡 BUG-27（已修复，R15）：EC 写入输入域防御缺失 + HTTP API 门域错误（5 处）

- `OmenHardware.SetFanLevelInternal`：`(byte)fanSpeed` 无钳制，负值补码截断
  （-1 → 0xFF=255，向 EC 写"最高速"，语义完全反转）。→ 新增 `ClampFanByte`（0-255 钳制，
  合法 0-100 原样通过，零回归）。
- `OmenHardware.SetIccMaxByWmi`：EC 有效域 {0}∪[160,255]（1-159 死区致 throttling/hang，
  PerfPage 注释自证），但 HTTP API 门只拦 50-250 → 50-159 可被外部进程原样下发。
  → 库边界钳制 + API 门改 `0 或 160-255`。
- `OmenHardware.SetLoadLine`：参数是**档位索引**（0=不设置，1-15，UI Tag 实证），
  HTTP API 门却按 80-130 校验（把"mΩ 值"当档位）→ 拒绝一切合法档位、放行 80-130 非法档位
  直写 EC。→ 库边界按 `GetLoadLineSupportLevels()` 拦截 + API 门改 0-15。

### 🟢 BUG-28（已修复，R15）：崩溃处理器自身缺陷（3 处）

- `App.xaml.cs CurrentDomain_UnhandledException`：非 UI 线程直接调 `DialogHelper.Error`
  （内部遍历 `Application.Current.Windows` + new FluentWindow）→ 自抛
  `InvalidOperationException`，原始异常信息展示失败，且原先**不落盘**。
  → 先 `Logger.Error` 保底，再 `Dispatcher.BeginInvoke` marshal 回 UI 弹窗，弹窗再兜底。
- `App/Logger.cs WriteToFile/Verbose`：`File.AppendAllText` 无自保 —— 日志被占用/目录只读时，
  崩溃处理器内的 Logger 自己再抛 → `args.Handled` 来不及置位 → **日志系统成为崩溃源**。
  → 两处写盘整体 try/catch 静默降级。
- 补 `TaskScheduler.UnobservedTaskException` 日志 handler（项目多处 Task.Run 火-忘，
  原先异常完全静默）。

### 🟢 BUG-29（已修复，R15）：可空/越界裸转换（3 处）

- `Pages/CoreKeepPage.xaml.cs:937/947`：`(int)(CoreKeepGuardInterval.Value * 1000)` ——
  Wpf.Ui `NumberBox.Value` 是 `double?`，清空输入框即抛 `InvalidOperationException`
  （FanPage 同款控件已有 null 守卫，此处对齐 `?? 2`）。
- `Pages/LightingPage.xaml.cs:712`：`PerKeySpeed = (byte)SelectedIndex` 无 -1 守卫 →
  静默写 255 持久化并下发 MCU。→ `SelectedIndex < 0` 早退。

### 🟢 BUG-30（已修复，R15）：UI 状态机/泄漏杂项（5 处）

- `Views/ProcessSelectDialog.xaml.cs`：`OkBtn.IsEnabled=false` 后从不恢复 → "确定"永远灰，
  唯一出口双击列表项。→ `ApplyFilter` 末尾按结果数恢复。
- `Views/StorageCleanWindow.xaml.cs`：`Rescan` 无 finally → 异常后界面**永久卡"忙"**
  （按钮全禁用、状态停"清理中"）。→ try/finally 复位。
- `Views/PipelineEditorWindow.xaml.cs`：热键录制会话静态字段（`_hkRecWin/_hkRecHandler/
  _hkRecCancel`）在录制中关窗时不清理 → 钉住已关闭窗口可视树 + 幽灵回调。→ `OnClosed`
  中若本窗口是活跃会话则 `CancelActiveHotkeyRecording()`。
- `Pages/NetworkBoostPage.xaml.cs`：每次 `Loaded` 给两控件 `+=` 匿名 `LostFocus`（CachedPageService
  缓存页,无法 `-=`）→ 来回切 N 次后一次失焦触发 N+1 次注册表写。→ 命名方法 + 先退订再订阅
  （对齐本文件 `OnLog` 既有幂等写法）。
- `Pages/MacroPage.xaml.cs`：`StopRecordingWatcher` 停 timer 不清 `_watcherPage` 静态引用 →
  录制中离开页面钉住整个 Page 实例。→ 置空（Unloaded 路径已先 Save 收尾，安全）。

### 🟢 BUG-31（已修复，R15）：防御性杂项（6 处）

- `OmenHardware.IsGamingProduct(null)` NRE → 校验等级恒 0（产品徽标降级）。→ 空串早退。
- `Models/LightingScene.Clone` 漏拷 `LightBarColors`（后加字段）→ 副本与内置场景共享数组。
  → 补防御性拷贝（对齐 ZoneColors/ScheduledDays）。
- `App.xaml.cs ShowExistingWindow`：`WaitForInputIdle` 对无消息循环同名进程抛
  `InvalidOperationException` → 冒泡跳过第二实例 `Shutdown()` 残留半初始化进程；
  `GetProcessesByName` 的 Process 未 Dispose。→ using + try/catch。
- `App.xaml.cs:187` `int.Parse(versionString)` 版本位数增长即 Overflow 中断启动。→ TryParse。
- `Pages/DashboardPage.xaml.cs:1508` `FindResource` 直转（同域惯例是 TryFindResource+兜底）。
  → 未改：键缺失属主题字典重构事故面，改动收益低 —— 记录为观察。
- `Views/OsdWindow.ShowOsd` `Application.Current.Dispatcher` 未判空。→ 并入 BUG-21 修复。

### ⚪ R15 存疑/观察项（记录不修，附确认方法）

| 项 | 位置 | 说明 / 确认方法 |
|---|---|---|
| V0 机 EC 0x30 映射三元两臂相同 | `OmenHardware.cs:426` | 注释自述"Unleash 在 V0 无对应档,回退 Default"——**有意为之**非笔误；0x00 是否 V0 合法值需 V0 机型 EC 表确认 |
| `IsPowerControlForDeviceSupported` Gamora10 分支恒 false | `OmenHardware.cs:1120` | 全工程无调用方（死代码）；接线时须先核对原意 |
| fanClean `+128` 语义 | `OmenHardware.cs:271` | 需 hp-wmi.c/OSH 对 EC 0x2E bit7 定义确认（能力位 vs 会话位）；当前仅清灰按钮传 fanClean=true，UI 路径无正常调速混入 |
| FloatingWindow 静态状态跨线程 | `Views/FloatingWindow.xaml.cs:17-23` | TrayService 定时器线程读 `_instances.Count` 无同步；List.Count 撕裂读实际影响极小 |
| PipelineEditor `_getStepValue` 静态 | `Views/PipelineEditorWindow.xaml.cs:39` | 编辑器 ShowDialog 模态串行，当前无并发入口 |
| AutomationPage/CoreKeepPage 反序列化集合可空 | `Pages/AutomationPage.xaml.cs:135` 等 | 仅外部手改 JSON `"Steps":null` 触发；后端已有守卫，UI 侧未改 |
| PerfPage 显示缓存静态字段无锁 | `Pages/PerfPage.xaml.cs:942` | 追踪到的调用点均经 UI 线程 |
| `GlobalMemoryStatusEx` 返回值未检查 | `Pages/NativeMethods.cs:253` | 失败时显示 0 GB（误导不崩溃）；API 失败极罕见 |
| SettingsPage 三处 async void await 后续体 | `Pages/SettingsPage.xaml.cs:388/411/430` | 服务内部已 catch，UI 异常被 Dispatcher 吞不崩进程 |

### R15 修复汇总

| BUG | 文件 | 改动 |
|---|---|---|
| BUG-23 | `App/GpuAppManager.cs` | 4 方法库边界 try/catch + Initialize 入 try + 空数组防护 |
| BUG-20 | `Views/SystemOptimizeWindow.xaml.cs` | 3 处守卫复位推迟到 Loaded 优先级 + 服务 combo 值未变早退 |
| BUG-21 | `Views/OsdWindow.xaml.cs` | Dismiss 快照遍历 + ShowOsd 判空 |
| BUG-22 | `Views/SystemOptimizeWindow.xaml.cs` + `Views/ProcessSelectDialog.xaml.cs` | 7 处 worker 体兜底 + 异常路径按钮复位 |
| BUG-24 | `Pages/PerfPage.xaml.cs` | 2 处 AMD CO worker 兜底 + Invoke 防卸载 |
| BUG-25 | `Pages/FanPage.xaml.cs` | 9 处 timer `?.` 统一 + legacy 除尘 try/finally |
| BUG-26 | `App/OmenLighting.cs` + `Pages/LightingPage.xaml.cs` | ConcurrentDictionary + 静态锁/快照 |
| BUG-27 | `OmenHardware.cs` + `Services/HardwareApiService.cs` | ClampFanByte + IccMax/LoadLine 库钳制 + API 门域修正 |
| BUG-28 | `App.xaml.cs` + `App/Logger.cs` | 崩溃处理器 marshal+落盘 + Logger 自保 + UnobservedTaskException |
| BUG-29 | `Pages/CoreKeepPage.xaml.cs` + `Pages/LightingPage.xaml.cs` | `?? 2` 可空守卫 ×2 + SelectedIndex<0 早退 |
| BUG-30 | `Views/ProcessSelectDialog.xaml.cs` + `Views/StorageCleanWindow.xaml.cs` + `Views/PipelineEditorWindow.xaml.cs` + `Pages/NetworkBoostPage.xaml.cs` + `Pages/MacroPage.xaml.cs` | OkBtn 恢复 / Rescan finally / OnClosed 清会话 / 命名 LostFocus 幂等订退 / _watcherPage 置空 |
| BUG-31 | `OmenHardware.cs` + `Models/LightingScene.cs` + `App.xaml.cs` | null 早退 / Clone 补拷 / ShowExisting using+catch / 版本 TryParse |

### ✅✅ R15 验证体系升级（Windows 真机，取代 Linux Roslyn 模拟）

- **全量编译**：`dotnet build OmenSuperHub.csproj -c Debug`（SDK 8.0.420，net481 WPF）
  → **0 错误 0 警告**（修复前基线同样 0/0，证明 R15 改动零编译回归）。
- **运行时回归**：`OmenXHub.exe --selftest` → **ExitCode=0**，13 项断言全 PASS
  （含 LightingPage 布局守卫、FrontendRelease 三项、MacroController/MacroService 等）。
- **逐处复核**：主代理对全部 12 组修复的触发点直接读源码验证（含 XAML 绑定声明、
  调用方 grep 交叉验证），非仅凭子代理报告。

