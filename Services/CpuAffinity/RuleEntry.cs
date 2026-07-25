// CpuAffinity/RuleEntry.cs - 规则条目模型
// 参考 CpuAffinityManager.Engine.RuleEntry
// ponytail: 用旧 DataContract 而非 System.Text.Json，避免引入新依赖，与项目其他 json 一致
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace OmenSuperHub.Services.CpuAffinity {

  /// <summary>单条亲和性规则：匹配条件 + 动作。</summary>
  [DataContract]
  public class RuleEntry {
    [DataMember] public string Id { get; set; } = "";
    [DataMember] public string Name { get; set; } = "";
    [DataMember] public bool Enabled { get; set; } = true;
    [DataMember] public RuleMatch Match { get; set; } = new RuleMatch();
    [DataMember] public RuleAction Action { get; set; } = new RuleAction();
  }

  /// <summary>匹配条件：进程名(通配符)、路径(可选)、排除(可选)。</summary>
  [DataContract]
  public class RuleMatch {
    /// <summary>进程名通配符，如 game*.exe。必填。</summary>
    [DataMember] public string Process { get; set; } = "";
    /// <summary>路径通配符，如 D:\Games\**。可选。</summary>
    [DataMember] public string Path { get; set; }
    /// <summary>排除模式列表。任一匹配则跳过此规则。</summary>
    [DataMember] public List<string> Exclude { get; set; }
  }

  /// <summary>规则动作：mode/level/customMask/socket/priority/lock。</summary>
  [DataContract]
  public class RuleAction {
    /// <summary>亲和性模式：all-cores/p-cores/e-cores/p-cores-no-smt/p-cores-first/no-smt/first-half/second-half/ccd0/ccd1/custom
    /// 支持 | 复合回退链与 @socketN 过滤。</summary>
    [DataMember] public string Mode { get; set; } = "all-cores";

    /// <summary>强制级别：soft-cpu-sets / hard-affinity / job-enforced / job-locked。</summary>
    [DataMember] public string Level { get; set; } = "hard-affinity";

    /// <summary>自定义掩码十六进制字符串(如 0xFF)，仅 mode=custom 时用。</summary>
    [DataMember] public string CustomMask { get; set; }

    /// <summary>物理 CPU socket 索引(0-based)，-1 或 null 表示全部。</summary>
    [DataMember] public int? SocketIndex { get; set; }

    /// <summary>优先级类提示：idle/belowNormal/normal/aboveNormal/high/realtime。</summary>
    [DataMember] public string CpuPriority { get; set; }

    /// <summary>true 时阻止子进程脱离 Job（配合 job-enforced）。</summary>
    [DataMember] public bool Lock { get; set; }

    /// <summary>解析 CustomMask 十六进制字符串为 ulong。</summary>
    public ulong? GetCustomMask() {
      if (string.IsNullOrWhiteSpace(CustomMask)) return null;
      string hex = CustomMask.Trim();
      if (hex.StartsWith("0x", System.StringComparison.OrdinalIgnoreCase))
        hex = hex.Substring(2);
      return ulong.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out ulong m) ? m : (ulong?)null;
    }
  }

  /// <summary>规则配置根：版本 + 规则列表。</summary>
  [DataContract]
  public class RuleConfig {
    [DataMember] public int Version { get; set; } = 2;
    [DataMember] public List<RuleEntry> Rules { get; set; } = new List<RuleEntry>();
  }
}
