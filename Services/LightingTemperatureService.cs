// LightingTemperatureService.cs — 温度联动灯效
// 定时轮询 CPU/GPU 温度,映射为四区域键盘颜色并写入硬件。
// WMI 通道单次写入 ~200ms, 轮询间隔设 4s 保证不堆积。
// 激活方式: LightingPage 中手动开启,或场景 TriggerMode="Temperature" 触发。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using static OmenSuperHub.OmenLighting;

namespace OmenSuperHub.Services {
  public static class LightingTemperatureService {
    static System.Threading.Timer _timer;
    static bool _running;
    static float _lastTemp = -1;
    static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(4);

    // ponytail: hysteresis threshold — 温度变化 < 1°C 不重写灯光,避免频繁 WMI 调用
    const float Hysteresis = 1.5f;

    public static bool IsRunning => _running;

    /// <summary>启动温度轮询。幂等调用 (已在运行则跳过)。</summary>
    public static void Start() {
      if (_running) return;
      _running = true;
      _timer = new System.Threading.Timer(_ => Tick(), null, TimeSpan.Zero, PollInterval);
      Logger.Info("LightingTemperatureService started");
    }

    /// <summary>停止温度轮询并恢复静态色。幂等调用。</summary>
    public static void Stop() {
      _running = false;
      _timer?.Dispose();
      _timer = null;
      _lastTemp = -1;
      Logger.Info("LightingTemperatureService stopped");
    }

    static void Tick() {
      if (!_running) return;
      try {
        float cpuTemp = HardwareService.CPUTemp;
        float gpuTemp = HardwareService.GPUTemp;
        float temp = Math.Max(cpuTemp, gpuTemp);
        if (temp <= 0) return; // 传感器未就绪

        if (Math.Abs(temp - _lastTemp) < Hysteresis) return;
        _lastTemp = temp;

        var color = TempToColor(temp);
        var colors = Enumerable.Repeat(color, 4).ToList();
        byte bright = (byte)ConfigService.LightingBrightness;
        var iface = ResolveInterface();

        SetZoneStaticColor(LightingDevice.Keyboard, colors, bright, iface);
      } catch (Exception ex) {
        Logger.Error($"LightingTemperatureService.Tick: {ex.Message}");
      }
    }

    /// <summary>
    /// 温度 → 单一颜色映射,五档线性插值:
    ///   ≤30°C → IceBlue (0,200,255)
    ///   50°C  → CoolGreen (0,255,100)
    ///   70°C  → WarmYellow (255,200,0)
    ///   85°C  → FieryOrange (255,100,0)
    ///   ≥100°C → HotRed (255,20,20)
    /// </summary>
    public static Color TempToColor(float temp) {
      (float stop, byte r, byte g, byte b)[] stops = {
        (30,  0,   200, 255),
        (50,  0,   255, 100),
        (70,  255, 200, 0),
        (85,  255, 100, 0),
        (100, 255, 20,  20),
      };

      if (temp <= stops[0].stop) return Color.FromRgb(stops[0].r, stops[0].g, stops[0].b);
      if (temp >= stops[stops.Length - 1].stop) return Color.FromRgb(stops[stops.Length - 1].r, stops[stops.Length - 1].g, stops[stops.Length - 1].b);

      for (int i = 0; i < stops.Length - 1; i++) {
        if (temp >= stops[i].stop && temp <= stops[i + 1].stop) {
          float ratio = (temp - stops[i].stop) / (stops[i + 1].stop - stops[i].stop);
          byte r = Lerp(stops[i].r, stops[i + 1].r, ratio);
          byte g = Lerp(stops[i].g, stops[i + 1].g, ratio);
          byte b = Lerp(stops[i].b, stops[i + 1].b, ratio);
          return Color.FromRgb(r, g, b);
        }
      }
      return Color.FromRgb(255, 20, 20);
    }

    static byte Lerp(byte a, byte b, float t) => (byte)(a + (b - a) * t);

    static LightingControlInterface ResolveInterface() {
      return ConfigService.LightingInterface switch {
        "Dojo" => LightingControlInterface.Dojo,
        "PerKey" => LightingControlInterface.Dojo, // PerKey 走 Dojo WMI 写四区域回退
        "HpSdk" => LightingControlInterface.BasicFourZone,
        _ => LightingControlInterface.BasicFourZone,
      };
    }
  }
}
