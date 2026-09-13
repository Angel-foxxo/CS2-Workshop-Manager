using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace GUI;

/// <summary>How serious a <see cref="MessageDialog"/> is, which picks its symbol and its accent.</summary>
public enum MessageKind
{
    Info,
    Warning,
    Danger,
}

/// <summary>
/// A message to acknowledge, or a question whose buttons say what they do, in the main window's style.
/// </summary>
public partial class MessageDialog : Window
{
    /// <param name="confirm">What the button that goes ahead says, or null for a message that is only acknowledged.</param>
    /// <param name="cancel">What the button that backs out says.</param>
    public MessageDialog(MessageKind kind, string title, string message, string? confirm, string cancel = "Cancel")
    {
        var question = confirm != null;

        InitializeComponent();

        Title = title;
        Message.Text = message;

        var symbol = kind switch
        {
            MessageKind.Warning => "warning",
            MessageKind.Danger => "danger",
            _ => "info",
        };

        Symbol.Source = new Bitmap(AssetLoader.Open(new Uri($"avares://CS2WorkshopManager-GUI/assets/{symbol}.png")));

        // the accent of the news, which the gradient and the highlighted button follow: the app's for information, amber for warnings, red for destructive questions
        var accentKey = kind switch
        {
            MessageKind.Warning => "WarningAccentColor",
            MessageKind.Danger => "DangerAccentColor",
            _ => "AccentColor",
        };

        this.Bind(WindowAccent.ColorProperty, this.GetResourceObservable(accentKey));

        // a question answers Enter by going ahead and Escape by backing out, a message answers both with OK
        ConfirmButton.Content = confirm;
        CancelButton.Content = cancel;
        ConfirmButton.IsVisible = CancelButton.IsVisible = question;
        ConfirmButton.IsDefault = CancelButton.IsCancel = question;
        OkButton.IsVisible = OkButton.IsDefault = OkButton.IsCancel = !question;
    }

    // the XAML loader wants a constructor without parameters
    public MessageDialog()
        : this(MessageKind.Info, string.Empty, string.Empty, null)
    {
    }

    /// <summary>Shows a message over <paramref name="owner"/> until it is acknowledged.</summary>
    public static Task ShowAsync(Window owner, MessageKind kind, string title, string message)
    {
        return new MessageDialog(kind, title, message, null).ShowDialog(owner);
    }

    /// <summary>
    /// Asks a question over <paramref name="owner"/>, with a button that goes ahead saying <paramref name="confirm"/> and a highlighted default that backs out saying <paramref name="cancel"/>.
    /// </summary>
    /// <returns>Whether to go ahead.</returns>
    public static Task<bool> AskAsync(Window owner, MessageKind kind, string title, string message, string confirm, string cancel = "Cancel")
    {
        return new MessageDialog(kind, title, message, confirm, cancel).ShowDialog<bool>(owner);
    }

    private void OnConfirm(object? sender, RoutedEventArgs e)
    {
        Close(true);
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
