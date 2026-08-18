// LightingPage.cs - 键盘灯效页面
// 设备/协议选择，区域颜色设置，亮度/动画速度调节
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Hp.Bridge.Client.SDKs.McuSDK2;
using Hp.Bridge.Client.SDKs.McuSDK2.Common.DataStructure; // LightingSetting
using Hp.Bridge.Client.SDKs.McuSDK2.Keyboard;  // LightingAudioEffectSetting
using OmenSuperHub.Models;
using OmenSuperHub.Services;
using OmenSuperHub.Utils;
using static OmenSuperHub.OmenLighting;

namespace OmenSuperHub.Pages {
  public partial class LightingPage : Page {
    bool _loading;
    bool _colorPicking;
    // ponytail: 实验性首次开灯提示一次性 gate。CachedPageService 缓存本页实例,本窗口生命周期内
    // Loaded 只在首次进入时触发一次(后续切回不重新 Loaded — NavigationView KeepNavJournal)。
    // 跨窗口重启时是新的 Page 实例, _experimentalPromptShown 重回 false 会再提示, 但
    // ConfigService.LightingUseOfficial 已持久化时直接早退不弹。
    bool _experimentalPromptShown;
    Color[] _zoneColors = new Color[] { Colors.White, Colors.White, Colors.White, Colors.White };
    int[] _lastZoneIdx = new int[] { 0, 0, 0, 0 };

    // ponytail: PerKey HID handle cache. Previously each click of Apply ran
    // OpenPerKeyKeyboard→SetPerKey*→StorePerKeyToFlash→CloseDeviceAsync — full
    // handshake every interaction, and the .Wait() on UI thread could deadlock
    // because the SDK async methods don't ConfigureAwait(false). Cache one
    // open handle for the page's lifetime; SelectionChanged handlers use it
    // for live updates without an explicit Apply button press, and page Unloaded
    // closes it. Locked because the SelectionChanged handlers can fire from any
    // UI-thread re-entrancy; OpenPerKeyKeyboard itself is already ThreadPool-
    // dispatched (see OmenLighting.OpenHidDevice), so cached value is just an int.
    int _perKeyHandle = -1;
    // _destroyed guards against CloseDeviceAsync racing with Unloaded + reload
    // (the page can be re-instantiated when the user switches tabs).
    bool _perKeyDestroyed;
    readonly object _perKeyLock = new();

    // ponytail: 硬件能力一次性探测 — 统一走 OmenLighting.DetectKeyboardCapability()
    // (App 启动时后台已探测并缓存,这里直接取缓存值,不再各自打 WMI/HP SDK)。
    bool _supportAni;
    bool _supportLightBar;
    KeyboardKind _kbKind = KeyboardKind.FourZone;
    bool _kbKindDetected;

    // 灯条独立 4 段色 (区别于键盘 _zoneColors)
    Color[] _lbColors = new Color[] { Color.FromRgb(255, 0, 0), Color.FromRgb(255, 0, 0), Color.FromRgb(255, 0, 0), Color.FromRgb(255, 0, 0) };
    bool _lbLoading;
    // ponytail: 灯带亮度真值 — 滑条只有 0-100,Dojo 高亮度直写档(128/228)超出滑条量程,
    // 所有下发/持久化统一读此值;滑条与直写按钮都只改它。
    int _lbBrightness = 100;

    public LightingPage() {
      // ponytail: 根因 —— XAML 反序列化期间 Slider 的 Value="100" 触发 ValueChanged 事件 (LightBright_Changed /
      // PerKeyBright_Changed), 此时 _loading 默认 false,事件 handler 进入 ConfigService.Save 或 EnsurePerKeyHandle
      // side-effect 路径, 抛异常被 WPF 包装成 "RangeBase.Value 设置引发异常" → NavigationView 静默吞掉 → 侧栏点击无响应。
      // 在 InitializeComponent 之前 gate=true, 让 Deserialize 期间的 ValueChanged / SelectionChanged 直接跳 side-effect;
      // Loaded 后再 Initialize: false → LoadState() → false, 之后用户交互照常触发 side-effect。
      _loading = true;
      InitializeComponent();
      Loaded += (s, e) => {
        // ponytail: NavigationView Keeps 3-page journal so this page is reused on
        // back-nav. Reset the destruction flag in Loaded so Unloaded (which sets it
        // true + closes the handle) doesn't leave the next visit permanently inert.
        _perKeyDestroyed = false;
        _loading = true; LoadState(); _loading = false;
        // ponytail: 首次渲染对齐窗口宽度,否则窄屏下 VSM 仍处于默认 Wide 态
        ApplyLayoutStates(ActualWidth);
        // ponytail: Loaded 期间能力门控批量切 Visibility + PlaceLightBarPanel 搬移面板后,
        // NavigationView 过渡里偶发不跑布局 → 页面空白、点击才出现(与核心保持二级页面
        // 当初同款症状)。显式 UpdateLayout 强制一遍 — 对齐 CoreKeepPage_Loaded 的修法。
        UpdateLayout();
        // ponytail: 实验性首次开灯提示 — 选"官方灯效软件"持久化 LightingUseOfficial=true 隐藏侧栏灯光项
        // + 持久关 LightingState 总开关(顺带让下次启动 ReplaySavedLighting 在 lj.Enabled=false 处早退,
        // 零硬件写入)。总开关切 off 走现有 LightEnable_Changed handler 即一并停三 timer + SetZoneOff,
        // 不重写已有停 timer 逻辑(参考 LightEnable_Changed off 路径)。延迟到 Loaded priority 之后弹,
        // 避免 NavigationView 过渡动画期间叠一个模态窗。
        if (!_experimentalPromptShown) {
          _experimentalPromptShown = true;
          Dispatcher.BeginInvoke(new Action(() => {
            if (ConfigService.LightingUseOfficial) return;  // 已选过官方: 理论不该再进灯光页(侧栏已隐藏),仅防御
            bool cont = Utils.DialogHelper.Choice(
              Strings.LightingExperimentalPrompt, Strings.LightingExperimentalTitle,
              Strings.LightingExperimentalContinue, Strings.LightingExperimentalOfficial);
            if (cont) return;  // 继续使用: 保留当前页面状态
            ConfigService.LightingUseOfficial = true;
            ConfigService.Save("LightingUseOfficial");
            // 总开关本就关: 直接补停三 timer(防 LightBarAnim/TempMode 子开关独立启停留下来的 timer);
            // 总开关开: 切 off 让同一 handler 接管(off 路径已含三 timer 停 + SetZoneOff + SaveLightingJson)。
            if (LightEnableToggle?.IsChecked == true) {
              LightEnableToggle.IsChecked = false;  // _loading=false → LightEnable_Changed 已接管停 timer + 关输出
            } else {
              LightingSceneService.Enabled = false;
              try { Services.LightingAnimationService.Stop(); } catch { }
              try { Services.LightingTemperatureService.Stop(); } catch { }
              SaveLightingJson();  // 落 Enabled=false (LightEnableToggle.IsChecked==false)
            }
            Pages.SettingsPage.ScrollToStubOnNextLoad = true;
            Views.MainWindow.NavigateToPage("Settings");
            Views.MainWindow.UpdateNavigationItems();
          }), System.Windows.Threading.DispatcherPriority.Loaded);
        }
      };
      Unloaded += (s, e) => {
        ClosePerKeyHandleLocked();
        // ponytail: 订阅在 LoadState 后做,但 Unloaded 没有对称取消 — CachedPageService 缓存
        // 本页时来回切导航会持续叠加 SceneChanged/ScenesListChanged 订阅,每次场景切换回调
        // 被多次触发(回调体里 BuildState 重排 UI)。Loaded 时 LoadState 会再订阅,所以这里
        // 安全 -= 一次;第二次 -= 撤不到任何东西也不抛。
        LightingSceneService.SceneChanged -= OnSceneChanged;
        LightingSceneService.ScenesListChanged -= OnScenesListChanged;
      };
    }

    // ponytail: load zone colors + toggle from JSON — ConfigService can't store per-zone hex or a master toggle
    static readonly string LightingJsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lighting.json");

    void LoadState() {
      // ponytail: JSON 补充 ConfigService 不能存的数据 — toggle 开关 + 4 区独立颜色
      var lj = LoadLightingJson();
      if (LightEnableToggle != null) LightEnableToggle.IsChecked = lj.Enabled;
      if (lj.ZoneColors != null && lj.ZoneColors.Length == 4) {
        for (int i = 0; i < 4; i++)
          _zoneColors[i] = ParseHexColor(lj.ZoneColors[i]);
      }

      LightDevCombo.SelectedIndex = ConfigService.LightingDevice == "lightbar" ? 1 : 0;
      // ponytail: restore protocol, 0=Basic 1=Dojo 2=HpSdk 3=PerKey
      LightProtoCombo.SelectedIndex = ConfigService.LightingInterface switch {
        "Dojo" => 1, "HpSdk" => 2, "PerKey" => 3, _ => 0
      };
      LightBrightSlider.Value = ConfigService.LightingBrightness;
      LightBrightVal.Text = ConfigService.LightingBrightness + "%";
      int animIdx = OmenLighting.AnimIndex(ConfigService.LightingAnimation);
      if (animIdx >= 0) AnimCombo.SelectedIndex = animIdx;
      else AnimCombo.SelectedIndex = 0;
      AnimSpeedCombo.SelectedIndex = 1;
      AnimDirCombo.SelectedIndex = ConfigService.LightingDirection == "Right" ? 1 : 0;
      AnimThemeCombo.SelectedIndex = ConfigService.LightingTheme switch {
        "Volcano" => 1, "Jungle" => 2, "Ocean" => 3, "Custom" => 4, _ => 0
      };

      // ponytail: 统一能力探测 — 取 App 启动时的缓存(DetectKeyboardCapability 惰性+锁,首次冷调用也安全)
      var cap = OmenLighting.DetectKeyboardCapability();
      _kbKind = cap.Kind;
      _kbKindDetected = cap.Detected;
      _supportAni = cap.AnimationSupported;
      _supportLightBar = cap.LightBarSupported;

      // ponytail: auto-detect keyboard type and light bar via HP SDK if available
      if (FourZoneHelper.Available) {
        string kbTypeName = _kbKind switch {
          KeyboardKind.PerKey => Strings.KbTypeRgbPerKey,
          KeyboardKind.OneZone => Strings.KbTypeOneZoneWithoutNumpad,
          KeyboardKind.LightBarOnly => Strings.LightingLightBarTitle,
          KeyboardKind.Normal => Strings.KbTypeNormal,
          _ => Strings.KbTypeFourZoneWithoutNumpad,
        };
        HeadingLighting.Text = $"{Strings.LightingControl} — {kbTypeName}" +
          (_supportLightBar ? $" | {Strings.LightingLightBar}" : "");
      }

      // ponytail: 探测结果只复位本会话 UI,不再回写 ConfigService —— 探测在 WMI/HID
      // 冷启动或瞬时失败时返回 false,旧逻辑会把用户保存的动画/设备/协议直接清掉并落盘,
      // 表现为"重启程序后灯光配置失效"(issue #19)。UI 侧 None/keyboard/BasicFourZone
      // 已保证本会话不发不支持的指令;探测恢复后下次进页面自动回到保存值。
      if (!_supportAni && ConfigService.LightingAnimation != "None") {
        if (AnimCombo != null) AnimCombo.SelectedIndex = 0;
      }

      ApplyCapabilityLayout();

      // ponytail: 首次启动自动检测协议 — lighting.json 不存在说明用户从未操作过灯光页,
      // 此时根据硬件自动推荐最佳协议,避免用户手动试错。
      if (!File.Exists(LightingJsonPath)) {
        string autoProto = OmenLighting.AutoDetectProtocol();
        int autoIdx = autoProto switch { "Dojo" => 1, "HpSdk" => 2, "PerKey" => 3, _ => 0 };
        if (autoIdx != LightProtoCombo.SelectedIndex) {
          LightProtoCombo.SelectedIndex = autoIdx;
          ConfigService.LightingInterface = autoProto;
          ConfigService.Save("LightingInterface");
          ApplyCapabilityLayout();
        }
      }

      // ponytail: 场景系统初始化 — 参考 OmenCore RgbSceneService
      // 首次调用 Initialize → 迁移旧 lighting.json 或创建内置场景
      LightingSceneService.Initialize();
      LightingSceneService.SceneChanged += OnSceneChanged;
      LightingSceneService.ScenesListChanged += OnScenesListChanged;
      RefreshSceneCombo();
      // 根据当前预设自动激活对应灯光场景 (初始化时 _file 才加载，补首次进页面的联动)
      LightingSceneService.NotifyPresetChanged(ConfigService.Preset);

      // 从激活场景恢复 UI 状态 (ConfigService 已由 ActivateScene 写入)
      var active = LightingSceneService.ActiveScene;
      if (active != null) {
        // 恢复 4 区颜色
        if (active.ZoneColors != null) {
          for (int i = 0; i < Math.Min(4, active.ZoneColors.Length); i++)
            _zoneColors[i] = ParseHexColor(active.ZoneColors[i]);
        }
        UpdateZonePreview();
      }

      // ponytail: restore PerKey persisted state (only used when the PerKey card is visible).
      // Use OmenLighting.AnimNames as the canonical order so XAML ComboBox + config stay aligned.
      string[] pkStatics = { "Red", "Green", "Blue", "White", "Cyan", "Pink", "Yellow",
        "IceBlue", "CoolGreen", "WarmYellow", "FieryOrange", "HotRed" };
      int spk = Array.IndexOf(pkStatics, ConfigService.PerKeyStaticColor);
      if (PerKeyStaticCombo != null) PerKeyStaticCombo.SelectedIndex = spk >= 0 ? spk : 0;
      int apk = OmenLighting.AnimIndex(ConfigService.PerKeyAnimation);
      if (PerKeyAnimCombo != null) PerKeyAnimCombo.SelectedIndex = apk >= 0 ? apk : 0;
      if (PerKeySpeedCombo != null && ConfigService.PerKeySpeed < PerKeySpeedCombo.Items.Count)
        PerKeySpeedCombo.SelectedIndex = ConfigService.PerKeySpeed;
      if (PerKeyBrightSlider != null) {
        PerKeyBrightSlider.Value = ConfigService.PerKeyBrightness;
        PerKeyBrightVal.Text = ConfigService.PerKeyBrightness + "%";
      }

      // ponytail: restore zone ComboBox positions to match _zoneColors loaded from JSON
      ComboBox[] zoneCombos = { Zone1Combo, Zone2Combo, Zone3Combo, Zone4Combo };
      string[] presetNames = { "Red", "Green", "Blue", "White", "Cyan", "Pink", "Yellow",
        "IceBlue", "CoolGreen", "WarmYellow", "FieryOrange", "HotRed" };
      for (int i = 0; i < 4 && i < zoneCombos.Length; i++) {
        var c = _zoneColors[i];
        bool matched = false;
        for (int j = 0; j < presetNames.Length; j++) {
          var (r, g, b) = OmenLighting.LookupColor(presetNames[j]);
          if (r == c.R && g == c.G && b == c.B) {
            zoneCombos[i].SelectedIndex = j;
            _lastZoneIdx[i] = j;
            matched = true;
            break;
          }
        }
        if (!matched && zoneCombos[i].Items.Count > 0) {
          int customIdx = zoneCombos[i].Items.Count - 1;
          zoneCombos[i].SelectedIndex = customIdx;
          _lastZoneIdx[i] = customIdx;
          if (zoneCombos[i].Items[customIdx] is ComboBoxItem item)
              item.Content = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        }
      }
      // 恢复温度联动模式状态
      if (TempModeToggle != null) {
        TempModeToggle.IsChecked = ConfigService.LightingTempMode;
        if (ConfigService.LightingTempMode)
          LightingTemperatureService.Start();
      }
      UpdateZonePreview();

      // 逐键布局 + 灯条面板构建(各自幂等,只建一次)
      InitPerKeyPicker();
      InitLightBarPanel();

      // 灯条持久化恢复 — 4 段色 hex + 亮度(与键盘 zone 分离存储)
      if (lj.LightBarColors != null && lj.LightBarColors.Length == 4) {
        _lbLoading = true;
        var lbCombos = new[] { LightBarSeg1Combo, LightBarSeg2Combo, LightBarSeg3Combo, LightBarSeg4Combo };
        for (int i = 0; i < 4; i++) {
          _lbColors[i] = ParseHexColor(lj.LightBarColors[i]);
          // 按预设 RGB 反查 combo 选中项(自定义 hex 落第一项,预览仍以 _lbColors 为准)
          if (lbCombos[i] != null) {
            bool matched = false;
            for (int j = 0; j < lbCombos[i].Items.Count; j++) {
              if (lbCombos[i].Items[j] is ComboBoxItem ci && ci.Tag is string cn) {
                var (r, g, b) = OmenLighting.LookupColor(cn);
                if (r == _lbColors[i].R && g == _lbColors[i].G && b == _lbColors[i].B) {
                  lbCombos[i].SelectedIndex = j; matched = true; break;
                }
              }
            }
            if (!matched) lbCombos[i].SelectedIndex = 0;
          }
        }
        // 恢复亮度真值:直写档(128/228)超滑条量程,滑条只吃到 100,文本显示真值
        _lbBrightness = Math.Min(228, Math.Max(0, lj.LightBarBrightness));
        if (LightBarBrightSlider != null) LightBarBrightSlider.Value = Math.Min(100, _lbBrightness);
        if (LightBarBrightVal != null) LightBarBrightVal.Text = _lbBrightness + "%";
        _lbLoading = false;
      }
      UpdateLightBarPreview();
    }

    void LightDev_SelectionChanged(object s, SelectionChangedEventArgs e) {
      if (_loading) return;
      ConfigService.LightingDevice = LightDevCombo.SelectedIndex == 1 ? "lightbar" : "keyboard";
      ConfigService.Save("LightingDevice");
      SaveLightingJson();
      // ponytail: 切换设备后立即下发当前颜色 — 解决"无论选键盘还是灯条,只调灯条颜色"的问题。
      // 用户切换 device 后无需再点 Apply 按钮即可看到效果。仅对 HP SDK 和 Dojo 协议生效
      // (BasicFourZone 的 ColorTable 写入较慢,且需要 Read-Modify-Write,留给 Apply 按钮处理)。
      // PerKey 协议走独立的 PerKeyLiveApply 路径,不在此处理。
      if (LightEnableToggle?.IsChecked != true) return;
      string iface = ConfigService.LightingInterface;
      if (iface != "HpSdk" && iface != "Dojo") return;
      try {
        var device = ConfigService.LightingDevice == "lightbar" ? LightingDevice.LightBar : LightingDevice.Keyboard;
        var colors = new List<System.Windows.Media.Color>(_zoneColors);
        byte bright = (byte)ConfigService.LightingBrightness;
        if (iface == "HpSdk") {
          OmenLighting.FourZoneHelper.SetStaticColor(device, colors, bright);
        } else {
          var ci = LightingControlInterface.Dojo;
          OmenLighting.SetZoneStaticColor(device, colors, bright, ci);
        }
      } catch (Exception ex) { Logger.Error($"LightDev live apply: {ex.Message}"); }
    }

    void LightProto_SelectionChanged(object s, SelectionChangedEventArgs e) {
      if (_loading) return;
      // ponytail: index 3 = PerKey protocol (added in this pass)
      ConfigService.LightingInterface = LightProtoCombo.SelectedIndex switch { 1 => "Dojo", 2 => "HpSdk", 3 => "PerKey", _ => "BasicFourZone" };
      ConfigService.Save("LightingInterface");
      ApplyCapabilityLayout();
      SaveLightingJson();
    }

    // ponytail: Auto-detect — probes kbType (Rgb→PerKey), cycle>260 (→Dojo), HP SDK (→HpSdk).
    // Reference: OmenCore KeyboardModelDatabase PreferredMethod logic.
    void BtnAutoDetect_Click(object sender, RoutedEventArgs e) {
      string proto = OmenLighting.AutoDetectProtocol();
      int idx = proto switch { "Dojo" => 1, "HpSdk" => 2, "PerKey" => 3, _ => 0 };
      _loading = true;
      LightProtoCombo.SelectedIndex = idx;
      _loading = false;
      ConfigService.LightingInterface = proto;
      ConfigService.Save("LightingInterface");
      ApplyCapabilityLayout();
      SaveLightingJson();
      // show detected protocol name + SystemID in the result TextBlock
      string name = idx switch { 1 => Strings.LightingProtoDojo, 2 => "HP SDK",
                                 3 => Strings.LightingPerKeyTitle, _ => Strings.LightingProtoBasic };
      if (AutoDetectResult != null) {
        string sysId = "";
        try { sysId = HP.Omen.Core.Model.Device.Models.DeviceModel.ThisSystemID; } catch { }
        string prefix = string.IsNullOrEmpty(sysId) ? "" : $"SysID {sysId} | ";
        AutoDetectResult.Text = prefix + string.Format(Strings.LightingAutoDetectResult, name);
        AutoDetectResult.Visibility = Visibility.Visible;
      }
    }

    void LightBright_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) {
      if (LightBrightVal != null) LightBrightVal.Text = (int)e.NewValue + "%";
      if (!_loading) {
        byte v = (byte)(int)e.NewValue;
        ConfigService.LightingBrightness = v;
        ConfigService.Save("LightingBrightness");
        SaveLightingJson();
        // ponytail: live brightness push — Dojo 单发亮度字节;HpSdk 走 SetStaticColor
        // (内部先亮度后颜色,参考 OmenCore ApplyProfileAsync 顺序,避免亮度写入清空颜色)。
        // PerKey 有独立滑条;BasicFourZone cmd 5 在部分机型不可靠,留给 Apply 按钮。
        if (LightEnableToggle?.IsChecked != true) return;
        string iface = ConfigService.LightingInterface;
        var device = ConfigService.LightingDevice == "lightbar" ? LightingDevice.LightBar : LightingDevice.Keyboard;
        try {
          if (iface == "Dojo") {
            OmenLighting.SetZoneBrightness(device, v, LightingControlInterface.Dojo);
          } else if (iface == "HpSdk") {
            var colors = new List<System.Windows.Media.Color>(_zoneColors);
            OmenLighting.FourZoneHelper.SetStaticColor(device, colors, v);
          }
        } catch (Exception ex) { Logger.Error($"LightBright live: {ex.Message}"); }
      }
    }

    void Anim_SelectionChanged(object s, SelectionChangedEventArgs e) {
      if (_loading || s is not ComboBox cb || cb.SelectedItem is not ComboBoxItem item) return;
      ConfigService.LightingAnimation = (string)item.Tag ?? "None";
      ConfigService.Save("LightingAnimation");
      ApplyCapabilityLayout();
      SaveLightingJson();
    }

    void AnimDir_SelectionChanged(object s, SelectionChangedEventArgs e) {
      if (_loading || _colorPicking) return;
      ConfigService.LightingDirection = AnimDirCombo.SelectedIndex == 1 ? "Right" : "Left";
      ConfigService.Save("LightingDirection");
      SaveLightingJson();
    }

    void AnimTheme_SelectionChanged(object s, SelectionChangedEventArgs e) {
      if (_loading || _colorPicking) return;
      string theme = AnimThemeCombo.SelectedIndex switch { 1 => "Volcano", 2 => "Jungle", 3 => "Ocean", 4 => "Custom", _ => "Galaxy" };
      ConfigService.LightingTheme = theme;
      ConfigService.Save("LightingTheme");
      SaveLightingJson();
    }

    void ZoneColor_SelectionChanged(object s, SelectionChangedEventArgs e) {
      if (_loading || _colorPicking) return;
      if (s is ComboBox combo && combo.SelectedItem is ComboBoxItem item) {
        int zone = int.Parse(combo.Name.Substring(4, 1)) - 1;
        string tag = (string)item.Tag;
        if (tag == "Custom") {
          using var cd = new System.Windows.Forms.ColorDialog { Color = System.Drawing.Color.FromArgb(_zoneColors[zone].R, _zoneColors[zone].G, _zoneColors[zone].B) };
          if (cd.ShowDialog() == System.Windows.Forms.DialogResult.OK) {
            _zoneColors[zone] = System.Windows.Media.Color.FromArgb(0xFF, cd.Color.R, cd.Color.G, cd.Color.B);
            _colorPicking = true;
            // ponytail: show hex so user sees it's custom, not a preset
            item.Content = $"#{_zoneColors[zone].R:X2}{_zoneColors[zone].G:X2}{_zoneColors[zone].B:X2}";
            combo.SelectedIndex = combo.Items.Count - 1;
            _colorPicking = false;
            SaveLightingJson();
            UpdateZonePreview();
          } else {
            // cancelled — restore previous preset selection
            _colorPicking = true;
            combo.SelectedIndex = Math.Max(0, _lastZoneIdx[zone]);
            _colorPicking = false;
          }
          return;
        }
        _lastZoneIdx[zone] = combo.SelectedIndex;
        // ponytail: shared OmenLighting.LookupColor — fixes the Pink drift between
        // PerKey (was 255,0,255 magenta) and Zone (was 0xFF,0x69,0xB4 pink).
        var (r, g, b) = OmenLighting.LookupColor(tag);
        _zoneColors[zone] = System.Windows.Media.Color.FromArgb(0xFF, r, g, b);
        SaveLightingJson();
        UpdateZonePreview();
      }
    }

    // ponytail: 4-zone color preview bar updater — gives visual feedback for which zone
    // has which color. Dimmed to 40% opacity when master toggle is off (60% black overlay).
    void UpdateZonePreview() {
      Border[] previews = { Zone1Preview, Zone2Preview, Zone3Preview, Zone4Preview };
      for (int i = 0; i < 4 && i < previews.Length; i++) {
        if (previews[i] != null)
          previews[i].Background = FrozenBrush(_zoneColors[i]);
      }
      if (ZonePreviewBar != null)
        ZonePreviewBar.Opacity = (LightEnableToggle?.IsChecked == true) ? 1.0 : 0.4;
    }

    void ApplyLightBtn_Click(object sender, RoutedEventArgs e) {
      try {
        var device = ConfigService.LightingDevice == "lightbar" ? LightingDevice.LightBar : LightingDevice.Keyboard;
        var isHpSdk = ConfigService.LightingInterface == "HpSdk";
        var iface = isHpSdk ? LightingControlInterface.BasicFourZone :
          ConfigService.LightingInterface == "Dojo" ? LightingControlInterface.Dojo : LightingControlInterface.BasicFourZone;
        var colors = new List<System.Windows.Media.Color>(_zoneColors);
        if (isHpSdk) {
          // ponytail: HP SDK path via OmenFourZoneLighting.dll only supports static color —
          // if user picked an animation here, surface that mismatch instead of dropping silently.
          if (AnimCombo.SelectedIndex > 0) {
            DialogHelper.Warn(Strings.LightingCapabilityAnimHpSdk, Strings.LightingControl);
            return;
          }
          FourZoneHelper.SetStaticColor(device, colors, (byte)ConfigService.LightingBrightness);
          return;
        }
        int animIdx = AnimCombo.SelectedIndex;
        if (animIdx > 0) {
          // ponytail: use ZoneEffectId(name) not SelectedIndex — BIOS effectId ≠ combo index.
          // ReplaySavedLighting already uses ZoneEffectId; this aligns the live-apply path.
          string animName = OmenLighting.AnimNames[animIdx];
          byte effectId = OmenLighting.ZoneEffectId(animName);
          byte speed = (byte)AnimSpeedCombo.SelectedIndex;
          byte direction = (byte)(AnimDirCombo.SelectedIndex == 1 ? 1 : 0);
          byte theme = (byte)AnimThemeCombo.SelectedIndex;
          Logger.Verbose($"ApplyLight: iface={iface} device={device} effectId={effectId} speed={speed} dir={direction} theme={theme} bright={ConfigService.LightingBrightness}");
          if (!OmenLighting.SupportsEffect(iface, effectId)) {
            DialogHelper.Warn(Strings.LightingCapabilityAnimBasic, Strings.LightingControl);
            return;
          }
          bool ok = OmenLighting.SetZoneAnimation(device, effectId, speed, direction, theme, colors,
            (byte)ConfigService.LightingBrightness, iface);
          if (!ok) DialogHelper.Warn(Strings.LightingCapabilityAnimBasic, Strings.LightingControl);
        } else {
          Logger.Verbose($"ApplyLight(static): iface={iface} device={device} bright={ConfigService.LightingBrightness}");
          OmenLighting.SetZoneStaticColor(device, colors, (byte)ConfigService.LightingBrightness, iface);
        }
        SaveLightingJson();
      } catch (Exception ex) { Logger.Error($"ApplyLightBtn_Click: {ex.Message}"); }
    }

    // ponytail: 能力自适应布局 — 按 KeyboardKind 重排整页卡片,替换原 ApplyLightingVisibility
    // 的散点显隐。矩阵:
    //   Normal(防御占位) / LightBarOnly / PerKey / OneZone / FourZone(默认,探测失败保守)
    // 灯条能力独立成 LightBarCard(FourZone/OneZone 机型带灯条时并排显示),设备下拉的
    // 灯条项恒隐藏(避免两条路径控制同一灯条)。
    void ApplyCapabilityLayout() {
      bool normal = _kbKindDetected && _kbKind == KeyboardKind.Normal;
      bool perKey = _kbKind == KeyboardKind.PerKey;
      bool lbOnly = _kbKind == KeyboardKind.LightBarOnly;
      bool oneZone = _kbKind == KeyboardKind.OneZone;
      bool fourZone = !normal && !perKey && !lbOnly && !oneZone;

      // 0. 不支持占位卡 + 总开关(防御:侧栏已隐藏,此卡只在直接导航进来时出现)
      if (UnsupportedCard != null) UnsupportedCard.Visibility = normal ? Visibility.Visible : Visibility.Collapsed;
      if (LightEnableToggle != null && normal) LightEnableToggle.Visibility = Visibility.Collapsed;

      // 1. 设备卡 — PerKey 机型设备无意义(锁键盘),普通/纯灯条仅剩键盘项也无意义
      if (LightCard0 != null)
        LightCard0.Visibility = (perKey || normal || lbOnly) ? Visibility.Collapsed : Visibility.Visible;

      // 2. 协议卡 — PerKey 机型锁死 PerKey 协议(见下),无需选择;灯条/普通隐藏
      if (LightCard1 != null)
        LightCard1.Visibility = (perKey || normal || lbOnly) ? Visibility.Collapsed : Visibility.Visible;

      // 3. 亮度速度卡 — PerKey 有独立亮度滑条,四分区动画速度卡保留给 OneZone/FourZone
      if (LightCard2 != null)
        LightCard2.Visibility = (perKey || normal || lbOnly) ? Visibility.Collapsed : Visibility.Visible;

      // 4. 分区颜色卡 — 仅四分区(4区)与单分区(1区可见)机型
      if (LightCard3 != null)
        LightCard3.Visibility = (perKey || normal || lbOnly) ? Visibility.Collapsed : Visibility.Visible;
      // OneZone: zone 2-4 折叠,写入时复制 zone1 同色(见 ApplyLightBtn_Click)
      var zoneVisibility = oneZone ? Visibility.Collapsed : Visibility.Visible;
      if (Zone2Cell != null) Zone2Cell.Visibility = zoneVisibility;
      if (Zone3Cell != null) Zone3Cell.Visibility = zoneVisibility;
      if (Zone4Cell != null) Zone4Cell.Visibility = zoneVisibility;
      if (Zone2Preview != null) Zone2Preview.Visibility = zoneVisibility;
      if (Zone3Preview != null) Zone3Preview.Visibility = zoneVisibility;
      if (Zone4Preview != null) Zone4Preview.Visibility = zoneVisibility;

      // 5. 动画卡 — 四分区/单分区且支持动画(cycle>260)
      if (AnimCard != null)
        AnimCard.Visibility = (fourZone || oneZone) && _supportAni ? Visibility.Visible : Visibility.Collapsed;

      // 6. PerKey 卡 + 协议锁
      if (PerKeyCard != null)
        PerKeyCard.Visibility = perKey ? Visibility.Visible : Visibility.Collapsed;
      if (perKey) {
        // 锁死 PerKey 协议/设备 — 不给"选错协议导致功能消失"的空间
        _loading = true;
        if (LightProtoCombo.SelectedIndex != 3) LightProtoCombo.SelectedIndex = 3;
        if (LightDevCombo.SelectedIndex != 0) LightDevCombo.SelectedIndex = 0;
        _loading = false;
        if (LightProtoCombo != null) LightProtoCombo.IsEnabled = false;
      } else if (LightProtoCombo != null && LightCard1.Visibility == Visibility.Visible) {
        LightProtoCombo.IsEnabled = true;
      }
      // 协议下拉项过滤 — 灯条项恒隐藏(独立卡),PerKey 项按机型
      if (LightBarItem != null) LightBarItem.Visibility = Visibility.Collapsed;
      if (PerKeyProtoItem != null)
        PerKeyProtoItem.Visibility = perKey ? Visibility.Visible : Visibility.Collapsed;

      // 7. 灯条卡 — 纯灯条机型独占,或四分区/单分区带灯条能力时显示。
      // 单键 RGB + 灯带一体机型:灯带面板整块搬入 PerKeyCard(一张卡,不分家),
      // 设备本身是一个照明系统(参考 OmenLinux/omen-rgb-keyboard)。
      if (LightBarCard != null)
        LightBarCard.Visibility = lbOnly || ((fourZone || oneZone) && _supportLightBar) ? Visibility.Visible : Visibility.Collapsed;
      PlaceLightBarPanel(perKey && _supportLightBar);
      // 一体机型主标题合并为"单键 RGB + 灯带"(灯带小标题不随面板搬入,不再重复)
      if (PerKeyTitleText != null)
        PerKeyTitleText.Text = perKey && _supportLightBar ? Strings.LightingPerKeyLbTitle : Strings.LightingPerKeyTitle;

      // 8. 应用按钮卡 — 键盘四分区路径专用(PerKey/灯条卡内已有自己的应用按钮)
      if (ApplyCard != null)
        ApplyCard.Visibility = (fourZone || oneZone) ? Visibility.Visible : Visibility.Collapsed;

      // 9. Dojo 高亮度面板 + 方向/主题使能(仅 Dojo+动画下发)
      bool isDojo = ConfigService.LightingInterface == "Dojo";
      if (DojoHighBrightPanel != null) DojoHighBrightPanel.Visibility = isDojo ? Visibility.Visible : Visibility.Collapsed;
      // 灯带高亮度直写面板 — LightBarIface 含 PerKey 回退 Dojo,一体机型也可见
      if (LightBarHighBrightPanel != null)
        LightBarHighBrightPanel.Visibility = LightBarIface() == LightingControlInterface.Dojo ? Visibility.Visible : Visibility.Collapsed;
      bool animEnabled = isDojo && AnimCombo != null && AnimCombo.SelectedIndex > 0;
      if (AnimDirCombo != null) AnimDirCombo.IsEnabled = animEnabled;
      if (AnimThemeCombo != null) AnimThemeCombo.IsEnabled = animEnabled;

      // 10. 逐键着色区可用性 — 动画非 None 时置灰(固件动效作用于全键盘)
      UpdatePerKeyPickerEnabled();

      // 11. 非 PerKey 机型释放缓存的 HID handle,不阻塞 OMEN Light Studio 等其他应用
      if (!perKey) ClosePerKeyHandleLocked();
    }

    // ponytail: 单键+灯带一体 — 灯带面板按能力整块搬移(PerKey 卡内 vs 独立灯带卡)。
    // 控件 x:Name/事件处理器不变,搬移对 InitLightBarPanel 与各 handler 透明;
    // 能力探测每会话稳定,首次搬移后 ReferenceEquals 短路,无重复 Parent 操作。
    void PlaceLightBarPanel(bool insidePerKey) {
      if (LightBarPanel == null) return;
      Panel target = insidePerKey ? PerKeyLightBarHost : LightBarCardHost;
      if (ReferenceEquals(LightBarPanel.Parent, target)) return;
      if (LightBarPanel.Parent is Panel from) from.Children.Remove(LightBarPanel);
      target.Children.Add(LightBarPanel);
    }

    void BtnBrightHigh_Click(object sender, RoutedEventArgs e) {
      if (_loading) return;
      if (sender is not Button btn || !byte.TryParse(btn.Tag?.ToString(), out byte v)) return;
      ConfigService.LightingBrightness = v;
      ConfigService.Save("LightingBrightness");
      SaveLightingJson();
      if (LightBrightVal != null) LightBrightVal.Text = v + "%";
      bool isDojo = ConfigService.LightingInterface == "Dojo";
      var device = ConfigService.LightingDevice == "lightbar" ? LightingDevice.LightBar : LightingDevice.Keyboard;
      try { if (isDojo) OmenLighting.SetZoneBrightness(device, v, LightingControlInterface.Dojo); }
      catch (Exception ex) { Logger.Error($"BtnBrightHigh_Click: {ex.Message}"); }
    }

    // ponytail: 灯带高亮度直写(128/228) — 与键盘四区 BtnBrightHigh_Click 同款,
    // 但走 LightBar 通道/LightBarIface,且动画运行中带新亮度重启渲染。
    void LightBarBrightHigh_Click(object sender, RoutedEventArgs e) {
      if (_loading) return;
      if (sender is not Button btn || !byte.TryParse(btn.Tag?.ToString(), out byte v)) return;
      _lbBrightness = v;
      SaveLightingJson();
      if (LightBarBrightVal != null) LightBarBrightVal.Text = v + "%";
      if (LightBarPanel == null || !LightBarPanel.IsVisible) return;
      try {
        if (LightingAnimationService.IsRunning && LightBarAnimCombo?.SelectedItem is ComboBoxItem a && a.Tag is string an) {
          LightingAnimationService.Start(LightingDevice.LightBar, an, _lbColors, v, LightBarIface());
          return;
        }
        OmenLighting.SetZoneBrightness(LightingDevice.LightBar, v, LightBarIface());
      } catch (Exception ex) { Logger.Error($"LightBarBrightHigh_Click: {ex.Message}"); }
    }

    // ponytail: 缓存 PerKey 诊断信息 — OpenPerKeyKeyboard 失败时记录检测到的 HP HID 设备,
    // 用于在 UI 上给用户更具体的提示(而不是只显示"未检测到单键RGB设备")。
    // 在 EnsurePerKeyHandle 失败时填充,成功时清空。
    string _perKeyDiagnostic;

    // ponytail: lazy HID open + reuse. Returns -1 with no error UI if previously
    // established as unavailable (e.g. wrong keyboard type) — the SelectionChanged
    // handlers silently no-op on -1; the explicit PerKeyApply_Click is the only
    // path that shows the connect-fail dialog, so error-fatigue from every slider
    // drag doesn't happen. Nullability: handle is just an int; lock is small.
    int EnsurePerKeyHandle() {
      lock (_perKeyLock) {
        if (_perKeyDestroyed) return -1;
        if (_perKeyHandle > 0) return _perKeyHandle;
        int h = OmenLighting.OpenPerKeyKeyboard();
        if (h > 0) {
          _perKeyHandle = h;
          _perKeyDiagnostic = null; // 成功时清空诊断信息
          UpdatePerKeyPickerConnection(true);
        } else {
          // 失败时获取诊断信息,供 UI 显示
          try { _perKeyDiagnostic = OmenLighting.GetPerKeyDiagnosticInfo(); }
          catch { _perKeyDiagnostic = null; }
          UpdatePerKeyPickerConnection(false);
        }
        return h;
      }
    }

    // ponytail: 构建 PerKey 失败提示 — 附加诊断信息(检测到的 HP HID 设备 PID),
    // 帮助用户判断是 OMEN 服务未运行还是机型 PID 未在已知列表中。
    string GetPerKeyFailMessage() {
      string baseMsg = Strings.LightingCapabilityPerKeyConnect;
      if (!string.IsNullOrEmpty(_perKeyDiagnostic))
        return baseMsg + "\n\n" + _perKeyDiagnostic;
      return baseMsg;
    }

    void ClosePerKeyHandleLocked() {
      int h;
      lock (_perKeyLock) {
        _perKeyDestroyed = true;
        h = _perKeyHandle; _perKeyHandle = -1;
      }
      if (h > 0) {
        // ponytail: dispatch off UI thread to avoid the same deadlock that
        // SetPerKey* + .Wait() would cause (SDK continuations need UI context).
        try { System.Threading.Tasks.Task.Run(() => OmenLighting.CloseDeviceAsync(h)).Wait(500); }
        catch { }
      }
    }

    void PerKeyStatic_SelectionChanged(object s, SelectionChangedEventArgs e) {
      if (_loading || s is not ComboBox cb || cb.SelectedItem is not ComboBoxItem item) return;
      ConfigService.PerKeyStaticColor = (string)item.Tag;
      ConfigService.Save("PerKeyStaticColor");
      PerKeyLiveApplyStatic();
      SaveLightingJson();
      // 基色变化 → 未着色键按钮背景跟随基色(已着色键保留)
      ApplyPerKeyButtonBaseColor();
    }

    void PerKeyAnim_SelectionChanged(object s, SelectionChangedEventArgs e) {
      if (_loading || s is not ComboBox cb || cb.SelectedItem is not ComboBoxItem item) return;
      ConfigService.PerKeyAnimation = (string)item.Tag;
      ConfigService.Save("PerKeyAnimation");
      // Animation selection now drives the apply path; static color picker still
      // honours the latest selection when animation is reverted to None.
      PerKeyLiveApply();
      UpdatePerKeyPickerEnabled();
      SaveLightingJson();
    }

    // ponytail: PerKey speed — was hardcoded LedSpeed=1 in PerKeyWriteAllBg.
    // Now user-configurable; SelectedIndex maps directly to McuSDK LedSpeed byte (0..3).
    void PerKeySpeed_SelectionChanged(object s, SelectionChangedEventArgs e) {
      if (_loading) return;
      ConfigService.PerKeySpeed = (byte)PerKeySpeedCombo.SelectedIndex;
      ConfigService.Save("PerKeySpeed");
      SaveLightingJson();
      PerKeyLiveApply();
    }

    void PerKeyBright_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) {
      if (PerKeyBrightVal != null) PerKeyBrightVal.Text = (int)e.NewValue + "%";
      if (_loading) return;
      ConfigService.PerKeyBrightness = (byte)(int)e.NewValue;
      ConfigService.Save("PerKeyBrightness");
      SaveLightingJson();
      // Live brightness: just one IOCTL — cheap enough to push on every tick.
      int h = EnsurePerKeyHandle();
      if (h <= 0) { UpdatePerKeyStatus(GetPerKeyFailMessage()); return; }
      PerKeyBackgroundRun(h, PerKeySetBrightnessBg, ok =>
        UpdatePerKeyStatus(ok ? Strings.LightingPerKeyBrightness + ": " + (int)e.NewValue + "%"
                              : GetPerKeyFailMessage()));
    }

    // ponytail: 单键高亮度直写(128/228) — 复用 PerKeySetBrightnessBg worker,
    // 真值写 ConfigService.PerKeyBrightness,后续动画 Setting.Brightness 同源带超档值。
    void PerKeyBrightHigh_Click(object sender, RoutedEventArgs e) {
      if (_loading) return;
      if (sender is not Button btn || !byte.TryParse(btn.Tag?.ToString(), out byte v)) return;
      ConfigService.PerKeyBrightness = v;
      ConfigService.Save("PerKeyBrightness");
      SaveLightingJson();
      if (PerKeyBrightVal != null) PerKeyBrightVal.Text = v + "%";
      int h = EnsurePerKeyHandle();
      if (h <= 0) { UpdatePerKeyStatus(GetPerKeyFailMessage()); return; }
      PerKeyBackgroundRun(h, PerKeySetBrightnessBg, ok =>
        UpdatePerKeyStatus(ok ? Strings.LightingPerKeyBrightness + ": " + v + "%"
                              : GetPerKeyFailMessage()));
    }

    void PerKeyApply_Click(object sender, RoutedEventArgs e) {
      // ponytail: 4 个 ComboBox + 亮度滑条都已是实时 RAM 写,Apply 按钮的独有语义
      // 是 StorePerKeyToFlash(冷启动保留)—— live updates 只写 RAM,不磨损 flash。
      // 不再每次重新 Open(破坏缓存语义),改用 EnsurePerKeyHandle。
      int h = EnsurePerKeyHandle();
      if (h <= 0) {
        string msg = GetPerKeyFailMessage();
        UpdatePerKeyStatus(msg);
        DialogHelper.Warn(msg, Strings.LightingControl);
        return;
      }
      PerKeyBackgroundRun(h, PerKeyFlashBg, ok =>
        UpdatePerKeyStatus(ok ? Strings.LightingPerKeyFlashSaved : Strings.KeyboardConnectFail));
    }

    void PerKeyLiveApplyStatic() {
      int h = EnsurePerKeyHandle();
      if (h <= 0) { UpdatePerKeyStatus(GetPerKeyFailMessage()); return; }
      PerKeyBackgroundRun(h, PerKeyWriteStaticBg, ok =>
        UpdatePerKeyStatus(ok ? Strings.LightingPerKeyStaticColor + ": " + ConfigService.PerKeyStaticColor
                              : GetPerKeyFailMessage()));
    }

    void PerKeyLiveApply() {
      int h = EnsurePerKeyHandle();
      if (h <= 0) { UpdatePerKeyStatus(GetPerKeyFailMessage()); return; }
      PerKeyBackgroundRun(h, PerKeyWriteAllBg, ok =>
        UpdatePerKeyStatus(ok ? Strings.LightingPerKeyAnimation + ": " + ConfigService.PerKeyAnimation
                              : GetPerKeyFailMessage()));
    }

    // ponytail: 144-key buffer reused across PerKey live/static writes — avoids
    // new byte[144]*3 on every slider tick (GC pressure). Safe because all writes
    // go through PerKeyBackgroundRun, which queues work onto the ThreadPool serially
    // from the UI thread — no concurrent writers. Ceiling: if a second page instance
    // ever showed simultaneously they'd race on these buffers; upgrade path = move
    // the buffers into per-page instance fields.
    static readonly byte[] _pkR = new byte[144], _pkG = new byte[144], _pkB = new byte[144];

    // Workers — all explicitly off UI thread via PerKeyBackgroundRun.
    delegate bool PerKeyWork(int h);

    static bool PerKeyWriteStaticBg(int h) {
      try {
        var (r, g, b) = OmenLighting.LookupColor(ConfigService.PerKeyStaticColor);
        // ponytail: Array.Fill 在 net481 不存在,手填两个静态 buffer 复用数组(已 clear)。
        for (int i = 0; i < _pkR.Length; i++) { _pkR[i] = r; _pkG[i] = g; _pkB[i] = b; }
        // 逐键覆盖 — 布局索引映射见 InitPerKeyPicker 的 ponytail 注释(真机校准点)
        foreach (var kvp in _perKeyColors) {
          if (!_perKeyIndex.TryGetValue(kvp.Key, out int idx) || idx >= _pkR.Length) continue;
          _pkR[idx] = kvp.Value.r; _pkG[idx] = kvp.Value.g; _pkB[idx] = kvp.Value.b;
        }
        return OmenLighting.SetPerKeyStaticColor(h, _pkR, _pkG, _pkB).GetAwaiter().GetResult();
      } catch (Exception ex) { Logger.Error($"PerKeyWriteStaticBg: {ex.Message}"); return false; }
    }

    static bool PerKeyWriteAllBg(int h) {
      try {
        if (ConfigService.PerKeyAnimation == "None") return PerKeyWriteStaticBg(h);
        // ponytail: write static base color first — McuSDK animations (Breathing/Pulse)
        // read current LED state as their base. Without this, stale colors from a
        // previous write would be used. Mirrors OmenCtl rgb_service pattern.
        if (!PerKeyWriteStaticBg(h)) return false;
        // AudioBeat 走音频通道 (麦克风/系统音频同步律动),其他动画走标准灯光通道
        if (ConfigService.PerKeyAnimation == "AudioBeat") {
          var audioSetting = new LightingAudioEffectSetting {
            Effect = OmenLighting.PerKeyEffectId("AudioBeat"),
            Brightness = ConfigService.PerKeyBrightness,
            LedSpeed = ConfigService.PerKeySpeed,
            Direction = 0,
            ShowMode = 0,
            ColorNumber = 4,
          };
          return OmenLighting.SetPerKeyAudioAnimation(h, audioSetting).GetAwaiter().GetResult();
        }
        byte mcuEff = OmenLighting.PerKeyEffectId(ConfigService.PerKeyAnimation);
        var setting = new LightingSetting {
          Effect = mcuEff, LedSpeed = ConfigService.PerKeySpeed, Direction = 0,
          Brightness = ConfigService.PerKeyBrightness, ColorNumber = 4, ShowMode = 0
        };
        return OmenLighting.SetPerKeyAnimation(h, setting).GetAwaiter().GetResult();
      } catch (Exception ex) { Logger.Error($"PerKeyWriteAllBg: {ex.Message}"); return false; }
    }

    static bool PerKeySetBrightnessBg(int h) {
      try { return OmenLighting.SetPerKeyBrightness(h, ConfigService.PerKeyBrightness).GetAwaiter().GetResult(); }
      catch (Exception ex) { Logger.Error($"PerKeySetBrightnessBg: {ex.Message}"); return false; }
    }

    // ponytail: master toggle off path — first caller of SetPerKeyLightingOff (was dead code).
    static bool PerKeyWriteOffBg(int h) {
      try { return OmenLighting.SetPerKeyLightingOff(h).GetAwaiter().GetResult(); }
      catch (Exception ex) { Logger.Error($"PerKeyWriteOffBg: {ex.Message}"); return false; }
    }

    static bool PerKeyFlashBg(int h) {
      try {
        if (!PerKeyWriteAllBg(h)) return false;
        return OmenLighting.StorePerKeyToFlash(h).GetAwaiter().GetResult();
      } catch (Exception ex) { Logger.Error($"PerKeyFlashBg: {ex.Message}"); return false; }
    }

    // ponytail: thin dispatcher — pushes work to ThreadPool so the SDK async Tasks'
    // continuations don't need UI context (the exact deadlock class OpenHidDevice
    // already had to dodge). Uses ThreadPool.QueueUserWorkItem to skip Task allocation.
    void PerKeyBackgroundRun(int h, PerKeyWork work, Action<bool> done) {
      // ponytail: 已入队的工作若在 Unloaded 后才执行,handle 已被 Close 置负 —
      // 早出避免 Push 一个注定失败的 IO,以及 Dispatcher.BeginInvoke 在 Unloaded
      // 后访问 stale UI。注意 done(false) 必须在当前 UI 上下文执行,不另起线程。
      if (_perKeyDestroyed) { done(false); return; }
      System.Threading.ThreadPool.QueueUserWorkItem(_ => {
        bool ok = false;
        try { ok = work(h); }
        catch (Exception ex) { Logger.Error($"PerKeyBackgroundRun: {ex.Message}"); }
        try { Dispatcher.BeginInvoke(new Action(() => done(ok))); }
        catch { /* page已卸载: status 更新丢弃,无副作用 */ }
      });
    }

    // ponytail: status line. Set on the UI thread via Dispatcher from background work.
    // Reuses the existing PerKey status TextBlock; if missing (corner case during
    // page teardown), no-op.
    void UpdatePerKeyStatus(string text) {
      try { if (PerKeyStatusText != null) PerKeyStatusText.Text = text; } catch { }
    }

    // ═══════════════════════════════════════════════════════════════
    // 逐键着色 — 可视化键盘布局 + 逐键颜色字典 + buffer 覆盖写入
    // ═══════════════════════════════════════════════════════════════

    // ponytail: US English 行主序布局表 — GetPerKeyLanguage 非 US 时同样用此表兜底。
    // buffer 索引 = 行主序枚举位置(0..N<144)。这是布局推断而非 SDK 官方映射:
    // McuSDK SetKeyboardStaticLighting(r[144],g,b) 的槽位语义无文档,若真机错位表现为
    // 颜色偏移(不损坏)。升级路径 = 拿到官方键位映射表后替换 _perKeyIndex 一处即可。
    static readonly string[] PerKeyLayoutRows = {
      "Esc F1 F2 F3 F4 F5 F6 F7 F8 F9 F10 F11 F12",
      "` 1 2 3 4 5 6 7 8 9 0 - = BkSp",
      "Tab Q W E R T Y U I O P [ ] \\",
      "Caps A S D F G H J K L ; ' Enter",
      "Shift Z X C V B N M , . / Shift",
      "Ctrl Win Alt Space Fn Menu ← ↑ ↓ →",
    };
    static readonly Dictionary<string, double> PerKeyWidth = new() {
      { "BkSp", 46 }, { "Tab", 46 }, { "Enter", 64 }, { "Shift", 66 },
      { "Ctrl", 40 }, { "Win", 40 }, { "Alt", 40 }, { "Space", 156 },
      { "Fn", 34 }, { "Menu", 40 },
    };
    // 键名本地化显示(只覆盖特殊名,字母/数字/符号键保持原样)
    static string LocalizedKeyName(string key) => key switch {
      "BkSp" => Strings.LightingPerKeyBkSp,
      "Enter" => Strings.LightingPerKeyEnter,
      "Caps" => Strings.LightingPerKeyCaps,
      _ => key,
    };
    // 键名 → buffer 索引(行主序),按钮 → 键名反向查色
    static readonly Dictionary<string, int> _perKeyIndex = new();
    static readonly Dictionary<string, (byte r, byte g, byte b)> _perKeyColors = new();
    Button _selectedKeyBtn;
    Brush _keyBorderBrush = Brushes.Transparent;  // InitPerKeyPicker 从主题资源填充
    static bool _pickerInited;
    // ponytail: 选中键描边 — 2px 青色足够在彩色键面/暗键盘底上清晰可见
    static readonly Brush _selectedKeyBorder = FrozenBrush(System.Windows.Media.Color.FromRgb(0x00, 0xFF, 0xC8));
    const double _selectedKeyBorderThickness = 2;

    // ponytail: 页面内所有运行时刷子统一冻结 — Freeze 去线程亲和,后台线程创建的刷子
    // 进可视树不会再让布局 pass 抛 InvalidOperationException("点不进页面"根因)。
    static SolidColorBrush FrozenBrush(System.Windows.Media.Color c) {
      var b = new SolidColorBrush(c);
      b.Freeze();
      return b;
    }

    static string LocalizedColorName(string name) => name switch {
      "Red" => Strings.LightingColorRed, "Green" => Strings.LightingColorGreen,
      "Blue" => Strings.LightingColorBlue, "White" => Strings.LightingColorWhite,
      "Cyan" => Strings.LightingColorCyan, "Pink" => Strings.LightingColorMagenta,
      "Yellow" => Strings.LightingColorYellow, "IceBlue" => Strings.LightingColorIceBlue,
      "CoolGreen" => Strings.LightingColorCoolGreen, "WarmYellow" => Strings.LightingColorWarmYellow,
      "FieryOrange" => Strings.LightingColorFieryOrange, "HotRed" => Strings.LightingColorHotRed,
      _ => name,
    };

    void InitPerKeyPicker() {
      if (_pickerInited || PerKeyLayout == null) return;
      _pickerInited = true;
      // 布局索引表(一次构建)
      _perKeyIndex.Clear();
      int idx = 0;
      foreach (var row in PerKeyLayoutRows)
        foreach (var key in row.Split(' '))
          _perKeyIndex[key] = idx++;
      // 每行一个水平 StackPanel,按键生成按钮。行宽不一(真实键盘行宽差),
      // HorizontalAlignment=Center 让每行在卡内居中 → 对称的键盘轮廓,消除左对齐的参差右边
      // ponytail: 主题自适应键帽 — 原硬编码 #2D2D2D+白字在亮色主题下是"暗块贴亮卡";
      // 底色/文字走主题资源,每键 1px 边框勾勒键型(选中仍切 2px 青色高亮环)。
      // 资源缺失时回退原暗色三件套(防御 ControlsDictionary 未合并的理论路径)。
      _keyBorderBrush = TryFindResource("BorderDefaultBrush") as Brush
        ?? FrozenBrush(System.Windows.Media.Color.FromRgb(0x66, 0x66, 0x70));
      var keyBg = TryFindResource("ControlFillColorDefaultBrush") as Brush
        ?? FrozenBrush(System.Windows.Media.Color.FromRgb(0x2D, 0x2D, 0x33));
      var keyFg = TryFindResource("TextFillColorPrimaryBrush") as Brush ?? Brushes.White;
      foreach (var row in PerKeyLayoutRows) {
        var rowPanel = new StackPanel {
          Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2),
          HorizontalAlignment = HorizontalAlignment.Center,
        };
        foreach (var key in row.Split(' ')) {
          var btn = new Button {
            Content = LocalizedKeyName(key), Tag = key,
            FontSize = 10, Padding = new Thickness(0),
            MinWidth = PerKeyWidth.TryGetValue(key, out var w) ? w : 30,
            Height = 28, Margin = new Thickness(2, 1, 2, 1),
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Background = keyBg, Foreground = keyFg,
            BorderBrush = _keyBorderBrush, BorderThickness = new Thickness(1),
          };
          btn.Click += PerKeyButton_Click;
          // ponytail: 长按已着色键清除该键颜色(右键也行,兼容触屏与鼠标)
          btn.PreviewMouseRightButtonDown += PerKeyKey_RightClickClear;
          rowPanel.Children.Add(btn);
        }
        PerKeyLayout.Items.Add(rowPanel);
      }
      // 色块板 — 即点即用色块(无下拉);末位是自定义色按钮
      if (PerKeyPalette != null) {
        foreach (var name in OmenLighting.PresetColorRgb.Keys) {
          var (r, g, b) = OmenLighting.LookupColor(name);
          var swatch = new Button {
            Tag = name, Width = 30, Height = 30, Margin = new Thickness(0, 0, 6, 6),
            Padding = new Thickness(0),
            Background = FrozenBrush(System.Windows.Media.Color.FromRgb(r, g, b)),
            BorderBrush = FrozenBrush(System.Windows.Media.Color.FromRgb(0x66, 0x66, 0x70)),
            BorderThickness = new Thickness(1),
            ToolTip = LocalizedColorName(name),
          };
          swatch.Click += PerKeySwatch_Click;
          PerKeyPalette.Children.Add(swatch);
        }
        var customBtn = new Button {
          Tag = "__custom__", Content = "...", Width = 30, Height = 30,
          Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(0),
          FontSize = 14, ToolTip = Strings.LightingPerKeyCustomColor,
        };
        customBtn.Click += PerKeySwatch_Click;
        PerKeyPalette.Children.Add(customBtn);
      }
      ApplyPerKeyButtonBaseColor();
    }

    // 把已着色键/基色铺到按钮上 — 静态色变化或 Clear 后调用
    void ApplyPerKeyButtonBaseColor() {
      if (!_pickerInited || PerKeyLayout == null) return;
      var baseColor = ColorFromName(ConfigService.PerKeyStaticColor);
      foreach (var row in PerKeyLayout.Items.OfType<StackPanel>())
        foreach (var btn in row.Children.OfType<Button>())
          if (btn.Tag is string key) {
            var c = _perKeyColors.TryGetValue(key, out var k)
              ? System.Windows.Media.Color.FromRgb(k.r, k.g, k.b)
              : baseColor;
            btn.Background = FrozenBrush(c);
            // ponytail: 键帽文字按底色亮度取黑/白 — 白字贴黄/白/冰蓝键不可读,亮色主题下尤其
            double lum = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
            btn.Foreground = lum > 0.6 ? Brushes.Black : Brushes.White;
          }
    }

    void PerKeyButton_Click(object sender, RoutedEventArgs e) {
      if (_selectedKeyBtn != null) {
        // 取消选中恢复 1px 主题边框(键帽常态边框,不再回透明)
        _selectedKeyBtn.BorderBrush = _keyBorderBrush;
        _selectedKeyBtn.BorderThickness = new Thickness(1);
      }
      _selectedKeyBtn = sender as Button;
      if (_selectedKeyBtn != null) {
        _selectedKeyBtn.BorderBrush = _selectedKeyBorder;
        _selectedKeyBtn.BorderThickness = new Thickness(_selectedKeyBorderThickness);
      }
    }

    // 右键已着色键 → 清除该键颜色
    void PerKeyKey_RightClickClear(object sender, System.Windows.Input.MouseButtonEventArgs e) {
      if (!_pickerInited || sender is not Button btn || btn.Tag is not string key) return;
      if (!_perKeyColors.Remove(key)) return;
      btn.Background = FrozenBrush(ColorFromName(ConfigService.PerKeyStaticColor));
      UpdatePerKeyColoredStatus();
      // 下发刷新
      int h = EnsurePerKeyHandle();
      if (h > 0) PerKeyBackgroundRun(h, PerKeyWriteStaticBg, _ => { });
    }

    void PerKeySwatch_Click(object sender, RoutedEventArgs e) {
      if (_loading || !_pickerInited) return;
      if (_selectedKeyBtn?.Tag is not string key) return;
      if (sender is not Button sw) return;
      byte r, g, b;
      if ((string)sw.Tag == "__custom__") {
        using var cd = new System.Windows.Forms.ColorDialog {
          Color = _perKeyColors.TryGetValue(key, out var cur)
            ? System.Drawing.Color.FromArgb(cur.r, cur.g, cur.b)
            : System.Drawing.Color.White,
        };
        if (cd.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        r = cd.Color.R; g = cd.Color.G; b = cd.Color.B;
      } else {
        (r, g, b) = OmenLighting.LookupColor((string)sw.Tag);
      }
      _perKeyColors[key] = (r, g, b);
      _selectedKeyBtn.Background = FrozenBrush(System.Windows.Media.Color.FromRgb(r, g, b));
      UpdatePerKeyColoredStatus();
      // 下发:基色整片 + 逐键覆盖(PerKeyWriteStaticBg 已含覆盖逻辑)
      int h = EnsurePerKeyHandle();
      if (h <= 0) { UpdatePerKeyStatus(GetPerKeyFailMessage()); return; }
      PerKeyBackgroundRun(h, PerKeyWriteStaticBg, ok =>
        UpdatePerKeyStatus(ok ? string.Format(Strings.LightingPerKeyColored, _perKeyColors.Count)
                              : GetPerKeyFailMessage()));
    }

    void PerKeyClear_Click(object sender, RoutedEventArgs e) {
      _perKeyColors.Clear();
      ApplyPerKeyButtonBaseColor();
      UpdatePerKeyColoredStatus();
      int h = EnsurePerKeyHandle();
      if (h > 0) PerKeyBackgroundRun(h, PerKeyWriteStaticBg, _ => { });
    }

    void UpdatePerKeyColoredStatus() {
      if (PerKeyPickerHint != null && ConfigService.PerKeyAnimation == "None")
        PerKeyPickerHint.Text = string.Format(Strings.LightingPerKeyColored, _perKeyColors.Count);
    }

    // 动画非 None 时整区置灰 — 固件动效作用于全键盘,逐键色无意义
    void UpdatePerKeyPickerEnabled() {
      if (PerKeyPickerPanel == null) return;
      bool animNone = ConfigService.PerKeyAnimation == "None";
      PerKeyPickerPanel.IsEnabled = animNone;
      if (PerKeyPickerHint != null)
        PerKeyPickerHint.Text = animNone ? Strings.LightingPerKeyApplyHint : Strings.LightingPerKeyAnimKeyDisabled;
    }

    // 连接态遮罩 — HID 打开失败后逐键布局区整体置灰,避免在无效连接上空点
    void UpdatePerKeyPickerConnection(bool connected) {
      if (PerKeyPickerPanel == null) return;
      PerKeyPickerPanel.IsEnabled = connected && ConfigService.PerKeyAnimation == "None";
      if (!connected && PerKeyPickerHint != null)
        PerKeyPickerHint.Text = Strings.LightingPerKeyDisconnected;
    }

    // ═══════════════════════════════════════════════════════════════
    // 灯条独立面板 — 4 段色 + 亮度 + 动画,走已有 LightBar WMI 通道
    // ═══════════════════════════════════════════════════════════════

    static LightingControlInterface LightBarIface() {
      // 灯条始终走 WMI 四区通道(独立于键盘路径):
      //   - 四分区/单分区机型:沿用当前 LightingInterface(Basic/Dojo)
      //   - PerKey 机型:键盘走 HID/McuSDK,但灯条仍是 WMI 四区 → 等同 PerKey 键盘温度联动回退 Dojo
      //   - 探测失败保守 FourZone → BasicFourZone
      // 与 LightingTemperatureService.ResolveInterface 同口径。
      return ConfigService.LightingInterface switch {
        "Dojo" => LightingControlInterface.Dojo,
        "PerKey" => LightingControlInterface.Dojo,  // PerKey 键盘:灯条回退 Dojo WMI
        _ => LightingControlInterface.BasicFourZone,
      };
    }

    // ponytail: 灯带动效校准表(2026-08 实测 PerKey+灯带机型,Dojo 协议,与 OGH 灯带面板对照)。
    // 灯带固件对 zone effectId 的解释与键盘四区不同:
    //   ID1(ColorCycle)/ID9(Swipe)→五彩纸屑  ID2(Starlight)→太阳  ID3(Breathing)→星光(火山)
    //   ID4(Wave)→间歇闪烁(火山)  ID5(Raindrop)/ID8(Sun)→无效果  ID6(AudioPulse)→波纹(银河)
    //   ID7(Confetti)→雨滴
    // Tag 存发送的 AnimName(ID 由 AnimNameToZoneId 决定),显示名按真实动效标注;
    // 无效果 ID(5/8)与重复 ID(9≡1)剔除。键盘四区面板不受影响,仍按原名。
    // Ceiling: 仅 Dojo 实测;BasicFourZone 灯带未验 — SupportsEffect 只放行 ID2/4,失败回退静态。
    static readonly string[] LightBarAnims = {
      "None", "ColorCycle", "Starlight", "Breathing", "Wave", "AudioPulse", "Confetti",
    };

    static string LocalizedLightBarAnimName(string name) => name switch {
      "ColorCycle" => Strings.LightingAnimConfetti,   // ID1 灯带实际:五彩纸屑
      "Starlight"  => Strings.LightingAnimSun,        // ID2 → 太阳
      "Breathing"  => Strings.LightingAnimStarlight,  // ID3 → 星光(火山)
      "Wave"       => Strings.LightingLbAnimBlink,    // ID4 → 间歇闪烁(火山)
      "AudioPulse" => Strings.LightingLbAnimRipple,   // ID6 → 波纹(银河)
      "Confetti"   => Strings.LightingAnimRaindrop,   // ID7 → 雨滴
      _ => Strings.LightingAnimNone,
    };

    /// <summary>--selftest 断言 — 校准表每个动画名须有 zone effectId(None 除外)且显示名不重复</summary>
    internal static string LightBarAnimsSelfCheck() {
      var fails = new List<string>();
      var labels = new HashSet<string>();
      foreach (var a in LightBarAnims) {
        if (a != "None" && OmenLighting.ZoneEffectId(a) == 0) fails.Add($"灯带动画 {a} 缺 zone effectId");
        if (!labels.Add(LocalizedLightBarAnimName(a))) fails.Add($"灯带动画显示名重复: {a}");
      }
      return fails.Count == 0 ? "PASS LightingPage: 灯带 effectId/显示名校准表有效"
        : "FAIL LightingPage: " + string.Join("; ", fails);
    }

    void InitLightBarPanel() {
      if (LightBarSeg1Combo == null || LightBarSeg1Combo.Items.Count > 0) return;
      var combos = new[] { LightBarSeg1Combo, LightBarSeg2Combo, LightBarSeg3Combo, LightBarSeg4Combo };
      foreach (var combo in combos) {
        foreach (var name in OmenLighting.PresetColorRgb.Keys)
          combo.Items.Add(new ComboBoxItem { Tag = name, Content = LocalizedColorName(name) });
        combo.SelectedIndex = 0;
      }
      // 动画列表: 按灯带真实动效命名(无 AudioBeat — 那是 PerKey 音频通道专属),见 LightBarAnims
      foreach (var anim in LightBarAnims)
        LightBarAnimCombo.Items.Add(new ComboBoxItem {
          Tag = anim, Content = LocalizedLightBarAnimName(anim) });
      LightBarAnimCombo.SelectedIndex = 0;
    }

    void LightBarColor_SelectionChanged(object sender, SelectionChangedEventArgs e) {
      if (_loading || _lbLoading || sender is not ComboBox combo || combo.SelectedItem is not ComboBoxItem item) return;
      int seg = combo.Name switch {
        "LightBarSeg1Combo" => 0, "LightBarSeg2Combo" => 1,
        "LightBarSeg3Combo" => 2, _ => 3,
      };
      var (r, g, b) = OmenLighting.LookupColor((string)item.Tag);
      _lbColors[seg] = System.Windows.Media.Color.FromRgb(r, g, b);
      UpdateLightBarPreview();
      SaveLightingJson();
      // 动画运行中改段色 → 以新基色重启软件渲染
      if (LightingAnimationService.IsRunning && LightBarAnimCombo?.SelectedItem is ComboBoxItem a && a.Tag is string an)
        LightingAnimationService.Start(LightingDevice.LightBar, an, _lbColors,
          (byte)_lbBrightness, LightBarIface());
    }

    void UpdateLightBarPreview() {
      var segs = new[] { LightBarSeg1, LightBarSeg2, LightBarSeg3, LightBarSeg4 };
      for (int i = 0; i < 4 && i < segs.Length; i++)
        if (segs[i] != null) segs[i].Background = FrozenBrush(_lbColors[i]);
    }

    void LightBarBright_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) {
      if (LightBarBrightVal != null) LightBarBrightVal.Text = (int)e.NewValue + "%";
      if (_loading) return;
      _lbBrightness = (int)e.NewValue;
      SaveLightingJson();
      // 合并卡机型 LightBarCard 折叠,须按面板实际可见性判断(IsVisible 含祖先折叠)
      if (LightBarPanel == null || !LightBarPanel.IsVisible) return;
      if (LightingAnimationService.IsRunning && LightBarAnimCombo?.SelectedItem is ComboBoxItem a && a.Tag is string an) {
        // 动画帧自带亮度 — 以新亮度重启,下一帧即生效
        LightingAnimationService.Start(LightingDevice.LightBar, an, _lbColors, (byte)_lbBrightness, LightBarIface());
        return;
      }
      try {
        OmenLighting.SetZoneBrightness(LightingDevice.LightBar, (byte)_lbBrightness, LightBarIface());
      } catch (Exception ex) { Logger.Error($"LightBar bright: {ex.Message}"); }
    }

    void LightBarAnim_SelectionChanged(object sender, SelectionChangedEventArgs e) {
      if (_loading || _lbLoading || sender is not ComboBox cb || cb.SelectedItem is not ComboBoxItem item) return;
      if (LightBarPanel != null && LightBarPanel.IsVisible)
        LightBarApply_Click(null, null);
    }

    void LightBarApply_Click(object sender, RoutedEventArgs e) {
      try {
        var colors = new List<System.Windows.Media.Color>(_lbColors);
        byte bright = (byte)_lbBrightness;
        var iface = LightBarIface();
        // Tag 即发送的 AnimName — LightBarAnims 校准表顺序与 AnimNames 不再一致,禁止按下标取
        string animName = (LightBarAnimCombo?.SelectedItem as ComboBoxItem)?.Tag as string;
        if (!string.IsNullOrEmpty(animName) && animName != "None") {
          // 软件渲染优先(参考 omen-rgb-keyboard):帧颜色本侧计算,显示名即真实动效,
          // 不受灯带固件 ID 映射/SupportsEffect 门控影响;渲染表未收录才走固件 ID 回退
          if (LightingAnimationService.Start(LightingDevice.LightBar, animName, _lbColors, bright, iface)) {
            if (LightBarStatusText != null) LightBarStatusText.Text = "✓";
            return;
          }
          byte effectId = OmenLighting.ZoneEffectId(animName);
          if (!OmenLighting.SupportsEffect(iface, effectId)) {
            if (LightBarStatusText != null) LightBarStatusText.Text = Strings.LightingCapabilityAnimBasic;
            return;
          }
          OmenLighting.SetZoneAnimation(LightingDevice.LightBar, effectId, 1, 0, 0, colors, bright, iface);
        } else {
          LightingAnimationService.Stop();  // 选"无":停软件动画,落静态
          OmenLighting.SetZoneStaticColor(LightingDevice.LightBar, colors, bright, iface);
        }
        if (LightBarStatusText != null) LightBarStatusText.Text = "✓";
      } catch (Exception ex) {
        Logger.Error($"LightBarApply: {ex.Message}");
        if (LightBarStatusText != null) LightBarStatusText.Text = Strings.LightingLightBarNotResponding;
      }
    }

    internal static void ReplaySavedLighting() {
      try {
        var lj = LoadLightingJson();
        // ponytail: 默认关闭 — 不开灯光控制时启动不碰灯光
        if (!lj.Enabled) return;
        // 场景回放/启动恢复前先停软件渲染动画,避免下一帧覆盖回写的静态色
        Services.LightingAnimationService.Stop();

        var device = lj.Device == "lightbar" ? LightingDevice.LightBar : LightingDevice.Keyboard;
        string iface = lj.Interface ?? "BasicFourZone";
        var ci = iface == "Dojo" ? LightingControlInterface.Dojo : LightingControlInterface.BasicFourZone;
        byte bright = (byte)lj.Brightness;

        // ponytail: 纯灯条机型(键盘无RGB)只写灯条,键盘写入必然失败还可能占 WMI 时间
        var cap = OmenLighting.DetectKeyboardCapability();
        if (cap.Kind == KeyboardKind.LightBarOnly) {
          if (lj.LightBarColors != null && lj.LightBarColors.Length == 4) {
            var lbColors = new List<System.Windows.Media.Color>();
            foreach (var hex in lj.LightBarColors) lbColors.Add(ParseHexColor(hex));
            var lbIface = iface == "Dojo" ? LightingControlInterface.Dojo : LightingControlInterface.BasicFourZone;
            OmenLighting.SetZoneStaticColor(LightingDevice.LightBar, lbColors, (byte)lj.LightBarBrightness, lbIface);
          }
          return;
        }

        // ponytail: PerKey 机型带灯条 — 键盘逐键色走 HID(由 LightingPage PerKey 路径处理,
        // 启动期 HID 冷探可能失败故不在这里写),灯条走 WMI 四区通道(与 LightBarIface 同口径:
        // PerKey 回退 Dojo)。两路径独立,不互相覆盖。
        if (cap.Kind == KeyboardKind.PerKey && cap.LightBarSupported
            && lj.LightBarColors != null && lj.LightBarColors.Length == 4) {
          var lbColors = new List<System.Windows.Media.Color>();
          foreach (var hex in lj.LightBarColors) lbColors.Add(ParseHexColor(hex));
          var lbIface = LightingControlInterface.Dojo;  // PerKey 灯条回退
          try { OmenLighting.SetZoneStaticColor(LightingDevice.LightBar, lbColors, (byte)lj.LightBarBrightness, lbIface); }
          catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"PerKey lightbar replay: {ex.Message}"); }
        }

        // ponytail: 4 区独立颜色从 JSON 恢复,不再用单一 LightingColor 填 4 区
        var colors4 = new List<System.Windows.Media.Color>();
        if (lj.ZoneColors != null) {
          foreach (var hex in lj.ZoneColors) colors4.Add(ParseHexColor(hex));
        }
        while (colors4.Count < 4) colors4.Add(System.Windows.Media.Color.FromRgb(255, 255, 255));

        if (iface == "HpSdk") {
          OmenLighting.FourZoneHelper.SetStaticColor(device, colors4, bright);
          return;
        }
        if (!string.IsNullOrEmpty(lj.Animation) && lj.Animation != "None") {
          byte animId = OmenLighting.ZoneEffectId(lj.Animation);
          byte speed = 1;
          byte direction = (byte)(lj.Direction == "Right" ? 1 : 0);
          byte theme = lj.Theme switch { "Volcano" => (byte)1, "Jungle" => (byte)2, "Ocean" => (byte)3, "Custom" => (byte)4, _ => (byte)0 };
          if (OmenLighting.SupportsEffect(ci, animId)) {
            OmenLighting.SetZoneAnimation(device, animId, speed, direction, theme, colors4, bright, ci);
            return;
          }
        }
        OmenLighting.SetZoneStaticColor(device, colors4, bright, ci);
      } catch (Exception ex) {
        System.Diagnostics.Debug.WriteLine($"ReplaySavedLighting failed: {ex.Message}");
      }
    }

    static System.Windows.Media.Color ColorFromName(string name) {
      var (r, g, b) = OmenLighting.LookupColor(name);
      return System.Windows.Media.Color.FromRgb(r, g, b);
    }

    // ponytail: VSM 精确切换 — 比 WrapPanel 物理换行更可预测、临界不闪。
    // Wide(>=1100) 双列,Narrow 单列。Zone 子 Grid 阈值 480(单卡半宽内足够双列)。
    // Loaded 后立刻 GoToState 一次,避免首次渲染态与窗口宽度不匹配。
    const double LightWideWidth = 1100;
    const double ZoneWideWidth = 480;

    void LightingPage_SizeChanged(object sender, SizeChangedEventArgs e) {
      if (!e.WidthChanged) return;
      ApplyLayoutStates(e.NewSize.Width);
    }

    void ApplyLayoutStates(double width) {
      bool wide = width >= LightWideWidth;
      VisualStateManager.GoToState(this, wide ? "Wide" : "Narrow", true);
      // ponytail: Zone 子 Grid 的 VSM 挂在 LightCard3(Control) 上,因为 GoToState 只接受 Control。
      // Storyboard 内的 TargetName 按名字查,可跨层级引用 ZoneColGap/Zone2Cell/Zone4Cell。
      bool zone2Col = wide || width >= ZoneWideWidth;
      if (LightCard3 != null)
        VisualStateManager.GoToState(LightCard3, zone2Col ? "Zone2Col" : "Zone1Col", true);
    }

    // ponytail: 灯光控制总开关 — 默认关闭, 开启后启动时恢复上次灯效设置
    // Toggle actively controls hardware: off → kill output, on → restore current page settings.
    void LightEnable_Changed(object sender, RoutedEventArgs e) {
      if (_loading) return;
      bool on = LightEnableToggle?.IsChecked == true;
      SaveLightingJson();
      // ponytail: 灯光总开关联动 3 个后端 timer 的启停,与"功能不开就不浪费开销"对齐:
      //  - LightingSceneService.Enabled:同步 lighting_scenes.json 的 Enabled + 60s 定时调度器
      //    (Enabled setter 内部双向联动 StartScheduler/StopScheduler)。
      //  - LightingAnimationService.Stop():50ms 一次的软件动画 timer — 用户没开灯就不该跑。
      //  - LightingTempService.Stop():4s 温度映射 timer — 灯都不开了温度联动更没意义。
      // 开 (on==true) 时只拉 scene 调度器,不主动 Start Animation/Temp — 那两个由灯条卡片
      // 和 TempMode 子开关各自独立启停(默认也都关),用户主动操作才跑。
      LightingSceneService.Enabled = on;
      if (!on) {
        try { LightingAnimationService.Stop(); } catch { }
        try { LightingTemperatureService.Stop(); } catch { }
      }
      if (ConfigService.LightingInterface == "PerKey") {
        int h = EnsurePerKeyHandle();
        if (h > 0) PerKeyBackgroundRun(h, on ? PerKeyWriteAllBg : PerKeyWriteOffBg, _ => { });
      } else if (on) {
        ApplyLightBtn_Click(null, null);
      } else {
        var device = ConfigService.LightingDevice == "lightbar" ? LightingDevice.LightBar : LightingDevice.Keyboard;
        var ci = ConfigService.LightingInterface == "Dojo" ? LightingControlInterface.Dojo : LightingControlInterface.BasicFourZone;
        try { OmenLighting.SetZoneOff(device, ci); }
        catch (Exception ex) { Logger.Error($"LightEnable off: {ex.Message}"); }
      }
      UpdateZonePreview();
    }

    void TempMode_Changed(object sender, RoutedEventArgs e) {
      if (_loading) return;
      bool on = TempModeToggle?.IsChecked == true;
      ConfigService.LightingTempMode = on;
      ConfigService.Save("LightingTempMode");
      if (on) {
        LightingTemperatureService.Start();
      } else {
        LightingTemperatureService.Stop();
        // 恢复当前场景的灯光状态
        ReplaySavedLighting();
      }
    }

    // ponytail: 快照当前灯光页状态到 JSON — ConfigService 存基础字段, JSON 补 zone colors + toggle
    void SaveLightingJson() {
      var zoneHex = new string[4];
      for (int i = 0; i < 4; i++) {
        var c = _zoneColors[i];
        zoneHex[i] = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
      }
      var lbHex = new string[4];
      for (int i = 0; i < 4; i++) {
        var c = _lbColors[i];
        lbHex[i] = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
      }
      SaveLightingJson(new LightingState {
        Enabled = LightEnableToggle?.IsChecked == true,
        Device = ConfigService.LightingDevice,
        Interface = ConfigService.LightingInterface,
        Brightness = ConfigService.LightingBrightness,
        Animation = ConfigService.LightingAnimation,
        Direction = ConfigService.LightingDirection,
        Theme = ConfigService.LightingTheme,
        ZoneColors = zoneHex,
        PerKeyStaticColor = ConfigService.PerKeyStaticColor,
        PerKeyAnimation = ConfigService.PerKeyAnimation,
        PerKeyBrightness = ConfigService.PerKeyBrightness,
        PerKeySpeed = ConfigService.PerKeySpeed,
        LightBarColors = lbHex,
        LightBarBrightness = _lbBrightness,
      });
    }

    internal static LightingState LoadLightingJsonInternal() {
      try {
        if (File.Exists(LightingJsonPath)) {
          var ser = new DataContractJsonSerializer(typeof(LightingState));
          using (var ms = new MemoryStream(File.ReadAllBytes(LightingJsonPath)))
            return ser.ReadObject(ms) as LightingState ?? new LightingState();
        }
      } catch (Exception ex) { Logger.Error($"LoadLightingJson: {ex.Message}"); }
      return new LightingState();
    }

    static LightingState LoadLightingJson() => LoadLightingJsonInternal();

    internal static void SaveLightingJsonInternal(LightingState state) {
      try {
        var ser = new DataContractJsonSerializer(typeof(LightingState));
        using (var ms = new MemoryStream()) {
          ser.WriteObject(ms, state);
          File.WriteAllBytes(LightingJsonPath, ms.ToArray());
        }
      } catch (Exception ex) { Logger.Error($"SaveLightingJson: {ex.Message}"); }
    }

    static void SaveLightingJson(LightingState state) => SaveLightingJsonInternal(state);

    static System.Windows.Media.Color ParseHexColor(string hex) {
      if (hex != null && hex.Length == 7 && hex[0] == '#') {
        try {
          return System.Windows.Media.Color.FromRgb(
            Convert.ToByte(hex.Substring(1, 2), 16),
            Convert.ToByte(hex.Substring(3, 2), 16),
            Convert.ToByte(hex.Substring(5, 2), 16));
        } catch { }
      }
      return System.Windows.Media.Color.FromRgb(255, 255, 255);
    }

    // ═══════════════════════════════════════════════════════════════
    // 场景系统 — 参考 OmenCore RgbSceneService
    // ═══════════════════════════════════════════════════════════════

    bool _sceneLoading;
    void SceneCombo_SelectionChanged(object s, SelectionChangedEventArgs e) {
      if (_loading || _sceneLoading) return;
      if (SceneCombo.SelectedItem is ComboBoxItem item && item.Tag is string sceneId) {
        LightingSceneService.ActivateScene(sceneId, "manual");
        // Reload page from the activated scene's settings
        _loading = true;
        LoadState();
        _loading = false;
      }
    }

    void BtnSceneSave_Click(object sender, RoutedEventArgs e) {
      var active = LightingSceneService.ActiveScene;
      if (active == null || active.IsBuiltIn) {
        // 内置场景不可覆盖 → 引导用户"另存为"
        BtnSceneSaveAs_Click(sender, e);
        return;
      }
      // 将当前 UI 状态写回当前场景
      UpdateActiveSceneFromUi();
      RefreshSceneCombo();
    }

    void BtnSceneSaveAs_Click(object sender, RoutedEventArgs e) {
      // 弹出命名对话框
      string name = Microsoft.VisualBasic.Interaction.InputBox(
        Strings.LightingSceneSaveAsTip, Strings.LightingSceneLabel, "我的场景");
      if (string.IsNullOrWhiteSpace(name)) return;

      var currentState = SnapshotCurrentState();
      var scene = LightingSceneService.CreateSceneFromCurrent(name, currentState);
      LightingSceneService.AddScene(scene);
      RefreshSceneCombo();
      // 选中新场景
      SelectSceneInCombo(scene.Id);
    }

    void BtnSceneDelete_Click(object sender, RoutedEventArgs e) {
      var active = LightingSceneService.ActiveScene;
      if (active == null || active.IsBuiltIn) return;
      var result = MessageBox.Show(
        string.Format("确定要删除场景 \"{0}\" 吗？", active.Name),
        Strings.LightingSceneDelete, MessageBoxButton.YesNo, MessageBoxImage.Question);
      if (result != MessageBoxResult.Yes) return;
      LightingSceneService.RemoveScene(active.Id);
      RefreshSceneCombo();
    }

    void OnSceneChanged(OmenSuperHub.Models.LightingScene scene) {
      // 由外部触发（性能模式/定时）→ 重新加载 UI
      Dispatcher.BeginInvoke(new Action(() => {
        _loading = true;
        LoadState();
        _loading = false;
        SelectSceneInCombo(scene?.Id);
      }));
    }

    void OnScenesListChanged(OmenSuperHub.Models.LightingScene[] scenes) {
      Dispatcher.BeginInvoke(new Action(RefreshSceneCombo));
    }

    void RefreshSceneCombo() {
      _sceneLoading = true;
      SceneCombo.Items.Clear();
      var scenes = LightingSceneService.AllScenes;
      string activeId = LightingSceneService.ActiveScene?.Id;
      foreach (var scene in scenes) {
        string display = LightingSceneService.GetSceneDisplayName(scene);
        SceneCombo.Items.Add(new ComboBoxItem { Content = display, Tag = scene.Id });
      }
      // 选中当前激活的场景
      for (int i = 0; i < SceneCombo.Items.Count; i++) {
        if (SceneCombo.Items[i] is ComboBoxItem item && (string)item.Tag == activeId) {
          SceneCombo.SelectedIndex = i;
          break;
        }
      }
      _sceneLoading = false;

      // 更新场景描述 + 同步触发模式
      var active = LightingSceneService.ActiveScene;
      UpdateTriggerCombo(active);
      if (SceneInfo != null && active != null) {
        var parts = new System.Text.StringBuilder();
        if (!string.IsNullOrEmpty(active.TriggerMode)) parts.Append($"触发模式: {active.TriggerMode}  ");
        if (!string.IsNullOrEmpty(active.ScheduledTime)) parts.Append($"定时: {active.ScheduledTime}  ");
        if (active.IsDefault) parts.Append(Strings.LightingSceneBuiltIn + "默认  ");
        if (active.IsBuiltIn) parts.Append("(" + Strings.LightingSceneBuiltIn + ")  ");
        SceneInfo.Text = parts.ToString().TrimEnd();
        SceneInfo.Visibility = parts.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
      }
    }

    void SelectSceneInCombo(string sceneId) {
      for (int i = 0; i < SceneCombo.Items.Count; i++) {
        if (SceneCombo.Items[i] is ComboBoxItem item && (string)item.Tag == sceneId) {
          _sceneLoading = true;
          SceneCombo.SelectedIndex = i;
          _sceneLoading = false;
          return;
        }
      }
    }

    /// <summary>同步触发模式 ComboBox 到当前场景的 TriggerMode</summary>
    void UpdateTriggerCombo(LightingScene active) {
      if (SceneTriggerCombo == null) return;
      _sceneLoading = true;
      string trigger = active?.TriggerMode ?? "";
      int idx = trigger switch {
        "Extreme" => 1, "GpuPriority" => 2, "LightUse" => 3, _ => 0
      };
      // 内置场景也允许修改触发模式
      SceneTriggerCombo.SelectedIndex = idx;
      SceneTriggerCombo.IsEnabled = active != null;
      _sceneLoading = false;
    }

    void SceneTrigger_SelectionChanged(object sender, SelectionChangedEventArgs e) {
      if (_loading || _sceneLoading) return;
      var active = LightingSceneService.ActiveScene;
      if (active == null) return;
      if (SceneTriggerCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag) {
        active.TriggerMode = string.IsNullOrEmpty(tag) ? null : tag;
        LightingSceneService.UpdateScene(active);
      }
    }

    /// <summary>将当前 UI 状态更新到激活场景</summary>
    void UpdateActiveSceneFromUi() {
      var active = LightingSceneService.ActiveScene;
      if (active == null || active.IsBuiltIn) return;
      var snap = SnapshotCurrentState();
      active.Device = snap.Device;
      active.Interface = snap.Interface;
      active.Brightness = snap.Brightness;
      active.Animation = snap.Animation;
      active.Direction = snap.Direction;
      active.Theme = snap.Theme;
      active.ZoneColors = snap.ZoneColors;
      active.PerKeyStaticColor = snap.PerKeyStaticColor;
      active.PerKeyAnimation = snap.PerKeyAnimation;
      active.PerKeyBrightness = snap.PerKeyBrightness;
      active.PerKeySpeed = snap.PerKeySpeed;
      active.LightBarColors = snap.LightBarColors;
      active.LightBarBrightness = snap.LightBarBrightness;
      LightingSceneService.UpdateScene(active);
    }

    LightingState SnapshotCurrentState() {
      var zoneHex = new string[4];
      for (int i = 0; i < 4; i++) {
        var c = _zoneColors[i];
        zoneHex[i] = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
      }
      var lbHex = new string[4];
      for (int i = 0; i < 4; i++) {
        var c = _lbColors[i];
        lbHex[i] = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
      }
      return new LightingState {
        Enabled = LightEnableToggle?.IsChecked == true,
        Device = ConfigService.LightingDevice,
        Interface = ConfigService.LightingInterface,
        Brightness = ConfigService.LightingBrightness,
        Animation = ConfigService.LightingAnimation,
        Direction = ConfigService.LightingDirection,
        Theme = ConfigService.LightingTheme,
        ZoneColors = zoneHex,
        PerKeyStaticColor = ConfigService.PerKeyStaticColor,
        PerKeyAnimation = ConfigService.PerKeyAnimation,
        PerKeyBrightness = ConfigService.PerKeyBrightness,
        PerKeySpeed = ConfigService.PerKeySpeed,
        LightBarColors = lbHex,
        LightBarBrightness = _lbBrightness,
      };
    }
  }

  // ponytail: lighting.json — mirrors ConfigService lighting fields + adds Enabled + per-zone hex colors
  [DataContract]
  public class LightingState {
    [DataMember(Order = 0)] public bool Enabled { get; set; }
    [DataMember(Order = 1)] public string Device { get; set; } = "keyboard";
    [DataMember(Order = 2)] public string Interface { get; set; } = "BasicFourZone";
    [DataMember(Order = 3)] public int Brightness { get; set; } = 100;
    [DataMember(Order = 4)] public string Animation { get; set; } = "None";
    [DataMember(Order = 5)] public string Direction { get; set; } = "Left";
    [DataMember(Order = 6)] public string Theme { get; set; } = "Custom";
    [DataMember(Order = 7)] public string[] ZoneColors { get; set; } = new[] { "#FFFFFF", "#FFFFFF", "#FFFFFF", "#FFFFFF" };
    [DataMember(Order = 8)] public string PerKeyStaticColor { get; set; } = "Red";
    [DataMember(Order = 9)] public string PerKeyAnimation { get; set; } = "None";
    [DataMember(Order = 10)] public int PerKeyBrightness { get; set; } = 100;
    [DataMember(Order = 11)] public int PerKeySpeed { get; set; } = 1;
    // 灯条独立面板持久化 — 与键盘 ZoneColors 分离
    [DataMember(Order = 12)] public string[] LightBarColors { get; set; }
    [DataMember(Order = 13)] public int LightBarBrightness { get; set; } = 100;
  }
}
