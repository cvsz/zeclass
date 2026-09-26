using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Xunit;
using zEClass.Core;
using zEClass.Tools;

namespace zEClass.Tests;

/// <summary>
/// Tests for the NDI bridge and its settings.
///
/// None of these spawn a real process. The bridge takes its process starter and its file probe
/// as delegates, so the tests use fakes. Anything here that touched a live process table would
/// be untestable on a build machine and dangerous on a classroom one — and process identity is
/// exactly what must never be guessed at.
/// </summary>
public sealed class NdiBridgeTests
{
    private sealed class FakeProcess : INdiProcess
    {
        public int Id { get; set; } = 4242;
        public DateTime StartTime { get; set; } = new(2026, 9, 26, 12, 0, 0, DateTimeKind.Local);
        public string ExePath { get; set; } = @"D:\ndi\vMixDesktopCapture.exe";
        public bool HasExited { get; set; }
        public IntPtr MainWindowHandle { get; set; } = new(1234);
        public bool KillCalled { get; private set; }
        public bool ThrowOnKill { get; set; }
        public bool DieOnKill { get; set; } = true;
        public bool MinimizeCalled { get; private set; }
        public bool Disposed { get; private set; }

        public void Kill()
        {
            KillCalled = true;
            if (DieOnKill)
            {
                HasExited = true;
            }

            if (ThrowOnKill)
            {
                // The kill raced the exit, or access was denied: the real failure the
                // production code once surfaced as an unhandled Win32Exception.
                throw new InvalidOperationException("process already exited");
            }
        }

        public void Minimize() => MinimizeCalled = true;

        public void Dispose() => Disposed = true;
    }

    private sealed class Harness
    {
        public readonly List<ProcessStartInfo> Starts = new();
        public readonly FakeProcess Process = new();
        public bool FileExists = true;
        public bool StarterReturnsNull;

        public NdiBridge Bridge(string? exe = null, bool enabled = true) => new(
            exe ?? @"D:\ndi\vMixDesktopCapture.exe",
            enabled,
            info =>
            {
                Starts.Add(info);
                return StarterReturnsNull ? null : Process;
            },
            _ => FileExists);
    }

    [Fact]
    public void EnsureRunning_StartsMinimizedFromItsOwnFolder()
    {
        var h = new Harness();
        var bridge = h.Bridge();

        Assert.Equal(NdiResult.Started, bridge.EnsureRunning());
        Assert.True(bridge.IsRunning);
        Assert.Single(h.Starts);
        Assert.Equal(ProcessWindowStyle.Minimized, h.Starts[0].WindowStyle);
        Assert.Equal(@"D:\ndi", h.Starts[0].WorkingDirectory);
    }

    [Fact]
    public void EnsureRunning_StartsAtMostOneInstance()
    {
        var h = new Harness();
        var bridge = h.Bridge();

        Assert.Equal(NdiResult.Started, bridge.EnsureRunning());
        Assert.Equal(NdiResult.AlreadyRunning, bridge.EnsureRunning());
        Assert.Single(h.Starts);
    }

    [Fact]
    public void EnsureRunning_WhenDisabled_StartsNothing()
    {
        var h = new Harness();

        Assert.Equal(NdiResult.Disabled, h.Bridge(enabled: false).EnsureRunning());
        Assert.Empty(h.Starts);
    }

    [Fact]
    public void EnsureRunning_WhenExeMissing_StartsNothingAndSaysSo()
    {
        var h = new Harness { FileExists = false };
        var bridge = h.Bridge();

        Assert.Equal(NdiResult.NotFound, bridge.EnsureRunning());
        Assert.Empty(h.Starts);
        Assert.False(bridge.IsRunning);
        Assert.Contains(@"D:\ndi\vMixDesktopCapture.exe", bridge.LastError);
    }

    [Fact]
    public void EnsureRunning_WhenStarterReturnsNull_ReportsFailure()
    {
        var h = new Harness { StarterReturnsNull = true };

        Assert.Equal(NdiResult.Failed, h.Bridge().EnsureRunning());
    }

    [Fact]
    public void EnsureRunning_WhenHelperDiesImmediately_ReportsFailure()
    {
        var h = new Harness();
        h.Process.HasExited = true;

        Assert.Equal(NdiResult.Failed, h.Bridge().EnsureRunning());
    }

    [Fact]
    public void EnsureRunning_MinimizesTheHelperWindowAfterStarting()
    {
        var h = new Harness();
        var bridge = h.Bridge();

        Assert.Equal(NdiResult.Started, bridge.EnsureRunning());
        Assert.True(h.Process.MinimizeCalled);
    }

    [Fact]
    public void EnsureRunning_WithoutAWindow_StillCountsAsStarted()
    {
        // A helper with no window (tray-only, or slow to show one) is still a running helper.
        // The minimize attempt must not turn a success into a failure.
        var h = new Harness();
        h.Process.MainWindowHandle = IntPtr.Zero;
        var bridge = h.Bridge();
        bridge.SettleTimeoutMs = 50;
        bridge.SettlePollMs = 5;

        Assert.Equal(NdiResult.Started, bridge.EnsureRunning());
        Assert.True(bridge.IsRunning);
    }

    [Fact]
    public void Stop_KillsOnlyTheTrackedInstance()
    {
        var h = new Harness();
        var bridge = h.Bridge();
        bridge.EnsureRunning();

        Assert.True(bridge.Stop());
        Assert.True(h.Process.KillCalled);
        Assert.True(h.Process.Disposed);
        Assert.False(bridge.IsRunning);
    }

    [Fact]
    public void Stop_WhenKillRacesAnExit_ReportsSuccess()
    {
        // The kill threw because the process exited first. Nothing of ours remains, so the
        // goal is met; surfacing an exception for a dead process would be the bug.
        // Alive at the identity check, gone by the time Kill runs: the exact race from
        // the field report. DieOnKill flips the state as Kill throws.
        var h = new Harness();
        h.Process.ThrowOnKill = true;
        h.Process.DieOnKill = true;
        h.Process.HasExited = false;
        var bridge = h.Bridge();
        bridge.EnsureRunning();

        Assert.True(bridge.Stop());
        Assert.Null(bridge.LastError);
    }

    [Fact]
    public void Stop_WhenKillFailsAndTheProcessSurvives_ReportsFailure()
    {
        var h = new Harness();
        h.Process.ThrowOnKill = true;
        h.Process.DieOnKill = false;
        h.Process.HasExited = false;
        var bridge = h.Bridge();
        bridge.EnsureRunning();

        Assert.False(bridge.Stop());
        Assert.Contains("still running", bridge.LastError);
    }

    [Fact]
    public void Stop_WhenIdle_ReturnsFalse()
    {
        var h = new Harness();

        Assert.False(h.Bridge().Stop());
    }

    [Fact]
    public void Stop_RefusesAPidThatWasReusedBySomethingElse()
    {
        // Windows reuses PIDs. The tracked process exited and an unrelated process now holds
        // the number with a different start time: killing it would be the exact disaster this
        // class exists to prevent.
        var h = new Harness();
        var bridge = h.Bridge();
        bridge.EnsureRunning();
        h.Process.StartTime = h.Process.StartTime.AddSeconds(30);

        Assert.False(bridge.Stop());
        Assert.False(h.Process.KillCalled);
        Assert.False(bridge.IsRunning);
    }

    [Fact]
    public void Stop_RefusesAProcessWithADifferentExecutable()
    {
        var h = new Harness();
        var bridge = h.Bridge();
        bridge.EnsureRunning();
        h.Process.ExePath = @"C:\Windows\notepad.exe";

        Assert.False(bridge.Stop());
        Assert.False(h.Process.KillCalled);
    }

    [Fact]
    public void IsRunning_AfterExternalExit_ForgetsTheProcess()
    {
        var h = new Harness();
        var bridge = h.Bridge();
        bridge.EnsureRunning();
        h.Process.HasExited = true;

        Assert.False(bridge.IsRunning);

        // And the next Ensure starts a fresh instance rather than adopting the corpse.
        h.Process.HasExited = false;
        Assert.Equal(NdiResult.Started, bridge.EnsureRunning());
        Assert.Equal(2, h.Starts.Count);
    }

    [Fact]
    public void ResolveExePath_PrefersTheEnvironmentOverride()
    {
        var previous = Environment.GetEnvironmentVariable(NdiBridge.ExePathVariable);
        try
        {
            Environment.SetEnvironmentVariable(NdiBridge.ExePathVariable, @"E:\tools\cap.exe");
            Assert.Equal(@"E:\tools\cap.exe", NdiBridge.ResolveExePath());
        }
        finally
        {
            Environment.SetEnvironmentVariable(NdiBridge.ExePathVariable, previous);
        }
    }

    [Fact]
    public void ResolveExePath_FallsBackToTheInstalledPath()
    {
        var previous = Environment.GetEnvironmentVariable(NdiBridge.ExePathVariable);
        try
        {
            Environment.SetEnvironmentVariable(NdiBridge.ExePathVariable, null);
            Assert.Equal(NdiBridge.DefaultExePath, NdiBridge.ResolveExePath());
        }
        finally
        {
            Environment.SetEnvironmentVariable(NdiBridge.ExePathVariable, previous);
        }
    }
}

/// <summary>Round-trip tests for the persisted preferences.</summary>
public sealed class AppSettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(),
        "zeclass-settings-" + Guid.NewGuid().ToString("N"));

    public AppSettingsTests() => Directory.CreateDirectory(_dir);

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
    public void MissingFile_YieldsDefaultsWithNdiAutoStartOn()
    {
        var settings = AppSettings.Load(Path.Combine(_dir, "settings.json"));

        Assert.True(settings.NdiAutoStart);
    }

    [Fact]
    public void SaveAndLoad_RoundTripsTheFlag()
    {
        var path = Path.Combine(_dir, "settings.json");
        new AppSettings { NdiAutoStart = false }.Save(path);

        Assert.False(AppSettings.Load(path).NdiAutoStart);
    }

    [Fact]
    public void CorruptFile_FallsBackToDefaults()
    {
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, "{ not json");

        Assert.True(AppSettings.Load(path).NdiAutoStart);
    }
}
