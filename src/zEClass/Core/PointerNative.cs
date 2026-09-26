using System;
using System.Runtime.InteropServices;

namespace zEClass.Core;

/// <summary>One contact sample delivered by the Windows pointer stack.</summary>
public readonly struct PointerSample
{
    public PointerSample(PointerInputType type, int id, double x, double y, double pressure,
        bool inContact, bool inRange, bool eraser, bool palm, double tilt, double orientation)
    {
        Type = type;
        Id = id;
        X = x;
        Y = y;
        Pressure = pressure;
        InContact = inContact;
        InRange = inRange;
        Eraser = eraser;
        Palm = palm;
        Tilt = tilt;
        Orientation = orientation;
    }

    public PointerInputType Type { get; }
    public int Id { get; }
    public double X { get; }
    public double Y { get; }

    /// <summary>0..1 normalized.</summary>
    public double Pressure { get; }
    public bool InContact { get; }

    /// <summary>Hovering within digitizer range without touching.</summary>
    public bool InRange { get; }
    public bool Eraser { get; }
    public bool Palm { get; }
    public double Tilt { get; }
    public double Orientation { get; }

    public bool IsPen => Type == PointerInputType.Pen;

    public bool IsTouch => Type == PointerInputType.Touch;

    public bool IsMouse => Type == PointerInputType.Mouse;
}

public enum PointerInputType
{
    Unknown = 0,
    Mouse = 1,
    Touch = 2,
    Pen = 3,
    TouchPen = 4,
}

/// <summary>
/// Thin, allocation-free reader for the Win32 pointer-message API. Struct layouts are read
/// through fixed byte offsets into an oversized unmanaged buffer so no marshalling guesswork
/// can corrupt memory; every field is range-validated before use.
/// </summary>
public static class PointerNative
{
    public const int WmPointerUpdate = 0x0245;
    public const int WmPointerDown = 0x0246;
    public const int WmPointerUp = 0x0247;
    public const int WmPointerEnter = 0x0249;
    public const int WmPointerLeave = 0x024A;
    public const int WmMouseMove = 0x0200;
    public const int WmMouseDown = 0x0201;
    public const int WmMouseUp = 0x0202;

    private const int BufferSize = 512;

    // POINTER_INFO field offsets, x64 layout.
    private const int OffPointerType = 0;
    private const int OffPointerId = 4;
    private const int OffPointerFlags = 12;
    private const int OffPixelLocationX = 32;
    private const int OffPixelLocationY = 36;

    // POINTER_PEN_INFO field offsets, x64 layout.
    private const int PenOffPenFlags = 4;
    private const int PenOffPressure = 168;
    private const int PenOffNegativeTilt = 176;
    private const int PenOffAzimuth = 180;
    private const int PenOffOrientation = 184;
    private const int PenOffTilt = 188;

    // POINTER_FLAGS
    private const uint FlagInContact = 0x00000001;
    private const uint FlagInRange = 0x00000002;
    private const uint FlagPalm = 0x00000008;

    // POINTER_PEN_FLAGS
    private const uint PenFlagEraser = 0x00000001;

    private const int PressureMax = 1024;
    private const int TiltMax = 9000;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterPointerInputTarget(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterPointerInputTargetEx(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetPointerInfo(int pointerId, IntPtr pointerInfo);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetPointerPenInfo(int pointerId, IntPtr pointerPenInfo);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetPointerType(int pointerId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetPointerFrameInfo(int pointerId, out uint frameId, out uint pointerCount);

    /// <summary>
    /// Reads every contact that belongs to one pointer frame. Two-finger gestures need this
    /// rather than the per-pointer messages, because both contacts must be sampled from the
    /// same frame; mixing WM_POINTER updates from two contacts lags and jitters.
    /// </summary>
    public static IReadOnlyList<PointerSample> ReadFrame(int pointerId)
    {
        var samples = new List<PointerSample>(4);
        if (!GetPointerFrameInfo(pointerId, out var frameId, out var count) || count == 0 || count > 64)
        {
            return samples;
        }

        var infoBuffer = Marshal.AllocHGlobal(BufferSize);
        var penBuffer = Marshal.AllocHGlobal(BufferSize);
        var idsBuffer = Marshal.AllocHGlobal((int)count * 4);
        try
        {
            if (GetPointerIds((int)frameId, (int)count, idsBuffer) is false)
            {
                return samples;
            }

            for (var i = 0; i < count; i++)
            {
                var id = Marshal.ReadInt32(idsBuffer, i * 4);
                if (GetPointerInfo(id, infoBuffer) == 0)
                {
                    continue;
                }

                var type = (PointerInputType)Marshal.ReadInt32(infoBuffer, OffPointerType);
                var flags = unchecked((uint)Marshal.ReadInt32(infoBuffer, OffPointerFlags));
                var x = Marshal.ReadInt32(infoBuffer, OffPixelLocationX);
                var y = Marshal.ReadInt32(infoBuffer, OffPixelLocationY);

                var pressure = 0.5;
                var eraser = false;
                var tilt = double.NaN;
                var orientation = double.NaN;
                if (GetPointerPenInfo(id, penBuffer) != 0 &&
                    type is PointerInputType.Pen or PointerInputType.TouchPen)
                {
                    eraser = (unchecked((uint)Marshal.ReadInt32(penBuffer, PenOffPenFlags)) &
                        PenFlagEraser) != 0;
                    var raw = Marshal.ReadInt32(penBuffer, PenOffPressure);
                    if (raw is > 0 and <= PressureMax)
                    {
                        pressure = (double)raw / PressureMax;
                    }

                    var rawTilt = Marshal.ReadInt32(penBuffer, PenOffTilt);
                    if (rawTilt is >= 0 and <= TiltMax)
                    {
                        tilt = rawTilt / 90.0;
                    }

                    var rawOrientation = Marshal.ReadInt32(penBuffer, PenOffOrientation);
                    if (rawOrientation is >= 0 and < 3600)
                    {
                        orientation = rawOrientation / 10.0;
                    }
                }

                samples.Add(new PointerSample(
                    type, id, x, y, pressure,
                    (flags & FlagInContact) != 0,
                    (flags & FlagInRange) != 0,
                    eraser,
                    (flags & FlagPalm) != 0,
                    tilt,
                    orientation));
            }
        }
        catch (Exception)
        {
            return samples;
        }
        finally
        {
            Marshal.FreeHGlobal(infoBuffer);
            Marshal.FreeHGlobal(penBuffer);
            Marshal.FreeHGlobal(idsBuffer);
        }

        return samples;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPointerIds(int frameId, int pointerCount, IntPtr pointerIds);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnableNonClientDpiScaling(IntPtr hwnd, bool enable);

    private static readonly IntPtr PerMonitorAwareV2 = new(-4);
    private static readonly IntPtr PerMonitorAware = new(-3);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessDPIAware();

    [DllImport("user32.dll")]
    private static extern bool AreDpiAwarenessContextsEqual(IntPtr a, IntPtr b);

    /// <summary>
    /// True when the process already runs with per-monitor-v2 awareness. The pointer stack
    /// refuses to register on a DPI-unaware or system-aware process, so this must be checked
    /// before <see cref="RegisterPointerInputTarget"/>.
    /// </summary>
    public static bool IsProcessDpiAwarenessSet()
    {
        try
        {
            return AreDpiAwarenessContextsEqual(GetThreadDpiAwarenessContext(), PerMonitorAwareV2) ||
                   AreDpiAwarenessContextsEqual(GetThreadDpiAwarenessContext(), PerMonitorAware);
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
    }

    public static bool EnablePerMonitorV2()
    {
        try
        {
            if (SetProcessDpiAwarenessContext(PerMonitorAwareV2))
            {
                return true;
            }

            return SetProcessDpiAwarenessContext(PerMonitorAware) || SetProcessDPIAware();
        }
        catch (EntryPointNotFoundException)
        {
            return SetProcessDPIAware();
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetThreadDpiAwarenessContext();

    public static PointerSample? Read(int pointerId)
    {
        var buffer = Marshal.AllocHGlobal(BufferSize);
        try
        {
            var pen = GetPointerPenInfo(pointerId, buffer);
            if (pen == 0)
            {
                return null;
            }

            var type = (PointerInputType)Marshal.ReadInt32(buffer, OffPointerType);
            var id = Marshal.ReadInt32(buffer, OffPointerId);
            var flags = unchecked((uint)Marshal.ReadInt32(buffer, OffPointerFlags));
            var x = Marshal.ReadInt32(buffer, OffPixelLocationX);
            var y = Marshal.ReadInt32(buffer, OffPixelLocationY);

            var pressure = 0.5;
            var eraser = false;
            var tilt = double.NaN;
            var orientation = double.NaN;

            if (type is PointerInputType.Pen or PointerInputType.TouchPen)
            {
                var penFlags = unchecked((uint)Marshal.ReadInt32(buffer, PenOffPenFlags));
                eraser = (penFlags & PenFlagEraser) != 0;
                var raw = Marshal.ReadInt32(buffer, PenOffPressure);
                if (raw is > 0 and <= PressureMax)
                {
                    pressure = (double)raw / PressureMax;
                }

                var rawTilt = Marshal.ReadInt32(buffer, PenOffTilt);
                if (rawTilt is >= 0 and <= TiltMax)
                {
                    tilt = rawTilt / 90.0;
                }

                var rawOrientation = Marshal.ReadInt32(buffer, PenOffOrientation);
                if (rawOrientation is >= 0 and < 3600)
                {
                    orientation = rawOrientation / 10.0;
                }

                // Fall back to the azimuth/negative-tilt pair when tilt is not reported.
                if (double.IsNaN(tilt))
                {
                    var negTilt = Marshal.ReadInt32(buffer, PenOffNegativeTilt);
                    var azimuth = Marshal.ReadInt32(buffer, PenOffAzimuth);
                    if (Math.Abs(negTilt) is > 0 and <= TiltMax && azimuth is >= 0 and < 3600)
                    {
                        tilt = Math.Abs(negTilt) / 90.0;
                        orientation = azimuth / 10.0;
                    }
                }
            }

            return new PointerSample(
                type,
                id,
                x,
                y,
                pressure,
                (flags & FlagInContact) != 0,
                (flags & FlagInRange) != 0,
                eraser,
                (flags & FlagPalm) != 0,
                tilt,
                orientation);
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public static PointerInputType TypeOf(int pointerId)
    {
        var t = GetPointerType(pointerId);
        return t > 0 ? (PointerInputType)t : PointerInputType.Unknown;
    }
}
