using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using zEClass.Core;
using Xunit;

namespace zEClass.Tests;

/// <summary>
/// Load-shaped checks. A classroom board accumulates thousands of strokes over a lesson and
/// many pages over a term, so the operations that run on every keystroke and page switch need
/// to stay well inside a frame budget and the file format needs to stay a sane size.
/// </summary>
public sealed class PerformanceTests
{
    [Fact]
    public void History_ExecuteStaysFastAtDepth()
    {
        var doc = new BoardDocument { PageCount = 1 };
        doc.EnsurePages();
        var history = new EditHistory(capacity: 200);
        var stroke = new InkStroke
        {
            Points = { new InkPoint { X = 1, Y = 1, Pressure = 0.5 } },
        };

        var sw = Stopwatch.StartNew();
        for (var i = 0; i < 1000; i++)
        {
            history.Execute(doc, new AddStrokesCommand("Draw", [stroke]));
        }

        sw.Stop();

        Assert.Equal(200, history.UndoCount);
        Assert.Equal(1000, doc.Active().Strokes.Count);
        Assert.True(sw.ElapsedMilliseconds < 3000,
            $"1000 executes took {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void UndoRedoStaysFastAtDepth()
    {
        var doc = new BoardDocument { PageCount = 1 };
        doc.EnsurePages();
        var history = new EditHistory(capacity: 200);
        for (var i = 0; i < 200; i++)
        {
            history.Execute(doc, new AddStrokesCommand("Draw",
                [new InkStroke { Points = { new InkPoint { X = i, Y = i } } }]));
        }

        var sw = Stopwatch.StartNew();
        for (var i = 0; i < 200; i++)
        {
            history.Undo(doc);
        }

        for (var i = 0; i < 200; i++)
        {
            history.Redo(doc);
        }

        sw.Stop();

        Assert.Equal(200, doc.Active().Strokes.Count);
        Assert.True(sw.ElapsedMilliseconds < 3000,
            $"400 undo/redo operations took {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void OutlineBuildStaysFastForLongStrokes()
    {
        var engine = new InkEngine();
        var points = new List<InkPoint>();
        for (var i = 0; i < 5000; i++)
        {
            points.Add(new InkPoint { X = i * 0.5, Y = Math.Sin(i * 0.01) * 100, Pressure = 0.6 });
        }

        var sw = Stopwatch.StartNew();
        var geometry = engine.BuildOutline(points, 4, 1);
        sw.Stop();

        Assert.False(geometry.IsEmpty());
        Assert.True(sw.ElapsedMilliseconds < 2000,
            $"5000-point outline took {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void SaveAndLoadRoundTripIsReasonableSizeForAFullLesson()
    {
        var dir = Path.Combine(Path.GetTempPath(), "zEClass-perf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var doc = new BoardDocument { PageCount = 10, Name = "Full lesson" };
            doc.EnsurePages();
            for (var p = 0; p < doc.Pages.Count; p++)
            {
                for (var s = 0; s < 300; s++)
                {
                    var stroke = new InkStroke { Width = 4 };
                    for (var i = 0; i < 40; i++)
                    {
                        stroke.Points.Add(new InkPoint
                        {
                            X = 100 + (i * 3),
                            Y = 100 + (s * 2) + (Math.Sin(i) * 5),
                            Pressure = 0.5,
                        });
                    }

                    doc.Pages[p].Strokes.Add(stroke);
                }
            }

            var path = Path.Combine(dir, "big.ebboard");
            var sw = Stopwatch.StartNew();
            BoardSerializer.Save(doc, path);
            sw.Stop();
            var saveMs = sw.ElapsedMilliseconds;

            var size = new FileInfo(path).Length;
            var loaded = BoardSerializer.Load(path);

            Assert.Equal(3000, loaded.Pages.Sum(x => x.Strokes.Count));
            Assert.True(saveMs < 8000, $"save took {saveMs} ms");
            // 120k samples should not balloon; JSON is verbose but this is the upper bound.
            Assert.True(size < 32 * 1024 * 1024, $"board file was {size / 1024 / 1024} MB");
        }
        finally
        {
            try
            {
                Directory.Delete(dir, true);
            }
            catch (IOException)
            {
            }
        }
    }

    [Fact]
    public void PageCountScalesWithoutReordering()
    {
        var doc = new BoardDocument { PageCount = 200 };
        for (var i = 0; i < 200; i++)
        {
            doc.Pages.Add(new BoardPage { Index = i });
        }

        doc.EnsurePages();

        Assert.Equal(200, doc.Pages.Count);
        Assert.Equal(Enumerable.Range(0, 200), doc.Pages.Select(p => p.Index));
    }

    [Fact]
    public void GestureTickDoesNotAccumulateState()
    {
        var gestures = new GestureRecognizer();
        gestures.NoteDown(1, 100, 100, 0);

        for (var i = 0; i < 1000; i++)
        {
            gestures.NoteHold(1, 100, 100, 100 + i);
        }

        // Repeated dwell ticks must not queue up gestures indefinitely.
        var consumed = 0;
        while (gestures.TryConsume(out _))
        {
            consumed++;
        }

        Assert.InRange(consumed, 0, 2);
    }
}
