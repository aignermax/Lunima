using Avalonia.Media;

namespace CAP.Avalonia.Controls.GdsImport;

/// <summary>One stable selection color; neutral context cannot be mistaken for selection.</summary>
internal static class GdsReviewSelectionStyle
{
    internal static readonly SolidColorBrush Selected = new(Color.Parse("#FFD166"));
    internal static readonly SolidColorBrush Context = new(Color.Parse("#9AA6AC"));
    internal const double ContextOpacity = .18;
}
