// PerfPage.cs - 性能调优页面
// CPU 功耗 (PL1/PL2)、GPU 设置 (TGP/PPAB/dState/DB/时钟)、电源方案、热切换、刷新率
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using OmenSuperHub.Services;
using static OmenSuperHub.OmenHardware;

namespace OmenSuperHub.Pages {
  public partial class PerfPage : System.Windows.Controls.Page {
    bool _loading;

    public PerfPage() { InitializeComponent(); Loaded += PerfPage_Loaded; }

    void PerfPage_Loaded(object sender, RoutedEventArgs e) {
      _loading = true;
      BuildOptions();
      LoadState();
      _loading = false;
    }

    // ══════════════════════════════════════
    //   Native methods for Power & Display
    // ══════════════════════════════════════
    static class NativeMethods_Display {
      public const int ENUM_CURRENT_SETTINGS = -1;
      public const int DM_DISPLAYFREQUENCY = 0x400000;
      [DllImport("user32.dll")] public static extern bool EnumDisplaySettings(string lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);
      [DllImport("user32.dll")] public static extern int ChangeDisplaySettings(ref DEVMODE lpDevMode, int dwFlags);
      [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
      public struct DEVMODE {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion; public short dmDriverVersion; public short dmSize; public short dmDriverExtra;
        public int dmFields; public short dmOrientation; public short dmPaperSize; public short dmPaperLength;
        public short dmPaperWidth; public short dmScale; public short dmCopies; public short dmDefaultSource;
        public short dmPrintQuality; public short dmColor; public short dmDuplex; public short dmYResolution;
        public short dmTTOption; public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels; public int dmBitsPerPel; public int dmPelsWidth; public int dmPelsHeight;
        public int dmDisplayFlags; public int dmDisplayFrequency;
      }
    }

    static class NativeMethods_Power {
      public static readonly Guid BEST_POWER_EFFICIENCY = Guid.Parse("961cc777-2547-4f9d-8174-7d86181b8a7a");
      public static readonly Guid BEST_PERFORMANCE = Guid.Parse("ded574b5-45a0-4f42-8737-46345c09c238");
      [DllImport("powrprof.dll")] public static extern uint PowerSetActiveScheme(IntPtr userPowerKey, ref Guid activePolicyGuid);
      [DllImport("powrprof.dll")] public static extern uint PowerSetActiveOverlayScheme(Guid overlaySchemeGuid);
      [DllImport("powrprof.dll")] public static extern uint PowerGetActiveScheme(IntPtr userPowerKey, out IntPtr activePolicyGuid);
      [DllImport("powrprof.dll")] public static extern uint PowerEnumerate(IntPtr rootPowerKey, IntPtr schemeGuid, IntPtr subGroupOfPowerSettings, uint accessFlags, uint index, IntPtr buffer, ref uint bufferSize);
      [DllImport("powrprof.dll")] public static extern uint PowerReadFriendlyName(IntPtr rootPowerKey, ref Guid schemeGuid, IntPtr subGroupOfPowerSettings, IntPtr powerSetting, IntPtr buffer, ref uint bufferSize);
    }

    // ══════════════════════════════════════
    //   Option builders
    // ══════════════════════════════════════
    void BuildOptions() {
      CpuPowerCombo.Items.Clear();
      CpuPowerCombo.Items.Add(new ComboBoxItem { Content = Strings.NotSet, Tag = "null" });
      CpuPowerCombo.Items.Add(new ComboBoxItem { Content = Strings.Maximum, Tag = "max" });
      for (int w = 10; w <= 254; w++) CpuPowerCombo.Items.Add(new ComboBoxItem { Content = w + " W", Tag = w });

      IccMaxCombo.Items.Clear();
      IccMaxCombo.Items.Add(new ComboBoxItem { Content = Strings.NotSet, Tag = 0 });
      for (int a = 1; a <= 255; a++) IccMaxCombo.Items.Add(new ComboBoxItem { Content = a + " A", Tag = a });

      AcLoadLineCombo.Items.Clear();
      AcLoadLineCombo.Items.Add(new ComboBoxItem { Content = Strings.NotSet, Tag = 0 });
      AcLoadLineCombo.Items.Add(new ComboBoxItem { Content = "170 mOhm", Tag = 1 });
      AcLoadLineCombo.Items.Add(new ComboBoxItem { Content = "160 mOhm", Tag = 2 });
      AcLoadLineCombo.Items.Add(new ComboBoxItem { Content = "150 mOhm", Tag = 3 });

      CtgpCombo.Items.Clear();
      CtgpCombo.Items.Add(new ComboBoxItem { Content = Strings.Enable, Tag = true });
      CtgpCombo.Items.Add(new ComboBoxItem { Content = Strings.Disable, Tag = false });

      TppCombo.Items.Clear();
      TppCombo.Items.Add(new ComboBoxItem { Content = Strings.TppDisable, Tag = 0 });
      for (int v = 1; v <= 255; v++) TppCombo.Items.Add(new ComboBoxItem { Content = v.ToString(), Tag = v });

      GpuClockCombo.Items.Clear();
      GpuClockCombo.Items.Add(new ComboBoxItem { Content = Strings.GpuClockRestore, Tag = 0 });
      int[] clockPresets = { 300, 600, 900, 1200, 1500, 1800, 2100, 2500 };
      foreach (int c in clockPresets) GpuClockCombo.Items.Add(new ComboBoxItem { Content = c + " MHz", Tag = c });

      GpuCoreOCCombo.Items.Clear();
      GpuCoreOCCombo.Items.Add(new ComboBoxItem { Content = Strings.NotSet, Tag = 0 });
      for (int o = -270; o <= 270; o += 15) {
        if (o == 0) continue;
        GpuCoreOCCombo.Items.Add(new ComboBoxItem { Content = (o >= 0 ? "+" : "") + o + " MHz", Tag = o });
      }

      GpuMemoryOCCombo.Items.Clear();
      GpuMemoryOCCombo.Items.Add(new ComboBoxItem { Content = Strings.NotSet, Tag = 0 });
      for (int m = 100; m <= 2000; m += 100)
        GpuMemoryOCCombo.Items.Add(new ComboBoxItem { Content = "+" + m + " MHz", Tag = m });

      FpsCombo.Items.Clear();
      FpsCombo.Items.Add(new ComboBoxItem { Content = Strings.Unlimited, Tag = 0 });
      foreach (int f in new[] { 30, 60, 90, 120, 144, 165, 240, 300, 360, 480, 1000 })
        FpsCombo.Items.Add(new ComboBoxItem { Content = f + " FPS", Tag = f });

      PowerModeCombo.Items.Clear();
      var modes = GetWindowsPowerModes();
      int savedPm = ConfigService.PowerMode;
      foreach (var m in modes)
        PowerModeCombo.Items.Add(new ComboBoxItem { Content = m.Name, Tag = m.Value, IsSelected = m.Value == savedPm });
      if (PowerModeCombo.SelectedIndex < 0 && modes.Count > 0) PowerModeCombo.SelectedIndex = 0;

      BuildPowerPlanOptions();
      BuildRefreshRateOptions();
    }

    void LoadState() {
      if (!string.IsNullOrEmpty(ConfigService.CpuPower)) {
        string cp = ConfigService.CpuPower;
        SelectCombo(CpuPowerCombo, cp == "max" ? Strings.Maximum : cp == "null" ? Strings.NotSet : cp);
      }
      if (ConfigService.IccMax > 0) { SelectCombo(IccMaxCombo, ConfigService.IccMax + " A"); IccMaxSlider.Value = ConfigService.IccMax; }
      else SelectCombo(IccMaxCombo, Strings.NotSet);
      if (ConfigService.AcLoadLine > 0) {
        int mOhm = 180 - 10 * ConfigService.AcLoadLine;
        SelectCombo(AcLoadLineCombo, mOhm + " mOhm");
      } else SelectCombo(AcLoadLineCombo, Strings.NotSet);
      PpabCheck.IsChecked = ConfigService.PpabEnabled;
      SelectCombo(CtgpCombo, ConfigService.TgpEnabled ? Strings.Enable : Strings.Disable);
      if (ConfigService.Tpp > 0) { SelectCombo(TppCombo, ConfigService.Tpp.ToString()); TppExtraSlider.Value = ConfigService.Tpp; }
      else SelectCombo(TppCombo, Strings.TppDisable);
      GetGfxMode(out int mode);
      GfxModeCombo.SelectedIndex = mode;
      UpdateHotSwitchVisibility();
      DbVersionCombo.SelectedIndex = ConfigService.DBVersion == 1 ? 0 : 1;
      DStateCombo.SelectedIndex = ConfigService.DState == 2 ? 1 : 0;
      if (ConfigService.MaxFrameRate <= 0) SelectCombo(FpsCombo, Strings.Unlimited);
      else SelectCombo(FpsCombo, ConfigService.MaxFrameRate + " FPS");
      if (ConfigService.GpuClock <= 0) SelectCombo(GpuClockCombo, Strings.GpuClockRestore);
      else SelectCombo(GpuClockCombo, ConfigService.GpuClock + " MHz");
      int coreOc = ConfigService.GpuCoreOverclock;
      if (coreOc < 0) SelectComboByTag(GpuCoreOCCombo, 0);
      else SelectComboByTag(GpuCoreOCCombo, coreOc);
      int memOc = ConfigService.GpuMemoryOverclock;
      if (memOc < 0) SelectComboByTag(GpuMemoryOCCombo, 0);
      else SelectComboByTag(GpuMemoryOCCombo, memOc);
      EcoQosToggle.IsChecked = ConfigService.EcoQosEnabled;
      EcoQosThrottlePluggedToggle.IsChecked = ConfigService.EcoQosThrottlePlugged;
      UpdateEcoQosSubEnabled();
      InitCoreKeepUI();
    }

    void SelectCombo(ComboBox combo, string text) {
      foreach (ComboBoxItem item in combo.Items)
        if (string.Equals(item.Content?.ToString(), text, StringComparison.Ordinal)) { combo.SelectedItem = item; return; }
    }

    void SelectComboByTag(ComboBox combo, object tag) {
      foreach (ComboBoxItem item in combo.Items)
        if (item.Tag != null && item.Tag.Equals(tag)) { combo.SelectedItem = item; return; }
    }

    // ── CPU 功率 ──
    void CpuPower_SelectionChanged(object sender, SelectionChangedEventArgs e) {
      if (_loading) return;
      var item = CpuPowerCombo.SelectedItem as ComboBoxItem;
      if (item == null) return;
      string tag = item.Tag.ToString();
      if (tag == "null") {
        ConfigService.CpuPower = "null";
        ConfigService.CpuPowerPl1 = -1;
        ConfigService.CpuPowerPl2 = -1;
        ConfigService.Save("CpuPower");
        return;
      }
      if (tag == "max") {
        ConfigService.CpuPower = "max";
        ConfigService.CpuPowerPl1 = 254;
        ConfigService.CpuPowerPl2 = 254;
        CpuPowerPL1Slider.Value = 254;
        CpuPowerPL2Slider.Value = 254;
        CpuPowerPL1Input.Text = "254";
        CpuPowerPL2Input.Text = "254";
        SetCpuPowerLimit(254);
        ConfigService.Save("CpuPower");
        return;
      }
      if (int.TryParse(tag, out int val) && val >= 10 && val <= 254) {
        ConfigService.CpuPower = val + " W";
        ConfigService.CpuPowerPl1 = val;
        ConfigService.CpuPowerPl2 = val;
        CpuPowerPL1Slider.Value = val;
        CpuPowerPL2Slider.Value = val;
        CpuPowerPL1Input.Text = val.ToString();
        CpuPowerPL2Input.Text = val.ToString();
        SetCpuPowerLimit((byte)val);
        ConfigService.Save("CpuPower");
      }
    }

    void CpuPowerExpand_Changed(object sender, RoutedEventArgs e) {
      CpuPowerExtra.Visibility = CpuPowerExpand.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    void CpuPowerPL1Slider_Changed(object s, RoutedPropertyChangedEventArgs<double> e) {
      if (CpuPowerPL1SliderVal != null) CpuPowerPL1SliderVal.Text = (int)e.NewValue + " W";
    }

    void CpuPowerPL2Slider_Changed(object s, RoutedPropertyChangedEventArgs<double> e) {
      if (CpuPowerPL2SliderVal != null) CpuPowerPL2SliderVal.Text = (int)e.NewValue + " W";
    }

    void CpuPowerPL1Apply_Click(object s, RoutedEventArgs e) {
      CpuPowerError.Visibility = Visibility.Collapsed;
      if (!int.TryParse(CpuPowerPL1Input.Text, out int val) || val < 1 || val > 254) {
        CpuPowerError.Text = Strings.ErrCpuPowerRange; CpuPowerError.Visibility = Visibility.Visible; return;
      }
      CpuPowerPL1Slider.Value = val;
      if (!SetCpuPowerLimitPL1Only((byte)val)) {
        CpuPowerError.Text = Strings.ErrCpuPowerWmi; CpuPowerError.Visibility = Visibility.Visible;
      }
      ConfigService.CpuPowerPl1 = val;
      ConfigService.Save("CpuPowerPl1");
    }

    void CpuPowerPL2Apply_Click(object s, RoutedEventArgs e) {
      CpuPowerError.Visibility = Visibility.Collapsed;
      if (!int.TryParse(CpuPowerPL2Input.Text, out int val) || val < 1 || val > 254) {
        CpuPowerError.Text = Strings.ErrCpuPowerRange; CpuPowerError.Visibility = Visibility.Visible; return;
      }
      CpuPowerPL2Slider.Value = val;
      if (!SetCpuPowerLimitPL2Only((byte)val)) {
        CpuPowerError.Text = Strings.ErrCpuPowerWmi; CpuPowerError.Visibility = Visibility.Visible;
      }
      ConfigService.CpuPowerPl2 = val;
      ConfigService.Save("CpuPowerPl2");
    }

    // ── IccMax ──
    void IccMaxExpand_Changed(object s, RoutedEventArgs e) {
      IccMaxExtra.Visibility = IccMaxExpand.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    void IccMaxSlider_Changed(object s, RoutedPropertyChangedEventArgs<double> e) {
      if (IccMaxSliderVal == null) return;
      int val = (int)e.NewValue;
      IccMaxSliderVal.Text = val == 0 ? Strings.NotSet : val + " A";
      IccMaxInput.Text = val.ToString();
      if (!_loading) { ConfigService.IccMax = val; ConfigService.Save("IccMax"); }
    }

    void IccMax_Changed(object sender, SelectionChangedEventArgs e) {
      if (_loading) return;
      var item = IccMaxCombo.SelectedItem as ComboBoxItem;
      if (item == null) return;
      int val = (int)item.Tag;
      if (val == 0) { ConfigService.IccMax = 0; IccMaxSlider.Value = 0; IccMaxInput.Text = "0"; ConfigService.Save("IccMax"); return; }
      ConfigService.IccMax = val; IccMaxSlider.Value = val; IccMaxInput.Text = val.ToString();
      SetIccMaxByWmi(val);
      ConfigService.Save("IccMax");
    }

    void IccMaxApply_Click(object s, RoutedEventArgs e) {
      IccMaxError.Visibility = Visibility.Collapsed;
      if (!int.TryParse(IccMaxInput.Text, out int val) || val < 0 || val > 255) {
        IccMaxError.Text = Strings.ErrIccMaxRange; IccMaxError.Visibility = Visibility.Visible; return;
      }
      IccMaxSlider.Value = val;
      if (val > 0) SetIccMaxByWmi(val);
      ConfigService.IccMax = val; ConfigService.Save("IccMax");
      SelectCombo(IccMaxCombo, val == 0 ? Strings.NotSet : val + " A");
    }

    // ── AC Load Line ──
    void AcLoadLine_Changed(object sender, SelectionChangedEventArgs e) {
      if (_loading) return;
      var item = AcLoadLineCombo.SelectedItem as ComboBoxItem;
      if (item == null) return;
      int level = (int)item.Tag;
      if (level == 0) { ConfigService.AcLoadLine = 0; ConfigService.Save("AcLoadLine"); return; }
      ConfigService.AcLoadLine = level;
      SetLoadLine(level);
      ConfigService.Save("AcLoadLine");
    }

    // ── 电源模式 ──
    void PowerMode_SelectionChanged(object sender, SelectionChangedEventArgs e) {
      if (_loading) return;
      var item = PowerModeCombo.SelectedItem as ComboBoxItem;
      if (item == null) return;
      int val = (int)item.Tag;
      ConfigService.PowerMode = val;
      ConfigService.Save("PowerMode");
      Guid guid;
      if (val == 0) guid = NativeMethods_Power.BEST_POWER_EFFICIENCY;
      else if (val == 2) guid = NativeMethods_Power.BEST_PERFORMANCE;
      else guid = Guid.Empty;
      NativeMethods_Power.PowerSetActiveOverlayScheme(guid);
    }

    // ── 电源计划 ──
    void BuildPowerPlanOptions() {
      PowerPlanCombo.Items.Clear();
      var plans = GetWindowsPowerPlans();
      string savedGuid = ConfigService.PowerPlanGuid;
      foreach (var p in plans) {
        bool isActive = string.IsNullOrEmpty(savedGuid) ? p.IsActive : p.Guid == savedGuid;
        PowerPlanCombo.Items.Add(new ComboBoxItem { Content = p.Name, Tag = p.Guid, IsSelected = isActive });
      }
      if (PowerPlanCombo.SelectedIndex < 0 && plans.Count > 0)
        PowerPlanCombo.SelectedIndex = 0;
    }

    void PowerPlan_SelectionChanged(object sender, SelectionChangedEventArgs e) {
      if (_loading) return;
      var item = PowerPlanCombo.SelectedItem as ComboBoxItem;
      if (item == null) return;
      string guid = (string)item.Tag;
      ConfigService.PowerPlanGuid = guid;
      ConfigService.Save("PowerPlanGuid");
      if (!string.IsNullOrEmpty(guid)) {
        var g = Guid.Parse(guid);
        NativeMethods_Power.PowerSetActiveScheme(IntPtr.Zero, ref g);
      }
    }

    static List<(string Name, string Guid, bool IsActive)> GetWindowsPowerPlans() {
      var plans = new List<(string, string, bool)>();
      try {
        string activeGuid = "";
        IntPtr activePtr;
        if (NativeMethods_Power.PowerGetActiveScheme(IntPtr.Zero, out activePtr) == 0) {
          activeGuid = Marshal.PtrToStructure<Guid>(activePtr).ToString();
          Marshal.FreeHGlobal(activePtr);
        }
        uint index = 0;
        while (true) {
          uint bufSize = 0;
          uint ret = NativeMethods_Power.PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 16, index, IntPtr.Zero, ref bufSize);
          if (ret == 259) break;
          if (ret != 234) break;
          IntPtr buf = Marshal.AllocHGlobal((int)bufSize);
          try {
            ret = NativeMethods_Power.PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 16, index, buf, ref bufSize);
            if (ret != 0) break;
            var guid = Marshal.PtrToStructure<Guid>(buf);
            string name = GetPowerPlanName(guid);
            plans.Add((name, guid.ToString(), guid.ToString() == activeGuid));
          } finally { Marshal.FreeHGlobal(buf); }
          index++;
        }
      } catch { }
      return plans;
    }

    static string GetPowerPlanName(Guid guid) {
      string g = guid.ToString();
      switch (g) {
        case "381b4222-f694-41f0-9685-ff5bb260df2f": return "平衡";
        case "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c": return "高性能";
        case "a1841308-3541-4fab-bc81-f71556f20b4a": return "节能";
        case "e9a42b02-d5df-448d-aa00-03f14749eb61": return "卓越性能";
      }
      try {
        uint bufSize = 2048;
        IntPtr buf = Marshal.AllocHGlobal((int)bufSize);
        try {
          if (NativeMethods_Power.PowerReadFriendlyName(IntPtr.Zero, ref guid, IntPtr.Zero, IntPtr.Zero, buf, ref bufSize) == 0) {
            string name = Marshal.PtrToStringUni(buf);
            if (!string.IsNullOrEmpty(name)) return name;
          }
        } finally { Marshal.FreeHGlobal(buf); }
      } catch { }
      return guid.ToString();
    }

    static List<(string Name, int Value)> GetWindowsPowerModes() {
      return new List<(string, int)> {
        (Strings.PowerModeEfficiency, 0),
        (Strings.PowerModeBalanced, 1),
        (Strings.PowerModePerformance, 2)
      };
    }

    // ── EcoQoS ──
    void UpdateEcoQosSubEnabled() {
      bool on = EcoQosToggle.IsChecked == true;
      EcoQosExtra.Opacity = on ? 1.0 : 0.4;
      EcoQosExtra.IsEnabled = on;
    }

    void EcoQosToggle_Checked(object sender, RoutedEventArgs e) {
      UpdateEcoQosSubEnabled();
      ConfigService.EcoQosEnabled = true;
      ConfigService.Save("EcoQosEnabled");
      EcoQosService.SetEnabled(true);
    }

    void EcoQosToggle_Unchecked(object sender, RoutedEventArgs e) {
      UpdateEcoQosSubEnabled();
      ConfigService.EcoQosEnabled = false;
      ConfigService.Save("EcoQosEnabled");
      EcoQosService.SetEnabled(false);
    }

    void EcoQosThrottlePlugged_Changed(object sender, RoutedEventArgs e) {
      ConfigService.EcoQosThrottlePlugged = EcoQosThrottlePluggedToggle.IsChecked == true;
      ConfigService.Save("EcoQosThrottlePlugged");
      EcoQosService.SetThrottlePlugged(EcoQosThrottlePluggedToggle.IsChecked == true);
    }

    void EcoQosWhitelistEdit_Click(object sender, RoutedEventArgs e) { ShowEcoQosListDialog(true); }
    void EcoQosBlacklistEdit_Click(object sender, RoutedEventArgs e) { ShowEcoQosListDialog(false); }

    void ShowEcoQosListDialog(bool isWhitelist) {
      string title = isWhitelist ? "进程白名单" : "进程黑名单";
      string current = isWhitelist ? ConfigService.EcoQosWhitelist : ConfigService.EcoQosBlacklist;
      var dlg = new Window {
        Title = title, Width = 400, Height = 300,
        WindowStartupLocation = WindowStartupLocation.CenterOwner,
        Owner = Window.GetWindow(this)
      };
      var sp = new StackPanel { Margin = new Thickness(10) };
      var tb = new TextBox { Text = current, AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap, Height = 200,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
      sp.Children.Add(tb);
      var btnP = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
      var ok = new Button { Content = "确定", Width = 80, Height = 30 };
      ok.Click += (_, __) => { dlg.DialogResult = true; dlg.Close(); };
      btnP.Children.Add(ok);
      var cancel = new Button { Content = "取消", Width = 80, Height = 30, Margin = new Thickness(8, 0, 0, 0) };
      cancel.Click += (_, __) => { dlg.DialogResult = false; dlg.Close(); };
      btnP.Children.Add(cancel);
      sp.Children.Add(btnP);
      dlg.Content = sp;
      if (dlg.ShowDialog() == true) {
        string result = tb.Text;
        if (isWhitelist) { ConfigService.EcoQosWhitelist = result; ConfigService.Save("EcoQosWhitelist"); EcoQosService.SaveWhitelist(result); }
        else { ConfigService.EcoQosBlacklist = result; ConfigService.Save("EcoQosBlacklist"); EcoQosService.SaveBlacklist(result); }
      }
    }

    // ── Core Keep ──
    void InitCoreKeepUI() {
      var data = CoreKeepService.Load();
      CoreKeepMasterToggle.IsChecked = data.MasterEnabled;
      CoreKeepList.ItemsSource = data.Entries;
      CoreKeepList.DisplayMemberPath = "ProcessName";
      CoreKeepList.IsEnabled = data.MasterEnabled;
      CoreKeepNewProcInput.IsEnabled = data.MasterEnabled;
      CoreKeepAddBtn.IsEnabled = data.MasterEnabled;
      CoreKeepList.SelectionChanged += CoreKeepList_SelectionChanged;
      if (data.MasterEnabled) CoreKeepService.StartAutoApply(data);
    }

    void CoreKeepList_SelectionChanged(object sender, SelectionChangedEventArgs e) {
      var entry = CoreKeepList.SelectedItem as CoreKeepEntry;
      if (entry == null) return;
      CoreKeepProcInput.Text = entry.ProcessName;
      CoreKeepPriorityText.Text = CoreKeepService.PriorityClassName(entry.PriorityClass);
      CoreKeepAffinityText.Text = "0x" + entry.AffinityMask.ToString("X");
      CoreKeepStatus.Text = entry.CapturedAt != null ? string.Format(Strings.CoreKeepStatusCapturedAt) + entry.CapturedAt : "";
    }

    void CoreKeepMasterToggle_Changed(object sender, RoutedEventArgs e) {
      bool on = CoreKeepMasterToggle.IsChecked == true;
      CoreKeepList.IsEnabled = on; CoreKeepNewProcInput.IsEnabled = on; CoreKeepAddBtn.IsEnabled = on;
      var data = CoreKeepService.Load();
      data.MasterEnabled = on;
      CoreKeepService.Save(data);
      if (on) CoreKeepService.StartAutoApply(data); else CoreKeepService.StopAutoApply();
    }

    void CoreKeepRefresh_Click(object sender, RoutedEventArgs e) {
      string procName = CoreKeepProcInput.Text?.Trim();
      if (string.IsNullOrEmpty(procName)) {
        RefreshCoreKeepList(CoreKeepService.Load());
        return;
      }
      if (!procName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) procName += ".exe";
      var entry = CoreKeepService.CaptureFromProcess(procName);
      CoreKeepPriorityText.Text = CoreKeepService.PriorityClassName(entry.PriorityClass);
      CoreKeepAffinityText.Text = "0x" + entry.AffinityMask.ToString("X");
      CoreKeepStatus.Text = string.Format(Strings.CoreKeepStatusCapturedAt) + entry.CapturedAt;
      var data = CoreKeepService.Load();
      var existing = data.Entries.Find(x => x.ProcessName.Equals(procName, StringComparison.OrdinalIgnoreCase));
      if (existing != null) {
        existing.PriorityClass = entry.PriorityClass;
        existing.AffinityMask = entry.AffinityMask;
        existing.CapturedAt = entry.CapturedAt;
        CoreKeepService.Save(data);
        RefreshCoreKeepList(data);
      }
    }
    void CoreKeepDelete_Click(object sender, RoutedEventArgs e) {
      var selected = CoreKeepList.SelectedItem as CoreKeepEntry;
      if (selected == null) return;
      var data = CoreKeepService.Load();
      data.Entries.RemoveAll(x => x.ProcessName == selected.ProcessName);
      CoreKeepService.Save(data);
      RefreshCoreKeepList(data);
    }
    void CoreKeepAdd_Click(object sender, RoutedEventArgs e) {
      string procName = CoreKeepNewProcInput.Text?.Trim();
      if (string.IsNullOrEmpty(procName)) return;
      if (!procName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) procName += ".exe";
      var data = CoreKeepService.Load();
      if (data.Entries.Exists(x => x.ProcessName.Equals(procName, StringComparison.OrdinalIgnoreCase))) return;
      var entry = CoreKeepService.CaptureFromProcess(procName);
      entry.Enabled = true;
      data.Entries.Add(entry);
      CoreKeepService.Save(data);
      CoreKeepNewProcInput.Text = "";
      RefreshCoreKeepList(data);
      if (data.MasterEnabled) CoreKeepService.StartAutoApply(data);
    }
    void RefreshCoreKeepList(CoreKeepData data) { CoreKeepList.ItemsSource = data.Entries; }

    // ── GPU 频率限制 ──
    void GpuClockExpand_Changed(object s, RoutedEventArgs e) {
      GpuClockExtra.Visibility = GpuClockExpand.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    void GpuClockSlider_Changed(object s, RoutedPropertyChangedEventArgs<double> e) {
      if (GpuClockSliderVal == null) return;
      int val = (int)e.NewValue;
      GpuClockSliderVal.Text = val == 0 ? Strings.GpuClockRestore : val + " MHz";
      GpuClockInput.Text = val.ToString();
      if (!_loading) { ConfigService.GpuClock = val; ConfigService.Save("GpuClock"); }
    }

    void GpuClock_SelectionChanged(object sender, SelectionChangedEventArgs e) {
      if (_loading) return;
      var item = GpuClockCombo.SelectedItem as ComboBoxItem;
      if (item == null) return;
      int val = (int)item.Tag;
      ConfigService.GpuClock = val;
      GpuClockSlider.Value = val;
      GpuClockInput.Text = val.ToString();
      if (val > 0) TrayService.SetGPUClockLimit(val);
      ConfigService.Save("GpuClock");
    }

    void GpuClockApply_Click(object s, RoutedEventArgs e) {
      GpuClockError.Visibility = Visibility.Collapsed;
      if (!int.TryParse(GpuClockInput.Text, out int val) || val < 0 || val > 2500) {
        GpuClockError.Text = Strings.ErrGpuClockRange; GpuClockError.Visibility = Visibility.Visible; return;
      }
      GpuClockSlider.Value = val;
      if (val > 0) TrayService.SetGPUClockLimit(val);
      ConfigService.GpuClock = val; ConfigService.Save("GpuClock");
      SelectCombo(GpuClockCombo, val == 0 ? Strings.GpuClockRestore : val + " MHz");
    }

    // ── GPU 核心超频 ──
    void GpuCoreOCExpand_Changed(object s, RoutedEventArgs e) {
      GpuCoreOCExtra.Visibility = GpuCoreOCExpand.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    void GpuCoreOCSlider_Changed(object s, RoutedPropertyChangedEventArgs<double> e) {
      if (GpuCoreOCSliderVal == null) return;
      int v = (int)e.NewValue;
      GpuCoreOCSliderVal.Text = (v >= 0 ? "+" : "") + v + " MHz";
      GpuCoreOCInput.Text = v.ToString();
      ConfigService.GpuCoreOverclock = v;
      if (v == 0) SelectCombo(GpuCoreOCCombo, Strings.NotSet);
      else SelectCombo(GpuCoreOCCombo, (v >= 0 ? "+" : "") + v + " MHz");
    }

    void GpuCoreOC_SelectionChanged(object sender, SelectionChangedEventArgs e) {
      if (_loading) return;
      var item = GpuCoreOCCombo.SelectedItem as ComboBoxItem;
      if (item == null) return;
      int val = (int)item.Tag;
      ConfigService.GpuCoreOverclock = val;
      GpuCoreOCSlider.Value = val;
      GpuCoreOCInput.Text = val.ToString();
      System.Threading.ThreadPool.QueueUserWorkItem(_ => GpuAppManager.SetCoreClockOffset(val));
      ConfigService.Save("GpuCoreOverclock");
    }

    void GpuCoreOCApply_Click(object s, RoutedEventArgs e) {
      GpuCoreOCError.Visibility = Visibility.Collapsed;
      if (!int.TryParse(GpuCoreOCInput.Text, out int val) || val < -270 || val > 270) {
        GpuCoreOCError.Text = "请输入 -270~270 之间的数值"; GpuCoreOCError.Visibility = Visibility.Visible; return;
      }
      GpuCoreOCSlider.Value = val;
      ConfigService.GpuCoreOverclock = val;
      System.Threading.ThreadPool.QueueUserWorkItem(_ => GpuAppManager.SetCoreClockOffset(val));
      ConfigService.Save("GpuCoreOverclock");
      SelectCombo(GpuCoreOCCombo, (val >= 0 ? "+" : "") + val + " MHz");
    }

    // ── GPU 显存超频 ──
    void GpuMemoryOCExpand_Changed(object s, RoutedEventArgs e) {
      GpuMemoryOCExtra.Visibility = GpuMemoryOCExpand.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    void GpuMemoryOCSlider_Changed(object s, RoutedPropertyChangedEventArgs<double> e) {
      if (GpuMemoryOCSliderVal == null) return;
      int v = (int)e.NewValue;
      GpuMemoryOCSliderVal.Text = "+" + v + " MHz";
      GpuMemoryOCInput.Text = v.ToString();
      ConfigService.GpuMemoryOverclock = v;
      if (v == 0) SelectCombo(GpuMemoryOCCombo, Strings.NotSet);
      else SelectCombo(GpuMemoryOCCombo, "+" + v + " MHz");
    }

    void GpuMemoryOC_SelectionChanged(object sender, SelectionChangedEventArgs e) {
      if (_loading) return;
      var item = GpuMemoryOCCombo.SelectedItem as ComboBoxItem;
      if (item == null) return;
      int val = (int)item.Tag;
      ConfigService.GpuMemoryOverclock = val;
      GpuMemoryOCSlider.Value = val;
      GpuMemoryOCInput.Text = val.ToString();
      System.Threading.ThreadPool.QueueUserWorkItem(_ => GpuAppManager.SetMemoryClockOffset(val));
      ConfigService.Save("GpuMemoryOverclock");
    }

    void GpuMemoryOCApply_Click(object s, RoutedEventArgs e) {
      GpuMemoryOCError.Visibility = Visibility.Collapsed;
      if (!int.TryParse(GpuMemoryOCInput.Text, out int val) || val < 0 || val > 2000) {
        GpuMemoryOCError.Text = "请输入 0~2000 之间的数值"; GpuMemoryOCError.Visibility = Visibility.Visible; return;
      }
      GpuMemoryOCSlider.Value = val;
      ConfigService.GpuMemoryOverclock = val;
      System.Threading.ThreadPool.QueueUserWorkItem(_ => GpuAppManager.SetMemoryClockOffset(val));
      ConfigService.Save("GpuMemoryOverclock");
      SelectCombo(GpuMemoryOCCombo, "+" + val + " MHz");
    }

    // ── 图形模式 ──
    void GfxMode_SelectionChanged(object s, SelectionChangedEventArgs e) {
      if (_loading) return;
      int mode = GfxModeCombo.SelectedIndex;
      if (mode == 3) {
        var confirm = System.Windows.MessageBox.Show(Strings.GfxUMAConfirm, Strings.GfxUMATitle,
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) { LoadState(); return; }
      }
      if (mode >= 0 && SetGfxMode(mode)) {
        GetGfxMode(out int current);
        if (current == mode) {
          System.Windows.MessageBox.Show(Strings.GfxSwitchedTo(
              mode == 0 ? "NVIDIA Advanced Optimus" :
              mode == 1 ? Strings.GfxDiscreteMode :
              mode == 2 ? Strings.GfxHybridMode : Strings.GfxUMALabel), Strings.Hint,
              MessageBoxButton.OK, MessageBoxImage.Information);
        } else {
          System.Windows.MessageBox.Show(Strings.GfxSwitchedTo(
              mode == 0 ? "NVIDIA Advanced Optimus" :
              mode == 1 ? Strings.GfxDiscreteMode :
              mode == 2 ? Strings.GfxHybridMode : Strings.GfxUMALabel) +
              "\n" + Strings.PerfGfxReboot, Strings.Hint,
              MessageBoxButton.OK, MessageBoxImage.Information);
        }
      }
    }

    // ── 热切换 ──
    void UpdateHotSwitchVisibility() {
      try {
        GetGfxMode(out int mode);
        HotSwitchCard.Visibility = (mode == 0 || mode == 2) ? Visibility.Visible : Visibility.Collapsed;
      } catch { HotSwitchCard.Visibility = Visibility.Collapsed; }
    }

    void HotSwitch_Click(object sender, RoutedEventArgs e) {
      int result = LaunchDDS();
      if (result != 0)
        System.Windows.MessageBox.Show(Strings.DdsInitFail, Strings.Error, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    // ── DB 版本 ──
    void DbVersion_SelectionChanged(object s, SelectionChangedEventArgs e) {
      if (_loading) return;
      if (!HardwareService.PowerOnline) {
        System.Windows.MessageBox.Show(Strings.PleaseConnectAC, Strings.Hint,
            MessageBoxButton.OK, MessageBoxImage.Warning);
        LoadState(); return;
      }
      if (DbVersionCombo.SelectedIndex == 0) {
        if (!TrayService.CheckDBVersion(1)) {
          System.Windows.MessageBox.Show(Strings.DriverNotAllow + "\n" + Strings.DriverVersionRange, Strings.Error,
              MessageBoxButton.OK, MessageBoxImage.Warning);
          LoadState(); return;
        }
        var confirm = System.Windows.MessageBox.Show(Strings.PerfDbUnlockWarning, Strings.DbUnlockTitle,
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) { LoadState(); return; }
        ConfigService.DBVersion = 1; ConfigService.Save("DBVersion");
        TrayService.ChangeDBVersion(1);
      } else {
        ConfigService.DBVersion = 2; ConfigService.Save("DBVersion");
        TrayService.ChangeDBVersion(2);
      }
    }

    // ── TGP / PPAB ──
    void TppExpand_Changed(object s, RoutedEventArgs e) {
      TppExtra.Visibility = TppExpand.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    void TppExtraSlider_Changed(object s, RoutedPropertyChangedEventArgs<double> e) {
      if (TppExtraSliderVal == null) return;
      int val = (int)e.NewValue;
      TppExtraSliderVal.Text = val.ToString();
      TppInput.Text = val.ToString();
      if (!_loading) { ConfigService.Tpp = val; ConfigService.Save("Tpp"); }
    }

    void Tpp_SelectionChanged(object sender, SelectionChangedEventArgs e) {
      if (_loading) return;
      var item = TppCombo.SelectedItem as ComboBoxItem;
      if (item == null) return;
      int val = (int)item.Tag;
      ConfigService.Tpp = val;
      TppExtraSlider.Value = val;
      TppInput.Text = val.ToString();
      if (val > 0) { SetConcurrentTdp((byte)val); PpabCheck.IsEnabled = ConfigService.TgpEnabled; }
      else { PpabCheck.IsEnabled = false; PpabCheck.IsChecked = false; }
      ConfigService.Save("Tpp");
    }

    void TppApply_Click(object s, RoutedEventArgs e) {
      TppError.Visibility = Visibility.Collapsed;
      if (!int.TryParse(TppInput.Text, out int val) || val < 0 || val > 255) {
        TppError.Text = Strings.ErrTppRange; TppError.Visibility = Visibility.Visible; return;
      }
      TppExtraSlider.Value = val;
      SetConcurrentTdp((byte)val);
      ConfigService.Tpp = val; ConfigService.Save("Tpp");
      SelectCombo(TppCombo, val == 0 ? Strings.TppDisable : val.ToString());
      if (val == 0) { PpabCheck.IsEnabled = false; PpabCheck.IsChecked = false; }
      else { PpabCheck.IsEnabled = ConfigService.TgpEnabled; }
    }

    void CtgpCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) {
      if (_loading) return;
      var item = CtgpCombo.SelectedItem as ComboBoxItem;
      if (item == null) return;
      bool enabled = (bool)item.Tag;
      ConfigService.TgpEnabled = enabled;
      PpabCheck.IsEnabled = enabled;
      if (!enabled) PpabCheck.IsChecked = false;
      ConfigService.Save("TgpEnabled");
      SetGpuPowerState(enabled, PpabCheck.IsChecked == true, ConfigService.DState == 2 ? 2 : 1);
    }

    void Ppab_Changed(object sender, RoutedEventArgs e) {
      bool enabled = PpabCheck.IsChecked == true;
      ConfigService.PpabEnabled = enabled;
      ConfigService.Save("PpabEnabled");
      SetGpuPowerState(ConfigService.TgpEnabled, enabled, ConfigService.DState == 2 ? 2 : 1);
      TppExpand.IsEnabled = enabled;
    }

    // ── dState ──
    void DState_SelectionChanged(object s, SelectionChangedEventArgs e) {
      if (_loading) return;
      ConfigService.DState = DStateCombo.SelectedIndex == 1 ? 2 : 1;
      ConfigService.Save("DState");
      SetGpuPowerState(ConfigService.TgpEnabled, ConfigService.PpabEnabled, ConfigService.DState);
    }

    // ── 最大帧率 ──
    void FpsExpand_Changed(object s, RoutedEventArgs e) {
      FpsExtra.Visibility = FpsExpand.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    void FpsSlider_Changed(object s, RoutedPropertyChangedEventArgs<double> e) {
      if (FpsSliderVal == null) return;
      int idx = (int)e.NewValue;
      string[] labels = { Strings.Unlimited, "30 FPS", "60 FPS", "90 FPS", "120 FPS", "144 FPS",
        "165 FPS", "240 FPS", "300 FPS", "360 FPS", "480 FPS", "1000 FPS" };
      if (idx >= 0 && idx < labels.Length) { FpsSliderVal.Text = labels[idx]; FpsInput.Text = labels[idx].Replace(" FPS", ""); }
    }

    void Fps_SelectionChanged(object sender, SelectionChangedEventArgs e) {
      if (_loading) return;
      var item = FpsCombo.SelectedItem as ComboBoxItem;
      if (item == null) return;
      int val = (int)item.Tag;
      int idx = 0;
      foreach (ComboBoxItem ci in FpsCombo.Items) { if ((int)ci.Tag == val) break; idx++; }
      FpsSlider.Value = idx;
      FpsInput.Text = val.ToString();
      ConfigService.MaxFrameRate = val == 0 ? -1 : val;
      if (HasNvidiaGpu()) HP.Omen.Core.Common.NVidiaApi.NvApiWrapper.NVAPI_SetMaxFrameRate(val);
      ConfigService.Save("MaxFrameRate");
    }

    void FpsApply_Click(object s, RoutedEventArgs e) {
      FpsError.Visibility = Visibility.Collapsed;
      if (!int.TryParse(FpsInput.Text, out int val) || val < 0) {
        FpsError.Text = Strings.ErrFpsNonNegative; FpsError.Visibility = Visibility.Visible; return;
      }
      int[] vals = { 0, 30, 60, 90, 120, 144, 165, 240, 300, 360, 480, 1000 };
      int bestIdx = 0;
      for (int i = 0; i < vals.Length; i++) {
        if (vals[i] == val) { bestIdx = i; break; }
        if (Math.Abs(vals[i] - val) < Math.Abs(vals[bestIdx] - val)) bestIdx = i;
      }
      if (vals[bestIdx] != val) {
        FpsError.Text = Strings.ErrFpsNotSupported; FpsError.Visibility = Visibility.Visible; return;
      }
      FpsSlider.Value = bestIdx;
      ConfigService.MaxFrameRate = vals[bestIdx] == 0 ? -1 : vals[bestIdx];
      if (HasNvidiaGpu()) HP.Omen.Core.Common.NVidiaApi.NvApiWrapper.NVAPI_SetMaxFrameRate(vals[bestIdx]);
      ConfigService.Save("MaxFrameRate");
      SelectCombo(FpsCombo, vals[bestIdx] == 0 ? Strings.Unlimited : vals[bestIdx] + " FPS");
    }

    // ── 屏幕刷新率 ──
    void BuildRefreshRateOptions() {
      RefreshRateCombo.Items.Clear();
      int current = GetCurrentRefreshRate();
      int saved = ConfigService.RefreshRate;
      int selected = saved > 0 ? saved : current;
      var rates = GetAvailableRefreshRates();
      foreach (int r in rates)
        RefreshRateCombo.Items.Add(new ComboBoxItem { Content = r + " Hz", Tag = r, IsSelected = r == selected });
      if (RefreshRateCombo.SelectedIndex < 0 && rates.Count > 0)
        SelectCombo(RefreshRateCombo, selected + " Hz");
      if (saved > 0) { RefreshRateSlider.Value = saved; RefreshRateSliderVal.Text = saved + " Hz"; RefreshRateInput.Text = saved.ToString(); }
      else { RefreshRateSlider.Value = current; RefreshRateSliderVal.Text = current + " Hz"; RefreshRateInput.Text = current.ToString(); }
    }

    void RefreshRateExpand_Changed(object s, RoutedEventArgs e) {
      RefreshRateExtra.Visibility = RefreshRateExpand.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    void RefreshRate_SelectionChanged(object sender, SelectionChangedEventArgs e) {
      if (_loading) return;
      var item = RefreshRateCombo.SelectedItem as ComboBoxItem;
      if (item == null) return;
      int val = (int)item.Tag;
      RefreshRateSlider.Value = val;
      RefreshRateInput.Text = val.ToString();
      ConfigService.RefreshRate = val;
      ApplyRefreshRate(val);
      ConfigService.Save("RefreshRate");
    }

    void RefreshRateSlider_Changed(object s, RoutedPropertyChangedEventArgs<double> e) {
      if (RefreshRateSliderVal == null) return;
      int val = (int)e.NewValue;
      RefreshRateSliderVal.Text = val + " Hz";
      RefreshRateInput.Text = val.ToString();
      if (!_loading) {
        SelectCombo(RefreshRateCombo, val + " Hz");
        ConfigService.RefreshRate = val;
        ApplyRefreshRate(val);
        ConfigService.Save("RefreshRate");
      }
    }

    void RefreshRateApply_Click(object s, RoutedEventArgs e) {
      RefreshRateError.Visibility = Visibility.Collapsed;
      if (!int.TryParse(RefreshRateInput.Text, out int val) || val < 30 || val > 360) {
        RefreshRateError.Text = Strings.ErrRefreshRateRange; RefreshRateError.Visibility = Visibility.Visible; return;
      }
      RefreshRateSlider.Value = val;
      ConfigService.RefreshRate = val;
      ApplyRefreshRate(val);
      ConfigService.Save("RefreshRate");
      SelectCombo(RefreshRateCombo, val + " Hz");
    }

    static int GetCurrentRefreshRate() {
      var dm = new NativeMethods_Display.DEVMODE();
      dm.dmSize = (short)Marshal.SizeOf(dm);
      if (NativeMethods_Display.EnumDisplaySettings(null, NativeMethods_Display.ENUM_CURRENT_SETTINGS, ref dm))
        return dm.dmDisplayFrequency;
      return 60;
    }

    static List<int> GetAvailableRefreshRates() {
      var seen = new HashSet<int>();
      var rates = new List<int>();
      var dm = new NativeMethods_Display.DEVMODE();
      dm.dmSize = (short)Marshal.SizeOf(typeof(NativeMethods_Display.DEVMODE));
      int mode = 0;
      while (NativeMethods_Display.EnumDisplaySettings(null, mode, ref dm)) {
        if (dm.dmPelsWidth == 0 || dm.dmPelsHeight == 0) { mode++; continue; }
        if (seen.Add(dm.dmDisplayFrequency)) rates.Add(dm.dmDisplayFrequency);
        mode++;
      }
      rates.Sort();
      return rates;
    }

    static void ApplyRefreshRate(int hz) {
      var dm = new NativeMethods_Display.DEVMODE();
      dm.dmSize = (short)Marshal.SizeOf(typeof(NativeMethods_Display.DEVMODE));
      if (!NativeMethods_Display.EnumDisplaySettings(null, NativeMethods_Display.ENUM_CURRENT_SETTINGS, ref dm)) return;
      int prevHz = dm.dmDisplayFrequency;
      dm.dmDisplayFrequency = hz;
      dm.dmFields = NativeMethods_Display.DM_DISPLAYFREQUENCY;
      int ret = NativeMethods_Display.ChangeDisplaySettings(ref dm, 0);
      if (ret != 0) {
        dm.dmDisplayFrequency = prevHz;
        NativeMethods_Display.ChangeDisplaySettings(ref dm, 0);
      }
    }

    bool _perfExpanded = true;
    const double PerfCollapseWidth = 1000;

    void PerfPage_SizeChanged(object sender, SizeChangedEventArgs e) {
      if (!e.WidthChanged) return;
      if (e.NewSize.Width > PerfCollapseWidth) {
        if (!_perfExpanded) { _perfExpanded = true; ExpandPerfGrids(); }
      } else {
        if (_perfExpanded) { _perfExpanded = false; CollapsePerfGrids(); }
      }
    }

    void ExpandPerfGrids() {
      LayoutPerfGrid(CpuPerfGrid, expand: true);
      LayoutPerfGrid(GpuPerfGrid, expand: true);
    }

    void CollapsePerfGrids() {
      LayoutPerfGrid(CpuPerfGrid, expand: false);
      LayoutPerfGrid(GpuPerfGrid, expand: false);
    }

    void LayoutPerfGrid(Grid grid, bool expand) {
      int childCount = grid.Children.Count;
      if (childCount == 0) return;
      bool isCpu = (childCount == 7);
      grid.ColumnDefinitions[1].Width = expand
        ? new GridLength(1, GridUnitType.Star)
        : new GridLength(0, GridUnitType.Pixel);
      for (int i = 0; i < childCount; i++) {
        if (!(grid.Children[i] is FrameworkElement c)) continue;
        if (expand) {
          Thickness expandMargin = (i % 2 == 0) ? new Thickness(0, 0, 4, 8) : new Thickness(4, 0, 0, 8);
          if (isCpu && i == 6) {
            Grid.SetRow(c, 3); Grid.SetColumn(c, 0); Grid.SetColumnSpan(c, 2);
            c.Margin = new Thickness(0, 0, 0, 8);
          } else {
            Grid.SetRow(c, i / 2); Grid.SetColumn(c, i % 2); Grid.SetColumnSpan(c, 1);
            c.Margin = expandMargin;
          }
        } else {
          Grid.SetRow(c, i); Grid.SetColumn(c, 0); Grid.SetColumnSpan(c, 1);
          c.Margin = new Thickness(0, 0, 0, 8);
        }
      }
    }
  }
}
