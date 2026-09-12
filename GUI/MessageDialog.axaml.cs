using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace GUI;

/// <summary>How serious a <see cref="MessageDialog"/> is, which picks its symbol and the colour of its gradient.</summary>
public enum MessageKind
{
    Info,
    Warning,
    Danger,
}

/// <summary>
/// A message to acknowledge, or a yes or no question, in the main window's style.
/// </summary>
public partial class MessageDialog : Window
{
    public MessageDialog(MessageKind kind, string title, string message, bool question)
    {
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

        InfoGradient.IsVisible = kind == MessageKind.Info;
        WarningGradient.IsVisible = kind == MessageKind.Warning;
        DangerGradient.IsVisible = kind == MessageKind.Danger;

        // a question answers Enter and Escape with No, a message with OK
        YesButton.IsVisible = NoButton.IsVisible = question;
        NoButton.IsDefault = NoButton.IsCancel = question;
        OkButton.IsVisible = OkButton.IsDefault = OkButton.IsCancel = !question;
    }

    // the XAML loader wants a constructor without parameters
    public MessageDialog()
        : this(MessageKind.Info, string.Empty, string.Empty, false)
    {
    }

    /// <summary>Shows a message over <paramref name="owner"/> until it is acknowledged.</summary>
    public static Task ShowAsync(Window owner, MessageKind kind, string title, string message)
    {
        return new MessageDialog(kind, title, message, false).ShowDialog(owner);
    }

    /// <summary>Asks a yes or no question over <paramref name="owner"/>, No being the default.</summary>
    public static Task<bool> AskAsync(Window owner, MessageKind kind, string title, string message)
    {
        return new MessageDialog(kind, title, message, true).ShowDialog<bool>(owner);
    }

    private void OnYes(object? sender, RoutedEventArgs e)
    {
        Close(true);
    }

    private void OnNo(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
