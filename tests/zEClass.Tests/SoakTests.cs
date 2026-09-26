using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using zEClass.Core;
using Xunit;

namespace zEClass.Tests;

/// <summary>
/// Long-running stability checks, tagged so they can be excluded from the fast loop.
///
///   dotnet test -c Release                                   (everything, still quick)
///   dotnet test -c Release --filter Category!=Soak           (skip)
///   dotnet test -c Release --filter Category=Soak            (these only)
///
/// A classroom machine runs for hours with the app open while a teacher draws, erases, changes
/// pages and autosaves every minute. These look for the failure modes that would only show up
/// after a lesson: unbounded history, a serializer that degrades with size, page indices that
/// drift after repeated inserts and deletes.
/// </summary>
[Trait("Category", "Soak")]
public sealed class SoakTests : IDisposable
{
    private readonly string _dir;

    public SoakTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "zEClass-soak-" + Guid.NewGuid().ToString("N"));
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

    private static InkStroke Stroke(int seed, int points = 24)
    {
        var rng = new Random(seed);
        var stroke = new InkStroke
        {
            Kind = StrokeKind.Pen,
            Width = 1 + (seed % 12),
            ColorArgb = (uint)(0xFF000000 | (seed * 2654435761u)),
        };
        var x = rng.NextDouble() * 1900;
        var y = rng.NextDouble() * 1000;
        var t = DateTime.UtcNow.Ticks;
        for (var i = 0; i < points; i++)
        {
            x += rng.NextDouble() * 8 - 4;
            y += rng.NextDouble() * 8 - 4;
            stroke.Points.Add(new InkPoint
            {
                X = x,
                Y = y,
                Pressure = rng.NextDouble(),
                IsStylus = seed % 2 == 0,
                TimeTicks = t + (i * 1_000_000L),
            });
        }

        return stroke;
    }

    /// <summary>
    /// A simulated lesson: draw, erase, navigate, add and delete pages, repeatedly, while
    /// autosaving. Asserts the invariants that matter rather than timing, because a slow run is
    /// acceptable but a corrupt board is not.
    /// </summary>
    [Fact]
    public void SimulatedLessonKeepsTheDocumentConsistent()
    {
        var doc = new BoardDocument { PageCount = 5 };
        doc.EnsurePages();
        var history = new EditHistory(capacity: 200);
        var rng = new Random(99);
        var path = Path.Combine(_dir, "lesson.ebboard");

        var sw = Stopwatch.StartNew();
        for (var round = 0; round < 120; round++)
        {
            // Draw a batch of strokes.
            var batch = Enumerable.Range(0, 12).Select(i => Stroke(round * 100 + i)).ToList();
            foreach (var stroke in batch)
            {
                history.Execute(doc, new AddStrokesCommand("Draw", [stroke]));
            }

            // Erase, the way a teacher fixes a mistake.
            if (doc.Active().Strokes.Count > 6)
            {
                var doomed = doc.Active().Strokes.Take(3).ToList();
                history.Execute(doc, new RemoveStrokesCommand("Erase", doomed));
            }

            // Navigate.
            doc.ActivePage = rng.Next(doc.PageCount);

            // Occasionally restructure the page list.
            switch (rng.Next(6))
            {
                case 0:
                    history.Execute(doc, new AddPageCommand());
                    break;
                case 1 when doc.Pages.Count > 2:
                    history.Execute(doc, new DeletePageCommand(rng.Next(doc.Pages.Count)));
                    break;
                case 2:
                    history.Execute(doc, new DuplicatePageCommand(rng.Next(doc.PageCount)));
                    break;
            }

            // Undo and redo some of it.
            if (rng.Next(2) == 0)
            {
                history.Undo(doc);
            }

            if (rng.Next(3) == 0)
            {
                history.Redo(doc);
            }

            // Autosave, as the app does every minute.
            if (round % 20 == 0)
            {
                BoardSerializer.Save(doc, path);
                var reloaded = BoardSerializer.Load(path);
                Assert.Equal(doc.Pages.Count, reloaded.Pages.Count);
                Assert.Equal(doc.Pages.Sum(p => p.Strokes.Count), reloaded.Pages.Sum(p => p.Strokes.Count));
            }
        }

        sw.Stop();

        // Structural invariants that must hold no matter what the sequence was.
        Assert.Equal(Enumerable.Range(0, doc.Pages.Count), doc.Pages.Select(p => p.Index));
        Assert.All(doc.Pages, p => Assert.InRange(p.Index, 0, doc.Pages.Count - 1));
        Assert.InRange(history.UndoCount, 0, history.Capacity);
        Assert.True(doc.PageCount >= 1);
        Assert.InRange(doc.ActivePage, 0, doc.PageCount - 1);
        Assert.True(sw.Elapsed < TimeSpan.FromMinutes(2),
            $"simulated lesson took {sw.Elapsed}");
    }

    /// <summary>
    /// History must stay bounded. Unbounded growth is the most likely leak in a long session,
    /// and it is invisible until the machine runs out of memory during a lesson.
    /// </summary>
    [Fact]
    public void HistoryDoesNotGrowWithoutBound()
    {
        var doc = new BoardDocument { PageCount = 1 };
        doc.EnsurePages();
        var history = new EditHistory(capacity: 200);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        var before = GC.GetTotalMemory(true);

        for (var i = 0; i < 5000; i++)
        {
            history.Execute(doc, new AddStrokesCommand("Draw", [Stroke(i, 4)]));
        }

        Assert.Equal(200, history.UndoCount);

        // Drop the page's ink and the history's references to it, then measure again.
        doc.Active().Strokes.Clear();
        history.Clear();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        var after = GC.GetTotalMemory(true);

        // Allow generous headroom for GC noise; the point is to catch order-of-magnitude growth.
        Assert.True(after - before < 32 * 1024 * 1024,
            $"managed heap grew by {(after - before) / 1024 / 1024} MB");
    }

    /// <summary>
    /// Repeated save and load must not accumulate state, which would show up as a board file
    /// that grows every time it is opened and saved.
    /// </summary>
    [Fact]
    public void RepeatedSaveLoadDoesNotGrowTheFile()
    {
        var doc = new BoardDocument { PageCount = 3 };
        doc.EnsurePages();
        for (var p = 0; p < 3; p++)
        {
            for (var s = 0; s < 50; s++)
            {
                doc.Pages[p].Strokes.Add(Stroke(p * 100 + s, 16));
            }
        }

        var path = Path.Combine(_dir, "stable.ebboard");
        BoardSerializer.Save(doc, path);
        var first = new FileInfo(path).Length;

        for (var i = 0; i < 10; i++)
        {
            var reloaded = BoardSerializer.Load(path);
            BoardSerializer.Save(reloaded, path);
        }

        var after = new FileInfo(path).Length;
        Assert.True(Math.Abs(after - first) <= 1024,
            $"file grew from {first} to {after} bytes over ten round trips");
    }

    /// <summary>
    /// Erasing repeatedly must not corrupt z-order or lose unrelated strokes, which is the bug
    /// a teacher would notice as content vanishing.
    /// </summary>
    [Fact]
    public void RepeatedEraseKeepsUnrelatedStrokesIntact()
    {
        var doc = new BoardDocument { PageCount = 1 };
        doc.EnsurePages();
        var history = new EditHistory();

        for (var i = 0; i < 200; i++)
        {
            history.Execute(doc, new AddStrokesCommand("Draw", [Stroke(i, 8)]));
        }

        var expectedTotal = 200;
        for (var round = 0; round < 20; round++)
        {
            var doomed = doc.Active().Strokes.Take(5).ToList();
            history.Execute(doc, new RemoveStrokesCommand("Erase", doomed));
            expectedTotal -= doomed.Count;
            Assert.Equal(expectedTotal, doc.Active().Strokes.Count);
        }

        for (var i = 0; i < 20; i++)
        {
            history.Undo(doc);
        }

        Assert.Equal(expectedTotal + (20 * 5), doc.Active().Strokes.Count);

        // And redoing them removes them again, in the same order.
        for (var i = 0; i < 20; i++)
        {
            history.Redo(doc);
        }

        Assert.Equal(expectedTotal, doc.Active().Strokes.Count);
    }

    /// <summary>Many pages must stay addressable and correctly indexed.</summary>
    [Fact]
    public void ManyPagesRemainIndexed()
    {
        var doc = new BoardDocument { PageCount = 1 };
        doc.EnsurePages();
        var history = new EditHistory();

        for (var i = 0; i < 60; i++)
        {
            history.Execute(doc, new AddPageCommand());
        }

        Assert.Equal(61, doc.Pages.Count);
        doc.EnsurePages();
        Assert.Equal(Enumerable.Range(0, 61), doc.Pages.Select(p => p.Index));

        for (var i = 0; i < 30; i++)
        {
            history.Execute(doc, new DeletePageCommand(doc.Pages.Count - 1));
        }

        doc.EnsurePages();
        Assert.Equal(31, doc.Pages.Count);
        Assert.Equal(Enumerable.Range(0, 31), doc.Pages.Select(p => p.Index));
    }
}
