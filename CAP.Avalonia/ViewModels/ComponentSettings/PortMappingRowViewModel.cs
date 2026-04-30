using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CAP.Avalonia.ViewModels.ComponentSettings;

/// <summary>
/// One row in the port-mapping dialog: the imported port name (read-only)
/// plus a ComboBox-bound selection from the component's available pin names.
/// </summary>
public partial class PortMappingRowViewModel : ObservableObject
{
    /// <summary>The original name as it appeared in the imported file.</summary>
    public string ImportedName { get; }

    /// <summary>Component pin names the user can pick from for this row.</summary>
    public ObservableCollection<string> AvailablePins { get; }

    /// <summary>The pin currently selected as the target for <see cref="ImportedName"/>.</summary>
    [ObservableProperty]
    private string _selectedPin;

    public PortMappingRowViewModel(
        string importedName,
        ObservableCollection<string> availablePins,
        string selectedPin)
    {
        ImportedName = importedName;
        AvailablePins = availablePins;
        _selectedPin = selectedPin;
    }
}
