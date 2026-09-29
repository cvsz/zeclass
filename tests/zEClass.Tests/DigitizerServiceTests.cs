using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using zEClass.Core;
using Xunit;
using Xunit.Abstractions;

namespace zEClass.Tests;

public sealed class DigitizerServiceTests
{
    private readonly ITestOutputHelper _output;

    public DigitizerServiceTests(ITestOutputHelper output) => _output = output;

    private sealed class FakeProbe(params DigitizerDeviceInfo[] devices) : IDigitizerProbe
    {
        public int Calls { get; private set; }

        public IReadOnlyList<DigitizerDeviceInfo> Enumerate()
        {
            Calls++;
            return devices;
        }
    }

    private sealed class ThrowingProbe : IDigitizerProbe
    {
        public IReadOnlyList<DigitizerDeviceInfo> Enumerate() =>
            throw new InvalidOperationException("SetupAPI unavailable");
    }

    private static DigitizerDeviceInfo Device(DigitizerKind kind, ushort vid = 0x1234) =>
        new("Test device", $@"\\?\hid#vid_{vid:X4}#test#{kind}", kind, vid, 0x5678,
            kind != DigitizerKind.None, kind == DigitizerKind.Pen, "TestVendor");

    [Fact]
    public void Probe_ReportsUsbTouchAndPen()
    {
        var service = new DigitizerService(new FakeProbe(
            Device(DigitizerKind.TouchScreen),
            Device(DigitizerKind.Pen)));

        var status = service.Probe();

        Assert.True(status.TouchActive);
        Assert.True(status.PenActive);
        Assert.True(status.UsbDigitizerPresent);
        Assert.True(status.Ready);
    }

    [Fact]
    public void Probe_TouchAndPenKindActivatesBoth()
    {
        var service = new DigitizerService(new FakeProbe(Device(DigitizerKind.TouchAndPen)));

        var status = service.Probe();

        Assert.True(status.TouchActive);
        Assert.True(status.PenActive);
    }

    [Fact]
    public void Probe_ExternalOnlyDeviceIsNotADigitizer()
    {
        var service = new DigitizerService(new FakeProbe(Device(DigitizerKind.External)));

        var status = service.Probe();

        Assert.False(status.UsbDigitizerPresent);
        Assert.False(status.Ready);
    }

    [Fact]
    public void Probe_NoDevicesStillReportsMouseFallbackMessage()
    {
        var service = new DigitizerService(new FakeProbe());

        var status = service.Probe();

        Assert.False(status.Ready);
        Assert.Contains("Mouse", status.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Probe_SurvivesProbeFailureAndDegradesGracefully()
    {
        var service = new DigitizerService(new ThrowingProbe());

        var status = service.Probe();

        Assert.NotNull(status);
        Assert.False(status.Ready);
        Assert.Empty(status.Devices);
    }

    [Fact]
    public void Probe_IncludesVendorAndProductIdsInSummary()
    {
        var service = new DigitizerService(new FakeProbe(Device(DigitizerKind.Pen, 0x0BC7)));

        var status = service.Probe();

        Assert.Contains("VID_0BC7", status.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PID_5678", status.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Probe_RealSetupApiProbeDoesNotThrow()
    {
        // Guards against a malformed P/Invoke signature: enumeration must degrade, not crash.
        var status = new DigitizerService().Probe();

        Assert.NotNull(status);
        Assert.NotNull(status.Devices);
    }

    // ---- InferKind fallback chain: description → path → vendor list ----------------

    [Theory]
    [InlineData("HID-compliant touch screen", DigitizerKind.TouchScreen)]
    [InlineData("HID-compliant touch screen (USB)", DigitizerKind.TouchScreen)]
    [InlineData("HID-compliant pen and touch", DigitizerKind.TouchAndPen)]
    [InlineData("HID-compliant pen", DigitizerKind.Pen)]
    [InlineData("Touch Screen (Elan)", DigitizerKind.TouchScreen)]
    [InlineData("HID-compliant mouse", DigitizerKind.External)]
    [InlineData("Standard 101/102-Key or Microsoft Natural Keyboard", DigitizerKind.External)]
    [InlineData("", DigitizerKind.External)]
    public void InferKind_DeviceDescriptionDrivesClassification(string description, DigitizerKind expected)
    {
        var kind = SetupApiDigitizerProbe.InferKind(
            @"\\?\hid#vid_1234&pid_5678#7&abcdef&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}",
            vid: 0x1234,
            string.IsNullOrEmpty(description) ? null : description);

        Assert.Equal(expected, kind);
    }

    [Theory]
    [InlineData(@"\\?\hid#touchscreen#7&abc&0&0000", DigitizerKind.TouchScreen)]
    [InlineData(@"\\?\hid#digitizer#7&abc&0&0000", DigitizerKind.TouchScreen)]
    [InlineData(@"\\?\hid#pen#7&abc&0&0000", DigitizerKind.Pen)]
    [InlineData(@"\\?\hid#stylus#7&abc&0&0000", DigitizerKind.Pen)]
    [InlineData(@"\\?\hid#vid_1234&pid_5678#7&abc&0&0000", DigitizerKind.External)]
    [InlineData("", DigitizerKind.External)] // malformed path: no separator, no keywords
    public void InferKind_FallsBackToPathKeywordsWhenNoDescription(string path, DigitizerKind expected)
    {
        Assert.Equal(expected, SetupApiDigitizerProbe.InferKind(path, 0x1234, deviceDesc: null));
    }

    [Theory]
    [InlineData((ushort)0x04F3, DigitizerKind.TouchAndPen)] // ELAN — known panel vendor
    [InlineData((ushort)0x056A, DigitizerKind.TouchAndPen)] // Wacom — known pen vendor
    [InlineData((ushort)0x1234, DigitizerKind.External)]     // unknown vendor
    [InlineData((ushort)0x0000, DigitizerKind.External)]     // vendor id unreadable
    public void InferKind_FallsBackToKnownPanelVendorList(ushort vid, DigitizerKind expected)
    {
        Assert.Equal(expected, SetupApiDigitizerProbe.InferKind(
            @"\\?\hid#vid_0000&pid_0000#7&abc&0&0000", vid, deviceDesc: null));
    }

    [Fact]
    public void InferKind_DeviceDescriptionWinsOverUninformativePath()
    {
        // The regression this guards: Dell/STM touch controllers enumerate with paths that
        // contain no keywords and vendor ids missing from the panel list, but Windows
        // reports SPDRP_DEVICEDESC = "HID-compliant touch screen".
        var kind = SetupApiDigitizerProbe.InferKind(
            @"\\?\hid#vid_0483&pid_a581&mi_00&col03#7&2ea5f116&0&0002", 0x0483,
            "HID-compliant touch screen");

        Assert.Equal(DigitizerKind.TouchScreen, kind);
    }

    [Fact]
    public void RealProbe_ReportsDigitizerSummaryForThisMachine()
    {
        var status = new DigitizerService().Probe();
        Assert.False(string.IsNullOrWhiteSpace(status.Summary));
        _output.WriteLine(status.Summary);
    }

    [Fact]
    public void RealProbe_HidInterfacesCarryWellFormedPathAndVendorId()
    {
        // Regression: the interface detail path used to be read two bytes too deep (dropping
        // a backslash); the declaration omitted CharSet, so the marshaller bound the ANSI
        // entry whose size probe reports zero bytes; the last parameter was passed as a
        // non-null PSP_DEVINFO_DATA and failed with ERROR_INVALID_PARAMETER; and the
        // attribute calls received a path string where a device handle was required, so
        // VID/PID always came back zero. A raw SetupAPI count decides whether this machine
        // has HID interfaces at all, so the check is vacuous only on machines that truly
        // have none instead of silently passing when enumeration is broken.
        var status = new DigitizerService().Probe();

        var hid = status.Devices
            .Where(d => d.DevicePath.Contains("hid#", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var raw = CountRawHidInterfaces();
        if (raw == 0)
        {
            _output.WriteLine("No HID interface on this machine; VID/PID check skipped.");
            return;
        }

        Assert.Equal(raw, hid.Count);
        Assert.All(hid, d => Assert.StartsWith(
            @"\\?\hid#", d.DevicePath, StringComparison.OrdinalIgnoreCase));
        Assert.All(hid, d => Assert.NotEqual((ushort)0, d.VendorId));
        _output.WriteLine(
            $"HID entries: {hid.Count}/{raw}; summary: {string.Join(" | ", hid.Select(d => d.Summary))}");
    }

    /// <summary>Counts present HID interfaces straight from SetupAPI, independent of the probe.</summary>
    private static int CountRawHidInterfaces()
    {
        HidD_GetHidGuid(out var guid);
        var set = SetupDiGetClassDevs(ref guid, null, IntPtr.Zero, DigcfPresent | DigcfDeviceInterface);
        if (set == IntPtr.Zero || set == new IntPtr(-1))
        {
            return 0;
        }

        try
        {
            var ifData = Marshal.AllocHGlobal(32);
            try
            {
                var count = 0;
                for (uint i = 0; ; i++)
                {
                    Marshal.WriteInt32(ifData, 32);
                    if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ifData))
                    {
                        break;
                    }

                    count++;
                }

                return count;
            }
            finally
            {
                Marshal.FreeHGlobal(ifData);
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }
    }

    private const uint DigcfPresent = 0x00000002;
    private const uint DigcfDeviceInterface = 0x00000010;

    [DllImport("hid.dll")]
    private static extern void HidD_GetHidGuid(out Guid guid);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, string? enumerator,
        IntPtr hwnd, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet,
        IntPtr deviceInfoData, ref Guid interfaceClassGuid, uint memberIndex,
        IntPtr deviceInterfaceData);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);
}
