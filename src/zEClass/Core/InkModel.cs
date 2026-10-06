using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
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

    /// <summary>
    /// Cap on the page count a file may claim. Page creation is proportional to PageCount,
    /// so a hand-crafted file claiming two billion pages would otherwise exhaust memory
    /// from a few hundred bytes of JSON.
    /// </summary>
    public const int MaxPageCount = 10_000;

    /// <summary>Cap on the shape geometry or text one stroke may carry.</summary>
    public const int MaxShapeTextChars = 100_000;

    /// <summary>Caps on collections a board file may claim. Sanitization trims past the
    /// cap instead of refusing the file: a hostile board loads as a usable (smaller) board
    /// rather than exhausting memory or crashing the render loop.
    /// Defaults mirror these constants; tests pass smaller values through
    /// <see cref="BoardLimits"/>.</summary>
    public const int MaxStrokesPerPage = 100_000;

    public const int MaxTotalStrokes = 500_000;

    public const int MaxPointsPerStroke = 100_000;

    public const long MaxTotalPoints = 5_000_000;

    public const int MaxImagesPerPage = 1_000;

    public const int MaxTotalImages = 5_000;

    public const int MaxPageNameChars = 1_000;

    /// <summary>Cap on the board name a file may carry.</summary>
    public const int MaxNameChars = 1_000;

    /// <summary>Cap on the language tag; anything longer is not a BCP-47 tag.</summary>
    public const int MaxLanguageChars = 32;

    /// <summary>Cap on the shape name one stroke may carry.</summary>
    public const int MaxShapeNameChars = 64;

    /// <summary>Cap on stored file paths (background/image sources). Matches the
    /// Win32 long-path ceiling so legitimate UNC/long paths survive.</summary>
    public const int MaxPathChars = 32_768;

    /// <summary>Upper bound on canvas dimensions in board units. Rendering a board tens of
    /// millions of units wide would collapse the viewport math; legit boards stay near
    /// display sizes.</summary>
    public const double MaxCanvasDimension = 100_000;

    /// <summary>Resource ceilings for board sanitization. Defaults protect a classroom
    /// machine; tests pass smaller values through the internal overloads.</summary>
    public sealed class BoardLimits
    {
        public static BoardLimits Default { get; } = new();

        public int MaxPageCount { get; init; } = BoardDocument.MaxPageCount;

        public int MaxShapeTextChars { get; init; } = BoardDocument.MaxShapeTextChars;

        public int MaxStrokesPerPage { get; init; } = BoardDocument.MaxStrokesPerPage;

        public int MaxTotalStrokes { get; init; } = BoardDocument.MaxTotalStrokes;

        public int MaxPointsPerStroke { get; init; } = BoardDocument.MaxPointsPerStroke;

        public long MaxTotalPoints { get; init; } = BoardDocument.MaxTotalPoints;

        public int MaxImagesPerPage { get; init; } = BoardDocument.MaxImagesPerPage;

        public int MaxTotalImages { get; init; } = BoardDocument.MaxTotalImages;

        public int MaxPageNameChars { get; init; } = BoardDocument.MaxPageNameChars;

        public int MaxNameChars { get; init; } = BoardDocument.MaxNameChars;

        public int MaxLanguageChars { get; init; } = BoardDocument.MaxLanguageChars;

        public int MaxShapeNameChars { get; init; } = BoardDocument.MaxShapeNameChars;

        public int MaxPathChars { get; init; } = BoardDocument.MaxPathChars;

        public double MaxCanvasDimension { get; init; } = BoardDocument.MaxCanvasDimension;
    }

    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// On-disk format version. Files without the field predate versioning and read as
    /// <see cref="FileFormatVersion"/>; <see cref="BoardSerializer.Load"/> refuses a file
    /// claiming a newer or non-positive version instead of misparsing it.
    /// </summary>
    public int Version { get; set; } = FileFormatVersion;

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
        // Hot path (every Active() call): when the list already matches the count exactly,
        // in order, with a valid active page, sorting and rebuilding are all no-ops.
        if (IsConsistent())
        {
            return this;
        }

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

    private bool IsConsistent()
    {
        if (Pages.Count != PageCount || ActivePage < 0 || ActivePage >= PageCount)
        {
            return false;
        }

        for (var i = 0; i < Pages.Count; i++)
        {
            if (Pages[i] is null || Pages[i].Index != i)
            {
                return false;
            }
        }

        return true;
    }

    public BoardPage Active() => EnsurePages().Pages[ActivePage];

    /// <summary>
    /// Repairs a document that came off disk. Board files are untrusted input: a truncated
    /// or hand-edited file must load as a usable board instead of crashing the render loop,
    /// and a hostile one must not claim two billion pages, embed unbounded strings, or
    /// smuggle non-finite coordinates into WPF drawing calls that reject them.
    /// </summary>
    public BoardDocument Sanitize() => Sanitize(BoardLimits.Default);

    internal BoardDocument Sanitize(BoardLimits limits)
    {
        if (string.IsNullOrEmpty(Name))
        {
            Name = "Untitled board";
        }
        else if (Name.Length > limits.MaxNameChars)
        {
            Name = Name[..limits.MaxNameChars];
        }

        if (string.IsNullOrEmpty(Language) || Language.Length > limits.MaxLanguageChars)
        {
            Language = "en";
        }

        if (!double.IsFinite(CanvasWidth) || CanvasWidth <= 0)
        {
            CanvasWidth = 1920;
        }

        if (CanvasWidth > limits.MaxCanvasDimension)
        {
            CanvasWidth = limits.MaxCanvasDimension;
        }

        if (!double.IsFinite(CanvasHeight) || CanvasHeight <= 0)
        {
            CanvasHeight = 1080;
        }

        if (CanvasHeight > limits.MaxCanvasDimension)
        {
            CanvasHeight = limits.MaxCanvasDimension;
        }

        if (!double.IsFinite(ViewScale) || ViewScale <= 0)
        {
            ViewScale = 1.0;
        }

        if (!double.IsFinite(ViewOffsetX))
        {
            ViewOffsetX = 0;
        }

        if (!double.IsFinite(ViewOffsetY))
        {
            ViewOffsetY = 0;
        }

        if (PageTurnMode is < 0 or > 3)
        {
            PageTurnMode = 2;
        }

        if (PageCount < 1)
        {
            PageCount = 1;
        }

        if (PageCount > limits.MaxPageCount)
        {
            PageCount = limits.MaxPageCount;
        }

        Pages ??= new();
        Pages.RemoveAll(p => p is null);
        Pages = Pages.DistinctBy(p => p.Index).ToList();
        Images ??= new();
        Images.RemoveAll(i => i is null);
        if (Images.Count > limits.MaxTotalImages)
        {
            Images.RemoveRange(limits.MaxTotalImages, Images.Count - limits.MaxTotalImages);
        }

        foreach (var image in Images)
        {
            SanitizeImage(image, limits);
        }

        var totalStrokes = 0;
        var totalPoints = 0L;
        var totalImages = Images.Count;
        foreach (var page in Pages)
        {
            page.Name ??= string.Empty;
            if (page.Name.Length > limits.MaxPageNameChars)
            {
                page.Name = page.Name[..limits.MaxPageNameChars];
            }

            if (page.BackgroundImage is { Length: > 0 } bg && bg.Length > limits.MaxPathChars)
            {
                page.BackgroundImage = null;
            }

            page.Strokes ??= new();
            page.Strokes.RemoveAll(s => s is null);
            if (page.Strokes.Count > limits.MaxStrokesPerPage)
            {
                page.Strokes.RemoveRange(limits.MaxStrokesPerPage, page.Strokes.Count - limits.MaxStrokesPerPage);
            }

            if (totalStrokes + page.Strokes.Count > limits.MaxTotalStrokes)
            {
                page.Strokes.RemoveRange(0, page.Strokes.Count);
            }
            else
            {
                totalStrokes += page.Strokes.Count;
            }

            page.Images ??= new();
            page.Images.RemoveAll(i => i is null);
            if (page.Images.Count > limits.MaxImagesPerPage)
            {
                page.Images.RemoveRange(limits.MaxImagesPerPage, page.Images.Count - limits.MaxImagesPerPage);
            }

            if (totalImages + page.Images.Count > limits.MaxTotalImages)
            {
                page.Images.RemoveRange(0, page.Images.Count);
            }
            else
            {
                totalImages += page.Images.Count;
            }

            foreach (var image in page.Images)
            {
                SanitizeImage(image, limits);
            }

            foreach (var stroke in page.Strokes)
            {
                SanitizeStroke(stroke, limits, ref totalPoints);
            }
        }

        return EnsurePages();
    }

    /// <summary>Deep copy used to snapshot the live document before an off-thread save.</summary>
    public BoardDocument Clone() => new()
    {
        Id = Id,
        Name = Name,
        CreatedUtc = CreatedUtc,
        ModifiedUtc = ModifiedUtc,
        CanvasWidth = CanvasWidth,
        CanvasHeight = CanvasHeight,
        PageCount = PageCount,
        ActivePage = ActivePage,
        PageTurnMode = PageTurnMode,
        AutoSaveEnabled = AutoSaveEnabled,
        AutoSaveIntervalSeconds = AutoSaveIntervalSeconds,
        AutoSaveAddress = AutoSaveAddress,
        PlaybackSpeed = PlaybackSpeed,
        RightTimeout = RightTimeout,
        UIStyle = UIStyle,
        Language = Language,
        ViewScale = ViewScale,
        ViewOffsetX = ViewOffsetX,
        ViewOffsetY = ViewOffsetY,
        Images = Images.Select(i => i.Clone()).ToList(),
        Pages = Pages.Select(p => p.Clone()).ToList(),
    };

    private static void SanitizeImage(InkImage image, BoardLimits limits)
    {
        image.SourcePath ??= string.Empty;
        if (image.SourcePath.Length > limits.MaxPathChars)
        {
            // A path this long cannot name a real file; keep the record (position and
            // size are still meaningful) but drop the unloadable path.
            image.SourcePath = string.Empty;
        }

        if (!double.IsFinite(image.X))
        {
            image.X = 0;
        }

        if (!double.IsFinite(image.Y))
        {
            image.Y = 0;
        }

        if (!double.IsFinite(image.Width) || image.Width < 0)
        {
            image.Width = 480;
        }

        if (!double.IsFinite(image.Height) || image.Height < 0)
        {
            image.Height = 360;
        }

        if (!double.IsFinite(image.Opacity))
        {
            image.Opacity = 1.0;
        }

        image.Opacity = Math.Clamp(image.Opacity, 0, 1);
        if (!double.IsFinite(image.RotationDegrees))
        {
            image.RotationDegrees = 0;
        }
    }

    private static void SanitizeStroke(InkStroke stroke, BoardLimits limits, ref long totalPoints)
    {
        if (!Enum.IsDefined(typeof(StrokeKind), stroke.Kind))
        {
            stroke.Kind = StrokeKind.Pen;
        }

        if (!Enum.IsDefined(typeof(LineStyle), stroke.LineStyle))
        {
            stroke.LineStyle = LineStyle.Solid;
        }

        if (!Enum.IsDefined(typeof(ShapeStyle), stroke.Fill))
        {
            stroke.Fill = ShapeStyle.Outline;
        }

        stroke.Shape ??= "line";
        stroke.Shape = Truncate(stroke.Shape, limits.MaxShapeNameChars) ?? "line";
        stroke.Geometry = Truncate(stroke.Geometry, limits.MaxShapeTextChars);
        stroke.Text = Truncate(stroke.Text, limits.MaxShapeTextChars);
        if (!double.IsFinite(stroke.Width) || stroke.Width <= 0)
        {
            stroke.Width = 4.0;
        }

        if (!double.IsFinite(stroke.Opacity))
        {
            stroke.Opacity = 1.0;
        }

        stroke.Opacity = Math.Clamp(stroke.Opacity, 0, 1);

        stroke.Points ??= new();
        stroke.Points.RemoveAll(p => p is null);
        if (stroke.Points.Count > limits.MaxPointsPerStroke)
        {
            stroke.Points.RemoveRange(limits.MaxPointsPerStroke, stroke.Points.Count - limits.MaxPointsPerStroke);
        }

        var room = limits.MaxTotalPoints - totalPoints;
        if (room < stroke.Points.Count)
        {
            if (room <= 0)
            {
                stroke.Points.Clear();
            }
            else
            {
                stroke.Points.RemoveRange((int)room, stroke.Points.Count - (int)room);
            }
        }

        totalPoints += stroke.Points.Count;
        foreach (var point in stroke.Points)
        {
            if (!double.IsFinite(point.X))
            {
                point.X = 0;
            }

            if (!double.IsFinite(point.Y))
            {
                point.Y = 0;
            }

            if (!double.IsFinite(point.Pressure))
            {
                point.Pressure = 0;
            }

            point.Pressure = Math.Clamp(point.Pressure, 0, 1);
            if (double.IsInfinity(point.Tilt))
            {
                point.Tilt = double.NaN;
            }
        }
    }

    private static string? Truncate(string? value, int maxChars) =>
        value is null
            ? null
            : value.Length <= maxChars ? value : value[..maxChars];

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

    /// <summary>
    /// Load ceiling in bytes. Boards reference their images by path rather than embedding
    /// them, so a real lesson file stays small; anything past this is a mistake or an
    /// attack and is refused before being read into memory.
    /// </summary>
    internal const long DefaultMaxFileSizeBytes = 256L * 1024 * 1024;

    public static void Save(BoardDocument doc, string path)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        doc.ModifiedUtc = DateTimeOffset.UtcNow;
        doc.Version = BoardDocument.FileFormatVersion;
        doc.EnsurePages();

        var tmp = path + ".tmp";
        var json = JsonSerializer.Serialize(doc, Options);
        var expectedBytes = Encoding.UTF8.GetByteCount(json);
        try
        {
            // Write beside the target, flush to the disk, then rename over it: a crash at
            // any point leaves either the old file or the new one, never a half-written
            // board, and a failed write is cleaned up instead of lingering as debris.
            using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write,
                                               FileShare.None))
            {
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false),
                                                     16 * 1024, leaveOpen: true))
                {
                    writer.Write(json);
                    writer.Flush();
                }

                stream.Flush(flushToDisk: true);
            }

            // Retain the previous board as .bak before it is replaced. The copy happens
            // first, so a crash here leaves the primary untouched; only the final rename
            // swaps content, and the backup survives it.
            if (File.Exists(path))
            {
                File.Copy(path, path + ".bak", overwrite: true);
            }

            File.Move(tmp, path, overwrite: true);

            // Read-back: a flush that returned success can still have persisted fewer
            // bytes than expected. Verify the file that replaced the board, and restore
            // the backup if it is short instead of leaving a truncated primary.
            var written = new FileInfo(path).Length;
            if (written != expectedBytes)
            {
                var bak = path + ".bak";
                if (File.Exists(bak))
                {
                    File.Copy(bak, path, overwrite: true);
                }

                throw new IOException(
                    $"Board file is {written} bytes after writing {expectedBytes}; " +
                    "the previous version was restored from backup.");
            }
        }
        catch
        {
            try
            {
                File.Delete(tmp);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            throw;
        }
    }

    public static BoardDocument Load(string path) => Load(path, DefaultMaxFileSizeBytes);

    internal static BoardDocument Load(string path, long maxBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var length = new FileInfo(path).Length;
        if (length > maxBytes)
        {
            throw new InvalidDataException(
                $"Board file is {length} bytes; the load limit is {maxBytes} bytes.");
        }

        var json = File.ReadAllText(path, Encoding.UTF8);
        var doc = JsonSerializer.Deserialize<BoardDocument>(json, Options)
                  ?? throw new InvalidDataException("Board file deserialized to null.");

        // Format gate: a file claiming a version this build does not understand is
        // rejected before any of its content is used, so a newer-format board can never
        // be half-parsed into a plausible-looking but wrong lesson. Files without the
        // field predate versioning and read as the current version via its default.
        if (doc.Version < 1 || doc.Version > BoardDocument.FileFormatVersion)
        {
            throw new InvalidDataException(
                $"Board file format version {doc.Version} is not supported " +
                $"(this build reads 1..{BoardDocument.FileFormatVersion}).");
        }

        return doc.Sanitize();
    }

    public static bool TryLoad(string path, out BoardDocument? doc, out string? error) =>
        TryLoad(path, DefaultMaxFileSizeBytes, out doc, out error);

    internal static bool TryLoad(string path, long maxBytes, out BoardDocument? doc,
        out string? error)
    {
        doc = null;
        error = null;
        try
        {
            doc = Load(path, maxBytes);
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException
                                       or UnauthorizedAccessException or NotSupportedException
                                       or ArgumentException or SecurityException)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Reconciles <c>*.ebboard.tmp</c> files left behind by a process that died mid-save.
    /// Called at startup for the autosave directory and when a board directory is opened;
    /// never runs concurrently with a save in the same directory (both are driven from the
    /// UI thread, and the autosave directory is recovered before the first autosave).
    /// <list type="bullet">
    /// <item>Temp with no primary: the crash happened between write and rename. If the
    /// temp parses it is the newest complete write and is promoted; otherwise it is moved
    /// aside as <c>.corrupt</c> — preserved as evidence, never retried.</item>
    /// <item>Temp beside an existing primary: the primary is the authoritative complete
    /// file and the temp is debris from an interrupted overwrite.</item>
    /// </list>
    /// </summary>
    internal static void RecoverStaleTempFiles(string? directory)
    {
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            return;
        }

        foreach (var tmp in Directory.EnumerateFiles(directory, "*.ebboard.tmp"))
        {
            try
            {
                var primary = tmp[..^".tmp".Length];
                if (File.Exists(primary))
                {
                    File.Delete(tmp);
                }
                else if (TryLoad(tmp, out _, out _))
                {
                    File.Move(tmp, primary);
                }
                else
                {
                    File.Move(tmp, tmp + ".corrupt", overwrite: true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Recovery is best-effort: one locked file must not stop the rest or
                // prevent startup. The untouched temp is retried next launch.
            }
        }
    }
}
