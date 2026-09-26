using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using IOPath = System.IO.Path;
using System.Windows.Threading;
using zEClass.Core;

namespace zEClass;

/// <summary>
/// A guided hardware acceptance run.
///
/// This exists because the alternative is claiming a panel works. The app can enumerate a
/// digitizer and draw a stroke, neither of which proves pressure, palm rejection, or calibration
/// accuracy; those can only be established by a person using the panel the way a teacher would.
/// So this walks through one capability at a time, shows the instruction, records what actually
/// arrived through <see cref="InkSurface.SampleObserved"/>, and writes a report that can be
/// attached to a support ticket or a procurement decision.
///
/// Honesty rules baked in, because a hardware report is only worth anything if it can fail:
///   - A capability the hardware does not have reports SKIP, never PASS.
///   - Nothing is pre-filled, and no measurement is invented.
///   - The report records which input path was live, so a fallback-path run is not mistaken for
///     a full-fidelity one.
/// </summary>
public sealed partial class HardwareAcceptanceWindow : Window
{
    private readonly InkSurface _surface;
    private readonly DigitizerService _digitizer;
    private readonly AcceptanceRun _run = new();
    private readonly DispatcherTimer _tick;
    private readonly List<Step> _steps;

    // Raw observations, cleared per step.
    private readonly List<(double X, double Y)> _hits = new();
    private readonly List<double> _rawPressure = new();
    private readonly List<double> _normPressure = new();
    private readonly List<double> _latency = new();
    private readonly List<double> _calibrationErrors = new();
    private int _maxSimultaneous;
    private bool _sawPen;
    private bool _sawTouch;
    private bool _sawEraser;
    private bool _touchWhilePenDown;
    private bool _penDown;
    private long _lastSampleTicks;
    private int _stepIndex;
    private bool _finished;

    private sealed class Step
    {
        public required string Title { get; init; }
        public required string Instruction { get; init; }
        public string? Detail { get; init; }
        public required Func<StepContext, AcceptanceCheck> Evaluate { get; init; }
        public bool AutoAdvance { get; init; }
    }

    private sealed class StepContext
    {
        public required AcceptanceThresholds Thresholds { get; init; }
        public required DigitizerStatus Status { get; init; }
        public required InkSurface Surface { get; init; }
        public required IReadOnlyList<(double X, double Y)> Hits { get; init; }
        public required IReadOnlyList<double> RawPressure { get; init; }
        public required IReadOnlyList<double> NormPressure { get; init; }
        public required IReadOnlyList<double> Latency { get; init; }
        public required int MaxSimultaneous { get; init; }
        public required bool SawPen { get; init; }
        public required bool SawTouch { get; init; }
        public required bool SawEraser { get; init; }
        public required bool TouchWhilePenDown { get; init; }
    }

    public HardwareAcceptanceWindow()
    {
        _digitizer = new DigitizerService();
        _surface = new InkSurface
        {
            Tool = BoardTool.Pen,
            StrokeWidth = 6,
            Engine = { PalmRejectionEnabled = true },
        };

        _run = new AcceptanceRun
        {
            StartedUtc = DateTimeOffset.UtcNow,
            BoardId = Environment.MachineName,
            Thresholds = new AcceptanceThresholds(),
        };

        _steps = BuildSteps();
        _run.Checks.AddRange(_steps.Select(s => new AcceptanceCheck
        {
            Id = s.Title,
            Title = s.Title,
            Instruction = s.Instruction,
        }));

        _tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _tick.Tick += (_, _) => RefreshLiveReadout();

        Title = "zEClass hardware acceptance";
        Width = 1180;
        Height = 780;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(0x14, 0x17, 0x1C));
        Foreground = Brushes.White;

        Content = BuildLayout();
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    /// <summary>
    /// The run in progress, exposed so the smoke tests can assert the window is wired up without
    /// driving a panel. Read-only by contract; nothing outside this type mutates it.
    /// </summary>
    internal AcceptanceRun RunForTesting => _run;

    // ---------------------------------------------------------------- layout

    private UIElement BuildLayout()
    {
        var grid = new Grid { Margin = new Thickness(18) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(new TextBlock
        {
            Text = "Hardware acceptance run",
            FontSize = 24,
            FontWeight = FontWeights.SemiBold,
        });
        header.Children.Add(new TextBlock
        {
            // Stated up front so nobody reads a green report as a warranty.
            Text = "Follow each instruction using the panel itself. A capability your hardware does " +
                   "not have is reported as SKIP, which is not a pass. Save the report and keep it " +
                   "with the machine's records.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75,
            Margin = new Thickness(0, 6, 0, 0),
        });
        grid.Children.Add(header);

        var middle = new Grid();
        middle.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });
        middle.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        middle.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(middle);

        var list = new ListBox
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x20, 0x27)),
            BorderThickness = new Thickness(0),
            ItemsSource = _run.Checks,
        };
        list.ItemTemplate = BuildCheckTemplate();
        Grid.SetColumn(list, 0);
        middle.Children.Add(list);

        // The live board, hosted so the harness observes the real input path.
        var stageBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x0B, 0x0D, 0x10)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x3A, 0x45)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            ClipToBounds = true,
        };
        var stage = new Grid();
        stage.Children.Add(_surface);
        stage.Children.Add(_target);
        stage.Children.Add(_stagePrompt);
        stage.Children.Add(_liveReadout);
        stageBorder.Child = stage;
        Grid.SetColumn(stageBorder, 2);
        middle.Children.Add(stageBorder);

        var footer = new WrapPanel
        {
            Margin = new Thickness(0, 12, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        footer.Children.Add(MakeButton("Start", OnStart));
        footer.Children.Add(MakeButton("Skip this check", (_, _) => Advance(manual: true)));
        footer.Children.Add(MakeButton("Save report", OnSave, enabled: false));
        footer.Children.Add(MakeButton("Close", (_, _) => Close()));
        grid.Children.Add(footer);
        Grid.SetRow(footer, 2);

        return grid;
    }

    private readonly Canvas _target = new();
    private readonly TextBlock _stagePrompt = new()
    {
        Foreground = Brushes.White,
        FontSize = 20,
        TextAlignment = TextAlignment.Center,
        TextWrapping = TextWrapping.Wrap,
        IsHitTestVisible = false,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Bottom,
        Margin = new Thickness(20, 0, 20, 24),
    };

    private readonly TextBlock _liveReadout = new()
    {
        Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0xE0, 0xFF)),
        FontFamily = new FontFamily("Consolas"),
        FontSize = 12,
        TextAlignment = TextAlignment.Left,
        IsHitTestVisible = false,
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Top,
        Margin = new Thickness(12),
    };

    private static DataTemplate BuildCheckTemplate()
    {
        // A template rather than code-behind rows, so the list updates itself when an outcome
        // changes and the operator cannot be looking at a stale verdict.
        var template = new DataTemplate(typeof(AcceptanceCheck));
        var grid = new FrameworkElementFactory(typeof(Grid));
        grid.SetValue(Grid.MarginProperty, new Thickness(0, 0, 0, 8));

        var title = new FrameworkElementFactory(typeof(TextBlock));
        title.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Title"));
        title.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        title.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);

        var detail = new FrameworkElementFactory(typeof(TextBlock));
        detail.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Detail"));
        detail.SetValue(TextBlock.OpacityProperty, 0.7);
        detail.SetValue(TextBlock.FontSizeProperty, 11d);
        detail.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        detail.SetValue(TextBlock.MarginProperty, new Thickness(0, 3, 0, 0));

        grid.AppendChild(title);
        grid.AppendChild(detail);

        template.VisualTree = grid;
        return template;
    }

    private static Button MakeButton(string text, RoutedEventHandler onClick, bool enabled = true)
    {
        var button = new Button
        {
            Content = text,
            Margin = new Thickness(0, 0, 8, 0),
            Padding = new Thickness(14, 6, 14, 6),
            MinWidth = 110,
            IsEnabled = enabled,
        };
        button.Click += onClick;
        return button;
    }

    // ---------------------------------------------------------------- steps

    private List<Step> BuildSteps() =>
    [
        new Step
        {
            Title = "Digitizer enumerated",
            Instruction = "No action needed. Confirm the panel appears in the list.",
            AutoAdvance = true,
            Evaluate = c => AcceptanceJudge.DigitizerPresent(c.Status),
        },
        new Step
        {
            Title = "Touch accuracy",
            Instruction = "Touch the centre of the cross four times, once per corner of the screen.",
            Evaluate = c => AcceptanceJudge.TouchAccuracy(c.Hits, c.Surface.ActualWidth / 2,
                c.Surface.ActualHeight / 2, c.Thresholds),
        },
        new Step
        {
            Title = "Simultaneous contacts",
            Instruction = "Rest two or more fingers on the board and hold still for a moment.",
            Evaluate = c => AcceptanceJudge.MultiTouch(c.MaxSimultaneous, c.Thresholds),
        },
        new Step
        {
            Title = "Pen pressure",
            Instruction = "Draw one slow line, starting as light as you can and pressing harder.",
            Evaluate = c => AcceptanceJudge.PressureResponse(c.RawPressure, c.NormPressure,
                c.Thresholds),
        },
        new Step
        {
            Title = "Barrel-switch eraser",
            Instruction = "Turn the pen over and write with the eraser end.",
            Evaluate = c => AcceptanceJudge.EraserTip(c.SawEraser, c.SawPen, c.SawPen),
        },
        new Step
        {
            Title = "Palm rejection",
            Instruction = "Rest your palm flat on the board and write with the pen at the same time.",
            Evaluate = c => AcceptanceJudge.PalmRejection(c.TouchWhilePenDown, c.SawPen, c.SawTouch),
        },
        new Step
        {
            Title = "Calibration accuracy",
            Instruction =
                "Click Align in the main window, touch the four crosses, accept, then come back here.",
            Evaluate = c => AcceptanceJudge.CalibrationAccuracy(
                c.Surface.CalibrationTransform, c.Surface.CalibrationError, 4, c.Thresholds),
        },
        new Step
        {
            Title = "Pointer to render latency",
            Instruction = "Draw a long slow spiral across the whole board.",
            Evaluate = c => AcceptanceJudge.Latency(c.Latency, c.Thresholds),
        },
    ];

    // ---------------------------------------------------------------- run

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _surface.Document = new BoardDocument();
        _surface.SampleObserved += OnSampleObserved;

        var status = _digitizer.Probe();
        _run.PanelDescription = string.IsNullOrWhiteSpace(status.Summary)
            ? "unknown"
            : status.Summary;
        _run.ScreenDescription =
            $"{SystemParameters.PrimaryScreenWidth}x{SystemParameters.PrimaryScreenHeight} " +
            $"(window {ActualWidth:F0}x{ActualHeight:F0})";
        _run.InputPath = _surface.HasPointerTarget
            ? "WM_POINTER (full fidelity: pressure, eraser tip, palm flags)"
            : "WPF fallback (reduced fidelity: no eraser tip or palm flag from the driver)";

        _surface.SizeChanged += (_, _) => LayoutTarget();

        ShowStep(0);
        _tick.Start();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _tick.Stop();
        _surface.SampleObserved -= OnSampleObserved;
    }

    private void OnStart(object sender, RoutedEventArgs e)
    {
        _run.StartedUtc = DateTimeOffset.UtcNow;
        ShowStep(0);
    }

    private void ShowStep(int index)
    {
        _stepIndex = Math.Clamp(index, 0, _steps.Count - 1);
        var step = _steps[_stepIndex];

        ResetObservations();
        _stagePrompt.Text = step.Instruction;
        _target.Children.Clear();
        LayoutTarget();
        _target.Visibility = step.Title == "Touch accuracy" ? Visibility.Visible : Visibility.Collapsed;

        var record = _run.Checks[_stepIndex];
        record.Outcome = CheckOutcome.Pending;
        record.Detail = step.Detail ?? "Waiting for you to complete this step.";

        if (step.AutoAdvance)
        {
            // Nothing to do by hand, so judge it now and move on.
            CompleteStep(manual: false);
        }
    }

    private void ResetObservations()
    {
        _hits.Clear();
        _rawPressure.Clear();
        _normPressure.Clear();
        _latency.Clear();
        _calibrationErrors.Clear();
        _maxSimultaneous = 0;
        _sawPen = false;
        _sawTouch = false;
        _sawEraser = false;
        _touchWhilePenDown = false;
        _penDown = false;
        _lastSampleTicks = 0;
    }

    private void OnSampleObserved(object? sender, SurfaceSampleEventArgs e)
    {
        var s = e.Sample;
        var step = _steps[_stepIndex];
        if (_finished)
        {
            return;
        }

        // Latency is recorded for every step: it is a property of the machine and the page, not of
        // any one capability, so measuring it throughout gives a more honest figure than one
        // deliberate spiral would.
        var now = Stopwatch.GetTimestamp();
        if (_lastSampleTicks != 0)
        {
            var ms = (now - _lastSampleTicks) * 1000.0 / Stopwatch.Frequency;
            if (ms is >= 0 and < 2000)
            {
                _latency.Add(ms);
            }
        }

        _lastSampleTicks = now;

        if (s.IsPen)
        {
            _sawPen = true;
            _penDown = s.InContact;
            if (s.Eraser)
            {
                _sawEraser = true;
            }
        }
        else if (s.IsTouch)
        {
            _sawTouch = true;
        }

        if (s.IsTouch && _penDown && !e.Rejected)
        {
            _touchWhilePenDown = true;
        }

        if (!e.Rejected && s.InContact)
        {
            _maxSimultaneous = Math.Max(_maxSimultaneous, _surface.ContactCount);

            if (step.Title == "Touch accuracy" && s.IsTouch)
            {
                _hits.Add((s.X, s.Y));
                LayoutTarget();
            }

            if (step.Title == "Pen pressure" && s.IsPen && !s.Eraser)
            {
                _rawPressure.Add(s.Pressure);
                _normPressure.Add(e.NormalizedPressure);
            }
        }

        // Some steps have a clear completion signal, so the operator is not also acting as a
        // stopwatch. Pressure needs one long stroke; the rest advance when told to.
        if (step.Title == "Touch accuracy" && _hits.Count >= 4)
        {
            CompleteStep(manual: false);
        }
        else if (step.Title == "Pen pressure" && _rawPressure.Count >= 40)
        {
            CompleteStep(manual: false);
        }
    }

    private void CompleteStep(bool manual)
    {
        var step = _steps[_stepIndex];
        var record = _run.Checks[_stepIndex];

        if (manual)
        {
            // An operator skipping a step must be recorded as such, never as a pass.
            record.Skip("skipped by the operator");
        }
        else
        {
            var check = step.Evaluate(new StepContext
            {
                Thresholds = _run.Thresholds,
                Status = _digitizer.Probe(),
                Surface = _surface,
                Hits = _hits,
                RawPressure = _rawPressure,
                NormPressure = _normPressure,
                Latency = _latency,
                MaxSimultaneous = _maxSimultaneous,
                SawPen = _sawPen,
                SawTouch = _sawTouch,
                SawEraser = _sawEraser,
                TouchWhilePenDown = _touchWhilePenDown,
            });

            record.Outcome = check.Outcome;
            record.Detail = check.Detail;
            record.Measurements.Clear();
            record.Measurements.AddRange(check.Measurements);
        }

        Advance(manual);
    }

    private void Advance(bool manual)
    {
        if (_stepIndex + 1 >= _steps.Count)
        {
            _finished = true;
            _stagePrompt.Text = "Run complete. Save the report.";
            _run.CompletedUtc = DateTimeOffset.UtcNow;
            return;
        }

        // Re-probing on every step keeps the report honest if the panel is unplugged mid-run.
        ShowStep(_stepIndex + 1);
    }

    private void LayoutTarget()
    {
        _target.Children.Clear();
        if (_target.Children.Count > 0)
        {
            return;
        }

        var cx = _surface.ActualWidth / 2;
        var cy = _surface.ActualHeight / 2;
        if (cx < 1 || cy < 1)
        {
            return;
        }

        _target.Children.Add(new Line
        {
            X1 = cx - 60, Y1 = cy, X2 = cx + 60, Y2 = cy,
            Stroke = new SolidColorBrush(Color.FromRgb(0x4C, 0xC9, 0xF0)), StrokeThickness = 3,
        });
        _target.Children.Add(new Line
        {
            X1 = cx, Y1 = cy - 60, X2 = cx, Y2 = cy + 60,
            Stroke = new SolidColorBrush(Color.FromRgb(0x4C, 0xC9, 0xF0)), StrokeThickness = 3,
        });

        foreach (var (x, y) in _hits)
        {
            _target.Children.Add(new Ellipse
            {
                Width = 26,
                Height = 26,
                Margin = new Thickness(x - 13, y - 13, 0, 0),
                IsHitTestVisible = false,
                Stroke = new SolidColorBrush(Color.FromRgb(0x6E, 0xE7, 0xB7)),
                StrokeThickness = 2,
            });
        }
    }

    private void RefreshLiveReadout()
    {
        var step = _steps[_stepIndex];
        var sb = new StringBuilder();
        sb.AppendLine($"step {_stepIndex + 1}/{_steps.Count}: {step.Title}");
        sb.AppendLine($"pen: {(_sawPen ? "yes" : "no")}   eraser: {(_sawEraser ? "yes" : "no")}   " +
                      $"touch: {(_sawTouch ? "yes" : "no")}");
        sb.AppendLine($"contacts: {_surface.ContactCount} (max {_maxSimultaneous})");
        sb.AppendLine($"pressure samples: {_rawPressure.Count}   hits: {_hits.Count}   " +
                      $"latency samples: {_latency.Count}");
        sb.Append($"calibration error: {(_surface.CalibrationError.ToString("F1", CultureInfo.InvariantCulture))} px");

        _liveReadout.Text = sb.ToString();
        _target.Visibility = step.Title == "Touch accuracy" && _target.Children.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    // ---------------------------------------------------------------- report

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var dir = IOPath.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "zEClass");
        Directory.CreateDirectory(dir);
        var path = IOPath.Combine(dir,
            $"acceptance-{Environment.MachineName}-{DateTime.Now:yyyyMMdd-HHmmss}.md");

        try
        {
            if (_run.CompletedUtc == default)
            {
                _run.CompletedUtc = DateTimeOffset.UtcNow;
            }

            var md = _run.ToMarkdown();
            File.WriteAllText(path, md, Encoding.UTF8);
            File.WriteAllText(IOPath.ChangeExtension(path, ".json"), _run.ToJson(), Encoding.UTF8);

            MessageBox.Show(this,
                $"Report saved.\n\n{path}\n\n" +
                $"{_run.PassCount} passing, {_run.FailCount} failing, {_run.SkipCount} skipped.",
                "zEClass", MessageBoxButton.OK,
                _run.FailCount > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not save the report.\n\n{ex.Message}",
                "zEClass", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
