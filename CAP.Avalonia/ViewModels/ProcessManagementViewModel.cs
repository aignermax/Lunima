using System;
using System.Collections.Generic;
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
/// A process can be imported from any supported foundry format (openEPDA uPDK YAML,
/// Nazca CSV tables) or built by hand. First slice of issue #570.
/// </summary>
public partial class ProcessManagementViewModel : ObservableObject
{
    private readonly IFileDialogService _fileDialog;
    private readonly IReadOnlyList<IProcessImporter> _importers;

    /// <summary>Name of the loaded process.</summary>
    [ObservableProperty]
    private string _processName = string.Empty;

    /// <summary>Status / result message.</summary>
    [ObservableProperty]
    private string _statusText = "No process loaded. Import a PDK (uPDK YAML or Nazca CSV) or start a new one.";

    /// <summary>True once a process is loaded (drives the grids' visibility).</summary>
    [ObservableProperty]
    private bool _hasProcess;

    /// <summary>Editable layer stack.</summary>
    public ObservableCollection<ProcessLayer> Layers { get; } = new();

    /// <summary>Editable cross-sections (waveguide + metal).</summary>
    public ObservableCollection<ProcessXsection> Xsections { get; } = new();

    /// <summary>Editable materials.</summary>
    public ObservableCollection<ProcessMaterial> Materials { get; } = new();

    /// <summary>Initialises the ViewModel with the default importer set.</summary>
    public ProcessManagementViewModel(IFileDialogService fileDialog)
        : this(fileDialog, new IProcessImporter[]
        {
            new UpdkYamlProcessImporter(),
            new NazcaCsvProcessImporter(),
        })
    {
    }

    /// <summary>Initialises the ViewModel with a specific importer set (tests).</summary>
    public ProcessManagementViewModel(IFileDialogService fileDialog, IReadOnlyList<IProcessImporter> importers)
    {
        _fileDialog = fileDialog;
        _importers = importers;
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
    /// Imports a process from a PDK file. The format is auto-detected: an openEPDA
    /// uPDK YAML blueprint or a Nazca CSV table (the user picks any CSV in the folder).
    /// </summary>
    [RelayCommand]
    private async Task ImportFromPdk()
    {
        var path = await _fileDialog.ShowOpenFileDialogAsync(
            "Select a PDK file (uPDK *.yaml, or a Nazca table_*.csv in the PDK folder)",
            "PDK Files|*.yaml;*.yml;*.csv|All Files|*.*");
        if (path == null)
            return;

        var importer = _importers.FirstOrDefault(i => i.CanImport(path));
        if (importer == null)
        {
            StatusText = $"Unsupported PDK file: {Path.GetFileName(path)}";
            return;
        }

        try
        {
            var process = importer.Import(path);
            Load(process);
            StatusText = $"Imported '{process.Name}' via {importer.FormatName}: {Layers.Count} layers, " +
                         $"{Xsections.Count} cross-sections, {Materials.Count} materials.";
        }
        catch (Exception ex)
        {
            StatusText = $"Import failed ({importer.FormatName}): {ex.Message}";
        }
    }

    /// <summary>Starts a blank process seeded with public SOI material defaults.</summary>
    [RelayCommand]
    private void NewProcess()
    {
        Load(new ProcessDefinition
        {
            Name = "New process",
            Materials = ProcessMaterialDefaults.Soi(),
        });
        StatusText = "New process started with public SOI material defaults. Add layers and cross-sections.";
    }

    /// <summary>Adds an empty layer row for manual entry.</summary>
    [RelayCommand]
    private void AddLayer() => Layers.Add(new ProcessLayer { Name = "NEW_LAYER" });

    /// <summary>Adds an empty cross-section row for manual entry.</summary>
    [RelayCommand]
    private void AddXsection() => Xsections.Add(new ProcessXsection { Name = "new_xs" });

    /// <summary>Adds an empty material row for manual entry.</summary>
    [RelayCommand]
    private void AddMaterial() => Materials.Add(new ProcessMaterial { Name = "NewMaterial" });

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items)
            target.Add(item);
    }
}
