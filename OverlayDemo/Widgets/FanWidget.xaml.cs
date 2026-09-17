using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Controls;
using System.Windows.Media;
using OverlayDemo.Core;

namespace OverlayDemo.Widgets;

public class FanRow : INotifyPropertyChanged
{
    public string Label { get; set; }
    private string _value = "--";
    private Brush _color = Brushes.White;
    private double _textOpacity = 1.0;
    public string Value { get => _value; set => Set(ref _value, value); }
    public Brush Color { get => _color; set => Set(ref _color, value); }
    public double TextOpacity { get => _textOpacity; set => Set(ref _textOpacity, value); }
    public event PropertyChangedEventHandler PropertyChanged;
    private void Set<T>(ref T f, T v, [CallerMemberName] string n = null)
    {
        if (Equals(f, v)) return;
        f = v; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}

public class FanTempItem : INotifyPropertyChanged
{
    public string Label { get; set; }
    private string _value = "--";
    private Brush _color = Brushes.White;
    private double _textOpacity = 1.0;
    public string Value { get => _value; set => Set(ref _value, value); }
    public Brush Color { get => _color; set => Set(ref _color, value); }
    public double TextOpacity { get => _textOpacity; set => Set(ref _textOpacity, value); }
    public event PropertyChangedEventHandler PropertyChanged;
    private void Set<T>(ref T f, T v, [CallerMemberName] string n = null)
    {
        if (Equals(f, v)) return;
        f = v; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}

public class FanViewModel : INotifyPropertyChanged
{
    private Brush _widgetBackground, _widgetBorderBrush;
    private double _textOpacity = 1.0;

    public ObservableCollection<FanRow> Rows { get; } = new();
    public ObservableCollection<FanTempItem> Temps { get; } = new();
    public Brush WidgetBackground { get => _widgetBackground; set { _widgetBackground = value; Notify("WidgetBackground"); } }
    public Brush WidgetBorderBrush { get => _widgetBorderBrush; set { _widgetBorderBrush = value; Notify("WidgetBorderBrush"); } }
    public double TextOpacity { get => _textOpacity; set { _textOpacity = value; Notify("TextOpacity"); } }
    public event PropertyChangedEventHandler PropertyChanged;
    private void Notify([CallerMemberName] string n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

    public FanViewModel()
    {
        Rows.Add(new FanRow { Label = "CPU Fan" });
        Rows.Add(new FanRow { Label = "GPU Fan" });
        Temps.Add(new FanTempItem { Label = "CPU" });
        Temps.Add(new FanTempItem { Label = "GPU" });
        MockTelemetry.Updated += Refresh;
        Refresh();
    }

    private void Refresh()
    {
        // 转速按百位补零对齐；停转显示 Inactive
        Rows[0].Value = MockTelemetry.FanCpu <= 0 ? "Inactive" : MockTelemetry.FanCpu.ToString().PadRight(4, '0');
        Rows[1].Value = MockTelemetry.FanGpu <= 0 ? "Inactive" : MockTelemetry.FanGpu.ToString().PadRight(4, '0');
        Temps[0].Value = $"{MockTelemetry.CpuTemp:F0}" + "°";
        Temps[0].Color = new SolidColorBrush(CardLogic.TemperatureColors[(int)CardLogic.FromTemperature(MockTelemetry.CpuTemp)]);
        Temps[1].Value = $"{MockTelemetry.GpuTemp:F0}" + "°";
        Temps[1].Color = new SolidColorBrush(CardLogic.TemperatureColors[(int)CardLogic.FromTemperature(MockTelemetry.GpuTemp)]);
    }

    public void ApplySettings(AppConfig cfg)
    {
        var text = new SolidColorBrush(Color.FromRgb((byte)cfg.TextColorR, (byte)cfg.TextColorG, (byte)cfg.TextColorB));
        WidgetBackground = new SolidColorBrush(Color.FromRgb(33, 33, 33)) { Opacity = cfg.BackgroundOpacity };
        WidgetBorderBrush = new SolidColorBrush(Color.FromRgb(33, 33, 33)) { Opacity = Math.Min(1.0, cfg.BackgroundOpacity + 0.2) };
        TextOpacity = cfg.TextOpacity;
        foreach (var r in Rows) { r.Color = text; r.TextOpacity = cfg.TextOpacity; }
        foreach (var t in Temps) t.TextOpacity = cfg.TextOpacity;
    }
}

public partial class FanWidget : UserControl
{
    public FanWidget()
    {
        InitializeComponent();
        DataContext = new FanViewModel();
    }
    public FanViewModel ViewModel => (FanViewModel)DataContext;
}
