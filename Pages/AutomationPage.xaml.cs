// AutomationPage.cs - 自动化页面
// 流水线列表管理、快捷操作、主开关，支持触发器和步骤的增删改查
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using OmenSuperHub.Services;
using OmenSuperHub.Utils;

namespace OmenSuperHub.Pages {
  public partial class AutomationPage : Page {
    public AutomationPage() {
      InitializeComponent();
      Action<string> execHandler = null;
      Loaded += (s, e) => {
        bool enabled = ConfigService.AutomationEnabled;
        AutoEnableToggle.IsChecked = enabled;
        AutoAddPipelineBtn.IsEnabled = enabled;
        AutoAddQuickActionBtn.IsEnabled = enabled;
        RefreshList();
        execHandler = (name) => Dispatcher.InvokeAsync(() => RefreshList());
        AutomationProcessor.ExecutionStatusChanged += execHandler;
      };
      Unloaded += (s, e) => {
        if (execHandler != null) {
          AutomationProcessor.ExecutionStatusChanged -= execHandler;
          execHandler = null;
        }
      };
    }

    void AutoEnableToggle_Checked(object sender, RoutedEventArgs e) {
      ConfigService.AutomationEnabled = true;
      ConfigService.Save("AutomationEnabled");
      AutoAddPipelineBtn.IsEnabled = true;
      AutoAddQuickActionBtn.IsEnabled = true;
    }

    void AutoEnableToggle_Unchecked(object sender, RoutedEventArgs e) {
      ConfigService.AutomationEnabled = false;
      ConfigService.Save("AutomationEnabled");
      AutoAddPipelineBtn.IsEnabled = false;
      AutoAddQuickActionBtn.IsEnabled = false;
    }

    void RefreshList() {
      AutoPipelineList.Children.Clear();
      AutoNoPipelines.Visibility = Visibility.Collapsed;
      var hasPipeline = false;
      var pipelines = AutomationService.Pipelines;
      if (pipelines != null) {
        foreach (var p in pipelines) {
          bool isQuickAction = p.Triggers.Count == 1 && p.Triggers[0].Type == "QuickAction";
          if (isQuickAction) continue;
          hasPipeline = true;
          AutoPipelineList.Children.Add(BuildPipelineCard(p));
        }
      }
      if (!hasPipeline)
        AutoNoPipelines.Visibility = Visibility.Visible;
      RefreshQuickActions();
      TrayService.RebuildMenu();
    }

    void RefreshQuickActions() {
      AutoQuickActionPanel.Children.Clear();
      var qas = AutomationService.GetQuickActions();
      AutoNoQuickActions.Visibility = qas.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
      foreach (var qa in qas)
        AutoQuickActionPanel.Children.Add(BuildQuickActionCard(qa));
    }

    FrameworkElement BuildQuickActionCard(AutomationPipeline p) {
      var border = new Border {
        Margin = new Thickness(0, 0, 8, 8),
        CornerRadius = new CornerRadius(8),
        Background = TryFindResource("CardBackgroundFillColorDefaultBrush") as Brush ?? SystemColors.WindowBrush,
        Padding = new Thickness(12, 10, 12, 10)
      };
      var sp = new StackPanel();

      // Header: same (*, Auto) grid as pipeline cards
      var grid = new Grid();
      grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
      grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
      grid.Children.Add(new TextBlock {
        Text = p.Name, FontWeight = FontWeights.SemiBold,
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Left
      });
      var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
      var runBtn = new Button {
        Content = new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.Play24, FontSize = 12 },
        Width = 28, Height = 24, Padding = new Thickness(0), ToolTip = null
      };
      runBtn.Click += (s, e) => AutomationProcessor.ExecutePipeline(p);
      btnPanel.Children.Add(runBtn);
      var editBtn = new Button { Content = Strings.ButtonEdit, MinWidth = 56, Height = 28, Margin = new Thickness(4, 0, 0, 0), Padding = new Thickness(8, 0, 8, 0) };
      editBtn.Click += (s, e) => ShowEditDialog(p);
      btnPanel.Children.Add(editBtn);
      var delBtn = new Button {
        Content = new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.Delete24, FontSize = 12 },
        Width = 28, Height = 24, Margin = new Thickness(4, 0, 0, 0), Padding = new Thickness(0)
      };
      delBtn.Click += (s, e) => { AutomationService.RemovePipeline(p); RefreshList(); };
      btnPanel.Children.Add(delBtn);
      Grid.SetColumn(btnPanel, 1);
      grid.Children.Add(btnPanel);
      sp.Children.Add(grid);

      // Subtitle: 无触发器（快捷操作）
      sp.Children.Add(new TextBlock {
        Text = Strings.AutomationQuickActions,
        FontSize = 11,
        Foreground = TryFindResource("TextFillColorSecondaryBrush") as Brush ?? SystemColors.GrayTextBrush,
        Margin = new Thickness(0, 2, 0, 0)
      });

      // Step summary (matches pipeline card)
      if (p.Steps.Count > 0) {
        sp.Children.Add(new TextBlock {
          Text = Strings.AutomationStepCount(p.Steps.Count), FontSize = 11,
          Foreground = TryFindResource("TextFillColorSecondaryBrush") as Brush ?? SystemColors.GrayTextBrush,
          Margin = new Thickness(0, 4, 0, 0)
        });
      }

      border.Child = sp;
      return border;
    }

    FrameworkElement BuildPipelineCard(AutomationPipeline p) {
      var border = new Border {
        Margin = new Thickness(0, 0, 0, 8),
        CornerRadius = new CornerRadius(8),
        Background = TryFindResource("CardBackgroundFillColorDefaultBrush") as Brush ?? SystemColors.WindowBrush,
        Padding = new Thickness(12, 8, 12, 8)
      };
      var sp = new StackPanel();

      // Header: Grid (*, Auto) — name left, buttons right
      var headerGrid = new Grid();
      headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
      headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

      // Left: name
      headerGrid.Children.Add(new TextBlock {
        Text = p.Name, FontWeight = FontWeights.SemiBold,
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Left
      });

      // Right: button group
      var btnPanel = new StackPanel {
        Orientation = Orientation.Horizontal,
        HorizontalAlignment = HorizontalAlignment.Right
      };

      var toggle = new Wpf.Ui.Controls.ToggleSwitch {
        IsChecked = p.Enabled, VerticalAlignment = VerticalAlignment.Center
      };
      toggle.Checked += (s, e) => { p.Enabled = true; AutomationService.Save(); };
      toggle.Unchecked += (s, e) => { p.Enabled = false; AutomationService.Save(); };
      btnPanel.Children.Add(toggle);

      if (AutomationProcessor.CurrentPipelineName == p.Name && AutomationProcessor.IsExecuting) {
        btnPanel.Children.Add(new TextBlock {
          Text = Strings.AutomationExecuting, Foreground = Brushes.Orange,
          VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0)
        });
      }

      var runBtn = new Button {
        Content = new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.Play24, FontSize = 12 },
        Width = 28, Height = 24, Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(0), ToolTip = null
      };
      runBtn.Click += (s, e) => AutomationProcessor.ExecutePipeline(p);
      btnPanel.Children.Add(runBtn);

      var editBtn = new Button { Content = Strings.ButtonEdit, MinWidth = 56, Height = 28, Margin = new Thickness(4, 0, 0, 0), Padding = new Thickness(8, 0, 8, 0) };
      editBtn.Click += (s, e) => ShowEditDialog(p);
      btnPanel.Children.Add(editBtn);

      var delBtn = new Button {
        Content = new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.Delete24, FontSize = 12 },
        Width = 28, Height = 24, Margin = new Thickness(4, 0, 0, 0), Padding = new Thickness(0)
      };
      delBtn.Click += (s, e) => {
        AutomationService.RemovePipeline(p);
        RefreshList();
      };
      btnPanel.Children.Add(delBtn);

      Grid.SetColumn(btnPanel, 1);
      headerGrid.Children.Add(btnPanel);
      sp.Children.Add(headerGrid);

      // Trigger labels
      p.EnsureTriggers();
      if (p.Triggers.Count > 0) {
        var triggerPanel = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        foreach (var t in p.Triggers) {
          triggerPanel.Children.Add(new Border {
            Background = TryFindResource("CardBackgroundFillColorSecondaryBrush") as Brush ?? SystemColors.ControlBrush,
            CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(0, 0, 4, 2),
            Child = new TextBlock {
              Text = AutomationTriggerHelper.GetDisplayText(t), FontSize = 11
            }
          });
        }
        sp.Children.Add(triggerPanel);
      }

      // Step summary
      if (p.Steps.Count > 0) {
        var stepsText = new TextBlock {
          Text = Strings.AutomationStepCount(p.Steps.Count), FontSize = 11,
          Foreground = TryFindResource("TextFillColorSecondaryBrush") as Brush ?? SystemColors.GrayTextBrush,
          Margin = new Thickness(0, 4, 0, 0)
        };
        sp.Children.Add(stepsText);
      }

      border.Child = sp;
      return border;
    }

    void AutoAddPipeline_Click(object sender, RoutedEventArgs e) {
      ShowEditDialog(null);
    }

    void AutoAddQuickAction_Click(object sender, RoutedEventArgs e) {
      ShowEditDialog(null, true);
    }

    void ShowEditDialog(AutomationPipeline existing, bool isQuickAction = false) {
      try {
        var owner = Window.GetWindow(this);
        if (owner == null) return;
        var editor = new Views.PipelineEditorWindow(existing, owner, isQuickAction);
        if (editor.ShowDialog() == true)
          RefreshList();
      } catch (Exception ex) {
        DialogHelper.Info("Error: " + ex.Message + "\n\n" + ex.StackTrace, "Pipeline Editor Error");
      }
    }

  }

  internal static class AutomationTriggerHelper {
    public static (string type, string label)[] AllTriggerTypes = {
      ("ProcessStart", Strings.AutomationTriggerProcessStart),
      ("ProcessStop", Strings.AutomationTriggerProcessStop),
      ("Startup", Strings.AutomationTriggerStartup),
      ("Resume", Strings.AutomationTriggerResume),
      ("SessionLock", Strings.AutomationTriggerSessionLock),
      ("SessionUnlock", Strings.AutomationTriggerSessionUnlock),
      ("PowerAC", Strings.AutomationTriggerPowerAC),
      ("PowerDC", Strings.AutomationTriggerPowerDC),
      ("DisplayConnect", Strings.AutomationTriggerDisplayConnect),
      ("DisplayDisconnect", Strings.AutomationTriggerDisplayDisconnect),
      ("TimeSchedule", Strings.AutomationTriggerTimeSchedule),
      ("BatteryAbove", Strings.AutomationTriggerBatteryAbove),
      ("BatteryBelow", Strings.AutomationTriggerBatteryBelow),
       ("CpuTempAbove", Strings.AutomationTriggerCpuTempAbove),
       ("GpuTempAbove", Strings.AutomationTriggerGpuTempAbove),
       ("QuickAction", Strings.AutomationTriggerQuickAction),
       ("Hotkey", Strings.AutomationTriggerHotkey),
     };

    public static string GetDisplayText(AutomationTrigger t) {
      foreach (var a in AllTriggerTypes)
        if (a.type == t.Type) {
          if (!string.IsNullOrEmpty(t.Value))
            return a.label + ": " + t.Value;
          return a.label;
        }
      return t.Type + (string.IsNullOrEmpty(t.Value) ? "" : ": " + t.Value);
    }

    }
}
