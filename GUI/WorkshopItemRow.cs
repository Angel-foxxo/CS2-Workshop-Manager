using System.ComponentModel;
using Avalonia.Labs.Gif;
using Avalonia.Media.Imaging;
using CS2WorkshopManager;

namespace GUI;

/// <summary>
/// A row of the published items list.
/// </summary>
public sealed class WorkshopItemRow(WorkshopItem item) : INotifyPropertyChanged
{
    private Bitmap? thumbnail;
    private IGifSource? animatedThumbnail;

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

    public Bitmap? Thumbnail
    {
        get => thumbnail;
        set
        {
            thumbnail = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Thumbnail)));
        }
    }

    /// <summary>The thumbnail when the preview is a gif, shown animated instead of <see cref="Thumbnail"/>.</summary>
    public IGifSource? AnimatedThumbnail
    {
        get => animatedThumbnail;
        set
        {
            animatedThumbnail = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AnimatedThumbnail)));
        }
    }
}
