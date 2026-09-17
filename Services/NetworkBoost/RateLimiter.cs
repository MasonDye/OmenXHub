using System;
using System.Threading;

namespace OmenSuperHub.Services.NetworkBoost {
  /// <summary>
  /// 令牌桶限速器：线程安全，多连接共享同一个桶。
  /// rateKBps=0 表示不限速。限速应用于双向数据泵，下载和上传共享同一个桶。
  /// </summary>
  internal class RateLimiter {
    readonly object _lock = new object();
    readonly double _rateBps;       // 0 = unlimited
    readonly double _maxBurst;      // 最大突发量（1秒的流量）
    double _tokens;
    DateTime _lastRefill;

    public bool IsUnlimited => _rateBps <= 0;

    public RateLimiter(double rateKBps) {
      _rateBps = rateKBps * 1024;
      if (_rateBps > 0) {
        _maxBurst = _rateBps;
        _tokens = _maxBurst;
        _lastRefill = DateTime.UtcNow;
      }
    }

    /// <summary>
    /// 消费 bytes 个字节，令牌不足时阻塞等待。
    /// ponytail: 分块扣减 —— 旧实现 `while (_tokens < bytes)` 在 bytes 超过桶容量
    /// (maxBurst = rateKBps×1024，即限速 < 64 KB/s) 时永远等不满，且 CopyLoop 按 64KB
    /// 整块喂 → 连接永久停摆；全局/网卡限速器共享桶会拖死所有走它的流量。
    /// 新逻辑每轮最多扣当前可用令牌，remaining 递减，rate>0 时 refill 必然推进 → 收敛。
    /// 对外语义不变：阻塞到全部令牌获得才返回。
    /// </summary>
    public void Consume(int bytes) {
      if (_rateBps <= 0 || bytes <= 0) return;
      lock (_lock) {
        int remaining = bytes;
        while (remaining > 0) {
          Refill();
          int take = TakeChunk(remaining, _tokens);
          if (take > 0) { _tokens -= take; remaining -= take; }
          if (remaining > 0) {
            // 令牌不足以扣下剩余量：短暂等待桶补充，50ms 上限避免长时间持锁
            double deficit = remaining - _tokens;
            int waitMs = (int)(deficit > 0 ? deficit / _rateBps * 1000 : 0) + 1;
            Monitor.Wait(_lock, Math.Min(waitMs, 50));
          }
        }
      }
    }

    // 每轮扣减量 = min(剩余需求, 桶内令牌整数字)；令牌 <1 不扣，等 refill。纯函数供 SelfCheck 断言。
    static int TakeChunk(int remaining, double tokens)
      => tokens < 1 ? 0 : (int)Math.Min(remaining, tokens);

    void Refill() {
      var now = DateTime.UtcNow;
      _tokens = Math.Min(_maxBurst, _tokens + (now - _lastRefill).TotalSeconds * _rateBps);
      _lastRefill = now;
    }

    /// <summary>--selftest: 分块扣减纯函数 + 小速率死锁回归（63KB/s 桶装不下 64KiB 整块）。</summary>
    public static string SelfCheck() {
      try {
        if (TakeChunk(65536, 1024) != 1024 || TakeChunk(500, 1024) != 500 || TakeChunk(65536, 0.9) != 0)
          return "FAIL RateLimiter.TakeChunk: 分块扣减量错误";
        var lim = new RateLimiter(63);   // maxBurst=64512 < 65536：旧逻辑在此永久等待
        var done = System.Threading.Tasks.Task.Run(() => lim.Consume(65536));
        if (!done.Wait(2000)) return "FAIL RateLimiter: 63KB/s 下 Consume(64KiB) 2s 未完成(死锁回归)";
        if (done.IsFaulted) return "FAIL RateLimiter: Consume threw " + done.Exception?.GetBaseException().Message;
        return "PASS RateLimiter chunked consume";
      } catch (Exception ex) { return "FAIL RateLimiter: " + ex.Message; }
    }
  }
}
