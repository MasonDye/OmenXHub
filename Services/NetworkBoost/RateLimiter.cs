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

    /// <summary>消费 n 个字节，令牌不足时阻塞等待。</summary>
    public void Consume(int bytes) {
      if (_rateBps <= 0 || bytes <= 0) return;
      lock (_lock) {
        Refill();
        while (_tokens < bytes) {
          // ponytail: 令牌不足时短暂等待让桶补充，最多 50ms 避免长时间持锁
          int waitMs = (int)((bytes - _tokens) / _rateBps * 1000) + 1;
          Monitor.Wait(_lock, Math.Min(waitMs, 50));
          Refill();
        }
        _tokens -= bytes;
      }
    }

    void Refill() {
      var now = DateTime.UtcNow;
      _tokens = Math.Min(_maxBurst, _tokens + (now - _lastRefill).TotalSeconds * _rateBps);
      _lastRefill = now;
    }
  }
}
