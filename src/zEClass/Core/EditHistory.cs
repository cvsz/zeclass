using System;
using System.Collections.Generic;
using System.Linq;

namespace zEClass.Core;

/// <summary>
/// A reversible edit. Every mutation to a board goes through one of these so that undo and
/// redo are symmetrical by construction rather than by remembering what to undo.
/// </summary>
public interface IEditCommand
{
    string Label { get; }

    void Redo(BoardDocument document);

    void Undo(BoardDocument document);

    /// <summary>Commands that cannot be reverted (none currently) are dropped from history.</summary>
    bool IsReversible => true;
}

/// <summary>Adds strokes, typically the end of a hand-drawn stroke.</summary>
public sealed class AddStrokesCommand : IEditCommand
{
    private readonly List<InkStroke> _strokes;

    public AddStrokesCommand(string label, IEnumerable<InkStroke> strokes)
    {
        Label = label;
        _strokes = strokes.Select(s => s.Clone()).ToList();
    }

    public string Label { get; }

    public IReadOnlyList<InkStroke> Strokes => _strokes;

    public void Redo(BoardDocument document) => document.Active().Strokes.AddRange(_strokes);

    public void Undo(BoardDocument document)
    {
        var page = document.Active();
        foreach (var stroke in _strokes)
        {
            var index = page.Strokes.FindIndex(s => s.Id == stroke.Id);
            if (index >= 0)
            {
                page.Strokes.RemoveAt(index);
            }
        }
    }
}

/// <summary>Removes specific strokes, for eraser strokes that clip a partial stroke.</summary>
public sealed class RemoveStrokesCommand : IEditCommand
{
    private readonly List<InkStroke> _removed = new();
    private readonly List<int> _indices = new();
    private int _pageIndex = -1;
    private bool _preloaded;

    public RemoveStrokesCommand(string label) => Label = label;

    public string Label { get; }

    public IReadOnlyList<InkStroke> Removed => _removed;

    public void Redo(BoardDocument document)
    {
        if (_removed.Count == 0)
        {
            return;
        }

        if (_preloaded)
        {
            // The caller supplied the exact strokes to remove, so their current indices are
            // what matters rather than a search by id.
            _pageIndex = document.ActivePage;
            _indices.Clear();
            var target = document.Active();
            foreach (var stroke in _removed)
            {
                var index = target.Strokes.FindIndex(s => s.Id == stroke.Id);
                if (index >= 0)
                {
                    _indices.Add(index);
                }
            }

            _indices.Sort();
            for (var i = _indices.Count - 1; i >= 0; i--)
            {
                target.Strokes.RemoveAt(_indices[i]);
            }

            return;
        }

        _pageIndex = document.ActivePage;
        _indices.Clear();
        var page = document.Active();

        // Record positions in ascending order so Undo can reinsert in the same z-order.
        var positions = new List<(int Index, InkStroke Stroke)>();
        foreach (var stroke in _removed)
        {
            var index = page.Strokes.FindIndex(s => s.Id == stroke.Id);
            if (index >= 0)
            {
                positions.Add((index, stroke));
            }
        }

        foreach (var (index, _) in positions.OrderBy(p => p.Index))
        {
            page.Strokes.RemoveAt(index);
        }

        _indices.AddRange(positions.OrderBy(p => p.Index).Select(p => p.Index));
    }

    /// <summary>Populates the removed set, so a caller can use this as a plain eraser command.</summary>
    public RemoveStrokesCommand(string label, IEnumerable<InkStroke> removed) : this(label)
    {
        _removed.AddRange(removed.Select(s => s.Clone()));
        _preloaded = true;
    }

    public void Undo(BoardDocument document)
    {
        if (_removed.Count == 0 || _pageIndex < 0)
        {
            return;
        }

        var page = document.Pages.FirstOrDefault(p => p.Index == _pageIndex);
        if (page is null)
        {
            return;
        }

        // Indices ascend, so inserting in that order restores the original z-order. Index i
        // pairs with _removed[i] because Redo recorded them in the same ascending order.
        for (var i = 0; i < _indices.Count && i < _removed.Count; i++)
        {
            var position = Math.Min(_indices[i], page.Strokes.Count);
            page.Strokes.Insert(position, _removed[i]);
        }
    }
}

/// <summary>Replaces the whole stroke list of the active page, used by clear and selection ops.</summary>
public sealed class ReplaceStrokesCommand : IEditCommand
{
    private List<InkStroke> _before = new();
    private List<InkStroke> _after;

    public ReplaceStrokesCommand(string label, IEnumerable<InkStroke> replacement)
    {
        Label = label;
        _after = replacement.Select(s => s.Clone()).ToList();
    }

    public string Label { get; }

    public void Redo(BoardDocument document)
    {
        var page = document.Active();
        _before = page.Strokes.Select(s => s.Clone()).ToList();
        page.Strokes = _after.Select(s => s.Clone()).ToList();
    }

    public void Undo(BoardDocument document)
    {
        document.Active().Strokes = _before.Select(s => s.Clone()).ToList();
    }
}

/// <summary>Moves strokes by an offset, for drag-and-drop of a selection.</summary>
public sealed class MoveStrokesCommand : IEditCommand
{
    private readonly List<InkStroke> _strokes;
    private readonly double _dx;
    private readonly double _dy;

    public MoveStrokesCommand(string label, IEnumerable<InkStroke> strokes, double dx, double dy)
    {
        Label = label;
        _strokes = strokes.Select(s => s.Clone()).ToList();
        _dx = dx;
        _dy = dy;
    }

    public string Label { get; }

    public void Redo(BoardDocument document) => Apply(document, _dx, _dy);

    public void Undo(BoardDocument document) => Apply(document, -_dx, -_dy);

    private void Apply(BoardDocument document, double dx, double dy)
    {
        foreach (var source in _strokes)
        {
            foreach (var target in document.Active().Strokes)
            {
                if (target.Id != source.Id)
                {
                    continue;
                }

                foreach (var p in target.Points)
                {
                    p.X += dx;
                    p.Y += dy;
                }
            }
        }
    }
}

/// <summary>Deletes a page, remembering enough to put it back where it was.</summary>
public sealed class DeletePageCommand : IEditCommand
{
    private BoardPage? _page;
    private int _activeBefore;
    private int _pageCountBefore;

    public DeletePageCommand(int pageIndex) => PageIndex = pageIndex;

    public string Label { get; } = "Delete page";

    public int PageIndex { get; }

    public void Redo(BoardDocument document)
    {
        _page = document.Pages.FirstOrDefault(p => p.Index == PageIndex);
        _activeBefore = document.ActivePage;
        _pageCountBefore = document.PageCount;
        document.Pages.RemoveAll(p => p.Index == PageIndex);

        // Pages keep their original indices after a delete, so the count follows the list
        // rather than being decremented blindly.
        document.PageCount = Math.Max(1, document.Pages.Count);
        document.ActivePage = Math.Clamp(document.ActivePage, 0, document.PageCount - 1);
    }

    public void Undo(BoardDocument document)
    {
        if (_page is null)
        {
            return;
        }

        document.Pages.Add(_page.Clone());
        document.PageCount = Math.Max(_pageCountBefore, document.Pages.Count);
        document.ActivePage = Math.Clamp(_activeBefore, 0, document.PageCount - 1);
    }
}

/// <summary>Adds a blank page, recording what it displaced for undo.</summary>
public sealed class AddPageCommand : IEditCommand
{
    private int _index;

    public AddPageCommand() => _index = -1;

    public string Label => "Add page";

    public void Redo(BoardDocument document)
    {
        _index = document.PageCount;
        document.Pages.Add(new BoardPage { Index = _index, Name = $"Page {_index + 1}" });
        document.PageCount = _index + 1;
        document.ActivePage = _index;
    }

    public void Undo(BoardDocument document)
    {
        if (_index < 0)
        {
            return;
        }

        document.Pages.RemoveAll(p => p.Index == _index);
        document.PageCount = Math.Max(1, _index);
        document.ActivePage = Math.Clamp(_index - 1, 0, document.PageCount - 1);
    }
}

/// <summary>Duplicates a page, including its ink.</summary>
public sealed class DuplicatePageCommand : IEditCommand
{
    private BoardPage? _copy;
    private int _insertedAt = -1;

    public DuplicatePageCommand(int pageIndex) => PageIndex = pageIndex;

    public string Label => "Duplicate page";

    public int PageIndex { get; }

    public void Redo(BoardDocument document)
    {
        var source = document.Pages.FirstOrDefault(p => p.Index == PageIndex);
        if (source is null)
        {
            return;
        }

        _copy = source.Clone();
        _insertedAt = PageIndex + 1;

        // Shift later pages up to make room, keeping indices contiguous.
        foreach (var p in document.Pages.Where(p => p.Index >= _insertedAt))
        {
            p.Index++;
        }

        _copy.Index = _insertedAt;
        _copy.Name = $"{_copy.Name} copy";
        document.Pages.Add(_copy);

        // Indices stay contiguous after the shift, so the count is simply the list length.
        document.Pages.Sort((a, b) => a.Index.CompareTo(b.Index));
        document.PageCount = document.Pages.Count;
        document.ActivePage = _insertedAt;
    }

    public void Undo(BoardDocument document)
    {
        if (_insertedAt < 0)
        {
            return;
        }

        document.Pages.RemoveAll(p => p.Index == _insertedAt);
        foreach (var p in document.Pages.Where(p => p.Index > _insertedAt))
        {
            p.Index--;
        }

        document.Pages.Sort((a, b) => a.Index.CompareTo(b.Index));
        document.PageCount = document.Pages.Count;
        document.ActivePage = Math.Clamp(PageIndex, 0, document.PageCount - 1);
    }
}

/// <summary>Sets a page background, restoring the previous one on undo.</summary>
public sealed class SetPageBackgroundCommand : IEditCommand
{
    private string? _previousImage;
    private uint _previousColor;
    private bool _captured;

    public SetPageBackgroundCommand(int pageIndex, string? image, uint colorArgb)
    {
        PageIndex = pageIndex;
        Image = image;
        ColorArgb = colorArgb;
    }

    public string Label => "Page background";

    public int PageIndex { get; }

    public string? Image { get; }

    public uint ColorArgb { get; }

    public void Redo(BoardDocument document)
    {
        var page = document.Pages.FirstOrDefault(p => p.Index == PageIndex);
        if (page is null)
        {
            return;
        }

        if (!_captured)
        {
            _previousImage = page.BackgroundImage;
            _previousColor = page.BackgroundColorArgb;
            _captured = true;
        }

        page.BackgroundImage = Image;
        page.BackgroundColorArgb = ColorArgb;
    }

    public void Undo(BoardDocument document)
    {
        if (!_captured)
        {
            return;
        }

        var page = document.Pages.FirstOrDefault(p => p.Index == PageIndex);
        if (page is null)
        {
            return;
        }

        page.BackgroundImage = _previousImage;
        page.BackgroundColorArgb = _previousColor;
    }
}

/// <summary>Sets a page's locked flag, which shields its content from clearing.</summary>
public sealed class SetPageLockedCommand : IEditCommand
{
    private bool _captured;
    private bool _previous;

    public SetPageLockedCommand(int pageIndex, bool locked)
    {
        PageIndex = pageIndex;
        IsLocked = locked;
        Label = locked ? "Lock page" : "Unlock page";
    }

    public string Label { get; }

    public int PageIndex { get; }

    public bool IsLocked { get; }

    public void Redo(BoardDocument document)
    {
        var page = document.Pages.FirstOrDefault(p => p.Index == PageIndex);
        if (page is null)
        {
            return;
        }

        if (!_captured)
        {
            _previous = page.Locked;
            _captured = true;
        }

        page.Locked = IsLocked;
    }

    public void Undo(BoardDocument document)
    {
        if (!_captured)
        {
            return;
        }

        var page = document.Pages.FirstOrDefault(p => p.Index == PageIndex);
        if (page is not null)
        {
            page.Locked = _previous;
        }
    }
}

/// <summary>Changes the zoom/pan viewport.</summary>
public sealed class SetViewportCommand : IEditCommand
{
    private double _previousScale;
    private double _previousOffsetX;
    private double _previousOffsetY;

    public SetViewportCommand(double scale, double offsetX, double offsetY)
    {
        Scale = scale;
        OffsetX = offsetX;
        OffsetY = offsetY;
    }

    public string Label => "Zoom";

    public double Scale { get; }

    public double OffsetX { get; }

    public double OffsetY { get; }

    public void Redo(BoardDocument document)
    {
        _previousScale = document.ViewScale;
        _previousOffsetX = document.ViewOffsetX;
        _previousOffsetY = document.ViewOffsetY;
        document.ViewScale = Scale;
        document.ViewOffsetX = OffsetX;
        document.ViewOffsetY = OffsetY;
    }

    public void Undo(BoardDocument document)
    {
        document.ViewScale = _previousScale;
        document.ViewOffsetX = _previousOffsetX;
        document.ViewOffsetY = _previousOffsetY;
    }
}

/// <summary>
/// Bounded undo/redo stacks. The bound matters in a classroom: a teacher drawing for an hour
/// must not be able to exhaust memory through history, and older history is the least valuable.
/// </summary>
public sealed class EditHistory
{
    private readonly Stack<IEditCommand> _undo = new();
    private readonly Stack<IEditCommand> _redo = new();

    public EditHistory(int capacity = 200) => Capacity = Math.Max(1, capacity);

    public int Capacity { get; }

    public int UndoCount => _undo.Count;

    public int RedoCount => _redo.Count;

    public string? LastUndoLabel => _undo.TryPeek(out var c) ? c.Label : null;

    public string? LastRedoLabel => _redo.TryPeek(out var c) ? c.Label : null;

    public event EventHandler? Changed;

    public void Execute(BoardDocument document, IEditCommand command)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(command);

        command.Redo(document);
        if (!command.IsReversible)
        {
            _undo.Clear();
            Raise();
            return;
        }

        _undo.Push(command);

        // A new edit invalidates the redo branch, which is what every editor does.
        _redo.Clear();
        Trim();
        Raise();
    }

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public bool Undo(BoardDocument document)
    {
        if (!_undo.TryPop(out var command))
        {
            return false;
        }

        command.Undo(document);
        _redo.Push(command);
        Raise();
        return true;
    }

    public bool Redo(BoardDocument document)
    {
        if (!_redo.TryPop(out var command))
        {
            return false;
        }

        command.Redo(document);
        _undo.Push(command);
        Trim();
        Raise();
        return true;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        Raise();
    }

    private void Trim()
    {
        while (_undo.Count > Capacity)
        {
            // Stack enumeration is newest-first, so the oldest entries sit at the tail. Copy
            // those out, clear, and push them back in their original relative order so the
            // survivors are exactly the most recent Capacity commands.
            var items = _undo.ToArray();
            var survivors = items[..Capacity];
            _undo.Clear();
            for (var i = survivors.Length - 1; i >= 0; i--)
            {
                _undo.Push(survivors[i]);
            }
        }
    }

    private void Raise() => Changed?.Invoke(this, EventArgs.Empty);
}
