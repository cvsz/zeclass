using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace EBoard;

/// <summary>
/// Minimal PDF page-image extractor for import.
///
/// Scope, stated plainly: this pulls **embedded raster images** out of a PDF and writes them as
/// page images. That covers the case that matters for teaching material, because decks exported
/// to PDF usually contain a full-page rendered slide. It is not a PDF renderer: a vector-only
/// export (a Word document saved as PDF, a diagram drawn in Illustrator) has no raster to
/// extract, and this reports that plainly instead of producing a blank page.
///
/// Everything is in-process and depends only on the framework, so it works on a school image
/// with no PDF software, no browser, and no print driver.
/// </summary>
public static class PdfImporter
{
    private const int MaxImagesPerDocument = 200;
    private const long MaxImageBytes = 64L * 1024 * 1024;

    /// <summary>
    /// Extracts page-sized images. Returns the paths written; failures are added to the
    /// caller's warning list rather than thrown.
    /// </summary>
    public static List<string> Render(string pdfPath, int pageWidth, int pageHeight,
        string stagingFolder, ImportResult result)
    {
        var written = new List<string>();
        try
        {
            var bytes = File.ReadAllBytes(pdfPath);
            var images = ExtractImages(bytes, result);
            if (images.Count == 0)
            {
                result.Warnings.Add(
                    $"{Path.GetFileName(pdfPath)}: no embedded image found. This PDF is drawn " +
                    "with vector text and shapes, which EBoard does not render. Export the " +
                    "slides as images and import those instead.");
                return written;
            }

            Directory.CreateDirectory(stagingFolder);
            var taken = 0;
            foreach (var image in images)
            {
                if (taken >= MaxImagesPerDocument || image.Value.Length > MaxImageBytes)
                {
                    break;
                }

                var target = Path.Combine(stagingFolder,
                    $"{Path.GetFileNameWithoutExtension(pdfPath)}-p{taken + 1}{image.Extension}");
                File.WriteAllBytes(target, image.Value);
                written.Add(target);
                taken++;
            }

            return written;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or ArgumentException)
        {
            result.Warnings.Add($"{Path.GetFileName(pdfPath)}: {ex.Message}");
            return written;
        }
    }

    /// <summary>
    /// Walks every stream in the file, inflates the FlateDecode ones, and keeps any that start
    /// with a JPEG or PNG signature. Walking raw bytes rather than parsing the object graph keeps
    /// this small and tolerant of the many small deviations real PDFs contain.
    /// </summary>
    private static List<(string Extension, byte[] Value)> ExtractImages(byte[] pdf,
        ImportResult result)
    {
        var found = new List<(string, byte[])>();
        var latin = Encoding.Latin1.GetString(pdf);
        var index = 0;

        while (index < latin.Length && found.Count < MaxImagesPerDocument)
        {
            var streamAt = latin.IndexOf("stream", index, StringComparison.Ordinal);
            if (streamAt < 0)
            {
                break;
            }

            // Skip the EOL after the keyword.
            var dataStart = streamAt + "stream".Length;
            if (dataStart < latin.Length && latin[dataStart] == '\r')
            {
                dataStart++;
            }

            if (dataStart < latin.Length && latin[dataStart] == '\n')
            {
                dataStart++;
            }

            var dataEnd = latin.IndexOf("endstream", dataStart, StringComparison.Ordinal);
            if (dataEnd < 0)
            {
                break;
            }

            var length = dataEnd - dataStart;
            if (length > 0)
            {
                var slice = new byte[length];
                Array.Copy(pdf, dataStart, slice, 0, length);
                var payload = TryInflate(slice);
                if (payload.Length == 0)
                {
                    index = dataEnd + "endstream".Length;
                    continue;
                }

                var ext = DetectImage(payload);
                if (ext is not null)
                {
                    found.Add((ext, payload));
                }
            }

            index = dataEnd + "endstream".Length;
        }

        if (found.Count == 0)
        {
            result.Warnings.Add($"no embedded image streams found");
        }

        return found;
    }

    private static byte[] TryInflate(byte[] data)
    {
        if (data.Length >= 2 && data[0] == 0x78)
        {
            // Looks like a zlib header; try raw deflate after the header bytes.
            foreach (var skip in new[] { 2, 1, 0 })
            {
                if (skip >= data.Length)
                {
                    continue;
                }

                var inflated = Inflate(data, skip);
                if (inflated.Length > 0)
                {
                    return inflated;
                }
            }
        }

        // Not compressed, or not zlib: the payload may already be the image.
        return data;
    }

    private static byte[] Inflate(byte[] data, int skip)
    {
        try
        {
            using var input = new MemoryStream(data, skip, data.Length - skip);
            using var deflate = new DeflateStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            deflate.CopyTo(output);
            return output.Length > 0 ? output.ToArray() : [];
        }
        catch (InvalidDataException)
        {
            return [];
        }
        catch (NotSupportedException)
        {
            return [];
        }
    }

    /// <summary>Returns the file extension for a recognised image, or null.</summary>
    public static string? DetectImage(byte[] data)
    {
        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
        {
            return ".jpg";
        }

        if (data.Length >= 8 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E &&
            data[3] == 0x47)
        {
            return ".png";
        }

        if (data.Length >= 6 && data[0] == 'G' && data[1] == 'I' && data[2] == 'F')
        {
            return ".gif";
        }

        if (data.Length >= 2 && data[0] == 'B' && data[1] == 'M')
        {
            return ".bmp";
        }

        return null;
    }

    /// <summary>Page count if it can be read from the page tree, otherwise null.</summary>
    public static int? TryReadPageCount(string pdfPath)
    {
        try
        {
            var text = Encoding.Latin1.GetString(File.ReadAllBytes(pdfPath));
            var counts = Regex.Matches(text, @"/Type\s*/Pages[^>]*?/Count\s+(\d+)");
            var best = 0;
            foreach (Match m in counts)
            {
                if (int.TryParse(m.Groups[1].Value, out var n))
                {
                    best = Math.Max(best, n);
                }
            }

            var pages = Regex.Matches(text, @"/Type\s*/Page[^s]").Count;
            return best > 0 ? best : pages > 0 ? pages : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
