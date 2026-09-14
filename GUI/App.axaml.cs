using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using CS2WorkshopManager;

namespace GUI;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            try
            {
                ApplyTheme(AppSettings.Load().Theme);
            }
            catch (Exception)
            {
                // a settings file that can not be read leaves the system theme, and is reported in the settings window
            }

            desktop.MainWindow = new MainWindow();
            desktop.Exit += (_, _) => WorkshopManager.ShutdownSteam();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Gives every window the theme set, or the system's.</summary>
    public static void ApplyTheme(AppTheme theme)
    {
        Current!.RequestedThemeVariant = theme switch
        {
            AppTheme.Light => ThemeVariant.Light,
            AppTheme.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
    }
}
