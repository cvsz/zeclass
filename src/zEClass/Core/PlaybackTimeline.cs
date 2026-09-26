using System;
using System.Collections.Generic;
using System.Linq;

namespace zEClass.Core;

/// <summary>
/// Replays recorded strokes in the order they were drawn, at the speed they were drawn.
///
/// The vendor manual describes "PLAY BACK: play back the page contents" and lists
/// "PLAYBACKSPEED" and "RIGHTTIME" in the shipped configuration, so playback speed is a real
/// setting rather than an invention. Timings come from the stroke sample timestamps, which the
/// ink surface already records, so no extra instrumentation is needed for strokes drawn in
/// this app.
/// </summary>
public sealed class PlaybackTimeline
{
    private readonly List<PlaybackStroke> _strokes = new();

    /// <summary>
    /// One recorded stroke, kept with its full rendering attributes so playback reproduces the
    /// original ink rather than a generic approximation.
    /// </summary>
    public sealed record PlaybackStroke(
        int Order,
        long StartTicks,
        long EndTicks,
        InkStroke Source)
    {
        public long DurationTicks => Math.Max(1, EndTicks - StartTicks);

        public IReadOnlyList<InkPoint> Points => Source.Points;
    }

    public IReadOnlyList<PlaybackStroke> Strokes => _strokes;

    public long TotalDurationTicks => _strokes.Count == 0
        ? 0
        : _strokes.Max(s => s.EndTicks) - _strokes.Min(s => s.StartTicks);

    public bool IsEmpty => _strokes.Count == 0;

    /// <summary>Builds a timeline from a page, sorted by draw order.</summary>
    public static PlaybackTimeline FromPage(BoardPage page)
    {
        var timeline = new PlaybackTimeline();
        if (page is null)
        {
            return timeline;
        }

        var order = 0;
        foreach (var stroke in page.Strokes)
        {
            if (stroke.Points.Count == 0)
            {
                continue;
            }

            var start = stroke.Points.Min(p => p.TimeTicks);
            var end = stroke.Points.Max(p => p.TimeTicks);
            timeline._strokes.Add(new PlaybackStroke(order++, start, end, stroke.Clone()));
        }

        timeline._strokes.Sort((a, b) =>
        {
            var byTime = a.StartTicks.CompareTo(b.StartTicks);
            return byTime != 0 ? byTime : a.Order.CompareTo(b.Order);
        });

        return timeline;
    }

    /// <summary>
    /// Strokes that should be visible at a given point in playback.
    ///
    /// A stroke is fully drawn once its end time has passed, and partially drawn before that,
    /// which is what makes a stroke look like it is being written rather than simply appearing.
    /// </summary>
    public List<(InkStroke Stroke, double Fraction)> VisibleAt(long ticks, long originTicks,
        double speed)
    {
        var result = new List<(InkStroke, double)>();
        var scale = speed <= 0 ? 1.0 : speed;

        // Everything below works in offsets from the origin, so convert once here. Mixing an
        // absolute position with relative stroke boundaries silently draws the whole page at once.
        var position = ticks - originTicks;

        foreach (var entry in _strokes)
        {
            var start = entry.StartTicks - originTicks;
            var end = entry.EndTicks - originTicks;
            if (position < start)
            {
                continue;
            }

            var fraction = end <= start ? 1.0 : (double)(position - start) / (end - start);
            fraction = Math.Clamp(fraction * scale, 0, 1);
            if (fraction <= 0)
            {
                continue;
            }

            // Clone keeps the original colour, width, style and fill, then the point list is
            // truncated to the current fraction so the stroke appears to be drawn.
            var partial = entry.Source.Clone();
            partial.Points = Slice(entry.Source.Points, fraction).ToList();
            result.Add((partial, fraction));
        }

        return result;
    }

    /// <summary>Prefix of a point list covering the given fraction of its samples.</summary>
    public static List<InkPoint> Slice(IReadOnlyList<InkPoint> points, double fraction)
    {
        var count = fraction >= 1
            ? points.Count
            : Math.Max(1, (int)Math.Ceiling(points.Count * Math.Clamp(fraction, 0, 1)));

        return points.Take(count).Select(p => new InkPoint
        {
            X = p.X,
            Y = p.Y,
            Pressure = p.Pressure,
            IsStylus = p.IsStylus,
            IsEraser = p.IsEraser,
            Tilt = p.Tilt,
            TimeTicks = p.TimeTicks,
        }).ToList();
    }

    public long OriginTicks => _strokes.Count == 0 ? 0 : _strokes.Min(s => s.StartTicks);
}
