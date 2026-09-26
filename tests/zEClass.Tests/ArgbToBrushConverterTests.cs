using System;
using System.Windows;
using System.Windows.Media;
using Xunit;

namespace zEClass.Tests;

/// <summary>
/// Tests for the palette color converter.
///
/// The converter is the only thing standing between a raw uint and a visible color swatch, so
/// the channel order is asserted exactly: a swapped red and blue channel would still render
/// *something*, and nobody would catch it except a test.
/// </summary>
public sealed class ArgbToBrushConverterTests
{
    [Fact]
    public void ToColor_SplitsArgbChannelsInOrder()
    {
        var color = ArgbToBrushConverter.ToColor(0xFF1B1B1F);

        Assert.Equal(0xFF, color.A);
        Assert.Equal(0x1B, color.R);
        Assert.Equal(0x1B, color.G);
        Assert.Equal(0x1F, color.B);
    }

    [Fact]
    public void ToColor_PreservesTransparency()
    {
        Assert.Equal(0x00, ArgbToBrushConverter.ToColor(0x00FFFFFF).A);
        Assert.Equal(0x80, ArgbToBrushConverter.ToColor(0x80FFFFFF).A);
    }

    [Fact]
    public void Convert_ReturnsABrushWithTheRightColor()
    {
        var converter = new ArgbToBrushConverter();

        var brush = Assert.IsType<SolidColorBrush>(converter.Convert(
            0xFFFF0000u, typeof(Brush), null!, System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(Colors.Red, brush.Color);
    }

    [Fact]
    public void Convert_RejectsNonColors()
    {
        var converter = new ArgbToBrushConverter();

        Assert.Equal(DependencyProperty.UnsetValue, converter.Convert(
            "not a color", typeof(Brush), null!, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(DependencyProperty.UnsetValue, converter.Convert(
            null!, typeof(Brush), null!, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ConvertBack_AlwaysThrows()
    {
        var converter = new ArgbToBrushConverter();

        Assert.Throws<NotSupportedException>(() => converter.ConvertBack(
            Brushes.Red, typeof(uint), null!, System.Globalization.CultureInfo.InvariantCulture));
    }
}
