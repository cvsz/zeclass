using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace zEClass.Tools;

/// <summary>
/// The measuring and drawing instruments from vendor manual section 5.2.2: ruler, compass,
/// set squares, protractor, and a sheet/table for tabulating results.
///
/// Implemented as overlay geometry rather than a separate process, so a tool can be shown
/// while the board stays visible. Each tool is a shape the teacher drags into place, and the
/// result is recorded on the board so it survives save and print.
/// </summary>
public sealed class GeometryToolWindow : Window
{
    private readonly Canvas _canvas = new();
    private Shape? _shape;
    private Point _dragStart;
    private readonly GeometryTool _tool;

    public GeometryToolWindow(GeometryTool tool)
    {
        _tool = tool;
        Title = $"zEClass {tool}";
        Width = 900;
        Height = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Topmost = true;
        ShowInTaskbar = false;
        Background = new SolidColorBrush(Color.FromArgb(0x40, 0x10, 0x12, 0x18));
        Content = _canvas;

        _canvas.MouseLeftButtonDown += OnDown;
        _canvas.MouseMove += OnMove;
        _canvas.MouseLeftButtonUp += OnUp;
        _canvas.MouseRightButtonDown += (_, _) => Close();
    }

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(_canvas);
        _shape = CreateShape(_tool, _dragStart);
        if (_shape is not null)
        {
            _canvas.Children.Add(_shape);
        }

        e.Handled = true;
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (_shape is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var current = e.GetPosition(_canvas);
        Resize(_shape, _tool, _dragStart, current);
        e.Handled = true;
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (_shape is not null)
        {
            // A tool is a transient overlay: releasing the pointer removes it, which keeps the
            // board uncluttered. The drawn result is committed separately by the board.
            _canvas.Children.Remove(_shape);
            _shape = null;
        }

        e.Handled = true;
    }

    private static readonly Brush Stroke = new SolidColorBrush(Color.FromRgb(0x3D, 0x7E, 0xFF));
    private static readonly Brush Fill = new SolidColorBrush(Color.FromArgb(48, 0x3D, 0x7E, 0xFF));

    private static Shape? CreateShape(GeometryTool tool, Point start) => tool switch
    {
        GeometryTool.Ruler => new Rectangle
        {
            Stroke = Stroke,
            Fill = Fill,
            StrokeThickness = 1.5,
            Width = 400,
            Height = 48,
        },
        GeometryTool.Protractor => new Path
        {
            Stroke = Stroke,
            Fill = Fill,
            StrokeThickness = 1.5,
        },
        GeometryTool.SetSquare => new Path
        {
            Stroke = Stroke,
            Fill = Fill,
            StrokeThickness = 1.5,
        },
        GeometryTool.Compass => new Path
        {
            Stroke = Stroke,
            StrokeThickness = 2,
        },
        _ => new Rectangle
        {
            Stroke = Stroke,
            Fill = Fill,
            StrokeThickness = 1.5,
        },
    };

    private static void Resize(Shape shape, GeometryTool tool, Point start, Point current)
    {
        var dx = current.X - start.X;
        var dy = current.Y - start.Y;
        var width = Math.Abs(dx);
        var height = Math.Abs(dy);

        switch (tool)
        {
            case GeometryTool.Ruler:
                shape.Width = Math.Max(80, width);
                shape.Height = 48;
                Canvas.SetLeft(shape, Math.Min(start.X, current.X));
                Canvas.SetTop(shape, start.Y - 24);
                break;

            case GeometryTool.Compass:
                var radius = Math.Max(10, Math.Sqrt((dx * dx) + (dy * dy)));
                ((Path)shape).Data = BuildCompass(start, radius);
                break;

            case GeometryTool.Protractor:
                ((Path)shape).Data = BuildProtractor(start, Math.Max(60, width));
                break;

            case GeometryTool.SetSquare:
                ((Path)shape).Data = BuildSetSquare(start, Math.Max(60, width), Math.Max(60, height));
                break;

            default:
                shape.Width = Math.Max(20, width);
                shape.Height = Math.Max(20, height);
                Canvas.SetLeft(shape, Math.Min(start.X, current.X));
                Canvas.SetTop(shape, Math.Min(start.Y, current.Y));
                break;
        }
    }

    private static Geometry BuildCompass(Point center, double radius)
    {
        var figure = new PathFigure { IsClosed = true, IsFilled = false };
        figure.Segments.Add(new ArcSegment(
            new Point(center.X, center.Y - radius),
            new Size(radius, radius), 0, false, SweepDirection.Clockwise, true));
        figure.StartPoint = figure.Segments[0] is ArcSegment arc ? arc.Point : center;
        return new PathGeometry(new[] { figure });
    }

    private static Geometry BuildProtractor(Point center, double radius)
    {
        var figure = new PathFigure { StartPoint = new Point(center.X - radius, center.Y), IsClosed = true };
        figure.Segments.Add(new ArcSegment(
            new Point(center.X + radius, center.Y),
            new Size(radius, radius), 0, false, SweepDirection.Clockwise, true));
        figure.Segments.Add(new LineSegment(new Point(center.X, center.Y), true));
        figure.Segments.Add(new LineSegment(figure.StartPoint, true));

        var geometry = new PathGeometry(new[] { figure });
        for (var degrees = 0; degrees <= 180; degrees += 10)
        {
            var radians = degrees * Math.PI / 180;
            var inner = new Point(
                center.X + (radius * 0.86 * Math.Cos(radians)),
                center.Y - (radius * 0.86 * Math.Sin(radians)));
            var outer = new Point(center.X + radius, center.Y);
            var tick = new PathFigure { StartPoint = inner, IsClosed = false };
            tick.Segments.Add(new LineSegment(outer, true));
            geometry.Figures.Add(tick);
        }

        geometry.Freeze();
        return geometry;
    }

    private static Geometry BuildSetSquare(Point corner, double width, double height)
    {
        var figure = new PathFigure
        {
            StartPoint = corner,
            IsClosed = true,
            IsFilled = true,
        };
        figure.Segments.Add(new LineSegment(new Point(corner.X + width, corner.Y), true));
        figure.Segments.Add(new LineSegment(new Point(corner.X, corner.Y + height), true));
        figure.Segments.Add(new LineSegment(corner, true));
        var geometry = new PathGeometry(new[] { figure });
        geometry.Freeze();
        return geometry;
    }
}

public enum GeometryTool
{
    Ruler = 0,
    Compass = 1,
    SetSquare = 2,
    Protractor = 3,
    Sheet = 4,
}

/// <summary>
/// Calculator from manual 5.2.2. Deliberately local and offline: a classroom calculator must
/// not reach the network, and a self-contained implementation cannot.
/// </summary>
public sealed class CalculatorWindow : Window
{
    private readonly TextBlock _display;
    private readonly List<Button> _keys = new();
    private double _accumulator;
    private string _pending = string.Empty;
    private double _entry;
    private bool _freshEntry = true;

    public CalculatorWindow()
    {
        Title = "zEClass calculator";
        Width = 320;
        Height = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Topmost = true;
        ShowInTaskbar = false;
        Background = new SolidColorBrush(Color.FromRgb(0x2B, 0x2F, 0x38));

        _display = new TextBlock
        {
            Foreground = Brushes.White,
            FontSize = 32,
            FontFamily = new FontFamily("Consolas"),
            TextAlignment = TextAlignment.Right,
            Margin = new Thickness(12),
            Text = "0",
        };

        var grid = new Grid { Margin = new Thickness(8) };
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(72) });
        for (var i = 0; i < 5; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        }

        var displayBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1C, 0x22)),
            Child = _display,
        };
        Grid.SetRow(displayBorder, 0);
        grid.Children.Add(displayBorder);

        var keys = new string[][]
        {
            new[] { "C", "CE", "←", "÷" },
            new[] { "7", "8", "9", "×" },
            new[] { "4", "5", "6", "−" },
            new[] { "1", "2", "3", "+" },
            new[] { "0", ".", "±", "=" },
        };

        for (var r = 0; r < keys.Length; r++)
        {
            for (var c = 0; c < keys[r].Length; c++)
            {
                var key = MakeKey(keys[r][c]);
                Grid.SetRow(key, r + 1);
                Grid.SetColumn(key, c);
                grid.Children.Add(key);
                _keys.Add(key);
            }
        }

        for (var i = 0; i < 4; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition
                { Width = new GridLength(1, GridUnitType.Star) });
        }

        Content = grid;
    }

    private Button MakeKey(string label)
    {
        var button = new Button
        {
            Content = label,
            Margin = new Thickness(3),
            FontSize = 18,
            Cursor = Cursors.Hand,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(label.Length == 1 && char.IsDigit(label[0])
                ? Color.FromRgb(0x3A, 0x3F, 0x4B)
                : Color.FromRgb(0x2F, 0x59, 0x99)),
        };
        button.Click += (_, _) => Press(label);
        return button;
    }

    private void Press(string key)
    {
        try
        {
            switch (key)
            {
                case "C":
                    _accumulator = 0;
                    _pending = string.Empty;
                    _entry = 0;
                    _freshEntry = true;
                    break;
                case "CE":
                    _entry = 0;
                    _freshEntry = true;
                    break;
                case "←":
                    _entry = Math.Floor(_entry / 10);
                    break;
                case "±":
                    _entry = -_entry;
                    break;
                case "+" or "−" or "×" or "÷":
                    if (_pending.Length > 0)
                    {
                        _accumulator = Evaluate(_accumulator, _entry, _pending);
                    }
                    else
                    {
                        _accumulator = _entry;
                    }

                    _entry = 0;
                    _pending = key;
                    _freshEntry = true;
                    break;
                case "=":
                    if (_pending.Length > 0)
                    {
                        _entry = Evaluate(_accumulator, _entry, _pending);
                        _accumulator = _entry;
                        _pending = string.Empty;
                    }

                    _freshEntry = true;
                    break;
                case ".":
                    if (_freshEntry)
                    {
                        _entry = 0;
                        _freshEntry = false;
                    }

                    if (!Format(_entry).Contains('.'))
                    {
                        _entry += 0.1;
                    }

                    break;
                default:
                    var digit = double.Parse(key, CultureInfo.InvariantCulture);
                    _entry = _freshEntry ? digit : (_entry * 10) + digit;
                    _freshEntry = false;
                    break;
            }

            _display.Text = Format(_entry);
        }
        catch (Exception ex)
        {
            CrashLog.Write("Calculator", ex);
            _display.Text = "Error";
        }
    }

    private static double Evaluate(double a, double b, string op) => op switch
    {
        "+" => a + b,
        "−" => a - b,
        "×" => a * b,
        "÷" => Math.Abs(b) < double.Epsilon ? double.NaN : a / b,
        _ => b,
    };

    private static string Format(double value) =>
        double.IsNaN(value) ? "Error" : value.ToString("0.##########", CultureInfo.InvariantCulture);
}

/// <summary>
/// Table/grid insertion from manual 5.2.2 "SHEET". Produces a ruled table the teacher can fill
/// in, sized to the requested rows and columns.
/// </summary>
public sealed class SheetWindow : Window
{
    public SheetWindow()
    {
        Title = "zEClass sheet";
        Width = 420;
        Height = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = SystemColors.ControlBrush;

        var rows = new TextBox { Text = "5", Width = 60, Margin = new Thickness(4) };
        var columns = new TextBox { Text = "4", Width = 60, Margin = new Thickness(4) };
        var preview = new Border { Height = 160, Background = Brushes.White, Margin = new Thickness(8) };

        void Redraw()
        {
            var r = Clamp(rows.Text, 1, 20);
            var c = Clamp(columns.Text, 1, 10);
            var panel = new Grid
            {
                Background = Brushes.White,
                Margin = new Thickness(4),
            };
            for (var i = 0; i < r; i++)
            {
                panel.RowDefinitions.Add(new RowDefinition());
            }

            for (var i = 0; i < c; i++)
            {
                panel.ColumnDefinitions.Add(new ColumnDefinition());
            }

            for (var i = 0; i < r; i++)
            {
                for (var j = 0; j < c; j++)
                {
                    var cell = new Border
                    {
                        BorderBrush = Brushes.Gray,
                        BorderThickness = new Thickness(0.5),
                        Background = i == 0 ? new SolidColorBrush(Color.FromRgb(0xE8, 0xEA, 0xF0)) : Brushes.White,
                    };
                    Grid.SetRow(cell, i);
                    Grid.SetColumn(cell, j);
                    panel.Children.Add(cell);
                }
            }

            preview.Child = panel;
        }

        rows.TextChanged += (_, _) => Redraw();
        columns.TextChanged += (_, _) => Redraw();
        Redraw();

        var panel2 = new StackPanel { Margin = new Thickness(8) };
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        row.Children.Add(new TextBlock { Text = "Rows", VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(rows);
        row.Children.Add(new TextBlock { Text = "Columns", Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(columns);
        panel2.Children.Add(row);
        panel2.Children.Add(preview);

        var insert = new Button { Content = "Insert on board", Height = 34, Margin = new Thickness(8) };
        insert.Click += (_, _) =>
        {
            if (InsertRequested is not null)
            {
                InsertRequested(Clamp(rows.Text, 1, 20), Clamp(columns.Text, 1, 10));
            }

            Close();
        };
        panel2.Children.Add(insert);
        Content = panel2;
    }

    /// <summary>Raised with the requested row and column counts.</summary>
    public event Action<int, int>? InsertRequested;

    private static int Clamp(string text, int min, int max) =>
        int.TryParse(text, out var value) ? Math.Clamp(value, min, max) : min;
}
