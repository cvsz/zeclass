using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using zEClass.Core;
using Microsoft.Win32;

namespace zEClass;

/// <summary>What an import produced.</summary>
public sealed class ImportResult
{
    public int PagesAdded { get; set; }

    public int ImagesAdded { get; set; }

    public List<string> Warnings { get; } = new();

    public string? Error { get; set; }

    public bool Succeeded => Error is null && ImagesAdded > 0;
}

/// <summary>
/// Brings external teaching material onto the board, covering the vendor manual's
/// "OPEN FILE: Open files, including video, word, ppt, PDF".
///
/// Strategy per format, chosen so nothing depends on software that a locked-down school image
/// will not have:
///  - images: placed directly,
///  - PDF: pages rendered by a minimal in-process reader,
///  - PPTX / DOCX: the embedded preview thumbnail if present, else the first slide image,
///    because both formats are zip containers and a thumbnail is what PowerPoint and Word
///    already publish inside them,
///  - anything else: reported as unsupported rather than silently ignored.
/// </summary>
public static class Importer
{
    private static readonly string[] ImageExtensions =
        [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp"];

    public static bool IsSupported(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ImageExtensions.Contains(ext) || ext is ".pdf" or ".pptx" or ".docx" or ".doc" or ".ppt";
    }

    /// <summary>Appends imported content as new pages, so existing work is never disturbed.</summary>
    public static ImportResult Import(BoardDocument document, InkSurface surface, string[] paths)
    {
        if (document is null || paths is null || paths.Length == 0)
        {
            return new ImportResult { Error = "Nothing to import." };
        }

        var result = new ImportResult();
        foreach (var path in paths)
        {
            try
            {
                ImportOne(document, surface, path, result);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                           or NotSupportedException or ArgumentException
                                           or InvalidDataException)
            {
                result.Warnings.Add($"{Path.GetFileName(path)}: {ex.Message}");
            }
        }

        return result;
    }

    private static void ImportOne(BoardDocument document, InkSurface surface, string path,
        ImportResult result)
    {
        if (!File.Exists(path))
        {
            result.Warnings.Add($"{Path.GetFileName(path)}: not found.");
            return;
        }

        var ext = Path.GetExtension(path).ToLowerInvariant();
        var page = new BoardPage
        {
            Index = document.PageCount,
            Name = Path.GetFileNameWithoutExtension(path),
        };

        // The page is only committed once content actually lands on it, so an unsupported or
        // unreadable file leaves a warning instead of a blank page at the end of the board.
        var imagesBefore = result.ImagesAdded;
        document.Pages.Add(page);
        document.PageCount++;
        result.PagesAdded++;

        switch (ext)
        {
            case ".pdf":
                ImportPdf(document, page, surface, path, result);
                break;

            case ".pptx" or ".docx" or ".ppt" or ".doc":
                ImportOffice(document, page, surface, path, result);
                break;

            default:
                if (ImageExtensions.Contains(ext))
                {
                    page.Images.Add(MakeImage(document, page.Index, path));
                    result.ImagesAdded++;
                }
                else
                {
                    result.Warnings.Add(
                        $"{Path.GetFileName(path)}: {ext} is not a supported import format.");
                }

                break;
        }

        if (result.ImagesAdded == imagesBefore)
        {
            document.Pages.Remove(page);
            document.PageCount = document.Pages.Count;
            result.PagesAdded--;
        }
    }

    private static InkImage MakeImage(BoardDocument document, int pageIndex, string path) => new()
    {
        PageIndex = pageIndex,
        X = 0,
        Y = 0,
        Width = document.CanvasWidth,
        Height = document.CanvasHeight,
        SourcePath = path,
    };

    private static void ImportPdf(BoardDocument document, BoardPage page, InkSurface surface,
        string path, ImportResult result)
    {
        var staged = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "zEClass", "import", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staged);

        var pages = PdfImporter.Render(path, (int)document.CanvasWidth, (int)document.CanvasHeight,
            staged, result);
        foreach (var rendered in pages)
        {
            page.Images.Add(MakeImage(document, page.Index, rendered));
            result.ImagesAdded++;
        }
    }

    private static void ImportOffice(BoardDocument document, BoardPage page, InkSurface surface,
        string path, ImportResult result)
    {
        var extracted = OfficePreviewExtractor.Extract(path);
        if (extracted is null)
        {
            result.Warnings.Add(
                $"{Path.GetFileName(path)}: no embedded preview found. Open the file, copy the " +
                "slide, and paste an image instead.");
            return;
        }

        page.Images.Add(MakeImage(document, page.Index, extracted));
        result.ImagesAdded++;
    }

    /// <summary>File dialog filter covering everything the importer accepts.</summary>
    public static string Filter =>
        "Teaching material|*.pdf;*.pptx;*.docx;*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|" +
        "PDF (*.pdf)|*.pdf|PowerPoint (*.pptx)|*.pptx|Word (*.docx)|*.docx|" +
        "Images (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|" +
        "All files (*.*)|*.*";

    public static OpenFileDialog CreateDialog() => new() { Filter = Filter, Multiselect = true };
}
