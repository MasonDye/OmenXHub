using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
                // ponytail: 核心倍频 ID 非连续(OGH CONTROL_ID 枚举)— 按规范序取前 N 个物理核
                var coreRatioControls = controls
                    .Where(c => XtuService.CoreRatioIds.Contains(c.Id))
                    .OrderBy(c => Array.IndexOf(XtuService.CoreRatioIds, c.Id))
                    .ToList();

                for (uint i = 0; i < info.PhysicalCoreCount && i < coreRatioControls.Count; i++)
                {
                    var control = coreRatioControls[(int)i];
                    _coreRatioItems.Add(new CoreRatioItem
                    {
                        Id = control.Id,
                        Name = Strings.CpuOcCoreNameFormat((int)i),
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

                SetStatus(Strings.CpuOcStatusReadyFormat((int)info.PhysicalCoreCount), "SystemFillColorSuccessBackgroundBrush");
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

    public class CoreRatioItem
    {
        public uint Id { get; set; }
        public string Name { get; set; }
        public double Value { get; set; }
        public double MinValue { get; set; }
        public double MaxValue { get; set; }
    }
}

