using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace zEClass.Core;

/// <summary>One sampled point of a stroke, in board coordinates (device independent pixels).</summary>
public sealed class InkPoint
{
    public double X { get; set; }
    public double Y { get; set; }

    /// <summary>Normalized 0..1. Digitizers that do not report pressure give 0.</summary>
    public double Pressure { get; set; }

    /// <summary>True when the contact arrived from a physical pen rather than a finger or mouse.</summary>
    public bool IsStylus { get; set; }

    /// <summary>True for the eraser end of a stylus.</summary>
    public bool IsEraser { get; set; }

    /// <summary>Rotation in degrees reported by the digitizer, or NaN when unsupported.</summary>
    public double Tilt { get; set; } = double.NaN;

    public long TimeTicks { get; set; }
}

public enum StrokeKind
{
    Pen = 0,
    Highlighter = 1,
    Eraser = 2,
    Shape = 3,
    Text = 4,
}

/// <summary>A single ink stroke with its rendering attributes.</summary>
public sealed class InkStroke
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public StrokeKind Kind { get; set; } = StrokeKind.Pen;

    public List<InkPoint> Points { get; set; } = new();

    /// <summary>Stroke colour as #AARRGGBB.</summary>
    public uint ColorArgb { get; set; } = 0xFF000000;

    /// <summary>Nominal width in board units at full pressure.</summary>
    public double Width { get; set; } = 4.0;

    public double Opacity { get; set; } = 1.0;

    /// <summary>Shape geometry when <see cref="Kind"/> is Shape or Text.</summary>
    public string? Geometry { get; set; }

    /// <summary>Shape name for the shape tools: line, rectangle, ellipse, triangle, arrow, star, polygon, text.</summary>
    public string Shape { get; set; } = "line";

    public LineStyle LineStyle { get; set; } = LineStyle.Solid;

    public ShapeStyle Fill { get; set; } = ShapeStyle.Outline;

    public string? Text { get; set; }

    public InkStroke Clone() => new()
    {
        Id = Id,
        Kind = Kind,
        ColorArgb = ColorArgb,
        Width = Width,
        Opacity = Opacity,
        Geometry = Geometry,
        Shape = Shape,
        LineStyle = LineStyle,
        Fill = Fill,
        Text = Text,
        Points = Points.Select(p => new InkPoint
        {
            X = p.X,
            Y = p.Y,
            Pressure = p.Pressure,
            IsStylus = p.IsStylus,
            IsEraser = p.IsEraser,
            Tilt = p.Tilt,
            TimeTicks = p.TimeTicks,
        }).ToList(),
    };
}

/// <summary>An image placed on a page, as a board-space rectangle with a cached source path.</summary>
public sealed class InkImage
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public int PageIndex { get; set; }

    public double X { get; set; }

    public double Y { get; set; }

    public double Width { get; set; } = 480;

    public double Height { get; set; } = 360;

    /// <summary>
    /// Absolute path to the source file on disk. Cached by the renderer; a board that is moved
    /// to another machine keeps working as long as the image is present, and reports a missing
    /// file rather than failing to load the board.
    /// </summary>
    public string SourcePath { get; set; } = string.Empty;

    public double Opacity { get; set; } = 1.0;

    public double RotationDegrees { get; set; }

    public bool IsLocked { get; set; }

    public Rect Bounds => new(X, Y, Width, Height);

    public bool Contains(double px, double py) =>
        px >= X && px <= X + Width && py >= Y && py <= Y + Height;

    public InkImage Clone() => new()
    {
        Id = Id,
        PageIndex = PageIndex,
        X = X,
        Y = Y,
        Width = Width,
        Height = Height,
        SourcePath = SourcePath,
        Opacity = Opacity,
        RotationDegrees = RotationDegrees,
        IsLocked = IsLocked,
    };
}

public sealed class BoardPage
{
    public int Index { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<InkStroke> Strokes { get; set; } = new();

    /// <summary>Images placed on this page, drawn beneath the ink.</summary>
    public List<InkImage> Images { get; set; } = new();

    public int? TimingsPage { get; set; }

    /// <summary>Page background as #AARRGGBB; opaque white by default.</summary>
    public uint BackgroundColorArgb { get; set; } = 0xFFFFFFFF;

    /// <summary>
    /// Optional image drawn under the ink and above the background colour. A locked page with
    /// a background is the vendor's "fill" pen: writing on it reveals the picture underneath.
    /// </summary>
    public string? BackgroundImage { get; set; }

    /// <summary>When locked, the page is shielded from clearing and page-level operations.</summary>
    public bool Locked { get; set; }

    public BoardPage Clone() => new()
    {
        Index = Index,
        Name = Name,
        TimingsPage = TimingsPage,
        BackgroundColorArgb = BackgroundColorArgb,
        BackgroundImage = BackgroundImage,
        Locked = Locked,
        Strokes = Strokes.Select(s => s.Clone()).ToList(),
        Images = Images.Select(i => i.Clone()).ToList(),
    };
}

public sealed class BoardDocument
{
    public const string FileExtension = ".ebboard";
    public const int FileFormatVersion = 1;

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Untitled board";
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ModifiedUtc { get; set; } = DateTimeOffset.UtcNow;

    public double CanvasWidth { get; set; } = 1920;
    public double CanvasHeight { get; set; } = 1080;
    public int PageCount { get; set; } = 5;
    public int ActivePage { get; set; }

    /// <summary>Page turn animation mode: 0 none, 1 fade, 2 slide, 3 flip.</summary>
    public int PageTurnMode { get; set; } = 2;

    public bool AutoSaveEnabled { get; set; } = true;
    public int AutoSaveIntervalSeconds { get; set; } = 60;
    public int AutoSaveAddress { get; set; }
    public int PlaybackSpeed { get; set; } = 2;
    public int RightTimeout { get; set; }

    public int UIStyle { get; set; } = 1;
    public string Language { get; set; } = "en";

    /// <summary>Zoom factor for the contents-roam viewport. 1.0 fits the surface.</summary>
    public double ViewScale { get; set; } = 1.0;

    public double ViewOffsetX { get; set; }

    public double ViewOffsetY { get; set; }

    /// <summary>Inserted images, page-indexed position kept with the stroke list.</summary>
    public List<InkImage> Images { get; set; } = new();

    public List<BoardPage> Pages { get; set; } = new();

    public BoardDocument EnsurePages()
    {
        Pages.Sort((a, b) => a.Index.CompareTo(b.Index));
        for (var i = 0; i < PageCount; i++)
        {
            if (Pages.All(p => p.Index != i))
            {
                Pages.Add(new BoardPage { Index = i, Name = $"Page {i + 1}" });
            }
        }

        Pages = Pages.Where(p => p.Index < PageCount).OrderBy(p => p.Index).ToList();
        if (ActivePage >= PageCount || ActivePage < 0)
        {
            ActivePage = 0;
        }

        return this;
    }

    public BoardPage Active() => EnsurePages().Pages[ActivePage];
}

/// <summary>
/// Digitizers that do not report tilt or orientation give NaN, which System.Text.Json
/// refuses to write. This maps NaN and infinity to JSON null and back, so a board file
/// always round-trips and never throws mid-lesson.
/// </summary>
public sealed class NaNTolerantDoubleConverter : JsonConverter<double>
{
    public override double Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options) => reader.TokenType switch
    {
        JsonTokenType.Number => reader.GetDouble(),
        JsonTokenType.Null or JsonTokenType.String => double.NaN,
        _ => double.NaN,
    };

    public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteNumberValue(value);
    }

    public override double ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options) => double.TryParse(reader.GetString(),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v)
        ? v
        : double.NaN;

    public override void WriteAsPropertyName(Utf8JsonWriter writer, double value,
        JsonSerializerOptions options) => writer.WritePropertyName(
            value.ToString(System.Globalization.CultureInfo.InvariantCulture));
}

public static class BoardSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new NaNTolerantDoubleConverter() },
    };

    public static void Save(BoardDocument doc, string path)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        doc.ModifiedUtc = DateTimeOffset.UtcNow;
        doc.EnsurePages();

        var tmp = path + ".tmp";
        var json = JsonSerializer.Serialize(doc, Options);
        File.WriteAllText(tmp, json, new UTF8Encoding(false));
        File.Move(tmp, path, overwrite: true);
    }

    public static BoardDocument Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var json = File.ReadAllText(path, Encoding.UTF8);
        var doc = JsonSerializer.Deserialize<BoardDocument>(json, Options)
                  ?? throw new InvalidDataException("Board file deserialized to null.");
        return doc.EnsurePages();
    }

    public static bool TryLoad(string path, out BoardDocument? doc, out string? error)
    {
        doc = null;
        error = null;
        try
        {
            doc = Load(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException
                                       or UnauthorizedAccessException or NotSupportedException)
        {
            error = ex.Message;
            return false;
        }
    }
}
