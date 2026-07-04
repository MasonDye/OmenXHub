using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using static OmenSuperHub.GpuAppManager;

namespace OmenSuperHub.Views {

  /// <summary>微星小飞机风格 V-F 曲线交互式编辑器</summary>
  public partial class VfCurveChart : UserControl {

    // ── Colour palette ──
    static readonly Brush GridBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E8E8E8"));
    static readonly Brush TextBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#777777"));
    static readonly Brush OrigCurveBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#BBBBBB"));
    static readonly Brush ModCurveBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1976D2"));
    static readonly Brush LockBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E53935"));
    static readonly Brush HandleBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1565C0"));
    static readonly Brush HandleHoverBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E53935"));
    static readonly Brush HandleDragBrush = Brushes.OrangeRed;
    static readonly Brush LockDashBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E53935"));
    static readonly Brush ChartBg = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FAFAFA"));

    // ── Data ──
    List<VfPoint> _original;
    int?[] _modifiedFreq = new int?[VfPointCount]; // null = use original
    int _lockIndex = -1; // index of the lock (flatten) point
    bool _isDirty;

    // ── Interaction state ──
    int _dragIdx = -1;
    int _dragStartFreq;
    int _hoverIdx = -1;

    // ── Layout cache ──
    double _ml, _mr, _mt, _mb, _pw, _ph;
    int _vMin, _vMax, _fMin, _fMax;

    // ── Public events ──
    public event Action CurveChanged;

    void ClearEdits() { for (int i = 0; i < VfPointCount; i++) _modifiedFreq[i] = null; }

    public VfCurveChart() {
      InitializeComponent();
      ClearEdits();
    }

    // ══════════════════════════════════════════════════
    //  Public API
    // ══════════════════════════════════════════════════

    /// <summary>从 GPU 读取原始曲线，彻底重置编辑器状态</summary>
    public void LoadOriginalCurve(List<VfPoint> points) {
      _original = points ?? new List<VfPoint>();
      ClearEdits();
      _lockIndex = -1;
      _isDirty = false;
      _dragIdx = -1;
      _hoverIdx = -1;
      Redraw();
    }

    /// <summary>获取用户编辑后的每点目标频率 (null = 保持原始值)</summary>
    public int?[] GetDesiredFrequencies() => (int?[])_modifiedFreq.Clone();

    /// <summary>外部恢复编辑状态 (用于读取已保存的配置)</summary>
    public void SetDesiredFrequencies(int?[] freqs) {
      if (freqs == null) return;
      int len = Math.Min(freqs.Length, VfPointCount);
      for (int i = 0; i < len; i++) _modifiedFreq[i] = freqs[i];
      _lockIndex = -1;
      _isDirty = freqs.Any(f => f.HasValue);
      Redraw();
    }

    /// <summary>清除所有编辑，回到原始曲线</summary>
    public void ResetEdits() {
      ClearEdits();
      _lockIndex = -1;
      _isDirty = false;
      Redraw();
      CurveChanged?.Invoke();
    }

    public bool IsDirty => _isDirty;
    public bool HasData => _original != null && _original.Count >= 2;

    // ══════════════════════════════════════════════════
    //  Layout & coordinate helpers
    // ══════════════════════════════════════════════════

    void ChartCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => Redraw();

    double ToX(int mv) => _ml + (mv - _vMin) * _pw / Math.Max(1, _vMax - _vMin);
    double ToY(int mhz) => _mt + _ph - (mhz - _fMin) * _ph / Math.Max(1, _fMax - _fMin);
    int FromX(double x) => (int)(_vMin + (x - _ml) / _pw * (_vMax - _vMin));
    int FromY(double y) => (int)(_fMin + (_mt + _ph - y) / _ph * (_fMax - _fMin));

    int EffectiveFreq(int idx) =>
        _modifiedFreq[idx] ?? (_original[idx].FrequencyMHz);

    void CalcLayout() {
      double w = ChartCanvas.ActualWidth, h = ChartCanvas.ActualHeight;
      _ml = 52; _mr = 18; _mt = 16; _mb = 36;
      _pw = Math.Max(40, w - _ml - _mr);
      _ph = Math.Max(40, h - _mt - _mb);

      if (_original != null && _original.Count >= 2) {
        var allFreq = new List<int>();
        foreach (var p in _original) allFreq.Add(EffectiveFreq(p.Index));
        _vMin = _original.Min(p => p.VoltageMv) - 25;
        _vMax = _original.Max(p => p.VoltageMv) + 25;
        _fMin = 0;
        _fMax = ((allFreq.Max() + 200) / 200) * 200;
      } else {
        _vMin = 600; _vMax = 1200; _fMin = 0; _fMax = 3000;
      }
    }

    // ══════════════════════════════════════════════════
    //  Rendering
    // ══════════════════════════════════════════════════

    public void Redraw() {
      var c = ChartCanvas; if (c == null) return;
      c.Children.Clear();
      if (_original == null || _original.Count < 2) { DrawPlaceholder(c); return; }

      CalcLayout();
      if (_pw < 40 || _ph < 40) return;

      // background
      var bg = new Rectangle { Width = _pw, Height = _ph, Fill = ChartBg, RadiusX = 4, RadiusY = 4 };
      Canvas.SetLeft(bg, _ml); Canvas.SetTop(bg, _mt); c.Children.Add(bg);

      DrawAxes(c);
      DrawOriginalCurve(c);
      DrawModifiedCurve(c);
      DrawHandles(c);
      DrawLockLine(c);
      DrawLegend(c);
    }

    void DrawPlaceholder(Canvas c) {
      var tb = new TextBlock {
        Text = "点击 [读取 V-F 曲线] 加载 GPU 曲线\n加载后可拖拽曲线点调频、Ctrl+点击锁平高频段",
        FontSize = 13, Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#AAAAAA")),
        TextAlignment = TextAlignment.Center
      };
      tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
      Canvas.SetLeft(tb, (c.ActualWidth - tb.DesiredSize.Width) / 2);
      Canvas.SetTop(tb, c.ActualHeight / 2 - 28);
      c.Children.Add(tb);
    }

    void DrawAxes(Canvas c) {
      // horizontal grid every 200 MHz
      for (int f = 0; f <= _fMax; f += 200) {
        double y = ToY(f);
        c.Children.Add(new Line { X1 = _ml, Y1 = y, X2 = _ml + _pw, Y2 = y, Stroke = GridBrush, StrokeThickness = 0.5 });
        var lbl = new TextBlock { Text = f.ToString(), FontSize = 10, Foreground = TextBrush };
        Canvas.SetLeft(lbl, _ml - 32); Canvas.SetTop(lbl, y - 7); c.Children.Add(lbl);
      }
      // vertical grid every 50 mV
      for (int v = ((_vMin + 49) / 50) * 50; v <= _vMax; v += 50) {
        double x = ToX(v);
        c.Children.Add(new Line { X1 = x, Y1 = _mt, X2 = x, Y2 = _mt + _ph, Stroke = GridBrush, StrokeThickness = 0.5 });
        var lbl = new TextBlock { Text = v.ToString(), FontSize = 10, Foreground = TextBrush };
        Canvas.SetLeft(lbl, x - 12); Canvas.SetTop(lbl, _mt + _ph + 4); c.Children.Add(lbl);
      }
      // axis titles
      var xt = new TextBlock { Text = "电压 (mV)", FontSize = 11, Foreground = TextBrush };
      Canvas.SetLeft(xt, _ml + _pw / 2 - 26); Canvas.SetTop(xt, _mt + _ph + 18); c.Children.Add(xt);
      var yt = new TextBlock { Text = "频率 (MHz)", FontSize = 11, Foreground = TextBrush };
      yt.LayoutTransform = new RotateTransform(-90);
      Canvas.SetLeft(yt, 2); Canvas.SetTop(yt, _mt + _ph / 2 - 28); c.Children.Add(yt);
    }

    void DrawOriginalCurve(Canvas c) {
      var geom = new StreamGeometry();
      using (var ctx = geom.Open()) {
        ctx.BeginFigure(OrigPt(0), false, false);
        for (int i = 1; i < _original.Count; i++) ctx.LineTo(OrigPt(i), true, false);
      }
      c.Children.Add(new Path {
        Data = geom, Stroke = OrigCurveBrush, StrokeThickness = 1.5,
        StrokeDashArray = new DoubleCollection { 5, 3 }
      });
    }

    void DrawModifiedCurve(Canvas c) {
      // skip if no edits (show nothing separate — original already visible)
      if (!_isDirty && _lockIndex < 0) return;

      var geom = new StreamGeometry();
      using (var ctx = geom.Open()) {
        ctx.BeginFigure(ModPt(0), false, false);
        for (int i = 1; i < _original.Count; i++) ctx.LineTo(ModPt(i), true, false);
      }
      c.Children.Add(new Path {
        Data = geom, Stroke = ModCurveBrush, StrokeThickness = 2.2
      });
    }

    void DrawHandles(Canvas c) {
      // show drag handles on the MODIFIED curve at every 3rd point
      int n = _original.Count;
      for (int i = 0; i < n; i += 3) {
        int freq = EffectiveFreq(i);
        var pt = ModPt(i);
        bool isHover = (i == _hoverIdx);
        bool isDrag = (i == _dragIdx);
        double r = isDrag ? 6 : isHover ? 5 : 3.5;
        Brush fill = isDrag ? HandleDragBrush : isHover ? HandleHoverBrush : HandleBrush;
        var dot = new Ellipse { Width = r * 2, Height = r * 2, Fill = fill, Cursor = Cursors.SizeNS };
        Canvas.SetLeft(dot, pt.X - r); Canvas.SetTop(dot, pt.Y - r);
        dot.Tag = i;
        c.Children.Add(dot);
      }
    }

    void DrawLockLine(Canvas c) {
      if (_lockIndex < 0 || _lockIndex >= _original.Count) return;
      int lockFreq = EffectiveFreq(_lockIndex);
      double lx = ToX(_original[_lockIndex].VoltageMv);
      double ly = ToY(lockFreq);

      // vertical dashed line from lock point to bottom
      var line = new Line {
        X1 = lx, Y1 = ly, X2 = lx, Y2 = _mt + _ph,
        Stroke = LockDashBrush, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 4, 3 }
      };
      c.Children.Add(line);

      // big lock marker
      var dot = new Ellipse { Width = 10, Height = 10, Fill = LockBrush };
      Canvas.SetLeft(dot, lx - 5); Canvas.SetTop(dot, ly - 5);
      c.Children.Add(dot);

      var lbl = new TextBlock {
        Text = $"[LOCK] {_original[_lockIndex].VoltageMv}mV / {lockFreq}MHz",
        FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = LockBrush
      };
      Canvas.SetLeft(lbl, lx + (_lockIndex < _original.Count * 0.7 ? 8 : -150));
      Canvas.SetTop(lbl, ly - 26);
      c.Children.Add(lbl);
    }

    void DrawLegend(Canvas c) {
      double lx = _ml + _pw - 160, ly = _mt + 4;

      // original
      var lo = new Rectangle { Width = 20, Height = 2, Fill = OrigCurveBrush, StrokeDashArray = new DoubleCollection { 3, 2 }, Stroke = OrigCurveBrush, StrokeThickness = 1 };
      Canvas.SetLeft(lo, lx); Canvas.SetTop(lo, ly + 6); c.Children.Add(lo);
      var lot = new TextBlock { Text = "原始曲线", FontSize = 10, Foreground = TextBrush };
      Canvas.SetLeft(lot, lx + 24); Canvas.SetTop(lot, ly); c.Children.Add(lot);

      // modified
      var lm = new Rectangle { Width = 20, Height = 3, Fill = ModCurveBrush };
      Canvas.SetLeft(lm, lx); Canvas.SetTop(lm, ly + 19); c.Children.Add(lm);
      string label = _isDirty ? "编辑后曲线" : (_lockIndex >= 0 ? "锁定曲线" : "编辑后曲线");
      var lmt = new TextBlock { Text = label, FontSize = 10, Foreground = TextBrush };
      Canvas.SetLeft(lmt, lx + 24); Canvas.SetTop(lmt, ly + 13); c.Children.Add(lmt);

      // hint
      var hint = new TextBlock { Text = "Ctrl+点击 = 锁平高频段", FontSize = 9, Foreground = TextBrush };
      Canvas.SetLeft(hint, lx); Canvas.SetTop(hint, ly + 34); c.Children.Add(hint);
    }

    // ── coord helpers ──
    Point OrigPt(int i) => new Point(ToX(_original[i].VoltageMv), ToY(_original[i].FrequencyMHz));
    Point ModPt(int i) => new Point(ToX(_original[i].VoltageMv), ToY(EffectiveFreq(i)));

    // ══════════════════════════════════════════════════
    //  Mouse interaction
    // ══════════════════════════════════════════════════

    int? HitTest(Point mouse) {
      if (_original == null) return null;
      double bestDist = 14; // max snap distance in pixels
      int bestIdx = -1;
      for (int i = 0; i < _original.Count; i++) {
        var pt = ModPt(i);
        double dx = mouse.X - pt.X, dy = mouse.Y - pt.Y;
        double dist = Math.Sqrt(dx * dx + dy * dy);
        if (dist < bestDist) { bestDist = dist; bestIdx = i; }
      }
      return bestIdx >= 0 ? bestIdx : (int?)null;
    }

    void Canvas_MouseDown(object sender, MouseButtonEventArgs e) {
      if (_original == null) return;
      var pos = e.GetPosition(ChartCanvas);
      var hit = HitTest(pos);
      bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);

      if (hit.HasValue) {
        if (ctrl) {
          // Ctrl+click → lock/flatten from this point rightward
          LockCurveAt(hit.Value);
        } else {
          // Start drag
          _dragIdx = hit.Value;
          _dragStartFreq = EffectiveFreq(_dragIdx);
          ChartCanvas.CaptureMouse();
          Redraw();
        }
      }

      ShowTooltip(hit, pos);
    }

    void Canvas_MouseMove(object sender, MouseEventArgs e) {
      if (_original == null) return;
      var pos = e.GetPosition(ChartCanvas);

      if (_dragIdx >= 0) {
        // dragging: set frequency based on Y position
        int newFreq = FromY(pos.Y);
        newFreq = Math.Max(200, Math.Min(4500, newFreq));
        // snap to 15 MHz
        newFreq = (newFreq / 15) * 15;
        SetPointFreq(_dragIdx, newFreq);
        Redraw();
        ShowTooltip(_dragIdx, pos);
      } else {
        var hit = HitTest(pos);
        if (hit != _hoverIdx) {
          _hoverIdx = hit ?? -1;
          Redraw();
        }
        ShowTooltip(hit, pos);
      }
    }

    void Canvas_MouseUp(object sender, MouseButtonEventArgs e) {
      if (_dragIdx >= 0) {
        int finalFreq = EffectiveFreq(_dragIdx);
        if (finalFreq != _dragStartFreq) {
          _isDirty = true;
          CurveChanged?.Invoke();
        }
        _dragIdx = -1;
        ChartCanvas.ReleaseMouseCapture();
        Redraw();
      }
      Canvas_MouseLeave(sender, e);
    }

    void Canvas_MouseLeave(object sender, MouseEventArgs e) {
      TooltipBorder.Visibility = Visibility.Collapsed;
    }

    void LockCurveAt(int idx) {
      int lockFreq = EffectiveFreq(idx);
      // flatten all points with higher voltage to the same frequency
      int lockVolt = _original[idx].VoltageMv;
      for (int i = 0; i < _original.Count; i++) {
        if (_original[i].VoltageMv >= lockVolt)
          _modifiedFreq[i] = lockFreq;
        else if (_original[i].VoltageMv >= _original[Math.Max(0, idx - 1)].VoltageMv)
          _modifiedFreq[i] = null; // clear transition zone, let linear interpolation handle it
      }
      _lockIndex = idx;
      _isDirty = true;
      _dragIdx = -1;
      Redraw();
      CurveChanged?.Invoke();
    }

    void SetPointFreq(int idx, int freq) {
      if (idx < 0 || idx >= _original.Count) return;
      // don't allow exceeding original curve at this voltage
      int origFreq = _original[idx].FrequencyMHz;
      _modifiedFreq[idx] = Math.Min(freq, origFreq + 15); // slight headroom
      // Clear lock on manual edit
      if (_lockIndex >= 0) _lockIndex = -1;
    }

    void ShowTooltip(int? hitIdx, Point screenPos) {
      if (!hitIdx.HasValue || hitIdx.Value < 0) {
        TooltipBorder.Visibility = Visibility.Collapsed;
        return;
      }
      int i = hitIdx.Value;
      var orig = _original[i];
      int curFreq = EffectiveFreq(i);
      int delta = curFreq - orig.FrequencyMHz;
      string deltaStr = delta >= 0 ? $"+{delta}" : $"{delta}";

      TooltipText.Text = $"V={orig.VoltageMv}mV  F={curFreq}MHz  d={deltaStr}MHz";
      TooltipBorder.Visibility = Visibility.Visible;

      // position tooltip near cursor but keep inside bounds
      double tx = screenPos.X + 14, ty = screenPos.Y - 26;
      if (tx + 180 > ChartCanvas.ActualWidth) tx = screenPos.X - 180;
      if (ty < 0) ty = screenPos.Y + 14;
      Canvas.SetLeft(TooltipBorder, tx);
      Canvas.SetTop(TooltipBorder, ty);
    }
  }
}
