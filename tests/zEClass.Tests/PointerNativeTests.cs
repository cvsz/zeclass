using System;
using System.Runtime.InteropServices;
using zEClass.Core;
using Xunit;

namespace zEClass.Tests;

/// <summary>
/// Regression guards for the WM_POINTER reader. Every layout/constant asserted here was
/// documented on learn.microsoft.com (POINTER_INFO, POINTER_PEN_INFO, Pointer Flags,
/// Pen Flags, Pen Mask) and previously read through hand-written x64-only offsets.
/// </summary>
public sealed class PointerNativeTests
{
    private static readonly bool Is64Bit = IntPtr.Size == 8;

    // ---- native layout ---------------------------------------------------------

    [Fact]
    public void PointerInfo_MatchesDocumentedSizeForThisArchitecture()
    {
        // POINTER_INFO: 96 bytes on x64/ARM64, 88 bytes on x86.
        Assert.Equal(Is64Bit ? 96 : 88, Marshal.SizeOf<PointerNative.PointerInfo>());
    }

    [Fact]
    public void PointerPenInfo_MatchesDocumentedSizeForThisArchitecture()
    {
        // POINTER_PEN_INFO: 120 bytes on x64/ARM64, 112 bytes on x86.
        Assert.Equal(Is64Bit ? 120 : 112, Marshal.SizeOf<PointerNative.PointerPenInfo>());
    }

    [Fact]
    public void PointerInfo_PixelLocationIsAtTheDocumentedOffset()
    {
        // ptPixelLocation: offset 32 on x64, 24 on x86. The old reader used 32 on both,
        // which read ptHimetricLocation on x86 builds.
        var offset = (int)Marshal.OffsetOf<PointerNative.PointerInfo>(
            nameof(PointerNative.PointerInfo.PtPixelLocation));
        Assert.Equal(Is64Bit ? 32 : 24, offset);
    }

    [Fact]
    public void PointerInfo_IdAndFlagsAreAtTheDocumentedOffsets()
    {
        Assert.Equal(4, (int)Marshal.OffsetOf<PointerNative.PointerInfo>(
            nameof(PointerNative.PointerInfo.PointerId)));
        Assert.Equal(12, (int)Marshal.OffsetOf<PointerNative.PointerInfo>(
            nameof(PointerNative.PointerInfo.PointerFlags)));
    }

    // ---- constants -------------------------------------------------------------

    [Fact]
    public void PointerFlags_CarryTheDocumentedValues()
    {
        Assert.Equal(0x00000002u, (uint)PointerNative.PointerFlags.InRange);
        Assert.Equal(0x00000004u, (uint)PointerNative.PointerFlags.InContact);
        Assert.Equal(0x00002000u, (uint)PointerNative.PointerFlags.Primary);
        Assert.Equal(0x000004000u, (uint)PointerNative.PointerFlags.Confidence);
        Assert.Equal(0x00040000u, (uint)PointerNative.PointerFlags.Up);
    }

    [Fact]
    public void PenFlags_CarryTheDocumentedValues()
    {
        Assert.Equal(0x1u, (uint)PointerNative.PenFlags.Barrel);
        Assert.Equal(0x2u, (uint)PointerNative.PenFlags.Inverted);
        Assert.Equal(0x4u, (uint)PointerNative.PenFlags.Eraser);
    }

    [Fact]
    public void PenMask_CarryTheDocumentedValues()
    {
        Assert.Equal(0x1u, (uint)PointerNative.PenMask.Pressure);
        Assert.Equal(0x2u, (uint)PointerNative.PenMask.Rotation);
        Assert.Equal(0x4u, (uint)PointerNative.PenMask.TiltX);
        Assert.Equal(0x8u, (uint)PointerNative.PenMask.TiltY);
    }

    [Fact]
    public void PointerInputType_MatchesTagPointerInputType()
    {
        // PT_TOUCH=2, PT_PEN=3, PT_MOUSE=4, PT_TOUCHPAD=5. The old enum mapped
        // Mouse=1 (actually PT_POINTER) and TouchPen=4 (actually PT_MOUSE).
        Assert.Equal(2, (int)PointerInputType.Touch);
        Assert.Equal(3, (int)PointerInputType.Pen);
        Assert.Equal(4, (int)PointerInputType.Mouse);
        Assert.Equal(5, (int)PointerInputType.Touchpad);
    }

    // ---- parsing ---------------------------------------------------------------

    private static PointerNative.PointerInfo Info(
        PointerInputType type, uint id, uint flags, int x = 100, int y = 50) => new()
        {
            PointerType = type,
            PointerId = id,
            PointerFlags = (PointerNative.PointerFlags)flags,
            PtPixelLocation = new PointerNative.NativePoint { X = x, Y = y },
        };

    [Fact]
    public void Parse_MouseSample_ReadsPositionAndContactFlags()
    {
        var sample = PointerNative.Parse(
            Info(PointerInputType.Mouse, 7,
                (uint)(PointerNative.PointerFlags.InRange | PointerNative.PointerFlags.InContact |
                       PointerNative.PointerFlags.New),
                x: 321, y: 654));

        Assert.Equal(PointerInputType.Mouse, sample.Type);
        Assert.Equal(7, sample.Id);
        Assert.Equal(321, sample.X);
        Assert.Equal(654, sample.Y);
        Assert.True(sample.InContact);
        Assert.True(sample.InRange);
        Assert.True(sample.IsMouse);
        Assert.False(sample.Eraser);
        Assert.False(sample.Palm);
        Assert.Equal(0.5, sample.Pressure);
        Assert.True(double.IsNaN(sample.Tilt));
    }

    [Fact]
    public void Parse_PenSample_ReadsPressureTiltRotationFromPenInfo()
    {
        var info = Info(PointerInputType.Pen, 3,
            (uint)(PointerNative.PointerFlags.InRange | PointerNative.PointerFlags.InContact));
        var pen = new PointerNative.PointerPenInfo
        {
            PointerInfo = info,
            PenFlags = PointerNative.PenFlags.Eraser,
            PenMask = PointerNative.PenMask.Pressure |
                      PointerNative.PenMask.TiltX |
                      PointerNative.PenMask.TiltY |
                      PointerNative.PenMask.Rotation,
            Pressure = 512,
            Rotation = 180,
            TiltX = 30,
            TiltY = 40,
        };

        var sample = PointerNative.Parse(in info, in pen, hasPenInfo: true);

        Assert.Equal(0.5, sample.Pressure, 3);
        Assert.Equal(50, sample.Tilt, 3);   // sqrt(30^2 + 40^2), degrees not 0..9000
        Assert.Equal(180, sample.Orientation, 3);
        Assert.True(sample.Eraser);
        Assert.False(sample.Palm);
        Assert.True(sample.IsPen);
    }

    [Fact]
    public void Parse_PenInvertedCountsAsEraser()
    {
        var info = Info(PointerInputType.Pen, 1, 0);
        var pen = new PointerNative.PointerPenInfo
        {
            PointerInfo = info,
            PenFlags = PointerNative.PenFlags.Inverted,
        };

        var sample = PointerNative.Parse(in info, in pen, hasPenInfo: true);

        Assert.True(sample.Eraser);
    }

    [Fact]
    public void Parse_PenWithoutPressureMaskKeepsNeutralPressure()
    {
        // penMask without PEN_MASK_PRESSURE means the pressure field is stale stack data.
        var info = Info(PointerInputType.Pen, 1, 0);
        var pen = new PointerNative.PointerPenInfo
        {
            PointerInfo = info,
            PenMask = PointerNative.PenMask.None,
            Pressure = 9999,
        };

        var sample = PointerNative.Parse(in info, in pen, hasPenInfo: true);

        Assert.Equal(0.5, sample.Pressure);
        Assert.True(double.IsNaN(sample.Tilt));
        Assert.True(double.IsNaN(sample.Orientation));
    }

    [Fact]
    public void Parse_PressureAboveTheDocumentedMaximumIsRejected()
    {
        var info = Info(PointerInputType.Pen, 1, 0);
        var pen = new PointerNative.PointerPenInfo
        {
            PointerInfo = info,
            PenMask = PointerNative.PenMask.Pressure,
            Pressure = 4096,
        };

        var sample = PointerNative.Parse(in info, in pen, hasPenInfo: true);

        Assert.Equal(0.5, sample.Pressure);
    }

    [Fact]
    public void Parse_TouchWithoutConfidenceFlagIsPalm()
    {
        var sample = PointerNative.Parse(Info(PointerInputType.Touch, 5,
            (uint)(PointerNative.PointerFlags.InRange | PointerNative.PointerFlags.InContact)));

        Assert.True(sample.Palm);
        Assert.True(sample.IsTouch);
    }

    [Fact]
    public void Parse_TouchWithConfidenceFlagIsNotPalm()
    {
        var sample = PointerNative.Parse(Info(PointerInputType.Touch, 5,
            (uint)(PointerNative.PointerFlags.InRange |
                   PointerNative.PointerFlags.InContact |
                   PointerNative.PointerFlags.Confidence)));

        Assert.False(sample.Palm);
    }

    [Fact]
    public void Parse_MouseIsNeverPalmEvenWithoutConfidence()
    {
        var sample = PointerNative.Parse(Info(PointerInputType.Mouse, 1,
            (uint)PointerNative.PointerFlags.InContact));

        Assert.False(sample.Palm);
    }

    // ---- live Win32 calls (fail closed, no hardware needed) --------------------

    [Fact]
    public void Read_UnknownPointerIdReturnsNull()
    {
        Assert.Null(PointerNative.Read(999999));
        Assert.Null(PointerNative.Read(-1));
    }

    [Fact]
    public void ReadFrame_UnknownPointerIdReturnsEmptyWithoutThrowing()
    {
        Assert.Empty(PointerNative.ReadFrame(999999));
        Assert.Empty(PointerNative.ReadFrame(-1));
    }

    [Fact]
    public void TypeOf_UnknownPointerIdReturnsUnknown()
    {
        Assert.Equal(PointerInputType.Unknown, PointerNative.TypeOf(999999));
        Assert.Equal(PointerInputType.Unknown, PointerNative.TypeOf(-1));
    }

    [Fact]
    public void User32_ExportsUsedByPointerNativeExist()
    {
        // Guards against the old GetPointerIds-style bug: a DllImport whose entry point
        // does not exist fails at runtime inside a catch(Exception) and silently returns
        // "no data".
        Assert.True(NativeLibrary.TryLoad("user32.dll", out var user32));
        try
        {
            Assert.True(NativeLibrary.TryGetExport(user32, "GetPointerInfo", out _));
            Assert.True(NativeLibrary.TryGetExport(user32, "GetPointerPenInfo", out _));
            Assert.True(NativeLibrary.TryGetExport(user32, "GetPointerType", out _));
            Assert.True(NativeLibrary.TryGetExport(user32, "GetPointerFrameInfo", out _));
        }
        finally
        {
            NativeLibrary.Free(user32);
        }
    }

    [Fact]
    public void WmPointerConstants_AreUnchanged()
    {
        Assert.Equal(0x0245, PointerNative.WmPointerUpdate);
        Assert.Equal(0x0246, PointerNative.WmPointerDown);
        Assert.Equal(0x0247, PointerNative.WmPointerUp);
        Assert.Equal(0x0249, PointerNative.WmPointerEnter);
        Assert.Equal(0x024A, PointerNative.WmPointerLeave);
    }
}
