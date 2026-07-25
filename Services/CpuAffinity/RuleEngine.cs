// CpuAffinity/RuleEngine.cs - 规则匹配引擎（首匹配优先）
// 参考 CpuAffinityManager.Engine.RuleEngine
// 读写线程安全（读时返回快照）
using System.Collections.Generic;
using System.Linq;

namespace OmenSuperHub.Services.CpuAffinity {

  /// <summary>
  /// 首匹配优先的规则引擎。禁用规则跳过；进程名必填，路径可选，排除可选。
  /// </summary>
  public class RuleEngine {
    readonly object _lock = new object();
    List<RuleEntry> _rules = new List<RuleEntry>();

    public IReadOnlyList<RuleEntry> Rules {
      get { lock (_lock) return _rules.AsReadOnly(); }
    }

    /// <summary>按顺序匹配，返回首个匹配规则；无匹配返回 null。</summary>
    public RuleEntry Match(string processName, string fullPath) {
      if (string.IsNullOrEmpty(processName)) return null;
      List<RuleEntry> copy;
      lock (_lock) copy = new List<RuleEntry>(_rules);

      foreach (var rule in copy) {
        if (!rule.Enabled) continue;
        if (string.IsNullOrEmpty(rule.Match?.Process)) continue;
        if (!Wildcard.Match(processName, rule.Match.Process, true)) continue;

        // 路径匹配（可选）
        if (!string.IsNullOrEmpty(rule.Match.Path) &&
            !Wildcard.MatchPath(fullPath ?? "", rule.Match.Path, true))
          continue;

        // 排除模式
        if (rule.Match.Exclude != null && rule.Match.Exclude.Count > 0) {
          if (rule.Match.Exclude.Any(ex => Wildcard.Match(processName, ex, true)))
            continue;
        }
        return rule;
      }
      return null;
    }

    public void SetRules(List<RuleEntry> rules) {
      lock (_lock) _rules = rules ?? new List<RuleEntry>();
    }

    public void AddRule(RuleEntry rule) {
      lock (_lock) {
        int idx = _rules.FindIndex(r => r.Id == rule.Id);
        if (idx >= 0) _rules[idx] = rule;
        else _rules.Add(rule);
      }
    }

    public bool RemoveRule(string ruleId) {
      lock (_lock) return _rules.RemoveAll(r => r.Id == ruleId) > 0;
    }
  }
}
