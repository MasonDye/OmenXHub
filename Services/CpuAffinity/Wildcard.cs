// CpuAffinity/Wildcard.cs - 通配符匹配
// 参考 CpuAffinityManager.Wildcard：支持 * ? | [chars]，以及路径 ** 多段匹配
using System;
using System.Linq;

namespace OmenSuperHub.Services.CpuAffinity {

  /// <summary>
  /// 通配符匹配引擎：
  ///   *    匹配除路径分隔符外任意字符
  ///   **   匹配包含路径分隔符的任意字符（多段）
  ///   ?    匹配单个字符
  ///   |    OR 分隔（多模式任一匹配）
  ///   [c]  字符类，支持范围 [0-9a-f] 与取反 [!0-9]
  /// ponytail: 用回溯算法而非正则，避免 Regex 编译开销。上限：无跨段 ** 路径匹配复杂场景的贪婪回溯退化。
  /// </summary>
  public static class Wildcard {
    public static bool Match(string input, string pattern, bool ignoreCase = true) {
      if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(pattern)) return false;
      if (pattern.Contains("|"))
        return pattern.Split('|')
          .Select(p => p.Trim())
          .Any(p => MatchSingle(input, p, ignoreCase));
      return MatchSingle(input, pattern, ignoreCase);
    }

    /// <summary>匹配完整文件路径。** 匹配零或多个路径段。</summary>
    public static bool MatchPath(string path, string pattern, bool ignoreCase = true) {
      if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(pattern)) return false;
      path = path.Replace('/', '\\');
      pattern = pattern.Replace('/', '\\');
      var pathSegs = path.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
      var patSegs = pattern.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
      return MatchSegments(pathSegs, 0, patSegs, 0, ignoreCase);
    }

    static bool MatchSegments(string[] path, int pi, string[] pat, int ppi, bool ignoreCase) {
      while (ppi < pat.Length) {
        if (pat[ppi] == "**") {
          ppi++;
          if (ppi >= pat.Length) return true;
          // ** 在每个位置尝试匹配剩余模式
          for (int i = pi; i < path.Length; i++)
            if (MatchSegments(path, i, pat, ppi, ignoreCase)) return true;
          return false;
        } else {
          if (pi >= path.Length) return false;
          if (!MatchSingle(path[pi], pat[ppi], ignoreCase)) return false;
          pi++; ppi++;
        }
      }
      return pi >= path.Length;
    }

    static bool MatchSingle(string input, string pattern, bool ignoreCase)
      => MatchSpan(input.AsSpan(), pattern.AsSpan(), ignoreCase);

    static bool MatchSpan(ReadOnlySpan<char> input, ReadOnlySpan<char> pattern, bool ignoreCase) {
      int i = 0, p = 0, starIdx = -1, matchIdx = 0;
      while (i < input.Length) {
        if (p < pattern.Length && pattern[p] == '*') {
          starIdx = p; matchIdx = i; p++;
        } else if (p < pattern.Length && pattern[p] == '?') {
          i++; p++;
        } else if (p < pattern.Length && pattern[p] == '[') {
          int close = FindCloseBracket(pattern, p);
          if (close < 0) {
            if (!CharEq(input[i], '[', ignoreCase)) {
              if (starIdx < 0) return false;
              p = starIdx + 1; matchIdx++; i = matchIdx;
            } else { i++; p++; }
          } else {
            if (!MatchCharClass(input[i], pattern.Slice(p, close - p + 1), ignoreCase)) {
              if (starIdx < 0) return false;
              p = starIdx + 1; matchIdx++; i = matchIdx;
            } else { i++; p = close + 1; }
          }
        } else if (p < pattern.Length) {
          if (!CharEq(input[i], pattern[p], ignoreCase)) {
            if (starIdx < 0) return false;
            p = starIdx + 1; matchIdx++; i = matchIdx;
          } else { i++; p++; }
        } else {
          if (starIdx < 0) return false;
          p = starIdx + 1; matchIdx++; i = matchIdx;
        }
      }
      while (p < pattern.Length && pattern[p] == '*') p++;
      return p == pattern.Length;
    }

    static bool MatchCharClass(char c, ReadOnlySpan<char> pattern, bool ignoreCase) {
      if (pattern.Length < 3 || pattern[0] != '[') return false;
      int idx = 1;
      bool negate = false;
      if (idx < pattern.Length && pattern[idx] == '!') { negate = true; idx++; }
      bool matched = false;
      while (idx < pattern.Length - 1) {
        if (idx + 2 < pattern.Length - 1 && pattern[idx + 1] == '-') {
          if (CharInRange(c, pattern[idx], pattern[idx + 2], ignoreCase)) matched = true;
          idx += 3;
        } else {
          if (CharEq(c, pattern[idx], ignoreCase)) matched = true;
          idx++;
        }
      }
      return negate ? !matched : matched;
    }

    static int FindCloseBracket(ReadOnlySpan<char> pattern, int start) {
      for (int i = start + 1; i < pattern.Length; i++)
        if (pattern[i] == ']') return i;
      return -1;
    }

    static bool CharEq(char a, char b, bool ignoreCase)
      => ignoreCase ? char.ToUpperInvariant(a) == char.ToUpperInvariant(b) : a == b;

    static bool CharInRange(char c, char lo, char hi, bool ignoreCase) {
      if (ignoreCase) {
        c = char.ToUpperInvariant(c);
        lo = char.ToUpperInvariant(lo);
        hi = char.ToUpperInvariant(hi);
      }
      return c >= lo && c <= hi;
    }
  }
}
