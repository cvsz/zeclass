using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using System.Windows.Media;
using zEClass.Core;

namespace zEClass.Tools;

/// <summary>
/// Base class for the floating annotation overlays from vendor manual section 5.2.1
/// (magnifier, spotlight, screen curtain). Every tool is click-through by default so the
/// teacher can keep writing on the board while it is active, and each can be toggled with a
/// global hotkey. Documented Win32 only: no vendor hook DLLs are loaded or emulated.
/// </summary>
public abstract class OverlayToolWindow : Window
{
    private const int WmHotkey = 0x0312;
    private const int HotkeyId = 0xBEEF;

    private HwndSource? _source;
    private IntPtr _ownerHandle;
    private bool _hotkeyRegistered;

    protected OverlayToolWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        WindowStartupLocation = WindowStartupLocation.Manual;
        ShowActivated = false;
    }

    /// <summary>
    /// When set, the overlay ignores the mouse so the board underneath stays usable. This is
    /// implemented by making the window's root content hit-test transparent rather than by
    /// exotic transparency modes, which behave inconsistently on multi-monitor setups.
    /// </summary>
    public bool ClickThrough
    {
        get => _clickThrough;
        set
        {
            _clickThrough = value;
            if (Content is UIElement element)
            {
                element.IsHitTestVisible = !value;
            }
        }
    }

    private bool _clickThrough = true;

    /// <summary>True while the overlay is on screen. Named to avoid clashing with Window.IsActive.</summary>
    public new bool IsActive => Visibility == Visibility.Visible;

    /// <summary>Places the overlay in device pixels, which is what callers hold.</summary>
    public void MoveToDeviceRect(Rect deviceRect)
    {
        var dpi = OwnerDpi;
        Left = deviceRect.Left / dpi;
        Top = deviceRect.Top / dpi;
        Width = Math.Max(1, deviceRect.Width / dpi);
        Height = Math.Max(1, deviceRect.Height / dpi);
    }

    /// <summary>
    /// Device pixels per DIP for this window, used to convert between the device-pixel screen
    /// coordinates the capture layer works in and WPF's DIP-based layout.
    /// </summary>
    protected double OwnerDpi
    {
        get
        {
            try
            {
                var m = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice;
                if (m.HasValue && Math.Abs(m.Value.M11) > 0.0001)
                {
                    return 1 / m.Value.M11;
                }
            }
            catch (InvalidOperationException)
            {
            }

            return 1;
        }
    }

    /// <summary>Registers a system-wide hotkey against the owning window.</summary>
    public void RegisterHotkey(Window owner, Key key, ModifierKeys modifiers)
    {
        if (key == Key.None || _hotkeyRegistered)
        {
            return;
        }

        var helper = new WindowInteropHelper(owner);
        _ownerHandle = helper.EnsureHandle();
        _source = HwndSource.FromHwnd(_ownerHandle);
        _source.AddHook(HotkeyHook);

        var vk = KeyInterop.VirtualKeyFromKey(key);
        _hotkeyRegistered = RegisterHotKey(_ownerHandle, HotkeyId, ModifiersToNative(modifiers), vk);
        if (!_hotkeyRegistered)
        {
            CrashLog.Info(
                $"RegisterHotKey failed ({Marshal.GetLastWin32Error()}); " +
                $"{key} will not toggle the overlay globally.");
        }
    }

    private static uint ModifiersToNative(ModifierKeys modifiers)
    {
        uint result = 0;
        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            result |= 0x0001;
        }

        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            result |= 0x0002;
        }

        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            result |= 0x0004;
        }

        if (modifiers.HasFlag(ModifierKeys.Windows))
        {
            result |= 0x0008;
        }

        return result;
    }

    private IntPtr HotkeyHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            Toggle();
        }

        return IntPtr.Zero;
    }

    public virtual void Toggle() => Visibility = IsActive ? Visibility.Collapsed : Visibility.Visible;

    protected virtual void OnClosingCore()
    {
    }

    protected override void OnClosed(EventArgs e)
    {
        OnClosingCore();
        if (_hotkeyRegistered)
        {
            UnregisterHotKey(_ownerHandle, HotkeyId);
            _hotkeyRegistered = false;
        }

        if (_source is not null)
        {
            _source.RemoveHook(HotkeyHook);
            _source = null;
        }

        base.OnClosed(e);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, int vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}

/// <summary>
/// One-time screen capture helper. Uses <c>BitBlt</c> against the desktop DC, which is the
/// approach that works for hardware-accelerated and DirectComposition windows, where
/// <c>PrintWindow</c> and DWM thumbnails return black.
/// </summary>
public static class ScreenCapture
{
    public const int SrcCopy = 0x00CC0020;
    public const int ScreenDc = 0;

    /// <summary>Grabs a device-pixel rectangle from the virtual desktop.</summary>
    public static BitmapSource? Grab(int left, int top, int width, int height)
    {
        if (width < 1 || height < 1)
        {
            return null;
        }

        var hdc = GetDC(IntPtr.Zero);
        if (hdc == IntPtr.Zero)
        {
            return null;
        }

        var memoryDc = IntPtr.Zero;
        var bitmap = IntPtr.Zero;
        var previous = IntPtr.Zero;
        try
        {
            memoryDc = CreateCompatibleDC(hdc);
            bitmap = CreateCompatibleBitmap(hdc, width, height);
            if (memoryDc == IntPtr.Zero || bitmap == IntPtr.Zero)
            {
                return null;
            }

            previous = SelectObject(memoryDc, bitmap);
            if (!BitBlt(memoryDc, 0, 0, width, height, hdc, left, top, SrcCopy))
            {
                return null;
            }

            var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch (Exception ex) when (ex is ExternalException or ArgumentException or OutOfMemoryException)
        {
            return null;
        }
        finally
        {
            if (previous != IntPtr.Zero && memoryDc != IntPtr.Zero)
            {
                SelectObject(memoryDc, previous);
            }

            if (bitmap != IntPtr.Zero)
            {
                DeleteObject(bitmap);
            }

            if (memoryDc != IntPtr.Zero)
            {
                DeleteDC(memoryDc);
            }

            ReleaseDC(IntPtr.Zero, hdc);
        }
    }

    /// <summary>Screen bounds of the primary monitor in device pixels.</summary>
    public static Rect VirtualScreenBounds() => new(
        GetSystemMetrics(76), GetSystemMetrics(77), GetSystemMetrics(78), GetSystemMetrics(79));

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int width, int height);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BitBlt(IntPtr dest, int x, int y, int w, int h, IntPtr src, int sx, int sy,
        int rop);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr obj);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
}
