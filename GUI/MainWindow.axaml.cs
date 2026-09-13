using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
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

    /// <summary>The game install, found when the first publish form needs it.</summary>
    private WorkshopManager? manager;

    public MainWindow()
    {
        InitializeComponent();

        PublishedItems.ItemsSource = rows;
        TileItems.ItemsSource = rows;
        Loaded += OnLoaded;

        // before the right click menu opens, the item under the pointer is the selected one
        PublishedItems.AddHandler(PointerPressedEvent, OnItemPressed, RoutingStrategies.Tunnel);
        TileItems.AddHandler(PointerPressedEvent, OnItemPressed, RoutingStrategies.Tunnel);
    }

    /// <summary>
    /// A right click selects the item under the pointer, as a left click would, so the menu's actions apply to it.
    /// </summary>
    private void OnItemPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsRightButtonPressed || e.Source is not Visual source)
        {
            return;
        }

        if (sender == PublishedItems)
        {
            if (source.FindAncestorOfType<DataGridRow>() is { } row)
            {
                PublishedItems.SelectedItem = row.DataContext;
            }
        }
        else if (source.FindAncestorOfType<ListBoxItem>() is { } tile)
        {
            TileItems.SelectedItem = tile.DataContext;
        }
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

    private async void OnNew(object? sender, RoutedEventArgs e)
    {
        await OpenSubmissionAsync(SubmissionMode.New, null);
    }

    private async void OnReUpload(object? sender, RoutedEventArgs e)
    {
        if (SelectedRow("re-upload") is { } row)
        {
            await OpenSubmissionAsync(SubmissionMode.ReUpload, row);
        }
    }

    private async void OnEdit(object? sender, RoutedEventArgs e)
    {
        if (SelectedRow("edit") is { } row)
        {
            await OpenSubmissionAsync(SubmissionMode.Edit, row);
        }
    }

    /// <summary>
    /// Swaps the item list for the publish form until that is done, then reloads the list when something was published.
    /// </summary>
    private async Task OpenSubmissionAsync(SubmissionMode mode, WorkshopItemRow? row)
    {
        if (await FindGameAsync() is not { } game)
        {
            return;
        }

        Main.IsVisible = false;
        Submission.IsVisible = true;

        SubmissionView.PublishedSubmission? published;

        try
        {
            published = await Submission.ShowAsync(mode, game, row, mode == SubmissionMode.Edit ? null : DefaultAddon(row));
        }
        finally
        {
            Submission.IsVisible = false;
            Main.IsVisible = true;
        }

        if (published != null)
        {
            // the workshop manager opens the published item's page
            await Launcher.LaunchUriAsync(published.Result.Url);
            await LoadItemsAsync();

            if (published.Result.NeedsWorkshopAgreement)
            {
                await MessageDialog.ShowAsync(this, MessageKind.Warning, "Published", $"Published \"{published.Title}\" ({published.Result.PublishedFileId}), but the Steam Workshop legal agreement must be accepted before it becomes visible.");
            }
            else
            {
                await MessageDialog.ShowAsync(this, MessageKind.Info, "Published", $"Published \"{published.Title}\" ({published.Result.PublishedFileId})");
            }
        }
    }

    /// <summary>The game install, found once, or null after telling the user it is not there.</summary>
    private async Task<WorkshopManager?> FindGameAsync()
    {
        try
        {
            return manager ??= WorkshopManager.FromSteamInstall();
        }
        catch (DirectoryNotFoundException exception)
        {
            await MessageDialog.ShowAsync(this, MessageKind.Warning, "Game Not Found", exception.Message);
            return null;
        }
    }

    /// <summary>The files and rules of any addon, starting from the selected item's.</summary>
    private async void OnFiles(object? sender, RoutedEventArgs e)
    {
        if (await FindGameAsync() is { } game)
        {
            var selected = (Tiles.IsVisible ? TileItems.SelectedItem : PublishedItems.SelectedItem) as WorkshopItemRow;

            await new AddonFilesWindow(game, DefaultAddon(selected)).ShowDialog(this);
        }
    }

    /// <summary>
    /// The addon to start from, like the workshop manager the one the tools have open. Without the tools it is the one <paramref name="row"/>'s item was last published from, or the last updated item's when there is no row.
    /// </summary>
    private string? DefaultAddon(WorkshopItemRow? row)
    {
        var item = row ?? rows.MaxBy(candidate => candidate.Item.TimeUpdated);

        return WorkshopManager.GetRunningToolsAddon() ?? (item == null ? null : WorkshopManager.GetPublishedSourceFolder(item.Item.PublishedFileId));
    }

    private async void OnView(object? sender, RoutedEventArgs e)
    {
        if (SelectedRow("view") is not { } row)
        {
            return;
        }

        if (!await Launcher.LaunchUriAsync(row.Item.Url))
        {
            await MessageDialog.ShowAsync(this, MessageKind.Warning, "View", $"Could not open {row.Item.Url}");
        }
    }

    private async void OnDelete(object? sender, RoutedEventArgs e)
    {
        if (SelectedRow("delete") is not { } row)
        {
            return;
        }

        if (!await MessageDialog.AskAsync(this, MessageKind.Danger, "Delete Submission", DeleteConfirmation))
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
            await MessageDialog.ShowAsync(this, MessageKind.Warning, "Delete Submission", exception.Message);
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

        _ = MessageDialog.ShowAsync(this, MessageKind.Warning, "No Map Selected", $"Select a map to {action}.");
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
            await MessageDialog.ShowAsync(this, MessageKind.Warning, "Workshop", exception.Message);
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

            row.Preview = preview;
            row.Thumbnail = PreviewImage.Decode(preview, ThumbnailSize);
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
