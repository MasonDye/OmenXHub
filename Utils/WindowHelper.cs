// WindowHelper.cs - 弹窗生命周期辅助
// ponytail: WPF owned window 关闭时会对 owner 做「焦点/激活还给 owner」操作;当弹窗与模态
// 弹窗(DialogHelper ShowDialog)焦点链交错,或 owner 处于托盘隐藏等状态时,关闭 owned window
// 可能把主窗口误最小化(实测,issue: 关闭 GPU 程序弹窗后主界面最小化)。
// 统一解法:所有设置 Owner 的弹窗在创建后调用 DetachOwnerOnClose,关闭前断开 Owner,
// 使关闭成为独立窗口关闭,不影响主窗口状态。
using System.Windows;

namespace OmenSuperHub.Utils {
  internal static class WindowHelper {
    public static void DetachOwnerOnClose(Window w) {
      if (w == null) return;
      w.Closing += (s, e) => { if (w.Owner != null) w.Owner = null; };
    }
  }
}
