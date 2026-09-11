using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
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

        // Steam's image CDN resizes on request, so only a thumbnail sized image is downloaded
        var url = new Uri(row.Item.PreviewUrl, $"?imw={ThumbnailSize}&imh={ThumbnailSize}&ima=fit&impolicy=Letterbox&letterbox=false");

        await ThumbnailDownloads.WaitAsync();

        try
        {
            using var stream = new MemoryStream(await Http.GetByteArrayAsync(url));

            row.Thumbnail = Bitmap.DecodeToWidth(stream, ThumbnailSize);
        }
        catch (HttpRequestException)
        {
            // no thumbnail is shown for items whose preview can not be fetched
        }
        finally
        {
            ThumbnailDownloads.Release();
        }
    }
}
