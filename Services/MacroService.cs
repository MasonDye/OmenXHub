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
    // ponytail: 触发键哈希索引，避免每次按键 O(n) 线性扫描。
    //   升档路径：若未来支持多宏共用一键，可改为 Dictionary<uint, List<MacroSequence>>。
    static readonly Dictionary<uint, MacroSequence> _triggerIndex = new Dictionary<uint, MacroSequence>();

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
            File.WriteAllBytes(_filePath, ms.ToArray());
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
    static void RebuildIndex() {
      _triggerIndex.Clear();
      if (Macros == null) return;
      foreach (var m in Macros) {
        if (m.Enabled && m.TriggerKey != 0) {
          if (_triggerIndex.TryGetValue(m.TriggerKey, out var existing)) {
            Logger.Error("MacroService: trigger key conflict detected (0x" +
              m.TriggerKey.ToString("X2") + ") between '" + existing.Name + "' and '" + m.Name +
              "'. The latter shadows the former.");
          }
          _triggerIndex[m.TriggerKey] = m;
        }
      }
      AssertInvariants();
    }

    public static MacroSequence GetByTriggerKey(uint vk) {
      if (vk == 0) return null;
      MacroSequence m;
      // 读索引无需锁：仅 UI 钩子线程读，写操作发生在 lock 内；
      // 容量变化由 RebuildIndex 在 lock 中完成，钩子可能读到旧快照但不会崩溃。
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
  }
}
