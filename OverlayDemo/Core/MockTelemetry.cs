using System;
using System.Windows.Threading;

namespace OverlayDemo.Core;

// 演示数据源：单一全局数据流，随机游走模拟传感器，1100ms 刷新。
// 各 widget 的 VM 订阅 Updated 事件读取快照。
public static class MockTelemetry
{
    private static readonly Random _rng = new();
    private static readonly DispatcherTimer _timer;

    public static double Cpu { get; private set; } = 35;
    public static double CpuTemp { get; private set; } = 55;
    public static double Gpu { get; private set; } = 40;
    public static double GpuTemp { get; private set; } = 58;
    public static int Ram { get; private set; } = 46;
    public static int Fps { get; private set; } = 120;
    public static double Upload { get; private set; } = 2.5;
    public static double Download { get; private set; } = 12;
    public static int FanCpu { get; private set; } = 2400;
    public static int FanGpu { get; private set; } = 1900;

    public static event Action Updated;

    static MockTelemetry()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(1100) };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
        Tick();
    }

    private static double Walk(double v, double min, double max, double step)
    {
        v += (_rng.NextDouble() - 0.5) * 2 * step;
        if (v < min) v = min;
        if (v > max) v = max;
        return v;   // ponytail: net481 无 Math.Clamp
    }

    private static void Tick()
    {
        Cpu = Walk(Cpu, 3, 100, 9);
        Gpu = Walk(Gpu, 2, 100, 11);
        CpuTemp = Walk(CpuTemp, 38, 92, 2.5);
        GpuTemp = Walk(GpuTemp, 40, 88, 2.5);
        Fps = (int)Math.Round(Walk(Fps, 24, 240, 18));
        Ram = 38 + _rng.Next(0, 30);
        Upload = Walk(Upload, 0.1, 12, 0.8);
        Download = Walk(Download, 0.5, 90, 5);
        // 转速跟随负载
        FanCpu = (int)Walk(FanCpu, 1200, 4200, (Cpu - 40) * 0.6 + 220);
        FanGpu = (int)Walk(FanGpu, 1000, 3800, (Gpu - 40) * 0.5 + 180);
        Updated?.Invoke();
    }
}
