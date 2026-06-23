// MacroService.cs - 宏序列数据模型与持久化
// 定义 MacroEvent/MacroSequence 数据契约，JSON 序列化存储到本地文件
using System;
using System.Collections.Generic;
using System.IO;
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

    public static MacroSequence GetByTriggerKey(uint vk) {
      if (vk == 0) return null;
      foreach (var m in Macros) {
        if (m.Enabled && m.TriggerKey == vk)
          return m;
      }
      return null;
    }
  }
}
