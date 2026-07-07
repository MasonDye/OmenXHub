// DialogHelper.cs - WPF-UI 风格弹窗（替代 System.Windows.MessageBox）
// 外观与程序主题一致，支持信息/警告/错误/确认
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using Wpf.Ui.Controls;
using UiSymbolIcon = Wpf.Ui.Controls.SymbolIcon;
using UiSymbolRegular = Wpf.Ui.Controls.SymbolRegular;

namespace OmenSuperHub.Utils {
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
      _Show(message, title, UiSymbolRegular.Info24, "#60CDFF", false);
    public static void Warn(string message, string title = "警告") =>
      _Show(message, title, UiSymbolRegular.Warning24, "#FFB900", false);
    public static void Error(string message, string title = "错误") =>
      _Show(message, title, UiSymbolRegular.DismissCircle24, "#FF6B6B", false);
    public static bool Confirm(string message, string title = "确认") =>
      _Show(message, title, UiSymbolRegular.QuestionCircle24, "#60CDFF", true);
    public static bool OkCancel(string message, string title = "确认") =>
      _Show(message, title, UiSymbolRegular.QuestionCircle24, "#60CDFF", true);

    static bool _Show(string message, string title, UiSymbolRegular icon, string iconColor, bool hasCancel) {
      var owner = _Owner();
      if (owner == null) {
        var r = System.Windows.MessageBox.Show(message, title, hasCancel ? System.Windows.MessageBoxButton.OKCancel : System.Windows.MessageBoxButton.OK);
        return r == System.Windows.MessageBoxResult.OK;
      }

      var bg = _Brush("CardBackgroundFillColorDefaultBrush", "#2C2C2C");
      var fg = _Brush("TextPrimaryBrush", "#E0E0E0");

      var result = false;
      var border = new Border { Padding = new Thickness(20) };
      var root = new StackPanel { MaxWidth = 480 };

      // Header
      var h = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 0, 0, 0) };
      h.Children.Add(new UiSymbolIcon { Symbol = icon, FontSize = 22, Foreground = _Brush("SystemAccentBrushPrimary", iconColor), Margin = new Thickness(0, 0, 10, 0) });
      h.Children.Add(new System.Windows.Controls.TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Foreground = fg });
      root.Children.Add(h);

      // Message
      root.Children.Add(new System.Windows.Controls.TextBlock {
        Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 13,
        Margin = new Thickness(0, 12, 0, 16),
        Foreground = fg, LineHeight = 20
      });

      // Buttons
      var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };

      var okBtn = new System.Windows.Controls.Button {
        Content = "确定", MinWidth = 80, Height = 32, Margin = new Thickness(6, 0, 0, 0),
        Cursor = Cursors.Hand, FontSize = 13,
        Background = _Brush("SystemAccentBrushPrimary", "#0078D4"),
        Foreground = Brushes.White, BorderThickness = new Thickness(0)
      };
      okBtn.Click += (_, __) => { result = true; border.IsEnabled = false; };
      btnRow.Children.Add(okBtn);

      if (hasCancel) {
        var noBtn = new System.Windows.Controls.Button {
          Content = "取消", MinWidth = 80, Height = 32, Margin = new Thickness(8, 0, 0, 0),
          Cursor = Cursors.Hand, FontSize = 13,
          Background = _Brush("CardBackgroundFillColorSecondaryBrush", "#3A3A3A"),
          Foreground = fg, BorderThickness = new Thickness(0)
        };
        noBtn.Click += (_, __) => { border.IsEnabled = false; };
        btnRow.Children.Add(noBtn);
      }

      root.Children.Add(btnRow);
      border.Child = root;
      border.Background = bg;
      border.BorderBrush = _Brush("CardBorderBrush", "#404040");
      border.BorderThickness = new Thickness(1);
      border.CornerRadius = new CornerRadius(10);
      border.Effect = new System.Windows.Media.Effects.DropShadowEffect {
        BlurRadius = 24, Opacity = 0.35, ShadowDepth = 4, Color = Colors.Black
      };

      var w = new Window {
        Title = title,
        WindowStyle = WindowStyle.SingleBorderWindow,
        ResizeMode = ResizeMode.NoResize,
        SizeToContent = SizeToContent.WidthAndHeight,
        WindowStartupLocation = WindowStartupLocation.CenterOwner,
        Owner = owner, ShowInTaskbar = false, Topmost = true,
        Background = bg,
        Content = border
      };
      w.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { w.Close(); } };
      border.IsEnabledChanged += (_, __) => {
        if (!border.IsEnabled)
          System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() => w.Close()));
      };
      w.ShowDialog();
      return result;
    }
  }
}
