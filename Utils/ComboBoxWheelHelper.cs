// ComboBoxWheelHelper.cs - ComboBox 鼠标滚轮支持
// 递归遍历可视化树，为所有 ComboBox 绑定鼠标滚轮事件实现滚轮切换选项
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace OmenSuperHub.Utils {
  public static class ComboBoxWheelHelper {
    public static void Enable(DependencyObject root) {
      WalkAndAttach(root);
    }

    static void WalkAndAttach(DependencyObject parent) {
      int count = VisualTreeHelper.GetChildrenCount(parent);
      for (int i = 0; i < count; i++) {
        var child = VisualTreeHelper.GetChild(parent, i);
        if (child is ComboBox combo)
          combo.PreviewMouseWheel += OnPreviewMouseWheel;
        else
          WalkAndAttach(child);
      }
    }

    static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e) {
      if (sender is ComboBox combo) {
        if (e.Delta > 0) combo.SelectedIndex = Math.Max(0, combo.SelectedIndex - 1);
        else if (combo.Items.Count > 0) combo.SelectedIndex = Math.Min(combo.Items.Count - 1, combo.SelectedIndex + 1);
        e.Handled = true;
      }
    }
  }
}
