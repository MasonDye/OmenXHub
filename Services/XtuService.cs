using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LibreHardwareMonitor.PawnIo;

namespace OmenSuperHub.Services
{
    // ponytail: 改为裸 MSR 直写,绕开本机不通的 HSA/RPC 通道(docs/MEMORY_XTU_HSA_RE.md
    // 结论:OmenCap 的 IntelXTUOverclockingService 在本机建不起 XTU 会话,所有命令返回
    // 0x2000000C)。协议移植自 UXTU Intel_Management(Intel Backend/Intel_Management.cs):
    //   倍频 → 直接写 MSR 0x1AD (MSR_TURBO_RATIO_LIMIT),8 字节低字节=核0,一次写满;
    //   电压 → OC Mailbox MSR 0x150,bit63=RUN_BUSY,bits[39:32]=cmd,bits[31:0]=data。
    // 类名保留 XtuService 减少 diff(原指 XTU 服务,现已是 MSR 实现)。
    public class XtuService : IDisposable
    {
        // ponytail: 倍频写入 MSR 0x1AD 的 8 个字节槽(低字节=核 0),与 UXTU changeClockRatioOffset
        // 一致。上限:仅覆盖 8 槽,>8 物理核平台按前 8 槽处理(0x1AD 本就只容纳 8 槽)。
        public static readonly uint[] CoreRatioIds = { 0, 1, 2, 3, 4, 5, 6, 7 };

        // ponytail: 电压控制项哨兵 Id — 0..7 是核索引,0xFF 表示电压项,供弹窗按 Id 检索。
        public const uint CpuVoltageOffsetId = 0xFF;

        const uint MsrTurboRatioLimit = 0x1AD;
        const uint MsrOcMailbox = 0x150;
        // ponytail: OC Mailbox 写命令(CPU Core 电压偏移)。bit63=RUN_BUSY、bits[39:32]=cmd
        // (0x11=写偏移)、bits[31:0]=data。0x80000011 = bit63 置 1 + cmd 0x11,移植自 UXTU。
        const ulong VoltageWriteCmdCore = 0x80000011UL;

        // ponytail: 软件侧安全限值(UXTU 不设限)。倍频 8..80 覆盖移动/桌面睿频区间;
        // 电压 -200..+200mV 覆盖常见降压/升压,超出交由硬件自行钳制。
        const int RatioMin = 8, RatioMax = 80;
        const int VoltageMinMv = -200, VoltageMaxMv = 200;

        private IntelMsr _msr;
        private bool _initialized;

        public bool IsConnected => _initialized;

        public async Task<bool> InitializeAsync()
        {
            try
            {
                // ponytail: IntelMsr 字段初始化加载 IntelMSR.bin;驱动不可用时 ReadMsr 返回 false。
                // 探针读 0x1AD — 通则 PawnIO + IntelMSR.bin 就绪,否则不可用。
                _msr = new IntelMsr();
                _initialized = _msr.ReadMsr(MsrTurboRatioLimit, out _);
                Logger.Info($"[XTU-MSR] 初始化: {(_initialized ? "就绪" : "PawnIO/IntelMSR 不可用")}");
                return _initialized;
            }
            catch (Exception ex)
            {
                Logger.Error($"[XTU-MSR] 初始化失败: {ex.Message}");
                _initialized = false;
                return false;
            }
        }

        public Task<OverclockingInfo> GetOverclockingInfoAsync()
        {
            var info = new OverclockingInfo
            {
                IsOverclockSupported = _initialized,
                // ponytail: 0x1AD 容纳 8 个倍频槽,弹窗最多展示 8 核。
                PhysicalCoreCount = (uint)CoreRatioIds.Length,
                ServiceVersion = "MSR"
            };
            return Task.FromResult(info);
        }

        public Task<List<TuningControl>> GetAllControlsAsync()
        {
            var controls = new List<TuningControl>();
            if (!_initialized) return Task.FromResult(controls);

            // 当前倍频:读 0x1AD 拆 8 字节(每字节一核)
            bool ok = _msr.ReadMsr(MsrTurboRatioLimit, out ulong cur);
            for (int i = 0; i < CoreRatioIds.Length; i++)
            {
                byte ratio = ok ? (byte)((cur >> (i * 8)) & 0xFF) : (byte)0;
                controls.Add(new TuningControl
                {
                    Id = (uint)i,
                    Name = $"P-Core Ratio {i + 1}",
                    ActiveValue = ratio,
                    MinValue = RatioMin,
                    MaxValue = RatioMax,
                    Enabled = true
                });
            }

            // ponytail: 电压偏移读回(OC Mailbox 0x10 读命令)UXTU 未实现,默认 0 —
            // 弹窗显示当前偏移不可得,应用时以滑块值为准。
            controls.Add(new TuningControl
            {
                Id = CpuVoltageOffsetId,
                Name = "CPU Core Voltage Offset",
                ActiveValue = 0,
                MinValue = VoltageMinMv,
                MaxValue = VoltageMaxMv,
                Enabled = true
            });
            return Task.FromResult(controls);
        }

        public async Task<bool> SetCoreRatioAsync(Dictionary<uint, decimal> coreRatios)
        {
            if (!_initialized || coreRatios == null || coreRatios.Count == 0) return false;
            try
            {
                // ponytail: 读当前值,按核索引改对应字节,整字写回 — UXTU 实测部分写会清零其余核。
                if (!_msr.ReadMsr(MsrTurboRatioLimit, out ulong cur)) return false;
                foreach (var kv in coreRatios)
                {
                    int idx = (int)kv.Key;
                    if (idx < 0 || idx >= CoreRatioIds.Length) continue;
                    int r = (int)Math.Round(kv.Value);
                    if (r < RatioMin) r = RatioMin;
                    if (r > RatioMax) r = RatioMax;
                    cur &= ~((ulong)0xFF << (idx * 8));
                    cur |= ((ulong)(byte)r) << (idx * 8);
                }
                bool written = _msr.WriteMsr(MsrTurboRatioLimit, cur);
                Logger.Info($"[XTU-MSR] 倍频写入: {(written ? "成功" : "失败")} (0x{cur:X16})");
                await Task.Delay(100);   // ponytail: 与 UXTU 同款写入后时序
                return written;
            }
            catch (Exception ex)
            {
                Logger.Error($"[XTU-MSR] 倍频写入失败: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> SetVoltageOffsetAsync(Dictionary<uint, decimal> voltageOffsets)
        {
            if (!_initialized || voltageOffsets == null || voltageOffsets.Count == 0) return false;
            try
            {
                // 弹窗只传 CPU Core 一项,取第一个值(单位 mV,有符号)
                int mv = (int)Math.Round(voltageOffsets.Values.First());
                if (mv < VoltageMinMv) mv = VoltageMinMv;
                if (mv > VoltageMaxMv) mv = VoltageMaxMv;

                // ponytail: 电压编码移植自 UXTU convertVoltageToHexMSR: round(mv*1.024)<<21,
                // 10bit 有符号(bits[30:21])。例:-50mV → round(-51.2)=-51 → -51<<21 = 0xFFFECE00。
                uint data = unchecked((uint)((int)Math.Round(mv * 1.024) << 21));
                ulong msrValue = (VoltageWriteCmdCore << 32) | data;

                // ponytail: 写前等 RUN_BUSY(bit63)清零,避免打断上一条命令(UXTU 是盲写+sleep,
                // 这里加正式握手更稳;读 0x150 是只读寄存器,安全)。
                if (!WaitMailboxIdle()) return false;
                bool written = _msr.WriteMsr(MsrOcMailbox, msrValue);
                if (written) WaitMailboxIdle();   // 写后等完成
                Logger.Info($"[XTU-MSR] 电压偏移写入: {mv}mV → {(written ? "成功" : "失败")} (0x{msrValue:X16})");
                return written;
            }
            catch (Exception ex)
            {
                Logger.Error($"[XTU-MSR] 电压写入失败: {ex.Message}");
                return false;
            }
        }

        // ponytail: 等 OC Mailbox(MSR 0x150 的 bit63)清零。上限 200ms 超时即放弃 —
        // 若读取恒返回 0(bit63 恒空)则视为空闲,退化为 UXTU 的盲写,不会死等。
        bool WaitMailboxIdle()
        {
            for (int i = 0; i < 200; i++)
            {
                if (!_msr.ReadMsr(MsrOcMailbox, out ulong v)) return false;
                if ((v & 0x8000000000000000UL) == 0) return true;
                System.Threading.Thread.Sleep(1);
            }
            return false;
        }

        public void Dispose()
        {
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
