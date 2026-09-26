using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;
using zEClass.Core;

namespace zEClass.Tests;

/// <summary>
/// Renders strokes to a bitmap and counts ink pixels, no input delivery involved.
///
/// A synthetic mouse drag recently produced zero pixels on screen while the status bar reported
/// a committed stroke. That left two suspects: the input path never delivered moves, or the
/// render path draws nothing. This test executes the render half deterministically — a
/// hand-built stroke exactly like the surface produces — so a zero here convicts the renderer
/// and a pass clears it.
/// </summary>
[Collection("WpfWindows")]
public sealed class RenderTests
{
    private readonly StaUiFixture _ui;

    public RenderTests(StaUiFixture ui) => _ui = ui;

    /// <summary>
    /// Builds the same stroke the surface builds for a pen drag: opaque near-black, width 4,
    /// twenty points along a diagonal.
    /// </summary>
    internal static InkStroke PenDrag()
    {
        var stroke = new InkStroke
        {
            Kind = StrokeKind.Pen,
            ColorArgb = 0xFF1B1B1F,
            Width = 4,
            Opacity = 1.0,
        };
        for (var i = 0; i < 20; i++)
        {
            stroke.Points.Add(new InkPoint
            {
                X = 100 + (i * 10),
                Y = 100 + (i * 6),
                Pressure = 0.5,
                TimeTicks = DateTime.UtcNow.Ticks + i,
            });
        }

        return stroke;
    }

    internal static int CountDarkPixels(RenderTargetBitmap bitmap)
    {
        var stride = bitmap.PixelWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);
        var dark = 0;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var b = pixels[i];
            var g = pixels[i + 1];
            var r = pixels[i + 2];
            if (r < 200 || g < 200 || b < 200)
            {
                dark++;
            }
        }

        return dark;
    }

    [Fact]
    public void RenderedPenStroke_ProducesVisibleInk()
    {
        _ui.Invoke(() =>
        {
            var document = new BoardDocument { PageCount = 1 };
            document.EnsurePages();
            document.Active().Strokes.Add(PenDrag());

            var bitmap = BoardRenderer.RenderPageToBitmap(document, 400, 300);
            var dark = CountDarkPixels(bitmap);

            Assert.True(dark > 50,
                $"a 20-point pen stroke rendered only {dark} dark pixels on a 400x300 page");
        });
    }

    [Fact]
    public void RenderedSinglePointStroke_ProducesADot()
    {
        _ui.Invoke(() =>
        {
            var document = new BoardDocument { PageCount = 1 };
            document.EnsurePages();
            var dot = PenDrag();
            dot.Points.RemoveRange(1, dot.Points.Count - 1);
            document.Active().Strokes.Add(dot);

            var bitmap = BoardRenderer.RenderPageToBitmap(document, 400, 300);

            Assert.True(CountDarkPixels(bitmap) > 0,
                "a single-point stroke rendered nothing at all");
        });
    }

    [Fact]
    public void EmptyPage_RendersNoInk()
    {
        _ui.Invoke(() =>
        {
            var document = new BoardDocument { PageCount = 1 };
            document.EnsurePages();

            var bitmap = BoardRenderer.RenderPageToBitmap(document, 400, 300);

            Assert.Equal(0, CountDarkPixels(bitmap));
        });
    }

    [Fact]
    public void SurfaceOnRender_PaintsCommittedStrokes()
    {
        // The exact live path: a surface bound to a document renders its committed strokes
        // through OnRender with the document's own view transform. When strokes committed
        // fine but the canvas stayed blank, this is the test that would have caught it.
        _ui.Invoke(() =>
        {
            var document = new BoardDocument { PageCount = 1 };
            document.EnsurePages();
            document.CanvasWidth = 1920;
            document.CanvasHeight = 1080;
            document.Active().Strokes.Add(RenderTests.PenDrag());

            var surface = new InkSurface();
            surface.Document = document;

            Assert.False(double.IsNaN(surface.ViewScale) || surface.ViewScale == 0,
                $"ViewScale is {surface.ViewScale}; every stroke would collapse");
            Assert.False(double.IsNaN(surface.ViewOffsetX) || double.IsNaN(surface.ViewOffsetY),
                $"view offset is ({surface.ViewOffsetX}, {surface.ViewOffsetY})");

            var onRender = typeof(InkSurface).GetMethod("OnRender",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.NotNull(onRender);

            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
                400, 300, 96, 96, PixelFormats.Pbgra32);
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                onRender!.Invoke(surface, [dc]);
            }

            bitmap.Render(visual);
            bitmap.Freeze();

            Assert.True(CountDarkPixels(bitmap) > 50,
                "OnRender painted the background but no stroke ink");
        });
    }

    [Fact]
    public void SurfaceBuildDrawing_RendersVisibleInk()
    {
        // The renderer's copy of the stroke path is tested above. The live surface has its
        // own BuildDrawing, and a field of live strokes once failed to paint while committed
        // strokes counted in the status bar, so this exercises the surface's own method with
        // a stroke built exactly the way BeginStroke builds one.
        _ui.Invoke(() =>
        {
            var surface = new InkSurface();
            var stroke = RenderTests.PenDrag();
            var method = typeof(InkSurface).GetMethod("BuildDrawing",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.NotNull(method);

            var drawing = Assert.IsAssignableFrom<System.Windows.Media.Drawing>(
                method!.Invoke(surface, [stroke]));
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
                400, 300, 96, 96, PixelFormats.Pbgra32);
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawDrawing(drawing);
            }

            bitmap.Render(visual);
            bitmap.Freeze();

            Assert.True(CountDarkPixels(bitmap) > 50,
                "the surface's own BuildDrawing produced no visible ink");
        });
    }
}
