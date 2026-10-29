using System.Globalization;

namespace CAP.Avalonia.ViewModels.Canvas;

/// <summary>
/// Parses the micrometer inputs of the length-matching panels: invariant first (the on-disk
/// format), then the UI culture so e.g. a German decimal comma is accepted when typing.
/// </summary>
internal static class LengthInput
{
    /// <summary>True when <paramref name="text"/> holds a number.</summary>
    public static bool TryParse(string? text, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            || double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
    }
}
