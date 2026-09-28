using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace zEClass;

/// <summary>
/// The About dialog: product name, version, developer credit, and branding artwork.
///
/// The artwork files live beside the executable under <c>Assets\</c> and are loaded defensively:
/// a missing or corrupt image is skipped, never a crash. That matters because the dialog must
/// open on a fresh install, on a machine where someone deleted an asset, and in the smoke test.
/// </summary>
public sealed class AboutWindow : Window
{
    public const string CompanyLogoFile = "zeazdev-logo.png";
    public const string EmblemFile = "emblem.png";

    public AboutWindow()
    {
        var info = AboutInfo.Current;

        Title = "About zEClass";
        Width = 480;
        Height = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(Color.FromRgb(0x14, 0x17, 0x1C));
        Foreground = Brushes.White;

        var root = new StackPanel
        {
            Margin = new Thickness(28, 24, 28, 20),
            Orientation = Orientation.Vertical,
        };

        var logo = TryLoadImage(CompanyLogoFile, 150);
        if (logo is not null)
        {
            logo.Margin = new Thickness(0, 0, 0, 8);
            root.Children.Add(logo);
        }

        root.Children.Add(new TextBlock
        {
            Text = info.ProductName,
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        });
        root.Children.Add(new TextBlock
        {
            Text = $"Version {info.Version}",
            FontSize = 13,
            Opacity = 0.7,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0),
        });

        var emblem = TryLoadImage(EmblemFile, 120);
        if (emblem is not null)
        {
            emblem.Margin = new Thickness(0, 16, 0, 0);
            root.Children.Add(emblem);
        }

        root.Children.Add(new TextBlock
        {
            Text = "Developed by " + info.Developer,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 16, 0, 0),
        });
        root.Children.Add(new TextBlock
        {
            Text = info.Copyright,
            FontSize = 12,
            Opacity = 0.7,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0),
        });
        root.Children.Add(new TextBlock
        {
            Text = info.Provenance,
            FontSize = 12,
            Opacity = 0.7,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 12, 0, 0),
        });

        var close = new Button
        {
            Content = "Close",
            MinWidth = 110,
            Padding = new Thickness(14, 6, 14, 6),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 20, 0, 0),
        };
        close.Click += (_, _) => Close();
        root.Children.Add(close);

        Content = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = root,
        };
    }

    /// <summary>
    /// Loads a branding image from the Assets folder, or returns null when it is missing or
    /// unreadable. Public so the smoke test can prove both paths.
    /// </summary>
    public static Image? TryLoadImage(string fileName, double height)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", fileName);
            if (!File.Exists(path))
            {
                return null;
            }

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();

            return new Image
            {
                Source = bitmap,
                Height = height,
                HorizontalAlignment = HorizontalAlignment.Center,
                Stretch = Stretch.Uniform,
            };
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException
            or System.Windows.Markup.XamlParseException)
        {
            return null;
        }
    }
}
