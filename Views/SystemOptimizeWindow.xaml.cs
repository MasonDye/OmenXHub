// SystemOptimizeWindow.xaml.cs - 系统优化二级弹窗（服务启动类型 + 开机启动项 + 通用优化）
// 服务端逻辑在 Services/SystemOptimization
using System;
using System.Collections.Generic;
using System.ComponentModel;
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

  public sealed class TweakItemVm : INotifyPropertyChanged {
    public OptimizationTweak Tweak { get; set; }

    private TweakState _state;
    public TweakState State {
      get => _state;
      set {
        if (_state == value) return;
        _state = value;
        // ponytail: State 变化连带 StateText/StateBrush 重算,单改当前项即可刷新文字颜色,
        // 不必整表重建(否则所有 Toggle 闪烁)
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(StateBrush));
      }
    }

    public string Name => Strings.TweakName(Tweak.Id);
    public string Description => Strings.TweakDescription(Tweak.Id);

    private bool _isChecked;
    public bool IsChecked {
      get => _isChecked;
      set { if (_isChecked == value) return; _isChecked = value; OnPropertyChanged(nameof(IsChecked)); }
    }

    public Visibility RestartVisible => Tweak.NeedsRestart ? Visibility.Visible : Visibility.Collapsed;
    public string StateText =>
      State == TweakState.Applied ? Strings.SysOptTweakApplied :
      State == TweakState.Partial ? Strings.SysOptTweakPartial :
      Strings.SysOptTweakNotApplied;
    // ponytail: 绑定每次刷新都会读 StateBrush — 静态冻结,不再每次访问 new 画刷
    static readonly SolidColorBrush BrushApplied = FrozenBrush(0x4C, 0xC3, 0x8A);
    static readonly SolidColorBrush BrushPartial = FrozenBrush(0xFF, 0xB9, 0x00);
    static readonly SolidColorBrush BrushNone = FrozenBrush(0x8A, 0x8A, 0x8A);

    static SolidColorBrush FrozenBrush(byte r, byte g, byte b) {
      var b2 = new SolidColorBrush(Color.FromRgb(r, g, b));
      b2.Freeze();
      return b2;
    }

    public SolidColorBrush StateBrush {
      get {
        switch (State) {
          case TweakState.Applied: return BrushApplied;
          case TweakState.Partial: return BrushPartial;
          default: return BrushNone;
        }
      }
    }

    public event PropertyChangedEventHandler PropertyChanged;
    void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
  }

  public partial class SystemOptimizeWindow : FluentWindow {
    bool _loadingServices;
    bool _loadingStartup;
    bool _startupLoaded;
    bool _loadingTweaks;
    bool _tweaksLoaded;
    // ponytail: 回滚期间抑制 SelectionChanged/Toggle 再入 — 失败回滚 set SelectedIndex/IsChecked
    // 会再次触发对应 handler,若无保护会重复执行一次操作(甚至反向覆盖),见各 handler 回滚分支。
    bool _rollingBackService;
    bool _rollingBackStartup;
    bool _rollingBackTweak;

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
      if (_loadingServices || _rollingBackService || !(sender is ComboBox combo)) return;
      var vm = combo.Tag as ServiceItemVm;
      if (vm == null) return;
      var target = combo.SelectedIndex == 0 ? ServiceStartupType.Automatic
                 : combo.SelectedIndex == 1 ? ServiceStartupType.Manual
                 : ServiceStartupType.Disabled;
      bool ok = SystemServiceOptimizer.SetStartupType(vm.Name, target);
      if (!ok) {
        _rollingBackService = true;   // 回滚也会触发本 handler,抑制再入
        try { combo.SelectedIndex = vm.StartupTypeIndex; }
        finally { _rollingBackService = false; }
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
      if (_loadingStartup || _rollingBackStartup || !(sender is ToggleSwitch toggle)) return;
      var item = toggle.Tag as StartupItem;
      if (item == null) return;
      bool ok = StartupItemOptimizer.SetEnabled(item, toggle.IsChecked == true);
      if (!ok) {
        _rollingBackStartup = true;   // 回滚 IsChecked 也会触发本 handler,抑制再入
        try { toggle.IsChecked = item.IsEnabled; }
        finally { _rollingBackStartup = false; }
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
          // ponytail: IsChecked 用 != NotApplied —— Partial(部分生效)也算"开",否则开关显示关
          // 但 StateText 显示"部分生效",两者矛盾。用户点关即完全恢复。
          items.Add(new TweakItemVm { Tweak = t, State = state, IsChecked = state != TweakState.NotApplied });
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
      if (_loadingTweaks || _rollingBackTweak || !(sender is ToggleSwitch toggle)) return;
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
            _rollingBackTweak = true;   // 回滚 IsChecked 也会触发本 handler,抑制再入
            try { toggle.IsChecked = !on; }
            finally { _rollingBackTweak = false; }
            // ponytail: 回滚后同步 vm.IsChecked,避免下次刷新前语义错位
            vm.IsChecked = !on;
            DialogHelper.Warn(Strings.SysOptTweakFailed(vm.Name));
          } else {
            // ponytail: 不再 ReloadTweaks() 整表重建 — 那会把所有 Tweak 的 Toggle 销毁重实例化,
            // 视觉上全部 Toggle 闪烁(与 AutomationPage 同款 bug)。只重算当前项状态+同步 IsChecked,
            // 让 StateText 反映"已应用/未应用",其余项不动。
            vm.IsChecked = on;
            vm.State = SystemTweaks.GetState(vm.Tweak);
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
