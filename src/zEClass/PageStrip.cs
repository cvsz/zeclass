using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using zEClass.Core;

namespace zEClass;

/// <summary>
/// One thumbnail in the page strip. Renders a live preview of the page so the teacher can see
/// what they are about to switch to, matching the page contents preview in the vendor manual.
/// </summary>
public sealed class PageThumbnail : Border
{
    private readonly Image _preview = new() { Stretch = Stretch.Uniform, SnapsToDevicePixels = true };
    private readonly Border _page = new();
    private readonly TextBlock _label = new();
    private readonly StackPanel _root;

    public PageThumbnail()
    {
        Width = 108;
        Height = 78;
        Margin = new Thickness(3);
        CornerRadius = new CornerRadius(4);
        BorderThickness = new Thickness(2);
        Child = _root = new StackPanel();

        _page.Height = 54;
        _page.Background = Brushes.White;
        _page.Child = _preview;
        _page.ClipToBounds = true;

        _label.TextAlignment = TextAlignment.Center;
        _label.FontSize = 11;
        _label.Foreground = Brushes.White;
        _label.Margin = new Thickness(0, 2, 0, 0);

        _root.Children.Add(_page);
        _root.Children.Add(_label);
    }

    public int PageIndex { get; private set; }

    public void Bind(int pageIndex, string name, bool isActive, bool isLocked)
    {
        PageIndex = pageIndex;
        _label.Text = isLocked ? $"{pageIndex + 1} locked" : $"{pageIndex + 1}";
        BorderBrush = isActive
            ? new SolidColorBrush(Color.FromRgb(0x3D, 0x7E, 0xFF))
            : new SolidColorBrush(Color.FromRgb(0x55, 0x5B, 0x66));
        Background = new SolidColorBrush(isActive
            ? Color.FromRgb(0x33, 0x39, 0x45)
            : Color.FromRgb(0x24, 0x27, 0x2E));
        ToolTip = name;
    }

    /// <summary>Renders the page into the thumbnail. Cheap: it is only redrawn on page change.</summary>
    public void Render(BoardPage page, double surfaceWidth, double surfaceHeight)
    {
        if (page.Strokes.Count > 2000 || surfaceWidth <= 0 || surfaceHeight <= 0)
        {
            _preview.Source = null;
            return;
        }

        try
        {
            var scale = Math.Min(_page.ActualWidth > 0 ? _page.ActualWidth : 102, surfaceWidth) /
                        surfaceWidth;
            var w = Math.Max(1, (int)(surfaceWidth * scale));
            var h = Math.Max(1, (int)(surfaceHeight * scale));

            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                var pageColor = Color.FromArgb((byte)((page.BackgroundColorArgb >> 24) & 0xFF),
                    (byte)((page.BackgroundColorArgb >> 16) & 0xFF),
                    (byte)((page.BackgroundColorArgb >> 8) & 0xFF),
                    (byte)(page.BackgroundColorArgb & 0xFF));
                dc.DrawRectangle(new SolidColorBrush(pageColor), null, new Rect(0, 0, w, h));
                dc.PushTransform(new ScaleTransform(w / surfaceWidth, h / surfaceHeight));
                var engine = new InkEngine();
                foreach (var stroke in page.Strokes)
                {
                    if (stroke.Points.Count == 0)
                    {
                        continue;
                    }

                    if (stroke.Kind is StrokeKind.Shape or StrokeKind.Text)
                    {
                        continue;
                    }

                    var geometry = engine.BuildOutline(stroke.Points, Math.Max(1, stroke.Width), 0.5);
                    dc.DrawGeometry(engine.BrushFor(stroke), null, geometry);
                }

                dc.Pop();
            }

            var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            _preview.Source = bitmap;
        }
        catch (Exception ex)
        {
            CrashLog.Write("PageThumbnail", ex);
            _preview.Source = null;
        }
    }
}

/// <summary>
/// The horizontal page strip. Each thumbnail carries the vendor's per-page controls
/// (delete, copy, settings) from manual section 4.2 through its context menu.
/// </summary>
public sealed class PageStrip : ItemsControl
{
    public ObservableCollection<PageThumbnail> Thumbnails { get; } = new();

    public event EventHandler<int>? PageSelected;

    public event EventHandler<int>? PageDeleted;

    public event EventHandler<int>? PageDuplicated;

    public event EventHandler<int>? PageSettingsRequested;

    public event EventHandler<int>? PageLockToggled;

    public PageStrip()
    {
        ItemsSource = Thumbnails;
        HorizontalContentAlignment = HorizontalAlignment.Left;
    }

    public void Rebuild(BoardDocument document, InkSurface surface)
    {
        Thumbnails.Clear();
        foreach (var page in document.Pages.OrderBy(p => p.Index))
        {
            var thumb = new PageThumbnail();
            thumb.Bind(page.Index, page.Name, page.Index == document.ActivePage, page.Locked);
            thumb.MouseLeftButtonUp += (_, e) =>
            {
                PageSelected?.Invoke(this, page.Index);
                e.Handled = true;
            };
            thumb.ContextMenu = BuildMenu(page.Index);
            thumb.Render(page, surface.CanvasWidth, surface.CanvasHeight);
            Thumbnails.Add(thumb);
        }
    }

    private ContextMenu BuildMenu(int pageIndex)
    {
        var menu = new ContextMenu();
        var delete = new MenuItem { Header = "Delete page" };
        delete.Click += (_, _) => PageDeleted?.Invoke(this, pageIndex);
        var duplicate = new MenuItem { Header = "Duplicate page" };
        duplicate.Click += (_, _) => PageDuplicated?.Invoke(this, pageIndex);
        var settings = new MenuItem { Header = "Page settings..." };
        settings.Click += (_, _) => PageSettingsRequested?.Invoke(this, pageIndex);
        var lockItem = new MenuItem { Header = "Lock / unlock page" };
        lockItem.Click += (_, _) => PageLockToggled?.Invoke(this, pageIndex);

        menu.Items.Add(duplicate);
        menu.Items.Add(delete);
        menu.Items.Add(lockItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(settings);
        return menu;
    }
}
