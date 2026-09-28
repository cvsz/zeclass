using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using zEClass.Core;
using Xunit;

namespace zEClass.Tests;

public sealed class BoardSerializerTests : IDisposable
{
    private readonly string _dir;

    public BoardSerializerTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "zEClass-tests-" + Guid.NewGuid().ToString("N"));
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

    [Fact]
    public void EnsurePages_CreatesEveryPageUpToPageCount()
    {
        var doc = new BoardDocument { PageCount = 5 };
        doc.EnsurePages();

        Assert.Equal(5, doc.Pages.Count);
        Assert.Equal([0, 1, 2, 3, 4], doc.Pages.Select(p => p.Index));
    }

    [Fact]
    public void EnsurePages_DropsPagesBeyondPageCount()
    {
        var doc = new BoardDocument { PageCount = 2 };
        doc.Pages.Add(new BoardPage { Index = 0 });
        doc.Pages.Add(new BoardPage { Index = 1 });
        doc.Pages.Add(new BoardPage { Index = 7 });
        doc.EnsurePages();

        Assert.Equal(2, doc.Pages.Count);
        Assert.DoesNotContain(doc.Pages, p => p.Index == 7);
    }

    [Fact]
    public void EnsurePages_ClampsOutOfRangeActivePage()
    {
        var doc = new BoardDocument { PageCount = 3, ActivePage = 9 };
        doc.EnsurePages();

        Assert.Equal(0, doc.ActivePage);
    }

    [Fact]
    public void Active_ReturnsPageAtActiveIndex()
    {
        var doc = new BoardDocument { PageCount = 4, ActivePage = 2 };
        doc.EnsurePages();

        Assert.Equal(2, doc.Active().Index);
    }

    [Fact]
    public void SaveAndLoad_RoundTripsInkAndSettings()
    {
        var doc = new BoardDocument { Name = "Chemistry 101", PageCount = 3, ActivePage = 1 };
        doc.EnsurePages();
        doc.Pages[1].Strokes.Add(new InkStroke
        {
            Kind = StrokeKind.Highlighter,
            ColorArgb = 0x80FDD835,
            Width = 12,
            Opacity = 0.35,
            Points =
            [
                new InkPoint { X = 10, Y = 20, Pressure = 0.75, IsStylus = true, Tilt = 31.5 },
                new InkPoint { X = 30, Y = 45, Pressure = 0.25, IsStylus = true },
            ],
        });

        var path = Path.Combine(_dir, "board.ebboard");
        BoardSerializer.Save(doc, path);
        var loaded = BoardSerializer.Load(path);

        Assert.Equal("Chemistry 101", loaded.Name);
        Assert.Equal(3, loaded.PageCount);
        Assert.Equal(1, loaded.ActivePage);

        var stroke = Assert.Single(loaded.Pages[1].Strokes);
        Assert.Equal(StrokeKind.Highlighter, stroke.Kind);
        Assert.Equal(0x80FDD835, stroke.ColorArgb);
        Assert.Equal(12, stroke.Width);
        Assert.Equal(2, stroke.Points.Count);
        Assert.Equal(0.75, stroke.Points[0].Pressure);
        Assert.True(stroke.Points[0].IsStylus);
        Assert.Equal(31.5, stroke.Points[0].Tilt);
    }

    [Fact]
    public void Save_IsAtomic_NoTemporaryFileLeftBehind()
    {
        var doc = new BoardDocument();
        var path = Path.Combine(_dir, "atomic.ebboard");
        BoardSerializer.Save(doc, path);

        Assert.True(File.Exists(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void Save_OverwritesExistingBoard()
    {
        var path = Path.Combine(_dir, "twice.ebboard");
        BoardSerializer.Save(new BoardDocument { Name = "first" }, path);
        BoardSerializer.Save(new BoardDocument { Name = "second" }, path);

        Assert.Equal("second", BoardSerializer.Load(path).Name);
    }

    [Fact]
    public void TryLoad_ReportsErrorForCorruptFile()
    {
        var path = Path.Combine(_dir, "corrupt.ebboard");
        File.WriteAllText(path, "{ this is not json");

        var ok = BoardSerializer.TryLoad(path, out var doc, out var error);

        Assert.False(ok);
        Assert.Null(doc);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void TryLoad_ReportsErrorForMissingFile()
    {
        var ok = BoardSerializer.TryLoad(Path.Combine(_dir, "missing.zEClass"), out var doc, out var error);

        Assert.False(ok);
        Assert.Null(doc);
        Assert.NotNull(error);
    }

    [Fact]
    public void Load_NormalizesPagesOnLegacyOrPartialFile()
    {
        var path = Path.Combine(_dir, "partial.ebboard");
        File.WriteAllText(path, "{\"Name\":\"partial\",\"PageCount\":3,\"Pages\":[]}");

        var doc = BoardSerializer.Load(path);

        Assert.Equal(3, doc.Pages.Count);
    }

    [Fact]
    public void InkStroke_Clone_IsIndependentCopy()
    {
        var stroke = new InkStroke
        {
            Kind = StrokeKind.Pen,
            Points = { new InkPoint { X = 1, Y = 2, Pressure = 0.5 } },
        };

        var clone = stroke.Clone();
        clone.Points[0].X = 999;
        clone.Points.Add(new InkPoint { X = 5, Y = 5 });

        Assert.Equal(1, stroke.Points[0].X);
        Assert.Single(stroke.Points);
    }

    [Fact]
    public void TryLoad_RejectsFileOverTheSizeLimit()
    {
        var path = Path.Combine(_dir, "big.ebboard");
        File.WriteAllText(path, new string('x', 2048));

        var ok = BoardSerializer.TryLoad(path, 1024, out var doc, out var error);

        Assert.False(ok);
        Assert.Null(doc);
        Assert.Contains("limit", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Load_NullPagesList_IsRepairedToAWorkingDocument()
    {
        var path = Path.Combine(_dir, "nullpages.ebboard");
        File.WriteAllText(path, "{\"PageCount\":3,\"Pages\":null}");

        var doc = BoardSerializer.Load(path);

        Assert.Equal(3, doc.Pages.Count);
        Assert.Equal(0, doc.ActivePage);
        Assert.NotNull(doc.Active());
    }

    [Fact]
    public void Load_NegativePageCount_ClampsToOnePage()
    {
        var path = Path.Combine(_dir, "negative.ebboard");
        File.WriteAllText(path, "{\"PageCount\":-5,\"Pages\":[]}");

        var doc = BoardSerializer.Load(path);

        Assert.Equal(1, doc.PageCount);
        Assert.NotNull(doc.Active());
    }

    [Fact]
    public void Load_AbsurdPageCount_ClampsToCapAndStaysFast()
    {
        var path = Path.Combine(_dir, "absurd.ebboard");
        File.WriteAllText(path, "{\"PageCount\":2000000000,\"Pages\":[]}");

        var doc = BoardSerializer.Load(path);

        Assert.Equal(BoardDocument.MaxPageCount, doc.PageCount);
        Assert.Equal(BoardDocument.MaxPageCount, doc.Pages.Count);
        Assert.NotNull(doc.Active());
    }

    [Fact]
    public void Load_NullEntriesAndMissingLists_AreRepaired()
    {
        var path = Path.Combine(_dir, "nullentries.ebboard");
        File.WriteAllText(path,
            "{\"PageCount\":1,\"Images\":[null]," +
            "\"Pages\":[{\"Index\":0,\"Strokes\":[null,{\"Kind\":99,\"Width\":-3," +
            "\"Opacity\":9,\"Points\":null,\"Geometry\":\"ok\",\"Text\":\"t\"}]," +
            "\"Images\":[null]}]}");

        var doc = BoardSerializer.Load(path);

        var page = Assert.Single(doc.Pages);
        var stroke = Assert.Single(page.Strokes);
        Assert.Equal(StrokeKind.Pen, stroke.Kind);
        Assert.Equal(4.0, stroke.Width);
        Assert.Equal(1.0, stroke.Opacity);
        Assert.Empty(stroke.Points);
        Assert.Empty(doc.Images);
        Assert.Empty(page.Images);
    }

    [Fact]
    public void Load_NonFiniteNumbers_AreSanitized()
    {
        var path = Path.Combine(_dir, "nonfinite.ebboard");
        File.WriteAllText(path,
            "{\"PageCount\":1,\"CanvasWidth\":null,\"CanvasHeight\":null,\"ViewScale\":null," +
            "\"Pages\":[{\"Index\":0,\"Strokes\":[{\"Points\":[{\"X\":null,\"Y\":null," +
            "\"Pressure\":null,\"Tilt\":\"\"}]}]}]}");

        var doc = BoardSerializer.Load(path);

        Assert.Equal(1920, doc.CanvasWidth);
        Assert.Equal(1080, doc.CanvasHeight);
        Assert.Equal(1.0, doc.ViewScale);
        var point = Assert.Single(doc.Pages[0].Strokes[0].Points);
        Assert.Equal(0, point.X);
        Assert.Equal(0, point.Y);
        Assert.Equal(0, point.Pressure);
        Assert.True(double.IsNaN(point.Tilt));
    }

    [Fact]
    public void Load_TruncatesOversizedGeometryAndText()
    {
        var path = Path.Combine(_dir, "huge.ebboard");
        var big = new string('g', BoardDocument.MaxShapeTextChars + 10);
        File.WriteAllText(path,
            "{\"PageCount\":1,\"Pages\":[{\"Index\":0,\"Strokes\":[{\"Geometry\":\"" + big +
            "\",\"Text\":\"" + big + "\"}]}]}");

        var doc = BoardSerializer.Load(path);

        var stroke = Assert.Single(doc.Pages[0].Strokes);
        Assert.Equal(BoardDocument.MaxShapeTextChars, stroke.Geometry!.Length);
        Assert.Equal(BoardDocument.MaxShapeTextChars, stroke.Text!.Length);
    }

    [Fact]
    public void Save_FailureLeavesTheExistingBoardUntouched()
    {
        var path = Path.Combine(_dir, "guarded.ebboard");
        BoardSerializer.Save(new BoardDocument { Name = "original" }, path);

        // Occupy the temporary path so the write cannot start; the save must fail without
        // touching the board that is already on disk.
        Directory.CreateDirectory(path + ".tmp");
        var ex = Record.Exception(() =>
            BoardSerializer.Save(new BoardDocument { Name = "second" }, path));
        Directory.Delete(path + ".tmp");

        Assert.True(ex is IOException or UnauthorizedAccessException,
            $"unexpected {ex?.GetType().FullName}: {ex?.Message}");
        Assert.Equal("original", BoardSerializer.Load(path).Name);
    }

    [Fact]
    public void Load_TrimsPointsPastPerStrokeCap()
    {
        var path = Path.Combine(_dir, "manystrokes.ebboard");
        var doc = new BoardDocument { PageCount = 1 };
        doc.EnsurePages();
        var stroke = new InkStroke();
        for (var i = 0; i < BoardDocument.MaxPointsPerStroke + 5000; i++)
        {
            stroke.Points.Add(new InkPoint { X = i, Y = i });
        }

        doc.Pages[0].Strokes.Add(stroke);
        BoardSerializer.Save(doc, path);

        var loaded = BoardSerializer.Load(path);

        Assert.Equal(BoardDocument.MaxPointsPerStroke, loaded.Pages[0].Strokes[0].Points.Count);
    }

    [Fact]
    public void Sanitize_EnforcesTotalBudgetsWithSmallLimits()
    {
        // Fast in-memory check of the board-wide trimming branches: production caps are
        // far too large to build in a unit test, so inject tiny ones.
        var limits = new BoardDocument.BoardLimits
        {
            MaxPointsPerStroke = 1000,
            MaxTotalPoints = 10,
            MaxStrokesPerPage = 1000,
            MaxTotalStrokes = 3,
            MaxImagesPerPage = 1000,
            MaxTotalImages = 2,
        };
        var doc = new BoardDocument { PageCount = 2 };
        doc.EnsurePages();
        foreach (var page in doc.Pages)
        {
            for (var s = 0; s < 2; s++)
            {
                var stroke = new InkStroke();
                for (var i = 0; i < 8; i++)
                {
                    stroke.Points.Add(new InkPoint { X = i, Y = i });
                }

                page.Strokes.Add(stroke);
            }

            page.Images.Add(new InkImage { SourcePath = "a.png" });
            page.Images.Add(new InkImage { SourcePath = "b.png" });
        }

        doc.Sanitize(limits);

        Assert.True(doc.Pages.Sum(p => p.Strokes.Count) <= 3);
        Assert.True(doc.Pages.Sum(p => p.Strokes.Sum(s => s.Points.Count)) <= 10);
        Assert.True(doc.Pages.Sum(p => p.Images.Count) + doc.Images.Count <= 2);
    }

    [Fact]
    public void Load_EnforcesTotalPointBudgetAcrossPages()
    {
        var path = Path.Combine(_dir, "pointbudget.ebboard");
        var doc = new BoardDocument { PageCount = 2 };
        doc.EnsurePages();
        foreach (var page in doc.Pages)
        {
            var stroke = new InkStroke();
            // Each page alone stays under the per-stroke cap; together they must hit the
            // board-wide budget.
            for (var i = 0; i < BoardDocument.MaxPointsPerStroke; i++)
            {
                stroke.Points.Add(new InkPoint { X = i, Y = i });
            }

            page.Strokes.Add(stroke);
        }

        BoardSerializer.Save(doc, path);
        var loaded = BoardSerializer.Load(path);

        var total = loaded.Pages.Sum(p => p.Strokes.Sum(s => s.Points.Count));
        Assert.True(total <= BoardDocument.MaxTotalPoints);
        Assert.True(total > 0);
    }

    [Fact]
    public void Load_DropsImagesPastCaps()
    {
        var path = Path.Combine(_dir, "manyimages.ebboard");
        var doc = new BoardDocument { PageCount = 1 };
        doc.EnsurePages();
        for (var i = 0; i < BoardDocument.MaxImagesPerPage + 50; i++)
        {
            doc.Pages[0].Images.Add(new InkImage { SourcePath = $"img{i}.png" });
        }

        BoardSerializer.Save(doc, path);
        var loaded = BoardSerializer.Load(path);

        Assert.Equal(BoardDocument.MaxImagesPerPage, loaded.Pages[0].Images.Count);
    }

    [Fact]
    public void Load_ClampsAbsurdCanvasAndTrimsPageName()
    {
        var path = Path.Combine(_dir, "canvas.ebboard");
        File.WriteAllText(path,
            "{\"PageCount\":1,\"CanvasWidth\":1e15,\"CanvasHeight\":-7," +
            "\"Pages\":[{\"Index\":0,\"Name\":\"" + new string('n', 3000) + "\"}]}");

        var doc = BoardSerializer.Load(path);

        Assert.Equal(BoardDocument.MaxCanvasDimension, doc.CanvasWidth);
        Assert.Equal(1080, doc.CanvasHeight);
        Assert.Equal(BoardDocument.MaxPageNameChars, doc.Pages[0].Name.Length);
    }

    [Fact]
    public void BoardDocument_Clone_IsDeepAndIndependent()
    {
        var doc = new BoardDocument { Name = "live", PageCount = 2 };
        doc.EnsurePages();
        doc.Pages[0].Strokes.Add(new InkStroke
        {
            Points = { new InkPoint { X = 1, Y = 2 } },
        });

        var snapshot = doc.Clone();
        doc.Pages[0].Strokes[0].Points[0].X = 999;
        doc.Pages[0].Strokes.Clear();
        doc.Name = "changed";

        Assert.Equal("live", snapshot.Name);
        Assert.Equal(1, snapshot.Pages[0].Strokes[0].Points[0].X);
        Assert.Single(snapshot.Pages[0].Strokes);
    }
}
