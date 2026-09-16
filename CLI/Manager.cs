using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using ConsoleAppFramework;
using CS2WorkshopManager;

namespace CLI;

public static class Manager
{
    private static ConsoleApp.ConsoleAppBuilder? app;

    public static async Task Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

        // titles and the copyright sign are not all in the console's own code page
        Console.OutputEncoding = Encoding.UTF8;

        ConsoleApp.Version = GetVersion();

        // https://github.com/Cysharp/ConsoleAppFramework
        app = ConsoleApp.Create();
        app.Add("", Commands.Help);
        app.Add("upload", Commands.Upload);
        app.Add("edit", Commands.Edit);
        app.Add("list", Commands.List);
        app.Add("view", Commands.View);
        app.Add("previews", Commands.Previews);
        app.Add("delete", Commands.Delete);
        app.Add("addons", Commands.Addons);
        app.Add("contents", Commands.Contents);
        app.Add("files", Commands.Files);
        app.Add("rules", RulesCommands.List);
        app.Add("rules unused", RulesCommands.Unused);
        app.Add("rules exclude", RulesCommands.Exclude);
        app.Add("rules include", RulesCommands.Include);
        app.Add("rules remove", RulesCommands.Remove);

        await app.RunAsync(args).ConfigureAwait(false);
    }

    /// <summary>Shows the help, the way --help does.</summary>
    internal static async Task ShowHelpAsync()
    {
        await app!.RunAsync(["--help"]).ConfigureAwait(false);
    }

    /// <summary>Opens the game at <paramref name="game"/>, or the Steam install when that is null.</summary>
    internal static WorkshopManager OpenGame(string? game)
    {
        if (game == null)
        {
            return WorkshopManager.FromSteamInstall();
        }

        if (!Directory.Exists(game))
        {
            throw new DirectoryNotFoundException($"Game folder \"{game}\" does not exist.");
        }

        return new WorkshopManager(Path.GetFullPath(game));
    }

    /// <summary>Get addon folder from addon name.</summary>
    internal static string OpenAddon(WorkshopManager manager, string addon)
    {
        var addonPath = Path.Combine(manager.AddonsRoot, addon);

        if (!Directory.Exists(addonPath))
        {
            throw new DirectoryNotFoundException($"Addon folder \"{addonPath}\" does not exist.");
        }

        return addonPath;
    }

    /// <summary>
    /// Runs a command, printing what went wrong for the errors the manager reports, and leaves Steam afterwards.
    /// </summary>
    /// <returns>The exit code.</returns>
    internal static async Task<int> RunAsync(Func<Task> command)
    {
        try
        {
            await command().ConfigureAwait(false);
            return 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException or ArgumentException)
        {
            await Console.Error.WriteLineAsync(exception.Message).ConfigureAwait(false);
            return 1;
        }
        finally
        {
            WorkshopManager.ShutdownSteam();
        }
    }

    /// <summary>Runs a command that has nothing to wait for.</summary>
    internal static int Run(Action command)
    {
        return RunAsync(() =>
        {
            command();
            return Task.CompletedTask;
        }).GetAwaiter().GetResult();
    }

    private static string GetVersion()
    {
        var assembly = typeof(Manager).Assembly;
        var info = new StringBuilder();
        info.Append("Version: ");
        info.AppendLine(assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion);
        info.Append("OS: ");
        info.Append(RuntimeInformation.OSDescription);
        info.Append(" (");
        info.Append(RuntimeInformation.OSArchitecture.ToString());
        info.AppendLine(")");
        info.AppendLine(assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()!.Copyright);
        info.Append("Source: ");
        info.Append(assembly.GetCustomAttributes<AssemblyMetadataAttribute>().First(metadata => metadata.Key == "RepositoryUrl").Value);
        return info.ToString();
    }
}

public static class Commands
{
    /// <summary>
    /// Command line Counter-Strike 2 Workshop manager.
    /// </summary>
    public static async Task Help()
    {
        await Manager.ShowHelpAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Packs a compiled Counter-Strike 2 addon and publishes it to the Steam Workshop, as a new item or as an update of an existing one.
    /// </summary>
    /// <param name="addon">-a, Name of the addon folder under game/csgo_addons to upload.</param>
    /// <param name="title">-t, Title of the workshop item, needed for a new one.</param>
    /// <param name="id">-i, Workshop ID of an existing submission, if this is provided everything will be treated as updating this submission, which keeps its title, description, visibility and tags unless they are given.</param>
    /// <param name="description">-d, Description of the workshop item.</param>
    /// <param name="description_file">Read the description from this text file instead.</param>
    /// <param name="changenote">-c, Change note, Defaults to "Created {title}." or "Edited {title}.".</param>
    /// <param name="changenote_file">Read the change note from this text file instead.</param>
    /// <param name="thumbnail">-th, Disk path for the thumbnail.</param>
    /// <param name="visibility">-v, Visibility of the workshop item: public, friendsonly, private or unlisted. A new item is private by default.</param>
    /// <param name="tags">Comma separated list of workshop tags added after the "CS2" and "Map" tags. Map game modes are Classic, Deathmatch, Armsrace, Wingman and Custom.</param>
    /// <param name="tags_dangerous">Comma separated workshop tags used as the complete tag list, without "CS2" and "Map". Without those the item does not show up as a Counter-Strike 2 map in the workshop or in the game's map browsers.</param>
    /// <param name="screenshots">Comma separated disk paths of pictures to add to the gallery under the thumbnail, PNG, JPG or GIF under 1 MB each.</param>
    /// <param name="videos">Comma separated YouTube links or video IDs to add to the gallery.</param>
    /// <param name="game">Path to the Counter-Strike 2 install folder. Located through Steam when omitted.</param>
    /// <param name="stage_only">Only pack the addon into game/csgo_addons/vpks/{id}/ and do not talk to Steam. Requires --id.</param>
    /// <param name="force">Update the item even when its installed workshop content was published from a different addon folder.</param>
    public static async Task<int> Upload(
        string addon,
        string? title = default,
        ulong? id = default,
        string? description = default,
        string? description_file = default,
        string? changenote = default,
        string? changenote_file = default,
        string? thumbnail = default,
        string? visibility = default,
        string? tags = default,
        string? tags_dangerous = default,
        string? screenshots = default,
        string? videos = default,
        string? game = default,
        bool stage_only = false,
        bool force = false
    )
    {
        WorkshopVisibility? itemVisibility = null;

        if (visibility != null)
        {
            if (!TryParseVisibility(visibility, out var parsed))
            {
                return 1;
            }

            itemVisibility = parsed;
        }

        if (tags != null && tags_dangerous != null)
        {
            await Console.Error.WriteLineAsync("Do not use --tags with --tags_dangerous.").ConfigureAwait(false);
            return 1;
        }

        // null when an existing item is to keep its tags
        IReadOnlyList<string>? itemTags = tags_dangerous != null
            ? SplitTags(tags_dangerous)
            : tags != null || id == null ? [.. WorkshopManager.DefaultTags, .. SplitTags(tags)] : null;

        itemTags = itemTags?.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        if (tags_dangerous != null)
        {
            Console.WriteLine($"Warning: uploading with tags [{string.Join(", ", itemTags!)}] without the `CS2` and `Map` tags the workshop manager always sets.");
        }

        if (!TryReadText(ref description, description_file, "description") || !TryReadText(ref changenote, changenote_file, "change note") || !TryCheckThumbnail(thumbnail))
        {
            return 1;
        }

        if (stage_only && id == null)
        {
            await Console.Error.WriteLineAsync("--stage_only requires --id.").ConfigureAwait(false);
            return 1;
        }

        if (id == null && string.IsNullOrWhiteSpace(title))
        {
            await Console.Error.WriteLineAsync("A new item needs --title.").ConfigureAwait(false);
            return 1;
        }

        return await Manager.RunAsync(async () =>
        {
            var manager = Manager.OpenGame(game);
            var addonPath = Manager.OpenAddon(manager, addon);

            Console.WriteLine($"Game: {manager.GamePath}");
            Console.WriteLine($"Addon: {addonPath}");

            if (stage_only)
            {
                // packed the same way a publish packs, the generated rules included and made again from their map first
                manager.RefreshAutoRules(addon);

                var stagingPath = AddonPackager.Stage(manager.AddonsRoot, addon, manager.GameInfoPath, id!.Value, title ?? string.Empty, DateTimeOffset.UtcNow, manager.LoadPackingRules(addon));

                Console.WriteLine($"Staged: {stagingPath}");

                foreach (var file in Directory.GetFiles(stagingPath))
                {
                    Console.WriteLine($"  {Path.GetFileName(file)} ({new FileInfo(file).Length:N0} bytes)");
                }

                return;
            }

            // the progress is written over itself on one line, which is ended whatever comes after it
            var progressShown = false;

            try
            {
                if (force && id != null && WorkshopManager.GetConflictingSourceFolder(addon, id.Value) is string previousAddon)
                {
                    Console.WriteLine($"Warning: workshop item {id} was last published from addon \"{previousAddon}\", updating it from \"{addon}\".");
                }

                // what is not given stays as it is on an existing item, and its gallery is kept in front of what is added, a new item starts private with what is given
                var item = id != null && (screenshots != null || videos != null) ? await FindItemAsync(id.Value).ConfigureAwait(false) : null;

                var result = await manager.PublishAsync(new AddonPublishOptions
                {
                    AddonName = addon,
                    PublishedFileId = id,
                    Title = title,
                    Description = description,
                    Visibility = itemVisibility,
                    Tags = itemTags,
                    ThumbnailImagePath = thumbnail,
                    Gallery = BuildGallery(item?.Previews ?? [], [], screenshots, videos),
                    ChangeNote = changenote,
                    AllowSourceFolderChange = force,
                }, new Progress<float>(progress =>
                {
                    progressShown = true;
                    Console.Write($"\rUploading {progress:P0}   ");
                })).ConfigureAwait(false);

                PrintPublished(result);
            }
            catch (SourceFolderConflictException exception)
            {
                throw new InvalidOperationException($"{exception.Message} Pass --force to update it anyway.", exception);
            }
            finally
            {
                if (progressShown)
                {
                    Console.WriteLine();
                }
            }
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Changes the provided info of a published item without uploading its files again.
    /// </summary>
    /// <param name="id">-i, Workshop ID of the submission to edit.</param>
    /// <param name="title">-t, New title.</param>
    /// <param name="description">-d, New description.</param>
    /// <param name="description_file">Read the new description from this text file instead.</param>
    /// <param name="thumbnail">-th, Disk path for a new thumbnail.</param>
    /// <param name="visibility">-v, New visibility: public, friendsonly, private or unlisted.</param>
    /// <param name="tags">Comma separated game mode tags to set, replacing the item's current game modes and keeping its other tags: Classic, Deathmatch, Armsrace, Wingman and Custom. Pass an empty string to clear them.</param>
    /// <param name="screenshots">Comma separated disk paths of pictures to add to the gallery under the thumbnail, PNG, JPG or GIF under 1 MB each.</param>
    /// <param name="videos">Comma separated YouTube links or video IDs to add to the gallery.</param>
    /// <param name="remove_previews">Comma separated indices of gallery entries to remove, as the previews command lists them.</param>
    /// <param name="changenote">-c, Change note, none by default.</param>
    /// <param name="changenote_file">Read the change note from this text file instead.</param>
    public static async Task<int> Edit(
        ulong id,
        string? title = default,
        string? description = default,
        string? description_file = default,
        string? thumbnail = default,
        string? visibility = default,
        string? tags = default,
        string? screenshots = default,
        string? videos = default,
        string? remove_previews = default,
        string? changenote = default,
        string? changenote_file = default
    )
    {
        WorkshopVisibility? itemVisibility = null;

        if (visibility != null)
        {
            if (!TryParseVisibility(visibility, out var parsed))
            {
                return 1;
            }

            itemVisibility = parsed;
        }

        if (!TryReadText(ref description, description_file, "description") || !TryReadText(ref changenote, changenote_file, "change note") || !TryCheckThumbnail(thumbnail))
        {
            return 1;
        }

        var removals = new List<int>();

        foreach (var index in SplitTags(remove_previews))
        {
            if (!int.TryParse(index, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
            {
                await Console.Error.WriteLineAsync($"\"{index}\" is not a gallery index, the previews command lists them.").ConfigureAwait(false);
                return 1;
            }

            removals.Add(parsed);
        }

        if (title == null && description == null && thumbnail == null && itemVisibility == null && tags == null && screenshots == null && videos == null && removals.Count == 0)
        {
            await Console.Error.WriteLineAsync("Nothing to change, give at least one of --title, --description, --thumbnail, --visibility, --tags, --screenshots, --videos or --remove_previews.").ConfigureAwait(false);
            return 1;
        }

        return await Manager.RunAsync(async () =>
        {
            IReadOnlyList<string>? itemTags = null;
            GalleryUpdate? gallery = null;

            // the item itself is only fetched for what needs it, the tags it has and the gallery it has
            var item = tags != null || screenshots != null || videos != null || removals.Count > 0 ? await FindItemAsync(id).ConfigureAwait(false) : null;

            if (item != null)
            {
                if (removals.Any(index => index >= item.Previews.Count))
                {
                    throw new ArgumentException($"The gallery has {item.Previews.Count} entries, the previews command lists them.");
                }

                gallery = BuildGallery(item.Previews, removals, screenshots, videos);
            }

            if (tags != null)
            {
                // the item's tags with its game modes swapped for the given ones, and "Map" put back should it have lost it
                var modes = SplitTags(tags);

                foreach (var mode in modes.Where(mode => !WorkshopManager.GameModeTags.Contains(mode, StringComparer.OrdinalIgnoreCase)))
                {
                    throw new ArgumentException($"\"{mode}\" is not a game mode, the game modes are {string.Join(", ", WorkshopManager.GameModeTags)}.");
                }

                var kept = item!.Tags.Where(tag => !WorkshopManager.GameModeTags.Contains(tag, StringComparer.OrdinalIgnoreCase)).ToList();

                if (!kept.Contains("Map", StringComparer.OrdinalIgnoreCase))
                {
                    kept.Add("Map");
                }

                itemTags = [.. kept, .. modes.Distinct(StringComparer.OrdinalIgnoreCase)];
            }

            var result = await new WorkshopManager(string.Empty).PublishAsync(new AddonPublishOptions
            {
                PublishedFileId = id,
                Title = title,
                Description = description,
                Visibility = itemVisibility,
                Tags = itemTags,
                ThumbnailImagePath = thumbnail,
                Gallery = gallery,
                ChangeNote = changenote,
            }).ConfigureAwait(false);

            PrintPublished(result);
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Lists the maps the logged in Steam account has published, newest first.
    /// </summary>
    public static async Task<int> List()
    {
        return await Manager.RunAsync(async () =>
        {
            Console.WriteLine($"{"ID",-11} {"Visibility",-12} {"Updated",-16} {"Size",10} {"Subs",7} {"Views",8} {"Likes",7} {"Favs",7}  Title [Tags]");

            var count = 0;

            await foreach (var item in WorkshopManager.GetPublishedItemsAsync().ConfigureAwait(false))
            {
                count++;
                Console.WriteLine($"{item.PublishedFileId,-11} {VisibilityName(item.Visibility),-12} {item.TimeUpdated.ToLocalTime():yyyy-MM-dd HH:mm} {AddonContents.FormatSize(item.Size),10} {item.Subscribers,7:N0} {item.Views,8:N0} {item.Likes,7:N0} {item.Favorites,7:N0}  {item.Title} [{string.Join(", ", item.Tags)}]");
            }

            Console.WriteLine($"{count} published maps");
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Opens a published item's workshop page in the browser.
    /// </summary>
    /// <param name="id">-i, Workshop ID of the submission.</param>
    public static int View(ulong id)
    {
        var url = new WorkshopPublishResult(id, false).Url.ToString();

        Console.WriteLine(url);

        using var browser = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

        return 0;
    }

    /// <summary>
    /// Lists the gallery under a published item's thumbnail, with the indices that --remove_previews takes.
    /// </summary>
    /// <param name="id">-i, Workshop ID of the submission.</param>
    public static async Task<int> Previews(ulong id)
    {
        return await Manager.RunAsync(async () =>
        {
            var item = await FindItemAsync(id).ConfigureAwait(false);

            for (var index = 0; index < item.Previews.Count; index++)
            {
                var preview = item.Previews[index];
                var kind = preview.Kind switch
                {
                    WorkshopPreviewKind.YouTubeVideo => "video",
                    WorkshopPreviewKind.Image => "screenshot",
                    _ => "other",
                };

                Console.WriteLine($"[{index}] {kind,-10} {preview.PageUrl?.ToString() ?? preview.Value}{(preview.FileName.Length > 0 ? $" ({preview.FileName})" : string.Empty)}");
            }

            Console.WriteLine($"{item.Previews.Count} gallery entries on \"{item.Title}\"");
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes a published item from the workshop, which cannot be undone. Users subscribed to it will no longer be able to access it.
    /// </summary>
    /// <param name="id">-i, Workshop ID of the submission to delete.</param>
    /// <param name="yes">-y, Delete without asking.</param>
    public static async Task<int> Delete(ulong id, bool yes = false)
    {
        return await Manager.RunAsync(async () =>
        {
            var item = await FindItemAsync(id).ConfigureAwait(false);

            if (!yes)
            {
                Console.Write($"Delete \"{item.Title}\" ({item.PublishedFileId}) from the workshop? This cannot be undone. Type yes to go ahead: ");

                if (!string.Equals(Console.ReadLine()?.Trim(), "yes", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("Not deleted.");
                    return;
                }
            }

            await WorkshopManager.DeleteItemAsync(id).ConfigureAwait(false);

            Console.WriteLine($"Deleted \"{item.Title}\" ({item.PublishedFileId}).");
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Lists the addon folders under game/csgo_addons, and which one the running workshop tools have open.
    /// </summary>
    /// <param name="game">Path to the Counter-Strike 2 install folder. Located through Steam when omitted.</param>
    public static int Addons(string? game = default)
    {
        return Manager.Run(() =>
        {
            var manager = Manager.OpenGame(game);
            var open = WorkshopManager.GetRunningToolsAddon();

            if (!string.IsNullOrEmpty(open))
            {
                Console.WriteLine($"Addon currently open in tools: \"{open}\"\n");
            }

            foreach (var addon in manager.GetAddonNames().Order(StringComparer.OrdinalIgnoreCase))
            {
                Console.WriteLine(addon);
            }
        });
    }

    /// <summary>
    /// Shows what an addon would upload by asset type.
    /// </summary>
    /// <param name="addon">-a, Name of the addon folder under game/csgo_addons.</param>
    /// <param name="game">Path to the Counter-Strike 2 install folder. Located through Steam when omitted.</param>
    public static int Contents(string addon, string? game = default)
    {
        return Manager.Run(() =>
        {
            var manager = Manager.OpenGame(game);
            var addonPath = Manager.OpenAddon(manager, addon);
            var contents = AddonPackager.GetContents(addonPath, manager.GameInfoPath, manager.LoadPackingRules(addon));

            foreach (var assetType in contents.AssetTypes)
            {
                Console.WriteLine(contents.Describe(assetType));
            }

            Console.WriteLine(contents.Summary);

            if (contents.ExceedsUploadLimit)
            {
                Console.WriteLine($"Warning: this exceeds the CS2 Workshop upload limit of {AddonContents.FormatSize(AddonPackager.MaxTotalSize)}.");
            }
        });
    }

    /// <summary>
    /// Lists all the files an addon would upload, with the packing rules applied, largest first.
    /// </summary>
    /// <param name="addon">-a, Name of the addon folder under game/csgo_addons.</param>
    /// <param name="game">Path to the Counter-Strike 2 install folder. Located through Steam when omitted.</param>
    /// <param name="all">Also list the files that are left out, marking each file [x] when it is uploaded and [ ] when not.</param>
    public static int Files(string addon, string? game = default, bool all = false)
    {
        return Manager.Run(() =>
        {
            var manager = Manager.OpenGame(game);
            var addonPath = Manager.OpenAddon(manager, addon);
            var packed = AddonPackager.CollectFiles(addonPath, manager.GameInfoPath, manager.LoadPackingRules(addon));
            var packedPaths = packed.Select(file => file.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var files = all ? AddonPackager.ListFiles(addonPath) : packed;

            foreach (var file in files.OrderByDescending(file => file.Length).ThenBy(file => file.FullName, StringComparer.OrdinalIgnoreCase))
            {
                var mark = all ? (packedPaths.Contains(file.FullName) ? "[x] " : "[ ] ") : string.Empty;

                Console.WriteLine($"{mark}{AddonContents.FormatSize(file.Length),12}  {AddonPackager.GetRelativePath(addonPath, file.FullName)}");
            }

            Console.WriteLine(all ? $"{packed.Count} of {files.Count} files are uploaded" : $"{packed.Count} files are uploaded");
        });
    }

    /// <summary>One of the account's published items, by id.</summary>
    internal static async Task<WorkshopItem> FindItemAsync(ulong id)
    {
        await foreach (var item in WorkshopManager.GetPublishedItemsAsync().ConfigureAwait(false))
        {
            if (item.PublishedFileId == id)
            {
                return item;
            }
        }

        throw new InvalidOperationException($"Workshop item {id} is not one of the published items of the logged in account.");
    }

    private static void PrintPublished(WorkshopPublishResult result)
    {
        Console.WriteLine($"Published: {result.Url}");

        if (result.NeedsWorkshopAgreement)
        {
            Console.WriteLine("The Steam Workshop legal agreement must be accepted before the item becomes visible.");
        }
    }

    private static bool TryParseVisibility(string visibility, out WorkshopVisibility parsed)
    {
        if (Enum.TryParse(visibility, ignoreCase: true, out parsed))
        {
            return true;
        }

        Console.Error.WriteLine("Visibility must be one of: public, friendsonly, private, unlisted.");
        return false;
    }

    private static string VisibilityName(WorkshopVisibility visibility)
    {
        return visibility == WorkshopVisibility.FriendsOnly ? "Friends Only" : visibility.ToString();
    }

    /// <summary>Takes <paramref name="text"/> from <paramref name="file"/> when that was given instead, and says no when both were or the file is missing.</summary>
    private static bool TryReadText(ref string? text, string? file, string what)
    {
        if (file == null)
        {
            return true;
        }

        if (text != null)
        {
            Console.Error.WriteLine($"Do not give the {what} both directly and as a file.");
            return false;
        }

        if (!File.Exists(file))
        {
            Console.Error.WriteLine($"{char.ToUpperInvariant(what[0])}{what[1..]} file \"{file}\" does not exist.");
            return false;
        }

        text = File.ReadAllText(file);
        return true;
    }

    private static bool TryCheckThumbnail(string? thumbnail)
    {
        if (thumbnail == null)
        {
            return true;
        }

        if (!File.Exists(thumbnail))
        {
            Console.Error.WriteLine($"Thumbnail image \"{thumbnail}\" does not exist.");
            return false;
        }

        try
        {
            WorkshopManager.ValidateThumbnailImage(thumbnail);
            return true;
        }
        catch (InvalidDataException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return false;
        }
    }

    private static string[] SplitTags(string? tags)
    {
        return tags?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) ?? [];
    }

    /// <summary>
    /// The gallery as the command line edits it: the item's entries but the removed ones, in their order, then the new screenshots and videos. Null when nothing is asked of it.
    /// </summary>
    private static GalleryUpdate? BuildGallery(IReadOnlyList<WorkshopPreview> current, List<int> removals, string? screenshots, string? videos)
    {
        if (removals.Count == 0 && screenshots == null && videos == null)
        {
            return null;
        }

        var wanted = new List<PreviewSource>();

        for (var index = 0; index < current.Count; index++)
        {
            if (!removals.Contains(index))
            {
                wanted.Add(PreviewSource.Existing(index));
            }
        }

        wanted.AddRange(SplitTags(screenshots).Select(PreviewSource.Screenshot));
        wanted.AddRange(SplitTags(videos).Select(PreviewSource.Video));

        return new GalleryUpdate(current, wanted);
    }
}

/// <summary>
/// Custom include and exclude rules, applied ahead of gameinfo's when an addon is packed: an addon's own, kept in publish_rules.txt in its content root folder,
/// or with --global the ones that apply to every addon, kept in the app's settings file and checked before the addon's own.
/// </summary>
public static class RulesCommands
{
    /// <summary>
    /// Lists the rules in the order they apply, the first matching rule deciding for a path.
    /// </summary>
    /// <param name="addon">-a, Name of the addon folder under game/csgo_addons.</param>
    /// <param name="global">-g, The rules that apply to every addon, kept in the app's settings file, instead of an addon's own.</param>
    /// <param name="game">Path to the Counter-Strike 2 install folder. Located through Steam when omitted.</param>
    public static int List(string? addon = default, bool global = false, string? game = default)
    {
        return Change(addon, global, game, rules =>
        {
            foreach (var rule in rules.Rules)
            {
                Console.WriteLine($"{(rule.Exclude ? "exclude" : "include"),-8} {rule.Pattern}");
            }

            Console.WriteLine(global ? $"{rules.Rules.Count} rules in {AppSettings.FilePath}, applied to every addon" : $"{rules.Rules.Count} rules in {AddonRules.FileName}");

            return false;
        });
    }

    /// <summary>
    /// Lists the addon's files that no compiled map reaches, largest first, and with --apply keeps them out of the upload from then on.
    /// This is what "Exclude unused content" in the Pack Filter window does.
    /// </summary>
    /// <param name="addon">-a, Name of the addon folder under game/csgo_addons.</param>
    /// <param name="map">The compiled map to start from, such as maps/de_dust2.vpk. The addon's own map when omitted.</param>
    /// <param name="apply">Save what is found as the addon's generated rules, which every upload then leaves out until they are cleared.</param>
    /// <param name="clear">Throw the generated rules away, so an upload takes everything again.</param>
    /// <param name="game">Path to the Counter-Strike 2 install folder. Located through Steam when omitted.</param>
    public static int Unused(string addon, string? map = default, bool apply = false, bool clear = false, string? game = default)
    {
        return Manager.Run(() =>
        {
            var manager = Manager.OpenGame(game);
            var addonPath = Manager.OpenAddon(manager, addon);

            if (clear)
            {
                if (apply)
                {
                    throw new ArgumentException("Give either --apply or --clear, not both.");
                }

                var cleared = manager.LoadAutoRules(addon).Rules.Count;
                manager.SaveAutoRules(addon, new AddonRules());

                Console.WriteLine($"Cleared {cleared} generated rules, {addon} uploads everything again.");
                return;
            }

            var (rules, result) = manager.BuildAutoRules(addon, map);

            if (!result.HasCompiledMap)
            {
                Console.WriteLine(map == null
                    ? $"{addon} has no compiled map under maps, so there is nothing to tell what it uses from what it does not."
                    : $"{addon} has no compiled map \"{map}\".");

                return;
            }

            var total = 0L;

            foreach (var rule in rules.Rules)
            {
                var length = new FileInfo(Path.Combine(addonPath, rule.Pattern)).Length;

                Console.WriteLine($"{AddonContents.FormatSize(length),12}  {rule.Pattern}");
                total += length;
            }

            Console.WriteLine($"{rules.Rules.Count} files, {AddonContents.FormatSize(total)}, are reached from none of {string.Join(", ", result.Maps)}");

            if (!apply)
            {
                Console.WriteLine("Add --apply to keep them out of the upload.");
                return;
            }

            manager.SaveAutoRules(addon, rules);

            Console.WriteLine($"Saved as generated rules in {AddonRules.FileName}, and made again from {rules.Map} on every upload.");
        });
    }

    /// <summary>
    /// Keeps a file or folder out of the upload. The rule goes first, so it wins over the rules before it and over gameinfo.
    /// </summary>
    /// <param name="pattern">Path under the addon the rule starts with, a folder ending in a slash such as materials/dev/.</param>
    /// <param name="addon">-a, Name of the addon folder under game/csgo_addons.</param>
    /// <param name="global">-g, Make a rule for every addon, kept in the app's settings file, instead of one for an addon. It wins over the addon's own rules.</param>
    /// <param name="game">Path to the Counter-Strike 2 install folder. Located through Steam when omitted.</param>
    public static int Exclude([Argument] string pattern, string? addon = default, bool global = false, string? game = default)
    {
        return Change(addon, global, game, rules => Insert(rules, new AddonRules.Rule(true, pattern), global));
    }

    /// <summary>
    /// Brings a file or folder into the upload. The rule goes first, so it wins over the rules before it and over gameinfo.
    /// </summary>
    /// <param name="pattern">Path under the addon the rule starts with, a folder ending in a slash such as materials/dev/.</param>
    /// <param name="addon">-a, Name of the addon folder under game/csgo_addons.</param>
    /// <param name="global">-g, Make a rule for every addon, kept in the app's settings file, instead of one for an addon. It wins over the addon's own rules.</param>
    /// <param name="game">Path to the Counter-Strike 2 install folder. Located through Steam when omitted.</param>
    public static int Include([Argument] string pattern, string? addon = default, bool global = false, string? game = default)
    {
        return Change(addon, global, game, rules => Insert(rules, new AddonRules.Rule(false, pattern), global));
    }

    /// <summary>
    /// Removes the rules for a path, whichever way they go.
    /// </summary>
    /// <param name="pattern">The path of the rules to remove, as they were given.</param>
    /// <param name="addon">-a, Name of the addon folder under game/csgo_addons.</param>
    /// <param name="global">-g, Remove from the rules that apply to every addon, kept in the app's settings file, instead of an addon's own.</param>
    /// <param name="game">Path to the Counter-Strike 2 install folder. Located through Steam when omitted.</param>
    public static int Remove([Argument] string pattern, string? addon = default, bool global = false, string? game = default)
    {
        return Change(addon, global, game, rules =>
        {
            var normalized = AddonRules.Normalize(pattern);
            var removed = rules.Rules.RemoveAll(rule => rule.Pattern.Equals(normalized, StringComparison.OrdinalIgnoreCase));

            if (removed == 0)
            {
                throw new InvalidOperationException($"There is no rule for \"{normalized}\".");
            }

            Console.WriteLine($"Removed {removed} rule{(removed == 1 ? "" : "s")} for {normalized}.");

            return true;
        });
    }

    /// <summary>Puts the rule first, after taking out any earlier rule for the same path, so the newest rule wins.</summary>
    private static bool Insert(AddonRules rules, AddonRules.Rule rule, bool global)
    {
        var normalized = AddonRules.Normalize(rule.Pattern);

        if (normalized.Length == 0)
        {
            throw new ArgumentException("The pattern is empty.");
        }

        rules.Rules.RemoveAll(existing => existing.Pattern.Equals(normalized, StringComparison.OrdinalIgnoreCase));
        rules.Rules.Insert(0, new AddonRules.Rule(rule.Exclude, normalized));

        Console.WriteLine($"{(rule.Exclude ? "Excluded" : "Included")} {normalized}{(global ? " for every addon" : "")}.");

        return true;
    }

    /// <summary>Loads the addon's rules, or with <paramref name="global"/> the ones for every addon, for <paramref name="change"/>, which says whether they need saving.</summary>
    private static int Change(string? addon, bool global, string? game, Func<AddonRules, bool> change)
    {
        return Manager.Run(() =>
        {
            if (global)
            {
                var settings = AppSettings.Load();

                if (settings.SavedByNewerApp)
                {
                    Console.Error.WriteLine($"Warning: the settings were saved by version {settings.SavedBy} of the app, newer than this version {AppSettings.AppVersion}, which can not use what that version added.");
                }

                if (change(settings.GlobalRules))
                {
                    settings.Save();
                }

                return;
            }

            if (addon == null)
            {
                throw new ArgumentException("Give --addon for an addon's rules, or --global for the rules that apply to every addon.");
            }

            var manager = Manager.OpenGame(game);
            Manager.OpenAddon(manager, addon);

            var rules = manager.LoadRules(addon);

            if (change(rules))
            {
                manager.SaveRules(addon, rules);
            }
        });
    }
}
