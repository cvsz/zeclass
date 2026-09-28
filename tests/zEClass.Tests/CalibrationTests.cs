using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using zEClass.Core;
using Xunit;

namespace zEClass.Tests;

public sealed class CalibrationTests : IDisposable
{
    private readonly string _dir;

    public CalibrationTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "zEClass-cal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, true);
        }
        catch (IOException)
        {
        }
    }

    private static Calibration Exact() =>
        Calibration.CreateForDisplay(1920, 1080);

    /// <summary>Applies a known distortion to the perfect captures so the fit has real work.</summary>
    private static Calibration Skewed(double scale, double offsetX, double offsetY)
    {
        var c = Exact();
        c.Captures = c.Targets
            .Select(t => new CalibrationPoint((t.X * scale) + offsetX, (t.Y * scale) + offsetY))
            .ToList();
        return c;
    }

    [Fact]
    public void CreateForDisplay_PlacesFourTargetsInCorners()
    {
        var c = Exact();

        Assert.Equal(Calibration.TargetCount, c.Targets.Count);
        Assert.Equal(1920, c.DisplayWidth);
        Assert.Equal(1080, c.DisplayHeight);
        // Inset is 18% of each axis, so the first target is at (345.6, 194.4).
        Assert.Equal(1920 * 0.18, c.Targets[0].X, 3);
        Assert.Equal(1080 * 0.18, c.Targets[0].Y, 3);
    }

    [Fact]
    public void CreateForDisplay_OrdersTargetsClockwiseFromTopLeft()
    {
        var c = Exact();
        var t = c.Targets;

        Assert.True(t[0].X < t[1].X && t[0].Y < t[2].Y, "first target should be top-left");
        Assert.True(t[1].X > t[3].X, "second should be top-right");
        Assert.True(t[2].Y > t[1].Y, "third should be bottom-right");
    }

    [Fact]
    public void IsComplete_RequiresFourFiniteCaptures()
    {
        var c = Exact();
        Assert.False(c.IsComplete);

        c.Captures = c.Targets.Select(p => p.Clone()).ToList();
        Assert.True(c.IsComplete);
    }

    [Fact]
    public void IsComplete_RejectsNaNCaptures()
    {
        var c = Exact();
        c.Captures = c.Targets.Select(p => p.Clone()).ToList();
        c.Captures[2] = new CalibrationPoint(double.NaN, double.NaN);

        Assert.False(c.IsComplete);
    }

    [Fact]
    public void BuildTransform_RecoversIdentityWhenCaptureMatchesTargets()
    {
        var c = Exact();
        c.Captures = c.Targets.Select(p => p.Clone()).ToList();

        var t = c.BuildTransform();

        Assert.NotNull(t);
        Assert.True(t!.IsIdentity, "a capture equal to the targets must map to the identity");
    }

    [Fact]
    public void BuildTransform_RecoversPureTranslation()
    {
        // Digitizer reports everything 40px right and 25px down of where the display is.
        var c = Exact();
        c.Captures = c.Targets.Select(t => new CalibrationPoint(t.X + 40, t.Y + 25)).ToList();

        var transform = c.BuildTransform();
        Assert.NotNull(transform);

        var first = transform!.Apply(c.Captures[0]);
        Assert.Equal(c.Targets[0].X, first.X, 3);
        Assert.Equal(c.Targets[0].Y, first.Y, 3);
    }

    [Fact]
    public void BuildTransform_RecoversUniformScaleAndOffset()
    {
        var c = Skewed(1.08, 30, -12);

        var transform = c.BuildTransform();
        Assert.NotNull(transform);

        for (var i = 0; i < Calibration.TargetCount; i++)
        {
            var mapped = transform!.Apply(c.Captures[i]);
            Assert.Equal(c.Targets[i].X, mapped.X, 3);
            Assert.Equal(c.Targets[i].Y, mapped.Y, 3);
        }
    }

    [Fact]
    public void BuildTransform_ReturnsNullForCollinearCaptures()
    {
        // All four hits on one line: the projective system is singular.
        var c = Exact();
        c.Captures = Enumerable.Range(0, 4).Select(i => new CalibrationPoint(100 + (i * 50), 400))
            .ToList();

        Assert.Null(c.BuildTransform());
    }

    [Fact]
    public void BuildTransform_ReturnsNullForRepeatedSinglePointCapture()
    {
        var c = Exact();
        c.Captures = Enumerable.Range(0, 4).Select(_ => new CalibrationPoint(500, 500)).ToList();

        Assert.Null(c.BuildTransform());
    }

    [Fact]
    public void BuildTransform_ReturnsNullForIncompleteCapture()
    {
        var c = Exact();
        c.Captures = [c.Targets[0].Clone(), c.Targets[1].Clone()];

        Assert.Null(c.BuildTransform());
    }

    [Fact]
    public void BuildTransform_MapsWholeSurfaceNotJustTargets()
    {
        // A transform that only fits the four targets is useless; the middle of the board must
        // land correctly too.
        var c = Skewed(1.05, 18, 22);
        var transform = c.BuildTransform();
        Assert.NotNull(transform);

        // Forward distortion is capture = board*1.05 + 18, so the board centre maps to this
        // digitizer point and must invert back to the centre.
        const double scale = 1.05;
        const double offsetX = 18;
        const double offsetY = 22;
        var centerX = (1920.0 / 2.0 * scale) + offsetX;
        var centerY = (1080.0 / 2.0 * scale) + offsetY;

        var mapped = transform!.Apply(new CalibrationPoint(centerX, centerY));
        Assert.Equal(1920 / 2.0, mapped.X, 1);
        Assert.Equal(1080 / 2.0, mapped.Y, 1);
    }

    [Fact]
    public void Transform_Identity_IsInvertibleAndNeutral()
    {
        var id = CalibrationTransform.Identity;

        Assert.True(id.IsInvertible);
        Assert.True(id.IsIdentity);

        var p = id.Apply(new CalibrationPoint(123, 456));
        Assert.Equal(123, p.X, 6);
        Assert.Equal(456, p.Y, 6);
    }

    [Fact]
    public void Transform_DeterminantIsNonZeroForRealCalibrations()
    {
        var c = Skewed(1.08, 30, -12);
        var t = c.BuildTransform();

        Assert.NotNull(t);
        Assert.True(t!.IsInvertible);
        Assert.True(Math.Abs(t.Determinant) > 1e-9);
    }

    [Fact]
    public void Transform_SingularMatrixIsNotInvertible()
    {
        var singular = new CalibrationTransform(1, 2, 3, 2, 4, 6, 0, 0, 1);

        Assert.False(singular.IsInvertible);
        Assert.Null(singular.Invert());
    }

    [Fact]
    public void Transform_ApplyReturnsNaNForZeroHomogeneousScale()
    {
        // w = 0 means the point projects to infinity.
        var t = new CalibrationTransform(1, 0, 0, 0, 1, 0, 0, 0, 0);

        var p = t.Apply(new CalibrationPoint(10, 10));

        Assert.False(p.IsFinite);
    }

    [Fact]
    public void Transform_InvertComposesBackToIdentity()
    {
        var c = Skewed(0.97, -40, 55);
        var forward = c.BuildTransform();
        Assert.NotNull(forward);

        var inverse = forward!.Invert();
        Assert.NotNull(inverse);

        var roundTrip = inverse!.Apply(forward.Apply(new CalibrationPoint(500, 400)));
        Assert.Equal(500, roundTrip.X, 6);
        Assert.Equal(400, roundTrip.Y, 6);
    }

    [Fact]
    public void CalibrationPoint_IsFiniteRejectsInfinity()
    {
        Assert.True(new CalibrationPoint(1, 2).IsFinite);
        Assert.False(new CalibrationPoint(double.PositiveInfinity, 2).IsFinite);
        Assert.False(new CalibrationPoint(1, double.NegativeInfinity).IsFinite);
    }

    [Fact]
    public void Calibration_CloneIsIndependent()
    {
        var c = Exact();
        c.Captures = c.Targets.Select(p => p.Clone()).ToList();

        var clone = c.Clone();
        clone.Captures[0].X = 1;
        clone.Targets.Clear();

        Assert.NotEqual(1, c.Captures[0].X);
        Assert.Equal(Calibration.TargetCount, c.Targets.Count);
    }

    [Fact]
    public void Store_SaveThenLoadRoundTrips()
    {
        var store = new CalibrationStore(_dir);
        var boardId = Guid.NewGuid();
        var c = Skewed(1.04, 12, 8);
        c.BoardId = boardId;
        c.CalibratedUtc = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

        Assert.True(store.TrySave(c));
        Assert.True(store.TryLoad(boardId, out var loaded));
        Assert.NotNull(loaded);
        Assert.Equal(Calibration.TargetCount, loaded!.Captures.Count);
        Assert.Equal(c.CalibratedUtc, loaded.CalibratedUtc);
        Assert.Equal(c.DisplayWidth, loaded.DisplayWidth);
    }

    [Fact]
    public void Store_RefusesToSaveIncompleteCalibration()
    {
        var store = new CalibrationStore(_dir);
        var c = Exact();
        c.BoardId = Guid.NewGuid();

        Assert.False(store.TrySave(c));
        Assert.Empty(store.ListBoards());
    }

    [Fact]
    public void Store_LoadReturnsFalseForUnknownBoard()
    {
        var store = new CalibrationStore(_dir);

        Assert.False(store.TryLoad(Guid.NewGuid(), out var loaded));
        Assert.Null(loaded);
    }

    [Fact]
    public void Store_SaveIsAtomicNoTempFileRemains()
    {
        var store = new CalibrationStore(_dir);
        var c = Exact();
        c.Captures = c.Targets.Select(p => p.Clone()).ToList();
        c.BoardId = Guid.NewGuid();

        Assert.True(store.TrySave(c));

        var path = store.PathFor(c.BoardId);
        Assert.True(File.Exists(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void Store_ListBoardsReportsSavedBoards()
    {
        var store = new CalibrationStore(_dir);
        foreach (var _ in new[] { 1, 2 })
        {
            var c = Exact();
            c.Captures = c.Targets.Select(p => p.Clone()).ToList();
            c.BoardId = Guid.NewGuid();
            Assert.True(store.TrySave(c));
        }

        Assert.Equal(2, store.ListBoards().Count);
    }

    [Fact]
    public void Store_LoadDegradesGracefullyOnCorruptFile()
    {
        var store = new CalibrationStore(_dir);
        var boardId = Guid.NewGuid();
        File.WriteAllText(store.PathFor(boardId), "{ not json");

        Assert.False(store.TryLoad(boardId, out var loaded));
        Assert.Null(loaded);
    }

    [Fact]
    public void Store_DeleteRemovesCalibration()
    {
        var store = new CalibrationStore(_dir);
        var c = Exact();
        c.Captures = c.Targets.Select(p => p.Clone()).ToList();
        c.BoardId = Guid.NewGuid();
        store.TrySave(c);

        Assert.True(store.Delete(c.BoardId));
        Assert.False(store.Delete(c.BoardId));
        Assert.Empty(store.ListBoards());
    }

    [Fact]
    public void Store_KeepsSeparatzEClasssSeparate()
    {
        var store = new CalibrationStore(_dir);
        var a = Skewed(1.0, 0, 0);
        a.BoardId = Guid.NewGuid();
        var b = Skewed(1.1, 5, 5);
        b.BoardId = Guid.NewGuid();

        store.TrySave(a);
        store.TrySave(b);

        store.TryLoad(a.BoardId, out var loadedA);
        store.TryLoad(b.BoardId, out var loadedB);

        Assert.Equal(1.0, loadedA!.Captures[0].X / loadedA.Targets[0].X, 2);
        Assert.NotEqual(loadedA.Captures[0].X, loadedB!.Captures[0].X);
    }
}
