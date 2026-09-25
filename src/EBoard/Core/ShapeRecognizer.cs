using System;
using System.Collections.Generic;
using System.Linq;

namespace EBoard.Core;

/// <summary>What a completed freehand shape was recognised as.</summary>
public enum RecognizedShape
{
    None = 0,
    Circle = 1,
    Square = 2,
    Triangle = 3,
    Line = 4,
}

/// <summary>Outcome of classifying a finished stroke.</summary>
public sealed record ShapeRecognition(RecognizedShape Shape, double Confidence, string Reason);

/// <summary>
/// Classifies a finished freehand stroke into a simple shape.
///
/// The vendor manual (section 4.3.4) describes a "recognition pen" that recognises exactly two
/// things: draw a circle and it becomes a spotlight, draw a square and it becomes a magnifier,
/// with the warning that the square's corners must be near 90 degrees or the magnifier fails.
/// That wording describes a deliberately naive recogniser, so this one is similarly tolerant
/// rather than a general curve-fitting engine.
///
/// Geometry used, all invariant to rotation, translation and scale so a shape drawn anywhere on
/// the board at any size classifies the same:
///  - bounding box aspect ratio,
///  - closure (end point near the start point, relative to the shape's own size),
///  - distance of every sample from the centroid, normalised by the mean radius, which is
///    near-constant for a circle and varies strongly for a square.
/// </summary>
public static class ShapeRecognizer
{
    /// <summary>Endpoints closer than this fraction of the shape size count as closed.</summary>
    public const double ClosureThreshold = 0.35;

    /// <summary>Aspect ratios outside this band are not treated as a square.</summary>
    public const double SquareAspectMin = 0.65;
    public const double SquareAspectMax = 1.5;

    public const double CircleAspectMin = 0.75;
    public const double CircleAspectMax = 1.35;

    /// <summary>
    /// Enclosed-area-to-bounding-box thresholds. A true circle fills pi/4 (0.785) of its box
    /// and a true square fills 1.0, so the boundary sits at about 0.89, comfortably between
    /// them and still tolerant of hand-drawn wobble.
    /// </summary>
    public const double CircleFillMax = 0.89;

    public const double SquareFillMin = 0.89;

    /// <summary>Fill ratio of a mathematically perfect circle: pi/4.</summary>
    public const double IdealCircleFill = Math.PI / 4;

    public const double IdealSquareFill = 1.0;

    /// <summary>
    /// Confidence is closeness to the ideal fill ratio, not distance from the decision
    /// threshold: a clean circle is a confident circle even though its fill (0.785) sits well
    /// below the 0.89 boundary that separates it from a square.
    /// </summary>
    private static double Confidence(double fill, double ideal) =>
        Math.Clamp(1.0 - (Math.Abs(fill - ideal) / 0.15), 0, 1);

    public const int MinimumPoints = 8;

    public static ShapeRecognition Recognize(IReadOnlyList<InkPoint> points)
    {
        if (points.Count < MinimumPoints)
        {
            return new ShapeRecognition(RecognizedShape.None, 0, "too few points");
        }

        var xs = points.Select(p => p.X).ToList();
        var ys = points.Select(p => p.Y).ToList();
        var minX = xs.Min();
        var maxX = xs.Max();
        var minY = ys.Min();
        var maxY = ys.Max();
        var width = maxX - minX;
        var height = maxY - minY;
        if (width < 4 || height < 4)
        {
            return new ShapeRecognition(RecognizedShape.None, 0, "degenerate bounds");
        }

        var size = Math.Sqrt(width * height);

        // A straight line is a poor aspect ratio and is never closed.
        var aspect = width >= height ? width / height : height / width;
        if (aspect > 3.0)
        {
            var open = Distance(points[0], points[^1]) / size;
            return open < ClosureThreshold
                ? new ShapeRecognition(RecognizedShape.None, 0, "flat, nearly closed")
                : new ShapeRecognition(RecognizedShape.Line, 0.7, "elongated and open");
        }

        var closure = Distance(points[0], points[^1]) / size;
        if (closure > ClosureThreshold)
        {
            return new ShapeRecognition(RecognizedShape.None, 0,
                $"not closed (gap {closure:F2} of size)");
        }

        // Fill ratio: enclosed area over bounding-box area. This is the discriminator that
        // actually works. A circle fills pi/4 (about 0.785) of its box; a square fills all of
        // it. Radial spread was tried first and fails, because samples arrive uniformly along
        // the perimeter and so under-represent the square's corners.
        var fill = Math.Abs(SignedArea(points)) / (width * height);
        if (aspect is >= CircleAspectMin and <= CircleAspectMax && fill < CircleFillMax)
        {
            return new ShapeRecognition(RecognizedShape.Circle, Confidence(fill, IdealCircleFill),
                $"round, fill {fill:F2}");
        }

        if (aspect is >= SquareAspectMin and <= SquareAspectMax && fill >= SquareFillMin)
        {
            return new ShapeRecognition(RecognizedShape.Square,
                Confidence(fill, IdealSquareFill), $"angular, fill {fill:F2}");
        }

        if (aspect is >= 1.3 and <= 2.6 && CornerScore(points) >= 0.5)
        {
            return new ShapeRecognition(RecognizedShape.Triangle, 0.6, "three corners");
        }

        return new ShapeRecognition(RecognizedShape.None, 0,
            $"no match (aspect {aspect:F2}, fill {fill:F2})");
    }

    /// <summary>Shoelace formula, absolute value taken by the caller.</summary>
    public static double SignedArea(IReadOnlyList<InkPoint> points)
    {
        var sum = 0.0;
        for (var i = 0; i < points.Count; i++)
        {
            var a = points[i];
            var b = points[(i + 1) % points.Count];
            sum += (a.X * b.Y) - (b.X * a.Y);
        }

        return sum / 2.0;
    }

    /// <summary>
    /// Standard deviation of sample radii around the centroid, divided by the mean radius.
    /// A circle scores near zero; a square, whose corners reach further than its edge midpoints,
    /// scores well above a third of its mean radius.
    /// </summary>
    public static double RadialSpread(IReadOnlyList<InkPoint> points)
    {
        var cx = points.Average(p => p.X);
        var cy = points.Average(p => p.Y);
        var radii = points.Select(p => Distance(p.X, p.Y, cx, cy)).ToList();
        var mean = radii.Average();
        if (mean < 1e-6)
        {
            return 0;
        }

        var variance = radii.Sum(r => (r - mean) * (r - mean)) / radii.Count;
        return Math.Sqrt(variance) / mean;
    }

    /// <summary>
    /// Fraction of the turning concentrated into a few sharp corners. Used only to separate a
    /// triangle from an unmatched blob.
    /// </summary>
    public static double CornerScore(IReadOnlyList<InkPoint> points)
    {
        var count = points.Count;
        var angles = new List<double>();
        for (var i = 0; i < count; i++)
        {
            var prev = points[(i - 1 + count) % count];
            var curr = points[i];
            var next = points[(i + 1) % count];

            var ax = prev.X - curr.X;
            var ay = prev.Y - curr.Y;
            var bx = next.X - curr.X;
            var by = next.Y - curr.Y;

            var a = Math.Sqrt((ax * ax) + (ay * ay));
            var b = Math.Sqrt((bx * bx) + (by * by));
            if (a < 1e-6 || b < 1e-6)
            {
                continue;
            }

            var cos = ((ax * bx) + (ay * by)) / (a * b);
            angles.Add(Math.Acos(Math.Clamp(cos, -1, 1)) * 180 / Math.PI);
        }

        if (angles.Count == 0)
        {
            return 0;
        }

        var total = angles.Sum();
        if (total < 1e-6)
        {
            return 0;
        }

        // Three dominant turns make up most of the total rotation.
        var topThree = angles.OrderByDescending(x => x).Take(3).Sum();
        return topThree / total;
    }

    private static double Distance(InkPoint a, InkPoint b) =>
        Distance(a.X, a.Y, b.X, b.Y);

    private static double Distance(double x1, double y1, double x2, double y2) =>
        Math.Sqrt(Math.Pow(x1 - x2, 2) + Math.Pow(y1 - y2, 2));

    /// <summary>
    /// Centre and radius of a recognised circle. The bounding box centre is used rather than
    /// the sample centroid, because a stroke that ends where it started samples the seam twice
    /// and drags the centroid off the true centre.
    /// </summary>
    public static (System.Windows.Point Center, double Radius) CircleFrom(
        IReadOnlyList<InkPoint> points)
    {
        var cx = (points.Min(p => p.X) + points.Max(p => p.X)) / 2;
        var cy = (points.Min(p => p.Y) + points.Max(p => p.Y)) / 2;
        return (new System.Windows.Point(cx, cy),
            (points.Max(p => p.X) - points.Min(p => p.X)) / 2);
    }

    /// <summary>Axis-aligned box of a recognised square, from its bounding box centre.</summary>
    public static (System.Windows.Point A, System.Windows.Point B, System.Windows.Point C,
        System.Windows.Point D) SquareFrom(IReadOnlyList<InkPoint> points)
    {
        var minX = points.Min(p => p.X);
        var maxX = points.Max(p => p.X);
        var minY = points.Min(p => p.Y);
        var maxY = points.Max(p => p.Y);
        return (
            new System.Windows.Point(minX, minY),
            new System.Windows.Point(maxX, minY),
            new System.Windows.Point(maxX, maxY),
            new System.Windows.Point(minX, maxY));
    }
}
