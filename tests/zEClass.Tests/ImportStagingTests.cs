using System;
using System.IO;
using Xunit;

namespace zEClass.Tests;

public sealed class ImportStagingTests : IDisposable
{
    private readonly string _root;

    public ImportStagingTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "zEClass-staging-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void CreateSessionFolder_IsUniqueDirectory()
    {
        var a = ImportStaging.CreateSessionFolder(_root);
        var b = ImportStaging.CreateSessionFolder(_root);

        Assert.NotEqual(a, b);
        Assert.True(Directory.Exists(a));
        Assert.True(Directory.Exists(b));
    }

    [Fact]
    public void CleanupOrphans_RemovesExpiredSessionsKeepsFresh()
    {
        var old = ImportStaging.CreateSessionFolder(_root);
        var fresh = ImportStaging.CreateSessionFolder(_root);
        Directory.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-30));

        ImportStaging.CleanupOrphans(_root, TimeSpan.FromDays(7), long.MaxValue);

        Assert.False(Directory.Exists(old));
        Assert.True(Directory.Exists(fresh));
    }

    [Fact]
    public void CleanupOrphans_EvictsOldestFirstOverSizeBudget()
    {
        var first = ImportStaging.CreateSessionFolder(_root);
        File.WriteAllBytes(Path.Combine(first, "a.bin"), new byte[100]);
        var second = ImportStaging.CreateSessionFolder(_root);
        File.WriteAllBytes(Path.Combine(second, "b.bin"), new byte[100]);
        Directory.SetLastWriteTimeUtc(first, DateTime.UtcNow.AddHours(-2));
        Directory.SetLastWriteTimeUtc(second, DateTime.UtcNow);

        ImportStaging.CleanupOrphans(_root, TimeSpan.FromDays(7), 150);

        Assert.False(Directory.Exists(first));
        Assert.True(Directory.Exists(second));
    }

    [Fact]
    public void CleanupOrphans_NeverTouchesForeignFolders()
    {
        var foreign = Path.Combine(_root, "teacher-notes");
        Directory.CreateDirectory(foreign);
        File.WriteAllText(Path.Combine(foreign, "keep.txt"), "keep");
        Directory.SetLastWriteTimeUtc(foreign, DateTime.UtcNow.AddDays(-60));

        ImportStaging.CleanupOrphans(_root, TimeSpan.Zero, 0);

        Assert.True(Directory.Exists(foreign));
    }

    [Fact]
    public void IsSessionFolder_ScreensNames()
    {
        Assert.True(ImportStaging.IsSessionFolder(
            Path.Combine(_root, Guid.NewGuid().ToString("N"))));
        Assert.False(ImportStaging.IsSessionFolder(Path.Combine(_root, "teacher-notes")));
        Assert.False(ImportStaging.IsSessionFolder(Path.Combine(_root, "..")));
    }
}
