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
        var procs = CoreKeepService.EnumerateProcesses();
        var names = procs.Select(p => p.Name).Distinct().OrderBy(n => n, System.StringComparer.OrdinalIgnoreCase).ToList();
        Dispatcher.BeginInvoke(new Action(() => {
          _allNames = names;
          ApplyFilter();
        }));
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
