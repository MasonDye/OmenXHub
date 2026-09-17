using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OverlayDemo.Core;

namespace OverlayDemo.Widgets;

public class PerfMode : INotifyPropertyChanged
{
    public string Name { get; set; }
    public string Icon { get; set; }
    private bool _checked;
    public bool Checked { get => _checked; set { _checked = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Checked))); } }
    public event PropertyChangedEventHandler PropertyChanged;
}

// 性能模式选择 VM：模式按钮 RadioButton 组互斥（演示层，选中态不落盘）
public class PerfViewModel : INotifyPropertyChanged
{
    private Brush _widgetBackground, _widgetBorderBrush, _textBrush = Brushes.White;
    private double _textOpacity = 1.0;
    private string _cpuText = "--", _gpuText = "--";
    private Brush _cpuBrush = Brushes.White, _gpuBrush = Brushes.White;

    public ObservableCollection<PerfMode> Modes { get; } = new()
    {
        new PerfMode { Name = "ECO", Icon = "\uE7E8" },
        new PerfMode { Name = "默认", Icon = "\uE7C3" },
        new PerfMode { Name = "性能", Icon = "\uE945", Checked = true },
        new PerfMode { Name = "极致", Icon = "\uE785" },
    };

    public Brush WidgetBackground { get => _widgetBackground; set { _widgetBackground = value; Notify("WidgetBackground"); } }
    public Brush WidgetBorderBrush { get => _widgetBorderBrush; set { _widgetBorderBrush = value; Notify("WidgetBorderBrush"); } }
    public Brush TextBrush { get => _textBrush; set { _textBrush = value; Notify("TextBrush"); } }
    public double TextOpacity { get => _textOpacity; set { _textOpacity = value; Notify("TextOpacity"); } }
    public string CpuText { get => _cpuText; set { _cpuText = value; Notify("CpuText"); } }
    public string GpuText { get => _gpuText; set { _gpuText = value; Notify("GpuText"); } }
    public Brush CpuBrush { get => _cpuBrush; set { _cpuBrush = value; Notify("CpuBrush"); } }
    public Brush GpuBrush { get => _gpuBrush; set { _gpuBrush = value; Notify("GpuBrush"); } }

    public event PropertyChangedEventHandler PropertyChanged;
    private void Notify([CallerMemberName] string n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

    public PerfViewModel()
    {
        MockTelemetry.Updated += Refresh;
        Refresh();
    }

    private void Refresh()
    {
        CpuText = $"{(int)MockTelemetry.Cpu}% {MockTelemetry.CpuTemp:F0}" + "°";
        CpuBrush = new SolidColorBrush(CardLogic.TemperatureColors[(int)CardLogic.FromTemperature(MockTelemetry.CpuTemp)]);
        GpuText = $"{(int)MockTelemetry.Gpu}% {MockTelemetry.GpuTemp:F0}" + "°";
        GpuBrush = new SolidColorBrush(CardLogic.TemperatureColors[(int)CardLogic.FromTemperature(MockTelemetry.GpuTemp)]);
    }

    public void ApplySettings(AppConfig cfg)
    {
        WidgetBackground = new SolidColorBrush(Color.FromRgb(33, 33, 33)) { Opacity = cfg.BackgroundOpacity };
        WidgetBorderBrush = new SolidColorBrush(Color.FromRgb(33, 33, 33)) { Opacity = Math.Min(1.0, cfg.BackgroundOpacity + 0.2) };
        TextBrush = new SolidColorBrush(Color.FromRgb((byte)cfg.TextColorR, (byte)cfg.TextColorG, (byte)cfg.TextColorB));
        TextOpacity = cfg.TextOpacity;
    }
}

public partial class PerfWidget : UserControl
{
    public PerfWidget()
    {
        InitializeComponent();
        DataContext = new PerfViewModel();
    }
    public PerfViewModel ViewModel => (PerfViewModel)DataContext;
}
