using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace EBoard;

/// <summary>
/// Minimal append-only log for fatal problems. Never records ink, credentials, or
/// network data - only exception type, message, and stack.
/// </summary>
public static class CrashLog
{
    /// <summary>
    /// Maximum log size before rotation. A classroom machine can run for weeks between
    /// maintenance visits, and an unbounded log would eventually fill the disk.
    /// </summary>
    public const long MaxBytes = 2 * 1024 * 1024;

    private const int KeepRotations = 3;

    private static readonly object Gate = new();
    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EBoard", "logs");

    public static string LogPath => Path.Combine(LogDirectory, "eboard.log");

    public static void Write(string source, Exception ex)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(LogDirectory);
                var sb = new StringBuilder()
                    .Append(DateTime.UtcNow.ToString("O"))
                    .Append(" [").Append(source).Append("] ")
                    .Append(Describe(ex))
                    .AppendLine();
                sb.AppendLine(ex.StackTrace);
                var inner = ex.InnerException;
                var depth = 0;
                while (inner is not null && depth < 8)
                {
                    sb.AppendLine("--- inner ---");
                    sb.AppendLine(inner.StackTrace);
                    inner = inner.InnerException;
                    depth++;
                }

                File.AppendAllText(LogPath, sb.ToString(), Encoding.UTF8);
                Rotate();
            }
        }
        catch (Exception)
        {
            // Logging must never throw into the UI thread.
        }
    }

    /// <summary>
    /// Flattens an exception chain. WPF wraps markup failures in a TargetInvocationException,
    /// so logging only the outer type hides the actual cause entirely.
    /// </summary>
    private static string Describe(Exception ex)
    {
        var parts = new List<string>();
        var current = ex;
        var depth = 0;
        while (current is not null && depth < 8)
        {
            parts.Add($"{current.GetType().FullName}: {current.Message}");
            current = current.InnerException;
            depth++;
        }

        return string.Join(" --> ", parts);
    }

    /// <summary>Shifts the current log aside once it passes the size cap.</summary>
    private static void Rotate()
    {
        try
        {
            var file = new FileInfo(LogPath);
            if (!file.Exists || file.Length < MaxBytes)
            {
                return;
            }

            var oldest = LogPath + "." + KeepRotations;
            if (File.Exists(oldest))
            {
                File.Delete(oldest);
            }

            for (var i = KeepRotations - 1; i >= 1; i--)
            {
                var from = LogPath + "." + i;
                var to = LogPath + "." + (i + 1);
                if (File.Exists(from))
                {
                    File.Move(from, to, overwrite: true);
                }
            }

            File.Move(LogPath, LogPath + ".1", overwrite: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public static void Info(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(LogDirectory);
                File.AppendAllText(LogPath,
                    $"{DateTime.UtcNow:O} [info] {message}{Environment.NewLine}", Encoding.UTF8);
            }
        }
        catch (Exception)
        {
        }
    }

    public static void OpenLog()
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            if (!File.Exists(LogPath))
            {
                File.WriteAllText(LogPath, string.Empty);
            }

            Process.Start(new ProcessStartInfo(LogPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Write("OpenLog", ex);
        }
    }

    /// <summary>
    /// Writes a diagnostics bundle to disk: environment, device inventory, recent log lines,
    /// and the stored calibration list. Everything the user can see about a problem, and
    /// nothing that would identify a student or a network.
    /// </summary>
    public static void ExportDiagnostics(string path, string body)
    {
        var sb = new StringBuilder()
            .AppendLine("EBoard diagnostics")
            .AppendLine($"Generated: {DateTime.UtcNow:O}")
            .AppendLine($"Machine: {Environment.MachineName}")
            .AppendLine($"OS: {Environment.OSVersion}")
            .AppendLine($"64-bit OS: {Environment.Is64BitOperatingSystem}, 64-bit process: {Environment.Is64BitProcess}")
            .AppendLine($"Runtime: {Environment.Version}")
            .AppendLine()
            .AppendLine(body)
            .AppendLine()
            .AppendLine("--- recent log ---");
        sb.AppendLine(ReadRecent(400));
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    /// <summary>Tail of the log, for the diagnostics bundle. Never throws.</summary>
    public static string ReadRecent(int lines)
    {
        try
        {
            if (!File.Exists(LogPath))
            {
                return "(no log)";
            }

            return string.Join(Environment.NewLine,
                File.ReadAllLines(LogPath).TakeLast(lines));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "(log unreadable: " + ex.Message + ")";
        }
    }
}
