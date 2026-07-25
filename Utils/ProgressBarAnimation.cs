// ProgressBarAnimation.cs - 进度条值过渡动画
// 参考 LenovoLegionToolkit.WPF.Behaviors.ProgressBarAnimateBehavior
// ponytail: LLT 用 Microsoft.Xaml.Behaviors.Behavior<ProgressBar>, 本项目未引入该包,
// 改用静态扩展方法 + ConditionalWeakTable 存动画状态, 语义完全一致:
// 250ms 线性 DoubleAnimation, FillBehavior.Stop 保持源值, IsAnimating 防重入。
// Ceiling: 一个 ProgressBar 250ms 内的新值会被丢弃 (与 LLT 一致)。
// 升级路径: 用 Queue<DoubleAnimation> 排队最新值, 但 LLT 也不这么做。
using System;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Animation;

namespace OmenSuperHub {
  public static class ProgressBarAnimation {
    static readonly Duration _duration = new Duration(TimeSpan.FromMilliseconds(250));
    static readonly ConditionalWeakTable<RangeBase, AnimState> _states
      = new ConditionalWeakTable<RangeBase, AnimState>();

    public static void EnableAnimation(this RangeBase bar) {
      // ponytail: CachedPageService 缓存 Page，不销毁它。
      // 用 CWT 做防重入守卫，不依赖 Unloaded 取消订阅，
      // 避免"导航离开→Unloaded 取消→回来不再订阅→动画永久失效"。
      if (_states.TryGetValue(bar, out _)) return;
      _states.Add(bar, new AnimState());
      bar.ValueChanged += OnValueChanged;
    }

    class AnimState {
      public bool IsAnimating;
    }

    static void OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) {
      if (!(sender is RangeBase bar)) return;
      if (!_states.TryGetValue(bar, out var state)) return;
      if (state.IsAnimating) return;
      state.IsAnimating = true;

      var anim = new DoubleAnimation(e.OldValue, e.NewValue, _duration, FillBehavior.Stop);
      anim.Completed += (s, _) => state.IsAnimating = false;
      bar.BeginAnimation(RangeBase.ValueProperty, anim);
      e.Handled = true;
    }
  }
}
