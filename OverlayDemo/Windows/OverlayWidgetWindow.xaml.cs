using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using OverlayDemo.Core;

namespace OverlayDemo.Windows;

// 卡片窗口绑定模型
public class WidgetWindowVM : INotifyPropertyChanged
{
    private Visibility _controlBarVisibility = Visibility.Hidden;
    private bool _isControlMenuVisible;
    private bool _isLocked = true;
    private FlowDirection _controlBarFlowDirection = FlowDirection.LeftToRight;
    private HorizontalAlignment _controlMenuAlignment = HorizontalAlignment.Left;

    public event PropertyChangedEventHandler PropertyChanged;
    private void Set<T>(ref T f, T v, [CallerMemberName] string n = null)
    {
        if (Equals(f, v)) return;
        f = v;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    public Visibility ControlBarVisibility { get => _controlBarVisibility; set => Set(ref _controlBarVisibility, value); }
    public bool IsControlMenuVisible { get => _isControlMenuVisible; set => Set(ref _isControlMenuVisible, value); }
    public bool IsLocked { get => _isLocked; set => Set(ref _isLocked, value); }
    public FlowDirection ControlBarFlowDirection { get => _controlBarFlowDirection; set => Set(ref _controlBarFlowDirection, value); }
    public HorizontalAlignment ControlMenuAlignment { get => _controlMenuAlignment; set => Set(ref _controlMenuAlignment, value); }
}

public partial class OverlayWidgetWindow : Window
{
    private readonly WidgetWindowVM _vm = new();
    private readonly AppConfig _cfg;
    private readonly WidgetState _state;
    private readonly DispatcherTimer _saveTimer;     // 拖动结束 200ms 防抖落盘
    private readonly DispatcherTimer _leaveTimer;    // 鼠标离开 500ms 后收控制条
    private IntPtr _hwnd;
    private bool _created;
    private bool _syncingVitals;
    private bool _ready;   // 构造完成前忽略绑定初始化回调

    public event Action<OverlayWidgetWindow> LockChanged;
    public event Action<OverlayWidgetWindow> SelectorRequested;
    public event Action<OverlayWidgetWindow> SettingsRequested;
    public event Action<OverlayWidgetWindow> CloseRequested;

    public WidgetWindowVM VM => _vm;
    public bool IsLocked => _vm.IsLocked;
    public IntPtr Handle => _hwnd;
    public int WidgetIndex { get; }

    public OverlayWidgetWindow(AppConfig cfg, int widgetIndex, WidgetState state, string title, UserControl content, bool vitalsToggles)
    {
        InitializeComponent();
        _cfg = cfg;
        WidgetIndex = widgetIndex;
        _state = state;
        DataContext = _vm;
        _vm.IsLocked = state.Locked;
        TitleText.Text = title;

        if (vitalsToggles)
        {
            VitalsSettings.Visibility = Visibility.Visible;
            VitalsToggles.ItemsSource = new List<VitalsToggle>
            {
                new VitalsToggle("CPU", () => cfg.ShowCpu, v => cfg.ShowCpu = v),
                new VitalsToggle("GPU", () => cfg.ShowGpu, v => cfg.ShowGpu = v),
                new VitalsToggle("RAM", () => cfg.ShowRam, v => cfg.ShowRam = v),
                new VitalsToggle("FPS", () => cfg.ShowFps, v => cfg.ShowFps = v),
                new VitalsToggle("NET", () => cfg.ShowNet, v => cfg.ShowNet = v),
            };
        }

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SavePosition(); };

        _leaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _leaveTimer.Tick += (_, _) =>
        {
            _leaveTimer.Stop();
            if (!_vm.IsControlMenuVisible) _vm.ControlBarVisibility = Visibility.Hidden;
        };

        ContentRegion.Content = content;
        _ready = true;
    }

    public class VitalsToggle : INotifyPropertyChanged
    {
        private readonly Action<bool> _set;
        public string Label { get; }
        public bool Checked
        {
            get => _get();
            set { _set(value); Changed?.Invoke(this, new PropertyChangedEventArgs(nameof(Checked))); }
        }
        private readonly Func<bool> _get;
        public event PropertyChangedEventHandler Changed;
        public VitalsToggle(string label, Func<bool> get, Action<bool> set) { Label = label; _get = get; _set = set; }
        event PropertyChangedEventHandler INotifyPropertyChanged.PropertyChanged
        {
            add => Changed += value;
            remove => Changed -= value;
        }
    }

    // WS_EX_NOACTIVATE + WS_EX_TOOLWINDOW：永不抢焦点、不进 Alt+Tab
    private void OnSourceInitialized(object sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        int ex = Win32.GetWindowLong(_hwnd, Win32.GWL_EXSTYLE);
        Win32.SetWindowLong(_hwnd, Win32.GWL_EXSTYLE, ex | Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TOOLWINDOW);
    }

    // 首次显示走纯 WPF Show()；Win32 SW_HIDE 与 WPF 异步显示存在竞争，
    // 只在窗口显示过之后才用 Win32 切换显隐。初始不显示的窗口由 App 侧延迟创建。
    private void EnsureCreated()
    {
        if (_created) return;
        _created = true;
        Left = _state.X;
        Top = _state.Y;
        base.Show();   // ShowActivated=False，不抢焦点；ContentRendered 后校正出屏
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        double w = ContentRegion.ActualWidth + 29;   // 控制条 24 + 边距
        if (IsOutOfScreen(_state.X, _state.Y, w, ContentRegion.ActualHeight + 10))
            { Left = 350; Top = 100; }
        else { Left = _state.X; Top = _state.Y; }
    }

    private bool IsOutOfScreen(double l, double t, double w, double h) =>
        l < SystemParameters.VirtualScreenLeft || l + w > SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth ||
        t < SystemParameters.VirtualScreenTop || t + h > SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight;

    public void ShowInactiveTopmost()
    {
        EnsureCreated();
        Win32.ShowInactiveTopmost(_hwnd);
        _vm.ControlBarVisibility = Visibility.Hidden;
    }

    public new void Hide()
    {
        if (!_created) return;
        Win32.HideWindow(_hwnd);
        _vm.IsControlMenuVisible = false;
        _vm.ControlBarVisibility = Visibility.Hidden;
    }

    private void Window_MouseEnter(object sender, MouseEventArgs e)
    {
        _leaveTimer.Stop();
        _vm.ControlBarVisibility = Visibility.Visible;
    }

    private void Window_MouseLeave(object sender, MouseEventArgs e)
    {
        _leaveTimer.Stop();
        _leaveTimer.Start();
    }

    private void Window_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
            ClampIntoVirtualScreen();
            _saveTimer.Stop();
            _saveTimer.Start();
        }
    }

    // clamp 进虚拟屏 + 贴左缘时菜单右对齐
    private void ClampIntoVirtualScreen()
    {
        double w = ActualWidth, h = ActualHeight;
        if (Left < SystemParameters.VirtualScreenLeft) Left = SystemParameters.VirtualScreenLeft;
        if (Left + w > SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth)
            Left = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - w;
        if (Top < SystemParameters.VirtualScreenTop) Top = SystemParameters.VirtualScreenTop;
        if (Top + h > SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight)
            Top = SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - h;

        bool nearLeft = Left <= SystemParameters.VirtualScreenLeft + 1;
        _vm.ControlBarFlowDirection = nearLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        _vm.ControlMenuAlignment = nearLeft ? HorizontalAlignment.Right : HorizontalAlignment.Left;
    }

    private void SavePosition()
    {
        _state.X = Left;
        _state.Y = Top;
        _cfg.Save();
    }

    private void Lock_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        _state.Locked = _vm.IsLocked;
        _cfg.Save();
        LockChanged?.Invoke(this);
    }

    private void LaunchMenu_Click(object sender, RoutedEventArgs e)
    {
        _vm.IsControlMenuVisible = !_vm.IsControlMenuVisible;
        ClampIntoVirtualScreen();
        Win32.ShowInactiveTopmost(_hwnd);
    }

    private void LaunchSelector_Click(object sender, RoutedEventArgs e) => SelectorRequested?.Invoke(this);

    // 打开主程序（OmenSuperHub）；未部署时回退为打开选择器
    private void LaunchHub_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string demoDir = AppDomain.CurrentDomain.BaseDirectory;
            string hub = System.IO.Path.GetFullPath(System.IO.Path.Combine(demoDir, @"..\..\..\..\bin\Debug\net481\OmenXHub.exe"));
            string alt = System.IO.Path.GetFullPath(System.IO.Path.Combine(demoDir, @"..\..\..\..\bin\Debug\net481\OmenXHub.exe"));
            string exe = System.IO.File.Exists(hub) ? hub : (System.IO.File.Exists(alt) ? alt : null);
            if (exe != null)
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true });
                return;
            }
        }
        catch { /* ponytail: 找不到主程序就回退选择器 */ }
        SelectorRequested?.Invoke(this);
    }
    private void LaunchSettings_Click(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke(this);

    private void CloseWidget_Click(object sender, RoutedEventArgs e)
    {
        _vm.IsControlMenuVisible = false;
        CloseRequested?.Invoke(this);
    }

    private void VitalsToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready || _syncingVitals) return;
        _cfg.Save();
        VitalsSettingsChanged?.Invoke();
    }

    public event Action VitalsSettingsChanged;

    // 设置面板改了透明度/颜色后，同步菜单里的勾选显示状态
    public void SyncVitalsToggles()
    {
        if (VitalsToggles?.ItemsSource == null) return;
        _syncingVitals = true;
        foreach (VitalsToggle t in VitalsToggles.Items)
        {
            t.Checked = t.Label switch
            {
                "CPU" => _cfg.ShowCpu, "GPU" => _cfg.ShowGpu, "RAM" => _cfg.ShowRam,
                "FPS" => _cfg.ShowFps, _ => _cfg.ShowNet,
            };
        }
        _syncingVitals = false;
    }
}
