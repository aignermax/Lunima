using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace CAP.Avalonia.Converters;

/// <summary>
/// Maps a text status badge (✅ ⚠ ❌ ✓ ≈ ✗ ?) to a design-system status icon or its
/// semantic brush, so lists show the same line icons as the rest of the UI. The
/// converter parameter selects the output: <c>icon</c> (geometry) or <c>brush</c>.
/// </summary>
public sealed class StatusBadgeConverter : IValueConverter
{
    private const string IconParameter = "icon";

    private static readonly Dictionary<string, (string Icon, string Brush)> Mapping = new()
    {
        ["✅"] = ("Icon.CircleCheck", "Brush.Success"),
        ["✓"] = ("Icon.CircleCheck", "Brush.Success"),
        ["⚠️"] = ("Icon.TriangleAlert", "Brush.Warning"),
        ["⚠"] = ("Icon.TriangleAlert", "Brush.Warning"),
        ["❌"] = ("Icon.CircleX", "Brush.Danger"),
        ["✗"] = ("Icon.CircleX", "Brush.Danger"),
        ["≈"] = ("Icon.CircleDot", "Brush.Info"),
    };

    private static readonly (string Icon, string Brush) Unknown = ("Icon.CircleHelp", "Brush.Text.Tertiary");

    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (Application.Current is null)
            return null;
        var (icon, brush) = value is string badge && Mapping.TryGetValue(badge, out var entry) ? entry : Unknown;
        var key = string.Equals(parameter as string, IconParameter, StringComparison.Ordinal) ? icon : brush;
        return Application.Current.TryFindResource(key, out var resource) ? resource : null;
    }

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
