using Avalonia.Controls;
using Avalonia.Interactivity;

namespace GUI;

/// <summary>
/// A yes or no question, answered through <see cref="Window.ShowDialog{TResult}"/> with No as the default.
/// </summary>
public partial class ConfirmDialog : Window
{
    public ConfirmDialog(string title, string message)
    {
        InitializeComponent();

        Title = title;
        Message.Text = message;
    }

    // the XAML loader wants a constructor without parameters
    public ConfirmDialog()
        : this(string.Empty, string.Empty)
    {
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
