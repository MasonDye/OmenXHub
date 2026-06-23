using Microsoft.Win32;
using System;
using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Appearance;

namespace OmenSuperHub.Services {
  public static class ThemeService {
    public static event Action ThemeChanged;

    public static void Initialize() {
      try {
        SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
        ApplyConfigTheme();
      } catch (Exception ex) {
        Console.WriteLine("ThemeService Init Failed: " + ex.Message);
      }
    }

    private static void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e) {
      if (e.Category == UserPreferenceCategory.General || e.Category == UserPreferenceCategory.Color) {
        if (ConfigService.Theme == "system") {
          ApplyConfigTheme();
          ThemeChanged?.Invoke();
        }
      }
    }

    public static void ApplyConfigTheme() {
      Application.Current.Dispatcher.Invoke(() => {
        try {
          switch (ConfigService.Theme) {
            case "light": ApplyTheme(true); break;
            case "dark": ApplyTheme(false); break;
            default:
              bool isLight = false;
              try {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")) {
                  if (key != null) {
                    object val = key.GetValue("AppsUseLightTheme");
                    if (val is int i && i > 0) isLight = true;
                  }
                }
              } catch { }
              ApplyTheme(isLight);
              break;
          }
          ApplyCustomAccent();
        } catch { }
      });
    }

    private static void ApplyTheme(bool isLightTheme) {
      var dicts = Application.Current.Resources.MergedDictionaries;
      ResourceDictionary colorDict = null;
      int colorIndex = -1;

      string targetSource = isLightTheme ? "Themes/Colors.Light.xaml" : "Themes/Colors.Dark.xaml";

      for (int i = 0; i < dicts.Count; i++) {
        var d = dicts[i];
        if (d.Source != null && d.Source.OriginalString.Contains("Themes/Colors.")) {
          colorDict = d;
          colorIndex = i;
          break;
        }
      }

      bool sameSource = colorDict != null &&
        colorDict.Source.OriginalString.EndsWith(targetSource, StringComparison.OrdinalIgnoreCase);

      // Always replace to force fresh brushes and trigger DynamicResource re-evaluation
      if (colorDict != null) dicts.Remove(colorDict);
      dicts.Insert(0, new ResourceDictionary { Source = new Uri(targetSource, UriKind.Relative) });

      // Switch WPF-UI base theme (no accent update — we handle it ourselves)
      ApplicationThemeManager.Apply(
        isLightTheme ? ApplicationTheme.Light : ApplicationTheme.Dark,
        updateAccent: false
      );

      // Read real Windows accent color from registry and expose as a brush
      try {
        using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM")) {
          if (key != null) {
            object val = key.GetValue("AccentColor");
            if (val is int dwmColor) {
              // DWM format: 0x00BBGGRR → convert to 0xAARRGGBB
              byte r = (byte)(dwmColor & 0xFF);
              byte g = (byte)((dwmColor >> 8) & 0xFF);
              byte b = (byte)((dwmColor >> 16) & 0xFF);
              var accentColor = Color.FromRgb(r, g, b);
              var accentBrush = new SolidColorBrush(accentColor);
              // Override WPF-UI's accent brushes so the indicator follows system accent
              Application.Current.Resources["SystemAccentColorSecondaryBrush"] = accentBrush;
              Application.Current.Resources["SystemAccentColorSecondary"] = accentColor;
            }
          }
        }
      } catch { }

      // Refresh OmenBrand to force DynamicResource re-evaluation with new palette
      ResourceDictionary omenBrand = null;
      int omenIndex = -1;
      for (int i = 0; i < dicts.Count; i++) {
        var d = dicts[i];
        if (d.Source != null && d.Source.OriginalString.Contains("OmenBrand.xaml")) {
          omenBrand = d;
          omenIndex = i;
          break;
        }
      }
      if (omenBrand != null) {
        dicts.Remove(omenBrand);
        dicts.Insert(omenIndex >= 0 ? omenIndex : 0, new ResourceDictionary { Source = new Uri("Themes/OmenBrand.xaml", UriKind.Relative) });
      }
    }

    public static void ApplyCustomAccent() {
      if (ConfigService.AccentColorSource != "custom") return;
      try {
        var color = (Color)ColorConverter.ConvertFromString(ConfigService.AccentColor);
        var dicts = Application.Current.Resources.MergedDictionaries;
        ResourceDictionary colorDict = null;
        for (int i = 0; i < dicts.Count; i++) {
          var d = dicts[i];
          if (d.Source != null && d.Source.OriginalString.Contains("Themes/Colors.")) {
            colorDict = d;
            break;
          }
        }
        if (colorDict != null) {
          colorDict["AccentOmen"] = color;
          colorDict["AccentOmenBrush"] = new SolidColorBrush(color);
        }
      } catch { }
    }

    public static void ResetAccent() {
      try {
        var dicts = Application.Current.Resources.MergedDictionaries;
        ResourceDictionary colorDict = null;
        bool isLight = ConfigService.Theme == "light";
        if (ConfigService.Theme == "system") {
          try {
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")) {
              if (key != null) {
                object val = key.GetValue("AppsUseLightTheme");
                if (val is int i && i > 0) isLight = true;
              }
            }
          } catch { }
        }
        string defaultColor = isLight ? "#FF000000" : "#FFFFFFFF";
        string defaultLight = isLight ? "#FF333333" : "#FFE0E0E0";
        string defaultDim = isLight ? "#FF808080" : "#FF808080";
        for (int i = 0; i < dicts.Count; i++) {
          var d = dicts[i];
          if (d.Source != null && d.Source.OriginalString.Contains("Themes/Colors.")) {
            colorDict = d;
            break;
          }
        }
        if (colorDict != null) {
          var c = (Color)ColorConverter.ConvertFromString(defaultColor);
          colorDict["AccentOmen"] = c;
          colorDict["AccentOmenBrush"] = new SolidColorBrush(c);
          colorDict["AccentOmenLight"] = (Color)ColorConverter.ConvertFromString(defaultLight);
          colorDict["AccentOmenDim"] = (Color)ColorConverter.ConvertFromString(defaultDim);
        }
      } catch { }
    }

    private static Color ChangeColorBrightness(Color color, float correctionFactor) {
        float red = (float)color.R;
        float green = (float)color.G;
        float blue = (float)color.B;

        if (correctionFactor < 0) {
            correctionFactor = 1 + correctionFactor;
            red *= correctionFactor;
            green *= correctionFactor;
            blue *= correctionFactor;
        } else {
            red = (255 - red) * correctionFactor + red;
            green = (255 - green) * correctionFactor + green;
            blue = (255 - blue) * correctionFactor + blue;
        }

        return Color.FromRgb((byte)red, (byte)green, (byte)blue);
    }

    private static Color MixColor(Color color1, Color color2, double percentage) {
        byte r = (byte)(color1.R * percentage + color2.R * (1 - percentage));
        byte g = (byte)(color1.G * percentage + color2.G * (1 - percentage));
        byte b = (byte)(color1.B * percentage + color2.B * (1 - percentage));
        return Color.FromRgb(r, g, b);
    }

    public static void Cleanup() {
      try { SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged; } catch { }
    }
  }
}
