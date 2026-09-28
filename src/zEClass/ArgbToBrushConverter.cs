using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace zEClass;

/// <summary>
/// Converts a 0xAARRGGBB palette entry to a brush for the ink-color picker swatch.
///
/// The picker items are raw color values, so without this the box renders their
/// <c>ToString()</c> (a bare number like 4279966495). Kept as a converter rather than a
/// wrapper type so the existing <c>uint[]</c> palette and its tests stay untouched.
/// </summary>
public sealed class ArgbToBrushConverter : IValueConverter
{
    public static Color ToColor(uint argb) => Color.FromArgb(
        (byte)(argb >> 24),
        (byte)((argb >> 16) & 0xFF),
        (byte)((argb >> 8) & 0xFF),
        (byte)(argb & 0xFF));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is uint argb
            ? new SolidColorBrush(ToColor(argb))
            : DependencyProperty.UnsetValue;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException("The color picker is one-way.");
}
