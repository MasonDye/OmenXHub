using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Controls;

namespace OmenSuperHub.Windows {
  public class BaseWindow : FluentWindow {
    protected BaseWindow() {
      SnapsToDevicePixels = true;
      ExtendsContentIntoTitleBar = true;
      WindowBackdropType = WindowBackdropType.Mica;
      Background = System.Windows.Media.Brushes.Transparent;
      DpiChanged += BaseWindow_DpiChanged;
    }

    private void BaseWindow_DpiChanged(object sender, DpiChangedEventArgs e) {
      VisualTreeHelper.SetRootDpi(this, e.NewDpi);
    }
  }
}
