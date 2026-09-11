using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Labs.Gif;
using Avalonia.Media.Imaging;
using CS2WorkshopUploader;

namespace GUI;

public partial class MainWindow : Window
{
    private const int ThumbnailSize = 128;

    private static readonly HttpClient Http = new();

    /// <summary>How many thumbnails are downloaded at once.</summary>
    private static readonly SemaphoreSlim ThumbnailDownloads = new(6);

    private readonly ObservableCollection<WorkshopItemRow> rows = [];

    public MainWindow()
    {
        InitializeComponent();

        PublishedItems.ItemsSource = rows;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        var thumbnails = new List<Task>();

        try
        {
            await foreach (var item in WorkshopUploader.GetPublishedItemsAsync())
            {
                var row = new WorkshopItemRow(item);

                rows.Add(row);
                Status.Text = $"{rows.Count} published items";

                thumbnails.Add(LoadThumbnailAsync(row));
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            Status.Text = exception.Message;
        }

        await Task.WhenAll(thumbnails);
    }

    private static async Task LoadThumbnailAsync(WorkshopItemRow row)
    {
        if (row.Item.PreviewUrl == null)
        {
            return;
        }

        await ThumbnailDownloads.WaitAsync();

        try
        {
            var preview = await Http.GetByteArrayAsync(row.Item.PreviewUrl);

            if (preview.AsSpan().StartsWith("GIF8"u8))
            {
                // the gif control decodes and plays the stream itself, so it keeps the stream
                row.AnimatedThumbnail = GifStreamSource.FromStream(new MemoryStream(preview));
            }
            else
            {
                using var stream = new MemoryStream(preview);

                row.Thumbnail = Bitmap.DecodeToWidth(stream, ThumbnailSize);
            }
        }
        catch (Exception)
        {
            // no thumbnail is shown for items whose preview can not be fetched or decoded, whatever the decoder throws
        }
        finally
        {
            ThumbnailDownloads.Release();
        }
    }
}
