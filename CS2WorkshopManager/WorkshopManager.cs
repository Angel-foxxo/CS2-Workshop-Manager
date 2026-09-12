using System.Diagnostics;
using System.IO;
using System.Linq;
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
    /// <summary> Name of the addon folder under game/csgo_addons to upload, or null to only change the info of the submission in <see cref="PublishedFileId"/>. </summary>
    public string? AddonName { get; init; }

    /// <summary> Workshop ID of an existing submission, if this is provided everything will be treated as updating this submission. </summary>
    public ulong? PublishedFileId { get; init; }

    /// <summary> Title of the workshop item, needed for an upload. When only editing info, null leaves the published title alone. </summary>
    public string? Title { get; init; }

    /// <summary> Description of the workshop item, empty for an upload when null. When only editing info, null leaves the published description alone. </summary>
    public string? Description { get; init; }

    /// <summary> Visibility of the Workshop item, see <see cref="WorkshopVisibility"/>, private for an upload when null. When only editing info, null leaves the published visibility alone. </summary>
    public WorkshopVisibility? Visibility { get; init; }

    /// <summary> The user facing list of submission tags that will show up on the Workshop, <see cref="WorkshopManager.DefaultTags"/> for an upload when null. When only editing info, null leaves the published tags alone. </summary>
    public IReadOnlyList<string>? Tags { get; init; }

    /// <summary> Disk path for the user facing thumbnail image that will show up on the Workshop. Null leaves the published preview alone. </summary>
    public string? ThumbnailImagePath { get; init; }

    /// <summary> Change note that shows up in the "Change Notes" tab. Null gives the workshop manager's "Created {Title}." or "Edited {Title}." for an upload and no note when only editing info. </summary>
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

    /// <summary>The game mode tags the workshop manager offers, added after <see cref="DefaultTags"/>.</summary>
    public static readonly string[] GameModeTags = ["Classic", "Deathmatch", "Armsrace", "Wingman", "Custom"];

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

    /// <summary>
    /// The addon folders under game/csgo_addons, without the folders the workshop manager keeps there itself.
    /// </summary>
    public IEnumerable<string> GetAddonNames()
    {
        return Directory.EnumerateDirectories(AddonsRoot)
            .Select(path => Path.GetFileName(path))
            .Where(name => !name.Equals("vpks", StringComparison.OrdinalIgnoreCase) && !name.Equals("workshop_items", StringComparison.OrdinalIgnoreCase));
    }

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
    /// The addon the running Counter-Strike 2 workshop tools were started with, read from the game's command line, or null when the tools are not running.
    /// </summary>
    public static string? GetRunningToolsAddon()
    {
        foreach (var process in Process.GetProcessesByName("cs2"))
        {
            using (process)
            {
                var arguments = ProcessCommandLine.Read(process)?.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                if (arguments == null || !arguments.Contains("-tools", StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                var addon = Array.FindIndex(arguments, argument => argument.Equals("-addon", StringComparison.OrdinalIgnoreCase));

                if (addon >= 0 && addon + 1 < arguments.Length)
                {
                    return arguments[addon + 1].Trim('"');
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The addon folder the item's installed workshop content was published from, or null when it is not installed or was not published by a workshop manager.
    /// </summary>
    public static string? GetPublishedSourceFolder(ulong publishedFileId)
    {
        var installDirectory = Steam.UGC.GetItemInstallFolder(publishedFileId);

        return installDirectory == null ? null : AddonPackager.ReadPublishedSourceFolder(installDirectory);
    }

    /// <summary>
    /// The addon the item was last published from.
    /// </summary>
    /// <returns>
    /// Last addon path the item was published from if trying to publish from a different addon, null otherwise.
    /// </returns>
    public static string? GetConflictingSourceFolder(string addonName, ulong publishedFileId)
    {
        var previous = GetPublishedSourceFolder(publishedFileId);

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

        if (options.AddonName == null && options.PublishedFileId == null)
        {
            throw new ArgumentException("A new submission needs an addon folder to upload.", nameof(options));
        }

        // an upload sets everything like the workshop manager does, an info edit only what was given
        var editsInfoOnly = options.AddonName == null;

        if (!editsInfoOnly && string.IsNullOrWhiteSpace(options.Title))
        {
            throw new ArgumentException("An upload needs a title.", nameof(options));
        }

        if (options.AddonName != null && options.PublishedFileId is ulong existingFileId && !options.AllowSourceFolderChange)
        {
            var previousAddonName = GetConflictingSourceFolder(options.AddonName, existingFileId);

            if (previousAddonName != null)
            {
                throw new SourceFolderConflictException(existingFileId, previousAddonName, options.AddonName);
            }
        }

        var publishedFileId = options.PublishedFileId ?? await CreateItemAsync().ConfigureAwait(false);

        var publishTime = DateTimeOffset.UtcNow;

        // only the info changes when there is no addon to upload
        var contentPath = options.AddonName == null ? null : AddonPackager.Stage(AddonsRoot, options.AddonName, GameInfoPath, publishedFileId, options.Title!, publishTime);

        var ugc = Steam.UGC;
        var handle = ugc.StartItemUpdate(AppId, publishedFileId);

        if (options.Title != null)
        {
            ugc.SetItemTitle(handle, options.Title);
        }

        if ((options.Description ?? (editsInfoOnly ? null : string.Empty)) is string description)
        {
            ugc.SetItemDescription(handle, description);
        }

        if ((options.Visibility ?? (editsInfoOnly ? null : WorkshopVisibility.Private)) is WorkshopVisibility visibility)
        {
            ugc.SetItemVisibility(handle, (ERemoteStoragePublishedFileVisibility)visibility);
        }

        if (options.ThumbnailImagePath != null)
        {
            ugc.SetItemPreview(handle, WriteThumbnail(options.ThumbnailImagePath, publishedFileId, publishTime));
        }

        if (contentPath != null)
        {
            ugc.SetItemContent(handle, contentPath);
        }

        if ((options.Tags ?? (editsInfoOnly ? null : DefaultTags)) is { } tags)
        {
            ugc.SetItemTags(handle, tags);
        }

        var changeNote = options.ChangeNote ?? (editsInfoOnly ? string.Empty : options.PublishedFileId == null ? $"Created {options.Title}." : $"Edited {options.Title}.");

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
