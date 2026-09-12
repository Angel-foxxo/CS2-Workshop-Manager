using System.IO;
using System.Threading.Tasks;
using StbImageSharp;
using StbImageWriteSharp;
using Steamworks;
using ValveResourceFormat.IO;

namespace CS2WorkshopManager;

/// <summary>
/// Values of <see cref="ERemoteStoragePublishedFileVisibility"/> because this enum sucks by default and I'm not using these long ass names in a CLI.
/// </summary>
public enum WorkshopVisibility
{
    Public = ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPublic,
    FriendsOnly = ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityFriendsOnly,
    Private = ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPrivate,
    Unlisted = ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityUnlisted,
}

public sealed record AddonPublishOptions
{
    /// <summary> Name of the addon folder under game/csgo_addons to upload. </summary>
    public required string AddonName { get; init; }

    /// <summary> Workshop ID of an existing submission, if this is provided everything will be treated as updating this submission. </summary>
    public ulong? PublishedFileId { get; init; }

    /// <summary> Title of the workshop item. </summary>
    public required string Title { get; init; }

    /// <summary> Description of the workshop item. </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary> Visibility of the Workshop item, see <see cref="WorkshopVisibility"/>. </summary>
    public WorkshopVisibility Visibility { get; init; } = WorkshopVisibility.Private;

    /// <summary> The user facing list of submission tags that will show up on the Workshop, see <see cref="WorkshopManager.DefaultTags"/> for default tags. />. </summary>
    public IReadOnlyList<string> Tags { get; init; } = WorkshopManager.DefaultTags;

    /// <summary> Disk path for the user facing thumbnail image that will show up on the Workshop. />. </summary>
    public string? ThumbnailImagePath { get; init; }

    /// <summary> Change note that shows up in the "Change Notes" tab. />. </summary>
    public string? ChangeNote { get; init; }

    /// <summary> Update the item even when the workshop content was published from a different addon folder, otherwise <see cref="SourceFolderConflictException"/> is thrown. </summary>
    public bool AllowSourceFolderChange { get; init; }
}

public sealed record WorkshopPublishResult(ulong PublishedFileId, bool NeedsWorkshopAgreement)
{
    public Uri Url => new($"https://steamcommunity.com/sharedfiles/filedetails/?id={PublishedFileId}");
}

/// <summary>
/// A workshop item as the workshop manager lists it.
/// </summary>
public sealed record WorkshopItem(
    ulong PublishedFileId,
    string Title,
    string Description,
    IReadOnlyList<string> Tags,
    WorkshopVisibility Visibility,
    DateTimeOffset TimeCreated,
    DateTimeOffset TimeUpdated,
    long Size,
    Uri? PreviewUrl)
{
    public Uri Url => new($"https://steamcommunity.com/sharedfiles/filedetails/?id={PublishedFileId}");
}

public sealed class SourceFolderConflictException : InvalidOperationException
{
    public ulong PublishedFileId { get; }
    public string? PreviousAddonName { get; }
    public string? AddonName { get; }

    public SourceFolderConflictException() { }
    public SourceFolderConflictException(string message) : base(message) { }
    public SourceFolderConflictException(string message, Exception innerException) : base(message, innerException) { }

    public SourceFolderConflictException(ulong publishedFileId, string previousAddonName, string addonName)
        : base($"Workshop item {publishedFileId} was last published from addon \"{previousAddonName}\", not \"{addonName}\", change AllowSourceFolderChange to allow this.")
    {
        PublishedFileId = publishedFileId;
        PreviousAddonName = previousAddonName;
        AddonName = addonName;
    }
}

/// <summary>
/// Packs a compiled Counter-Strike 2 addon and publishes it to the Steam Workshop.
/// </summary>
public sealed class WorkshopManager
{
    /// <summary>CS2 appid.</summary>
    public const uint AppId = 730;

    public const int ThumbnailJpegQuality = 75;

    public static readonly string[] DefaultTags = ["CS2", "Map"];

    private static SteamClient? steam;

    private static SteamClient Steam
    {
        get
        {
            InitializeSteam();
            return steam!;
        }
    }

    public string GamePath { get; }
    public string AddonsRoot => Path.Combine(GamePath, "game", "csgo_addons");

    public string GameInfoPath => Path.Combine(GamePath, "game", "csgo", "gameinfo.gi");

    public WorkshopManager(string gamePath)
    {
        GamePath = gamePath;
    }

    public static WorkshopManager FromSteamInstall()
    {
        var game = GameFolderLocator.FindSteamGameByAppId((int)AppId)
            ?? throw new DirectoryNotFoundException("Counter-Strike 2 is not installed in any Steam library.");

        return new WorkshopManager(game.GamePath);
    }

    /// <summary>
    /// Connects to the running Steam client as Counter-Strike 2.
    /// </summary>
    public static void InitializeSteam()
    {
        steam ??= new SteamClient(AppId);
    }

    public static void ShutdownSteam()
    {
        steam?.Dispose();
        steam = null;
    }

    /// <summary>
    /// The addon the item was last published from./>.
    /// </summary>
    /// <returns>
    /// Last addon path the item was published from if trying to publish from a different addon, null otherwise.
    /// </returns>
    public static string? GetConflictingSourceFolder(string addonName, ulong publishedFileId)
    {
        var installDirectory = Steam.UGC.GetItemInstallFolder(publishedFileId);

        if (installDirectory == null)
        {
            return null;
        }

        var previous = AddonPackager.ReadPublishedSourceFolder(installDirectory);

        return previous != null && !previous.Equals(addonName, StringComparison.OrdinalIgnoreCase) ? previous : null;
    }

    /// <summary>
    /// Every workshop item the logged in account has published for Counter-Strike 2 with the CS2 tag, most recently updated first, as Steam's pages of them arrive.
    /// </summary>
    public static async IAsyncEnumerable<WorkshopItem> GetPublishedItemsAsync()
    {
        var client = Steam;
        var ugc = client.UGC;
        var accountId = client.User.GetAccountId();

        var count = 0u;

        for (var page = 1u; ; page++)
        {
            var query = ugc.CreateQueryUserUGCRequest(accountId, EUserUGCList.Published, EUGCMatchingUGCType.Items, EUserUGCListSortOrder.LastUpdatedDesc, AppId, AppId, page);

            if (query == SteamUGC.InvalidQueryHandle)
            {
                throw new InvalidOperationException("Failed to create a workshop query.");
            }

            try
            {
                // only items tagged as CS2 maps, the tag the workshop manager always sets
                ugc.AddRequiredTag(query, DefaultTags[0]);
                ugc.SetReturnLongDescription(query, true);

                var completed = await client.WaitForCallResultAsync<SteamUGCQueryCompleted>(ugc.SendQueryUGCRequest(query)).ConfigureAwait(false);

                if (completed.Result != EResult.OK)
                {
                    throw new InvalidOperationException($"Workshop query failed: {completed.Result}");
                }

                for (var index = 0u; index < completed.NumResultsReturned; index++)
                {
                    if (ugc.GetQueryUGCResult(query, index) is not SteamUGCDetails details)
                    {
                        continue;
                    }

                    count++;

                    yield return new WorkshopItem(
                        details.PublishedFileId,
                        details.Title,
                        details.Description,
                        details.Tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                        (WorkshopVisibility)details.Visibility,
                        DateTimeOffset.FromUnixTimeSeconds(details.TimeCreated),
                        DateTimeOffset.FromUnixTimeSeconds(details.TimeUpdated),
                        (long)details.TotalFilesSize,
                        ugc.GetQueryUGCPreviewURL(query, index));
                }

                if (completed.NumResultsReturned < SteamUGC.ResultsPerPage || count >= completed.TotalMatchingResults)
                {
                    yield break;
                }
            }
            finally
            {
                ugc.ReleaseQueryUGCRequest(query);
            }
        }
    }

    public async Task<WorkshopPublishResult> PublishAsync(AddonPublishOptions options, IProgress<float>? progress = null)
    {
        InitializeSteam();

        if (options.PublishedFileId is ulong existingFileId && !options.AllowSourceFolderChange)
        {
            var previousAddonName = GetConflictingSourceFolder(options.AddonName, existingFileId);

            if (previousAddonName != null)
            {
                throw new SourceFolderConflictException(existingFileId, previousAddonName, options.AddonName);
            }
        }

        var publishedFileId = options.PublishedFileId ?? await CreateItemAsync().ConfigureAwait(false);

        var publishTime = DateTimeOffset.UtcNow;
        var contentPath = AddonPackager.Stage(AddonsRoot, options.AddonName, GameInfoPath, publishedFileId, options.Title, publishTime);

        var ugc = Steam.UGC;
        var handle = ugc.StartItemUpdate(AppId, publishedFileId);

        ugc.SetItemTitle(handle, options.Title);
        ugc.SetItemDescription(handle, options.Description);
        ugc.SetItemVisibility(handle, (ERemoteStoragePublishedFileVisibility)options.Visibility);

        if (options.ThumbnailImagePath != null)
        {
            ugc.SetItemPreview(handle, WriteThumbnail(options.ThumbnailImagePath, publishedFileId, publishTime));
        }

        ugc.SetItemContent(handle, contentPath);
        ugc.SetItemTags(handle, options.Tags);

        var changeNote = options.ChangeNote ?? (options.PublishedFileId == null ? $"Created {options.Title}." : $"Edited {options.Title}.");

        var result = await Steam.WaitForCallResultAsync<SubmitItemUpdateResult>(ugc.SubmitItemUpdate(handle, changeNote), () =>
        {
            var update = ugc.GetItemUpdateProgress(handle);

            if (update.BytesTotal > 0)
            {
                progress?.Report((float)update.BytesProcessed / update.BytesTotal);
            }
        }).ConfigureAwait(false);

        if (result.Result != EResult.OK)
        {
            throw new InvalidOperationException($"SubmitItemUpdate failed: {result.Result}");
        }

        return new WorkshopPublishResult(publishedFileId, result.UserNeedsToAcceptWorkshopLegalAgreement);
    }

    /// <summary>
    /// Deletes a published item from the workshop, which can not be undone.
    /// </summary>
    public static async Task DeleteItemAsync(ulong publishedFileId)
    {
        var result = await Steam.WaitForCallResultAsync<DeleteItemResult>(Steam.UGC.DeleteItem(publishedFileId)).ConfigureAwait(false);

        if (result.Result != EResult.OK)
        {
            throw new InvalidOperationException($"DeleteItem failed: {result.Result}");
        }
    }

    private static async Task<ulong> CreateItemAsync()
    {
        var result = await Steam.WaitForCallResultAsync<CreateItemResult>(Steam.UGC.CreateItem(AppId, EWorkshopFileType.Community)).ConfigureAwait(false);

        if (result.Result != EResult.OK)
        {
            throw new InvalidOperationException($"CreateItem failed: {result.Result}");
        }

        return result.PublishedFileId;
    }

    /// <summary>
    /// Throws when the file is not an image stb can decode.
    /// </summary>
    public static void ValidateThumbnailImage(string path)
    {
        DecodeThumbnailImage(path);
    }

    private static ImageResult DecodeThumbnailImage(string path)
    {
        using var stream = File.OpenRead(path);

        try
        {
            return ImageResult.FromStream(stream, StbImageSharp.ColorComponents.RedGreenBlue);
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidDataException($"Thumbnail image '{path}' could not be decoded: {exception.Message.Trim()}", exception);
        }
    }

    /// <summary>
    /// Steam only accepts small thumbnail images (1 mb or less) so the thumbnail is transformed into a JPEG, except for gifs which are uploaded unchanged.
    /// </summary>
    private static string WriteThumbnail(string sourcePath, ulong publishedFileId, DateTimeOffset time)
    {
        var image = DecodeThumbnailImage(sourcePath);

        var directory = Path.Combine(Path.GetTempPath(), $"workshopupload_{publishedFileId}");
        Directory.CreateDirectory(directory);

        Span<byte> header = stackalloc byte[4];
        bool isGif;

        using (var source = File.OpenRead(sourcePath))
        {
            isGif = source.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) == header.Length && header.SequenceEqual("GIF8"u8);
        }

        if (isGif)
        {
            var gifPath = Path.Combine(directory, $"thumbnail_{time.ToUnixTimeSeconds():x}.gif");
            File.Copy(sourcePath, gifPath, overwrite: true);
            return gifPath;
        }

        var path = Path.Combine(directory, $"thumbnail_{time.ToUnixTimeSeconds():x}.jpg");

        using var stream = File.Create(path);
        new ImageWriter().WriteJpg(image.Data, image.Width, image.Height, StbImageWriteSharp.ColorComponents.RedGreenBlue, stream, ThumbnailJpegQuality);

        return path;
    }
}
