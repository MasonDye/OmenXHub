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
    public partial class CpuOcDialog : Window
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

        private void SetStatus(string message, string colorHex)
        {
            StatusText.Text = message;
            StatusBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex));
        }

        private async Task InitializeXtuAsync()
        {
            try
            {
                SetStatus("正在检测超频支持...", "#FFF3CD");

                var connected = await _xtuService.InitializeAsync();
                if (!connected)
                {
                    SetStatus("❌ 无法连接到 XTU 服务,请确保已安装 Intel XTU", "#F8D7DA");
                    return;
                }

                var info = await _xtuService.GetOverclockingInfoAsync();
                if (!info.IsOverclockSupported)
                {
                    SetStatus("⚠️ 当前平台不支持超频", "#FFF3CD");
                    return;
                }

                var controls = await _xtuService.GetAllControlsAsync();
                var coreRatioControls = controls.Where(c => c.Id >= 0x1F0 && c.Id <= 0x1F7).ToList();

                for (uint i = 0; i < info.PhysicalCoreCount; i++)
                {
                    var control = coreRatioControls.FirstOrDefault(c => c.Id == 0x1F0 + i);
                    if (control != null)
                    {
                        _coreRatioItems.Add(new CoreRatioItem
                        {
                            Id = control.Id,
                            Name = $"核心 {i}",
                            Value = (double)control.ActiveValue,
                            MinValue = (double)control.MinValue,
                            MaxValue = (double)control.MaxValue
                        });
                    }
                }

                var voltageControl = controls.FirstOrDefault(c => c.Id == 0x1F1);
                if (voltageControl != null)
                {
                    VoltageOffsetSlider.Value = (double)voltageControl.ActiveValue;
                    VoltageOffsetNum.Text = voltageControl.ActiveValue.ToString();
                }

                SetStatus($"✅ 检测到 {info.PhysicalCoreCount} 个物理核心,超频已解锁", "#D4EDDA");
            }
            catch (Exception ex)
            {
                SetStatus($"❌ 初始化失败: {ex.Message}", "#F8D7DA");
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

                var voltageOffsets = new Dictionary<uint, decimal>
                {
                    { 0x1F1, decimal.Parse(VoltageOffsetNum.Text) }
                };
                var voltageSuccess = await _xtuService.SetVoltageOffsetAsync(voltageOffsets);

                if (ratioSuccess && voltageSuccess)
                {
                    SetStatus("✅ 超频设置已应用", "#D4EDDA");
                    await Task.Delay(1500);
                    DialogResult = true;
                    Close();
                }
                else
                {
                    SetStatus("⚠️ 部分设置失败,请查看日志", "#FFF3CD");
                }
            }
            catch (Exception ex)
            {
                SetStatus($"❌ 应用失败: {ex.Message}", "#F8D7DA");
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

