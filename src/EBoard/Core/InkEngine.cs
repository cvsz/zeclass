using System.Windows;
using System.Windows.Media;

namespace EBoard.Core;

public enum BoardTool
{
    Pen = 0,
    Highlighter = 1,
    Eraser = 2,
    Line = 3,
    Rectangle = 4,
    Ellipse = 5,
    Arrow = 6,
    Text = 7,
    Select = 8,
    Pan = 9,
    Lasso = 10,
    Triangle = 11,
    Star = 12,
    Polygon = 13,
}

/// <summary>
/// Converts a pointer sample into board-space ink. Handles palm rejection, pressure
/// normalization, and per-device width scaling so a passive digitizer still produces
/// usable line weight variation.
/// </summary>
public sealed class InkEngine
{
    private readonly Dictionary<PressureCalibration, PressureRange> _pressureRanges = new();
    private readonly HashSet<int> _activeStylusIds = new();
    private readonly HashSet<int> _activeTouchIds = new();
    private readonly List<InkPoint> _pending = new();

    public double MinWidthRatio { get; set; } = 0.35;

    public bool PalmRejectionEnabled { get; set; } = true;

    public double PressureScale { get; set; } = 1.0;

    /// <summary>Ink never thins to nothing, so a light touch still leaves a visible line.</summary>
    public double MinimumNormalizedPressure { get; set; } = 0.12;

    /// <summary>Pressure span a digitizer must show before its samples are trusted.</summary>
    public const double MinimumUsableSpan = 0.02;

    public int MaxStrokePoints { get; set; } = 200_000;

    /// <summary>Width used when the digitizer reports no usable pressure.</summary>
    public double DefaultPressure { get; set; } = 0.5;

    public double MinimumWidthPx { get; set; } = 0.5;

    public int ActiveStylusCount => _activeStylusIds.Count;

    public int ActiveTouchCount => _activeTouchIds.Count;

    public void NoteStylusDown(int stylusId) => _activeStylusIds.Add(stylusId);

    public void NoteStylusUp(int stylusId) => _activeStylusIds.Remove(stylusId);

    public void NoteTouchDown(int touchId) => _activeTouchIds.Add(touchId);

    public void NoteTouchUp(int touchId) => _activeTouchIds.Remove(touchId);

    public void Reset() => _pending.Clear();

    /// <summary>
    /// A touch contact is dropped while any pen contact is active, so a resting hand does
    /// not scribble on the board. Touch still works when no pen is in range.
    /// </summary>
    public bool ShouldIgnoreTouch() => PalmRejectionEnabled && _activeStylusIds.Count > 0;

    /// <summary>
    /// Tracks the observed pressure envelope for one device. Pen digitizers report a raw
    /// range that varies per model (and a contact-only digitizer reports a single value), so
    /// the first samples establish the floor and later samples raise the ceiling.
    /// </summary>
    public void RecordPressure(int deviceId, double rawPressure)
    {
        var key = new PressureCalibration(deviceId);
        if (!_pressureRanges.TryGetValue(key, out var range))
        {
            _pressureRanges[key] = new PressureRange(rawPressure, rawPressure);
            return;
        }

        _pressureRanges[key] = range.Extend(rawPressure);
    }

    public double NormalizePressure(int deviceId, double rawPressure)
    {
        if (rawPressure <= 0)
        {
            return 0;
        }

        if (!_pressureRanges.TryGetValue(new PressureCalibration(deviceId), out var range))
        {
            return 1.0;
        }

        var normalized = range.Normalize(rawPressure);
        return Math.Clamp(Math.Max(normalized, MinimumNormalizedPressure) * PressureScale, 0.0, 1.0);
    }

    /// <summary>True once enough variation has been seen to trust the normalized value.</summary>
    public bool HasCalibration(int deviceId) =>
        _pressureRanges.TryGetValue(new PressureCalibration(deviceId), out var r) &&
        r.Max - r.Min >= MinimumUsableSpan;

    public InkPoint CreateSample(Point pos, double rawPressure, bool isStylus, bool isEraser,
        double tilt = double.NaN, int deviceId = 0)
    {
        if (isStylus)
        {
            RecordPressure(deviceId, rawPressure);
        }

        return new InkPoint
        {
            X = pos.X,
            Y = pos.Y,
            Pressure = isStylus ? NormalizePressure(deviceId, rawPressure) : 0.5,
            IsStylus = isStylus,
            IsEraser = isEraser,
            Tilt = tilt,
            TimeTicks = DateTime.UtcNow.Ticks,
        };
    }

    /// <summary>Width for a sample, blending tool base width with pressure.</summary>
    public double EffectiveWidth(double baseWidth, InkPoint sample)
    {
        // A reported pressure of zero means "no data" (contact-only digitizer, mouse, or the
        // WPF fallback), not "zero force", so mid-scale is the honest interpretation.
        var pressure = sample.Pressure <= 0 ? DefaultPressure : Math.Clamp(sample.Pressure, 0, 1);
        var ratio = MinWidthRatio + (1.0 - MinWidthRatio) * pressure;
        return Math.Max(MinimumWidthPx, baseWidth * ratio);
    }

    public SolidColorBrush BrushFor(InkStroke stroke)
    {
        var color = Color.FromArgb(
            (byte)Math.Round(Math.Clamp(stroke.Opacity, 0, 1) * 255),
            (byte)((stroke.ColorArgb >> 16) & 0xFF),
            (byte)((stroke.ColorArgb >> 8) & 0xFF),
            (byte)(stroke.ColorArgb & 0xFF));
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// Builds the outline for a stroke as a filled geometry, so variable width renders as a
    /// smooth ribbon instead of a chain of overlapping circles.
    /// </summary>
    public StreamGeometry BuildOutline(IReadOnlyList<InkPoint> points, double baseWidth, double minimumWidth)
    {
        var geometry = new StreamGeometry();
        if (points.Count == 0)
        {
            return geometry;
        }

        using (var ctx = geometry.Open())
        {
            if (points.Count == 1)
            {
                var r = Math.Max(minimumWidth, baseWidth * MinWidthRatio) / 2.0;
                ctx.BeginFigure(new Point(points[0].X - r, points[0].Y - r), true, true);
                ctx.LineTo(new Point(points[0].X + r, points[0].Y - r), true, true);
                ctx.LineTo(new Point(points[0].X + r, points[0].Y + r), true, true);
                ctx.LineTo(new Point(points[0].X - r, points[0].Y + r), true, true);
            }
            else
            {
                var left = new List<Point>(points.Count);
                var right = new List<Point>(points.Count);
                for (var i = 0; i < points.Count; i++)
                {
                    ComputeSide(points, i, baseWidth, minimumWidth, left, right);
                }

                ctx.BeginFigure(SmoothedPath(left), false, false);
                AppendSmoothed(ctx, left, true);
                for (var i = right.Count - 1; i >= 0; i--)
                {
                    ctx.LineTo(right[i], true, false);
                }

                ctx.LineTo(left[0], true, false);
            }
        }

        geometry.Freeze();
        return geometry;
    }

    private void ComputeSide(IReadOnlyList<InkPoint> points, int i, double baseWidth,
        double minimumWidth, List<Point> left, List<Point> right)
    {
        var p = points[i];
        Point dir;
        if (i == 0)
        {
            dir = new Point(points[1].X - p.X, points[1].Y - p.Y);
        }
        else if (i == points.Count - 1)
        {
            dir = new Point(p.X - points[i - 1].X, p.Y - points[i - 1].Y);
        }
        else
        {
            dir = new Point(points[i + 1].X - points[i - 1].X, points[i + 1].Y - points[i - 1].Y);
        }

        var len = Math.Sqrt(dir.X * dir.X + dir.Y * dir.Y);
        if (len < 1e-6)
        {
            dir = new Point(1, 0);
            len = 1;
        }

        var half = Math.Max(minimumWidth, EffectiveWidth(baseWidth, p)) / 2.0;
        var nx = -dir.Y / len * half;
        var ny = dir.X / len * half;
        left.Add(new Point(p.X + nx, p.Y + ny));
        right.Add(new Point(p.X - nx, p.Y - ny));
    }

    private static Point SmoothedPath(List<Point> side) => side[0];

    private static void AppendSmoothed(StreamGeometryContext ctx, List<Point> side, bool start)
    {
        for (var i = 1; i < side.Count - 1; i++)
        {
            var mid = new Point(
                (side[i].X + side[i + 1].X) / 2.0,
                (side[i].Y + side[i + 1].Y) / 2.0);
            ctx.LineTo(mid, true, false);
        }

        if (side.Count > 1)
        {
            ctx.LineTo(side[^1], true, false);
        }
    }

    private readonly record struct PressureCalibration(int DeviceId);

    private readonly record struct PressureRange(double Min, double Max)
    {
        public PressureRange Extend(double sample) =>
            sample > Max ? new PressureRange(Min, sample) : this;

        public double Normalize(double sample)
        {
            var span = Max - Min;
            return span < InkEngine.MinimumUsableSpan ? 1.0 : (sample - Min) / span;
        }
    }
}
