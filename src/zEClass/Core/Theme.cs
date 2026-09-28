using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace zEClass.Core;

/// <summary>Named colour sets for the board chrome.</summary>
public sealed record ThemeColors(
    string Name,
    string ChromeBackground,
    string ChromeForeground,
    string ChromePanel,
    string ChromeBorder,
    string Accent,
    string PageBackground,
    string InkDefault)
{
    public SolidColorBrush ChromeBackgroundBrush => Brush(ChromeBackground);

    public SolidColorBrush ChromeForegroundBrush => Brush(ChromeForeground);

    public SolidColorBrush ChromePanelBrush => Brush(ChromePanel);

    public SolidColorBrush ChromeBorderBrush => Brush(ChromeBorder);

    public SolidColorBrush AccentBrush => Brush(Accent);

    public SolidColorBrush PageBackgroundBrush => Brush(PageBackground);

    public SolidColorBrush InkDefaultBrush => Brush(InkDefault);

    private static SolidColorBrush Brush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}

/// <summary>
/// Board themes.
///
/// The high-contrast theme is not a preference: an interactive panel in a bright classroom is
/// often viewed through a protective glass panel, and low-contrast chrome becomes hard to read.
/// It also gives a teacher usable touch targets, which matters more on a wall panel than on a
/// desk. Default selection follows the OS accessibility setting rather than being hard-coded.
/// </summary>
public sealed class Theme
{
    public static readonly ThemeColors Dark = new(
        "Dark",
        "#FF20232A", "#FFF2F4F8", "#FF2B2F38", "#FF3A3F4B", "#FF3D7EFF",
        "#FFFFFFFF", "#FF1B1B1F");

    public static readonly ThemeColors Light = new(
        "Light",
        "#FFEDEFF3", "#FF16181D", "#FFFFFFFF", "#FFC9CEDA", "#FF1E6FD9",
        "#FFFFFFFF", "#FF16181D");

    public static readonly ThemeColors HighContrast = new(
        "High contrast",
        "#FF000000", "#FFFFFFFF", "#FF000000", "#FFFFFFFF", "#FFFFD400",
        "#FFFFFFFF", "#FF000000");

    public static IReadOnlyList<ThemeColors> All { get; } = [Dark, Light, HighContrast];

    public static ThemeColors Default
    {
        get
        {
            try
            {
                // SystemParameters.HighContrast is the OS-reported flag, which is what the
                // accessibility setting actually drives.
                return SystemParameters.HighContrast ? HighContrast : Dark;
            }
            catch (Exception)
            {
                return Dark;
            }
        }
    }
}
