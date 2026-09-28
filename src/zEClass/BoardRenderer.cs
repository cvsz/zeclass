using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Text;
using System.Windows.Media.Imaging;
using zEClass.Core;

namespace zEClass;

/// <summary>
/// Renders a page to a bitmap for export, thumbnail generation and copy-to-clipboard. Kept
/// separate from the live surface so output is deterministic and independent of what happens
/// to be on screen.
/// </summary>
public static class BoardRenderer
{
    public static RenderTargetBitmap RenderPageToBitmap(BoardDocument document, double width, double height)
    {
        var w = (int)Math.Max(1, Math.Min(8000, Math.Round(width)));
        var h = (int)Math.Max(1, Math.Min(8000, Math.Round(height)));
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            DrawPage(dc, document, w, h);
        }

        var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    public static void DrawPage(DrawingContext dc, BoardDocument document, int pixelWidth, int pixelHeight)
    {
        var page = document.Active();
        var pageColor = Color.FromArgb((byte)((page.BackgroundColorArgb >> 24) & 0xFF),
            (byte)((page.BackgroundColorArgb >> 16) & 0xFF),
            (byte)((page.BackgroundColorArgb >> 8) & 0xFF),
            (byte)(page.BackgroundColorArgb & 0xFF));
        dc.DrawRectangle(new SolidColorBrush(pageColor), null, new Rect(0, 0, pixelWidth, pixelHeight));

        foreach (var image in page.Images)
        {
            var source = TryLoadImage(image.SourcePath);
            if (source is null)
            {
                dc.DrawRectangle(Brushes.LightGray, new Pen(Brushes.Gray, 1), image.Bounds);
                continue;
            }

            dc.PushOpacity(image.Opacity);
            dc.DrawImage(source, image.Bounds);
            dc.Pop();
        }

        if (!string.IsNullOrEmpty(page.BackgroundImage) &&
            TryLoadImage(page.BackgroundImage) is { } background)
        {
            dc.DrawImage(background, new Rect(0, 0, pixelWidth, pixelHeight));
        }

        var engine = new InkEngine();
        foreach (var stroke in page.Strokes)
        {
            var drawing = BuildStroke(engine, stroke);
            if (drawing is not null)
            {
                dc.DrawDrawing(drawing);
            }
        }
    }

    private static ImageSource? TryLoadImage(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static Drawing? BuildStroke(InkEngine engine, InkStroke stroke)
    {
        if (stroke.Points.Count == 0)
        {
            return null;
        }

        if (stroke.Kind is StrokeKind.Shape or StrokeKind.Text)
        {
            if (stroke.Kind == StrokeKind.Text)
            {
                var session = TextEditSession.FromStroke(stroke);
                return BuildText(session, engine.BrushFor(stroke));
            }

            var a = stroke.Points[0];
            var b = stroke.Points[^1];
            var rect = new Rect(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(b.X - a.X),
                Math.Abs(b.Y - a.Y));
            Geometry geometry = stroke.Shape switch
            {
                "rectangle" => new RectangleGeometry(rect, 0, 0),
                "ellipse" => new EllipseGeometry(
                    new Point(rect.X + (rect.Width / 2), rect.Y + (rect.Height / 2)),
                    rect.Width / 2, rect.Height / 2),
                _ => new LineGeometry(new Point(a.X, a.Y), new Point(b.X, b.Y)),
            };
            geometry.Freeze();
            var pen = new Pen(engine.BrushFor(stroke), Math.Max(1, stroke.Width))
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
            };
            pen.Freeze();
            return new GeometryDrawing(
                stroke.Fill != ShapeStyle.Outline ? engine.BrushFor(stroke) : null, pen, geometry);
        }

        var outline = engine.BuildOutline(stroke.Points, stroke.Width, 0.5);
        if (stroke.LineStyle != LineStyle.Solid)
        {
            var dashed = new Pen(engine.BrushFor(stroke), Math.Max(1, stroke.Width))
            {
                DashStyle = stroke.LineStyle == LineStyle.Dashed
                    ? new DashStyle([6, 4], 0)
                    : new DashStyle([1, 3], 0),
            };
            dashed.Freeze();
            return new GeometryDrawing(null, dashed, outline);
        }

        return new GeometryDrawing(engine.BrushFor(stroke), null, outline);
    }

    private static Drawing BuildText(TextEditSession session, Brush brush)
    {
        var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal,
            FontStretches.Normal);
        var formatted = new FormattedText(session.Text ?? string.Empty,
            System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            typeface, session.FontSize, brush, 1.0);
        var w = Math.Max(1, (int)Math.Ceiling(formatted.Width));
        var h = Math.Max(1, (int)Math.Ceiling(formatted.Height));
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawText(formatted, new Point(0, 0));
        }

        var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return new ImageDrawing(bitmap, new Rect(0, 0, formatted.Width, formatted.Height));
    }

    /// <summary>
    /// Writes a single-file PDF, one page per board page, with no external dependency.
    ///
    /// Each page is rendered to a bitmap and embedded as a JPEG image stream (DCTDecode),
    /// which the PDF specification supports natively. That keeps the writer small and
    /// dependency-free at the cost of lossy text edges; raising
    /// <see cref="PdfJpegQuality"/> trades file size for fidelity.
    /// </summary>
    public static void WritePdf(string path, BoardDocument document, IEnumerable<BoardPage> pages,
        double width, double height)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(document);

        var images = new List<byte[]>();
        var imageSizes = new List<(int W, int H)>();
        foreach (var page in pages)
        {
            var savedActive = document.ActivePage;
            document.ActivePage = page.Index;
            var bitmap = RenderPageToBitmap(document, width, height);
            document.ActivePage = savedActive;

            images.Add(EncodeJpeg(bitmap, PdfJpegQuality));
            imageSizes.Add((bitmap.PixelWidth, bitmap.PixelHeight));
        }

        if (images.Count == 0)
        {
            return;
        }

        var pdf = new PdfWriter(images, imageSizes, width, height);
        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, pdf.Build());
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>JPEG quality used for PDF page images, 1-100.</summary>
    public const int PdfJpegQuality = 88;

    private static byte[] EncodeJpeg(RenderTargetBitmap bitmap, int quality)
    {
        var encoder = new JpegBitmapEncoder { QualityLevel = Math.Clamp(quality, 1, 100) };
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }
}

/// <summary>
/// Minimal PDF writer for image-per-page documents. Emits exactly the objects a conforming
/// reader needs: catalog, page tree, one page per board page, and a JPEG stream per page.
/// </summary>
internal sealed class PdfWriter
{
    private readonly List<byte[]> _images;
    private readonly List<(int W, int H)> _sizes;
    private readonly double _width;
    private readonly double _height;

    public PdfWriter(List<byte[]> images, List<(int W, int H)> sizes, double width, double height)
    {
        _images = images;
        _sizes = sizes;
        _width = width;
        _height = height;
    }

    public byte[] Build()
    {
        // Fixed object layout, allocated before writing so ids are known up front:
        //   1 catalog, 2 page tree, then per image: page (3+3i), content (4+3i), image (5+3i).
        var pageIds = new List<int>();
        var contentIds = new List<int>();
        var imageIds = new List<int>();
        for (var i = 0; i < _images.Count; i++)
        {
            pageIds.Add(3 + (i * 3));
            contentIds.Add(4 + (i * 3));
            imageIds.Add(5 + (i * 3));
        }

        var body = new Dictionary<int, byte[]>
        {
            [1] = Ascii("<< /Type /Catalog /Pages 2 0 R >>"),
            [2] = Ascii($"<< /Type /Pages /Kids [{Join(pageIds)}] /Count {_images.Count} >>"),
        };

        for (var i = 0; i < _images.Count; i++)
        {
            var name = $"Im{i}";
            var (w, h) = _sizes[i];

            body[pageIds[i]] = Ascii(
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {_width:F2} {_height:F2}] " +
                $"/Resources << /XObject << /{name} {imageIds[i]} 0 R >> >> " +
                $"/Contents {contentIds[i]} 0 R >>");

            var content = Ascii($"q {_width:F2} 0 0 {_height:F2} 0 0 cm /{name} Do Q\n");
            var contentObj = new List<byte>();
            contentObj.AddRange(Ascii($"<< /Length {content.Length} >>\nstream\n"));
            contentObj.AddRange(content);
            contentObj.AddRange(Ascii("\nendstream"));
            body[contentIds[i]] = contentObj.ToArray();

            var imageObj = new List<byte>();
            imageObj.AddRange(Ascii(
                $"<< /Type /XObject /Subtype /Image /Width {w} /Height {h} " +
                "/ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length " +
                $"{_images[i].Length} >>\nstream\n"));
            imageObj.AddRange(_images[i]);
            imageObj.AddRange(Ascii("\nendstream"));
            body[imageIds[i]] = imageObj.ToArray();
        }

        var maxId = _images.Count == 0 ? 2 : imageIds[^1];
        using var ms = new MemoryStream();
        ms.Write(Ascii("%PDF-1.4\n"));
        ms.Write(new byte[] { (byte)'%', 0xE2, 0xE3, 0xCF, 0xD3, (byte)'\n' });

        var offsets = new Dictionary<int, long>();
        for (var id = 1; id <= maxId; id++)
        {
            offsets[id] = ms.Position;
            ms.Write(Ascii($"{id} 0 obj\n"));
            ms.Write(body.TryGetValue(id, out var payload) ? payload : Ascii("null"));
            ms.Write(Ascii("\nendobj\n"));
        }

        var xref = ms.Position;
        ms.Write(Ascii($"xref\n0 {maxId + 1}\n0000000000 65535 f \n"));
        for (var id = 1; id <= maxId; id++)
        {
            ms.Write(Ascii($"{offsets[id]:D10} 00000 n \n"));
        }

        ms.Write(Ascii(
            $"trailer\n<< /Size {maxId + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n"));
        return ms.ToArray();
    }

    private static string Join(List<int> ids) => string.Join(" ", ids.Select(i => $"{i} 0 R"));

    /// <summary>Latin-1 so bytes above 0x7F pass through unchanged.</summary>
    private static byte[] Ascii(string s) => Encoding.Latin1.GetBytes(s);
}
