using System.IO;
using ValveKeyValue;

namespace CS2WorkshopManager;

/// <summary>
/// What the user has set for every game and addon alike, kept as settings.txt in the user's application data folder in the key values format of the game's own files.
/// </summary>
public sealed class AppSettings
{
    public const string FileName = "settings.txt";

    private const string RulesKey = "publish_rules";

    /// <summary>Where the settings are kept, under the user's application data folder: %AppData% on Windows, ~/.config on Linux.</summary>
    public static string FilePath { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CS2WorkshopManager", FileName);

    /// <summary>The packing rules that apply to every addon, checked before the addon's own rules and gameinfo's.</summary>
    public AddonRules GlobalRules { get; } = new();

    /// <summary>The settings as saved, or none when nothing has been saved yet.</summary>
    public static AppSettings Load()
    {
        var settings = new AppSettings();

        if (!File.Exists(FilePath))
        {
            return settings;
        }

        using var stream = File.OpenRead(FilePath);
        var data = KVSerializer.Create(KVSerializationFormat.KeyValues1Text).Deserialize(stream, KVSerializerOptions.DefaultOptions);

        if (data.Root.TryGetValue(RulesKey, out var rules))
        {
            settings.GlobalRules.Read(rules);
        }

        return settings;
    }

    public void Save()
    {
        var data = KVObject.Collection();
        data.Add(RulesKey, GlobalRules.Write());

        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);

        using var stream = File.Create(FilePath);
        KVSerializer.Create(KVSerializationFormat.KeyValues1Text).Serialize(stream, data, "settings");
    }
}
