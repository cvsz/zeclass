using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EBoard.Tools;

/// <summary>
/// Records the screen to an uncompressed AVI.
///
/// Written as a direct RIFF/AVI muxer rather than through avifil32, for two reasons. avifil32 is
/// a compatibility shim that is not present on every current Windows image and calling it from
/// a modern process needs workarounds; and a file produced on a classroom machine has to be
/// reproducible, which a self-contained muxer guarantees. The cost is file size, so the frame
/// rate is modest.
///
/// Uncompressed BI_RGB is deliberately the simplest thing that works. A compressed stream would
/// need a bundled encoder, which would add tens of megabytes to an install a school may copy
/// over a slow link.
/// </summary>
public sealed class ScreenRecorder : IDisposable
{
    public const double Fps = 10.0;

    private const int MaxWidth = 1920;
    private const int MaxHeight = 1080;

    private readonly List<byte[]> _frames = new();
    private string? _outputPath;
    private int _width;
    private int _height;
    private int _rowBytes;
    private DateTime? _startedUtc;
    private byte[]? _latest;
    private int _latestStride;

    public bool IsRecording => _outputPath is not null;

    public int FrameCount => _frames.Count;

    public int Width => _width;

    public int Height => _height;

    public int RowBytes => _rowBytes;

    public byte[]? LatestFrame => _latest;

    public int LatestStride => _latestStride;

    public long BufferedBytes => (long)_frames.Count * _rowBytes * _height;

    public TimeSpan Elapsed => _startedUtc is null
        ? TimeSpan.Zero
        : DateTime.UtcNow - _startedUtc.Value;

    /// <summary>
    /// Begins capture. Resolution is fixed at start because an AVI stream cannot change size
    /// partway through, so a mid-recording display change is dropped rather than corrupting the
    /// file.
    /// </summary>
    public bool Start(string path, int? width = null, int? height = null)
    {
        if (IsRecording)
        {
            return false;
        }

        try
        {
            var bounds = ScreenCapture.VirtualScreenBounds();
            _width = Math.Clamp(width ?? (int)bounds.Width, 16, MaxWidth);
            _height = Math.Clamp(height ?? (int)bounds.Height, 16, MaxHeight);

            // DIB rows are padded to a four-byte boundary.
            var unpadded = _width * 3;
            _rowBytes = unpadded + ((4 - (unpadded % 4)) % 4);

            _outputPath = path;
            _frames.Clear();
            _startedUtc = DateTime.UtcNow;
            return true;
        }
        catch (Exception ex)
        {
            CrashLog.Write("ScreenRecorder.Start", ex);
            _outputPath = null;
            return false;
        }
    }

    /// <summary>
    /// Captures one frame. A failure returns false without aborting, so a single dropped frame
    /// does not lose a whole lesson.
    /// </summary>
    public bool CaptureFrame()
    {
        if (!IsRecording)
        {
            return false;
        }

        try
        {
            var bounds = ScreenCapture.VirtualScreenBounds();
            var source = ScreenCapture.Grab((int)bounds.X, (int)bounds.Y, _width, _height);
            if (source is null)
            {
                return false;
            }

            var pixels = new byte[_rowBytes * _height];
            var decoded = source.PixelWidth == _width && source.PixelHeight == _height
                ? source
                : Rescale(source, _width, _height);

            decoded.CopyPixels(pixels, _rowBytes, 0);
            _latest = pixels;
            _latestStride = _rowBytes;

            // DIBs are bottom-up, so the frame is flipped on capture rather than on write.
            _frames.Add(FlipVertically(pixels, _rowBytes, _height));
            return true;
        }
        catch (Exception ex)
        {
            CrashLog.Write("ScreenRecorder.CaptureFrame", ex);
            return false;
        }
    }

    /// <summary>
    /// Appends a frame directly, bypassing screen capture. Exposed so the muxer can be tested
    /// without a desktop session, and so a caller that already has frames (a replay, or a
    /// second display) can record without a redundant grab.
    /// </summary>
    /// <param name="bgr">Top-down 24-bit BGR, width times 3 bytes per row, rows unpadded.</param>
    public bool AddFrame(byte[] bgr)
    {
        if (!IsRecording)
        {
            return false;
        }

        if (bgr.Length < _width * 3 * _height)
        {
            return false;
        }

        var padded = new byte[_rowBytes * _height];
        var unpadded = _width * 3;
        for (var y = 0; y < _height; y++)
        {
            Array.Copy(bgr, y * unpadded, padded, y * _rowBytes, unpadded);
        }

        _latest = padded;
        _latestStride = _rowBytes;
        _frames.Add(FlipVertically(padded, _rowBytes, _height));
        return true;
    }

    /// <summary>Finishes the recording and writes the AVI. Returns the path, or null on failure.</summary>
    public string? Stop()
    {
        if (!IsRecording || _outputPath is null)
        {
            return null;
        }

        var path = Path.GetFullPath(_outputPath);
        var frames = _frames.ToArray();
        _outputPath = null;
        _startedUtc = null;
        _frames.Clear();

        if (frames.Length == 0)
        {
            return null;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var width = _width;
            var height = _height;
            var rowBytes = _rowBytes;
            WriteAvi(path, frames, width, height, rowBytes);
            CrashLog.Info($"Screen recording written: {path} ({frames.Length} frames, " +
                          $"{width}x{height})");
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashLog.Write("ScreenRecorder.Stop", ex);
            return null;
        }
    }

    private static BitmapSource Rescale(BitmapSource source, int width, int height)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(source, new Rect(0, 0, width, height));
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Bgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgr24, null, 0);
        converted.Freeze();
        return converted;
    }

    private static byte[] FlipVertically(byte[] source, int rowBytes, int height)
    {
        var flipped = new byte[source.Length];
        for (var y = 0; y < height; y++)
        {
            var from = y * rowBytes;
            var to = (height - 1 - y) * rowBytes;
            Array.Copy(source, from, flipped, to, rowBytes);
        }

        return flipped;
    }

    private static void WriteAvi(string path, byte[][] frames, int width, int height, int rowBytes)
    {
        var frameBytes = rowBytes * height;
        var usPerFrame = (int)(1_000_000 / Fps);

        var tmp = path + ".tmp";
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var w = new BinaryWriter(fs, Encoding.ASCII))
        {
            w.Write(Encoding.ASCII.GetBytes("RIFF"));
            // The size field sits immediately after the FourCC, at offset 4.
            var riffSizeField = fs.Position;
            w.Write(0u);
            w.Write(Encoding.ASCII.GetBytes("AVI "));

            // ---- LIST hdrl ----
            var hdrlStart = fs.Position;
            w.Write(Encoding.ASCII.GetBytes("LIST"));
            w.Write(0u);
            w.Write(Encoding.ASCII.GetBytes("hdrl"));

            w.Write(Encoding.ASCII.GetBytes("avih"));
            w.Write(56u);
            var avih = new byte[56];
            PutI32(avih, 0, usPerFrame);
            PutI32(avih, 4, frameBytes * frames.Length);
            PutI32(avih, 8, 0);
            PutI32(avih, 12, 0x10); // AVIF_HASINDEX
            PutI32(avih, 16, frames.Length);
            PutI32(avih, 20, 0);
            PutI32(avih, 24, 1); // one stream
            PutI32(avih, 28, frameBytes);
            PutI32(avih, 32, width);
            PutI32(avih, 36, height);
            w.Write(avih);

            w.Write(Encoding.ASCII.GetBytes("strh"));
            w.Write(56u);
            var strh = new byte[56];
            Encoding.ASCII.GetBytes("vids").CopyTo(strh, 0);
            Encoding.ASCII.GetBytes("DIB ").CopyTo(strh, 4);
            PutI32(strh, 12, 0);
            PutI16(strh, 16, 0);
            PutI16(strh, 18, 0);
            PutI32(strh, 20, 0);
            PutI32(strh, 24, (int)Fps);
            PutI32(strh, 28, 0);
            PutI32(strh, 32, usPerFrame);
            PutI32(strh, 36, 0);
            PutI32(strh, 40, frames.Length);
            PutI32(strh, 44, frameBytes);
            PutI32(strh, 48, unchecked((int)0xFFFFFFFF));
            w.Write(strh);

            w.Write(Encoding.ASCII.GetBytes("strf"));
            w.Write(40u);
            var strf = new byte[40];
            PutI32(strf, 0, 40);
            PutI32(strf, 4, width);
            PutI32(strf, 8, height);
            PutI16(strf, 12, 1);  // planes
            PutI16(strf, 14, 24); // bits per pixel
            PutI32(strf, 16, 0);  // BI_RGB
            PutI32(strf, 20, frameBytes);
            w.Write(strf);

            PatchSize(w, fs, hdrlStart + 4);

            // ---- LIST movi ----
            var moviStart = fs.Position;
            w.Write(Encoding.ASCII.GetBytes("LIST"));
            w.Write(0u);
            w.Write(Encoding.ASCII.GetBytes("movi"));
            var moviDataStart = fs.Position;

            var index = new List<(int Offset, int Size)>(frames.Length);
            foreach (var frame in frames)
            {
                // Offsets in idx1 are relative to the start of the movi data.
                var offset = (int)(fs.Position - moviDataStart);
                w.Write(Encoding.ASCII.GetBytes("00db"));
                w.Write((uint)frame.Length);
                w.Write(frame);
                if (frame.Length % 2 == 1)
                {
                    w.Write((byte)0); // chunks are word aligned
                }

                index.Add((offset, frame.Length));
            }

            PatchSize(w, fs, moviStart + 4);

            // ---- idx1 ----
            w.Write(Encoding.ASCII.GetBytes("idx1"));
            w.Write((uint)(index.Count * 16));
            foreach (var (offset, size) in index)
            {
                w.Write(Encoding.ASCII.GetBytes("00db"));
                w.Write(0x10u); // AVIIF_KEYFRAME
                w.Write((uint)offset);
                w.Write((uint)size);
            }

            PatchSize(w, fs, riffSizeField);
            w.Flush();
        }

        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>Backpatches a 32-bit chunk size that must be written after the chunk body.</summary>
    private static void PatchSize(BinaryWriter w, FileStream fs, long sizeFieldPosition)
    {
        w.Flush();
        var end = fs.Position;
        fs.Position = sizeFieldPosition;
        w.Write((uint)(end - sizeFieldPosition - 4));
        w.Flush();
        fs.Position = end;
    }

    private static void PutI32(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)value;
        buffer[offset + 1] = (byte)(value >> 8);
        buffer[offset + 2] = (byte)(value >> 16);
        buffer[offset + 3] = (byte)(value >> 24);
    }

    private static void PutI16(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)value;
        buffer[offset + 1] = (byte)(value >> 8);
    }

    public void Dispose() => Stop();
}
