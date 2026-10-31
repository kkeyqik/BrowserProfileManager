using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using BPM.App.Services;
using BPM.Core.Analysis;
using BPM.Core.Diagnostics;
using BPM.Core.Models;
using Wpf.Ui.Controls;

namespace BPM.App.Views;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        (value is true) != Invert ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>null / empty string / 0 → Collapsed (or the opposite with Invert).</summary>
public sealed class EmptyToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool empty = value is null || value is string { Length: 0 } || value is int i && i == 0;
        return empty != Invert ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class PathToImageConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        ImageCache.FromFile(value as string, parameter is string s && int.TryParse(s, out var w) ? w : 64);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Profile colour (ARGB) → brush; a neutral grey when the browser stored none.</summary>
public sealed class ArgbToBrushConverter : IValueConverter
{
    static readonly SolidColorBrush Fallback = Freeze(new SolidColorBrush(Color.FromRgb(0x5B, 0x6B, 0x7F)));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not uint argb) return Fallback;
        var color = Color.FromArgb(0xFF, (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
        return Freeze(new SolidColorBrush(color));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;

    static SolidColorBrush Freeze(SolidColorBrush b) { b.Freeze(); return b; }
}

public sealed class BytesToTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        long bytes when bytes >= 1L << 30 => $"{bytes / (double)(1L << 30):0.0} GB",
        long bytes when bytes >= 1L << 20 => $"{bytes / (double)(1L << 20):0} MB",
        long bytes => $"{Math.Max(1, bytes / 1024)} KB",
        _ => "measuring…",
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Shared status colours that read well on light and dark backgrounds.</summary>
public static class Palette
{
    public static readonly Color Good = Color.FromRgb(0x2F, 0xA0, 0x4E);
    public static readonly Color Caution = Color.FromRgb(0xC8, 0x8A, 0x12);
    public static readonly Color Bad = Color.FromRgb(0xD9, 0x3F, 0x3F);
    public static readonly Color Neutral = Color.FromRgb(0x7A, 0x86, 0x94);
    public static readonly Color Accent = Color.FromRgb(0x3B, 0x82, 0xF6);

    public static SolidColorBrush Brush(Color c, byte alpha = 0xFF)
    {
        var brush = new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
        brush.Freeze();
        return brush;
    }
}

/// <summary>Risk / on-off / severity / permission weight → colour. Background="True" gives a soft tint for badges.</summary>
public sealed class StatusBrushConverter : IValueConverter
{
    public bool Background { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var color = value switch
        {
            RiskLevel.High or ProblemSeverity.Critical or PermissionWeight.High or CheckStatus.Error => Palette.Bad,
            RiskLevel.Medium or ProblemSeverity.Warning or PermissionWeight.Medium or CheckStatus.Warning => Palette.Caution,
            RiskLevel.Low or PermissionWeight.Low or CheckStatus.Ok => Palette.Good,
            ProblemSeverity.Info or CheckStatus.Info => Palette.Accent,
            true => Palette.Good,
            false => Palette.Neutral,
            _ => Palette.Neutral,
        };
        return Palette.Brush(color, Background ? (byte)0x33 : (byte)0xFF);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class SeverityToSymbolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        ProblemSeverity.Critical or CheckStatus.Error => SymbolRegular.ErrorCircle24,
        ProblemSeverity.Warning or CheckStatus.Warning => SymbolRegular.Warning24,
        CheckStatus.Ok => SymbolRegular.CheckmarkCircle24,
        _ => SymbolRegular.Info24,
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class JoinListConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is IEnumerable<string> items ? string.Join(", ", items) : "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
