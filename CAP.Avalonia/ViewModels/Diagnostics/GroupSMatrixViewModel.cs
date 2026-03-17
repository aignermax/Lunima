using System.Collections.ObjectModel;
using CAP_Core.Components.Core;
using CAP_Core.Grid;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CAP.Avalonia.ViewModels.Diagnostics;

/// <summary>
/// ViewModel for displaying ComponentGroup diagnostics.
/// Shows basic group information (child count, group name).
/// Groups no longer compute S-Matrices directly — all waveguides are recalculated live.
/// </summary>
public partial class GroupSMatrixViewModel : ObservableObject
{
    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private ObservableCollection<GroupMatrixInfo> _groupMatrices = new();

    [ObservableProperty]
    private bool _hasGroups;

    /// <summary>
    /// Updates the diagnostics display based on the current grid state.
    /// </summary>
    public void UpdateFromGrid(GridManager? grid)
    {
        GroupMatrices.Clear();

        if (grid == null)
        {
            StatusText = "No grid loaded";
            HasGroups = false;
            return;
        }

        var allComponents = grid.TileManager.GetAllComponents();
        var groups = allComponents.OfType<ComponentGroup>().ToList();

        if (groups.Count == 0)
        {
            StatusText = "No component groups in design";
            HasGroups = false;
            return;
        }

        HasGroups = true;

        foreach (var group in groups)
        {
            var info = new GroupMatrixInfo
            {
                GroupName = group.GroupName,
                ChildCount = group.ChildComponents.Count,
            };

            GroupMatrices.Add(info);
        }

        StatusText = $"{groups.Count} group(s) in design";
    }
}

/// <summary>
/// Information about a single ComponentGroup.
/// </summary>
public class GroupMatrixInfo
{
    /// <summary>
    /// The name of the group.
    /// </summary>
    public string GroupName { get; set; } = "";

    /// <summary>
    /// Number of child components in the group.
    /// </summary>
    public int ChildCount { get; set; }

    /// <summary>
    /// Whether this group has a computed S-Matrix (always false in simplified model).
    /// </summary>
    public bool HasSMatrix { get; set; }

    /// <summary>
    /// Number of wavelengths with S-Matrix data.
    /// </summary>
    public int WavelengthCount { get; set; }

    /// <summary>
    /// Display status text.
    /// </summary>
    public string Status => $"{ChildCount} children";
}
