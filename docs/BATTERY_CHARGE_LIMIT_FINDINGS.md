# 电池充电限制 —— myHP 逆向发现 + 可行性结论

> 日期：2026-08-26
> 目标：`AD2F1837.myHP`（HP.HPX.dll, CoreRT AOT）充电限制功能
> 机器：OMEN Gaming Laptop 16-am0xxx, BIOS F.12 (AMI)
> 结论：**本机 BIOS 无直接 WMI 充电限制命令；但有 3 条可行路径，见 §5**

## 1. myHP 的充电限制架构（已确认）

```
myHP UI (React Native, Bundle/index.windows.bundle)
  └─ PowerManagerClient (WinRT, HP.AppFramework.PowerManagerClient.winmd)
       ├─ BatteryParticulars { get/put_BatteryThresholdValue }
       ├─ getBatteryChargeControlMode / setBatteryChargeControlMode
       ├─ getBatteryHealth / getBatteryHealthStatusDetails
       ├─ setIntelligentChargeScheulerOn/Off
       └─ setLetHPManageMyBatterySelection
            └─ 实现在 HP.HPX.dll (198MB, CoreRT/.NET Native AOT, 34 万函数)
                 └─ 底层 → hpqBIntM WMI (SECU + hpqBDataIn + hpqBIOSInt{size})
```

- **充电模式枚举**（JS bundle 确认）：
  - `BatteryChargeControlMode_Let_HP_Manage_My_Battery`
  - `BatteryChargeControlMode_Maximize_My_Battery_Health`
  - `BatteryChargeControlMode_Minimize_My_Battery_Health`
- **注册表开关**（feature_filters_v2.2.json 确认）：
  - `HKLM\SOFTWARE\HP\HP App\SysControl\BatteryExtenderMode\Enabled`（BEM）
  - `HKLM\SOFTWARE\HP\HP App\Battery\Enabled`
  - `HKLM\SOFTWARE\HP\HP App\BatteryDisabled\pathExists`
  - `HKLM\SOFTWARE\HP\HP App\BatteryManager\ScheduleBatteryCharge\Enabled`（计划充电）
- **服务侧**：`HPAppHelperCap`（C:\Windows\System32\DriverStore\FileRepository\hpcustomcapcomp...\AppHelperCap.exe）含 `BatteryService.dll`（C++ WinRT，`IdleChargeThreshold`/`CheckChargeStatusTestCommand`）

## 2. 本机探测结果（关键实验，已运行）

用 `hpqBIntM` WMI 通道（提权后）扫描命令：

| 命令 | 结果 |
|---|---|
| 0x20008 / cmdType 0x28 | ✅ 有效（SystemDesignData） |
| 0x20008 / cmdType 0x10/0x2C | ✅ 有效（返回 0x02/0x21） |
| 0x20009 / cmdType 0x04 | ✅ 有效（返回 0x64=100，键盘亮度） |
| **0x2000C - 0x20020 全部 cmdType** | ❌ rc=0x3（**无效命令**） |

**结论**：本机 BIOS 只实现 0x20008（设计数据/系统信息）+ 0x20009（键盘/灯光）命令——**没有直接的电池充电限制 WMI 命令**。这就是"之前每次都不成功"的直接原因之一（另一原因见下）。

## 3. 之前失败的两个根本原因

1. **权限**：`hpqBIntM` WMI 的电池命令需要管理员/SYSTEM——普通用户调用直接"拒绝访问"（已实测验证）。之前可能用普通权限跑。
2. **命令号不对**：0x2000C/0x2000D 等猜测命令在本机 BIOS 返回无效（rc=3）。HP 各机型充电限制命令号不同，不能硬编码猜测。

## 4. 未完成：HP.HPX.dll 的真实命令号

- HP.HPX.dll 是 **CoreRT AOT**（198MB，34 万函数），ILSpy 无法反编译，IDA 全量分析极慢（>20 分钟未完成）
- myHP 内部符号（从 AOT 字符串提取）：`GetBatterySwitchThreshold`/`SetBatterySwitchThreshold`/`GetBatteryChargeInfo`/`SetBatteryChargeInfo`/`GetChargeControlStatus`/`SetBatteryChargeState`/`GetHPPMSystemControlSupport`/`SendBEMDisableDataAsync`/`.bemUserBatteryThreshold`——这些是 commandType 名，command 号需 AOT 反汇编确认

## 5. 三条可实现路径（按推荐排序）

### 路径 A：WinRT 激活 myHP 的 PowerManagerClient（最优雅，推荐先试）

myHP 已部署，`HP.AppFramework.PowerManagerClient.winmd` 的 `BatteryParticulars` 是 **activatable class**（inProcessServer → HP.HPX.dll）。可以：

```csharp
// C# 或 PowerShell 直接激活 myHP 的 WinRT 组件
// 1. 通过 Windows.ApplicationModel 包 API 找到已部署的 myHP
// 2. RoActivateInstance("HP.AppFramework.PowerManagerClient.BatteryParticulars")
// 3. 调用 put_BatteryThresholdValue / setBatteryChargeControlMode
```

**可行性**：WinRT 类激活后走 myHP 自己的完整实现（含命令号+权限），不依赖猜测。**风险**：需要包激活权限（同 UWP 包 ID 校验，类似 OMEN 的 SecurePipe V1）——需实测。

### 路径 B：通过 HPAppHelperCap 服务的 Bridge（推荐第二）

`BatteryService.dll` 是 AppHelperCap 的 Bridge 模块。服务已在运行（LocalSystem），写注册表键（`SysControl\BatteryExtenderMode\Enabled`）后，服务的 `CheckRegistryPathExists`/系统控制逻辑会接管。**可能只需写注册表即可触发**——这是 myHP 自己的 manifest 声明路径。

**验证方法**：提权写 `HKLM\SOFTWARE\HP\HP App\SysControl\BatteryExtenderMode\Enabled = 1`，观察电池行为/EC 变化。

### 路径 C：直接找 EC 寄存器（OMEN 特有，最后手段）

OMEN 16-am0xxx 是 EC 控制机型。充电限制可能走 EC 寄存器（PawnIO 可读写，项目已有）。**需要**：EC 地址表（从 BIOS/EC 固件提取或社区资料）。本机探测显示 WMI 无命令，EC 是最后可能。

## 6. 实测补充（本机验证）

已完成并记录：
- ✅ `hpqBIntM` WMI 提权后可用；基线命令正常
- ❌ 0x2000C-0x20020 全命令扫描：**本机 BIOS 无电池充电限制命令**（rc=0x3）
- ❌ WinRT 激活 `BatteryParticulars`：**0x80070057 E_INVALIDARG**——UWP 包外无法直接激活（需包身份）
- ✅ 提权写入 `HKLM\SOFTWARE\HP\HP App\SysControl\BatteryExtenderMode\Enabled=1` + `ScheduleBatteryCharge\Enabled=1` **成功**
- ✅ 重启 HPAppHelperCap 服务成功
- ❌ 写注册表+重启服务后**电池状态无变化**（仍 92% 充电中）——服务可能检测到本机不支持，或需完整 myHP 调用链

## 7. 最终结论

**充电限制在本机（OMEN 16-am0xxx BIOS F.12）能否实现？**

三条路径的实测结果：
| 路径 | 结果 | 判定 |
|---|---|---|
| A. WinRT 激活 | E_INVALIDARG，包外不可激活 | ❌ 需包身份 |
| B. 注册表+服务 | 写入成功但无效果 | ⚠️ 服务未应用，可能 BIOS 不支持 |
| C. WMI 直接命令 | 0x2000C-0x20020 全无效 | ❌ BIOS 无此命令 |

**最可能真相**：这台 OMEN 16-am0xxx 的 BIOS F.12 **不支持充电限制功能**（HP 的 Battery Health Manager/Adaptive Battery Optimizer 只在新机型/部分 OMEN 上开放）。WMI 无命令 + 服务不响应注册表，共同指向硬件/固件不支持。

**但仍有最后机会**：
1. **等 IDA 的 HP.HPX.dll 分析**（后台仍在跑，完成后 grep `SetBatteryChargeInfo` xref 拿真实命令号——可能是我扫描范围外的命令值）
2. **EC 直写**（PawnIO）：OMEN 的 EC 可能有充电限制寄存器（社区资料显示部分 OMEN 用 EC 0x87/0x98 区域）
3. **确认 myHP 在本机是否显示充电限制 UI**——如果 myHP 自己都不显示这个功能，则确定硬件不支持

## 8. 已产出的工具

- `tools/battprobe/` —— 电池 WMI 探针（net48，需提权运行，可扩展扫描任意 command/cmdType）
- 探针已验证：WMI 通道、权限门槛、本机命令支持面
