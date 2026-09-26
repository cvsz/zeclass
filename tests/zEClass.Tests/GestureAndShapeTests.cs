using System;
using System.Collections.Generic;
using System.Linq;
using zEClass.Core;
using Xunit;

namespace zEClass.Tests;

public sealed class TwoFingerGestureTests
{
    private static TwoFingerGestureRecognizer Tracking()
    {
        var g = new TwoFingerGestureRecognizer();
        g.Begin(1, 0, 0);
        g.Begin(2, 200, 200);
        return g;
    }

    [Fact]
    public void TwoContactsBeginTracking()
    {
        var g = Tracking();

        Assert.True(g.IsTracking);
        Assert.Equal(2, g.ContactCount);
    }

    [Fact]
    public void SingleContactIsNotTracked()
    {
        var g = new TwoFingerGestureRecognizer();
        g.Begin(1, 10, 10);

        Assert.False(g.IsTracking);
        Assert.Null(g.Consume());
    }

    [Fact]
    public void DiagonalNarrowContactsAreClassifiedAsRotate()
    {
        // The manual's rule: horizontal separation under 2 cm, vertical over 2 cm.
        var g = new TwoFingerGestureRecognizer();
        g.Begin(1, 400, 300);
        g.Begin(2, 401, 340);

        Assert.Equal(GestureKind.Rotate, g.CurrentMode);
    }

    [Fact]
    public void WideContactsAreClassifiedAsScale()
    {
        var g = new TwoFingerGestureRecognizer();
        g.Begin(1, 300, 400);
        g.Begin(2, 700, 400);

        Assert.Equal(GestureKind.Scale, g.CurrentMode);
    }

    [Fact]
    public void VeryCloseContactsAreNotScaled()
    {
        var g = new TwoFingerGestureRecognizer();
        g.Begin(1, 400, 400);
        g.Begin(2, 405, 400);

        Assert.Equal(GestureKind.None, g.CurrentMode);
    }

    [Fact]
    public void PinchingOutReportsIncreasedScale()
    {
        var g = Tracking();
        g.Update(1, 0, 0);
        g.Update(2, 300, 200);

        var gesture = g.Consume();

        Assert.NotNull(gesture);
        Assert.Equal(GestureKind.Scale, gesture!.Value.Kind);
        Assert.True(gesture.Value.Scale > 1.1, $"expected growth, got {gesture.Value.Scale}");
    }

    [Fact]
    public void PinchingInReportsDecreasedScale()
    {
        var g = Tracking();
        g.Update(1, 0, 0);
        g.Update(2, 150, 150);

        var gesture = g.Consume();

        Assert.NotNull(gesture);
        Assert.True(gesture!.Value.Scale < 0.9, $"expected shrink, got {gesture.Value.Scale}");
    }

    [Fact]
    public void SmallScaleChangeIsIgnored()
    {
        var g = Tracking();
        g.Update(2, 205, 205);

        Assert.Null(g.Consume());
    }

    [Fact]
    public void OneGestureIsEmittedPerContact()
    {
        var g = Tracking();
        g.Update(2, 300, 200);

        Assert.NotNull(g.Consume());
        // Second consume without new movement must not repeat the same gesture.
        Assert.Null(g.Consume());
    }

    [Fact]
    public void RotatingDiagonalContactsReportsDegrees()
    {
        // The manual's rule only treats a contact pair as a rotation while the gap stays
        // narrow and horizontal, which for a given separation bounds the detectable angle.
        // The test rotates within that band and computes the new positions rather than
        // hard-coding them, so it cannot drift from the geometry it is checking.
        const double x = 400.0;
        const double yTop = 300.0;
        const double separation = 40.0;
        const double degrees = 2.5;

        var g = new TwoFingerGestureRecognizer { RotateThresholdDegrees = 2 };
        g.Begin(1, x, yTop);
        g.Begin(2, x, yTop + separation);
        Assert.Equal(GestureKind.Rotate, g.CurrentMode);

        var midX = x;
        var midY = yTop + (separation / 2.0);
        var radians = degrees * Math.PI / 180;

        void Place(int id, double px, double py)
        {
            var dx = px - midX;
            var dy = py - midY;
            g.Update(id,
                midX + ((dx * Math.Cos(radians)) - (dy * Math.Sin(radians))),
                midY + ((dx * Math.Sin(radians)) + (dy * Math.Cos(radians))));
        }

        Place(1, x, yTop);
        Place(2, x, yTop + separation);

        var gesture = g.Consume();

        Assert.NotNull(gesture);
        Assert.Equal(GestureKind.Rotate, gesture!.Value.Kind);
        Assert.InRange(gesture.Value.RotationDegrees, 1.5, 3.5);
    }

    [Fact]
    public void EndingAContactStopsTracking()
    {
        var g = Tracking();
        g.End(2);

        Assert.False(g.IsTracking);
        Assert.Equal(GestureKind.None, g.CurrentMode);
    }

    [Fact]
    public void ResetClearsEverything()
    {
        var g = Tracking();
        g.Reset();

        Assert.Equal(0, g.ContactCount);
        Assert.Null(g.Consume());
    }

    [Fact]
    public void CloseContactsThatSeparateBecomeAScale()
    {
        // A gesture that starts ambiguous must be re-classified rather than stuck as None.
        var g = new TwoFingerGestureRecognizer();
        g.Begin(1, 400, 400);
        g.Begin(2, 405, 400);
        Assert.Equal(GestureKind.None, g.CurrentMode);

        g.Update(1, 300, 400);
        g.Update(2, 500, 400);

        Assert.Equal(GestureKind.Scale, g.CurrentMode);
    }
}

public sealed class ShapeRecognizerTests
{
    private static List<InkPoint> Circle(double cx, double cy, double r, int n = 72)
    {
        var pts = new List<InkPoint>(n + 1);
        for (var i = 0; i <= n; i++)
        {
            var a = 2 * Math.PI * i / n;
            pts.Add(new InkPoint { X = cx + (r * Math.Cos(a)), Y = cy + (r * Math.Sin(a)) });
        }

        return pts;
    }

    private static List<InkPoint> Square(double x, double y, double size, int perSide = 20)
    {
        var pts = new List<InkPoint>();
        var corners = new[]
        {
            (x, y), (x + size, y), (x + size, y + size), (x, y + size), (x, y),
        };
        for (var c = 0; c < corners.Length - 1; c++)
        {
            var (x0, y0) = corners[c];
            var (x1, y1) = corners[c + 1];
            for (var i = 0; i < perSide; i++)
            {
                var t = i / (double)perSide;
                pts.Add(new InkPoint { X = x0 + ((x1 - x0) * t), Y = y0 + ((y1 - y0) * t) });
            }
        }

        return pts;
    }

    private static List<InkPoint> Line(double x0, double y0, double x1, double y1, int n = 30)
    {
        var pts = new List<InkPoint>();
        for (var i = 0; i <= n; i++)
        {
            var t = i / (double)n;
            pts.Add(new InkPoint { X = x0 + ((x1 - x0) * t), Y = y0 + ((y1 - y0) * t) });
        }

        return pts;
    }

    [Fact]
    public void CircleIsRecognized()
    {
        var result = ShapeRecognizer.Recognize(Circle(400, 400, 120));

        Assert.Equal(RecognizedShape.Circle, result.Shape);
        Assert.True(result.Confidence > 0.5, result.Reason);
    }

    [Fact]
    public void SquareIsRecognized()
    {
        var result = ShapeRecognizer.Recognize(Square(300, 300, 200));

        Assert.Equal(RecognizedShape.Square, result.Shape);
    }

    [Fact]
    public void RecognitionIsInvariantToPosition()
    {
        var a = ShapeRecognizer.Recognize(Circle(100, 100, 80));
        var b = ShapeRecognizer.Recognize(Circle(1600, 900, 80));

        Assert.Equal(a.Shape, b.Shape);
    }

    [Fact]
    public void RecognitionIsInvariantToScale()
    {
        var small = ShapeRecognizer.Recognize(Circle(400, 400, 40));
        var large = ShapeRecognizer.Recognize(Circle(400, 400, 260));

        Assert.Equal(RecognizedShape.Circle, small.Shape);
        Assert.Equal(RecognizedShape.Circle, large.Shape);
    }

    [Fact]
    public void RecognitionIsInvariantToRotation()
    {
        var pts = Circle(400, 400, 100);
        var rotated = pts.Select(p => new InkPoint
        {
            X = 400 + (0.8 * (p.X - 400)) - (0.6 * (p.Y - 400)),
            Y = 400 + (0.6 * (p.X - 400)) + (0.8 * (p.Y - 400)),
        }).ToList();

        Assert.Equal(RecognizedShape.Circle, ShapeRecognizer.Recognize(rotated).Shape);
    }

    [Fact]
    public void OpenStrokeIsNotRecognized()
    {
        var result = ShapeRecognizer.Recognize(Line(200, 400, 600, 400));

        Assert.NotEqual(RecognizedShape.Circle, result.Shape);
        Assert.NotEqual(RecognizedShape.Square, result.Shape);
    }

    [Fact]
    public void TooFewPointsIsRejected()
    {
        var result = ShapeRecognizer.Recognize([new InkPoint { X = 1, Y = 1 }, new InkPoint { X = 2, Y = 2 }]);

        Assert.Equal(RecognizedShape.None, result.Shape);
    }

    [Fact]
    public void DegenerateStrokeIsRejected()
    {
        var result = ShapeRecognizer.Recognize(Line(100, 100, 101, 100));

        Assert.Equal(RecognizedShape.None, result.Shape);
    }

    [Fact]
    public void RadialSpreadIsLowForACircle()
    {
        Assert.True(ShapeRecognizer.RadialSpread(Circle(0, 0, 100)) < 0.1);
    }

    [Fact]
    public void RadialSpreadSeparatesACircleFromASquare()
    {
        // Sampling is uniform along the perimeter, so a square's corners are under-represented
        // and its spread is lower than intuition suggests. The test records the real values so
        // a future change to the recogniser cannot silently break the ordering.
        var circle = ShapeRecognizer.RadialSpread(Circle(0, 0, 100));
        var square = ShapeRecognizer.RadialSpread(Square(-100, -100, 200));

        Assert.True(circle < 0.05, $"circle spread was {circle:F3}");
        Assert.True(square > circle * 2, $"square {square:F3} vs circle {circle:F3}");
    }

    [Fact]
    public void FillRatioSeparatesCircleFromSquare()
    {
        var circleFill = Math.Abs(ShapeRecognizer.SignedArea(Circle(400, 400, 100))) /
                         (200.0 * 200.0);
        var squareFill = Math.Abs(ShapeRecognizer.SignedArea(Square(300, 300, 200))) /
                         (200.0 * 200.0);

        Assert.InRange(circleFill, 0.77, 0.80);
        Assert.Equal(1.0, squareFill, 2);
    }

    [Fact]
    public void SquareFromProducesTheBoundingBox()
    {
        var corners = ShapeRecognizer.SquareFrom(Square(300, 300, 200));

        Assert.Equal(300, corners.A.X, 1);
        Assert.Equal(300, corners.A.Y, 1);
        Assert.Equal(500, corners.C.X, 1);
        Assert.Equal(500, corners.C.Y, 1);
    }

    [Fact]
    public void CircleFromReportsCentreAndRadius()
    {
        var circle = ShapeRecognizer.CircleFrom(Circle(500, 300, 150));

        Assert.Equal(500, circle.Center.X, 0);
        Assert.Equal(300, circle.Center.Y, 0);
        Assert.Equal(150, circle.Radius, 0);
    }

    [Fact]
    public void HandWaverWobbleStillRecognizesAsACircle()
    {
        // Real ink is never a perfect circle; a little noise must not break recognition.
        var rng = new Random(1234);
        var pts = Circle(400, 400, 120).Select(p => new InkPoint
        {
            X = p.X + (rng.NextDouble() - 0.5) * 4,
            Y = p.Y + (rng.NextDouble() - 0.5) * 4,
        }).ToList();

        Assert.Equal(RecognizedShape.Circle, ShapeRecognizer.Recognize(pts).Shape);
    }
}
