using System.IO;
using ValveKeyValue;

namespace CS2WorkshopManager;

/// <summary>
/// Custom file packing rules, the same way VpkDirectories works, kept as publish_rules.txt in the addon's content folder and applied ahead of gameinfo's VpkDirectories.
/// A rule is a path prefix to include or exclude, matched the way gameinfo's entries are: the first rule that fits a path decides.
/// The file holds two blocks: the rules the user made, and under <see cref="AutoRootName"/> the ones a crawl of the addon's map generated.
/// </summary>
public sealed class AddonRules
{
    public const string FileName = "publish_rules.txt";

    private const string RootName = "publish_rules";

    /// <summary>The block the generated rules are kept in, apart from the user's own so that neither is lost when the other is saved.</summary>
    private const string AutoRootName = "unused_content_auto_rules";

    private const string ExcludeKey = "exclude";
    private const string IncludeKey = "include";
    private const string MapKey = "map";

    /// <summary>A path prefix to keep out of, or in, the upload.</summary>
    public readonly record struct Rule(bool Exclude, string Pattern);

    public List<Rule> Rules { get; } = [];

    /// <summary>
    /// For a generated block, the map the rules were made from, so that they can be made again from the same one. Null for rules the user made.
    /// </summary>
    public string? Map { get; set; }

    public static string GetPath(string contentRoot, string addonName)
    {
        return Path.Combine(contentRoot, addonName, FileName);
    }

    /// <summary>The rules the user made, none when there is no file.</summary>
    public static AddonRules Load(string path)
    {
        return Load(path, auto: false);
    }

    /// <summary>The generated rules, none when there is no file or nothing has been generated for the addon yet.</summary>
    public static AddonRules LoadAuto(string path)
    {
        return Load(path, auto: true);
    }

    private static AddonRules Load(string path, bool auto)
    {
        var rules = new AddonRules();

        if (!File.Exists(path))
        {
            return rules;
        }

        using var stream = File.OpenRead(path);
        var data = KVSerializer.Create(KVSerializationFormat.KeyValues1Text).Deserialize(stream, KVSerializerOptions.DefaultOptions);

        // the generated block follows the user's own in the file, which the parser gives back as a child of it
        if (!auto)
        {
            rules.Read(data.Root);
        }
        else if (data.Root.TryGetValue(AutoRootName, out var generated))
        {
            rules.Read(generated);
        }

        return rules;
    }

    /// <summary>
    /// Writes both of the file's blocks. Each is given every time so that saving one keeps the other, an addon's own rules
    /// surviving a regenerated list and a regenerated list surviving a rule the user makes.
    /// </summary>
    public static void Save(string path, AddonRules rules, AddonRules auto)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(auto);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var serializer = KVSerializer.Create(KVSerializationFormat.KeyValues1Text);

        using var stream = File.Create(path);
        serializer.Serialize(stream, rules.Write(), RootName);

        if (auto.Rules.Count == 0)
        {
            return;
        }

        stream.Write("\n"u8);
        serializer.Serialize(stream, auto.Write(), AutoRootName);
    }

    /// <summary>Adds the rules a key values block holds, its "exclude" and "include" entries in their order.</summary>
    internal void Read(KVObject block)
    {
        foreach (var child in block.Children)
        {
            if (child.Key.Equals(MapKey, StringComparison.OrdinalIgnoreCase))
            {
                Map = Normalize((string)child.Value);
                continue;
            }

            var exclude = child.Key.Equals(ExcludeKey, StringComparison.OrdinalIgnoreCase);

            if (exclude || child.Key.Equals(IncludeKey, StringComparison.OrdinalIgnoreCase))
            {
                Rules.Add(new Rule(exclude, Normalize((string)child.Value)));
            }
        }
    }

    /// <summary>The rules as a key values block, an "exclude" or "include" entry each in their order.</summary>
    internal KVObject Write()
    {
        var data = KVObject.ListCollection();

        if (Map != null)
        {
            data.Add(MapKey, Map);
        }

        foreach (var rule in Rules)
        {
            data.Add(rule.Exclude ? ExcludeKey : IncludeKey, rule.Pattern);
        }

        return data;
    }

    /// <summary>These rules followed by <paramref name="next"/>'s, as one set for packing by, leaving both as they are.</summary>
    public AddonRules Then(AddonRules next)
    {
        ArgumentNullException.ThrowIfNull(next);

        var rules = new AddonRules();

        rules.Rules.AddRange(Rules);
        rules.Rules.AddRange(next.Rules);

        return rules;
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
