// AudioCaptureService.cs - 系统输出音频捕获(音乐律动数据源)
// 用 NAudio WasapiLoopbackCapture 抓系统默认输出(听什么就律动什么),DataAvailable 回调
// 累加采样 → 帧 RMS 能量(0~1, EMA 平滑)。无音频设备/初始化失败优雅降级(能量=0)。
// 生命周期: 仅音乐律动(AudioPulse)动画激活时 Start,Stop 时停止 — 按需,不常驻后台线程。
using System;
using NAudio.Wave;

namespace OmenSuperHub.Services {
  internal static class AudioCaptureService {
    static WasapiLoopbackCapture _capture;
    static readonly object _lock = new object();

    // ponytail: 能量窗口 — 最近 ~50ms 采样的 RMS(0~1)。EMA 平滑防抖,音乐停顿快速回落。
    static double _energy;          // 当前平滑能量
    static double _frameSum;        // 当前窗口平方和
    static int _frameCount;         // 当前窗口采样数
    static DateTime _lastFrameAt;   // 窗口重置时间
    const double EmaAlpha = 0.25;

    public static double CurrentEnergy { get { lock (_lock) return _energy; } }
    public static bool IsRunning => _capture != null;

    /// <summary>启动环回采集。幂等(已在采则跳过)。无设备/失败时静默降级(能量=0)。</summary>
    public static void Start() {
      lock (_lock) {
        if (_capture != null) return;
        try {
          _capture = new WasapiLoopbackCapture();
          _frameSum = 0; _frameCount = 0; _energy = 0;
          _lastFrameAt = DateTime.UtcNow;
          _capture.DataAvailable += OnData;
          _capture.RecordingStopped += (s, e) => { /* 异常停止时由调用方 Stop 处理 */ };
          _capture.StartRecording();
        } catch {
          // 无默认输出设备/权限/服务不可用 → 降级
          try { _capture?.Dispose(); } catch { }
          _capture = null;
          _energy = 0;
        }
      }
    }

    public static void Stop() {
      lock (_lock) {
        var c = _capture; _capture = null;
        if (c != null) {
          try { c.DataAvailable -= OnData; c.StopRecording(); } catch { }
          try { c.Dispose(); } catch { }
        }
        _energy = 0;
      }
    }

    static void OnData(object sender, WaveInEventArgs e) {
      // 解析 PCM 采样(NAudio loopback 默认 32-bit IEEE float),累加平方和。
      lock (_lock) {
        // 每 ~50ms 结算一次能量并重置窗口
        var now = DateTime.UtcNow;
        if ((now - _lastFrameAt).TotalMilliseconds >= 50) {
          double rms = _frameCount > 0 ? Math.Sqrt(_frameSum / _frameCount) : 0;
          double norm = Math.Min(1, rms / 0.25);   // 0.25 为经验归一化上限(接近削波)
          _energy = EmaAlpha * norm + (1 - EmaAlpha) * _energy;
          _frameSum = 0; _frameCount = 0;
          _lastFrameAt = now;
        }
        // 累加本缓冲的平方和(按 4 字节 float 解释)
        for (int i = 0; i + 4 <= e.BytesRecorded; i += 4) {
          float f = BitConverter.ToSingle(e.Buffer, i);
          _frameSum += f * f;
          _frameCount++;
        }
      }
    }
  }
}
