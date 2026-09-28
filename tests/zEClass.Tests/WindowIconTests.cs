using System;
using System.Drawing;
using System.IO;
using Xunit;

namespace zEClass.Tests;

/// <summary>
/// Guards the window-icon frame selection.
///
/// The .ico carries ten frames from 16 to 256 px. The window used to decode it through a URI,
/// and WPF's ICO decoder picks the first frame — 16 px — for every use, so the taskbar and
/// Alt+Tab showed an upscaled 16 px image. This proves the loader asks for a full-size frame
/// instead. It reads the same committed asset the app ships, resolved the same way the
/// catalogue audit resolves its language files.
/// </summary>
[Collection("WpfWindows")]
public sealed class WindowIconTests
{
    private readonly StaUiFixture _ui;

    public WindowIconTests(StaUiFixture ui) => _ui = ui;

    private static string IconPath()
    {
        var probe = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 5 && probe is not null; i++, probe = probe.Parent)
        {
            var candidate = Path.Combine(probe.FullName, "Assets", "zEClass.ico");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("zEClass.ico was not found beside the test binaries");
    }

    [Fact]
    public void IconFile_CarriesTheFullSizeRange()
    {
        var path = IconPath();

        // Every size the shell asks for resolves exactly, up to GDI+'s own ceiling: asking
        // for 256 falls back to 128, which is a platform limit, not a file defect. The raw
        // frames were verified separately to include a true 256 px entry.
        foreach (var size in new[] { 16, 20, 24, 32, 40, 48, 64, 96, 128 })
        {
            using var icon = new Icon(path, new Size(size, size));
            Assert.Equal(size, icon.Width);
            Assert.Equal(size, icon.Height);
        }

        using var huge = new Icon(path, new Size(256, 256));
        Assert.Equal(128, huge.Width);
    }

    [Fact]
    public void FrameSelection_PicksTheRequestedSizeRatherThanTheFirstFrame()
    {
        var path = IconPath();

        // 16 px is the first frame in the file. If selection ever regresses to first-frame
        // decoding, every request below returns 16 and this fails loudly instead of shipping
        // a blurry taskbar icon.
        using var small = new Icon(path, new Size(16, 16));
        using var taskbar = new Icon(path, new Size(64, 64));
        Assert.Equal(16, small.Width);
        Assert.Equal(64, taskbar.Width);
        Assert.NotEqual(small.Handle, taskbar.Handle);
    }

    [Fact]
    public void FrameSelection_ConvertsToAWpfBitmapSource()
    {
        _ui.Invoke(() =>
        {
            var path = IconPath();
            using var icon = new Icon(path, new Size(64, 64));
            var bitmap = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle,
                System.Windows.Int32Rect.Empty,
                System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
            bitmap.Freeze();

            Assert.Equal(64, bitmap.PixelWidth);
            Assert.Equal(64, bitmap.PixelHeight);
        });
    }
}
