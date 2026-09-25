using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using EBoard.Core;
using EBoard.Tools;

namespace EBoard;

/// <summary>
/// Live magnifier, replacing the packaged <c>INZoom.exe</c> / <c>Zoom.dll</c> and covering
/// manual section 5.2.1 "MAGNIFIER". Samples the desktop around a point and shows it scaled,
/// so the teacher can write small. Click-through by default, so the board stays usable while
/// it is open.
/// </summary>
public sealed class MagnifierWindow : OverlayToolWindow
{
    private readonly Image _content;
    private readonly TextBlock _label;
    private readonly Border _frame;
    private readonly DispatcherTimer _timer;
    private double _zoom = 2.0;

    public MagnifierWindow()
    {
        Title = "EBoard magnifier";
        Width = 360;
        Height = 240;
        ClickThrough = true;

        _content = new Image { Stretch = Stretch.Fill, SnapsToDevicePixels = false };
        _frame = new Border
        {
            BorderThickness = new Thickness(3),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3D, 0x7E, 0xFF)),
            Background = Brushes.Black,
            Child = _content,
        };

        _label = new TextBlock
        {
            Foreground = Brushes.White,
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(4, 2, 4, 2),
        };

        var panel = new Grid();
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(_frame, 0);
        Grid.SetRow(_label, 1);
        panel.Children.Add(_frame);
        panel.Children.Add(_label);
        Content = panel;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(70) };
        _timer.Tick += (_, _) => Refresh();
        UpdateLabel();
    }

    /// <summary>Screen position the magnifier samples, in device pixels.</summary>
    public Point FollowPoint { get; set; } = new(960, 540);

    public double Zoom
    {
        get => _zoom;
        set
        {
            _zoom = Math.Clamp(value, 1.25, 8);
            UpdateLabel();
        }
    }

    public override void Toggle()
    {
        if (IsActive)
        {
            Stop();
        }
        else
        {
            Start();
        }
    }

    public void Start()
    {
        Visibility = Visibility.Visible;
        _timer.Start();
        Refresh();
    }

    public void Stop()
    {
        _timer.Stop();
        Visibility = Visibility.Collapsed;
        _content.Source = null;
    }

    private void UpdateLabel() => _label.Text = $"{Zoom:F1}x - move the pointer to aim";

    private void Refresh()
    {
        try
        {
            var dpi = OwnerDpi;
            var deviceWidth = (int)(ActualWidth * dpi);
            var deviceHeight = (int)(ActualHeight * dpi);
            if (deviceWidth < 16 || deviceHeight < 16)
            {
                return;
            }

            var left = (int)(FollowPoint.X - (deviceWidth / (2 * _zoom)));
            var top = (int)(FollowPoint.Y - (deviceHeight / (2 * _zoom)));
            _content.Source = ScreenCapture.Grab(left, top, deviceWidth, deviceHeight);
        }
        catch (Exception ex)
        {
            CrashLog.Write("Magnifier", ex);
            Stop();
        }
    }
}

/// <summary>
/// Spotlight, replacing <c>ScreenHighLight.exe</c> (manual 5.2.1 "SPOTLIGHT"). Dims the whole
/// screen except a movable circle, so attention goes to one part of the board.
/// </summary>
public sealed class SpotlightWindow : OverlayToolWindow
{
    private const double DefaultDiameter = 260;

    private readonly Ellipse _hole;
    private readonly Canvas _canvas;
    private double _diameter = DefaultDiameter;

    public SpotlightWindow()
    {
        Title = "EBoard spotlight";
        ClickThrough = true;
        ResizeMode = ResizeMode.NoResize;

        _hole = new Ellipse
        {
            Stroke = new SolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x07)),
            StrokeThickness = 3,
            Visibility = Visibility.Collapsed,
        };

        _canvas = new Canvas();
        _canvas.Children.Add(_hole);
        Content = _canvas;

        var bounds = ScreenCapture.VirtualScreenBounds();
        Width = bounds.Width;
        Height = bounds.Height;
        Left = 0;
        Top = 0;

        MouseMove += (_, e) => MoveHole(e.GetPosition(this));
        PreviewMouseLeftButtonDown += (_, e) =>
        {
            ClickThrough = false;
            Drag();
            e.Handled = true;
        };
        PreviewMouseLeftButtonUp += (_, _) => ClickThrough = true;
    }

    /// <summary>Centre of the lit circle, in device pixels relative to the virtual screen.</summary>
    public Point Center { get; private set; } = new(
        ScreenCapture.VirtualScreenBounds().Left + 960,
        ScreenCapture.VirtualScreenBounds().Top + 540);

    /// <summary>Repositions the lit circle and updates the overlay immediately.</summary>
    public void MoveTo(Point deviceCenter)
    {
        Center = deviceCenter;
        Visibility = Visibility.Visible;
        _hole.Visibility = Visibility.Visible;
        PositionHole();
    }

    public double Diameter
    {
        get => _diameter;
        set => _diameter = Math.Clamp(value, 60, 1200);
    }

    public override void Toggle()
    {
        if (IsActive)
        {
            HideSpot();
        }
        else
        {
            ShowSpot();
        }
    }

    public void ShowSpot()
    {
        Visibility = Visibility.Visible;
        _hole.Visibility = Visibility.Visible;
        PositionHole();
    }

    public void HideSpot()
    {
        Visibility = Visibility.Collapsed;
        _hole.Visibility = Visibility.Collapsed;
    }

    private void MoveHole(Point local) => Center = new Point(local.X * OwnerDpi, local.Y * OwnerDpi);

    private void PositionHole()
    {
        var dpi = OwnerDpi;
        _hole.Width = _diameter / dpi;
        _hole.Height = _diameter / dpi;
        _hole.Margin = new Thickness((Center.X / dpi) - (_diameter / dpi / 2),
            (Center.Y / dpi) - (_diameter / dpi / 2), 0, 0);
    }

    private void Drag()
    {
        try
        {
            CaptureMouse();
            var release = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            {
                RoutedEvent = Mouse.MouseMoveEvent,
            };
            MoveHole(release.GetPosition(this));
            PositionHole();
        }
        catch (InvalidOperationException)
        {
        }
    }
}

/// <summary>
/// Screen curtain, replacing <c>DrawCurtain.exe</c> (manual 5.2.1 "SCREEN CURTAIN"). Covers the
/// screen so an answer is hidden, and a horizontal swipe reveals it again. This is the tool the
/// vendor manual says the teacher uses to "hide answer".
/// </summary>
public sealed class CurtainWindow : OverlayToolWindow
{
    private readonly Border _curtain;
    private readonly TextBlock _hint;
    private double _revealFraction = 1.0;

    public CurtainWindow()
    {
        Title = "EBoard screen curtain";
        ClickThrough = false;
        ResizeMode = ResizeMode.NoResize;

        _curtain = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xF0, 0x10, 0x12, 0x18)),
            Child = new TextBlock
            {
                Text = "Screen hidden - press the hotkey, or double-click, to reveal",
                Foreground = Brushes.White,
                FontSize = 20,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        _hint = (TextBlock)_curtain.Child;

        var bounds = ScreenCapture.VirtualScreenBounds();
        Width = bounds.Width;
        Height = bounds.Height;
        Left = 0;
        Top = 0;
        Content = _curtain;

        MouseDoubleClick += (_, _) => ToggleReveal();
    }

    /// <summary>1.0 fully closed, 0 fully open. Partial values support a wipe reveal.</summary>
    public double RevealFraction
    {
        get => _revealFraction;
        set
        {
            _revealFraction = Math.Clamp(value, 0, 1);
            _curtain.Height = Math.Max(1, ActualHeight * _revealFraction);
            _hint.Visibility = _revealFraction > 0.95 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    public override void Toggle()
    {
        if (!IsActive)
        {
            Visibility = Visibility.Visible;
            RevealFraction = 1;
        }
        else
        {
            Visibility = Visibility.Collapsed;
        }
    }

    public void ToggleReveal() => RevealFraction = _revealFraction > 0.5 ? 0 : 1;
}

/// <summary>
/// Teaching clock, covering manual 5.2.1 "CLOCK" (simulated, digital, counting, countdown).
/// Purely local and offline, so it is self-contained.
/// </summary>
public sealed class ClockWindow : OverlayToolWindow
{
    private readonly TextBlock _display;
    private readonly DispatcherTimer _timer;
    private DateTime _countdownTarget;
    private bool _countingDown;
    private int _elapsedSeconds;

    public ClockWindow()
    {
        Title = "EBoard clock";
        Width = 320;
        Height = 150;
        ClickThrough = true;
        WindowStyle = WindowStyle.SingleBorderWindow;
        Topmost = true;

        _display = new TextBlock
        {
            Foreground = Brushes.White,
            FontSize = 44,
            FontFamily = new FontFamily("Consolas"),
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var panel = new Grid();
        panel.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xE0, 0x10, 0x12, 0x18)),
            Child = _display,
        });
        Content = panel;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Tick();
        Mode = ClockMode.Clock;
    }

    public ClockMode Mode { get; private set; }

    public override void Toggle()
    {
        if (IsActive)
        {
            _timer.Stop();
            Visibility = Visibility.Collapsed;
        }
        else
        {
            Visibility = Visibility.Visible;
            _timer.Start();
        }
    }

    public void SetMode(ClockMode mode)
    {
        Mode = mode;
        _elapsedSeconds = 0;
        _countingDown = mode == ClockMode.Countdown;
        _countdownTarget = DateTime.Now.AddMinutes(mode == ClockMode.Countdown ? 5 : 0);
        Tick();
    }

    public void StartCountdown(TimeSpan duration)
    {
        Mode = ClockMode.Countdown;
        _countdownTarget = DateTime.Now.Add(duration);
        _countingDown = true;
        Visibility = Visibility.Visible;
        _timer.Start();
    }

    private void Tick()
    {
        _display.Text = Mode switch
        {
            ClockMode.Simulated => $"AM {DateTime.Now:hh:mm:ss}",
            ClockMode.Counting => Format(TimeSpan.FromSeconds(_elapsedSeconds)),
            ClockMode.Countdown => Format(_countdownTarget - DateTime.Now),
            _ => DateTime.Now.ToString("HH:mm:ss"),
        };

        if (Mode == ClockMode.Counting)
        {
            _elapsedSeconds++;
        }
        else if (Mode == ClockMode.Countdown && _countdownTarget <= DateTime.Now)
        {
            _display.Text = "Time is up";
        }
    }

    private static string Format(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
        {
            span = TimeSpan.Zero;
        }

        return $"{(int)span.TotalHours:D2}:{span.Minutes:D2}:{span.Seconds:D2}";
    }
}

public enum ClockMode
{
    Clock = 0,
    Simulated = 1,
    Counting = 2,
    Countdown = 3,
}

/// <summary>
/// On-screen keyboard, replacing the packaged <c>myosk.exe</c>. Synthetic keystrokes are only
/// ever sent in response to a key the operator actually pressed on this window, and the window
/// is not click-through, so nothing is typed without a deliberate action.
/// </summary>
public sealed class OnScreenKeyboardWindow : Window
{
    private static readonly string[] Rows = new[]
    {
        "1", "2", "3", "4", "5", "6", "7", "8", "9", "0",
        "Q", "W", "E", "R", "T", "Y", "U", "I", "O", "P",
        "A", "S", "D", "F", "G", "H", "J", "K", "L",
        "Z", "X", "C", "V", "B", "N", "M",
    };

    public OnScreenKeyboardWindow()
    {
        Title = "EBoard keyboard";
        Width = 720;
        Height = 260;
        WindowStyle = WindowStyle.SingleBorderWindow;
        Topmost = true;
        ShowInTaskbar = false;
        Background = new SolidColorBrush(Color.FromRgb(0x2B, 0x2F, 0x38));

        var panel = new WrapPanel { Margin = new Thickness(8) };
        foreach (var key in Rows)
        {
            panel.Children.Add(MakeKey(key));
        }

        panel.Children.Add(MakeAction("Space", () => Send(' ')));
        panel.Children.Add(MakeAction("Backspace", Backspace));
        panel.Children.Add(MakeAction("Enter", Enter));

        Content = panel;
    }

    public event Action<string>? TextTyped;

    private Button MakeKey(string label)
    {
        var button = new Button
        {
            Content = label,
            Width = 56,
            Height = 56,
            Margin = new Thickness(3),
            FontSize = 18,
            Cursor = System.Windows.Input.Cursors.Hand,
            Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x3F, 0x4B)),
            Foreground = Brushes.White,
        };
        button.Click += (_, _) =>
        {
            TextTyped?.Invoke(label);
            Send(char.ToUpperInvariant(label[0]));
        };
        return button;
    }

    private Button MakeAction(string label, Action action)
    {
        var button = new Button
        {
            Content = label,
            Width = 110,
            Height = 56,
            Margin = new Thickness(3),
            Cursor = System.Windows.Input.Cursors.Hand,
            Background = new SolidColorBrush(Color.FromRgb(0x3D, 0x7E, 0xFF)),
            Foreground = Brushes.White,
        };
        button.Click += (_, _) => action();
        return button;
    }

    private static void Send(char c) => SendKeys.Send(c);

    private static void Backspace() => SendKeys.SendBackspace();

    private static void Enter() => SendKeys.SendEnter();
}

/// <summary>
/// Synthetic input. Kept in one place so every keystroke the app generates goes through a
/// single audited path. Uses <c>keybd_event</c> rather than SendInput because the app only
/// needs key-down/key-up pairs, and this is the call that behaves identically across the
/// Windows versions a classroom image is likely to carry.
/// </summary>
public static class SendKeys
{
    public static void Send(char c)
    {
        var shift = char.IsUpper(c);
        var scan = MapVirtualKey(char.ToUpperInvariant(c));
        if (scan == 0)
        {
            return;
        }

        if (shift)
        {
            keybd_event(ShiftKey, 0, 0, UIntPtr.Zero);
        }

        keybd_event((byte)scan, 0, 0, UIntPtr.Zero);
        keybd_event((byte)scan, 0, KeyUp, UIntPtr.Zero);
        if (shift)
        {
            keybd_event(ShiftKey, 0, KeyUp, UIntPtr.Zero);
        }
    }

    public static void SendBackspace()
    {
        keybd_event(Backspace, 0, 0, UIntPtr.Zero);
        keybd_event(Backspace, 0, KeyUp, UIntPtr.Zero);
    }

    public static void SendEnter()
    {
        keybd_event(Enter, 0, 0, UIntPtr.Zero);
        keybd_event(Enter, 0, KeyUp, UIntPtr.Zero);
    }

    private const byte KeyUp = 0x0002;
    private const byte ShiftKey = 0xA0;
    private const byte Backspace = 0x08;
    private const byte Enter = 0x0D;

    private static int MapVirtualKey(char c) => c switch
    {
        'A' => 0x41, 'B' => 0x42, 'C' => 0x43, 'D' => 0x44, 'E' => 0x45, 'F' => 0x46,
        'G' => 0x47, 'H' => 0x48, 'I' => 0x49, 'J' => 0x4A, 'K' => 0x4B, 'L' => 0x4C,
        'M' => 0x4D, 'N' => 0x4E, 'O' => 0x4F, 'P' => 0x50, 'Q' => 0x51, 'R' => 0x52,
        'S' => 0x53, 'T' => 0x54, 'U' => 0x55, 'V' => 0x56, 'W' => 0x57, 'X' => 0x58,
        'Y' => 0x59, 'Z' => 0x5A,
        >= '0' and <= '9' => c,
        ' ' => 0x20,
        _ => 0,
    };

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extraInfo);
}
