// StorageCleanWindow.xaml.cs - 储存清理弹窗 (参考 Dism++ 空间回收: 扫描→勾选→清理→复扫)
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using OmenSuperHub.Services;
using OmenSuperHub.Utils;

namespace OmenSuperHub.Views {
  public partial class StorageCleanWindow {
    // ponytail: 单例 — 与 HelpWindow/SystemOptimizeWindow 的 ShowInstance 约定一致
    static StorageCleanWindow _window;
    public static void ShowInstance(Window owner) {
      if (_window == null) {
        _window = new StorageCleanWindow();
        _window.Closed += (_, __) => _window = null;
      }
      if (owner != null && owner.IsLoaded) _window.Owner = owner;
      _window.Show();
      _window.Activate();
    }

    class RowVm : INotifyPropertyChanged {
      public CleanItem Item;
      public string Name => Item.Name;
      public string Description => Item.Description;
      bool _sel;
      public bool IsSelected { get => _sel; set { _sel = value; P(nameof(IsSelected)); } }
      string _sizeText = "…";
      public string SizeText { get => _sizeText; set { _sizeText = value; P(nameof(SizeText)); } }
      public event PropertyChangedEventHandler PropertyChanged;
      void P(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    readonly System.Collections.ObjectModel.ObservableCollection<RowVm> _rows = new();

    public StorageCleanWindow() {
      InitializeComponent();
      ItemsList.ItemsSource = _rows;
      // ponytail: Escape 关闭走 KeyDown 事件订阅(FluentWindow 无 OnPreviewKeyDown 可 override),
      // 与 SystemOptimizeWindow.xaml.cs 同款。
      KeyDown += (s, e) => { if (e.Key == Key.Escape) Close(); };
      Loaded += async (_, __) => await Rescan();
    }

    bool _busy;
    void SetBusy(bool on, string status) {
      _busy = on;
      CleanBtn.IsEnabled = !on;
      RescanBtn.IsEnabled = !on;
      SelectAllBox.IsEnabled = !on;
      StatusText.Text = status;
    }

    async Task Rescan() {
      if (_busy) return;
      SetBusy(true, Strings.StorageCleanScanning);
      // 重建行(首次)或刷新 ; CleanItem 是业务对象, RowVm 是 UI 投影
      if (_rows.Count == 0) {
        foreach (var it in DiskCleaner.BuildItems()) {
          var vm = new RowVm { Item = it, IsSelected = false };
          vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(RowVm.IsSelected)) RefreshTotal(); };
          _rows.Add(vm);
        }
      } else {
        foreach (var r in _rows) r.SizeText = "…";
      }
      var snapshot = _rows.Select(r => r.Item).ToList();
      await Task.Run(() => DiskCleaner.Scan(snapshot));
      foreach (var r in _rows) r.SizeText = DiskCleaner.FmtBytes(r.Item.SizeBytes);
      SetBusy(false, "");
      RefreshTotal();
    }

    long TotalSelectedBytes() => _rows.Where(r => r.IsSelected).Sum(r => r.Item.SizeBytes);

    void RefreshTotal() {
      SelectedTotalText.Text = DiskCleaner.FmtBytes(TotalSelectedBytes());
    }

    void SelectAll_Checked(object sender, RoutedEventArgs e) {
      if (_busy) return;
      foreach (var r in _rows) r.IsSelected = true;
    }
    void SelectAll_Unchecked(object sender, RoutedEventArgs e) {
      if (_busy) return;
      foreach (var r in _rows) r.IsSelected = false;
    }

    async void Rescan_Click(object sender, RoutedEventArgs e) => await Rescan();

    async void CleanBtn_Click(object sender, RoutedEventArgs e) {
      if (_busy) return;
      var picked = _rows.Where(r => r.IsSelected).Select(r => r.Item).ToList();
      foreach (var it in picked) it.IsSelected = true;   // ponytail: 同步勾选状态供 Clean() 过滤
      if (picked.Count == 0) { DialogHelper.Info(Strings.StorageCleanNothingSelected, Strings.StorageCleanTitle); return; }
      long total = picked.Sum(it => it.SizeBytes);
      if (!DialogHelper.Confirm(string.Format(Strings.StorageCleanConfirm, DiskCleaner.FmtBytes(total)), Strings.StorageCleanTitle))
        return;
      SetBusy(true, Strings.StorageCleanCleaning);
      long freed = await Task.Run(() => DiskCleaner.Clean(picked));
      SetBusy(false, "");
      DialogHelper.Info(string.Format(Strings.StorageCleanFreed, DiskCleaner.FmtBytes(freed)), Strings.StorageCleanTitle);
      await Rescan();
    }

    void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();

    // ponytail: FluentWindow 不提供可 override 的 OnPreviewKeyDown(编译 CS0115)——
    // 与 SystemOptimizeWindow 一致改用 KeyDown 事件订阅处理 Escape 关闭。
  }
}
