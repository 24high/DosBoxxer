using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace DosBoxxer.App.Converters;

/// <summary>
/// Resolves an icon resource key (e.g. <c>IconGenreAction</c>) to the corresponding
/// <see cref="Geometry"/> from the application resources. Needed because category tiles pick
/// their icon at runtime from the ViewModel.
/// </summary>
public sealed class IconKeyToGeometryConverter : IValueConverter
{
    public static IconKeyToGeometryConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string key || key.Length == 0)
        {
            return null;
        }

        var application = Application.Current;
        if (application is null)
        {
            return null;
        }

        return application.TryFindResource(key, out var resource) && resource is Geometry geometry
            ? geometry
            : (Geometry?)null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Returns <c>true</c> when the bound integer is greater than zero.</summary>
public sealed class GreaterThanZeroConverter : IValueConverter
{
    public static GreaterThanZeroConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int number && number > 0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Inverts a boolean; used for "is not busy" style bindings.</summary>
public sealed class InverseBooleanConverter : IValueConverter
{
    public static InverseBooleanConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not bool flag || !flag;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not bool flag || !flag;
}

/// <summary>
/// Maps a nullable boolean to the index of a three-state selector
/// (0 = inherit / null, 1 = true, 2 = false), used by the per-game DOSBox overrides.
/// </summary>
public sealed class NullableBoolToIndexConverter : IValueConverter
{
    public static NullableBoolToIndexConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            true => 1,
            false => 2,
            _ => 0,
        };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            1 => true,
            2 => false,
            _ => (bool?)null,
        };
}
