using System;
using System.Collections.Generic;
using zEClass.Core;
using Xunit;

namespace zEClass.Tests;

public sealed class DigitizerServiceTests
{
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

    [Fact]
    public void RealProbe_ReportsDigitizerSummaryForThisMachine()
    {
        var status = new DigitizerService().Probe();
        Assert.False(string.IsNullOrWhiteSpace(status.Summary));
    }
}
