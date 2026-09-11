using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SkiaSharp;
using Steamworks;
using ValveResourceFormat.IO;

namespace CS2WorkshopUploader;

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

    /// <summary> The user facing list of submission tags that will show up on the Workshop, see <see cref="WorkshopUploader.DefaultTags"/> for default tags. />. </summary>
    public IReadOnlyList<string> Tags { get; init; } = WorkshopUploader.DefaultTags;

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
public sealed class WorkshopUploader
{
    /// <summary>CS2 appid.</summary>
    public static readonly AppId_t AppId = new(730);

    public const int ThumbnailJpegQuality = 75;

    public static readonly SKEncodedImageFormat[] ThumbnailFormats = [SKEncodedImageFormat.Png, SKEncodedImageFormat.Jpeg, SKEncodedImageFormat.Gif, SKEncodedImageFormat.Webp];

    public static readonly string[] DefaultTags = ["CS2", "Map"];

    private static readonly TimeSpan CallbackPollInterval = TimeSpan.FromMilliseconds(50);

    private static bool SteamInitialized;

    public string GamePath { get; }
    public string AddonsRoot => Path.Combine(GamePath, "game", "csgo_addons");

    public string GameInfoPath => Path.Combine(GamePath, "game", "csgo", "gameinfo.gi");

    public WorkshopUploader(string gamePath)
    {
        GamePath = gamePath;
    }

    public static WorkshopUploader FromSteamInstall()
    {
        var game = GameFolderLocator.FindSteamGameByAppId((int)AppId.m_AppId)
            ?? throw new DirectoryNotFoundException("Counter-Strike 2 is not installed in any Steam library.");

        return new WorkshopUploader(game.GamePath);
    }

    /// <summary>
    /// Connects to the running Steam client as Counter-Strike 2.
    /// </summary>
    public static void InitializeSteam()
    {
        if (SteamInitialized)
        {
            return;
        }

        Environment.SetEnvironmentVariable("SteamAppId", AppId.ToString());
        Environment.SetEnvironmentVariable("SteamGameId", AppId.ToString());

        var result = SteamAPI.InitEx(out var message);

        if (result != ESteamAPIInitResult.k_ESteamAPIInitResult_OK)
        {
            throw new InvalidOperationException($"Failed to initialize Steam ({result}): {message}");
        }

        SteamInitialized = true;
    }

    public static void ShutdownSteam()
    {
        if (!SteamInitialized)
        {
            return;
        }

        SteamAPI.Shutdown();
        SteamInitialized = false;
    }

    /// <summary>
    /// The addon the item was last published from./>.
    /// </summary>
    /// <returns>
    /// Last addon path the item was published from if trying to publish from a different addon, null otherwise.
    /// </returns>
    public static string? GetConflictingSourceFolder(string addonName, ulong publishedFileId)
    {
        InitializeSteam();

        if (!SteamUGC.GetItemInstallInfo(new PublishedFileId_t(publishedFileId), out _, out var installDirectory, 260, out _))
        {
            return null;
        }

        var previous = AddonPackager.ReadPublishedSourceFolder(installDirectory);

        return previous != null && !previous.Equals(addonName, StringComparison.OrdinalIgnoreCase) ? previous : null;
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

        var handle = SteamUGC.StartItemUpdate(AppId, new PublishedFileId_t(publishedFileId));

        SteamUGC.SetItemTitle(handle, options.Title);
        SteamUGC.SetItemDescription(handle, options.Description);
        SteamUGC.SetItemVisibility(handle, (ERemoteStoragePublishedFileVisibility)options.Visibility);

        if (options.ThumbnailImagePath != null)
        {
            SteamUGC.SetItemPreview(handle, WriteThumbnail(options.ThumbnailImagePath, publishedFileId, publishTime));
        }

        SteamUGC.SetItemContent(handle, contentPath);
        SteamUGC.SetItemTags(handle, [.. options.Tags]);

        var changeNote = options.ChangeNote ?? (options.PublishedFileId == null ? $"Created {options.Title}." : $"Edited {options.Title}.");

        var result = await WaitForCallResultAsync<SubmitItemUpdateResult_t>(SteamUGC.SubmitItemUpdate(handle, changeNote), () =>
        {
            SteamUGC.GetItemUpdateProgress(handle, out var processed, out var total);

            if (total > 0)
            {
                progress?.Report((float)processed / total);
            }
        }).ConfigureAwait(false);

        if (result.m_eResult != EResult.k_EResultOK)
        {
            throw new InvalidOperationException($"SubmitItemUpdate failed: {result.m_eResult}");
        }

        return new WorkshopPublishResult(publishedFileId, result.m_bUserNeedsToAcceptWorkshopLegalAgreement);
    }

    private static async Task<ulong> CreateItemAsync()
    {
        var result = await WaitForCallResultAsync<CreateItemResult_t>(SteamUGC.CreateItem(AppId, EWorkshopFileType.k_EWorkshopFileTypeCommunity)).ConfigureAwait(false);

        if (result.m_eResult != EResult.k_EResultOK)
        {
            throw new InvalidOperationException($"CreateItem failed: {result.m_eResult}");
        }

        return result.m_nPublishedFileId.m_PublishedFileId;
    }

    private static async Task<T> WaitForCallResultAsync<T>(SteamAPICall_t call, Action? onPoll = null)
    {
        var completion = new TaskCompletionSource<T>();

        using var callResult = CallResult<T>.Create((result, ioFailure) =>
        {
            if (ioFailure)
            {
                completion.SetException(new IOException("Steam API call failed."));
            }
            else
            {
                completion.SetResult(result);
            }
        });

        callResult.Set(call);

        while (!completion.Task.IsCompleted)
        {
            SteamAPI.RunCallbacks();
            onPoll?.Invoke();

            await Task.Delay(CallbackPollInterval).ConfigureAwait(false);
        }

        return await completion.Task.ConfigureAwait(false);
    }

    /// <summary>
    /// Throws when the file is not an image in one of <see cref="ThumbnailFormats"/>, detected from its contents rather than its extension.
    /// </summary>
    public static void ValidateThumbnailImage(string path)
    {
        using var codec = SKCodec.Create(path);

        if (codec == null || !ThumbnailFormats.Contains(codec.EncodedFormat))
        {
            throw new InvalidDataException($"Thumbnail image '{path}' is {codec?.EncodedFormat.ToString() ?? "not an image"}, supported formats: {string.Join(", ", ThumbnailFormats)}.");
        }
    }

    /// <summary>
    /// Steam only accepts small thumbnail images (1 mb or less) so the thumbnail is transformed into a JPEG, except for gifs which are uploaded unchanged.
    /// </summary>
    private static string WriteThumbnail(string sourcePath, ulong publishedFileId, DateTimeOffset time)
    {
        using var codec = SKCodec.Create(sourcePath);

        if (codec == null || !ThumbnailFormats.Contains(codec.EncodedFormat))
        {
            throw new InvalidDataException($"Thumbnail image '{sourcePath}' is {codec?.EncodedFormat.ToString() ?? "not an image"}, supported formats: {string.Join(", ", ThumbnailFormats)}.");
        }

        using var bitmap = SKBitmap.Decode(codec)
            ?? throw new InvalidDataException($"Failed to decode thumbnail image '{sourcePath}'.");

        var directory = Path.Combine(Path.GetTempPath(), $"workshopupload_{publishedFileId}");
        Directory.CreateDirectory(directory);

        if (codec.EncodedFormat == SKEncodedImageFormat.Gif)
        {
            var gifPath = Path.Combine(directory, $"thumbnail_{time.ToUnixTimeSeconds():x}.gif");
            File.Copy(sourcePath, gifPath, overwrite: true);
            return gifPath;
        }

        var path = Path.Combine(directory, $"thumbnail_{time.ToUnixTimeSeconds():x}.jpg");

        using var data = bitmap.Encode(SKEncodedImageFormat.Jpeg, ThumbnailJpegQuality)
            ?? throw new InvalidDataException($"Failed to encode thumbnail image '{sourcePath}' as JPEG.");

        using var stream = File.Create(path);
        data.SaveTo(stream);

        return path;
    }
}
