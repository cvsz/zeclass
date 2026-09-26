using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace zEClass.Core;

/// <summary>How a selection region is defined on the board.</summary>
public enum SelectionShape
{
    None = 0,
    Rectangle = 1,
    Lasso = 2,
}

/// <summary>
/// A region of the board plus the content inside it. The vendor manual describes this as the
/// "select" tool exposing delete, copy and cut, so the model is a shape plus the items it
/// captures, kept separate from the strokes so the same region can be re-evaluated.
/// </summary>
public sealed class Selection
{
    public SelectionShape Shape { get; private set; } = SelectionShape.None;

    public Rect Rectangle { get; private set; }

    public IReadOnlyList<Point> LassoPoints => _lasso;

    private readonly List<Point> _lasso = new();

    public IReadOnlyList<InkStroke> Strokes => _strokes;

    public IReadOnlyList<InkImage> Images => _images;

    private readonly List<InkStroke> _strokes = new();
    private readonly List<InkImage> _images = new();

    public bool IsEmpty => Shape == SelectionShape.None || (_strokes.Count == 0 && _images.Count == 0);

    public bool IsActive => Shape != SelectionShape.None;

    /// <summary>
    /// Starts a rectangular selection between two raw drag points. Takes points rather than a
    /// <see cref="Rect"/> because a drag can be in any direction and WPF's Rect rejects a
    /// negative width, so the drag rectangle cannot be constructed until it is normalized.
    /// </summary>
    public void BeginRectangle(double x0, double y0, double x1, double y1)
    {
        Shape = SelectionShape.Rectangle;
        Rectangle = FromCorners(x0, y0, x1, y1);
        _lasso.Clear();
        _strokes.Clear();
        _images.Clear();
    }

    public void BeginRectangle(Rect rect)
    {
        Shape = SelectionShape.Rectangle;
        Rectangle = rect;
        _lasso.Clear();
        _strokes.Clear();
        _images.Clear();
    }

    public static Rect FromCorners(double x0, double y0, double x1, double y1) => new(
        Math.Min(x0, x1),
        Math.Min(y0, y1),
        Math.Abs(x1 - x0),
        Math.Abs(y1 - y0));

    public void BeginLasso()
    {
        Shape = SelectionShape.Lasso;
        _lasso.Clear();
        _strokes.Clear();
        _images.Clear();
    }

    public void AddLassoPoint(Point point)
    {
        if (Shape == SelectionShape.Lasso)
        {
            _lasso.Add(point);
        }
    }

    public void EndLasso()
    {
        if (Shape == SelectionShape.Lasso && _lasso.Count >= 3)
        {
            Rectangle = BoundsOf(_lasso);
        }
    }

    public void Clear()
    {
        Shape = SelectionShape.None;
        Rectangle = Rect.Empty;
        _lasso.Clear();
        _strokes.Clear();
        _images.Clear();
    }

    /// <summary>Recomputes captured content for the current region against the given page.</summary>
    public void Capture(BoardPage page)
    {
        _strokes.Clear();
        _images.Clear();
        if (!IsActive || page is null)
        {
            return;
        }

        foreach (var stroke in page.Strokes)
        {
            if (Shape == SelectionShape.Lasso
                    ? AnyPointInsideLasso(stroke.Points)
                    : RectContainsStroke(Rectangle, stroke))
            {
                _strokes.Add(stroke);
            }
        }

        foreach (var image in page.Images ?? new List<InkImage>())
        {
            if (Rectangle.IntersectsWith(image.Bounds))
            {
                _images.Add(image);
            }
        }
    }

    public void Remove(BoardPage page)
    {
        foreach (var stroke in _strokes)
        {
            page.Strokes.RemoveAll(s => s.Id == stroke.Id);
        }

        if (page.Images is null)
        {
            return;
        }

        foreach (var image in _images)
        {
            page.Images.RemoveAll(i => i.Id == image.Id);
        }
    }

    /// <summary>Copies captured content, re-identifying it so a paste is independent.</summary>
    public void CopyTo(BoardDocument document, double dx, double dy, IEditCommandSink sink)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(sink);

        var page = document.Active();
        var newStrokes = new List<InkStroke>();
        foreach (var stroke in _strokes)
        {
            var copy = stroke.Clone();
            copy.Id = Guid.NewGuid();
            foreach (var p in copy.Points)
            {
                p.X += dx;
                p.Y += dy;
            }

            newStrokes.Add(copy);
        }

        if (newStrokes.Count > 0)
        {
            sink.Execute(document, new AddStrokesCommand("Paste", newStrokes));
        }

        if (_images.Count > 0 && page.Images is not null)
        {
            foreach (var image in _images)
            {
                var copy = image.Clone();
                copy.Id = Guid.NewGuid();
                copy.X += dx;
                copy.Y += dy;
                page.Images.Add(copy);
            }
        }
    }


    /// <summary>Component-wise equality, since WPF's Rect does not implement value equality.</summary>
    public static bool SameRect(Rect a, Rect b) =>
        Math.Abs(a.X - b.X) < 1e-9 && Math.Abs(a.Y - b.Y) < 1e-9 &&
        Math.Abs(a.Width - b.Width) < 1e-9 && Math.Abs(a.Height - b.Height) < 1e-9;

    /// <summary>A stroke is captured when any of its samples fall inside the region.</summary>
    public static bool RectContainsStroke(Rect rect, InkStroke stroke)
    {
        foreach (var p in stroke.Points)
        {
            if (p.X >= rect.Left && p.X <= rect.Right && p.Y >= rect.Top && p.Y <= rect.Bottom)
            {
                return true;
            }
        }

        return false;
    }

    public static Rect BoundsOf(IReadOnlyList<Point> points)
    {
        if (points.Count == 0)
        {
            return Rect.Empty;
        }

        var minX = points.Min(p => p.X);
        var maxX = points.Max(p => p.X);
        var minY = points.Min(p => p.Y);
        var maxY = points.Max(p => p.Y);
        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    /// <summary>
    /// Even-odd point-in-polygon test. A stroke counts as inside when any sample is inside,
    /// which matches how teachers expect a lasso around a diagram to take the whole diagram.
    /// </summary>
    public bool AnyPointInsideLasso(IReadOnlyList<InkPoint> points)
    {
        if (_lasso.Count < 3)
        {
            return false;
        }

        foreach (var p in points)
        {
            if (IsInsideLasso(new Point(p.X, p.Y)))
            {
                return true;
            }
        }

        return false;
    }

    public bool IsInsideLasso(Point point)
    {
        if (_lasso.Count < 3)
        {
            return false;
        }

        var inside = false;
        for (int i = 0, j = _lasso.Count - 1; i < _lasso.Count; j = i++)
        {
            var pi = _lasso[i];
            var pj = _lasso[j];
            if (pi.Y > point.Y != pj.Y > point.Y &&
                point.X < ((pj.X - pi.X) * (point.Y - pi.Y) / (pj.Y - pi.Y)) + pi.X)
            {
                inside = !inside;
            }
        }

        return inside;
    }
}

/// <summary>Indirection so a selection can record commands without owning the history.</summary>
public interface IEditCommandSink
{
    void Execute(BoardDocument document, IEditCommand command);
}
