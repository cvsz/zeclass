using System;
using zEClass.Core;
using Xunit;

namespace zEClass.Tests;

public sealed class ShareableDiagnosticsTests
{
    [Fact]
    public void DeviceLine_DropsPathNameAndManufacturer()
    {
        var device = new DigitizerDeviceInfo(
            "Touchscreen",
            @"\\?\hid#vid_0BC7&pid_0001#7&1a2b3c4d&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}",
            DigitizerKind.TouchAndPen,
            0x0BC7, 0x0001, true, true, "SecretVendor Inc.");

        var line = ShareableDiagnostics.DeviceLine(device);

        Assert.Contains("VID_0BC7", line);
        Assert.Contains("PID_0001", line);
        Assert.DoesNotContain("hid#", line);
        Assert.DoesNotContain("1a2b3c4d", line);
        Assert.DoesNotContain("SecretVendor", line);
        Assert.DoesNotContain("Touchscreen", line);
    }

    [Fact]
    public void BoardFileName_StripsDirectories()
    {
        Assert.Equal("lesson.ebboard", ShareableDiagnostics.BoardFileName(
            @"C:\Users\teacher\Documents\zEClass\lesson.ebboard"));
        Assert.Equal("(not saved yet)", ShareableDiagnostics.BoardFileName(null));
        Assert.Equal("(not saved yet)", ShareableDiagnostics.BoardFileName(string.Empty));
    }

    [Fact]
    public void ShareableBody_ContainsNoMachineIdentity()
    {
        // The shareable export is assembled from these helpers plus counts and OS/runtime
        // lines. This pins the helpers' contract: nothing identifying may pass through.
        var user = Environment.UserName;
        var machine = Environment.MachineName;
        var line = ShareableDiagnostics.DeviceLine(new DigitizerDeviceInfo(
            machine, $@"C:\Users\{user}\device", DigitizerKind.Pen, 1, 2, true, true, user));
        var file = ShareableDiagnostics.BoardFileName($@"C:\Users\{user}\board.ebboard");

        Assert.DoesNotContain(user, line + file);
        Assert.DoesNotContain(machine, line + file);
    }
}
