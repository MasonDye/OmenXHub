// Services/RaplPowerLimitService.cs - Intel RAPL 直写 CPU 功率墙 (IA32_PKG_POWER_LIMIT 0x610)
//
// 背景：项目原本用 HP BIOS WMI 0x29 (载荷 [PL2, PL1, PL4, TPP]) 请求 EC 设置功率墙，
// 该路径“WMI 返回成功 ≠ 实际生效”且无读回校验（见 docs/MEMORY_POWER_THERMAL_DIAGNOSTICS.md）。
// 当用户在设置页打开「启用高级硬件访问 (EC/SMU)」(ConfigService.EnableEcAccess) 后，
// Intel 平台的 PL1/PL2 改走本服务：PawnIO IntelMSR.bin 直写 0x610，读-改-写 + 回读验证。
// 写入失败或通道不可用时自动回退到原 WMI 路径，保证零回归。
// ponytail: 双重写入 —— 直写成功后 OmenHardware 仍延迟补发 WMI 0x29 同步 EC 侧状态
// （光影机型 EC 侧功率墙状态需保持一致），详见 OmenHardware.QueueDualWmiSend。
//
// 位域参考 Intel SDM Vol.4 (IA32_PKG_POWER_LIMIT)；功率单位换算与参考实现
// 「Intel降压定频」 msr.pyc 的 _power_unit / read_power_limits 一致：
//   pu  = 1 << (rdmsr(0x606) & 0xF)      → 本机 0x606=0x000A0E03 → pu=8（1/8 W 步进）
//   PL1 = (v & 0x7FFF) / pu，使能位 bit15；PL2 = ((v>>32) & 0x7FFF) / pu，使能位 bit47
// 注：参考实现清 PL2 时间窗用的掩码是 ~(0x3F<<53)，与 SDM 的 bits[55:49] 不符；
// 这里不照抄——默认完全保留原时间窗位不动，只改功率值与使能位。

using System;
using System.IO;
using System.Management;
using LibreHardwareMonitor.PawnIo;

namespace OmenSuperHub.Services {
  /// <summary>Intel RAPL 功率墙直写（EnableEcAccess 开启时启用；OmenHardware 会再补发 WMI 0x29 双重写入）</summary>
  internal static class RaplPowerLimitService {
    const uint MsrRaplPowerUnit = 0x606;
    const uint MsrPkgPowerLimit = 0x610;

    // IA32_PKG_POWER_LIMIT 位域
    const int Pl1EnableBit = 15;
    const int Pl2LimitShift = 32;
    const int Pl2EnableBit = 47;
    const int LockBit = 63;
    const ulong LimitMask = 0x7FFFUL;   // bits[14:0] / bits[46:32]

    // 安全钳位：与 WMI 路径的 byte 参数语义对齐（10..254W），避免写 0 将 CPU 锁死。
    public const int WattMin = 10;
    public const int WattMax = 254;

    static readonly object _lock = new object();
    static IntelMsr _msr;
    static bool _initFailed;
    static int _powerUnit;
    static ulong? _originalRegister;
    // ponytail: 崩溃自愈 —— 0x610 的修改跨进程存活(进程被杀/崩溃/断电都不走 OnExit),
    // 首次真写前把本启动的原始寄存器值落盘(带 boot 身份),下次 IsAvailable 初始化时发现
    // 遗留快照即还原。0x610 不跨重启持久,boot 身份不符的快照必须丢弃而非回写(见 heal)。
    static bool _snapshotPersisted;

    /// <summary>通道是否可用（PawnIO 已装 + Intel CPU + 0x610 可读且未锁）</summary>
    public static bool IsAvailable {
      get {
        if (_initFailed) return false;
        if (_msr != null) return true;
        lock (_lock) {
          if (_msr != null) return true;
          if (_initFailed) return false;
          try {
            if (!PawnIo.IsInstalled) { _initFailed = true; return false; }
            if (!OmenHardware.HasIntelCpu()) { _initFailed = true; return false; }
            var msr = new IntelMsr();
            // PawnIO IntelMSR.bin 对白名单外的 MSR 读会返回 true/0，全零不能当作可用。
            if (!msr.ReadMsr(MsrPkgPowerLimit, out ulong v) || v == 0) {
              _initFailed = true; msr.Close(); return false;
            }
            // 锁位置 1 后整个寄存器只读到下次重启，直写必失败，不开放假能力。
            if (((v >> LockBit) & 1) != 0) {
              Logger.Warn("[Rapl] 0x610 已被 BIOS 锁定(bit63=1)，直写不可用，保留 WMI 路径");
              _initFailed = true; msr.Close(); return false;
            }
            if (!msr.ReadMsr(MsrRaplPowerUnit, out ulong unitRaw) || unitRaw == 0) {
              _initFailed = true; msr.Close(); return false;
            }
            _powerUnit = DecodePowerUnit(unitRaw);
            _msr = msr;
            // 先自愈(上次进程被杀/崩溃遗留的同-boot 快照回写),再记录本 boot 原值 ——
            // 顺序颠倒会把"上次会话改过的值"当成出厂原值。快照只在本会话真正写入前落盘
            // (TrySetPowerLimits 首笔),从未写过 PL 的会话不生成文件。
            if (HealOrPruneSnapshot(msr) && msr.ReadMsr(MsrPkgPowerLimit, out ulong healed)) v = healed;
            _originalRegister = v;
            DecodeWatts(v, _powerUnit, out int pl1, out int pl2);
            Logger.Info($"[Rapl] 0x610=0x{v:X16}, 功率单位=1/{_powerUnit}W, 当前 PL1={pl1}W PL2={pl2}W");
            return true;
          } catch (Exception ex) {
            Logger.Error($"[Rapl] 初始化失败: {ex.Message}");
            _initFailed = true;
            return false;
          }
        }
      }
    }

    /// <summary>EnableEcAccess 开启且通道可用时，功率墙应该走直写而不是 WMI</summary>
    public static bool ShouldHandlePowerLimit => ConfigService.EnableEcAccess && IsAvailable;

    /// <summary>App 启动期调用(后台线程):存在遗留快照才初始化通道并自愈。
    /// 不走这个入口的话,自愈被 ShouldHandlePowerLimit 的 EnableEcAccess 门挡住 ——
    /// "用完就关开关,之后进程被杀"的场景永远轮不到还原。无快照时只花一次
    /// File.Exists,常路径零硬件开销;快照存在而通道不可用(PawnIO 被卸载等)则
    /// 快照留着,重启后异-boot 剪除。</summary>
    public static void StartupHealIfNeeded() {
      try { if (File.Exists(SnapshotPath)) { _ = IsAvailable; } }
      catch (Exception ex) { Logger.Warn("[Rapl] StartupHeal: " + ex.Message); }
    }

    // ═══ 纯逻辑编解码（可被自检直接调用，不触硬件）═══

    /// <summary>功率单位：pu = 1 &lt;&lt; (0x606 &amp; 0xF)，典型值 8（即 1/8 W）</summary>
    public static int DecodePowerUnit(ulong powerUnitRaw) {
      int exp = (int)(powerUnitRaw & 0xF);
      return 1 << exp;
    }

    /// <summary>从 0x610 解出 PL1/PL2 瓦数（四舍五入）</summary>
    public static void DecodeWatts(ulong register, int powerUnit, out int pl1Watt, out int pl2Watt) {
      if (powerUnit <= 0) powerUnit = 8;
      pl1Watt = (int)Math.Round((register & LimitMask) / (double)powerUnit);
      pl2Watt = (int)Math.Round(((register >> Pl2LimitShift) & LimitMask) / (double)powerUnit);
    }

    /// <summary>
    /// 读-改-写：只替换功率值位与使能位，其余位（钳位/时间窗/保留位）原样保留。
    /// pl1Watt / pl2Watt 传 null 表示该字段不动（对应 WMI 0x29 的 0xFF 哨兵语义）。
    /// </summary>
    public static ulong EncodeRegister(ulong current, int powerUnit, int? pl1Watt, int? pl2Watt) {
      if (powerUnit <= 0) powerUnit = 8;
      ulong v = current;
      if (pl1Watt.HasValue) {
        ulong raw = WattToRaw(pl1Watt.Value, powerUnit);
        v = (v & ~LimitMask) | raw;
        v |= 1UL << Pl1EnableBit;
      }
      if (pl2Watt.HasValue) {
        ulong raw = WattToRaw(pl2Watt.Value, powerUnit);
        v = (v & ~(LimitMask << Pl2LimitShift)) | (raw << Pl2LimitShift);
        v |= 1UL << Pl2EnableBit;
      }
      return v;
    }

    static ulong WattToRaw(int watt, int powerUnit) {
      if (watt < WattMin) watt = WattMin;
      if (watt > WattMax) watt = WattMax;
      ulong raw = (ulong)((long)watt * powerUnit);
      return raw > LimitMask ? LimitMask : raw;
    }

    // ═══ 硬件读写 ═══

    /// <summary>读当前生效的 PL1/PL2（瓦），失败返回 false</summary>
    public static bool TryReadPowerLimits(out int pl1Watt, out int pl2Watt) {
      pl1Watt = pl2Watt = 0;
      if (!IsAvailable) return false;
      lock (_lock) {
        if (!_msr.ReadMsr(MsrPkgPowerLimit, out ulong v)) return false;
        DecodeWatts(v, _powerUnit, out pl1Watt, out pl2Watt);
        return true;
      }
    }

    /// <summary>
    /// 写 PL1/PL2（瓦）。null = 保持原值。写入后回读验证，功率位不一致则视为失败。
    /// </summary>
    public static bool TrySetPowerLimits(int? pl1Watt, int? pl2Watt) {
      if (!IsAvailable) return false;
      if (!pl1Watt.HasValue && !pl2Watt.HasValue) return true;
      lock (_lock) {
        try {
          if (!_msr.ReadMsr(MsrPkgPowerLimit, out ulong current)) {
            Logger.Warn("[Rapl] 读 0x610 失败，放弃直写");
            return false;
          }
          if (_originalRegister == null) _originalRegister = current;

          ulong target = EncodeRegister(current, _powerUnit, pl1Watt, pl2Watt);
          if (target == current) return true;   // 幂等：无变化不写

          // 首笔真写前落崩溃自愈快照(boot 身份)。已被同-boot 快照兜过就跳过写盘。
          // 记 current(首笔写前瞬间的值)而非 _originalRegister —— 中途可能有他方写入,
          // "我们动手前的值"才是正确的还原目标。
          if (!_snapshotPersisted) PersistSnapshot(current);

          if (!_msr.WriteMsr(MsrPkgPowerLimit, target)) {
            Logger.Warn($"[Rapl] 写 0x610 被驱动拒绝 (target=0x{target:X16})");
            return false;
          }
          // 回读验证：BIOS/微码可能静默忽略写入，只比功率值位（硬件可能自行调整其余位）。
          if (!_msr.ReadMsr(MsrPkgPowerLimit, out ulong after)) return false;
          bool ok = true;
          if (pl1Watt.HasValue) ok &= (after & LimitMask) == (target & LimitMask);
          if (pl2Watt.HasValue) ok &= ((after >> Pl2LimitShift) & LimitMask) == ((target >> Pl2LimitShift) & LimitMask);
          if (!ok) {
            DecodeWatts(after, _powerUnit, out int gotPl1, out int gotPl2);
            Logger.Warn($"[Rapl] 回读不符: 期望 PL1={pl1Watt?.ToString() ?? "-"} PL2={pl2Watt?.ToString() ?? "-"}, " +
                        $"实际 PL1={gotPl1}W PL2={gotPl2}W (0x{after:X16})");
            return false;
          }
          Logger.Info($"[Rapl] 直写生效: PL1={pl1Watt?.ToString() ?? "不变"} PL2={pl2Watt?.ToString() ?? "不变"} (0x{after:X16})");
          return true;
        } catch (Exception ex) {
          Logger.Error($"[Rapl] 写入异常: {ex.Message}");
          return false;
        }
      }
    }

    /// <summary>OnExit 入口：干净退出只清快照、不回写 MSR。保持全工程既有契约
    /// "直写生效直到重启/下次启动预设重放"(与 WMI 0x29 路径一致,避免出现唯一一个
    /// 退出即自撤的设置项)。快照存在的唯一合法含义因此收敛为"上次进程非正常死亡",
    /// 启动自愈(HealOrPruneSnapshot)据此回写原值。</summary>
    public static void ClearSnapshotOnExit() {
      try { if (File.Exists(SnapshotPath)) File.Delete(SnapshotPath); } catch { }
      _snapshotPersisted = false;
    }

    // ═══ 崩溃自愈快照（boot 身份限定）═══
    // 0x610 的修改跨进程存活(被杀/崩溃不走 OnExit),但跨重启回固件默认 —— 快照必须
    // 带 boot 身份:同 boot 遗留 = 上次真没还原,回写;异 boot = 寄存器已自复位,只删
    // 文件绝不盲写(陈旧原值可能 ≠ 本次 boot 的默认值)。

    static string SnapshotPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OmenXHub", "rapl-0x610-snapshot.txt");

    // WMI 查询仅在有遗留快照或首笔直写时发生,常规读路径零开销。
    static string GetBootId() {
      try {
        using (var s = new ManagementObjectSearcher("SELECT LastBootUpTime FROM Win32_OperatingSystem"))
          foreach (ManagementObject o in s.Get())
            using (o) return Convert.ToString(o["LastBootUpTime"], System.Globalization.CultureInfo.InvariantCulture);
      } catch (Exception ex) { Logger.Verbose("[Rapl] GetBootId: " + ex.Message); }
      return "";
    }

    /// <summary>快照文本 "bootId|REGHEX" → 同 boot 且可解析才返回 true。纯函数,SelfCheck 覆盖。
    /// 任一侧 bootId 为空(WMI 查询失败)一律不匹配 —— 身份未知时永不回写,失败方向取
    /// "跳过自愈"(最多少还原一次),不取"误判同 boot 盲写陈旧值"。</summary>
    internal static bool TryParseSnapshot(string text, string currentBootId, out ulong original) {
      original = 0;
      if (string.IsNullOrEmpty(currentBootId)) return false;
      var parts = (text ?? "").Split('|');
      if (parts.Length < 2 || string.IsNullOrEmpty(parts[0]) || parts[0] != currentBootId) return false;
      return ulong.TryParse(parts[1], System.Globalization.NumberStyles.HexNumber,
              System.Globalization.CultureInfo.InvariantCulture, out original) && original != 0;
    }

    /// <summary>发现遗留快照:同 boot → 回写原值(返回 true,调用方重读寄存器);异 boot/畸形 → 删除不写。</summary>
    static bool HealOrPruneSnapshot(IntelMsr msr) {
      try {
        if (!File.Exists(SnapshotPath)) return false;
        string text = File.ReadAllText(SnapshotPath);
        try { File.Delete(SnapshotPath); } catch { }
        if (!TryParseSnapshot(text, GetBootId(), out ulong orig)) {
          Logger.Info("[Rapl] 遗留快照属异 boot/畸形(0x610 重启即回默认),已丢弃不回放");
          return false;
        }
        if (msr.ReadMsr(MsrPkgPowerLimit, out ulong now) && now == orig) return false; // 已还原过,no-op
        if (!msr.WriteMsr(MsrPkgPowerLimit, orig)) {
          Logger.Warn("[Rapl] 崩溃自愈回写被驱动拒绝,快照已消费(重启后 0x610 反正自复位)");
          return false;
        }
        // 回读验证(与 TrySetPowerLimits 同款):固件可能静默忽略写入
        if (msr.ReadMsr(MsrPkgPowerLimit, out ulong after) && after != orig)
          Logger.Warn($"[Rapl] 崩溃自愈回读不符: 0x{after:X16}");
        Logger.Warn($"[Rapl] 上次会话异常退出遗留直写,自愈还原 0x{orig:X16}");
        return true;
      } catch (Exception ex) { Logger.Warn("[Rapl] snapshot heal: " + ex.Message); return false; }
    }

    static void PersistSnapshot(ulong original) {
      try {
        var dir = Path.GetDirectoryName(SnapshotPath);
        if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
        // temp + Replace 原子换入(同 MacroService/AutomationService 惯例)
        string tmp = SnapshotPath + ".tmp";
        File.WriteAllText(tmp, GetBootId() + "|" + original.ToString("X16", System.Globalization.CultureInfo.InvariantCulture));
        if (File.Exists(SnapshotPath)) File.Replace(tmp, SnapshotPath, null);
        else File.Move(tmp, SnapshotPath);
        _snapshotPersisted = true;
      } catch (Exception ex) { Logger.Warn("[Rapl] snapshot persist: " + ex.Message); }
    }

    /// <summary>状态描述（诊断日志用）</summary>
    public static string Describe() {
      if (!IsAvailable) return "RAPL 直写不可用";
      return TryReadPowerLimits(out int pl1, out int pl2)
        ? $"RAPL 可用: PL1={pl1}W PL2={pl2}W (1/{_powerUnit}W 步进)"
        : "RAPL 可用，但读取失败";
    }

    public static void Close() {
      lock (_lock) {
        _msr?.Close();
        _msr = null;
      }
    }

    /// <summary>纯逻辑自检：编解码往返、null 不动语义、钳位。不初始化 PawnIO、不碰 MSR。</summary>
    public static string SelfCheck() {
      var fails = new System.Collections.Generic.List<string>();
      // ground truth: 本机 0x606=0x000A0E03 → pu=8; 0x610=0x0042841000DF8410 → PL1=PL2=130W
      if (DecodePowerUnit(0x000A0E03UL) != 8) fails.Add("DecodePowerUnit(0x000A0E03) != 8");
      DecodeWatts(0x0042841000DF8410UL, 8, out int pl1, out int pl2);
      if (pl1 != 130 || pl2 != 130) fails.Add($"decode {pl1}/{pl2} != 130/130");
      // 写入往返
      const ulong sample = 0x0042841000DF8410UL;
      ulong v = EncodeRegister(sample, 8, 65, 90);
      DecodeWatts(v, 8, out int e1, out int e2);
      if (e1 != 65 || e2 != 90) fails.Add($"roundtrip {e1}/{e2} != 65/90");
      // 只许动功率值位与两个使能位——时间窗/钳位/锁位原样保留
      ulong touched = v ^ sample;
      ulong allowed = LimitMask | (LimitMask << Pl2LimitShift)
                    | (1UL << Pl1EnableBit) | (1UL << Pl2EnableBit);
      if ((touched & ~allowed) != 0) fails.Add($"touched bits outside limit/enable: 0x{touched & ~allowed:X16}");
      // null = 该字段不动（WMI 0xFF 哨兵语义）
      if (EncodeRegister(0x1234UL, 8, null, null) != 0x1234UL) fails.Add("null must be a no-op");
      // 钳位：10..254W，防止写 0 锁死
      DecodeWatts(EncodeRegister(0UL, 8, 5, 300), 8, out int lo, out int hi);
      if (lo != WattMin || hi != WattMax) fails.Add($"clamp {lo}/{hi} != {WattMin}/{WattMax}");
      // 崩溃自愈快照解析:同 boot 往返 / 异 boot 拒 / 空 bootId 拒(身份未知不盲写) / 畸形拒
      bool ok1 = TryParseSnapshot("BOOT1|" + sample.ToString("X16"), "BOOT1", out ulong s1);
      if (!ok1 || s1 != sample) fails.Add("snapshot same-boot roundtrip failed");
      if (TryParseSnapshot("BOOT1|ABCD", "BOOT2", out _)) fails.Add("snapshot cross-boot must reject");
      if (TryParseSnapshot("BOOT1|ABCD", "", out _)) fails.Add("snapshot empty current bootId must reject");
      if (TryParseSnapshot("BOOT1|", "BOOT1", out _)) fails.Add("snapshot malformed hex must reject");
      if (TryParseSnapshot("BOOT1|0", "BOOT1", out _)) fails.Add("snapshot zero value must reject (寄存器全零非合法原值)");
      if (TryParseSnapshot("garbage", "BOOT1", out _)) fails.Add("snapshot no-separator must reject");
      return fails.Count == 0 ? "OK" : "FAIL RaplPowerLimit: " + string.Join(" | ", fails);
    }
  }
}
