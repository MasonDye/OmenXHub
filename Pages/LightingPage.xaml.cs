// LightingPage.cs - 键盘灯效页面
// 设备/协议选择，区域颜色设置，亮度/动画速度调节
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Hp.Bridge.Client.SDKs.McuSDK2.Common.DataStructure; // LightingSetting
using OmenSuperHub.Services;
using OmenSuperHub.Utils;
using static OmenSuperHub.OmenLighting;

namespace OmenSuperHub.Pages {
  public partial class LightingPage : Page {
    bool _loading;
    bool _colorPicking;
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

    // ponytail: 硬件能力一次性探测 — 参考 WinForms OmenSuperHub 全局 supportAni/supportLightbar/kbType
    // 在 LoadState 中赋值，ApplyLightingVisibility 消费。避免每次 SelectionChanged 都打 HP SDK。
    bool _supportAni;
    bool _supportLightBar;
    Omen.OmenFourZoneLighting.KeyboardType _kbType = Omen.OmenFourZoneLighting.KeyboardType.Normal;

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
      };
      Unloaded += (s, e) => ClosePerKeyHandleLocked();
    }

    void LoadState() {
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
      ApplyLightingVisibility();

      // ponytail: auto-detect keyboard type and light bar via HP SDK if available
      if (FourZoneHelper.Available) {
        string kbTypeName = FourZoneHelper.GetKeyboardTypeName();
        bool lbSupported = FourZoneHelper.IsLightBarSupported();
        HeadingLighting.Text = $"{Strings.LightingControl} — {kbTypeName}" +
          (lbSupported ? $" | {Strings.LightingLightBar}" : "");
      }

      // ponytail: 一次性硬件能力探测 — 参考 WinForms Program.cs 启动期的 supportAni/supportLightbar/kbType
      // 赋值。IsAnimationSupported() 内部 FourZoneSupportHelper 有"二次调用返回 false"的缺陷，
      // 所以只调用一次并缓存到字段。try/catch 兜底：非 OMEN 硬件或 SDK 异常时退化为全 false（最保守）。
      try { _kbType = FourZoneHelper.GetKeyboardType(); } catch { _kbType = Omen.OmenFourZoneLighting.KeyboardType.Normal; }
      try { _supportLightBar = FourZoneHelper.IsLightBarSupported(); } catch { _supportLightBar = false; }
      try { _supportAni = OmenLighting.IsAnimationSupported(); } catch { _supportAni = false; }

      // ponytail: 硬件不支持但持久化值存在 → 复位 config + UI，避免脏值在不可见控件里潜伏。
      // _loading=true 期间设 SelectedIndex 不会触发 SelectionChanged 副作用。
      if (!_supportAni && ConfigService.LightingAnimation != "None") {
        ConfigService.LightingAnimation = "None";
        ConfigService.Save("LightingAnimation");
        if (AnimCombo != null) AnimCombo.SelectedIndex = 0;
      }
      if (!_supportLightBar && ConfigService.LightingDevice == "lightbar") {
        ConfigService.LightingDevice = "keyboard";
        ConfigService.Save("LightingDevice");
        if (LightDevCombo != null) LightDevCombo.SelectedIndex = 0;
      }
      if (_kbType != Omen.OmenFourZoneLighting.KeyboardType.Rgb && ConfigService.LightingInterface == "PerKey") {
        ConfigService.LightingInterface = "BasicFourZone";
        ConfigService.Save("LightingInterface");
        if (LightProtoCombo != null) LightProtoCombo.SelectedIndex = 0;
      }

      // 再次应用：现在 _supportAni/_supportLightBar/_kbType 已就位
      ApplyLightingVisibility();

      // ponytail: restore PerKey persisted state (only used when the PerKey card is visible).
      // Use OmenLighting.AnimNames as the canonical order so XAML ComboBox + config stay aligned.
      string[] pkStatics = { "Red", "Green", "Blue", "White", "Cyan", "Pink", "Yellow" };
      int spk = Array.IndexOf(pkStatics, ConfigService.PerKeyStaticColor);
      if (PerKeyStaticCombo != null) PerKeyStaticCombo.SelectedIndex = spk >= 0 ? spk : 0;
      int apk = OmenLighting.AnimIndex(ConfigService.PerKeyAnimation);
      if (PerKeyAnimCombo != null) PerKeyAnimCombo.SelectedIndex = apk >= 0 ? apk : 0;
      if (PerKeyBrightSlider != null) {
        PerKeyBrightSlider.Value = ConfigService.PerKeyBrightness;
        PerKeyBrightVal.Text = ConfigService.PerKeyBrightness + "%";
      }
    }

    void LightDev_SelectionChanged(object s, SelectionChangedEventArgs e) {
      if (_loading) return;
      ConfigService.LightingDevice = LightDevCombo.SelectedIndex == 1 ? "lightbar" : "keyboard";
      ConfigService.Save("LightingDevice");
    }

    void LightProto_SelectionChanged(object s, SelectionChangedEventArgs e) {
      if (_loading) return;
      // ponytail: index 3 = PerKey protocol (added in this pass)
      ConfigService.LightingInterface = LightProtoCombo.SelectedIndex switch { 1 => "Dojo", 2 => "HpSdk", 3 => "PerKey", _ => "BasicFourZone" };
      ConfigService.Save("LightingInterface");
      ApplyLightingVisibility();
    }

    void LightBright_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) {
      if (LightBrightVal != null) LightBrightVal.Text = (int)e.NewValue + "%";
      if (!_loading) {
        ConfigService.LightingBrightness = (byte)(int)e.NewValue;
        ConfigService.Save("LightingBrightness");
      }
    }

    void Anim_SelectionChanged(object s, SelectionChangedEventArgs e) {
      if (_loading) return;
      int idx = AnimCombo.SelectedIndex;
      ConfigService.LightingAnimation = (idx >= 0 && idx < OmenLighting.AnimNames.Length)
        ? OmenLighting.AnimNames[idx] : "None";
      ConfigService.Save("LightingAnimation");
      ApplyLightingVisibility();
    }

    void AnimDir_SelectionChanged(object s, SelectionChangedEventArgs e) {
      if (_loading || _colorPicking) return;
      ConfigService.LightingDirection = AnimDirCombo.SelectedIndex == 1 ? "Right" : "Left";
      ConfigService.Save("LightingDirection");
    }

    void AnimTheme_SelectionChanged(object s, SelectionChangedEventArgs e) {
      if (_loading || _colorPicking) return;
      string theme = AnimThemeCombo.SelectedIndex switch { 1 => "Volcano", 2 => "Jungle", 3 => "Ocean", 4 => "Custom", _ => "Galaxy" };
      ConfigService.LightingTheme = theme;
      ConfigService.Save("LightingTheme");
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
      }
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
          byte speed = (byte)AnimSpeedCombo.SelectedIndex;
          byte direction = (byte)(AnimDirCombo.SelectedIndex == 1 ? 1 : 0);
          byte theme = (byte)AnimThemeCombo.SelectedIndex;
          Logger.Verbose($"ApplyLight: iface={iface} device={device} effectId={animIdx} speed={speed} dir={direction} theme={theme} bright={ConfigService.LightingBrightness}");
          if (!OmenLighting.SupportsEffect(iface, (byte)animIdx)) {
            DialogHelper.Warn(Strings.LightingCapabilityAnimBasic, Strings.LightingControl);
            return;
          }
          bool ok = OmenLighting.SetZoneAnimation(device, (byte)animIdx, speed, direction, theme, colors,
            (byte)ConfigService.LightingBrightness, iface);
          if (!ok) DialogHelper.Warn(Strings.LightingCapabilityAnimBasic, Strings.LightingControl);
        } else {
          Logger.Verbose($"ApplyLight(static): iface={iface} device={device} bright={ConfigService.LightingBrightness}");
          OmenLighting.SetZoneStaticColor(device, colors, (byte)ConfigService.LightingBrightness, iface);
        }
      } catch (Exception ex) { Logger.Error($"ApplyLightBtn_Click: {ex.Message}"); }
    }

    void ApplyLightingVisibility() {
      // ponytail: 参考 WinForms Program.Menu.cs:908-1006 的可见性规则：
      //   1. 动画卡仅 _supportAni 时可见（cycle>260）
      //   2. 灯条设备选项仅 _supportLightBar 时可见
      //   3. PerKey 协议选项仅 kbType==Rgb 时可见（PerKey 卡本身沿用 kbIsPerKey||isPerKeyProto）
      // Zone 颜色卡始终显示 4 区——参考 AddLightingUI 也是固定生成 4 个 zone picker，
      // OneZone/FourZone 区分仅用于状态显示（本页无状态显示区）。

      // 1. 动画卡
      if (AnimCard != null)
        AnimCard.Visibility = _supportAni ? Visibility.Visible : Visibility.Collapsed;

      // 2. 灯条设备选项
      if (LightBarItem != null)
        LightBarItem.Visibility = _supportLightBar ? Visibility.Visible : Visibility.Collapsed;

      // 3. PerKey 协议选项 + PerKey 卡
      bool kbIsPerKey = _kbType == Omen.OmenFourZoneLighting.KeyboardType.Rgb;
      if (PerKeyProtoItem != null)
        PerKeyProtoItem.Visibility = kbIsPerKey ? Visibility.Visible : Visibility.Collapsed;
      bool isPerKeyProto = LightProtoCombo != null && LightProtoCombo.SelectedIndex == 3;
      bool showPerKey = kbIsPerKey || isPerKeyProto;
      if (PerKeyCard != null) PerKeyCard.Visibility = showPerKey ? Visibility.Visible : Visibility.Collapsed;

      // 4. Dojo 高亮度面板
      bool isDojo = ConfigService.LightingInterface == "Dojo";
      if (DojoHighBrightPanel != null) DojoHighBrightPanel.Visibility = isDojo ? Visibility.Visible : Visibility.Collapsed;

      // 5. 方向/主题仅 Dojo+动画下发,UI 一处统一管控避免双函数状态分歧
      bool animEnabled = isDojo && AnimCombo != null && AnimCombo.SelectedIndex > 0;
      if (AnimDirCombo != null) AnimDirCombo.IsEnabled = animEnabled;
      if (AnimThemeCombo != null) AnimThemeCombo.IsEnabled = animEnabled;

      // 6. entering a non-PerKey protocol drops the cached HID handle so it
      // doesn't linger and block other Per-key-aware apps (e.g. OMEN Light Studio).
      if (!showPerKey) ClosePerKeyHandleLocked();
    }

    void BtnBrightHigh_Click(object sender, RoutedEventArgs e) {
      if (_loading) return;
      if (sender is not Button btn || !byte.TryParse(btn.Tag?.ToString(), out byte v)) return;
      ConfigService.LightingBrightness = v;
      ConfigService.Save("LightingBrightness");
      if (LightBrightVal != null) LightBrightVal.Text = v + "%";
      bool isDojo = ConfigService.LightingInterface == "Dojo";
      var device = ConfigService.LightingDevice == "lightbar" ? LightingDevice.LightBar : LightingDevice.Keyboard;
      try { if (isDojo) OmenLighting.SetZoneBrightness(device, v, LightingControlInterface.Dojo); }
      catch (Exception ex) { Logger.Error($"BtnBrightHigh_Click: {ex.Message}"); }
    }

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
        if (h > 0) _perKeyHandle = h; // negative stays — next call retries
        return h;
      }
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
    }

    void PerKeyAnim_SelectionChanged(object s, SelectionChangedEventArgs e) {
      if (_loading || s is not ComboBox cb || cb.SelectedItem is not ComboBoxItem item) return;
      ConfigService.PerKeyAnimation = (string)item.Tag;
      ConfigService.Save("PerKeyAnimation");
      // Animation selection now drives the apply path; static color picker still
      // honours the latest selection when animation is reverted to None.
      PerKeyLiveApply();
    }

    void PerKeyBright_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) {
      if (PerKeyBrightVal != null) PerKeyBrightVal.Text = (int)e.NewValue + "%";
      if (_loading) return;
      ConfigService.PerKeyBrightness = (byte)(int)e.NewValue;
      ConfigService.Save("PerKeyBrightness");
      // Live brightness: just one IOCTL — cheap enough to push on every tick.
      int h = EnsurePerKeyHandle();
      if (h <= 0) { UpdatePerKeyStatus(Strings.LightingCapabilityPerKeyConnect); return; }
      PerKeyBackgroundRun(h, PerKeySetBrightnessBg, ok =>
        UpdatePerKeyStatus(ok ? Strings.LightingPerKeyBrightness + ": " + (int)e.NewValue + "%"
                              : Strings.LightingCapabilityPerKeyConnect));
    }

    void PerKeyApply_Click(object sender, RoutedEventArgs e) {
      // ponytail: 4 个 ComboBox + 亮度滑条都已是实时 RAM 写,Apply 按钮的独有语义
      // 是 StorePerKeyToFlash(冷启动保留)—— live updates 只写 RAM,不磨损 flash。
      // 不再每次重新 Open(破坏缓存语义),改用 EnsurePerKeyHandle。
      int h = EnsurePerKeyHandle();
      if (h <= 0) {
        UpdatePerKeyStatus(Strings.LightingCapabilityPerKeyConnect);
        DialogHelper.Warn(Strings.LightingCapabilityPerKeyConnect, Strings.LightingControl);
        return;
      }
      PerKeyBackgroundRun(h, PerKeyFlashBg, ok =>
        UpdatePerKeyStatus(ok ? Strings.LightingPerKeyFlashSaved : Strings.KeyboardConnectFail));
    }

    void PerKeyLiveApplyStatic() {
      int h = EnsurePerKeyHandle();
      if (h <= 0) { UpdatePerKeyStatus(Strings.LightingCapabilityPerKeyConnect); return; }
      PerKeyBackgroundRun(h, PerKeyWriteStaticBg, ok =>
        UpdatePerKeyStatus(ok ? Strings.LightingPerKeyStaticColor + ": " + ConfigService.PerKeyStaticColor
                              : Strings.LightingCapabilityPerKeyConnect));
    }

    void PerKeyLiveApply() {
      int h = EnsurePerKeyHandle();
      if (h <= 0) { UpdatePerKeyStatus(Strings.LightingCapabilityPerKeyConnect); return; }
      PerKeyBackgroundRun(h, PerKeyWriteAllBg, ok =>
        UpdatePerKeyStatus(ok ? Strings.LightingPerKeyAnimation + ": " + ConfigService.PerKeyAnimation
                              : Strings.LightingCapabilityPerKeyConnect));
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
        return OmenLighting.SetPerKeyStaticColor(h, _pkR, _pkG, _pkB).GetAwaiter().GetResult();
      } catch (Exception ex) { Logger.Error($"PerKeyWriteStaticBg: {ex.Message}"); return false; }
    }

    static bool PerKeyWriteAllBg(int h) {
      try {
        if (ConfigService.PerKeyAnimation == "None") return PerKeyWriteStaticBg(h);
        byte mcuEff = OmenLighting.PerKeyEffectId(ConfigService.PerKeyAnimation);
        var setting = new LightingSetting {
          Effect = mcuEff, LedSpeed = 1, Direction = 0,
          Brightness = ConfigService.PerKeyBrightness, ColorNumber = 4, ShowMode = 0
        };
        return OmenLighting.SetPerKeyAnimation(h, setting).GetAwaiter().GetResult();
      } catch (Exception ex) { Logger.Error($"PerKeyWriteAllBg: {ex.Message}"); return false; }
    }

    static bool PerKeySetBrightnessBg(int h) {
      try { return OmenLighting.SetPerKeyBrightness(h, ConfigService.PerKeyBrightness).GetAwaiter().GetResult(); }
      catch (Exception ex) { Logger.Error($"PerKeySetBrightnessBg: {ex.Message}"); return false; }
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

    internal static void ReplaySavedLighting() {
      try {
        var device = ConfigService.LightingDevice == "lightbar" ? LightingDevice.LightBar : LightingDevice.Keyboard;
        string iface = ConfigService.LightingInterface;
        var ci = iface == "Dojo" ? LightingControlInterface.Dojo : LightingControlInterface.BasicFourZone;
        byte bright = ConfigService.LightingBrightness;
        // ponytail: shared OmenLighting.LookupColor — same KeyError-fallback Pink fix
        // as the live UI path; previously ReplaySaved's Pink=(255,0,255) diverged from
        // Zone's Pink=(0xFF,0x69,0xB4) on cold boot replay.
        System.Windows.Media.Color c = ColorFromName(ConfigService.LightingColor);
        var colors4 = new List<System.Windows.Media.Color> { c, c, c, c };
        if (iface == "HpSdk") {
          OmenLighting.FourZoneHelper.SetStaticColor(device, colors4, bright);
          return;
        }
        if (!string.IsNullOrEmpty(ConfigService.LightingAnimation) && ConfigService.LightingAnimation != "None") {
          byte animId = OmenLighting.ZoneEffectId(ConfigService.LightingAnimation);
          byte speed = 1;
          byte direction = (byte)(ConfigService.LightingDirection == "Right" ? 1 : 0);
          byte theme = ConfigService.LightingTheme switch { "Volcano" => (byte)1, "Jungle" => (byte)2, "Ocean" => (byte)3, "Custom" => (byte)4, _ => (byte)0 };
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
  }
}
