// LightingSceneService.cs - 灯光场景管理服务
// 参考 OmenCore RgbSceneService: 场景 CRUD + 切换应用 + 性能模式联动 + 时间调度
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Timers;
using OmenSuperHub.Models;
using OmenSuperHub.Pages;
using Timer = System.Timers.Timer;

namespace OmenSuperHub.Services {
  public static class LightingSceneService {
    static readonly string ScenesJsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lighting_scenes.json");
    static LightingSceneFile _file;
    static readonly object _lock = new();
    static Timer _scheduleTimer;
    static string _lastPerfMode;

    // 预设键名直接作为 TriggerMode — 无需映射
    /// <summary>预设切换时调用 — 直接用预设键名匹配场景 TriggerMode</summary>
    internal static void NotifyPresetChanged(string presetKey) {
      OnPerformanceModeChanged(presetKey);
    }

    /// <summary>当前激活的场景 (null=未加载)</summary>
    public static LightingScene ActiveScene {
      get {
        lock (_lock) {
          if (_file == null) return null;
          return _file.Scenes.FirstOrDefault(s => s.Id == _file.ActiveSceneId);
        }
      }
    }

    /// <summary>所有场景列表</summary>
    public static LightingScene[] AllScenes {
      get { lock (_lock) { return _file?.Scenes ?? Array.Empty<LightingScene>(); } }
    }

    /// <summary>灯光总开关</summary>
    public static bool Enabled {
      get { lock (_lock) { return _file?.Enabled ?? false; } }
      set {
        lock (_lock) { if (_file != null) { _file.Enabled = value; Save(); } }
        // ponytail: 总开关双向联动 60s 定时调度器 — 关灯时调度器跑也是空 tick (ActivateScene
        // 内已 gate _file.Enabled),把调度器一并停掉彻底零开销;开灯时再拉起。锁外调
        // StartScheduler/StopScheduler — 它们只动 _scheduleTimer,不动 _lock,锁外安全。
        if (value) StartScheduler(); else StopScheduler();
      }
    }

    public static event Action<LightingScene> SceneChanged;
    public static event Action<LightingScene[]> ScenesListChanged;

    // ═══════════════════════════════════════════════════════════════
    // 初始化
    // ═══════════════════════════════════════════════════════════════

    /// <summary>加载场景文件,不存在则从旧 lighting.json 迁移或创建内置场景</summary>
    public static void Initialize() {
      lock (_lock) {
        if (File.Exists(ScenesJsonPath)) {
          Load();
          // 验证激活场景仍存在
          if (_file.Scenes.All(s => s.Id != _file.ActiveSceneId) && _file.Scenes.Length > 0)
            _file.ActiveSceneId = _file.Scenes[0].Id;
        } else {
          // ponytail: 从旧 lighting.json 迁移或创建默认内置场景
          MigrateFromOldFormat();
        }
      }
      // 事件：通知 UI 场景列表已变更
      ScenesListChanged?.Invoke(AllScenes);
      // ponytail: 此时 _file 已加载到内存,真值 _file.Enabled 已知 — 开关开时才拉起
      // 60s 定时调度器。App 启动期已不再无条件 StartScheduler(原 OnStartup L83 已删)。
      // 后续页切总开关由 Enabled setter 双向联动启停此调度器。
      if (Enabled) StartScheduler();
    }

    static void MigrateFromOldFormat() {
      try {
        // 尝试读取旧 lighting.json → 作为默认场景导入
        var oldState = LightingPage.LoadLightingJsonInternal();
        var defaultScene = LightingStateToScene(oldState, "omendefault", "OMEN 默认", isDefault: true, isBuiltIn: true);
        var scenes = new List<LightingScene> { defaultScene };
        // 添加内置场景模板
        scenes.AddRange(CreateBuiltInScenes());
        _file = new LightingSceneFile { Scenes = scenes.ToArray(), ActiveSceneId = defaultScene.Id, Enabled = oldState.Enabled };
      } catch {
        // 旧格式读取失败 → 纯内置场景
        _file = new LightingSceneFile {
          Scenes = CreateBuiltInScenes(),
          ActiveSceneId = "omenred",
          Enabled = true
        };
      }
      Save();
    }

    static LightingScene LightingStateToScene(LightingState state, string id, string name, bool isDefault, bool isBuiltIn) {
      return new LightingScene {
        Id = id, Name = name, IsDefault = isDefault, IsBuiltIn = isBuiltIn,
        Device = state.Device ?? "keyboard",
        Interface = state.Interface ?? "BasicFourZone",
        Brightness = state.Brightness,
        Animation = state.Animation ?? "None",
        Direction = state.Direction ?? "Left",
        Theme = state.Theme ?? "Galaxy",
        ZoneColors = state.ZoneColors ?? new[] { "#FF0000", "#FF0000", "#FF0000", "#FF0000" },
        PerKeyStaticColor = state.PerKeyStaticColor ?? "Red",
        PerKeyAnimation = state.PerKeyAnimation ?? "None",
        PerKeyBrightness = state.PerKeyBrightness,
        PerKeySpeed = state.PerKeySpeed,
        LightBarColors = state.LightBarColors,
        LightBarBrightness = state.LightBarBrightness,
      };
    }

    /// <summary>--selfcheck 断言 — 内置场景出厂不得携带自动触发规则,防 issue #19 回归</summary>
    internal static string SelfCheck() {
      var fails = new List<string>();
      var scenes = CreateBuiltInScenes();
      var ids = new HashSet<string>();
      foreach (var s in scenes) {
        if (!ids.Add(s.Id)) fails.Add($"场景 id 重复: {s.Id}");
        if (!string.IsNullOrEmpty(s.TriggerMode)) fails.Add($"内置场景 {s.Id} 出厂携带 TriggerMode={s.TriggerMode}");
        if (!string.IsNullOrEmpty(s.ScheduledTime)) fails.Add($"内置场景 {s.Id} 出厂携带 ScheduledTime={s.ScheduledTime}");
      }
      if (scenes.Length == 0) fails.Add("无内置场景");
      // ponytail: 能力探测降级断言 — Kind==Normal 只能来自确认探测(Detected==true),
      // 否则普通键盘误判会隐藏 RGB 机型灯光页。探测本身不得抛异常。
      try {
        var cap = OmenSuperHub.OmenLighting.DetectKeyboardCapability();
        if (cap == null) fails.Add("能力探测返回 null");
        else if (cap.Kind == OmenSuperHub.OmenLighting.KeyboardKind.Normal && !cap.Detected)
          fails.Add("能力探测: 未确认探测(Detected=false)却判 Normal — 会误隐藏灯光页");
      } catch (Exception ex) {
        fails.Add($"能力探测抛异常: {ex.Message}");
      }
      return fails.Count == 0 ? "PASS LightingSceneService: 内置场景无自动触发规则 + 能力探测降级安全"
        : "FAIL LightingSceneService: " + string.Join("; ", fails);
    }

    /// <summary>参考 OmenCore RgbSceneService 内置场景</summary>
    // ponytail: 内置场景出厂不带 TriggerMode/ScheduledTime —— 联动必须用户在场景卡里
    // 主动指定 (issue #19)。之前内置场景预绑 Extreme/GpuPriority/LightUse + 22:00 定时,
    // 导致切换/恢复预设或首次进入灯光页时 ActivateScene 用内置场景覆写 ConfigService
    // 和 lighting.json,用户自 saved 的灯光配置"重启后失效"。Load() 的合并逻辑会用这里
    // 的 null 治愈旧 lighting_scenes.json 里预绑的触发规则。
    static LightingScene[] CreateBuiltInScenes() {
      return new[] {
        new LightingScene { Id = "omenred", Name = "OMEN Red", IsBuiltIn = true, IsDefault = true,
          Animation = "None", ZoneColors = new[] { "#FF0000", "#FF0000", "#FF0000", "#FF0000" }, Theme = "Custom" },
        new LightingScene { Id = "gaming", Name = "游戏模式", IsBuiltIn = true,
          Animation = "Breathing", ZoneColors = new[] { "#FF2200", "#FF2200", "#FF4400", "#FF4400" },
          Theme = "Volcano" },
        new LightingScene { Id = "balanced", Name = "平衡模式", IsBuiltIn = true,
          Animation = "ColorCycle", ZoneColors = new[] { "#00FF88", "#00CCFF", "#0088FF", "#4400FF" },
          Theme = "Ocean" },
        new LightingScene { Id = "quiet", Name = "静音模式", IsBuiltIn = true,
          Animation = "Starlight", ZoneColors = new[] { "#00FF44", "#0044FF", "#00FF44", "#0044FF" },
          Theme = "Jungle" },
        new LightingScene { Id = "night", Name = "夜间模式", IsBuiltIn = true,
          Animation = "None", Brightness = 20,
          ZoneColors = new[] { "#FF6600", "#FF6600", "#FF6600", "#FF6600" }, Theme = "Custom" },
        new LightingScene { Id = "rainbow", Name = "彩虹光谱", IsBuiltIn = true,
          Animation = "Wave", ZoneColors = new[] { "#FF0000", "#00FF00", "#0000FF", "#FF00FF" }, Theme = "Galaxy" },
      };
    }

    public static string GetSceneDisplayName(LightingScene scene) {
      if (scene == null) return "";
      string suffix = "";
      if (scene.IsDefault) suffix += " ★";
      if (!string.IsNullOrEmpty(scene.TriggerMode)) suffix += $" [{scene.TriggerMode}]";
      if (!string.IsNullOrEmpty(scene.ScheduledTime)) suffix += $" @{scene.ScheduledTime}";
      return scene.Name + suffix;
    }

    // ═══════════════════════════════════════════════════════════════
    // 场景 CRUD
    // ═══════════════════════════════════════════════════════════════

    /// <summary>从当前页面状态创建新场景</summary>
    public static LightingScene CreateSceneFromCurrent(string name, LightingState state) {
      return LightingStateToScene(state, Guid.NewGuid().ToString("N").Substring(0, 8), name, isDefault: false, isBuiltIn: false);
    }

    /// <summary>添加场景</summary>
    public static void AddScene(LightingScene scene) {
      lock (_lock) {
        var list = (_file?.Scenes ?? Array.Empty<LightingScene>()).ToList();
        list.Add(scene);
        _file.Scenes = list.ToArray();
        Save();
      }
      ScenesListChanged?.Invoke(AllScenes);
    }

    /// <summary>更新已有场景 (按 Id 匹配)</summary>
    public static void UpdateScene(LightingScene scene) {
      lock (_lock) {
        var list = (_file?.Scenes ?? Array.Empty<LightingScene>()).ToList();
        int idx = list.FindIndex(s => s.Id == scene.Id);
        if (idx < 0) { list.Add(scene); } else { list[idx] = scene; }
        _file.Scenes = list.ToArray();
        Save();
      }
      ScenesListChanged?.Invoke(AllScenes);
    }

    /// <summary>删除场景 (内置场景不可删)</summary>
    public static bool RemoveScene(string sceneId) {
      lock (_lock) {
        var list = (_file?.Scenes ?? Array.Empty<LightingScene>()).ToList();
        var scene = list.FirstOrDefault(s => s.Id == sceneId);
        if (scene == null || scene.IsBuiltIn) return false;
        list.Remove(scene);
        _file.Scenes = list.ToArray();
        if (_file.ActiveSceneId == sceneId && list.Count > 0)
          _file.ActiveSceneId = list[0].Id;
        Save();
      }
      ScenesListChanged?.Invoke(AllScenes);
      return true;
    }

    // ═══════════════════════════════════════════════════════════════
    // 场景切换
    // ═══════════════════════════════════════════════════════════════

    /// <summary>激活场景并应用到硬件 — 参考 OmenCore ApplySceneAsync</summary>
    public static bool ActivateScene(string sceneId, string trigger = "manual") {
      LightingScene scene;
      lock (_lock) {
        scene = _file.Scenes.FirstOrDefault(s => s.Id == sceneId);
        if (scene == null) return false;
        _file.ActiveSceneId = sceneId;
        Save();
      }

      // 应用到 ConfigService (LightingPage 从 ConfigService 读)
      ApplySceneToConfig(scene);

      // 同步到旧 lighting.json (向后兼容 ReplaySavedLighting)
      SaveLegacyLightingJson();

      // 如果灯光已开启,立即应用到硬件
      // ponytail: ReplaySavedLighting 走 WMI/HID 同步 IO(冷启动可达数秒) — 预设切换
      // 路径上它在 UI 线程的 Dispatcher.Invoke 里执行,会让主窗口/托盘假死(issue #20
      // "OMEN 键切换后软件打不开")。移到 ThreadPool,与 App 启动重放同款做法。
      if (_file.Enabled) {
        System.Threading.ThreadPool.QueueUserWorkItem(_ => {
          try { LightingPage.ReplaySavedLighting(); } catch { }
        });
      }

      SceneChanged?.Invoke(scene);
      Logger.Info($"Scene activated: {scene.Name} (trigger={trigger})");
      return true;
    }

    static void ApplySceneToConfig(LightingScene scene) {
      ConfigService.LightingDevice = scene.Device;
      ConfigService.LightingInterface = scene.Interface;
      ConfigService.LightingBrightness = (byte)scene.Brightness;
      ConfigService.LightingAnimation = scene.Animation;
      ConfigService.LightingDirection = scene.Direction;
      ConfigService.LightingTheme = scene.Theme;
      ConfigService.PerKeyStaticColor = scene.PerKeyStaticColor;
      ConfigService.PerKeyAnimation = scene.PerKeyAnimation;
      ConfigService.PerKeyBrightness = (byte)scene.PerKeyBrightness;
      ConfigService.PerKeySpeed = (byte)scene.PerKeySpeed;
      ConfigService.Save(); // null → 全量保存
    }

    /// <summary>写回旧 lighting.json — 保证 ReplaySavedLighting 启动恢复路径兼容</summary>
    static void SaveLegacyLightingJson() {
      try {
        var scene = ActiveScene;
        if (scene == null) return;
        // 保留当前 lighting.json 的 Enabled 状态 — 切换场景不应关闭灯光总开关
        bool currentEnabled = LightingPage.LoadLightingJsonInternal().Enabled;
        var state = new LightingState {
          Enabled = currentEnabled,
          Device = scene.Device,
          Interface = scene.Interface,
          Brightness = scene.Brightness,
          Animation = scene.Animation,
          Direction = scene.Direction,
          Theme = scene.Theme,
          ZoneColors = scene.ZoneColors,
          PerKeyStaticColor = scene.PerKeyStaticColor,
          PerKeyAnimation = scene.PerKeyAnimation,
          PerKeyBrightness = scene.PerKeyBrightness,
          PerKeySpeed = scene.PerKeySpeed,
          LightBarColors = scene.LightBarColors,
          LightBarBrightness = scene.LightBarBrightness,
        };
        LightingPage.SaveLightingJsonInternal(state);
      } catch { }
    }

    // ═══════════════════════════════════════════════════════════════
    // 性能模式联动
    // ═══════════════════════════════════════════════════════════════

    /// <summary>性能模式变更时调用 — 参考 OmenCore OnPerformanceModeChangedAsync</summary>
    public static void OnPerformanceModeChanged(string newMode) {
      if (newMode == _lastPerfMode) return;
      _lastPerfMode = newMode;

      lock (_lock) {
        if (_file == null) return;
        // 查找 TriggerMode 匹配的场景
        var matching = _file.Scenes.FirstOrDefault(s =>
          !string.IsNullOrEmpty(s.TriggerMode) &&
          s.TriggerMode.Equals(newMode, StringComparison.OrdinalIgnoreCase));
        if (matching != null && matching.Id != _file.ActiveSceneId) {
          ActivateScene(matching.Id, "performance");
        }
      }
    }

    // ═══════════════════════════════════════════════════════════════
    // 定时调度
    // ═══════════════════════════════════════════════════════════════

    /// <summary>启动定时调度器 (每分钟检查 ScheduledTime 匹配)</summary>
    public static void StartScheduler() {
      StopScheduler();
      _scheduleTimer = new Timer(60000) { AutoReset = true };
      _scheduleTimer.Elapsed += (s, e) => CheckScheduledScenes();
      _scheduleTimer.Start();
    }

    public static void StopScheduler() {
      if (_scheduleTimer != null) { _scheduleTimer.Stop(); _scheduleTimer.Dispose(); _scheduleTimer = null; }
    }

    static void CheckScheduledScenes() {
      var now = DateTime.Now;
      string timeKey = now.ToString("HH:mm");
      int dayOfWeek = (int)now.DayOfWeek;

      lock (_lock) {
        if (_file == null) return;
        var scheduledScene = _file.Scenes.FirstOrDefault(s =>
          s.ScheduledTime == timeKey &&
          (s.ScheduledDays == null || s.ScheduledDays.Length == 0 || s.ScheduledDays.Contains(dayOfWeek)));
        if (scheduledScene != null && scheduledScene.Id != _file.ActiveSceneId) {
          ActivateScene(scheduledScene.Id, "schedule");
        }
      }
    }

    // ═══════════════════════════════════════════════════════════════
    // JSON 持久化
    // ═══════════════════════════════════════════════════════════════

    static void Load() {
      try {
        var ser = new DataContractJsonSerializer(typeof(LightingSceneFile));
        using (var ms = new MemoryStream(File.ReadAllBytes(ScenesJsonPath)))
          _file = ser.ReadObject(ms) as LightingSceneFile;
        if (_file?.Scenes == null || _file.Scenes.Length == 0) {
          _file = new LightingSceneFile { Scenes = CreateBuiltInScenes(), ActiveSceneId = _file?.Scenes?.FirstOrDefault()?.Id ?? "omenred" };
        }
        // 还原内置场景定义 (确保内置场景存在且标记 IsBuiltIn)
        var builtIns = CreateBuiltInScenes();
        var merged = new List<LightingScene>();
        foreach (var bi in builtIns) {
          var existing = _file.Scenes.FirstOrDefault(s => s.Id == bi.Id);
          if (existing != null) {
            // ponytail: 只还原 IsBuiltIn 标记,不覆写 TriggerMode/ScheduledTime ——
            // 联动规则是用户 opt-in (场景卡指定),启动时强制还原会把它抹掉。
            existing.IsBuiltIn = true;
            merged.Add(existing);
          } else {
            merged.Add(bi);
          }
        }
        // 追加用户自定义场景
        foreach (var s in _file.Scenes) {
          if (!builtIns.Any(bi => bi.Id == s.Id))
            merged.Add(s);
        }
        _file.Scenes = merged.ToArray();
      } catch (Exception ex) {
        Logger.Error($"LightingSceneService.Load: {ex.Message}");
        _file = new LightingSceneFile { Scenes = CreateBuiltInScenes(), ActiveSceneId = "omenred", Enabled = true };
      }
    }

    static void Save() {
      try {
        var ser = new DataContractJsonSerializer(typeof(LightingSceneFile));
        using (var ms = new MemoryStream()) {
          ser.WriteObject(ms, _file);
          File.WriteAllBytes(ScenesJsonPath, ms.ToArray());
        }
      } catch (Exception ex) { Logger.Error($"LightingSceneService.Save: {ex.Message}"); }
    }
  }
}
