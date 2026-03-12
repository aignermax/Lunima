using CommunityToolkit.Mvvm.ComponentModel;

namespace CAP.Avalonia.ViewModels.Panels;

/// <summary>
/// ViewModel for the bottom panel containing status text and other bottom UI elements.
/// </summary>
public partial class BottomPanelViewModel : ObservableObject
{
    /// <summary>
    /// Status text displayed in the status bar.
    /// </summary>
    [ObservableProperty]
    private string _statusText = "Ready";
}
