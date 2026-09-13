// ProcessSelectDialog.cs - 进程选择器弹窗（复用 CoreKeepService.EnumerateProcesses）
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OmenSuperHub.Services.CpuAffinity;

namespace OmenSuperHub.Views {
  public partial class ProcessSelectDialog : Wpf.Ui.Controls.FluentWindow {
    List<string> _allNames;
    public string SelectedProcess { get; private set; }

    public ProcessSelectDialog(Window owner) {
      InitializeComponent();
      Owner = owner;
      // ponytail: 关闭前断开 Owner,避免 owned window 关闭把主窗口误最小化(通用弹窗 bug)
      Utils.WindowHelper.DetachOwnerOnClose(this);
      Loaded += (s, e) => LoadProcesses();
    }

    void LoadProcesses() {
      OkBtn.IsEnabled = false;
      ProcList.ItemsSource = new[] { "..." };
      System.Threading.ThreadPool.QueueUserWorkItem(_ => {
        // R15/BUG-22 同款: 枚举进程抛异常在线程池裸奔会终止进程,兜底并让 UI 恢复可用。
        try {
          var procs = CoreKeepService.EnumerateProcesses();
          var names = procs.Select(p => p.Name).Distinct().OrderBy(n => n, System.StringComparer.OrdinalIgnoreCase).ToList();
          Dispatcher.BeginInvoke(new Action(() => {
            _allNames = names;
            ApplyFilter();
          }));
        } catch (Exception ex) {
          Logger.Error("LoadProcesses: " + ex.Message);
          Dispatcher.BeginInvoke(new Action(() => {
            ProcList.ItemsSource = new string[0];
            EmptyHint.Visibility = Visibility.Visible;
            ProcList.Visibility = Visibility.Collapsed;
          }));
        }
      });
    }

    void ApplyFilter() {
      if (_allNames == null) return;
      string q = (SearchBox.Text ?? "").Trim().ToLowerInvariant();
      var filtered = string.IsNullOrEmpty(q)
        ? _allNames
        : _allNames.Where(n => n.ToLowerInvariant().Contains(q)).ToList();
      ProcList.ItemsSource = filtered;
      EmptyHint.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
      ProcList.Visibility = filtered.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
      if (filtered.Count > 0) ProcList.SelectedIndex = 0;
      // R15/BUG-23: LoadProcesses 置灰 OkBtn 后原代码从不恢复 —— "确定"永远点不动，
      // 唯一出口是双击列表项。筛选结果非空即恢复可用。
      OkBtn.IsEnabled = filtered.Count > 0;
    }

    void SearchBox_TextChanged(object s, TextChangedEventArgs e) => ApplyFilter();

    void ProcList_MouseDoubleClick(object s, MouseButtonEventArgs e) => Confirm();

    void OkBtn_Click(object s, RoutedEventArgs e) => Confirm();

    void CancelBtn_Click(object s, RoutedEventArgs e) { DialogResult = false; Close(); }

    void Confirm() {
      if (ProcList.SelectedItem is string name && !string.IsNullOrEmpty(name)) {
        SelectedProcess = name;
        DialogResult = true;
        Close();
      }
    }
  }
}
