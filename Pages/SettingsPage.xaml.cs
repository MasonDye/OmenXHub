// SettingsPage.xaml.cs - 设置页面
// Windows 11 风格布局，覆盖浮窗/Omen键/托盘图标/自启动/主题/语言/自定义背景/调试日志
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OmenSuperHub.Services;
using Forms = System.Windows.Forms;

namespace OmenSuperHub.Pages {
  public partial class SettingsPage : Page {
    bool _loading = true;
    bool _screenOptionsBuilt;
    public SettingsPage() { InitializeComponent(); Loaded += SettingsPage_Loaded; }
    private void SettingsPage_Loaded(object sender, RoutedEventArgs e) { _loading = true; LoadState(); if (!_screenOptionsBuilt) { BuildScreenOptions(); _screenOptionsBuilt = true; } _loading = false; }

    void LoadState() {
      switch (Strings.Current) {
        case AppLanguage.TraditionalChinese: LangCombo.SelectedIndex = 1; break;
        case AppLanguage.English: LangCombo.SelectedIndex = 2; break;
        default: LangCombo.SelectedIndex = 0; break;
      }
      switch (ConfigService.Theme) {
        case "dark": ThemeCombo.SelectedIndex = 1; break;
        case "light": ThemeCombo.SelectedIndex = 2; break;
        default: ThemeCombo.SelectedIndex = 0; break;
      }
      AutoStartToggle.IsChecked = ConfigService.AutoStart == "on";
      FloatingToggle.IsChecked = ConfigService.FloatingBar == "on";
      FloatSizeSlider.Value = ConfigService.TextSize;
      FloatSizeVal.Text = ConfigService.TextSize.ToString();
      FloatTextOpacitySlider.Value = ConfigService.FloatingTextOpacity;
      FloatTextOpacityVal.Text = (int)(ConfigService.FloatingTextOpacity * 100) + "%";
      switch (ConfigService.FloatingBarLoc) {
        case "right": FloatLocCombo.SelectedIndex = 1; break;
        case "top": FloatLocCombo.SelectedIndex = 2; break;
        case "free": FloatLocCombo.SelectedIndex = 3; break;
        default: FloatLocCombo.SelectedIndex = 0; break;
      }
      FloatLayoutCombo.SelectedIndex = ConfigService.FloatingBarLayout == "col" ? 1 : 0;
      switch (ConfigService.OmenKey) {
        case "custom": OmenKeyCombo.SelectedIndex = 0; break;
        case "showMain": OmenKeyCombo.SelectedIndex = 1; break;
        case "cyclePresets": OmenKeyCombo.SelectedIndex = 2; SyncCycleCandidates(); break;
        case "app": OmenKeyCombo.SelectedIndex = 3; break;
        default: OmenKeyCombo.SelectedIndex = 4; break;
      }
      OmenKeyAppPanel.Visibility = ConfigService.OmenKey == "app" ? Visibility.Visible : Visibility.Collapsed;
      OmenKeyCyclePanel.Visibility = ConfigService.OmenKey == "cyclePresets" ? Visibility.Visible : Visibility.Collapsed;
      OmenKeyAppPathText.Text = !string.IsNullOrEmpty(ConfigService.OmenKeyAppPath)
        ? ConfigService.OmenKeyAppPath : Strings.OmenKeyNoAppSelected;
      OsdToggle.IsChecked = ConfigService.ShowOsd;
      DataLocalizeToggle.IsChecked = ConfigService.DataLocalize == "on";
      DebugLogToggle.IsChecked = ConfigService.VerboseLogging;
      DebugShowAllUiToggle.IsChecked = ConfigService.DebugShowAllUi;
      switch (ConfigService.CustomIcon) {
        case "custom": TrayIconCombo.SelectedIndex = 1; break;
        case "dynamic": TrayIconCombo.SelectedIndex = 2; break;
        default: TrayIconCombo.SelectedIndex = 0; break;
      }
      // Custom background
      CustomBgPathText.Text = !string.IsNullOrEmpty(ConfigService.CustomBgPath)
        ? ConfigService.CustomBgPath : Strings.CustomBgDesc;
      CustomBgOpacitySlider.Value = ConfigService.CustomBgOpacity;
      CustomBgOpacityVal.Text = (int)(ConfigService.CustomBgOpacity * 100) + "%";
      CustomBgBlurToggle.IsChecked = ConfigService.CustomBgBlurEnabled;
    }

    void SyncCycleCandidates() {
      var candidates = ConfigService.OmenKeyPresetCandidates
        .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
        .ToHashSet();
      CycleExtreme.IsChecked = candidates.Contains("Extreme");
      CycleGpuPriority.IsChecked = candidates.Contains("GpuPriority");
      CycleLightUse.IsChecked = candidates.Contains("LightUse");

      // ponytail: build one checkbox per custom preset file. Tag=FileKey.
      // Also prunes dead keys (custom preset deleted) from the stored list.
      CycleCustomsHost.Children.Clear();
      var customs = PresetManager.EnumerateCustomPresets();
      var liveCustomKeys = new System.Collections.Generic.HashSet<string>();
      foreach (var (display, key) in customs) {
        liveCustomKeys.Add(key);
        var cb = new CheckBox {
          Content = display,
          Tag = key,
          IsChecked = candidates.Contains(key),
          Margin = new Thickness(0, 0, 12, 0),
        };
        cb.Checked += CycleCandidate_Changed;
        cb.Unchecked += CycleCandidate_Changed;
        CycleCustomsHost.Children.Add(cb);
      }
      // prune dead custom keys from stored candidates (built-ins never pruned)
      if (candidates.Any(c => !PresetManager.IsBuiltIn(c) && !liveCustomKeys.Contains(c))) {
        var pruned = candidates.Where(c => PresetManager.IsBuiltIn(c) || liveCustomKeys.Contains(c)).ToList();
        ConfigService.OmenKeyPresetCandidates = string.Join(";", pruned);
        ConfigService.Save("OmenKeyPresetCandidates");
      }
    }

    void Lang_SelectionChanged(object s, SelectionChangedEventArgs e) {
      if (_loading) return;
      AppLanguage lang = LangCombo.SelectedIndex == 1 ? AppLanguage.TraditionalChinese :
                          LangCombo.SelectedIndex == 2 ? AppLanguage.English : AppLanguage.SimplifiedChinese;
      Strings.SetLanguage(lang);
      ConfigService.Language = lang.ToString();
      ConfigService.Save("Language");
      TrayService.RebuildMenu();
    }

    void Theme_SelectionChanged(object s, SelectionChangedEventArgs e) {
      if (_loading) return;
      ConfigService.Theme = ThemeCombo.SelectedIndex == 1 ? "dark" : ThemeCombo.SelectedIndex == 2 ? "light" : "system";
      ConfigService.Save("Theme");
      ThemeService.ApplyConfigTheme();
    }

    void AutoStartToggle_Changed(object sender, RoutedEventArgs e) {
      ConfigService.AutoStart = AutoStartToggle.IsChecked == true ? "on" : "off";
      ConfigService.Save("AutoStart");
      if (AutoStartToggle.IsChecked == true) TrayService.AutoStartEnable();
      else TrayService.AutoStartDisable();
    }

    void FloatingToggle_Changed(object sender, RoutedEventArgs e) {
      ConfigService.FloatingBar = FloatingToggle.IsChecked == true ? "on" : "off";
      ConfigService.Save("FloatingBar");
      if (FloatingToggle.IsChecked == true) Views.FloatingWindow.ShowInstances();
      else Views.FloatingWindow.CloseAll();
    }

    void OmenKey_SelectionChanged(object s, SelectionChangedEventArgs e) {
      if (_loading) return;
      string[] keys = { "custom", "showMain", "cyclePresets", "app", "none" };
      int idx = OmenKeyCombo.SelectedIndex;
      string key = idx >= 0 && idx < keys.Length ? keys[idx] : "none";
      ConfigService.OmenKey = key;
      ConfigService.Save("OmenKey");
      OmenHardware.OmenKeyOff();
      OmenHardware.OmenKeyOn(key);
      OmenKeyAppPanel.Visibility = key == "app" ? Visibility.Visible : Visibility.Collapsed;
      OmenKeyCyclePanel.Visibility = key == "cyclePresets" ? Visibility.Visible : Visibility.Collapsed;
      if (key == "custom" || key == "showMain" || key == "cyclePresets" || key == "app") {
        TrayService.checkFloatingTimer.IsEnabled = true;
      } else {
        TrayService.checkFloatingTimer.IsEnabled = false;
      }
    }

    void FloatSize_Changed(object s, RoutedPropertyChangedEventArgs<double> e) {
      int val = (int)e.NewValue;
      if (FloatSizeVal != null) FloatSizeVal.Text = val.ToString();
      if (!_loading) {
        ConfigService.TextSize = val;
        ConfigService.Save("FloatingBarSize");
        Views.FloatingWindow.UpdateAllText();
      }
    }


    void FloatTextOpacity_Changed(object s, RoutedPropertyChangedEventArgs<double> e) {
      double val = e.NewValue;
      if (FloatTextOpacityVal != null) FloatTextOpacityVal.Text = (int)(val * 100) + "%";
      if (!_loading) {
        ConfigService.FloatingTextOpacity = val;
        ConfigService.Save("FloatingTextOpacity");
        Views.FloatingWindow.ApplyAllOpacity();
      }
    }

    void FloatLoc_SelectionChanged(object s, SelectionChangedEventArgs e) {
      if (_loading) return;
      string[] locs = { "left", "right", "top", "free" };
      int idx = FloatLocCombo.SelectedIndex;
      ConfigService.FloatingBarLoc = idx >= 0 && idx < locs.Length ? locs[idx] : "left";
      ConfigService.Save("FloatingBarLoc");
      Views.FloatingWindow.UpdateAllText();
    }

    void FloatLayout_SelectionChanged(object s, SelectionChangedEventArgs e) {
      if (_loading) return;
      ConfigService.FloatingBarLayout = FloatLayoutCombo.SelectedIndex == 1 ? "col" : "row";
      ConfigService.Save("FloatingBarLayout");
      Views.FloatingWindow.UpdateAllText();
    }

    void BuildScreenOptions() {
      FloatScreenPanel.Children.Clear();
      var screens = Forms.Screen.AllScreens;
      string saved = ConfigService.FloatingBarScreen;
      var selected = new System.Collections.Generic.HashSet<string>(
        saved.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
      for (int i = 0; i < screens.Length; i++) {
        string label = Strings.FormatScreenLabel(i + 1, screens[i].DeviceName);
        var cb = new System.Windows.Controls.CheckBox {
          Content = label,
          Tag = screens[i].DeviceName,
          IsChecked = selected.Contains(screens[i].DeviceName),
          Margin = new Thickness(0, 2, 8, 2),
        };
        cb.Checked += FloatScreen_Changed;
        cb.Unchecked += FloatScreen_Changed;
        FloatScreenPanel.Children.Add(cb);
      }
    }

    void FloatScreen_Changed(object sender, RoutedEventArgs e) {
      var selected = new System.Collections.Generic.List<string>();
      foreach (System.Windows.Controls.CheckBox cb in FloatScreenPanel.Children) {
        if (cb.IsChecked == true) selected.Add((string)cb.Tag);
      }
      ConfigService.FloatingBarScreen = string.Join(",", selected);
      ConfigService.Save("FloatingBarScreen");
    }

    void OmenKeySelectApp_Click(object sender, RoutedEventArgs e) {
      var dialog = new Microsoft.Win32.OpenFileDialog {
        Title = Strings.FileDialogSelectApp,
        Filter = Strings.FileDialogExeFilter,
        CheckFileExists = true,
      };
      if (dialog.ShowDialog() == true) {
        ConfigService.OmenKeyAppPath = dialog.FileName;
        ConfigService.Save("OmenKeyAppPath");
        OmenKeyAppPathText.Text = dialog.FileName;
      }
    }

    void CycleCandidate_Changed(object sender, RoutedEventArgs e) {
      if (_loading) return;
      var selected = new System.Collections.Generic.List<string>();
      if (CycleExtreme.IsChecked == true) selected.Add("Extreme");
      if (CycleGpuPriority.IsChecked == true) selected.Add("GpuPriority");
      if (CycleLightUse.IsChecked == true) selected.Add("LightUse");
      // ponytail: dynamic — collect from custom checkboxes by Tag (FileKey)
      foreach (var ch in CycleCustomsHost.Children) {
        if (ch is CheckBox cb && cb.IsChecked == true && cb.Tag is string key)
          selected.Add(key);
      }
      ConfigService.OmenKeyPresetCandidates = string.Join(";", selected.Distinct());
      ConfigService.Save("OmenKeyPresetCandidates");
    }

    void OsdToggle_Changed(object sender, RoutedEventArgs e) {
      ConfigService.ShowOsd = OsdToggle.IsChecked == true;
      ConfigService.Save("ShowOsd");
      // ponytail: 对齐 lock-key 轮询到新状态 —— 之前 ShowOsd=false 后 _lockKeyTimer 仍每 200 ms 跑空 tick。
      Views.OsdWindow.RefreshMonitorState();
      if (!ConfigService.ShowOsd) Views.OsdWindow.Dismiss();
    }


    void TrayIcon_SelectionChanged(object s, SelectionChangedEventArgs e) {
      if (_loading) return;
      string[] icons = { "original", "custom", "dynamic" };
      int idx = TrayIconCombo.SelectedIndex;
      ConfigService.CustomIcon = idx >= 0 && idx < icons.Length ? icons[idx] : "original";
      ConfigService.Save("CustomIcon");
      TrayService.ApplyIconStyle();
      TrayService.RebuildMenu();
    }

    void DataLocalizeToggle_Changed(object sender, RoutedEventArgs e) {
      ConfigService.DataLocalize = DataLocalizeToggle.IsChecked == true ? "on" : "off";
      ConfigService.Save("DataLocalize");
    }

    void DebugLogToggle_Changed(object sender, RoutedEventArgs e) {
      ConfigService.VerboseLogging = DebugLogToggle.IsChecked == true;
      ConfigService.Save("VerboseLogging");
    }

    void DebugShowAllUiToggle_Changed(object sender, RoutedEventArgs e) {
      ConfigService.DebugShowAllUi = DebugShowAllUiToggle.IsChecked == true;
      ConfigService.Save("DebugShowAllUi");
      // 通知性能页刷新可见性
      if (PerfPage.Instance != null)
        PerfPage.Instance.ApplyHardwareVisibility();
    }

    void CustomLogoSelect_Click(object sender, RoutedEventArgs e) {
      var dialog = new Microsoft.Win32.OpenFileDialog {
        Title = Strings.FileDialogSelectLogo,
        Filter = Strings.FileDialogImgFilter,
        CheckFileExists = true,
      };
      if (dialog.ShowDialog() == true) {
        ConfigService.CustomLogoPath = dialog.FileName;
        ConfigService.Save("CustomLogoPath");
        Views.MainWindow.ApplyCustomLogoToInstance();
      }
    }

    void CustomLogoReset_Click(object sender, RoutedEventArgs e) {
      ConfigService.CustomLogoPath = "";
      ConfigService.Save("CustomLogoPath");
      Views.MainWindow.ApplyCustomLogoToInstance();
    }

    // ══════ Custom Background ══════

    void CustomBgSelect_Click(object sender, RoutedEventArgs e) {
      var dialog = new Microsoft.Win32.OpenFileDialog {
        Title = Strings.FileDialogSelectLogo,
        Filter = Strings.FileDialogImgFilter,
        CheckFileExists = true,
      };
      if (dialog.ShowDialog() == true) {
        ConfigService.CustomBgPath = dialog.FileName;
        ConfigService.Save("CustomBgPath");
        CustomBgPathText.Text = dialog.FileName;
        Views.MainWindow.ApplyCustomBgToInstance();
      }
    }

    void CustomBgReset_Click(object sender, RoutedEventArgs e) {
      ConfigService.CustomBgPath = "";
      ConfigService.Save("CustomBgPath");
      CustomBgPathText.Text = Strings.CustomBgDesc;
      Views.MainWindow.ApplyCustomBgToInstance();
    }

    void CustomBgOpacity_Changed(object s, RoutedPropertyChangedEventArgs<double> e) {
      double val = e.NewValue;
      if (CustomBgOpacityVal != null) CustomBgOpacityVal.Text = (int)(val * 100) + "%";
      if (!_loading) {
        ConfigService.CustomBgOpacity = val;
        ConfigService.Save("CustomBgOpacity");
        Views.MainWindow.ApplyCustomBgToInstance();
      }
    }

    void CustomBgBlur_Changed(object sender, RoutedEventArgs e) {
      if (_loading) return;
      ConfigService.CustomBgBlurEnabled = CustomBgBlurToggle.IsChecked == true;
      ConfigService.Save("CustomBgBlurEnabled");
      Views.MainWindow.ApplyCustomBgToInstance();
    }

  }
}
