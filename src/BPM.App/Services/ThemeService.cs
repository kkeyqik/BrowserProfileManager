using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace BPM.App.Services;

public static class ThemeService
{
    /// <summary>Brand colours, used instead of the Windows accent colour so the app looks the same on every PC:
    /// blue in the light theme, violet to match the dark glass look.</summary>
    public static readonly Color Brand = Color.FromRgb(0x3B, 0x82, 0xF6);
    public static readonly Color GlassBrand = Color.FromRgb(0x8B, 0x5C, 0xF6);

    static Color BrandFor(ApplicationTheme theme) => theme == ApplicationTheme.Dark ? GlassBrand : Brand;

    static readonly Uri GlassSource = new("pack://application:,,,/Views/GlassDark.xaml", UriKind.Absolute);
    static ResourceDictionary? glass;

    static Window? watched;
    static bool accentHooked;

    /// <summary>The dark theme gets the glass look on top of WPF-UI's dark colours; the light theme drops it. It's kept
    /// last among the app's dictionaries so it wins over the theme dictionary WPF-UI swaps in.</summary>
    static void ApplyGlass(ApplicationTheme theme)
    {
        if (Application.Current?.Resources.MergedDictionaries is not { } dictionaries) return;
        glass ??= new ResourceDictionary { Source = GlassSource };
        dictionaries.Remove(glass);
        if (theme == ApplicationTheme.Dark) dictionaries.Add(glass);
    }

    /// <summary>Applies "System", "Light" or "Dark" to the app. Only "System" follows Windows' theme changes; picking
    /// Light or Dark afterwards stops following them.</summary>
    public static void Apply(string theme, Window window)
    {
        var resolved = theme switch
        {
            "Light" => ApplicationTheme.Light,
            "Dark" => ApplicationTheme.Dark,
            _ => SystemUsesLightTheme() ? ApplicationTheme.Light : ApplicationTheme.Dark,
        };
        if (!accentHooked)
        {
            // Keep the look and brand colour in step whenever the theme changes (also when the system watcher switches it).
            ApplicationThemeManager.Changed += (current, _) =>
            {
                ApplyGlass(current);
                ApplicationAccentColorManager.Apply(BrandFor(current), current);
            };
            accentHooked = true;
        }

        bool followSystem = theme is not ("Light" or "Dark");
        if (!followSystem && watched != null)
        {
            SystemThemeWatcher.UnWatch(watched);
            watched = null;
        }
        ApplicationThemeManager.Apply(resolved, WindowBackdropType.Mica, false);
        ApplyGlass(resolved);
        ApplicationAccentColorManager.Apply(BrandFor(resolved), resolved);
        if (followSystem && watched == null)
        {
            SystemThemeWatcher.Watch(window, WindowBackdropType.Mica, false);
            watched = window;
        }
    }

    static bool SystemUsesLightTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int value && value == 1;
    }
}
