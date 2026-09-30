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
using Moq;
using Shouldly;
using Xunit;

namespace UnitTests.Integration;

/// <summary>
/// Authoring utility for the shipped rung-6 front-door example
/// <c>examples/Two Chiplets - Edge-Coupler Link.lun</c> (issue #1255): rebuilds the
/// #1214 journey composition (<see cref="ChipletEdgeCouplerJourneyDesign"/>), binds both
/// chiplets to the demo-PDK process through the placement-policy code path, and writes
/// the file through the real save command — byte-for-byte what the product serializer
/// produces, cached routes and process bindings included.
/// <para>
/// Gated by <c>CAP_AUTHOR_EXAMPLES=1</c> (unset, the test is a no-op) and
/// <c>Category=Slow</c>:
/// <c>SMART_TEST_EXCLUDE_CATEGORY= CAP_AUTHOR_EXAMPLES=1 python3 tools/smart_test.py TwoChipletsExampleAuthoring</c>
/// </para>
/// </summary>
[Trait("Category", "Slow")]
public class TwoChipletsExampleAuthoringTests
{
    /// <summary>Environment variable that arms the authoring pass ("1"); unset, the test is a no-op.</summary>
    private const string AuthorEnableVariable = "CAP_AUTHOR_EXAMPLES";

    /// <summary>File name of the example inside <c>examples/</c>.</summary>
    public const string ExampleFileName = "Two Chiplets - Edge-Coupler Link.lun";

    [Fact]
    public async Task Author_TwoChipletsEdgeCouplerLink_Example()
    {
        if (Environment.GetEnvironmentVariable(AuthorEnableVariable) != "1")
            return;

        var design = ChipletEdgeCouplerJourneyDesign.BuildComposed();
        var catalog = ProcessCatalog.BuildGroups(new[]
        {
            new PdkProcessEntry(design.DemoPdk.Name, ProcessFingerprintFactory.From(design.DemoPdk)),
        });

        // Bind both chiplets through the exact placement-policy code path the UI uses.
        var policy = new PlacementPolicyContext(
            () => ActiveProcessSelection.Playground(),
            () => Array.Empty<string>(),
            component => ComponentPdkSourceResolver.Resolve(component, design.Templates),
            getProcessCatalog: () => catalog);
        foreach (var chiplet in new[] { design.ChipletA, design.ChipletB })
        {
            var (isAllowed, blockReason, derivedBinding) =
                policy.CheckGroupPlacementAt(chiplet, targetGroup: null, chiplet.GroupName);
            isAllowed.ShouldBeTrue($"the placement policy must admit chiplet '{chiplet.GroupName}': {blockReason}");
            chiplet.ProcessBinding = derivedBinding;
        }

        // BuildComposed aligns chiplet B at the model level (MoveGroup); sync the group
        // VMs so the persisted CanvasX/Y match the model origin like a UI drag would.
        foreach (var chiplet in new[] { design.ChipletA, design.ChipletB })
        {
            var groupVm = design.Canvas.Components.Single(vm => vm.Component == chiplet);
            groupVm.X = chiplet.PhysicalX;
            groupVm.Y = chiplet.PhysicalY;
        }

        var examplePath = Path.Combine(ExampleDesignFilesTests.ExamplesDirectory(), ExampleFileName);
        var fileOps = new FileOperationsViewModel(
            design.Canvas,
            new CommandManager(),
            new SimpleNazcaExporter(),
            new SaxExporter(),
            new ObservableCollection<ComponentTemplate>(design.Templates),
            new GdsExportViewModel(new GdsExportService()),
            new PhotonTorchExportViewModel(new PhotonTorchExporter(), design.Canvas),
            null!,
            errorConsole: new ErrorConsoleService());
        fileOps.ProcessCatalogProvider = () => catalog;
        var dialog = new Mock<IFileDialogService>();
        dialog.Setup(f => f.ShowSaveFileDialogAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(examplePath);
        fileOps.FileDialogService = dialog.Object;

        await fileOps.SaveDesignAsCommand.ExecuteAsync(null);

        File.Exists(examplePath).ShouldBeTrue($"the example must be written to {examplePath}");
    }
}
