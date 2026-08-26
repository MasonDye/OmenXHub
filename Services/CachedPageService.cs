// CachedPageService.cs - 页面实例缓存服务
// 实现 Wpf.Ui.IPageService，缓存 Page 实例避免每次导航重建 XAML 可视树
using System;
using System.Collections.Generic;
using System.Windows;
using Wpf.Ui;

namespace OmenSuperHub.Services {
  public class CachedPageService : IPageService {
    readonly Dictionary<Type, FrameworkElement> _cache = new Dictionary<Type, FrameworkElement>();

    public T GetPage<T>() where T : class {
      var type = typeof(T);
      if (!_cache.TryGetValue(type, out var page)) {
        page = (FrameworkElement)Activator.CreateInstance(type);
        _cache[type] = page;
      }
      return (T)(object)page;
    }

    public FrameworkElement GetPage(Type pageType) {
      if (!_cache.TryGetValue(pageType, out var page)) {
        page = (FrameworkElement)Activator.CreateInstance(pageType);
        _cache[pageType] = page;
      }
      return page;
    }

    // ponytail: 主面板 Hide 时调 ReleaseFrontend 清当前页 Unloaded + ClearJournal + 本 Clear。
    // 字典引用一断,各页已通过其 Unloaded 解订阅/停 timer,GC 可回收。下次 Navigate 重新 ctor+Loaded，
    // 与首次访问体验一致（每页 ctor+Loaded <50ms）。底盘上限：缓存全清,若用户频繁开关主面板会重复
    // 重建各页,重建成本远小于持续驻留的几十 MB XAML 可视树。
    //
    // ponytail: 解耦修复 —— 只清字典引用不够:被缓存的「非当前页」从未挂在 Presenter 上,
    // 其 Unloaded 不会触发,静态订阅(PerfPage.Instance / OnPresetChanged / OnLanguageChanged /
    // BoostService.OnLog / SceneChanged 等)会持续钉住旧实例,阻止 GC。清字典前对每个缓存页
    // 同步触发一次 Unloaded,让各页自己的 Unloaded 清理对称执行(解静态订阅、置 null 静态字段、
    // 关 HID、落未保存改动)。RaiseEvent 同步调用子订阅,不依赖 Presenter 卸载。
    public void Clear() {
      foreach (var page in _cache.Values) {
        try {
          if (page is System.Windows.FrameworkElement fe)
            fe.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.FrameworkElement.UnloadedEvent));
        } catch { /* 页可能已脱离 — 忽略,继续清其余 */ }
      }
      _cache.Clear();
    }
  }
}
