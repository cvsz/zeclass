using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace zEClass.Tools;

/// <summary>
/// Records the screen to an uncompressed AVI, streamed to disk.
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
///
/// Frames stream to disk as they are captured: the header is written with placeholder counts,
/// each frame appends one chunk, and <see cref="Stop"/> backpatches the counts, appends the
/// index, and renames the file into place. Memory stays flat no matter how long the recording
/// runs; only the small frame index and the latest frame for preview are held. Duration, file
/// size, frame rate, and free disk space are all bounded by <see cref="RecordingLimits"/>.
/// </summary>
public sealed class ScreenRecorder : IDisposable
{
    public const double Fps = 10.0;

    /// <summary>Resource ceilings for recording. Defaults suit a classroom machine; the byte
    /// cap matches the AVI 32-bit chunk ceiling, which binds before the duration cap at full
    /// resolution. Tests pass smaller values through <see cref="Limits"/>.</summary>
    public sealed class RecordingLimits
    {
        public static RecordingLimits Default { get; } = new();

        public TimeSpan MaxRecordingDuration { get; init; } = TimeSpan.FromMinutes(10);

        public long MaxRecordingBytes { get; init; } = 4L * 1024 * 1024 * 1024;

        public long MinimumFreeDiskSpace { get; init; } = 1L * 1024 * 1024 * 1024;

        public int MaxFrameRate { get; init; } = 10;

        public int MaxWidth { get; init; } = 1920;

        public int MaxHeight { get; init; } = 1080;
    }

    /// <summary>Limits for the recording being started. Assign before <see cref="Start"/>.</summary>
    public RecordingLimits Limits { get; set; } = RecordingLimits.Default;

    private sealed class Session : IDisposable
    {
        public Session(FileStream stream, BinaryWriter writer, string tmpPath, string finalPath,
            long moviDataStart, long avihRatePos, long avihFramesPos, long strhFramesPos,
            long hdrlSizePos, long moviSizePos, long riffSizePos)
        {
            Stream = stream;
            Writer = writer;
            TmpPath = tmpPath;
            FinalPath = finalPath;
            MoviDataStart = moviDataStart;
            AvihRatePos = avihRatePos;
            AvihFramesPos = avihFramesPos;
            StrhFramesPos = strhFramesPos;
            HdrlSizePos = hdrlSizePos;
            MoviSizePos = moviSizePos;
            RiffSizePos = riffSizePos;
            Index = [];
        }

        public FileStream Stream { get; }

        public BinaryWriter Writer { get; }

        public string TmpPath { get; }

        public string FinalPath { get; }

        public long MoviDataStart { get; }

        public long AvihRatePos { get; }

        public long AvihFramesPos { get; }

        public long StrhFramesPos { get; }

        public long HdrlSizePos { get; }

        public long MoviSizePos { get; }

        public long RiffSizePos { get; }

        public List<(int Offset, int Size)> Index { get; }

        public long AppendedBytes { get; set; }

        public void Dispose()
        {
            Writer.Dispose();
            Stream.Dispose();
        }
    }

    private Session? _session;
    private int _width;
    private int _height;
    private int _rowBytes;
    private int _frameBytes;
    private DateTime? _startedUtc;
    private DateTime? _lastFrameUtc;
    private byte[]? _latest;
    private int _latestStride;
    private string? _lastError;
    private bool _limitReached;
    private int _droppedFrames;
    private bool _disposed;

    public bool IsRecording => _session is not null;

    public int FrameCount => _session?.Index.Count ?? 0;

    public int Width => _width;

    public int Height => _height;

    public int RowBytes => _rowBytes;

    public byte[]? LatestFrame => _latest;

    public int LatestStride => _latestStride;

    /// <summary>Bytes written to the recording file so far.</summary>
    public long BufferedBytes => _session is null ? 0 : _session.MoviDataStart + _session.AppendedBytes;

    /// <summary>Why the last operation failed or a limit tripped. Cleared on <see cref="Start"/>.</summary>
    public string? LastError => _lastError;

    /// <summary>True once a duration, size, rate-budget, or disk limit has tripped. The
    /// recording keeps its frames; call <see cref="Stop"/> to finalize them.</summary>
    public bool LimitReached => _limitReached;

    /// <summary>Frames dropped by the frame-rate throttle.</summary>
    public int DroppedFrames => _droppedFrames;

    public TimeSpan Elapsed => _startedUtc is null
        ? TimeSpan.Zero
        : DateTime.UtcNow - _startedUtc.Value;

    /// <summary>
    /// Begins capture. Resolution is fixed at start because an AVI stream cannot change size
    /// partway through, so a mid-recording display change is dropped rather than corrupting the
    /// file. Refuses to start when the target drive cannot cover the minimum free space.
    /// </summary>
    public bool Start(string path, int? width = null, int? height = null)
    {
        if (IsRecording)
        {
            return false;
        }

        _lastError = null;
        _limitReached = false;
        _droppedFrames = 0;
        _latest = null;

        if (string.IsNullOrWhiteSpace(path))
        {
            _lastError = "Recording path is empty.";
            return false;
        }

        var limits = Limits;
        try
        {
            var bounds = ScreenCapture.VirtualScreenBounds();
            _width = Math.Clamp(width ?? (int)bounds.Width, 16, limits.MaxWidth);
            _height = Math.Clamp(height ?? (int)bounds.Height, 16, limits.MaxHeight);

            // DIB rows are padded to a four-byte boundary.
            var unpadded = _width * 3;
            _rowBytes = unpadded + ((4 - (unpadded % 4)) % 4);
            _frameBytes = _rowBytes * _height;

            var finalPath = Path.GetFullPath(path);
            if (!HasFreeSpace(finalPath, limits.MinimumFreeDiskSpace))
            {
                _lastError =
                    $"Not enough free disk space to start recording to {finalPath}.";
                return false;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
            var session = BeginSession(finalPath, _width, _height, _frameBytes);
            if (session is null)
            {
                _lastError = $"Could not create the recording file at {finalPath}.";
                return false;
            }

            _session = session;
            _startedUtc = DateTime.UtcNow;
            _lastFrameUtc = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or ArgumentException or NotSupportedException)
        {
            CrashLog.Write("ScreenRecorder.Start", ex);
            _lastError = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Captures one frame. A failure returns false without aborting, so a single dropped frame
    /// does not lose a whole lesson. Frames arriving faster than the rate limit are dropped and
    /// counted; a tripped duration, size, or disk limit sets <see cref="LimitReached"/> and
    /// rejects further frames until <see cref="Stop"/> finalizes what was captured.
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
            return AppendFrame(pixels);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or ArgumentException or InvalidOperationException)
        {
            CrashLog.Write("ScreenRecorder.CaptureFrame", ex);
            _lastError = ex.Message;
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

        return AppendFrame(padded);
    }

    private bool AppendFrame(byte[] topDown)
    {
        var session = _session;
        if (session is null)
        {
            return false;
        }

        var limits = Limits;
        var now = DateTime.UtcNow;

        if (_startedUtc is not null && now - _startedUtc.Value >= limits.MaxRecordingDuration)
        {
            _limitReached = true;
            _lastError = "Recording duration limit reached; stopping with what was captured.";
            return false;
        }

        if (limits.MaxFrameRate > 0 && _lastFrameUtc is not null &&
            now - _lastFrameUtc.Value < TimeSpan.FromSeconds(1.0 / limits.MaxFrameRate))
        {
            _droppedFrames++;
            return true;
        }

        // Projected file size: header so far, buffered chunks, this chunk, its future index
        // entry, and slack for the trailing index. All tracked counters, no overflow: the
        // estimate stays under real disk sizes, and the first clause covers a cap the header
        // already exceeds (or a non-positive cap) before the subtraction runs.
        var need = (long)_frameBytes + 8 + 1 + 16 + 64;
        var estimate = session.MoviDataStart + session.AppendedBytes;
        if (estimate >= limits.MaxRecordingBytes || need > limits.MaxRecordingBytes - estimate)
        {
            _limitReached = true;
            _lastError = "Recording size limit reached; stopping with what was captured.";
            return false;
        }

        // AVI offsets are 32-bit: stop before a chunk could land past the ceiling.
        if (session.AppendedBytes > int.MaxValue - _frameBytes - 64)
        {
            _limitReached = true;
            _lastError = "Recording exceeded the 32-bit AVI offset ceiling; stopping.";
            return false;
        }

        if (!HasFreeSpace(session.FinalPath, limits.MinimumFreeDiskSpace + _frameBytes))
        {
            _limitReached = true;
            _lastError = "Free disk space ran out; stopping with what was captured.";
            return false;
        }

        try
        {
            // DIBs are bottom-up, so the frame is flipped on capture rather than on write.
            var flipped = FlipVertically(topDown, _rowBytes, _height);
            var offset = session.MoviDataStart + session.AppendedBytes;
            var writer = session.Writer;
            writer.Write(Encoding.ASCII.GetBytes("00db"));
            writer.Write((uint)flipped.Length);
            writer.Write(flipped);
            if (flipped.Length % 2 == 1)
            {
                writer.Write((byte)0); // chunks are word aligned
            }

            // Offsets in idx1 are relative to the start of the movi data. The byte cap
            // keeps the file near the 32-bit AVI ceiling, so the narrowing is safe.
            session.Index.Add(((int)(offset - session.MoviDataStart), flipped.Length));
            session.AppendedBytes += flipped.Length + 8 + (flipped.Length % 2);
            _lastFrameUtc = now;
            _latest = topDown;
            _latestStride = _rowBytes;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or ObjectDisposedException)
        {
            // The disk gave up mid-frame. Keep the session so Stop can still finalize the
            // valid prefix already on disk.
            CrashLog.Write("ScreenRecorder.AppendFrame", ex);
            _lastError = ex.Message;
            return false;
        }
    }

    private static bool HasFreeSpace(string path, long requiredBytes)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrEmpty(root))
            {
                return true;
            }

            return new DriveInfo(root).AvailableFreeSpace >= requiredBytes;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or ArgumentException or NotSupportedException)
        {
            // Fail open: an unreadable drive estimate must not kill a lesson. The actual
            // file creation and writes still fail loudly if the disk is really gone.
            return true;
        }
    }

    /// <summary>
    /// Finishes the recording and finalizes the AVI. Returns the path, or null when nothing
    /// was captured. A partial recording after a tripped limit still finalizes: the file is
    /// valid, and <see cref="LastError"/> says why it stopped early.
    /// </summary>
    public string? Stop()
    {
        var session = _session;
        _session = null;
        _startedUtc = null;
        if (session is null)
        {
            return null;
        }

        try
        {
            if (session.Index.Count == 0)
            {
                session.Dispose();
                try
                {
                    File.Delete(session.TmpPath);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }

                return null;
            }

            FinishSession(session);
            CrashLog.Info($"Screen recording written: {session.FinalPath} " +
                          $"({session.Index.Count} frames, {_width}x{_height})");
            var final = session.FinalPath;
            session.Dispose();
            File.Move(session.TmpPath, final, overwrite: true);
            return final;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashLog.Write("ScreenRecorder.Stop", ex);
            _lastError = ex.Message;
            try
            {
                session.Dispose();
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (ObjectDisposedException)
            {
            }

            try
            {
                File.Delete(session.TmpPath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            return null;
        }
        finally
        {
            session.Dispose();
        }
    }

    private void FinishSession(Session session)
    {
        var writer = session.Writer;
        var stream = session.Stream;
        var frameBytes = _frameBytes;
        var count = session.Index.Count;

        PatchU32(writer, stream, session.AvihRatePos, unchecked((int)((uint)frameBytes * (uint)count)));
        PatchU32(writer, stream, session.AvihFramesPos, count);
        PatchU32(writer, stream, session.StrhFramesPos, count);
        PatchSize(writer, stream, session.HdrlSizePos);
        PatchSize(writer, stream, session.MoviSizePos);

        writer.Write(Encoding.ASCII.GetBytes("idx1"));
        writer.Write((uint)(count * 16));
        foreach (var (offset, size) in session.Index)
        {
            writer.Write(Encoding.ASCII.GetBytes("00db"));
            writer.Write(0x10u); // AVIIF_KEYFRAME
            writer.Write((uint)offset);
            writer.Write((uint)size);
        }

        PatchSize(writer, stream, session.RiffSizePos);
        writer.Flush();
        stream.Flush(flushToDisk: true);
    }

    private static Session? BeginSession(string finalPath, int width, int height, int frameBytes)
    {
        var usPerFrame = (int)(1_000_000 / Fps);
        var tmp = finalPath + ".tmp";
        FileStream? stream = null;
        BinaryWriter? writer = null;
        try
        {
            stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None);
            writer = new BinaryWriter(stream, Encoding.ASCII);

            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            var riffSizePos = stream.Position;
            writer.Write(0u);
            writer.Write(Encoding.ASCII.GetBytes("AVI "));

            // ---- LIST hdrl ----
            var hdrlStart = stream.Position;
            writer.Write(Encoding.ASCII.GetBytes("LIST"));
            var hdrlSizePos = stream.Position;
            writer.Write(0u);
            writer.Write(Encoding.ASCII.GetBytes("hdrl"));

            writer.Write(Encoding.ASCII.GetBytes("avih"));
            writer.Write(56u);
            var avihData = stream.Position;
            var avih = new byte[56];
            PutI32(avih, 0, usPerFrame);
            PutI32(avih, 4, 0); // patched with the real byte rate on finish
            PutI32(avih, 8, 0);
            PutI32(avih, 12, 0x10); // AVIF_HASINDEX
            PutI32(avih, 16, 0); // patched with the frame count on finish
            PutI32(avih, 20, 0);
            PutI32(avih, 24, 1); // one stream
            PutI32(avih, 28, frameBytes);
            PutI32(avih, 32, width);
            PutI32(avih, 36, height);
            writer.Write(avih);

            writer.Write(Encoding.ASCII.GetBytes("strh"));
            writer.Write(56u);
            var strhData = stream.Position;
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
            PutI32(strh, 40, 0); // patched with the frame count on finish
            PutI32(strh, 44, frameBytes);
            PutI32(strh, 48, unchecked((int)0xFFFFFFFF));
            writer.Write(strh);

            writer.Write(Encoding.ASCII.GetBytes("strf"));
            writer.Write(40u);
            var strf = new byte[40];
            PutI32(strf, 0, 40);
            PutI32(strf, 4, width);
            PutI32(strf, 8, height);
            PutI16(strf, 12, 1);  // planes
            PutI16(strf, 14, 24); // bits per pixel
            PutI32(strf, 16, 0);  // BI_RGB
            PutI32(strf, 20, frameBytes);
            writer.Write(strf);

            // ---- LIST movi ----
            var moviStart = stream.Position;
            writer.Write(Encoding.ASCII.GetBytes("LIST"));
            var moviSizePos = stream.Position;
            writer.Write(0u);
            writer.Write(Encoding.ASCII.GetBytes("movi"));
            writer.Flush();

            return new Session(stream, writer, tmp, finalPath, stream.Position,
                avihData + 4, avihData + 16, strhData + 40,
                hdrlSizePos, moviSizePos, riffSizePos);
        }
        catch
        {
            // Never leave a half-written header behind a failed start.
            try
            {
                writer?.Dispose();
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (ObjectDisposedException)
            {
            }

            try
            {
                stream?.Dispose();
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (ObjectDisposedException)
            {
            }

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

    private static void PatchU32(BinaryWriter w, FileStream fs, long position, int value)
    {
        w.Flush();
        var end = fs.Position;
        fs.Position = position;
        w.Write(value);
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

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
    }
}
