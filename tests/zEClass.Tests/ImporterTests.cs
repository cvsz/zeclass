using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using zEClass.Core;
using Xunit;

namespace zEClass.Tests;

public sealed class ImporterTests : IDisposable
{
    private readonly string _dir;

    public ImporterTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "zEClass-import-" + Guid.NewGuid().ToString("N"));
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

    private static byte[] PngBytes()
    {
        // A 1x1 PNG, enough for signature checks and board-file round trips.
        return Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
    }

    [Fact]
    public void IsSupported_AcceptsTeachingFormats()
    {
        foreach (var ext in new[] { ".pdf", ".pptx", ".docx", ".png", ".jpg", ".bmp" })
        {
            Assert.True(Importer.IsSupported("lesson" + ext), ext);
        }
    }

    [Fact]
    public void IsSupported_RejectsUnknownFormats()
    {
        Assert.False(Importer.IsSupported("lesson.zip"));
        Assert.False(Importer.IsSupported("lesson.exe"));
    }

    [Fact]
    public void DetectImage_RecognizesCommonSignatures()
    {
        Assert.Equal(".jpg", PdfImporter.DetectImage([0xFF, 0xD8, 0xFF, 0xE0, 0, 0]));
        Assert.Equal(".png", PdfImporter.DetectImage(PngBytes()));
        Assert.Equal(".gif", PdfImporter.DetectImage(Encoding_Latin1("GIF89a")));
        Assert.Equal(".bmp", PdfImporter.DetectImage(Encoding_Latin1("BM......")));
    }

    [Fact]
    public void DetectImage_RejectsNonImageData()
    {
        Assert.Null(PdfImporter.DetectImage(Encoding_Latin1("%PDF-1.7\n1 0 obj")));
        Assert.Null(PdfImporter.DetectImage([]));
    }

    [Fact]
    public void Import_AddsAPagePerFile()
    {
        var doc = new BoardDocument { PageCount = 1 };
        doc.EnsurePages();
        var a = Path.Combine(_dir, "one.png");
        var b = Path.Combine(_dir, "two.png");
        File.WriteAllBytes(a, PngBytes());
        File.WriteAllBytes(b, PngBytes());

        var result = Importer.Import(doc, null!, [a, b]);

        Assert.True(result.Succeeded, string.Join("; ", result.Warnings));
        Assert.Equal(2, result.PagesAdded);
        Assert.Equal(2, result.ImagesAdded);
        Assert.Equal(3, doc.PageCount);
    }

    [Fact]
    public void Import_NeverDisturbsExistingPages()
    {
        var doc = new BoardDocument { PageCount = 1 };
        doc.EnsurePages();
        doc.Pages[0].Strokes.Add(new InkStroke
        {
            Points = { new InkPoint { X = 1, Y = 2 } },
        });

        var file = Path.Combine(_dir, "slide.png");
        File.WriteAllBytes(file, PngBytes());
        Importer.Import(doc, null!, [file]);

        Assert.Single(doc.Pages[0].Strokes);
        Assert.Equal(2, doc.Pages.Count);
    }

    [Fact]
    public void Import_ReportsMissingFileAsWarning()
    {
        var doc = new BoardDocument { PageCount = 1 };
        doc.EnsurePages();

        var result = Importer.Import(doc, null!, [Path.Combine(_dir, "nope.png")]);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Warnings, w => w.Contains("not found"));
    }

    [Fact]
    public void Import_ReportsUnsupportedFormatRatherThanFailing()
    {
        var doc = new BoardDocument { PageCount = 1 };
        doc.EnsurePages();
        var file = Path.Combine(_dir, "archive.zip");
        File.WriteAllBytes(file, [0x50, 0x4B, 0x03, 0x04]);

        var result = Importer.Import(doc, null!, [file]);

        Assert.Contains(result.Warnings, w => w.Contains("not a supported import format"));
    }

    [Fact]
    public void Import_VectorOnlyPdfSaysSoClearly()
    {
        var doc = new BoardDocument { PageCount = 1 };
        doc.EnsurePages();
        var pdf = Path.Combine(_dir, "text-only.pdf");

        // A structurally valid PDF whose only content stream is uncompressed text.
        var body = "%PDF-1.4\n1 0 obj\n<< /Type /Catalog >>\nendobj\n" +
                   "2 0 obj\n<< /Type /Page /Count 1 >>\nstream\nBT /F1 12 Tf (Hello) Tj ET\n" +
                   "endstream\nendobj\n%%EOF\n";
        File.WriteAllText(pdf, body);

        var result = Importer.Import(doc, null!, [pdf]);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Warnings, w => w.Contains("vector"));
    }

    [Fact]
    public void Import_ImageLandsOnItsOwnPageAtCanvasSize()
    {
        var doc = new BoardDocument { PageCount = 1, CanvasWidth = 1600, CanvasHeight = 900 };
        doc.EnsurePages();
        var file = Path.Combine(_dir, "bg.png");
        File.WriteAllBytes(file, PngBytes());

        Importer.Import(doc, null!, [file]);

        var added = doc.Pages.Single(p => p.Index == 1);
        var image = Assert.Single(added.Images);
        Assert.Equal(1600, image.Width);
        Assert.Equal(900, image.Height);
        Assert.Equal(1, image.PageIndex);
    }

    [Fact]
    public void OfficePreview_NonContainerReturnsNull()
    {
        var file = Path.Combine(_dir, "plain.docx");
        File.WriteAllText(file, "not a zip");

        Assert.Null(OfficePreviewExtractor.Extract(file));
    }

    [Fact]
    public void OfficePreview_TitleFallsBackToFileName()
    {
        var file = Path.Combine(_dir, "week-3.docx");
        File.WriteAllText(file, "not a zip");

        Assert.Equal("week-3", OfficePreviewExtractor.TitleOf(file));
    }

    [Fact]
    public void TryReadPageCount_HandlesGarbage()
    {
        var file = Path.Combine(_dir, "junk.pdf");
        File.WriteAllText(file, "not a pdf at all");

        Assert.Null(PdfImporter.TryReadPageCount(file));
    }

    [Fact]
    public void Render_RefusesOversizedFileBeforeReading()
    {
        var file = Path.Combine(_dir, "huge.pdf");
        File.WriteAllBytes(file, new byte[3000]);
        var limits = new PdfImporter.PdfLimits { MaxPdfFileBytes = 1024 };

        var result = new ImportResult();
        var written = PdfImporter.Render(
            file, 1920, 1080, Path.Combine(_dir, "stage"), result, limits);

        Assert.Empty(written);
        Assert.Contains(result.Warnings, w => w.Contains("import limit"));
    }

    [Fact]
    public void Render_RejectsCompressedBombWithinBudget()
    {
        var file = Path.Combine(_dir, "bomb.pdf");
        File.WriteAllBytes(file, PdfWithStreams(ZlibBomb(100_000)));
        var limits = new PdfImporter.PdfLimits { MaxInflatedStreamBytes = 4096 };

        var result = new ImportResult();
        var written = PdfImporter.Render(
            file, 1920, 1080, Path.Combine(_dir, "stage"), result, limits);

        Assert.Empty(written);
        Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public void Render_SkipsOversizedStreamKeepsOtherImages()
    {
        var file = Path.Combine(_dir, "mixed.pdf");
        File.WriteAllBytes(file, PdfWithStreams(new byte[5000], PngBytes()));
        var limits = new PdfImporter.PdfLimits { MaxCompressedStreamBytes = 100 };

        var result = new ImportResult();
        var stage = Path.Combine(_dir, "stage");
        var written = PdfImporter.Render(file, 1920, 1080, stage, result, limits);

        var single = Assert.Single(written);
        Assert.Equal(PngBytes(), File.ReadAllBytes(single));
        Assert.Contains(result.Warnings, w => w.Contains("oversized"));
    }

    [Fact]
    public void Render_MalformedStreamWithoutEndDoesNotThrow()
    {
        var file = Path.Combine(_dir, "malformed.pdf");
        File.WriteAllText(file, "%PDF-1.4\n1 0 obj\n<< >>\nstream\nABCDEF");

        var result = new ImportResult();
        var written = PdfImporter.Render(
            file, 1920, 1080, Path.Combine(_dir, "stage"), result, new PdfImporter.PdfLimits());

        Assert.Empty(written);
        Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public void Render_TruncatedFileDoesNotThrow()
    {
        var file = Path.Combine(_dir, "truncated.pdf");
        File.WriteAllText(file, "%PDF-1.4\n1 0 obj\n<< /Length 100 >>\nstream\nAB");

        var result = new ImportResult();
        var written = PdfImporter.Render(
            file, 1920, 1080, Path.Combine(_dir, "stage"), result, new PdfImporter.PdfLimits());

        Assert.Empty(written);
        Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public void Render_InvalidFilterDataIsTreatedAsRaw()
    {
        var file = Path.Combine(_dir, "garbage.pdf");
        File.WriteAllBytes(file, PdfWithStreams(
            System.Text.Encoding.Latin1.GetBytes("not compressed at all, just text")));

        var result = new ImportResult();
        var written = PdfImporter.Render(
            file, 1920, 1080, Path.Combine(_dir, "stage"), result, new PdfImporter.PdfLimits());

        Assert.Empty(written);
        Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public void Render_ExtractsEmbeddedPng()
    {
        var file = Path.Combine(_dir, "photo.pdf");
        File.WriteAllBytes(file, PdfWithStreams(PngBytes()));

        var result = new ImportResult();
        var stage = Path.Combine(_dir, "stage");
        var written = PdfImporter.Render(
            file, 1920, 1080, stage, result, new PdfImporter.PdfLimits());

        var single = Assert.Single(written);
        Assert.Equal(PngBytes(), File.ReadAllBytes(single));
    }

    [Fact]
    public void Render_TotalInflatedBudgetStopsRunaway()
    {
        var file = Path.Combine(_dir, "many.pdf");
        File.WriteAllBytes(file, PdfWithStreams(
            new byte[1024], new byte[1024], new byte[1024], new byte[1024], new byte[1024]));
        var limits = new PdfImporter.PdfLimits { MaxTotalInflatedBytes = 2048 };

        var result = new ImportResult();
        var written = PdfImporter.Render(
            file, 1920, 1080, Path.Combine(_dir, "stage"), result, limits);

        Assert.Empty(written);
        Assert.Contains(result.Warnings, w => w.Contains("budget"));
    }

    [Fact]
    public void TryReadPageCount_CapsHugeDeclaredCount()
    {
        var file = Path.Combine(_dir, "huge-count.pdf");
        File.WriteAllText(file, "<< /Type /Pages /Count 999999999 >>");

        Assert.Equal(10, PdfImporter.TryReadPageCount(
            file, new PdfImporter.PdfLimits { MaxPages = 10 }));
    }

    [Fact]
    public void TryReadPageCount_CountsMultiplePages()
    {
        var file = Path.Combine(_dir, "three.pdf");
        File.WriteAllText(file,
            "<< /Type /Pages /Count 3 >>\n" +
            "<< /Type /Page >>\n<< /Type /Page >>\n<< /Type /Page >>\n");

        Assert.Equal(3, PdfImporter.TryReadPageCount(file));
    }

    private static byte[] PdfWithStreams(params byte[][] payloads)
    {
        using var ms = new MemoryStream();
        void Write(string s)
        {
            var bytes = System.Text.Encoding.Latin1.GetBytes(s);
            ms.Write(bytes, 0, bytes.Length);
        }

        Write("%PDF-1.4\n");
        var i = 0;
        foreach (var payload in payloads)
        {
            i++;
            Write($"{i} 0 obj\n<< /Length {payload.Length} >>\nstream\n");
            ms.Write(payload, 0, payload.Length);
            Write("\nendstream\nendobj\n");
        }

        Write("%%EOF\n");
        return ms.ToArray();
    }

    private static byte[] ZlibBomb(int expandedBytes)
    {
        using var ms = new MemoryStream();
        ms.Write([0x78, 0x9C], 0, 2);
        using (var deflate = new DeflateStream(
                   ms, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            deflate.Write(new byte[expandedBytes], 0, expandedBytes);
        }

        return ms.ToArray();
    }

    [Fact]
    public void Extract_RefusesOversizedArchiveBeforeReading()
    {
        var file = BuildZip("huge.pptx", ("ppt/media/a.png", PngBytes()));
        var limits = new OfficePreviewExtractor.ZipLimits { MaxArchiveBytes = 16 };

        Assert.Null(OfficePreviewExtractor.Extract(file, limits));
    }

    [Fact]
    public void Extract_RejectsTooManyEntries()
    {
        var file = BuildZip("many.pptx",
            ("a.txt", [1]), ("b.txt", [2]), ("c.txt", [3]), ("d.txt", [4]), ("e.txt", [5]));
        var limits = new OfficePreviewExtractor.ZipLimits { MaxEntryCount = 2 };

        Assert.Null(OfficePreviewExtractor.Extract(file, limits));
    }

    [Fact]
    public void Extract_RejectsBombByCompressionRatio()
    {
        var file = BuildZip("bomb.pptx", ("ppt/media/bomb.bin", new byte[1_000_000]));

        Assert.Null(OfficePreviewExtractor.Extract(
            file, new OfficePreviewExtractor.ZipLimits()));
    }

    [Fact]
    public void Extract_RejectsEntryOverUncompressedCap()
    {
        var file = BuildZip("mid.pptx", ("ppt/media/mid.bin", new byte[10_240]));
        var limits = new OfficePreviewExtractor.ZipLimits
        {
            MaxUncompressedEntryBytes = 1024,
            MaxCompressionRatio = 1_000_000,
        };

        Assert.Null(OfficePreviewExtractor.Extract(file, limits));
    }

    [Fact]
    public void Extract_SkipsTraversalEntryFindsValid()
    {
        var file = BuildZip("mixed.pptx",
            ("ppt/media/../../evil.png", PngBytes()),
            ("ppt/media/ok.png", PngBytes()));

        var extracted = OfficePreviewExtractor.Extract(
            file, new OfficePreviewExtractor.ZipLimits());

        Assert.NotNull(extracted);
        Assert.Equal(PngBytes(), File.ReadAllBytes(extracted));
    }

    [Fact]
    public void Extract_TraversalOnlyReturnsNull()
    {
        var file = BuildZip("evil.pptx", ("ppt/media/../../evil.png", PngBytes()));

        Assert.Null(OfficePreviewExtractor.Extract(
            file, new OfficePreviewExtractor.ZipLimits()));
    }

    [Fact]
    public void Extract_ReturnsPreviewForMinimalPptx()
    {
        var file = BuildZip("deck.pptx", ("ppt/media/slide1.png", PngBytes()));

        var extracted = OfficePreviewExtractor.Extract(
            file, new OfficePreviewExtractor.ZipLimits());

        Assert.NotNull(extracted);
        Assert.True(File.Exists(extracted));
        Assert.Equal(PngBytes(), File.ReadAllBytes(extracted));
    }

    [Fact]
    public void TitleOf_FallsBackWhenCoreXmlOversized()
    {
        var core = System.Text.Encoding.UTF8.GetBytes(
            "<cp:coreProperties><dc:title>Week 3</dc:title>" +
            new string('x', 5000) + "</cp:coreProperties>");
        var file = BuildZip("big.docx", ("docProps/core.xml", core));
        var limits = new OfficePreviewExtractor.ZipLimits { MaxTitleXmlBytes = 16 };

        Assert.Equal("big", OfficePreviewExtractor.TitleOf(file, limits));
    }

    [Fact]
    public void TitleOf_ReadsTitleWithinBudget()
    {
        var core = System.Text.Encoding.UTF8.GetBytes(
            "<cp:coreProperties><dc:title>Week 3</dc:title></cp:coreProperties>");
        var file = BuildZip("week-3.docx", ("docProps/core.xml", core));

        Assert.Equal("Week 3", OfficePreviewExtractor.TitleOf(
            file, new OfficePreviewExtractor.ZipLimits()));
    }

    [Fact]
    public void IsSafeEntryName_RejectsHostileNames()
    {
        Assert.False(OfficePreviewExtractor.IsSafeEntryName(string.Empty));
        Assert.False(OfficePreviewExtractor.IsSafeEntryName(".."));
        Assert.False(OfficePreviewExtractor.IsSafeEntryName("ppt/media/../../evil.png"));
        Assert.False(OfficePreviewExtractor.IsSafeEntryName("/absolute.png"));
        Assert.False(OfficePreviewExtractor.IsSafeEntryName("C:/drive.png"));
        Assert.False(OfficePreviewExtractor.IsSafeEntryName("ppt:media:back.png"));
        Assert.True(OfficePreviewExtractor.IsSafeEntryName("ppt/media/ok.png"));
        Assert.True(OfficePreviewExtractor.IsSafeEntryName("docProps/thumbnail.jpeg"));
    }

    [Fact]
    public void Extract_EmfPayload_IsSavedAsEmfByContent()
    {
        // AGENTS §12 fixture: EMF media. The header is an EMF record type 1
        // with a plausible size; the extension must come from the bytes.
        var emf = new byte[96];
        emf[0] = 0x01;
        var file = BuildZip("art.pptx", ("ppt/media/art.emf", emf));

        var extracted = OfficePreviewExtractor.Extract(
            file, new OfficePreviewExtractor.ZipLimits());

        Assert.NotNull(extracted);
        Assert.Equal(".emf", Path.GetExtension(extracted));
        Assert.Equal(emf, File.ReadAllBytes(extracted));
    }

    [Fact]
    public void Extract_WmfPayload_IsSavedAsWmfByContent()
    {
        // AGENTS §12 fixture: WMF media, identified by the placeable signature.
        var wmf = new byte[] { 0xD7, 0xCD, 0xC6, 0x9A, 0, 0, 0, 0 };
        var file = BuildZip("chart.pptx", ("ppt/media/chart.wmf", wmf));

        var extracted = OfficePreviewExtractor.Extract(
            file, new OfficePreviewExtractor.ZipLimits());

        Assert.NotNull(extracted);
        Assert.Equal(".wmf", Path.GetExtension(extracted));
        Assert.Equal(wmf, File.ReadAllBytes(extracted));
    }

    [Fact]
    public void Extract_PngPayloadInEmfEntry_IsNeverSavedAsEmf()
    {
        // AGENTS §12: "Do not save a PNG/JPEG payload as .emf" — content beats name.
        var file = BuildZip("fake.pptx", ("ppt/media/fake.emf", PngBytes()));

        var extracted = OfficePreviewExtractor.Extract(
            file, new OfficePreviewExtractor.ZipLimits());

        Assert.NotNull(extracted);
        Assert.Equal(".png", Path.GetExtension(extracted));
        Assert.Equal(PngBytes(), File.ReadAllBytes(extracted));
    }

    [Fact]
    public void Extract_JpegPayloadInPngEntry_IsSavedAsJpg()
    {
        // The other direction of the same rule: JPEG bytes keep their real type.
        var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0 };
        var file = BuildZip("photo.pptx", ("ppt/media/photo.png", jpeg));

        var extracted = OfficePreviewExtractor.Extract(
            file, new OfficePreviewExtractor.ZipLimits());

        Assert.NotNull(extracted);
        Assert.Equal(".jpg", Path.GetExtension(extracted));
        Assert.Equal(jpeg, File.ReadAllBytes(extracted));
    }

    [Fact]
    public void Extract_DeckWithoutMedia_ReturnsNull()
    {
        // AGENTS §12 fixture: "no preview" — a valid package with nothing to show.
        var file = BuildZip("bare.pptx", ("ppt/presentation.xml", Encoding_Latin1("<p:presentation/>")));

        Assert.Null(OfficePreviewExtractor.Extract(
            file, new OfficePreviewExtractor.ZipLimits()));
    }

    [Fact]
    public void Extract_MalformedZip_ReturnsNull()
    {
        // AGENTS §12 fixture: malformed ZIP — a zip signature with no archive behind it.
        var file = Path.Combine(_dir, "bad.pptx");
        File.WriteAllBytes(file, [0x50, 0x4B, 0x03, 0x04]);

        Assert.Null(OfficePreviewExtractor.Extract(
            file, new OfficePreviewExtractor.ZipLimits()));
    }

    [Fact]
    public void Extract_WordMedia_IsExtractedFromDocx()
    {
        // AGENTS §12 fixture: valid DOCX with media.
        var file = BuildZip("spec.docx", ("word/media/diagram.png", PngBytes()));

        var extracted = OfficePreviewExtractor.Extract(
            file, new OfficePreviewExtractor.ZipLimits());

        Assert.NotNull(extracted);
        Assert.Equal(PngBytes(), File.ReadAllBytes(extracted));
    }

    [Fact]
    public void MacroEnabledOpenXml_IsSupportedAndExtractable()
    {
        // AGENTS §12: DOCM/PPTM must be explicitly supported (or rejected) — here supported.
        Assert.True(Importer.IsSupported("lesson.docm"));
        Assert.True(Importer.IsSupported("lesson.pptm"));

        var file = BuildZip("macro.docm", ("word/media/img.png", PngBytes()));
        Assert.NotNull(OfficePreviewExtractor.Extract(
            file, new OfficePreviewExtractor.ZipLimits()));
    }

    /// <summary>AGENTS §25: explicit import-latency budget, no "performance
    /// tested" without numbers. The fixtures are tiny, so this guards against
    /// accidental superlinear work in the import paths, not absolute speed.</summary>
    [Fact]
    public void ImportLatency_MixedBatch_StaysWithinBudget()
    {
        var doc = new BoardDocument { PageCount = 1 };
        doc.EnsurePages();
        var files = Enumerable.Range(0, 4)
            .Select(i =>
            {
                var file = Path.Combine(_dir, $"slide-{i}.png");
                File.WriteAllBytes(file, PngBytes());
                return file;
            })
            .ToArray();
        var pptx = BuildZip("deck.pptx", ("ppt/media/slide1.png", PngBytes()));
        var pdf = Path.Combine(_dir, "text-only.pdf");
        File.WriteAllText(pdf,
            "%PDF-1.4\n1 0 obj\n<< /Type /Catalog >>\nendobj\n" +
            "2 0 obj\n<< /Type /Page /Count 1 >>\nstream\nBT /F1 12 Tf (Hello) Tj ET\nendstream\nendobj\n%%EOF\n");

        var sw = Stopwatch.StartNew();
        var images = Importer.Import(doc, null!, files);
        var preview = OfficePreviewExtractor.Extract(
            pptx, new OfficePreviewExtractor.ZipLimits());
        var parsed = Importer.Import(doc, null!, [pdf]);
        sw.Stop();

        Assert.True(images.Succeeded, string.Join("; ", images.Warnings));
        Assert.Equal(4, images.PagesAdded);
        Assert.NotNull(preview);
        Assert.Contains(parsed.Warnings, w => w.Contains("vector"));

        const long BudgetMs = 5000;
        Assert.True(sw.ElapsedMilliseconds < BudgetMs,
            $"mixed import batch took {sw.ElapsedMilliseconds} ms (budget {BudgetMs} ms)");
    }

    private string BuildZip(string name, params (string Entry, byte[] Content)[] entries)
    {
        var path = Path.Combine(_dir, name);
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            foreach (var (entry, content) in entries)
            {
                var created = archive.CreateEntry(entry, CompressionLevel.SmallestSize);
                using var stream = created.Open();
                stream.Write(content, 0, content.Length);
            }
        }

        return path;
    }

    private static byte[] Encoding_Latin1(string s) => System.Text.Encoding.Latin1.GetBytes(s);
}

/// <summary>
/// Checks the importer against the real vendor manual, which is a 31-page PDF with embedded
/// images.
///
/// The manual is the vendor's copyrighted document and is deliberately not committed, so this
/// test skips unless you point it at your own copy via the <c>ZECLASS_VENDOR_MANUAL</c>
/// environment variable. It skips loudly rather than passing quietly: a green result that means
/// "nothing was actually checked" is worse than an honest skip, because a broken PDF importer
/// would otherwise look fine in CI forever.
/// </summary>
public sealed class PdfImporterRealWorldTests
{
    private static string? LocateManual()
    {
        var fromEnv = Environment.GetEnvironmentVariable("ZECLASS_VENDOR_MANUAL");
        if (!string.IsNullOrWhiteSpace(fromEnv) && File.Exists(fromEnv))
        {
            return fromEnv;
        }

        // Also accept a copy placed beside the repository, so no absolute path is ever baked in.
        var local = Path.Combine(AppContext.BaseDirectory, "testdata", "vendor-manual.pdf");
        return File.Exists(local) ? local : null;
    }

    [SkippableFact]
    public void RealManualYieldsExtractableImages()
    {
        var manual = LocateManual();
        Skip.If(manual is null,
            "vendor manual not present. Set ZECLASS_VENDOR_MANUAL to a local copy of the " +
            "EClass user guide to exercise the PDF importer against a real document.");

        Assert.NotNull(PdfImporter.TryReadPageCount(manual));

        var dir = Path.Combine(Path.GetTempPath(), "zEClass-pdf-" + Guid.NewGuid().ToString("N"));
        try
        {
            var result = new ImportResult();
            var files = PdfImporter.Render(manual, 1920, 1080, dir, result);

            Assert.NotEmpty(files);
            Assert.All(files, f => Assert.True(File.Exists(f)));
            Assert.All(files, f => Assert.True(new FileInfo(f).Length > 0, f));
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
}
