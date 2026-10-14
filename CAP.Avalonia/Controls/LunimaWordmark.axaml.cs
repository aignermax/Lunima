using Avalonia.Controls;

namespace CAP.Avalonia.Controls;

/// <summary>
/// The Lunima wordmark: the waveguide "L" of the app icon followed by "unima". Scales with
/// its height; the text follows the primary text brush so it works in every theme.
/// </summary>
public partial class LunimaWordmark : UserControl
{
    /// <summary>Creates the wordmark.</summary>
    public LunimaWordmark() => InitializeComponent();
}
