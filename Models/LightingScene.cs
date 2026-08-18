// LightingScene.cs - 灯光场景数据模型
// 参考 OmenCore RgbScene: 多场景管理 + 性能模式联动 + 时间调度
using System;
using System.Runtime.Serialization;

namespace OmenSuperHub.Models {
  /// <summary>灯光场景 — 一组完整的键盘/灯条灯光设置快照</summary>
  [DataContract]
  public class LightingScene {
    [DataMember(Order = 0)] public string Id { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 8);
    [DataMember(Order = 1)] public string Name { get; set; } = "新场景";
    [DataMember(Order = 2)] public string Device { get; set; } = "keyboard";      // "keyboard" | "lightbar"
    [DataMember(Order = 3)] public string Interface { get; set; } = "BasicFourZone"; // "BasicFourZone" | "Dojo" | "HpSdk" | "PerKey"
    [DataMember(Order = 4)] public int Brightness { get; set; } = 100;
    [DataMember(Order = 5)] public string Animation { get; set; } = "None";
    [DataMember(Order = 6)] public string Direction { get; set; } = "Left";
    [DataMember(Order = 7)] public string Theme { get; set; } = "Galaxy";
    [DataMember(Order = 8)] public string[] ZoneColors { get; set; } = { "#FF0000", "#FF0000", "#FF0000", "#FF0000" };
    [DataMember(Order = 9)] public string PerKeyStaticColor { get; set; } = "Red";
    [DataMember(Order = 10)] public string PerKeyAnimation { get; set; } = "None";
    [DataMember(Order = 11)] public int PerKeyBrightness { get; set; } = 100;
    [DataMember(Order = 12)] public int PerKeySpeed { get; set; } = 1;

    // === 场景系统扩展 ===
    /// <summary>性能模式触发: 匹配 ConfigService.<see cref="OmenSuperHub.Services.ConfigService.PerformanceMode"/> 值时自动激活</summary>
    [DataMember(Order = 13)] public string TriggerMode { get; set; } // null="手动", "Turbo"/"Balanced"/"Quiet"/"Eco"
    /// <summary>是否为默认场景 (启动时激活)</summary>
    [DataMember(Order = 14)] public bool IsDefault { get; set; }
    /// <summary>是否为内置场景 (不可删除)</summary>
    [DataMember(Order = 15)] public bool IsBuiltIn { get; set; }
    /// <summary>定时激活时间 "HH:mm" (null=不定时)</summary>
    [DataMember(Order = 16)] public string ScheduledTime { get; set; }
    /// <summary>定时生效的星期几 (0=Sun, 空=每天)</summary>
    [DataMember(Order = 17)] public int[] ScheduledDays { get; set; }
    // 灯条独立面板快照 — 与键盘 ZoneColors 分离
    [DataMember(Order = 18)] public string[] LightBarColors { get; set; }
    [DataMember(Order = 19)] public int LightBarBrightness { get; set; } = 100;

    public LightingScene Clone(string newName = null) {
      var c = (LightingScene)MemberwiseClone();
      c.Id = Guid.NewGuid().ToString("N").Substring(0, 8);
      c.Name = newName ?? Name + " (副本)";
      c.IsBuiltIn = false;
      c.TriggerMode = null;      // 副本默认不自动触发
      c.IsDefault = false;
      if (ZoneColors != null) c.ZoneColors = (string[])ZoneColors.Clone();
      if (ScheduledDays != null) c.ScheduledDays = (int[])ScheduledDays.Clone();
      return c;
    }
  }

  /// <summary>场景文件根对象 — lighting_scenes.json</summary>
  [DataContract]
  public class LightingSceneFile {
    [DataMember(Order = 0)] public LightingScene[] Scenes { get; set; } = Array.Empty<LightingScene>();
    [DataMember(Order = 1)] public string ActiveSceneId { get; set; }
    [DataMember(Order = 2)] public bool Enabled { get; set; } = true;
  }
}
