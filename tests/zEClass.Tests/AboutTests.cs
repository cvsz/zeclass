using Xunit;

namespace zEClass.Tests;

/// <summary>
/// Tests for the About dialog's facts and construction.
///
/// The credit line is the one thing in the app that must never be wrong, so the wording lives in
/// <see cref="AboutInfo"/> and is asserted here rather than eyeballed in a screenshot.
/// </summary>
[Collection("WpfWindows")]
public sealed class AboutTests
{
    private readonly StaUiFixture _ui;

    public AboutTests(StaUiFixture ui) => _ui = ui;
    [Fact]
    public void Current_NamesTheProductAndDeveloper()
    {
        var info = AboutInfo.Current;

        Assert.Equal("zEClass Interactive Whiteboard", info.ProductName);
        Assert.Equal("ZEAZDEV COMPANY LIMITED", info.Developer);
        Assert.Contains(info.Developer, info.Copyright);
        Assert.Contains("no vendor code", info.Provenance);
    }

    [Fact]
    public void Current_VersionIsAPlainDottedTriple()
    {
        var info = AboutInfo.Current;

        Assert.Matches(@"^\d+\.\d+\.\d+$", info.Version);
    }

    [Fact]
    public void MissingArtwork_DegradesToNullRatherThanThrowing()
    {
        _ui.Invoke(() =>
        {
            // These files are not committed, so on a fresh checkout this proves the
            // dialog's graceful path rather than failing for lack of artwork.
            var missing = AboutWindow.TryLoadImage("definitely-not-here.png", 100);
            Assert.Null(missing);
        });
    }

    [Fact]
    public void ShippedArtwork_LoadsSuccessfully()
    {
        _ui.Invoke(() =>
        {
            // The branding files are committed under Assets and flow to the test output, so
            // this proves the dialog's happy path against the real artwork, not just the
            // missing-file fallback above.
            var logo = AboutWindow.TryLoadImage(AboutWindow.CompanyLogoFile, 150);
            var emblem = AboutWindow.TryLoadImage(AboutWindow.EmblemFile, 120);
            Assert.NotNull(logo);
            Assert.NotNull(emblem);
            Assert.Equal(150, logo.Height);
            Assert.Equal(120, emblem.Height);
        });
    }

    [Fact]
    public void Window_ConstructsWithoutThrowing()
    {
        _ui.Invoke(() =>
        {
            var window = new AboutWindow();
            Assert.Equal("About zEClass", window.Title);
        });
    }
}
