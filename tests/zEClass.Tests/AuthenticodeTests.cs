using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using zEClass.Tools;

namespace zEClass.Tests;

/// <summary>
/// Authenticode verification through wintrust.dll. Unsigned means a real unsigned PE;
/// byte garbage is unverifiable rather than unsigned. Catalog-only system binaries read
/// back as NotSigned through this path, which callers must treat as absence of evidence.
/// </summary>
public sealed class AuthenticodeTests : IDisposable
{
    private readonly string _dir;

    public AuthenticodeTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "zEClass-auth-" + Guid.NewGuid().ToString("N"));
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

    private string CopyOwnAssembly(string name)
    {
        var path = Path.Combine(_dir, name);
        File.Copy(GetType().Assembly.Location, path);
        return path;
    }

    [Fact]
    public void Verify_UnsignedRealPe_ReturnsNotSigned()
    {
        Assert.Equal(SignatureState.NotSigned, Authenticode.Verify(CopyOwnAssembly("helper.exe")));
    }

    [Fact]
    public void Verify_CatalogOnlyBinaryReadsAsNotSigned()
    {
        var notepad = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System), "notepad.exe");

        Assert.Equal(SignatureState.NotSigned, Authenticode.Verify(notepad));
    }

    [Fact]
    public void Verify_GarbageFile_ReturnsInvalid()
    {
        var path = Path.Combine(_dir, "garbage.exe");
        File.WriteAllBytes(path, [0x4D, 0x5A, .. new byte[100]]);

        Assert.Equal(SignatureState.Invalid, Authenticode.Verify(path));
    }

    [Fact]
    public void Verify_TransientLockRecoversToCorrectVerdict()
    {
        // A lock that lands inside the native call (antivirus scan on close is the
        // usual cause) must not harden into a permanent Invalid. The observer runs
        // synchronously on the worker before each native call, so releasing inside
        // it is deterministic: attempt 0 is guaranteed to collide (the lock has been
        // held since before Verify started), attempt 1 is guaranteed a clear read.
        var path = CopyOwnAssembly("locked.exe");
        using var exclusive = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
        var attempts = 0;
        void Observer(int attempt)
        {
            Interlocked.Increment(ref attempts);
            if (attempt == 1)
            {
                exclusive.Dispose();
            }
        }

        Assert.Equal(SignatureState.NotSigned, Authenticode.Verify(path, Observer));
        Assert.Equal(2, attempts);
    }

    [Fact]
    public void PublisherOf_UnsignedReturnsNull()
    {
        Assert.Null(Authenticode.PublisherOf(CopyOwnAssembly("helper.exe")));
    }

    [Fact]
    public void PublisherOf_SystemBinaryReturnsNull()
    {
        // No embedded certificate to read; catalog signers are not exposed here.
        var notepad = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System), "notepad.exe");

        Assert.Null(Authenticode.PublisherOf(notepad));
    }
}
