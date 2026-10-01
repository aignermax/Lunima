using System.Collections.ObjectModel;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Export;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Panels;
using CAP_Core;
using CAP_Core.Components.Process;
using CAP_Core.Export;
using CAP_DataAccess.Components.ComponentDraftMapper;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;
using Shouldly;
using UnitTests.Components;

namespace UnitTests.Integration;

/// <summary>
/// Loads the shipped rung-6 example <c>Two Chiplets - Edge-Coupler Link.lun</c>
/// (#1255) into a canvas through the real file-operations path the Home screen
/// uses, with the demo-PDK templates and process catalog the design's
/// bindings resolve against. Shared by the tour tests (#1288).
/// </summary>
internal static class TwoChipletsExampleLoader
{
    /// <summary>Opens the shipped example as an untitled copy; asserts success and flushed routing.</summary>
    public static async Task LoadAsync(DesignCanvasViewModel canvas, string exampleFileName)
    {
        var examplePath = Path.Combine(
            ExampleDesignFilesTests.ExamplesDirectory(), exampleFileName);
        var demoPdk = MultiProcessChipletJourneyDesign.LoadPdk(ChipletEdgeCouplerJourneyDesign.DemoPdkFile);
        var templates = new List<ComponentTemplate>
        {
            MultiProcessChipletJourneyDesign.TemplateFor(demoPdk, "Grating Coupler"),
            MultiProcessChipletJourneyDesign.TemplateFor(demoPdk, "Straight Waveguide 100µm"),
            MultiProcessChipletJourneyDesign.TemplateFor(demoPdk, "Edge Coupler"),
        };
        var catalog = ProcessCatalog.BuildGroups(new[]
        {
            new PdkProcessEntry(demoPdk.Name, ProcessFingerprintFactory.From(demoPdk)),
        });
        var fileOps = new FileOperationsViewModel(
            canvas, new CommandManager(), new SimpleNazcaExporter(), new SaxExporter(),
            new ObservableCollection<ComponentTemplate>(templates),
            new GdsExportViewModel(new GdsExportService()),
            new PhotonTorchExportViewModel(new PhotonTorchExporter(), canvas),
            null!, errorConsole: new ErrorConsoleService())
        {
            ProcessCatalogProvider = () => catalog,
        };

        (await fileOps.OpenDesignAsCopyAsync(examplePath)).ShouldBeTrue("the shipped example must open");
        await fileOps.PostLoadRouting;
    }
}
