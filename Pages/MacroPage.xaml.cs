// MacroPage.cs - 键盘宏管理页面
// 宏序列的录制、播放、编辑、删除，支持触发键绑定和重复次数配置
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OmenSuperHub.Services;
using OmenSuperHub.Utils;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = System.Windows.Controls.TextBox;
using Button = System.Windows.Controls.Button;

namespace OmenSuperHub.Pages {
  public partial class MacroPage : Page {
    bool _capturingKey;
    Action<uint> _keyCaptureCallback;

    public MacroPage() {
      InitializeComponent();
      Loaded += (s, e) => {
        MacroMasterToggle.IsChecked = ConfigService.MacroEnabled;
        MacroController.SetEnabled(ConfigService.MacroEnabled);
        AddMacroBtn.IsEnabled = ConfigService.MacroEnabled;
        RefreshList();
      };
    }

    void RefreshList() {
      MacroList.Children.Clear();
      NoMacrosText.Visibility = Visibility.Collapsed;
      var macros = MacroService.Macros;
      if (macros == null || macros.Count == 0) {
        NoMacrosText.Visibility = Visibility.Visible;
        return;
      }
      foreach (var m in macros) {
        MacroList.Children.Add(BuildMacroCard(m));
      }
    }

    FrameworkElement BuildMacroCard(MacroSequence m) {
      var card = new Border {
        Margin = new Thickness(0, 0, 0, 8),
        CornerRadius = new CornerRadius(8),
        Background = TryFindResource("CardBackgroundFillColorDefaultBrush") as Brush ?? SystemColors.WindowBrush,
        Padding = new Thickness(12, 10, 12, 10)
      };
      var sp = new StackPanel();

      var header = new Grid();
      header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
      header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
      header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

      var icon = new SymbolIcon { Symbol = SymbolRegular.Keyboard24, FontSize = 16, Margin = new Thickness(0, 0, 8, 0) };
      Grid.SetColumn(icon, 0);
      header.Children.Add(icon);

      var namePanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
      namePanel.Children.Add(new TextBlock {
        Text = m.Name, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, 0, 6, 0), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 200
      });
      string triggerName = MacroController.GetKeyName(m.TriggerKey);
      namePanel.Children.Add(new TextBlock {
        Text = "[" + triggerName + "]", FontSize = 11, VerticalAlignment = VerticalAlignment.Center,
        Foreground = TryFindResource("TextFillColorSecondaryBrush") as Brush ?? Brushes.Gray,
        TextTrimming = TextTrimming.CharacterEllipsis
      });
      Grid.SetColumn(namePanel, 1);
      header.Children.Add(namePanel);

      var toggle = new ToggleSwitch {
        IsChecked = m.Enabled, Tag = m, Margin = new Thickness(8, 0, 0, 0)
      };
      toggle.Checked += (s, e) => { m.Enabled = true; MacroService.Save(); };
      toggle.Unchecked += (s, e) => { m.Enabled = false; MacroService.Save(); };
      Grid.SetColumn(toggle, 2);
      header.Children.Add(toggle);
      sp.Children.Add(header);

      var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(24, 8, 0, 0) };
      buttons.Children.Add(MakeBtn(Strings.MacroRecord, SymbolRegular.Record24, (s, e) => {
        if (MacroController.IsRecording) return;
        MacroController.StartRecording(m, true);
        DialogHelper.Info(Strings.MacroRecordHint, Strings.MacroRecording);
        MacroController.StopRecording();
        MacroService.Save();
        RefreshList();
      }));
      buttons.Children.Add(MakeBtn(Strings.MacroPlayTest, SymbolRegular.Play24, (s, e) => {
        if (!MacroController.IsPlaying)
          MacroController.PlayMacro(m);
      }));
      buttons.Children.Add(MakeBtn(Strings.ButtonEdit, SymbolRegular.Edit24, (s, e) => {
        if (ShowEditDialog(m) == true) { MacroService.Save(); RefreshList(); }
      }));
      buttons.Children.Add(MakeBtn(Strings.MacroDelete, SymbolRegular.Delete24, (s, e) => {
        if (DialogHelper.Confirm(Strings.MacroConfirmDelete, Strings.MacroConfirmDeleteTitle)) {
          MacroService.RemoveMacro(m);
          RefreshList();
        }
      }));
      sp.Children.Add(buttons);
      card.Child = sp;
      return card;
    }

    static Button MakeBtn(string text, Wpf.Ui.Controls.SymbolRegular icon, RoutedEventHandler click) {
      var btn = new Button {
        Margin = new Thickness(0, 0, 6, 0), Height = 28, Padding = new Thickness(8, 0, 8, 0), FontSize = 12
      };
      var inner = new StackPanel { Orientation = Orientation.Horizontal };
      inner.Children.Add(new SymbolIcon { Symbol = icon, FontSize = 12, Margin = new Thickness(0, 0, 4, 0) });
      inner.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
      btn.Content = inner;
      btn.Click += click;
      return btn;
    }

    void AddMacro_Click(object sender, RoutedEventArgs e) {
      var m = new MacroSequence { Name = "New Macro" };
      if (ShowEditDialog(m) == true) {
        MacroService.AddMacro(m);
        MacroService.Save();
      }
      RefreshList();
    }

    bool? ShowEditDialog(MacroSequence m) {
      var win = new FluentWindow {
        Title = Strings.MacroEditTitle, Width = 460, Height = 480,
        WindowStartupLocation = WindowStartupLocation.CenterOwner,
        Owner = Window.GetWindow(this), ResizeMode = ResizeMode.NoResize,
        ShowInTaskbar = false, WindowBackdropType = WindowBackdropType.Mica,
        ExtendsContentIntoTitleBar = true, Background = System.Windows.Media.Brushes.Transparent
      };
      var root = new Grid();
      root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
      root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
      root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

      var titleBar = new Border {
        BorderBrush = TryFindResource("BorderSubtleBrush") as Brush ?? System.Windows.Media.Brushes.Transparent,
        BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(16, 12, 16, 12)
      };
      titleBar.Child = new StackPanel { Orientation = Orientation.Horizontal, Children = {
        new SymbolIcon { Symbol = SymbolRegular.Keyboard24, FontSize = 16, Margin = new Thickness(0, 0, 8, 0) },
        new TextBlock { Text = Strings.MacroEditTitle, FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center }
      }};
      root.Children.Add(titleBar);

      var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(12, 8, 12, 0) };
      var fields = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };

      // Macro Name card
      fields.Children.Add(new Border {
        Background = TryFindResource("CardBackgroundFillColorDefaultBrush") as Brush ?? System.Windows.Media.Brushes.White,
        CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 14, 12, 14), Margin = new Thickness(0, 0, 0, 8),
        Child = new Grid {
          ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) } },
          Children = {
            new TextBlock { Text = Strings.MacroName, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) }
          }
        }
      });
      var nameBox = new TextBox { Text = m.Name, Height = 32, FontSize = 14, VerticalContentAlignment = VerticalAlignment.Center, MinWidth = 200 };
      Grid.SetColumn(nameBox, 1);
      ((Grid)((Border)fields.Children[fields.Children.Count - 1]).Child).Children.Add(nameBox);

      // Trigger Key card
      fields.Children.Add(new Border {
        Background = TryFindResource("CardBackgroundFillColorDefaultBrush") as Brush ?? System.Windows.Media.Brushes.White,
        CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 14, 12, 14), Margin = new Thickness(0, 0, 0, 8),
        Child = new StackPanel { Children = {
          new TextBlock { Text = Strings.MacroTriggerKey, FontSize = 13, Margin = new Thickness(0, 0, 0, 8) }
        } }
      });
      var triggerRow = new StackPanel { Orientation = Orientation.Horizontal };
      var triggerText = new TextBlock {
        Text = MacroController.GetKeyName(m.TriggerKey), VerticalAlignment = VerticalAlignment.Center,
        MinWidth = 100, Margin = new Thickness(0, 0, 8, 0), FontSize = 13,
        Foreground = TryFindResource("TextFillColorSecondaryBrush") as Brush ?? System.Windows.Media.Brushes.Gray
      };
      triggerRow.Children.Add(triggerText);
      var captureBtn = new Button {
        MinWidth = 100, Height = 28, Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(0, 0, 6, 0)
      };
      var captureInner = new StackPanel { Orientation = Orientation.Horizontal };
      captureInner.Children.Add(new SymbolIcon { Symbol = SymbolRegular.Keyboard24, FontSize = 12, Margin = new Thickness(0, 0, 4, 0) });
      captureInner.Children.Add(new TextBlock { Text = Strings.MacroCaptureKey, VerticalAlignment = VerticalAlignment.Center });
      captureBtn.Content = captureInner;
      captureBtn.Click += (s, e) => {
        _capturingKey = true;
        _keyCaptureCallback = (vk) => {
          m.TriggerKey = vk;
          triggerText.Text = MacroController.GetKeyName(vk);
          _capturingKey = false;
        };
      };
      triggerRow.Children.Add(captureBtn);
      var clearBtn = new Button {
        MinWidth = 60, Height = 28, Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(0, 0, 6, 0)
      };
      var clearInner = new StackPanel { Orientation = Orientation.Horizontal };
      clearInner.Children.Add(new SymbolIcon { Symbol = SymbolRegular.Delete24, FontSize = 12, Margin = new Thickness(0, 0, 4, 0) });
      clearInner.Children.Add(new TextBlock { Text = Strings.MacroClearKey, VerticalAlignment = VerticalAlignment.Center });
      clearBtn.Content = clearInner;
      clearBtn.Click += (s, e) => { m.TriggerKey = 0; triggerText.Text = "(none)"; };
      triggerRow.Children.Add(clearBtn);
      var eventsText = new TextBlock {
        Text = Strings.MacroEventsCount(m.Events.Count), VerticalAlignment = VerticalAlignment.Center,
        FontSize = 11, Foreground = TryFindResource("TextFillColorSecondaryBrush") as Brush ?? System.Windows.Media.Brushes.Gray
      };
      triggerRow.Children.Add(eventsText);
      ((StackPanel)((Border)fields.Children[fields.Children.Count - 1]).Child).Children.Add(triggerRow);

      // Repeat Count card
      fields.Children.Add(new Border {
        Background = TryFindResource("CardBackgroundFillColorDefaultBrush") as Brush ?? System.Windows.Media.Brushes.White,
        CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 14, 12, 14), Margin = new Thickness(0, 0, 0, 8),
        Child = new Grid {
          ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) } },
          Children = {
            new TextBlock { Text = Strings.MacroRepeatCount, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) }
          }
        }
      });
      var repeatBox = new TextBox { Text = m.RepeatCount.ToString(), Width = 80, Height = 32, FontSize = 14, VerticalContentAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
      Grid.SetColumn(repeatBox, 1);
      ((Grid)((Border)fields.Children[fields.Children.Count - 1]).Child).Children.Add(repeatBox);

      // Options card
      fields.Children.Add(new Border {
        Background = TryFindResource("CardBackgroundFillColorDefaultBrush") as Brush ?? System.Windows.Media.Brushes.White,
        CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 14, 12, 14), Margin = new Thickness(0, 0, 0, 8),
        Child = new StackPanel { Children = {
          new Grid {
            ColumnDefinitions = { new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }, new ColumnDefinition { Width = GridLength.Auto } },
            Children = {
              new TextBlock { Text = Strings.MacroIgnoreDelays, FontSize = 13, VerticalAlignment = VerticalAlignment.Center }
            }
          }
        } }
      });
      var ignoreToggle = new ToggleSwitch { IsChecked = m.IgnoreDelays, Margin = new Thickness(8, 0, 0, 0) };
      Grid.SetColumn(ignoreToggle, 1);
      ((Grid)((StackPanel)((Border)fields.Children[fields.Children.Count - 1]).Child).Children[0]).Children.Add(ignoreToggle);

      var secondOpt = new Grid {
        Margin = new Thickness(0, 8, 0, 0),
        ColumnDefinitions = { new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }, new ColumnDefinition { Width = GridLength.Auto } }
      };
      secondOpt.Children.Add(new TextBlock { Text = Strings.MacroInterruptOnOtherKey, FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
      var interruptToggle = new ToggleSwitch { IsChecked = m.InterruptOnOtherKey, Margin = new Thickness(8, 0, 0, 0) };
      Grid.SetColumn(interruptToggle, 1);
      secondOpt.Children.Add(interruptToggle);
      ((StackPanel)((Border)fields.Children[fields.Children.Count - 1]).Child).Children.Add(secondOpt);

      scroll.Content = fields;
      Grid.SetRow(scroll, 1);
      root.Children.Add(scroll);

      // Button bar
      var btnBar = new Border {
        BorderBrush = TryFindResource("BorderSubtleBrush") as Brush ?? System.Windows.Media.Brushes.Transparent,
        BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(16, 10, 16, 10)
      };
      var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
      var saveBtn = new Button {
        MinWidth = 80, Height = 30, Margin = new Thickness(0, 0, 8, 0), FontSize = 13
      };
      var saveInner = new StackPanel { Orientation = Orientation.Horizontal };
      saveInner.Children.Add(new SymbolIcon { Symbol = SymbolRegular.Checkmark24, FontSize = 12, Margin = new Thickness(0, 0, 4, 0) });
      saveInner.Children.Add(new TextBlock { Text = Strings.ButtonSave, VerticalAlignment = VerticalAlignment.Center });
      saveBtn.Content = saveInner;
      saveBtn.Click += (s, e) => {
        m.Name = nameBox.Text;
        if (int.TryParse(repeatBox.Text, out int r) && r >= 1 && r <= 10)
          m.RepeatCount = r;
        m.IgnoreDelays = ignoreToggle.IsChecked == true;
        m.InterruptOnOtherKey = interruptToggle.IsChecked == true;
        win.DialogResult = true;
      };
      btnPanel.Children.Add(saveBtn);
      var cancelBtn = new Button { MinWidth = 80, Height = 30, FontSize = 13 };
      var cancelInner = new StackPanel { Orientation = Orientation.Horizontal };
      cancelInner.Children.Add(new SymbolIcon { Symbol = SymbolRegular.Delete24, FontSize = 12, Margin = new Thickness(0, 0, 4, 0) });
      cancelInner.Children.Add(new TextBlock { Text = Strings.ButtonCancel, VerticalAlignment = VerticalAlignment.Center });
      cancelBtn.Content = cancelInner;
      cancelBtn.Click += (s, e) => win.DialogResult = false;
      btnPanel.Children.Add(cancelBtn);
      btnBar.Child = btnPanel;
      Grid.SetRow(btnBar, 2);
      root.Children.Add(btnBar);

      win.Content = root;

      System.Windows.Input.KeyEventHandler handler = null;
      handler = (s, e) => {
        if (_capturingKey && _keyCaptureCallback != null) {
          uint vk = (uint)System.Windows.Input.KeyInterop.VirtualKeyFromKey(e.Key);
          _keyCaptureCallback(vk);
          _keyCaptureCallback = null;
          e.Handled = true;
        }
      };
      win.KeyDown += handler;
      return win.ShowDialog();
    }

    void     MacroMasterToggle_Checked(object sender, RoutedEventArgs e) {
      ConfigService.MacroEnabled = true;
      ConfigService.Save("MacroEnabled");
      MacroController.SetEnabled(true);
      AddMacroBtn.IsEnabled = true;
    }

    void MacroMasterToggle_Unchecked(object sender, RoutedEventArgs e) {
      ConfigService.MacroEnabled = false;
      ConfigService.Save("MacroEnabled");
      MacroController.SetEnabled(false);
      AddMacroBtn.IsEnabled = false;
    }

  }
}
