using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using zEClass.Core;
using Microsoft.Win32;

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

                    WriteCbSize(ifData, ifDataSize);
                    if (!SetupDiGetDeviceInterfaceDetail(hDevInfo, ifData, IntPtr.Zero, 0,
                            out var required, out _)
                        || required == 0)
                    {
                        continue;
                    }

                    var detail = Marshal.AllocHGlobal((int)required);
                    try
                    {
                        WriteCbSize(detail, detailSize);
                        if (!SetupDiGetDeviceInterfaceDetail(hDevInfo, ifData, detail, required,
                                out _, out _))
                        {
                            continue;
                        }

                        // DevicePath starts at offset 6 (4-byte cbSize + first WCHAR) on both
                        // x86 and x64.
                        var path = Marshal.PtrToStringUni(detail + 6);
                        if (!string.IsNullOrEmpty(path))
                        {
                            found.Add(Describe(path!));
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

    private static DigitizerDeviceInfo Describe(string path)
    {
        var attributes = new HidAttributes[1];
        ushort vid = 0;
        ushort pid = 0;
        if (HidD_GetAttributes(path, attributes))
        {
            vid = attributes[0].VendorID;
            pid = attributes[0].ProductID;
        }

        var buffer = new byte[512];
        string? manufacturer = null;
        if (HidD_GetManufacturerString(path, buffer, buffer.Length) && buffer[0] != 0)
        {
            manufacturer = Decode(buffer);
        }

        var kind = InferKind(path, vid);
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

    private static DigitizerKind InferKind(string path, ushort vid)
    {
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

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct HidAttributes
    {
        public int Size;
        public ushort VendorID;
        public ushort ProductID;
        public ushort VersionNumber;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, string? enumerator, IntPtr hwnd,
        uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr hDevInfo,
        IntPtr deviceInterfaceData, IntPtr deviceInterfaceDetailData,
        uint deviceInterfaceDetailDataSize, out uint requiredSize, out IntPtr deviceInstanceId);

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

    [DllImport("hid.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidD_GetAttributes(string path,
        [In, Out] HidAttributes[] attributes);

    [DllImport("hid.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidD_GetManufacturerString(string path, byte[] buffer, int bufferLength);
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
