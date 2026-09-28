using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using zEClass.Core;
using Microsoft.Win32;

namespace zEClass;

/// <summary>
/// Export options: which pages, which format, at what resolution. Kept in a plain class so the
/// dialog is a thin shell over it and the choices are testable.
/// </summary>
public sealed class ExportOptions
{
    public bool AllPages { get; set; } = true;

    public List<int> PageIndices { get; set; } = new();

    public ExportFormat Format { get; set; } = ExportFormat.Pdf;

    /// <summary>Output pixels per inch. 150 is a sensible compromise for classroom projection.</summary>
    public int Dpi { get; set; } = 150;

    public string? OutputPath { get; set; }

    public IEnumerable<int> ResolvePages(BoardDocument document)
    {
        var indices = AllPages
            ? document.Pages.Select(p => p.Index).OrderBy(i => i)
            : PageIndices.OrderBy(i => i);
        return indices.Where(i => document.Pages.Any(p => p.Index == i));
    }
}

public enum ExportFormat
{
    Pdf = 0,
    PngPerPage = 1,
    zEClassFile = 2,
}

public static class ExportDialog
{
    public static void Show(Window owner, BoardDocument document, InkSurface surface)
    {
        var options = new ExportOptions();
        var dialog = new SaveFileDialog
        {
            Filter = "PDF document (*.pdf)|*.pdf|PNG image (*.png)|*.png|zEClass board (*.ebboard)|*.ebboard",
            AddExtension = true,
            DefaultExt = ".pdf",
            FileName = Sanitize(document.Name) + ".pdf",
        };

        if (dialog.ShowDialog(owner) != true)
        {
            return;
        }

        options.OutputPath = dialog.FileName;
        options.Format = Path.GetExtension(dialog.FileName).ToLowerInvariant() switch
        {
            ".png" => ExportFormat.PngPerPage,
            ".ebboard" => ExportFormat.zEClassFile,
            _ => ExportFormat.Pdf,
        };

        var pages = options.ResolvePages(document).ToList();
        try
        {
            switch (options.Format)
            {
                case ExportFormat.Pdf:
                    BoardRenderer.WritePdf(dialog.FileName, document, Select(document, pages),
                        surface.CanvasWidth, surface.CanvasHeight);
                    break;

                case ExportFormat.PngPerPage:
                    WritePng(document, Select(document, pages), surface, Path.GetDirectoryName(
                        dialog.FileName) ?? ".", Path.GetFileNameWithoutExtension(dialog.FileName));
                    break;

                case ExportFormat.zEClassFile:
                    BoardSerializer.Save(document, dialog.FileName);
                    break;
            }

            MessageBox.Show(owner, $"Exported to:\n{dialog.FileName}", "zEClass",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or NotSupportedException or ArgumentException)
        {
            CrashLog.Write("Export", ex);
            MessageBox.Show(owner, $"Export failed.\n\n{ex.Message}", "zEClass",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static List<BoardPage> Select(BoardDocument document, IEnumerable<int> indices) =>
        indices.Select(i => document.Pages.FirstOrDefault(p => p.Index == i))
            .Where(p => p is not null)
            .Select(p => p!)
            .ToList();

    private static void WritePng(BoardDocument document, List<BoardPage> pages, InkSurface surface,
        string directory, string baseName)
    {
        Directory.CreateDirectory(directory);
        var savedActive = document.ActivePage;
        try
        {
            foreach (var page in pages)
            {
                document.ActivePage = page.Index;
                var bitmap = BoardRenderer.RenderPageToBitmap(document, surface.CanvasWidth,
                    surface.CanvasHeight);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                var path = Path.Combine(directory, $"{baseName}-page{page.Index + 1}.png");
                using var fs = File.Create(path);
                encoder.Save(fs);
            }
        }
        finally
        {
            document.ActivePage = savedActive;
        }
    }

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "board" : cleaned;
    }
}
