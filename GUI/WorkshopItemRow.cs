using System.ComponentModel;
using Avalonia.Media.Imaging;
using CS2WorkshopUploader;

namespace GUI;

/// <summary>
/// A row of the published items list.
/// </summary>
public sealed class WorkshopItemRow(WorkshopItem item) : INotifyPropertyChanged
{
    private Bitmap? thumbnail;

    public event PropertyChangedEventHandler? PropertyChanged;

    public WorkshopItem Item { get; } = item;

    public ulong PublishedFileId => Item.PublishedFileId;
    public string Title => Item.Title;
    public string Tags => string.Join(",", Item.Tags);
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
}
