// MacroController.cs - 键盘宏录制与回放引擎
// 使用低级键盘/鼠标钩子 (WH_KEYBOARD_LL/WH_MOUSE_LL) 实现宏录制和回放
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace OmenSuperHub.Services {
  internal static class MacroController {
    const int WH_KEYBOARD_LL = 13;
    const int WH_MOUSE_LL = 14;
    const uint MAGIC_NUMBER = 1337;
    const int WM_KEYDOWN = 0x0100;
    const int WM_KEYUP = 0x0101;
    const int WM_SYSKEYDOWN = 0x0104;
    const int WM_SYSKEYUP = 0x0105;
    const int WM_LBUTTONDOWN = 0x0201;
    const int WM_LBUTTONUP = 0x0202;
    const int WM_RBUTTONDOWN = 0x0204;
    const int WM_RBUTTONUP = 0x0205;
    const int WM_MBUTTONDOWN = 0x0207;
    const int WM_MBUTTONUP = 0x0208;
    const int WM_MOUSEWHEEL = 0x020A;
    const int WM_MOUSEHWHEEL = 0x020E;

    static IntPtr _kbHookId = IntPtr.Zero;
    static IntPtr _mouseHookId = IntPtr.Zero;
    static HookProc _kbProc;
    static HookProc _mouseProc;
    static bool _enabled = true;
    static bool _recording;
    static bool _playing;
    static MacroSequence _recordingTarget;
    static bool _captureMouse;
    static readonly HashSet<uint> _pressedKeys = new HashSet<uint>();
    static DateTime _lastRecordedEventTime;
    static CancellationTokenSource _playCts;

    public static bool IsRecording => _recording;
    public static bool IsPlaying => _playing;

    public static void Start() {
      _kbProc = LowLevelKeyboardProc;
      _mouseProc = LowLevelMouseProc;
      _kbHookId = SetWindowsHookEx(WH_KEYBOARD_LL, _kbProc, GetModuleHandle(null), 0);
      if (_kbHookId == IntPtr.Zero)
        Logger.Error("MacroController: Failed to install keyboard hook");
      else
        Logger.Info("MacroController: Keyboard hook installed");
    }

    public static void Stop() {
      if (_kbHookId != IntPtr.Zero) { UnhookWindowsHookEx(_kbHookId); _kbHookId = IntPtr.Zero; }
      if (_mouseHookId != IntPtr.Zero) { UnhookWindowsHookEx(_mouseHookId); _mouseHookId = IntPtr.Zero; }
      StopRecording();
      _playCts?.Cancel();
      _playCts?.Dispose();
      _playCts = null;
    }

    public static void SetEnabled(bool enabled) { _enabled = enabled; }

    public static void StartRecording(MacroSequence target, bool captureMouse) {
      if (_recording) return;
      _recordingTarget = target;
      _captureMouse = captureMouse;
      _recordingTarget.Events.Clear();
      _pressedKeys.Clear();
      _lastRecordedEventTime = DateTime.Now;
      _recording = true;
      if (captureMouse && _mouseHookId == IntPtr.Zero) {
        _mouseHookId = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, GetModuleHandle(null), 0);
      }
      Logger.Info("MacroController: Recording started");
    }

    public static void StopRecording() {
      if (!_recording) return;
      _recording = false;
      if (_mouseHookId != IntPtr.Zero) { UnhookWindowsHookEx(_mouseHookId); _mouseHookId = IntPtr.Zero; }
      _recordingTarget = null;
      Logger.Info("MacroController: Recording stopped");
    }

    public static void PlayMacro(MacroSequence macro) {
      if (_playing || macro == null || macro.Events.Count == 0) return;
      _playing = true;
      _playCts = new CancellationTokenSource();
      var token = _playCts.Token;
      Task.Run(() => {
        try {
          for (int r = 0; r < macro.RepeatCount; r++) {
            if (token.IsCancellationRequested) break;
            PlayEvents(macro, token);
          }
        } catch (Exception ex) {
          Logger.Error("MacroController.PlayMacro error: " + ex.Message);
        } finally {
          _playing = false;
        }
      }, token);
    }

    public static void CancelPlayback() {
      _playCts?.Cancel();
    }

    static async System.Threading.Tasks.Task PlayEvents(MacroSequence macro, CancellationToken token) {
      foreach (var evt in macro.Events) {
        if (token.IsCancellationRequested) break;
        int delay = macro.IgnoreDelays ? 0 : evt.DelayMs;
        if (delay > 0) await System.Threading.Tasks.Task.Delay(delay, token);
        if (evt.Source == MacroSource.Keyboard) {
          uint scanCode = MapVirtualKey((int)evt.Key, 0);
          uint flags = 0;
          if (evt.Key == 0xA0 || evt.Key == 0xA1 || evt.Key == 0xA2 || evt.Key == 0xA3 ||
              evt.Key == 0x5B || evt.Key == 0x5C) {
            flags = KEYEVENTF_EXTENDEDKEY;
          }
          if (evt.Direction == MacroDirection.Down) {
            keybd_event((byte)evt.Key, (byte)scanCode, flags, new IntPtr(MAGIC_NUMBER));
          } else if (evt.Direction == MacroDirection.Up) {
            keybd_event((byte)evt.Key, (byte)scanCode, flags | KEYEVENTF_KEYUP, new IntPtr(MAGIC_NUMBER));
          }
        } else {
          int mouseData = 0;
          uint mouseFlags = 0;
          if (evt.Direction == MacroDirection.Wheel) {
            mouseFlags = MOUSEEVENTF_WHEEL;
            mouseData = evt.ScrollDelta;
          } else if (evt.Direction == MacroDirection.HorizontalWheel) {
            mouseFlags = MOUSEEVENTF_HWHEEL;
            mouseData = evt.ScrollDelta;
          } else if (evt.Direction == MacroDirection.Down) {
            if (evt.Key == 1) mouseFlags = MOUSEEVENTF_LEFTDOWN;
            else if (evt.Key == 2) mouseFlags = MOUSEEVENTF_RIGHTDOWN;
            else if (evt.Key == 4) mouseFlags = MOUSEEVENTF_MIDDLEDOWN;
          } else if (evt.Direction == MacroDirection.Up) {
            if (evt.Key == 1) mouseFlags = MOUSEEVENTF_LEFTUP;
            else if (evt.Key == 2) mouseFlags = MOUSEEVENTF_RIGHTUP;
            else if (evt.Key == 4) mouseFlags = MOUSEEVENTF_MIDDLEUP;
          }
          mouse_event(mouseFlags, 0, 0, mouseData, new IntPtr(MAGIC_NUMBER));
        }
      }
    }

    static IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam) {
      if (nCode >= 0) {
        int msg = (int)wParam;
        KBDLLHOOKSTRUCT kbd = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
        uint vk = kbd.vkCode;
        IntPtr extraInfo = kbd.dwExtraInfo;
        if ((uint)extraInfo.ToInt64() == MAGIC_NUMBER)
          return CallNextHookEx(_kbHookId, nCode, wParam, lParam);

        if (_recording && _recordingTarget != null) {
          int delay = (int)(DateTime.Now - _lastRecordedEventTime).TotalMilliseconds;
          _lastRecordedEventTime = DateTime.Now;
          if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN) {
            if (_pressedKeys.Add(vk)) {
              _recordingTarget.Events.Add(new MacroEvent {
                Source = MacroSource.Keyboard, Direction = MacroDirection.Down,
                Key = vk, DelayMs = delay
              });
            }
          } else if (msg == WM_KEYUP || msg == WM_SYSKEYUP) {
            _pressedKeys.Remove(vk);
            _recordingTarget.Events.Add(new MacroEvent {
              Source = MacroSource.Keyboard, Direction = MacroDirection.Up,
              Key = vk, DelayMs = delay
            });
          }
          if (vk == 0x1B) {
            System.Windows.Application.Current?.Dispatcher.Invoke(() => StopRecording());
            return (IntPtr)1;
          }
          return (IntPtr)1;
        }

        if (_enabled && !_playing) {
          MacroSequence macro = MacroService.GetByTriggerKey(vk);
          if (macro != null && (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)) {
            PlayMacro(macro);
            return (IntPtr)1;
          }
        }

        if (_playing) {
          MacroSequence macro = MacroService.GetByTriggerKey(vk);
          if (macro != null && macro.InterruptOnOtherKey) {
            CancelPlayback();
          }
        }
      }
      return CallNextHookEx(_kbHookId, nCode, wParam, lParam);
    }

    static IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam) {
      if (nCode >= 0 && _recording && _recordingTarget != null) {
        MSLLHOOKSTRUCT ms = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
        if ((uint)ms.dwExtraInfo.ToInt64() == MAGIC_NUMBER)
          return CallNextHookEx(_mouseHookId, nCode, wParam, lParam);

        int msg = (int)wParam;
        int delay = (int)(DateTime.Now - _lastRecordedEventTime).TotalMilliseconds;
        _lastRecordedEventTime = DateTime.Now;

        if (msg == WM_LBUTTONDOWN) _recordingTarget.Events.Add(new MacroEvent { Source = MacroSource.Mouse, Direction = MacroDirection.Down, Key = 1, DelayMs = delay });
        else if (msg == WM_LBUTTONUP) _recordingTarget.Events.Add(new MacroEvent { Source = MacroSource.Mouse, Direction = MacroDirection.Up, Key = 1, DelayMs = delay });
        else if (msg == WM_RBUTTONDOWN) _recordingTarget.Events.Add(new MacroEvent { Source = MacroSource.Mouse, Direction = MacroDirection.Down, Key = 2, DelayMs = delay });
        else if (msg == WM_RBUTTONUP) _recordingTarget.Events.Add(new MacroEvent { Source = MacroSource.Mouse, Direction = MacroDirection.Up, Key = 2, DelayMs = delay });
        else if (msg == WM_MBUTTONDOWN) _recordingTarget.Events.Add(new MacroEvent { Source = MacroSource.Mouse, Direction = MacroDirection.Down, Key = 4, DelayMs = delay });
        else if (msg == WM_MBUTTONUP) _recordingTarget.Events.Add(new MacroEvent { Source = MacroSource.Mouse, Direction = MacroDirection.Up, Key = 4, DelayMs = delay });
        else if (msg == WM_MOUSEWHEEL) {
          short delta = (short)(ms.mouseData >> 16);
          _recordingTarget.Events.Add(new MacroEvent { Source = MacroSource.Mouse, Direction = MacroDirection.Wheel, ScrollDelta = delta, DelayMs = delay });
        } else if (msg == WM_MOUSEHWHEEL) {
          short delta = (short)(ms.mouseData >> 16);
          _recordingTarget.Events.Add(new MacroEvent { Source = MacroSource.Mouse, Direction = MacroDirection.HorizontalWheel, ScrollDelta = delta, DelayMs = delay });
        }
      }
      return CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
    }

    public static string GetKeyName(uint vk) {
      if (vk == 0) return "(无)";
      if (vk >= 0x30 && vk <= 0x39) return ((char)vk).ToString();
      if (vk >= 0x41 && vk <= 0x5A) return ((char)vk).ToString();
      // ponytail: direct mapping for OEM keys (layout-independent)
      switch (vk) {
        case 0x08: return "Backspace";  case 0x09: return "Tab";
        case 0x0D: return "Enter";      case 0x10: return "Shift";
        case 0x11: return "Ctrl";       case 0x12: return "Alt";
        case 0x13: return "Pause";      case 0x14: return "Caps Lock";
        case 0x1B: return "Escape";     case 0x20: return "Space";
        case 0x21: return "Page Up";    case 0x22: return "Page Down";
        case 0x23: return "End";        case 0x24: return "Home";
        case 0x25: return "Left";       case 0x26: return "Up";
        case 0x27: return "Right";      case 0x28: return "Down";
        case 0x2C: return "Print Scrn"; case 0x2D: return "Insert";
        case 0x2E: return "Delete";     case 0x5B: return "LWin";
        case 0x5C: return "RWin";
        case 0x60: return "Num 0";      case 0x61: return "Num 1";
        case 0x62: return "Num 2";      case 0x63: return "Num 3";
        case 0x64: return "Num 4";      case 0x65: return "Num 5";
        case 0x66: return "Num 6";      case 0x67: return "Num 7";
        case 0x68: return "Num 8";      case 0x69: return "Num 9";
        case 0x6A: return "Num *";      case 0x6B: return "Num +";
        case 0x6C: return "Num Enter";  case 0x6D: return "Num -";
        case 0x6E: return "Num .";      case 0x6F: return "Num /";
        case 0x70: return "F1";         case 0x71: return "F2";
        case 0x72: return "F3";         case 0x73: return "F4";
        case 0x74: return "F5";         case 0x75: return "F6";
        case 0x76: return "F7";         case 0x77: return "F8";
        case 0x78: return "F9";         case 0x79: return "F10";
        case 0x7A: return "F11";        case 0x7B: return "F12";
        case 0x90: return "Num Lock";   case 0x91: return "Scroll Lock";
        case 0xA0: return "LShift";     case 0xA1: return "RShift";
        case 0xA2: return "LCtrl";      case 0xA3: return "RCtrl";
        case 0xA4: return "LAlt";       case 0xA5: return "RAlt";
        // OEM symbol keys (US layout)
        case 0xBA: return ";";  case 0xBB: return "=";
        case 0xBC: return ",";  case 0xBD: return "-";
        case 0xBE: return ".";  case 0xBF: return "/";
        case 0xC0: return "`";  case 0xDB: return "[";
        case 0xDC: return "\\"; case 0xDD: return "]";
        case 0xDE: return "'";  case 0xE2: return "\\";
        default: {
          // try ToUnicodeEx as last resort
          long scan = MapVirtualKey((int)vk, 0);
          int result = ToUnicodeEx(vk, (uint)scan, new byte[256], new char[256], 256, 0, GetKeyboardLayout(0));
          if (result > 0) return new string(new char[256], 0, result).Trim();
          return "0x" + vk.ToString("X2");
        }
      }
    }

    // P/Invoke
    delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("user32.dll")]
    static extern uint MapVirtualKey(int uCode, uint uMapType);

    [DllImport("user32.dll")]
    static extern int ToUnicodeEx(uint wVirtKey, uint wScanCode, byte[] lpKeyState,
        [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 4)] char[] pwszBuff, int cchBuff, uint wFlags, IntPtr dwhkl);

    [DllImport("user32.dll")]
    static extern bool GetKeyboardState(byte[] lpKeyState);

    [DllImport("user32.dll")]
    static extern IntPtr GetKeyboardLayout(uint idThread);

    [DllImport("user32.dll")]
    static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, IntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    static extern void mouse_event(uint dwFlags, int dx, int dy, int dwData, IntPtr dwExtraInfo);

    const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    const uint KEYEVENTF_KEYUP = 0x0002;
    const uint MOUSEEVENTF_WHEEL = 0x0800;
    const uint MOUSEEVENTF_HWHEEL = 0x1000;
    const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    const uint MOUSEEVENTF_LEFTUP = 0x0004;
    const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    const uint MOUSEEVENTF_MIDDLEUP = 0x0040;

    [StructLayout(LayoutKind.Sequential)]
    struct KBDLLHOOKSTRUCT {
      public uint vkCode;
      public uint scanCode;
      public uint flags;
      public uint time;
      public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MSLLHOOKSTRUCT {
      public int pt_x;
      public int pt_y;
      public int mouseData;
      public uint flags;
      public uint time;
      public IntPtr dwExtraInfo;
    }
  }
}
