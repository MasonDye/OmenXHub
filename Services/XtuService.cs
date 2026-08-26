using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LibreHardwareMonitor.PawnIo;

namespace OmenSuperHub.Services
{
    // ponytail: Intel 混合架构(8P+8E)裸 MSR 直写,绕开本机不通的 HSA/RPC 通道。
    //   P-core 倍频 → MSR 0x1AD (MSR_TURBO_RATIO_LIMIT), 8 个 8-bit 槽,低字节=核 0;
    //   E-core 倍频 → MSR 0x1AE (MSR_TURBO_RATIO_LIMIT1), 8 个 8-bit 槽,低字节=核 0;
    //   电压偏移   → OC Mailbox MSR 0x150 (UXTU 同款编码)。
    // 写-读-验证:MSR 易失,写入后立即回读比对。若回读不一致,可能是该核心 FUSE 锁定 —
    // 本类不预判可写性,如实回读交 UI/调用方判断。类名保留 XtuService 减少 diff。
    public class XtuService : IDisposable
    {
        // ponytail: 槽索引 0..15。前 8(0..7)= P-core → 0x1AD;后 8(8..15)= E-core → 0x1AE。
        // 8P+8E 固定 16 槽;单核数量少于 16 的平台,多余的槽读/写为 0。
        public static readonly uint[] CoreRatioIds = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 };

        // ponytail: 电压控制项哨兵 Id —— 0..15 是核槽索引,0xFF 表示电压项。
        public const uint CpuVoltageOffsetId = 0xFF;

        const uint MsrTurboRatioP = 0x1AD;   // P-core 每核倍频上限 (8 槽)
        const uint MsrTurboRatioE = 0x1AE;   // E-core(atom)每核倍频上限 (8 槽,hybrid)
        const uint MsrOcMailbox = 0x150;
        // ponytail: OC Mailbox 写命令(CPU Core 电压偏移)。bit63=RUN_BUSY、bits[39:32]=cmd
        // (0x11=写偏移)、bits[31:0]=data。0x80000011 = bit63 置 1 + cmd 0x11 (port UXTU)。
        const ulong VoltageWriteCmdCore = 0x80000011UL;

        // ponytail: 软件侧安全限值。倍频 8..255(每槽 1 字节上限)、电压 -200..+200mV。
        const int RatioMin = 8, RatioMax = 255;
        const int VoltageMinMv = -200, VoltageMaxMv = 200;

        private IntelMsr _msr;
        private bool _initialized;
        // 当前已知倍频快照(核索引 0..15 → 倍频值),用于读-改-写与回读展示。
        private readonly byte[] _ratios = new byte[16];

        public bool IsConnected => _initialized;

        public Task<bool> InitializeAsync()
        {
            try
            {
                _msr = new IntelMsr();
                // 探针读 0x1AD/0x1AE —— 通则 PawnIO + IntelMSR.bin 就绪。
                bool readP = _msr.ReadMsr(MsrTurboRatioP, out ulong pVal);
                bool readE = _msr.ReadMsr(MsrTurboRatioE, out ulong eVal);
                _initialized = readP && readE;
                if (_initialized) {
                    // ponytail: 大端序对齐 UXTU —— _ratios[0](核0)对应 MSR 最高字节(bits[63:56]),
                    // 依次到 _ratios[7](核7)对应 bits[7:0]。(src: Intel_Management.readClockRatios
                    // 用 ToString("X16") 高位在前逐 2 字符拆分,index0=最高字节。)
                    for (int i = 0; i < 8; i++) {
                        _ratios[i]     = (byte)((pVal >> ((7 - i) * 8)) & 0xFF);
                        _ratios[i + 8] = (byte)((eVal >> ((7 - i) * 8)) & 0xFF);
                    }
                }
                Logger.Info($"[XTU-MSR] 初始化: {(_initialized ? "就绪" : "PawnIO/IntelMSR 不可用")}");
                return Task.FromResult(_initialized);
            }
            catch (Exception ex)
            {
                Logger.Error($"[XTU-MSR] 初始化失败: {ex.Message}");
                _initialized = false;
                return Task.FromResult(false);
            }
        }

        public Task<OverclockingInfo> GetOverclockingInfoAsync()
        {
            var info = new OverclockingInfo
            {
                IsOverclockSupported = _initialized,
                PhysicalCoreCount = 8,       // P-core 数
                EfficientCoreCount = 8,      // E-core 数
                ServiceVersion = "MSR"
            };
            return Task.FromResult(info);
        }

        public Task<List<TuningControl>> GetAllControlsAsync()
        {
            var controls = new List<TuningControl>();
            if (!_initialized) return Task.FromResult(controls);

            // 真实回读当前倍频:先刷新快照,再投影成控制项。
            if (_msr.ReadMsr(MsrTurboRatioP, out ulong pVal) && _msr.ReadMsr(MsrTurboRatioE, out ulong eVal)) {
                for (int i = 0; i < 8; i++) {
                    _ratios[i]     = (byte)((pVal >> ((7 - i) * 8)) & 0xFF);
                    _ratios[i + 8] = (byte)((eVal >> ((7 - i) * 8)) & 0xFF);
                }
            }
            for (int i = 0; i < 16; i++) {
                controls.Add(new TuningControl {
                    Id = (uint)i,
                    Name = (i < 8 ? "P-Core " : "E-Core ") + (i < 8 ? i + 1 : i - 7),
                    ActiveValue = _ratios[i],
                    MinValue = RatioMin,
                    MaxValue = RatioMax,
                    Enabled = true
                });
            }

            // 电压项:当前 mV 从持久化字段读(MSR 电压读回需 OC Mailbox 0x10,UUXTU 未实现,用保存值兜底)。
            controls.Add(new TuningControl {
                Id = CpuVoltageOffsetId,
                Name = "CPU Core Voltage Offset",
                ActiveValue = ConfigService.IntelVoltageOffset,
                MinValue = VoltageMinMv,
                MaxValue = VoltageMaxMv,
                Enabled = true
            });
            return Task.FromResult(controls);
        }

        public async Task<bool> SetCoreRatioAsync(Dictionary<uint, decimal> coreRatios)
        {
            if (!_initialized || coreRatios == null || coreRatios.Count == 0) return false;
            try {
                // 读当前两个 MSR,按核索引拆 P/E,读-改-写(大端:核0=高字节,见上)。
                if (!_msr.ReadMsr(MsrTurboRatioP, out ulong pVal)) return false;
                if (!_msr.ReadMsr(MsrTurboRatioE, out ulong eVal)) return false;
                for (int i = 0; i < 8; i++) {
                    _ratios[i]     = (byte)((pVal >> ((7 - i) * 8)) & 0xFF);
                    _ratios[i + 8] = (byte)((eVal >> ((7 - i) * 8)) & 0xFF);
                }

                foreach (var kv in coreRatios) {
                    int idx = (int)kv.Key;
                    if (idx < 0 || idx >= 16) continue;
                    int r = (int)Math.Round(kv.Value);
                    if (r < RatioMin) r = RatioMin;
                    if (r > RatioMax) r = RatioMax;
                    _ratios[idx] = (byte)r;
                }

                // 重组两个 64-bit 值并写回(大端:核0=高字节 bits[63:56])。
                ulong newP = 0, newE = 0;
                for (int i = 0; i < 8; i++) {
                    newP |= ((ulong)_ratios[i])     << ((7 - i) * 8);
                    newE |= ((ulong)_ratios[i + 8]) << ((7 - i) * 8);
                }
                bool okP = _msr.WriteMsr(MsrTurboRatioP, newP);
                bool okE = _msr.WriteMsr(MsrTurboRatioE, newE);
                await Task.Delay(100);

                // 回读验证(写-读),如实反映是否生效。
                _msr.ReadMsr(MsrTurboRatioP, out ulong chkP);
                _msr.ReadMsr(MsrTurboRatioE, out ulong chkE);
                bool pStuck = chkP != newP, eStuck = chkE != newE;
                Logger.Info($"[XTU-MSR] 倍频写: P={(okP ? "ok" : "fail")}/{(pStuck ? "未生效" : "生效")}, E={(okE ? "ok" : "fail")}/{(eStuck ? "未生效" : "生效")}");

                // 持久化到注册表,下次打开/重启 PresetManager 重应用(MSR 易失)。
                ConfigService.IntelPerCoreRatios = EncodeRatios(_ratios);
                ConfigService.Save("IntelPerCoreRatios");
                return okP && okE;
            } catch (Exception ex) {
                Logger.Error($"[XTU-MSR] 倍频写入失败: {ex.Message}");
                return false;
            }
        }

        public Task<bool> SetVoltageOffsetAsync(Dictionary<uint, decimal> voltageOffsets)
        {
            if (!_initialized || voltageOffsets == null || voltageOffsets.Count == 0) return Task.FromResult(false);
            try {
                int mv = (int)Math.Round(voltageOffsets.Values.First());
                if (mv < VoltageMinMv) mv = VoltageMinMv;
                if (mv > VoltageMaxMv) mv = VoltageMaxMv;

                // ponytail: 电压编码 port UXTU convertVoltageToHexMSR: round(mv*1.024)<<21。
                uint data = unchecked((uint)((int)Math.Round(mv * 1.024) << 21));
                ulong msrValue = (VoltageWriteCmdCore << 32) | data;

                if (!WaitMailboxIdle()) return Task.FromResult(false);
                bool written = _msr.WriteMsr(MsrOcMailbox, msrValue);
                if (written) WaitMailboxIdle();
                if (written) {
                    ConfigService.IntelVoltageOffset = mv;
                    ConfigService.Save("IntelVoltageOffset");
                }
                Logger.Info($"[XTU-MSR] 电压偏移: {mv}mV → {(written ? "成功" : "失败")} (0x{msrValue:X16})");
                return Task.FromResult(written);
            } catch (Exception ex) {
                Logger.Error($"[XTU-MSR] 电压写入失败: {ex.Message}");
                return Task.FromResult(false);
            }
        }

        bool WaitMailboxIdle() {
            for (int i = 0; i < 200; i++) {
                if (!_msr.ReadMsr(MsrOcMailbox, out ulong v)) return false;
                if ((v & 0x8000000000000000UL) == 0) return true;
                System.Threading.Thread.Sleep(1);
            }
            return false;
        }

        // ponytail: 序列化 "core:ratio,core:ratio"。仅写非零倍频(0 槽=未读到的占位,不回写)。
        static string EncodeRatios(byte[] ratios) {
            var parts = new List<string>();
            for (int i = 0; i < ratios.Length; i++)
                if (ratios[i] > 0) parts.Add($"{i}:{ratios[i]}");
            return string.Join(",", parts);
        }

        public void Dispose() {
            try { _msr?.Close(); } catch { }
            _initialized = false;
        }
    }

    public class OverclockingInfo
    {
        public bool IsOverclockSupported { get; set; }
        public bool IsSystemUnlocked { get; set; }
        public bool IsTurboBoostEnabled { get; set; }
        public bool IsCoreOcEnabled { get; set; }
        public bool IsClrOcEnabled { get; set; }
        public string ServiceVersion { get; set; }
        public uint PhysicalCoreCount { get; set; }
        public uint EfficientCoreCount { get; set; }
    }

    public class TuningControl
    {
        public uint Id { get; set; }
        public string Name { get; set; }
        public decimal DefaultValue { get; set; }
        public decimal ActiveValue { get; set; }
        public decimal MinValue { get; set; }
        public decimal MaxValue { get; set; }
        public bool Enabled { get; set; }
        public bool ReadOnly { get; set; }
        public bool RequiresReboot { get; set; }
        public List<decimal> SupportedValues { get; set; }
    }
}
