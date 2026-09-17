using LibreHardwareMonitor.Hardware;

namespace LibreHardwareMonitor.PawnIo;

public class IntelMsr
{
    private readonly long[] _inArray = new long[1];
    private readonly long[] _writeArray = new long[2];
    private readonly PawnIo _pawnIO = PawnIo.LoadModuleFromResource(typeof(IntelMsr).Assembly, $"{nameof(LibreHardwareMonitor)}.Resources.PawnIO.IntelMSR.bin");
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

    // Execute 会吞掉模块返回的 NTSTATUS（如白名单外写入的 ACCESS_DENIED）并静默返回，
    // 导致写失败与写成功无法区分；此版本透传 HRESULT 供调用方判定真实写入结果。
    public bool WriteMsrChecked(uint index, ulong value, out int hr)
    {
        _writeArray[0] = index;
        _writeArray[1] = (long)value;
        hr = _pawnIO.ExecuteHr("ioctl_write_msr", _writeArray, 2, new long[0], 0, out _);
        return hr == 0;
    }

    public void Close() => _pawnIO.Close();
}
