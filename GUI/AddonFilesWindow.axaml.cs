using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CS2WorkshopManager;

namespace GUI;

/// <summary>A user rule as the rules list shows it.</summary>
public sealed record RuleRow(AddonRules.Rule Rule)
{
    public string Kind => Rule.Exclude ? "Exclude" : "Include";

    public string Pattern => Rule.Pattern;
}

/// <summary>A file under the addon and whether the upload takes it, which a rescan updates in place so the list keeps its rows and its scroll.</summary>
public sealed class FileRow(string relativePath, long size, bool packed) : INotifyPropertyChanged
{
    private bool packed = packed;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string RelativePath { get; } = relativePath;

    public long Size { get; } = size;

    public string SizeText => AddonContents.FormatSize(Size);

    public bool Packed
    {
        get => packed;
        set
        {
            if (packed != value)
            {
                packed = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Packed)));
            }
        }
    }
}

/// <summary>
/// What an addon uploads: its asset types, every file under it, and the user's own include and exclude rules, which are changed here and saved as they change.
/// </summary>
public partial class AddonFilesWindow : Window
{
    private readonly WorkshopManager manager;

    private AddonRules rules = new();
    private string? addon;

    /// <summary>Every file under the addon, which the search narrows down for the list.</summary>
    private List<FileRow> files = [];

    /// <summary>Whether a change is still being saved and scanned, during which what is shown is about to change.</summary>
    private bool changing;

    public AddonFilesWindow(WorkshopManager manager, string? addon)
    {
        this.manager = manager;

        InitializeComponent();

        try
        {
            var addons = manager.GetAddonNames().Order(StringComparer.OrdinalIgnoreCase).ToList();

            AddonBox.ItemsSource = addons;
            AddonBox.SelectedItem = addon == null ? null : addons.Find(name => name.Equals(addon, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Status.Text = exception.Message;
        }
    }

    // the XAML loader wants a constructor without parameters
    public AddonFilesWindow()
        : this(new WorkshopManager(string.Empty), null)
    {
    }

    private async void OnAddonChanged(object? sender, SelectionChangedEventArgs e)
    {
        await ReloadAsync();
    }

    /// <summary>
    /// Reads the rules and scans the addon again, which is where everything shown comes from.
    /// </summary>
    private async Task ReloadAsync()
    {
        if (AddonBox.SelectedItem is not string name)
        {
            addon = null;
            files = [];
            Contents.Contents = null;
            RulesList.ItemsSource = null;
            FilesList.ItemsSource = null;
            return;
        }

        // a rescan after a rule change keeps the last count up until the new one is ready, instead of flashing through "Scanning"
        if (addon != name)
        {
            Status.Text = $"Scanning {name}...";
        }

        addon = name;

        var addonPath = Path.Combine(manager.AddonsRoot, name);
        var gameInfoPath = manager.GameInfoPath;

        try
        {
            rules = manager.LoadRules(name);

            var current = rules;
            var (packed, all) = await Task.Run(() =>
            {
                var packedFiles = AddonPackager.CollectFiles(addonPath, gameInfoPath, current);
                var packedPaths = packedFiles.Select(file => file.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var every = AddonPackager.ListFiles(addonPath)
                    .Select(file => (Path: AddonPackager.GetRelativePath(addonPath, file.FullName), file.Length, Packed: packedPaths.Contains(file.FullName)))
                    .OrderByDescending(entry => entry.Length).ThenBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                return (packedFiles, every);
            });

            // the selection moved on while this addon was scanned
            if (addon != name)
            {
                return;
            }

            Contents.Contents = AddonContents.FromFiles(packed);
            RulesList.ItemsSource = rules.Rules.Select(rule => new RuleRow(rule)).ToList();

            // the same files as before only change their ticks, so the list keeps its rows and its scroll
            if (files.Count == all.Count && files.Zip(all).All(pair => pair.First.RelativePath == pair.Second.Path && pair.First.Size == pair.Second.Length))
            {
                foreach (var (row, entry) in files.Zip(all))
                {
                    row.Packed = entry.Packed;
                }
            }
            else
            {
                files = all.Select(entry => new FileRow(entry.Path, entry.Length, entry.Packed)).ToList();
                ShowFiles();
            }

            Status.Text = $"{packed.Count} of {all.Count} files are packed";
        }
        catch (Exception exception)
        {
            // the rules file is hand editable too, so its parser's own errors are reported like the file system's
            Status.Text = exception.Message;
        }
    }

    private void OnSearchChanged(object? sender, TextChangedEventArgs e)
    {
        ShowFiles();
    }

    /// <summary>The files whose path contains what was searched for, or all of them.</summary>
    private void ShowFiles()
    {
        var search = SearchBox.Text?.Trim() ?? string.Empty;

        FilesList.ItemsSource = search.Length == 0 ? files : files.Where(file => file.RelativePath.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>Applies a change to the rules, saves them and scans again, or says why it could not save.</summary>
    private async Task ChangeRulesAsync(Action<AddonRules> change)
    {
        if (addon == null || changing)
        {
            return;
        }

        changing = true;

        try
        {
            change(rules);
            manager.SaveRules(addon, rules);
            await ReloadAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Status.Text = exception.Message;
        }
        finally
        {
            changing = false;
        }
    }

    private async void OnAddRule(object? sender, RoutedEventArgs e)
    {
        var pattern = AddonRules.Normalize(RulePattern.Text ?? string.Empty);

        if (pattern.Length == 0)
        {
            return;
        }

        var exclude = (string?)((Button)sender!).Tag == "exclude";
        RulePattern.Text = string.Empty;

        await ChangeRulesAsync(current => current.Add(new AddonRules.Rule(exclude, pattern)));
    }

    private async void OnRemoveRule(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is RuleRow row)
        {
            await ChangeRulesAsync(current => current.Rules.Remove(row.Rule));
        }
    }

    /// <summary>
    /// A tick undoes the user's own rule for that file when there is one. Otherwise unticking keeps the file out by its path, and ticking brings it in by its path ahead of whatever else keeps it out.
    /// </summary>
    private async void OnFileClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as CheckBox)?.DataContext is not FileRow file)
        {
            return;
        }

        await ChangeRulesAsync(current =>
        {
            // a rule the user made for this very file, in the direction being undone
            var removed = current.Rules.RemoveAll(rule => rule.Exclude != file.Packed && rule.Pattern.Equals(file.RelativePath, StringComparison.OrdinalIgnoreCase));

            if (removed > 0)
            {
                return;
            }

            // the newest rule goes first, since the first matching rule wins, so it beats whatever else the user has
            if (file.Packed)
            {
                current.Rules.Insert(0, new AddonRules.Rule(true, file.RelativePath));
            }
            else
            {
                current.Rules.Insert(0, new AddonRules.Rule(false, file.RelativePath));
            }
        });
    }
}
