using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OmenSuperHub.Services {
  internal static class IntelAdvancedService {
    static readonly object _msrLock = new();

    public static bool IsAvailable => OmenXHubDriver.IsAvailable;

    public static int CoreCount { get; private set; } = Environment.ProcessorCount;

    static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;

    // ── Per-core ratio, MSR 0x1AD ──────────────────────
    public static Task<bool> SetClockRatioAsync(int ratio) {
      return Task.FromResult(SetPerCoreRatiosSync(new[] { ratio }));
    }

    public static Task<bool> SetPerCoreRatiosAsync(int[] ratios) {
      return Task.FromResult(SetPerCoreRatiosSync(ratios));
    }

    static bool SetPerCoreRatiosSync(int[] ratios) {
      if (!OmenXHubDriver.IsAvailable) return false;
      ulong value = 0;
      int count = Math.Min(ratios.Length, 8);
      for (int i = 0; i < count; i++)
        value |= ((ulong)(byte)Clamp(ratios[i], 4, 83)) << (i * 8);
      return OmenXHubDriver.WriteMsr(0x1AD, value);
    }

    // ── Voltage offset, MSR 0x150 ──────────────────────
    static ulong VoltageMsr(int planeId, decimal offset) {
      ulong cmd = planeId switch {
        0 => 0x80000011UL, 1 => 0x80000111UL,
        2 => 0x80000211UL, 3 => 0x80000411UL,
        _ => 0x80000011UL
      };
      int enc = (int)Math.Round((double)offset * 1.024) << 21;
      return (cmd << 32) | (uint)(enc & 0xFFFFFFFF);
    }

    public static Task<bool> SetVoltageOffsetAsync(int planeId, decimal offset) {
      if (!OmenXHubDriver.IsAvailable) return Task.FromResult(false);
      return Task.FromResult(OmenXHubDriver.WriteMsr(0x150, VoltageMsr(planeId, offset)));
    }

    // Batch: accepts same (controlId, offset) tuples the old SDK used
    public static Task<bool> ApplyValuesAsync(IEnumerable<(uint id, decimal value)> values) {
      if (!OmenXHubDriver.IsAvailable) return Task.FromResult(false);
      var list = values.ToList();
      bool ok = true;
      foreach (var v in list) {
        int plane = v.id switch { 24 => 0, 25 => 2, 26 => 1, 27 => 3, _ => 0 };
        ok &= OmenXHubDriver.WriteMsr(0x150, VoltageMsr(plane, v.value));
      }
      return Task.FromResult(ok);
    }

    // ── Power balance, MSR 0x63A / 0x642 ──────────────
    public static Task<bool> SetPowerBalanceAsync(int value) {
      if (!OmenXHubDriver.IsAvailable) return Task.FromResult(false);
      value = Clamp(value, 0, 31);
      bool ok = OmenXHubDriver.WriteMsr(0x63A, (ulong)value);
      ok &= OmenXHubDriver.WriteMsr(0x642, (ulong)(31 - value));
      return Task.FromResult(ok);
    }

    // ── Turbo Boost (MSR 0x1A0 bit 38) ─────────────
    public static Task<bool> SetTurboBoostAsync(bool enable) {
      if (!OmenXHubDriver.IsAvailable) return Task.FromResult(false);
      if (!OmenXHubDriver.ReadMsr(0x1A0, out ulong val)) return Task.FromResult(false);
      if (enable) val &= ~(1UL << 38);
      else val |= 1UL << 38;
      return Task.FromResult(OmenXHubDriver.WriteMsr(0x1A0, val));
    }

    // ── HWP Energy Performance Preference (MSR 0x774) ─
    public static Task<bool> SetHwpEppAsync(int epp) {
      if (!OmenXHubDriver.IsAvailable) return Task.FromResult(false);
      if (!OmenXHubDriver.ReadMsr(0x774, out ulong val)) return Task.FromResult(false);
      val &= ~(0xFFUL << 24);
      val |= ((ulong)(byte)Clamp(epp, 0, 255)) << 24;
      return Task.FromResult(OmenXHubDriver.WriteMsr(0x774, val));
    }

    // ── iGPU Power Limit (MSR 0x621) ──────────────
    public static Task<bool> SetIgpuPowerLimitAsync(int watts) {
      if (!OmenXHubDriver.IsAvailable) return Task.FromResult(false);
      watts = Clamp(watts, 0, 4095);
      ulong value = (ulong)(watts * 8) | (1UL << 15);
      return Task.FromResult(OmenXHubDriver.WriteMsr(0x621, value));
    }

    // ── iGPU Max Ratio (MSR 0x1A2 bits 15:8) ─────
    public static Task<bool> SetIgpuMaxRatioAsync(int ratio) {
      if (!OmenXHubDriver.IsAvailable) return Task.FromResult(false);
      lock (_msrLock) {
        if (!OmenXHubDriver.ReadMsr(0x1A2, out ulong val)) return Task.FromResult(false);
        val &= ~(0xFFUL << 8);
        val |= ((ulong)(byte)Clamp(ratio, 8, 60)) << 8;
        return Task.FromResult(OmenXHubDriver.WriteMsr(0x1A2, val));
      }
    }

    // ── Package C-State limit (MSR 0xE2) ────────────
    public static Task<bool> SetCStateLimitAsync(int limit) {
      if (!OmenXHubDriver.IsAvailable) return Task.FromResult(false);
      if (!OmenXHubDriver.ReadMsr(0xE2, out ulong val)) return Task.FromResult(false);
      val &= ~0xFUL;
      val |= (ulong)(byte)Clamp(limit, 0, 10);
      return Task.FromResult(OmenXHubDriver.WriteMsr(0xE2, val));
    }

    // ── PROCHOT Offset (MSR 0x1A2) ──────────────────
    public static Task<bool> SetProchotOffsetAsync(int offset) {
      if (!OmenXHubDriver.IsAvailable) return Task.FromResult(false);
      lock (_msrLock) {
        if (!OmenXHubDriver.ReadMsr(0x1A2, out ulong val)) return Task.FromResult(false);
        val &= ~(0xFFUL << 16);
        val |= ((ulong)(byte)Clamp(offset, 0, 63)) << 16;
        return Task.FromResult(OmenXHubDriver.WriteMsr(0x1A2, val));
      }
    }
  }
}
