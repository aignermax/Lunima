using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Analysis.LogicAnalysis;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Export;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Panels;
using CAP_Core;
using CAP_Core.Components.Core;
using CAP_Core.Components.Creation;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Shouldly;
using UnitTests.Helpers;
using Xunit;

namespace UnitTests.Integration;

/// <summary>
/// Issue #1401 — the "chips from gates" journey Jonas clicks, driven end to end through the
/// real ViewModels over the production DI container (the <see cref="OpenEblDialogEndToEndTests"/>
/// pattern, no test-side adapters): the shipped NOT/NAND gate cell is read as a NOT through the
/// Truth Table panel (input A, output Y, bias, threshold 0.375) and given the signal name "IN";
/// the group is saved as a prefab through the Group Library's save command into a temp library
/// directory; a new session (fresh container + fresh <see cref="GroupLibraryManager"/> over the
/// same directory) places the prefab twice and wires instance 1's Y into instance 2's A on the
/// canvas; Logic Build then shows two gates with distinct hierarchical ids, one input toggle
/// named "IN", and the chain evaluates A → NOT → NOT = A. Saving the design as .lun and
/// reloading into a third session reproduces ids, names and evaluation exactly.
/// </summary>
public class PrefabGateCellJourneyTests : IDisposable
{
    private const string ExampleFileName = "Logic Gate NOT-NAND.lun";
    private const string TemplateName = "E2E Not Cell";
    private const string InputSignalName = "IN";
    private const double NotThreshold = 0.375;

    private readonly string _libraryDir =
        Path.Combine(Path.GetTempPath(), $"prefab-journey-{Guid.NewGuid():N}");
    private readonly List<string> _tempFiles = new();

    public void Dispose()
    {
        foreach (var path in _tempFiles.Where(File.Exists))
            File.Delete(path);
        if (Directory.Exists(_libraryDir))
            Directory.Delete(_libraryDir, true);
    }

    [Fact]
    public async Task NotGatePrefab_PlaceTwiceChainBuildSaveReload_ReproducesIdentity()
    {
        LocalizationService.Instance.SetLanguage(SupportedLanguage.English.Code);

        // ── Session 1: read the gate cell as NOT in the Truth Table panel, name the input,
        //    save the group as a prefab through the Group Library's save command. ──
        using var session1 = NewSession();
        var canvas1 = session1.GetRequiredService<DesignCanvasViewModel>();
        var loadOps = ComposeFileOperations(session1, canvas1);
        var examplePath = Path.Combine(ExampleDesignFilesTests.ExamplesDirectory(), ExampleFileName);
        (await loadOps.LoadDesignFromPathAsync(examplePath)).ShouldBeTrue(
            $"the shipped example '{ExampleFileName}' must load through the real load path");
        await loadOps.PostLoadRouting;

        var gateVm = canvas1.Components.Single(c => c.Component is ComponentGroup);
        canvas1.Selection.SelectSingle(gateVm);
        var truthTable = session1.GetRequiredService<TruthTableViewModel>();
        truthTable.ConfigureForSelection(gateVm, canvas1);
        truthTable.IsGroupSelected.ShouldBeTrue("the gate group must activate the Truth Table panel");
        foreach (var pin in truthTable.InputPins)
            pin.IsChecked = pin.PinName == "A"; // read as NOT: drop the NAND's B input
        truthTable.OutputPins.Single(p => p.PinName == "Y").IsChecked = true;
        truthTable.BiasPins.Single(p => p.PinName == "BIAS").IsChecked = true;
        truthTable.Threshold = NotThreshold;
        await truthTable.ExtractCommand.ExecuteAsync(null);
        truthTable.HasResult.ShouldBeTrue($"the NOT extraction must succeed: {truthTable.StatusText}");
        truthTable.InputPins.Single(p => p.PinName == "A").SignalName = InputSignalName;

        var gate = (ComponentGroup)gateVm.Component;
        gate.TruthTablePinAssignment.ShouldNotBeNull("the extraction persists the roles on the group");
        gate.TruthTablePinAssignment!.InputPinNames.ShouldBe(new[] { "A" });
        gate.TruthTablePinAssignment.InputSignalNames.ShouldBe(
            new Dictionary<string, string> { ["A"] = InputSignalName },
            "the panel's signal-name edit writes into the persisted assignment");

        var libraryVm = new ComponentLibraryViewModel(new GroupLibraryManager(_libraryDir));
        new SaveGroupToLibraryCommand(libraryVm, new GroupPreviewGenerator(), gate, TemplateName).Execute();
        libraryVm.UserGroups.Count.ShouldBe(1, "the prefab must land in the Group Library");

        // ── Session 2: fresh manager over the same directory — place the prefab twice and
        //    chain instance 1's output into instance 2's input on the canvas. ──
        using var session2 = NewSession();
        var canvas2 = session2.GetRequiredService<DesignCanvasViewModel>();
        var manager2 = new GroupLibraryManager(_libraryDir);
        manager2.LoadTemplates();
        var template = manager2.UserTemplates.Single(t => t.Name == TemplateName);
        template.TemplateGroup.ShouldNotBeNull("the reloaded prefab must carry its serialized group");

        var place1 = PlaceGroupTemplateCommand.TryCreate(canvas2, manager2, template, 0, 0);
        var place2 = PlaceGroupTemplateCommand.TryCreate(
            canvas2, manager2, template, template.WidthMicrometers * 3, 0);
        place1.ShouldNotBeNull("a first placement spot must exist");
        place2.ShouldNotBeNull("a second, non-overlapping placement spot must exist");
        place1!.Execute();
        place2!.Execute();
        canvas2.Components.Count.ShouldBe(2);

        var inst1 = place1.GroupToPlace;
        var inst2 = place2.GroupToPlace;
        inst1.GroupName.ShouldNotBe(inst2.GroupName, "each instance keeps a distinct gate identity");
        foreach (var inst in new[] { inst1, inst2 })
        {
            inst.TruthTablePinAssignment.ShouldNotBeNull(
                "the roles must survive prefab save → new session → place");
            inst.TruthTablePinAssignment!.InputSignalNames!["A"].ShouldBe(InputSignalName,
                "the signal name must survive prefab save → new session → place");
        }

        await canvas2.ConnectPinsAsync(Pin(inst1, "Y"), Pin(inst2, "A"));
        canvas2.Connections.Count.ShouldBe(1);

        var panel2 = await BuildLogicPanel(session2, canvas2);
        AssertNotChain(panel2, inst1.GroupName, inst2.GroupName);

        // ── Save the design as .lun; session 3 reloads it and builds again. ──
        var lunPath = NewTempFile(".lun");
        var saveOps = ComposeFileOperations(session2, canvas2);
        var dialog = new Mock<IFileDialogService>();
        dialog.Setup(d => d.ShowSaveFileDialogAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(lunPath);
        saveOps.FileDialogService = dialog.Object;
        await saveOps.SaveDesignAsCommand.ExecuteAsync(null);
        File.Exists(lunPath).ShouldBeTrue("the design must be written through the real save path");

        using var session3 = NewSession();
        var canvas3 = session3.GetRequiredService<DesignCanvasViewModel>();
        var reloadOps = ComposeFileOperations(session3, canvas3);
        (await reloadOps.LoadDesignFromPathAsync(lunPath)).ShouldBeTrue(
            "the saved design must reload through the real load path");
        await reloadOps.PostLoadRouting;
        canvas3.Components.Count.ShouldBe(2);
        canvas3.Connections.Count.ShouldBe(1);

        var panel3 = await BuildLogicPanel(session3, canvas3);
        AssertNotChain(panel3, inst1.GroupName, inst2.GroupName);
    }

    /// <summary>
    /// Pins the built network: one input toggle reading the persisted signal name, two output
    /// taps under distinct gate ids, and A → NOT → NOT = A for both input values.
    /// </summary>
    private static void AssertNotChain(LogicPanelViewModel panel, string driverId, string chainedId)
    {
        var input = panel.Inputs.ShouldHaveSingleItem(
            "only the driving instance's A pin is an unconnected network input");
        input.PinName.ShouldBe(InputSignalName, "the toggle shows the persisted signal name");

        panel.Outputs.Select(o => o.RawPinName.Split('.')[0]).Distinct().Count()
            .ShouldBe(2, "the two placed instances build as two gates with distinct ids");
        var drivingOutput = panel.Outputs.Single(o => o.RawPinName == $"{driverId}.Y");
        var chainedOutput = panel.Outputs.Single(o => o.RawPinName == $"{chainedId}.Y");

        input.IsOn = false;
        drivingOutput.IsOne.ShouldBeTrue("NOT(0) = 1 at the driving gate's tap");
        chainedOutput.IsOne.ShouldBeFalse("A → NOT → NOT = A for A = 0");
        input.IsOn = true;
        drivingOutput.IsOne.ShouldBeFalse("NOT(1) = 0 at the driving gate's tap");
        chainedOutput.IsOne.ShouldBeTrue("A → NOT → NOT = A for A = 1");
        input.IsOn = false; // leave the network at rest for the next caller
    }

    /// <summary>Builds the Logic panel over the canvas through the real Build command.</summary>
    private static async Task<LogicPanelViewModel> BuildLogicPanel(
        ServiceProvider session, DesignCanvasViewModel canvas)
    {
        var panel = session.GetRequiredService<LogicPanelViewModel>();
        panel.Configure(canvas);
        await panel.BuildNetworkCommand.ExecuteAsync(null);
        panel.HasNetwork.ShouldBeTrue($"Logic Build must succeed: {panel.StatusText}");
        return panel;
    }

    /// <summary>The instance's connectable external pin, surfaced by the placement's S-matrix sync.</summary>
    private static PhysicalPin Pin(ComponentGroup group, string name) =>
        group.PhysicalPins.Single(p => p.Name == name);

    /// <summary>A fresh production container — one per "session" of the journey.</summary>
    private ServiceProvider NewSession()
    {
        var preferencesPath = NewTempFile(".json");
        return ProductionContainerTestHelper.BuildWithTempPreferences(preferencesPath);
    }

    /// <summary>
    /// Composes <see cref="FileOperationsViewModel"/> from the container's singletons exactly as
    /// <c>MainViewModel</c>'s constructor does (the type is owned by MainViewModel, not registered).
    /// </summary>
    private static FileOperationsViewModel ComposeFileOperations(
        ServiceProvider session, DesignCanvasViewModel canvas)
    {
        var leftPanel = session.GetRequiredService<LeftPanelViewModel>();
        leftPanel.Initialize(); // production startup step: loads the bundled PDKs
        return new FileOperationsViewModel(
            canvas,
            session.GetRequiredService<CommandManager>(),
            session.GetRequiredService<SimpleNazcaExporter>(),
            session.GetRequiredService<CAP_Core.Export.SaxExporter>(),
            leftPanel.AllTemplates,
            session.GetRequiredService<GdsExportViewModel>(),
            session.GetRequiredService<PhotonTorchExportViewModel>(),
            session.GetRequiredService<VerilogAExportViewModel>(),
            session.GetRequiredService<ErrorConsoleService>(),
            session.GetRequiredService<UserSMatrixOverrideStore>());
    }

    private string NewTempFile(string extension)
    {
        var path = Path.Combine(Path.GetTempPath(), $"prefab-journey-{Guid.NewGuid():N}{extension}");
        _tempFiles.Add(path);
        return path;
    }
}
