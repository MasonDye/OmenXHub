using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OmenSuperHub.Services;
using static OmenSuperHub.OmenLighting;

namespace OmenSuperHub.Pages {
  public partial class LightingPage : Page {
    bool _loading;
#pragma warning disable CS0169
    int _animSpeed;
#pragma warning restore CS0169
    Color[] _zoneColors = new Color[] { Colors.White, Colors.White, Colors.White, Colors.White };

    public LightingPage() {
      InitializeComponent();
      Loaded += (s, e) => { _loading = true; LoadState(); _loading = false; };
    }

    void LoadState() {
      LightDevCombo.SelectedIndex = ConfigService.LightingDevice == "lightbar" ? 1 : 0;
      LightProtoCombo.SelectedIndex = ConfigService.LightingInterface == "Dojo" ? 1 : 0;
      LightBrightSlider.Value = ConfigService.LightingBrightness;
      LightBrightVal.Text = ConfigService.LightingBrightness + "%";
      string[] anims = { "None", "ColorCycle", "Starlight", "Breathing", "Wave", "Raindrop", "AudioPulse", "Confetti", "Sun", "Swipe" };
      int animIdx = Array.IndexOf(anims, ConfigService.LightingAnimation);
      if (animIdx >= 0) AnimCombo.SelectedIndex = animIdx;
      else AnimCombo.SelectedIndex = 0;
      AnimSpeedCombo.SelectedIndex = 1;
    }

    void LightDev_SelectionChanged(object s, SelectionChangedEventArgs e) {
      if (_loading) return;
      ConfigService.LightingDevice = LightDevCombo.SelectedIndex == 1 ? "lightbar" : "keyboard";
      ConfigService.Save("LightingDevice");
    }

    void LightProto_SelectionChanged(object s, SelectionChangedEventArgs e) {
      if (_loading) return;
      ConfigService.LightingInterface = LightProtoCombo.SelectedIndex == 1 ? "Dojo" : "Basic";
      ConfigService.Save("LightingInterface");
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
    }

    void AnimSpeed_SelectionChanged(object s, SelectionChangedEventArgs e) {
      if (_loading) return;
      _animSpeed = AnimSpeedCombo.SelectedIndex;
    }

    void ZoneColor_SelectionChanged(object s, SelectionChangedEventArgs e) {
      if (_loading) return;
      if (s is ComboBox combo && combo.SelectedItem is ComboBoxItem item) {
        int zone = int.Parse(combo.Name.Substring(4, 1)) - 1;
        string colorName = (string)item.Tag;
        _zoneColors[zone] = colorName switch {
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
        var iface = ConfigService.LightingInterface == "Dojo" ? LightingControlInterface.Dojo : LightingControlInterface.BasicFourZone;
        var colors = new List<System.Windows.Media.Color>(_zoneColors);
        OmenLighting.SetZoneStaticColor(device, colors, (byte)ConfigService.LightingBrightness, iface);
      } catch { }
    }

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
