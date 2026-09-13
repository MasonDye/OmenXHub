// MacroService.cs - 宏序列数据模型与持久化
// 定义 MacroEvent/MacroSequence 数据契约，JSON 序列化存储到本地文件
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace OmenSuperHub.Services {
  [DataContract]
  public enum MacroSource { Keyboard = 0, Mouse = 1 }

  [DataContract]
  public enum MacroDirection { Down = 0, Up = 1, Wheel = 2, HorizontalWheel = 3 }

  [DataContract]
  public class MacroEvent {
    [DataMember] public MacroSource Source { get; set; }
    [DataMember] public MacroDirection Direction { get; set; }
    [DataMember] public uint Key { get; set; }
    [DataMember] public int ScrollDelta { get; set; }
    [DataMember] public int DelayMs { get; set; }
  }

  [DataContract]
  public class MacroSequence {
    [DataMember] public string Name { get; set; }
    [DataMember] public uint TriggerKey { get; set; }
    [DataMember] public int RepeatCount { get; set; }
    [DataMember] public bool IgnoreDelays { get; set; }
    [DataMember] public bool InterruptOnOtherKey { get; set; }
    [DataMember] public bool Enabled { get; set; }
    [DataMember] public List<MacroEvent> Events { get; set; }

    public MacroSequence() {
      Name = "";
      RepeatCount = 1;
      Enabled = true;
      Events = new List<MacroEvent>();
    }
  }

  internal static class MacroService {
    private static string _filePath;
    private static readonly object _lock = new object();
    private static DataContractJsonSerializer _serializer;
    public static List<MacroSequence> Macros { get; private set; }
    // ponytail: 触发键哈希索引，避免每次按键 O(n) 线性扫描。整体换引用的快照 —— 钩子线程无锁读，
    // RebuildIndex 构建新表后一次性赋值。.NET Dictionary 并发读+写不保证读者安全（原地 Clear/扩容
    // 时 TryGetValue 可抛或死循环），而引用赋值原子，读者永远看到完整旧表或完整新表，滞后读旧表无害。
    //   升档路径：若未来支持多宏共用一键，可改为 Dictionary<uint, List<MacroSequence>>。
    static Dictionary<uint, MacroSequence> _triggerIndex = new Dictionary<uint, MacroSequence>();

    public static void Initialize() {
      string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
      string dir = Path.Combine(appData, "OmenXHub");
      if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
      _filePath = Path.Combine(dir, "macros.json");
      _serializer = new DataContractJsonSerializer(typeof(List<MacroSequence>),
          new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
      Load();
    }

    public static void Load() {
      lock (_lock) {
        try {
          if (File.Exists(_filePath)) {
            using (var fs = new FileStream(_filePath, FileMode.Open, FileAccess.Read))
              Macros = (List<MacroSequence>)_serializer.ReadObject(fs);
          } else {
            Macros = new List<MacroSequence>();
          }
        } catch (Exception ex) {
          Logger.Error("MacroService.Load failed: " + ex.Message);
          Macros = new List<MacroSequence>();
        }
        RebuildIndex();
      }
    }

    public static void Save() {
      lock (_lock) {
        try {
          using (var ms = new MemoryStream()) {
            _serializer.WriteObject(ms, Macros);
            // ponytail: net481 无 File.Move(overwrite) —— temp + Replace 原子换入，崩溃不留半截
            // 文件；否则 Load 失败降级空列表后，下一次 Save 会把可恢复的数据覆盖为空。
            string tmp = _filePath + ".tmp";
            File.WriteAllBytes(tmp, ms.ToArray());
            if (File.Exists(_filePath)) File.Replace(tmp, _filePath, null);
            else File.Move(tmp, _filePath);
          }
        } catch (Exception ex) {
          Logger.Error("MacroService.Save failed: " + ex.Message);
        }
        RebuildIndex();
      }
    }

    public static void AddMacro(MacroSequence macro) {
      Macros.Add(macro);
      Save();
    }

    public static void RemoveMacro(MacroSequence macro) {
      Macros.Remove(macro);
      Save();
    }

    // 重建触发键索引（必须在 lock 内或单线程调用）。配合保存时的冲突检测，
    // 同一触发键至多有一个启用宏；若旧数据已存在冲突，记录日志且后者覆盖前者。
    // 构建局部新表后整体换引用 —— 见 _triggerIndex 注释（钩子线程无锁读的快照保障）。
    internal static void RebuildIndex() {
      var idx = new Dictionary<uint, MacroSequence>();
      if (Macros != null) {
        foreach (var m in Macros) {
          if (m.Enabled && m.TriggerKey != 0) {
            if (idx.TryGetValue(m.TriggerKey, out var existing)) {
              Logger.Error("MacroService: trigger key conflict detected (0x" +
                m.TriggerKey.ToString("X2") + ") between '" + existing.Name + "' and '" + m.Name +
                "'. The latter shadows the former.");
            }
            idx[m.TriggerKey] = m;
          }
        }
      }
      _triggerIndex = idx;
      AssertInvariants();
    }

    public static MacroSequence GetByTriggerKey(uint vk) {
      if (vk == 0) return null;
      MacroSequence m;
      // 读索引无需锁：钩子线程读到的是不可变快照（RebuildIndex 整体换引用，见 _triggerIndex 注释），
      // 最多滞后一拍拿到旧表 —— 完整有效，不会撕裂。
      if (_triggerIndex.TryGetValue(vk, out m)) return m;
      return null;
    }

    // ponytail: 最小自检——索引与启用宏一致。Assert 调用方在锁内。失败即外部修改了 Macros 未走 Save/Load 路径。
    internal static void AssertInvariants() {
      if (Macros == null) return;
      var dup = Macros.Where(x => x.Enabled && x.TriggerKey != 0)
                      .GroupBy(x => x.TriggerKey)
                      .Where(g => g.Count() > 1)
                      .Select(g => g.Key)
                      .ToList();
      if (dup.Count > 0)
        Logger.Error("MacroService.AssertInvariants: duplicate enabled TriggerKey: " +
          string.Join(", ", dup.ConvertAll(k => "0x" + k.ToString("X2"))));
    }

    // --selftest: 快照索引语义 + 文件往返。索引部分只触 RebuildIndex/GetByTriggerKey；
    // 往返部分把 _filePath 重定向到临时路径（SelfCheck 在类内可直改私有字段），覆盖 Save 的
    // Move(首存)与 Replace(再存)两分支、tmp 无残留、数据无损，全程不碰真实 macros.json。
    public static string SelfCheck() {
      var a = new MacroSequence { Name = "A", TriggerKey = 0x71, Enabled = true };
      var b = new MacroSequence { Name = "B", TriggerKey = 0x71, Enabled = true }; // 同键冲突，后者覆盖
      var c = new MacroSequence { Name = "C", TriggerKey = 0x72, Enabled = false };
      Macros = new List<MacroSequence> { a, b, c };
      RebuildIndex();
      if (GetByTriggerKey(0x71) != b) return "[MacroService] FAIL: same-key conflict should shadow with latter";
      if (GetByTriggerKey(0x72) != null) return "[MacroService] FAIL: disabled macro must not be indexed";
      Macros = new List<MacroSequence>();
      RebuildIndex();
      if (GetByTriggerKey(0x71) != null) return "[MacroService] FAIL: stale index entry after clear";

      string tmpPath = Path.Combine(Path.GetTempPath(), "omenxhub_macro_selftest.json");
      try {
        _filePath = tmpPath;
        if (File.Exists(tmpPath)) File.Delete(tmpPath);
        _serializer = new DataContractJsonSerializer(typeof(List<MacroSequence>),
            new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
        Macros = new List<MacroSequence> { new MacroSequence { Name = "R", TriggerKey = 0x70, Enabled = true } };
        Save();
        if (!File.Exists(tmpPath)) return "[MacroService] FAIL: first save (Move branch) produced no file";
        Save();
        if (File.Exists(tmpPath + ".tmp")) return "[MacroService] FAIL: .tmp left behind after Replace";
        Load();
        if (Macros == null || Macros.Count != 1 || Macros[0].Name != "R")
          return "[MacroService] FAIL: save/load round trip lost data";
        return "[MacroService] PASS";
      } finally {
        try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { }
        _filePath = null;
        _serializer = null;
        Macros = new List<MacroSequence>();
        RebuildIndex();
      }
    }
  }
}
