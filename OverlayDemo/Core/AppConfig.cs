using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Markup;

namespace OverlayDemo.Core;

// 单个 widget 的窗口状态
public class WidgetState
{
    public bool Launched { get; set; }
    public bool Locked { get; set; } = true;      // 钉住
    public double X { get; set; } = -1;
    public double Y { get; set; } = -1;
}

// 应用配置：XamlWriter 序列化持久化，零依赖
public class AppConfig
{
    public int HotkeyIndex { get; set; } = 0;               // 0=Shift+F2 1=Shift+F6 2=Shift+F7 3=Ctrl+Alt+F9
    public int BackgroundTransparency { get; set; } = 100;  // 滑条 0-100，实际不透明度 = (v*0.9+10)/100
    public int TextTransparency { get; set; } = 100;
    public int TextColorR { get; set; } = 238;
    public int TextColorG { get; set; } = 238;
    public int TextColorB { get; set; } = 238;

    public bool ShowCpu { get; set; } = true;
    public bool ShowGpu { get; set; } = true;
    public bool ShowRam { get; set; } = true;
    public bool ShowFps { get; set; } = true;
    public bool ShowNet { get; set; } = true;

    // 老版本单 widget 开关，仅用于配置迁移
    public bool WidgetLaunched { get; set; }

    // widget 顺序：0=Vitals 1=Compact 2=Fan 3=Perf
    // ponytail: XamlWriter 不支持泛型 List<T>，用数组
    public WidgetState[] Widgets { get; set; } = new WidgetState[0];

    public WidgetState State(int index)
    {
        if (Widgets.Length <= index)
        {
            var arr = new WidgetState[index + 1];
            Widgets.CopyTo(arr, 0);
            Widgets = arr;
        }
        if (Widgets[index] == null) Widgets[index] = new WidgetState();
        return Widgets[index];
    }

    // 滑条 0-100 → 不透明度
    public double BackgroundOpacity => (BackgroundTransparency * 0.9 + 10.0) / 100.0;
    public double TextOpacity => (TextTransparency * 0.9 + 10.0) / 100.0;

    private static string ConfigPath
    {
        get
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OverlayDemo");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "config.xaml");
        }
    }

    public static AppConfig Load()
    {
        bool fresh = !File.Exists(ConfigPath);
        AppConfig cfg;
        try
        {
            cfg = fresh ? new AppConfig() : (AppConfig)XamlReader.Parse(File.ReadAllText(ConfigPath));
        }
        catch { cfg = new AppConfig(); fresh = true; }   // ponytail: 损坏配置回默认值即可，无升级需求
        cfg.EnsureDefaults(fresh);
        return cfg;
    }

    // 全新配置默认打开首个 widget；老配置（单 WidgetLaunched 字段）迁移；位置错开
    public void EnsureDefaults(bool fresh = false)
    {
        if (Widgets.Length == 0)
            State(0).Launched = fresh || WidgetLaunched;
        for (int i = 0; i < 4; i++)
        {
            var s = State(i);
            if (s.X < 0) { s.X = 350 + i * 60; s.Y = 100 + i * 180; }
        }
    }

    public void Save()
    {
        File.WriteAllText(ConfigPath, XamlWriter.Save(this));
    }
}
