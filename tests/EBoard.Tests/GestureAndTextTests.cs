using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using EBoard.Core;
using Xunit;

namespace EBoard.Tests;

public sealed class GestureRecognizerTests
{
    [Fact]
    public void FistHeldLongEnoughIsDetected()
    {
        var g = new GestureRecognizer();
        g.NoteDown(1, 500, 500, 0);

        for (var ms = 400; ms <= 1600; ms += 100)
        {
            g.NoteHold(1, 500, 500, ms);
        }

        Assert.True(g.TryConsume(out var kind));
        Assert.Equal(GestureKind.FistErase, kind);
    }

    [Fact]
    public void QuickTapIsNotAFistDwell()
    {
        var g = new GestureRecognizer();
        g.NoteDown(1, 500, 500, 0);
        g.NoteHold(1, 500, 500, 500);

        Assert.False(g.TryConsume(out _));
    }

    [Fact]
    public void MovingContactIsNotADwell()
    {
        var g = new GestureRecognizer();
        g.NoteDown(1, 500, 500, 0);
        for (var ms = 400; ms <= 2000; ms += 100)
        {
            // Drifts well beyond the still radius.
            g.NoteHold(1, 500 + ms, 500, ms);
        }

        Assert.False(g.TryConsume(out _));
    }

    [Fact]
    public void PalmDwellIsDetectedShorterThanFist()
    {
        var g = new GestureRecognizer { FistDwellMs = 3000 };
        // The OS classified this contact as a palm, which is what separates the two dwells.
        g.NoteDown(1, 500, 500, 0, isPalm: true);

        for (var ms = 400; ms <= 1200; ms += 100)
        {
            g.NoteHold(1, 500, 500, ms);
        }

        Assert.True(g.TryConsume(out var kind));
        Assert.Equal(GestureKind.PalmLaunch, kind);
    }

    [Fact]
    public void PalmFlaggedContactDoesNotBecomeAFist()
    {
        var g = new GestureRecognizer();
        g.NoteDown(1, 500, 500, 0, isPalm: true);

        for (var ms = 400; ms <= 3000; ms += 100)
        {
            g.NoteHold(1, 500, 500, ms);
        }

        Assert.True(g.TryConsume(out var kind));
        Assert.Equal(GestureKind.PalmLaunch, kind);
    }

    [Fact]
    public void NormalContactHeldForFistDurationIsNotAPalmLaunch()
    {
        // The shorter palm threshold must not pre-empt the fist on an ordinary contact.
        var g = new GestureRecognizer();
        g.NoteDown(1, 500, 500, 0, isPalm: false);

        for (var ms = 400; ms <= 1000; ms += 100)
        {
            g.NoteHold(1, 500, 500, ms);
        }

        Assert.False(g.TryConsume(out _));
    }

    [Fact]
    public void FastHorizontalSwipeRightIsDetected()
    {
        var g = new GestureRecognizer();
        g.NoteDown(1, 100, 500, 0);
        // 400 px in 200 ms, horizontal.
        g.NoteMove(1, 500, 505, 200);

        Assert.True(g.TryConsume(out var kind));
        Assert.Equal(GestureKind.WaveRight, kind);
    }

    [Fact]
    public void FastHorizontalSwipeLeftIsDetected()
    {
        var g = new GestureRecognizer();
        g.NoteDown(1, 900, 500, 0);
        g.NoteMove(1, 500, 495, 200);

        Assert.True(g.TryConsume(out var kind));
        Assert.Equal(GestureKind.WaveLeft, kind);
    }

    [Fact]
    public void SlowDragIsNotASwipe()
    {
        var g = new GestureRecognizer();
        g.NoteDown(1, 100, 500, 0);
        // Same distance but far slower, so speed is below the threshold.
        g.NoteMove(1, 600, 500, 2000);

        Assert.False(g.TryConsume(out _));
    }

    [Fact]
    public void MostlyVerticalDragIsNotASwipe()
    {
        var g = new GestureRecognizer();
        g.NoteDown(1, 500, 100, 0);
        g.NoteMove(1, 520, 600, 200);

        Assert.False(g.TryConsume(out _));
    }

    [Fact]
    public void ShortFlickIsNotASwipe()
    {
        var g = new GestureRecognizer();
        g.NoteDown(1, 500, 500, 0);
        g.NoteMove(1, 560, 500, 60);

        Assert.False(g.TryConsume(out _));
    }

    [Fact]
    public void TryConsume_EmptiesThePendingGesture()
    {
        var g = new GestureRecognizer();
        g.NoteDown(1, 100, 500, 0);
        g.NoteMove(1, 600, 500, 150);

        Assert.True(g.TryConsume(out _));
        Assert.False(g.TryConsume(out _));
    }

    [Fact]
    public void NoteUp_ForgetsTheContact()
    {
        var g = new GestureRecognizer();
        g.NoteDown(1, 500, 500, 0);
        g.NoteUp(1, 500, 500, 100);

        for (var ms = 200; ms <= 2000; ms += 100)
        {
            g.NoteHold(1, 500, 500, ms);
        }

        Assert.False(g.TryConsume(out _));
    }

    [Fact]
    public void Reset_ClearsAllState()
    {
        var g = new GestureRecognizer();
        g.NoteDown(1, 100, 500, 0);
        g.NoteMove(1, 600, 500, 150);
        g.Reset();

        Assert.False(g.TryConsume(out _));
        Assert.Equal(0, g.ActiveContactCount);
    }

    [Fact]
    public void ActiveContactCountTracksLiveContacts()
    {
        var g = new GestureRecognizer();
        g.NoteDown(1, 10, 10, 0);
        g.NoteDown(2, 20, 20, 0);

        Assert.Equal(2, g.ActiveContactCount);

        g.NoteUp(1, 10, 10, 10);
        Assert.Equal(1, g.ActiveContactCount);
    }
}

public sealed class TextEditSessionTests
{
    [Fact]
    public void BuildStroke_PreservesTextAndLayout()
    {
        var session = new TextEditSession
        {
            X = 100,
            Y = 200,
            Text = "E = mc2",
            FontSize = 40,
            ColorArgb = 0xFFE53935,
            PageIndex = 2,
        };

        var stroke = TextEditSession.BuildStroke(session);

        Assert.Equal(StrokeKind.Text, stroke.Kind);
        Assert.Equal("E = mc2", stroke.Text);
        Assert.Equal("text", stroke.Shape);
        Assert.Equal(0xFFE53935u, stroke.ColorArgb);
        Assert.Single(stroke.Points);
    }

    [Fact]
    public void FromStroke_RoundTripsLayout()
    {
        var session = new TextEditSession
        {
            X = 12.5,
            Y = 34.25,
            Text = "hi",
            FontSize = 28,
            PageIndex = 1,
        };

        var restored = TextEditSession.FromStroke(TextEditSession.BuildStroke(session));

        Assert.Equal(12.5, restored.X, 3);
        Assert.Equal(34.25, restored.Y, 3);
        Assert.Equal("hi", restored.Text);
        Assert.Equal(28, restored.FontSize);
        Assert.Equal(1, restored.PageIndex);
    }

    [Fact]
    public void FromStroke_FallsBackToFirstPointWhenLayoutIsMissing()
    {
        var stroke = new InkStroke
        {
            Kind = StrokeKind.Text,
            Text = "x",
            Points = { new InkPoint { X = 77, Y = 88 } },
        };

        var restored = TextEditSession.FromStroke(stroke);

        Assert.Equal(77, restored.X);
        Assert.Equal(88, restored.Y);
    }

    [Fact]
    public void FromStroke_ToleratesMalformedLayout()
    {
        var stroke = new InkStroke
        {
            Kind = StrokeKind.Text,
            Text = "x",
            Geometry = "not,a,valid,layout",
            Points = { new InkPoint { X = 5, Y = 6 } },
        };

        var restored = TextEditSession.FromStroke(stroke);

        Assert.Equal(5, restored.X);
        Assert.Equal(6, restored.Y);
    }
}
