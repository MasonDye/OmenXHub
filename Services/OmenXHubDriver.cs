// OmenXHubDriver.cs — user-mode C# wrapper for \\.\OmenXHub kernel driver
// Replaces both IntelMsr (MSR) and SmuReg (PCI MMIO) from PawnIO.
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace OmenSuperHub.Services;

/// <summary>Wraps the OmenXHub kernel driver for MSR and PCI MMIO access.</summary>
internal static class OmenXHubDriver {
  const string DevicePath = @"\\.\OmenXHub";

  const uint IOCTL_TYPE = 0x8000;
  const uint FILE_ANY_ACCESS = 0;
  const uint METHOD_BUFFERED = 0;

  // ── IOCTL codes ────────────────────────────────────────────────
  static readonly uint IOCTL_READ_MSR  = CTL_CODE(IOCTL_TYPE, 0x800);
  static readonly uint IOCTL_WRITE_MSR = CTL_CODE(IOCTL_TYPE, 0x801);
  static readonly uint IOCTL_READ_PCI  = CTL_CODE(IOCTL_TYPE, 0x802);
  static readonly uint IOCTL_WRITE_PCI = CTL_CODE(IOCTL_TYPE, 0x803);

  [StructLayout(LayoutKind.Sequential, Pack = 1)]
  struct MsrRw {
    public uint Index;
    public ulong Value;
  }

  [StructLayout(LayoutKind.Sequential, Pack = 1)]
  struct PciRw {
    public uint Bus;
    public uint Device;
    public uint Function;
    public uint Offset;
    public uint Value;
  }

  static volatile IntPtr _handle = InvalidHandle;
  static readonly object _lock = new();
  static readonly IntPtr InvalidHandle = new(-1);

  static uint CTL_CODE(uint deviceType, uint function) =>
      (deviceType << 16) | (function << 2) | FILE_ANY_ACCESS;

  public static bool IsAvailable {
    get {
      if (_handle != InvalidHandle) return true;
      TryOpen();
      return _handle != InvalidHandle;
    }
  }

  public static void TryOpen() {
    if (_handle != InvalidHandle) return;
    lock (_lock) {
      if (_handle != InvalidHandle) return;
      _handle = CreateFile(
          DevicePath,
          0xC0000000, // GENERIC_READ | GENERIC_WRITE
          3,          // FILE_SHARE_READ | FILE_SHARE_WRITE
          IntPtr.Zero,
          3,          // OPEN_EXISTING
          0,
          IntPtr.Zero);
      if (_handle == InvalidHandle || _handle == IntPtr.Zero) {
        _handle = InvalidHandle;
      }
    }
  }

  public static void Close() {
    lock (_lock) {
      if (_handle != InvalidHandle && _handle != IntPtr.Zero) {
        CloseHandle(_handle);
        _handle = InvalidHandle;
      }
    }
  }

  // ── MSR ────────────────────────────────────────────────────────

  public static bool ReadMsr(uint index, out ulong value) {
    value = 0;
    if (_handle == InvalidHandle) return false;
    var req = new MsrRw { Index = index };
    byte[] inBuf = StructToBytes(req);
    byte[] outBuf = new byte[Marshal.SizeOf<MsrRw>()];
    if (!DeviceIoControl(_handle, IOCTL_READ_MSR, inBuf, (uint)inBuf.Length,
                         outBuf, (uint)outBuf.Length, out _, IntPtr.Zero))
      return false;
    value = BytesToStruct<MsrRw>(outBuf).Value;
    return true;
  }

  public static bool WriteMsr(uint index, ulong value) {
    if (_handle == InvalidHandle) return false;
    var req = new MsrRw { Index = index, Value = value };
    byte[] buf = StructToBytes(req);
    return DeviceIoControl(_handle, IOCTL_WRITE_MSR, buf, (uint)buf.Length,
                           null, 0, out _, IntPtr.Zero);
  }

  // ── PCI MMIO ───────────────────────────────────────────────────

  public static bool ReadPci(uint bus, uint dev, uint func, uint offset, out uint value) {
    value = 0;
    if (_handle == InvalidHandle) return false;
    var req = new PciRw { Bus = bus, Device = dev, Function = func, Offset = offset };
    byte[] inBuf = StructToBytes(req);
    byte[] outBuf = new byte[Marshal.SizeOf<PciRw>()];
    if (!DeviceIoControl(_handle, IOCTL_READ_PCI, inBuf, (uint)inBuf.Length,
                         outBuf, (uint)outBuf.Length, out _, IntPtr.Zero))
      return false;
    value = BytesToStruct<PciRw>(outBuf).Value;
    return true;
  }

  public static bool WritePci(uint bus, uint dev, uint func, uint offset, uint value) {
    if (_handle == InvalidHandle) return false;
    var req = new PciRw { Bus = bus, Device = dev, Function = func, Offset = offset, Value = value };
    byte[] buf = StructToBytes(req);
    return DeviceIoControl(_handle, IOCTL_WRITE_PCI, buf, (uint)buf.Length,
                           null, 0, out _, IntPtr.Zero);
  }

  // ── Helpers ────────────────────────────────────────────────────

  static byte[] StructToBytes<T>(T s) where T : struct {
    byte[] b = new byte[Marshal.SizeOf<T>()];
    GCHandle h = GCHandle.Alloc(b, GCHandleType.Pinned);
    try { Marshal.StructureToPtr(s, h.AddrOfPinnedObject(), false); return b; }
    finally { h.Free(); }
  }

  static T BytesToStruct<T>(byte[] b) where T : struct {
    GCHandle h = GCHandle.Alloc(b, GCHandleType.Pinned);
    try { return Marshal.PtrToStructure<T>(h.AddrOfPinnedObject()); }
    finally { h.Free(); }
  }

  // ── P/Invoke ───────────────────────────────────────────────────

  [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
  static extern IntPtr CreateFile(string name, uint access, uint share,
      IntPtr sec, uint creationDisposition, uint flags, IntPtr template);

  [DllImport("kernel32.dll", SetLastError = true)]
  static extern bool DeviceIoControl(IntPtr h, uint code,
      byte[] inBuf, uint inLen, byte[] outBuf, uint outLen,
      out uint returned, IntPtr overlapped);

  [DllImport("kernel32.dll", SetLastError = true)]
  static extern bool CloseHandle(IntPtr h);
}
