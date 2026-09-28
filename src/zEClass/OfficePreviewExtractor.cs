using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace zEClass;

/// <summary>
/// Pulls the preview image out of an Open XML container.
///
/// PPTX and DOCX are zip archives, and both formats embed a preview thumbnail for the shell to
/// show. Reading that thumbnail is enough to put something meaningful on the board without
/// Office being installed, which matters because school images routinely have no Office and
/// automation against it is fragile and slow.
///
/// This reads a preview, not document content: it cannot reflow a slide, and it says so rather
/// than pretending otherwise.
///
/// Office files are hostile input: every read below is bounded by <see cref="ZipLimits"/>
/// (archive size, entry count, per-entry compressed and uncompressed sizes, total extraction
/// budget, compression ratio, title XML size, and processing time), entry names are screened
/// by <see cref="IsSafeEntryName"/> before use, and the written file carries the extension of
/// the image content that was actually detected, never a hardcoded one.
/// </summary>
public static class OfficePreviewExtractor
{
    /// <summary>Resource ceilings for Open XML import. Defaults protect a classroom machine;
    /// tests pass smaller values through the internal overloads.</summary>
    public sealed class ZipLimits
    {
        public static ZipLimits Default { get; } = new();

        public long MaxArchiveBytes { get; init; } = 512L * 1024 * 1024;

        public int MaxEntryCount { get; init; } = 4096;

        public long MaxCompressedEntryBytes { get; init; } = 256L * 1024 * 1024;

        public long MaxUncompressedEntryBytes { get; init; } = 256L * 1024 * 1024;

        public long MaxTotalUncompressedBytes { get; init; } = 512L * 1024 * 1024;

        /// <summary>Refuses entries whose uncompressed-to-compressed size ratio exceeds this.
        /// A 1 MB run of zeros compresses to ~1 KB (ratio ~1000); real thumbnails and slide
        /// media are already compressed, so their ratio stays near 1.</summary>
        public long MaxCompressionRatio { get; init; } = 100;

        public long MaxTitleXmlBytes { get; init; } = 1L * 1024 * 1024;

        public TimeSpan MaxProcessingTime { get; init; } = TimeSpan.FromSeconds(30);
    }

    /// <summary>
    /// Returns the path of an extracted preview image, or null when the container has none.
    /// The image is written to a per-call staging folder because the source may be read-only.
    /// </summary>
    public static string? Extract(string path) => Extract(path, ZipLimits.Default);

    internal static string? Extract(string path, ZipLimits limits)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is not (".pptx" or ".docx" or ".docm" or ".pptm"))
        {
            return null;
        }

        try
        {
            if (new FileInfo(path).Length > limits.MaxArchiveBytes)
            {
                CrashLog.Info($"Office preview refused oversized archive: {path}");
                return null;
            }

            using var archive = ZipFile.OpenRead(path);
            if (archive.Entries.Count > limits.MaxEntryCount)
            {
                CrashLog.Info($"Office preview refused archive with {archive.Entries.Count} entries: {path}");
                return null;
            }

            var deadline = DateTime.UtcNow + limits.MaxProcessingTime;
            long totalUncompressed = 0;
            var entry = FindPreview(archive);
            while (entry is not null)
            {
                if (DateTime.UtcNow > deadline)
                {
                    CrashLog.Info($"Office preview processing budget exceeded: {path}");
                    return null;
                }

                if (TryReadEntry(entry, limits, ref totalUncompressed, out var bytes) &&
                    bytes.Length > 0)
                {
                    var folder = ImportStaging.CreateSessionFolder();

                    var target = Path.Combine(folder,
                        Path.GetFileNameWithoutExtension(path) + DetectExtension(bytes, entry.Name));
                    File.WriteAllBytes(target, bytes);
                    return target;
                }

                // The preferred entry was hostile or unreadable: fall through to the next
                // candidate rather than giving up on the whole file.
                entry = FindPreview(archive, entry);
            }

            return null;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException
                                       or UnauthorizedAccessException or NotSupportedException
                                       or ArgumentException)
        {
            CrashLog.Info($"Office preview extract failed for {path}: {ex.Message}");
            return null;
        }
    }

    private static ZipArchiveEntry? FindPreview(ZipArchive archive, ZipArchiveEntry? after = null)
    {
        // Prefer the declared thumbnail, then a slide image, then the media folder.
        // Unsafe names (traversal, absolute, ADS) are screened before anything reads them.
        var candidates = archive.Entries
            .Where(e => !string.IsNullOrEmpty(e.Name) && IsSafeEntryName(e.FullName))
            .OrderBy(e => PreviewRank(e.FullName))
            .ThenBy(e => e.FullName, StringComparer.Ordinal)
            .ToList();

        var skip = after is not null;
        foreach (var candidate in candidates)
        {
            if (skip)
            {
                if (ReferenceEquals(candidate, after))
                {
                    skip = false;
                }

                continue;
            }

            if (candidate.FullName.StartsWith("docProps/thumbnail", StringComparison.OrdinalIgnoreCase) ||
                candidate.FullName.EndsWith(".emf", StringComparison.OrdinalIgnoreCase) ||
                candidate.FullName.EndsWith(".wmf", StringComparison.OrdinalIgnoreCase) ||
                candidate.FullName.StartsWith("ppt/media/", StringComparison.OrdinalIgnoreCase) ||
                candidate.FullName.StartsWith("word/media/", StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return null;
    }

    private static int PreviewRank(string name)
    {
        if (name.StartsWith("docProps/thumbnail", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (name.StartsWith("ppt/media/", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (name.StartsWith("word/media/", StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        return 3;
    }

    /// <summary>
    /// True when a ZIP entry name is safe to locate and read: relative, free of parent
    /// traversal, free of drive/ADS syntax, and carrying a real file name.
    /// </summary>
    public static bool IsSafeEntryName(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            return false;
        }

        // Alternate-data-stream syntax and drive-qualified paths are never legitimate
        // inside an Open XML package.
        if (fullName.Contains(':'))
        {
            return false;
        }

        if (fullName.StartsWith('/') || fullName.StartsWith('\\') || Path.IsPathRooted(fullName))
        {
            return false;
        }

        var segments = fullName.Split('/', '\\');
        foreach (var segment in segments)
        {
            if (segment.Length == 0 || segment == "." || segment == "..")
            {
                return false;
            }
        }

        // The leaf must be a file name, not a directory.
        var leaf = segments[^1];
        return leaf.Length > 0 && !fullName.EndsWith('/') && !fullName.EndsWith('\\');
    }

    /// <summary>
    /// Reads one entry within budget. Returns false when the entry's declared sizes already
    /// exceed the caps, the ratio check fails, the stream overruns the caps while copying,
    /// or the entry cannot be opened. Declared sizes are untrusted hints, so the streaming
    /// copy enforces the same caps independently.
    /// </summary>
    private static bool TryReadEntry(ZipArchiveEntry entry, ZipLimits limits,
        ref long totalUncompressed, out byte[] bytes)
    {
        bytes = [];
        try
        {
            // Overflow-safe comparisons: the multiplier stays on the small declared side.
            if (entry.CompressedLength > limits.MaxCompressedEntryBytes ||
                entry.Length > limits.MaxUncompressedEntryBytes)
            {
                return false;
            }

            if (entry.CompressedLength > 0 &&
                entry.Length / entry.CompressedLength > limits.MaxCompressionRatio)
            {
                return false;
            }

            if (entry.Length > limits.MaxTotalUncompressedBytes - totalUncompressed)
            {
                return false;
            }

            using var stream = entry.Open();
            using var output = new MemoryStream();
            var buffer = new byte[81920];
            long written = 0;
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                if ((long)read > limits.MaxUncompressedEntryBytes - written ||
                    (long)read > limits.MaxTotalUncompressedBytes - totalUncompressed - written)
                {
                    return false;
                }

                written += read;
                output.Write(buffer, 0, read);

                // Re-check the ratio against observed bytes: a lying header must not smuggle
                // a bomb past the declared-size gate.
                if (entry.CompressedLength > 0 &&
                    written / entry.CompressedLength > limits.MaxCompressionRatio)
                {
                    return false;
                }
            }

            bytes = output.ToArray();
            totalUncompressed += written;
            return bytes.Length > 0;
        }
        catch (IOException)
        {
            bytes = [];
            return false;
        }
        catch (InvalidDataException)
        {
            bytes = [];
            return false;
        }
    }

    /// <summary>
    /// Maps detected image content to its file extension. Sniffs magic bytes first; falls back
    /// to the entry's own extension when it names a known image type; never hardcodes .emf.
    /// </summary>
    internal static string DetectExtension(byte[] bytes, string entryName)
    {
        var sniffed = PdfImporter.DetectImage(bytes);
        if (sniffed is not null)
        {
            return sniffed == ".jpeg" ? ".jpg" : sniffed;
        }

        // EMF: type 0x00000001 followed by a plausible header size.
        if (bytes.Length >= 88 && bytes[0] == 0x01 && bytes[1] == 0x00 &&
            bytes[2] == 0x00 && bytes[3] == 0x00)
        {
            return ".emf";
        }

        // WMF: placeable header D7 CD C6 9A, orald-style 01 00 09 00.
        if (bytes.Length >= 4 &&
            ((bytes[0] == 0xD7 && bytes[1] == 0xCD && bytes[2] == 0xC6 && bytes[3] == 0x9A) ||
             (bytes[0] == 0x01 && bytes[1] == 0x00 && bytes[2] == 0x09 && bytes[3] == 0x00)))
        {
            return ".wmf";
        }

        return Path.GetExtension(entryName).ToLowerInvariant() switch
        {
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".emf" or ".wmf"
                or ".tif" or ".tiff" or ".webp" => Path.GetExtension(entryName).ToLowerInvariant() == ".jpeg"
                    ? ".jpg"
                    : Path.GetExtension(entryName).ToLowerInvariant(),
            _ => ".png",
        };
    }

    /// <summary>Best-effort title from the package, for the new page name.</summary>
    public static string TitleOf(string path) => TitleOf(path, ZipLimits.Default);

    internal static string TitleOf(string path, ZipLimits limits)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is not (".pptx" or ".docx" or ".docm" or ".pptm"))
        {
            return Path.GetFileNameWithoutExtension(path);
        }

        try
        {
            if (new FileInfo(path).Length > limits.MaxArchiveBytes)
            {
                return Path.GetFileNameWithoutExtension(path);
            }

            using var archive = ZipFile.OpenRead(path);
            if (archive.Entries.Count > limits.MaxEntryCount)
            {
                return Path.GetFileNameWithoutExtension(path);
            }

            var core = archive.GetEntry("docProps/core.xml");
            if (core is null || !IsSafeEntryName(core.FullName))
            {
                return Path.GetFileNameWithoutExtension(path);
            }

            if (core.Length > limits.MaxTitleXmlBytes ||
                core.CompressedLength > limits.MaxCompressedEntryBytes)
            {
                return Path.GetFileNameWithoutExtension(path);
            }

            using var stream = core.Open();
            using var reader = new StreamReader(
                new BoundedReadStream(stream, limits.MaxTitleXmlBytes), Encoding.UTF8);
            var xml = reader.ReadToEnd();
            var match = Regex.Match(xml, "<dc:title>(.*?)</dc:title>", RegexOptions.Singleline);
            var title = match.Success ? match.Groups[1].Value.Trim() : string.Empty;
            // Titles are page names: cap length and strip control characters rather than
            // trusting package XML.
            if (title.Length == 0)
            {
                return Path.GetFileNameWithoutExtension(path);
            }

            var clean = new string(title.Where(c => !char.IsControl(c)).ToArray()).Trim();
            if (clean.Length == 0)
            {
                return Path.GetFileNameWithoutExtension(path);
            }

            return clean.Length <= 128 ? clean : clean[..128];
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException
                                       or UnauthorizedAccessException or ArgumentException)
        {
            return Path.GetFileNameWithoutExtension(path);
        }
    }

    /// <summary>Stream wrapper that refuses to yield more than <see cref="_cap"/> bytes.</summary>
    private sealed class BoundedReadStream(Stream inner, long cap) : Stream
    {
        private long _remaining = cap;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_remaining <= 0)
            {
                return 0;
            }

            var allowed = (int)Math.Min(count, _remaining);
            var read = inner.Read(buffer, offset, allowed);
            _remaining -= read;
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
