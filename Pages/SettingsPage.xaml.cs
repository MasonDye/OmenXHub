// SettingsPage.xaml.cs - 设置页面
// Windows 11 风格布局，覆盖浮窗/Omen键/托盘图标/自启动/主题/语言/自定义背景/调试日志
using System;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OmenSuperHub.Services;
using Forms = System.Windows.Forms;

namespace OmenSuperHub.Pages {
  public partial class SettingsPage : Page {
    bool _loading = true;
    bool _screenOptionsBuilt;
    bool _extraSensorsBuilt;
    bool _gpuSelectorBuilt;
    // ponytail: 跨页面跳转信号 — LightingPage 选"官方灯效"后置 true,Loaded 内 BringIntoView 滚到存根卡。
    public static bool ScrollToStubOnNextLoad;
    public SettingsPage() { InitializeComponent(); Loaded += SettingsPage_Loaded; }
    private void SettingsPage_Loaded(object sender, RoutedEventArgs e) {
      _loading = true; LoadState();
      if (!_screenOptionsBuilt) { BuildScreenOptions(); _screenOptionsBuilt = true; }
      if (!_extraSensorsBuilt) { BuildExtraTempSensorOptions(); _extraSensorsBuilt = true; }
      if (!_gpuSelectorBuilt) { BuildGpuSelectorOptions(); _gpuSelectorBuilt = true; }
      _loading = false;
      // ponytail: Loaded 里 Build 三个选择器(可视树填充),缓存页二次 Loaded 会空白 —
      // 显式 UpdateLayout 强制一遍,对齐 LightingPage/CoreKeepPage 的修法。
      UpdateLayout();
      // ponytail: 由 LightingPage 触发的定向跳转 — 滚到 OMEN Light Studio 存根卡, 让用户能直接接
      // "注册存根/启动"/"恢复 OXH 灯效"。Loaded 后页面已上可视树,BringIntoView 在 ThreadPool 再跑一拍
      // 确保 ScrollViewer 已度量, 避免滚错位置。
      if (ScrollToStubOnNextLoad) {
        ScrollToStubOnNextLoad = false;
        Dispatcher.BeginInvoke(new Action(() => OccStubCard?.BringIntoView()),
          System.Windows.Threading.DispatcherPriority.Loaded);
      }
    }

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
      TrayHoverPopupToggle.IsChecked = ConfigService.TrayHoverPopup;
      OsdPositionPanel.Visibility = ConfigService.ShowOsd ? Visibility.Visible : Visibility.Collapsed;
      switch (ConfigService.OsdPosition) {
        case "topLeft": OsdPositionCombo.SelectedIndex = 1; break;
        case "topRight": OsdPositionCombo.SelectedIndex = 2; break;
        case "topCenter": OsdPositionCombo.SelectedIndex = 3; break;
        case "bottomLeft": OsdPositionCombo.SelectedIndex = 4; break;
        case "bottomRight": OsdPositionCombo.SelectedIndex = 5; break;
        default: OsdPositionCombo.SelectedIndex = 0; break;
      }
      DataLocalizeToggle.IsChecked = ConfigService.DataLocalize == "on";
      DebugLogToggle.IsChecked = ConfigService.VerboseLogging;
      DebugShowAllUiToggle.IsChecked = ConfigService.DebugShowAllUi;
      // ponytail: 模拟键盘类型回填 — 顺序与 XAML ComboBoxItem 一致(Real/Normal/OneZone/FourZone/PerKey+灯带)
      if (DebugKbKindCombo != null) {
        int idx = ConfigService.DebugKbKind switch {
          "Normal" => 1, "OneZone" => 2, "FourZone" => 3, "PerKey" => 4, _ => 0,
        };
        DebugKbKindCombo.SelectedIndex = idx;
      }
      // ponytail: EC/SMU 直写开关 — PawnIO 未安装时禁用 ToggleSwitch 并在描述尾追加状态。
      // GetPawnIOState() 在未安装时仅读注册表(快),不触发 sc query。
      bool pawnioInstalled = OmenHardware.IsPawnIOInstalled();
      EcAccessToggle.IsChecked = ConfigService.EnableEcAccess;
      EcAccessToggle.IsEnabled = pawnioInstalled;
      EcAccessDescText.Text = Strings.SettingsEnableEcAccessDesc
        + (pawnioInstalled ? "" : "  ⚠ " + OmenHardware.GetPawnIOState());
      // Light Studio / OGH 存根状态(PS 壳调用 1~2s,后台线程刷)
      RefreshLightStudioCard();
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
      // Simple Mode
      SimpleModeToggle.IsChecked = ConfigService.EnableSimpleMode;
      SimpleModeCustomPanel.Visibility = ConfigService.EnableSimpleMode ? Visibility.Visible : Visibility.Collapsed;
      var navSelected = (ConfigService.SimpleModeNavItems ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
      CbDashboard.IsChecked = navSelected.Contains("Dashboard");
      CbFan.IsChecked = navSelected.Contains("Fan");
      CbPerf.IsChecked = navSelected.Contains("Perf");
      CbLighting.IsChecked = navSelected.Contains("Lighting");
      CbAutomation.IsChecked = navSelected.Contains("Automation");
      CbMacro.IsChecked = navSelected.Contains("Macro");
      CbNetworkBoost.IsChecked = navSelected.Contains("NetworkBoost");
      CbOther.IsChecked = navSelected.Contains("Other");
      // ponytail: "恢复 OXH 灯效" 按钮仅当用户已选官方灯效软件时可见,撤回后立刻隐藏。
      // CbLighting 同步跟随: 用户选官方灯光后侧栏灯光项已被隐藏, 此处简洁模式白名单的
      // 灯光勾选框也一并折叠(否则勾了也无效果,反给人"勾了能恢复"的错觉)。撤回时一并恢复可见。
      // 不动 IsChecked —— SettingsPage / LoadState 仍按 SimpleModeNavItems 读写, 撤回后状态原样。
      bool usingOfficial = ConfigService.LightingUseOfficial;
      if (EnableOxhLightingBtn != null)
        EnableOxhLightingBtn.Visibility = usingOfficial ? Visibility.Visible : Visibility.Collapsed;
      if (CbLighting != null)
        CbLighting.Visibility = usingOfficial ? Visibility.Collapsed : Visibility.Visible;
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
      // ponytail: 空 = 首次运行未配置 → 默认全勾(与 ParseSelectedDeviceNames 同口径);
      // 用户勾/取消任一框即写入显式选择,此后完全按用户配置走
      bool allByDefault = string.IsNullOrWhiteSpace(saved);
      var selected = new System.Collections.Generic.HashSet<string>(
        saved.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
      for (int i = 0; i < screens.Length; i++) {
        string label = Strings.FormatScreenLabel(i + 1, screens[i].DeviceName);
        var cb = new System.Windows.Controls.CheckBox {
          Content = label,
          Tag = screens[i].DeviceName,
          IsChecked = allByDefault || selected.Contains(screens[i].DeviceName),
          Margin = new Thickness(0, 2, 8, 2),
        };
        cb.Checked += FloatScreen_Changed;
        cb.Unchecked += FloatScreen_Changed;
        FloatScreenPanel.Children.Add(cb);
      }
    }

    void FloatScreen_Changed(object sender, RoutedEventArgs e) {
      if (_loading) return;   // 构建期 IsChecked 赋值不应触发保存
      var selected = new System.Collections.Generic.List<string>();
      foreach (System.Windows.Controls.CheckBox cb in FloatScreenPanel.Children) {
        if (cb.IsChecked == true) selected.Add((string)cb.Tag);
      }
      // ponytail: 全空时存显式哨兵 "NONE" 而非空串 —— 空串被 ParseSelectedDeviceNames 当成
      // "未配置→默认全选"，取消勾选唯一一块屏时会立刻把浮窗又拉回来（"关了还有"）。用哨兵
      // 区分"用户显式清空选择（不显示浮窗）"与"从未配置（默认全屏浮窗）"。
      string joined = string.Join(",", selected);
      ConfigService.FloatingBarScreen = joined.Length == 0 ? "NONE" : joined;
      ConfigService.Save("FloatingBarScreen");
      // ponytail: 实时应用 —— 与 FloatSize/FloatLoc/FloatLayout 一致,改选择后立刻按新集合增删浮窗。
      // 否则勾选/取消显示器只落盘,浮窗仍停留在旧显示器上,表现为"多选失效"。
      if (ConfigService.FloatingBar == "on")
        Views.FloatingWindow.ShowInstances();
    }

    // ═══ 额外温度传感器勾选(照抄 BuildScreenOptions / FloatScreen_Changed 范式) ═══
    // ID 来自 HardwareService.ExtraSensorIds;显示名按 ID 反查 Strings.SysXxx(单一来源,不复制 ID→名表)。
    static string ExtraTempLabel(string id) => id switch {
      "GPUNV_HOTSPOT" => Strings.SysGpuHotSpot,
      "CPU_COREMAX" => Strings.SysCpuCoreMax,
      "CPU_COREAVG" => Strings.SysCpuCoreAvg,
      "CPU_TJMAX_DISTANCE" => Strings.SysCpuTjmaxDistance,
      "STORAGE_NVME_0" => Strings.SysNvme,
      "MOTHERBOARD_SUPERIO" => Strings.SysMotherboard,
      _ => id,
    };

    void BuildExtraTempSensorOptions() {
      ExtraTempSensorPanel.Children.Clear();
      string saved = ConfigService.ExtraTempSensors;
      // ponytail: 空 = 首启全勾(与 BuildScreenOptions 同口径);用户取消任一即写入显式选择
      bool allByDefault = string.IsNullOrWhiteSpace(saved);
      var selected = new System.Collections.Generic.HashSet<string>(
        saved.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
      foreach (var id in Services.HardwareService.ExtraSensorIds) {
        var cb = new System.Windows.Controls.CheckBox {
          Content = ExtraTempLabel(id),
          Tag = id,
          IsChecked = allByDefault || selected.Contains(id),
          Margin = new Thickness(0, 2, 12, 2),
        };
        cb.Checked += ExtraTempSensor_Changed;
        cb.Unchecked += ExtraTempSensor_Changed;
        ExtraTempSensorPanel.Children.Add(cb);
      }
    }

    void ExtraTempSensor_Changed(object sender, RoutedEventArgs e) {
      if (_loading) return;
      var selected = new System.Collections.Generic.List<string>();
      foreach (System.Windows.Controls.CheckBox cb in ExtraTempSensorPanel.Children) {
        if (cb.IsChecked == true) selected.Add((string)cb.Tag);
      }
      ConfigService.ExtraTempSensors = string.Join(",", selected);
      ConfigService.Save("ExtraTempSensors");
    }

    // ═══ GPU 监控目标 — 启动后 LHM 枚举的 GPU 列表填入 ComboBox(空=独显优先;选具体型号) ═══
    void BuildGpuSelectorOptions() {
      var combo = GpuSelectorCombo;
      if (combo == null) return;
      combo.Items.Clear();
      // 默认项:独显优先
      combo.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = Strings.GpuSelectorAuto, Tag = "" });
      // 启动后 LHM Open 已枚举硬件;采 GPU 名(可能此时为空 — 跳过)
      foreach (var (name, vendor) in Services.HardwareService.GetAvailableGpus())
        combo.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = vendor + ": " + name, Tag = name });
      // 选中当前存值(空显默认项 → index 0)
      string cur = ConfigService.SelectedGpu ?? "";
      int idx = 0;
      for (int i = 0; i < combo.Items.Count; i++) {
        if (combo.Items[i] is System.Windows.Controls.ComboBoxItem item && (string)item.Tag == cur) { idx = i; break; }
      }
      combo.SelectedIndex = idx;
    }

    void GpuSelector_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) {
      if (_loading) return;
      if (GpuSelectorCombo.SelectedItem is System.Windows.Controls.ComboBoxItem item)
        ConfigService.SelectedGpu = (string)item.Tag ?? "";
      else
        ConfigService.SelectedGpu = "";
      ConfigService.Save("SelectedGpu");
    }

    // ═══ OMEN Light Studio / OGH 存根卡 ═══
    // QueryState/Register/Remove 都是 PowerShell 壳调用(1~2s) — 一律后台线程,Dispatcher 回填。

    async void RefreshLightStudioCard() {
      var st = await System.Threading.Tasks.Task.Run(() => Services.OccStubService.QueryState());
      if (LightStudioStatusText == null) return;
      string ls = st.LightStudioInstalled ? Strings.OccStubLsOk : Strings.OccStubLsMissing;
      string occ = st.OccIsStub ? Strings.OccStubOccRegOk
        : (st.OccInstalled ? Strings.OccStubOccReal : Strings.OccStubOccMissing);
      LightStudioStatusText.Text = $"Light Studio: {ls} | OGH: {occ}";
      if (OccStubLaunchBtn != null) OccStubLaunchBtn.IsEnabled = st.LightStudioInstalled;
      // 包装未装: 显示"安装",装好后收起回归"注册/移除/启动"三件套
      if (OccStubInstallBtn != null)
        OccStubInstallBtn.Visibility = st.LightStudioInstalled ? Visibility.Collapsed : Visibility.Visible;
    }

    async void OccStubReg_Click(object sender, RoutedEventArgs e) {
      // ponytail: 注册存根走 Add-AppxPackage -Register,需要开发者模式。未开启时先询问用户,
      // 用户同意才写入 HKLM 开启(需管理员,程序已 requireAdministrator)再继续;拒绝则中止。
      if (!Services.OccStubService.IsDeveloperModeEnabled()) {
        if (!Utils.DialogHelper.Confirm(Strings.OccStubEnableDevModePrompt, Strings.OccStubRegBtn)) return;
        if (!Services.OccStubService.EnableDeveloperMode()) {
          Utils.DialogHelper.Error(Strings.OccStubEnableDevModeFail, Strings.OccStubRegBtn);
          return;
        }
      }
      OccStubRegBtn.IsEnabled = OccStubRmBtn.IsEnabled = false;
      LightStudioStatusText.Text = Strings.OccStubWorking;
      string err = await System.Threading.Tasks.Task.Run(() => Services.OccStubService.Register());
      OccStubRegBtn.IsEnabled = OccStubRmBtn.IsEnabled = true;
      if (err != null && LightStudioStatusText != null) LightStudioStatusText.Text = err;
      RefreshLightStudioCard();
    }

    async void OccStubRm_Click(object sender, RoutedEventArgs e) {
      OccStubRegBtn.IsEnabled = OccStubRmBtn.IsEnabled = false;
      LightStudioStatusText.Text = Strings.OccStubWorking;
      string err = await System.Threading.Tasks.Task.Run(() => Services.OccStubService.Remove());
      OccStubRegBtn.IsEnabled = OccStubRmBtn.IsEnabled = true;
      if (err != null && LightStudioStatusText != null) LightStudioStatusText.Text = err;
      RefreshLightStudioCard();
    }

    void OccStubLaunch_Click(object sender, RoutedEventArgs e) {
      if (!Services.OccStubService.LaunchLightStudio())
        if (LightStudioStatusText != null) LightStudioStatusText.Text = Strings.OccStubLsMissing;
    }

    // ponytail: 撤回"使用官方灯效软件" — 清标志 + 刷新侧栏恢复 NavLighting。不在此重启灯后端
    // timer: 灯光页被隐藏时用户进不去, 总开关下次在灯光页重新打开时 LightEnable_Changed 会自然
    // StartScheduler(后台 ReplaySavedLighting 也已由 lj.Enabled=false 早退, 此时不变)。
    void EnableOxhLightingBtn_Click(object sender, RoutedEventArgs e) {
      ConfigService.LightingUseOfficial = false;
      ConfigService.Save("LightingUseOfficial");
      Views.MainWindow.UpdateNavigationItems();
      if (EnableOxhLightingBtn != null) EnableOxhLightingBtn.Visibility = Visibility.Collapsed;
      if (CbLighting != null) CbLighting.Visibility = Visibility.Visible;
      Utils.DialogHelper.Info(Strings.LightingUseOfficialReverted, Strings.LightingExperimentalTitle);
    }

    // 拉起商店到 OLS 详情页(explorer 壳,不继承管理员令牌)。ms-store 无法静默装,深链是唯一干净路径。
    void OccStubInstall_Click(object sender, RoutedEventArgs e) {
      if (!Services.OccStubService.InstallLightStudio())
        if (LightStudioStatusText != null) LightStudioStatusText.Text = Strings.OccStubLsMissing;
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
      if (_loading) return;
      ConfigService.ShowOsd = OsdToggle.IsChecked == true;
      ConfigService.Save("ShowOsd");
      // ponytail: 对齐 lock-key 轮询到新状态 —— 之前 ShowOsd=false 后 _lockKeyTimer 仍每 200 ms 跑空 tick。
      Views.OsdWindow.RefreshMonitorState();
      if (!ConfigService.ShowOsd) Views.OsdWindow.Dismiss();
      OsdPositionPanel.Visibility = ConfigService.ShowOsd ? Visibility.Visible : Visibility.Collapsed;
    }

    void TrayHoverPopup_Changed(object sender, RoutedEventArgs e) {
      ConfigService.TrayHoverPopup = TrayHoverPopupToggle.IsChecked == true;
      ConfigService.Save("TrayHoverPopup");
    }

    void OsdPosition_SelectionChanged(object s, SelectionChangedEventArgs e) {
      if (_loading) return;
      string[] positions = { "bottomCenter", "topLeft", "topRight", "topCenter", "bottomLeft", "bottomRight" };
      int idx = OsdPositionCombo.SelectedIndex;
      ConfigService.OsdPosition = idx >= 0 && idx < positions.Length ? positions[idx] : "bottomCenter";
      ConfigService.Save("OsdPosition");
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

    // ponytail: DEBUG 模拟键盘类型 — 更新后清能力缓存 + 刷新侧栏(普通键盘隐藏灯光项)。
    // 回填受 _loading 保护,无需额外防递归标志。
    void DebugKbKindCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) {
      if (_loading) return;
      if (DebugKbKindCombo?.SelectedItem is System.Windows.Controls.ComboBoxItem item)
        ConfigService.DebugKbKind = item.Tag as string ?? "";
      else
        ConfigService.DebugKbKind = "";
      ConfigService.Save("DebugKbKind");
      // 清缓存 → 下次 DetectKeyboardCapability 按新 DebugKbKind 重新求值
      OmenLighting.InvalidateKeyboardCapabilityCache();
      OmenLighting.DetectKeyboardCapability();
      Views.MainWindow.UpdateNavigationItems();
    }

    // ponytail: EC/SMU 直写开关变更 — 持久化并通知性能页刷新降压卡片可见性。
    // 不在此处直接应用降压,留待 PerfPage 加载时按当前预设统一应用,避免双写入冲突。
    void EcAccessToggle_Changed(object sender, RoutedEventArgs e) {
      if (_loading) return;
      ConfigService.EnableEcAccess = EcAccessToggle.IsChecked == true;
      ConfigService.Save("EnableEcAccess");
      if (PerfPage.Instance != null)
        PerfPage.Instance.ApplyHardwareVisibility();
    }

    // ══════ 简洁模式 ══════
    void SimpleModeToggle_Changed(object sender, RoutedEventArgs e) {
      if (_loading) return;
      ConfigService.EnableSimpleMode = SimpleModeToggle.IsChecked == true;
      ConfigService.Save("EnableSimpleMode");
      SimpleModeCustomPanel.Visibility = ConfigService.EnableSimpleMode ? Visibility.Visible : Visibility.Collapsed;
      Views.MainWindow.UpdateNavigationItems();
      // ponytail: 同步刷新托盘右键菜单 — 简洁模式隐藏侧栏的页不应在右键菜单里继续出现
      TrayService.RebuildMenu();
      // ponytail: 自动化后端跟随 Automation 页可见性 — 隐藏时停后端 (WMI/定时器/热键),
      // 重新可见且总开关开时复活。性能 / 风扇服务与本类无关,不受影响。
      OmenSuperHub.Services.AutomationProcessor.ReevaluateBackendNeeded();
    }

    void CbNavItem_Changed(object sender, RoutedEventArgs e) {
      if (_loading) return;
      var selected = new System.Collections.Generic.List<string>();
      if (CbDashboard.IsChecked == true) selected.Add("Dashboard");
      if (CbFan.IsChecked == true) selected.Add("Fan");
      if (CbPerf.IsChecked == true) selected.Add("Perf");
      if (CbLighting.IsChecked == true) selected.Add("Lighting");
      if (CbAutomation.IsChecked == true) selected.Add("Automation");
      if (CbMacro.IsChecked == true) selected.Add("Macro");
      if (CbNetworkBoost.IsChecked == true) selected.Add("NetworkBoost");
      if (CbOther.IsChecked == true) selected.Add("Other");
      ConfigService.SimpleModeNavItems = string.Join(",", selected);
      ConfigService.Save("SimpleModeNavItems");
      Views.MainWindow.UpdateNavigationItems();
      // ponytail: 同上 — 白名单变更,右键菜单也要跟着过滤
      if (ConfigService.EnableSimpleMode) TrayService.RebuildMenu();
      // ponytail: 自动化后端跟随白名单 —— 用户在简洁模式下取消勾选 Automation → 视同关闭,
      // 重新勾选 → 自动复活 (总开关仍开时)。即使简洁模式本身未变, 单 cb 变化也要刷一次。
      OmenSuperHub.Services.AutomationProcessor.ReevaluateBackendNeeded();
    }

    // ══════ UEFI 重启 ══════

    void UefiRestart_Click(object sender, RoutedEventArgs e) {
      // ponytail: GetFirmwareType 检测启动模式，1=Bios 2=Uefi；仅 UEFI 支持进固件
      if (!OmenSuperHub.Services.CpuAffinity.Kernel32.GetFirmwareType(out uint fw) || fw != 2) {
        Utils.DialogHelper.Info(Strings.UefiRestartNotSupported, Strings.UefiRestartHeading);
        return;
      }
      if (!Utils.DialogHelper.Confirm(Strings.UefiRestartConfirm, Strings.UefiRestartHeading)) return;
      try {
        var psi = new System.Diagnostics.ProcessStartInfo {
          FileName = System.IO.Path.Combine(Environment.SystemDirectory, "shutdown.exe"),
          Arguments = "/r /fw /t 0",
          UseShellExecute = false,
          CreateNoWindow = true
        };
        using (System.Diagnostics.Process.Start(psi)) { }
      } catch (Exception ex) {
        Utils.DialogHelper.Warn(Strings.UefiRestartFailed + ex.Message, Strings.UefiRestartHeading);
      }
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
