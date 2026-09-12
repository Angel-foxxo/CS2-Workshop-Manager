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
using CS2WorkshopManager;

namespace GUI;

public partial class MainWindow : Window
{
    /// <summary>Thumbnails are decoded at the tile size and drawn smaller in the list.</summary>
    private const int ThumbnailSize = 240;

    /// <summary>A tile in the tiles view including its margins.</summary>
    private const double TileWidth = 264;

    /// <summary>The workshop manager's own wording.</summary>
    private const string DeleteConfirmation = "This will delete the submission from the workshop and users subscribed to it will no longer be able to access it.\n\nThis action cannot be undone!\n\nAre you sure you want to delete the submission?";

    private static readonly HttpClient Http = new();

    /// <summary>How many thumbnails are downloaded at once.</summary>
    private static readonly SemaphoreSlim ThumbnailDownloads = new(6);

    private readonly ObservableCollection<WorkshopItemRow> rows = [];

    public MainWindow()
    {
        InitializeComponent();

        PublishedItems.ItemsSource = rows;
        TileItems.ItemsSource = rows;
        Loaded += OnLoaded;
    }

    /// <summary>
    /// Sizes the tiles to the whole columns that fit the viewport, so their centering splits the leftover width evenly.
    /// </summary>
    private void OnTilesViewportSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        var columns = Math.Max(1, (int)((e.NewSize.Width - TileItems.Margin.Left - TileItems.Margin.Right) / TileWidth));

        TileItems.Width = columns * TileWidth;
    }

    private void OnViewToggle(object? sender, RoutedEventArgs e)
    {
        var tiles = sender == TilesToggle;

        ListToggle.IsChecked = !tiles;
        TilesToggle.IsChecked = tiles;
        PublishedItems.IsVisible = !tiles;
        Tiles.IsVisible = tiles;
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        await LoadItemsAsync();
    }

    private async void OnRefresh(object? sender, RoutedEventArgs e)
    {
        await LoadItemsAsync();
    }

    private async void OnView(object? sender, RoutedEventArgs e)
    {
        if (SelectedRow("view") is not { } row)
        {
            return;
        }

        if (!await Launcher.LaunchUriAsync(row.Item.Url))
        {
            Status.Text = $"Could not open {row.Item.Url}";
        }
    }

    private async void OnDelete(object? sender, RoutedEventArgs e)
    {
        if (SelectedRow("delete") is not { } row)
        {
            return;
        }

        if (!await new ConfirmDialog("Delete Submission", DeleteConfirmation).ShowDialog<bool>(this))
        {
            return;
        }

        DeleteButton.IsEnabled = false;

        try
        {
            await WorkshopManager.DeleteItemAsync(row.PublishedFileId);

            rows.Remove(row);
            Status.Text = $"Deleted {row.Title}, {rows.Count} published items";
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            Status.Text = exception.Message;
        }
        finally
        {
            DeleteButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// The map selected in whichever view is showing, or null after telling the user to select one for the action.
    /// </summary>
    private WorkshopItemRow? SelectedRow(string action)
    {
        var selected = Tiles.IsVisible ? TileItems.SelectedItem : PublishedItems.SelectedItem;

        if (selected is WorkshopItemRow row)
        {
            return row;
        }

        Status.Text = $"Select a map to {action}.";
        return null;
    }

    /// <summary>
    /// Replaces the list with the account's published items as Steam returns them.
    /// </summary>
    private async Task LoadItemsAsync()
    {
        // a refresh while a load is still streaming in would add to a list that was just cleared
        RefreshButton.IsEnabled = false;
        rows.Clear();

        var thumbnails = new List<Task>();

        try
        {
            await foreach (var item in WorkshopManager.GetPublishedItemsAsync())
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
        finally
        {
            RefreshButton.IsEnabled = true;
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
