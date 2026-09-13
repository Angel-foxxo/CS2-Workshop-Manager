using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;

namespace GUI;

/// <summary>
/// A window's accent colour, from which the rest of its colouring follows: the centre of its gradient is the main colour a quarter of the way to the accent,
/// its highlighted buttons wear the accent toned down towards the main colour and brighten under the pointer, and every control colour the theme takes from the accent takes it from this one instead.
/// Set it on the window in XAML, or bind it from code when the colour is only known then.
/// </summary>
public static class WindowAccent
{
    public static readonly AttachedProperty<Color> ColorProperty = AvaloniaProperty.RegisterAttached<Window, Color>("Color", typeof(WindowAccent));

    /// <summary>How far from the main colour towards the accent the gradient's centre sits.</summary>
    private const double GradientMix = 0.25;

    /// <summary>How far from the main colour towards the accent a highlighted button's face sits, short of the accent so its text stays readable.</summary>
    private const double ButtonMix = 0.65;

    /// <summary>How much lighter, or darker on the light theme, a highlighted button gets under the pointer, and twice that when pressed.</summary>
    private const double HoverStep = 0.06;

    static WindowAccent()
    {
        ColorProperty.Changed.AddClassHandler<Window>((window, e) => Apply(window, e.GetNewValue<Color>()));
    }

    public static Color GetColor(Window window)
    {
        return window.GetValue(ColorProperty);
    }

    public static void SetColor(Window window, Color value)
    {
        window.SetValue(ColorProperty, value);
    }

    /// <summary>Puts the colours derived from <paramref name="accent"/> into the window's resources, ahead of the theme's for everything in the window.</summary>
    private static void Apply(Window window, Color accent)
    {
        var variant = window.ActualThemeVariant;
        var application = Application.Current!;

        if (!application.TryFindResource("AppColor", variant, out var found) || found is not Color main
            || !application.TryFindResource("AccentColor", variant, out found) || found is not Color themeAccent)
        {
            return;
        }

        var step = variant == ThemeVariant.Light ? -HoverStep : HoverStep;
        var brush = new SolidColorBrush(accent);
        var face = Mix(main, accent, ButtonMix);
        var hover = new SolidColorBrush(Shift(face, step));
        var pressed = new SolidColorBrush(Shift(face, 2 * step));
        var resources = window.Resources;

        resources["AppAccentColor"] = Mix(main, accent, GradientMix);
        resources["AccentButtonBackground"] = resources["AccentButtonBorderBrush"] = new SolidColorBrush(face);
        resources["AccentButtonBackgroundPointerOver"] = resources["AccentButtonBorderBrushPointerOver"] = hover;
        resources["AccentButtonBackgroundPressed"] = resources["AccentButtonBorderBrushPressed"] = pressed;

        // whatever the theme paints in the shared accent, the accent brush and the Fluent colours pointed at it, this window paints in its own
        foreach (var key in ThemeKeysPainted(application, themeAccent, variant))
        {
            resources[key] = brush;
        }
    }

    /// <summary>The keys of the theme's brushes in <paramref name="color"/>.</summary>
    private static List<object> ThemeKeysPainted(Application application, Color color, ThemeVariant variant)
    {
        var keys = new List<object>();

        foreach (var provider in application.Resources.MergedDictionaries)
        {
            if ((provider is ResourceInclude include ? include.Loaded : provider) is not IResourceDictionary dictionary)
            {
                continue;
            }

            foreach (var key in dictionary.Keys)
            {
                if (dictionary.TryGetResource(key, variant, out var value) && value is SolidColorBrush { Opacity: 1 } painted && painted.Color == color)
                {
                    keys.Add(key);
                }
            }
        }

        return keys;
    }

    /// <summary>The colour <paramref name="amount"/> of the way from <paramref name="from"/> to <paramref name="to"/>.</summary>
    private static Color Mix(Color from, Color to, double amount)
    {
        return Color.FromRgb(Step(from.R, to.R), Step(from.G, to.G), Step(from.B, to.B));

        byte Step(byte start, byte end)
        {
            return (byte)Math.Round(start + (end - start) * amount);
        }
    }

    /// <summary>The colour with its lightness moved by <paramref name="amount"/>.</summary>
    private static Color Shift(Color color, double amount)
    {
        var hsl = color.ToHsl();

        return new HslColor(hsl.A, hsl.H, hsl.S, Math.Clamp(hsl.L + amount, 0, 1)).ToRgb();
    }
}
