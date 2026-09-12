using System.ComponentModel;
using CS2WorkshopManager;

namespace GUI;

/// <summary>
/// A row of the published items list.
/// </summary>
public sealed class WorkshopItemRow(WorkshopItem item) : INotifyPropertyChanged
{
    private object? thumbnail;

    public event PropertyChangedEventHandler? PropertyChanged;

    public WorkshopItem Item { get; } = item;

    public ulong PublishedFileId => Item.PublishedFileId;
    public string Title => Item.Title;
    public string Tags => string.Join(",", Item.Tags);

    /// <summary>The tags spaced out for the tiles view.</summary>
    public string TagsLine => string.Join(" | ", Item.Tags);
    public string Description => Item.Description;
    public WorkshopVisibility Visibility => Item.Visibility;
    public DateTimeOffset LastUpdated => Item.TimeUpdated.ToLocalTime();
    public DateTimeOffset DateCreated => Item.TimeCreated.ToLocalTime();
    public double SizeMegabytes => Item.Size / (1024.0 * 1024.0);

    /// <summary>The preview image file as downloaded, decoded again at full size when the item is opened for editing.</summary>
    public byte[]? Preview { get; set; }

    /// <summary>The preview decoded for the lists, see <see cref="PreviewImage.Decode"/>.</summary>
    public object? Thumbnail
    {
        get => thumbnail;
        set
        {
            thumbnail = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Thumbnail)));
        }
    }
}
