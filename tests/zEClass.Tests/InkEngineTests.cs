using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using zEClass.Core;
using Xunit;

namespace zEClass.Tests;

public sealed class InkEngineTests
{
    private static InkStroke Stroke(params InkPoint[] points) => new()
    {
        Kind = StrokeKind.Pen,
        Width = 8,
        Points = points.ToList(),
    };

    [Fact]
    public void NormalizePressure_ReturnsFullWhenNoCalibrationYet()
    {
        var engine = new InkEngine();

        Assert.Equal(1.0, engine.NormalizePressure(1, 0.5), 3);
    }

    [Fact]
    public void NormalizePressure_RampsBetweenObservedMinAndMax()
    {
        var engine = new InkEngine();
        engine.RecordPressure(7, 0.2);
        engine.RecordPressure(7, 0.8);

        var mid = engine.NormalizePressure(7, 0.5);

        Assert.InRange(mid, 0.4, 0.6);
    }

    [Fact]
    public void NormalizePressure_IsPerDevice()
    {
        var engine = new InkEngine();
        engine.RecordPressure(1, 0.1);
        engine.RecordPressure(1, 0.9);
        engine.RecordPressure(2, 0.1);
        engine.RecordPressure(2, 0.3);

        // Device 2 has its own range, so the same raw value maps differently.
        Assert.True(engine.NormalizePressure(2, 0.3) > engine.NormalizePressure(1, 0.3));
    }

    [Fact]
    public void NormalizePressure_FloorsSoInkNeverDisappears()
    {
        var engine = new InkEngine();
        engine.RecordPressure(3, 0.4);
        engine.RecordPressure(3, 0.6);

        // Lightest plausible non-zero contact must still be visible.
        var low = engine.NormalizePressure(3, 0.38);

        Assert.True(low >= 0.1, $"expected a visible floor, got {low}");
    }

    [Fact]
    public void NormalizePressure_IsZeroForNoContact()
    {
        var engine = new InkEngine();
        engine.RecordPressure(4, 0.5);

        Assert.Equal(0, engine.NormalizePressure(4, 0));
    }

    [Fact]
    public void PalmRejection_IgnoresTouchWhilePenIsActive()
    {
        var engine = new InkEngine { PalmRejectionEnabled = true };

        Assert.False(engine.ShouldIgnoreTouch());
        engine.NoteStylusDown(42);
        Assert.True(engine.ShouldIgnoreTouch());
        engine.NoteStylusUp(42);
        Assert.False(engine.ShouldIgnoreTouch());
    }

    [Fact]
    public void PalmRejection_CanBeDisabled()
    {
        var engine = new InkEngine { PalmRejectionEnabled = false };
        engine.NoteStylusDown(1);

        Assert.False(engine.ShouldIgnoreTouch());
    }

    [Fact]
    public void ActiveCounts_TrackContacts()
    {
        var engine = new InkEngine();
        engine.NoteStylusDown(1);
        engine.NoteStylusDown(2);
        engine.NoteTouchDown(9);

        Assert.Equal(2, engine.ActiveStylusCount);
        Assert.Equal(1, engine.ActiveTouchCount);
    }

    [Fact]
    public void EffectiveWidth_ScalesWithPressure()
    {
        var engine = new InkEngine { MinWidthRatio = 0.5 };
        var light = new InkPoint { Pressure = 0.05 };
        var heavy = new InkPoint { Pressure = 1.0 };

        var wLight = engine.EffectiveWidth(10, light);
        var wHeavy = engine.EffectiveWidth(10, heavy);

        Assert.True(wHeavy > wLight, $"{wHeavy} should exceed {wLight}");
        Assert.Equal(5.25, wLight, 3);
        Assert.Equal(10.0, wHeavy, 3);
    }

    [Fact]
    public void EffectiveWidth_TreatsZeroPressureAsMidScale()
    {
        // A contact-only digitizer or the mouse reports no usable pressure; the line must
        // still come out at nominal width, not at the thinnest possible width.
        var engine = new InkEngine { MinWidthRatio = 0.35 };

        var noData = engine.EffectiveWidth(10, new InkPoint { Pressure = 0.0 });
        var midScale = engine.EffectiveWidth(10, new InkPoint { Pressure = 0.5 });

        Assert.Equal(midScale, noData, 3);
    }

    [Fact]
    public void EffectiveWidth_IsMonotonicAcrossPressureRange()
    {
        var engine = new InkEngine();
        var previous = double.MinValue;

        // Starts just above zero: a reported 0 means "no pressure data" and is deliberately
        // treated as mid-scale, so it is not part of the monotonic ramp.
        for (var i = 1; i <= 10; i++)
        {
            var pressure = i / 10.0;
            var width = engine.EffectiveWidth(8, new InkPoint { Pressure = pressure });
            Assert.True(width >= previous, $"width decreased at pressure {pressure}");
            previous = width;
        }
    }

    [Fact]
    public void EffectiveWidth_NeverBelowHalfPixel()
    {
        var engine = new InkEngine();
        var width = engine.EffectiveWidth(0.2, new InkPoint { Pressure = 0 });

        Assert.True(width >= 0.5);
    }

    [Fact]
    public void BuildOutline_ProducesGeometryForStroke()
    {
        var engine = new InkEngine();
        var stroke = Stroke(
            new InkPoint { X = 0, Y = 0, Pressure = 1 },
            new InkPoint { X = 50, Y = 50, Pressure = 1 },
            new InkPoint { X = 100, Y = 0, Pressure = 1 });

        var geometry = engine.BuildOutline(stroke.Points, stroke.Width, 1.0);

        Assert.False(geometry.IsEmpty());
        Assert.True(geometry.Bounds.Width > 50);
    }

    [Fact]
    public void BuildOutline_HandlesSinglePointAsADot()
    {
        var engine = new InkEngine();
        var stroke = Stroke(new InkPoint { X = 25, Y = 25, Pressure = 1 });

        var geometry = engine.BuildOutline(stroke.Points, stroke.Width, 1.0);

        Assert.False(geometry.IsEmpty());
    }

    [Fact]
    public void BuildOutline_ReturnsEmptyForNoPoints()
    {
        var engine = new InkEngine();

        Assert.True(engine.BuildOutline(Array.Empty<InkPoint>(), 4, 1).IsEmpty());
    }

    [Fact]
    public void BuildOutline_TwiceWithIdenticalPointsYieldsSameBounds()
    {
        var engine = new InkEngine();
        var points = new[]
        {
            new InkPoint { X = 0, Y = 0, Pressure = 0.5 },
            new InkPoint { X = 30, Y = 20, Pressure = 0.9 },
            new InkPoint { X = 60, Y = 10, Pressure = 0.2 },
        };

        var a = engine.BuildOutline(points, 6, 1).Bounds;
        var b = engine.BuildOutline(points, 6, 1).Bounds;

        Assert.Equal(a, b);
    }

    [Fact]
    public void CreateSample_MarksStylusAndEraser()
    {
        var engine = new InkEngine();

        var sample = engine.CreateSample(new Point(5, 6), 0.7, isStylus: true, isEraser: true, 12);

        Assert.True(sample.IsStylus);
        Assert.True(sample.IsEraser);
        Assert.Equal(12, sample.Tilt);
        Assert.Equal(5, sample.X);
        Assert.Equal(6, sample.Y);
    }

    [Fact]
    public void CreateSample_MouseGetsMidPressure()
    {
        var engine = new InkEngine();

        var sample = engine.CreateSample(new Point(0, 0), 0, isStylus: false, isEraser: false);

        Assert.Equal(0.5, sample.Pressure, 3);
        Assert.False(sample.IsStylus);
    }

    [Fact]
    public void BrushFor_UsesStrokeColorAndOpacity()
    {
        var engine = new InkEngine();
        var stroke = new InkStroke { ColorArgb = 0xFF1E88E5, Opacity = 0.5 };

        var brush = engine.BrushFor(stroke);

        // 0.5 opacity lands in the middle of the alpha range, not at full or zero.
        Assert.InRange(brush.Color.A, 120, 135);
        Assert.Equal(0x1E, brush.Color.R);
        Assert.Equal(0x88, brush.Color.G);
        Assert.Equal(0xE5, brush.Color.B);
        Assert.True(brush.IsFrozen);
    }

    [Fact]
    public void BrushFor_FullOpacityIsOpaque()
    {
        var engine = new InkEngine();

        var brush = engine.BrushFor(new InkStroke { ColorArgb = 0xFF000000, Opacity = 1.0 });

        Assert.Equal(255, brush.Color.A);
    }
}
