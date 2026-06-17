using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CAP.Avalonia.Services;
using CAP_DataAccess.Components.ComponentDraftMapper;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels;

/// <summary>
/// Lets the user view and adjust the fabrication process behind a PDK: its layer
/// stack, waveguide/metal cross-sections (widths + bend radii) and materials.
/// A process can be imported from a foundry PDK's CSV tables (e.g. HHI). This is
/// the first slice of issue #570 (process model + one-process-per-chip).
/// </summary>
public partial class ProcessManagementViewModel : ObservableObject
{
    private readonly IFileDialogService _fileDialog;
    private readonly PdkProcessCsvImporter _importer = new();

    /// <summary>Name of the loaded process.</summary>
    [ObservableProperty]
    private string _processName = string.Empty;

    /// <summary>Status / result message.</summary>
    [ObservableProperty]
    private string _statusText = "No process loaded. Import a PDK to see its fabrication process.";

    /// <summary>True once a process is loaded (drives the grids' visibility).</summary>
    [ObservableProperty]
    private bool _hasProcess;

    /// <summary>Editable layer stack.</summary>
    public ObservableCollection<ProcessLayer> Layers { get; } = new();

    /// <summary>Editable cross-sections (waveguide + metal).</summary>
    public ObservableCollection<ProcessXsection> Xsections { get; } = new();

    /// <summary>Editable materials.</summary>
    public ObservableCollection<ProcessMaterial> Materials { get; } = new();

    /// <summary>Initialises the ViewModel.</summary>
    public ProcessManagementViewModel(IFileDialogService fileDialog)
    {
        _fileDialog = fileDialog;
    }

    /// <summary>Populates the editable collections from a process definition.</summary>
    public void Load(ProcessDefinition process)
    {
        ProcessName = process.Name;
        Replace(Layers, process.Layers);
        Replace(Xsections, process.Xsections);
        Replace(Materials, process.Materials);
        HasProcess = true;
    }

    /// <summary>Builds a process definition from the current editable state.</summary>
    public ProcessDefinition ToProcess() => new()
    {
        Name = ProcessName,
        Layers = Layers.ToList(),
        Xsections = Xsections.ToList(),
        Materials = Materials.ToList(),
    };

    /// <summary>
    /// Imports a process from a foundry PDK directory. The user picks any CSV in the
    /// PDK folder (e.g. table_layers.csv); the importer reads the sibling tables.
    /// </summary>
    [RelayCommand]
    private async Task ImportFromPdk()
    {
        var path = await _fileDialog.ShowOpenFileDialogAsync(
            "Select a PDK table (e.g. table_layers.csv) in the PDK folder",
            "CSV Files|*.csv|All Files|*.*");
        if (path == null)
            return;

        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
            var process = _importer.Import(dir);
            Load(process);
            StatusText = $"Imported process '{process.Name}': {Layers.Count} layers, " +
                         $"{Xsections.Count} cross-sections, {Materials.Count} materials.";
        }
        catch (Exception ex)
        {
            StatusText = $"Import failed: {ex.Message}";
        }
    }

    private static void Replace<T>(ObservableCollection<T> target, System.Collections.Generic.IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items)
            target.Add(item);
    }
}
