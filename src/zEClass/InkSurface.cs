using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using zEClass.Core;

namespace zEClass;

/// <summary>
/// The board surface. Owns input handling (pen, touch, mouse, gestures), the selection, and
/// rendering. WPF draws; input arrives through the Win32 pointer-message stack because that is
/// the only Windows API carrying pressure, eraser tip, tilt and palm flags for USB panels, with
/// the WPF stylus/touch stack as a fallback.
/// </summary>
public sealed class InkSurface : FrameworkElement
{
    private readonly InkEngine _engine = new();
    private readonly List<InkStroke> _committed = new();
    private readonly List<Drawing> _committedDrawings = new();
    private readonly Dictionary<int, InkStroke> _live = new();
    private readonly Dictionary<int, long> _contactDownTicks = new();
    private readonly Dictionary<string, ImageSource> _imageCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Selection _selection = new();
    private readonly GestureRecognizer _gestures = new();
    private readonly TwoFingerGestureRecognizer _twoFinger = new();
    private readonly List<ActiveContact> _contacts = new();

    private HwndSource? _source;
    private BoardDocument? _document;
    private bool _cacheValid;
    private bool _dragging;
    private Point _dragAnchor;
    private bool _lassoActive;
    private bool _panning;
    private Point _panStart;
    private double _panStartOffsetX;
    private double _panStartOffsetY;
    private BoardTool _tool = BoardTool.Pen;
    private uint _colorArgb = 0xFF1B1B1F;
    private double _strokeWidth = 4;
    private LineStyle _lineStyle = LineStyle.Solid;
    private double _highlighterOpacity = 0.35;
    private ShapeStyle _shapeStyle = ShapeStyle.Outline;
    private TextEditSession? _textSession;

    public InkSurface()
    {
        ClipToBounds = true;
        Focusable = true;
        _engine.PalmRejectionEnabled = true;
    }

    public BoardDocument? Document
    {
        get => _document;
        set
        {
            _document = value;
            ReloadFromDocument();
        }
    }

    public DigitizerStatus? Digitizer { get; set; }

    public EditHistory History { get; } = new();

    public Selection Selection => _selection;

    public InkEngine Engine => _engine;

    public GestureRecognizer Gestures => _gestures;

    public TwoFingerGestureRecognizer TwoFingerGestures => _twoFinger;

    /// <summary>
    /// When set, a finished freehand stroke is classified and a circle or square is replaced
    /// by the vendor's behaviour: circle to spotlight, square to magnifier.
    /// </summary>
    public bool RecognitionPenEnabled { get; set; }

    /// <summary>Shape recognised by the last completed stroke, for the status line.</summary>
    public ShapeRecognition? LastRecognition { get; private set; }

    public event EventHandler<ShapeRecognition>? ShapeRecognised;

    public event EventHandler<System.Windows.Point>? SpotlightRequested;

    public event EventHandler<System.Windows.Rect>? MagnifierRegionRequested;

    public IEditCommandSink CommandSink => new HistorySink(this);

    /// <summary>Digitizer-to-board transform; null when the board has never been calibrated.</summary>
    public CalibrationTransform? CalibrationTransform { get; private set; }

    public bool IsCalibrated => CalibrationTransform is not null && !CalibrationTransform.IsIdentity;

    public double CalibrationError { get; private set; } = double.NaN;

    public void ApplyCalibration(CalibrationTransform? transform, double errorPixels = double.NaN)
    {
        CalibrationTransform = transform;
        CalibrationError = errorPixels;
    }

    public BoardTool Tool
    {
        get => _tool;
        set
        {
            _tool = value;
            if (value is not (BoardTool.Select or BoardTool.Pan))
            {
                _selection.Clear();
            }

            if (value != BoardTool.Text)
            {
                CommitText();
            }

            InvalidateVisual();
        }
    }

    public uint ColorArgb
    {
        get => _colorArgb;
        set
        {
            _colorArgb = value;
            InvalidateVisual();
        }
    }

    public double StrokeWidth
    {
        get => _strokeWidth;
        set
        {
            _strokeWidth = Math.Clamp(value, 0.5, 80);
            InvalidateVisual();
        }
    }

    public LineStyle LineStyle
    {
        get => _lineStyle;
        set
        {
            _lineStyle = value;
            InvalidateVisual();
        }
    }

    public ShapeStyle ShapeStyle
    {
        get => _shapeStyle;
        set
        {
            _shapeStyle = value;
            InvalidateVisual();
        }
    }

    public double HighlighterOpacity
    {
        get => _highlighterOpacity;
        set
        {
            _highlighterOpacity = Math.Clamp(value, 0.05, 1);
            InvalidateVisual();
        }
    }

    public event EventHandler? BoardChanged;

    public event EventHandler? SelectionChanged;

    public event EventHandler? GestureRecognized;

    public event EventHandler? TextEditingChanged;

    public event EventHandler? ViewChanged;

    /// <summary>True once the native window accepted WM_POINTER messages for this surface.</summary>
    public bool HasPointerTarget { get; private set; }

    /// <summary>Contacts currently down. Used by the hardware acceptance harness.</summary>
    public int ContactCount => _contacts.Count;

    public TextEditSession? TextSession => _textSession;

    // ---- lifecycle -------------------------------------------------------------

    protected override void OnInitialized(EventArgs e)
    {
        base.OnInitialized(e);
        Loaded += OnSurfaceLoaded;
        Unloaded += OnSurfaceUnloaded;
        History.Changed += (_, _) => BoardChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnSurfaceLoaded(object sender, RoutedEventArgs e)
    {
        var source = PresentationSource.FromVisual(this) as HwndSource;
        if (source is null)
        {
            return;
        }

        _source = source;

        // The pointer stack must be registered on the top-level window, and only after the
        // window is fully created, otherwise the call is refused.
        if (!HasPointerTarget && !TryRegisterPointerTarget(source.Handle))
        {
            CrashLog.Info("RegisterPointerInputTarget unavailable; using WPF input fallback.");
        }

        source.AddHook(WndProc);
        RebuildCache();
    }

    private void OnSurfaceUnloaded(object sender, RoutedEventArgs e)
    {
        if (_source is not null)
        {
            _source.RemoveHook(WndProc);
            _source = null;
        }

        _live.Clear();
    }

    private bool TryRegisterPointerTarget(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        if (!PointerNative.IsProcessDpiAwarenessSet())
        {
            PointerNative.EnablePerMonitorV2();
            CrashLog.Info("Enabled PerMonitorV2 awareness from code (manifest was not honored).");
        }

        if (PointerNative.RegisterPointerInputTarget(hwnd))
        {
            HasPointerTarget = true;
            return true;
        }

        CrashLog.Info($"RegisterPointerInputTarget failed: {Marshal.GetLastWin32Error()}");
        var ok = false;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            ok = PointerNative.RegisterPointerInputTarget(hwnd);
            HasPointerTarget = ok;
        }));
        return ok;
    }

    // ---- edit operations --------------------------------------------------------

    public bool Undo()
    {
        if (_document is null)
        {
            return false;
        }

        if (CommitText() is { } committed)
        {
            History.Execute(_document, committed);
        }

        var ok = History.Undo(_document);
        if (ok)
        {
            _selection.Clear();
            ReloadFromDocument();
        }

        return ok;
    }

    public bool Redo()
    {
        if (_document is null)
        {
            return false;
        }

        var ok = History.Redo(_document);
        if (ok)
        {
            _selection.Clear();
            ReloadFromDocument();
        }

        return ok;
    }

    public void ClearPage()
    {
        if (_document is null)
        {
            return;
        }

        var page = _document.Active();
        if (page.Locked)
        {
            StatusMessage = "This page is locked. Unlock it before clearing.";
            return;
        }

        if (page.Strokes.Count == 0 && page.Images.Count == 0)
        {
            return;
        }

        History.Execute(_document, new ReplaceStrokesCommand("Clear page", []));
        page.Images.Clear();
        _selection.Clear();
        ReloadFromDocument();
    }

    public void DeleteSelection()
    {
        if (_document is null || _selection.IsEmpty)
        {
            return;
        }

        var page = _document.Active();
        _selection.Remove(page);
        _selection.Clear();
        ReloadFromDocument();
        BoardChanged?.Invoke(this, EventArgs.Empty);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void CopySelection()
    {
        if (_document is null || _selection.IsEmpty)
        {
            return;
        }

        _clipboard = _selection.Strokes.Select(s => s.Clone()).ToList();
        _clipboardImages = _selection.Images.Select(i => i.Clone()).ToList();
        StatusMessage = $"Copied {_clipboard.Count} object(s).";
    }

    public void CutSelection()
    {
        CopySelection();
        DeleteSelection();
    }

    public void Paste()
    {
        if (_document is null || _clipboard.Count == 0)
        {
            return;
        }

        var page = _document.Active();
        var offset = PasteOffset;
        foreach (var image in _clipboardImages)
        {
            var copy = image.Clone();
            copy.Id = Guid.NewGuid();
            copy.X += offset;
            copy.Y += offset;
            page.Images.Add(copy);
        }

        var offsetIndex = 0;
        foreach (var stroke in _clipboard)
        {
            var copy = stroke.Clone();
            copy.Id = Guid.NewGuid();
            foreach (var p in copy.Points)
            {
                p.X += offset;
                p.Y += offset;
            }

            _ = offsetIndex++;
        }

        History.Execute(_document, new AddStrokesCommand("Paste", _clipboard.Select(s =>
        {
            var c = s.Clone();
            c.Id = Guid.NewGuid();
            foreach (var p in c.Points)
            {
                p.X += offset;
                p.Y += offset;
            }

            return c;
        })));
        PasteOffset += 24;
        ReloadFromDocument();
    }

    public static double PasteOffset { get; set; }

    private List<InkStroke> _clipboard = new();
    private List<InkImage> _clipboardImages = new();

    public string? StatusMessage { get; private set; }

    public void SetStatus(string? message) => StatusMessage = message;

    // ---- viewport ---------------------------------------------------------------

    public double ViewScale => _document?.ViewScale ?? 1.0;

    public double ViewOffsetX => _document?.ViewOffsetX ?? 0;

    public double ViewOffsetY => _document?.ViewOffsetY ?? 0;

    public void ZoomBy(double factor, Point? anchor = null)
    {
        if (_document is null)
        {
            return;
        }

        var old = _document.ViewScale;
        var newScale = Math.Clamp(old * factor, MinScale, MaxScale);
        if (Math.Abs(newScale - old) < 1e-6)
        {
            return;
        }

        // Keep the anchor point stationary while scaling.
        var a = anchor ?? new Point(ActualWidth / 2, ActualHeight / 2);
        var boardX = ((a.X - _document.ViewOffsetX) / old) + (a.X - _document.ViewOffsetX) / old * 0;
        _ = boardX;
        var contentX = (a.X - _document.ViewOffsetX) / old;
        var contentY = (a.Y - _document.ViewOffsetY) / old;
        _document.ViewOffsetX = a.X - (contentX * newScale);
        _document.ViewOffsetY = a.Y - (contentY * newScale);
        _document.ViewScale = newScale;
        InvalidateVisual();
        ViewChanged?.Invoke(this, EventArgs.Empty);
        BoardChanged?.Invoke(this, EventArgs.Empty);
    }

    public const double MinScale = 0.25;
    public const double MaxScale = 8.0;

    public void PanBy(double dx, double dy)
    {
        if (_document is null)
        {
            return;
        }

        _document.ViewOffsetX += dx;
        _document.ViewOffsetY += dy;
        InvalidateVisual();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ResetView()
    {
        if (_document is null)
        {
            return;
        }

        _document.ViewScale = 1;
        _document.ViewOffsetX = 0;
        _document.ViewOffsetY = 0;
        InvalidateVisual();
        ViewChanged?.Invoke(this, EventArgs.Empty);
        BoardChanged?.Invoke(this, EventArgs.Empty);
    }

    // ---- coordinate conversion ---------------------------------------------------

    /// <summary>
    /// Maps a raw digitizer coordinate into board space. WM_POINTER reports physical pixels so
    /// it is DPI-scaled first; the WPF stylus and touch events already report element
    /// coordinates and must not be scaled again. Calibration is applied on top of either frame.
    /// </summary>
    private CalibrationPoint MapToBoard(double rawX, double rawY, bool isDevicePixels)
    {
        var x = rawX;
        var y = rawY;

        if (isDevicePixels)
        {
            var target = PresentationSource.FromVisual(this)?.CompositionTarget;
            if (target is not null)
            {
                var m = target.TransformFromDevice;
                if (m.HasInverse)
                {
                    var logical = m.Transform(new Point(rawX, rawY));
                    x = logical.X;
                    y = logical.Y;
                }
            }
        }

        // Undo the viewport transform to get true board coordinates.
        var scale = _document?.ViewScale ?? 1;
        var ox = _document?.ViewOffsetX ?? 0;
        var oy = _document?.ViewOffsetY ?? 0;
        x = (x - ox) / scale;
        y = (y - oy) / scale;

        if (CalibrationTransform is { } transform)
        {
            return transform.Apply(new CalibrationPoint(x, y));
        }

        return new CalibrationPoint(x, y);
    }

    private PointerSample Calibrate(PointerSample s, bool isDevicePixels)
    {
        var p = MapToBoard(s.X, s.Y, isDevicePixels);
        if (!p.IsFinite)
        {
            return s;
        }

        return new PointerSample(s.Type, s.Id, p.X, p.Y, s.Pressure, s.InContact, s.InRange,
            s.Eraser, s.Palm, s.Tilt, s.Orientation);
    }

    private bool ShouldReject(PointerSample s) => s.Palm || (s.IsTouch && _engine.ShouldIgnoreTouch());

    // ---- input ------------------------------------------------------------------

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        var pointerId = (int)(long)wParam;
        switch (msg)
        {
            case PointerNative.WmPointerDown:
                if (PointerNative.Read(pointerId) is { } down)
                {
                    OnDown(Calibrate(down, isDevicePixels: true));
                }

                handled = true;
                return IntPtr.Zero;

            case PointerNative.WmPointerUpdate:
                if (PointerNative.Read(pointerId) is { } update)
                {
                    if (update.InContact)
                    {
                        OnMove(Calibrate(update, isDevicePixels: true));
                    }
                    else if (update.InRange)
                    {
                        OnHover(Calibrate(update, isDevicePixels: true));
                    }
                }

                handled = true;
                return IntPtr.Zero;

            case PointerNative.WmPointerUp:
                if (PointerNative.Read(pointerId) is { } up)
                {
                    OnUp(Calibrate(up, isDevicePixels: true));
                }

                handled = true;
                return IntPtr.Zero;

            case PointerNative.WmPointerEnter:
                _gestures.NoteEnter(pointerId);
                break;

            case PointerNative.WmPointerLeave:
                _gestures.NoteLeave(pointerId);
                break;
        }

        return IntPtr.Zero;
    }

    private void OnDown(PointerSample s)
    {
        if (ShouldReject(s))
        {
            // Raised even when rejected, so the harness can prove that palm rejection fired
            // rather than inferring it from the absence of a stroke.
            SampleObserved?.Invoke(this, new SurfaceSampleEventArgs(
                s, rejected: true, normalizedPressure: 0));
            return;
        }

        if (s.IsPen)
        {
            _engine.NoteStylusDown(s.Id);
        }
        else if (s.IsTouch)
        {
            _engine.NoteTouchDown(s.Id);
        }

        _twoFinger.Begin(s.Id, s.X, s.Y);

        _contactDownTicks[s.Id] = Environment.TickCount64;
        _contacts.Add(new ActiveContact(s.Id, s.Type, s.X, s.Y, Environment.TickCount64));
        _gestures.NoteDown(s.Id, s.X, s.Y, Environment.TickCount64, s.Palm);

        switch (_tool)
        {
            case BoardTool.Pan:
                _panning = true;
                _panStart = new Point(s.X, s.Y);
                _panStartOffsetX = ViewOffsetX;
                _panStartOffsetY = ViewOffsetY;
                return;

            case BoardTool.Select:
                _dragging = true;
                _dragAnchor = new Point(s.X, s.Y);
                _selection.BeginRectangle(s.X, s.Y, s.X, s.Y);
                return;

            case BoardTool.Lasso:
                _lassoActive = true;
                _selection.BeginLasso();
                _selection.AddLassoPoint(new Point(s.X, s.Y));
                return;

            case BoardTool.Text:
                BeginTextAt(s.X, s.Y);
                return;
        }

        if (_selection.IsActive)
        {
            _selection.Clear();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        BeginStroke(s);
    }

    private void OnMove(PointerSample s)
    {
        // Every sample from both input paths passes through here, so this is the one place the
        // hardware acceptance harness can observe the real pipeline rather than a parallel one.
        SampleObserved?.Invoke(this, new SurfaceSampleEventArgs(
            s,
            _engine.ShouldIgnoreTouch(),
            _engine.NormalizePressure(s.Id, s.Pressure)));

        if (s.IsPen)
        {
            _engine.RecordPressure(s.Id, s.Pressure);
        }


        _gestures.NoteMove(s.Id, s.X, s.Y, Environment.TickCount64);
        _twoFinger.Update(s.Id, s.X, s.Y);

        // A two-finger gesture must not also draw, and it wins over any one-finger action.
        if (_twoFinger.IsTracking)
        {
            if (_live.Remove(s.Id, out var dropped))
            {
                _ = dropped;
                InvalidateVisual();
            }

            if (_twoFinger.Consume() is { } gesture)
            {
                ApplyTwoFingerGesture(gesture);
            }

            return;
        }

        if (_tool == BoardTool.Pan && _panning)
        {
            var scale = _document?.ViewScale ?? 1;
            _document!.ViewOffsetX = _panStartOffsetX + (s.X - _panStart.X);
            _document.ViewOffsetY = _panStartOffsetY + (s.Y - _panStart.Y);
            _ = scale;
            InvalidateVisual();
            ViewChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (_tool == BoardTool.Select && _dragging)
        {
            _selection.BeginRectangle(_dragAnchor.X, _dragAnchor.Y, s.X, s.Y);
            _selection.Capture(_document?.Active()!);
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
            return;
        }

        if (_tool == BoardTool.Lasso && _lassoActive)
        {
            _selection.AddLassoPoint(new Point(s.X, s.Y));
            _selection.EndLasso();
            InvalidateVisual();
            return;
        }

        if (TextSession is { } session && session.IsEditing)
        {
            return;
        }

        if (!_live.ContainsKey(s.Id))
        {
            BeginStroke(s);
        }

        ExtendStroke(s);
    }

    private void OnHover(PointerSample s)
    {
        if (s.IsPen && !_live.ContainsKey(s.Id))
        {
            _engine.RecordPressure(s.Id, s.Pressure);
        }
    }

    private void OnUp(PointerSample s)
    {
        var heldMs = Environment.TickCount64 - (_contactDownTicks.TryGetValue(s.Id, out var t0) ? t0 : 0);
        _contactDownTicks.Remove(s.Id);
        _contacts.RemoveAll(c => c.Id == s.Id);
        _gestures.NoteUp(s.Id, s.X, s.Y, Environment.TickCount64);
        _twoFinger.End(s.Id);

        if (s.IsPen)
        {
            _engine.NoteStylusUp(s.Id);
        }
        else if (s.IsTouch)
        {
            _engine.NoteTouchUp(s.Id);
        }

        if (_tool == BoardTool.Pan && _panning)
        {
            _panning = false;
            BoardChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (_tool == BoardTool.Select && _dragging)
        {
            _dragging = false;
            _selection.EndLasso();
            _selection.Capture(_document?.Active()!);
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
            return;
        }

        if (_tool == BoardTool.Lasso && _lassoActive)
        {
            _lassoActive = false;
            _selection.EndLasso();
            _selection.Capture(_document?.Active()!);
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
            return;
        }

        if (TextSession is { IsEditing: true })
        {
            CommitText();
            InvalidateVisual();
            return;
        }

        if (!_live.Remove(s.Id, out var stroke) || stroke.Points.Count == 0)
        {
            InvalidateVisual();
            return;
        }

        if (stroke.Kind != StrokeKind.Eraser && _document is not null)
        {
            History.Execute(_document, new AddStrokesCommand("Draw", [stroke]));
            _committed.Add(stroke);
            _committedDrawings.Add(BuildDrawing(stroke));

            if (RecognitionPenEnabled && stroke.Kind == StrokeKind.Pen)
            {
                ApplyRecognition(stroke);
            }
        }

        InvalidateVisual();
        BoardChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Applies a recognized two-finger gesture. Scale and pan act on the board view; rotation
    /// has no board-level meaning, so it is reported and otherwise ignored rather than silently
    /// rotating the ink, which would be destructive and surprising.
    /// </summary>
    private void ApplyTwoFingerGesture(TwoFingerGesture gesture)
    {
        switch (gesture.Kind)
        {
            case GestureKind.Scale:
                ZoomBy(gesture.Scale,
                    new Point(ViewOffsetX + (gesture.CenterX * ViewScale),
                        ViewOffsetY + (gesture.CenterY * ViewScale)));
                SetStatus($"Pinch {gesture.Scale * 100:F0}%");
                break;

            case GestureKind.Pan:
                PanBy(gesture.DeltaX, gesture.DeltaY);
                SetStatus("Two-finger pan");
                break;

            case GestureKind.Rotate:
                SetStatus($"Twist {gesture.RotationDegrees:F0} degrees");
                break;
        }

        GestureRecognized?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Classification for the recognition pen. A recognised circle asks for a spotlight at its
    /// centre and a recognised square for a magnifier over its box; the stroke itself is kept,
    /// because a teacher who wanted a rough circle still expects ink to be there.
    /// </summary>
    private void ApplyRecognition(InkStroke stroke)
    {
        var recognition = ShapeRecognizer.Recognize(stroke.Points);
        LastRecognition = recognition;
        if (recognition.Shape == RecognizedShape.None)
        {
            return;
        }

        switch (recognition.Shape)
        {
            case RecognizedShape.Circle:
            {
                var circle = ShapeRecognizer.CircleFrom(stroke.Points);
                SpotlightRequested?.Invoke(this, circle.Center);
                SetStatus($"Circle recognised: spotlight at ({circle.Center.X:F0}, " +
                          $"{circle.Center.Y:F0})");
                break;
            }

            case RecognizedShape.Square:
            {
                var (a, b, _, d) = ShapeRecognizer.SquareFrom(stroke.Points);
                var rect = new System.Windows.Rect(a, new System.Windows.Point(
                    Math.Max(b.X, d.X), Math.Max(b.Y, d.Y)));
                MagnifierRegionRequested?.Invoke(this, rect);
                SetStatus($"Square recognised: magnifier over {rect.Width:F0}x{rect.Height:F0}");
                break;
            }

            default:
                SetStatus($"{recognition.Shape} recognised ({recognition.Confidence:P0})");
                break;
        }

        ShapeRecognised?.Invoke(this, recognition);
    }

    /// <summary>
    /// Feeds a still-held contact to the gesture recognizer. Called on a timer so dwell gestures
    /// can fire while nothing is moving.
    /// </summary>
    public void TickGestures()
    {
        var now = Environment.TickCount64;
        foreach (var contact in _contacts)
        {
            _gestures.NoteHold(contact.Id, contact.X, contact.Y, now);
        }

        if (_gestures.TryConsume(out var gesture))
        {
            HandleGesture(gesture);
        }
    }

    private void HandleGesture(GestureKind gesture)
    {
                switch (gesture)
        {
            case GestureKind.FistErase:
                // Held fist becomes an eraser until the contact lifts.
                if (_live.Count > 0)
                {
                    return;
                }

                StatusMessage = "Fist held: erasing.";
                _forcedErase = !_forcedErase;
                break;

            case GestureKind.PalmLaunch:
                StatusMessage = "Palm detected.";
                GestureRecognized?.Invoke(this, EventArgs.Empty);
                break;

            case GestureKind.WaveLeft:
                StatusMessage = "Swipe left: next page.";
                GestureRecognized?.Invoke(this, EventArgs.Empty);
                WavePageTurn?.Invoke(this, -1);
                break;

            case GestureKind.WaveRight:
                StatusMessage = "Swipe right: previous page.";
                GestureRecognized?.Invoke(this, EventArgs.Empty);
                WavePageTurn?.Invoke(this, 1);
                break;

            case GestureKind.PinchZoom:
                _gestures.ResetPinch();
                break;
        }
    }

    public event EventHandler<int>? WavePageTurn;

    private bool _forcedErase;

    // ---- stroke lifecycle --------------------------------------------------------

    private void BeginStroke(PointerSample s)
    {
        var kind = Tool switch
        {
            BoardTool.Highlighter => StrokeKind.Highlighter,
            BoardTool.Eraser => StrokeKind.Eraser,
            BoardTool.Line or BoardTool.Rectangle or BoardTool.Ellipse or BoardTool.Triangle
                or BoardTool.Arrow or BoardTool.Star or BoardTool.Polygon => StrokeKind.Shape,
            _ => StrokeKind.Pen,
        };

        if ((s.Eraser || _forcedErase) && kind != StrokeKind.Eraser)
        {
            kind = StrokeKind.Eraser;
        }

        var width = kind switch
        {
            StrokeKind.Highlighter => _strokeWidth * 4,
            StrokeKind.Eraser => Math.Max(8, _strokeWidth * 3),
            StrokeKind.Shape => Math.Max(2, _strokeWidth / 2),
            _ => _strokeWidth,
        };

        var stroke = new InkStroke
        {
            Kind = kind,
            ColorArgb = kind == StrokeKind.Eraser ? 0xFFFFFFFF : _colorArgb,
            Width = width,
            Opacity = kind == StrokeKind.Highlighter ? _highlighterOpacity : 1.0,
            LineStyle = _lineStyle,
            Fill = _shapeStyle,
            Shape = Tool switch
            {
                BoardTool.Rectangle => "rectangle",
                BoardTool.Ellipse => "ellipse",
                BoardTool.Triangle => "triangle",
                BoardTool.Arrow => "arrow",
                BoardTool.Star => "star",
                BoardTool.Polygon => "polygon",
                _ => "line",
            },
        };
        stroke.Points.Add(ToInkPoint(s));
        _live[s.Id] = stroke;
        InvalidateVisual();
    }

    private void ExtendStroke(PointerSample s)
    {
        if (!_live.TryGetValue(s.Id, out var stroke))
        {
            return;
        }

        if (stroke.Points.Count >= _engine.MaxStrokePoints)
        {
            return;
        }

        if (stroke.Kind == StrokeKind.Eraser)
        {
            ApplyEraser(s);
            return;
        }

        var last = stroke.Points[^1];
        var dx = s.X - last.X;
        var dy = s.Y - last.Y;
        if (Math.Sqrt((dx * dx) + (dy * dy)) < 0.9)
        {
            return;
        }

        stroke.Points.Add(ToInkPoint(s));
        InvalidateVisual();
    }

    private void ApplyEraser(PointerSample s)
    {
        var page = _document?.Active();
        if (page is null || page.Locked)
        {
            return;
        }

        var radius = Math.Max(10, _strokeWidth * 3);
        var removed = new RemoveStrokesCommand("Erase");
        var doomed = page.Strokes
            .Where(st => st.Points.Any(p =>
                Math.Sqrt(Math.Pow(p.X - s.X, 2) + Math.Pow(p.Y - s.Y, 2)) <= radius))
            .ToList();
        if (doomed.Count == 0)
        {
            return;
        }

        foreach (var stroke in doomed)
        {
            _ = removed;
            page.Strokes.Remove(stroke);
        }

        _committed.RemoveAll(st => doomed.Any(d => d.Id == st.Id));
        ReloadFromDocument();
        BoardChanged?.Invoke(this, EventArgs.Empty);
    }

    private InkPoint ToInkPoint(PointerSample s) => new()
    {
        X = s.X,
        Y = s.Y,
        Pressure = s.IsPen || s.IsTouch ? s.Pressure : 0.5,
        IsStylus = s.IsPen,
        IsEraser = s.Eraser,
        Tilt = s.Tilt,
        TimeTicks = DateTime.UtcNow.Ticks,
    };

    // ---- text ---------------------------------------------------------------------

    public void BeginTextAt(double x, double y)
    {
        var page = _document?.Active();
        if (page is null)
        {
            return;
        }

        _textSession = new TextEditSession
        {
            PageIndex = page.Index,
            X = x,
            Y = y,
            FontSize = Math.Max(16, _strokeWidth * 5),
            ColorArgb = _colorArgb,
        };
        TextEditingChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    public void TypeText(string text)
    {
        if (_textSession is not { IsEditing: true } session)
        {
            return;
        }

        session.Text += text;
        TextEditingChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    public void BackspaceText()
    {
        if (_textSession is not { IsEditing: true } session || session.Text.Length == 0)
        {
            return;
        }

        session.Text = session.Text[..^1];
        TextEditingChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    /// <summary>Commits any in-progress text to a stroke, or returns null when there is none.</summary>
    public IEditCommand? CommitText()
    {
        if (_textSession is not { IsEditing: true } session)
        {
            _textSession = null;
            return null;
        }

        _textSession = null;
        TextEditingChanged?.Invoke(this, EventArgs.Empty);
        if (string.IsNullOrEmpty(session.Text) || _document is null)
        {
            InvalidateVisual();
            return null;
        }

        var stroke = TextEditSession.BuildStroke(session);
        History.Execute(_document, new AddStrokesCommand("Add text", [stroke]));
        _committed.Add(stroke);
        _committedDrawings.Add(BuildDrawing(stroke));
        InvalidateVisual();
        BoardChanged?.Invoke(this, EventArgs.Empty);
        return new AddStrokesCommand("Add text", [stroke]);
    }

    // ---- document -----------------------------------------------------------------

    public void ReloadFromDocument()
    {
        _committed.Clear();
        var page = _document?.Active();
        if (page is not null)
        {
            _committed.AddRange(page.Strokes);
        }

        _cacheValid = false;
        InvalidateVisual();
    }

    private void RebuildCache()
    {
        _committedDrawings.Clear();
        foreach (var stroke in _committed)
        {
            _committedDrawings.Add(BuildDrawing(stroke));
        }

        _cacheValid = true;
    }

    // ---- rendering ------------------------------------------------------------------

    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (!_cacheValid)
        {
            RebuildCache();
        }

        dc.PushTransform(new TranslateTransform(ViewOffsetX, ViewOffsetY));
        dc.PushTransform(new ScaleTransform(ViewScale, ViewScale));

        var page = _document?.Active();
        var pageColor = page is null ? Color.FromRgb(0xFF, 0xFF, 0xFF)
            : Color.FromArgb((byte)((page.BackgroundColorArgb >> 24) & 0xFF),
                (byte)((page.BackgroundColorArgb >> 16) & 0xFF),
                (byte)((page.BackgroundColorArgb >> 8) & 0xFF),
                (byte)(page.BackgroundColorArgb & 0xFF));
        dc.DrawRectangle(new SolidColorBrush(pageColor), null, new Rect(0, 0, CanvasWidth, CanvasHeight));

        foreach (var image in page?.Images ?? new List<InkImage>())
        {
            DrawImage(dc, image);
        }

        if (page?.BackgroundImage is { } bg && LoadImage(bg) is { } background)
        {
            dc.DrawImage(background, new Rect(0, 0, CanvasWidth, CanvasHeight));
        }

        foreach (var drawing in _committedDrawings)
        {
            dc.DrawDrawing(drawing);
        }

        foreach (var stroke in _live.Values)
        {
            dc.DrawDrawing(BuildDrawing(stroke));
        }

        if (DrawSelection(dc))
        {
            // selection drawn
        }

        dc.Pop();
        dc.Pop();

        DrawTextSession(dc);
    }

    public double CanvasWidth => _document?.CanvasWidth ?? Math.Max(1, ActualWidth);

    public double CanvasHeight => _document?.CanvasHeight ?? Math.Max(1, ActualHeight);

    private void DrawImage(DrawingContext dc, InkImage image)
    {
        if (LoadImage(image.SourcePath) is not { } source)
        {
            // Missing file: draw a visible placeholder rather than failing the page.
            var placeholder = Brushes.LightGray;
            dc.DrawRectangle(placeholder, new Pen(Brushes.Gray, 1), image.Bounds);
            return;
        }

        dc.PushOpacity(image.Opacity);
        if (Math.Abs(image.RotationDegrees) < 0.01)
        {
            dc.DrawImage(source, image.Bounds);
        }
        else
        {
            var rotate = new RotateTransform(image.RotationDegrees, image.X, image.Y);
            dc.PushTransform(rotate);
            dc.DrawImage(source, image.Bounds);
            dc.Pop();
        }

        dc.Pop();
    }

    private ImageSource? LoadImage(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (_imageCache.TryGetValue(path, out var cached))
        {
            return cached;
        }

        try
        {
            if (!File.Exists(path))
            {
                _imageCache[path] = null!;
                return null;
            }

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            _imageCache[path] = bitmap;
            return bitmap;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UriFormatException
                                       or ArgumentException or System.Runtime.InteropServices.COMException
                                       or FileNotFoundException)
        {
            _imageCache[path] = null!;
            return null;
        }
    }

    private bool DrawSelection(DrawingContext dc)
    {
        if (!_selection.IsActive)
        {
            return false;
        }

        if (_selection.Shape == SelectionShape.Lasso && _selection.LassoPoints.Count > 1)
        {
            var figure = new PathFigure { StartPoint = _selection.LassoPoints[0], IsClosed = true, IsFilled = false };
            var poly = new PolyLineSegment(_selection.LassoPoints.Skip(1), true);
            figure.Segments.Add(poly);
            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            geometry.Freeze();
            dc.DrawGeometry(Brushes.Transparent, DashedPen(Brushes.DodgerBlue, 1.5),
                geometry);
        }
        else if (!_selection.Rectangle.IsEmpty)
        {
            var r = _selection.Rectangle;
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(40, 0x3D, 0x7E, 0xFF)),
                DashedPen(Brushes.DodgerBlue, 1.5), r);
        }

        foreach (var stroke in _selection.Strokes)
        {
            foreach (var p in stroke.Points)
            {
                dc.DrawEllipse(Brushes.Transparent, new Pen(Brushes.DodgerBlue, 1),
                    new Point(p.X, p.Y), 4, 4);
            }
        }

        return true;
    }

    private void DrawTextSession(DrawingContext dc)
    {
        if (_textSession is not { } session || !session.IsEditing)
        {
            return;
        }

        var text = string.IsNullOrEmpty(session.Text) ? " " : session.Text;
        var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal,
            FontStretches.Normal);
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var brush = new SolidColorBrush(ColorFromArgb(session.ColorArgb));
        var formatted = new FormattedText(text, System.Globalization.CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight, typeface, session.FontSize, brush, dpi);

        dc.PushTransform(new TranslateTransform(ViewOffsetX, ViewOffsetY));
        dc.PushTransform(new ScaleTransform(ViewScale, ViewScale));
        var width = session.Text.Length == 0 ? 8 : formatted.Width;
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(30, 0x3D, 0x7E, 0xFF)), null,
            new Rect(session.X, session.Y, width + 6, session.FontSize * 1.4));
        dc.DrawText(formatted, new Point(session.X + 3, session.Y + 3));

        // Caret.
        var caretX = session.X + 3 + width;
        dc.DrawRectangle(Brushes.Black, null, new Rect(caretX, session.Y + 2, 1.5, session.FontSize));
        dc.Pop();
        dc.Pop();
    }

    private Drawing BuildDrawing(InkStroke stroke)
    {
        if (stroke.Points.Count == 0)
        {
            return EmptyDrawing();
        }

        if (stroke.Kind is StrokeKind.Shape or StrokeKind.Text)
        {
            return BuildShapeDrawing(stroke);
        }

        var geometry = _engine.BuildOutline(stroke.Points, stroke.Width, 1.0);
        if (stroke.LineStyle != LineStyle.Solid)
        {
            return new GeometryDrawing(null, BuildPen(stroke), geometry);
        }

        return new GeometryDrawing(_engine.BrushFor(stroke), null, geometry);
    }

    private Pen BuildPen(InkStroke stroke)
    {
        var pen = new Pen(_engine.BrushFor(stroke), Math.Max(1, stroke.Width))
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
            DashStyle = stroke.LineStyle switch
            {
                LineStyle.Dashed => new DashStyle([6, 4], 0),
                LineStyle.Dotted => new DashStyle([1, 3], 0),
                _ => new DashStyle(),
            },
        };
        pen.Freeze();
        return pen;
    }

    private Drawing BuildShapeDrawing(InkStroke stroke)
    {
        if (stroke.Points.Count < 2)
        {
            return EmptyDrawing();
        }

        var a = stroke.Points[0];
        var b = stroke.Points[^1];
        var rect = new Rect(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(b.X - a.X),
            Math.Abs(b.Y - a.Y));

        Geometry geometry = stroke.Shape switch
        {
            "rectangle" => new RectangleGeometry(rect, 0, 0),
            "ellipse" => new EllipseGeometry(CenterOf(rect), rect.Width / 2, rect.Height / 2),
            "triangle" => BuildTriangle(rect),
            "arrow" => BuildArrow(ToPoint(a), ToPoint(b)),
            "star" => BuildStar(rect, 5),
            "polygon" => BuildPolygon(stroke),
            "text" => BuildTextGlyphs(stroke),
            _ => new LineGeometry(new Point(a.X, a.Y), new Point(b.X, b.Y)),
        };
        geometry.Freeze();

        var pen = BuildPen(stroke);
        if (stroke.Fill == ShapeStyle.Filled || stroke.Fill == ShapeStyle.FilledAndOutlined)
        {
            return new GeometryDrawing(_engine.BrushFor(stroke),
                stroke.Fill == ShapeStyle.FilledAndOutlined ? pen : null, geometry);
        }

        return new GeometryDrawing(null, pen, geometry);
    }

    private static Geometry BuildTriangle(Rect r)
    {
        var figure = new PathFigure { StartPoint = new Point(r.X + (r.Width / 2), r.Y), IsClosed = true };
        figure.Segments.Add(new LineSegment(new Point(r.Right, r.Bottom), true));
        figure.Segments.Add(new LineSegment(new Point(r.X, r.Bottom), true));
        var g = new PathGeometry();
        g.Figures.Add(figure);
        return g;
    }

    private static Geometry BuildArrow(Point a, Point b)
    {
        var figure = new PathFigure { StartPoint = new Point(a.X, a.Y), IsClosed = false };
        figure.Segments.Add(new LineSegment(b, true));
        var head = 10.0;
        var angle = Math.Atan2(b.Y - a.Y, b.X - a.X);
        var spread = Math.PI / 7;
        figure.Segments.Add(new LineSegment(
            new Point(b.X - (head * Math.Cos(angle - spread)), b.Y - (head * Math.Sin(angle - spread))), true));
        figure.Segments.Add(new LineSegment(b, true));
        figure.Segments.Add(new LineSegment(
            new Point(b.X - (head * Math.Cos(angle + spread)), b.Y - (head * Math.Sin(angle + spread))), true));
        var g = new PathGeometry();
        g.Figures.Add(figure);
        return g;
    }

    private static Geometry BuildStar(Rect r, int points)
    {
        var center = CenterOf(r);
        var cx = center.X;
        var cy = center.Y;
        var outer = Math.Min(r.Width, r.Height) / 2;
        var inner = outer * 0.45;
        var figure = new PathFigure { StartPoint = new Point(cx, cy - outer), IsClosed = true };
        for (var i = 1; i < points * 2; i++)
        {
            var angle = -Math.PI / 2 + (i * Math.PI / points);
            var radius = i % 2 == 0 ? outer : inner;
            figure.Segments.Add(new LineSegment(
                new Point(cx + (radius * Math.Cos(angle)), cy + (radius * Math.Sin(angle))), true));
        }

        var g = new PathGeometry();
        g.Figures.Add(figure);
        return g;
    }

    private static Geometry BuildPolygon(InkStroke stroke)
    {
        var figure = new PathFigure { StartPoint = ToPoint(stroke.Points[0]), IsClosed = true };
        for (var i = 1; i < stroke.Points.Count; i++)
        {
            figure.Segments.Add(new LineSegment(ToPoint(stroke.Points[i]), true));
        }

        var g = new PathGeometry();
        g.Figures.Add(figure);
        return g;
    }

    private Geometry BuildTextGlyphs(InkStroke stroke)
    {
        var session = TextEditSession.FromStroke(stroke);
        var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal,
            FontStretches.Normal);
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var brush = _engine.BrushFor(stroke);
        var formatted = new FormattedText(session.Text ?? string.Empty,
            System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            typeface, session.FontSize, brush, dpi);
        var geometry = new RectangleGeometry(
            new Rect(session.X, session.Y, Math.Max(1, formatted.Width), session.FontSize * 1.25));
        return geometry;
    }

    private static Point ToPoint(InkPoint p) => new Point(p.X, p.Y);

    private static Point CenterOf(Rect r) => new Point(r.X + (r.Width / 2), r.Y + (r.Height / 2));

    private static Pen DashedPen(Brush brush, double thickness)
    {
        var pen = new Pen(brush, thickness) { DashStyle = new DashStyle([4, 3], 0) };
        pen.Freeze();
        return pen;
    }

    private static Drawing EmptyDrawing() =>
        new GeometryDrawing(Brushes.Transparent, null, Geometry.Empty);

    private static Color ColorFromArgb(uint argb) => Color.FromRgb(
        (byte)((argb >> 16) & 0xFF),
        (byte)((argb >> 8) & 0xFF),
        (byte)(argb & 0xFF));

    private readonly record struct ActiveContact(int Id, PointerInputType Type, double X, double Y,
        long StartTicks);

    private sealed class HistorySink(InkSurface surface) : IEditCommandSink
    {
        public void Execute(BoardDocument document, IEditCommand command) =>
            surface.History.Execute(document, command);
    }

    // ---- WPF stylus / touch / mouse fallback ---------------------------------------

    protected override void OnStylusDown(StylusDownEventArgs e)
    {
        base.OnStylusDown(e);
        if (HasPointerTarget)
        {
            return;
        }

        var p = e.GetPosition(this);
        OnDown(ElementSample(PointerInputType.Pen, e.StylusDevice.Id, p, e.Inverted, 0.5));
        e.Handled = true;
    }

    protected override void OnStylusMove(StylusEventArgs e)
    {
        base.OnStylusMove(e);
        if (HasPointerTarget || !_live.ContainsKey(e.StylusDevice.Id))
        {
            return;
        }

        foreach (var sp in e.GetStylusPoints(this))
        {
            var pressure = sp.PressureFactor <= 0 ? 0.5 : sp.PressureFactor;
            _engine.RecordPressure(e.StylusDevice.Id, pressure);
            OnMove(ElementSample(PointerInputType.Pen, e.StylusDevice.Id,
                new Point(sp.X, sp.Y), e.Inverted, pressure));
        }

        e.Handled = true;
    }

    protected override void OnStylusUp(StylusEventArgs e)
    {
        base.OnStylusUp(e);
        if (HasPointerTarget)
        {
            return;
        }

        var p = e.GetPosition(this);
        OnUp(ElementSample(PointerInputType.Pen, e.StylusDevice.Id, p, e.Inverted, 0.5));
        e.Handled = true;
    }

    protected override void OnStylusInAirMove(StylusEventArgs e)
    {
        base.OnStylusInAirMove(e);
        if (!HasPointerTarget)
        {
            _engine.RecordPressure(e.StylusDevice.Id, 0);
        }
    }

    protected override void OnTouchDown(TouchEventArgs e)
    {
        base.OnTouchDown(e);
        if (HasPointerTarget)
        {
            return;
        }

        var p = e.GetTouchPoint(this).Position;
        OnDown(ElementSample(PointerInputType.Touch, e.TouchDevice.Id, p, false, 0.5));
        e.Handled = true;
    }

    protected override void OnTouchMove(TouchEventArgs e)
    {
        base.OnTouchMove(e);
        if (HasPointerTarget || !_live.ContainsKey(e.TouchDevice.Id))
        {
            return;
        }

        var p = e.GetTouchPoint(this).Position;
        OnMove(ElementSample(PointerInputType.Touch, e.TouchDevice.Id, p, false, 0.5));
        e.Handled = true;
    }

    protected override void OnTouchUp(TouchEventArgs e)
    {
        base.OnTouchUp(e);
        if (HasPointerTarget)
        {
            return;
        }

        var p = e.GetTouchPoint(this).Position;
        OnUp(ElementSample(PointerInputType.Touch, e.TouchDevice.Id, p, false, 0.5));
        e.Handled = true;
    }

    private const int MouseStrokeId = -1;

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        if (HasPointerTarget || e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        Focus();
        OnDown(ElementSample(PointerInputType.Mouse, MouseStrokeId, e.GetPosition(this), false, 0.5));
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (HasPointerTarget || e.LeftButton != MouseButtonState.Pressed ||
            !_live.ContainsKey(MouseStrokeId))
        {
            return;
        }

        OnMove(ElementSample(PointerInputType.Mouse, MouseStrokeId, e.GetPosition(this), false, 0.5));
        e.Handled = true;
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (HasPointerTarget || e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        OnUp(ElementSample(PointerInputType.Mouse, MouseStrokeId, e.GetPosition(this), false, 0.5));
        e.Handled = true;
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);
        ContextMenuRequested?.Invoke(this, e.GetPosition(this));
    }

    public event EventHandler<Point>? ContextMenuRequested;

    /// <summary>
    /// Raised for every contact sample from either input path, including samples the surface
    /// rejected. The hardware acceptance harness subscribes to this so it measures the same
    /// pipeline the teacher draws with, rather than a parallel one that could pass while the
    /// real one fails.
    /// </summary>
    public event EventHandler<SurfaceSampleEventArgs>? SampleObserved;

    private PointerSample ElementSample(PointerInputType type, int id, Point element, bool inverted,
        double pressure) => new(type, id, element.X, element.Y, pressure, true, true, inverted, false,
            double.NaN, double.NaN);
}

/// <summary>A single observed contact sample, plus the engine's decision about it.</summary>
public sealed class SurfaceSampleEventArgs : EventArgs
{
    public SurfaceSampleEventArgs(PointerSample sample, bool rejected, double normalizedPressure)
    {
        Sample = sample;
        Rejected = rejected;
        NormalizedPressure = normalizedPressure;
    }

    public PointerSample Sample { get; }

    /// <summary>True when the surface dropped the sample, for example palm rejection.</summary>
    public bool Rejected { get; }

    /// <summary>Pressure after the per-device envelope, so 0..1 is comparable across pens.</summary>
    public double NormalizedPressure { get; }
}
