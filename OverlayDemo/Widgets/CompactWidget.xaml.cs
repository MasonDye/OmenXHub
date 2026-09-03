using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Controls;
using System.Windows.Media;
using OverlayDemo.Core;

namespace OverlayDemo.Widgets;

public class CompactRow : INotifyPropertyChanged
{
    public string Label { get; set; }
    private string _value = "--";
    private Brush _color = Brushes.White;
    private Brush _labelBrush = Brushes.White;
    private Brush _background = Brushes.Transparent;
    private double _textOpacity = 1.0;
    public string Value { get => _value; set => Set(ref _value, value); }
    public Brush Color { get => _color; set => Set(ref _color, value); }
    public Brush LabelBrush { get => _labelBrush; set => Set(ref _labelBrush, value); }
    public Brush Background { get => _background; set => Set(ref _background, value); }
    public double TextOpacity { get => _textOpacity; set => Set(ref _textOpacity, value); }
    public event PropertyChangedEventHandler PropertyChanged;
    private void Set<T>(ref T f, T v, [CallerMemberName] string n = null)
    {
        if (Equals(f, v)) return;
        f = v; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}

public class CompactViewModel : INotifyPropertyChanged
{
    public ObservableCollection<CompactRow> Rows { get; } = new();
    public event PropertyChangedEventHandler PropertyChanged;
    private void Notify([CallerMemberName] string n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

    public CompactViewModel()
    {
        foreach (var l in new[] { "CPU", "GPU", "RAM", "FPS", "NET" })
            Rows.Add(new CompactRow { Label = l });
        MockTelemetry.Updated += Refresh;
        Refresh();
    }

    private void Refresh()
    {
        Rows[0].Value = $"{(int)MockTelemetry.Cpu}% {MockTelemetry.CpuTemp:F0}" + "°";
        Rows[0].Color = TempBrush(CardLogic.FromTemperature(MockTelemetry.CpuTemp));
        Rows[1].Value = $"{(int)MockTelemetry.Gpu}% {MockTelemetry.GpuTemp:F0}" + "°";
        Rows[1].Color = TempBrush(CardLogic.FromTemperature(MockTelemetry.GpuTemp));
        Rows[2].Value = $"{MockTelemetry.Ram}%";
        Rows[3].Value = $"{MockTelemetry.Fps}";
        Rows[4].Value = $"{MockTelemetry.Upload:F1} / {MockTelemetry.Download:F1} Mbps";
    }

    private static Brush TempBrush(TemperatureState s) => new SolidColorBrush(CardLogic.TemperatureColors[(int)s]);

    public void ApplySettings(AppConfig cfg)
    {
        var text = new SolidColorBrush(Color.FromRgb((byte)cfg.TextColorR, (byte)cfg.TextColorG, (byte)cfg.TextColorB));
        var bg = new SolidColorBrush(Color.FromRgb(50, 50, 50)) { Opacity = cfg.BackgroundOpacity };
        foreach (var r in Rows)
        {
            r.TextOpacity = cfg.TextOpacity;
            r.Background = bg;
            r.LabelBrush = text;
            if (r != Rows[0] && r != Rows[1]) r.Color = text;   // 温度行保持温度色
        }
    }
}

public partial class CompactWidget : UserControl
{
    public CompactWidget()
    {
        InitializeComponent();
        DataContext = new CompactViewModel();
    }
    public CompactViewModel ViewModel => (CompactViewModel)DataContext;
}
