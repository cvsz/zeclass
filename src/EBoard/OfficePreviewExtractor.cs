using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace EBoard;

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
/// </summary>
public static class OfficePreviewExtractor
{
    private static readonly Regex JpegStart = new("ÿØÿ", RegexOptions.Compiled);

    /// <summary>
    /// Returns the path of an extracted preview image, or null when the container has none.
    /// The image is written to a per-call staging folder because the source may be read-only.
    /// </summary>
    public static string? Extract(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is not (".pptx" or ".docx" or ".docm" or ".pptm"))
        {
            return null;
        }

        try
        {
            using var archive = ZipFile.OpenRead(path);
            var entry = FindPreview(archive);
            if (entry is null)
            {
                return null;
            }

            var bytes = ReadAll(entry);
            if (bytes.Length == 0)
            {
                return null;
            }

            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "EBoard", "import", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);

            var target = Path.Combine(folder, Path.GetFileNameWithoutExtension(path) + ".emf");
            File.WriteAllBytes(target, bytes);
            return target;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException
                                       or UnauthorizedAccessException or NotSupportedException)
        {
            CrashLog.Info($"Office preview extract failed for {path}: {ex.Message}");
            return null;
        }
    }

    private static ZipArchiveEntry? FindPreview(ZipArchive archive)
    {
        // Prefer the declared thumbnail, then a slide image, then the media folder.
        var candidates = archive.Entries
            .Where(e => !string.IsNullOrEmpty(e.Name))
            .OrderBy(e => PreviewRank(e.FullName))
            .ThenBy(e => e.FullName, StringComparer.Ordinal)
            .ToList();

        return candidates.FirstOrDefault(e =>
            e.FullName.StartsWith("docProps/thumbnail", StringComparison.OrdinalIgnoreCase) ||
            e.FullName.EndsWith(".emf", StringComparison.OrdinalIgnoreCase) ||
            e.FullName.EndsWith(".wmf", StringComparison.OrdinalIgnoreCase) ||
            e.FullName.StartsWith("ppt/media/", StringComparison.OrdinalIgnoreCase) ||
            e.FullName.StartsWith("word/media/", StringComparison.OrdinalIgnoreCase));
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

    private static byte[] ReadAll(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>Best-effort title from the package, for the new page name.</summary>
    public static string TitleOf(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is not (".pptx" or ".docx"))
        {
            return Path.GetFileNameWithoutExtension(path);
        }

        try
        {
            using var archive = ZipFile.OpenRead(path);
            var core = archive.GetEntry("docProps/core.xml");
            if (core is null)
            {
                return Path.GetFileNameWithoutExtension(path);
            }

            using var reader = new StreamReader(core.Open(), Encoding.UTF8);
            var xml = reader.ReadToEnd();
            var match = Regex.Match(xml, "<dc:title>(.*?)</dc:title>", RegexOptions.Singleline);
            return match.Success && match.Groups[1].Value.Length > 0
                ? match.Groups[1].Value
                : Path.GetFileNameWithoutExtension(path);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException
                                       or UnauthorizedAccessException)
        {
            return Path.GetFileNameWithoutExtension(path);
        }
    }
}
