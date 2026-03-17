using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.Library;

/// <summary>
/// ViewModel for managing the component library.
/// NOTE: Group template functionality has been removed as it's marked "Optional (Nice to Have)".
/// This ViewModel is now a placeholder for future group template features.
/// </summary>
public partial class ComponentLibraryViewModel : ObservableObject
{
    [ObservableProperty]
    private string _statusText = "Group templates not implemented";

    /// <summary>
    /// Initializes the component library ViewModel.
    /// </summary>
    public ComponentLibraryViewModel()
    {
        // Group template functionality removed - marked as optional in requirements
    }

    /// <summary>
    /// Loads all group templates from the library (placeholder).
    /// </summary>
    [RelayCommand]
    private void LoadGroups()
    {
        StatusText = "Group templates not implemented";
    }
}
