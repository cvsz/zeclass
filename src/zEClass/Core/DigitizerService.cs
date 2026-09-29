using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using zEClass.Core;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace zEClass.Core;

/// <summary>What kind of contact surface the OS reports.</summary>
public enum DigitizerKind
{
    None = 0,
    TouchScreen = 1,
    Pen = 2,
    TouchAndPen = 3,
    External = 4,
}

/// <summary>One digitizer device discovered on the machine.</summary>
public sealed record DigitizerDeviceInfo(
    string DeviceName,
    string DevicePath,
    DigitizerKind Kind,
    ushort VendorId,
    ushort ProductId,
    bool SupportsPressure,
    bool SupportsHover,
    string? Manufacturer)
{
    public string Summary =>
        $"{DeviceName} [{Kind}] VID_{VendorId:X4} PID_{ProductId:X4}" +
        (SupportsPressure ? " pressure" : " no-pressure") +
        (SupportsHover ? " hover" : string.Empty);
}

public interface IDigitizerProbe
{
    IReadOnlyList<DigitizerDeviceInfo> Enumerate();
}

/// <summary>
/// Discovers HID digitizers (USB touch panels, pens) through SetupAPI and cross-checks the
/// Windows ink-desktop and touch registry state. Read-only: no driver install, no device
/// reconfiguration, no elevation.
/// </summary>
public sealed class SetupApiDigitizerProbe : IDigitizerProbe
{
    private const uint DigcfPresent = 0x00000002;
    private const uint DigcfDeviceInterface = 0x00000010;
    private const uint FileReadData = 0x00000001;
    private const uint FileWriteData = 0x00000002;
    private const uint FileReadAttributes = 0x00000080;
    private const uint ShareReadWrite = 0x00000003;
    private const uint OpenExisting = 3;

    // sizeof(SP_DEVINFO_DATA): DWORD + GUID + DWORD + pointer, padded to pointer
    // alignment (32 bytes on x64, 28 bytes on x86).
    private static readonly int DevInfoDataSize = Marshal.SizeOf<SP_DEVINFO_DATA>();

    /// <summary>Known interactive-panel and pen-digitizer USB vendor IDs.</summary>
    private static readonly HashSet<ushort> PanelVendors = new()
    {
        0x04F1, 0x04F3, 0x0B16, 0x0BC7, 0x1B80, 0x2A78,
        0x222E, 0x056A, 0x0D3A, 0x258A, 0x1BCF, 0x0CDE, 0x17EF,
    };

    public IReadOnlyList<DigitizerDeviceInfo> Enumerate()
    {
        var results = new List<DigitizerDeviceInfo>();
        try
        {
            results.AddRange(EnumerateHidInterfaces());
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }

        results.Add(ProbeTouchSupport());
        results.Add(ProbeInkDesktop());
        return results
            .GroupBy(d => (d.DevicePath, d.Kind))
            .Select(g => g.First())
            .ToList();
    }

    private static List<DigitizerDeviceInfo> EnumerateHidInterfaces()
    {
        var found = new List<DigitizerDeviceInfo>();
        HidD_GetHidGuid(out var hidGuid);
        var hDevInfo = SetupDiGetClassDevs(ref hidGuid, null, IntPtr.Zero,
            DigcfPresent | DigcfDeviceInterface);
        if (hDevInfo == IntPtr.Zero || hDevInfo == new IntPtr(-1))
        {
            return found;
        }

        try
        {
            // sizeof(SP_DEVICE_INTERFACE_DETAIL_DATA_W): 4-byte cbSize plus a flexible WCHAR
            // array, padded to pointer alignment on x64.
            var detailSize = IntPtr.Size == 8 ? 8 : 6;

            // sizeof(SP_DEVICE_INTERFACE_INTERFACE_DATA): 4 + 16 + 4, padded to 8 on x64.
            var ifDataSize = IntPtr.Size == 8 ? 32 : 28;
            var ifData = Marshal.AllocHGlobal(ifDataSize);
            try
            {
                for (uint i = 0; ; i++)
                {
                    WriteCbSize(ifData, ifDataSize);
                    if (!SetupDiEnumDeviceInterfaces(hDevInfo, IntPtr.Zero, ref hidGuid, i, ifData))
                    {
                        break;
                    }

                    // Size probe: this call intentionally fails with ERROR_INSUFFICIENT_BUFFER
                    // and only reports the byte count the real call needs, so the boolean
                    // result carries no information. Only a zero byte count means give up.
                    WriteCbSize(ifData, ifDataSize);
                    _ = SetupDiGetDeviceInterfaceDetail(hDevInfo, ifData, IntPtr.Zero, 0,
                        out var required, IntPtr.Zero);
                    if (required == 0)
                    {
                        continue;
                    }

                    var detail = Marshal.AllocHGlobal((int)required);
                    try
                    {
                        WriteCbSize(detail, detailSize);

                        // Fetch SP_DEVINFO_DATA alongside the path so the Windows device
                        // description (SPDRP_DEVICEDESC, e.g. "HID-compliant touch screen")
                        // can be used for classification. SP_DEVINFO_DATA is blittable, so
                        // zero-filling the block and patching cbSize is equivalent to a
                        // structure copy. If the call with devInfoData fails for any reason,
                        // retry without it: the path alone is still usable.
                        string? deviceDesc = null;
                        var devInfoDataPtr = Marshal.AllocHGlobal(DevInfoDataSize);
                        try
                        {
                            Marshal.WriteInt32(devInfoDataPtr, 0, DevInfoDataSize);
                            if (SetupDiGetDeviceInterfaceDetail(hDevInfo, ifData, detail, required,
                                    out _, devInfoDataPtr))
                            {
                                var devInfoData = Marshal.PtrToStructure<SP_DEVINFO_DATA>(devInfoDataPtr);
                                deviceDesc = GetDeviceDescription(hDevInfo, ref devInfoData);
                            }
                            else if (!SetupDiGetDeviceInterfaceDetail(hDevInfo, ifData, detail, required,
                                         out _, IntPtr.Zero))
                            {
                                continue;
                            }
                        }
                        finally
                        {
                            Marshal.FreeHGlobal(devInfoDataPtr);
                        }

                        // SP_DEVICE_INTERFACE_DETAIL_DATA_W stores DWORD cbSize at offset 0
                        // and the WCHAR path at offset 4 (WCHAR aligns to 2) on x86 and x64
                        // alike. Reading at offset 6 drops a leading backslash, so no API
                        // accepts the result.
                        var path = Marshal.PtrToStringUni(detail + 4);
                        if (!string.IsNullOrEmpty(path))
                        {
                            found.Add(Describe(path!, deviceDesc));
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(detail);
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(ifData);
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(hDevInfo);
        }

        return found;
    }

    private static void WriteCbSize(IntPtr buffer, int size) => Marshal.WriteInt32(buffer, size);

    // Reads SPDRP_DEVICEDESC (the Windows device description) for a device info node.
    // The standard two-call pattern: the null-buffer probe fails with
    // ERROR_INSUFFICIENT_BUFFER and reports the required byte count; a device with no
    // description reports zero and degrades to null (the caller falls back to path
    // heuristics). Any failure degrades to null; this never throws for untrusted input.
    private static string? GetDeviceDescription(IntPtr hDevInfo, ref SP_DEVINFO_DATA devInfoData)
    {
        _ = SetupDiGetDeviceRegistryProperty(hDevInfo, ref devInfoData, SPDRP_DEVICEDESC,
            out _, IntPtr.Zero, 0, out var required);
        if (required == 0)
        {
            return null;
        }

        var buffer = Marshal.AllocHGlobal((int)required);
        try
        {
            return SetupDiGetDeviceRegistryProperty(hDevInfo, ref devInfoData, SPDRP_DEVICEDESC,
                out _, buffer, required, out _)
                ? Marshal.PtrToStringUni(buffer)
                : null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static DigitizerDeviceInfo Describe(string path, string? deviceDesc)
    {
        ushort vid = 0;
        ushort pid = 0;
        string? manufacturer = null;
        DigitizerKind kind = DigitizerKind.External;

        // HidP_GetCaps needs read access to the device. Touch screens are typically held
        // open by the system HID stack, so this open often fails on exactly the devices we
        // care about; that is expected and the caller falls back to SPDRP_DEVICEDESC.
        using (var handle = CreateFileW(path, FileReadData | FileWriteData, ShareReadWrite,
                     IntPtr.Zero, OpenExisting, 0, IntPtr.Zero))
        {
            if (!handle.IsInvalid)
            {
                kind = ClassifyByHidCaps(handle);
            }
        }

        // HidD_GetAttributes and HidD_GetManufacturerString take an open device handle, not
        // a path string; passing a path makes them fail with ERROR_INVALID_HANDLE and leaves
        // VID/PID at zero. FILE_READ_ATTRIBUTES with read/write sharing is the read-only way
        // in, and a device that refuses the open degrades to unknown ids instead of crashing.
        using var attrHandle = CreateFileW(path, FileReadAttributes, ShareReadWrite,
            IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (!attrHandle.IsInvalid)
        {
            var attributes = new HidAttributes
            {
                Size = (uint)Marshal.SizeOf<HidAttributes>(),
            };
            if (HidD_GetAttributes(attrHandle, ref attributes))
            {
                vid = attributes.VendorID;
                pid = attributes.ProductID;
            }

            var buffer = new byte[512];
            if (HidD_GetManufacturerString(attrHandle, buffer, buffer.Length) && buffer[0] != 0)
            {
                manufacturer = Decode(buffer);
            }
        }

        // HID caps unreadable (common for system-held touch screens): fall back to the
        // Windows device description, then the path/vendor heuristic.
        if (kind == DigitizerKind.External)
        {
            kind = InferKind(path, vid, deviceDesc);
        }

        var pressure = kind is DigitizerKind.Pen or DigitizerKind.TouchScreen
            or DigitizerKind.TouchAndPen;
        return new DigitizerDeviceInfo(
            FriendlyName(path),
            path,
            kind,
            vid,
            pid,
            pressure,
            kind is DigitizerKind.Pen or DigitizerKind.TouchAndPen,
            manufacturer);
    }

    // Classification seam: Windows device description first (authoritative when the
    // driver reports one), then the interface path, then the known-panel vendor list.
    // Internal so the fallback chain is unit-testable without hardware.
    internal static DigitizerKind InferKind(string path, ushort vid, string? deviceDesc)
    {
        // First try Windows device description (e.g., "HID-compliant touch screen")
        if (!string.IsNullOrEmpty(deviceDesc))
        {
            var desc = deviceDesc.ToLowerInvariant();
            var isTouch = desc.Contains("touch");
            var isPen = desc.Contains("pen") || desc.Contains("stylus");
            if (isTouch && isPen)
            {
                return DigitizerKind.TouchAndPen;
            }
            if (isTouch)
            {
                return DigitizerKind.TouchScreen;
            }
            if (isPen)
            {
                return DigitizerKind.Pen;
            }
        }

        // Fall back to path-based heuristic
        var lower = path.ToLowerInvariant();
        var name = lower[(lower.LastIndexOf('\\') + 1)..];
        if (name.Contains("touch") || name.Contains("digitizer") || name.Contains("digitzer"))
        {
            return DigitizerKind.TouchScreen;
        }

        if (name.Contains("pen") || name.Contains("stylus"))
        {
            return DigitizerKind.Pen;
        }

        return PanelVendors.Contains(vid) ? DigitizerKind.TouchAndPen : DigitizerKind.External;
    }

    private static string FriendlyName(string path)
    {
        var leaf = path[(path.LastIndexOf('\\') + 1)..];
        var hash = leaf.IndexOf('#');
        if (hash >= 0 && hash + 1 < leaf.Length)
        {
            var tail = leaf[(hash + 1)..];
            if (tail.Length > 0)
            {
                return tail;
            }
        }

        return string.IsNullOrEmpty(leaf) ? "HID device" : leaf;
    }

    private static string Decode(byte[] buffer)
    {
        var text = new StringBuilder(buffer.Length);
        for (var i = 0; i + 1 < buffer.Length; i += 2)
        {
            if (buffer[i] == 0 && buffer[i + 1] == 0)
            {
                break;
            }

            text.Append((char)(buffer[i] | (buffer[i + 1] << 8)));
        }

        return text.ToString().Trim();
    }

    private static DigitizerDeviceInfo ProbeTouchSupport()
    {
        var enabled = ReadDword(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\TabletTip\1.7", "EnableTouch") == 1;
        return new DigitizerDeviceInfo(
            "Windows touch support",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\TabletTip\1.7",
            enabled ? DigitizerKind.TouchScreen : DigitizerKind.None,
            0,
            0,
            enabled,
            false,
            "Microsoft");
    }

    private static DigitizerDeviceInfo ProbeInkDesktop()
    {
        var hasInk = ReadDword(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\TabletTip\1.7",
            "InkDesktopSupport") == 1;
        return new DigitizerDeviceInfo(
            "Windows ink desktop",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\TabletTip\1.7",
            hasInk ? DigitizerKind.Pen : DigitizerKind.None,
            0,
            0,
            hasInk,
            hasInk,
            "Microsoft");
    }

    private static int? ReadDword(string hivePath, string valueName)
    {
        try
        {
            var isLm = hivePath.StartsWith(@"HKEY_LOCAL_MACHINE", StringComparison.OrdinalIgnoreCase);
            var hk = isLm ? Registry.LocalMachine : Registry.CurrentUser;
            var sub = hivePath[(hivePath.IndexOf('\\') + 1)..];
            using var key = hk.OpenSubKey(sub);
            return key?.GetValue(valueName) switch
            {
                int i => i,
                long l => (int)l,
                _ => null,
            };
        }
        catch (SecurityException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    // sizeof(HIDD_ATTRIBUTES) is 12 bytes on x86 and x64 (ULONG plus three USHORTs padded
    // to the 4-byte alignment of the ULONG). hid.dll writes the whole structure, so a
    // packed 10-byte layout would let the native write run past the managed field.
    [StructLayout(LayoutKind.Sequential)]
    private struct HidAttributes
    {
        public uint Size;
        public ushort VendorID;
        public ushort ProductID;
        public ushort VersionNumber;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, string? enumerator, IntPtr hwnd,
        uint flags);

    // CharSet.Unicode is load-bearing: setupapi.dll exports only the W and A spellings, so
    // without it the marshaller binds SetupDiGetDeviceInterfaceDetailA, whose size probe
    // reports zero bytes here and whose result would be ANSI text read as UTF-16. The last
    // parameter is PSP_DEVINFO_DATA, not a DEVINST; a non-null pointer to an unzeroed
    // structure fails with ERROR_INVALID_PARAMETER (1784), so callers pass IntPtr.Zero.
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr hDevInfo,
        IntPtr deviceInterfaceData, IntPtr deviceInterfaceDetailData,
        uint deviceInterfaceDetailDataSize, out uint requiredSize, IntPtr devInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet,
        IntPtr deviceInfoData, ref Guid interfaceClassGuid, uint memberIndex,
        IntPtr deviceInterfaceData);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern void HidD_GetHidGuid(out Guid guid);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceRegistryProperty(IntPtr deviceInfoSet,
        ref SP_DEVINFO_DATA deviceInfoData, uint property, out uint propertyRegDataType,
        IntPtr propertyBuffer, uint propertyBufferSize, out uint requiredSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVINFO_DATA
    {
        public uint cbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    private const uint SPDRP_DEVICEDESC = 0x00000000; // Device description (friendly name)

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string fileName, uint desiredAccess,
        uint shareMode, IntPtr securityAttributes, uint creationDisposition,
        uint flagsAndAttributes, IntPtr templateFile);

    // ---- HID caps for digitizer classification ---------------------------------
    // Usage Page 0x0D = Digitizers; Usage 0x04 = Touch Screen, 0x02 = Pen, 0x03 = Touch+Pen

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetPreparsedData(SafeFileHandle handle, out IntPtr preparsedData);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern int HidP_GetCaps(IntPtr preparsedData, out HidCaps caps);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

    [StructLayout(LayoutKind.Sequential)]
    private struct HidCaps
    {
        public ushort UsagePage;
        public ushort Usage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
        public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps;
        public ushort NumberInputValueCaps;
        public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps;
        public ushort NumberOutputValueCaps;
        public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps;
        public ushort NumberFeatureValueCaps;
        public ushort NumberFeatureDataIndices;
    }

    private static DigitizerKind ClassifyByHidCaps(SafeFileHandle handle)
    {
        if (!HidD_GetPreparsedData(handle, out var preparsedData))
        {
            return DigitizerKind.External;
        }

        try
        {
            var result = HidP_GetCaps(preparsedData, out var caps);
            if (result != 0)
            {
                return DigitizerKind.External;
            }

            // Usage Page 0x0D = Digitizers
            if (caps.UsagePage == 0x0D)
            {
                return caps.Usage switch
                {
                    0x04 => DigitizerKind.TouchScreen,  // Touch screen
                    0x02 => DigitizerKind.Pen,          // Pen/stylus
                    0x03 => DigitizerKind.TouchAndPen,  // Touch + pen combo
                    _ => DigitizerKind.External
                };
            }

            return DigitizerKind.External;
        }
        finally
        {
            HidD_FreePreparsedData(preparsedData);
        }
    }

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidD_GetAttributes(SafeFileHandle handle,
        ref HidAttributes attributes);

    [DllImport("hid.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidD_GetManufacturerString(SafeFileHandle handle, byte[] buffer,
        int bufferLength);
}

/// <summary>Aggregated readiness view shown in the board diagnostics panel.</summary>
public sealed record DigitizerStatus(
    bool TouchActive,
    bool PenActive,
    bool UsbDigitizerPresent,
    string Summary,
    IReadOnlyList<DigitizerDeviceInfo> Devices)
{
    public bool Ready => TouchActive || PenActive;
}

public sealed class DigitizerService
{
    private readonly IDigitizerProbe _probe;

    public DigitizerService() : this(new SetupApiDigitizerProbe())
    {
    }

    public DigitizerService(IDigitizerProbe probe) => _probe = probe;

    public DigitizerStatus Probe()
    {
        IReadOnlyList<DigitizerDeviceInfo> devices;
        try
        {
            devices = _probe.Enumerate();
        }
        catch (Exception)
        {
            devices = new DigitizerDeviceInfo[0];
        }

        var touch = devices.Any(d =>
            d.Kind is DigitizerKind.TouchScreen or DigitizerKind.TouchAndPen);
        var pen = devices.Any(d => d.Kind is DigitizerKind.Pen or DigitizerKind.TouchAndPen);
        var usb = devices.Any(d =>
            d.Kind is DigitizerKind.TouchScreen or DigitizerKind.Pen or DigitizerKind.TouchAndPen);

        var summary = devices.Count == 0
            ? "No HID digitizer enumerated. Mouse input remains available."
            : string.Join(" | ", devices.Select(d => d.Summary));

        return new DigitizerStatus(touch, pen, usb, summary, devices);
    }
}
