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
    public void EnsurePages_LeavesConsistentDocumentsAlone()
    {
        var doc = new BoardDocument { PageCount = 3, ActivePage = 2 };
        doc.EnsurePages();
        doc.Pages[1].Strokes.Add(new InkStroke());

        var same = doc.EnsurePages();

        Assert.Same(doc, same);
        Assert.Equal(3, doc.Pages.Count);
        Assert.Equal(2, doc.ActivePage);
        Assert.Single(doc.Pages[1].Strokes);
        Assert.Equal(new[] { 0, 1, 2 }, doc.Pages.Select(p => p.Index));
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
    public void Load_FutureFormatVersion_IsRejectedBeforeUse()
    {
        var path = Path.Combine(_dir, "future.ebboard");
        File.WriteAllText(path, "{\"Version\":99,\"PageCount\":1,\"Pages\":[]}");

        var ok = BoardSerializer.TryLoad(path, out var doc, out var error);

        Assert.False(ok);
        Assert.Null(doc);
        Assert.Contains("version", error, StringComparison.OrdinalIgnoreCase);
        Assert.Throws<InvalidDataException>(() => BoardSerializer.Load(path));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Load_NonPositiveFormatVersion_IsRejected(int version)
    {
        var path = Path.Combine(_dir, $"badver{version}.ebboard");
        File.WriteAllText(path, $"{{\"Version\":{version},\"PageCount\":1,\"Pages\":[]}}");

        var ok = BoardSerializer.TryLoad(path, out _, out var error);

        Assert.False(ok);
        Assert.Contains("version", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Load_FileWithoutVersionField_ReadsAsCurrentVersion()
    {
        // Files saved before versioning existed have no Version property; they must keep
        // loading as version 1 rather than being rejected as version 0.
        var path = Path.Combine(_dir, "legacy.ebboard");
        File.WriteAllText(path, "{\"PageCount\":2,\"Pages\":[]}");

        var doc = BoardSerializer.Load(path);

        Assert.Equal(BoardDocument.FileFormatVersion, doc.Version);
        Assert.Equal(2, doc.Pages.Count);
    }

    [Fact]
    public void Save_StampsAndRoundTripsCurrentFormatVersion()
    {
        var path = Path.Combine(_dir, "versioned.ebboard");
        BoardSerializer.Save(new BoardDocument { PageCount = 1 }, path);

        Assert.Contains("\"Version\":1", File.ReadAllText(path), StringComparison.Ordinal);
        Assert.Equal(BoardDocument.FileFormatVersion, BoardSerializer.Load(path).Version);
    }

    [Fact]
    public void Load_TruncatesOversizedTopLevelStringsAndDropsUnusablePaths()
    {
        var path = Path.Combine(_dir, "bigstrings.ebboard");
        var hugeName = new string('N', BoardDocument.MaxNameChars + 100);
        var hugeLang = new string('L', BoardDocument.MaxLanguageChars + 10);
        var hugeShape = new string('S', BoardDocument.MaxShapeNameChars + 10);
        var hugePath = new string('P', BoardDocument.MaxPathChars + 10);
        File.WriteAllText(path,
            "{\"PageCount\":1,\"Name\":\"" + hugeName + "\",\"Language\":\"" + hugeLang +
            "\",\"Images\":[{\"SourcePath\":\"" + hugePath + "\"}]," +
            "\"Pages\":[{\"Index\":0,\"BackgroundImage\":\"" + hugePath +
            "\",\"Strokes\":[{\"Shape\":\"" + hugeShape + "\"}]}]}");

        var doc = BoardSerializer.Load(path);

        Assert.Equal(BoardDocument.MaxNameChars, doc.Name.Length);
        Assert.Equal("en", doc.Language);
        Assert.Equal(string.Empty, Assert.Single(doc.Images).SourcePath);
        var page = Assert.Single(doc.Pages);
        Assert.Null(page.BackgroundImage);
        Assert.Equal(BoardDocument.MaxShapeNameChars, page.Strokes[0].Shape.Length);
    }

    [Fact]
    public void Sanitize_KeepsLegitimatePathsAndNamesUntouched()
    {
        var doc = new BoardDocument
        {
            Name = "Class 4A — หน่วยที่ 3",
            Language = "zh-Hans",
            PageCount = 1,
        };
        doc.Pages.Add(new BoardPage
        {
            Index = 0,
            BackgroundImage = @"C:\lessons\day 1\พื้นหลัง.png",
        });

        doc.Sanitize();

        Assert.Equal("Class 4A — หน่วยที่ 3", doc.Name);
        Assert.Equal("zh-Hans", doc.Language);
        Assert.Equal(@"C:\lessons\day 1\พื้นหลัง.png", doc.Pages[0].BackgroundImage);
    }

    // ---- §6 crash-safe persistence ------------------------------------------------

    [Fact]
    public void Save_KeepsPreviousBoardAsBackup()
    {
        var path = Path.Combine(_dir, "backup.ebboard");
        BoardSerializer.Save(new BoardDocument { Name = "first" }, path);
        BoardSerializer.Save(new BoardDocument { Name = "second" }, path);

        Assert.Equal("second", BoardSerializer.Load(path).Name);
        Assert.True(File.Exists(path + ".bak"));
        Assert.Equal("first", BoardSerializer.Load(path + ".bak").Name);
    }

    [Fact]
    public void Save_TargetHeldByAnotherProcess_FailsWithoutTouchingTheOldBoard()
    {
        var path = Path.Combine(_dir, "locked.ebboard");
        BoardSerializer.Save(new BoardDocument { Name = "original" }, path);

        using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.ThrowsAny<IOException>(() =>
                BoardSerializer.Save(new BoardDocument { Name = "replacement" }, path));
        }

        Assert.Equal("original", BoardSerializer.Load(path).Name);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void Save_MissingParentDirectory_FailsCleanly()
    {
        var path = Path.Combine(_dir, "no-such-dir", "board.ebboard");

        Assert.Throws<DirectoryNotFoundException>(() =>
            BoardSerializer.Save(new BoardDocument(), path));

        Assert.False(File.Exists(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void SaveAndLoad_UnicodePathRoundTrips()
    {
        var dir = Path.Combine(_dir, "บทที่ 1 — วิทยาศาสตร์");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "กระดาน ทดสอบ.ebboard");
        BoardSerializer.Save(new BoardDocument { Name = "หน่วยที่ 3" }, path);

        Assert.Equal("หน่วยที่ 3", BoardSerializer.Load(path).Name);
    }

    [Fact]
    public void SaveAndLoad_PathBeyondLegacy260LimitRoundTrips()
    {
        // .NET Core addresses long paths directly; a classroom board lives under deep
        // network shares, so the save path must not assume MAX_PATH.
        var dir = _dir;
        for (var i = 0; i < 16; i++)
        {
            dir = Path.Combine(dir, "deep-segment-" + i.ToString("D2") + "-padding");
        }

        Directory.CreateDirectory(dir);
        Assert.True(Path.Combine(dir, "deep.ebboard").Length > 260);
        var path = Path.Combine(dir, "deep.ebboard");
        BoardSerializer.Save(new BoardDocument { Name = "deep" }, path);

        Assert.Equal("deep", BoardSerializer.Load(path).Name);
    }

    [Fact]
    public void RecoverStaleTempFiles_PromotesValidTempWhenPrimaryIsMissing()
    {
        // Crash happened between the durable write and the rename: the temp is the
        // newest complete board and must become the primary.
        var primary = Path.Combine(_dir, "crashed.ebboard");
        BoardSerializer.Save(new BoardDocument { Name = "unwritten" }, primary + ".staging");
        File.Move(primary + ".staging", primary + ".tmp");
        File.Delete(primary);

        BoardSerializer.RecoverStaleTempFiles(_dir);

        Assert.True(File.Exists(primary));
        Assert.False(File.Exists(primary + ".tmp"));
        Assert.Equal("unwritten", BoardSerializer.Load(primary).Name);
    }

    [Fact]
    public void RecoverStaleTempFiles_MovesCorruptTempAsideWithoutDeletingIt()
    {
        var primary = Path.Combine(_dir, "corrupttmp.ebboard");
        File.WriteAllText(primary + ".tmp", "{ not json");

        BoardSerializer.RecoverStaleTempFiles(_dir);

        Assert.False(File.Exists(primary));
        Assert.False(File.Exists(primary + ".tmp"));
        Assert.True(File.Exists(primary + ".tmp.corrupt"));
    }

    [Fact]
    public void RecoverStaleTempFiles_DeletesTempBesideHealthyPrimary()
    {
        var primary = Path.Combine(_dir, "healthy.ebboard");
        BoardSerializer.Save(new BoardDocument { Name = "keep" }, primary);
        File.WriteAllText(primary + ".tmp", "{\"PageCount\":1}");

        BoardSerializer.RecoverStaleTempFiles(_dir);

        Assert.Equal("keep", BoardSerializer.Load(primary).Name);
        Assert.False(File.Exists(primary + ".tmp"));
    }

    [Fact]
    public void RecoverStaleTempFiles_MissingOrEmptyDirectoryIsANoOp()
    {
        BoardSerializer.RecoverStaleTempFiles(Path.Combine(_dir, "not-created"));
        BoardSerializer.RecoverStaleTempFiles(string.Empty);
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
