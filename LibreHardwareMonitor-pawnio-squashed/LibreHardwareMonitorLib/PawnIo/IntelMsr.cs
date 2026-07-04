using LibreHardwareMonitor.Hardware;

namespace LibreHardwareMonitor.PawnIo;

public class IntelMsr
{
    private readonly long[] _inArray = new long[1];
    private readonly long[] _writeArray = new long[2];
    private readonly PawnIo _pawnIO = PawnIo.LoadModuleFromResource(typeof(IntelMsr).Assembly, $"{nameof(LibreHardwareMonitor)}.Resources.PawnIo.IntelMSR.bin");

    public bool ReadMsr(uint index, out ulong value)
    {
        _inArray[0] = index;
        value = 0;
        try
        {
            long[] outArray = _pawnIO.Execute("ioctl_read_msr", _inArray, 1);
            value = (ulong)outArray[0];
        }
        catch
        {
            return false;
        }

        return true;
    }

    public bool ReadMsr(uint index, out uint eax, out uint edx)
    {
        _inArray[0] = index;
        eax = 0;
        edx = 0;
        try
        {
            long[] outArray = _pawnIO.Execute("ioctl_read_msr", _inArray, 1);
            eax = (uint)outArray[0];
            edx = (uint)(outArray[0] >> 32);
        }
        catch
        {
            return false;
        }

        return true;
    }

    public bool ReadMsr(uint index, out uint eax, out uint edx, GroupAffinity affinity)
    {
        GroupAffinity previousAffinity = ThreadAffinity.Set(affinity);
        bool result = ReadMsr(index, out eax, out edx);
        ThreadAffinity.Set(previousAffinity);
        return result;
    }

    public bool WriteMsr(uint index, ulong value)
    {
        _writeArray[0] = index;
        _writeArray[1] = (long)value;
        try
        {
            _pawnIO.Execute("ioctl_write_msr", _writeArray, 0);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Close() => _pawnIO.Close();
}
