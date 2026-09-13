using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CS2WorkshopManager;

namespace GUI;

public enum SubmissionMode
{
    /// <summary>Upload an addon as a new workshop item.</summary>
    New,

    /// <summary>Upload an addon over an existing workshop item.</summary>
    ReUpload,

    /// <summary>Change an existing workshop item's info without uploading.</summary>
    Edit,
}

/// <summary>
/// The workshop manager's publish form for a new submission, a re-upload or an info edit, shown in place of the item list.
/// </summary>
public partial class SubmissionView : UserControl
{
    /// <summary>The workshop manager refuses descriptions and update notes of this many characters or more.</summary>
    private const int MaxTextLength = 8000;

    /// <summary>The visibilities as the dropdown lists them, with the workshop manager's wording.</summary>
    private static readonly VisibilityChoice[] VisibilityChoices =
    [
        new(WorkshopVisibility.Public, "Public"),
        new(WorkshopVisibility.FriendsOnly, "Friends Only"),
        new(WorkshopVisibility.Private, "Private"),
        new(WorkshopVisibility.Unlisted, "Unlisted"),
    ];

    private readonly List<CheckBox> gameModeBoxes = [];

    private SubmissionMode mode;
    private WorkshopManager? manager;
    private WorkshopItem? item;

    /// <summary>What the chosen addon folder holds, null while it is being scanned or when there is none.</summary>
    private AddonContents? contents;

    /// <summary>The image picked for the preview, null keeps whatever the item has.</summary>
    private string? thumbnailPath;

    private TaskCompletionSource<PublishedSubmission?>? finished;

    public SubmissionView()
    {
        InitializeComponent();

        VisibilityBox.ItemsSource = VisibilityChoices;

        foreach (var tag in WorkshopManager.GameModeTags)
        {
            var box = new CheckBox { Content = tag == "Armsrace" ? "Arms Race" : tag, Tag = tag };
            box.IsCheckedChanged += OnGameModeChanged;

            gameModeBoxes.Add(box);
            GameModesList.Children.Add(box);
        }

        PreviewDrop.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        PreviewDrop.AddHandler(DragDrop.DropEvent, OnDrop);
    }

    /// <summary>
    /// Fills the form for a new submission or for <paramref name="row"/>'s item, with <paramref name="addon"/> chosen, and waits until it is submitted or cancelled.
    /// </summary>
    /// <returns>What was published, or null when the form was cancelled.</returns>
    public Task<PublishedSubmission?> ShowAsync(SubmissionMode mode, WorkshopManager manager, WorkshopItemRow? row, string? addon)
    {
        this.mode = mode;
        this.manager = manager;
        item = row?.Item;
        thumbnailPath = null;
        PreviewPath.IsVisible = false;

        Heading.Text = mode switch
        {
            SubmissionMode.ReUpload => $"Re-Upload of \"{item!.Title}\" ({item.PublishedFileId})",
            SubmissionMode.Edit => $"Edit Info of \"{item!.Title}\" ({item.PublishedFileId})",
            _ => "New Submission",
        };

        var uploads = mode != SubmissionMode.Edit;

        ChangeNoteLabel.IsVisible = ChangeNoteBox.IsVisible = mode == SubmissionMode.ReUpload;
        AddonLabel.IsVisible = AddonPanel.IsVisible = uploads;

        ChangeNoteBox.Text = string.Empty;
        TitleBox.Text = item?.Title ?? string.Empty;
        DescriptionBox.Text = item?.Description ?? string.Empty;
        ShowDescription(preview: false);
        VisibilityBox.SelectedItem = Array.Find(VisibilityChoices, choice => choice.Value == (item?.Visibility ?? WorkshopVisibility.Private));
        Status.Text = string.Empty;

        foreach (var box in gameModeBoxes)
        {
            box.IsChecked = item != null && item.Tags.Contains((string)box.Tag!, StringComparer.OrdinalIgnoreCase);
        }

        SetPreview(row?.Preview == null ? null : PreviewImage.Decode(row.Preview));

        if (uploads)
        {
            try
            {
                var addons = manager.GetAddonNames().Order(StringComparer.OrdinalIgnoreCase).ToList();

                AddonBox.ItemsSource = addons;
                AddonBox.SelectedItem = addon == null ? null : addons.Find(name => name.Equals(addon, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _ = MessageDialog.ShowAsync(OwnerWindow, MessageKind.Warning, "Addon Folders", exception.Message);
            }
        }

        TitleBox.Focus();

        finished = new TaskCompletionSource<PublishedSubmission?>();
        return finished.Task;
    }

    private void OnDescriptionViewToggle(object? sender, RoutedEventArgs e)
    {
        ShowDescription(sender == PreviewToggle);
    }

    /// <summary>Shows the description as typed, or laid out the way the workshop page would show it.</summary>
    private void ShowDescription(bool preview)
    {
        EditToggle.IsChecked = !preview;
        PreviewToggle.IsChecked = preview;
        FormatButtons.IsEnabled = !preview;
        DescriptionBox.IsVisible = !preview;
        DescriptionPreview.IsVisible = preview;

        if (preview)
        {
            DescriptionRendered.Text = DescriptionBox.Text;
        }
    }

    /// <summary>
    /// Wraps the selected description text in the button's Steam formatting tag, or opens a tag at the caret to type into.
    /// </summary>
    private void OnFormat(object? sender, RoutedEventArgs e)
    {
        var tag = (string)((Button)sender!).Tag!;
        var selected = DescriptionBox.SelectedText;
        var from = Math.Min(DescriptionBox.SelectionStart, DescriptionBox.SelectionEnd);

        var (open, close) = tag switch
        {
            "url" => ("[url=https://]", "[/url]"),
            "list" => ("[list]\n[*]", "\n[/list]"),
            _ => ($"[{tag}]", $"[/{tag}]"),
        };

        DescriptionBox.SelectedText = open + selected + close;

        if (tag == "url")
        {
            // the address placeholder is selected so it is typed over
            DescriptionBox.SelectionStart = from + "[url=".Length;
            DescriptionBox.SelectionEnd = from + "[url=https://".Length;
        }
        else
        {
            // the caret lands inside empty tags, and after tags wrapped around a selection
            var caret = selected.Length == 0 ? from + open.Length : from + open.Length + selected.Length + close.Length;

            DescriptionBox.SelectionStart = DescriptionBox.SelectionEnd = caret;
        }

        DescriptionBox.Focus();
    }

    private void OnGameModeChanged(object? sender, RoutedEventArgs e)
    {
        var picked = gameModeBoxes.Where(box => box.IsChecked == true).Select(box => box.Content).ToList();

        GameModesButton.Content = picked.Count == 0 ? "None" : string.Join(", ", picked);
    }

    private async void OnAddonChanged(object? sender, SelectionChangedEventArgs e)
    {
        await ScanAddonAsync();
    }

    /// <summary>Opens the addon's files and rules, and scans again afterwards since they may have changed.</summary>
    private async void OnFiles(object? sender, RoutedEventArgs e)
    {
        if (manager == null)
        {
            return;
        }

        await new AddonFilesWindow(manager, AddonBox.SelectedItem as string).ShowDialog(OwnerWindow);
        await ScanAddonAsync();
    }

    /// <summary>Scans what the chosen addon would upload, with the user's rules, and shows it in the graph.</summary>
    private async Task ScanAddonAsync()
    {
        contents = null;
        Contents.Contents = null;

        if (AddonBox.SelectedItem is not string addon || manager == null)
        {
            return;
        }

        Status.Text = $"Scanning {addon}...";

        var addonPath = Path.Combine(manager.AddonsRoot, addon);
        var gameInfoPath = manager.GameInfoPath;

        try
        {
            var rules = manager.LoadRules(addon);
            var scanned = await Task.Run(() => AddonPackager.GetContents(addonPath, gameInfoPath, rules));

            // the selection moved on while this folder was scanned
            if (!Equals(AddonBox.SelectedItem, addon))
            {
                return;
            }

            contents = scanned;
            Contents.Contents = scanned;
            Status.Text = string.Empty;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Status.Text = string.Empty;
            await MessageDialog.ShowAsync(OwnerWindow, MessageKind.Warning, "Addon Folder", exception.Message);
        }
    }

    /// <summary>The window the form sits in, which owns its dialogs.</summary>
    private Window OwnerWindow => (Window)TopLevel.GetTopLevel(this)!;

    private void SetPreview(object? source)
    {
        Preview.Source = source;
        PreviewHint.IsVisible = source == null;
    }

    private void OnClearPreview(object? sender, RoutedEventArgs e)
    {
        thumbnailPath = null;
        SetPreview(null);
        PreviewPath.IsVisible = false;
    }

    private async void OnBrowse(object? sender, RoutedEventArgs e)
    {
        var files = await TopLevel.GetTopLevel(this)!.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Preview Image",
            AllowMultiple = false,
            FileTypeFilter = [FilePickerFileTypes.ImageAll],
        });

        if (files.Count > 0 && files[0].TryGetLocalPath() is string path)
        {
            await PickThumbnailAsync(path);
        }
    }

    private static void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.TryGetFile()?.TryGetLocalPath() is string path)
        {
            await PickThumbnailAsync(path);
        }
    }

    private async Task PickThumbnailAsync(string path)
    {
        try
        {
            // the check the upload makes, which decodes the whole image
            await Task.Run(() => WorkshopManager.ValidateThumbnailImage(path));

            var file = await File.ReadAllBytesAsync(path);

            thumbnailPath = path;
            PreviewPath.Text = path;
            PreviewPath.IsVisible = true;

            try
            {
                SetPreview(PreviewImage.Decode(file));
            }
            catch (Exception)
            {
                // the upload can still convert formats the window can not show, whatever the decoder throws
                SetPreview(null);
                await MessageDialog.ShowAsync(OwnerWindow, MessageKind.Info, "Preview Image", "The image will be uploaded, but can not be shown here.");
            }
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            await MessageDialog.ShowAsync(OwnerWindow, MessageKind.Warning, "Preview Image", exception.Message);
        }
    }

    /// <summary>
    /// The tags the workshop manager would submit: an upload starts over from its own tags, an info edit keeps the item's and applies the game mode boxes to them.
    /// </summary>
    private List<string> BuildTags()
    {
        var tags = new List<string>();

        if (mode == SubmissionMode.Edit)
        {
            tags.AddRange(item!.Tags);

            if (!tags.Contains("Map", StringComparer.OrdinalIgnoreCase))
            {
                tags.Add("Map");
            }
        }
        else
        {
            tags.AddRange(WorkshopManager.DefaultTags);
        }

        foreach (var box in gameModeBoxes)
        {
            var tag = (string)box.Tag!;

            if (box.IsChecked == true)
            {
                if (!tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
                {
                    tags.Add(tag);
                }
            }
            else
            {
                tags.RemoveAll(existing => existing.Equals(tag, StringComparison.OrdinalIgnoreCase));
            }
        }

        return tags;
    }

    /// <summary>
    /// The workshop manager's checks before it submits, in its order.
    /// </summary>
    /// <returns>What is wrong, or null when the form can be submitted.</returns>
    private string? Validate(string title, string description, string changeNote, AddonPublishOptions options)
    {
        if (contents?.ExceedsUploadLimit == true)
        {
            return $"Exceeds {AddonContents.FormatSize(AddonPackager.MaxTotalSize)} upload limit! Please optimize your content under this limit.";
        }

        if (mode == SubmissionMode.Edit && options.Title == null && options.Description == null && options.Visibility == null && options.Tags == null && options.ThumbnailImagePath == null)
        {
            return "Nothing was changed.";
        }

        if (title.Length == 0)
        {
            return "Enter a title.";
        }

        if (description.Length == 0)
        {
            return "Enter a description.";
        }

        if (description.Length >= MaxTextLength)
        {
            return $"The description must be shorter than {MaxTextLength} characters.";
        }

        if (mode == SubmissionMode.ReUpload)
        {
            if (changeNote.Length == 0)
            {
                return "Enter update notes for this change.";
            }

            if (changeNote.Length >= MaxTextLength)
            {
                return $"The update notes must be shorter than {MaxTextLength} characters.";
            }
        }

        if (AddonPanel.IsVisible && AddonBox.SelectedItem == null)
        {
            return "Select an addon folder.";
        }

        return Preview.Source == null && thumbnailPath == null ? "Pick a preview image." : null;
    }

    private async void OnSubmit(object? sender, RoutedEventArgs e)
    {
        var title = TitleBox.Text ?? string.Empty;
        var description = DescriptionBox.Text ?? string.Empty;
        var changeNote = ChangeNoteBox.Text ?? string.Empty;
        var visibility = ((VisibilityChoice)VisibilityBox.SelectedItem!).Value;
        var tags = BuildTags();
        var edits = mode == SubmissionMode.Edit;

        // an info edit only sends what changed, like the workshop manager
        var options = new AddonPublishOptions
        {
            AddonName = AddonPanel.IsVisible ? AddonBox.SelectedItem as string : null,
            PublishedFileId = item?.PublishedFileId,
            Title = edits && title == item!.Title ? null : title,
            Description = edits && description == item!.Description ? null : description,
            Visibility = edits && visibility == item!.Visibility ? null : visibility,
            Tags = edits && tags.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(item!.Tags) ? null : tags,
            ThumbnailImagePath = thumbnailPath,
            ChangeNote = mode == SubmissionMode.ReUpload ? changeNote : null,
        };

        if (Validate(title, description, changeNote, options) is string problem)
        {
            await MessageDialog.ShowAsync(OwnerWindow, MessageKind.Warning, "Submission", problem);
            return;
        }

        await PublishAsync(options);
    }

    private async Task PublishAsync(AddonPublishOptions options)
    {
        SetBusy(true);
        UploadProgress.Value = 0;
        Status.Text = options.AddonName == null ? "Updating..." : "Packaging submission...";

        var progress = new Progress<float>(fraction =>
        {
            UploadProgress.Value = fraction;
            Status.Text = $"Submitting {fraction:P0}";
        });

        try
        {
            // packing happens on the calling thread, which would freeze the window
            var result = await Task.Run(() => manager!.PublishAsync(options, progress));

            // an info edit that left the title alone still knows it from the item
            Finish(new PublishedSubmission(result, options.Title ?? item!.Title));
        }
        catch (SourceFolderConflictException exception)
        {
            SetBusy(false);

            var question = $"This item was last published from addon \"{exception.PreviousAddonName}\".\n\nUpload it from \"{exception.AddonName}\" anyway?";

            if (await MessageDialog.AskAsync(OwnerWindow, MessageKind.Warning, "Different Addon Folder", question, "Upload"))
            {
                await PublishAsync(options with { AllowSourceFolderChange = true });
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or InvalidDataException or ArgumentException)
        {
            Status.Text = string.Empty;
            await MessageDialog.ShowAsync(OwnerWindow, MessageKind.Warning, "Submission Failed", exception.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        Form.IsEnabled = !busy;
        BackButton.IsEnabled = CancelButton.IsEnabled = SubmitButton.IsEnabled = !busy;
        UploadProgress.IsVisible = busy;
    }

    private async void OnCancel(object? sender, RoutedEventArgs e)
    {
        if (await MessageDialog.AskAsync(OwnerWindow, MessageKind.Warning, "Leave Submission", "Leave without submitting?\n\nWhat you entered here will be lost.", "Leave"))
        {
            Finish(null);
        }
    }

    private void Finish(PublishedSubmission? published)
    {
        var task = finished;

        finished = null;
        task?.TrySetResult(published);
    }

    /// <summary>What the form published and the title it gave it.</summary>
    public sealed record PublishedSubmission(WorkshopPublishResult Result, string Title);

    /// <summary>A visibility and the label the dropdown shows for it.</summary>
    private sealed record VisibilityChoice(WorkshopVisibility Value, string Label)
    {
        public override string ToString()
        {
            return Label;
        }
    }
}
