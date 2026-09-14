using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;

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

    /// <summary>The Fluent keys for text on a surface painted in the accent.</summary>
    private static readonly string[] OnAccentKeys =
    [
        "ButtonForegroundPressed",
        "ToggleButtonForegroundPressed",
        "ToggleButtonForegroundChecked",
        "ToggleButtonForegroundCheckedPointerOver",
        "ToggleButtonForegroundCheckedPressed",
        "MenuFlyoutItemForegroundPressed",
    ];

    /// <summary>How far from the main colour towards the accent a popup's panel sits on the dark theme, where the panel is the window colour lifted towards the accent.</summary>
    private const double PopupMix = 0.2;

    /// <summary>How far towards the accent a popup's panel leans on the light theme, from the window colour lifted halfway to white.</summary>
    private const double LightPopupMix = 0.06;

    /// <summary>How far from the main colour towards the accent the outline of fields and check boxes sits, before it is greyed a little.</summary>
    private const double OutlineMix = 0.45;

    /// <summary>The theme's keys each window has painted in its accent, to take back before painting again.</summary>
    private static readonly ConditionalWeakTable<Window, List<object>> Painted = [];

    static WindowAccent()
    {
        ColorProperty.Changed.AddClassHandler<Window>((window, _) => ApplyLater(window));

        // the colouring is derived from the theme's colours, so it is derived again when the theme switches
        ThemeVariantScope.ActualThemeVariantProperty.Changed.AddClassHandler<Window>((window, _) => ApplyLater(window));
    }

    /// <summary>
    /// Derives the window's colouring once the change asking for it has gone through, since the theme's brushes it looks for take on a new accent in that same change.
    /// </summary>
    private static void ApplyLater(Window window)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (window.IsSet(ColorProperty))
            {
                Apply(window, GetColor(window));
            }
        });
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
            || !application.TryFindResource("ShadeColor", variant, out found) || found is not Color shade
            || !application.TryFindResource("ContrastSoftColor", variant, out found) || found is not Color contrastSoft
            || !application.TryFindResource("AccentBrush", variant, out found) || found is not SolidColorBrush accentBrush
            || !application.TryFindResource("PopupBrush", variant, out found) || found is not SolidColorBrush popupBrush
            || !application.TryFindResource("OutlineBrush", variant, out found) || found is not SolidColorBrush outlineBrush)
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
        resources["AccentButtonForeground"] = resources["AccentButtonForegroundPointerOver"] = resources["AccentButtonForegroundPressed"] = new SolidColorBrush(TextOn(face));

        // text on the accent itself: checked toggles, pressed buttons, selected rows and items
        var onAccent = new SolidColorBrush(TextOn(accent));

        resources["OnAccentBrush"] = onAccent;

        foreach (var key in OnAccentKeys)
        {
            resources[key] = onAccent;
        }

        // the panels of popups and the outlines of fields lean towards the accent: a popup is the window colour lifted towards it, on the light theme lifted towards white first,
        // and an outline sits between the two, greyed a little so it reads as an edge
        var light = variant == ThemeVariant.Light;
        var popup = light ? Mix(Mix(main, shade, 0.5), accent, LightPopupMix) : Mix(main, accent, PopupMix);
        var outline = Mix(Mix(main, accent, OutlineMix), contrastSoft, light ? 0.3 : 0.12);

        // whatever the theme paints with those shared brushes, the brushes themselves and the Fluent keys pointed at them, this window paints in its own,
        // after taking back what it painted before, which the theme or the accent may since have moved away from
        var painted = Painted.GetOrCreateValue(window);

        foreach (var key in painted)
        {
            resources.Remove(key);
        }

        painted.Clear();

        var own = new Dictionary<SolidColorBrush, SolidColorBrush>
        {
            [accentBrush] = brush,
            [popupBrush] = new SolidColorBrush(popup),
            [outlineBrush] = new SolidColorBrush(outline),
        };

        foreach (var (key, replacement) in ThemeKeysPainted(application, own, variant))
        {
            painted.Add(key);
            resources[key] = replacement;
        }
    }

    /// <summary>The keys of the theme that are one of the brushes in <paramref name="own"/>, each with the brush this window paints in its place.</summary>
    private static List<(object Key, SolidColorBrush Replacement)> ThemeKeysPainted(Application application, Dictionary<SolidColorBrush, SolidColorBrush> own, ThemeVariant variant)
    {
        var keys = new List<(object, SolidColorBrush)>();

        foreach (var provider in application.Resources.MergedDictionaries)
        {
            if ((provider is ResourceInclude include ? include.Loaded : provider) is not IResourceDictionary dictionary)
            {
                continue;
            }

            foreach (var key in dictionary.Keys)
            {
                if (dictionary.TryGetResource(key, variant, out var value) && value is SolidColorBrush themeBrush && own.TryGetValue(themeBrush, out var replacement))
                {
                    keys.Add((key, replacement));
                }
            }
        }

        return keys;
    }

    /// <summary>The colour <paramref name="amount"/> of the way from <paramref name="from"/> to <paramref name="to"/>.</summary>
    /// <summary>Black or white, whichever reads better on <paramref name="surface"/>: the one with the higher contrast ratio, which black has once the surface is light enough.</summary>
    private static Color TextOn(Color surface)
    {
        var luminance = 0.2126 * Linear(surface.R) + 0.7152 * Linear(surface.G) + 0.0722 * Linear(surface.B);

        return luminance > 0.179 ? Colors.Black : Colors.White;

        static double Linear(byte channel)
        {
            var value = channel / 255.0;

            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }
    }

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
