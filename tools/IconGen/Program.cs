using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EBoard.IconGen;

/// <summary>
/// Draws the EBoard mark and writes a multi-resolution .ico.
///
/// The artwork is vector and re-rendered per size rather than scaled from one bitmap, because
/// a downscaled 256 px icon turns to mush at 16 px in the taskbar. At the small sizes the
/// details that cannot survive (the pen nib, the frame bevel) are dropped, leaving the
/// silhouette a teacher can still recognise across a classroom.
/// </summary>
internal static class Program
{
    private static readonly int[] Sizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256];

    private static readonly Color Frame = Color.FromRgb(0x1B, 0x1F, 0x27);
    private static readonly Color FrameEdge = Color.FromRgb(0x33, 0x39, 0x45);
    private static readonly Color Face = Color.FromRgb(0xFF, 0xFF, 0xFF);
    private static readonly Color InkBlue = Color.FromRgb(0x3D, 0x7E, 0xFF);
    private static readonly Color InkRed = Color.FromRgb(0xE5, 0x39, 0x35);

    /// <summary>
    /// Dumps individual frames to PNG. Used for visual inspection of the mark at the sizes the
    /// shell actually asks for, which is the only reliable way to know a 16 px icon reads.
    /// </summary>
    public static void DumpFrames(string outDir, params int[] sizes)
    {
        Directory.CreateDirectory(outDir);
        foreach (var size in sizes)
        {
            File.WriteAllBytes(Path.Combine(outDir, $"frame-{size}.png"), RenderPng(size));
        }
    }

    private static int Main(string[] args)
    {
        var output = args.Length > 0
            ? args[0]
            : Path.Combine(AppContext.BaseDirectory, "EBoard.ico");

        if (args.Length > 1 && args[1] == "--dump")
        {
            var sizes = args.Skip(2).Select(int.Parse).ToArray();
            DumpFrames(output, sizes.Length == 0 ? [16, 32, 48, 256] : sizes);
            Console.WriteLine($"dumped frames to {output}");
            return 0;
        }

        var frames = new List<byte[]>(Sizes.Length);
        foreach (var size in Sizes)
        {
            frames.Add(RenderPng(size));
            Console.WriteLine($"  rendered {size}x{size}");
        }

        var ico = PackIco(frames, Sizes);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllBytes(output, ico);
        Console.WriteLine($"wrote {output} ({ico.Length} bytes, {frames.Count} sizes)");
        return 0;
    }

    private static byte[] RenderPng(int size)
    {
        // Draw at 4x and downsample, so edges are antialiased rather than stair-stepped. The
        // geometry is produced by the same unit-square code as every other size, scaled by a
        // plain transform, which keeps the mark identical across the set.
        const int Supersample = 4;
        var edge = size * Supersample;

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(Supersample, Supersample));
            Draw(dc, size);
            dc.Pop();
        }

        // DPI stays at 96 while the pixel dimensions grow. Raising the DPI as well would make
        // Render map the visual's units to even more pixels and blow the artwork up again.
        var large = new RenderTargetBitmap(edge, edge, 96, 96, PixelFormats.Pbgra32);
        large.Render(visual);
        large.Freeze();

        var bitmap = new TransformedBitmap(large,
            new ScaleTransform(1.0 / Supersample, 1.0 / Supersample));
        bitmap.Freeze();

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// The mark: a whiteboard seen head on, with a hand-written stroke across it. Drawn in a
    /// 0..1 unit square so every size uses the same geometry.
    /// </summary>
    private static void Draw(DrawingContext dc, int size)
    {
        var s = size;
        double U(double v) => v * s;

        // Rounded board body with a subtle vertical gradient so it reads as a lit panel.
        var body = new LinearGradientBrush(
            Color.FromRgb(0x3A, 0x41, 0x4F), Color.FromRgb(0x20, 0x24, 0x2C), 90);
        body.Freeze();
        dc.DrawRoundedRectangle(body, new Pen(new SolidColorBrush(FrameEdge), U(0.02)),
            new Rect(U(0.05), U(0.05), U(0.90), U(0.90)), U(0.09), U(0.09));

        // White writing surface, inset, with a shadow so it looks recessed.
        var surfaceRect = new Rect(U(0.15), U(0.15), U(0.70), U(0.60));
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(60, 0, 0, 0)), null,
            new Rect(surfaceRect.X, surfaceRect.Y + U(0.02), surfaceRect.Width, surfaceRect.Height));
        dc.DrawRectangle(new SolidColorBrush(Face), null, surfaceRect);

        // Board tray along the bottom of the surface.
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xE3, 0xE6, 0xEC)), null,
            new Rect(U(0.15), U(0.75), U(0.70), U(0.05)));

        if (s >= 32)
        {
            // Ink stroke across the surface. Two segments so it reads as handwriting rather
            // than a single ruled line.
            var stroke = new Pen(new SolidColorBrush(InkBlue), Math.Max(1, U(0.055)))
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round,
            };
            stroke.Freeze();
            var figure = new PathFigure
            {
                StartPoint = new Point(U(0.24), U(0.42)),
                IsClosed = false,
                IsFilled = false,
            };
            figure.Segments.Add(new BezierSegment(
                new Point(U(0.36), U(0.24)),
                new Point(U(0.44), U(0.58)),
                new Point(U(0.56), U(0.36)), true));
            figure.Segments.Add(new BezierSegment(
                new Point(U(0.66), U(0.22)),
                new Point(U(0.72), U(0.52)),
                new Point(U(0.78), U(0.44)), true));
            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            geometry.Freeze();
            dc.DrawGeometry(null, stroke, geometry);
        }
        else
        {
            // At 16-24 px a bezier turns to noise; a single clean bar keeps the shape legible.
            var stroke = new Pen(new SolidColorBrush(InkBlue), Math.Max(1, U(0.07)))
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
            };
            stroke.Freeze();
            dc.DrawLine(stroke, new Point(U(0.26), U(0.40)), new Point(U(0.74), U(0.40)));
        }

        // Pen nib in the lower right, the way a teacher holds one against the board.
        if (s >= 48)
        {
            var pen = new LinearGradientBrush(
                Color.FromRgb(0xFF, 0xC1, 0x07), Color.FromRgb(0xE5, 0x8A, 0x00), 45);
            pen.Freeze();
            var body2 = new PathGeometry();
            var f = new PathFigure
            {
                StartPoint = new Point(U(0.63), U(0.86)),
                IsClosed = true,
            };
            f.Segments.Add(new LineSegment(new Point(U(0.80), U(0.60)), true));
            f.Segments.Add(new LineSegment(new Point(U(0.88), U(0.66)), true));
            f.Segments.Add(new LineSegment(new Point(U(0.71), U(0.92)), true));
            body2.Figures.Add(f);
            body2.Freeze();
            dc.DrawGeometry(pen, new Pen(new SolidColorBrush(Frame), U(0.015)), body2);

            var tip = new PathGeometry();
            var tf = new PathFigure { StartPoint = new Point(U(0.63), U(0.86)), IsClosed = true };
            tf.Segments.Add(new LineSegment(new Point(U(0.57), U(0.94)), true));
            tf.Segments.Add(new LineSegment(new Point(U(0.67), U(0.96)), true));
            tip.Figures.Add(tf);
            tip.Freeze();
            dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(0x33, 0x39, 0x45)), null, tip);

            // Ink dot where the nib meets the tray, and a red mark on the surface for colour.
            dc.DrawEllipse(new SolidColorBrush(InkRed), null,
                new Point(U(0.32), U(0.62)), U(0.055), U(0.055));
        }
        else if (s >= 32)
        {
            dc.DrawEllipse(new SolidColorBrush(InkRed), null,
                new Point(U(0.32), U(0.62)), U(0.06), U(0.06));
        }
    }

    /// <summary>
    /// Packs PNG-compressed frames into an .ico. PNG entries are valid from Windows Vista
    /// onward and are what every current Windows shell expects, including for the small sizes
    /// where it preserves antialiasing better than a hand-built BMP would.
    /// </summary>
    private static byte[] PackIco(IReadOnlyList<byte[]> pngFrames, IReadOnlyList<int> sizes)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);

        w.Write((ushort)0); // reserved
        w.Write((ushort)1); // type: icon
        w.Write((ushort)sizes.Count);

        var offset = 6 + (sizes.Count * 16);
        for (var i = 0; i < sizes.Count; i++)
        {
            // 0 means 256 in the on-disk format; the field is a single byte.
            w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            w.Write((byte)0); // palette size
            w.Write((byte)0); // reserved
            w.Write((ushort)1); // colour planes
            w.Write((ushort)32); // bits per pixel
            w.Write(pngFrames[i].Length);
            w.Write(offset);
            offset += pngFrames[i].Length;
        }

        foreach (var frame in pngFrames)
        {
            w.Write(frame);
        }

        w.Flush();
        return ms.ToArray();
    }
}
