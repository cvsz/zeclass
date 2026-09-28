using System;
using System.IO;
using System.Linq;

namespace zEClass;

/// <summary>
/// Managed temporary storage for import staging (PDF page images, Office previews).
///
/// Every import lands in a unique per-operation folder so concurrent imports never share
/// files. Folders belong to the app's own directory and are reclaimed at startup by age
/// and total size, so a crash between import and use cannot grow the disk without bound.
/// Callers must only ever delete folders this class created: cleanup enumerates the
/// staging root and ignores anything that is not a session folder it recognizes.
/// </summary>
public static class ImportStaging
{
    /// <summary>How long a staged folder may lie unclaimed before startup cleanup takes it.</summary>
    public static TimeSpan MaxAge { get; } = TimeSpan.FromDays(7);

    /// <summary>Ceiling on the whole staging root; oldest folders go first.</summary>
    public static long MaxTotalBytes { get; } = 1L * 1024 * 1024 * 1024;

    public static string Root
    {
        get
        {
            var root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "zEClass", "import");
            Directory.CreateDirectory(root);
            return root;
        }
    }

    /// <summary>Creates a unique, empty session folder for one import operation.</summary>
    public static string CreateSessionFolder() => CreateSessionFolder(Root);

    internal static string CreateSessionFolder(string root)
    {
        var folder = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>
    /// Reclaims staging from previous runs: folders older than <paramref name="maxAge"/>
    /// go first, then the oldest remaining until the root fits
    /// <paramref name="maxTotalBytes"/>. Only GUID-named session folders are touched.
    /// </summary>
    public static void CleanupOrphans(TimeSpan? maxAge = null, long? maxTotalBytes = null) =>
        CleanupOrphans(Root, maxAge ?? MaxAge, maxTotalBytes ?? MaxTotalBytes);

    internal static void CleanupOrphans(string root, TimeSpan maxAge, long maxTotalBytes)
    {
        string[] folders;
        try
        {
            folders = Directory.GetDirectories(root)
                .Where(IsSessionFolder)
                .OrderBy(f => Directory.GetLastWriteTimeUtc(f))
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashLog.Info($"Import staging cleanup skipped: {ex.Message}");
            return;
        }

        var now = DateTime.UtcNow;
        foreach (var folder in folders)
        {
            try
            {
                if (now - Directory.GetLastWriteTimeUtc(folder) >= maxAge)
                {
                    Directory.Delete(folder, true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                CrashLog.Info($"Import staging cleanup skipped {folder}: {ex.Message}");
            }
        }

        // Enforce the size budget oldest-first across whatever survived the age pass.
        try
        {
            var remaining = Directory.GetDirectories(root)
                .Where(IsSessionFolder)
                .Select(f => new { Path = f, Bytes = FolderBytes(f), Write = Directory.GetLastWriteTimeUtc(f) })
                .OrderBy(x => x.Write)
                .ToList();
            var total = remaining.Sum(x => x.Bytes);
            foreach (var entry in remaining)
            {
                if (total <= maxTotalBytes)
                {
                    break;
                }

                try
                {
                    Directory.Delete(entry.Path, true);
                    total -= entry.Bytes;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    CrashLog.Info($"Import staging cleanup skipped {entry.Path}: {ex.Message}");
                    break;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashLog.Info($"Import staging cleanup skipped: {ex.Message}");
        }
    }

    internal static bool IsSessionFolder(string path)
    {
        var name = Path.GetFileName(path);
        return name.Length == 32 && Guid.TryParseExact(name, "N", out _);
    }

    private static long FolderBytes(string folder)
    {
        long total = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            {
                try
                {
                    total += new FileInfo(file).Length;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return total;
    }
}
