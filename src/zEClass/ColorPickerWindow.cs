using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace zEClass;

/// <summary>
/// Minimal colour picker. WPF has no built-in one and the vendor's colour set is small, so
/// this offers a swatch grid plus an HSV picker, which is enough to choose any colour a
/// teacher needs without pulling in a dependency.
/// </summary>
public sealed class ColorPickerWindow : Window
{
    private static readonly uint[] Swatches =
    [
        0xFF000000, 0xFF404040, 0xFF808080, 0xFFC0C0C0, 0xFFFFFFFF, 0xFFF44336,
        0xFFE91E63, 0xFF9C27B0, 0xFF673AB7, 0xFF3F51B5, 0xFF2196F3, 0xFF03A9F4,
        0xFF00BCD4, 0xFF009688, 0xFF4CAF50, 0xFF8BC34A, 0xFFCDDC39, 0xFFFFEB3B,
        0xFFFFC107, 0xFFFF9800, 0xFFFF5722, 0xFF795548, 0xFF607D8B, 0xFF6D4C41,
    ];

    private readonly TextBox _hex = new();
    private readonly Slider _hue = new() { Minimum = 0, Maximum = 360, Value = 0, Margin = new Thickness(8) };
    private readonly Slider _sat = new() { Minimum = 0, Maximum = 1, Value = 1, Margin = new Thickness(8) };
    private readonly Slider _val = new() { Minimum = 0, Maximum = 1, Value = 1, Margin = new Thickness(8) };
    private readonly Border _preview = new();
    private Color _current = Colors.Black;

    public ColorPickerWindow(Color initial)
    {
        Title = "Choose a colour";
        Width = 360;
        Height = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(Color.FromRgb(0xF5, 0xF6, 0xF8));
        _current = initial;
        _preview.Background = new SolidColorBrush(initial);
        _hex.Text = ToHex(initial);
        _hex.Margin = new Thickness(8, 4, 8, 8);

        var root = new StackPanel { Margin = new Thickness(8) };
        var grid = new WrapPanel();
        foreach (var argb in Swatches)
        {
            var swatchColor = Color.FromRgb((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
            var swatch = MakeSwatch(swatchColor);
            swatch.MouseLeftButtonUp += (_, _) =>
            {
                _current = swatchColor;
                _preview.Background = new SolidColorBrush(swatchColor);
                _hex.Text = ToHex(swatchColor);
                SelectedColor = swatchColor;
            };
            grid.Children.Add(swatch);
        }

        var field = new Border
        {
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            BorderBrush = Brushes.Gray,
            Height = 44,
            Margin = new Thickness(8, 8, 8, 8),
            Child = _preview,
        };

        root.Children.Add(new TextBlock { Text = "Swatches", Margin = new Thickness(8, 8, 8, 4) });
        root.Children.Add(grid);
        root.Children.Add(new TextBlock { Text = "Custom", Margin = new Thickness(8, 8, 8, 4) });
        root.Children.Add(field);
        root.Children.Add(Labelled("Hue", _hue));
        root.Children.Add(Labelled("Saturation", _sat));
        root.Children.Add(Labelled("Brightness", _val));
        root.Children.Add(_hex);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(8),
        };
        var cancel = new Button { Content = "Cancel", Width = 84, Height = 30, Margin = new Thickness(4) };
        cancel.Click += (_, _) => { DialogResult = false; };
        var ok = new Button { Content = "OK", Width = 84, Height = 30, Margin = new Thickness(4) };
        ok.Click += (_, _) => { DialogResult = true; };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        root.Children.Add(buttons);

        _hue.ValueChanged += (_, _) => ApplyHsv();
        _sat.ValueChanged += (_, _) => ApplyHsv();
        _val.ValueChanged += (_, _) => ApplyHsv();
        _hex.LostFocus += (_, _) => ApplyHex();

        Content = root;
    }

    public Color SelectedColor { get; private set; }

    private static FrameworkElement Labelled(string label, Slider slider)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(8, 4, 8, 0) });
        panel.Children.Add(slider);
        return panel;
    }

    private static Border MakeSwatch(Color color) => new()
    {
        Width = 34,
        Height = 34,
        Margin = new Thickness(3),
        CornerRadius = new CornerRadius(3),
        Background = new SolidColorBrush(color),
        BorderBrush = Brushes.Gray,
        BorderThickness = new Thickness(1),
        Cursor = Cursors.Hand,
        ToolTip = ToHex(color),
    };

    private void ApplyHsv()
    {
        var h = _hue.Value / 360.0;
        var s = _sat.Value;
        var v = _val.Value;
        var c = v * s;
        var hp = h * 6;
        var x = c * (1 - Math.Abs((hp % 2) - 1));
        double r1, g1, b1;
        if (hp < 1) { r1 = c; g1 = x; b1 = 0; }
        else if (hp < 2) { r1 = x; g1 = c; b1 = 0; }
        else if (hp < 3) { r1 = 0; g1 = c; b1 = x; }
        else if (hp < 4) { r1 = 0; g1 = x; b1 = c; }
        else if (hp < 5) { r1 = x; g1 = 0; b1 = c; }
        else { r1 = c; g1 = 0; b1 = x; }

        var m = v - c;
        var color = Color.FromRgb(
            (byte)Math.Round((r1 + m) * 255),
            (byte)Math.Round((g1 + m) * 255),
            (byte)Math.Round((b1 + m) * 255));
        _current = color;
        _preview.Background = new SolidColorBrush(color);
        _hex.Text = ToHex(color);
        SelectedColor = color;
    }

    private void ApplyHex()
    {
        var text = _hex.Text?.Trim().TrimStart('#') ?? string.Empty;
        if (text.Length == 6 && uint.TryParse(text, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var rgb))
        {
            _current = Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
            _preview.Background = new SolidColorBrush(_current);
            SelectedColor = _current;
        }
        else
        {
            _hex.Text = ToHex(_current);
        }
    }

    public static string ToHex(Color c) => $"{c.R:X2}{c.G:X2}{c.B:X2}";
}
