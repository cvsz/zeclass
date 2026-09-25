using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using EBoard.Core;
using Xunit;

namespace EBoard.Tests;

public sealed class SelectionTests
{
    private static BoardPage PageWith(params InkStroke[] strokes)
    {
        var page = new BoardPage { Index = 0 };
        page.Strokes.AddRange(strokes);
        return page;
    }

    private static InkStroke StrokeAt(params (double X, double Y)[] points) => new()
    {
        Points = points.Select(p => new InkPoint { X = p.X, Y = p.Y, Pressure = 0.5 }).ToList(),
    };

    [Fact]
    public void BeginRectangle_NormalizesDragDirection()
    {
        var selection = new Selection();
        // Dragged up and to the left, which WPF's Rect cannot represent directly.
        selection.BeginRectangle(100, 100, 50, 70);

        Assert.True(Selection.SameRect(new Rect(50, 70, 50, 30), selection.Rectangle),
            $"got {selection.Rectangle}");
    }

    [Fact]
    public void FromCorners_ProducesAValidRectForAnyDragDirection()
    {
        var a = Selection.FromCorners(100, 100, 50, 70);
        var b = Selection.FromCorners(50, 70, 100, 100);

        Assert.True(Selection.SameRect(a, b));
        Assert.True(a.Width >= 0 && a.Height >= 0);
    }

    [Fact]
    public void Capture_IncludesStrokesWhoseSamplesAreInside()
    {
        var inside = StrokeAt((10, 10), (20, 20));
        var outside = StrokeAt((200, 200));
        var page = PageWith(inside, outside);
        var selection = new Selection();

        selection.BeginRectangle(new Rect(0, 0, 100, 100));
        selection.Capture(page);

        Assert.Single(selection.Strokes);
        Assert.Equal(inside.Id, selection.Strokes[0].Id);
    }

    [Fact]
    public void Capture_IsEmptyWhenRegionHoldsNothing()
    {
        var page = PageWith(StrokeAt((500, 500)));
        var selection = new Selection();
        selection.BeginRectangle(new Rect(0, 0, 10, 10));

        selection.Capture(page);

        Assert.True(selection.IsEmpty);
    }

    [Fact]
    public void Remove_DeletesCapturedStrokesOnly()
    {
        var keep = StrokeAt((200, 200));
        var drop = StrokeAt((10, 10));
        var page = PageWith(keep, drop);
        var selection = new Selection();
        selection.BeginRectangle(new Rect(0, 0, 50, 50));
        selection.Capture(page);

        selection.Remove(page);

        Assert.Single(page.Strokes);
        Assert.Equal(keep.Id, page.Strokes[0].Id);
    }

    [Fact]
    public void Remove_AlsoRemovesCapturedImages()
    {
        var page = PageWith();
        page.Images.Add(new InkImage { X = 10, Y = 10, Width = 50, Height = 50 });
        page.Images.Add(new InkImage { X = 300, Y = 300, Width = 50, Height = 50 });
        var selection = new Selection();
        selection.BeginRectangle(new Rect(0, 0, 100, 100));
        selection.Capture(page);

        selection.Remove(page);

        Assert.Single(page.Images);
        Assert.Equal(300, page.Images[0].X);
    }

    [Fact]
    public void Clear_ResetsRegionAndContent()
    {
        var page = PageWith(StrokeAt((10, 10)));
        var selection = new Selection();
        selection.BeginRectangle(new Rect(0, 0, 50, 50));
        selection.Capture(page);

        selection.Clear();

        Assert.False(selection.IsActive);
        Assert.True(selection.IsEmpty);
    }

    [Fact]
    public void Lasso_IsInsideForConcaveShape()
    {
        var selection = new Selection();
        selection.BeginLasso();
        // A simple square.
        selection.AddLassoPoint(new Point(0, 0));
        selection.AddLassoPoint(new Point(100, 0));
        selection.AddLassoPoint(new Point(100, 100));
        selection.AddLassoPoint(new Point(0, 100));
        selection.EndLasso();

        Assert.True(selection.IsInsideLasso(new Point(50, 50)));
        Assert.False(selection.IsInsideLasso(new Point(150, 50)));
    }

    [Fact]
    public void Lasso_CapturesStrokesWithAnySampleInside()
    {
        var stroke = StrokeAt((-10, -10), (50, 50));
        var page = PageWith(stroke);
        var selection = new Selection();
        selection.BeginLasso();
        selection.AddLassoPoint(new Point(0, 0));
        selection.AddLassoPoint(new Point(100, 0));
        selection.AddLassoPoint(new Point(100, 100));
        selection.AddLassoPoint(new Point(0, 100));
        selection.EndLasso();

        selection.Capture(page);

        Assert.Single(selection.Strokes);
    }

    [Fact]
    public void Lasso_WithTooFewPointsCapturesNothing()
    {
        var page = PageWith(StrokeAt((10, 10)));
        var selection = new Selection();
        selection.BeginLasso();
        selection.AddLassoPoint(new Point(0, 0));
        selection.AddLassoPoint(new Point(100, 100));
        selection.EndLasso();

        selection.Capture(page);

        Assert.Empty(selection.Strokes);
    }

    [Fact]
    public void RectContainsStroke_IsTrueWhenAnySampleIsInside()
    {
        var rect = new Rect(0, 0, 10, 10);

        Assert.True(Selection.RectContainsStroke(rect, StrokeAt((-5, -5), (5, 5))));
        Assert.False(Selection.RectContainsStroke(rect, StrokeAt((50, 50))));
    }

    [Fact]
    public void BoundsOf_ComputesTheEnvelope()
    {
        var bounds = Selection.BoundsOf([
            new Point(10, 20), new Point(-5, 40), new Point(30, 5),
        ]);

        Assert.Equal(-5, bounds.X);
        Assert.Equal(5, bounds.Y);
        Assert.Equal(35, bounds.Width);
        Assert.Equal(35, bounds.Height);
    }

    [Fact]
    public void CopyTo_AppliesOffsetAndReidentifiesContent()
    {
        var page = PageWith(StrokeAt((10, 10)));
        var selection = new Selection();
        selection.BeginRectangle(new Rect(0, 0, 50, 50));
        selection.Capture(page);
        var originalId = selection.Strokes[0].Id;

        var doc = new BoardDocument { PageCount = 1 };
        doc.EnsurePages();
        var sink = new RecordingSink();
        selection.CopyTo(doc, 20, 30, sink);

        var pasted = sink.Commands.OfType<AddStrokesCommand>().Single().Strokes;
        Assert.NotEqual(originalId, pasted[0].Id);
        Assert.Equal(30, pasted[0].Points[0].X);
        Assert.Equal(40, pasted[0].Points[0].Y);
    }

    [Fact]
    public void Capture_OnNullPageIsANoOp()
    {
        var selection = new Selection();
        selection.BeginRectangle(new Rect(0, 0, 10, 10));

        selection.Capture(null!);

        Assert.True(selection.IsEmpty);
    }

    private sealed class RecordingSink : IEditCommandSink
    {
        public List<IEditCommand> Commands { get; } = new();

        public void Execute(BoardDocument document, IEditCommand command) => Commands.Add(command);
    }
}
