using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using zEClass.Core;
using zEClass.Tools;
using Microsoft.Win32;

namespace zEClass;

public partial class MainWindow : Window
{
    private BoardDocument _document = new();
    private readonly DigitizerService _digitizer = new();
    private readonly CalibrationStore _calibrationStore = new();
    private readonly DispatcherTimer _autoSaveTimer;
    private readonly DispatcherTimer _gestureTimer;
    private readonly PageStrip _pageStrip = new();
    private string? _filePath;
    private bool _suppressFullScreenToggle;
    private bool _initializingPickers;
    private WindowStyle _preFullScreenStyle;
    private WindowState _preFullScreenState;

    public MainWindow()
    {
        InitializeComponent();

        foreach (var c in PaletteColors)
        {
            ColorBox.Items.Add(c);
        }

        ColorBox.SelectedIndex = 0;
        ColorBox.SelectionChanged += (_, _) => ApplyColor();

        LineStyleBox.Items.Add("Solid");
        LineStyleBox.Items.Add("Dashed");
        LineStyleBox.Items.Add("Dotted");
        LineStyleBox.SelectedIndex = 0;

        FillBox.Items.Add("Outline");
        FillBox.Items.Add("Filled");
        FillBox.Items.Add("Both");
        FillBox.SelectedIndex = 0;

        foreach (var theme in Theme.All)
        {
            ThemeBox.Items.Add(theme);
        }

        ThemeBox.SelectedItem = ThemeManager.Current;
        ThemeManager.Apply(ThemeManager.Current);

        // Populating a ComboBox can select its first item and fire SelectionChanged before the
        // handler below is wired for the initial value, so the handler is suppressed until the
        // intended language is actually chosen.
        _initializingPickers = true;
        try
        {
            Locator.Current.LoadFromDisk();
            PopulateLanguageBox();
        }
        finally
        {
            _initializingPickers = false;
        }

        ApplyLanguage(LanguageBox.SelectedItem is LanguageInfo info ? info.Code : "en");
        CrashLog.Info($"Language: {Locator.Current.Language} " +
                      $"({Locator.Current.AvailableLanguages.Count} available)");
        RefreshLocalizedText();


        _document.PageCount = 5;
        _document.EnsurePages();
        _document.CanvasWidth = 1920;
        _document.CanvasHeight = 1080;
        Surface.Document = _document;
        Surface.BoardChanged += OnBoardChanged;
        Surface.SelectionChanged += (_, _) => UpdateStatus();
        Surface.ViewChanged += (_, _) => UpdateZoomText();
        Surface.WavePageTurn += (_, direction) => GoToPage(_document.ActivePage + direction);
        Surface.ContextMenuRequested += (_, point) => ShowContextMenu(point);
        Surface.SpotlightRequested += (_, centre) =>
        {
            EnsureTools();
            if (!_spotlight!.IsActive)
            {
                _spotlight.ShowSpot();
            }

            _spotlight.MoveTo(ToDevice(centre));
        };
        Surface.MagnifierRegionRequested += (_, rect) =>
        {
            EnsureTools();
            _magnifier!.Zoom = Math.Clamp(
                Math.Min(rect.Width, rect.Height) > 0
                    ? Math.Max(1.25, 400 / Math.Max(1, Math.Min(rect.Width, rect.Height)))
                    : 2.0,
                1.25,
                8);
            _magnifier.FollowPoint = ToDevice(new Point(
                rect.X + (rect.Width / 2), rect.Y + (rect.Height / 2)));
            if (!_magnifier.IsActive)
            {
                _magnifier.Start();
            }
        };
        RebindSurface();
        BuildPageStrip();

        _autoSaveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(Math.Max(15, _document.AutoSaveIntervalSeconds)),
        };
        _autoSaveTimer.Tick += (_, _) => AutoSave();

        // Dwell gestures need a tick even while nothing is moving.
        _gestureTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _gestureTimer.Tick += (_, _) => Surface.TickGestures();

        _recordTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _recordTimer.Tick += OnRecordTick;

        CalibrationLayer.StateChanged += OnCalibrationStateChanged;

        Loaded += OnLoaded;
        Closing += OnClosing;
        KeyDown += OnKeyDown;
        PreviewKeyDown += OnCalibrationKeyDown;
    }

    private static readonly uint[] PaletteColors =
    [
        0xFF1B1B1F, 0xFFFFFFFF, 0xFFE53935, 0xFFFB8C00, 0xFFFDD835,
        0xFF43A047, 0xFF00ACC1, 0xFF1E88E5, 0xFF8E24AA, 0xFF6D4C41,
    ];

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyIcon();
        CrashLog.Info($"zEClass started. OS {Environment.OSVersion} .NET {Environment.Version}");
        RefreshDigitizer();
        RestoreCalibration();
        StartNdiCapture();
        _document.CanvasWidth = Math.Max(1, Surface.ActualWidth);
        _document.CanvasHeight = Math.Max(1, Surface.ActualHeight);
        UpdatePageText();
        UpdateZoomText();
        UpdateStatus();
        _autoSaveTimer.Start();
        _gestureTimer.Start();
        Surface.Focus();
    }

    private AppSettings _settings = new();
    private NdiBridge? _ndi;

    /// <summary>
    /// Starts the NDI capture helper in the background when it is enabled and present. Silent
    /// when the helper is not installed: a board without the streaming tool is a complete board,
    /// not a broken one.
    /// </summary>
    private void StartNdiCapture()
    {
        _settings = AppSettings.Load();
        _ndi = new NdiBridge(NdiBridge.ResolveExePath(), _settings.NdiAutoStart);
        UpdateNdiButton();
        if (!_settings.NdiAutoStart)
        {
            return;
        }

        RefreshNdiAsync();
    }

    /// <summary>
    /// Runs the bridge off the UI thread: starting the helper plus waiting for its window to
    /// minimize can take seconds, and startup must never hang behind it.
    /// </summary>
    private void RefreshNdiAsync()
    {
        var bridge = _ndi;
        if (bridge is null)
        {
            return;
        }

        NdiBtn.IsEnabled = false;
        Task.Run(() =>
        {
            var result = bridge.EnsureRunning();
            Dispatcher.Invoke(() =>
            {
                switch (result)
                {
                    case NdiResult.Started:
                        StatusText.Text = "NDI capture started in the background.";
                        break;
                    case NdiResult.Failed:
                        StatusText.Text = bridge.LastError ?? "NDI capture could not start.";
                        break;
                    case NdiResult.NotFound:
                        // Deliberately quiet beyond the button state: most machines will never have it.
                        break;
                }

                UpdateNdiButton();
            });
        });
    }

    private void OnNdiChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressNdiToggle || _ndi is null)
        {
            return;
        }

        if (NdiBtn.IsChecked == true)
        {
            _settings.NdiAutoStart = true;
            _settings.Save();
            RefreshNdiAsync();
        }
        else
        {
            _settings.NdiAutoStart = false;
            _settings.Save();
            var bridge = _ndi;
            NdiBtn.IsEnabled = false;
            Task.Run(() =>
            {
                var stopped = bridge.Stop();
                var message = stopped ? "NDI capture stopped."
                    : bridge.LastError ?? "NDI capture could not be stopped.";
                Dispatcher.Invoke(() =>
                {
                    StatusText.Text = message;
                    UpdateNdiButton();
                });
            });
        }
    }

    /// <summary>Keeps the toggle honest: checked means actually running, not merely enabled.</summary>
    private void UpdateNdiButton()
    {
        if (_ndi is null)
        {
            NdiBtn.IsEnabled = false;
            return;
        }

        // Setting IsChecked fires the toggle handler, which would save settings and restart
        // the helper as a side effect of merely displaying state. Suppressed while syncing.
        _suppressNdiToggle = true;
        try
        {
            NdiBtn.IsEnabled = _ndi.ExeFound || _ndi.IsRunning;
            NdiBtn.IsChecked = _ndi.IsRunning;
        }
        finally
        {
            _suppressNdiToggle = false;
        }

        NdiBtn.ToolTip = !_ndi.ExeFound && !_ndi.IsRunning
            ? $"vMix Desktop Capture was not found at {_ndi.ExePath}."
            : _ndi.IsRunning
                ? "NDI capture is running minimized. Uncheck to stop it."
                : "Start the NDI capture helper minimized in the background.";
    }

    private bool _suppressNdiToggle;

    /// <summary>
    /// Loads the window icon from the generated .ico. WPF's XAML type converter rejects a bare
    /// .ico path (it only understands image formats its converter handles), so the icon is
    /// applied here from disk instead. The executable's own icon comes from the application
    /// manifest and is always present regardless of this.
    ///
    /// The frame is selected explicitly at 64 px rather than decoded through a URI. WPF's ICO
    /// decoder over a URI picks the first frame in the file, which is the 16 px one, and the
    /// taskbar and Alt+Tab then showed an upscaled 16 px image. Requesting 64 px takes the exact
    /// 64 px frame for high-DPI taskbars and downscales cleanly everywhere else.
    /// </summary>
    private void ApplyIcon()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "zEClass.ico");
            if (!File.Exists(path))
            {
                CrashLog.Info($"Window icon not found at {path}; using the executable default.");
                return;
            }

            using var icon = new System.Drawing.Icon(path, new System.Drawing.Size(64, 64));
            var bitmap = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle,
                System.Windows.Int32Rect.Empty,
                System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
            bitmap.Freeze();
            Icon = bitmap;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UriFormatException
                                        or ArgumentException
                                        or System.Runtime.InteropServices.COMException)
        {
            CrashLog.Info($"Window icon not applied: {ex.Message}");
        }
    }

    /// <summary>
    /// Converts a board coordinate to device pixels, which is the space the overlay tools and
    /// the screen capture layer work in. Uses the surface's own render transform so the result
    /// is correct while the board is zoomed or panned.
    /// </summary>
    private Point ToDevice(Point boardPoint) => new(
        Surface.ViewOffsetX + (boardPoint.X * Surface.ViewScale),
        Surface.ViewOffsetY + (boardPoint.Y * Surface.ViewScale));

    private void OnPinchChanged(object sender, RoutedEventArgs e)
    {
        // Two-finger recognition is always on: a two-finger gesture cannot be a mistake on a
        // board, and a pen produces a single contact.
        _ = sender;
        _ = e;
    }

    private void OnRecognitionPenChanged(object sender, RoutedEventArgs e)
    {
        var button = (ToggleButton)sender;
        Surface.RecognitionPenEnabled = button.IsChecked == true;
        StatusText.Text = Surface.RecognitionPenEnabled
            ? "Recognition pen: draw a circle for a spotlight, a square for a magnifier."
            : "Recognition pen off.";
    }

    // ---- annotation tools (manual 5.2.1) -------------------------------------------

    private MagnifierWindow? _magnifier;
    private SpotlightWindow? _spotlight;
    private CurtainWindow? _curtain;
    private ClockWindow? _clock;
    private OnScreenKeyboardWindow? _keyboard;

    private void EnsureTools()
    {
        var bounds = ScreenCapture.VirtualScreenBounds();

        _magnifier ??= new MagnifierWindow();
        _magnifier.MoveToDeviceRect(new Rect(
            bounds.X + ((bounds.Width - 360) / 2), bounds.Y + ((bounds.Height - 240) / 2), 360, 240));
        _magnifier.RegisterHotkey(this, Key.F1, ModifierKeys.None);

        _spotlight ??= new SpotlightWindow();
        _spotlight.RegisterHotkey(this, Key.F2, ModifierKeys.None);

        _curtain ??= new CurtainWindow();
        _curtain.RegisterHotkey(this, Key.F3, ModifierKeys.None);

        _clock ??= new ClockWindow();
        _clock.RegisterHotkey(this, Key.F4, ModifierKeys.None);
    }

    private void OnMagnifierClick(object sender, RoutedEventArgs e)
    {
        EnsureTools();
        _magnifier!.FollowPoint = CursorPositionInDevicePixels();
        _magnifier.Toggle();
    }

    private void OnSpotlightClick(object sender, RoutedEventArgs e)
    {
        EnsureTools();
        _spotlight!.Toggle();
    }

    private void OnCurtainClick(object sender, RoutedEventArgs e)
    {
        EnsureTools();
        _curtain!.Toggle();
    }

    private void OnClockClick(object sender, RoutedEventArgs e)
    {
        EnsureTools();
        if (_clock!.IsActive)
        {
            _clock.Toggle();
            return;
        }

        _clock.SetMode(ClockMode.Clock);
        _clock.Toggle();
    }

    private void OnKeyboardClick(object sender, RoutedEventArgs e)
    {
        if (_keyboard is not null && _keyboard.IsVisible)
        {
            _keyboard.Close();
            _keyboard = null;
            return;
        }

        _keyboard = new OnScreenKeyboardWindow
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
        };

        // Typing on the virtual keyboard feeds the board's text tool when one is active,
        // which is how a teacher adds a label without a physical keyboard.
        _keyboard.TextTyped += text =>
        {
            if (Surface.TextSession is { IsEditing: true })
            {
                Surface.TypeText(text);
            }
        };
        _keyboard.Show();
    }

    /// <summary>
    /// Inserts a screenshot of the whole desktop as a page image, covering manual 5.2.1
    /// "CAPTURE" (select-and-capture, capture without window frame, capture total screen).
    /// </summary>
    private void OnCaptureClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var bounds = ScreenCapture.VirtualScreenBounds();
            var shot = ScreenCapture.Grab((int)bounds.X, (int)bounds.Y, (int)bounds.Width,
                (int)bounds.Height);
            if (shot is null)
            {
                StatusText.Text = "Screen capture failed. Another app may be blocking it.";
                return;
            }

            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "zEClass", "captures");
            Directory.CreateDirectory(path);
            var file = Path.Combine(path, $"capture-{DateTime.Now:yyyyMMdd-HHmmss}.png");
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(shot));
            using (var stream = File.Create(file))
            {
                encoder.Save(stream);
            }

            var page = _document.Active();
            page.Images.Add(new InkImage
            {
                PageIndex = page.Index,
                X = 40,
                Y = 40,
                Width = Math.Min(Surface.CanvasWidth - 80, bounds.Width),
                Height = Math.Min(Surface.CanvasHeight - 80, bounds.Width * (bounds.Height / bounds.Width)),
                SourcePath = file,
            });

            Surface.ReloadFromDocument();
            BuildPageStrip();
            StatusText.Text = $"Captured the screen to {Path.GetFileName(file)} and placed it on the page.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashLog.Write("Capture", ex);
            StatusText.Text = "Screen capture failed.";
        }
    }

    private static Point CursorPositionInDevicePixels()
    {
        var bounds = ScreenCapture.VirtualScreenBounds();
        if (GetCursorPos(out var point))
        {
            return new Point(point.X, point.Y);
        }

        return new Point(bounds.Left + (bounds.Width / 2), bounds.Top + (bounds.Height / 2));
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _autoSaveTimer.Stop();
        _gestureTimer.Stop();
        _recordTimer.Stop();
        _recorder.Stop();
        _audio.Stop();
        // Supervised lifecycle: the helper was started for this board session, so it is
        // reaped with it rather than left orphaned. It stops only our own tracked instance.
        _ndi?.Stop();
        Surface.CommitText();
        foreach (var tool in new Window?[]
                 {
                     _magnifier, _spotlight, _curtain, _clock, _keyboard,
                 })
        {
            try
            {
                tool?.Close();
            }
            catch (InvalidOperationException)
            {
            }
        }

        if (!string.IsNullOrEmpty(_filePath))
        {
            TrySave(_filePath);
        }
        else
        {
            AutoSave();
        }
    }

    private void RefreshDigitizer()
    {
        DigitizerStatus status;
        try
        {
            status = _digitizer.Probe();
        }
        catch (Exception ex)
        {
            CrashLog.Write("DigitizerProbe", ex);
            DigitizerText.Text = "Digitizer: probe failed";
            return;
        }

        Surface.Digitizer = status;
        DigitizerText.Text = status switch
        {
            { UsbDigitizerPresent: true, PenActive: true } => "Digitizer: USB pen + touch",
            { UsbDigitizerPresent: true } => "Digitizer: USB touch",
            { PenActive: true } => "Digitizer: pen",
            { TouchActive: true } => "Digitizer: touch (OS)",
            _ => "Digitizer: none (mouse)",
        };
        UpdateCalibrationButtons();
        UpdateStatus();
    }

    // ---- tools -------------------------------------------------------------------

    private void OnToolClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } ||
            !Enum.TryParse<BoardTool>(tag, out var tool))
        {
            return;
        }

        Surface.Tool = tool;
        Surface.Cursor = tool switch
        {
            BoardTool.Select or BoardTool.Pan => Cursors.Arrow,
            BoardTool.Text => Cursors.IBeam,
            _ => Cursors.Cross,
        };

        foreach (var b in ToolButtons())
        {
            b.BorderBrush = b == sender
                ? new SolidColorBrush(Color.FromRgb(0x3D, 0x7E, 0xFF))
                : new SolidColorBrush(Color.FromRgb(0x3A, 0x3F, 0x4B));
        }

        UpdateStatus();
    }

    private IEnumerable<Button> ToolButtons()
    {
        yield return PenBtn;
        yield return HighlighterBtn;
        yield return EraserBtn;
        yield return LineBtn;
        yield return RectBtn;
        yield return EllipseBtn;
        yield return TriangleBtn;
        yield return ArrowBtn;
        yield return StarBtn;
        yield return TextBtn;
        yield return SelectBtn;
        yield return LassoBtn;
        yield return PanBtn;
    }

    private void OnWidthChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (Surface is not null)
        {
            Surface.StrokeWidth = e.NewValue;
        }
    }

    private void OnLineStyleChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LineStyleBox.SelectedIndex >= 0 && Surface is not null)
        {
            Surface.LineStyle = (LineStyle)LineStyleBox.SelectedIndex;
        }
    }

    private void OnFillChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FillBox.SelectedIndex >= 0 && Surface is not null)
        {
            Surface.ShapeStyle = (ShapeStyle)FillBox.SelectedIndex;
        }
    }

    private void ApplyColor()
    {
        if (ColorBox.SelectedItem is uint argb)
        {
            Surface.ColorArgb = argb;
        }
    }

    private void OnBoardChanged(object? sender, EventArgs e)
    {
        _document.ModifiedUtc = DateTimeOffset.UtcNow;
        if (Surface.StatusMessage is { } message)
        {
            StatusText.Text = message;
            Surface.SetStatus(null);
            return;
        }

        UpdateStatus();
    }

    // ---- edit ---------------------------------------------------------------------

    private void OnUndoClick(object sender, RoutedEventArgs e) => Surface.Undo();

    private void OnRedoClick(object sender, RoutedEventArgs e) => Surface.Redo();

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "Clear every stroke on this page?", "zEClass",
                MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK)
        {
            Surface.ClearPage();
            BuildPageStrip();
        }
    }

    private void ShowContextMenu(Point point)
    {
        var menu = new ContextMenu();
        if (Surface.Selection.IsEmpty)
        {
            menu.Items.Add(Item("Copy page image", (_, _) => CopyPageImage()));
            menu.Items.Add(Item("Paste", (_, _) => Surface.Paste()));
        }
        else
        {
            menu.Items.Add(Item($"Delete {Surface.Selection.Strokes.Count} object(s)", (_, _) =>
            {
                Surface.DeleteSelection();
                BuildPageStrip();
            }));
            menu.Items.Add(Item("Copy", (_, _) => Surface.CopySelection()));
            menu.Items.Add(Item("Cut", (_, _) => Surface.CutSelection()));
        }

        menu.IsOpen = true;
    }

    private static MenuItem Item(string header, RoutedEventHandler handler)
    {
        var item = new MenuItem { Header = header };
        item.Click += handler;
        return item;
    }

    private void OnInsertImageClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff)|" +
                     "*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|All files (*.*)|*.*",
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var page = _document.Active();
        var image = new InkImage
        {
            PageIndex = page.Index,
            X = (Surface.ActualWidth / 2) - 240,
            Y = (Surface.ActualHeight / 2) - 180,
            Width = 480,
            Height = 360,
            SourcePath = dialog.FileName,
        };

        // Natural aspect ratio from the file, when it can be read.
        try
        {
            var frame = System.Windows.Media.Imaging.BitmapFrame.Create(
                new Uri(dialog.FileName, UriKind.Absolute), BitmapCreateOptions.DelayCreation,
                BitmapCacheOption.OnLoad);
            if (frame.PixelHeight > 0)
            {
                image.Height = 480.0 * frame.PixelHeight / frame.PixelWidth;
            }
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UriFormatException)
        {
            CrashLog.Info($"Could not read image size for {dialog.FileName}: {ex.Message}");
        }

        page.Images.Add(image);
        Surface.ReloadFromDocument();
        BuildPageStrip();
    }

    // ---- pages ---------------------------------------------------------------------

    private void RebindSurface()
    {
        Surface.Document = _document;
        Surface.ReloadFromDocument();
    }

    private void BuildPageStrip()
    {
        _pageStrip.PageSelected -= OnStripPageSelected;
        _pageStrip.PageDeleted -= OnStripPageDeleted;
        _pageStrip.PageDuplicated -= OnStripPageDuplicated;
        _pageStrip.PageSettingsRequested -= OnStripPageSettings;
        _pageStrip.PageLockToggled -= OnStripPageLockToggled;
        _pageStrip.PageSelected += OnStripPageSelected;
        _pageStrip.PageDeleted += OnStripPageDeleted;
        _pageStrip.PageDuplicated += OnStripPageDuplicated;
        _pageStrip.PageSettingsRequested += OnStripPageSettings;
        _pageStrip.PageLockToggled += OnStripPageLockToggled;

        PageStripHost.ItemsSource = null;
        PageStripHost.ItemsSource = _pageStrip.Thumbnails;
        _pageStrip.Rebuild(_document, Surface);
    }

    private void OnStripPageSelected(object? sender, int pageIndex) => GoToPage(pageIndex);

    private void OnStripPageDeleted(object? sender, int pageIndex)
    {
        if (_document.Pages.Count <= 1)
        {
            StatusText.Text = "A board must keep at least one page.";
            return;
        }

        if (MessageBox.Show(this, $"Delete page {pageIndex + 1}? This can be undone.",
                "zEClass", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
        {
            return;
        }

        Surface.History.Execute(_document, new DeletePageCommand(pageIndex));
        Surface.ReloadFromDocument();
        UpdatePageText();
        UpdateStatus();
    }

    private void OnStripPageDuplicated(object? sender, int pageIndex)
    {
        Surface.History.Execute(_document, new DuplicatePageCommand(pageIndex));
        Surface.ReloadFromDocument();
        UpdatePageText();
        UpdateStatus();
    }

    private void OnStripPageLockToggled(object? sender, int pageIndex)
    {
        var page = _document.Pages.FirstOrDefault(p => p.Index == pageIndex);
        if (page is null)
        {
            return;
        }

        Surface.History.Execute(_document, new SetPageLockedCommand(pageIndex, !page.Locked));
        UpdatePageText();
        UpdateStatus();
    }

    private void OnStripPageSettings(object? sender, int pageIndex) => ShowPageSettings(pageIndex);

    /// <summary>
    /// Per-page background colour and image, per manual section 4.2. A background image behind
    /// a locked page reproduces the vendor's "fill" pen: writing on the page reveals the picture.
    /// </summary>
    private void ShowPageSettings(int pageIndex)
    {
        var page = _document.Pages.FirstOrDefault(p => p.Index == pageIndex);
        if (page is null)
        {
            return;
        }

        var window = new Window
        {
            Title = $"Page {pageIndex + 1} settings",
            Width = 380,
            Height = 320,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = System.Windows.Media.Brushes.White,
            Owner = this,
        };

        var colorText = new TextBlock
        {
            Text = $"Background: #{page.BackgroundColorArgb & 0xFFFFFF:X6}",
            Margin = new Thickness(12),
        };
        var imageText = new TextBlock
        {
            Text = string.IsNullOrEmpty(page.BackgroundImage)
                ? "Background image: none"
                : $"Background image: {Path.GetFileName(page.BackgroundImage)}",
            Margin = new Thickness(12, 6, 12, 6),
            TextWrapping = TextWrapping.Wrap,
        };

        var pickColor = new Button { Content = "Choose background colour...", Margin = new Thickness(12, 6, 12, 6) };
        pickColor.Click += (_, _) =>
        {
            var picker = new ColorPickerWindow(Color.FromRgb(
                (byte)((page.BackgroundColorArgb >> 16) & 0xFF),
                (byte)((page.BackgroundColorArgb >> 8) & 0xFF),
                (byte)(page.BackgroundColorArgb & 0xFF)));
            if (picker.ShowDialog() != true)
            {
                return;
            }

            var chosen = picker.SelectedColor;
            var argb = 0xFF000000u | ((uint)chosen.R << 16) | ((uint)chosen.G << 8) | chosen.B;
            Surface.History.Execute(_document,
                new SetPageBackgroundCommand(pageIndex, page.BackgroundImage, argb));
            colorText.Text = $"Background: #{argb & 0xFFFFFF:X6}";
            Surface.ReloadFromDocument();
            BuildPageStrip();
        };

        var pickImage = new Button { Content = "Choose background image...", Margin = new Thickness(12, 6, 12, 6) };
        pickImage.Click += (_, _) =>
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp",
            };
            if (dialog.ShowDialog(window) == true)
            {
                Surface.History.Execute(_document,
                    new SetPageBackgroundCommand(pageIndex, dialog.FileName, page.BackgroundColorArgb));
                imageText.Text = $"Background image: {Path.GetFileName(dialog.FileName)}";
                Surface.ReloadFromDocument();
                BuildPageStrip();
            }
        };

        var clearImage = new Button { Content = "Remove background image", Margin = new Thickness(12, 6, 12, 6) };
        clearImage.Click += (_, _) =>
        {
            Surface.History.Execute(_document,
                new SetPageBackgroundCommand(pageIndex, null, page.BackgroundColorArgb));
            imageText.Text = "Background image: none";
            Surface.ReloadFromDocument();
            BuildPageStrip();
        };

        var panel = new StackPanel();
        panel.Children.Add(colorText);
        panel.Children.Add(pickColor);
        panel.Children.Add(imageText);
        panel.Children.Add(pickImage);
        panel.Children.Add(clearImage);
        window.Content = panel;
        window.ShowDialog();
    }

    private void UpdatePageText()
    {
        PageText.Text = $"Page {_document.ActivePage + 1} / {_document.PageCount}";
        BuildPageStrip();
    }

    private void UpdateZoomText() => ZoomText.Text = $"{Surface.ViewScale * 100:F0}%";

    private void OnAddPageClick(object sender, RoutedEventArgs e)
    {
        Surface.History.Execute(_document, new AddPageCommand());
        Surface.ReloadFromDocument();
        UpdatePageText();
        UpdateStatus();
    }

    private void OnPrevPageClick(object sender, RoutedEventArgs e) => GoToPage(_document.ActivePage - 1);

    private void OnNextPageClick(object sender, RoutedEventArgs e) => GoToPage(_document.ActivePage + 1);

    private void OnZoomInClick(object sender, RoutedEventArgs e) => Surface.ZoomBy(1.25);

    private void OnZoomOutClick(object sender, RoutedEventArgs e) => Surface.ZoomBy(1 / 1.25);

    private void OnZoomResetClick(object sender, RoutedEventArgs e) => Surface.ResetView();

    private void GoToPage(int index)
    {
        Surface.CommitText();
        if (index < 0 || index >= _document.PageCount || index == _document.ActivePage)
        {
            return;
        }

        _document.ActivePage = index;
        Surface.Selection.Clear();
        Surface.ReloadFromDocument();
        UpdatePageText();
        UpdateStatus();
    }

    // ---- calibration ---------------------------------------------------------------

    private Guid BoardKey
    {
        get
        {
            var status = Surface.Digitizer;
            if (status is null || !status.UsbDigitizerPresent)
            {
                return Guid.Empty;
            }

            var signature = DescribeDigitizer(status) + "|" +
                            $"{_document.CanvasWidth:0}x{_document.CanvasHeight:0}";
            return StableKey(signature);
        }
    }

    private static string DescribeDigitizer(DigitizerStatus? status)
    {
        if (status is null)
        {
            return "unknown";
        }

        return status.Devices
            .Where(d => d.Kind is DigitizerKind.TouchScreen or DigitizerKind.Pen
                or DigitizerKind.TouchAndPen)
            .Select(d => $"{d.VendorId:X4}:{d.ProductId:X4}")
            .OrderBy(s => s, StringComparer.Ordinal)
            .FirstOrDefault() ?? "no-hid-digitizer";
    }

    private static Guid StableKey(string signature)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(signature));
        return new Guid(hash.AsSpan(0, 16));
    }

    private void OnCalibrateClick(object sender, RoutedEventArgs e)
    {
        if (CalibrationLayer.Visibility == Visibility.Visible)
        {
            EndCalibration(apply: false);
            return;
        }

        var width = (int)Math.Round(Surface.ActualWidth);
        var height = (int)Math.Round(Surface.ActualHeight);
        if (width < 10 || height < 10)
        {
            MessageBox.Show(this, "The board surface has no size yet. Maximize the window and retry.",
                "zEClass", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var key = BoardKey;
        if (key != Guid.Empty && _calibrationStore.TryLoad(key, out var existing) && existing is not null)
        {
            var choice = MessageBox.Show(this,
                "This board is already calibrated.\n\n" +
                "Yes = start a new calibration, No = cancel.",
                "zEClass", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (choice != MessageBoxResult.Yes)
            {
                return;
            }
        }

        CalibrationLayer.SurfaceSize = new Size(width, height);
        CalibrationLayer.Begin(width, height);
        CalibrationLayer.Visibility = Visibility.Visible;
        CalibrationLayer.Focus();
        StatusText.Text =
            "Calibration: touch the centre of each cross with the pen or your finger (4 targets).";
        UpdateCalibrationButtons();
    }

    private void OnCalibrationStateChanged(object? sender, EventArgs e)
    {
        if (CalibrationLayer.IsFinished)
        {
            return;
        }

        StatusText.Text = $"Calibration: target {CalibrationLayer.TargetIndex + 1} of " +
                          $"{CalibrationLayer.TargetCount}.";
    }

    private void EndCalibration(bool apply)
    {
        CalibrationLayer.Visibility = Visibility.Collapsed;

        if (!apply)
        {
            StatusText.Text = "Calibration cancelled.";
            UpdateCalibrationButtons();
            return;
        }

        if (CalibrationLayer.Result is null)
        {
            MessageBox.Show(this,
                "That calibration did not produce a usable mapping. The four hits were too close " +
                "together or on a straight line. Try again and hit each cross near its centre.",
                "zEClass", MessageBoxButton.OK, MessageBoxImage.Warning);
            StatusText.Text = "Calibration failed; the previous setting is unchanged.";
            UpdateCalibrationButtons();
            return;
        }

        var error = CalibrationLayer.ErrorPixels;
        Surface.ApplyCalibration(CalibrationLayer.Result, error);

        if (CalibrationLayer.BuildCalibration() is { } calibration)
        {
            calibration.BoardId = BoardKey;
            calibration.CalibratedUtc = DateTimeOffset.UtcNow;
            calibration.DisplayWidth = (int)Surface.ActualWidth;
            calibration.DisplayHeight = (int)Surface.ActualHeight;
            if (!_calibrationStore.TrySave(calibration))
            {
                MessageBox.Show(this,
                    "The calibration works for this session but could not be saved, so it will " +
                    "be lost on exit.",
                    "zEClass", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        StatusText.Text = $"Calibrated. Worst error {error:F1} px.";
        if (!CalibrationLayer.IsAcceptable)
        {
            MessageBox.Show(this,
                $"Calibration applied with a worst-case error of {error:F1} px, above the " +
                $"{CalibrationOverlay.AcceptableErrorPixels:F0} px target. Ink will land " +
                "consistently but slightly off; recalibrate if that is visible during a lesson.",
                "zEClass", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        UpdateCalibrationButtons();
        UpdateStatus();
    }

    private void OnCalibrationKeyDown(object sender, KeyEventArgs e)
    {
        if (CalibrationLayer.Visibility != Visibility.Visible)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Escape:
                EndCalibration(apply: false);
                e.Handled = true;
                break;
            case Key.Enter when CalibrationLayer.IsFinished:
                EndCalibration(apply: true);
                e.Handled = true;
                break;
        }
    }

    private void RestoreCalibration()
    {
        var key = BoardKey;
        if (key == Guid.Empty || !_calibrationStore.TryLoad(key, out var calibration) ||
            calibration is null || calibration.BuildTransform() is not { } transform)
        {
            Surface.ApplyCalibration(null);
            UpdateCalibrationButtons();
            UpdateStatus();
            return;
        }

        var error = CalibrationOverlay.ComputeWorstResidual(calibration, transform);
        Surface.ApplyCalibration(transform, error);
        CrashLog.Info(
            $"Applied stored calibration for {key:N}, worst error {error:F1} px, " +
            $"saved {calibration.CalibratedUtc:O}");
        UpdateCalibrationButtons();
    }

    private void UpdateCalibrationButtons()
    {
        var calibrating = CalibrationLayer.Visibility == Visibility.Visible;
        CalibrateBtn.Content = calibrating ? "Done" : "Align";
        var key = BoardKey;
        var calibrated = key != Guid.Empty && _calibrationStore.TryLoad(key, out var c) && c is not null;
        CalibrateBtn.BorderBrush = calibrated
            ? new SolidColorBrush(Color.FromRgb(0x43, 0xA0, 0x47))
            : new SolidColorBrush(calibrating
                ? Color.FromRgb(0x3D, 0x7E, 0xFF)
                : Color.FromRgb(0x3A, 0x3F, 0x4B));
        CalibrateBtn.ToolTip = calibrated
            ? "Board is calibrated. Click to recalibrate."
            : "Board is not calibrated. Click to run four-point calibration.";
    }

    // ---- file ---------------------------------------------------------------------

    /// <summary>
    /// Brings external material onto the board as new pages. Anything that cannot be read
    /// produces a warning rather than a blank page, and the reason is shown in full, because a
    /// teacher who gets a blank page has no way to know whether the file or the app is at fault.
    /// </summary>
    private void OnImportClick(object sender, RoutedEventArgs e)
    {
        var dialog = Importer.CreateDialog();
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var result = Importer.Import(_document, Surface, dialog.FileNames);
        if (result.PagesAdded > 0)
        {
            _document.ActivePage = _document.PageCount - 1;
            Surface.ReloadFromDocument();
            BuildPageStrip();
            UpdatePageText();
        }

        if (result.Warnings.Count > 0)
        {
            MessageBox.Show(this,
                $"Imported {result.PagesAdded} page(s).\n\n" +
                string.Join("\n\n", result.Warnings),
                "zEClass import", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else if (result.PagesAdded == 0)
        {
            StatusText.Text = "Nothing to import.";
        }
        else
        {
            StatusText.Text = $"Imported {result.PagesAdded} page(s).";
        }
    }

    private void OnPrintClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Surface.CommitText();
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "zEClass");
            Directory.CreateDirectory(folder);
            var staged = Path.Combine(folder,
                $"zEClass-print-{DateTime.Now:yyyyMMdd-HHmmss}.pdf");
            BoardRenderer.WritePdf(staged, _document, _document.Pages.OrderBy(p => p.Index),
                Surface.CanvasWidth, Surface.CanvasHeight);

            // ShellExecute print is the only route that works on a school image with no PDF
            // viewer installed; Acrobat's "print" verb is the reliable one.
            var psi = new System.Diagnostics.ProcessStartInfo(staged)
            {
                Verb = "print",
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
            };
            if (System.Diagnostics.Process.Start(psi) is null)
            {
                StatusText.Text = $"Ready to print: {staged}";
                return;
            }

            StatusText.Text = "Sent to the default printer.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            CrashLog.Write("Print", ex);
            MessageBox.Show(this,
                $"Could not start printing.\n\n{ex.Message}\n\n" +
                "A PDF has been written to your Pictures folder instead.",
                "zEClass", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// Packages the board and opens a mail draft. The board file plus rendered pages go into a
    /// zip on disk; the draft is composed with the board attached, and nothing is sent without
    /// the teacher sending it from their own mail client.
    /// </summary>
    private void OnEmailClick(object sender, RoutedEventArgs e)
    {
        string? zip = null;
        try
        {
            Surface.CommitText();
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "zEClass");
            Directory.CreateDirectory(folder);
            zip = Path.Combine(folder,
                $"{SanitizeFileName(_document.Name)}-{DateTime.Now:yyyyMMdd-HHmmss}.zip");

            using (var archive = System.IO.Compression.ZipFile.Open(zip,
                       System.IO.Compression.ZipArchiveMode.Create))
            {
                var board = Path.Combine(folder, "board.ebboard");
                TrySave(board);
                archive.CreateEntryFromFile(board, "board.ebboard");

                var staging = Path.Combine(Path.GetTempPath(),
                    "zEClass-mail-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(staging);
                try
                {
                    BoardRenderer.WritePdf(Path.Combine(staging, "board.pdf"), _document,
                        _document.Pages.OrderBy(p => p.Index),
                        Surface.CanvasWidth, Surface.CanvasHeight);
                    archive.CreateEntryFromFile(Path.Combine(staging, "board.pdf"), "board.pdf");
                }
                finally
                {
                    try
                    {
                        Directory.Delete(staging, true);
                    }
                    catch (IOException)
                    {
                    }
                }
            }

            var psi = new System.Diagnostics.ProcessStartInfo("mailto:")
            {
                UseShellExecute = true,
            };
            psi.ArgumentList.Add("?subject=" + Uri.EscapeDataString(
                $"{_document.Name} ({_document.PageCount} pages)"));
            psi.ArgumentList.Add("&body=" + Uri.EscapeDataString(
                "The board has been attached as a zip."));
            System.Diagnostics.Process.Start(psi);

            StatusText.Text = $"Packaged as {Path.GetFileName(zip)}; check your mail draft.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            CrashLog.Write("Email", ex);
            StatusText.Text = zip is not null && File.Exists(zip)
                ? $"Could not open a mail draft. The package is at {zip}."
                : "Could not package the board for email.";
        }
    }

    private PlaybackWindow? _playback;
    private readonly ScreenRecorder _recorder = new();
    private readonly AudioRecorder _audio = new();
    private readonly DispatcherTimer _recordTimer;

    private void OnPlaybackClick(object sender, RoutedEventArgs e)
    {
        if (_playback is not null && _playback.IsVisible)
        {
            _playback.Close();
            _playback = null;
            return;
        }

        _playback = new PlaybackWindow(_document) { Owner = this };
        if (!_playback.HasRecordedContent)
        {
            MessageBox.Show(this,
                "This page has no timed strokes to replay. Playback follows the timestamps " +
                "recorded as you draw, so a page drawn before this feature existed replays as " +
                "a single moment.",
                "zEClass", MessageBoxButton.OK, MessageBoxImage.Information);
            _playback.Close();
            _playback = null;
            return;
        }

        _playback.Show();
    }

    private void OnRecordClick(object sender, RoutedEventArgs e)
    {
        if (_recorder.IsRecording)
        {
            var path = _recorder.Stop();
            _recordTimer.Stop();
            RecordBtn.Content = "Record";
            StatusText.Text = path is null
                ? "Recording stopped with no frames captured."
                : $"Recording saved to {Path.GetFileName(path)}.";
            return;
        }

        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "zEClass");
        var target = Path.Combine(folder,
            $"zEClass-{DateTime.Now:yyyyMMdd-HHmmss}.avi");
        if (!_recorder.Start(target))
        {
            StatusText.Text = "Could not start the recording.";
            return;
        }

        _recordTimer.Start();
        RecordBtn.Content = "Stop";
        StatusText.Text = "Recording. Uncompressed AVI, so expect a large file.";
    }

    private void OnAudioClick(object sender, RoutedEventArgs e)
    {
        if (_audio.IsRecording)
        {
            // Elapsed is read before stopping: Stop resets the timer, so reading it after
            // would always report a zero-length recording.
            var elapsed = _audio.Elapsed;
            var path = _audio.Stop();
            AudioBtn.Content = "Audio";
            RecordStatus.Text = path is null
                ? "Audio recording failed. Check that a microphone is available."
                : $"Audio saved to {Path.GetFileName(path)} ({elapsed:mm\\:ss}).";
            return;
        }

        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "zEClass");
        var target = Path.Combine(folder,
            $"zEClass-audio-{DateTime.Now:yyyyMMdd-HHmmss}.wav");
        if (!_audio.Start(target))
        {
            RecordStatus.Text = "Could not start audio recording. Check that a microphone is available.";
            return;
        }

        AudioBtn.Content = "Stop";
        RecordStatus.Text = "Recording audio. Uncompressed WAV, about 10 MB per minute.";
    }

    private void OnRecordTick(object? sender, EventArgs e)
    {
        if (!_recorder.CaptureFrame())
        {
            return;
        }

        RecordStatus.Text = $"{_recorder.FrameCount} frames, " +
                            $"{_recorder.BufferedBytes / (1024 * 1024)} MB buffered";
    }

    private HardwareAcceptanceWindow? _acceptance;
    private AboutWindow? _about;

    /// <summary>Opens the About dialog with the developer credit and branding.</summary>
    private void OnAboutClick(object sender, RoutedEventArgs e)
    {
        if (_about is not null && _about.IsVisible)
        {
            _about.Activate();
            return;
        }

        _about = new AboutWindow { Owner = this };
        _about.Closed += (_, _) => _about = null;
        _about.Show();
    }

    /// <summary>
    /// Opens the guided hardware acceptance run. It takes a live surface of its own, so the
    /// teacher can draw on the test page without disturbing the lesson board.
    /// </summary>
    private void OnAcceptanceClick(object sender, RoutedEventArgs e)
    {
        if (_acceptance is not null && _acceptance.IsVisible)
        {
            _acceptance.Activate();
            return;
        }

        _acceptance = new HardwareAcceptanceWindow { Owner = this };
        _acceptance.Closed += (_, _) => _acceptance = null;
        _acceptance.Show();
    }

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeBox.SelectedItem is ThemeColors theme)
        {
            ThemeManager.Apply(theme);
            _document.UIStyle = Theme.All.ToList().IndexOf(theme);
            UpdateStatus();
        }
    }

    /// <summary>
    /// Fills the language picker with native names, ordered so English comes first and the rest
    /// follow the catalogue order. Codes are shown alongside because two users may genuinely
    /// want different variants, and "Deutsch" alone does not tell you which file was loaded.
    /// </summary>
    private void PopulateLanguageBox()
    {
        LanguageBox.Items.Clear();
        foreach (var info in LanguageCatalog.All)
        {
            if (!Locator.Current.AvailableLanguages.Contains(info.Code, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            LanguageBox.Items.Add(info);
        }

        // A catalogue file can exist for a language the static table does not know about, for
        // example one dropped in by hand. Offer it rather than silently hiding it.
        foreach (var code in Locator.Current.AvailableLanguages)
        {
            if (LanguageCatalog.Find(code) is null)
            {
                LanguageBox.Items.Add(new LanguageInfo(code, code, code, false));
            }
        }

        var current = LanguageBox.Items
            .OfType<LanguageInfo>()
            .FirstOrDefault(i => string.Equals(i.Code, Locator.Current.Language,
                StringComparison.OrdinalIgnoreCase))
            ?? LanguageBox.Items.OfType<LanguageInfo>().FirstOrDefault();

        LanguageBox.SelectedItem = current;
        if (current is not null)
        {
            LanguageBox.ToolTip = $"{current.EnglishName} ({current.Code})";
        }
    }

    /// <summary>
    /// Applies a language, including mirroring the whole window for a right-to-left script.
    /// Half-mirrored is worse than untranslated, so the direction is part of applying a language
    /// rather than an afterthought.
    /// </summary>
    private void ApplyLanguage(string code)
    {
        if (!Locator.Current.SetLanguage(code))
        {
            return;
        }

        _document.Language = Locator.Current.Language;
        FlowDirection = LanguageCatalog.IsRightToLeft(Locator.Current.Language)
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;
        RefreshLocalizedText();
    }

    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializingPickers)
        {
            return;
        }

        if (LanguageBox.SelectedItem is LanguageInfo info)
        {
            LanguageBox.ToolTip = $"{info.EnglishName} ({info.Code})";
            ApplyLanguage(info.Code);
        }
    }

    /// <summary>
    /// Re-reads every user-visible string. Called at startup and after a language change; it is
    /// deliberately a flat switch rather than bindings, because a partial translation must
    /// degrade word by word instead of leaving a window half-bound.
    /// </summary>
    private void RefreshLocalizedText()
    {
        DocNameText.Text = _document.Name;
        PenBtn.Content = Locator.T("tool.pen");
        HighlighterBtn.Content = Locator.T("tool.highlighter");
        EraserBtn.Content = Locator.T("tool.eraser");
        LineBtn.Content = Locator.T("tool.line");
        RectBtn.Content = Locator.T("tool.rect");
        EllipseBtn.Content = Locator.T("tool.ellipse");
        TriangleBtn.Content = Locator.T("tool.triangle");
        ArrowBtn.Content = Locator.T("tool.arrow");
        StarBtn.Content = Locator.T("tool.star");
        TextBtn.Content = Locator.T("tool.text");
        SelectBtn.Content = Locator.T("tool.select");
        LassoBtn.Content = Locator.T("tool.lasso");
        PanBtn.Content = Locator.T("tool.pan");
        UndoBtn.Content = Locator.T("action.undo");
        RedoBtn.Content = Locator.T("action.redo");
        ClearBtn.Content = Locator.T("action.clear");
        NewBtn.Content = Locator.T("action.new");
        OpenBtn.Content = Locator.T("action.open");
        ImportBtn.Content = Locator.T("action.import");
        SaveBtn.Content = Locator.T("action.save");
        SaveAsBtn.Content = Locator.T("action.saveas");
        ExportBtn.Content = Locator.T("action.export");
        PrintBtn.Content = Locator.T("action.print");
        EmailBtn.Content = Locator.T("action.email");
        FullScreenBtn.Content = Locator.T("action.fullscreen");
        CalibrateBtn.Content = Locator.T("action.align");
        DiagBtn.Content = Locator.T("action.diagnostics");
        ExportDiagBtn.Content = Locator.T("action.saveReport");
        Title = Locator.T("app.title");
    }

    private void OnNewClick(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "Start a new board? Unsaved changes are lost.", "zEClass",
                MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
        {
            return;
        }

        _document = new BoardDocument { PageCount = 5 };
        _document.EnsurePages();
        _filePath = null;
        Surface.History.Clear();
        DocNameText.Text = _document.Name;
        RebindSurface();
        UpdatePageText();
        UpdateStatus();
    }

    private void OnOpenClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "zEClass board (*.ebboard)|*.ebboard|All files (*.*)|*.*",
            InitialDirectory = SafzEClasssFolder(),
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        if (BoardSerializer.TryLoad(dialog.FileName, out var doc, out var error))
        {
            _document = doc!;
            _filePath = dialog.FileName;
            Surface.History.Clear();
            DocNameText.Text = _document.Name;
            RebindSurface();
            UpdatePageText();
            UpdateZoomText();
            UpdateStatus();
        }
        else
        {
            MessageBox.Show(this, $"Could not open the board file.\n\n{error}", "zEClass",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_filePath))
        {
            OnSaveAsClick(sender, e);
            return;
        }

        Surface.CommitText();
        if (TrySave(_filePath))
        {
            UpdateStatus();
        }
    }

    private void OnSaveAsClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "zEClass board (*.ebboard)|*.ebboard",
            DefaultExt = BoardDocument.FileExtension,
            FileName = SanitizeFileName(_document.Name) + BoardDocument.FileExtension,
            InitialDirectory = SafzEClasssFolder(),
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        _filePath = dialog.FileName;
        _document.Name = Path.GetFileNameWithoutExtension(_filePath);
        DocNameText.Text = _document.Name;
        Surface.CommitText();
        TrySave(_filePath);
        UpdateStatus();
    }

    private void OnExportClick(object sender, RoutedEventArgs e) => ExportDialog.Show(this, _document, Surface);

    private bool TrySave(string path)
    {
        try
        {
            BoardSerializer.Save(_document, path);
            CrashLog.Info($"Saved board to {path}");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or NotSupportedException or ArgumentException)
        {
            CrashLog.Write("Save", ex);
            MessageBox.Show(this, $"Could not save the board.\n\n{ex.Message}", "zEClass",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    private void AutoSave()
    {
        try
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "zEClass", "boards");
            Directory.CreateDirectory(folder);
            BoardSerializer.Save(_document, Path.Combine(folder, "autosave.ebboard"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashLog.Write("AutoSave", ex);
        }
    }

    private void CopyPageImage()
    {
        try
        {
            var bitmap = BoardRenderer.RenderPageToBitmap(_document, Surface.CanvasWidth,
                Surface.CanvasHeight);
            var data = new System.Windows.Media.Imaging.PngBitmapEncoder();
            data.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = new MemoryStream();
            data.Save(stream);
            stream.Position = 0;
            Clipboard.SetDataObject(new DataObject(bitmap), true);
            StatusText.Text = "Page copied to the clipboard as an image.";
        }
        catch (Exception ex)
        {
            CrashLog.Write("CopyPageImage", ex);
            StatusText.Text = "Could not copy the page image.";
        }
    }

    private static string SafzEClasssFolder()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "zEClass");
        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception)
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }

        return folder;
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "board" : cleaned;
    }

    // ---- diagnostics ----------------------------------------------------------------

    private void OnDiagClick(object sender, RoutedEventArgs e)
    {
        var body = BuildDiagnosticsBody();
        MessageBox.Show(this, body, "zEClass diagnostics",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnExportDiagnosticsClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "Diagnostics (*.txt)|*.txt",
            FileName = $"zEClass-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            CrashLog.ExportDiagnostics(dialog.FileName, BuildDiagnosticsBody());
            StatusText.Text = $"Diagnostics written to {dialog.FileName}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashLog.Write("ExportDiagnostics", ex);
            StatusText.Text = "Could not write the diagnostics file.";
        }
    }

    private string BuildDiagnosticsBody()
    {
        var status = Surface.Digitizer ?? _digitizer.Probe();
        var sb = new StringBuilder();
        sb.AppendLine($"OS: {Environment.OSVersion}");
        sb.AppendLine($"Runtime: {Environment.Version} ({RuntimeInformation.FrameworkDescription})");
        sb.AppendLine($"OS64: {Environment.Is64BitOperatingSystem} App64: {Environment.Is64BitProcess}");
        sb.AppendLine($"Touch: {status.TouchActive}  Pen: {status.PenActive}  USB: {status.UsbDigitizerPresent}");
        sb.AppendLine();
        sb.AppendLine("Digitizer devices:");
        if (status.Devices.Count == 0)
        {
            sb.AppendLine("  (none)");
        }
        else
        {
            foreach (var d in status.Devices)
            {
                sb.AppendLine($"  {d.Summary}");
                if (!string.IsNullOrEmpty(d.Manufacturer))
                {
                    sb.AppendLine($"    manufacturer: {d.Manufacturer}");
                }
            }
        }

        sb.AppendLine();
        sb.AppendLine("Pointer input:");
        sb.AppendLine($"  Pointer messages: {(Surface.HasPointerTarget ? "registered" : "FALLBACK")}");
        sb.AppendLine($"  Active pen contacts:    {Surface.Engine.ActiveStylusCount}");
        sb.AppendLine($"  Active touch contacts:  {Surface.Engine.ActiveTouchCount}");
        sb.AppendLine($"  Palm rejection: {Surface.Engine.PalmRejectionEnabled}");
        sb.AppendLine($"  Contacts tracked:       {Surface.Gestures.ActiveContactCount}");
        sb.AppendLine($"  Gestures enabled:       {Surface.Gestures.Enabled}");
        sb.AppendLine();
        sb.AppendLine("Calibration:");
        sb.AppendLine($"  Applied: {Surface.IsCalibrated}");
        sb.AppendLine($"  Worst error: {(double.IsNaN(Surface.CalibrationError) ? "n/a" : $"{Surface.CalibrationError:F1} px")}");
        sb.AppendLine($"  Stored boards: {_calibrationStore.ListBoards().Count}");
        sb.AppendLine();
        sb.AppendLine("Board:");
        sb.AppendLine($"  Pages: {_document.PageCount}  Active: {_document.ActivePage + 1}");
        sb.AppendLine($"  Strokes on page: {_document.Active().Strokes.Count}");
        sb.AppendLine($"  Images on page: {_document.Active().Images.Count}");
        sb.AppendLine($"  Undo depth: {Surface.History.UndoCount}  Redo depth: {Surface.History.RedoCount}");
        sb.AppendLine($"  Surface: {Surface.ActualWidth:F0} x {Surface.ActualHeight:F0}  " +
                      $"Canvas: {Surface.CanvasWidth:F0} x {Surface.CanvasHeight:F0}");
        sb.AppendLine($"  View: {Surface.ViewScale * 100:F0}%  offset " +
                      $"({Surface.ViewOffsetX:F0}, {Surface.ViewOffsetY:F0})");
        sb.AppendLine();
        sb.AppendLine($"Board file: {_filePath ?? "(not saved yet)"}");
        return sb.ToString();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out System.Drawing.Point point);

    private void UpdateStatus()
    {
        var page = _document.Active();
        var strokes = page.Strokes.Count;
        var selection = Surface.Selection;
        var parts = new List<string>
        {
            _document.Name,
            $"page {_document.ActivePage + 1}/{_document.PageCount}",
            $"{strokes} stroke{(strokes == 1 ? "" : "s")}",
        };

        if (page.Images.Count > 0)
        {
            parts.Add($"{page.Images.Count} image(s)");
        }

        if (selection.Strokes.Count > 0)
        {
            parts.Add($"{selection.Strokes.Count} selected");
        }

        if (page.Locked)
        {
            parts.Add("locked");
        }

        if (Surface.IsCalibrated)
        {
            parts.Add("calibrated");
        }

        if (Surface.StatusMessage is { } message)
        {
            parts.Add(message);
        }

        StatusText.Text = string.Join("  |  ", parts);
    }

    // ---- full screen ---------------------------------------------------------------

    private void OnFullScreenOn(object sender, RoutedEventArgs e)
    {
        if (_suppressFullScreenToggle)
        {
            return;
        }

        _preFullScreenStyle = WindowStyle;
        _preFullScreenState = WindowState;
        WindowStyle = WindowStyle.None;
        WindowState = WindowState.Maximized;
    }

    private void OnFullScreenOff(object sender, RoutedEventArgs e)
    {
        if (_suppressFullScreenToggle)
        {
            return;
        }

        WindowStyle = _preFullScreenStyle;
        WindowState = _preFullScreenState;
    }

    // ---- keyboard ------------------------------------------------------------------

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (CalibrationLayer.Visibility == Visibility.Visible)
        {
            return;
        }

        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);

        // Text tool captures printable characters before any shortcut.
        if (Surface.TextSession is { IsEditing: true } session)
        {
            if (e.Key == Key.Escape)
            {
                Surface.CommitText();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Back)
            {
                Surface.BackspaceText();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Enter)
            {
                Surface.CommitText();
                e.Handled = true;
                return;
            }

            if (e.Key >= Key.A && e.Key <= Key.Z && !ctrl)
            {
                Surface.TypeText(((char)e.Key).ToString());
                e.Handled = true;
                return;
            }

            if (e.Key >= Key.D0 && e.Key <= Key.D9 && !ctrl)
            {
                Surface.TypeText(((char)('0' + (e.Key - Key.D0))).ToString());
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Space)
            {
                Surface.TypeText(" ");
                e.Handled = true;
                return;
            }
        }

        switch (e.Key)
        {
            case Key.Escape when FullScreenBtn.IsChecked == true:
                _suppressFullScreenToggle = true;
                FullScreenBtn.IsChecked = false;
                _suppressFullScreenToggle = false;
                e.Handled = true;
                break;
            case Key.S when ctrl:
                OnSaveClick(sender, e);
                e.Handled = true;
                break;
            case Key.O when ctrl:
                OnOpenClick(sender, e);
                e.Handled = true;
                break;
            case Key.Y when ctrl:
                Surface.Redo();
                e.Handled = true;
                break;
            case Key.Z when ctrl:
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                {
                    Surface.Redo();
                }
                else
                {
                    Surface.Undo();
                }

                e.Handled = true;
                break;
            case Key.C when ctrl:
                Surface.CopySelection();
                e.Handled = true;
                break;
            case Key.X when ctrl:
                Surface.CutSelection();
                e.Handled = true;
                break;
            case Key.V when ctrl:
                Surface.Paste();
                e.Handled = true;
                break;
            case Key.Delete:
                Surface.DeleteSelection();
                BuildPageStrip();
                e.Handled = true;
                break;
            case Key.PageDown:
            case Key.Right when !ctrl:
                GoToPage(_document.ActivePage + 1);
                e.Handled = true;
                break;
            case Key.PageUp:
            case Key.Left when !ctrl:
                GoToPage(_document.ActivePage - 1);
                e.Handled = true;
                break;
            case Key.F11:
                FullScreenBtn.IsChecked = !(FullScreenBtn.IsChecked ?? false);
                e.Handled = true;
                break;
            case Key.Add or Key.OemPlus when ctrl:
                Surface.ZoomBy(1.25);
                e.Handled = true;
                break;
            case Key.Subtract or Key.OemMinus when ctrl:
                Surface.ZoomBy(1 / 1.25);
                e.Handled = true;
                break;
            case Key.D0 when ctrl:
                Surface.ResetView();
                e.Handled = true;
                break;
            case Key.B:
                OnToolClick(PenBtn, e);
                break;
            case Key.E:
                OnToolClick(EraserBtn, e);
                break;
            case Key.H:
                OnToolClick(HighlighterBtn, e);
                break;
            case Key.T:
                OnToolClick(TextBtn, e);
                break;
            case Key.N when ctrl:
                OnAddPageClick(sender, e);
                e.Handled = true;
                break;
            case Key.F1:
                OnMagnifierClick(sender, e);
                e.Handled = true;
                break;
            case Key.F3:
                OnCurtainClick(sender, e);
                e.Handled = true;
                break;
            case Key.F4:
                OnClockClick(sender, e);
                e.Handled = true;
                break;
            case Key.F5:
                OnKeyboardClick(sender, e);
                e.Handled = true;
                break;
            case Key.F6:
                OnCaptureClick(sender, e);
                e.Handled = true;
                break;
        }
    }
}
