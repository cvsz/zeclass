using System;
using System.Diagnostics;
using System.IO;

namespace zEClass.Tools;

/// <summary>What <see cref="NdiBridge.EnsureRunning"/> decided.</summary>
public enum NdiResult
{
    Started,
    AlreadyRunning,
    Disabled,
    NotFound,
    Failed,
}

/// <summary>A running external process, abstracted so tests never spawn real processes.</summary>
public interface INdiProcess : IDisposable
{
    int Id { get; }
    DateTime StartTime { get; }
    string ExePath { get; }
    bool HasExited { get; }

    /// <summary>Window handle, or zero when the process has no window yet.</summary>
    IntPtr MainWindowHandle { get; }

    void Kill();

    /// <summary>Minimizes the main window; a no-op when there is none.</summary>
    void Minimize();
}

/// <summary>
/// Supervises the vMix Desktop Capture helper (NDI screen feed for streaming the board).
///
/// The helper is a third-party executable that is not bundled: the bridge looks for it at
/// <c>ZECLASS_NDI_CAPTURE</c> when set, otherwise at its installed location, and does nothing
/// at all when it is absent. When present it is started minimized with its own folder as the
/// working directory (its capture DLL lives beside it), tracked by PID, and reaped when the
/// board closes.
///
/// The stop path identifies the process by PID <em>and</em> start time <em>and</em> executable
/// path before touching it. PIDs get reused by Windows, so PID alone would eventually kill an
/// innocent process; the triple check is the whole point of this class.
/// </summary>
public sealed class NdiBridge : IDisposable
{
    public const string DefaultExePath = @"D:\eclass\vMixDesktopCaptureNDI\vMixDesktopCapture.exe";
    public const string ExePathVariable = "ZECLASS_NDI_CAPTURE";

    /// <summary>Optional SHA-256 pin for the helper binary. When set, the helper starts
    /// only when its file hashes to this value (hex, case-insensitive).</summary>
    public const string ExeHashVariable = "ZECLASS_NDI_SHA256";

    private readonly Func<ProcessStartInfo, INdiProcess?> _starter;
    private readonly Func<string, bool> _fileExists;
    private INdiProcess? _tracked;
    private DateTime _trackedStart;
    private bool _disposed;

    public NdiBridge(string exePath, bool enabled,
        Func<ProcessStartInfo, INdiProcess?>? starter = null,
        Func<string, bool>? fileExists = null)
    {
        ExePath = exePath;
        Enabled = enabled;
        _starter = starter ?? (info => StartSystem(info));
        _fileExists = fileExists ?? File.Exists;
    }

    /// <summary>Resolves the helper location: explicit override first, installed path second.</summary>
    public static string ResolveExePath() =>
        Environment.GetEnvironmentVariable(ExePathVariable) is { Length: > 0 } custom
            ? custom
            : DefaultExePath;

    public string ExePath { get; }

    public bool Enabled { get; set; }

    public bool ExeFound => _fileExists(ExePath);

    public string? LastError { get; private set; }

    /// <summary>
    /// How long to wait for the helper's window before giving up on minimizing it.
    /// Internal so tests can shrink it; production uses the five-second default.
    /// </summary>
    internal int SettleTimeoutMs { get; set; } = 5000;

    internal int SettlePollMs { get; set; } = 250;

    /// <summary>True while the process this bridge started is still alive and still itself.</summary>
    public bool IsRunning
    {
        get
        {
            if (_tracked is null)
            {
                return false;
            }

            if (!Matches(_tracked))
            {
                // Died, or the PID was reused by something else: forget it either way. Never
                // adopt a stranger as our own.
                _tracked.Dispose();
                _tracked = null;
                return false;
            }

            return true;
        }
    }

    /// <summary>
    /// Starts the helper minimized when it should be running and isn't. Safe to call often:
    /// it starts at most one instance and reports what it decided.
    /// </summary>
    public NdiResult EnsureRunning()
    {
        if (!Enabled)
        {
            return NdiResult.Disabled;
        }

        if (!ExeFound)
        {
            LastError = $"NDI helper not found at {ExePath}.";
            return NdiResult.NotFound;
        }

        string canonical;
        try
        {
            canonical = Path.GetFullPath(ExePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or ArgumentException or NotSupportedException)
        {
            LastError = $"NDI helper path is not usable: {ex.Message}";
            CrashLog.Info($"NdiBridge: {LastError} ({ExePath})");
            return NdiResult.Failed;
        }

        if (!_fileExists(canonical))
        {
            LastError = $"NDI helper not found at {canonical}.";
            return NdiResult.NotFound;
        }

        if (!VerifyHashPin(canonical))
        {
            LastError = "NDI helper hash does not match ZECLASS_NDI_SHA256; refusing to start it.";
            CrashLog.Info($"NdiBridge: {LastError} ({canonical})");
            return NdiResult.Failed;
        }

        if (IsRunning)
        {
            return NdiResult.AlreadyRunning;
        }

        try
        {
            // The working directory matters: the capture DLL sits beside the exe, and without
            // it the helper starts and dies within a second. The canonical path (not the raw
            // environment value) is what gets executed.
            var folder = Path.GetDirectoryName(canonical) ?? string.Empty;
            var info = new ProcessStartInfo
            {
                FileName = canonical,
                WorkingDirectory = folder,
                WindowStyle = ProcessWindowStyle.Minimized,
                UseShellExecute = true,
            };

            var process = _starter(info);
            if (process is null || process.HasExited)
            {
                process?.Dispose();
                LastError = "NDI helper exited immediately after starting.";
                CrashLog.Info($"NdiBridge: {LastError} ({ExePath})");
                return NdiResult.Failed;
            }

            _tracked = process;
            _trackedStart = process.StartTime;
            LastError = null;
            CrashLog.Info($"NdiBridge: started {ExePath} (pid {process.Id}), minimized.");
            SettleMinimized(process, SettleTimeoutMs, SettlePollMs);
            return NdiResult.Started;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            LastError = $"Could not start the NDI helper: {ex.Message}";
            CrashLog.Info($"NdiBridge: {LastError}");
            return NdiResult.Failed;
        }
    }

    /// <summary>
    /// Stops the helper, but only the exact instance this bridge started.
    ///
    /// Returns true when nothing tracked remains afterwards: stopped by us, or already gone
    /// (a kill that races an exit still achieves the goal, so it reports success rather than
    /// surfacing a Win32Exception for a process that no longer exists). Returns false when
    /// there was nothing of ours to stop, the identity no longer matches, or a live matching
    /// process survived the kill.
    /// </summary>
    public bool Stop()
    {
        var tracked = _tracked;
        _tracked = null;
        if (tracked is null)
        {
            return false;
        }

        try
        {
            if (!Matches(tracked))
            {
                CrashLog.Info("NdiBridge: tracked process no longer matches; not stopping it.");
                return false;
            }

            try
            {
                tracked.Kill();
            }
            catch (Exception ex) when (ex is InvalidOperationException
                or System.ComponentModel.Win32Exception or NotSupportedException)
            {
                // The kill raced the exit, or access was denied. Either way the only question
                // that matters is whether anything of ours is still alive.
                CrashLog.Info($"NdiBridge: kill did not complete cleanly. {ex.Message}");
            }

            if (StillAlive(tracked))
            {
                LastError = "The NDI helper is still running after the stop request.";
                CrashLog.Info($"NdiBridge: {LastError}");
                return false;
            }

            CrashLog.Info($"NdiBridge: stopped {ExePath} (pid {tracked.Id}).");
            return true;
        }
        finally
        {
            tracked.Dispose();
        }
    }

    private static bool StillAlive(INdiProcess process)
    {
        try
        {
            return !process.HasExited;
        }
        catch (Exception ex) when (ex is InvalidOperationException
            or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            // Cannot even ask: treat as alive so Stop reports failure rather than claiming
            // success it cannot verify.
            CrashLog.Info($"NdiBridge: could not confirm the helper exited. {ex.Message}");
            return true;
        }
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

    /// <summary>
    /// The minimized start request is only a request: helpers that show their own window on top
    /// ignore it. So once the main window exists, minimize it explicitly. The wait is bounded
    /// so a slow helper cannot hang startup, and a missing window is not a failure — the
    /// process itself is what matters.
    ///
    /// Runs on the calling thread, which is why the window calls EnsureRunning from a
    /// background task rather than the UI thread.
    /// </summary>
    private static void SettleMinimized(INdiProcess process, int timeoutMs, int pollMs)
    {
        try
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                if (process.HasExited)
                {
                    return;
                }

                if (process.MainWindowHandle != IntPtr.Zero)
                {
                    break;
                }

                System.Threading.Thread.Sleep(pollMs);
            }

            process.Minimize();
        }
        catch (Exception ex) when (ex is InvalidOperationException
            or System.ComponentModel.Win32Exception or System.Threading.ThreadInterruptedException)
        {
            CrashLog.Info($"NdiBridge: could not minimize the helper window. {ex.Message}");
        }
    }

    private static bool VerifyHashPin(string canonicalPath)
    {
        var pin = Environment.GetEnvironmentVariable(ExeHashVariable);
        if (string.IsNullOrWhiteSpace(pin))
        {
            return true;
        }

        try
        {
            using var stream = File.OpenRead(canonicalPath);
            var hash = System.Security.Cryptography.SHA256.HashData(stream);
            var actual = Convert.ToHexString(hash);
            return actual.Equals(pin.Trim(), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashLog.Info($"NdiBridge: could not hash the helper for pinning. {ex.Message}");
            return false;
        }
    }

    private bool Matches(INdiProcess process)
    {
        try
        {
            // All three must hold. PID alone is not identity: Windows reuses PIDs, and killing
            // whatever happens to hold our old number would be exactly the failure this class
            // exists to prevent. The path compares in canonical form because the OS reports
            // the full path while the configuration may be relative.
            string expected;
            try
            {
                expected = Path.GetFullPath(ExePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                           or ArgumentException or NotSupportedException)
            {
                expected = ExePath;
            }

            return !process.HasExited &&
                process.StartTime == _trackedStart &&
                string.Equals(process.ExePath, expected, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is InvalidOperationException
            or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return false;
        }
    }

    private static INdiProcess? StartSystem(ProcessStartInfo info)
    {
        var process = Process.Start(info);
        return process is null ? null : new SystemNdiProcess(process);
    }

    private sealed class SystemNdiProcess : INdiProcess
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SwMinimize = 6;

        private readonly Process _process;

        public SystemNdiProcess(Process process) => _process = process;

        public int Id => _process.Id;

        public DateTime StartTime
        {
            get
            {
                try
                {
                    return _process.StartTime;
                }
                catch (Exception ex) when (ex is InvalidOperationException
                    or System.ComponentModel.Win32Exception or NotSupportedException)
                {
                    return DateTime.MinValue;
                }
            }
        }

        public string ExePath
        {
            get
            {
                try
                {
                    return _process.MainModule?.FileName ?? string.Empty;
                }
                catch (Exception ex) when (ex is InvalidOperationException
                    or System.ComponentModel.Win32Exception or NotSupportedException)
                {
                    return string.Empty;
                }
            }
        }

        public bool HasExited
        {
            get
            {
                try
                {
                    return _process.HasExited;
                }
                catch (Exception ex) when (ex is InvalidOperationException
                    or System.ComponentModel.Win32Exception or NotSupportedException)
                {
                    return true;
                }
            }
        }

        public IntPtr MainWindowHandle
        {
            get
            {
                try
                {
                    return _process.MainWindowHandle;
                }
                catch (Exception ex) when (ex is InvalidOperationException
                    or System.ComponentModel.Win32Exception or NotSupportedException)
                {
                    return IntPtr.Zero;
                }
            }
        }

        public void Minimize()
        {
            var handle = MainWindowHandle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            try
            {
                ShowWindow(handle, SwMinimize);
            }
            catch (Exception ex) when (ex is InvalidOperationException
                or System.ComponentModel.Win32Exception)
            {
                CrashLog.Info($"NdiBridge: minimize failed. {ex.Message}");
            }
        }

        public void Kill()
        {
            _process.Kill();
            _process.WaitForExit(3000);
        }

        public void Dispose() => _process.Dispose();
    }
}
