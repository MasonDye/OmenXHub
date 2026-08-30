# Intel XTU / HSA 超频通道逆向结论

> 日期: 2026-08-22 · 耗时一整个调试会话 · 结论:客户端侧已完全打通,服务器侧模块在本机不可用

## 已确证的架构(全部实测)

```
OmenXHub / OGH UI
  └─ IntelXTUSDK.dll (托管, Hp.Bridge.Client.SDKs.IntelXTUSDK)
      └─ RpcClient.dll → NativeRpcClient.dll (rpcrt4 MSRPC)
          └─ ncalrpc:[HPOmenRpcEndpoint]           ← ALPC 端点,非命名管道!
              └─ 宿主: HPOmenCap 服务 (HP Omen HSA, OmenCap.exe, LocalSystem, session 0)
                  └─ IntelXTUOverclockingService.dll (原生, DriverStore\hpomencustomcapcomp...\OmenCap\)
                      └─ [本机断裂点] → Intel XtuService.exe (XTU3SERVICE, SysWOW64)
```

关键事实:
- **没有 `\\.\pipe\XTU3SERVICE` 管道** — XtuService.exe 不含任何 pipe 字符串。
  `NamedPipeClientStream.Connect` 对不存在的管道抛"操作已超时"(.NET 陷阱),原实现因此永远超时。
- OGH 端点字符串与协议取自反编译(可信): `GetClientForEndpoint("HPOmenRpcEndpoint")`,
  serviceProvider `IntelXTUOverclockingService`,消息=函数名字符串(如 `"XTUGetServiceVersion()"`),
  命令号见 IntelXTUSDK Enums(注意: docs/MEMORY_INTEL_XTU.md 的命令号表部分有误,
  实际 GetServiceVersion=258,SmallCoreCount=275)。
- **首次 OpenConnection 返回 5(拒绝访问),同进程立即重试即 0** — SDK 无内建重试,
  XtuService.InitializeAsync 已加重试(实测多次稳定复现)。
- `WinNTServiceHelper.StartService` 走 `HPSysInfoRpcEndpoint`,对本进程 rc=5 — 不要用。
  XTU3SERVICE 本就随系统运行,无需启动。

## 本机断裂点(穷举验证)

连接成功后(OpenConnection=0),**所有命令**(769 init/770/516 启动 monitor/513/258/277/259 …)
统一返回 `Status=0x2000000C "get available monitor error"`:
- OGH 开着/完全退出,结果相同(排除 monitor 独占)
- 提权/非提权,结果相同
- SDK 包装与裸 RpcClient 直发,结果相同
→ **OmenCap 的 IntelXTUOverclockingService 模块在本机无法建立 XTU 会话,此路不通。**

## OGH UI 实际走的路(未完成的方向)

OGH 的超频 UI(`HP.Omen.OMENOverclockingModule.dll`)不依赖 HSA 桥,走自有管道:
- UI 建 pipe server `HP.Omen.OMENOverclocking.ViewModel{sessionId}`
- UI 连 `PerformanceControlFg{sessionId}` / `PerformanceControlToAdvancedPerformanceTuningPipeStr{sessionId}`
- 消息: `PerseusRevMsg{ FuncType, SendParameter: PerformanceControlMsg{ Command, Data } }`
  (Command 35=初始化握手;倍频/电压的具体命令号在更深的 VM 基类,未挖完)
- 后台链: OmenCommandCenterBackground.exe → 最终执行在 HP 私有 MSR 槽位协议上
  (`HPIntelOCHelperMSR`: HPCPUMSRCommand 槽 1000+i=P核倍频、2000+i=E核,经 HP 驱动,
  **非裸 MSR 地址**;电压偏移编码 = `v<0 ? v+2000 : v` 再 `×1.024+0.5` 取整,11bit 字段)

## 真实控制 ID 表(反编译自 HP.Omen.Core.Common CONTROL_ID,已用于 XtuService)

P核倍频: 29,30,31,32,42,43,96,97,107,108,218..225 (非连续!)
CPU 电压偏移: 34 · CPU 缓存电压偏移: 79 · E核倍频: 4500+i · PL1: 48

## 下一步的三个选项

1. **继续挖 OGH 管道协议**(方向 A 延伸):命令号枚举 + 序列化格式 + 后台处理器,
   复用活着的执行器(自带平台限值保护),代价是依赖 OGH 后台常驻。
2. **裸 MSR 直写**(自包含):经 LHM Ring0/WinRing0,倍频 MSR 0x3E8 族 + OC Mailbox 0x150
   (电压编码已破译)。风险:无平台限值保护,写入位序需真机小心验证 — 电压写错有硬件风险。
3. **接受现状**:HSA 路径的完整客户端已就绪(`Services/XtuService.cs` + Resources/ 四个 DLL),
   在 HSA 桥可用的机型上即插即用;本机卡片如实显示"无法连接"。
