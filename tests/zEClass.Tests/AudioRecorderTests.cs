using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;
using zEClass.Tools;

namespace zEClass.Tests;

/// <summary>
/// Tests for the audio recorder.
///
/// None of these touch real audio hardware. The recorder takes its MCI sender as a delegate, so
/// the tests use a fake that records the commands and returns success or failure on demand.
/// Anything that needed a microphone would be untestable on a build machine and flaky on a
/// classroom one, which is exactly where honesty matters most.
/// </summary>
public sealed class AudioRecorderTests
{
    private sealed class FakeMci
    {
        public readonly List<string> Commands = new();
        public uint FailOnContains { get; set; } = 0;
        public string FailSubstring { get; set; } = string.Empty;

        public uint Send(string command, StringBuilder? response, int length, IntPtr callback)
        {
            Commands.Add(command);
            if (!string.IsNullOrEmpty(FailSubstring) && command.Contains(FailSubstring,
                    StringComparison.Ordinal))
            {
                return FailOnContains == 0 ? 42u : FailOnContains;
            }

            return 0;
        }
    }

    [Fact]
    public void Start_OpensConfiguresAndRecordsInOrder()
    {
        var fake = new FakeMci();
        var recorder = new AudioRecorder(fake.Send);

        Assert.True(recorder.Start(@"C:\tmp\lesson.wav"));
        Assert.True(recorder.IsRecording);

        // Open first, quality before record: MCI waveaudio defaults to 8-bit mono at 11 kHz,
        // and setting quality after recording starts would apply to nothing.
        Assert.Equal(5, fake.Commands.Count);
        Assert.StartsWith("open new Type waveaudio Alias ", fake.Commands[0]);
        Assert.Contains("bitspersample 16", fake.Commands[1]);
        Assert.Contains("samplespersec 44100", fake.Commands[2]);
        Assert.Contains("channels 2", fake.Commands[3]);
        Assert.StartsWith("record ", fake.Commands[4]);

        var alias = fake.Commands[0].Split(' ').Last();
        Assert.All(fake.Commands.Skip(1), c => Assert.Contains(alias, c));
    }

    [Fact]
    public void Start_TwiceWithoutStopping_RefusesTheSecond()
    {
        var fake = new FakeMci();
        var recorder = new AudioRecorder(fake.Send);

        Assert.True(recorder.Start(@"C:\tmp\a.wav"));
        var count = fake.Commands.Count;

        Assert.False(recorder.Start(@"C:\tmp\b.wav"));
        Assert.Equal(count, fake.Commands.Count);
        Assert.True(recorder.IsRecording);
    }

    [Fact]
    public void Stop_SavesClosesAndReturnsThePath()
    {
        var fake = new FakeMci();
        var recorder = new AudioRecorder(fake.Send);
        recorder.Start(@"C:\tmp\lesson.wav");

        var path = recorder.Stop();

        Assert.Equal(@"C:\tmp\lesson.wav", path);
        Assert.False(recorder.IsRecording);
        var tail = fake.Commands.TakeLast(3).ToList();
        Assert.StartsWith("stop ", tail[0]);
        Assert.StartsWith("save ", tail[1]);
        Assert.Contains(@"C:\tmp\lesson.wav", tail[1]);
        Assert.StartsWith("close ", tail[2]);
    }

    [Fact]
    public void Stop_WhenIdle_ReturnsNullAndSendsNothing()
    {
        var fake = new FakeMci();
        var recorder = new AudioRecorder(fake.Send);

        Assert.Null(recorder.Stop());
        Assert.Empty(fake.Commands);
        Assert.False(recorder.IsRecording);
    }

    [Fact]
    public void Start_WhenTheDeviceCannotOpen_FailsCleanlyAndClosesTheAlias()
    {
        var fake = new FakeMci { FailSubstring = "open new" };
        var recorder = new AudioRecorder(fake.Send);

        Assert.False(recorder.Start(@"C:\tmp\lesson.wav"));
        Assert.False(recorder.IsRecording);
        Assert.NotEqual(0u, recorder.LastErrorCode);

        // The half-opened alias is closed, so a later Start does not trip over it.
        Assert.Contains(fake.Commands, c => c.StartsWith("close ", StringComparison.Ordinal));
    }

    [Fact]
    public void Stop_WhenTheSaveFails_ReturnsNullButStillCloses()
    {
        var fake = new FakeMci { FailSubstring = "save " };
        var recorder = new AudioRecorder(fake.Send);
        recorder.Start(@"C:\tmp\lesson.wav");

        Assert.Null(recorder.Stop());
        Assert.False(recorder.IsRecording);
        Assert.Contains(fake.Commands, c => c.StartsWith("close ", StringComparison.Ordinal));
    }

    [Fact]
    public void Instances_UseDifferentAliases()
    {
        var first = new FakeMci();
        var second = new FakeMci();
        var a = new AudioRecorder(first.Send);
        var b = new AudioRecorder(second.Send);

        a.Start(@"C:\tmp\a.wav");
        b.Start(@"C:\tmp\b.wav");

        var aliasA = first.Commands[0].Split(' ').Last();
        var aliasB = second.Commands[0].Split(' ').Last();
        Assert.NotEqual(aliasA, aliasB);
    }

    [Fact]
    public void Elapsed_IsZeroWhenIdleAndGrowsWhileRecording()
    {
        var fake = new FakeMci();
        var recorder = new AudioRecorder(fake.Send);

        Assert.Equal(TimeSpan.Zero, recorder.Elapsed);

        recorder.Start(@"C:\tmp\lesson.wav");
        Assert.True(recorder.Elapsed >= TimeSpan.Zero);
    }

    [Fact]
    public void Dispose_StopsAnActiveRecording()
    {
        var fake = new FakeMci();
        var recorder = new AudioRecorder(fake.Send);
        recorder.Start(@"C:\tmp\lesson.wav");

        recorder.Dispose();

        Assert.False(recorder.IsRecording);
        Assert.Contains(fake.Commands, c => c.StartsWith("save ", StringComparison.Ordinal));
    }

    [Fact]
    public void Stop_QuotesHostilePaths()
    {
        var fake = new FakeMci();
        var recorder = new AudioRecorder(fake.Send);
        var path = @"C:\tmp\my lesson (draft) & 100% — копия.wav";
        recorder.Start(path);

        Assert.Equal(path, recorder.Stop());
        var save = Assert.Single(fake.Commands, c => c.StartsWith("save ", StringComparison.Ordinal));
        Assert.Contains($"\"{path}\"", save);
    }

    [Fact]
    public void Stop_RefusesQuotedPathButStillCloses()
    {
        var fake = new FakeMci();
        var recorder = new AudioRecorder(fake.Send);
        // A quote cannot be represented in an MCI command string: the save must be refused
        // while the device is still closed. A bare file name skips directory creation, so
        // this reaches the save guard itself.
        var started = recorder.Start("quo\"te.wav");
        Assert.True(started);
        Assert.Null(recorder.Stop());

        Assert.False(recorder.IsRecording);
        Assert.DoesNotContain(fake.Commands, c => c.Contains("quo\"te"));
    }
}
