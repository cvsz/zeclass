using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using zEClass.Core;

namespace zEClass;

/// <summary>
/// Applies a <see cref="ThemeColors"/> to the live application resources, so every element
/// bound to the palette keys follows. Colours are written into the resource dictionary rather
/// than a style trigger set, because that keeps one code path for all three themes.
/// </summary>
public static class ThemeManager
{
    public static event EventHandler<ThemeColors>? ThemeChanged;

    public static ThemeColors Current { get; private set; } = Theme.Default;

    public static void Apply(ThemeColors theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        Current = theme;

        if (Application.Current is null)
        {
            return;
        }

        Set("ChromeBackground", theme.ChromeBackgroundBrush);
        Set("ChromeForeground", theme.ChromeForegroundBrush);
        Set("ChromeAccent", theme.AccentBrush);
        Set("ChromePanel", theme.ChromePanelBrush);
        Set("ChromeBorder", theme.ChromeBorderBrush);
        Set("BoardVoid", theme.ChromeBackgroundBrush);

        ThemeChanged?.Invoke(null, theme);
    }

    public static void ApplyByName(string name)
    {
        var theme = Theme.All.FirstOrDefault(t =>
            string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
        if (theme is not null)
        {
            Apply(theme);
        }
    }

    private static void Set(string key, Brush brush)
    {
        var resources = Application.Current.Resources;
        if (resources.MergedDictionaries.Count > 0)
        {
            var dictionary = resources.MergedDictionaries[0];
            dictionary[key] = brush;
            return;
        }

        resources[key] = brush;
    }

    /// <summary>Touch target size, enlarged on a wall panel where a finger is the input device.</summary>
    public static double TouchTargetSize =>
        Current == Theme.HighContrast ? 64 : 52;
}
