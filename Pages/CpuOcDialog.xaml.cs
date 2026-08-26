using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using OmenSuperHub.Services;

namespace OmenSuperHub.Pages
{
    public partial class CpuOcDialog : Wpf.Ui.Controls.FluentWindow
    {
        private readonly XtuService _xtuService;
        private ObservableCollection<CoreRatioItem> _coreRatioItems;

        public CpuOcDialog()
        {
            InitializeComponent();
            _xtuService = new XtuService();
            _coreRatioItems = new ObservableCollection<CoreRatioItem>();
            CoreRatioItems.ItemsSource = _coreRatioItems;

            Loaded += CpuOcDialog_Loaded;
        }

        private async void CpuOcDialog_Loaded(object sender, RoutedEventArgs e)
        {
            await InitializeXtuAsync();
        }

        // ponytail: 状态条用主题语义笔刷(Wpf.Ui SystemFillColor* 家族)而非硬编码十六进制色 —
        // 后者在暗色主题下是刺眼的亮色块。key 未命中时退化为透明,不崩。
        private void SetStatus(string message, string brushKey)
        {
            StatusText.Text = message;
            StatusBorder.Background = (TryFindResource(brushKey) as Brush) ?? Brushes.Transparent;
        }

        private async Task InitializeXtuAsync()
        {
            try
            {
                SetStatus(Strings.CpuOcStatusDetecting, "SystemFillColorAttentionBackgroundBrush");

                var connected = await _xtuService.InitializeAsync();
                if (!connected)
                {
                    SetStatus(Strings.CpuOcStatusNoService, "SystemFillColorCriticalBackgroundBrush");
                    return;
                }

                var info = await _xtuService.GetOverclockingInfoAsync();
                if (!info.IsOverclockSupported)
                {
                    SetStatus(Strings.CpuOcStatusNotSupported, "SystemFillColorAttentionBackgroundBrush");
                    return;
                }

                var controls = await _xtuService.GetAllControlsAsync();
                // ponytail: 倍频项 = 16 个槽(8P+8E),Id 已按 0..15 排序;control.Name 已含 P-Core/E-Core 前缀。
                var coreRatioControls = controls
                    .Where(c => XtuService.CoreRatioIds.Contains(c.Id))
                    .OrderBy(c => Array.IndexOf(XtuService.CoreRatioIds, c.Id))
                    .ToList();

                foreach (var control in coreRatioControls)
                {
                    _coreRatioItems.Add(new CoreRatioItem
                    {
                        Id = control.Id,
                        Name = control.Name,
                        Value = (double)control.ActiveValue,
                        MinValue = (double)control.MinValue,
                        MaxValue = (double)control.MaxValue
                    });
                }

                var voltageControl = controls.FirstOrDefault(c => c.Id == XtuService.CpuVoltageOffsetId);
                if (voltageControl != null)
                {
                    // 平台真实限值收紧滑块范围(硬件安全:不做超出 XTU 上报范围的输入)
                    VoltageOffsetSlider.Minimum = (double)voltageControl.MinValue;
                    VoltageOffsetSlider.Maximum = (double)voltageControl.MaxValue;
                    VoltageOffsetSlider.Value = (double)voltageControl.ActiveValue;
                    VoltageOffsetNum.Text = voltageControl.ActiveValue.ToString();
                }

                SetStatus(Strings.CpuOcStatusReadyFormat((int)coreRatioControls.Count), "SystemFillColorSuccessBackgroundBrush");
            }
            catch (Exception ex)
            {
                SetStatus(Strings.CpuOcStatusInitFailedPrefix + ex.Message, "SystemFillColorCriticalBackgroundBrush");
            }
        }

        private async void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var coreRatios = new Dictionary<uint, decimal>();
                foreach (var item in _coreRatioItems)
                {
                    coreRatios[item.Id] = (decimal)item.Value;
                }

                var ratioSuccess = await _xtuService.SetCoreRatioAsync(coreRatios);

                // ponytail: 键用 CpuVoltageOffsetId 哨兵(原硬编码 0x1F1 是逆向文档猜错的旧 ID,
                // 与 XtuService.CpuVoltageOffsetId 不一致,导致电压写入发到无效控制项)。
                if (!decimal.TryParse(VoltageOffsetNum.Text, out decimal voltageMv))
                    voltageMv = 0;
                var voltageOffsets = new Dictionary<uint, decimal>
                {
                    { XtuService.CpuVoltageOffsetId, voltageMv }
                };
                var voltageSuccess = await _xtuService.SetVoltageOffsetAsync(voltageOffsets);

                if (ratioSuccess && voltageSuccess)
                {
                    SetStatus(Strings.CpuOcStatusApplied, "SystemFillColorSuccessBackgroundBrush");
                    await Task.Delay(1500);
                    DialogResult = true;
                    Close();
                }
                else
                {
                    SetStatus(Strings.CpuOcStatusPartialFail, "SystemFillColorAttentionBackgroundBrush");
                }
            }
            catch (Exception ex)
            {
                SetStatus(Strings.CpuOcStatusApplyFailedPrefix + ex.Message, "SystemFillColorCriticalBackgroundBrush");
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            _xtuService?.Dispose();
            DialogResult = false;
            Close();
        }
    }

    public class CoreRatioItem : INotifyPropertyChanged
    {
        public uint Id { get; set; }
        public string Name { get; set; }
        public double MinValue { get; set; }
        public double MaxValue { get; set; }

        private double _value;
        public double Value
        {
            get => _value;
            set
            {
                if (_value == value) return;
                _value = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}

