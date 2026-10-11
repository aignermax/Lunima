using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace CAP.Avalonia.Converters;

/// <summary>
/// Resolves a resource key such as <c>Icon.Cpu</c> (from <c>Styles/Icons.axaml</c>) to the
/// icon geometry, for view models that choose an icon by name.
/// </summary>
public sealed class IconKeyConverter : IValueConverter
{
    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string key || Application.Current is null)
            return null;
        return Application.Current.TryFindResource(key, out var resource) ? resource : null;
    }

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
