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
        page = (FrameworkElement)CreateInstanceSafe(type);
        _cache[type] = page;
      }
      return (T)(object)page;
    }

    public FrameworkElement GetPage(Type pageType) {
      if (!_cache.TryGetValue(pageType, out var page)) {
        page = (FrameworkElement)CreateInstanceSafe(pageType);
        _cache[pageType] = page;
      }
      return page;
    }

    // ponytail: Activator.CreateInstance 在 XAML 反序列化失败时会直接抛 XamlParseException,
    // NavigationView 静默吞掉后会表现为"侧栏点击完全无响应"。一次性诊断：捕获并把异常文本
    // 弹给用户,告诉他真实绑不动的地方。确认根因后删掉这个 try/catch。
    static object CreateInstanceSafe(Type type) {
      try {
        return Activator.CreateInstance(type);
      } catch (Exception ex) {
        var msg = "页面实例化失败: " + type.Name +
                  "\n\n异常类型: " + (ex is System.Windows.Markup.XamlParseException pe ? "XamlParseException" : ex.GetType().Name) +
                  "\n消息: " + ex.Message +
                  "\n\nInnerException: " + (ex.InnerException?.Message ?? "(null)") +
                  "\n\nStackTrace:\n" + ex.StackTrace;
        try { System.Windows.MessageBox.Show(msg, "OmenXHub 诊断", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error); }
        catch { }
        throw;
      }
    }
  }
}
