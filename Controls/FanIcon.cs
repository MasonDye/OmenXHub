// FanIcon.cs - 自定义风扇矢量图标
// 继承 IconElement，用 GeometryGroup + RotateTransform 实现 4 片 90° 对称弯刀叶片
// 自动继承 Foreground 画笔，与 SymbolIcon 行为一致
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Wpf.Ui.Controls;

namespace OmenSuperHub.Controls {
  public class FanIcon : IconElement {
    // ponytail: 单片弯刀叶片（朝上），通过 90° 旋转复制 4 片实现完美对称
    // 视图框 24x24，中心 (12,12)
    private const string BladePath =
      "M12,10.6 C9.6,10.6 7.8,9 7.8,6.4 C7.8,4.2 9.4,3 11.2,3.6 C11.6,6.4 11.8,8.8 12,10.6 Z";

    private static Geometry CreateFanGeometry() {
      var group = new GeometryGroup { FillRule = FillRule.Nonzero };

      // 4 片叶片，每片旋转 90°
      for (int i = 0; i < 4; i++) {
        // ponytail: Geometry.Parse 返回的是冻结对象，必须 Clone 才能设置 Transform
        var blade = Geometry.Parse(BladePath).Clone();
        blade.Transform = new RotateTransform(i * 90, 12, 12);
        group.Children.Add(blade);
      }

      // 中心轴心圆
      var hub = Geometry.Parse("M12,12 m-1.8,0 a1.8,1.8 0 1 0 3.6,0 a1.8,1.8 0 1 0 -3.6,0 Z").Clone();
      group.Children.Add(hub);

      return group;
    }

    protected override UIElement InitializeChildren() {
      var path = new System.Windows.Shapes.Path {
        Data = CreateFanGeometry(),
        Stretch = Stretch.Uniform,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        UseLayoutRounding = true,
      };
      // 绑定 Foreground 到 Path 的 Fill，使其跟随主题色
      path.SetBinding(System.Windows.Shapes.Path.FillProperty,
        new Binding("Foreground") { Source = this });
      // ponytail: IconElement 无 FontSize 属性，用 Width/Height 直接控制尺寸
      path.SetBinding(System.Windows.FrameworkElement.WidthProperty,
        new Binding("IconSize") { Source = this });
      path.SetBinding(System.Windows.FrameworkElement.HeightProperty,
        new Binding("IconSize") { Source = this });
      return path;
    }

    public double IconSize {
      get { return (double)GetValue(IconSizeProperty); }
      set { SetValue(IconSizeProperty, value); }
    }

    public static readonly DependencyProperty IconSizeProperty =
      DependencyProperty.Register("IconSize", typeof(double), typeof(FanIcon),
        new PropertyMetadata(28.0));
  }
}
