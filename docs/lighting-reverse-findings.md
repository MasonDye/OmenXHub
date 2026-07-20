# 灯光页逆向取证报告

日期：2026-07-18
来源：`C:\Program Files\WindowsApps\AD2F1837.OMENLightStudio_1.0.68.0_x64__v10z8vjag6ke6\LightStudio-ui`
工具：`ilspycmd 8.2.0.7535` + Python 字符串提取（PE 字符串表）
目的：为"完善本项目灯光页"提供 ground truth。

---

## 当前决定

**走方案 A（仅 UI 补齐）**：见第四节。理由详见 B'（已放弃 B）。

## TL;DR — 一个出乎意料的真相

**OMEN Light Studio 1.0.68 在 4 区键盘上根本不下发"动画 effectId/方向/主题/速度"字节给固件。**
动画完全由 Aurora CPU 端的 `EffectsEngine` 以 ~15 fps（`recordTimer = new Timer(66.67ms)`）逐帧渲染后，
对每一帧的 4 个 zone 各自调一次 `SetStatic`/`SetZoneColors` 下发纯颜色。

固件侧 **不存任何动画状态**，只接受"当前 4 个 zone 各是什么颜色 + 亮度"。

→ 因此原计划"从 Aurora.dll 反编译出 Dojo 动画 effectId 字节表作为 ground truth 修正本项目
`OmenLighting.cs`"，这条路径不存在。**Aurora 没有 Dojo 字节表。**

---

## 一、能拿到的强证据（与本项目静态字节布局对照）

### 1.1 Aurora 内的 `Omen.OmenFourZoneLighting.FourZoneLighting`

DLL：`LightStudio-ui/OmenFourZoneLighting.dll`（19976 字节）
**本项目自带的 `Resources/OmenFourZoneLighting.dll` 也是 19976 字节，散列一致——这就是同一个 SDK。**

反编译出来的关键常量（全字段）：

```csharp
LightingWmiCmd          = 131081  // 0x20009  与本项目 WMI_COMMAND_ID 一致
LightingWmiCmdGaming     = 131080  // 0x20008  键盘类型查询走这个
LightingCmdType_GetPlatformInfo = 1
LightingCmdType_GetZoneColors   = 2
LightingCmdType_SetZoneColors   = 3
LightingCmdType_Status          = 4
LightingCmdType_SetBrightness   = 5
LightingCmdType_SetLightBarColors = 11
LightingCmdType_GetKeyboardType   = 43
LightingZoneCount = 4
ColorSize = 3
ColorOffset = 25
Sign = { 83, 69, 67, 85 }   // "SECU"
BrightnessLevelOn = 100
```

WMI 调用通道：`root\wmi` → `hpqBIntM.InstanceName='ACPI\PNP0C14\0_0'` → 方法 `hpqBIOSInt128`，
`hpqBDataIn` 把 `Sign + Command + CommandType + Size + hpqBData` 打包进 `InData`，返回 `OutData.Data`。
**与本项目 `OmenHardware.SendOmenBiosWmi` 完全同源。**

### 1.2 静态 4 区下发字节布局（强证据，已 1:1 对照）

Aurora `FourZoneLighting.SetZoneColors(Color[] zoneColors)`：

```
Execute(131081, 2, 0, null) -> 读 128B 返回表
  array[25 + i*3]     = zoneColors[i].R
  array[25 + i*3 + 1] = zoneColors[i].G
  array[25 + i*3 + 2] = zoneColors[i].B
Execute(131081, 3, array.Length, array) -> 写回
```

本项目 `OmenLighting.SetZoneStaticColor` 的 `BasicFourZone` 分支（`App/OmenLighting.cs` 行 244-253）：

```
SendOmenBiosWmi(2, [0], 128, 131081) -> 读 128B 表
  table[25 + i*3..+2] = R,G,B
SendOmenBiosWmi(3, table, 0, 131081) -> 写回
```

**结论：4 区静态色 WMI 字节布局，Aurora 与本项目一字节不差。**

### 1.3 灯条静态下发字节布局（强证据，已 1:1 对照）

Aurora `FourZoneLighting.SetLightBarColors(Color[] zoneColors, int type=0)`：

```
array = new byte[128]
array[0] = type                // 0=默认目标
array[1] = 0
array[3] = 0xFF                // brightness 满档
array[6] = 4                   // color count
array[7 + i*3] = R
array[8 + i*3] = G
array[9 + i*3] = B
Execute(131081, 11, array.Length, array)
```

本项目 `OmenLighting.SetZoneStaticColor` 的 `Dojo` 分支（行 230-241）：相同 128B 缓冲、
`[0]=target`、`[3]=brightness`、`[6]=4`、`[7+i*3]=RGB`、CmdType=11。

**结论：灯条静态字节布局，Aurora 与本项目一字节不差。**
（本项目此处 `data[3]=brightness` 可由调用方控制，Aurora 写死 0xFF——逻辑等价）

---

## 二、拿不到 / 与方案预期不符的部分

### 2.1 "Dojo 9 个动画 effectId + 主题/方向/速度位字段"在 Aurora 中找不到

- `Aurora.dll` 全类扫描：无 `Dojo`、无 `*FourZoneAnimation*`、无 `SetMultiColor*`、无 `SetPresetColor*`、
  无 `effectId` 常量类、无 `LightingTheme*` / `LightingDirection*` / `ColorCycle` / `Starlight` / `Raindrop` /
  `Confetti` / `AudioPulse` / `Swipe` 等名称。
- `Aurora.EffectsEngine.Animations.*`（`AnimationEllipse` / `AnimationFill` / `AnimationFrame` /
  `AnimationMix` / `AnimationTrack` ...）是 **CPU 端逐帧渲染原语**，渲染完后只送 DeviceKeys→Color 给设备。
- `Omen.Devices.Omen.OmenKeyboard.SetLights` 中只 P/Invoke 了
  `OmenLighting_Keyboard_SetStatic`，从不调 `SetSingleColorAnimation` / `SetPresetColorAnimation` /
  `SetMultiColorAnimation`——尽管 `OmenLightingSDK.dll` 导出了它们。
- `Omen.Devices.Omen.OmenFourZoneLighting.SetLights` 中只 `SetColorData(Color[4], DeviceType)` 写共享内存，
  helper 进程随后用 `OmenFourZoneLighting.dll` 的 `SetZoneColors`（静态）/`SetLightBarColors`（静态）下发。
  **没有任何动画相关方法。**

### 2.2 `OmenLightingSDK.dll` 是 native C++，ILSpy 不能反编译出 C 源码

我提取了它的导出函数表（PE 字符串表）：

| 设备组 | 关键 Set 方法 |
|---|---|
| Keyboard | SetStatic, SetSingleColorAnimation, SetPresetColorAnimation, SetMultiColorAnimation, GetAvailableKeys, GetKeyByChar, GetKeyboardLanguage |
| Mouse | SetStatic, SetSingleColorAnimation, SetPresetColorAnimation, SetMultiColorAnimation |
| MousePad | SetStatic, SetSingleColorAnimation, SetPresetColorAnimation, SetMultiColorAnimation, GetZoneCount |
| Chassis | SetStatic, SetSingleColorAnimation, SetPresetColorAnimation, SetMultiColorAnimation |
| Headset | SetStatic, SetSingleColorAnimation, SetPresetColorAnimation, SetMultiColorAnimation |
| Speaker | SetStatic, SetSingleColorAnimation, SetPresetColorAnimation, SetMultiColorAnimation |
| Display | SetStatic (only), GetAvailableLeds |
| Argb / ArgbG2 | SetStatic (only) |

→ 各 Keyboard 机型（Modena / Ralph / Cybug / Hendricks / DojoVibrance / Voco / Starmade / QuakerBrunobear）
有独立的 C++ 类，每个通过 HID 报告字节与硬件通信。
**这些 HID 字节序列需要 IDA Pro / Ghidra 反汇编 native 才能恢复，ILSpy 做不到。**

→ 即便拿到，也是 HID 字节，不是本项目当前用的 WMI 字节通道；通道不同字节也不同。

### 2.3 本项目 `SetZoneAnimation` Dojo 分支字节的真正出处

本项目 `App/OmenLighting.cs` 行 266-290 的 Dojo 动画字节：

```
128B data:
  data[0] = target          // 0=LightBar, 1=FourZoneAni
  data[1] = effectId        // 1..9
  data[2] bitfield:
    [1:0] speed  (0..3)
    [3:2] direction (1 -> 0x08, else -> 0x04)
    [7:4] theme   (0->0x10, 1->0x20, 2->0x30, 3->0x40, 4->0x50)
  data[3] = brightness
  data[6] = colorCount      // theme==4 时
  data[7 + i*3..+2] = R,G,B // 最多 4 组
WMI: SendOmenBiosWmi(11, data, 0, 131081)
```

这套字节 **不在 Aurora 工程里**，没有官方代码可以验证。
可能的来源（猜测，无从证实）：
1. HP 早期 Omen Gaming Hub / OmenCommandCenter 的 WMI 协议逆向上代产物。
2. 本项目作者从机器 WMI 响应或手册中拼出来的一份完整猜测。
3. 部分 Bit 也许是正确的（CmdType=11 确实是 `SetLightBarColors`，且[data[0]=target] 与静态布局可衔接），但 effectId/speed/direction/theme 的位分配是**逆向推断值**，不可信。

### 2.4 BasicFourZone 动画字节支持的"2 个 effect"

本项目 `SetZoneAnimation` 的 BasicFourZone 分支（行 292-319）：
- `effectId==2 (Starlight)` → `draxEffect=2`
- `effectId==4 (Wave)`     → `draxEffect=1`
- 其他返回

```
data[0] = 0
data[1] = draxEffect (1 或 2)
data[2] = interval  (speed: 0->10, 1->5, 2->2)
data[3] = brightness
data[4] = colorCount
data[5 + i*3..+2] = R,G,B
WMI: SendOmenBiosWmi(7, ...)
```

Aurora `OmenFourZoneLighting.dll` **没有 CmdType=7、没有 byte[5+i*3] 数据结构**。
CmdType 7 在 Aurora 不存在；本项目这份也是另一个无法交叉验证的逆向推断值。

---

## 三、与本项目 UI 现状的社会化贴合度

| 本项目 UI 现状 | Aurora 真相 | 评估 |
|---|---|---|
| 4 区静态色 | 1:1 字节一致 | ✅ 可信 |
| 灯条静态色 | 1:1 字节一致 | ✅ 可信 |
| 4 区动画 effect 1..9（Dojo） | Aurora 不下发动画字节 | ❓ 无 ground truth，疑似 |
| BasicFourZone 动画 Starlight/Wave | Aurora 无 CmdType=7 | ❓ 无 ground truth |
| Brightness WMI(CmdType=5) | Aurora 一致 `SetBrightness(byte level)` | ✅ 可信 |
| Direction / Theme ComboBox | Aurora 不存在这个概念 | ❓ 仅 Dojo 字节才有意义 |
| Per-key RGB | Aurora 也只 `SetStatic` per-key，逐帧渲染 | ✅ 与本项目后端语义一致 |
| Per-key Animation 调用 SetPerKeyAnimation | Aurora 不调 Set*Animation，逐帧 SetStatic | ⚠️ Aurora 路线不同 |
| 多设备灯光（鼠标/耳机/外壳） | Aurora 支持但同样逐帧 SetStatic | ✅ 通道存在 HID 路线 |

---

## 四、可供后续选择的方向

**A. 仅做 UI 补齐**（推荐，最有据可循）：
- 复用项目已定义但无控件的字符串 `LightingDirLeft`/`LightingDirRight`/`LightingTheme*`，
  在 LightingPage 上加 Direction/Theme 下拉框。
- Dojo 协议下开放 Direction/Theme/Speed 4 档（位字段已存在），其他协议禁用。
- Apply 时做能力检测：BasicFourZone 选了 ColorCycle/Raindrop/AudioPulse/Confetti/Sun/Swipe 时弹提示
  "此协议不支持，请改用 Dojo" 而不是静默丢弃。
- 4 区静态与灯条静态是 1:1 校准过的，作为"正确"路径。

**B. 同时反汇编 `OmenLightingSDK.dll` 提取 HID 字节**：
- 工具升级到 IDA Pro/Ghidra，反汇编各机型 Keyboard class。
- 这套是 HID 字节、独立通道。要在本项目新增 HID 通道作为 WMI 之外的备选路径。
- 工作量上一个数量级，且 Aurora 自己根本不用 `SetMultiColorAnimation`——价值存疑。

**B'（实际探索结果，已放弃）**：用 `pefile + capstone` 反汇编了 `OmenLightingSDK.dll`
中 `OmenLighting_Keyboard_SetMultiColorAnimation` / `SetPresetColorAnimation` /
`SetSingleColorAnimation` 三个导出函数（pcodepublisher）。

证据（节选）：

```
SetSingleColorAnimation @ rva=0x443d0:
  ...
  0x180044436:  movzx eax, word ptr [r8]       ; effectType (word)
  0x18004443a:  mov   word ptr [rsp + 0x20], ax
  0x18004443f:  movzx eax, byte ptr [r8 + 2]   ; speed byte
  0x180044444:  lea   r8, [rsp + 0x20]
  0x180044449:  mov   byte ptr [rsp + 0x22], al
  0x18004444d:  mov   rax, qword ptr [rcx]     ; vtable
  0x180044450:  call  qword ptr [rax + 0x40]   ; <- setSingleColorAnimation 虚函数

SetPresetColorAnimation @ rva=0x43610:
  ...
  0x180043676:  mov rax, qword ptr [rcx]       ; vtable
  0x180043679:  call qword ptr [rax + 0x48]    ; <- setPresetColorAnimation 虚函数

SetMultiColorAnimation @ rva=0x44470:
  ...
  0x18004464b:  mov r10, qword ptr [r12]      ; this->vtable
  0x18004464f:  lea  r8,  [rsp + 0x28]        ; 颜色 count + buffer 包
  0x180044654:  mov  edx, dword ptr [rsp + 0x20] ; effectId
  0x180044658:  mov  r9,  r14                 ; LightingEffectProperty*
  0x18004465e:  call qword ptr [r10 + 0x38]   ; <- setMultiColorAnimation 虚函数
```

= 三个导出全是 **thin trampoline + 虚表分发**。真正 HID 字节构造落在每种机型的
`<Model>Keyboard::setMultiColorAnimation` / `setSingleColorAnimation` / `setPresetColorAnimation`
虚函数实现里；MSVC RTTI 类名扫描在 `.data` 段确认存在：
`ModenaKeyboard`、`VocoKeyboard`、`VocoKeyboard25C1`、`DojoVibranceKeyboard`、
`DojoVibranceKeyboard26C1`、`StarmadeKeyboard`、`CybugKeyboard`、`HendricksKeyboard`、
`RalphKeyboard`、`QuakerBrunobearKeyboard`、`QuakerBrunobearKeyboard24C1`、`WoodStockKeyboard`
共 **12 种机型 Keyboard 类**，每个都有三个独立虚函数实现。

→ 决定放弃 B。理由：
1. **不可本机验证**：本机没有 OMEN 4 区/逐键 RGB 键盘硬件，反汇编出的 HID 字节表
   无法用真机回灌核对，错的字节表危害大于无字节表。
2. **工作量大**：12 机型 × 3 虚函数 = 36 个 HID 字节构造算法。每个要追 vtable[0x38/0x40/0x48]
   →扫描 `CompleteObjectLocator`→type descriptor→回到 this 对象布局→再反汇编虚函数体识别
   `HidD_SetFeature`/`WriteFile` 包前 6+ 帧 reportId+byte0 标头。单机型 <1 小时可证伪，
   全套 12 机工作量数小时且仍无验真。
3. **Aurora 已不会用这套路径**：Aurora 工程实际只调 `OmenLighting_Keyboard_SetStatic`，
   不调任何 `*ColorAnimation*`——native 多色动画导出只供 HyperX Alloy Origins / 早期 Gaming Hub
   旧路径，本项目走它反而背离参考实现。
4. **本项目现有 WMI Dojo 字节虽无 ground truth，但 BasicFourZone 静态 + 灯条静态已被
   Aurora 1:1 校准"正确"**，逼近 Aurora 行为的最低风险路径是 **方案 A**：在 Aurora 不下发动画
   的事实下保持现状，把 UI 缺失补齐、无效下发改成显式失败提示即可。

→ 这也就是为什么本次 session 决定走方案 A 而非 B（见下方"决定"块）。

**C. 重新设计为"Aurora 路线"逐帧渲染**：
- 放弃固件动画 effectId 概念，CPU 端用 WPF `DispatcherTimer`/CompositionControl 逐帧把 effectId 渲染成
  4 个 zone 的当下颜色，然后下发 `SetZoneStaticColor`。
- 优势：路径与 Aurora 100% 同源，必能在真机上跑通。
- 劣势：放弃固件动画（意味着清空动画时 CPU 持续占用 15fps 调度，休眠/熄屏等场景需要清场）。
- 这是"贴近 Light Studio 哲学"的根本路线，但改变架构。

---

## 五、保留与清理

- 临时反编译产物在 `.zcode/tmp-omendecomp/`，不进 repo，使用后清理。
- 本报告 `docs/lighting-reverse-findings.md` 是后续工作的 ground-truth 参考，保留。
- 反编译所需的 ilspycmd 是 dotnet 全局工具，已装到 `C:\Users\PC\.dotnet\tools\`，
  后续如需重新提取可用 `ilspycmd -t "<类名>" "<DLL路径>"`。

## 引用

- `Aurora.dll` 反编译产物（仅取证，不长期保留）：
  - `Aurora.Devices.Omen.OmenFourZoneLighting` / `OmenKeyboard` / `OmenDevices` / `Effects`
- `OmenFourZoneLighting.dll` 反编译产物（取证）：
  - `Omen.OmenFourZoneLighting.FourZoneLighting` / `KeyboardType` 枚举 / `LightBarCmdByte` 枚举
- `OmenLightingSDK.dll` 字符串表（取证）：56 个 `OmenLighting_*` 导出函数 + 各机型 C++ RTTI 名
