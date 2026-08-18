// AmdUndervoltService.cs - AMD Ryzen Curve Optimizer 全核降压
// 移植自 OMEN Core 的 AmdUndervoltProvider/RyzenControl/RyzenSmu 三件套,
// 合并为单文件。复用项目内 squashed PawnIo.cs(直连驱动,不依赖 PawnIOLib.dll)。
// ponytail: 用 PawnIo.Open() 只开设备句柄不加载 bin,避免与 LibreHardwareMonitor
// 内部 RyzenSmu 双重加载 RyzenSMU.bin 冲突。PCI 总线用全局命名 mutex 序列化。
using System;
using System.Management;
using System.Threading;
using LibreHardwareMonitor.PawnIo;

namespace OmenSuperHub.Services {
  // AMD Ryzen CPU 家族标识 (Zen1 ~ Strix Halo/Fire Range)
  public enum RyzenFamily {
    Unknown = -999,
    Zen1Plus = -1, Raven = 0, Picasso = 1, Dali = 2,
    RenoirLucienne = 3, Matisse = 4, VanGogh = 5, Vermeer = 6,
    CezanneBarcelo = 7, Rembrandt = 8, Phoenix = 9,
    RaphaelDragonRange = 10, Mendocino = 11, HawkPoint = 12,
    StrixPoint = 13, StrixHalo = 14, FireRange = 15
  }

  // SMU 命令返回状态 (G-Helper/UXTU 约定)
  public enum SmuStatus : int {
    Bad = 0x0, Ok = 0x1, Failed = 0xFF,
    UnknownCmd = 0xFE, CmdRejectedPrereq = 0xFD, CmdRejectedBusy = 0xFC
  }

  // ponytail: 单例 — PawnIo 句柄在进程生命周期内常驻,避免反复 CreateFile。
  // 不实现 IDisposable:进程退出时 OS 自动回收句柄,无需显式 Close。
  public class AmdUndervoltService {
    static AmdUndervoltService _instance;
    public static AmdUndervoltService Instance => _instance ??= new AmdUndervoltService();

    readonly PawnIo _pawnIo;
    readonly Mutex _pciBusMutex;
    const ushort SmuTimeout = 8192;

    // MP1/PSMU 邮箱地址 (按家族配置)
    uint _mp1Msg, _mp1Rsp, _mp1Arg, _psmuMsg, _psmuRsp, _psmuArg;

    // CPU 信息
    RyzenFamily _family = RyzenFamily.Unknown;
    string _cpuName = "";
    bool _initialized;

    public bool IsAvailable => _pawnIo != null && _pawnIo.IsLoaded && _initialized;
    public RyzenFamily Family => _family;
    public string CpuName => _cpuName;
    public bool SupportsUndervolt => _family != RyzenFamily.Unknown
      && _family != RyzenFamily.Zen1Plus
      && _family != RyzenFamily.Raven
      && _family != RyzenFamily.Picasso
      && _family != RyzenFamily.Dali;

    AmdUndervoltService() {
      try {
        // ponytail: 必须加载 RyzenSMU.bin 模块 — ioctl_*_smu_register 是该模块导出的函数,
        // 不是驱动原生函数。模块在驱动侧全局,重复加载是幂等的(驱动会拒绝同名校验和)。
        // 用 LHM 程序集里的同一份 bin 资源,确保版本与 LHM 内部 RyzenSmu 一致。
        _pawnIo = PawnIo.LoadModuleFromResource(
          typeof(LibreHardwareMonitor.PawnIo.RyzenSmu).Assembly,
          "LibreHardwareMonitor.Resources.PawnIO.RyzenSMU.bin");
        if (!_pawnIo.IsLoaded) return;
        // ponytail: Global\Access_PCI 是 LibreHardwareMonitor.MutExes 用的同一把全局锁,
        // 跨进程序列化 PCI 配置访问,避免与 LHM 的 PM 表读取冲突。
        _pciBusMutex = new Mutex(false, @"Global\Access_PCI");
        DetectCpu();
        ConfigureSmuAddresses();
        _initialized = SupportsUndervolt;
      } catch { /* _initialized 留 false */ }
    }

    // ── CPU 家族检测 (port from UXTU Family.setCpuFamily) ──
    // ponytail: UXTU 用 PROCESSOR_IDENTIFIER 环境变量解析数字 Family/Model,
    // 比依赖 WMI Caption 字符串匹配可靠得多 (Caption 格式因 Windows 版本/语言而异)。
    void DetectCpu() {
      try {
        // CPU 名称仍走 WMI (环境变量不含名称)
        using var searcher = new ManagementObjectSearcher("select * from Win32_Processor");
        foreach (ManagementObject obj in searcher.Get()) {
          using (obj) { _cpuName = obj["Name"]?.ToString() ?? ""; }
          break;
        }
      } catch { }
      _family = DetectFamily();
    }

    RyzenFamily DetectFamily() {
      // 解析 PROCESSOR_IDENTIFIER: "AuthenticAMD Family 25 Model 80 Stepping 0"
      int fam = 0, model = 0;
      try {
        string id = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "";
        var words = id.Split(' ');
        int fi = Array.IndexOf(words, "Family") + 1;
        int mi = Array.IndexOf(words, "Model") + 1;
        if (fi > 0 && mi > 0) {
          fam = int.Parse(words[fi]);
          model = int.Parse(words[mi].TrimEnd(','));
        }
      } catch { return RyzenFamily.Unknown; }

      // Zen1/Zen2 (Family 23 = 0x17)
      if (fam == 23) {
        switch (model) {
          case 1: case 8: return RyzenFamily.Zen1Plus;     // SummitRidge/PinnacleRidge
          case 17: case 18: return RyzenFamily.Raven;
          case 24: return RyzenFamily.Picasso;
          case 32: return RyzenFamily.Dali;
          case 80: case 96: return RyzenFamily.RenoirLucienne;
          case 104: return RyzenFamily.RenoirLucienne;      // Lucienne
          case 113: return RyzenFamily.Matisse;             // ponytail: Matisse 在 Family 23,不是 25!
          case 144: case 145: return RyzenFamily.VanGogh;
          case 160: return RyzenFamily.Mendocino;
        }
      }
      // Zen3/Zen4 (Family 25 = 0x19)
      if (fam == 25) {
        switch (model) {
          case 33: return RyzenFamily.Vermeer;
          case 63: case 68: return RyzenFamily.Rembrandt;
          case 80: return RyzenFamily.CezanneBarcelo;
          case 97: return RyzenFamily.RaphaelDragonRange;   // Raphael/DragonRange (HX 判断见下)
          case 116: case 120: return RyzenFamily.Phoenix;
          case 117: case 124: return RyzenFamily.HawkPoint;
        }
      }
      // Zen5/Zen6 (Family 26 = 0x1A)
      if (fam == 26) {
        switch (model) {
          case 68: return _cpuName.Contains("HX") ? RyzenFamily.FireRange : RyzenFamily.RaphaelDragonRange;
          case 32: case 36: return RyzenFamily.StrixPoint;
          case 112: return RyzenFamily.StrixHalo;
        }
      }
      return RyzenFamily.Unknown;
    }

    // ── SMU 地址表 (port from OMEN Core RyzenControl.ConfigureSmuAddresses) ──
    void ConfigureSmuAddresses() {
      switch (_family) {
        case RyzenFamily.Zen1Plus:
          _mp1Msg=0x3B10528; _mp1Rsp=0x3B10564; _mp1Arg=0x3B10598;
          _psmuMsg=0x3B1051C; _psmuRsp=0x3B10568; _psmuArg=0x3B10590;
          break;
        case RyzenFamily.Raven:
        case RyzenFamily.Picasso:
        case RyzenFamily.Dali:
        case RyzenFamily.RenoirLucienne:
        case RyzenFamily.CezanneBarcelo:
          _mp1Msg=0x3B10528; _mp1Rsp=0x3B10564; _mp1Arg=0x3B10998;
          _psmuMsg=0x3B10A20; _psmuRsp=0x3B10A80; _psmuArg=0x3B10A88;
          break;
        case RyzenFamily.VanGogh:
        case RyzenFamily.Rembrandt:
        case RyzenFamily.Phoenix:
        case RyzenFamily.Mendocino:
        case RyzenFamily.HawkPoint:
        case RyzenFamily.StrixHalo:
          _mp1Msg=0x3B10528; _mp1Rsp=0x3B10578; _mp1Arg=0x3B10998;
          _psmuMsg=0x3B10A20; _psmuRsp=0x3B10A80; _psmuArg=0x3B10A88;
          break;
        case RyzenFamily.StrixPoint:
          _mp1Msg=0x3B10928; _mp1Rsp=0x3B10978; _mp1Arg=0x3B10998;
          _psmuMsg=0x3B10A20; _psmuRsp=0x3B10A80; _psmuArg=0x3B10A88;
          break;
        case RyzenFamily.Matisse:
        case RyzenFamily.Vermeer:
          _mp1Msg=0x3B10530; _mp1Rsp=0x3B1057C; _mp1Arg=0x3B109C4;
          _psmuMsg=0x3B10524; _psmuRsp=0x3B10570; _psmuArg=0x3B10A40;
          break;
        case RyzenFamily.RaphaelDragonRange:
        case RyzenFamily.FireRange:
          _mp1Msg=0x3B10530; _mp1Rsp=0x3B1057C; _mp1Arg=0x3B109C4;
          _psmuMsg=0x03B10524; _psmuRsp=0x03B10570; _psmuArg=0x03B10A40;
          break;
        default:
          _mp1Msg=_mp1Rsp=_mp1Arg=_psmuMsg=_psmuRsp=_psmuArg=0;
          break;
      }
    }

    // ── SMU 邮箱协议 (port from UXTU RyzenSmu.RyzenSMU) ──
    // ponytail: 关键 — 用 ioctl_write_smu_register / ioctl_read_smu_register
    // (RyzenSMU.bin 模块导出的 SMU 寄存器直读写函数),而非 ioctl_pci_*_config_dword。
    // 前者直接读写 SMU 地址空间;后者走 PCI 配置空间 SMN mailbox 间接访问,路径不同。
    public SmuStatus SendMp1(uint message, ref uint[] args) =>
      SendMsg(_mp1Msg, _mp1Rsp, _mp1Arg, message, ref args);

    public SmuStatus SendPsmu(uint message, ref uint[] args) =>
      SendMsg(_psmuMsg, _psmuRsp, _psmuArg, message, ref args);

    SmuStatus SendMsg(uint addrMsg, uint addrRsp, uint addrArg, uint msg, ref uint[] args) {
      if (!IsAvailable) return SmuStatus.Failed;
      if (!_pciBusMutex.WaitOne(10000)) return SmuStatus.CmdRejectedBusy;
      try {
        // ponytail: 对齐 UXTU ExecuteMailboxFlow — 第一步等 rsp != 0 (确认 SMU 就绪),
        // 不是等空闲!SMU 就绪后 rsp 是非零的(上次响应残留/固件就绪标志),等 == 0 会超时失败。
        if (!WaitRspNonZero(addrRsp, out _)) return SmuStatus.Failed;
        // 清响应寄存器
        if (!SmuWriteReg(addrRsp, 0)) return SmuStatus.Failed;
        // 写参数 (固定 6 个 uint)
        uint[] cmdArgs = new uint[6];
        int len = Math.Min(args.Length, cmdArgs.Length);
        for (int i = 0; i < len; i++) cmdArgs[i] = args[i];
        for (int i = 0; i < cmdArgs.Length; i++)
          if (!SmuWriteReg(addrArg + (uint)(i*4), cmdArgs[i])) return SmuStatus.Failed;
        // 发消息
        if (!SmuWriteReg(addrMsg, msg)) return SmuStatus.Failed;
        // 等完成 (响应寄存器 != 0)
        if (!WaitRspNonZero(addrRsp, out uint rsp)) return SmuStatus.Failed;
        if (rsp > 0xFF) return SmuStatus.Failed;
        SmuStatus status = (SmuStatus)rsp;
        // 成功才回读参数
        if (status == SmuStatus.Ok && args != null && args.Length > 0) {
          int count = Math.Min(args.Length, 6);
          for (int i = 0; i < count; i++)
            if (!SmuReadReg(addrArg + (uint)(i*4), ref args[i])) return SmuStatus.Failed;
        }
        return status;
      } finally { _pciBusMutex.ReleaseMutex(); }
    }

    // 等 rsp != 0 (UXTU WaitForResponse: 确认 SMU 就绪/命令完成)
    bool WaitRspNonZero(uint addrRsp, out uint rsp) {
      rsp = 0;
      ushort timeout = SmuTimeout;
      do {
        if (SmuReadReg(addrRsp, ref rsp) && rsp != 0) return true;
      } while (--timeout > 0);
      return false;
    }

    // SMU 寄存器直读写 — 调用 RyzenSMU.bin 模块的 ioctl_*_smu_register 函数
    bool SmuWriteReg(uint addr, uint data) {
      if (!_pawnIo.IsLoaded) return false;
      long[] inBuf = { unchecked((long)addr), unchecked((long)data) };
      int hr = _pawnIo.ExecuteHr("ioctl_write_smu_register", inBuf, 2, new long[0], 0, out _);
      return hr == 0;
    }

    bool SmuReadReg(uint addr, ref uint data) {
      if (!_pawnIo.IsLoaded) return false;
      long[] inBuf = { unchecked((long)addr) };
      long[] outBuf = new long[1];
      int hr = _pawnIo.ExecuteHr("ioctl_read_smu_register", inBuf, 1, outBuf, 1, out _);
      if (hr == 0) { data = unchecked((uint)outBuf[0]); return true; }
      return false;
    }

    // ── 高层 API:全核 Curve Optimizer 偏移 ──
    // port from UXTU set-coall 命令表 (MP1=true + RSMU=false 都发)
    // value: 负值=降压,正值=加压。安全范围 -50..+50。
    public SmuStatus SetAllCoreCO(int value) {
      if (!IsAvailable) return SmuStatus.Failed;
      // ponytail: Math.Clamp 不在 net480;手动钳制
      if (value < -50) value = -50; if (value > 50) value = 50;
      // 负值编码:0x100000 - |value| (G-Helper 约定)
      uint uvalue = value < 0 ? (uint)(0x100000 - (uint)(-value)) : (uint)value;
      uint[] args = new uint[6];
      args[0] = uvalue;
      SmuStatus result = SmuStatus.Failed;

      switch (_family) {
        case RyzenFamily.RenoirLucienne:
        case RyzenFamily.CezanneBarcelo:
          // UXTU Socket_FP6_AM4: set-coall true=0x55(MP1), false=0xB1(RSMU)
          result = SendMp1(0x55, ref args);
          if (result == SmuStatus.Ok) result = SendPsmu(0xB1, ref args);
          break;
        case RyzenFamily.Matisse:
        case RyzenFamily.Vermeer:
          // UXTU Socket_AM4_V2: set-coall true=0x36(MP1), false=0xB(RSMU)
          result = SendMp1(0x36, ref args);
          if (result == SmuStatus.Ok) result = SendPsmu(0xB, ref args);
          break;
        case RyzenFamily.VanGogh:
          // UXTU Socket_FF3: set-coall true=0x4c(MP1), false=0x5d(RSMU)
          result = SendMp1(0x4c, ref args);
          if (result == SmuStatus.Ok) result = SendPsmu(0x5d, ref args);
          break;
        case RyzenFamily.Rembrandt:
        case RyzenFamily.Phoenix:
        case RyzenFamily.Mendocino:
        case RyzenFamily.HawkPoint:
        case RyzenFamily.StrixPoint:
        case RyzenFamily.StrixHalo:
          // UXTU Socket_FT6_FP7_FP8: set-coall true=0x4c(MP1), false=0x5d(RSMU)
          result = SendMp1(0x4c, ref args);
          if (result == SmuStatus.Ok) result = SendPsmu(0x5d, ref args);
          break;
        case RyzenFamily.RaphaelDragonRange:
        case RyzenFamily.FireRange:
          // UXTU Socket_AM5_V1: set-coall true=0x36(MP1), false=0x7(RSMU)
          result = SendMp1(0x36, ref args);
          if (result == SmuStatus.Ok) result = SendPsmu(0x7, ref args);
          break;
        default:
          // 未知家族 — 尝试 Rembrandt/Phoenix 命令兜底
          result = SendMp1(0x4c, ref args);
          if (result == SmuStatus.Ok) result = SendPsmu(0x5d, ref args);
          break;
      }
      return result;
    }

    public SmuStatus Reset() => SetAllCoreCO(0);

    // ── 分核 Curve Optimizer 偏移 (port from UXTU set-coper) ──
    // core: 全局核心索引 0..15 (内部拆 ccd = core/8, coreInCcd = core%8)。
    // ponytail: 上限 — 假设最多 2 CCD×8 核=16 核,覆盖所有 Ryzen 移动/桌面。
    // 桌面 >16 核(如 7950X 16C/32T)仍按 16 物理核处理,够用。
    // offset: 负值=降压,安全范围 -100..0。
    public SmuStatus SetPerCoreCO(int core, int offset) {
      if (!IsAvailable) return SmuStatus.Failed;
      if (core < 0 || core > 15) return SmuStatus.Failed;
      if (offset < -100) offset = -100; if (offset > 0) offset = 0;
      // 负值编码:0x100000 - |value|
      uint uvalue = offset < 0 ? (uint)(0x100000 - (uint)(-offset)) : (uint)offset;
      // 参数编码 (UXTU BuildCoperArg): (ccd << 8 | coreInCcd) << 20 | encoded_offset
      int ccd = core / 8;
      int coreInCcd = core % 8;
      uint arg = ((uint)(ccd << 8 | coreInCcd) << 20) | (uvalue & 0xFFFFF);
      uint[] args = new uint[6];
      args[0] = arg;
      // 分核命令 ID 按家族分发 (UXTU set-coper: MP1=true + RSMU=false 都发)
      SmuStatus result = SmuStatus.Failed;
      switch (_family) {
        case RyzenFamily.RenoirLucienne:
        case RyzenFamily.CezanneBarcelo:
          // UXTU Socket_FP6_AM4: set-coper true=0x54(MP1), false=0x52(RSMU)
          result = SendMp1(0x54, ref args);
          if (result == SmuStatus.Ok) result = SendPsmu(0x52, ref args);
          break;
        case RyzenFamily.Matisse:
        case RyzenFamily.Vermeer:
          // UXTU Socket_AM4_V2: set-coper true=0x35(MP1), false=0x0a(RSMU)
          result = SendMp1(0x35, ref args);
          if (result == SmuStatus.Ok) result = SendPsmu(0x0a, ref args);
          break;
        case RyzenFamily.VanGogh:
        case RyzenFamily.Rembrandt:
        case RyzenFamily.Phoenix:
        case RyzenFamily.Mendocino:
        case RyzenFamily.HawkPoint:
        case RyzenFamily.StrixPoint:
        case RyzenFamily.StrixHalo:
          // UXTU Socket_FT6_FP7_FP8 / FF3: set-coper true=0x4b(MP1), false=0x53(RSMU)
          result = SendMp1(0x4b, ref args);
          if (result == SmuStatus.Ok) result = SendPsmu(0x53, ref args);
          break;
        case RyzenFamily.RaphaelDragonRange:
        case RyzenFamily.FireRange:
          // UXTU Socket_AM5_V1: set-coper true=0x35(MP1), false=0x6(RSMU)
          result = SendMp1(0x35, ref args);
          if (result == SmuStatus.Ok) result = SendPsmu(0x6, ref args);
          break;
        default:
          result = SendMp1(0x4b, ref args);
          if (result == SmuStatus.Ok) result = SendPsmu(0x53, ref args);
          break;
      }
      return result;
    }

    // 批量应用分核偏移。offsets: 核心索引 -> 偏移值。返回成功核数。
    // ponytail: 逐核发送 SMU 命令,无事务性 — 部分失败时已写入的核保持设置。
    public int ApplyPerCoreCO(System.Collections.Generic.Dictionary<int, int> offsets) {
      if (!IsAvailable || offsets == null || offsets.Count == 0) return 0;
      int ok = 0;
      foreach (var kv in offsets) {
        if (kv.Value == 0) continue;
        if (SetPerCoreCO(kv.Key, kv.Value) == SmuStatus.Ok) ok++;
      }
      return ok;
    }

    // 解析持久化字符串 "core:offset,core:offset" 为字典。空串/null 返回空。
    public static System.Collections.Generic.Dictionary<int, int> ParsePerCoreOffsets(string s) {
      var dict = new System.Collections.Generic.Dictionary<int, int>();
      if (string.IsNullOrEmpty(s)) return dict;
      foreach (var pair in s.Split(',')) {
        var parts = pair.Split(':');
        if (parts.Length == 2 && int.TryParse(parts[0], out int core) && int.TryParse(parts[1], out int off))
          dict[core] = off;
      }
      return dict;
    }

    // ponytail: ONE runnable check — 启动时自检 SMU 链路是否通(读 SMU 版本)。
    // 不写任何寄存器,只读 0xB8/0xBC 邮箱,失败说明 PawnIO 未就绪或地址错。
    // 仅在 Debug 构建启用,避免 Release 增加启动开销。
    public bool SelfCheck() {
      if (!IsAvailable) return false;
      try {
        // 读 MP1 响应寄存器(不写消息,只读当前值)— 验证 PCI 配置空间访问通畅
        uint data = 0;
        return SmuReadReg(_mp1Rsp, ref data);
      } catch { return false; }
    }
  }
}
