using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;

namespace GUI;

/// <summary>
/// The thumbnail a new submission starts with: the app's icon and name over the gradient the dark theme paints its windows with, drawn to a picture file.
/// </summary>
public static class DefaultThumbnail
{
    private const int Width = 1280;
    private const int Height = 720;

    /// <summary>Where the picture is written, under the temp folder like the thumbnails the upload converts.</summary>
    public static string FilePath { get; } = Path.Combine(Path.GetTempPath(), "CS2WorkshopManager", "default_thumbnail.png");

    /// <summary>Draws the thumbnail to <see cref="FilePath"/> and returns that path.</summary>
    public static string Render()
    {
        var application = Application.Current!;
        var main = ThemeColor(application, "AppColor");
        var contrast = ThemeColor(application, "ContrastColor");
        var accent = App.ThemeAccent(ThemeVariant.Dark);

        // the same gradient as a window's, from the same corner, with the icon and the name in the middle
        var picture = new Panel { Width = Width, Height = Height };

        picture.Children.Add(new DitheredGradient
        {
            CenterColor = WindowAccent.GradientCenter(main, accent),
            EdgeColor = main,
            GradientOrigin = new RelativePoint(0.9, 0, RelativeUnit.Relative),
        });

        var content = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Spacing = 32 };

        content.Children.Add(new Image
        {
            Source = new Bitmap(AssetLoader.Open(new Uri("avares://CS2WorkshopManager-GUI/assets/icon.png"))),
            Width = 280,
            Height = 280,
        });

        content.Children.Add(new TextBlock
        {
            Text = "CS2 Workshop Manager",
            FontSize = 64,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(contrast),
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        picture.Children.Add(content);

        picture.Measure(new Size(Width, Height));
        picture.Arrange(new Rect(0, 0, Width, Height));

        using var bitmap = new RenderTargetBitmap(new PixelSize(Width, Height));
        bitmap.Render(picture);

        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);

        using (var stream = File.Create(FilePath))
        {
            bitmap.Save(stream, new PngBitmapEncoderOptions());
        }

        return FilePath;
    }

    /// <summary>A colour of the dark theme by its key.</summary>
    private static Color ThemeColor(Application application, string key)
    {
        return application.TryFindResource(key, ThemeVariant.Dark, out var found) && found is Color color ? color : Colors.Black;
    }
}
