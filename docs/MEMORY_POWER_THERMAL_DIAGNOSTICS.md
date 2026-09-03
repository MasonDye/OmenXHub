# 功耗钳制 / 温度墙 / 绑定陷阱 工作区记忆

> 沉淀于 2026-08-29 会话。机型：OMEN Gaming Laptop 16-am0xxx（8D3F，i7-14650HX 8P+8E，RTX 5060 Laptop，BIOS F.12，sku=RPLHXR_N22X2X4X6 / devType=Hanna）。
> OSH 参考工程：`E:\Desktop\OmenSuperHub`（能启动、SDK 读值成功 = 已验证形态，可对照）。

## 0. 环境与部署坑

- OmenXHub.exe **提权运行**：非提权 shell `taskkill` 被拒、bin 被锁。验证构建用
  `dotnet build -p:OutDir=<临时目录> -p:IntermediateOutputPath=<临时目录>/obj/`；正式部署必须用户手动关应用。
- Mutex `"MyUniqueAppMutex"` 与 OmenSuperHub.exe **共享**——两者同跑后启动者静默退出（“启动后消失”）。
- ZCode computer-use 对提权应用被 **UIPI 拦截**（UIA 不可用），无法自动点击验证 UI。
- 事件日志查崩溃：`Get-WinEvent -LogName Application` 找 APPCRASH；`c00000fd` = 栈溢出。

## 1. CPU 被锁 30-40W 的诊断结论与方法

**结论**：OXH/OSH 的 CPU 功率走 HP BIOS WMI `0x29`，载荷 `[PL2, PL1, PL4, TPP]`（`OmenHardware.cs` `SetCpuPowerLimit*`；WMI 成功 ≠ 实际生效，无读回）。但“锁 30-40W 改不动”的真凶是 **Windows 电源模式（EPP/HWP 频率请求层）**：最佳能效把 HX 持续功率压在 30-45W，只有最佳性能解锁（实测切换即回 100W+）。PL 写入在另一层，拦不住。

- “有时候”的来源：内置预设出厂绑定电源模式——`GpuPriority`/`LightUse` 绑 `PowerMode=0`（最佳能效），见 `Services/PresetManager.cs` `GetBuiltInDefaults`。切过这两个预设后就锁。
- 平衡档同样不解锁全部预算（OS/DTT 策略折中）；要满血用最佳性能，温度交给风扇预设管（两者独立绑定）。

**诊断顺序（HWiNFO）**：
1. PL1/PL2 静态+动态 → 限制寄存器是否被人写低；
2. IA/GT/Ring Limit Reasons 及子项 → 哪个域、哪种原因（Power/Current/Thermal/PROCHOT）；
3. 核心有效频率 → 频率请求层（钉低频）vs 硬件钳制；
4. PROCHOT / BD PROCHOT / EDP Other → EC/VR 硬件保护。

**红鲱鱼**：
- Ring 域 “Max VR Voltage, ICCmax, PL4” 瞬时 1% 是正常电气尖峰噪声，不是持续钳制；
- IR/环境/PCH/VR 冷机全同值正常（EC `0x23` index 0-3 传感器组，`OmenHardware.GetSensorTemperature`）；
- “CPU 温度”（Package 二极管，含 uncore、响应慢）比“核心最高”高几度正常。

**本案证据链**：PL1/PL2=130W 全开 + IA 全否 + RING:PROCHOT 全否 → 排除硬件钳制 → OS 频率请求层 → 电源模式切最佳性能验证结案。

## 2. HWiNFO vs LibreHardwareMonitor

- HWiNFO = 闭源内核驱动 + 逐机型怪癖表（limit reasons、PL1-4、ring、PROCHOT 等数百域）；LHM 开源只实现标准 MSR 精选子集；本项目用 **PawnIO 改装版**（`LibreHardwareMonitor-pawnio-squashed`），能覆盖面再窄一层。诊断用 HWiNFO、常驻监控用 LHM 是合理分工。
- LHM 的 Distance to TjMax 是**每核一条** `"Core #N Distance to TjMax"`（`LibreHardwareMonitorLib/Hardware/Cpu/IntelCpu.cs:395`）。精确名匹配永远 miss → 已改 `EndsWith` + 取最小（距离=TjMax−温度，最小=最热核，与“核心最高”同语义；每 tick 首见覆写防粘滞）。

## 3. CPU 温度墙 / HP SDK 依赖链 / Costura 栈溢出（最重要的坑）

- 温度墙真值 = HP SDK `PerformanceControl.GetPlatformSettings(devType, sku).temperatureThrottlingPerformance`（本机 **98**）；回退 100 = 硬件 TjMax。
- 流程 bug（已修）：`RefreshSysInfo` 缓存命中早退 → `GetCpuTjmax()` 永不执行（日志 0 次调用为证）→ 每进程首次进 Dashboard 强制后台完整刷新一次（`_sysInfoFreshenedOnce`）。
- SDK 依赖：PerformanceControl 按强名请求 `System.Threading.Tasks.Extensions` **4.1.1.0**——该版本无公开 NuGet 包（4.5.1→4.2.0.0，4.5.4→4.2.0.1，4.6.3→4.2.4.0）。
- **栈溢出根因**：Costura 嵌入 = `Assembly.Load(bytes)` = 匿名加载上下文，**app.config 绑定重定向不生效** → 4.1.1.0 请求撞 Costura 跨版本喂入 → 解析重入栈溢出（`c00000fd`，三种嵌入方式复现、CLR 偏移一致）。OSH 不崩 = HP 链落盘加载（默认上下文，重定向生效）。
- **最优解（已落地）**：`FodyWeavers.xml` `<Costura ExcludeAssemblies="PerformanceControl|HP.Omen.Core.*|...|System.Threading.Tasks.Extensions" />` 排除嵌入、松散落盘；`app.config` 重定向 `4.1.1.0→4.2.4.0`（loose 4.2.4.0 取自 OSH `packages\System.Threading.Tasks.Extensions.4.6.3\lib\net462`）。
- **6.x 配置陷阱**：`ExcludeAssemblies` 属性形式用 `|` 分隔、元素形式用换行分隔（见构建生成的 `FodyWeavers.xsd` 注解）；写错**静默忽略**。
- `Logging.dll` **必须保持嵌入**：Intel XTU SDK（字节加载）依赖 Costura 按名不校验版本的供给；落盘后它的旧版本请求会失败。
- **勿再尝试任何形式嵌入 Tasks.Extensions**——复现条件 = exe 内存在 `costura.system.threading.tasks.extensions.dll.compressed` 资源。

## 4. NVIDIA 温度墙

- 读取 `nvidia-smi -q -d TEMPERATURE`；新驱动（RTX 50 系）字段改名 `GPU Target Temperature Specification`，正则需可选后缀（`App/GpuAppManager.cs` `GetGpuTemperatureTarget`，已修）。**OSH 同款旧正则，新驱动机器同样读不出**。

## 5. 额外温度行语义

- 行可见 = 用户勾选 `IsExtraEnabled` **且** `HasExtraTemp(id)`（`Services/HardwareService.cs`：`_extraRaw` 仅有效读到时写入，`ContainsKey` = 本机真有该传感器）。
- 临时丢读显 “-” 不隐藏（防抖）；从未读到 = 机型无此传感器 → 隐藏（笔记本 SuperIO 主板温度；台式机读到自动出现）。

## 6. Wpf.Ui 3.1.1 坑（本轮早些时候）

- `CardExpander` 的 `Icon=` 属性**不渲染** → 图标必须 Header 内联 `<ui:SymbolIcon>`。
- 非法 `SymbolRegular` 枚举名**编译通过、运行期页面加载抛 XamlParseException**（`SettingsAdjustments24`/`Memory24` 事故，核心保持页打不开）→ 新符号名必须先反射 Wpf.Ui.dll 验证再用。
