// RoutingRulesPage.cs - 进程级分流规则页面（限速增强版）
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using OmenSuperHub.Services.NetworkBoost;
using OmenSuperHub.Utils;
using OmenSuperHub.Views;

namespace OmenSuperHub.Pages {
  public partial class RoutingRulesPage : Page {
    // 快捷预设：常见游戏/应用进程名
    static readonly string[] _presets = {
      "cs2.exe", "chrome.exe", "steam.exe", "discord.exe",
      "epicgameslauncher.exe", "firefox.exe", "spotify.exe", "msedge.exe"
    };

    public RoutingRulesPage() {
      InitializeComponent();
      Loaded += OnLoaded;
      Unloaded += (s, e) => SaveAll();
    }

    void OnLoaded(object s, RoutedEventArgs e) {
      BuildPresets();
      LoadRules();
      // ponytail: Loaded 里大量填充可视树,被 CachedPageService 缓存二次 Loaded 时会渲染成
      // 空白页 — 显式 UpdateLayout 强制一遍,对齐 LightingPage/CoreKeepPage 的修法。
      UpdateLayout();
    }

    void BackBtn_Click(object s, RoutedEventArgs e) {
      Views.MainWindow.NavigateToPage("NetworkBoost");
    }

    void BuildPresets() {
      PresetPanel.Children.Clear();
      foreach (var name in _presets) {
        var btn = new Wpf.Ui.Controls.Button {
          Content = name,
          Appearance = Wpf.Ui.Controls.ControlAppearance.Transparent,
          Padding = new Thickness(10, 4, 10, 4),
          Margin = new Thickness(0, 0, 6, 6),
          FontSize = 12
        };
        btn.Click += (s, e) => AddRuleIfNew(name, "aggregation", 0);
        PresetPanel.Children.Add(btn);
      }
    }

    void LoadRules() {
      RulesPanel.Children.Clear();
      foreach (var r in RoutingRuleStore.Load()) AddRow(r.ProcessName, r.Outbound, r.LimitKBps);
      if (RulesPanel.Children.Count == 0) AddRow("", "aggregation", 0);
    }

    void AddRuleIfNew(string process, string outbound, int limitKBps) {
      if (IsDuplicate(process)) {
        DialogHelper.Warn(Strings.BoostRulesDup + ": " + process);
        return;
      }
      AddRow(process, outbound, limitKBps);
      SaveAll();
    }

    bool IsDuplicate(string process) {
      if (string.IsNullOrWhiteSpace(process)) return false;
      return RulesPanel.Children.OfType<Grid>()
        .Any(g => GetTextBox(g, 0)?.Text?.Trim().Equals(process, System.StringComparison.OrdinalIgnoreCase) == true);
    }

    void AddRow(string process, string outbound, int limitKBps) {
      var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
      grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
      grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
      grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
      grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
      grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });

      // Column 0: 进程名
      var tb = new TextBox {
        Text = process,
        Margin = new Thickness(0, 0, 6, 0),
        FontSize = 13,
        Tag = outbound
      };
      tb.LostFocus += (s, e) => SaveAll();
      Grid.SetColumn(tb, 0);
      grid.Children.Add(tb);

      // Column 1: 选择进程按钮
      var pick = new Wpf.Ui.Controls.Button {
        Content = Strings.BoostRulesSelectProcess,
        Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary,
        Padding = new Thickness(8, 2, 8, 2),
        Margin = new Thickness(0, 0, 6, 0),
        FontSize = 12
      };
      pick.Click += (s, e) => {
        var dlg = new ProcessSelectDialog(Window.GetWindow(this));
        if (dlg.ShowDialog() == true && !string.IsNullOrEmpty(dlg.SelectedProcess)) {
          tb.Text = dlg.SelectedProcess;
          SaveAll();
        }
      };
      Grid.SetColumn(pick, 1);
      grid.Children.Add(pick);

      // Column 2: 限速 KB/s（0 = 不限）
      var limitTb = new TextBox {
        Text = limitKBps > 0 ? limitKBps.ToString() : "",
        Width = 100,
        Margin = new Thickness(0, 0, 6, 0),
        FontSize = 13,
        Tag = "limit"
      };
      limitTb.LostFocus += (s, e) => SaveAll();
      Grid.SetColumn(limitTb, 2);
      grid.Children.Add(limitTb);

      // Column 3: 出口通道
      var combo = new ComboBox {
        Width = 140,
        Margin = new Thickness(0, 0, 6, 0),
        FontSize = 13
      };
      combo.Items.Add(new ComboBoxItem { Tag = "aggregation", Content = Strings.BoostOutboundAgg });
      combo.Items.Add(new ComboBoxItem { Tag = "direct", Content = Strings.BoostOutboundDirect });
      combo.Items.Add(new ComboBoxItem { Tag = "nic_ethernet", Content = Strings.BoostOutboundEth });
      combo.Items.Add(new ComboBoxItem { Tag = "nic_wifi", Content = Strings.BoostOutboundWifi });
      combo.SelectedIndex = 0;
      for (int i = 0; i < combo.Items.Count; i++) {
        if ((string)((ComboBoxItem)combo.Items[i]).Tag == outbound) { combo.SelectedIndex = i; break; }
      }
      combo.SelectionChanged += (s, e) => {
        if (combo.SelectedItem is ComboBoxItem item)
          tb.Tag = (string)item.Tag;
        SaveAll();
      };
      Grid.SetColumn(combo, 3);
      grid.Children.Add(combo);

      // Column 4: 删除
      var del = new Wpf.Ui.Controls.Button {
        Content = Strings.BoostRulesDelete,
        Width = 50,
        FontSize = 12,
        Appearance = Wpf.Ui.Controls.ControlAppearance.Caution
      };
      del.Click += (s, e) => { RulesPanel.Children.Remove(grid); SaveAll(); };
      Grid.SetColumn(del, 4);
      grid.Children.Add(del);

      RulesPanel.Children.Add(grid);
    }

    void AddBtn_Click(object s, RoutedEventArgs e) {
      AddRow("", "aggregation", 0);
      SaveAll();
    }

    void ClearAllBtn_Click(object s, RoutedEventArgs e) {
      if (RulesPanel.Children.Count == 0) return;
      if (DialogHelper.Confirm(Strings.BoostRulesClearAll + "?")) {
        RulesPanel.Children.Clear();
        AddRow("", "aggregation", 0);
        SaveAll();
      }
    }

    // 按列索引查找 Grid 内的 TextBox
    static TextBox GetTextBox(Grid g, int col) {
      return g.Children.OfType<TextBox>().FirstOrDefault(t => Grid.GetColumn(t) == col);
    }

    void SaveAll() {
      var rules = new List<RoutingRule>();
      foreach (var child in RulesPanel.Children) {
        if (child is Grid g) {
          var tb = GetTextBox(g, 0);
          if (tb == null || string.IsNullOrWhiteSpace(tb.Text)) continue;
          var limitTb = GetTextBox(g, 2);
          int limit = 0;
          if (limitTb != null) int.TryParse((limitTb.Text ?? "").Trim(), out limit);
          rules.Add(new RoutingRule {
            ProcessName = tb.Text.Trim(),
            Outbound = tb.Tag as string ?? "aggregation",
            LimitKBps = System.Math.Max(0, limit)
          });
        }
      }
      RoutingRuleStore.Save(rules);
    }
  }
}
