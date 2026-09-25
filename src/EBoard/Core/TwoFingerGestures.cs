using System;
using System.Collections.Generic;
using System.Linq;

namespace EBoard.Core;

/// <summary>
/// Two-finger gesture recognition: pinch to scale, twist to rotate, drag to pan.
///
/// The vendor manual gives the discrimination rule explicitly (section 3.6): a rotation is two
/// fingers on the object, diagonal, with the horizontal separation under 2 cm while the
/// vertical separation is over 2 cm. Anything else that is two-finger is a scale or pan. This
/// encodes that rule rather than guessing at thresholds.
///
/// Pinch and rotate need the pointer *frame* API, not the per-pointer messages, because both
/// fingers must be read from the same frame; mixing WM_POINTER updates from two contacts
/// produces a laggy, jittery result.
/// </summary>
public sealed class TwoFingerGestureRecognizer
{
    /// <summary>Horizontal separation below this and vertical above it reads as a rotation.</summary>
    public double RotationHorizontalMax { get; set; } = 2.0;

    public double RotationVerticalMin { get; set; } = 2.0;

    /// <summary>Minimum contact separation before a pinch registers, to reject a resting hand.</summary>
    public double MinSeparation { get; set; } = 24.0;

    /// <summary>Scale change required before a pinch is reported, as a fraction.</summary>
    public double PinchThreshold { get; set; } = 0.06;

    /// <summary>Rotation change required before a twist is reported, in degrees.</summary>
    public double RotateThresholdDegrees { get; set; } = 4.0;

    /// <summary>Centre movement required before a two-finger pan is reported, in pixels.</summary>
    public double PanThreshold { get; set; } = 12.0;

    private readonly Dictionary<int, GesturePoint> _active = new();
    private GestureFrame? _start;
    private GestureKind _mode = GestureKind.None;
    private bool _committed;

    private sealed record GesturePoint(int Id, double X, double Y);

    private sealed record GestureFrame(double Cx, double Cy, double Distance, double AngleDegrees);

    public bool IsTracking => _active.Count >= 2;

    public GestureKind CurrentMode => _mode;

    public int ContactCount => _active.Count;

    public void Begin(int id, double x, double y)
    {
        _active[id] = new GesturePoint(id, x, y);
        if (_active.Count == 2)
        {
            _start = Measure()!;
            _mode = Classify(_start);
            _committed = false;
        }
    }

    public void Update(int id, double x, double y)
    {
        if (_active.ContainsKey(id))
        {
            _active[id] = new GesturePoint(id, x, y);

            // Re-classify as the fingers move, so a contact pair that starts ambiguous is
            // reported as soon as it clearly becomes a scale or a rotation.
            if (_active.Count == 2)
            {
                var measured = Measure();
                var mode = Classify(measured);
                if (mode != _mode)
                {
                    _mode = mode;
                    _start = measured;
                    _committed = false;
                }
            }
        }
    }

    public void End(int id)
    {
        _active.Remove(id);
        if (_active.Count < 2)
        {
            Reset();
        }
    }

    public void Reset()
    {
        _active.Clear();
        _start = null;
        _mode = GestureKind.None;
        _committed = false;
    }

    private GestureFrame? Measure()
    {
        if (_active.Count < 2)
        {
            return null;
        }

        var pts = _active.Values.Take(2).ToList();
        var dx = pts[1].X - pts[0].X;
        var dy = pts[1].Y - pts[0].Y;
        return new GestureFrame(
            (pts[0].X + pts[1].X) / 2,
            (pts[0].Y + pts[1].Y) / 2,
            Math.Sqrt((dx * dx) + (dy * dy)),
            Math.Atan2(dy, dx) * 180 / Math.PI);
    }

    /// <summary>
    /// Applies the manual's rule: diagonal contacts with a narrow horizontal and wide vertical
    /// gap are a rotation; anything else with two contacts is a scale or pan.
    /// </summary>
    private GestureKind Classify(GestureFrame? frame)
    {
        if (frame is null)
        {
            return GestureKind.None;
        }

        var pts = _active.Values.Take(2).ToList();
        var dx = Math.Abs(pts[1].X - pts[0].X);
        var dy = Math.Abs(pts[1].Y - pts[0].Y);
        if (dx < RotationHorizontalMax && dy > RotationVerticalMin)
        {
            return GestureKind.Rotate;
        }

        return frame.Distance >= MinSeparation ? GestureKind.Scale : GestureKind.None;
    }

    /// <summary>
    /// Produces a gesture for the current frame, or null when nothing has moved far enough.
    /// One gesture is emitted per two-finger contact, so the caller does not have to debounce.
    /// </summary>
    public TwoFingerGesture? Consume()
    {
        if (_active.Count < 2 || _start is null || _committed)
        {
            return null;
        }

        var current = Measure();
        if (current is null)
        {
            return null;
        }

        // The mode is maintained by Update, so a re-classification here means only that the
        // separation crossed a threshold without an intervening Update.
        var mode = _mode;
        if (mode == GestureKind.None)
        {
            return null;
        }

        switch (mode)
        {
            case GestureKind.Scale:
            {
                if (_start.Distance < MinSeparation || current.Distance < MinSeparation)
                {
                    return null;
                }

                var scale = current.Distance / _start.Distance;
                if (Math.Abs(scale - 1) < PinchThreshold)
                {
                    return null;
                }

                _committed = true;
                return new TwoFingerGesture(GestureKind.Scale, current.Cx, current.Cy, scale, 0);
            }

            case GestureKind.Rotate:
            {
                var delta = NormalizeDegrees(current.AngleDegrees - _start.AngleDegrees);
                if (Math.Abs(delta) < RotateThresholdDegrees)
                {
                    return null;
                }

                _committed = true;
                return new TwoFingerGesture(GestureKind.Rotate, current.Cx, current.Cy, 1, delta);
            }

            case GestureKind.None:
            {
                var dx = current.Cx - _start.Cx;
                var dy = current.Cy - _start.Cy;
                if (Math.Sqrt((dx * dx) + (dy * dy)) < PanThreshold)
                {
                    return null;
                }

                _committed = true;
                return new TwoFingerGesture(GestureKind.Pan, current.Cx, current.Cy, 1, 0)
                {
                    DeltaX = dx,
                    DeltaY = dy,
                };
            }
        }

        return null;
    }

    private static double NormalizeDegrees(double degrees)
    {
        while (degrees > 180)
        {
            degrees -= 360;
        }

        while (degrees < -180)
        {
            degrees += 360;
        }

        return degrees;
    }
}

/// <summary>A recognized two-finger gesture, in board coordinates.</summary>
public readonly record struct TwoFingerGesture(
    GestureKind Kind,
    double CenterX,
    double CenterY,
    double Scale,
    double RotationDegrees)
{
    public double DeltaX { get; init; }

    public double DeltaY { get; init; }

    public override string ToString() => Kind switch
    {
        GestureKind.Scale => $"scale {Scale:F2} at ({CenterX:F0},{CenterY:F0})",
        GestureKind.Rotate => $"rotate {RotationDegrees:F1} deg at ({CenterX:F0},{CenterY:F0})",
        GestureKind.Pan => $"pan ({DeltaX:F0},{DeltaY:F0})",
        _ => "none",
    };
}
