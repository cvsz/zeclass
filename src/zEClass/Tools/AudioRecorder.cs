using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace zEClass.Tools;

/// <summary>
/// Records classroom audio to an uncompressed WAV file.
///
/// Through MCI (`winmm.dll`) rather than a bundled audio library, for the same reason the screen
/// recorder avoids avifil32: MCI has shipped in-box with every Windows since 3.1, so there is no
/// codec to install, no NuGet native payload, and nothing for a locked-down school image to
/// refuse. The cost is the same as the screen recorder's: size. 16-bit stereo at 44.1 kHz is
/// about 10 MB per minute, and the UI says so when recording starts.
///
/// Fail-soft throughout. A missing microphone, a device claimed by another app, or a denied
/// save all surface as a status message, never as an exception in the teacher's face.
/// </summary>
public sealed class AudioRecorder : IDisposable
{
    /// <summary>MCI command sender, injectable so tests never touch real audio hardware.</summary>
    public delegate uint MciCommand(string command, StringBuilder? response, int responseLength,
        IntPtr callback);

    [DllImport("winmm.dll", CharSet = CharSet.Auto)]
    private static extern uint mciSendStringNative(string command, StringBuilder? returnValue,
        int returnLength, IntPtr callback);

    private readonly MciCommand _mci;
    private readonly string _alias;
    private string? _outputPath;
    private DateTime? _startedUtc;

    public AudioRecorder()
        : this((command, response, length, callback) =>
            mciSendStringNative(command, response, length, callback))
    {
    }

    internal AudioRecorder(MciCommand mci)
    {
        _mci = mci;
        // Unique per instance: two recorders must never share an MCI alias, or stopping one
        // would close the other's device out from under it.
        _alias = "zec_audio_" + Guid.NewGuid().ToString("N");
    }

    public bool IsRecording => _outputPath is not null;

    public TimeSpan Elapsed => _startedUtc is null
        ? TimeSpan.Zero
        : DateTime.UtcNow - _startedUtc.Value;

    /// <summary>MCI error code from the last failed command, or 0 when the last command worked.</summary>
    public uint LastErrorCode { get; private set; }

    /// <summary>Begins recording. Returns false when the audio device cannot be opened.</summary>
    public bool Start(string path)
    {
        if (IsRecording)
        {
            return false;
        }

        try
        {
            var folder = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }

            // Quality is set before recording starts: MCI waveaudio defaults to 8-bit mono at
            // 11 kHz, which is fine for a telephone and miserable for a classroom.
            if (!Send($"open new Type waveaudio Alias {_alias}") ||
                !Send($"set {_alias} bitspersample 16") ||
                !Send($"set {_alias} samplespersec 44100") ||
                !Send($"set {_alias} channels 2") ||
                !Send($"record {_alias}"))
            {
                // A half-opened device must not be left behind for the next Start to trip over.
                // The cleanup close runs after, so the original failure is stashed first:
                // reporting "close succeeded" when the open failed would be a lie.
                var failure = LastErrorCode;
                Send($"close {_alias}");
                LastErrorCode = failure;
                return false;
            }

            _outputPath = path;
            _startedUtc = DateTime.UtcNow;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or ArgumentException or NotSupportedException)
        {
            CrashLog.Write("AudioRecorder.Start", ex);
            return false;
        }
    }

    /// <summary>
    /// Stops recording and writes the WAV file. Returns the path, or null when nothing was
    /// recording or the save failed. A path containing a double quote is refused: MCI has
    /// no quoting escape, so it cannot be passed safely — the device is still closed.
    /// </summary>
    public string? Stop()
    {
        if (!IsRecording)
        {
            return null;
        }

        var path = _outputPath;
        _outputPath = null;
        _startedUtc = null;

        try
        {
            Send($"stop {_alias}", $"stop {_alias}");
            if (path is not null && !path.Contains('"') &&
                Send($"save {_alias} \"{path}\"", $"save {_alias} <path>"))
            {
                Send($"close {_alias}", $"close {_alias}");
                return path;
            }

            if (path is not null && path.Contains('"'))
            {
                CrashLog.Info("AudioRecorder: save path contains a quote and was refused.");
            }

            Send($"close {_alias}", $"close {_alias}");
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashLog.Write("AudioRecorder.Stop", ex);
            return null;
        }
    }

    public void Dispose() => Stop();

    private bool Send(string command, string? logLabel = null)
    {
        var code = _mci(command, null, 0, IntPtr.Zero);
        LastErrorCode = code;
        if (code != 0)
        {
            // Never log the full command: the save form carries the destination path.
            CrashLog.Info($"AudioRecorder MCI error {code} on: {logLabel ?? command}");
        }

        return code == 0;
    }
}
