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
    bool _boostOpRunning;

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
      // R15/BUG-D1: 页被 CachedPageService 缓存,每次导航都重跑 OnLoaded —— 旧代码每次
      // Loaded 都 += 匿名 lambda(无法 -=),来回切 N 次后一次失焦触发 N+1 次 SaveLimitValue
      // (重复注册表写)。改命名方法 + 先退订再订阅,与本文件 OnLog 的幂等写法对齐。
      GlobalLimitBox.LostFocus -= OnLimitLostFocus;
      GlobalLimitBox.LostFocus += OnLimitLostFocus;
      NicLimitBox.LostFocus -= OnLimitLostFocus;
      NicLimitBox.LostFocus += OnLimitLostFocus;
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

    void OnLimitLostFocus(object s, RoutedEventArgs e) => SaveLimitValue();

    void SaveLimitValue() {
      // 审查修复: 溢出值(>int.MaxValue)TryParse 失败 → 静默存 0(不限速)而文本框仍显示
      // 用户输入,显示与下发不一致。垃圾/溢出还原为当前配置;空串 = 清空不限速(合法输入)。
      int g = ParseLimitBox(GlobalLimitBox, ConfigService.BoostGlobalLimitKBps);
      int n = ParseLimitBox(NicLimitBox, ConfigService.BoostNicLimitKBps);
      ConfigService.BoostGlobalLimitKBps = Math.Max(0, g);
      ConfigService.BoostNicLimitKBps = Math.Max(0, n);
      ConfigService.Save("BoostGlobalLimit");
      ConfigService.Save("BoostNicLimit");
    }

    static int ParseLimitBox(System.Windows.Controls.TextBox box, int current) {
      string t = (box.Text ?? "").Trim();
      if (t.Length == 0) return 0;
      if (int.TryParse(t, out int v) && v >= 0) return v;
      box.Text = current > 0 ? current.ToString() : "";
      return current;
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

    async void StartBtn_Click(object s, RoutedEventArgs e) {
      // ponytail: BoostService.Start/Stop 在 TUN 路径含 Thread.Sleep(1500)+WaitForExit(3000)
      // (TunManager 启动校验/route.exe 清理),同步调 UI 冻结最长 ~4.5s;挪到 Task.Run。
      // _boostOpRunning 守卫狂点(按钮禁用期间走不到重入,双保险)。
      if (_boostOpRunning) return;
      _boostOpRunning = true;
      StartBtn.IsEnabled = false;
      try {
        bool wasRunning = BoostService.IsRunning;
        string err = "";
        bool ok = true;
        await System.Threading.Tasks.Task.Run(() => {
          if (wasRunning) BoostService.Stop();
          else ok = BoostService.Start(out err);
        });
        if (!ok) DialogHelper.Error(err);
      } finally {
        StartBtn.IsEnabled = true;
        _boostOpRunning = false;
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
        // 审查修复: 长驻会话无上限追加 → 超 ~1200 行截头保留尾部 600 行,内存有界。
        if (LogBox.LineCount > 1200) {
          var tail = new System.Text.StringBuilder();
          for (int i = LogBox.LineCount - 600; i < LogBox.LineCount; i++)
            tail.Append(LogBox.GetLineText(i)).Append('\n');
          LogBox.Clear();
          LogBox.AppendText(tail.ToString());
        }
        LogBox.ScrollToEnd();
      }));
    }
  }
}
