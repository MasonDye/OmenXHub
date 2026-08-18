// SystemOptimizeWindow.xaml.cs - 系统优化二级弹窗（服务启动类型 + 开机启动项 + 通用优化）
// 服务端逻辑在 Services/SystemOptimization
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Wpf.Ui.Controls;
using OmenSuperHub.Services.SystemOptimization;
using OmenSuperHub.Utils;

namespace OmenSuperHub.Views {

  public sealed class ServiceItemVm {
    public ServiceItem Item { get; set; }
    public string DisplayName => Item.DisplayName;
    public string Name => Item.Name;
    public ServiceState State => Item.State;
    public bool CanChange => Item.CanChange;
    /// <summary>ComboBox 索引：0=自动 1=手动 2=禁用</summary>
    public int StartupTypeIndex {
      get {
        switch (Item.StartupType) {
          case ServiceStartupType.Manual: return 1;
          case ServiceStartupType.Disabled: return 2;
          default: return 0;
        }
      }
    }
  }

  public sealed class TweakItemVm {
    public OptimizationTweak Tweak { get; set; }
    public TweakState State { get; set; }
    public string Name => Strings.TweakName(Tweak.Id);
    public string Description => Strings.TweakDescription(Tweak.Id);
    public bool IsChecked { get; set; }
    public Visibility RestartVisible => Tweak.NeedsRestart ? Visibility.Visible : Visibility.Collapsed;
    public string StateText =>
      State == TweakState.Applied ? Strings.SysOptTweakApplied :
      State == TweakState.Partial ? Strings.SysOptTweakPartial :
      Strings.SysOptTweakNotApplied;
    public SolidColorBrush StateBrush {
      get {
        switch (State) {
          case TweakState.Applied: return new SolidColorBrush(Color.FromRgb(0x4C, 0xC3, 0x8A));
          case TweakState.Partial: return new SolidColorBrush(Color.FromRgb(0xFF, 0xB9, 0x00));
          default: return new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0x8A));
        }
      }
    }
  }

  public partial class SystemOptimizeWindow : FluentWindow {
    bool _loadingServices;
    bool _loadingStartup;
    bool _startupLoaded;
    bool _loadingTweaks;
    bool _tweaksLoaded;

    public SystemOptimizeWindow() {
      InitializeComponent();
      Loaded += (s, e) => ReloadServices();
      KeyDown += (s, e) => { if (e.Key == Key.Escape) Close(); };
    }

    void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();

    // ── 一键优化 ──

    void OneClickOptimize_Click(object sender, RoutedEventArgs e) {
      if (!DialogHelper.Confirm(Strings.SysOptOneClickConfirm, Strings.SysOptOneClickTitle)) return;
      OneClickOptimizeBtn.IsEnabled = false;
      System.Threading.ThreadPool.QueueUserWorkItem(_ => {
        var r = SystemServiceOptimizer.ApplyRecommendedPreset();
        Dispatcher.BeginInvoke(new Action(() => {
          OneClickOptimizeBtn.IsEnabled = true;
          ReloadServices();
          DialogHelper.Info(Strings.SysOptPresetResult(r.Applied, r.AlreadyOptimal, r.Skipped, r.Failed),
                            Strings.SysOptOneClickTitle);
        }));
      });
    }

    // ── 恢复 ──

    void RestoreBtn_Click(object sender, RoutedEventArgs e) {
      if (!DialogHelper.Confirm(Strings.SysOptRestoreConfirm, Strings.SysOptRestoreTitle)) return;
      RestoreBtn.IsEnabled = false;
      System.Threading.ThreadPool.QueueUserWorkItem(_ => {
        var r = SystemServiceOptimizer.ApplyDefaultPreset();
        Dispatcher.BeginInvoke(new Action(() => {
          RestoreBtn.IsEnabled = true;
          ReloadServices();
          DialogHelper.Info(Strings.SysOptPresetResult(r.Applied, r.AlreadyOptimal, r.Skipped, r.Failed),
                            Strings.SysOptRestoreTitle);
        }));
      });
    }

    void RefreshBtn_Click(object sender, RoutedEventArgs e) {
      ReloadServices();
      ReloadStartup();
      ReloadTweaks();
    }

    // ── 服务 ──

    void ReloadServices() {
      System.Threading.ThreadPool.QueueUserWorkItem(_ => {
        var items = new List<ServiceItemVm>();
        foreach (var s in SystemServiceOptimizer.Enumerate())
          items.Add(new ServiceItemVm { Item = s });
        Dispatcher.BeginInvoke(new Action(() => {
          _loadingServices = true;
          ServiceList.ItemsSource = items;
          _loadingServices = false;
        }));
      });
    }

    void ServiceType_SelectionChanged(object sender, SelectionChangedEventArgs e) {
      if (_loadingServices || !(sender is ComboBox combo)) return;
      var vm = combo.Tag as ServiceItemVm;
      if (vm == null) return;
      var target = combo.SelectedIndex == 0 ? ServiceStartupType.Automatic
                 : combo.SelectedIndex == 1 ? ServiceStartupType.Manual
                 : ServiceStartupType.Disabled;
      bool ok = SystemServiceOptimizer.SetStartupType(vm.Name, target);
      if (!ok) {
        combo.SelectedIndex = vm.StartupTypeIndex; // 回滚 UI
        DialogHelper.Warn(Strings.SysOptServiceFailed(vm.DisplayName));
      } else {
        vm.Item.StartupType = target;
      }
    }

    // ── 启动项 ──

    void ReloadStartup() {
      System.Threading.ThreadPool.QueueUserWorkItem(_ => {
        var items = StartupItemOptimizer.Enumerate();
        Dispatcher.BeginInvoke(new Action(() => {
          _loadingStartup = true;
          StartupList.ItemsSource = items;
          _startupLoaded = true;
          _loadingStartup = false;
        }));
      });
    }

    void StartupToggle_Changed(object sender, RoutedEventArgs e) {
      if (_loadingStartup || !(sender is ToggleSwitch toggle)) return;
      var item = toggle.Tag as StartupItem;
      if (item == null) return;
      bool ok = StartupItemOptimizer.SetEnabled(item, toggle.IsChecked == true);
      if (!ok) {
        toggle.IsChecked = item.IsEnabled; // 回滚 UI
        DialogHelper.Warn(Strings.SysOptStartupFailed(item.Name));
      } else {
        item.IsEnabled = toggle.IsChecked == true;
      }
    }

    // ── 通用优化 ──

    void ReloadTweaks() {
      System.Threading.ThreadPool.QueueUserWorkItem(_ => {
        var items = new List<TweakItemVm>();
        foreach (var t in SystemTweaks.All) {
          var state = SystemTweaks.GetState(t);
          items.Add(new TweakItemVm { Tweak = t, State = state, IsChecked = state == TweakState.Applied });
        }
        Dispatcher.BeginInvoke(new Action(() => {
          _loadingTweaks = true;
          TweakList.ItemsSource = items;
          _tweaksLoaded = true;
          _loadingTweaks = false;
        }));
      });
    }

    void TweakToggle_Changed(object sender, RoutedEventArgs e) {
      if (_loadingTweaks || !(sender is ToggleSwitch toggle)) return;
      var vm = toggle.Tag as TweakItemVm;
      if (vm == null) return;
      bool on = toggle.IsChecked == true;
      toggle.IsEnabled = false;
      System.Threading.ThreadPool.QueueUserWorkItem(_ => {
        bool ok;
        try { SystemTweaks.Apply(vm.Tweak, on); ok = true; }
        catch { ok = false; }
        Dispatcher.BeginInvoke(new Action(() => {
          toggle.IsEnabled = true;
          if (!ok) {
            toggle.IsChecked = !on; // 回滚 UI
            DialogHelper.Warn(Strings.SysOptTweakFailed(vm.Name));
          } else {
            ReloadTweaks();
          }
        }));
      });
    }

    // ── Tab 懒加载 ──

    void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e) {
      if (e.AddedItems.Count == 0 || !(e.AddedItems[0] is TabItem tab)) return;
      int idx = MainTabs.Items.IndexOf(tab);
      if (idx == 1 && !_startupLoaded) ReloadStartup();
      if (idx == 2 && !_tweaksLoaded) ReloadTweaks();
    }
  }
}
