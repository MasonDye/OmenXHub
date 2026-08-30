// Services/LightingAnimationService.cs — 灯带软件渲染动画引擎
// 参考 OmenLinux/omen-rgb-keyboard:动画不走固件效果 ID(灯带固件对 zone ID 的解释与
// 键盘四区不同,见 LightingPage.LightBarAnims 校准表),帧颜色在软件计算,20 FPS 定时经
// WMI 静态色通道下发 — 显示名即真实动效,且不受 SupportsEffect 门控(BasicFourZone 同样可跑)。
// Ceiling: WMI 单帧延迟 >50ms 时跳帧不排队(视觉降级不堆积);键盘 PerKey 动画仍走 HID 固件。
using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Media;
using static OmenSuperHub.OmenLighting;

namespace OmenSuperHub.Services {
  // internal: 签名暴露 OmenLighting 的 internal 枚举(LightingDevice/Interface),不能比它们更公开
  internal static class LightingAnimationService {
    static Timer _timer;
    static int _inFlight;
    static LightingDevice _device;
    static LightingControlInterface _iface;
    static string _effect;
    static readonly Color[] _base = new Color[4];
    static byte _brightness;
    static DateTime _start;
    static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(50);  // 20 FPS,对齐参考项目

    public static bool IsRunning => _timer != null;

    delegate Color[] Renderer(Color[] b, double t);
    // 键 = 灯带下拉 Tag(AnimName);渲染目标 = 校准表标注的真实动效(显示名)
    static readonly Dictionary<string, Renderer> Renderers = new() {
      { "ColorCycle", Confetti },   // 显示:五彩纸屑
      { "Starlight",  SunGlow },    // 显示:太阳
      { "Breathing",  Starlight },  // 显示:星光
      { "Wave",       Blink },      // 显示:间歇闪烁
      { "AudioPulse", Ripple },     // 显示:波纹
      { "Confetti",   Raindrop },   // 显示:雨滴
    };

    /// <summary>启动软件渲染动画(幂等:先停旧动画)。effect 不在渲染表内返回 false,调用方回退固件 ID 路径。</summary>
    public static bool Start(LightingDevice device, string effect, Color[] baseColors, byte brightness, LightingControlInterface iface) {
      if (!Renderers.ContainsKey(effect)) return false;
      Stop();
      _device = device; _effect = effect; _iface = iface; _brightness = brightness;
      for (int i = 0; i < 4; i++) _base[i] = i < baseColors.Length ? baseColors[i] : baseColors[0];
      _start = DateTime.UtcNow;
      // ponytail: 音乐律动(AudioPulse)激活时启系统音频采集,否则不常驻后台线程。
      if (effect == "AudioPulse") AudioCaptureService.Start();
      _timer = new Timer(Tick, null, TimeSpan.Zero, FrameInterval);
      return true;
    }

    public static void Stop() {
      var t = _timer; _timer = null; _effect = null;
      t?.Dispose();
      AudioCaptureService.Stop();   // 幂等,非 AudioPulse 时是 no-op
    }

    static void Tick(object state) {
      if (_timer == null || _effect == null) return;
      if (Interlocked.CompareExchange(ref _inFlight, 1, 0) != 0) return;  // 上一帧未返回:跳帧不排队
      try {
        double t = (DateTime.UtcNow - _start).TotalSeconds;
        var frame = Renderers[_effect](_base, t);
        SetZoneStaticColor(_device, new List<Color>(frame), _brightness, _iface);
      } catch { /* 单帧失败静默,下一帧重试 */ }
      finally { Interlocked.Exchange(ref _inFlight, 0); }
    }

    // ── 渲染器:4 段基色 + 时间 t(秒) → 该帧 4 段颜色 ──

    static Color[] Confetti(Color[] b, double t) {
      // 每段每 200ms 换一色彩纸(不依赖基色)
      var palette = new[] { Color.FromRgb(255,0,0), Color.FromRgb(0,255,0), Color.FromRgb(0,120,255),
        Color.FromRgb(255,216,0), Color.FromRgb(255,0,200), Color.FromRgb(0,255,255), Color.FromRgb(255,120,0) };
      int slot = (int)(t * 5);
      var c = new Color[4];
      for (int i = 0; i < 4; i++) c[i] = palette[(i * 31 + slot * 17 + slot * slot * 7) % palette.Length];
      return c;
    }

    static Color[] SunGlow(Color[] b, double t) {
      // 暖橙↔亮黄 缓慢呼吸,四段同色
      double s = 0.5 + 0.5 * Math.Sin(2 * Math.PI * t / 3.2);
      var c = Lerp(Color.FromRgb(200, 70, 0), Color.FromRgb(255, 220, 90), s);
      return new[] { c, c, c, c };
    }

    static Color[] Starlight(Color[] b, double t) {
      // 暗底基色 + 各段异速稀疏闪烁峰
      var c = new Color[4];
      double[] w = { 1.7, 2.3, 1.3, 2.9 }, p = { 0.0, 1.9, 3.7, 5.1 };
      for (int i = 0; i < 4; i++) {
        double k = Math.Max(0, Math.Sin(w[i] * t + p[i]));
        c[i] = Scale(b[i], 0.18 + 0.82 * Math.Pow(k, 10));
      }
      return c;
    }

    static Color[] Blink(Color[] b, double t) {
      // 间歇通断:亮 0.9s / 微亮 0.5s
      double k = (t % 1.4) < 0.9 ? 1.0 : 0.05;
      return new[] { Scale(b[0], k), Scale(b[1], k), Scale(b[2], k), Scale(b[3], k) };
    }

    static Color[] Ripple(Color[] b, double t) {
      // ponytail: 真音频律动 — 能量来自 AudioCaptureService(系统输出环回)。能量驱动波峰亮度,
      // 停顿(能量≈0)回落到暗底;采集不可用(无声卡/失败)时回退旧时间波,保证效果不僵死。
      double energy = AudioCaptureService.CurrentEnergy;
      if (energy <= 0.01) {
        // 回退:时间波(原有行为),避免无音频时静止
        var c0 = new Color[4];
        for (int i = 0; i < 4; i++) {
          double k = 0.5 + 0.5 * Math.Sin(2 * Math.PI * (t / 1.2 - i / 4.0));
          c0[i] = Lerp(Scale(b[i], 0.25), Lerp(b[i], Colors.White, 0.35), k);
        }
        return c0;
      }
      // 音频能量:整体亮度 = 基色 × 能量(带最低可见),波峰段额外冲白
      var c = new Color[4];
      for (int i = 0; i < 4; i++) {
        double k = 0.35 + 0.65 * energy;                       // 亮度随能量
        double peak = 0.5 + 0.5 * Math.Sin(2 * Math.PI * (t / 1.2 - i / 4.0));  // 段相位
        double mix = Math.Min(1, energy * (0.6 + 0.8 * peak));  // 波峰段更亮/更白
        c[i] = Lerp(Scale(b[i], k), Lerp(b[i], Colors.White, 0.5), mix);
      }
      return c;
    }

    static Color[] Raindrop(Color[] b, double t) {
      // 各段错峰短促落滴闪光
      double[] period = { 2.1, 3.3, 1.7, 2.7 }, phase = { 0.0, 0.8, 1.9, 2.6 };
      var c = new Color[4];
      for (int i = 0; i < 4; i++) {
        double x = (t + phase[i]) % period[i];
        double k = x < 0.22 ? Math.Sin(Math.PI * x / 0.22) : 0;   // 单滴包络
        c[i] = Lerp(Scale(b[i], 0.15), b[i], Math.Max(k, 0.15));
      }
      return c;
    }

    static Color Scale(Color c, double k) => Color.FromRgb(Cl(c.R * k), Cl(c.G * k), Cl(c.B * k));
    static Color Lerp(Color a, Color b, double t) =>
      Color.FromRgb(Cl(a.R + (b.R - a.R) * t), Cl(a.G + (b.G - a.G) * t), Cl(a.B + (b.B - a.B) * t));
    static byte Cl(double v) => (byte)Math.Max(0, Math.Min(255, Math.Round(v)));

    /// <summary>--selftest 断言 — 渲染器输出恒为 4 段合法颜色且随时间演化</summary>
    internal static string SelfCheck() {
      var fails = new List<string>();
      var base4 = new[] { Color.FromRgb(255, 0, 0), Color.FromRgb(0, 255, 0), Color.FromRgb(0, 0, 255), Color.FromRgb(255, 255, 255) };
      foreach (var kv in Renderers) {
        var f1 = kv.Value(base4, 0.0);
        var f2 = kv.Value(base4, 1.37);
        if (f1 == null || f1.Length != 4 || f2 == null || f2.Length != 4) { fails.Add($"{kv.Key} 帧长度≠4"); continue; }
        foreach (var c in f1)
          if (c.A != 255) { fails.Add($"{kv.Key} 非法颜色(alpha)"); break; }
        bool changed = false;
        for (int i = 0; i < 4; i++) if (f1[i] != f2[i]) { changed = true; break; }
        if (!changed) fails.Add($"{kv.Key} 帧不随时间变化(死效果)");
      }
      return fails.Count == 0 ? $"PASS LightingAnimationService: {Renderers.Count} 个渲染器输出合法且随时间演化"
        : "FAIL LightingAnimationService: " + string.Join("; ", fails);
    }
  }
}
