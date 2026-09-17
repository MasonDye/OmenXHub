using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LibreHardwareMonitor.PawnIo;
using OmenSuperHub.Services.CpuAffinity;

namespace OmenSuperHub.Services
{
    // Intel 硬件预取器控制（MSR 0x1A4，每核 4 位，1=禁用）。
    // ponytail: 倍频/电压/功耗平衡等 Intel 超频面整体移至 feature/intel-ring0-oc 分支
    // （PawnIO 写白名单不含 0x1AD，应用内 WinRing0 备选通道写入位级损坏待查，
    // 复现细节见该分支快照提交说明），本文件只保留已验证的预取器读写与只读诊断。
    public class XtuService : IDisposable
    {
        const uint MsrMiscFeatureControl = 0x1A4;

        IntelMsr _msr;
        bool _hasPrefetcher;

        public bool IsConnected => _hasPrefetcher;
        /// <summary>0x1A4 探测通过（预取器控制可用）。需 InitializeAsync 完成后读取。</summary>
        public bool HasPrefetcher => _hasPrefetcher;

        // 初始化含驱动打开，可能阻塞，必须在后台线程执行。
        public Task<bool> InitializeAsync() => Task.Run(Initialize);

        bool Initialize()
        {
            try
            {
                if (!OmenHardware.HasIntelCpu()) return false;
                _msr = new IntelMsr();
                // 0x1A4 在读白名单内，读到即视为预取器控制可用（值 0 是合法的"全开"）。
                _hasPrefetcher = _msr.ReadMsr(MsrMiscFeatureControl, out _);
                Logger.Info($"[IntelTune] 探测: 1A4预取器={_hasPrefetcher}");
                return _hasPrefetcher;
            }
            catch (Exception ex)
            {
                Logger.Error($"[IntelTune] 初始化失败: {ex.Message}");
                _hasPrefetcher = false;
                return false;
            }
        }

        // ─── 硬件预取器（MSR 0x1A4，每核 4 位，1=禁用） ────────────────────
        public const int PrefetcherBitCount = 4;

        /// <summary>0x1A4 低 4 位掩码编码：l2Hw/adjacent/dcu/dcuIp 传 true 表示<b>禁用</b>该预取器。</summary>
        public static int EncodePrefetcherMask(bool l2HwDisabled, bool adjacentDisabled,
            bool dcuDisabled, bool dcuIpDisabled)
        {
            int mask = 0;
            if (l2HwDisabled) mask |= 1 << 0;
            if (adjacentDisabled) mask |= 1 << 1;
            if (dcuDisabled) mask |= 1 << 2;
            if (dcuIpDisabled) mask |= 1 << 3;
            return mask;
        }

        /// <summary>当前生效的预取器掩码（低 4 位），失败返回 -1。读核 0 的值作全局状态。</summary>
        public int TryGetPrefetcherMask()
        {
            if (_msr == null || !_hasPrefetcher) return -1;
            if (!WithCoreAffinity(0, out IntPtr restore)) return -1;
            try {
                if (!_msr.ReadMsr(MsrMiscFeatureControl, out ulong v)) return -1;
                return (int)(v & 0xF);
            } finally { SetThreadAffinityMask(GetCurrentThread(), restore); }
        }

        /// <summary>
        /// 将 4 位掩码写入所有逻辑核。0x1A4 是每核 MSR，必须把执行线程亲和到目标核再写。
        /// 回读语义：P 核四位全部可保持；E 核(Gracemont)无 L2 邻行预取器(bit1 写 1 不保持)、
        /// DCU 位恒 1 —— 回读与请求不同是拓扑差异不是故障，只要求写入被驱动执行，差异记日志。
        /// </summary>
        public bool TrySetPrefetcherMask(int mask)
        {
            if (_msr == null || !_hasPrefetcher) return false;
            mask &= (1 << PrefetcherBitCount) - 1;
            bool allIssued = true;
            int cores = Math.Min(Environment.ProcessorCount, 64);
            for (int i = 0; i < cores; i++) {
                if (!WithCoreAffinity(i, out IntPtr restore)) { allIssued = false; continue; }
                try {
                    bool issued = _msr.WriteMsrChecked(MsrMiscFeatureControl, (ulong)mask, out int hr) && hr == 0;
                    if (issued && _msr.ReadMsr(MsrMiscFeatureControl, out ulong after)) {
                        int got = (int)(after & 0xF);
                        if (got != mask)
                            Logger.Info($"[IntelTune] 0x1A4 核 {i} 回读 0x{got:X} ≠ 请求 0x{mask:X}（该核预取器子集不同，正常拓扑差异）");
                    }
                    if (!issued) Logger.Info($"[IntelTune] 0x1A4 核 {i} 写入被拒 (hr=0x{hr:X8})");
                    allIssued &= issued;
                } finally { SetThreadAffinityMask(GetCurrentThread(), restore); }
            }
            return allIssued;
        }

        // 硬件诊断转储：只读，不写任何寄存器。用于确认本机 MSR 真实布局。
        // 用法：OmenXHub.exe --intelmsr（需管理员权限）
        public static string DumpDiagnostics()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[IntelMsrDump] " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            if (!OmenHardware.HasIntelCpu()) {
                sb.AppendLine("非 Intel 平台，跳过");
                return sb.ToString();
            }

            try {
                CpuTopology t = CoreKeepService.GetTopology();
                sb.AppendLine($"掩码: P=0x{t.PcoreMask:X} E=0x{t.EcoreMask:X} " +
                    $"SMT0=0x{t.Smt0Mask:X} 总逻辑={t.TotalLogicalProcessors}");
            } catch (Exception ex) {
                sb.AppendLine("掩码读取失败: " + ex.Message);
            }

            IntelMsr msr = null;
            try {
                msr = new IntelMsr();
                uint[] candidates = {
                    0x17, 0x35, 0x8B, 0xCE, 0x198, 0x199, 0x19C, 0x1A2,
                    0x1A4, 0x1AD, 0x1AE, 0x1AF, 0x1B0, 0x601, 0x606, 0x610, 0x611, 0x771, 0x774, 0x1FC
                };
                foreach (uint index in candidates) {
                    if (msr.ReadMsr(index, out ulong value)) {
                        string bytes = string.Join(",", Enumerable.Range(0, 8)
                            .Select(i => ((value >> (i * 8)) & 0xFF).ToString()));
                        sb.AppendLine($"MSR 0x{index:X3} = 0x{value:X16}  字节[1..8 活跃核]=[{bytes}]");
                    } else {
                        sb.AppendLine($"MSR 0x{index:X3} = 读取失败");
                    }
                }
            } catch (Exception ex) {
                sb.AppendLine("MSR 通道不可用（是否以管理员运行？）: " + ex.Message);
            } finally {
                try { msr?.Close(); } catch { }
            }
            return sb.ToString();
        }

        // 纯逻辑自检：不触碰硬件，可在任意机型的 --selftest 中运行。
        public static bool SelfCheck(out string message)
        {
            var errors = new List<string>();

            if (EncodePrefetcherMask(true, false, true, false) != 0b0101)
                errors.Add("预取器掩码编码: 位序应为 L2/邻行/DCU/DCU-IP");
            if (EncodePrefetcherMask(false, false, false, false) != 0)
                errors.Add("预取器掩码编码: 全开应为 0");

            message = errors.Count == 0
                ? "PASS XtuService: 预取器掩码编码"
                : "FAIL XtuService: " + string.Join(" | ", errors);
            return errors.Count == 0;
        }

        public void Dispose()
        {
            try { _msr?.Close(); } catch { }
            _msr = null;
            _hasPrefetcher = false;
        }

        // ponytail: 单组亲和(1L<<core)覆盖 ≤64 逻辑核,本机 24 线程足够;
        // 跨处理器组机器需 GROUP_AFFINITY 升级,当前机型无此需求。
        static bool WithCoreAffinity(int coreIdx, out IntPtr previous)
        {
            previous = SetThreadAffinityMask(GetCurrentThread(), new IntPtr(1L << coreIdx));
            return previous != IntPtr.Zero;
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        static extern IntPtr SetThreadAffinityMask(IntPtr hThread, IntPtr dwThreadAffinityMask);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        static extern IntPtr GetCurrentThread();
    }
}
