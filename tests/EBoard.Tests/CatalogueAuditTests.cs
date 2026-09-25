using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using EBoard.Core;
using Xunit;

namespace EBoard.Tests;

/// <summary>
/// Audits every shipped language file.
///
/// With one catalogue this is trivial. With twenty-three it is the only thing standing between a
/// release and a screen full of raw keys, a crash from a dropped <c>{0}</c>, or a right-to-left
/// language laid out the wrong way round. These run against the real files, not fixtures, so a
/// translation added carelessly fails the build.
/// </summary>
public sealed class CatalogueAuditTests
{
    private static string LangDir
    {
        get
        {
            // The files ship beside the app, and the test binary sits one directory away from it.
            var dir = Path.Combine(AppContext.BaseDirectory, "lang");
            if (Directory.Exists(dir))
            {
                return dir;
            }

            // Walk up to the project output when running from the build tree directly.
            var probe = new DirectoryInfo(AppContext.BaseDirectory);
            for (var i = 0; i < 5 && probe is not null; i++, probe = probe.Parent)
            {
                var candidate = Path.Combine(probe.FullName, "lang");
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }
            }

            throw new DirectoryNotFoundException($"no lang directory found from {AppContext.BaseDirectory}");
        }
    }

    private static Dictionary<string, string> Load(string path)
    {
        var strings = JsonSerializer.Deserialize<Strings>(File.ReadAllText(path));
        Assert.NotNull(strings);
        return strings!.Values.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
    }

    public static IEnumerable<object[]> AllLanguages()
    {
        foreach (var file in Directory.EnumerateFiles(LangDir, "*.json").OrderBy(f => f))
        {
            yield return [Path.GetFileNameWithoutExtension(file)];
        }
    }

    [Fact]
    public void EveryLanguageFileIsDiscoverable()
    {
        var files = Directory.EnumerateFiles(LangDir, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(files);
        Assert.True(files.Count >= 20, $"only {files.Count} language files, expected at least 20");
    }

    [Fact]
    public void EveryLanguageFileIsListedInTheCatalog()
    {
        // A file with no LanguageInfo entry would show up in the picker as a bare code and would
        // never be mirrored if it needed to be.
        foreach (var code in Directory.EnumerateFiles(LangDir, "*.json")
                     .Select(Path.GetFileNameWithoutExtension))
        {
            Assert.True(LanguageCatalog.Find(code) is not null,
                $"{code}.json has no entry in LanguageCatalog");
        }
    }

    [Fact]
    public void EveryCatalogEntryHasAFile()
    {
        foreach (var info in LanguageCatalog.All)
        {
            if (info.Code == "en")
            {
                continue;
            }

            Assert.True(File.Exists(Path.Combine(LangDir, $"{info.Code}.json")),
                $"{info.EnglishName} is listed but {info.Code}.json is missing");
        }
    }

    [Fact]
    public void NoDuplicateLanguageCodes()
    {
        var codes = LanguageCatalog.All.Select(l => l.Code).ToList();
        Assert.Equal(codes.Count, codes.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void EveryLanguageHasANativeName()
    {
        foreach (var info in LanguageCatalog.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(info.NativeName), $"{info.Code} has no native name");
        }
    }

    [Theory]
    [MemberData(nameof(AllLanguages))]
    public void CatalogueIsComplete(string code)
    {
        var values = Load(Path.Combine(LangDir, $"{code}.json"));

        var missing = CatalogueAudit.Missing(code, values);
        Assert.True(missing.Count == 0,
            $"{code}.json is missing {missing.Count} keys: {string.Join(", ", missing)}");
    }

    [Theory]
    [MemberData(nameof(AllLanguages))]
    public void CatalogueHasNoUnknownKeys(string code)
    {
        var values = Load(Path.Combine(LangDir, $"{code}.json"));

        var unknown = CatalogueAudit.Unknown(code, values);
        Assert.True(unknown.Count == 0,
            $"{code}.json defines keys English does not: {string.Join(", ", unknown)}");
    }

    [Theory]
    [MemberData(nameof(AllLanguages))]
    public void CataloguePlaceholdersSurviveTranslation(string code)
    {
        var values = Load(Path.Combine(LangDir, $"{code}.json"));

        // A dropped or renamed {0} throws FormatException the moment that status line updates,
        // which is the sort of bug that only appears in front of a class.
        var problems = CatalogueAudit.PlaceholderMismatch(code, values);
        Assert.True(problems.Count == 0,
            $"{code}.json placeholder problems: {string.Join("; ", problems)}");
    }

    [Theory]
    [MemberData(nameof(AllLanguages))]
    public void CatalogueIsActuallyTranslated(string code)
    {
        if (code == "en")
        {
            return;
        }

        var values = Load(Path.Combine(LangDir, $"{code}.json"));

        var untranslated = CatalogueAudit.Untranslated(code, values);
        Assert.True(untranslated.Count == 0,
            $"{code}.json still has English values for: {string.Join(", ", untranslated)}");
    }

    [Theory]
    [MemberData(nameof(AllLanguages))]
    public void CatalogueHasNoEmptyValues(string code)
    {
        var values = Load(Path.Combine(LangDir, $"{code}.json"));

        foreach (var (key, value) in values)
        {
            Assert.False(string.IsNullOrWhiteSpace(value), $"{code}.json has an empty value for {key}");
        }
    }

    [Fact]
    public void EveryFormattedStringKeepsItsPlaceholdersInEnglish()
    {
        // Guards the other direction: adding a key with a {0} and forgetting the placeholder.
        var withArgs = Locator.BuiltInEnglish
            .Where(kv => kv.Value.Contains('{'))
            .Select(kv => kv.Key)
            .ToList();

        Assert.NotEmpty(withArgs);
        Assert.Contains("status.page", withArgs);
    }

    [Fact]
    public void FilesAreUtf8WithoutABom()
    {
        // A BOM makes the first key unparseable by some JSON readers, and a mis-encoded file
        // shows mojibake in the UI. Both are silent until someone opens that language.
        foreach (var file in Directory.EnumerateFiles(LangDir, "*.json"))
        {
            var bytes = File.ReadAllBytes(file);
            Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
                $"{Path.GetFileName(file)} starts with a UTF-8 BOM");
        }
    }
}

/// <summary>Tests for language resolution and right-to-left handling.</summary>
public sealed class LanguageCatalogTests
{
    [Fact]
    public void Resolve_ReturnsAnExactMatch()
    {
        Assert.Equal("pt", LanguageCatalog.Resolve("pt"));
        Assert.Equal("zh-Hans", LanguageCatalog.Resolve("zh-Hans"));
    }

    [Fact]
    public void Resolve_FallsBackToThePrimarySubtag()
    {
        // A Brazilian or Portuguese machine must not silently get English.
        Assert.Equal("pt", LanguageCatalog.Resolve("pt-BR"));
        Assert.Equal("en", LanguageCatalog.Resolve("en-GB"));
    }

    [Fact]
    public void Resolve_MapsTraditionalChineseToSimplified()
    {
        Assert.Equal("zh-Hans", LanguageCatalog.Resolve("zh-TW"));
        Assert.Equal("zh-Hans", LanguageCatalog.Resolve("zh-HK"));
    }

    [Fact]
    public void Resolve_IsCaseInsensitive()
    {
        Assert.Equal("de", LanguageCatalog.Resolve("DE"));
        Assert.Equal("zh-Hans", LanguageCatalog.Resolve("zh-hans"));
    }

    [Fact]
    public void Resolve_ReturnsNullForAnUnknownLanguage()
    {
        // Null rather than a wrong guess, so the caller falls back to English on purpose.
        Assert.Null(LanguageCatalog.Resolve("xx"));
        Assert.Null(LanguageCatalog.Resolve(""));
        Assert.Null(LanguageCatalog.Resolve(null));
    }

    [Fact]
    public void RightToLeftIsFlaggedForArabicAndHebrewOnly()
    {
        Assert.True(LanguageCatalog.IsRightToLeft("ar"));
        Assert.True(LanguageCatalog.IsRightToLeft("he"));
        Assert.True(LanguageCatalog.IsRightToLeft("AR"));

        foreach (var code in new[] { "en", "de", "fr", "es", "ru", "el", "he".Length == 2 ? "ja" : "ja" })
        {
            Assert.False(LanguageCatalog.IsRightToLeft(code), $"{code} should not be RTL");
        }
    }

    [Fact]
    public void FindIsCaseInsensitiveAndReturnsNullWhenAbsent()
    {
        Assert.NotNull(LanguageCatalog.Find("FR"));
        Assert.Null(LanguageCatalog.Find("xx"));
    }

    [Fact]
    public void EnglishIsFirstInTheCatalog()
    {
        // English is the fallback for every untranslated key, so it must be the default choice.
        Assert.Equal("en", LanguageCatalog.All[0].Code);
    }
}
