using Avalonia.Controls;

namespace CAP.Avalonia.Views;

/// <summary>
/// Non-modal tool window hosting the AI Design Assistant chat. Opened from
/// the toolbar; the DataContext is the shared <c>MainViewModel</c> so the
/// hosted panel keeps its existing bindings.
/// </summary>
public partial class AiAssistantWindow : Window
{
    /// <summary>Initializes the window.</summary>
    public AiAssistantWindow()
    {
        InitializeComponent();
    }
}
