using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TruckModsListUpdater.Core.Mods;

namespace TruckModsListUpdater.App.Views;

/// <summary>Loads a cached icon file without keeping it locked; null or unreadable files show nothing.</summary>
public sealed class IconPathConverter : IValueConverter
{
    public int DecodeWidth { get; set; } = 276;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || !File.Exists(path)) return null;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            image.DecodePixelWidth = DecodeWidth;
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception e) when (e is IOException or NotSupportedException or FileFormatException or UriFormatException)
        {
            return null;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Issue severity to a badge colour.</summary>
public sealed class SeverityBrushConverter : IValueConverter
{
    static readonly Brush Error = Frozen(Color.FromRgb(0xE5, 0x48, 0x4D));
    static readonly Brush Warning = Frozen(Color.FromRgb(0xF2, 0xA9, 0x3B));
    static readonly Brush Info = Frozen(Color.FromRgb(0x8A, 0x8F, 0x98));

    static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        IssueSeverity.Error => Error,
        IssueSeverity.Warning => Warning,
        _ => Info,
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class NullToCollapsedConverter : IValueConverter
{
    /// <summary>With ConverterParameter "invert": visible only when the value is empty.</summary>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool empty = value is null || (value is string s && s.Length == 0);
        if (parameter as string == "invert") empty = !empty;
        return empty ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class FilterNameConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        ViewModels.ModFilter.All => "All mods",
        ViewModels.ModFilter.Local => "Local mods",
        ViewModels.ModFilter.Workshop => "Workshop mods",
        ViewModels.ModFilter.WithIssues => "With warnings",
        _ => value?.ToString() ?? "",
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
