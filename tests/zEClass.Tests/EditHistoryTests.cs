using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using zEClass.Core;
using Xunit;

namespace zEClass.Tests;

public sealed class EditHistoryTests
{
    private static BoardDocument Doc(int pages = 2)
    {
        var d = new BoardDocument { PageCount = pages };
        return d.EnsurePages();
    }

    /// <summary>
    /// EnsurePages adds a trailing page when the caller supplied fewer than PageCount, so
    /// "Doc(n)" is only n pages if the caller also created n-1 explicitly first.
    /// </summary>
    private static BoardDocument ExactPages(int count)
    {
        var d = new BoardDocument { PageCount = count };
        for (var i = 0; i < count; i++)
        {
            d.Pages.Add(new BoardPage { Index = i });
        }

        return d.EnsurePages();
    }

    private static InkStroke Stroke(double x, double y, double pressure = 0.5) => new()
    {
        Points = { new InkPoint { X = x, Y = y, Pressure = pressure } },
    };

    [Fact]
    public void Execute_AppliesCommandAndPushesUndo()
    {
        var doc = Doc();
        var history = new EditHistory();

        history.Execute(doc, new AddStrokesCommand("Draw", [Stroke(1, 2)]));

        Assert.Single(doc.Active().Strokes);
        Assert.Equal(1, history.UndoCount);
        Assert.Equal(0, history.RedoCount);
        Assert.Equal("Draw", history.LastUndoLabel);
    }

    [Fact]
    public void Undo_RemovesTheStroke()
    {
        var doc = Doc();
        var history = new EditHistory();
        history.Execute(doc, new AddStrokesCommand("Draw", [Stroke(1, 2)]));

        Assert.True(history.Undo(doc));

        Assert.Empty(doc.Active().Strokes);
        Assert.Equal(0, history.UndoCount);
        Assert.Equal(1, history.RedoCount);
    }

    [Fact]
    public void Redo_ReappliesTheStroke()
    {
        var doc = Doc();
        var history = new EditHistory();
        history.Execute(doc, new AddStrokesCommand("Draw", [Stroke(1, 2)]));
        history.Undo(doc);

        Assert.True(history.Redo(doc));

        Assert.Single(doc.Active().Strokes);
        Assert.Equal(1, history.UndoCount);
    }

    [Fact]
    public void UndoThenRedo_RestoresExactState()
    {
        var doc = Doc();
        var history = new EditHistory();
        history.Execute(doc, new AddStrokesCommand("Draw", [Stroke(10, 20), Stroke(30, 40)]));
        history.Undo(doc);
        history.Redo(doc);

        var strokes = doc.Active().Strokes;
        Assert.Equal(2, strokes.Count);
        Assert.Equal(10, strokes[0].Points[0].X);
        Assert.Equal(30, strokes[1].Points[0].X);
    }

    [Fact]
    public void Undo_OnEmptyHistoryReturnsFalse()
    {
        var doc = Doc();
        Assert.False(new EditHistory().Undo(doc));
    }

    [Fact]
    public void Redo_OnEmptyBranchReturnsFalse()
    {
        var doc = Doc();
        Assert.False(new EditHistory().Redo(doc));
    }

    [Fact]
    public void NewEdit_ClearsTheRedoBranch()
    {
        var doc = Doc();
        var history = new EditHistory();
        history.Execute(doc, new AddStrokesCommand("A", [Stroke(1, 1)]));
        history.Undo(doc);
        Assert.Equal(1, history.RedoCount);

        history.Execute(doc, new AddStrokesCommand("B", [Stroke(2, 2)]));

        Assert.Equal(0, history.RedoCount);
    }

    [Fact]
    public void History_TrimsToCapacity()
    {
        var doc = Doc();
        var history = new EditHistory(capacity: 5);
        for (var i = 0; i < 20; i++)
        {
            history.Execute(doc, new AddStrokesCommand("Draw", [Stroke(i, i)]));
        }

        Assert.Equal(5, history.UndoCount);

        // Undo pops newest-first, so undoing the five survivors removes strokes 15..19 and
        // leaves 0..14 behind.
        for (var i = 0; i < 5; i++)
        {
            Assert.True(history.Undo(doc));
        }

        Assert.Equal(15, doc.Active().Strokes.Count);
        Assert.Equal(0, doc.Active().Strokes[0].Points[0].X);
        Assert.Equal(14, doc.Active().Strokes[^1].Points[0].X);
    }

    [Fact]
    public void Clear_EmptiesBothStacks()
    {
        var doc = Doc();
        var history = new EditHistory();
        history.Execute(doc, new AddStrokesCommand("Draw", [Stroke(1, 1)]));
        history.Undo(doc);

        history.Clear();

        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void Changed_FiresOnExecuteUndoAndRedo()
    {
        var doc = Doc();
        var history = new EditHistory();
        var count = 0;
        history.Changed += (_, _) => count++;

        history.Execute(doc, new AddStrokesCommand("Draw", [Stroke(1, 1)]));
        history.Undo(doc);
        history.Redo(doc);

        Assert.Equal(3, count);
    }

    [Fact]
    public void AddStrokes_ClonesInputSoLaterMutationIsNotRecorded()
    {
        var doc = Doc();
        var history = new EditHistory();
        var stroke = Stroke(1, 2);
        history.Execute(doc, new AddStrokesCommand("Draw", [stroke]));

        stroke.Points[0].X = 999;
        history.Undo(doc);
        history.Redo(doc);

        Assert.Equal(1, doc.Active().Strokes[0].Points[0].X);
    }

    [Fact]
    public void ReplaceStrokes_UndoRestoresThePreviousList()
    {
        var doc = Doc();
        var history = new EditHistory();
        history.Execute(doc, new AddStrokesCommand("Draw", [Stroke(1, 1), Stroke(2, 2)]));

        history.Execute(doc, new ReplaceStrokesCommand("Clear", []));
        Assert.Empty(doc.Active().Strokes);

        history.Undo(doc);
        Assert.Equal(2, doc.Active().Strokes.Count);
    }

    [Fact]
    public void MoveStrokes_UndoOffsetsBackByTheSameAmount()
    {
        var doc = Doc();
        var history = new EditHistory();
        history.Execute(doc, new AddStrokesCommand("Draw", [Stroke(10, 20)]));
        var id = doc.Active().Strokes[0].Id;

        history.Execute(doc, new MoveStrokesCommand("Move", [doc.Active().Strokes[0]], 5, -3));
        Assert.Equal(15, doc.Active().Strokes[0].Points[0].X);
        Assert.Equal(17, doc.Active().Strokes[0].Points[0].Y);

        history.Undo(doc);
        Assert.Equal(id, doc.Active().Strokes[0].Id);
        Assert.Equal(10, doc.Active().Strokes[0].Points[0].X);
        Assert.Equal(20, doc.Active().Strokes[0].Points[0].Y);
    }

    [Fact]
    public void AddPage_IncrementsCountAndUndoRestoresIt()
    {
        var doc = Doc(2);
        var history = new EditHistory();

        history.Execute(doc, new AddPageCommand());
        Assert.Equal(3, doc.PageCount);
        Assert.Equal(2, doc.ActivePage);

        history.Undo(doc);
        Assert.Equal(2, doc.PageCount);
        Assert.Equal(1, doc.ActivePage);
    }

    [Fact]
    public void DeletePage_RemovesThePageAndUndoPutsItBack()
    {
        var doc = ExactPages(3);
        doc.Pages[1].Strokes.Add(Stroke(7, 7));
        var history = new EditHistory();

        history.Execute(doc, new DeletePageCommand(1));
        Assert.Equal(2, doc.PageCount);
        Assert.DoesNotContain(doc.Pages, p => p.Index == 1);

        history.Undo(doc);
        Assert.Equal(3, doc.PageCount);
        var restored = doc.Pages.First(p => p.Index == 1);
        Assert.Single(restored.Strokes);
        Assert.Equal(7, restored.Strokes[0].Points[0].X);
    }

    [Fact]
    public void DuplicatePage_ShiftsLaterPagesAndUndoRestoresIndices()
    {
        var doc = ExactPages(3);
        doc.Pages[0].Strokes.Add(Stroke(1, 1));
        doc.Pages[1].Strokes.Add(Stroke(2, 2));
        var history = new EditHistory();

        history.Execute(doc, new DuplicatePageCommand(0));

        Assert.Equal(4, doc.PageCount);
        Assert.Equal(1, doc.ActivePage);
        Assert.Equal(1, doc.Pages[0].Strokes[0].Points[0].X);
        // The duplicate carries the same ink at the new index.
        Assert.Equal(1, doc.Pages[1].Strokes[0].Points[0].X);
        // Original page 1 shifted to index 2.
        Assert.Equal(2, doc.Pages[2].Strokes[0].Points[0].X);

        history.Undo(doc);
        Assert.Equal(3, doc.PageCount);
        Assert.Equal([0, 1, 2], doc.Pages.Select(p => p.Index));
    }

    [Fact]
    public void SetPageBackground_UndoRestoresThePreviousImage()
    {
        var doc = Doc(2);
        doc.Pages[0].BackgroundImage = "old.png";
        doc.Pages[0].BackgroundColorArgb = 0xFF112233;
        var history = new EditHistory();

        history.Execute(doc, new SetPageBackgroundCommand(0, "new.png", 0xFFAABBCC));
        Assert.Equal("new.png", doc.Pages[0].BackgroundImage);
        Assert.Equal(0xFFAABBCCu, doc.Pages[0].BackgroundColorArgb);

        history.Undo(doc);
        Assert.Equal("old.png", doc.Pages[0].BackgroundImage);
        Assert.Equal(0xFF112233u, doc.Pages[0].BackgroundColorArgb);
    }

    [Fact]
    public void SetPageLocked_TogglesAndUndoes()
    {
        var doc = Doc(2);
        var history = new EditHistory();

        history.Execute(doc, new SetPageLockedCommand(0, true));
        Assert.True(doc.Pages[0].Locked);

        history.Undo(doc);
        Assert.False(doc.Pages[0].Locked);

        history.Redo(doc);
        Assert.True(doc.Pages[0].Locked);
    }

    [Fact]
    public void SetViewport_UndoRestoresThePreviousTransform()
    {
        var doc = Doc();
        doc.ViewScale = 1;
        var history = new EditHistory();

        history.Execute(doc, new SetViewportCommand(2, 100, 50));
        Assert.Equal(2, doc.ViewScale);

        history.Undo(doc);
        Assert.Equal(1, doc.ViewScale);
        Assert.Equal(0, doc.ViewOffsetX);
    }

    [Fact]
    public void UndoAcrossPages_OnlyTouchesTheAffectedPage()
    {
        var doc = Doc(2);
        doc.Pages[1].Strokes.Add(Stroke(50, 50));
        doc.ActivePage = 1;
        var history = new EditHistory();
        history.Execute(doc, new AddStrokesCommand("Draw", [Stroke(60, 60)]));

        history.Undo(doc);

        Assert.Single(doc.Pages[1].Strokes);
        Assert.Equal(50, doc.Pages[1].Strokes[0].Points[0].X);
    }
}
