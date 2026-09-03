using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using OverlayDemo.Core;

namespace OverlayDemo.Windows;

public class RailItem : INotifyPropertyChanged
{
    private bool _shown;
    public string Icon { get; set; }
    public string Title { get; set; }
    public int Index { get; set; }
    public bool Shown
    {
        get => _shown;
        set { _shown = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Shown))); }
    }
    public event PropertyChangedEventHandler PropertyChanged;
}

public partial class SelectorWindow : Window
{
    private readonly AppConfig _cfg;
    private IntPtr _hwnd;
    private bool _loading;
    private bool _created;
    private bool _ready;   // 构造完成前忽略所有 UI 回调（绑定初始化会触发事件）

    public ObservableCollection<RailItem> RailItems { get; } = new();

    public string HotkeyText => _cfg.HotkeyIndex switch
    {
        1 => "Shift\n+F6",
        2 => "Shift\n+F7",
        3 => "Ctrl+Alt\n+F9",
        _ => "Shift\n+F2",
    };

    public event Action<int> WidgetToggleRequested;
    public event Action SettingsChanged;
    public event Action HotkeyChanged;
    public event Action CollapseRequested;

    public bool IsSettingVisible
    {
        get => (bool)GetValue(IsSettingVisibleProperty);
        set => SetValue(IsSettingVisibleProperty, value);
    }
    public static readonly DependencyProperty IsSettingVisibleProperty =
        DependencyProperty.Register(nameof(IsSettingVisible), typeof(bool), typeof(SelectorWindow), new PropertyMetadata(false));

    public IntPtr Handle => _hwnd;

    public SelectorWindow(AppConfig cfg)
    {
        InitializeComponent();
        _cfg = cfg;
        DataContext = this;
        (string icon, string title)[] defs =
        {
            ("\uE7C3", "System Vitals"),
            ("\uE8F1", "System Vitals Compact"),
            ("\uE9D9", "Fan Monitor"),
            ("\uE7E8", "Performance Control"),
        };
        for (int i = 0; i < defs.Length; i++)
            RailItems.Add(new RailItem { Icon = defs[i].icon, Title = defs[i].title, Index = i, Shown = cfg.State(i).Launched });
        InitControls();
    }

    private void InitControls()
    {
        _loading = true;
        HotkeyCombo.Items.Clear();
        HotkeyCombo.Items.Add("Shift + F2");
        HotkeyCombo.Items.Add("Shift + F6");
        HotkeyCombo.Items.Add("Shift + F7");
        HotkeyCombo.Items.Add("Ctrl + Alt + F9");
        HotkeyCombo.SelectedIndex = Math.Max(0, Math.Min(3, _cfg.HotkeyIndex));

        // 9 色文字色板
        var colors = new[]
        {
            Color.FromRgb(238, 238, 238), Color.FromRgb(255, 205, 63), Color.FromRgb(15, 250, 132),
            Color.FromRgb(56, 216, 255), Color.FromRgb(86, 124, 255), Color.FromRgb(184, 120, 255),
            Color.FromRgb(255, 120, 186), Color.FromRgb(249, 53, 15), Color.FromRgb(255, 163, 56),
        };
        for (int i = 0; i < colors.Length; i++)
        {
            var rb = new RadioButton
            {
                Style = (Style)FindResource("ColorSwatch"),
                Background = new SolidColorBrush(colors[i]),
                IsChecked = colors[i].R == _cfg.TextColorR && colors[i].G == _cfg.TextColorG && colors[i].B == _cfg.TextColorB,
            };
            int idx = i;
            rb.Checked += (_, _) => SetTextColor(colors[idx]);
            Swatches.Children.Add(rb);
        }
        _loading = false;
        _ready = true;
        Notify(nameof(HotkeyText));
    }

    private void SetTextColor(Color c)
    {
        if (!_ready || _loading) return;
        _cfg.TextColorR = c.R; _cfg.TextColorG = c.G; _cfg.TextColorB = c.B;
        _cfg.Save();
        SettingsChanged?.Invoke();
    }

    private void Notify([CallerMemberName] string n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

    public event PropertyChangedEventHandler PropertyChanged;

    private void OnSourceInitialized(object sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        int ex = Win32.GetWindowLong(_hwnd, Win32.GWL_EXSTYLE);
        Win32.SetWindowLong(_hwnd, Win32.GWL_EXSTYLE, ex | Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TOOLWINDOW);
    }

    // rail 定位屏幕左下角。SizeToContent 在 Loaded 时 ActualHeight 还没算出来，
    // 定位要在 ContentRendered（布局完成）后再做一次
    private void OnLoaded(object sender, RoutedEventArgs e) => PlaceBottomLeft();

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        PlaceBottomLeft();
        // 滑条初值在布局完成后同步，避免模板未应用时的时序问题
        _loading = true;
        BgSlider.Value = _cfg.BackgroundTransparency;
        TextSlider.Value = _cfg.TextTransparency;
        _loading = false;
    }

    private void PlaceBottomLeft()
    {
        Left = SystemParameters.WorkArea.Left + 12;
        Top = SystemParameters.WorkArea.Bottom - ActualHeight - 12;
    }

    private void EnsureCreated()
    {
        if (_created) return;
        _created = true;
        base.Show();
    }

    public new void Show()
    {
        EnsureCreated();
        Win32.ShowInactiveTopmost(_hwnd);
        SyncRailChecks();
    }

    public new void Hide()
    {
        IsSettingVisible = false;
        Win32.HideWindow(_hwnd);
    }

    public void SyncRailChecks()
    {
        foreach (var it in RailItems)
            it.Shown = _cfg.State(it.Index).Launched;
        Notify(nameof(HotkeyText));
    }

    private void RailItem_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready || _loading) return;
        if ((sender as CheckBox)?.DataContext is RailItem it)
            WidgetToggleRequested?.Invoke(it.Index);
    }

    private void HotkeyButton_Click(object sender, RoutedEventArgs e)
    {
        IsSettingVisible = true;
        Win32.ShowInactiveTopmost(_hwnd);
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e) => IsSettingVisible = !IsSettingVisible;

    private void SettingClose_Click(object sender, RoutedEventArgs e) => IsSettingVisible = false;

    private void CollapseButton_Click(object sender, RoutedEventArgs e) => CollapseRequested?.Invoke();

    private void Window_MouseDown(object sender, MouseButtonEventArgs e) { }

    private void Hotkey_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _loading) return;
        _cfg.HotkeyIndex = HotkeyCombo.SelectedIndex;
        _cfg.Save();
        HotkeyChanged?.Invoke();
        Notify(nameof(HotkeyText));
    }

    private void BgSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready || _loading) return;
        _cfg.BackgroundTransparency = (int)e.NewValue;
        _cfg.Save();
        SettingsChanged?.Invoke();
    }

    private void TextSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready || _loading) return;
        _cfg.TextTransparency = (int)e.NewValue;
        _cfg.Save();
        SettingsChanged?.Invoke();
    }
}
