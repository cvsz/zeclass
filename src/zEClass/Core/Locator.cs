using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Markup;

namespace zEClass.Core;

/// <summary>One translatable string, looked up by key.</summary>
public sealed class Strings
{
    private Dictionary<string, string>? _values = new(StringComparer.Ordinal);

    /// <summary>
    /// Key to text. Never null: the deserializer can leave it unset for a malformed file, and a
    /// null here would take down the whole interface on a lookup.
    /// </summary>
    public Dictionary<string, string> Values
    {
        get => _values ??= new Dictionary<string, string>(StringComparer.Ordinal);
        init => _values = value ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    public static readonly Strings Empty = new();

    public string this[string key] =>
        Values.TryGetValue(key, out var v) ? v : key;

    public bool Has(string key) => Values.ContainsKey(key);

    public string Format(string key, params object?[] args)
    {
        var template = this[key];
        try
        {
            return string.Format(CultureInfo.CurrentUICulture, template, args);
        }
        catch (FormatException)
        {
            // A malformed translation must not blank the UI.
            return template;
        }
    }
}

/// <summary>
/// Localized string lookup with English as the built-in fallback.
///
/// Resource files live beside the executable under <c>lang\*.json</c>. Only a translation file
/// is needed to add a language: a missing key falls back to English, so a partial translation
/// degrades word by word rather than showing raw keys across the interface.
/// </summary>
/// <summary>
/// A language the app can offer, with the metadata the UI needs beyond the code itself.
/// </summary>
public sealed record LanguageInfo(string Code, string EnglishName, string NativeName,
    bool RightToLeft);

/// <summary>
/// The languages the app knows about, beyond the <c>lang\*.json</c> files on disk.
///
/// Two reasons this is a static table rather than derived from the files: the picker should show
/// "Deutsch" and not "de", and a file being present is not the same as a language being
/// <em>ready</em>. Shipping Arabic or Hebrew means mirroring the layout, so those are flagged
/// right-to-left and the window flips rather than leaving a broken mirrored-in-half UI.
/// </summary>
public static class LanguageCatalog
{
    public static readonly IReadOnlyList<LanguageInfo> All =
    [
        new("en", "English", "English", false),
        new("de", "German", "Deutsch", false),
        new("fr", "French", "Français", false),
        new("it", "Italian", "Italiano", false),
        new("es", "Spanish", "Español", false),
        new("pt", "Portuguese", "Português", false),
        new("nl", "Dutch", "Nederlands", false),
        new("pl", "Polish", "Polski", false),
        new("cs", "Czech", "Čeština", false),
        new("sv", "Swedish", "Svenska", false),
        new("da", "Danish", "Dansk", false),
        new("nb", "Norwegian", "Norsk", false),
        new("fi", "Finnish", "Suomi", false),
        new("tr", "Turkish", "Türkçe", false),
        new("el", "Greek", "Ελληνικά", false),
        new("ro", "Romanian", "Română", false),
        new("hu", "Hungarian", "Magyar", false),
        new("ru", "Russian", "Русский", false),
        new("uk", "Ukrainian", "Українська", false),
        new("ar", "Arabic", "العربية", true),
        new("he", "Hebrew", "עברית", true),
        new("zh-Hans", "Chinese (Simplified)", "简体中文", false),
        new("ja", "Japanese", "日本語", false),
        new("ko", "Korean", "한국어", false),
    ];

    public static LanguageInfo? Find(string code) =>
        All.FirstOrDefault(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase));

    public static bool IsRightToLeft(string code) => Find(code)?.RightToLeft ?? false;

    /// <summary>
    /// Resolves a user or OS language to a code we actually ship, for example <c>pt-BR</c> to
    /// <c>pt</c> and <c>zh-TW</c> to <c>zh-Hans</c> if that is what we have. Returns null when
    /// there is no match, so the caller can fall back to English rather than to a wrong language.
    /// </summary>
    public static string? Resolve(string? requested)
    {
        if (string.IsNullOrWhiteSpace(requested))
        {
            return null;
        }

        var code = requested.Trim();
        if (Find(code) is { } exact)
        {
            // Return the canonical code, not what was asked for: "DE" would otherwise become the
            // active language, and the picker and the document would then disagree with the file
            // that was actually loaded.
            return exact.Code;
        }

        var primary = code.Split('-', '_')[0];
        if (Find(primary) is not null)
        {
            return primary;
        }

        // Traditional Chinese has no catalogue here; Simplified is the honest nearest match, and
        // this is documented rather than silently substituted.
        if (string.Equals(primary, "zh", StringComparison.OrdinalIgnoreCase))
        {
            return "zh-Hans";
        }

        return null;
    }
}

/// <summary>
/// Finds gaps in a translated catalogue. Kept separate from <see cref="Locator"/> so the checks
/// are testable without touching the filesystem or the WPF thread.
/// </summary>
public static class CatalogueAudit
{
    /// <summary>Keys present in English but missing from the translation.</summary>
    public static IReadOnlyList<string> Missing(string language, IReadOnlyDictionary<string, string> values) =>
        Locator.BuiltInEnglish.Keys
            .Where(k => !values.ContainsKey(k) || string.IsNullOrWhiteSpace(values[k]))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Keys in the translation that English does not define. These are almost always typos, and
    /// they are worse than missing keys because they look translated while never appearing.
    /// </summary>
    public static IReadOnlyList<string> Unknown(string language, IReadOnlyDictionary<string, string> values) =>
        values.Keys
            .Where(k => !Locator.BuiltInEnglish.ContainsKey(k))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Values identical to English, excluding ones that genuinely are the same word in many
    /// languages. Catches a file that was copied from en.json and never edited.
    /// </summary>
    public static IReadOnlyList<string> Untranslated(string language,
        IReadOnlyDictionary<string, string> values) =>
        values.Keys
            .Where(k => Locator.BuiltInEnglish.TryGetValue(k, out var en) &&
                string.Equals(en, values[k], StringComparison.Ordinal) &&
                !InvariantAcrossLanguages.Contains(k) &&
                !CorrectlyIdentical.Contains($"{language}:{k}"))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Entries where the English value is genuinely the correct form in that language, so an
    /// identical string is right rather than lazy. Listed per language and key rather than as a
    /// blanket rule, because "Oval" is the German word but "Page" is the French one and neither is
    /// true of the other.
    ///
    /// Anything added here is a review decision. If a key ever gets a real translation for that
    /// language, delete the entry and the audit starts checking it again.
    /// </summary>
    private static readonly HashSet<string> CorrectlyIdentical = new(StringComparer.Ordinal)
    {
        "en:app.title", "en:tool.lasso", "en:tool.text",

        // Borrowed unchanged in every catalogue we ship.
        "ar:tool.lasso", "cs:tool.lasso", "da:tool.lasso", "de:tool.lasso", "el:tool.lasso",
        "es:tool.lasso", "fi:tool.lasso", "fr:tool.lasso", "he:tool.lasso", "hu:tool.lasso",
        "it:tool.lasso", "ja:tool.lasso", "ko:tool.lasso", "nb:tool.lasso", "nl:tool.lasso",
        "pl:tool.lasso", "pt:tool.lasso", "ro:tool.lasso", "ru:tool.lasso", "sv:tool.lasso",
        "tr:tool.lasso", "uk:tool.lasso", "zh-Hans:tool.lasso",

        // Same word in that language.
        "cs:tool.text",      // Text
        "de:tool.text",      // Text
        "ro:tool.text",      // Text
        "sv:tool.text",      // Text
        "de:tool.ellipse",   // Oval
        "el:action.email",   // Email
        "fr:tool.capture",   // Capture
        "nl:tool.pen",       // Pen
        "fr:status.page",    // Page {0} / {1}
        "hu:status.page",    // {0} / {1}. oldal
        "ja:status.page",    // {0} / {1} ページ
        "ko:status.page",    // {0} / {1} 페이지
        "ar:status.page",    // صفحة {0} / {1}
    };

    private static readonly HashSet<string> InvariantAcrossLanguages = new(StringComparer.Ordinal);

    /// <summary>
    /// Placeholder arguments must survive translation, otherwise a formatted string throws at
    /// runtime. This is the failure mode that unit tests miss and a teacher sees: a crash or a
    /// stray "{0}" the moment a status line updates.
    /// </summary>
    public static IReadOnlyList<string> PlaceholderMismatch(string language,
        IReadOnlyDictionary<string, string> values)
    {
        var problems = new List<string>();
        foreach (var (key, translated) in values)
        {
            if (!Locator.BuiltInEnglish.TryGetValue(key, out var english))
            {
                continue;
            }

            var expected = Placeholders(english);
            var actual = Placeholders(translated);
            if (!expected.OrderBy(x => x, StringComparer.Ordinal)
                .SequenceEqual(actual.OrderBy(x => x, StringComparer.Ordinal)))
            {
                problems.Add($"{key}: expected [{string.Join(",", expected)}] " +
                    $"but got [{string.Join(",", actual)}]");
            }
        }

        return problems;
    }

    private static IEnumerable<string> Placeholders(string value)
    {
        var i = 0;
        while (i < value.Length)
        {
            if (value[i] != '{')
            {
                i++;
                continue;
            }

            var end = value.IndexOf('}', i);
            if (end < 0)
            {
                yield break;
            }

            var body = value.Substring(i + 1, end - i - 1);
            // Keep the index and any format specifier, drop the alignment filler.
            var colon = body.IndexOf(':');
            yield return colon >= 0 ? body.Substring(0, colon) : body;
            i = end + 1;
        }
    }
}

public sealed class Locator
{
    /// <summary>
    /// The built-in English catalogue. Exposed so a loaded translation can be layered on top of
    /// it rather than replacing it, which is what makes a partial translation usable.
    ///
    /// Declaration order matters: this must be initialized before <c>Current</c> below, because
    /// static field initializers run top to bottom and the Locator constructor copies this
    /// dictionary. Getting that order wrong yields an empty catalogue and every lookup returning
    /// its own key.
    /// </summary>
    public static Dictionary<string, string> BuiltInEnglish { get; } = new(StringComparer.Ordinal)
    {
        ["app.title"] = "zEClass Interactive Whiteboard",
        ["tool.pen"] = "Pen",
        ["tool.highlighter"] = "Mark",
        ["tool.eraser"] = "Erase",
        ["tool.line"] = "Line",
        ["tool.rect"] = "Rect",
        ["tool.ellipse"] = "Oval",
        ["tool.triangle"] = "Tri",
        ["tool.arrow"] = "Arrow",
        ["tool.star"] = "Star",
        ["tool.text"] = "Text",
        ["tool.select"] = "Select",
        ["tool.lasso"] = "Lasso",
        ["tool.pan"] = "Pan",
        ["action.undo"] = "Undo",
        ["action.redo"] = "Redo",
        ["action.clear"] = "Clear",
        ["action.new"] = "New",
        ["action.open"] = "Open",
        ["action.import"] = "Import",
        ["action.save"] = "Save",
        ["action.saveas"] = "Save as",
        ["action.export"] = "Export",
        ["action.print"] = "Print",
        ["action.email"] = "Email",
        ["action.fullscreen"] = "Full screen",
        ["action.align"] = "Align",
        ["action.calibrate"] = "Calibrate board",
        ["action.diagnostics"] = "Diagnostics",
        ["action.saveReport"] = "Save report",
        ["tool.magnifier"] = "Zoom tool",
        ["tool.spotlight"] = "Spotlight",
        ["tool.curtain"] = "Curtain",
        ["tool.clock"] = "Clock",
        ["tool.keyboard"] = "Keys",
        ["tool.capture"] = "Capture",
        ["status.page"] = "Page {0} / {1}",
        ["status.strokes"] = "{0} strokes",
        ["status.calibrated"] = "calibrated",
        ["status.locked"] = "locked",
        ["msg.calibrateHint"] =
            "Calibration: touch the centre of each cross with the pen or your finger.",
        ["msg.calibrateFailed"] =
            "That calibration did not produce a usable mapping. Try again and hit each " +
            "cross near its centre.",
        ["msg.calibrateApplied"] = "Calibrated. Worst error {0:F1} px.",
        ["msg.recognitionCircle"] = "Circle recognised: spotlight.",
        ["msg.recognitionSquare"] = "Square recognised: magnifier.",
        ["msg.pageLocked"] = "This page is locked. Unlock it before clearing.",
        ["import.failed"] = "Nothing could be imported.",
    };

    private static Locator _current = new();

    private readonly Dictionary<string, Strings> _catalogues = new(StringComparer.OrdinalIgnoreCase);

    public Locator()
    {
        if (BuiltInEnglish.Count == 0)
        {
            throw new InvalidOperationException(
                "The English catalogue is empty; the static initializer order is wrong.");
        }

        _catalogues["en"] = new Strings { Values = new Dictionary<string, string>(
            BuiltInEnglish, StringComparer.Ordinal) };
    }

    public static Locator Current => _current;

    public static event EventHandler? LanguageChanged;

    public string Language { get; private set; } = "en";

    public Strings Strings => _catalogues.TryGetValue(Language, out var s) ? s : _catalogues["en"];

    public IReadOnlyList<string> AvailableLanguages
    {
        get
        {
            var names = _catalogues.Keys.ToList();
            names.Sort(StringComparer.Ordinal);
            return names;
        }
    }

    /// <summary>Loads every <c>lang\*.json</c> beside the executable, ignoring bad files.</summary>
    public void LoadFromDisk(string? folder = null)
    {
        folder ??= Path.Combine(AppContext.BaseDirectory, "lang");
        try
        {
            if (!Directory.Exists(folder))
            {
                return;
            }

            foreach (var file in Directory.EnumerateFiles(folder, "*.json"))
            {
                var code = Path.GetFileNameWithoutExtension(file);
                try
                {
                    var json = File.ReadAllText(file);
                    var strings = JsonSerializer.Deserialize<Strings>(json);
                    if (strings is { Values.Count: > 0 })
                    {
                        // Merge over English rather than replacing it, so a partial translation
                        // degrades word by word instead of blanking every untranslated label.
                        var merged = new Dictionary<string, string>(BuiltInEnglish, StringComparer.Ordinal);
                        foreach (var (key, value) in strings.Values)
                        {
                            if (!string.IsNullOrEmpty(value))
                            {
                                merged[key] = value;
                            }
                        }

                        _catalogues[code] = new Strings { Values = merged };
                    }
                }
                catch (Exception ex) when (ex is JsonException or IOException)
                {
                    CrashLog.Info($"Language file {code} not loaded: {ex.Message}");
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashLog.Info($"Language folder not read: {ex.Message}");
        }
    }

    public bool SetLanguage(string code)
    {
        if (string.IsNullOrWhiteSpace(code) || !_catalogues.ContainsKey(code))
        {
            return false;
        }

        Language = code;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo(code);
        }
        catch (CultureNotFoundException)
        {
        }

        LanguageChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Short form of a lookup. A C# indexer cannot be static, so this is a method rather than
    /// <c>Locator[key]</c>.
    /// </summary>
    public static string T(string key) => Current.Strings[key];

    public static string Format(string key, params object?[] args) =>
        Current.Strings.Format(key, args);
}

/// <summary>
/// Markup extension so XAML can bind a string: <c>Text="{loc:Str tool.pen}"</c>.
/// The value updates when the language changes, which is what a runtime language switch needs.
/// </summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class StrExtension : MarkupExtension
{
    public StrExtension()
    {
    }

    public StrExtension(string key) => Key = key;

    public string? Key { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (string.IsNullOrEmpty(Key))
        {
            return string.Empty;
        }

        if (serviceProvider?.GetService(typeof(ILocalizable)) is ILocalizable target)
        {
            return target.Localize(Key);
        }

        return Locator.T(Key);
    }
}

/// <summary>Implemented by windows that re-read localized text when the language changes.</summary>
public interface ILocalizable
{
    string Localize(string key);

    void RefreshLocalization();
}
