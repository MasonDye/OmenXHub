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
#pragma warning disable CS0169
    int _animSpeed;
#pragma warning restore CS0169
    bool _colorPicking;
    Color[] _zoneColors = new Color[] { Colors.White, Colors.White, Colors.White, Colors.White };
    int[] _lastZoneIdx = new int[] { 0, 0, 0, 0 };

    public LightingPage() {
      InitializeComponent();
      Loaded += (s, e) => { _loading = true; LoadState(); _loading = false; };
    }

    void LoadState() {
      LightDevCombo.SelectedIndex = ConfigService.LightingDevice == "lightbar" ? 1 : 0;
      // ponytail: restore protocol, 0=Basic 1=Dojo 2=HpSdk
      LightProtoCombo.SelectedIndex = ConfigService.LightingInterface == "Dojo" ? 1 :
        ConfigService.LightingInterface == "HpSdk" ? 2 : 0;
      LightBrightSlider.Value = ConfigService.LightingBrightness;
      LightBrightVal.Text = ConfigService.LightingBrightness + "%";
      string[] anims = { "None", "ColorCycle", "Starlight", "Breathing", "Wave", "Raindrop", "AudioPulse", "Confetti", "Sun", "Swipe" };
      int animIdx = Array.IndexOf(anims, ConfigService.LightingAnimation);
      if (animIdx >= 0) AnimCombo.SelectedIndex = animIdx;
      else AnimCombo.SelectedIndex = 0;
      AnimSpeedCombo.SelectedIndex = 1;
      AnimDirCombo.SelectedIndex = ConfigService.LightingDirection == "Right" ? 1 : 0;
      AnimThemeCombo.SelectedIndex = ConfigService.LightingTheme switch {
        "Volcano" => 1, "Jungle" => 2, "Ocean" => 3, "Custom" => 4, _ => 0
      };
      UpdateAnimParamEnablement();

      // ponytail: auto-detect keyboard type and light bar via HP SDK if available
      if (FourZoneHelper.Available) {
        string kbTypeName = FourZoneHelper.GetKeyboardTypeName();
        bool lbSupported = FourZoneHelper.IsLightBarSupported();
        HeadingLighting.Text = $"{Strings.LightingControl} — {kbTypeName}" +
          (lbSupported ? $" | {Strings.LightingLightBar}" : "");
      }

      // ponytail: restore PerKey persisted state (only used when the PerKey card is visible)
      string[] pkStatics = { "Red", "Green", "Blue", "White", "Cyan", "Pink", "Yellow" };
      int spk = Array.IndexOf(pkStatics, ConfigService.PerKeyStaticColor);
      if (PerKeyStaticCombo != null) PerKeyStaticCombo.SelectedIndex = spk >= 0 ? spk : 0;
      string[] pkAnims = { "None", "ColorCycle", "Starlight", "Breathing", "Wave", "Raindrop", "AudioPulse", "Confetti", "Sun", "Swipe" };
      int apk = Array.IndexOf(pkAnims, ConfigService.PerKeyAnimation);
      if (PerKeyAnimCombo != null) PerKeyAnimCombo.SelectedIndex = apk >= 0 ? apk : 0;
      if (PerKeyBrightSlider != null) {
        PerKeyBrightSlider.Value = ConfigService.PerKeyBrightness;
        PerKeyBrightVal.Text = ConfigService.PerKeyBrightness + "%";
      }
      ApplyLightingVisibility();
    }

    void LightDev_SelectionChanged(object s, SelectionChangedEventArgs e) {
      if (_loading) return;
      ConfigService.LightingDevice = LightDevCombo.SelectedIndex == 1 ? "lightbar" : "keyboard";
      ConfigService.Save("LightingDevice");
    }

    void LightProto_SelectionChanged(object s, SelectionChangedEventArgs e) {
      if (_loading) return;
      // ponytail: index 3 = PerKey protocol (added in this pass)
      ConfigService.LightingInterface = LightProtoCombo.SelectedIndex switch { 1 => "Dojo", 2 => "HpSdk", 3 => "PerKey", _ => "Basic" };
      ConfigService.Save("LightingInterface");
      UpdateAnimParamEnablement();
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
      string[] anims = { "None", "ColorCycle", "Starlight", "Breathing", "Wave", "Raindrop", "AudioPulse", "Confetti", "Sun", "Swipe" };
      int idx = AnimCombo.SelectedIndex;
      ConfigService.LightingAnimation = idx >= 0 && idx < anims.Length ? anims[idx] : "None";
      ConfigService.Save("LightingAnimation");
      UpdateAnimParamEnablement();
    }

    void AnimSpeed_SelectionChanged(object s, SelectionChangedEventArgs e) {
      if (_loading) return;
      _animSpeed = AnimSpeedCombo.SelectedIndex;
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

    void UpdateAnimParamEnablement() {
      if (AnimDirCombo == null || AnimThemeCombo == null) return;
      bool isDojo = ConfigService.LightingInterface == "Dojo";
      bool isAnim = AnimCombo != null && AnimCombo.SelectedIndex > 0;
      bool enable = isDojo && isAnim;
      AnimDirCombo.IsEnabled = enable;
      AnimThemeCombo.IsEnabled = enable;
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
        _zoneColors[zone] = tag switch {
          "Red" => Colors.Red,
          "Green" => Colors.Green,
          "Blue" => Colors.Blue,
          "White" => Colors.White,
          "Cyan" => Colors.Cyan,
          "Pink" => Color.FromRgb(0xFF, 0x69, 0xB4),
          "Yellow" => Colors.Yellow,
          _ => Colors.White,
        };
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
      bool kbIsPerKey = false;
      try {
        kbIsPerKey = FourZoneHelper.Available &&
                     OmenLighting.FourZoneHelper.GetKeyboardType() == Omen.OmenFourZoneLighting.KeyboardType.Rgb;
      } catch { }
      bool isPerKeyProto = LightProtoCombo != null && LightProtoCombo.SelectedIndex == 3;
      bool showPerKey = kbIsPerKey || isPerKeyProto;
      if (PerKeyCard != null) PerKeyCard.Visibility = showPerKey ? Visibility.Visible : Visibility.Collapsed;
      bool isDojo = ConfigService.LightingInterface == "Dojo";
      if (DojoHighBrightPanel != null) DojoHighBrightPanel.Visibility = isDojo ? Visibility.Visible : Visibility.Collapsed;
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

    void PerKeyStatic_SelectionChanged(object s, SelectionChangedEventArgs e) {
      if (_loading || s is not ComboBox cb || cb.SelectedItem is not ComboBoxItem item) return;
      ConfigService.PerKeyStaticColor = (string)item.Tag;
      ConfigService.Save("PerKeyStaticColor");
    }

    void PerKeyAnim_SelectionChanged(object s, SelectionChangedEventArgs e) {
      if (_loading || s is not ComboBox cb || cb.SelectedItem is not ComboBoxItem item) return;
      ConfigService.PerKeyAnimation = (string)item.Tag;
      ConfigService.Save("PerKeyAnimation");
    }

    void PerKeyBright_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) {
      if (PerKeyBrightVal != null) PerKeyBrightVal.Text = (int)e.NewValue + "%";
      if (_loading) return;
      ConfigService.PerKeyBrightness = (byte)(int)e.NewValue;
      ConfigService.Save("PerKeyBrightness");
    }

    void PerKeyApply_Click(object sender, RoutedEventArgs e) {
      int handle = OmenLighting.OpenPerKeyKeyboard();
      if (handle <= 0) {
        DialogHelper.Warn(Strings.LightingCapabilityPerKeyConnect, Strings.LightingControl);
        return;
      }
      try {
        if (ConfigService.PerKeyAnimation == "None") {
          var (r, g, b) = PerKeyColorRgb(ConfigService.PerKeyStaticColor);
          const int keyCount = 144;
          var rs = new byte[keyCount]; var gs = new byte[keyCount]; var bs = new byte[keyCount];
          for (int i = 0; i < keyCount; i++) { rs[i] = r; gs[i] = g; bs[i] = b; }
          OmenLighting.SetPerKeyStaticColor(handle, rs, gs, bs).Wait();
          OmenLighting.StorePerKeyToFlash(handle).Wait();
        } else {
          byte mcuEff = PerKeyAnimationToMcuEff(ConfigService.PerKeyAnimation);
          var setting = new LightingSetting {
            Effect = mcuEff, LedSpeed = 1, Direction = 0,
            Brightness = ConfigService.PerKeyBrightness, ColorNumber = 4, ShowMode = 0
          };
          OmenLighting.SetPerKeyAnimation(handle, setting).Wait();
        }
        OmenLighting.SetPerKeyBrightness(handle, ConfigService.PerKeyBrightness).Wait();
      } catch (Exception ex) {
        Logger.Error($"PerKeyApply_Click: {ex.Message}");
        DialogHelper.Warn(Strings.KeyboardConnectFail, Strings.LightingControl);
      } finally {
        try { OmenLighting.CloseDeviceAsync(handle).Wait(); } catch { }
      }
    }

    static (byte r, byte g, byte b) PerKeyColorRgb(string name) => name switch {
      "Red" => (255, 0, 0), "Green" => (0, 255, 0), "Blue" => (0, 0, 255),
      "White" => (255, 255, 255), "Cyan" => (0, 255, 255), "Pink" => (255, 0, 255),
      "Yellow" => (255, 255, 0), _ => (255, 255, 255)
    };

    static byte PerKeyAnimationToMcuEff(string animName) => animName switch {
      "ColorCycle" => 7, "Starlight" => 2, "Breathing" => 8, "Wave" => 10,
      "Raindrop" => 13, "Confetti" => 14, "Sun" => 15, "Swipe" => 16,
      _ => 4
    };

    internal static void ReplaySavedLighting() {
      try {
        var device = ConfigService.LightingDevice == "lightbar" ? LightingDevice.LightBar : LightingDevice.Keyboard;
        string iface = ConfigService.LightingInterface;
        var ci = iface == "Dojo" ? LightingControlInterface.Dojo : LightingControlInterface.BasicFourZone;
        byte bright = ConfigService.LightingBrightness;
        if (iface == "HpSdk") {
          var c = LightingColorFromName(ConfigService.LightingColor);
          var colors = new List<System.Windows.Media.Color> { c, c, c, c };
          OmenLighting.FourZoneHelper.SetStaticColor(device, colors, bright);
          return;
        }
        if (!string.IsNullOrEmpty(ConfigService.LightingAnimation) && ConfigService.LightingAnimation != "None") {
          byte animId = AnimNameToId(ConfigService.LightingAnimation);
          byte speed = 1;
          byte direction = (byte)(ConfigService.LightingDirection == "Right" ? 1 : 0);
          byte theme = ConfigService.LightingTheme switch { "Volcano" => (byte)1, "Jungle" => (byte)2, "Ocean" => (byte)3, "Custom" => (byte)4, _ => (byte)0 };
          if (OmenLighting.SupportsEffect(ci, animId)) {
            var c = LightingColorFromName(ConfigService.LightingColor);
            var colors = new List<System.Windows.Media.Color> { c, c, c, c };
            OmenLighting.SetZoneAnimation(device, animId, speed, direction, theme, colors, bright, ci);
            return;
          }
        }
        var sc = LightingColorFromName(ConfigService.LightingColor);
        OmenLighting.SetZoneStaticColor(device, new List<System.Windows.Media.Color> { sc, sc, sc, sc }, bright, ci);
      } catch (Exception ex) {
        System.Diagnostics.Debug.WriteLine($"ReplaySavedLighting failed: {ex.Message}");
      }
    }

    static System.Windows.Media.Color LightingColorFromName(string name) => name switch {
      "Red" => System.Windows.Media.Color.FromRgb(255, 0, 0),
      "Green" => System.Windows.Media.Color.FromRgb(0, 255, 0),
      "Blue" => System.Windows.Media.Color.FromRgb(0, 0, 255),
      "White" => System.Windows.Media.Color.FromRgb(255, 255, 255),
      "Cyan" => System.Windows.Media.Color.FromRgb(0, 255, 255),
      "Pink" => System.Windows.Media.Color.FromRgb(255, 0, 255),
      "Yellow" => System.Windows.Media.Color.FromRgb(255, 255, 0),
      _ => System.Windows.Media.Color.FromRgb(255, 255, 255),
    };

    static byte AnimNameToId(string name) => name switch {
      "ColorCycle" => 2, "Starlight" => 3, "Breathing" => 4, "Wave" => 6,
      "Raindrop" => 7, "AudioPulse" => 8, "Confetti" => 9, "Sun" => 10, "Swipe" => 11,
      _ => (byte)0
    };

    bool _lightExpanded = true;
    const double LightCollapseWidth = 1000;

    void LightingPage_SizeChanged(object sender, SizeChangedEventArgs e) {
      if (!e.WidthChanged) return;
      if (e.NewSize.Width > LightCollapseWidth) {
        if (!_lightExpanded) { _lightExpanded = true; ExpandLightGrid(); }
      } else {
        if (_lightExpanded) { _lightExpanded = false; CollapseLightGrid(); }
      }
    }

    void ExpandLightGrid() {
      LightGrid.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
      Thickness[] expandMargins = { new(0, 0, 6, 8), new(6, 0, 0, 8), new(0, 0, 6, 0), new(6, 0, 0, 0) };
      for (int i = 0; i < 4 && i < LightGrid.Children.Count; i++) {
        var c = LightGrid.Children[i] as FrameworkElement;
        if (c == null) continue;
        int row = i / 2;
        int col = i % 2;
        Grid.SetRow(c, row);
        Grid.SetColumn(c, col);
        Grid.SetColumnSpan(c, 1);
        c.Margin = expandMargins[i];
      }
    }

    void CollapseLightGrid() {
      LightGrid.ColumnDefinitions[1].Width = new GridLength(0, GridUnitType.Pixel);
      for (int i = 0; i < 4 && i < LightGrid.Children.Count; i++) {
        var c = LightGrid.Children[i] as FrameworkElement;
        if (c == null) continue;
        Grid.SetRow(c, i);
        Grid.SetColumn(c, 0);
        Grid.SetColumnSpan(c, 1);
        c.Margin = new Thickness(0, 0, 0, 8);
      }
    }
  }
}
