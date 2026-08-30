// BatteryHealthService.cs - 电池健康信息
// WMI Win32_Battery 读设计容量/满充容量/电量/充电态 → 磨损度。
// 磨损度 = (1 - FullChargedCapacity/DesignCapacity)×100。查询失败/null 时返回不在场,不抛异常。
// 上限: Win32_Battery 不提供循环次数(CycleCount),需 BATTERY_QUERY_INFORMATION IOCTL 才能读,
// 本轮不做,留待后续。
using System.Management;

namespace OmenSuperHub.Services {
  public sealed class BatteryHealth {
    public bool Present;          // 是否有可读电池(WMI 查到记录)
    public int ChargePercent;     // 当前电量 %(0~100)
    public bool Charging;         // 是否在充电
    public int WearPercent;       // 磨损度 %(0~100, 0=全新)
  }

  internal static class BatteryHealthService {
    // ponytail: 每轮查询是 WMI 调用,调用方(Dashboard)按 30s 节流。
    public static BatteryHealth Query() {
      var result = new BatteryHealth { Present = false, ChargePercent = -1, WearPercent = -1 };
      try {
        using (var searcher = new ManagementObjectSearcher(
          "SELECT DesignCapacity, FullChargeCapacity, BatteryStatus, EstimatedChargeRemaining FROM Win32_Battery"))
        using (var col = searcher.Get()) {
          foreach (ManagementBaseObject obj in col) {
            using (obj) {
              result.Present = true;
              // BatteryStatus: 2=充电中, 1=放电, 其它=未知/在线
              int status = 0;
              try { status = System.Convert.ToInt32(obj["BatteryStatus"]); } catch { }
              result.Charging = status == 2;

              int remaining = -1;
              try { remaining = System.Convert.ToInt32(obj["EstimatedChargeRemaining"]); } catch { }
              if (remaining >= 0 && remaining <= 100) result.ChargePercent = remaining;

              int design = 0, full = 0;
              try { design = System.Convert.ToInt32(obj["DesignCapacity"]); } catch { }
              try { full = System.Convert.ToInt32(obj["FullChargeCapacity"]); } catch { }
              if (design > 0 && full > 0) {
                int wear = (int)((1.0 - (double)full / design) * 100);
                result.WearPercent = System.Math.Max(0, System.Math.Min(100, wear));
              }
              break;  // 只取第一条(笔记本通常单电池)
            }
          }
        }
      } catch {
        // WMI 查询失败(无电池/权限/服务未启动) → Present=false,调用方显示 "-"
      }
      return result;
    }
  }
}
