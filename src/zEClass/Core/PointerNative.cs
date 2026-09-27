using System;
using System.Collections.Generic;
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

    /// <summary>Tilt magnitude in degrees, 0..90 (NaN when the device reports none).</summary>
    public double Tilt { get; }

    /// <summary>Pen twist in degrees, 0..359 (NaN when the device reports none).</summary>
    public double Orientation { get; }

    public bool IsPen => Type == PointerInputType.Pen;

    public bool IsTouch => Type == PointerInputType.Touch;

    public bool IsMouse => Type == PointerInputType.Mouse;
}

/// <summary>
/// ค่าตรงกับ POINTER_INPUT_TYPE ใน winuser.h ห้ามเปลี่ยนตัวเลขเอง เพราะ struct marshal
/// อ่านค่าชนิดนี้เป็น int จากข้อความ WM_POINTER ตรงๆ
/// </summary>
public enum PointerInputType
{
    Unknown = 0,
    Pointer = 1,
    Touch = 2,
    Pen = 3,
    Mouse = 4,
    Touchpad = 5,
}

/// <summary>
/// ตัวอ่าน Win32 pointer-message แบบ struct marshal ตาม layout ที่ MSDN กำหนด
/// (POINTER_INFO / POINTER_PEN_INFO) จึงถูกต้องทั้ง x64/x86/ARM64 โดยไม่ต้องเดา offset
/// ทุกฟิลด์ถูก range-validate ก่อนใช้ และทุก P/Invoke มีลายเซ็นตรงกับ winuser.h
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

    private const int MaxFramePointers = 64;
    private const uint PressureMax = 1024;
    private const int TiltDegreesMax = 90;
    private const uint RotationDegreesMax = 360;

    // ---- native layout (winuser.h) --------------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativePoint
    {
        public int X;
        public int Y;
    }

    /// <summary>POINTER_INFO — 96 ไบต์บน x64, 88 ไบต์บน x86 (ทดสอบโดย unit test)</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct PointerInfo
    {
        public PointerInputType PointerType;
        public uint PointerId;
        public uint FrameId;
        public PointerFlags PointerFlags;
        public IntPtr SourceDevice;
        public IntPtr HwndTarget;
        public NativePoint PtPixelLocation;
        public NativePoint PtHimetricLocation;
        public NativePoint PtPixelLocationRaw;
        public NativePoint PtHimetricLocationRaw;
        public uint DwTime;
        public uint HistoryCount;
        public int InputData;
        public uint DwKeyStates;
        public ulong PerformanceCount;
        public int ButtonChangeType;
    }

    /// <summary>POINTER_PEN_INFO — 120 ไบต์บน x64, 112 ไบต์บน x86 (ทดสอบโดย unit test)</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct PointerPenInfo
    {
        public PointerInfo PointerInfo;
        public PenFlags PenFlags;
        public PenMask PenMask;
        public uint Pressure;
        public uint Rotation;
        public int TiltX;
        public int TiltY;
    }

    /// <summary>POINTER_FLAGS — ค่าจากหน้า Pointer Flags ของ MSDN</summary>
    [Flags]
    internal enum PointerFlags : uint
    {
        None = 0,
        New = 0x00000001,
        InRange = 0x00000002,
        InContact = 0x00000004,
        FirstButton = 0x00000010,
        Primary = 0x00002000,
        Confidence = 0x000004000,
        Canceled = 0x00008000,
        Down = 0x00010000,
        Update = 0x00020000,
        Up = 0x00040000,
    }

    /// <summary>PEN_FLAGS — Barrel=1, Inverted=2 (กลับด้าน), Eraser=4</summary>
    [Flags]
    internal enum PenFlags : uint
    {
        None = 0,
        Barrel = 0x00000001,
        Inverted = 0x00000002,
        Eraser = 0x00000004,
    }

    /// <summary>PEN_MASK — บอกว่าฟิลด์ใดใน POINTER_PEN_INFO ใช้ได้จริง</summary>
    [Flags]
    internal enum PenMask : uint
    {
        None = 0,
        Pressure = 0x00000001,
        Rotation = 0x00000002,
        TiltX = 0x00000004,
        TiltY = 0x00000008,
    }

    // ---- P/Invoke (ลายเซ็นตรงกับ winuser.h ทุกตัว) ---------------------------

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetPointerInfo(uint pointerId, ref PointerInfo pointerInfo);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetPointerPenInfo(uint pointerId, ref PointerPenInfo pointerPenInfo);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetPointerType(uint pointerId, out PointerInputType pointerType);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetPointerFrameInfo(uint pointerId, ref uint pointerCount,
        [Out] PointerInfo[] pointerInfo);

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

    [DllImport("user32.dll")]
    private static extern IntPtr GetThreadDpiAwarenessContext();

    /// <summary>
    /// True when the process already runs with per-monitor-v2 (or system) awareness.
    /// ptPixelLocation ของ WM_POINTER เป็นพิกัดจอจริง ถ้า process ไม่ได้ DPI-aware
    /// ค่าที่อ่านมาจะเพี้ยนตอนจอสองเครื่องคนละ scale
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

    // ---- reading --------------------------------------------------------------

    /// <summary>
    /// อ่านจุดสัมผัสหนึ่งจุดจาก pointer id ของข้อความ WM_POINTER
    /// คืน null เมื่อ id ไม่มีอยู่จริงหรือระบบไม่ยอมให้อ่าน (อ่านได้เฉพาะบน UI thread
    /// ของ thread ที่ได้รับข้อความเท่านั้น)
    /// </summary>
    public static PointerSample? Read(int pointerId)
    {
        if (pointerId < 0)
        {
            return null;
        }

        try
        {
            var info = default(PointerInfo);
            if (!GetPointerInfo((uint)pointerId, ref info))
            {
                return null;
            }

            return SampleFor(in info);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// อ่านทุกจุดสัมผัสในเฟรมเดียวกัน (GetPointerFrameInfo) นิ้วสองนิ้วต้องถูกอ่าน
    /// จากเฟรมเดียวกันไม่งั้น gesture สั่นคลอน
    /// </summary>
    public static IReadOnlyList<PointerSample> ReadFrame(int pointerId)
    {
        var samples = new List<PointerSample>(4);
        if (pointerId < 0)
        {
            return samples;
        }

        try
        {
            var infos = new PointerInfo[MaxFramePointers];
            var count = (uint)infos.Length;
            if (!GetPointerFrameInfo((uint)pointerId, ref count, infos) ||
                count == 0 || count > (uint)infos.Length)
            {
                return samples;
            }

            for (var i = 0; i < count; i++)
            {
                samples.Add(SampleFor(in infos[i]));
            }
        }
        catch (Exception)
        {
            return samples;
        }

        return samples;
    }

    /// <summary>ชนิดของ pointer id (PT_TOUCH=2, PT_PEN=3, PT_MOUSE=4) คืน Unknown เมื่ออ่านไม่ได้</summary>
    public static PointerInputType TypeOf(int pointerId)
    {
        if (pointerId < 0)
        {
            return PointerInputType.Unknown;
        }

        try
        {
            return GetPointerType((uint)pointerId, out var type) ? type : PointerInputType.Unknown;
        }
        catch (Exception)
        {
            return PointerInputType.Unknown;
        }
    }

    /// <summary>
    /// เติม pen data ต่อเมื่อ pointer เป็น pen และ GetPointerPenInfo สำเร็จ
    /// (pen fields ไม่มีใน mouse/touch และไม่ควรอ่านมั่วจาก buffer ที่ระบบไม่ได้เขียน)
    /// </summary>
    internal static PointerSample SampleFor(in PointerInfo info)
    {
        if (info.PointerType == PointerInputType.Pen)
        {
            var pen = default(PointerPenInfo);
            if (GetPointerPenInfo(info.PointerId, ref pen))
            {
                return Parse(in info, in pen, hasPenInfo: true);
            }
        }

        return Parse(in info, default, hasPenInfo: false);
    }

    internal static PointerSample Parse(in PointerInfo info) =>
        Parse(in info, default, hasPenInfo: false);

    /// <summary>
    /// แปลง struct เป็น PointerSample — ฟิลด์ pressure/tilt/rotation อ่านเฉพาะตอน
    /// PEN_MASK บอกว่าใช้ได้ ไม่งั้นค่าใน struct เป็นค่าขยะจาก stack
    /// </summary>
    internal static PointerSample Parse(in PointerInfo info, in PointerPenInfo pen, bool hasPenInfo)
    {
        var flags = info.PointerFlags;
        var inContact = flags.HasFlag(PointerFlags.InContact);
        var inRange = flags.HasFlag(PointerFlags.InRange);

        // POINTER_FLAG_CONFIDENCE หายไป = อุปกรณ์ฟันธงว่าสัมผัสนี้ "ไม่ตั้งใจ" (ฝ่ามือ) ใช้กับ touch เท่านั้น
        var palm = info.PointerType == PointerInputType.Touch &&
                   !flags.HasFlag(PointerFlags.Confidence);

        var pressure = 0.5;
        var eraser = false;
        var tilt = double.NaN;
        var orientation = double.NaN;

        if (hasPenInfo && info.PointerType == PointerInputType.Pen)
        {
            // PEN_FLAG_ERASER = ปุ่มยางลบ, PEN_FLAG_INVERTED = กลับด้าน (ใช้ปลายยางลบ)
            eraser = pen.PenFlags.HasFlag(PenFlags.Eraser) ||
                     pen.PenFlags.HasFlag(PenFlags.Inverted);

            if (pen.PenMask.HasFlag(PenMask.Pressure) && pen.Pressure <= PressureMax)
            {
                pressure = pen.Pressure / (double)PressureMax;
            }

            if (pen.PenMask.HasFlag(PenMask.TiltX) || pen.PenMask.HasFlag(PenMask.TiltY))
            {
                // tiltX/tiltY เป็นองศา -90..+90 ตาม MSDN ไม่ใช่ 0..9000
                var tx = Math.Clamp(pen.TiltX, -TiltDegreesMax, TiltDegreesMax);
                var ty = Math.Clamp(pen.TiltY, -TiltDegreesMax, TiltDegreesMax);
                tilt = Math.Sqrt((double)tx * tx + (double)ty * ty);
            }

            if (pen.PenMask.HasFlag(PenMask.Rotation) && pen.Rotation < RotationDegreesMax)
            {
                orientation = pen.Rotation;
            }
        }

        return new PointerSample(
            info.PointerType,
            (int)info.PointerId,
            info.PtPixelLocation.X,
            info.PtPixelLocation.Y,
            pressure,
            inContact,
            inRange,
            eraser,
            palm,
            tilt,
            orientation);
    }
}
