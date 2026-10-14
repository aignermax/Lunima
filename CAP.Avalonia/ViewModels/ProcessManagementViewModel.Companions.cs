using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CAP_Core.Routing.WaveguideCompanions;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels;

/// <summary>
/// The process editor's waveguide companion layers: cladding / trench / keep-out bands the
/// process draws along every routed waveguide (the non-core layers of the Nazca xsection).
/// They always belong to the process being edited, so every row is saved.
/// </summary>
public partial class ProcessManagementViewModel
{
    /// <summary>Default growth beyond each core edge of a newly added band, in µm.</summary>
    private const double NewCompanionOffsetMicrometers = 2.0;

    /// <summary>The roles offered by the Role column.</summary>
    public static IReadOnlyList<WaveguideCompanionRole> CompanionRoles { get; } =
        new[] { WaveguideCompanionRole.Cladding, WaveguideCompanionRole.Trench, WaveguideCompanionRole.KeepOut };

    /// <summary>Editable companion layers of the loaded process.</summary>
    public ObservableCollection<ProcessCompanionLayerRowViewModel> CompanionLayers { get; } = new();

    /// <summary>Adds a trench band 2 µm beyond each core edge on the next free layer number.</summary>
    [RelayCommand]
    private void AddCompanionLayer()
    {
        int nextLayer = Layers.Select(l => l.Layer)
            .Concat(CompanionLayers.Select(c => c.Layer))
            .DefaultIfEmpty(0).Max() + 1;
        CompanionLayers.Add(new ProcessCompanionLayerRowViewModel(new ProcessCompanionLayer
        {
            Name = "WG_TRENCH",
            Layer = nextLayer,
            Role = WaveguideCompanionRole.Trench,
            OffsetUm = NewCompanionOffsetMicrometers,
        }));
        HasProcess = true;
    }

    /// <summary>Removes one companion layer row.</summary>
    [RelayCommand]
    private void RemoveCompanionLayer(ProcessCompanionLayerRowViewModel? row)
    {
        if (row != null)
            CompanionLayers.Remove(row);
    }

    private void LoadCompanionLayers(ProcessDefinition process)
    {
        CompanionLayers.Clear();
        foreach (var layer in process.WaveguideCompanionLayers ?? new List<ProcessCompanionLayer>())
            CompanionLayers.Add(new ProcessCompanionLayerRowViewModel(layer));
    }

    /// <summary>The companion layers to persist; null when none, so older PDK files stay byte-identical.</summary>
    private List<ProcessCompanionLayer>? CompanionLayersForSave() =>
        CompanionLayers.Count == 0 ? null : CompanionLayers.Select(row => row.Model).ToList();
}
