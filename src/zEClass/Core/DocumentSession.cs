using System;
using System.IO;

namespace zEClass.Core;

/// <summary>
/// Dirty-state and autosave bookkeeping for one open board, kept UI-independent so it can
/// be unit-tested without a WPF dispatcher.
///
/// Every board mutation funnels through <c>InkSurface.BoardChanged</c>, which the window maps
/// to <see cref="NotifyModified"/>. Saves and autosaves report back through
/// <see cref="NotifySaved"/> and <see cref="NotifyAutosaved"/>, so the window always knows
/// whether unsaved work exists (<see cref="IsDirty"/>) and whether a background snapshot is
/// stale before it overwrites a newer autosave.
/// </summary>
public sealed class DocumentSession
{
    /// <summary>How many previous autosave generations to retain beside the live one.</summary>
    public const int AutosaveGenerations = 2;

    // Mutations arrive on the UI thread while autosave completions land on workers:
    // without this, 64-bit revision counters can tear on x86 and updates can vanish.
    private readonly object _gate = new();
    private long _currentRevision;
    private long _savedRevision;
    private long _autosavedRevision;
    private DateTime? _lastAutosaveUtc;

    public long CurrentRevision
    {
        get { lock (_gate) { return _currentRevision; } }
    }

    public long SavedRevision
    {
        get { lock (_gate) { return _savedRevision; } }
    }

    public long AutosavedRevision
    {
        get { lock (_gate) { return _autosavedRevision; } }
    }

    public DateTime? LastAutosaveUtc
    {
        get { lock (_gate) { return _lastAutosaveUtc; } }
    }

    public bool IsDirty => CurrentRevision != SavedRevision;

    public bool HasUnsavedAutosaveWork => CurrentRevision != AutosavedRevision;

    public void NotifyModified()
    {
        lock (_gate)
        {
            _currentRevision++;
        }
    }

    public void NotifySaved()
    {
        lock (_gate)
        {
            _savedRevision = _currentRevision;
        }
    }

    public void NotifyAutosaved(long revision, DateTime utcNow)
    {
        // A stale background snapshot must never move the watermark backwards: only the
        // newest completed write counts.
        lock (_gate)
        {
            if (revision > _autosavedRevision)
            {
                _autosavedRevision = revision;
                _lastAutosaveUtc = utcNow;
            }
        }
    }

    /// <summary>True when a background snapshot for <paramref name="revision"/> is still worth
    /// writing: newer work has not already been autosaved while it was serializing.</summary>
    public bool ShouldWriteAutosave(long revision)
    {
        lock (_gate)
        {
            return revision > _autosavedRevision;
        }
    }

    /// <summary>
    /// True when an autosave tick should produce a snapshot: enabled, dirty, not already
    /// autosaved at this revision, and the interval has elapsed since the last autosave.
    /// </summary>
    public bool ShouldAutosave(bool enabled, int intervalSeconds, DateTime utcNow)
    {
        lock (_gate)
        {
            // Enabled, dirty (current differs from saved), and not already autosaved at
            // this revision; then the interval must have elapsed since the last autosave.
            if (!enabled || _currentRevision == _savedRevision ||
                _currentRevision == _autosavedRevision)
            {
                return false;
            }

            if (_lastAutosaveUtc is null)
            {
                return true;
            }

            return utcNow - _lastAutosaveUtc.Value >= TimeSpan.FromSeconds(Math.Max(1, intervalSeconds));
        }
    }

    /// <summary>Called on New/Open/loaded-board: the fresh document starts clean.</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _currentRevision = 0;
            _savedRevision = 0;
            _autosavedRevision = 0;
            _lastAutosaveUtc = null;
        }
    }

    /// <summary>
    /// True when an autosave file is a recovery candidate: it exists and was written after
    /// the last save of the board (or the board was never saved).
    /// </summary>
    public static bool NeedsRecovery(DateTime? savedWriteUtc, DateTime? autosaveWriteUtc) =>
        autosaveWriteUtc.HasValue &&
        (!savedWriteUtc.HasValue || autosaveWriteUtc.Value > savedWriteUtc.Value);

    /// <summary>
    /// Makes room for a new autosave while keeping growth bounded: call before writing the
    /// new live file. Shifts live to .1, .1 to .2, and so on, dropping the oldest generation.
    /// Missing files are skipped; failures leave existing files alone.
    /// </summary>
    public static void RotateAutosaves(string livePath, int generations = AutosaveGenerations)
    {
        if (generations < 1 || string.IsNullOrWhiteSpace(livePath))
        {
            return;
        }

        try
        {
            for (var n = generations - 1; n >= 1; n--)
            {
                var src = n == 1 ? livePath : livePath + "." + (n - 1);
                var dst = livePath + "." + n;
                if (!File.Exists(src))
                {
                    continue;
                }

                if (File.Exists(dst))
                {
                    File.Delete(dst);
                }

                File.Move(src, dst, overwrite: false);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or ArgumentException or NotSupportedException)
        {
            CrashLog.Info($"Autosave rotation skipped: {ex.Message}");
        }
    }
}
