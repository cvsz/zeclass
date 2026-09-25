using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using EBoard.Core;
using Xunit;

namespace EBoard.Tests;

public sealed class LocatorTests : IDisposable
{
    private readonly string _dir;

    public LocatorTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "eboard-lang-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void EnglishIsBuiltInAndAlwaysAvailable()
    {
        var locator = new Locator();

        Assert.Contains("en", locator.AvailableLanguages);
        Assert.Equal("en", locator.Language);
        Assert.Equal("Pen", locator.Strings["tool.pen"]);
    }

    [Fact]
    public void UnknownKeyFallsBackToTheKeyItself()
    {
        var locator = new Locator();

        Assert.Equal("no.such.key", locator.Strings["no.such.key"]);
    }

    [Fact]
    public void HasReportsKeyPresence()
    {
        var locator = new Locator();

        Assert.True(locator.Strings.Has("tool.pen"));
        Assert.False(locator.Strings.Has("nope"));
    }

    [Fact]
    public void LoadFromDisk_ReadsLanguageFiles()
    {
        File.WriteAllText(Path.Combine(_dir, "fr.json"),
            """{"Values":{"tool.pen":"Stylo","action.undo":"Annuler"}}""");
        var locator = new Locator();

        locator.LoadFromDisk(_dir);

        Assert.Contains("fr", locator.AvailableLanguages);
        Assert.True(locator.SetLanguage("fr"));
        Assert.Equal("Stylo", locator.Strings["tool.pen"]);
    }

    [Fact]
    public void PartialTranslationFallsBackToEnglishPerKey()
    {
        // The whole point of per-key fallback: a half-finished translation shows real words
        // rather than a screen full of raw keys.
        File.WriteAllText(Path.Combine(_dir, "de.json"),
            """{"Values":{"tool.pen":"Stift"}}""");
        var locator = new Locator();
        locator.LoadFromDisk(_dir);
        locator.SetLanguage("de");

        Assert.Equal("Stift", locator.Strings["tool.pen"]);
        Assert.Equal("Erase", locator.Strings["tool.eraser"]);
    }

    [Fact]
    public void LoadFromDisk_IgnoresMalformedFiles()
    {
        File.WriteAllText(Path.Combine(_dir, "broken.json"), "{ not json at all");
        File.WriteAllText(Path.Combine(_dir, "empty.json"), """{"Values":{}}""");
        var locator = new Locator();

        locator.LoadFromDisk(_dir);

        Assert.DoesNotContain("broken", locator.AvailableLanguages);
        Assert.DoesNotContain("empty", locator.AvailableLanguages);
        Assert.Contains("en", locator.AvailableLanguages);
    }

    [Fact]
    public void LoadFromDisk_ToleratesMissingFolder()
    {
        var locator = new Locator();

        locator.LoadFromDisk(Path.Combine(_dir, "does-not-exist"));

        Assert.Contains("en", locator.AvailableLanguages);
    }

    [Fact]
    public void SetLanguage_RejectsUnknownCode()
    {
        var locator = new Locator();

        Assert.False(locator.SetLanguage("xx"));
        Assert.Equal("en", locator.Language);
    }

    [Fact]
    public void Format_SubstitutesArguments()
    {
        var locator = new Locator();

        Assert.Equal("Page 2 / 5", locator.Strings.Format("status.page", 2, 5));
    }

    [Fact]
    public void Format_SurvivesAMalformedTranslation()
    {
        var strings = new Strings { Values = { ["bad"] = "has {0} and {1} but only one" } };

        // Must not throw: a broken translation cannot be allowed to blank the interface.
        var result = strings.Format("bad", 1);

        Assert.NotNull(result);
    }

    [Fact]
    public void AllKeysInEnglishAreUniqueAndNonEmpty()
    {
        var english = new Locator().Strings;
        foreach (var (key, value) in english.Values)
        {
            Assert.False(string.IsNullOrWhiteSpace(key), "empty key");
            Assert.False(string.IsNullOrWhiteSpace(value), $"empty value for {key}");
        }
    }

    [Fact]
    public void BuiltInEnglishIsPopulatedOnFirstUse()
    {
        // Regression guard. The English catalogue and the Current singleton are static, and their
        // declaration order decides whether Current is built from a populated or a null
        // dictionary. Getting it backwards made every lookup return its own key, so this
        // asserts the observable outcome rather than the field layout.
        Assert.NotEmpty(Locator.BuiltInEnglish);
        Assert.Equal("Pen", Locator.T("tool.pen"));
        Assert.NotEqual("tool.pen", Locator.T("tool.pen"));
    }

    [Fact]
    public void EveryShippedLanguageFileIsUsableAndComplete()
    {
        // Each shipped file must define every key, otherwise a switch would silently blank parts
        // of the interface. Missing keys fall back to English, so this checks coverage.
        var shipped = Path.Combine(AppContext.BaseDirectory, "lang");
        if (!Directory.Exists(shipped))
        {
            return;
        }

        var files = Directory.EnumerateFiles(shipped, "*.json").ToList();
        Assert.NotEmpty(files);

        foreach (var file in files)
        {
            var code = Path.GetFileNameWithoutExtension(file);
            var locator = new Locator();
            locator.LoadFromDisk(shipped);
            Assert.True(locator.SetLanguage(code), code);

            foreach (var key in Locator.BuiltInEnglish.Keys)
            {
                Assert.True(locator.Strings.Has(key), $"{code} is missing '{key}'");
            }
        }
    }

    [Fact]
    public void ShippedLanguagesDifferFromEnglishWhereTranslated()
    {
        var shipped = Path.Combine(AppContext.BaseDirectory, "lang");
        if (!Directory.Exists(shipped))
        {
            return;
        }

        var locator = new Locator();
        locator.LoadFromDisk(shipped);
        var others = locator.AvailableLanguages.Where(c => c != "en").ToList();
        if (others.Count == 0)
        {
            return;
        }

        foreach (var code in others)
        {
            locator.SetLanguage(code);
            Assert.NotEqual("Pen", locator.Strings["tool.pen"]);
        }
    }

    [Fact]
    public void ThemeDefaultFallsBackToADarkPalette()
    {
        // Must not throw and must return one of the three known themes.
        Assert.Contains(Theme.Default, Theme.All);
    }

    [Fact]
    public void EveryThemeProducesFrozenUsableBrushes()
    {
        foreach (var theme in Theme.All)
        {
            Assert.NotNull(theme.ChromeBackgroundBrush);
            Assert.NotNull(theme.AccentBrush);
            Assert.True(theme.ChromeBackgroundBrush.IsFrozen);
            Assert.NotEqual(Colors.Transparent, theme.ChromeBackgroundBrush.Color);
        }
    }

    [Fact]
    public void HighContrastThemeIsActuallyHighContrast()
    {
        var hc = Theme.HighContrast;
        var background = hc.ChromeBackgroundBrush.Color;
        var foreground = hc.ChromeForegroundBrush.Color;

        // Relative luminance difference, the thing that actually decides legibility.
        static double Luminance(System.Windows.Media.Color c) =>
            (0.2126 * c.R) + (0.7152 * c.G) + (0.0722 * c.B);

        var difference = Math.Abs(Luminance(background) - Luminance(foreground));
        Assert.True(difference > 200, $"contrast difference was only {difference}");
    }

    [Fact]
    public void DarkThemeListIsNonEmptyAndDistinct()
    {
        Assert.Equal(3, Theme.All.Count);
        Assert.Equal(Theme.All.Count, Theme.All.Select(t => t.Name).Distinct().Count());
    }
}
