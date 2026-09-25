using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EBoard.Core;
using Xunit;

namespace EBoard.Tests;

public sealed class BoardSerializerTests : IDisposable
{
    private readonly string _dir;

    public BoardSerializerTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "eboard-tests-" + Guid.NewGuid().ToString("N"));
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
        var ok = BoardSerializer.TryLoad(Path.Combine(_dir, "missing.eboard"), out var doc, out var error);

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
}
