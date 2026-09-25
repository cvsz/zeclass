using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using EBoard.Core;

namespace EBoard;

/// <summary>
/// Replays a page's ink in draw order, covering the vendor manual's "PLAY BACK: play back the
/// page contents" and its PLAYBACKSPEED setting.
///
/// The board surface is not modified: playback draws into a separate overlay, so stopping it
/// leaves the real page untouched and a teacher can replay as often as they like.
/// </summary>
public sealed class PlaybackWindow : Window
{
    private const double FrameMs = 1000.0 / 60.0;

    private readonly BoardDocument _document;
    private readonly Canvas _canvas = new();
    private readonly PlaybackTimeline _timeline;
    private readonly DispatcherTimer _timer;
    private readonly Slider _scrubber;
    private readonly TextBlock _clock;
    private readonly Button _playPause;
    private long _originTicks;
    private long _positionTicks;
    private double _speed = 1.0;
    private bool _playing;
    private DateTime _lastTick;

    public PlaybackWindow(BoardDocument document)
    {
        _document = document;
        _timeline = PlaybackTimeline.FromPage(document.Active());

        Title = "EBoard playback";
        Width = 1000;
        Height = 640;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brushes.Black;
        Topmost = true;

        var border = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x3F, 0x4B)),
            BorderThickness = new Thickness(1),
            Background = Brushes.White,
            Child = _canvas,
        };

        _clock = new TextBlock
        {
            Foreground = Brushes.White,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 14,
            MinWidth = 90,
            VerticalAlignment = VerticalAlignment.Center,
        };

        _playPause = new Button
        {
            Content = "Play",
            Width = 80,
            Height = 34,
            Margin = new Thickness(0, 0, 10, 0),
            Cursor = System.Windows.Input.Cursors.Hand,
            Background = new SolidColorBrush(Color.FromRgb(0x3D, 0x7E, 0xFF)),
            Foreground = Brushes.White,
        };
        _playPause.Click += (_, _) => Toggle();

        _scrubber = new Slider
        {
            Minimum = 0,
            Maximum = Math.Max(1, _timeline.TotalDurationTicks / (double)TimeSpan.TicksPerMillisecond),
            Value = 0,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 10, 0),
        };
        _scrubber.ValueChanged += (_, e) =>
        {
            if (!_playing)
            {
                Seek(TimeSpan.FromMilliseconds(e.NewValue));
            }
        };

        var speed = new ComboBox
        {
            Width = 76,
            Height = 30,
            SelectedIndex = 1,
            Margin = new Thickness(0, 0, 10, 0),
        };
        speed.Items.Add("0.5x");
        speed.Items.Add("1x");
        speed.Items.Add("2x");
        speed.Items.Add("4x");
        speed.SelectionChanged += (_, _) => _speed = speed.SelectedIndex switch
        {
            0 => 0.5,
            2 => 2.0,
            3 => 4.0,
            _ => 1.0,
        };

        var close = new Button
        {
            Content = "Close",
            Width = 80,
            Height = 34,
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        close.Click += (_, _) => Close();

        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(12, 8, 12, 8),
        };
        bar.Children.Add(_playPause);
        bar.Children.Add(new TextBlock
        {
            Text = "Speed",
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
        });
        bar.Children.Add(speed);
        bar.Children.Add(_scrubber);
        bar.Children.Add(_clock);
        bar.Children.Add(close);

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(border, 0);
        Grid.SetRow(bar, 1);
        root.Children.Add(border);
        root.Children.Add(bar);
        Content = root;

        _originTicks = _timeline.OriginTicks;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(FrameMs) };
        _timer.Tick += (_, _) => Advance();

        Loaded += (_, _) => Render();
    }

    public bool HasRecordedContent => !_timeline.IsEmpty;

    public void Toggle()
    {
        if (_playing)
        {
            Pause();
        }
        else
        {
            Play();
        }
    }

    public void Play()
    {
        if (_timeline.IsEmpty)
        {
            return;
        }

        if (_positionTicks >= _timeline.TotalDurationTicks)
        {
            _positionTicks = 0;
        }

        _lastTick = DateTime.UtcNow;
        _playing = true;
        _playPause.Content = "Pause";
        _timer.Start();
    }

    public void Pause()
    {
        _playing = false;
        _playPause.Content = "Play";
        _timer.Stop();
    }

    public void Stop()
    {
        Pause();
        _positionTicks = 0;
        Render();
    }

    public void Seek(TimeSpan position)
    {
        _positionTicks = Math.Clamp((long)(position.TotalMilliseconds * TimeSpan.TicksPerMillisecond),
            0, _timeline.TotalDurationTicks);
        _scrubber.Value = _positionTicks / (double)TimeSpan.TicksPerMillisecond;
        Render();
    }

    private void Advance()
    {
        var now = DateTime.UtcNow;
        var elapsed = (now - _lastTick).TotalMilliseconds;
        _lastTick = now;
        _positionTicks += (long)(elapsed * TimeSpan.TicksPerMillisecond * _speed);

        if (_positionTicks >= _timeline.TotalDurationTicks)
        {
            _positionTicks = _timeline.TotalDurationTicks;
            Pause();
        }

        _scrubber.Value = Math.Min(_scrubber.Maximum, _positionTicks / (double)TimeSpan.TicksPerMillisecond);
        Render();
    }

    private void Render()
    {
        _canvas.Children.Clear();
        _clock.Text = Format(_positionTicks / (double)TimeSpan.TicksPerMillisecond);
        if (_timeline.IsEmpty)
        {
            return;
        }

        var visible = _timeline.VisibleAt(_originTicks + _positionTicks, _originTicks, 1.0);
        var engine = new InkEngine();
        foreach (var (stroke, _) in visible)
        {
            if (stroke.Points.Count == 0)
            {
                continue;
            }

            var geometry = engine.BuildOutline(stroke.Points, stroke.Width, 0.5);
            var view = new DrawingVisual();
            using (var dc = view.RenderOpen())
            {
                dc.DrawGeometry(engine.BrushFor(stroke), null, geometry);
            }

            var w = Math.Max(1, (int)Math.Ceiling(geometry.Bounds.Width + 4));
            var h = Math.Max(1, (int)Math.Ceiling(geometry.Bounds.Height + 4));
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(w, h, 96, 96,
                PixelFormats.Pbgra32);
            bitmap.Render(view);
            bitmap.Freeze();

            var image = new Image
            {
                Source = bitmap,
                Width = w,
                Height = h,
            };
            Canvas.SetLeft(image, geometry.Bounds.X - 2);
            Canvas.SetTop(image, geometry.Bounds.Y - 2);
            _canvas.Children.Add(image);
        }
    }

    private static string Format(double milliseconds)
    {
        var span = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
        return $"{(int)span.TotalMinutes:D2}:{span.Seconds:D2}.{span.Milliseconds / 10:D2}";
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        base.OnClosed(e);
    }
}
