using System;
using System.IO;

namespace zEClass.Core;

/// <summary>
/// Redaction for diagnostics that leave the machine: the shareable report keeps everything
/// a supporter needs (OS/runtime, digitizer kinds, counts, board shape) while dropping
/// machine name, user name, full paths, per-instance device paths, and manufacturer
/// strings. There is no telemetry and no network transmission; this only controls what
/// the operator hands to someone else.
/// </summary>
public static class ShareableDiagnostics
{
    /// <summary>Kind plus vendor/product only: no device path, no instance id, no name.</summary>
    public static string DeviceLine(DigitizerDeviceInfo device) =>
        $"{device.Kind} VID_{device.VendorId:X4} PID_{device.ProductId:X4}";

    /// <summary>File name only: the board may live under a personal folder.</summary>
    public static string BoardFileName(string? path) =>
        string.IsNullOrEmpty(path) ? "(not saved yet)" : Path.GetFileName(path);
}
