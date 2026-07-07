// DialogHelper.cs - WPF-UI 风格弹窗（替代 System.Windows.MessageBox）
// 使用 FluentWindow + Mica 背景，与自动化/宏编辑器二级菜单一致
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using Wpf.Ui.Controls;
using UiSymbolIcon = Wpf.Ui.Controls.SymbolIcon;
using UiSymbolRegular = Wpf.Ui.Controls.SymbolRegular;

namespace OmenSuperHub.Utils {
  internal class DialogResultWindow : FluentWindow {
    public int Result;
    public DialogResultWindow() {
      WindowStartupLocation = WindowStartupLocation.CenterOwner;
      ShowInTaskbar = false;
      ResizeMode = ResizeMode.NoResize;
      SizeToContent = SizeToContent.WidthAndHeight;
      ExtendsContentIntoTitleBar = true;
      WindowBackdropType = WindowBackdropType.Mica;
      Background = Brushes.Transparent;
      MinWidth = 360;
      MaxWidth = 520;
    }
  }

  internal static class DialogHelper {
    static Window _Owner() {
      if (Application.Current == null) return null;
      for (int i = Application.Current.Windows.Count - 1; i >= 0; i--) {
        var w = Application.Current.Windows[i];
        if (w.IsActive && w.Visibility == Visibility.Visible) return w;
      }
      return Application.Current.MainWindow;
    }

    static SolidColorBrush _Brush(string key, string fallback) {
      var b = Application.Current?.TryFindResource(key) as SolidColorBrush;
      return b ?? new SolidColorBrush((Color)ColorConverter.ConvertFromString(fallback));
    }

    public static void Info(string message, string title = "提示") =>
      _Show(message, title, UiSymbolRegular.Info24, "#60CDFF", false, false);
    public static void Warn(string message, string title = "警告") =>
      _Show(message, title, UiSymbolRegular.Warning24, "#FFB900", false, false);
    public static void Error(string message, string title = "错误") =>
      _Show(message, title, UiSymbolRegular.DismissCircle24, "#FF6B6B", false, false);
    public static bool Confirm(string message, string title = "确认") =>
      _Show(message, title, UiSymbolRegular.QuestionCircle24, "#60CDFF", true, false) == 1;
    public static bool OkCancel(string message, string title = "确认") =>
      _Show(message, title, UiSymbolRegular.QuestionCircle24, "#60CDFF", true, false) == 1;
    /// <summary>返回 0=取消, 1=是, 2=否</summary>
    public static int YesNoCancel(string message, string title = "确认") =>
      (int)_Show(message, title, UiSymbolRegular.QuestionCircle24, "#60CDFF", true, true);

    static int _Show(string message, string title, UiSymbolRegular icon, string iconColor, bool hasCancel, bool yesNoCancel) {
      var owner = _Owner();
      if (owner == null) {
        if (yesNoCancel) {
          var r = System.Windows.MessageBox.Show(message, title, System.Windows.MessageBoxButton.YesNoCancel);
          return r == System.Windows.MessageBoxResult.Yes ? 1 : r == System.Windows.MessageBoxResult.No ? 2 : 0;
        }
        var r2 = System.Windows.MessageBox.Show(message, title, hasCancel ? System.Windows.MessageBoxButton.OKCancel : System.Windows.MessageBoxButton.OK);
        return r2 == System.Windows.MessageBoxResult.OK ? 1 : 0;
      }

      var bg = _Brush("CardBackgroundFillColorDefaultBrush", "#2C2C2C");
      var fg = _Brush("TextPrimaryBrush", "#E0E0E0");
      var borderSubtle = _Brush("BorderSubtleBrush", "#404040");
      int result = 0;
      var root = new StackPanel();

      // ── Title bar ──
      var titleBar = new Border {
        BorderBrush = borderSubtle, BorderThickness = new Thickness(0, 0, 0, 1),
        Padding = new Thickness(16, 12, 16, 12)
      };
      var titlePanel = new StackPanel { Orientation = Orientation.Horizontal };
      titlePanel.Children.Add(new UiSymbolIcon { Symbol = icon, FontSize = 18, Foreground = _Brush("SystemAccentBrushPrimary", iconColor), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
      titlePanel.Children.Add(new System.Windows.Controls.TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Foreground = fg });
      titleBar.Child = titlePanel;
      root.Children.Add(titleBar);

      // ── Content card ──
      var contentCard = new Border {
        Background = _Brush("CardBackgroundFillColorDefaultBrush", "#2C2C2C"),
        CornerRadius = new CornerRadius(8),
        Padding = new Thickness(16, 16, 16, 12),
        Margin = new Thickness(12, 8, 12, 0)
      };
      var contentStack = new StackPanel();
      contentStack.Children.Add(new System.Windows.Controls.TextBlock {
        Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 13,
        Foreground = fg, LineHeight = 20, Margin = new Thickness(0, 0, 0, 16)
      });

      // ── Buttons ──
      var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };

      void AddBtn(string label, int returnVal, bool primary) {
        var b = new System.Windows.Controls.Button {
          Content = label, MinWidth = 80, Height = 32, Margin = new Thickness(6, 0, 0, 0),
          Cursor = Cursors.Hand, FontSize = 13,
          Background = primary ? _Brush("SystemAccentBrushPrimary", "#0078D4") : _Brush("CardBackgroundFillColorSecondaryBrush", "#3A3A3A"),
          Foreground = primary ? Brushes.White : fg,
          BorderThickness = new Thickness(0)
        };
        b.Click += (_, __) => { result = returnVal; }; // close handled via window loop
        btnRow.Children.Add(b);
      }

      AddBtn("确定", 1, true);
      if (yesNoCancel) {
        btnRow.Children.Clear();
        AddBtn("是", 1, true);
        AddBtn("否", 2, false);
        AddBtn("取消", 0, false);
      } else if (hasCancel) {
        AddBtn("取消", 0, false);
      }

      contentStack.Children.Add(btnRow);
      contentCard.Child = contentStack;
      root.Children.Add(contentCard);

      // ── Window ──
      var w = new DialogResultWindow();
      w.Content = root;
      w.Owner = owner;
      w.Title = title;
      w.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { result = 0; w.Close(); } };
      // Wire buttons to close
      foreach (var child in FindVisualChildren<System.Windows.Controls.Button>(root))
        child.Click += (_, __) => w.Close();

      w.ShowDialog();
      return result;
    }

    static System.Collections.Generic.IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject {
      if (parent == null) yield break;
      int count = VisualTreeHelper.GetChildrenCount(parent);
      for (int i = 0; i < count; i++) {
        var child = VisualTreeHelper.GetChild(parent, i);
        if (child is T t) yield return t;
        foreach (var gc in FindVisualChildren<T>(child)) yield return gc;
      }
    }
  }
}
