using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace zEClass;

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
///
/// PDFs are hostile input: every read below is bounded by <see cref="PdfLimits"/> (file size,
/// per-stream compressed and inflated sizes, total inflated budget, image count, page count,
/// and processing time), inflating uses chunked reads with an explicit cap instead of an open
/// <c>CopyTo</c>, and arithmetic around the budgets is overflow-safe.
/// </summary>
public static class PdfImporter
{
    /// <summary>Resource ceilings for PDF import. Defaults protect a classroom machine;
    /// tests pass smaller values through the internal overloads.</summary>
    public sealed class PdfLimits
    {
        public static PdfLimits Default { get; } = new();

        public long MaxPdfFileBytes { get; init; } = 256L * 1024 * 1024;

        public long MaxCompressedStreamBytes { get; init; } = 64L * 1024 * 1024;

        public long MaxInflatedStreamBytes { get; init; } = 64L * 1024 * 1024;

        public long MaxTotalInflatedBytes { get; init; } = 256L * 1024 * 1024;

        public int MaxImages { get; init; } = 200;

        public int MaxPages { get; init; } = 2000;

        public TimeSpan MaxProcessingTime { get; init; } = TimeSpan.FromSeconds(60);
    }

    private static readonly byte[] StreamMarker = [(byte)'s', (byte)'t', (byte)'r', (byte)'e', (byte)'a', (byte)'m'];
    private static readonly byte[] EndStreamMarker =
        [(byte)'e', (byte)'n', (byte)'d', (byte)'s', (byte)'t', (byte)'r', (byte)'e', (byte)'a', (byte)'m'];

    /// <summary>
    /// Extracts page-sized images. Returns the paths written; failures are added to the
    /// caller's warning list rather than thrown.
    /// </summary>
    public static List<string> Render(string pdfPath, int pageWidth, int pageHeight,
        string stagingFolder, ImportResult result) =>
        Render(pdfPath, pageWidth, pageHeight, stagingFolder, result, PdfLimits.Default);

    internal static List<string> Render(string pdfPath, int pageWidth, int pageHeight,
        string stagingFolder, ImportResult result, PdfLimits limits)
    {
        var written = new List<string>();
        try
        {
            // Read through an open handle with the cap enforced during the copy: a file that
            // grows between the size probe and the read is refused rather than buffered.
            var bytes = ReadBounded(pdfPath, limits.MaxPdfFileBytes);
            if (bytes is null)
            {
                result.Warnings.Add(
                    $"{Path.GetFileName(pdfPath)}: file exceeds the " +
                    $"{limits.MaxPdfFileBytes}-byte import limit and was refused before reading.");
                return written;
            }

            var images = ExtractImages(bytes, result, limits);
            if (images.Count == 0)
            {
                result.Warnings.Add(
                    $"{Path.GetFileName(pdfPath)}: no embedded image found. This PDF is drawn " +
                    "with vector text and shapes, which zEClass does not render. Export the " +
                    "slides as images and import those instead.");
                return written;
            }

            Directory.CreateDirectory(stagingFolder);
            var taken = 0;
            foreach (var image in images)
            {
                if (taken >= limits.MaxImages)
                {
                    break;
                }

                if (image.Value.Length > limits.MaxInflatedStreamBytes)
                {
                    result.Warnings.Add(
                        $"{Path.GetFileName(pdfPath)}: skipped an image larger than the " +
                        $"{limits.MaxInflatedStreamBytes}-byte per-image limit.");
                    continue;
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
    /// Walks every stream in the file, inflates the FlateDecode ones within budget, and keeps
    /// any that start with a recognised image signature. Markers are searched as raw bytes so
    /// the file is never duplicated into a giant string; a stream whose declared span exceeds
    /// the compressed cap is skipped without being copied.
    /// </summary>
    private static List<(string Extension, byte[] Value)> ExtractImages(byte[] pdf,
        ImportResult result, PdfLimits limits)
    {
        var found = new List<(string, byte[])>();
        long totalInflated = 0;
        var oversizedSkipped = 0;
        var deadline = DateTime.UtcNow + limits.MaxProcessingTime;
        var index = 0;

        while (index < pdf.Length && found.Count < limits.MaxImages)
        {
            if (DateTime.UtcNow > deadline)
            {
                result.Warnings.Add("PDF processing budget exceeded; stopped scanning.");
                break;
            }

            var streamAt = IndexOf(pdf, StreamMarker, index);
            if (streamAt < 0)
            {
                break;
            }

            // Skip the EOL after the keyword.
            var dataStart = streamAt + StreamMarker.Length;
            if (dataStart < pdf.Length && pdf[dataStart] == (byte)'\r')
            {
                dataStart++;
            }

            if (dataStart < pdf.Length && pdf[dataStart] == (byte)'\n')
            {
                dataStart++;
            }

            var dataEnd = IndexOf(pdf, EndStreamMarker, dataStart);
            if (dataEnd < 0)
            {
                break;
            }

            // The EOL marker immediately before endstream is framing, not stream data.
            var end = dataEnd;
            if (end > dataStart && pdf[end - 1] == (byte)'\n')
            {
                end--;
            }

            if (end > dataStart && pdf[end - 1] == (byte)'\r')
            {
                end--;
            }

            var length = (long)end - dataStart;
            if (length > 0)
            {
                if (length > limits.MaxCompressedStreamBytes)
                {
                    oversizedSkipped++;
                }
                else
                {
                    var slice = new byte[(int)length];
                    Array.Copy(pdf, dataStart, slice, 0, (int)length);
                    if (TryInflate(slice, limits.MaxInflatedStreamBytes, out var payload) &&
                        payload.Length > 0)
                    {
                        // Overflow-safe: totalInflated never exceeds the cap, so the
                        // subtraction below cannot go negative.
                        if ((long)payload.Length > limits.MaxTotalInflatedBytes - totalInflated)
                        {
                            result.Warnings.Add(
                                "PDF total inflation budget exceeded; stopped scanning.");
                            break;
                        }

                        totalInflated += payload.Length;
                        var ext = DetectImage(payload);
                        if (ext is not null)
                        {
                            found.Add((ext, payload));
                        }
                    }
                }
            }

            index = dataEnd + EndStreamMarker.Length;
        }

        if (oversizedSkipped > 0)
        {
            result.Warnings.Add($"{oversizedSkipped} oversized stream(s) skipped.");
        }

        if (found.Count == 0)
        {
            result.Warnings.Add($"no embedded image streams found");
        }

        return found;
    }

    private static int IndexOf(byte[] haystack, byte[] needle, int start)
    {
        if (needle.Length == 0 || start >= haystack.Length)
        {
            return -1;
        }

        var limit = haystack.Length - needle.Length;
        for (var i = start; i <= limit; i++)
        {
            if (haystack[i] != needle[0])
            {
                continue;
            }

            var j = 1;
            while (j < needle.Length && haystack[i + j] == needle[j])
            {
                j++;
            }

            if (j == needle.Length)
            {
                return i;
            }
        }

        return -1;
    }

    private static bool TryInflate(byte[] data, long maxBytes, out byte[] payload)
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

                if (TryInflateRaw(data, skip, maxBytes, out payload))
                {
                    return true;
                }
            }
        }

        // Not compressed, or not zlib: the payload may already be the image.
        payload = data;
        return true;
    }

    private static bool TryInflateRaw(byte[] data, int skip, long maxBytes, out byte[] payload)
    {
        payload = [];
        try
        {
            using var input = new MemoryStream(data, skip, data.Length - skip);
            using var deflate = new DeflateStream(input, CompressionMode.Decompress);
            if (!TryCopyBounded(deflate, maxBytes, out payload))
            {
                return false;
            }

            return payload.Length > 0;
        }
        catch (InvalidDataException)
        {
            payload = [];
            return false;
        }
        catch (NotSupportedException)
        {
            payload = [];
            return false;
        }
        catch (IOException)
        {
            payload = [];
            return false;
        }
    }

    /// <summary>
    /// Copies at most <paramref name="maxBytes"/> from <paramref name="source"/>, returning
    /// false as soon as the cap would be exceeded. The running total never passes the cap,
    /// so the arithmetic cannot overflow.
    /// </summary>
    private static bool TryCopyBounded(Stream source, long maxBytes, out byte[] result)
    {
        result = [];
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            if ((long)read > maxBytes - total)
            {
                return false;
            }

            total += read;
            output.Write(buffer, 0, read);
        }

        result = output.ToArray();
        return true;
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

    /// <summary>Page count if it can be read from the page tree, otherwise null.
    /// Refuses files past the import size limit before reading, scans only a bounded head
    /// and tail (the catalog lives at the start, the trailer at the end), and caps the
    /// answer so a hostile /Count cannot drive unbounded downstream work.</summary>
    public static int? TryReadPageCount(string pdfPath) =>
        TryReadPageCount(pdfPath, PdfLimits.Default);

    internal static int? TryReadPageCount(string pdfPath, PdfLimits limits)
    {
        // 1 MB each end is ample: /Count is a few dozen bytes wherever it sits.
        const long ScanBytes = 1L * 1024 * 1024;
        try
        {
            var length = new FileInfo(pdfPath).Length;
            if (length > limits.MaxPdfFileBytes)
            {
                return null;
            }

            var text = ReadHeadAndTail(pdfPath, length, ScanBytes);
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
            return best > 0 ? Math.Min(best, limits.MaxPages)
                : pages > 0 ? Math.Min(pages, limits.MaxPages)
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Reads a file capped at <paramref name="maxBytes"/>, or null when it is
    /// longer. The length is checked on the open handle and re-checked during the copy,
    /// so TOCTOU growth is refused instead of buffered.</summary>
    private static byte[]? ReadBounded(string path, long maxBytes)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > maxBytes)
        {
            return null;
        }

        using var output = new MemoryStream();
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            if ((long)read > maxBytes - total)
            {
                return null;
            }

            total += read;
            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }

    /// <summary>Reads at most <paramref name="scanBytes"/> from the start and end of a file
    /// as Latin-1 text, for trailer/header scanning without loading the whole file.</summary>
    private static string ReadHeadAndTail(string path, long length, long scanBytes)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var head = new byte[(int)Math.Min(scanBytes, length)];
        var headRead = 0;
        while (headRead < head.Length)
        {
            var read = stream.Read(head, headRead, head.Length - headRead);
            if (read == 0)
            {
                break;
            }

            headRead += read;
        }

        if (length <= scanBytes * 2)
        {
            var rest = new byte[(int)(length - headRead)];
            var restRead = 0;
            while (restRead < rest.Length)
            {
                var read = stream.Read(rest, restRead, rest.Length - restRead);
                if (read == 0)
                {
                    break;
                }

                restRead += read;
            }

            return Encoding.Latin1.GetString(head, 0, headRead) +
                   Encoding.Latin1.GetString(rest, 0, restRead);
        }

        var tail = new byte[(int)scanBytes];
        stream.Position = length - tail.Length;
        var tailRead = 0;
        while (tailRead < tail.Length)
        {
            var read = stream.Read(tail, tailRead, tail.Length - tailRead);
            if (read == 0)
            {
                break;
            }

            tailRead += read;
        }

        return Encoding.Latin1.GetString(head, 0, headRead) +
               Encoding.Latin1.GetString(tail, 0, tailRead);
    }
}
