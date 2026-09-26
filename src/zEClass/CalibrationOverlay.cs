using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using zEClass.Core;

namespace zEClass;

/// <summary>
/// Four-target calibration overlay. Shows one target at a time, records where the operator
/// actually hit, then reports the resulting digitizer-to-board transform and its worst-case
/// error. This is the procedure the vendor manual documents: four crosses, held briefly each.
/// </summary>
public sealed class CalibrationOverlay : FrameworkElement
{
    private const double MarkerSize = 44;
    private readonly List<CalibrationPoint> _captures = new();
    private Calibration? _session;
    private int _targetIndex;
    private bool _finished;
    private CalibrationTransform? _transform;
    private double _error = double.NaN;

    public CalibrationOverlay()
    {
        Focusable = true;
        IsHitTestVisible = true;
    }

    /// <summary>Board surface size in DIPs; targets are laid out inside this.</summary>
    public Size SurfaceSize { get; set; } = new(1920, 1080);

    public string Title { get; set; } = "Calibrate the board";

    public event EventHandler? StateChanged;

    public int TargetIndex => _targetIndex;

    public int TargetCount => Calibration.TargetCount;

    public bool IsFinished => _finished;

    public IReadOnlyList<CalibrationPoint> Captures => _captures;

    public CalibrationPoint? CurrentTarget =>
        _targetIndex < _session?.Targets.Count ? _session.Targets[_targetIndex] : null;

    public double ErrorPixels => _error;

    public void Begin(int surfaceWidth, int surfaceHeight)
    {
        _session = Calibration.CreateForDisplay(surfaceWidth, surfaceHeight);
        _captures.Clear();
        _targetIndex = 0;
        _finished = false;
        _transform = null;
        _error = double.NaN;
                InvalidateVisual();
        RaiseStateChanged();
    }

    /// <summary>Records a hit at the given board coordinate for the current target.</summary>
    public void RecordCapture(double x, double y)
    {
        if (_finished || _session is null || _targetIndex >= _session.Targets.Count)
        {
            return;
        }

        _captures.Add(new CalibrationPoint(x, y));
        _session.Captures = _captures.ToList();
        _targetIndex++;

        if (_targetIndex >= Calibration.TargetCount)
        {
            Finish();
        }
        else
        {
            InvalidateVisual();
            RaiseStateChanged();
        }
    }

    /// <summary>
    /// Computes the transform and the worst-case residual. A poor capture is reported rather
    /// than accepted silently, because a bad calibration is worse than none: it makes every
    /// stroke land somewhere consistently wrong.
    /// </summary>
    public bool Finish()
    {
        _session ??= Calibration.CreateForDisplay((int)SurfaceSize.Width, (int)SurfaceSize.Height);
        _session.Captures = _captures.ToList();

        var transform = BuildTransform(_session);
        if (transform is null)
        {
            _finished = true;
            _transform = null;
            _error = double.NaN;
            RaiseStateChanged();
            return false;
        }

        _transform = transform;
        _error = ComputeWorstResidual(_session, transform);
        _finished = true;
        InvalidateVisual();
        RaiseStateChanged();
        return true;
    }

    /// <summary>
    /// Fits the digitizer-to-board transform from a capture, or null when the capture is
    /// degenerate. Exposed on the overlay so tests and the window share one implementation.
    /// </summary>
    public static CalibrationTransform? BuildTransform(Calibration calibration)
    {
        ArgumentNullException.ThrowIfNull(calibration);
        if (!calibration.IsComplete)
        {
            return null;
        }

        // Solve board -> digitizer with the standard 8-parameter fit, then invert it.
        return ProjectiveSolver.SolveProjective(calibration.Targets, calibration.Captures)?.Invert();
    }

    public static double ComputeWorstResidual(Calibration calibration, CalibrationTransform transform)
    {
        if (!calibration.IsComplete)
        {
            return double.NaN;
        }

        var worst = 0.0;
        for (var i = 0; i < Calibration.TargetCount; i++)
        {
            var mapped = transform.Apply(calibration.Captures[i]);
            if (!mapped.IsFinite)
            {
                return double.NaN;
            }

            var dx = mapped.X - calibration.Targets[i].X;
            var dy = mapped.Y - calibration.Targets[i].Y;
            worst = Math.Max(worst, Math.Sqrt((dx * dx) + (dy * dy)));
        }

        return worst;
    }

    /// <summary>Residual below this is indistinguishable from a perfect hit.</summary>
    public const double AcceptableErrorPixels = 12.0;

    public bool IsAcceptable => _transform is not null && _error <= AcceptableErrorPixels;

    public Calibration? BuildCalibration() => _session;

    public CalibrationTransform? Result => _transform;

    private void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

    // ---- input ------------------------------------------------------------------

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.ChangedButton == MouseButton.Left)
        {
            RecordCapture(e.GetPosition(this).X, e.GetPosition(this).Y);
            e.Handled = true;
        }
    }

    protected override void OnTouchDown(TouchEventArgs e)
    {
        base.OnTouchDown(e);
        var p = e.GetTouchPoint(this).Position;
        RecordCapture(p.X, p.Y);
        e.Handled = true;
    }

    protected override void OnStylusDown(StylusDownEventArgs e)
    {
        base.OnStylusDown(e);
        var p = e.GetPosition(this);
        RecordCapture(p.X, p.Y);
        e.Handled = true;
    }

    // ---- rendering --------------------------------------------------------------

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, ActualWidth, ActualHeight));

        if (_session is null || _finished)
        {
            DrawFinished(dc);
            return;
        }

        var target = CurrentTarget;
        if (target is null)
        {
            return;
        }

        var brush = new SolidColorBrush(Color.FromRgb(0x3D, 0x7E, 0xFF));
        var white = Brushes.White;
        var r = MarkerSize / 2;

        // Target cross.
        dc.DrawLine(new Pen(white, 2), new Point(target.X - r, target.Y), new Point(target.X + r, target.Y));
        dc.DrawLine(new Pen(white, 2), new Point(target.X, target.Y - r), new Point(target.X, target.Y + r));
        dc.DrawEllipse(null, new Pen(white, 2),
            new Point(target.X, target.Y), r, r);
        dc.DrawEllipse(brush, null, new Point(target.X, target.Y), 6, 6);

        // Previously captured points, so the operator can see progress and spot a bad hit.
        for (var i = 0; i < _captures.Count; i++)
        {
            var c = _captures[i];
            dc.DrawEllipse(null, new Pen(Brushes.Green, 2), new Point(c.X, c.Y), 12, 12);
            var t = _session.Targets[i];
            dc.DrawLine(new Pen(Brushes.Green, 1) { DashStyle = new DashStyle([2, 3], 0) },
                new Point(c.X, c.Y), new Point(t.X, t.Y));
        }

        DrawBanner(dc,
            $"{Title}\nTarget {_targetIndex + 1} of {Calibration.TargetCount} - " +
            "touch the centre of the cross with the pen or your finger");
    }

    private void DrawFinished(DrawingContext dc)
    {
        if (_transform is null)
        {
            DrawBanner(dc,
                "Calibration failed.\nThe captures were too close together or on a line.\n" +
                "Recalibrate and spread the hits across all four targets.");
            return;
        }

        var verdict = IsAcceptable ? "Looks good." : "Usable, but check the accuracy.";
        DrawBanner(dc,
            $"Calibration complete.\nWorst error: {_error:F1} px\n{verdict}");
    }

    private void DrawBanner(DrawingContext dc, string text)
    {
        var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal,
            FontStretches.Normal);
        var lines = text.Split('\n');
        var size = 26.0;
        var brush = Brushes.White;
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this).PixelsPerDip;

        for (var i = 0; i < lines.Length; i++)
        {
            var formatted = new FormattedText(lines[i], System.Globalization.CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight, typeface, size, brush, dpi);
            dc.DrawText(formatted, new Point(60, 60 + (i * (size * 1.35))));
        }
    }
}
