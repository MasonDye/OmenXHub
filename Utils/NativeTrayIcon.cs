// NativeTrayIcon.cs - 原生系统托盘图标
// 使用 P/Invoke (Shell_NotifyIconW) 实现托盘图标，通过光标位置轮询合成 MouseEnter/Leave 事件
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace OmenSuperHub.Utils {
  public class NativeTrayIcon : NativeWindow, IDisposable {
    const uint NIM_ADD = 0;
    const uint NIM_MODIFY = 1;
    const uint NIM_DELETE = 2;
    const uint NIM_SETVERSION = 4;
    const uint NIF_MESSAGE = 1;
    const uint NIF_ICON = 2;
    const uint NIF_TIP = 4;
    const uint NIF_INFO = 0x10;
    const uint NIIF_NONE = 0;
    const uint NIIF_INFO = 1;
    const uint NIIF_WARNING = 2;
    const uint NIIF_ERROR = 3;
    const uint WM_USER = 0x0400;
    const uint TRAY_CALLBACK = WM_USER + 1069;
    const uint NIN_POPUPOPEN = 0x0404;
    const uint NIN_POPUPCLOSE = 0x0405;
    const uint WM_MOUSEMOVE = 0x0200;
    const uint WM_LBUTTONUP = 0x0202;
    const uint WM_RBUTTONUP = 0x0205;
    // How long the cursor must rest on the icon before we raise MouseEnter
    const int HoverDelayMs = 120;
    // How long the cursor must stay gone before we raise MouseLeave
    const int LeaveDelayMs = 80;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct NOTIFYICONDATAW {
      public uint cbSize;
      public IntPtr hWnd;
      public uint uID;
      public uint uFlags;
      public uint uCallbackMessage;
      public IntPtr hIcon;
      [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
      public uint dwState;
      public uint dwStateMask;
      [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
      public uint uTimeout;     // union: uTimeout | uVersion
      [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
      public uint dwInfoFlags;
      public Guid guidItem;
      public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern bool Shell_NotifyIconW(uint msg, ref NOTIFYICONDATAW data);

    static uint _nextId;
    readonly uint _id = ++_nextId;
    bool _added;
    bool _visible;
    Icon _icon;
    string _tipText;
    System.Windows.Forms.Timer _retryTimer;

    // ── Hover detection state ───────────────────────────────
    // Windows 11's notification area does NOT reliably deliver
    // NIN_POPUPOPEN/NIN_POPUPCLOSE. WM_MOUSEMOVE, however, is
    // always sent while the cursor is over the icon. We use it
    // (debounced) to synthesize reliable MouseEnter/Leave.
    System.Windows.Forms.Timer _hoverTimer;
    bool _isHovering;
    int _lastMoveTick;          // TickCount of last mouse-move over icon (0 = none seen)
    int _iconX, _iconY;         // last recorded icon position (from Cursor.Position at move)
    int _leaveTick;             // when we first noticed the cursor leave (0 = not leaving)
    int _hoverSeenMoves;        // consecutive moves seen, to avoid fly-by flashes

    public event Action MouseEnter;
    public event Action MouseLeave;
    public event Action Click;
    public event Action RightClick;

    public Icon Icon { get => _icon; set { var old = _icon; _icon = value; old?.Dispose(); Update(); } }

    public void ShowBalloonTip(string title, string text, int timeoutMs, uint iconType = NIIF_INFO) {
      if (!_added) return;
      var data = new NOTIFYICONDATAW {
        cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
        hWnd = Handle,
        uID = _id,
        uFlags = NIF_INFO,
        szInfo = text ?? "",
        szInfoTitle = title ?? "",
        dwInfoFlags = iconType,
        uTimeout = (uint)timeoutMs
      };
      Shell_NotifyIconW(NIM_MODIFY, ref data);
    }

    public void SetIcon(Icon icon) { _icon = icon; Update(); }
    public void SetTip(string tip) { _tipText = tip; Update(); }
    public void Show() { _visible = true; Update(); }
    public void Hide() { _visible = false; Update(); }

    void Update() {
      if (_visible && Handle == IntPtr.Zero)
        CreateHandle(new CreateParams());

      var data = new NOTIFYICONDATAW {
        cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
        hWnd = Handle,
        uID = _id,
        uFlags = NIF_MESSAGE | NIF_TIP,
        uCallbackMessage = TRAY_CALLBACK,
        szTip = _tipText ?? "OMEN X Hub"
      };
      if (_icon != null) {
        data.uFlags |= NIF_ICON;
        data.hIcon = _icon.Handle;
      }
      if (!_visible && _added) {
        Shell_NotifyIconW(NIM_DELETE, ref data);
        _added = false;
        StopRetry();
        return;
      }
      if (!_visible) return;
      if (!_added) {
        if (!Shell_NotifyIconW(NIM_ADD, ref data)) {
          StartRetry();
          return;
        }
        data.uTimeout = 4;
        Shell_NotifyIconW(NIM_SETVERSION, ref data);
        _added = true;
        StopRetry();
      } else {
        Shell_NotifyIconW(NIM_MODIFY, ref data);
      }
    }

    void StartRetry() {
      if (_retryTimer != null) return;
      _retryTimer = new System.Windows.Forms.Timer { Interval = 2000 };
      _retryTimer.Tick += (s, e) => {
        if (!_visible) { StopRetry(); return; }
        IntPtr hIcon;
        try { hIcon = _icon?.Handle ?? IntPtr.Zero; } catch (ObjectDisposedException) { StopRetry(); return; }
        var data = new NOTIFYICONDATAW {
          cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
          hWnd = Handle, uID = _id,
          uFlags = NIF_MESSAGE | NIF_TIP | NIF_ICON,
          uCallbackMessage = TRAY_CALLBACK,
          szTip = _tipText ?? "OMEN X Hub",
          hIcon = hIcon
        };
        if (Shell_NotifyIconW(NIM_ADD, ref data)) {
          data.uTimeout = 4;
          Shell_NotifyIconW(NIM_SETVERSION, ref data);
          _added = true;
          StopRetry();
        }
      };
      _retryTimer.Start();
    }

    void StopRetry() {
      if (_retryTimer != null) {
        _retryTimer.Stop();
        _retryTimer.Dispose();
        _retryTimer = null;
      }
    }

    protected override void WndProc(ref Message m) {
      if (m.Msg == TRAY_CALLBACK) {
        var lo = (uint)m.LParam & 0xFFFF;
        switch (lo) {
          case NIN_POPUPOPEN:
            // Shell-native hover signal. Reliable on some shells, NOT on
            // Win11's notification area — but if it arrives, take it.
            EnsureHoverTimer();
            MarkHoverNow();
            base.WndProc(ref m);
            return;
          case NIN_POPUPCLOSE:
            EnsureHoverTimer();
            _lastMoveTick = 0; // force the leave path on next tick
            base.WndProc(ref m);
            return;
          case WM_MOUSEMOVE:
            // Always delivered while the cursor is over the icon, on every
            // Windows version. This is our PRIMARY, reliable hover source.
            EnsureHoverTimer();
            MarkHoverNow();
            return;
          case WM_LBUTTONUP:
            Click?.Invoke();
            return;
          case WM_RBUTTONUP:
            RightClick?.Invoke();
            return;
        }
      }
      base.WndProc(ref m);
    }

    // ── Hover synthesis helpers ──────────────────────────────
    // We do NOT parse the icon coords from lParam (the layout differs
    // across shell versions and is easy to get wrong). Instead we read
    // Cursor.Position directly at the moment a WM_MOUSEMOVE arrives — that
    // is exactly where the icon is, since the move just happened on it.
    void EnsureHoverTimer() {
      if (_hoverTimer != null) return;
      _hoverTimer = new System.Windows.Forms.Timer { Interval = 40 };
      _hoverTimer.Tick += OnHoverTick;
      _hoverTimer.Start();
    }

    void MarkHoverNow() {
      // Record the icon's screen position from the live cursor. This is the
      // moment the move happened ON the icon, so Cursor.Position IS the icon
      // point (with whatever sub-pixel jitter the move had).
      var cur = System.Windows.Forms.Cursor.Position;
      _iconX = cur.X;
      _iconY = cur.Y;
      _lastMoveTick = Environment.TickCount;
      _hoverSeenMoves = 0; // a fresh move restarts the "presence" counter
    }

    void OnHoverTick(object s, EventArgs e) {
      // KEY INSIGHT: WM_MOUSEMOVE only fires while the cursor *moves*.
      // A still hover (the state we want to show the popup for) does NOT
      // keep streaming moves. So we cannot use idle-time alone. Instead:
      //   - "on icon" = the live Cursor.Position is still within +/-14px
      //     of the icon point we recorded at the last mouse-move.
      //   - entering  = require a couple of consecutive ticks where the
      //     cursor is on the icon (rejects fast fly-bys).
      //   - leaving   = cursor has left the recorded point; close after a
      //     short grace period so micro-movements don't flicker the popup.
      // ponytail: dropped the old "near screen edge" guard — the WM_MOUSEMOVE
      // callback only fires when the cursor crosses the Shell-registered icon,
      // so _iconX/_iconY is always a real icon point (never an in-app UI point).
      // The edge guard was a redundant second filter that caused false-negatives
      // (e.g. multi-monitor, top taskbar, scaled DPI where the icon sits just
      // inside the 100px band) and made hover fail to open the popup.
      if (_lastMoveTick == 0) return; // never seen the cursor on the icon

      var cur = System.Windows.Forms.Cursor.Position;
      bool nearIcon = Math.Abs(cur.X - _iconX) <= 14 && Math.Abs(cur.Y - _iconY) <= 14;

      if (nearIcon) {
        _leaveTick = 0;
        if (!_isHovering) {
          _hoverSeenMoves++;
          if (_hoverSeenMoves >= 2) {   // ~80ms of presence before opening
            _isHovering = true;
            MouseEnter?.Invoke();
          }
        } else {
          // While hovering, keep tracking the icon point so that the user
          // can move the mouse a little without it counting as "left".
          _iconX = cur.X; _iconY = cur.Y;
        }
      } else {
        _hoverSeenMoves = 0;
        if (_isHovering) {
          if (_leaveTick == 0) _leaveTick = Environment.TickCount;
          if (Environment.TickCount - _leaveTick >= LeaveDelayMs) {
            _isHovering = false;
            _leaveTick = 0;
            MouseLeave?.Invoke();
          }
        }
      }
    }

    public void Dispose() {
      var t = _hoverTimer;
      _hoverTimer = null;
      if (t != null) { t.Stop(); t.Dispose(); }
      StopRetry();
      _visible = false;
      if (_added) Update();
      if (Handle != IntPtr.Zero) ReleaseHandle();
    }
  }
}
