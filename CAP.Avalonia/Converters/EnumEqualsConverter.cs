using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace CAP.Avalonia.Converters;

/// <summary>
/// One-way converter: true when an enum value's name equals the string
/// <c>ConverterParameter</c> (comma-separated to match any of several names). Used to mark
/// the active tool of a segmented button group, e.g. the canvas mode buttons.
/// </summary>
public sealed class EnumEqualsConverter : IValueConverter
{
    /// <summary>Shared instance for <c>{x:Static}</c> use in AXAML.</summary>
    public static readonly EnumEqualsConverter Instance = new();

    /// <inheritdoc/>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not Enum enumValue || parameter is not string names)
            return false;
        var name = enumValue.ToString();
        foreach (var candidate in names.Split(','))
        {
            if (candidate.Trim() == name)
                return true;
        }
        return false;
    }

    /// <inheritdoc/>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
