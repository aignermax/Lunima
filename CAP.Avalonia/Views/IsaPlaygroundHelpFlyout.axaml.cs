using Avalonia.Controls;

namespace CAP.Avalonia.Views;

/// <summary>
/// Help of the ISA playground window (#1237): the existing machine explanation
/// plus a "Why light adds" section (#1152 text budget) with a looping carry-ripple
/// diagram — a light pulse hops the carry through the four full adders of
/// <c>0111 + 0001</c>, the sum flips to 1000 and the time bar grows with every hop,
/// which is the picosecond number in the playground's status line. Opened from the
/// <c>HelpFlyoutButton</c> in the window header.
/// </summary>
public partial class IsaPlaygroundHelpFlyout : UserControl
{
    /// <summary>Initializes the flyout content.</summary>
    public IsaPlaygroundHelpFlyout()
    {
        InitializeComponent();
    }
}
