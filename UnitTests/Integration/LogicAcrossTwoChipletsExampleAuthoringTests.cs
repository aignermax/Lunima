using System.Collections.ObjectModel;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Export;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Panels;
using CAP_Core;
using CAP_Core.Components.Core;
using CAP_Core.Components.Process;
using CAP_Core.Export;
using CAP_DataAccess.Components.ComponentDraftMapper;
using Moq;
using Shouldly;
using UnitTests.Components;
using Xunit;

namespace UnitTests.Integration;

/// <summary>
/// Authoring utility for the shipped rung-4×6 front-door example
/// <c>examples/Logic Gate Across Two Chiplets.lun</c> (issue #1455): rebuilds the
/// #1430 journey composition (<see cref="LogicAcrossChipletLinkJourneyDesign"/>),
/// persists friendly signal names on the two nested gate groups (input <c>A</c> on the
/// NOT, output <c>NOT_A</c> on the NOT, output <c>Y</c> on the AND — the AND's inputs
/// already ship named <c>A</c>/<c>B</c> by <c>Logic Gate AND-from-NAND.lun</c>), binds
/// both chiplets to the demo-PDK process through the placement-policy code path, and
/// writes the file through the real save command — byte-for-byte what the product
/// serializer produces, cached routes and process bindings included. Same pattern as
/// <see cref="TwoChipletsExampleAuthoringTests"/>.
/// <para>
/// Gated by <c>CAP_AUTHOR_EXAMPLES=1</c> (unset, the test is a no-op) and
/// <c>Category=Slow</c>:
/// <c>SMART_TEST_EXCLUDE_CATEGORY= CAP_AUTHOR_EXAMPLES=1 python3 tools/smart_test.py LogicAcrossTwoChipletsExampleAuthoring</c>
/// </para>
/// </summary>
[Trait("Category", "Slow")]
public class LogicAcrossTwoChipletsExampleAuthoringTests
{
    /// <summary>Environment variable that arms the authoring pass ("1"); unset, the test is a no-op.</summary>
    private const string AuthorEnableVariable = "CAP_AUTHOR_EXAMPLES";

    /// <summary>File name of the example inside <c>examples/</c>.</summary>
    public const string ExampleFileName = "Logic Gate Across Two Chiplets.lun";

    /// <summary>Network-input signal name shown by the Logic panel for the NOT's input pin.</summary>
    public const string NotInputSignalName = "A";

    /// <summary>Network-tap name for the NOT's output pin (the cross-chiplet intermediate).</summary>
    public const string NotOutputSignalName = "NOT_A";

    /// <summary>Network-tap name for the AND's output pin (the network's primary output).</summary>
    public const string AndOutputSignalName = "Y";

    [Fact]
    public async Task Author_LogicAcrossTwoChiplets_Example()
    {
        if (Environment.GetEnvironmentVariable(AuthorEnableVariable) != "1")
            return;

        var design = await LogicAcrossChipletLinkJourneyDesign.BuildComposedAsync();
        ApplySignalNames(design);

        var demoPdk = MultiProcessChipletJourneyDesign.LoadPdk(ChipletEdgeCouplerJourneyDesign.DemoPdkFile);
        var templates = TestPdkLoader.LoadAllTemplates();
        var catalog = ProcessCatalog.BuildGroups(new[]
        {
            new PdkProcessEntry(demoPdk.Name, ProcessFingerprintFactory.From(demoPdk)),
        });

        // Bind both chiplets through the exact placement-policy code path the UI uses.
        var policy = new PlacementPolicyContext(
            () => ActiveProcessSelection.Playground(),
            () => Array.Empty<string>(),
            component => ComponentPdkSourceResolver.Resolve(component, templates),
            getProcessCatalog: () => catalog);
        foreach (var chiplet in new[] { design.ChipletA, design.ChipletB })
        {
            var (isAllowed, blockReason, derivedBinding) =
                policy.CheckGroupPlacementAt(chiplet, targetGroup: null, chiplet.GroupName);
            isAllowed.ShouldBeTrue($"the placement policy must admit chiplet '{chiplet.GroupName}': {blockReason}");
            chiplet.ProcessBinding = derivedBinding;
        }

        // BuildComposedAsync aligns chiplet B at the model level (MoveGroup); sync the
        // group VMs so the persisted CanvasX/Y match the model origin like a UI drag would.
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
            new ObservableCollection<ComponentTemplate>(templates),
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

    /// <summary>
    /// Persists friendly signal names on the two nested gate groups so the Logic panel
    /// shows named toggles and named output taps instead of raw <c>&lt;gate&gt;.&lt;pin&gt;</c>
    /// ids: the NOT's input becomes network input <c>A</c> (its <c>Y</c> tap reads
    /// <c>NOT_A</c>), the AND's output tap reads <c>Y</c>. The AND's input signal names
    /// (<c>A</c>/<c>B</c>) ship with <c>Logic Gate AND-from-NAND.lun</c> already; its
    /// <c>A</c> input is driven across the link by the NOT, so it never becomes a
    /// network-level toggle — only <c>B</c> stays a user input.
    /// </summary>
    private static void ApplySignalNames(LogicAcrossChipletLinkJourneyDesign design)
    {
        var notGate = FindNestedGate(design.ChipletA, "NOT");
        notGate.TruthTablePinAssignment.ShouldNotBeNull(
            "the NOT gate must carry its persisted pin roles from the journey build");
        notGate.TruthTablePinAssignment!.InputSignalNames =
            new Dictionary<string, string> { ["A"] = NotInputSignalName };
        notGate.TruthTablePinAssignment!.OutputSignalNames =
            new Dictionary<string, string> { ["Y"] = NotOutputSignalName };

        var andGate = FindNestedGate(design.ChipletB, "AND");
        andGate.TruthTablePinAssignment.ShouldNotBeNull(
            "the shipped AND-from-NAND example must carry its persisted pin roles");
        andGate.TruthTablePinAssignment!.OutputSignalNames =
            new Dictionary<string, string> { ["Y"] = AndOutputSignalName };
    }

    private static ComponentGroup FindNestedGate(ComponentGroup chiplet, string gateName) =>
        chiplet.ChildComponents.OfType<ComponentGroup>().Single(g => g.GroupName == gateName);
}
