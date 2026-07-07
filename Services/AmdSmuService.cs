// AmdSmuService.cs - AMD SMU 直写（SMU mailbox 协议，替换 AMDRyzenSDK.dll）
// Based on UXTU's RyzenSmu / AMDPawnIO architecture.
// Uses DeviceIoControl → PawnIO driver directly (no PawnIOLib.dll dependency).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace OmenSuperHub.Services {

  /// <summary>
  /// AMD SMU mailbox 直写服务：Curve Optimizer, PPT/TDC/EDC, PBO。
  /// 通过 PawnIO 驱动 + RyzenSMU.bin 直接操作 SMU 寄存器。
  /// </summary>
  public static class AmdSmuService {

    // ─── Public API ───

    public static bool IsAvailable => _available;
    public static string CpuFamilyName => _cpuFamilyName;

    /// <summary>全核 Curve Optimizer 偏移 (-30 ~ +30)</summary>
    public static bool SetCurveOptimizerAllCore(int offset) {
      if (!_available || offset < -30 || offset > 30) return false;
      return RunCommand("set-coall", offset >= 0 ? (uint)offset : (uint)(0x100000 - (-offset)));
    }

    /// <summary>每核 Curve Optimizer 偏移 (ccd=0, core=0-7)</summary>
    public static bool SetCurveOptimizerPerCore(int ccd, int core, int offset) {
      if (!_available || offset < -30 || offset > 30) return false;
      int magnitude = Math.Min(Math.Abs(offset), 0xFFFFF);
      uint encoded = offset < 0
          ? (uint)((0x100000 - magnitude) & 0xFFFFF)
          : (uint)(magnitude & 0xFFFFF);
      uint prefix = (uint)((((ccd << 4) | (0 & 15)) << 4 | (core & 15)) << 20);
      return RunCommand("set-coper", prefix | encoded);
    }

    /// <summary>iGPU Curve Optimizer 偏移 (-30 ~ +30)：复用 set-cogfx mailbox，编码与 set-coall 一致</summary>
    // ponytail: 仅有 APU/HX 系链注册 set-cogfx；AM5 桌面平台 SR3 DRG/GRN 无此命令，RunCommand 会返回 false（安全降级）。
    public static bool SetCurveOptimizerIGpu(int offset) {
      if (!_available || offset < -30 || offset > 30) return false;
      return RunCommand("set-cogfx", offset >= 0 ? (uint)offset : (uint)(0x100000 - (-offset)));
    }

    /// <summary>设置 PPT Limit (mW)</summary>
    public static bool SetPptLimit(uint mw) {
      if (!_available) return false;
      return RunCommand("ppt-limit", mw);
    }

    /// <summary>设置 TDC Limit (mA)</summary>
    public static bool SetTdcLimit(uint ma) {
      if (!_available) return false;
      return RunCommand("tdc-limit", ma);
    }

    /// <summary>设置 EDC Limit (mA)</summary>
    public static bool SetEdcLimit(uint ma) {
      if (!_available) return false;
      return RunCommand("edc-limit", ma);
    }

    /// <summary>设置温度限制 (C)</summary>
    public static bool SetTctlTemp(uint temp) {
      if (!_available) return false;
      return RunCommand("tctl-temp", temp);
    }

    /// <summary>STAPM 持续功耗限制 (mW)</summary>
    public static bool SetStapmLimit(uint mw) {
      if (!_available) return false;
      return RunCommand("stapm-limit", mw);
    }

    /// <summary>Fast PPT 峰值功耗限制 (mW)</summary>
    public static bool SetFastLimit(uint mw) {
      if (!_available) return false;
      return RunCommand("fast-limit", mw);
    }

    /// <summary>Slow PPT 平均功耗限制 (mW)</summary>
    public static bool SetSlowLimit(uint mw) {
      if (!_available) return false;
      return RunCommand("slow-limit", mw);
    }

    /// <summary>PBO Scalar 设置</summary>
    public static bool SetPboScalar(uint scalar) {
      if (!_available) return false;
      return RunCommand("pbo-scalar", scalar);
    }

    /// <summary>STAPM 持续时间窗口 (秒)</summary>
    public static bool SetStapmTime(uint seconds) {
      if (!_available) return false;
      return RunCommand("stapm-time", seconds);
    }

    /// <summary>Slow PPT 持续时间窗口 (秒)</summary>
    public static bool SetSlowTime(uint seconds) {
      if (!_available) return false;
      return RunCommand("slow-time", seconds);
    }

    /// <summary>CPU VRM 持续电流限制 TDC (mA)</summary>
    public static bool SetVrmCurrent(uint ma) {
      if (!_available) return false;
      return RunCommand("vrm-current", ma);
    }

    /// <summary>SoC VRM 持续电流限制 TDC (mA)</summary>
    public static bool SetVrmSocCurrent(uint ma) {
      if (!_available) return false;
      return RunCommand("vrmsoc-current", ma);
    }

    /// <summary>CPU VRM 最大电流限制 EDC (mA)</summary>
    public static bool SetVrmMaxCurrent(uint ma) {
      if (!_available) return false;
      return RunCommand("vrmmax-current", ma);
    }

    /// <summary>SoC VRM 最大电流限制 EDC (mA)</summary>
    public static bool SetVrmSocMaxCurrent(uint ma) {
      if (!_available) return false;
      return RunCommand("vrmsocmax-current", ma);
    }

    /// <summary>皮肤温度功耗限制 (mW)</summary>
    public static bool SetSkinTempLimit(uint mw) {
      if (!_available) return false;
      return RunCommand("skin-temp-limit", mw);
    }

    /// <summary>APU 皮肤温度限制 (°C, 乘以 256 后发送)</summary>
    public static bool SetApuSkinTemp(uint tempC) {
      if (!_available) return false;
      return RunCommand("apu-skin-temp", tempC * 256);
    }

    /// <summary>dGPU 皮肤温度限制 (°C, 乘以 256 后发送)</summary>
    public static bool SetDgpuSkinTemp(uint tempC) {
      if (!_available) return false;
      return RunCommand("dgpu-skin-temp", tempC * 256);
    }

    /// <summary>APU Slow 功耗限制 — A+A 平台功耗分配 (mW)</summary>
    public static bool SetApuSlowLimit(uint mw) {
      if (!_available) return false;
      return RunCommand("apu-slow-limit", mw);
    }

    /// <summary>iGPU 时钟覆盖 (MHz, 200-2200)</summary>
    public static bool SetGfxClk(uint mhz) {
      if (!_available || mhz < 200 || mhz > 2200) return false;
      return RunCommand("gfx-clk", mhz);
    }

    // ─── Private state ───

    static bool _available;
    static string _cpuFamilyName = "Unknown";
    static SmuDevice _smu;

    static AmdSmuService() {
      try {
        // Detect CPU
        if (!DetectCpu()) return;
        // Load PawnIO module
        byte[] module = LoadEmbeddedResource("OmenSuperHub.Resources.RyzenSMU.bin");
        if (module == null) return;
        _smu = SmuDevice.Create(module);
        if (_smu == null || !_smu.IsLoaded) return;
        // Set SMU addresses
        SmuMailbox.SetAddresses(_cpuFamilyName);
        _available = true;
        Logger.Info($"AmdSmuService initialized: {_cpuFamilyName}");
      } catch (Exception ex) {
        Logger.Error($"AmdSmuService init failed: {ex.Message}");
      }
    }

    public static void Shutdown() {
      _smu?.Dispose();
      _smu = null;
      _available = false;
    }

    static bool RunCommand(string name, uint value) {
      try {
        uint[] args = new uint[6];
        args[0] = value;
        return SmuMailbox.SendCommand(name, ref args);
      } catch { return false; }
    }

    static bool DetectCpu() {
      try {
        using (var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor"))
        using (var results = searcher.Get()) {
          foreach (ManagementObject obj in results) {
            string name = obj["Name"]?.ToString() ?? "";
            _cpuFamilyName = ClassifyCpu(name);
            if (_cpuFamilyName != "Unknown") return true;
          }
        }
      } catch { }
      return false;
    }

    static string ClassifyCpu(string name) {
      // Dragon Range (7945HX / 7940HX / 7845HX / 7745HX / 7645HX)
      if (name.Contains("7945") || name.Contains("7940") || name.Contains("7845") ||
          name.Contains("7745") || name.Contains("7645") || name.Contains("Dragon"))
        return "DragonRange";
      // Fire Range (9955HX / 9850HX etc.)
      if (name.Contains("9955") || name.Contains("9850") || name.Contains("Fire"))
        return "FireRange";
      // Phoenix Point (7840HS/7940HS/7640HS/7840U/7940U)
      if (name.Contains("7840") || name.Contains("7940") || name.Contains("7640") ||
          (name.Contains("7x40") && !name.Contains("7x45")))
        return "PhoenixPoint";
      // Hawk Point (8845HS/8840HS/8645HS)
      if (name.Contains("8845") || name.Contains("8840") || name.Contains("8645") ||
          name.Contains("8540"))
        return "HawkPoint";
      // Strix Point (AI 9 HX 370 / AI 9 365)
      if (name.Contains("HX 370") || name.Contains("HX 365") || name.Contains("AI 9") ||
          name.Contains("Strix"))
        return "StrixPoint";
      // Rembrandt (6800H/6900HX/6600H)
      if (name.Contains("6900") || name.Contains("6800") || name.Contains("6600"))
        return "Rembrandt";
      // Cezanne / Barcelo (5800H/5900HX/5600H)
      if (name.Contains("5900") || name.Contains("5800") || name.Contains("5600") ||
          name.Contains("5400"))
        return "Cezanne";
      // Renoir / Lucienne (4800H/4600H)
      if (name.Contains("4800") || name.Contains("4600"))
        return "Renoir";
      // Raphael desktop (7950X/7900X/7700X/7600X)
      if (name.Contains("7950X") || name.Contains("7900X") || name.Contains("7700X") || name.Contains("7600X"))
        return "Raphael";
      // Granite Ridge (9950X/9900X)
      if (name.Contains("9950X") || name.Contains("9900X"))
        return "GraniteRidge";
      // Mendocino (low-end mobile)
      if (name.Contains("7320") || name.Contains("7520") || name.Contains("Mendocino"))
        return "Mendocino";
      return "Unknown";
    }

    static byte[] LoadEmbeddedResource(string resourceName) {
      try {
        using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)) {
          if (s == null) return null;
          using (var ms = new MemoryStream()) { s.CopyTo(ms); return ms.ToArray(); }
        }
      } catch { return null; }
    }
  }

  // ═══════════════════════════════════════════════════════════════
  // SMU Mailbox Protocol + Command Tables
  // ═══════════════════════════════════════════════════════════════

  internal static class SmuMailbox {

    // SMU register addresses (set per socket)
    public static uint MP1_ADDR_MSG, MP1_ADDR_RSP, MP1_ADDR_ARG;
    public static uint PSMU_ADDR_MSG, PSMU_ADDR_RSP, PSMU_ADDR_ARG;

    // Command table: (name, use_MP1, command_id)
    static List<(string, bool, uint)> _commands;

    const int POLL_LIMIT = 8192;
    static readonly Mutex _pciMutex = CreateGlobalMutex("Global\\Access_PCI");

    public static void SetAddresses(string family) {
      switch (family) {
        case "PhoenixPoint": case "HawkPoint": case "Rembrandt":
        case "StrixPoint": case "Mendocino":
          Socket_FT6_FP7_FP8(family == "StrixPoint"); break;
        case "DragonRange": case "FireRange":
        case "Raphael": case "GraniteRidge":
          Socket_AM5_V1(); break;
        case "Cezanne": case "Renoir":
          Socket_FP6_AM4(); break;
      }
    }

    static void Socket_FT6_FP7_FP8(bool isStrix) {
      if (isStrix) {
        MP1_ADDR_MSG = 0x3b10928; MP1_ADDR_RSP = 0x3b10978; MP1_ADDR_ARG = 0x3b10998;
      } else {
        MP1_ADDR_MSG = 0x3B10528; MP1_ADDR_RSP = 0x3B10578; MP1_ADDR_ARG = 0x3B10998;
      }
      PSMU_ADDR_MSG = 0x03B10a20; PSMU_ADDR_RSP = 0x03B10a80; PSMU_ADDR_ARG = 0x03B10a88;

      _commands = new List<(string, bool, uint)> {
        ("set-coall", true, 0x4c),  ("set-coall", false, 0x5d),
        ("set-coper", true, 0x4b),  ("set-coper", false, 0x53),
        ("set-cogfx", false, 0xb7),
        ("pbo-scalar", false, 0x3e), ("get-pbo-scalar", false, 0x0f),
        ("tctl-temp", true, 0x19),
        ("stapm-limit", true, 0x14), ("stapm-limit", false, 0x31),
        ("fast-limit", true, 0x15), ("fast-limit", false, 0x32),
        ("slow-limit", true, 0x16), ("slow-limit", false, 0x33),
        // Boost duration windows
        ("stapm-time", true, 0x18),
        ("slow-time", true, 0x17),
        // VRM current limits (TDC/EDC)
        ("vrm-current", true, 0x1a),
        ("vrmsoc-current", true, 0x1b),
        ("vrmmax-current", true, 0x1c),
        ("vrmsocmax-current", true, 0x1d),
        // Skin temperature limits
        ("skin-temp-limit", true, 0x4a),
        ("apu-skin-temp", true, 0x33),
        ("dgpu-skin-temp", true, 0x34),
        // A+A platform power sharing
        ("apu-slow-limit", true, 0x23),
        // iGPU clock override (PSMU mailbox)
        ("gfx-clk", false, 0x89),
      };
    }

    static void Socket_AM5_V1() {
      MP1_ADDR_MSG = 0x3B10530; MP1_ADDR_RSP = 0x3B1057C; MP1_ADDR_ARG = 0x3B109C4;
      PSMU_ADDR_MSG = 0x03B10524; PSMU_ADDR_RSP = 0x03B10570; PSMU_ADDR_ARG = 0x03B10A40;
      _commands = new List<(string, bool, uint)> {
        ("set-coall", true, 0x36),  ("set-coall", false, 0x7),
        ("set-coper", true, 0x35),  ("set-coper", false, 0x6),
        ("set-cogfx", false, 0xA7),
        ("pbo-scalar", false, 0x5b), ("get-pbo-scalar", false, 0x6d),
        ("tctl-temp", true, 0x3f), ("tctl-temp", false, 0x59),
        ("ppt-limit", true, 0x3e), ("ppt-limit", false, 0x56),
        ("tdc-limit", true, 0x3c), ("tdc-limit", false, 0x57),
        ("edc-limit", true, 0x3d), ("edc-limit", false, 0x58),
        ("stapm-limit", true, 0x4f), ("fast-limit", true, 0x3e),
        ("slow-limit", true, 0x5f), ("slow-limit", false, 0xcb),
        // Boost duration windows (AM5)
        ("stapm-time", true, 0x4e),
        ("slow-time", true, 0x60),
      };
    }

    static void Socket_FP6_AM4() {
      MP1_ADDR_MSG = 0x3B10528; MP1_ADDR_RSP = 0x3B10564; MP1_ADDR_ARG = 0x3B10998;
      PSMU_ADDR_MSG = 0x03B10A20; PSMU_ADDR_RSP = 0x03B10A80; PSMU_ADDR_ARG = 0x03B10A88;
      _commands = new List<(string, bool, uint)> {
        ("set-coall", true, 0x55),  ("set-coall", false, 0xB1),
        ("set-coper", true, 0x54),  ("set-coper", false, 0x52),
        ("set-cogfx", false, 0x53),
        ("pbo-scalar", true, 0x49), ("pbo-scalar", false, 0x3f),
        ("get-pbo-scalar", false, 0x0f),
        ("tctl-temp", true, 0x19),
        ("stapm-limit", true, 0x14), ("stapm-limit", false, 0x31),
        ("fast-limit", true, 0x15), ("fast-limit", false, 0x32),
        ("slow-limit", true, 0x16), ("slow-limit", false, 0x33),
        // Boost duration windows
        ("stapm-time", true, 0x18),
        ("slow-time", true, 0x17),
        // VRM current limits (TDC/EDC)
        ("vrm-current", true, 0x1a),
        ("vrmsoc-current", true, 0x1b),
        ("vrmmax-current", true, 0x1c),
        ("vrmsocmax-current", true, 0x1d),
        // Skin temperature limits (Renoir/Cezanne IDs)
        ("skin-temp-limit", true, 0x4a),
        ("apu-skin-temp", true, 0x38),
        ("dgpu-skin-temp", true, 0x39),
        // iGPU clock override (PSMU mailbox)
        ("gfx-clk", false, 0x89),
      };
    }

    public static bool SendCommand(string name, ref uint[] args) {
      if (_commands == null) return false;
      var matches = _commands.Where(c => c.Item1 == name).ToList();
      if (matches.Count == 0) return false;

      if (!WaitMutex(_pciMutex, 10)) return false;
      try {
        foreach (var cmd in matches) {
          uint msgAddr = cmd.Item2 ? MP1_ADDR_MSG : PSMU_ADDR_MSG;
          uint rspAddr = cmd.Item2 ? MP1_ADDR_RSP : PSMU_ADDR_RSP;
          uint argAddr = cmd.Item2 ? MP1_ADDR_ARG : PSMU_ADDR_ARG;
          if (!ExecuteMailbox(msgAddr, rspAddr, argAddr, cmd.Item3, ref args))
            return false;
        }
        return true;
      } finally { SafeReleaseMutex(_pciMutex); }
    }

    static bool ExecuteMailbox(uint msgAddr, uint rspAddr, uint argAddr, uint cmdId, ref uint[] args) {
      // Wait idle
      if (!WaitReg(rspAddr, 0, true)) return false;
      // Clear response
      if (!SmuReg.Write32(rspAddr, 0)) return false;
      // Write args
      for (int i = 0; i < 6; i++) {
        uint val = (args != null && i < args.Length) ? args[i] : 0;
        if (!SmuReg.Write32(argAddr + (uint)(i * 4), val)) return false;
      }
      // Send command
      if (!SmuReg.Write32(msgAddr, cmdId)) return false;
      // Wait for response
      if (!WaitReg(rspAddr, 0, false)) return false;
      // Read response status
      if (!SmuReg.Read32(rspAddr, out uint rsp)) return false;
      if (rsp > 0xFF) return false;
      return rsp == 0x01; // OK
    }

    static bool WaitReg(uint addr, uint expected, bool wantsEq) {
      for (int i = 0; i < POLL_LIMIT; i++) {
        if (SmuReg.Read32(addr, out uint val)) {
          if (wantsEq && val == expected) return true;
          if (!wantsEq && val != expected) return true;
        }
        if ((i & 0x3FF) == 0x3FF) Thread.Sleep(1);
      }
      return !wantsEq;
    }

    static Mutex CreateGlobalMutex(string name) {
      try {
        var sid = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
        var rule = new MutexAccessRule(sid, MutexRights.FullControl, AccessControlType.Allow);
        var sec = new MutexSecurity(); sec.AddAccessRule(rule);
        return new Mutex(false, name, out _, sec);
      } catch { return null; }
    }

    static bool WaitMutex(Mutex m, int timeoutMs) {
      try { return m.WaitOne(timeoutMs, false); }
      catch (AbandonedMutexException) { return true; }
      catch { return false; }
    }

    static void SafeReleaseMutex(Mutex m) { try { m.ReleaseMutex(); } catch { } }
  }

  // ═══════════════════════════════════════════════════════════════
  // SMU Register R/W via PawnIO ioctl
  // ═══════════════════════════════════════════════════════════════

  static class SmuReg {
    const string IOCTL_READ = "ioctl_read_smu_register";
    const string IOCTL_WRITE = "ioctl_write_smu_register";

    static SmuDevice _device;

    public static void SetDevice(SmuDevice d) { _device = d; }

    public static bool Read32(uint reg, out uint value) {
      value = 0;
      if (_device == null) return false;
      long[] inBuf = { unchecked((long)reg) };
      long[] outBuf = new long[1];
      if (_device.ExecuteHr(IOCTL_READ, inBuf, 1, outBuf, 1, out _) != 0)
        return false;
      value = unchecked((uint)outBuf[0]);
      return true;
    }

    public static bool Write32(uint reg, uint value) {
      if (_device == null) return false;
      long[] inBuf = { unchecked((long)reg), unchecked((long)value) };
      return _device.ExecuteHr(IOCTL_WRITE, inBuf, 2, null, 0, out _) == 0;
    }
  }

  // ═══════════════════════════════════════════════════════════════
  // DeviceIoControl-based PawnIO driver interface
  // (Adapted from UXTU's AMDPawnIO)
  // ═══════════════════════════════════════════════════════════════

  sealed class SmuDevice : IDisposable {
    const string DevicePath = @"\\?\GLOBALROOT\Device\PawnIO";
    const string OldDevicePath = @"\\.\PawnIO";
    const uint ShareReadWrite = 0x00000003;
    const uint DeviceType = 41394u << 16;
    const uint IoctlExecuteFn = 0x841 << 2;
    const uint IoctlLoadBinary = 0x821 << 2;
    const int E_HANDLE = unchecked((int)0x80070006);

    enum ControlCode : uint {
      LoadBinary = DeviceType | IoctlLoadBinary,
      Execute = DeviceType | IoctlExecuteFn
    }

    enum FileAccess : uint { GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000 }
    enum CreationDisposition : uint { OPEN_EXISTING = 3 }

    SafeFileHandle _handle;

    public bool IsLoaded => _handle != null && !_handle.IsInvalid && !_handle.IsClosed;

    SmuDevice(SafeFileHandle h) { _handle = h; }

    public static SmuDevice Create(byte[] moduleBytes) {
      IntPtr raw = CreateFile(DevicePath,
          FileAccess.GENERIC_READ | FileAccess.GENERIC_WRITE, ShareReadWrite,
          IntPtr.Zero, CreationDisposition.OPEN_EXISTING, 0, IntPtr.Zero);
      if (raw == IntPtr.Zero || raw.ToInt64() == -1) {
        raw = CreateFile(OldDevicePath,
            FileAccess.GENERIC_READ | FileAccess.GENERIC_WRITE, ShareReadWrite,
            IntPtr.Zero, CreationDisposition.OPEN_EXISTING, 0, IntPtr.Zero);
        if (raw == IntPtr.Zero || raw.ToInt64() == -1) return null;
      }
      try {
        if (!DeviceIoControl(raw, ControlCode.LoadBinary, moduleBytes, (uint)moduleBytes.Length,
            null, 0, out _, IntPtr.Zero)) {
          CloseHandle(raw); return null;
        }
        var dev = new SmuDevice(new SafeFileHandle(raw, true));
        SmuReg.SetDevice(dev);
        return dev;
      } catch { try { CloseHandle(raw); } catch { } return null; }
    }

    public int ExecuteHr(string name, long[] inBuf, uint inSize, long[] outBuf, uint outSize, out uint returnSize) {
      if (!IsLoaded) { returnSize = 0; return E_HANDLE; }
      byte[] request = BuildRequest(name, inBuf ?? Array.Empty<long>(), inSize);
      byte[] response = new byte[(outBuf?.Length ?? 0) * 8];
      if (!DeviceIoControl(_handle, ControlCode.Execute, request, (uint)request.Length,
          response, (uint)response.Length, out uint bytesReturned, IntPtr.Zero)) {
        returnSize = 0; return Marshal.GetHRForLastWin32Error();
      }
      if (outBuf != null && bytesReturned > 0)
        Buffer.BlockCopy(response, 0, outBuf, 0, Math.Min((int)bytesReturned, outBuf.Length * 8));
      returnSize = bytesReturned / 8;
      return 0;
    }

    public void Dispose() { if (IsLoaded) _handle.Close(); }

    static byte[] BuildRequest(string fn, long[] args, uint argCount) {
      byte[] buf = new byte[32 + argCount * 8];
      byte[] nameB = System.Text.Encoding.ASCII.GetBytes(fn);
      Buffer.BlockCopy(nameB, 0, buf, 0, Math.Min(31, nameB.Length));
      if (argCount > 0) Buffer.BlockCopy(args, 0, buf, 32, (int)argCount * 8);
      return buf;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool DeviceIoControl(SafeFileHandle h, ControlCode code, byte[] inBuf, uint inSize,
        byte[] outBuf, uint outSize, out uint returned, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool DeviceIoControl(IntPtr h, ControlCode code, byte[] inBuf, uint inSize,
        byte[] outBuf, uint outSize, out uint returned, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    static extern IntPtr CreateFile(string path, FileAccess access, uint share,
        IntPtr sec, CreationDisposition disp, uint flags, IntPtr tmpl);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CloseHandle(IntPtr h);
  }
}
