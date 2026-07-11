using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace LibreHardwareMonitor.PawnIo;

// ponytail: replaced DllImport("PawnIOLib") with direct DeviceIoControl to \\?\GLOBALROOT\Device\PawnIO.
// Eliminates the runtime dependency on PawnIOLib.dll — only the PawnIO kernel driver needs to be installed.
public unsafe class PawnIo
{
    private const uint DEVICE_TYPE = 41394u << 16;
    private const int FN_NAME_LENGTH = 32;
    private const uint IOCTL_PIO_EXECUTE_FN = 0x841 << 2;
    private const uint IOCTL_PIO_LOAD_BINARY = 0x821 << 2;

    private const uint GENERIC_READ_WRITE = 0x80000000u | 0x40000000u; // GENERIC_READ | GENERIC_WRITE
    private const uint FILE_SHARE_READ_WRITE = 0x01 | 0x02;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;
    private static readonly IntPtr INVALID_HANDLE = new IntPtr(-1);

    private IntPtr _handle;

    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFile(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode,
        IntPtr lpSecurityAttributes, uint dwCreationDisposition,
        uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        IntPtr hDevice, uint dwIoControlCode,
        byte* lpInBuffer, uint nInBufferSize,
        byte* lpOutBuffer, uint nOutBufferSize,
        uint* lpBytesReturned, IntPtr lpOverlapped);

    [DllImport("kernel32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    // ponytail: pawnio_version DllImport kept for version detection only — the critical
    // hardware monitoring path (LoadModuleFromResource / Execute) uses direct DeviceIoControl.
    [DllImport("PawnIOLib", ExactSpelling = true, PreserveSig = false)]
    private static extern void pawnio_version(out uint version);

    static PawnIo()
    {
        using (RegistryKey subKey = Registry.LocalMachine.OpenSubKey(
                   @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO"))
        {
            if (subKey != null && System.Version.TryParse(subKey.GetValue("DisplayVersion") as string, out System.Version ver))
            {
                _registryVersion = ver;
                return;
            }
        }

        using (RegistryKey registryKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
        {
            using (RegistryKey subKeyWow64 = registryKey.OpenSubKey(
                       @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO"))
            {
                if (subKeyWow64 != null &&
                    System.Version.TryParse(subKeyWow64.GetValue("DisplayVersion") as string, out System.Version ver))
                {
                    _registryVersion = ver;
                }
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether PawnIO is installed on the system (registry key present).
    /// </summary>
    public static bool IsInstalled => _registryVersion is not null;

    /// <summary>
    /// Gets the installation path of PawnIO, if installed on the system.
    /// </summary>
    public static string InstallPath
    {
        get
        {
            if ((Registry.GetValue(
                     @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO",
                     "InstallLocation", null) ??
                 Registry.GetValue(
                     @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\PawnIO",
                     "Install_Dir", null) ??
                 Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) +
                 Path.DirectorySeparatorChar + "PawnIO") is string
                {
                    Length: > 0
                } path)
            {
                if (Directory.Exists(path))
                    return path;
            }

            return null;
        }
    }

    private static readonly Version _registryVersion;

    /// <summary>
    /// Retrieves the version of the installed PawnIO. Tries the native DLL call first,
    /// falls back to the registry DisplayVersion if PawnIOLib.dll is unavailable.
    /// </summary>
    public static System.Version Version()
    {
        try
        {
            pawnio_version(out uint v);
            return new System.Version((int)((v >> 16) & 0xFF),
                               (int)((v >> 8) & 0xFF),
                               (int)(v & 0xFF),
                               0);
        }
        catch
        {
            // PawnIOLib.dll not available, use registry.
        }

        return _registryVersion;
    }

    /// <summary>
    /// Gets a value indicating whether the underlying device handle is open and valid.
    /// </summary>
    public bool IsLoaded => _handle != IntPtr.Zero && _handle != INVALID_HANDLE;

    public void Close()
    {
        if (_handle != IntPtr.Zero && _handle != INVALID_HANDLE)
        {
            CloseHandle(_handle);
            _handle = IntPtr.Zero;
        }
    }

    public static PawnIo LoadModuleFromResource(Assembly assembly, string resourceName)
    {
        var pawnIO = new PawnIo();

        IntPtr handle = CreateFile(@"\\?\GLOBALROOT\Device\PawnIO",
                                   GENERIC_READ_WRITE,
                                   FILE_SHARE_READ_WRITE,
                                   IntPtr.Zero,
                                   OPEN_EXISTING,
                                   FILE_ATTRIBUTE_NORMAL,
                                   IntPtr.Zero);

        if (handle == INVALID_HANDLE || handle == IntPtr.Zero)
            return pawnIO;

        using Stream stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            CloseHandle(handle);
            return pawnIO;
        }

        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        byte[] bin = memory.ToArray();

        fixed (byte* pIn = bin)
        {
            if (DeviceIoControl(handle, (uint)ControlCode.LoadBinary, pIn, (uint)bin.Length,
                                null, 0, null, IntPtr.Zero))
            {
                pawnIO._handle = handle;
                return pawnIO;
            }
        }

        CloseHandle(handle);
        return pawnIO;
    }

    public long[] Execute(string name, long[] input, int outLength)
    {
        if (!IsLoaded)
            return new long[outLength];

        byte[] output = new byte[outLength * sizeof(long)];
        byte[] totalInput = new byte[(input.Length * sizeof(long)) + FN_NAME_LENGTH];
        Buffer.BlockCopy(Encoding.ASCII.GetBytes(name), 0, totalInput, 0,
                         Math.Min(FN_NAME_LENGTH - 1, name.Length));
        Buffer.BlockCopy(input, 0, totalInput, FN_NAME_LENGTH, input.Length * sizeof(long));

        uint read = 0;

        fixed (byte* pIn = totalInput, pOut = output)
        {
            if (DeviceIoControl(_handle, (uint)ControlCode.Execute, pIn, (uint)totalInput.Length,
                                pOut, (uint)output.Length, &read, IntPtr.Zero))
            {
                long[] outp = new long[read / sizeof(long)];
                Buffer.BlockCopy(output, 0, outp, 0, (int)read);
                return outp;
            }
        }

        return new long[outLength];
    }

    public int ExecuteHr(string name, long[] inBuffer, uint inSize, long[] outBuffer, uint outSize,
                         out uint returnSize)
    {
        if (inBuffer.Length < inSize)
            throw new ArgumentOutOfRangeException(nameof(inSize));

        if (outBuffer.Length < outSize)
            throw new ArgumentOutOfRangeException(nameof(outSize));

        if (!IsLoaded)
        {
            returnSize = 0;
            return 0;
        }

        uint read = 0;

        byte[] output = new byte[outSize * sizeof(long)];
        byte[] totalInput = new byte[(inSize * sizeof(long)) + FN_NAME_LENGTH];
        Buffer.BlockCopy(Encoding.ASCII.GetBytes(name), 0, totalInput, 0,
                         Math.Min(FN_NAME_LENGTH - 1, name.Length));
        Buffer.BlockCopy(inBuffer, 0, totalInput, FN_NAME_LENGTH, inBuffer.Length * sizeof(long));

        fixed (byte* pIn = totalInput, pOut = output)
        {
            if (DeviceIoControl(_handle, (uint)ControlCode.Execute, pIn, (uint)totalInput.Length,
                                pOut, (uint)output.Length, &read, IntPtr.Zero))
            {
                Buffer.BlockCopy(output, 0, outBuffer, 0,
                                 Math.Min((int)read, outBuffer.Length * sizeof(long)));
                returnSize = read / sizeof(long);
                return 0;
            }
        }

        returnSize = 0;
        return Marshal.GetHRForLastWin32Error();
    }

    private enum ControlCode : uint
    {
        LoadBinary = DEVICE_TYPE | IOCTL_PIO_LOAD_BINARY,
        Execute = DEVICE_TYPE | IOCTL_PIO_EXECUTE_FN
    }
}
