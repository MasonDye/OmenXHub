using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace OmenSuperHub.Services.NetworkBoost {
  /// <summary>进程级分流规则：进程名 + 出口通道（aggregation / direct / nic_ethernet / nic_wifi）+ 限速。</summary>
  public class RoutingRule {
    public string ProcessName { get; set; } = "";
    public string Outbound { get; set; } = "aggregation";
    public int LimitKBps { get; set; } = 0;  // 0 = 不限速
  }

  /// <summary>
  /// 规则持久化到本地 JSON 文件（%AppData%\OmenXHub\routing-rules.json），关闭程序后保留。
  /// 首次加载时若文件不存在则回退读取注册表旧数据（兼容迁移）。
  /// </summary>
  internal static class RoutingRuleStore {
    static readonly JavaScriptSerializer Json = new JavaScriptSerializer();

    public static string FilePath {
      get {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OmenXHub");
        return Path.Combine(dir, "routing-rules.json");
      }
    }

    public static List<RoutingRule> Load() {
      try {
        string s = File.Exists(FilePath) ? File.ReadAllText(FilePath) : "";
        if (string.IsNullOrEmpty(s)) s = ConfigService.BoostRulesJson; // 兼容旧注册表数据
        if (string.IsNullOrEmpty(s)) return new List<RoutingRule>();
        return Json.Deserialize<List<RoutingRule>>(s) ?? new List<RoutingRule>();
      } catch { return new List<RoutingRule>(); }
    }

    public static void Save(List<RoutingRule> rules) {
      string json = Json.Serialize(rules ?? new List<RoutingRule>());
      try {
        string path = FilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, json);
      } catch { }
    }
  }
}
