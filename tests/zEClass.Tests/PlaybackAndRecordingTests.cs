using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using zEClass.Core;
using zEClass.Tools;
using Xunit;

namespace zEClass.Tests;

public sealed class PlaybackTimelineTests
{
    private static InkStroke Stroke(long startMs, params (double X, double Y)[] points)
    {
        var stroke = new InkStroke { Width = 6, ColorArgb = 0xFFE53935 };
        foreach (var (x, y) in points)
        {
            stroke.Points.Add(new InkPoint
            {
                X = x,
                Y = y,
                TimeTicks = (startMs + (stroke.Points.Count * 10)) * TimeSpan.TicksPerMillisecond,
            });
        }

        return stroke;
    }

    [Fact]
    public void FromPage_OrdersStrokesByDrawTime()
    {
        var page = new BoardPage { Index = 0 };
        page.Strokes.Add(Stroke(1000, (10, 10), (20, 20)));
        page.Strokes.Add(Stroke(500, (30, 30), (40, 40)));

        var timeline = PlaybackTimeline.FromPage(page);

        Assert.Equal(2, timeline.Strokes.Count);
        Assert.True(timeline.Strokes[0].StartTicks < timeline.Strokes[1].StartTicks);
    }

    [Fact]
    public void FromPage_SkipsEmptyStrokes()
    {
        var page = new BoardPage { Index = 0 };
        page.Strokes.Add(new InkStroke());
        page.Strokes.Add(Stroke(100, (1, 1)));

        var timeline = PlaybackTimeline.FromPage(page);

        Assert.Single(timeline.Strokes);
    }

    [Fact]
    public void FromPage_EmptyPageProducesEmptyTimeline()
    {
        var timeline = PlaybackTimeline.FromPage(new BoardPage { Index = 0 });

        Assert.True(timeline.IsEmpty);
        Assert.Equal(0, timeline.TotalDurationTicks);
    }

    [Fact]
    public void FromPage_NullPageIsSafe()
    {
        var timeline = PlaybackTimeline.FromPage(null!);

        Assert.True(timeline.IsEmpty);
    }

    [Fact]
    public void VisibleAt_BeforeAnythingHappensShowsNothing()
    {
        var page = new BoardPage { Index = 0 };
        page.Strokes.Add(Stroke(1000, (10, 10), (20, 20)));
        var timeline = PlaybackTimeline.FromPage(page);

        var visible = timeline.VisibleAt(timeline.OriginTicks - 1000, timeline.OriginTicks, 1.0);

        Assert.Empty(visible);
    }

    [Fact]
    public void VisibleAt_MidStrokeShowsAPartialStroke()
    {
        var page = new BoardPage { Index = 0 };
        var stroke = Stroke(1000, (0, 0), (10, 0), (20, 0), (30, 0), (40, 0), (50, 0),
            (60, 0), (70, 0), (80, 0), (90, 0));
        page.Strokes.Add(stroke);
        var timeline = PlaybackTimeline.FromPage(page);
        var origin = timeline.OriginTicks;

        // Half way through the stroke's own duration.
        var half = origin + (stroke.Points[1].TimeTicks - stroke.Points[0].TimeTicks) / 2;
        var visible = timeline.VisibleAt(half, origin, 1.0);

        Assert.Single(visible);
        Assert.True(visible[0].Item2 > 0 && visible[0].Item2 < 1,
            $"fraction was {visible[0].Item2}");
    }

    [Fact]
    public void VisibleAt_AfterEverythingShowsTheWholePage()
    {
        var page = new BoardPage { Index = 0 };
        page.Strokes.Add(Stroke(1000, (0, 0), (10, 0), (20, 0)));
        var timeline = PlaybackTimeline.FromPage(page);

        var visible = timeline.VisibleAt(timeline.OriginTicks + timeline.TotalDurationTicks + 1,
            timeline.OriginTicks, 1.0);

        Assert.Single(visible);
        Assert.Equal(1.0, visible[0].Item2, 3);
        Assert.Equal(3, visible[0].Item1.Points.Count);
    }

    [Fact]
    public void VisibleAt_PreservesStrokeAppearance()
    {
        var page = new BoardPage { Index = 0 };
        var stroke = Stroke(1000, (0, 0), (10, 0), (20, 0));
        stroke.LineStyle = LineStyle.Dashed;
        stroke.Fill = ShapeStyle.Filled;
        page.Strokes.Add(stroke);
        var timeline = PlaybackTimeline.FromPage(page);

        var visible = timeline.VisibleAt(timeline.OriginTicks + timeline.TotalDurationTicks,
            timeline.OriginTicks, 1.0);

        var replayed = visible[0].Item1;
        Assert.Equal(0xFFE53935u, replayed.ColorArgb);
        Assert.Equal(6, replayed.Width);
        Assert.Equal(LineStyle.Dashed, replayed.LineStyle);
        Assert.Equal(ShapeStyle.Filled, replayed.Fill);
    }

    [Fact]
    public void VisibleAt_DoesNotAliasTheOriginalStroke()
    {
        var page = new BoardPage { Index = 0 };
        page.Strokes.Add(Stroke(1000, (0, 0), (10, 0), (20, 0)));
        var timeline = PlaybackTimeline.FromPage(page);

        var visible = timeline.VisibleAt(timeline.OriginTicks + timeline.TotalDurationTicks,
            timeline.OriginTicks, 1.0);

        visible[0].Item1.Points.Clear();
        Assert.Equal(3, page.Strokes[0].Points.Count);
    }

    [Fact]
    public void Slice_AlwaysReturnsAtLeastOnePoint()
    {
        var points = new List<InkPoint> { new() { X = 1, Y = 1 }, new() { X = 2, Y = 2 } };

        Assert.Single(PlaybackTimeline.Slice(points, 0));
    }

    [Fact]
    public void Slice_FullFractionCopiesEverything()
    {
        var points = new List<InkPoint>
        {
            new() { X = 1, Y = 1, Pressure = 0.4 },
            new() { X = 2, Y = 2, Pressure = 0.6 },
            new() { X = 3, Y = 3 },
        };

        var sliced = PlaybackTimeline.Slice(points, 1.0);

        Assert.Equal(3, sliced.Count);
        Assert.Equal(0.4, sliced[0].Pressure, 3);
    }
}

public sealed class ScreenRecorderTests : IDisposable
{
    private readonly string _dir;

    public ScreenRecorderTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "zEClass-rec-" + Guid.NewGuid().ToString("N"));
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

    /// <summary>
    /// Writes an AVI through the real muxer and validates the container structure by hand.
    /// A recorder that produces a file no player will open is worse than none, so the RIFF
    /// sizes, the chunk order and the index offsets are all checked rather than assumed.
    /// </summary>
    [Fact]
    public void MuxerProducesAValidRiffContainer()
    {
        var width = 64;
        var height = 48;
        var frames = 3;
        var path = Path.Combine(_dir, "test.avi");

        var recorder = new ScreenRecorder
        {
            // The muxer test is about the container, not the rate throttle.
            Limits = new ScreenRecorder.RecordingLimits { MaxFrameRate = 0 },
        };
        Assert.True(recorder.Start(path, width, height));
        Assert.Equal(64, recorder.Width);
        Assert.Equal(48, recorder.Height);

        var solid = new byte[width * 3 * height];
        for (var i = 0; i < frames; i++)
        {
            // Injected rather than grabbed, so the muxer is testable without a desktop session.
            Array.Fill(solid, (byte)(40 + (i * 20)));
            Assert.True(recorder.AddFrame(solid));
        }

        Assert.Equal(frames, recorder.FrameCount);
        var written = recorder.Stop();
        Assert.NotNull(written);
        Assert.True(File.Exists(written));

        var bytes = File.ReadAllBytes(written);
        var text = Encoding.ASCII.GetString(bytes);

        Assert.Equal("RIFF", text[..4]);
        Assert.Equal("AVI ", text[8..12]);
        Assert.Contains("hdrl", text);
        Assert.Contains("avih", text);
        Assert.Contains("strh", text);
        Assert.Contains("strf", text);
        Assert.Contains("movi", text);
        Assert.Contains("idx1", text);

        // The RIFF size must cover the file minus the 8-byte header.
        var riffSize = BitConverter.ToUInt32(bytes, 4);
        Assert.Equal(bytes.Length - 8, (int)riffSize);
    }

    [Fact]
    public void MuxerIndexOffsetsPointAtTheFrameChunks()
    {
        var path = Path.Combine(_dir, "index.avi");
        var recorder = new ScreenRecorder
        {
            Limits = new ScreenRecorder.RecordingLimits { MaxFrameRate = 0 },
        };
        recorder.Start(path, 32, 32);
        var solid = new byte[32 * 3 * 32];
        for (var i = 0; i < 2; i++)
        {
            recorder.AddFrame(solid);
        }

        var written = recorder.Stop();
        Assert.NotNull(written);

        var bytes = File.ReadAllBytes(written);
        var text = Encoding.ASCII.GetString(bytes);
        var moviData = text.IndexOf("movi", StringComparison.Ordinal) + 4;
        var idx = text.LastIndexOf("idx1", StringComparison.Ordinal);
        Assert.True(idx > 0);

        // Each index entry: FourCC, flags, offset, size.
        for (var i = 0; i < 2; i++)
        {
            var entry = idx + 4 + 4 + (i * 16);
            Assert.Equal("00db", text.Substring(entry, 4));
            var offset = (int)BitConverter.ToUInt32(bytes, entry + 8);
            var absolute = moviData + offset;
            Assert.Equal("00db", text.Substring(absolute, 4));
        }
    }

    [Fact]
    public void FrameSizeIsFourByteAligned()
    {
        var recorder = new ScreenRecorder();
        recorder.Start(Path.Combine(_dir, "align.avi"), 101, 50);

        Assert.Equal(0, recorder.RowBytes % 4);
    }

    [Fact]
    public void StartTwiceIsRejected()
    {
        var recorder = new ScreenRecorder();
        Assert.True(recorder.Start(Path.Combine(_dir, "a.avi"), 32, 32));
        Assert.False(recorder.Start(Path.Combine(_dir, "b.avi"), 32, 32));
        recorder.Stop();
    }

    [Fact]
    public void CaptureWithoutStartReturnsFalse()
    {
        var recorder = new ScreenRecorder();

        Assert.False(recorder.CaptureFrame());
        Assert.Null(recorder.Stop());
    }

    [Fact]
    public void StopWithNoFramesWritesNothing()
    {
        var path = Path.Combine(_dir, "empty.avi");
        var recorder = new ScreenRecorder();
        recorder.Start(path, 32, 32);

        Assert.Null(recorder.Stop());
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void MemoryStaysFlatWhileFramesStreamToDisk()
    {
        var path = Path.Combine(_dir, "flat.avi");
        var recorder = new ScreenRecorder
        {
            Limits = new ScreenRecorder.RecordingLimits { MaxFrameRate = 0 },
        };
        recorder.Start(path, 64, 48);
        var solid = new byte[64 * 3 * 48];

        GC.Collect();
        GC.WaitForPendingFinalizers();
        var before = GC.GetTotalMemory(true);

        for (var i = 0; i < 300; i++)
        {
            Assert.True(recorder.AddFrame(solid));
        }

        Assert.Equal(300, recorder.FrameCount);
        // The frames live on disk, not in memory: the file holds megabytes while the heap
        // must not have grown anywhere near that.
        Assert.True(recorder.BufferedBytes > 2 * 1024 * 1024, $"{recorder.BufferedBytes}");

        GC.Collect();
        GC.WaitForPendingFinalizers();
        var after = GC.GetTotalMemory(true);

        Assert.True(after - before < 32 * 1024 * 1024,
            $"managed heap grew by {(after - before) / 1024 / 1024} MB over 300 frames");
        recorder.Stop();
    }

    [Fact]
    public void DurationLimitFinalizesWithCapturedFrames()
    {
        var path = Path.Combine(_dir, "limited.avi");
        var recorder = new ScreenRecorder
        {
            Limits = new ScreenRecorder.RecordingLimits
            {
                MaxFrameRate = 0,
                MaxRecordingDuration = TimeSpan.FromMilliseconds(50),
            },
        };
        recorder.Start(path, 32, 32);
        var solid = new byte[32 * 3 * 32];
        Assert.True(recorder.AddFrame(solid));
        Assert.True(recorder.AddFrame(solid));

        System.Threading.Thread.Sleep(120);

        Assert.False(recorder.AddFrame(solid));
        Assert.True(recorder.LimitReached);
        Assert.False(string.IsNullOrWhiteSpace(recorder.LastError));
        Assert.Equal(2, recorder.FrameCount);

        var written = recorder.Stop();
        Assert.NotNull(written);
        Assert.True(new FileInfo(written).Length > 0);
    }

    [Fact]
    public void SizeLimitStopsBeforeOverflow()
    {
        var path = Path.Combine(_dir, "capped.avi");
        var recorder = new ScreenRecorder
        {
            Limits = new ScreenRecorder.RecordingLimits
            {
                MaxFrameRate = 0,
                MaxRecordingBytes = 100,
            },
        };
        recorder.Start(path, 32, 32);

        Assert.False(recorder.AddFrame(new byte[32 * 3 * 32]));
        Assert.True(recorder.LimitReached);
        Assert.False(string.IsNullOrWhiteSpace(recorder.LastError));
        Assert.Null(recorder.Stop());
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void InsufficientDiskRefusesStart()
    {
        var recorder = new ScreenRecorder
        {
            Limits = new ScreenRecorder.RecordingLimits
            {
                MinimumFreeDiskSpace = long.MaxValue,
            },
        };

        Assert.False(recorder.Start(Path.Combine(_dir, "nospace.avi"), 32, 32));
        Assert.False(string.IsNullOrWhiteSpace(recorder.LastError));
    }

    [Fact]
    public void FrameRateThrottleDropsAndCounts()
    {
        var recorder = new ScreenRecorder();
        recorder.Start(Path.Combine(_dir, "throttle.avi"), 32, 32);
        var solid = new byte[32 * 3 * 32];

        Assert.True(recorder.AddFrame(solid));
        Assert.True(recorder.AddFrame(solid));
        Assert.True(recorder.AddFrame(solid));

        Assert.Equal(1, recorder.FrameCount);
        Assert.Equal(2, recorder.DroppedFrames);
        recorder.Stop();
    }

    [Fact]
    public void NoThrottleWhenRateIsZero()
    {
        var recorder = new ScreenRecorder
        {
            Limits = new ScreenRecorder.RecordingLimits { MaxFrameRate = 0 },
        };
        recorder.Start(Path.Combine(_dir, "nothrottle.avi"), 32, 32);
        var solid = new byte[32 * 3 * 32];
        for (var i = 0; i < 5; i++)
        {
            Assert.True(recorder.AddFrame(solid));
        }

        Assert.Equal(5, recorder.FrameCount);
        Assert.Equal(0, recorder.DroppedFrames);
        recorder.Stop();
    }

    [Fact]
    public void CorruptedShortFrameIsRejected()
    {
        var recorder = new ScreenRecorder
        {
            Limits = new ScreenRecorder.RecordingLimits { MaxFrameRate = 0 },
        };
        recorder.Start(Path.Combine(_dir, "short.avi"), 32, 32);

        Assert.False(recorder.AddFrame(new byte[10]));
        Assert.Equal(0, recorder.FrameCount);
        recorder.Stop();
    }

    [Fact]
    public void StartClampsAbsurdDimensions()
    {
        var recorder = new ScreenRecorder();
        recorder.Start(Path.Combine(_dir, "clamp.avi"), 99999, -5);

        Assert.Equal(1920, recorder.Width);
        Assert.Equal(16, recorder.Height);
        recorder.Stop();
    }
}
