using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using OmenSuperHub;
using OmenSuperHub.Services;

namespace OmenSuperHub.Views {
  public partial class PipelineEditorWindow : Wpf.Ui.Controls.FluentWindow {
    readonly AutomationPipeline _pipeline;
    readonly bool _isNew;
    readonly bool _isQuickAction;

    static readonly string[] TriggerTypes = {
      "ProcessStart", "ProcessStop", "PowerAC", "PowerDC", "Startup", "Resume",
      "TimeSchedule", "SessionLock", "SessionUnlock", "QuickAction",
      "BatteryAbove", "BatteryBelow", "CpuTempAbove", "GpuTempAbove",
      "DisplayConnect", "DisplayDisconnect"
    };

    static readonly string[] ThresholdTriggerTypes = {
      "BatteryAbove", "BatteryBelow", "CpuTempAbove", "GpuTempAbove"
    };

    static readonly string[] ValueTriggerTypes = {
      "ProcessStart", "ProcessStop", "TimeSchedule"
    };

    public PipelineEditorWindow(AutomationPipeline existing, Window owner, bool isQuickAction = false) {
      InitializeComponent();
      Owner = owner;
      _isNew = existing == null;
      _isQuickAction = isQuickAction || (!_isNew && existing.Triggers.Count == 1 && existing.Triggers[0].Type == "QuickAction");
      _pipeline = _isNew ? new AutomationPipeline { Name = Strings.NewPipelineDefaultName, Steps = new List<AutomationStep>() } : existing;
      _pipeline.EnsureTriggers();

      if (_isQuickAction && _pipeline.Triggers.Count == 0)
        _pipeline.Triggers.Add(new AutomationTrigger("QuickAction"));

      DialogTitle.Text = _isNew ? Strings.AutomationAddPipeline : Strings.AutomationEditPipeline;
      NameBox.Text = _pipeline.Name;
      SaveBtn.Content = Strings.AutomationSave;
      CancelBtn.Content = Strings.AutomationCancel;
      StepsHeading.Text = Strings.AutomationStepType + ":";
      TriggersHeading.Text = Strings.AutomationTriggersHeading;
      AddTriggerBtn.Content = Strings.AutomationAddTrigger;
      AddStepBtn.Content = Strings.AutomationAddStep;

      if (_isQuickAction)
        AddTriggerBtn.Visibility = Visibility.Collapsed;

      RefreshTriggersUI();
      RefreshStepsUI();
    }

    // ── Triggers UI ──

    void RefreshTriggersUI() {
      TriggersPanel.Children.Clear();
      _pipeline.EnsureTriggers();

      for (int i = 0; i < _pipeline.Triggers.Count; i++) {
        int idx = i;
        var trig = _pipeline.Triggers[idx];

        var border = new Border {
          Background = TryFindResource("CardBackgroundFillColorDefaultBrush") as Brush,
          CornerRadius = new CornerRadius(6),
          Margin = new Thickness(0, 0, 0, 6),
          Padding = new Thickness(12, 8, 12, 8)
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var toggle = new Wpf.Ui.Controls.ToggleSwitch {
          IsChecked = trig.Enabled,
          Margin = new Thickness(0, 0, 8, 0)
        };
        toggle.Checked += (s, a) => { _pipeline.Triggers[idx].Enabled = true; };
        toggle.Unchecked += (s, a) => { _pipeline.Triggers[idx].Enabled = false; };
        Grid.SetColumn(toggle, 0);
        grid.Children.Add(toggle);

        string label = GetTriggerLabel(trig.Type);
        if (Array.IndexOf(ValueTriggerTypes, trig.Type) >= 0 && !string.IsNullOrEmpty(trig.Value))
          label += ": " + trig.Value;
        else if (Array.IndexOf(ThresholdTriggerTypes, trig.Type) >= 0 && !string.IsNullOrEmpty(trig.Value))
          label += " >= " + trig.Value;
        grid.Children.Add(new TextBlock {
          Text = label,
          Foreground = TryFindResource("TextPrimaryBrush") as Brush,
          VerticalAlignment = VerticalAlignment.Center,
          Margin = new Thickness(4, 0, 4, 0)
        });
        Grid.SetColumn(grid.Children[1], 1);

        if (!_isQuickAction) {
          var delBtn = new Button {
            Content = new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.Delete24, FontSize = 12 },
            Width = 24, Height = 24, Padding = new Thickness(0)
          };
          int delIdx = idx;
          delBtn.Click += (s, a) => {
            _pipeline.Triggers.RemoveAt(delIdx);
            RefreshTriggersUI();
          };
          Grid.SetColumn(delBtn, 2);
          grid.Children.Add(delBtn);
        }

        border.Child = grid;
        TriggersPanel.Children.Add(border);
      }

      if (_pipeline.Triggers.Count == 0) {
        TriggersPanel.Children.Add(new TextBlock {
          Text = Strings.AutomationNoTriggers,
          Foreground = TryFindResource("TextSecondaryBrush") as Brush,
          Margin = new Thickness(4, 2, 0, 2),
          FontSize = 11
        });
      }
    }

    void AddTriggerBtn_Click(object sender, RoutedEventArgs e) {
      string brush_card = "CardBackgroundFillColorDefaultBrush";
      string brush_border = "BorderSubtleBrush";
      string brush_textSecondary = "TextSecondaryBrush";

      var dialog = new Wpf.Ui.Controls.FluentWindow {
        Title = Strings.AutomationAddTrigger,
        Width = 480, Height = 420,
        WindowStartupLocation = WindowStartupLocation.CenterOwner,
        Owner = this, ResizeMode = ResizeMode.NoResize,
        ShowInTaskbar = false,
        WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType.Mica,
        ExtendsContentIntoTitleBar = true,
        Background = System.Windows.Media.Brushes.Transparent
      };

      var rootGrid = new Grid();
      rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
      rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
      rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

      // Title
      rootGrid.Children.Add(new Border {
        BorderBrush = TryFindResource(brush_border) as Brush,
        BorderThickness = new Thickness(0, 0, 0, 1),
        Padding = new Thickness(16, 12, 16, 12),
        Child = new TextBlock { Text = Strings.AutomationAddTrigger, FontSize = 14, FontWeight = FontWeights.SemiBold }
      });

      // Content
      var sv = new ScrollViewer { Margin = new Thickness(12, 8, 12, 0) };
      var cs = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };

      // Trigger type card
      var typeGrid = new Grid();
      typeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
      typeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
      typeGrid.Children.Add(new TextBlock {
        Text = Strings.AutomationTriggerType + ":", FontSize = 13,
        VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0)
      });
      var typeCombo = new ComboBox { MinHeight = 36, FontSize = 13, MinWidth = 240 };
      foreach (var tt in TriggerTypes)
        typeCombo.Items.Add(new ComboBoxItem { Content = GetTriggerLabel(tt), Tag = tt, MinHeight = 28, Padding = new Thickness(4, 2, 4, 2) });
      typeCombo.SelectedIndex = 0;
      Grid.SetColumn(typeCombo, 1);
      typeGrid.Children.Add(typeCombo);
      cs.Children.Add(new Border {
        Background = TryFindResource(brush_card) as Brush,
        CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 8, 12, 8),
        Margin = new Thickness(0, 0, 0, 8), Child = typeGrid
      });

      // Value card (collapsible)
      var valGrid = new Grid();
      valGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
      valGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
      valGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
      valGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
      valGrid.Children.Add(new TextBlock {
        Text = Strings.AutomationTriggerValue + ":", FontSize = 13,
        VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 6)
      });
      var valBox = new TextBox { Height = 32, FontSize = 13, VerticalContentAlignment = VerticalAlignment.Center };
      Grid.SetColumn(valBox, 1);
      valGrid.Children.Add(valBox);
      var valHint = new TextBlock {
        Text = GetTriggerValueHint((string)((ComboBoxItem)typeCombo.SelectedItem).Tag),
        FontSize = 11, Foreground = TryFindResource(brush_textSecondary) as Brush
      };
      Grid.SetRow(valHint, 1);
      Grid.SetColumn(valHint, 1);
      valGrid.Children.Add(valHint);
      var valCard = new Border {
        Background = TryFindResource(brush_card) as Brush,
        CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 8, 12, 8),
        Margin = new Thickness(0, 0, 0, 8), Child = valGrid
      };
      cs.Children.Add(valCard);

      typeCombo.SelectionChanged += (s, a) => {
        string tt = (string)((ComboBoxItem)typeCombo.SelectedItem).Tag;
        bool showVal = Array.IndexOf(ValueTriggerTypes, tt) >= 0 || Array.IndexOf(ThresholdTriggerTypes, tt) >= 0;
        valCard.Visibility = showVal ? Visibility.Visible : Visibility.Collapsed;
        valHint.Text = GetTriggerValueHint(tt);
      };
      string initTt = (string)((ComboBoxItem)typeCombo.SelectedItem).Tag;
      valCard.Visibility = Array.IndexOf(ValueTriggerTypes, initTt) >= 0 || Array.IndexOf(ThresholdTriggerTypes, initTt) >= 0
        ? Visibility.Visible : Visibility.Collapsed;

      sv.Content = cs;
      Grid.SetRow(sv, 1);
      rootGrid.Children.Add(sv);

      // Button bar
      var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
      var addBtn = new Button { Content = Strings.AutomationSave, MinWidth = 90, Height = 30, Padding = new Thickness(16, 4, 16, 4), Margin = new Thickness(0, 0, 8, 0) };
      addBtn.Click += (s, a) => {
        var item = typeCombo.SelectedItem as ComboBoxItem;
        if (item == null) return;
        _pipeline.Triggers.Add(new AutomationTrigger((string)item.Tag, valBox.Text ?? ""));
        RefreshTriggersUI();
        dialog.Close();
      };
      btnPanel.Children.Add(addBtn);
      var clsBtn = new Button { Content = Strings.AutomationCancel, MinWidth = 90, Height = 30, Padding = new Thickness(16, 4, 16, 4) };
      clsBtn.Click += (s, a) => dialog.Close();
      btnPanel.Children.Add(clsBtn);
      rootGrid.Children.Add(new Border {
        BorderBrush = TryFindResource(brush_border) as Brush,
        BorderThickness = new Thickness(0, 1, 0, 0),
        Padding = new Thickness(16, 8, 16, 8), Child = btnPanel
      });
      Grid.SetRow(rootGrid.Children[rootGrid.Children.Count - 1], 2);

      dialog.Content = rootGrid;
      dialog.ShowDialog();
    }

    // ── Steps UI ──

    void RefreshStepsUI() {
      StepsPanel.Children.Clear();
      for (int i = 0; i < _pipeline.Steps.Count; i++) {
        int idx = i;
        var step = _pipeline.Steps[idx];

        var card = new Border {
          Background = TryFindResource("CardBackgroundFillColorDefaultBrush") as Brush,
          CornerRadius = new CornerRadius(6),
          Margin = new Thickness(0, 0, 0, 6),
          Padding = new Thickness(0)
        };
        var outerStack = new StackPanel();

        // ── Header ──
        var header = new Grid { Margin = new Thickness(12, 8, 12, 8) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        if (idx > 0) {
          var upBtn = new Button {
            Content = new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.ArrowUp24, FontSize = 12 },
            Width = 24, Height = 24, Padding = new Thickness(0), Margin = new Thickness(0, 0, 2, 0)
          };
          int upIdx = idx;
          upBtn.Click += (s, a) => {
            var tmp = _pipeline.Steps[upIdx];
            _pipeline.Steps[upIdx] = _pipeline.Steps[upIdx - 1];
            _pipeline.Steps[upIdx - 1] = tmp;
            RefreshStepsUI();
          };
          Grid.SetColumn(upBtn, 0);
          header.Children.Add(upBtn);
        }
        if (idx < _pipeline.Steps.Count - 1) {
          var dnBtn = new Button {
            Content = new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.ArrowDown24, FontSize = 12 },
            Width = 24, Height = 24, Padding = new Thickness(0), Margin = new Thickness(0, 0, 2, 0)
          };
          int dnIdx = idx;
          dnBtn.Click += (s, a) => {
            var tmp = _pipeline.Steps[dnIdx];
            _pipeline.Steps[dnIdx] = _pipeline.Steps[dnIdx + 1];
            _pipeline.Steps[dnIdx + 1] = tmp;
            RefreshStepsUI();
          };
          Grid.SetColumn(dnBtn, 1);
          header.Children.Add(dnBtn);
        }

        string label = AutomationStepTypes.GetLabel(step.Type);
        if (!string.IsNullOrEmpty(step.Value)) label += ": " + step.Value;
        if (step.DelayMs > 0) label += " (+" + step.DelayMs + "ms)";
        header.Children.Add(new TextBlock {
          Text = label,
          Foreground = TryFindResource("TextPrimaryBrush") as Brush,
          VerticalAlignment = VerticalAlignment.Center,
          Margin = new Thickness(4, 0, 4, 0)
        });
        Grid.SetColumn(header.Children[header.Children.Count - 1], 2);

        // ── Expand button ──
        var expandBtn = new ToggleButton {
          Width = 24, Height = 24, Padding = new Thickness(0),
          Content = new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.ChevronDown24, FontSize = 12 }
        };

        // ── Body panel (collapsed by default) ──
        var body = new Border {
          Background = TryFindResource("CardBackgroundFillColorDefaultBrush") as Brush,
          CornerRadius = new CornerRadius(0, 0, 6, 6),
          Padding = new Thickness(12, 8, 12, 8),
          Visibility = Visibility.Collapsed
        };
        var bodyGrid = new Grid();
        bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bodyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        bodyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        bodyGrid.Children.Add(new TextBlock {
          Text = Strings.AutomationStepValue + ":", FontSize = 12,
          VerticalAlignment = VerticalAlignment.Center,
          Margin = new Thickness(0, 0, 8, 4)
        });
        var valBox = new TextBox {
          Text = step.Value ?? "", Height = 32, FontSize = 12,
          VerticalContentAlignment = VerticalAlignment.Center
        };
        valBox.TextChanged += (s, a) => { _pipeline.Steps[idx].Value = valBox.Text; };
        Grid.SetColumn(valBox, 1);
        bodyGrid.Children.Add(valBox);

        bodyGrid.Children.Add(new TextBlock {
          Text = Strings.AutomationDelayMs + ":", FontSize = 12,
          VerticalAlignment = VerticalAlignment.Center,
          Margin = new Thickness(0, 4, 8, 0)
        });
        Grid.SetRow(bodyGrid.Children[bodyGrid.Children.Count - 1], 1);
        var delayBox = new TextBox {
          Text = step.DelayMs.ToString(), Height = 32, FontSize = 12,
          VerticalContentAlignment = VerticalAlignment.Center
        };
        delayBox.TextChanged += (s, a) => {
          int.TryParse(delayBox.Text, out int d);
          _pipeline.Steps[idx].DelayMs = d;
        };
        Grid.SetColumn(delayBox, 1);
        Grid.SetRow(delayBox, 1);
        bodyGrid.Children.Add(delayBox);

        body.Child = bodyGrid;

        expandBtn.Checked += (s, a) => {
          body.Visibility = Visibility.Visible;
          ((Wpf.Ui.Controls.SymbolIcon)expandBtn.Content).Symbol = Wpf.Ui.Controls.SymbolRegular.ChevronUp24;
        };
        expandBtn.Unchecked += (s, a) => {
          body.Visibility = Visibility.Collapsed;
          ((Wpf.Ui.Controls.SymbolIcon)expandBtn.Content).Symbol = Wpf.Ui.Controls.SymbolRegular.ChevronDown24;
        };

        Grid.SetColumn(expandBtn, 3);
        header.Children.Add(expandBtn);

        var delBtn = new Button {
          Content = new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.Delete24, FontSize = 12 },
          Width = 24, Height = 24, Padding = new Thickness(0), Margin = new Thickness(2, 0, 0, 0)
        };
        int delIdx = idx;
        delBtn.Click += (s, a) => { _pipeline.Steps.RemoveAt(delIdx); RefreshStepsUI(); };
        Grid.SetColumn(delBtn, 4);
        header.Children.Add(delBtn);

        outerStack.Children.Add(header);
        outerStack.Children.Add(body);
        card.Child = outerStack;
        StepsPanel.Children.Add(card);
      }
    }

    void AddStepBtn_Click(object sender, RoutedEventArgs e) {
      string brush_card = "CardBackgroundFillColorDefaultBrush";
      string brush_border = "BorderSubtleBrush";
      string brush_textSecondary = "TextSecondaryBrush";

      var dialog = new Wpf.Ui.Controls.FluentWindow {
        Title = Strings.AutomationAddStep,
        Width = 480, Height = 440,
        WindowStartupLocation = WindowStartupLocation.CenterOwner,
        Owner = this, ResizeMode = ResizeMode.NoResize,
        ShowInTaskbar = false,
        WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType.Mica,
        ExtendsContentIntoTitleBar = true,
        Background = System.Windows.Media.Brushes.Transparent
      };

      var rootGrid = new Grid();
      rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
      rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
      rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

      // Title
      rootGrid.Children.Add(new Border {
        BorderBrush = TryFindResource(brush_border) as Brush,
        BorderThickness = new Thickness(0, 0, 0, 1),
        Padding = new Thickness(16, 12, 16, 12),
        Child = new TextBlock { Text = Strings.AutomationAddStep, FontSize = 14, FontWeight = FontWeights.SemiBold }
      });

      // Content
      var sv = new ScrollViewer { Margin = new Thickness(12, 8, 12, 0) };
      var cs = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };

      // Step type card
      var typeGrid = new Grid();
      typeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
      typeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
      typeGrid.Children.Add(new TextBlock {
        Text = Strings.AutomationStepType + ":", FontSize = 13,
        VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0)
      });
      var typeCombo = new ComboBox { MinHeight = 36, FontSize = 13, MinWidth = 240 };
      foreach (var st in AutomationStepTypes.All)
        typeCombo.Items.Add(new ComboBoxItem { Content = AutomationStepTypes.GetLabel(st), Tag = st, MinHeight = 28, Padding = new Thickness(4, 2, 4, 2) });
      typeCombo.SelectedIndex = 0;
      Grid.SetColumn(typeCombo, 1);
      typeGrid.Children.Add(typeCombo);
      cs.Children.Add(new Border {
        Background = TryFindResource(brush_card) as Brush,
        CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 8, 12, 8),
        Margin = new Thickness(0, 0, 0, 8), Child = typeGrid
      });

      // Value card
      var valGrid = new Grid();
      valGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
      valGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
      valGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
      valGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
      valGrid.Children.Add(new TextBlock {
        Text = Strings.AutomationStepValue + ":", FontSize = 13,
        VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 6)
      });
      var valBox = new TextBox { Height = 32, FontSize = 13, VerticalContentAlignment = VerticalAlignment.Center };
      Grid.SetColumn(valBox, 1);
      valGrid.Children.Add(valBox);
      var valHint = new TextBlock {
        Text = AutomationStepTypes.GetValueHint("SetPreset"),
        FontSize = 11, Foreground = TryFindResource(brush_textSecondary) as Brush
      };
      Grid.SetRow(valHint, 1);
      Grid.SetColumn(valHint, 1);
      valGrid.Children.Add(valHint);
      typeCombo.SelectionChanged += (s, a) => {
        var item = typeCombo.SelectedItem as ComboBoxItem;
        if (item != null) valHint.Text = AutomationStepTypes.GetValueHint((string)item.Tag);
      };
      cs.Children.Add(new Border {
        Background = TryFindResource(brush_card) as Brush,
        CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 8, 12, 8),
        Margin = new Thickness(0, 0, 0, 8), Child = valGrid
      });

      // Delay card
      var delayGrid = new Grid();
      delayGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
      delayGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
      delayGrid.Children.Add(new TextBlock {
        Text = Strings.AutomationDelayMs + ":", FontSize = 13,
        VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0)
      });
      var delayBox = new TextBox { Text = "0", Height = 32, FontSize = 13, VerticalContentAlignment = VerticalAlignment.Center };
      Grid.SetColumn(delayBox, 1);
      delayGrid.Children.Add(delayBox);
      cs.Children.Add(new Border {
        Background = TryFindResource(brush_card) as Brush,
        CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 8, 12, 8),
        Margin = new Thickness(0, 0, 0, 8), Child = delayGrid
      });

      sv.Content = cs;
      Grid.SetRow(sv, 1);
      rootGrid.Children.Add(sv);

      // Button bar
      var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
      var addBtn = new Button { Content = Strings.AutomationSave, MinWidth = 90, Height = 30, Padding = new Thickness(16, 4, 16, 4), Margin = new Thickness(0, 0, 8, 0) };
      addBtn.Click += (s, a) => {
        var item = typeCombo.SelectedItem as ComboBoxItem;
        if (item == null) return;
        int.TryParse(delayBox.Text, out int delay);
        _pipeline.Steps.Add(new AutomationStep { Type = (string)item.Tag, Value = valBox.Text, DelayMs = delay });
        RefreshStepsUI();
        dialog.Close();
      };
      btnPanel.Children.Add(addBtn);
      var clsBtn = new Button { Content = Strings.AutomationCancel, MinWidth = 90, Height = 30, Padding = new Thickness(16, 4, 16, 4) };
      clsBtn.Click += (s, a) => dialog.Close();
      btnPanel.Children.Add(clsBtn);
      rootGrid.Children.Add(new Border {
        BorderBrush = TryFindResource(brush_border) as Brush,
        BorderThickness = new Thickness(0, 1, 0, 0),
        Padding = new Thickness(16, 8, 16, 8), Child = btnPanel
      });
      Grid.SetRow(rootGrid.Children[rootGrid.Children.Count - 1], 2);

      dialog.Content = rootGrid;
      dialog.ShowDialog();
    }

    // ── Save / Cancel ──

    void SaveBtn_Click(object sender, RoutedEventArgs e) {
      _pipeline.Name = NameBox.Text;
      _pipeline.EnsureTriggers();
      if (_isNew) AutomationService.AddPipeline(_pipeline);
      else AutomationService.Save();
      DialogResult = true;
      Close();
    }

    void CancelBtn_Click(object sender, RoutedEventArgs e) {
      DialogResult = false;
      Close();
    }

    // ── Helpers ──

    static string GetTriggerLabel(string tt) {
      switch (tt) {
        case "ProcessStart": return Strings.AutomationTriggerProcessStart;
        case "ProcessStop": return Strings.AutomationTriggerProcessStop;
        case "PowerAC": return Strings.AutomationTriggerPowerAC;
        case "PowerDC": return Strings.AutomationTriggerPowerDC;
        case "Startup": return Strings.AutomationTriggerStartup;
        case "Resume": return Strings.AutomationTriggerResume;
        case "TimeSchedule": return Strings.AutomationTriggerTimeSchedule;
        case "SessionLock": return Strings.AutomationTriggerSessionLock;
        case "SessionUnlock": return Strings.AutomationTriggerSessionUnlock;
        case "QuickAction": return Strings.AutomationTriggerQuickAction;
        case "BatteryAbove": return Strings.AutomationTriggerBatteryAbove;
        case "BatteryBelow": return Strings.AutomationTriggerBatteryBelow;
        case "CpuTempAbove": return Strings.AutomationTriggerCpuTempAbove;
        case "GpuTempAbove": return Strings.AutomationTriggerGpuTempAbove;
        case "DisplayConnect": return Strings.AutomationTriggerDisplayConnect;
        case "DisplayDisconnect": return Strings.AutomationTriggerDisplayDisconnect;
        default: return tt;
      }
    }

    static string GetTriggerValueHint(string tt) {
      switch (tt) {
        case "ProcessStart":
        case "ProcessStop": return Strings.AutomationProcessHint;
        case "TimeSchedule": return Strings.AutomationTimeHint;
        case "BatteryAbove":
        case "BatteryBelow":
        case "CpuTempAbove":
        case "GpuTempAbove": return Strings.AutomationThresholdHint;
        default: return "";
      }
    }
  }
}
