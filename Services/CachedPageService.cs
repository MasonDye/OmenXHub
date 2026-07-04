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
  }
}
