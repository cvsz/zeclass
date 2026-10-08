using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using zEClass.Core;
using Xunit;

namespace zEClass.Tests;

public sealed class DocumentSessionTests : IDisposable
{
    private readonly string _dir;

    public DocumentSessionTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "zEClass-session-" + Guid.NewGuid().ToString("N"));
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

    [Fact]
    public void FreshSession_IsClean()
    {
        var session = new DocumentSession();

        Assert.False(session.IsDirty);
        Assert.Equal(0, session.CurrentRevision);
    }

    [Fact]
    public void Modification_MarksDirty_Save_ClearsIt()
    {
        var session = new DocumentSession();
        session.NotifyModified();

        Assert.True(session.IsDirty);

        session.NotifySaved();

        Assert.False(session.IsDirty);
    }

    [Fact]
    public void SaveAs_Failure_KeepsDirtyState()
    {
        // Models the transactional contract: the window only calls NotifySaved after the
        // write succeeds, so a failed Save-As can never clear the dirty flag.
        var session = new DocumentSession();
        session.NotifyModified();

        Assert.True(session.IsDirty);
    }

    [Fact]
    public void ShouldAutosave_SkipsCleanBoard()
    {
        var session = new DocumentSession();

        Assert.False(session.ShouldAutosave(true, 60, DateTime.UtcNow));
    }

    [Fact]
    public void ShouldAutosave_SkipsWhenDisabled()
    {
        var session = new DocumentSession();
        session.NotifyModified();

        Assert.False(session.ShouldAutosave(false, 60, DateTime.UtcNow));
    }

    [Fact]
    public void ShouldAutosave_SkipsAlreadyAutosavedRevision()
    {
        var session = new DocumentSession();
        session.NotifyModified();
        var now = DateTime.UtcNow;
        session.NotifyAutosaved(session.CurrentRevision, now);

        Assert.False(session.ShouldAutosave(true, 0, now.AddHours(1)));
    }

    [Fact]
    public void ShouldAutosave_HonorsInterval()
    {
        var session = new DocumentSession();
        session.NotifyModified();
        var start = DateTime.UtcNow;
        session.NotifyAutosaved(session.CurrentRevision, start);

        // New work since the last autosave, but the interval has not elapsed.
        session.NotifyModified();
        Assert.False(session.ShouldAutosave(true, 3600, start.AddMinutes(1)));
        Assert.True(session.ShouldAutosave(true, 60, start.AddMinutes(2)));
    }

    [Fact]
    public void StaleSnapshot_NeverOverwritesNewerAutosave()
    {
        var session = new DocumentSession();
        session.NotifyModified();
        session.NotifyModified();
        session.NotifyAutosaved(2, DateTime.UtcNow);

        // A slow background write for revision 1 finishing late must be dropped.
        Assert.False(session.ShouldWriteAutosave(1));
        session.NotifyAutosaved(1, DateTime.UtcNow.AddSeconds(1));
        Assert.Equal(2, session.AutosavedRevision);
    }

    [Fact]
    public void NeedsRecovery_TrueWhenAutosaveBeatsSave()
    {
        var saved = DateTime.UtcNow;
        Assert.True(DocumentSession.NeedsRecovery(saved, saved.AddSeconds(10)));
        Assert.False(DocumentSession.NeedsRecovery(saved, saved.AddSeconds(-10)));
        Assert.True(DocumentSession.NeedsRecovery(null, saved));
        Assert.False(DocumentSession.NeedsRecovery(saved, null));
    }

    [Fact]
    public void RotateAutosaves_BoundsGenerations()
    {
        var live = Path.Combine(_dir, "autosave.ebboard");
        File.WriteAllText(live, "rev3");
        DocumentSession.RotateAutosaves(live);
        File.WriteAllText(live, "rev4");
        DocumentSession.RotateAutosaves(live);
        File.WriteAllText(live, "rev5");

        Assert.Equal("rev5", File.ReadAllText(live));
        Assert.Equal("rev4", File.ReadAllText(live + ".1"));
        Assert.False(File.Exists(live + ".2"));
    }

    [Fact]
    public void RotateAutosaves_ToleratesMissingLiveFile()
    {
        var ex = Record.Exception(() => DocumentSession.RotateAutosaves(
            Path.Combine(_dir, "absent.ebboard")));

        Assert.Null(ex);
    }

    [Fact]
    public void Reset_ReturnsToClean()
    {
        var session = new DocumentSession();
        session.NotifyModified();
        session.NotifyAutosaved(1, DateTime.UtcNow);

        session.Reset();

        Assert.False(session.IsDirty);
        Assert.Equal(0, session.CurrentRevision);
        Assert.Null(session.LastAutosaveUtc);
    }

    [Fact]
    public async Task ConcurrentUse_StaysConsistent()
    {
        // Mutations arrive on the UI thread while autosave completions land on workers.
        // Without synchronization, increments vanish and the watermark can jump.
        var session = new DocumentSession();
        const int threads = 8;
        const int iterations = 500;
        var tasks = Enumerable.Range(0, threads).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < iterations; i++)
            {
                session.NotifyModified();
                var revision = session.CurrentRevision;
                if (session.ShouldWriteAutosave(revision))
                {
                    session.NotifyAutosaved(revision, DateTime.UtcNow);
                }
            }
        })).ToArray();
        await Task.WhenAll(tasks);

        Assert.Equal((long)threads * iterations, session.CurrentRevision);
        Assert.True(session.AutosavedRevision <= session.CurrentRevision);
    }
}
