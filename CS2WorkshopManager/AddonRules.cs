using System.IO;
using ValveKeyValue;

namespace CS2WorkshopManager;

/// <summary>
/// Custom file packing rules, the same way VpkDirectories works, kept as publish_rules.txt in the addon's content folder and applied ahead of gameinfo's VpkDirectories.
/// A rule is a path prefix to include or exclude, matched the way gameinfo's entries are: the first rule that fits a path decides.
/// </summary>
public sealed class AddonRules
{
    public const string FileName = "publish_rules.txt";

    private const string ExcludeKey = "exclude";
    private const string IncludeKey = "include";

    /// <summary>A path prefix to keep out of, or in, the upload.</summary>
    public readonly record struct Rule(bool Exclude, string Pattern);

    public List<Rule> Rules { get; } = [];

    public static string GetPath(string contentRoot, string addonName)
    {
        return Path.Combine(contentRoot, addonName, FileName);
    }

    public static AddonRules Load(string path)
    {
        var rules = new AddonRules();

        if (!File.Exists(path))
        {
            return rules;
        }

        using var stream = File.OpenRead(path);
        var data = KVSerializer.Create(KVSerializationFormat.KeyValues1Text).Deserialize(stream, KVSerializerOptions.DefaultOptions);

        foreach (var child in data.Root.Children)
        {
            var exclude = child.Key.Equals(ExcludeKey, StringComparison.OrdinalIgnoreCase);

            if (exclude || child.Key.Equals(IncludeKey, StringComparison.OrdinalIgnoreCase))
            {
                rules.Rules.Add(new Rule(exclude, Normalize((string)child.Value)));
            }
        }

        return rules;
    }

    public void Save(string path)
    {
        var data = KVObject.ListCollection();

        foreach (var rule in Rules)
        {
            data.Add(rule.Exclude ? ExcludeKey : IncludeKey, rule.Pattern);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        using var stream = File.Create(path);
        KVSerializer.Create(KVSerializationFormat.KeyValues1Text).Serialize(stream, data, "publish_rules");
    }

    public void Add(Rule rule)
    {
        rule = rule with { Pattern = Normalize(rule.Pattern) };

        if (!Rules.Contains(rule))
        {
            Rules.Add(rule);
        }
    }

    public static string Normalize(string pattern)
    {
        return pattern.Trim().Replace('\\', '/');
    }
}
