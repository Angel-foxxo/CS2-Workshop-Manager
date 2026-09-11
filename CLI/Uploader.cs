using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using ConsoleAppFramework;
using CS2WorkshopUploader;

namespace CLI;

public static class Uploader
{
    public static async Task Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

        ConsoleApp.Version = GetVersion();

        // https://github.com/Cysharp/ConsoleAppFramework
        await ConsoleApp.RunAsync(args, HandleArguments);
    }

    /// <summary>
    /// Packs a compiled Counter-Strike 2 addon and publishes it to the Steam Workshop.
    /// </summary>
    /// <param name="addon">-a, Name of the addon folder under game/csgo_addons to upload.</param>
    /// <param name="title">-t, Title of the workshop item.</param>
    /// <param name="id">-i, Workshop ID of an existing submission, if this is provided everything will be treated as updating this submission. </param>
    /// <param name="description">-d, Description of the workshop item.</param>
    /// <param name="description_file">Read the description from this text file instead.</param>
    /// <param name="changenote">-c, Change note, Defaults to "Created {title}." or "Edited {title}.".</param>
    /// <param name="changenote_file">Read the change note from this text file instead.</param>
    /// <param name="thumbnail">-th, Disk path for the thumbnail, this will get re-encoded to Jpeg.</param>
    /// <param name="visibility">-v, Visibility of the workshop item: public, friendsonly, private or unlisted.</param>
    /// <param name="tags">Comma separated list of workshop tags added after the "CS2" and "Map" tags. Map game modes are Classic, Deathmatch, Armsrace, Wingman and Custom.</param>
    /// <param name="tags_dangerous">Comma separated workshop tags used as the complete tag list, without "CS2" and "Map". Without those the item does not show up as a Counter-Strike 2 map in the workshop or in the game's map browsers.</param>
    /// <param name="game">Path to the Counter-Strike 2 install folder. Located through Steam when omitted.</param>
    /// <param name="stage_only">Only pack the addon into game/csgo_addons/vpks/{id}/ and do not talk to Steam. Requires --id.</param>
    /// <param name="force">Update the item even when its installed workshop content was published from a different addon folder.</param>
    private static async Task<int> HandleArguments(
        string addon,
        string title,
        ulong? id = default,
        string? description = default,
        string? description_file = default,
        string? changenote = default,
        string? changenote_file = default,
        string? thumbnail = default,
        string visibility = nameof(WorkshopVisibility.Private),
        string? tags = default,
        string? tags_dangerous = default,
        string? game = default,
        bool stage_only = false,
        bool force = false
    )
    {
        if (!Enum.TryParse<WorkshopVisibility>(visibility, ignoreCase: true, out var itemVisibility))
        {
            Console.Error.WriteLine("Visibility must be one of: public, friendsonly, private, unlisted.");
            return 1;
        }

        if (tags != null && tags_dangerous != null)
        {
            Console.Error.WriteLine("Do not use --tags with --tags_dangerous.");
            return 1;
        }

        var itemTags = tags_dangerous != null
            ? SplitTags(tags_dangerous)
            : [.. WorkshopUploader.DefaultTags, .. SplitTags(tags)];

        itemTags = [.. itemTags.Distinct(StringComparer.OrdinalIgnoreCase)];

        if (tags_dangerous != null)
        {
            Console.WriteLine($"Warning: uploading with tags [{string.Join(", ", itemTags)}] without the `CS2` and `Map` tags the workshop manager always sets.");
        }

        if (description != null && description_file != null)
        {
            Console.Error.WriteLine("Do not use --description with --description_file.");
            return 1;
        }

        if (changenote != null && changenote_file != null)
        {
            Console.Error.WriteLine("Do not use --changenote with --changenote_file.");
            return 1;
        }

        if (description_file != null)
        {
            if (!File.Exists(description_file))
            {
                Console.Error.WriteLine($"Description file \"{description_file}\" does not exist.");
                return 1;
            }

            description = await File.ReadAllTextAsync(description_file);
        }

        if (changenote_file != null)
        {
            if (!File.Exists(changenote_file))
            {
                Console.Error.WriteLine($"Change note file \"{changenote_file}\" does not exist.");
                return 1;
            }

            changenote = await File.ReadAllTextAsync(changenote_file);
        }

        if (thumbnail != null && !File.Exists(thumbnail))
        {
            Console.Error.WriteLine($"Thumbnail image \"{thumbnail}\" does not exist.");
            return 1;
        }

        if (stage_only && id == null)
        {
            Console.Error.WriteLine("--stage_only requires --id.");
            return 1;
        }

        WorkshopUploader uploader;

        if (game != null)
        {
            if (!Directory.Exists(game))
            {
                Console.Error.WriteLine($"Game folder \"{game}\" does not exist.");
                return 1;
            }

            uploader = new WorkshopUploader(Path.GetFullPath(game));
        }
        else
        {
            try
            {
                uploader = WorkshopUploader.FromSteamInstall();
            }
            catch (DirectoryNotFoundException exception)
            {
                Console.Error.WriteLine(exception.Message);
                return 1;
            }
        }

        var addonPath = Path.Combine(uploader.AddonsRoot, addon);

        if (!Directory.Exists(addonPath))
        {
            Console.Error.WriteLine($"Addon folder \"{addonPath}\" does not exist.");
            return 1;
        }

        Console.WriteLine($"Game: {uploader.GamePath}");
        Console.WriteLine($"Addon: {addonPath}");

        if (stage_only)
        {
            var stagingPath = AddonPackager.Stage(uploader.AddonsRoot, addon, uploader.GameInfoPath, id!.Value, title, DateTimeOffset.UtcNow);

            Console.WriteLine($"Staged: {stagingPath}");

            foreach (var file in Directory.GetFiles(stagingPath))
            {
                Console.WriteLine($"  {Path.GetFileName(file)} ({new FileInfo(file).Length:N0} bytes)");
            }

            return 0;
        }

        try
        {
            if (force && id != null && WorkshopUploader.GetConflictingSourceFolder(addon, id.Value) is string previousAddon)
            {
                Console.WriteLine($"Warning: workshop item {id} was last published from addon \"{previousAddon}\", updating it from \"{addon}\".");
            }

            var result = await uploader.PublishAsync(new AddonPublishOptions
            {
                AddonName = addon,
                PublishedFileId = id,
                Title = title,
                Description = description ?? string.Empty,
                Visibility = itemVisibility,
                Tags = itemTags,
                ThumbnailImagePath = thumbnail,
                ChangeNote = changenote,
                AllowSourceFolderChange = force,
            }, new Progress<float>(progress => Console.Write($"\rUploading {progress:P0}   ")));

            Console.WriteLine();
            Console.WriteLine($"Published: {result.Url}");

            if (result.NeedsWorkshopAgreement)
            {
                Console.WriteLine("The Steam Workshop legal agreement must be accepted before the item becomes visible.");
            }
        }
        catch (SourceFolderConflictException exception)
        {
            Console.Error.WriteLine($"{exception.Message} Pass --force to update it anyway.");
            return 1;
        }
        catch (Exception exception)
        {
            Console.WriteLine();
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
        finally
        {
            WorkshopUploader.ShutdownSteam();
        }

        return 0;
    }

    private static string[] SplitTags(string? tags)
    {
        return tags?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) ?? [];
    }

    private static string GetVersion()
    {
        var info = new StringBuilder();
        info.Append("Version: ");
        info.AppendLine(typeof(Uploader).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion);
        info.Append("OS: ");
        info.Append(RuntimeInformation.OSDescription);
        info.Append(" (");
        info.Append(RuntimeInformation.OSArchitecture.ToString());
        info.Append(')');
        return info.ToString();
    }
}
