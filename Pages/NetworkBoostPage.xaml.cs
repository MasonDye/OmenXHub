// NetworkBoostPage.cs - 多网卡加速页面（HypoMux 移植）
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using OmenSuperHub.Services;
using OmenSuperHub.Services.NetworkBoost;
using OmenSuperHub.Utils;

namespace OmenSuperHub.Pages {
  public partial class NetworkBoostPage : Page {
    DispatcherTimer _timer;
    bool _loading;

    public NetworkBoostPage() {
      InitializeComponent();
      Loaded += OnLoaded;
      Unloaded += OnUnloaded;
    }

    void OnLoaded(object s, RoutedEventArgs e) {
      // ponytail: OnLog 订阅移到 Loaded(先退订再订,幂等) — 页面被 CachedPageService 缓存后
      // 离开(Unloaded 退订)→再回来(Loaded)能重新订阅,否则日志面板在二次进入后不再更新。
      BoostService.OnLog -= OnBoostLog;
      BoostService.OnLog += OnBoostLog;
      _loading = true;
      // 模式下拉
      ModeCombo.SelectedIndex = ConfigService.BoostMode == "tun" ? 1 : 0;
      // 限速值
      GlobalLimitBox.Text = ConfigService.BoostGlobalLimitKBps > 0 ? ConfigService.BoostGlobalLimitKBps.ToString() : "";
      NicLimitBox.Text = ConfigService.BoostNicLimitKBps > 0 ? ConfigService.BoostNicLimitKBps.ToString() : "";
      GlobalLimitBox.LostFocus += (s2, e2) => SaveLimitValue();
      NicLimitBox.LostFocus += (s2, e2) => SaveLimitValue();
      _loading = false;

      BoostService.Scan();
      RebuildNicList();
      UpdateStatus();

      // ponytail: 守卫 —— 页被缓存且上次 Unloaded 未触发时旧 timer 可能仍在跑,先停再建避免叠加。
      if (_timer != null) { _timer.Stop(); _timer = null; }
      _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
      _timer.Tick += OnTimerTick;
      _timer.Start();
    }

    void SaveLimitValue() {
      int g = 0, n = 0;
      int.TryParse((GlobalLimitBox.Text ?? "").Trim(), out g);
      int.TryParse((NicLimitBox.Text ?? "").Trim(), out n);
      ConfigService.BoostGlobalLimitKBps = Math.Max(0, g);
      ConfigService.BoostNicLimitKBps = Math.Max(0, n);
      ConfigService.Save("BoostGlobalLimit");
      ConfigService.Save("BoostNicLimit");
    }

    void OnUnloaded(object s, RoutedEventArgs e) {
      if (_timer != null) { _timer.Stop(); _timer = null; }
      BoostService.OnLog -= OnBoostLog;
    }

    void RebuildNicList() {
      NicListPanel.Children.Clear();
      foreach (var nic in BoostService.AllNics) {
        var cb = new CheckBox {
          Tag = nic.Name,
          Content = nic.Name + "  (" + nic.Ip + ")",
          IsChecked = BoostService.SelectedNics.Exists(n => n.Name == nic.Name),
          Margin = new Thickness(0, 4, 0, 4),
          FontSize = 13
        };
        cb.Checked += (s, e) => {
          var name = (string)((CheckBox)s).Tag;
          BoostService.SetSelected(name, true);
        };
        cb.Unchecked += (s, e) => {
          var name = (string)((CheckBox)s).Tag;
          BoostService.SetSelected(name, false);
        };
        NicListPanel.Children.Add(cb);
      }
      if (NicListPanel.Children.Count == 0)
        NicListPanel.Children.Add(new TextBlock {
          Text = "—", FontSize = 12, Foreground = (Brush)FindResource("TextSecondaryBrush"),
          Margin = new Thickness(0, 6, 0, 0)
        });
    }

    void ModeCombo_SelectionChanged(object s, SelectionChangedEventArgs e) {
      if (_loading || ModeCombo == null) return;
      var item = ModeCombo.SelectedItem as ComboBoxItem;
      if (item == null) return;
      BoostService.SetMode((string)item.Tag ?? "proxy");
    }

    void StartBtn_Click(object s, RoutedEventArgs e) {
      if (BoostService.IsRunning) {
        BoostService.Stop();
        UpdateStatus();
        return;
      }
      string err;
      if (!BoostService.Start(out err)) {
        DialogHelper.Error(err);
        return;
      }
      UpdateStatus();
    }

    void SelectAllBtn_Click(object s, RoutedEventArgs e) {
      foreach (var nic in BoostService.AllNics) BoostService.SetSelected(nic.Name, true);
      RebuildNicList();
    }

    void ClearBtn_Click(object s, RoutedEventArgs e) {
      foreach (var nic in BoostService.AllNics) BoostService.SetSelected(nic.Name, false);
      RebuildNicList();
    }

    void RefreshBtn_Click(object s, RoutedEventArgs e) {
      BoostService.Scan();
      RebuildNicList();
    }

    void GotoRulesBtn_Click(object s, RoutedEventArgs e) {
      Views.MainWindow.NavigateToPage("RoutingRules");
    }

    void UpdateStatus() {
      if (!IsLoaded) return;
      Dispatcher.BeginInvoke(new Action(() => {
        if (BoostService.IsRunning) {
          StartBtn.Content = Strings.BoostStop;
          StatusDot.Fill = (Brush)FindResource("AccentGreenBrush");
          StatusText.Text = BoostService.IsTun ? Strings.BoostStatusTun : Strings.BoostStatusProxy;
        } else {
          StartBtn.Content = Strings.BoostStart;
          StatusDot.Fill = (Brush)FindResource("TextFillColorDisabledBrush");
          StatusText.Text = Strings.BoostStatusStopped;
        }
      }));
    }

    void OnTimerTick(object s, EventArgs e) {
      // ponytail: RefreshTotals 是亲 UI 的只读聚合 (sums NicInfo.Down/UpMbps, 后端 ProxyEngine
      // 已经自己维护这些值);窗口关闭到托盘时刷新三个 TextBlock 是看不见的写。
      // BoostService 代理线程独立运行,本 timer 关停不影响加速。窗口恢复下次进页 Loaded 重启。
      if (Window.GetWindow(this)?.IsVisible != true) { _timer?.Stop(); return; }
      BoostService.RefreshTotals();
      TotalDownText.Text = BoostService.TotalDownMbps.ToString("F1");
      TotalUpText.Text = BoostService.TotalUpMbps.ToString("F1");
      TotalConnText.Text = BoostService.TotalConnections.ToString();
      UpdateStatus();
    }

    void OnBoostLog(string msg) {
      Dispatcher.BeginInvoke(new Action(() => {
        LogBox.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + msg + "\n");
        LogBox.ScrollToEnd();
      }));
    }
  }
}
