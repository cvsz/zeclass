using System;
using System.IO;
using System.Linq;
using EBoard.Core;
using Xunit;

namespace EBoard.Tests;

public sealed class ImporterTests : IDisposable
{
    private readonly string _dir;

    public ImporterTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "eboard-import-" + Guid.NewGuid().ToString("N"));
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

    private static byte[] Encoding_Latin1(string s) => System.Text.Encoding.Latin1.GetBytes(s);
}

/// <summary>
/// Checks the importer against the real vendor manual, which is a 31-page PDF with embedded
/// images. Skipped when the file is absent so the suite still runs on a machine without it.
/// </summary>
public sealed class PdfImporterRealWorldTests
{
    private const string ManualPath = @"D:\eclass\EClass_ExtractedMSI\disk1\Newusersmanual.pdf";

    [Fact]
    public void RealManualYieldsExtractableImages()
    {
        if (!File.Exists(ManualPath))
        {
            return;
        }

        Assert.NotNull(PdfImporter.TryReadPageCount(ManualPath));

        var dir = Path.Combine(Path.GetTempPath(), "eboard-pdf-" + Guid.NewGuid().ToString("N"));
        try
        {
            var result = new ImportResult();
            var files = PdfImporter.Render(ManualPath, 1920, 1080, dir, result);

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
