using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Markup;

namespace EBoard.Core;

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
        ["app.title"] = "EBoard Interactive Whiteboard",
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
