using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OverlayDemo.Core;

namespace OverlayDemo.Widgets;

// 卡片集合 VM：订阅全局遥测数据流刷新
public class VitalsViewModel : INotifyPropertyChanged
{
    private Brush _widgetBackground;
    private Brush _widgetBorderBrush;

    public event PropertyChangedEventHandler PropertyChanged;
    private void Set<T>(ref T f, T v, [CallerMemberName] string n = null)
    {
        if (Equals(f, v)) return;
        f = v;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    public ObservableCollection<CardItem> Cards { get; } = new();

    public Brush WidgetBackground { get => _widgetBackground; set => Set(ref _widgetBackground, value); }
    public Brush WidgetBorderBrush { get => _widgetBorderBrush; set => Set(ref _widgetBorderBrush, value); }

    public VitalsViewModel()
    {
        foreach (var name in new[] { "CPU", "GPU", "RAM", "FPS", "NET" })
            Cards.Add(new CardItem { MenuName = name });
        MockTelemetry.Updated += Refresh;
        Refresh();
    }

    public void Refresh()
    {
        Cards[0].MenuData = $"{(int)MockTelemetry.Cpu}%";
        Cards[0].MenuData2 = $"{MockTelemetry.CpuTemp:F0}\u00b0";
        Cards[0].MenuColorEx = TempBrush(CardLogic.FromTemperature(MockTelemetry.CpuTemp));
        Cards[1].MenuData = $"{(int)MockTelemetry.Gpu}%";
        Cards[1].MenuData2 = $"{MockTelemetry.GpuTemp:F0}\u00b0";
        Cards[1].MenuColorEx = TempBrush(CardLogic.FromTemperature(MockTelemetry.GpuTemp));
        Cards[2].MenuData = $"{MockTelemetry.Ram}%";
        Cards[3].MenuData = $"{MockTelemetry.Fps}";
        Cards[4].MenuData = MockTelemetry.Upload.ToString("F1");
        Cards[4].MenuData2 = MockTelemetry.Download.ToString("F1");
    }

    private static Brush TempBrush(TemperatureState s) => new SolidColorBrush(CardLogic.TemperatureColors[(int)s]);

    // 全局透明度/文字色应用 + 通栏卡重排
    public void ApplySettings(AppConfig cfg)
    {
        var text = new SolidColorBrush(Color.FromRgb((byte)cfg.TextColorR, (byte)cfg.TextColorG, (byte)cfg.TextColorB));
        Cards[0].MenuChecked = cfg.ShowCpu;
        Cards[1].MenuChecked = cfg.ShowGpu;
        Cards[2].MenuChecked = cfg.ShowRam;
        Cards[3].MenuChecked = cfg.ShowFps;
        Cards[4].MenuChecked = cfg.ShowNet;
        foreach (var c in Cards)
        {
            c.MenuColor = text;
            if (c.MenuName != "NET" && c.MenuName != "CPU" && c.MenuName != "GPU")
                c.MenuColorEx = text;
            c.MenuTextTransparency = cfg.TextOpacity;
            c.MenuBgTransparency = cfg.BackgroundOpacity;
        }
        ApplyCardStyles();
        WidgetBackground = new SolidColorBrush(Color.FromRgb(33, 33, 33)) { Opacity = cfg.BackgroundOpacity };
        WidgetBorderBrush = new SolidColorBrush(Color.FromRgb(33, 33, 33)) { Opacity = Math.Min(1.0, cfg.BackgroundOpacity + 0.2) };
    }

    public void ApplyCardStyles()
    {
        var rd = new ResourceDictionary { Source = new Uri("pack://application:,,,/Styles.xaml", UriKind.Absolute) };
        CardLogic.ApplyCardStyles(Cards, (Style)rd["CardBasic"], (Style)rd["CardExtended"], (Style)rd["CardNetwork"]);
    }
}

public partial class VitalsWidget : UserControl
{
    public VitalsWidget()
    {
        InitializeComponent();
        DataContext = new VitalsViewModel();
    }

    public VitalsViewModel ViewModel => (VitalsViewModel)DataContext;
}
