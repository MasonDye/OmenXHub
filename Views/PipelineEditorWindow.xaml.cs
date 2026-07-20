// PipelineEditorWindow.xaml.cs - 自动化流水线编辑器
// 卡片式 UI 编辑触发器和步骤，支持展开/折叠、拖拽排序、选择器对话框
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using OmenSuperHub;
using OmenSuperHub.Services;
using OmenSuperHub.Utils;

namespace OmenSuperHub.Views {
  public partial class PipelineEditorWindow : Wpf.Ui.Controls.FluentWindow {
    readonly AutomationPipeline _pipeline;
    readonly bool _isNew;
    readonly bool _isQuickAction;

    static readonly string[] TriggerTypes = {
      "ProcessStart", "ProcessStop", "PowerAC", "PowerDC", "Startup", "Resume",
      "TimeSchedule", "SessionLock", "SessionUnlock", "QuickAction",
      "BatteryAbove", "BatteryBelow", "CpuTempAbove", "GpuTempAbove",
      "DisplayConnect", "DisplayDisconnect", "Hotkey"
    };

    static readonly string[] ThresholdTriggerTypes = {
      "BatteryAbove", "BatteryBelow", "CpuTempAbove", "GpuTempAbove"
    };

    static readonly string[] ValueTriggerTypes = {
      "ProcessStart", "ProcessStop", "TimeSchedule", "Hotkey"
    };

    Func<string> _getStepValue;

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

    static FrameworkElement BuildTriggerValueControl(string type, out Func<string> getValue) {
      var textBox = new TextBox { Height = 32, FontSize = 13, VerticalContentAlignment = VerticalAlignment.Center };
      getValue = () => textBox.Text;

      switch (type) {
        case "TimeSchedule": {
          var sp = new StackPanel { Orientation = Orientation.Horizontal };
          var hr = new ComboBox { Height = 32, FontSize = 13, Width = 70 };
          for (int i = 0; i < 24; i++) hr.Items.Add(i.ToString("D2"));
          hr.SelectedIndex = 8;
          sp.Children.Add(hr);
          sp.Children.Add(new TextBlock { Text = ":", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) });
          var mn = new ComboBox { Height = 32, FontSize = 13, Width = 70 };
          for (int i = 0; i < 60; i++) mn.Items.Add(i.ToString("D2"));
          mn.SelectedIndex = 0;
          sp.Children.Add(mn);
          getValue = () => hr.SelectedItem?.ToString() + ":" + mn.SelectedItem?.ToString();
          return sp;
        }
        case "Hotkey": {
          var sp = new StackPanel { Orientation = Orientation.Horizontal };
          var tb = new TextBox { Height = 32, FontSize = 13, VerticalContentAlignment = VerticalAlignment.Center, Width = 160, IsReadOnly = true };
          tb.Text = "";
          tb.Tag = "点击录制...";
          var btn = new Button { Content = "录制", Height = 32, Margin = new Thickness(4, 0, 0, 0), Padding = new Thickness(8, 2, 8, 2) };
          btn.Click += (s, a) => {
            tb.Text = "按下快捷键...";
            var win = Window.GetWindow((DependencyObject)s);
            if (win == null) return;
            KeyEventHandler handler = null;
            handler = (ks, ke) => {
              if (ke.Key == Key.Enter || ke.Key == Key.Escape || ke.Key == Key.Tab) return;
              // ponytail: skip modifier-only keys, keep listening for the actual key
              if (ke.Key == Key.LeftCtrl || ke.Key == Key.RightCtrl || ke.Key == Key.LeftShift || ke.Key == Key.RightShift ||
                  ke.Key == Key.LeftAlt || ke.Key == Key.RightAlt || ke.Key == Key.LWin || ke.Key == Key.RWin) return;
              var mods = new List<string>();
              if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) mods.Add("Ctrl");
              if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) mods.Add("Shift");
              if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0) mods.Add("Alt");
              if ((Keyboard.Modifiers & ModifierKeys.Windows) != 0) mods.Add("Win");
              mods.Add(KeyToFriendlyName(ke.Key));
              tb.Text = string.Join("+", mods);
              win.PreviewKeyDown -= handler;
              ke.Handled = true;
            };
            win.PreviewKeyDown += handler;
          };
          sp.Children.Add(tb); sp.Children.Add(btn);
          getValue = () => (tb.Text == "按下快捷键..." || string.IsNullOrEmpty(tb.Text)) ? "" : tb.Text;
          return sp;
        }
        case "BatteryAbove":
        case "BatteryBelow":
        case "CpuTempAbove":
        case "GpuTempAbove": {
          var sp = new StackPanel { Orientation = Orientation.Horizontal };
          var numBox = new TextBox { Height = 32, FontSize = 13, VerticalContentAlignment = VerticalAlignment.Center, Width = 80, Text = "80" };
          var decBtn = new Button { Content = "-", Width = 28, Height = 28, Margin = new Thickness(4, 0, 0, 0), Padding = new Thickness(0) };
          var incBtn = new Button { Content = "+", Width = 28, Height = 28, Margin = new Thickness(2, 0, 0, 0), Padding = new Thickness(0) };
          decBtn.Click += (s, a) => { if (int.TryParse(numBox.Text, out int v) && v > 0) numBox.Text = (v - 1).ToString(); };
          incBtn.Click += (s, a) => { if (int.TryParse(numBox.Text, out int v)) numBox.Text = (v + 1).ToString(); };
          sp.Children.Add(numBox); sp.Children.Add(decBtn); sp.Children.Add(incBtn);
          getValue = () => numBox.Text;
          return sp;
        }
        case "ProcessStart":
        case "ProcessStop": {
          var sp = new StackPanel { Orientation = Orientation.Horizontal };
          var tb = new TextBox { Height = 32, FontSize = 13, VerticalContentAlignment = VerticalAlignment.Center, Width = 160, Text = "notepad.exe" };
          var browseBtn = new Button { Content = "浏览...", Height = 32, Margin = new Thickness(4, 0, 0, 0), Padding = new Thickness(8, 0, 8, 0) };
          browseBtn.Click += (s, a) => {
            var ofd = new Microsoft.Win32.OpenFileDialog { Filter = "可执行文件|*.exe|所有文件|*.*", Title = "选择程序" };
            if (ofd.ShowDialog() == true) {
              tb.Text = System.IO.Path.GetFileName(ofd.FileName);
            }
          };
          sp.Children.Add(tb);
          sp.Children.Add(browseBtn);
          getValue = () => tb.Text;
          return sp;
        }
        default:
          return textBox;
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

      rootGrid.Children.Add(new Border {
        BorderBrush = TryFindResource(brush_border) as Brush,
        BorderThickness = new Thickness(0, 0, 0, 1),
        Padding = new Thickness(16, 12, 16, 12),
        Child = new TextBlock { Text = Strings.AutomationAddTrigger, FontSize = 14, FontWeight = FontWeights.SemiBold }
      });

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

      // Value card with dynamic control
      var valGrid = new Grid();
      valGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
      valGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
      valGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
      valGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
      valGrid.Children.Add(new TextBlock {
        Text = Strings.AutomationTriggerValue + ":", FontSize = 13,
        VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 6)
      });

      var valContainer = new ContentPresenter { VerticalAlignment = VerticalAlignment.Center, MinHeight = 32 };
      Grid.SetColumn(valContainer, 1);
      valGrid.Children.Add(valContainer);

      var valHint = new TextBlock {
        Text = GetTriggerValueHint((string)((ComboBoxItem)typeCombo.SelectedItem).Tag),
        FontSize = 11, Foreground = TryFindResource(brush_textSecondary) as Brush
      };
      Grid.SetRow(valHint, 1);
      Grid.SetColumn(valHint, 1);
      valGrid.Children.Add(valHint);

      Func<string> getTriggerValue = () => "";
      typeCombo.SelectionChanged += (s, a) => {
        string tt = (string)((ComboBoxItem)typeCombo.SelectedItem).Tag;
        bool showVal = Array.IndexOf(ValueTriggerTypes, tt) >= 0 || Array.IndexOf(ThresholdTriggerTypes, tt) >= 0;
        valGrid.Visibility = showVal ? Visibility.Visible : Visibility.Collapsed;
        valHint.Text = GetTriggerValueHint(tt);
        valContainer.Content = BuildTriggerValueControl(tt, out var fn);
        getTriggerValue = fn;
      };
      string initTt = (string)((ComboBoxItem)typeCombo.SelectedItem).Tag;
      valGrid.Visibility = Array.IndexOf(ValueTriggerTypes, initTt) >= 0 || Array.IndexOf(ThresholdTriggerTypes, initTt) >= 0
        ? Visibility.Visible : Visibility.Collapsed;
      valContainer.Content = BuildTriggerValueControl(initTt, out var initGet);
      getTriggerValue = initGet;

      var valCard = new Border {
        Background = TryFindResource(brush_card) as Brush,
        CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 8, 12, 8),
        Margin = new Thickness(0, 0, 0, 8), Child = valGrid
      };
      cs.Children.Add(valCard);

      sv.Content = cs;
      Grid.SetRow(sv, 1);
      rootGrid.Children.Add(sv);

      var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
      var addBtn = new Button { Content = Strings.AutomationSave, MinWidth = 90, Height = 30, Padding = new Thickness(16, 4, 16, 4), Margin = new Thickness(0, 0, 8, 0) };
      addBtn.Click += (s, a) => {
        var item = typeCombo.SelectedItem as ComboBoxItem;
        if (item == null) return;
        _pipeline.Triggers.Add(new AutomationTrigger((string)item.Tag, getTriggerValue?.Invoke() ?? ""));
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

    static FrameworkElement BuildStepValueControl(string type, out Func<string> getValue) {
      var textBox = new TextBox { Height = 32, FontSize = 13, VerticalContentAlignment = VerticalAlignment.Center };
      getValue = () => textBox.Text;

      switch (type) {
        case "SetPreset": {
          var cb = new ComboBox { Height = 32, FontSize = 13, IsEditable = true };
          // ponytail: dynamic — populate from PresetManager
          foreach (var (display, key) in PresetManager.EnumerateAllPresets())
            cb.Items.Add(display);
          getValue = () => {
            string t = cb.Text;
            // resolve display name back to preset key
            foreach (var (display, key) in PresetManager.EnumerateAllPresets())
              if (display == t) return key;
            return t; // fallback: raw text as key
          };
          return cb;
        }
        case "SetRefreshRate": {
          var cb = new ComboBox { Height = 32, FontSize = 13, IsEditable = true };
          foreach (var v in new[] { "30", "60", "120", "144", "165", "240", "360" }) cb.Items.Add(v);
          cb.SelectedIndex = 1;
          getValue = () => cb.Text;
          return cb;
        }
        case "SetPowerMode": {
          var cb = new ComboBox { Height = 32, FontSize = 13 };
          cb.Items.Add(new ComboBoxItem { Content = "节能 (0)", Tag = "0" });
          cb.Items.Add(new ComboBoxItem { Content = "平衡 (1)", Tag = "1" });
          cb.Items.Add(new ComboBoxItem { Content = "性能 (2)", Tag = "2" });
          cb.SelectedIndex = 2;
          getValue = () => ((ComboBoxItem)cb.SelectedItem)?.Tag as string ?? "2";
          return cb;
        }
        case "SetMaxFrameRate": {
          var cb = new ComboBox { Height = 32, FontSize = 13, IsEditable = true };
          foreach (var v in new[] { "0", "30", "60", "120", "144", "240" }) cb.Items.Add(v);
          cb.SelectedIndex = 1;
          getValue = () => cb.Text;
          return cb;
        }
        case "SetCpuPower": {
          var cb = new ComboBox { Height = 32, FontSize = 13, IsEditable = true };
          foreach (var v in new[] { "max", "65 W", "55 W", "45 W", "35 W", "25 W", "20 W", "15 W", "10 W" }) cb.Items.Add(v);
          getValue = () => cb.Text;
          return cb;
        }
        case "SetFanMode": {
          var sp = new StackPanel();
          var cb = new ComboBox { Height = 32, FontSize = 13 };
          cb.Items.Add(new ComboBoxItem { Content = "安静模式", Tag = "silent" });
          cb.Items.Add(new ComboBoxItem { Content = "降温模式", Tag = "cool" });
          cb.Items.Add(new ComboBoxItem { Content = "平衡模式", Tag = "balanced" });
          cb.Items.Add(new ComboBoxItem { Content = "导入自定义曲线", Tag = "import" });
          cb.Items.Add(new ComboBoxItem { Content = "手动模式", Tag = "manual" });
          cb.SelectedIndex = 0;
          sp.Children.Add(cb);

          var extraPanel = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
          sp.Children.Add(extraPanel);
          string importedJson = null;

          var manualRow = new StackPanel { Orientation = Orientation.Horizontal, Visibility = Visibility.Collapsed };
          var slider = new Slider { Minimum = 0, Maximum = 100, Value = 50, Width = 140, Height = 28 };
          var pctLbl = new TextBlock { Text = "50%", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), MinWidth = 36 };
          slider.ValueChanged += (s, a) => pctLbl.Text = (int)a.NewValue + "%";
          manualRow.Children.Add(slider);
          manualRow.Children.Add(pctLbl);
          extraPanel.Children.Add(manualRow);

          var importRow = new StackPanel { Orientation = Orientation.Horizontal, Visibility = Visibility.Collapsed };
          var importPath = new TextBox { Height = 32, FontSize = 13, VerticalContentAlignment = VerticalAlignment.Center, Width = 200, IsReadOnly = true };
          var importBtn = new Button { Content = "浏览...", Height = 32, Margin = new Thickness(4, 0, 0, 0), Padding = new Thickness(8, 0, 8, 0) };
          importBtn.Click += (s, a) => {
            var ofd = new OpenFileDialog { Filter = "风扇曲线 JSON|*.json|所有文件|*.*" };
            if (ofd.ShowDialog() == true) {
              try {
                var json = File.ReadAllText(ofd.FileName);
                var result = FanService.ImportCurveFromJson(json);
                if (result != null) {
                  importedJson = json;
                  importPath.Text = System.IO.Path.GetFileName(ofd.FileName);
                } else {
                  DialogHelper.Info("无效的风扇曲线文件", "导入失败");
                }
              } catch { DialogHelper.Info("读取文件失败", "导入失败"); }
            }
          };
          importRow.Children.Add(importPath);
          importRow.Children.Add(importBtn);
          extraPanel.Children.Add(importRow);

          cb.SelectionChanged += (s, a) => {
            var tag = ((ComboBoxItem)cb.SelectedItem)?.Tag as string;
            manualRow.Visibility = tag == "manual" ? Visibility.Visible : Visibility.Collapsed;
            importRow.Visibility = tag == "import" ? Visibility.Visible : Visibility.Collapsed;
          };
          getValue = () => {
            var tag = ((ComboBoxItem)cb.SelectedItem)?.Tag as string ?? "silent";
            if (tag == "manual") return "manual:" + (int)slider.Value;
            if (tag == "import") return "json:" + (importedJson ?? "");
            return tag;
          };
          return sp;
        }
        case "SetTempSensitivity": {
          var cb = new ComboBox { Height = 32, FontSize = 13 };
          foreach (var v in new[] { "realtime", "high", "medium", "low" })
            cb.Items.Add(new ComboBoxItem { Content = v, Tag = v });
          cb.SelectedIndex = 2;
          getValue = () => ((ComboBoxItem)cb.SelectedItem)?.Tag as string ?? "medium";
          return cb;
        }
        case "SetGPUHybridMode": {
          var cb = new ComboBox { Height = 32, FontSize = 13 };
          cb.Items.Add(new ComboBoxItem { Content = "关闭独显", Tag = "disable" });
          cb.Items.Add(new ComboBoxItem { Content = "开启独显", Tag = "enable" });
          cb.SelectedIndex = 0;
          getValue = () => ((ComboBoxItem)cb.SelectedItem)?.Tag as string ?? "disable";
          return cb;
        }
        case "SetBrightness": {
          var sp = new StackPanel { Orientation = Orientation.Horizontal };
          var slider = new Slider { Minimum = 0, Maximum = 100, Value = 80, Width = 140, Height = 28 };
          var lbl = new TextBlock { Text = "80%", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), MinWidth = 36 };
          slider.ValueChanged += (s, a) => lbl.Text = (int)a.NewValue + "%";
          sp.Children.Add(slider); sp.Children.Add(lbl);
          getValue = () => ((int)slider.Value).ToString();
          return sp;
        }
        case "SetMicrophone": {
          var cb = new ComboBox { Height = 32, FontSize = 13 };
          cb.Items.Add(new ComboBoxItem { Content = "静音", Tag = "mute" });
          cb.Items.Add(new ComboBoxItem { Content = "取消静音", Tag = "unmute" });
          cb.SelectedIndex = 0;
          getValue = () => ((ComboBoxItem)cb.SelectedItem)?.Tag as string ?? "mute";
          return cb;
        }
        case "SetWiFi":
        case "SetBluetooth": {
          var cb = new ComboBox { Height = 32, FontSize = 13 };
          cb.Items.Add(new ComboBoxItem { Content = "开启", Tag = "on" });
          cb.Items.Add(new ComboBoxItem { Content = "关闭", Tag = "off" });
          cb.SelectedIndex = 0;
          getValue = () => ((ComboBoxItem)cb.SelectedItem)?.Tag as string ?? "on";
          return cb;
        }
        case "RunProgram":
        case "PlaySound": {
          var sp = new StackPanel { Orientation = Orientation.Horizontal };
          var tb = new TextBox { Height = 32, FontSize = 13, VerticalContentAlignment = VerticalAlignment.Center, Width = 200 };
          var btn = new Button {
            Content = "浏览...", Height = 32, Margin = new Thickness(4, 0, 0, 0),
            Padding = new Thickness(8, 0, 8, 0)
          };
          btn.Click += (s, a) => {
            var ofd = new OpenFileDialog();
            if (type == "PlaySound") ofd.Filter = "WAV 文件|*.wav|所有文件|*.*";
            else ofd.Filter = "可执行文件|*.exe;*.bat;*.cmd|所有文件|*.*";
            if (ofd.ShowDialog() == true) tb.Text = ofd.FileName;
          };
          sp.Children.Add(tb); sp.Children.Add(btn);
          getValue = () => tb.Text;
          return sp;
        }
        case "SetFanCurve": {
          var cb = new ComboBox { Height = 32, FontSize = 13, IsEditable = true };
          foreach (var v in new[] { "silent", "cool", "custom" }) cb.Items.Add(v);
          getValue = () => cb.Text;
          return cb;
        }
        case "SetGpuPower": {
          var cb = new ComboBox { Height = 32, FontSize = 13 };
          cb.Items.Add(new ComboBoxItem { Content = "CTGP开+DB开 (max)", Tag = "max" });
          cb.Items.Add(new ComboBoxItem { Content = "CTGP开+DB关 (med)", Tag = "med" });
          cb.Items.Add(new ComboBoxItem { Content = "CTGP关+DB关 (min)", Tag = "min" });
          cb.SelectedIndex = 0;
          getValue = () => ((ComboBoxItem)cb.SelectedItem)?.Tag as string ?? "max";
          return cb;
        }
        default:
          return textBox;
      }
    }

    void AddStepBtn_Click(object sender, RoutedEventArgs e) {
      string brush_card = "CardBackgroundFillColorDefaultBrush";
      string brush_border = "BorderSubtleBrush";
      string brush_textSecondary = "TextSecondaryBrush";

      var dialog = new Wpf.Ui.Controls.FluentWindow {
        Title = Strings.AutomationAddStep,
        Width = 480, Height = 460,
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

      rootGrid.Children.Add(new Border {
        BorderBrush = TryFindResource(brush_border) as Brush,
        BorderThickness = new Thickness(0, 0, 0, 1),
        Padding = new Thickness(16, 12, 16, 12),
        Child = new TextBlock { Text = Strings.AutomationAddStep, FontSize = 14, FontWeight = FontWeights.SemiBold }
      });

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

      // Value card - content rebuilt on type change
      var valCardGrid = new Grid();
      valCardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
      valCardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
      valCardGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
      valCardGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

      var valLabel = new TextBlock {
        Text = Strings.AutomationStepValue + ":", FontSize = 13,
        VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 6)
      };
      valCardGrid.Children.Add(valLabel);

      _getStepValue = () => "";
      var valContainer = new ContentPresenter { VerticalAlignment = VerticalAlignment.Center, MinHeight = 32 };
      Grid.SetColumn(valContainer, 1);
      valCardGrid.Children.Add(valContainer);

      var valHint = new TextBlock {
        Text = AutomationStepTypes.GetValueHint("SetPreset"),
        FontSize = 11, Foreground = TryFindResource(brush_textSecondary) as Brush
      };
      Grid.SetRow(valHint, 1);
      Grid.SetColumn(valHint, 1);
      valCardGrid.Children.Add(valHint);

      typeCombo.SelectionChanged += (s, a) => {
        var item = typeCombo.SelectedItem as ComboBoxItem;
        if (item == null) return;
        string tt = (string)item.Tag;
        valHint.Text = AutomationStepTypes.GetValueHint(tt);
        valContainer.Content = BuildStepValueControl(tt, out var fn);
        _getStepValue = fn;
      };
      // Init first control
      valContainer.Content = BuildStepValueControl("SetPreset", out var initFn);
      _getStepValue = initFn;

      cs.Children.Add(new Border {
        Background = TryFindResource(brush_card) as Brush,
        CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 8, 12, 8),
        Margin = new Thickness(0, 0, 0, 8), Child = valCardGrid
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

      var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
      var addBtn = new Button { Content = Strings.AutomationSave, MinWidth = 90, Height = 30, Padding = new Thickness(16, 4, 16, 4), Margin = new Thickness(0, 0, 8, 0) };
      addBtn.Click += (s, a) => {
        var item = typeCombo.SelectedItem as ComboBoxItem;
        if (item == null) return;
        int.TryParse(delayBox.Text, out int delay);
        _pipeline.Steps.Add(new AutomationStep { Type = (string)item.Tag, Value = _getStepValue?.Invoke() ?? "", DelayMs = delay });
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
	      else AutomationService.UpdatePipeline(_pipeline);
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
        case "Hotkey": return Strings.AutomationTriggerHotkey;
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
        case "Hotkey": return Strings.AutomationHotkeyHint;
        default: return "";
      }
    }

    static string KeyToFriendlyName(Key k) {
      int n = (int)k;
      if (n >= (int)Key.D0 && n <= (int)Key.D9) return (n - (int)Key.D0).ToString();
      switch (k) {
        case Key.OemPeriod:       return ".";
        case Key.OemComma:        return ",";
        case Key.OemMinus:        return "-";
        case Key.OemPlus:         return "+";
        case Key.OemQuestion:     return "/";
        case Key.OemSemicolon:    return ";";
        case Key.OemQuotes:       return "'";
        case Key.OemOpenBrackets: return "[";
        case Key.OemCloseBrackets:return "]";
        case Key.OemPipe:         return "\\";
        case Key.OemTilde:        return "`";
        default:                  return k.ToString();
      }
    }
  }
}
