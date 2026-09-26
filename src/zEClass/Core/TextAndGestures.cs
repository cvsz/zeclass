using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;

namespace zEClass.Core;

public enum LineStyle
{
    Solid = 0,
    Dashed = 1,
    Dotted = 2,
}

public enum ShapeStyle
{
    Outline = 0,
    Filled = 1,
    FilledAndOutlined = 2,
}

/// <summary>
/// In-progress text on the board. The vendor manual describes this as "click anywhere on the
/// whiteboard page to use keyboard to input letters", so text is anchored at a point rather
/// than flowing in a text box, and it becomes a normal stroke once committed.
/// </summary>
public sealed class TextEditSession
{
    public int PageIndex { get; set; }

    public double X { get; set; }

    public double Y { get; set; }

    public string Text { get; set; } = string.Empty;

    public double FontSize { get; set; } = 32;

    public uint ColorArgb { get; set; } = 0xFF1B1B1F;

    public bool IsEditing => true;

    /// <summary>
    /// Converts the session into a text stroke. The stroke keeps a copy of the layout inputs in
    /// its Text/Geometry fields so the text can be re-rendered after a reload.
    /// </summary>
    public static InkStroke BuildStroke(TextEditSession session)
    {
        var stroke = new InkStroke
        {
            Kind = StrokeKind.Text,
            ColorArgb = session.ColorArgb,
            Width = Math.Max(1, session.FontSize / 12),
            Opacity = 1,
            Shape = "text",
            Text = session.Text,
            Geometry = string.Create(CultureInfo.InvariantCulture,
                $"{session.X:F3},{session.Y:F3},{session.FontSize:F3},{session.PageIndex}"),
        };
        stroke.Points.Add(new InkPoint { X = session.X, Y = session.Y, Pressure = 1 });
        return stroke;
    }

    public static TextEditSession FromStroke(InkStroke stroke)
    {
        var session = new TextEditSession { Text = stroke.Text ?? string.Empty, ColorArgb = stroke.ColorArgb };
        if (!string.IsNullOrEmpty(stroke.Geometry))
        {
            var parts = stroke.Geometry.Split(',');
            if (parts.Length == 4 &&
                double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) &&
                double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) &&
                double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var size) &&
                int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var page))
            {
                session.X = x;
                session.Y = y;
                session.FontSize = size;
                session.PageIndex = page;
            }
        }

        if (session.X == 0 && session.Y == 0 && stroke.Points.Count > 0)
        {
            session.X = stroke.Points[0].X;
            session.Y = stroke.Points[0].Y;
        }

        return session;
    }
}

public enum GestureKind
{
    None = 0,

    /// <summary>Fist held still for about 1.5 s, per manual section 3.6.</summary>
    FistErase = 1,

    /// <summary>Palm resting on the board for about 1 s.</summary>
    PalmLaunch = 2,

    /// <summary>Fast horizontal swipe at board mid-height.</summary>
    WaveLeft = 3,

    WaveRight = 4,

    /// <summary>Two contacts moving apart or together.</summary>
    PinchZoom = 5,

    /// <summary>Two contacts moving apart or together; reports a scale factor.</summary>
    Scale = 6,

    /// <summary>Two contacts twisting; reports degrees.</summary>
    Rotate = 7,

    /// <summary>Two contacts dragging together; reports a translation.</summary>
    Pan = 8,
}

public enum PendingGestureResult
{
    None = 0,
}

/// <summary>
/// Dwell- and motion-based gesture detection for the rules the vendor manual specifies. Kept
/// separate from the surface so the timing rules can be unit tested without a window.
///
/// The manual's own numbers are used as thresholds: fist 1.5 s, palm 1 s. Wave and pinch are
/// inferred from the described motion since the manual does not give numbers for them.
/// </summary>
public sealed class GestureRecognizer
{
    private readonly HashSet<int> _down = new();
    private readonly Dictionary<int, Track> _tracks = new();
    private GestureKind _pending = GestureKind.None;
    private long _pendingSince;

    private readonly record struct Track(double X, double Y, long Start, long Last, int Moves, bool IsPalm);

    public long FistDwellMs { get; set; } = 1500;

    public long PalmDwellMs { get; set; } = 1000;

    /// <summary>Movement below this counts as still.</summary>
    public double StillRadius { get; set; } = 28;

    /// <summary>Swipe needs at least this horizontal travel.</summary>
    public double WaveMinDistance { get; set; } = 180;

    /// <summary>Swipe must be faster than this, in pixels per second.</summary>
    public double WaveMinSpeed { get; set; } = 900;

    /// <summary>Swipe must be roughly horizontal.</summary>
    public double WaveMaxVerticalRatio { get; set; } = 0.6;

    public bool Enabled { get; set; } = true;

    public void NoteEnter(int id) => _down.Add(id);

    public void NoteUp(int id, double x, double y, long nowMs)
    {
        _down.Remove(id);
        _tracks.Remove(id);
    }

    public void NoteLeave(int id)
    {
        _down.Remove(id);
        _tracks.Remove(id);
    }

    public void NoteDown(int id, double x, double y, long nowMs, bool isPalm = false)
    {
        _down.Add(id);
        _tracks[id] = new Track(x, y, nowMs, nowMs, 0, isPalm);
    }

    public void NoteMove(int id, double x, double y, long nowMs)
    {
        if (!_tracks.TryGetValue(id, out var t))
        {
            _tracks[id] = new Track(x, y, nowMs, nowMs, 0, false);
            return;
        }

        var moved = Math.Sqrt(Math.Pow(x - t.X, 2) + Math.Pow(y - t.Y, 2));
        if (moved < StillRadius)
        {
            _tracks[id] = t with { Last = nowMs, Moves = t.Moves + 1 };
        }
        else
        {
            _tracks[id] = t with { X = x, Y = y, Last = nowMs, Moves = t.Moves + 1 };
            DetectWave(t.Start, t.X, t.Y, x, y, nowMs);
        }
    }

    public void NoteHold(int id, double x, double y, long nowMs)
    {
        if (!_tracks.TryGetValue(id, out var t))
        {
            return;
        }

        if (nowMs - t.Start < StillMs)
        {
            return;
        }

        if (Math.Sqrt(Math.Pow(x - t.X, 2) + Math.Pow(y - t.Y, 2)) > StillRadius)
        {
            return;
        }

        var held = nowMs - t.Start;

        // A contact the OS classified as a palm is a launch intent at the shorter dwell; a
        // normal contact is only a fist once it has been held long enough to be deliberate.
        // Without this the 1.5 s fist threshold would never fire, because the shorter palm
        // threshold would always trip first on the same contact.
        if (t.IsPalm)
        {
            if (held >= PalmDwellMs && _pending == GestureKind.None)
            {
                _pending = GestureKind.PalmLaunch;
                _pendingSince = nowMs;
            }

            return;
        }

        if (held >= FistDwellMs && _pending == GestureKind.None)
        {
            _pending = GestureKind.FistErase;
            _pendingSince = nowMs;
        }
    }

    /// <summary>Below this a contact is treated as a press rather than a resting hand.</summary>
    public long StillMs { get; set; } = 400;

    private void DetectWave(long startMs, double sx, double sy, double x, double y, long nowMs)
    {
        var dx = x - sx;
        var dy = y - sy;
        var distance = Math.Sqrt((dx * dx) + (dy * dy));
        if (distance < WaveMinDistance)
        {
            return;
        }

        if (Math.Abs(dy) > WaveMaxVerticalRatio * Math.Abs(dx))
        {
            return;
        }

        var elapsedSeconds = Math.Max(0.001, (nowMs - startMs) / 1000.0);
        if (distance / elapsedSeconds < WaveMinSpeed)
        {
            return;
        }

        _pending = dx < 0 ? GestureKind.WaveLeft : GestureKind.WaveRight;
        _pendingSince = nowMs;
    }

    public bool TryConsume(out GestureKind gesture)
    {
        gesture = _pending;
        if (_pending == GestureKind.None)
        {
            return false;
        }

        _pending = GestureKind.None;
        return true;
    }

    public void Reset()
    {
        _down.Clear();
        _tracks.Clear();
        _pending = GestureKind.None;
    }

    public void ResetPinch()
    {
        _pending = GestureKind.None;
    }

    public int ActiveContactCount => _tracks.Count;
}
