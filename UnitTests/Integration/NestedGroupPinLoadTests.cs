using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Export;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Panels;
using CAP_Core;
using CAP_Core.Components.Core;
using CAP_Core.Export;
using Moq;
using Shouldly;
using UnitTests.Helpers;
using Xunit;

namespace UnitTests.Integration;

/// <summary>
/// Issue #1438: a chiplet that exposes a pin of a NESTED group must survive a real
/// save/load. The restore used to look the internal pin up in the nested group's
/// <see cref="Component.PhysicalPins"/>, which only exist after an S-matrix/pin
/// sync — the load threw "Internal pin not found" and the canvas came back empty.
/// The serializer now materializes the nested group's pins from its restored
/// external pins, and a group that still fails to restore is skipped with a load
/// diagnostic instead of taking the whole design down.
/// </summary>
public class NestedGroupPinLoadTests
{
    private const string ChipletName = "chiplet1438";
    private const string GateName = "gate1438";
    private readonly ObservableCollection<ComponentTemplate> _library =
        new(TestPdkLoader.LoadAllTemplates());

    [Fact]
    public async Task ChipletExposingNestedGroupPin_SaveLoad_RestoresPinConnectionAndComponents()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"nested-pin-{Guid.NewGuid():N}.lun");
        try
        {
            var (saveVm, saveCanvas, _) = CreateSetup();
            var (chiplet, nestedPin) = BuildChipletWithNestedGatePin(saveCanvas);
            var topLevelCount = saveCanvas.Components.Count;
            var connectionCount = saveCanvas.Connections.Count;
            var exposedPinName = chiplet.ExternalPins.Single(p => p.InternalPin == nestedPin).Name;
            await SaveToFile(saveVm, tempFile);

            var (loadVm, loadCanvas, errorConsole) = CreateSetup();
            await LoadFromFile(loadVm, tempFile);

            loadCanvas.Components.Count.ShouldBe(topLevelCount,
                "the save/load round trip must restore every top-level component");
            loadCanvas.Connections.Count.ShouldBe(connectionCount,
                "the connection onto the chiplet's exposed nested-group pin must survive");
            var loadedChiplet = loadCanvas.Components
                .Select(vm => vm.Component).OfType<ComponentGroup>().Single();
            var restoredPin = loadedChiplet.ExternalPins.SingleOrDefault(p => p.Name == exposedPinName);
            restoredPin.ShouldNotBeNull("the exposed pin of the nested group must be restored");
            restoredPin!.InternalPin.ParentComponent.ShouldBeOfType<ComponentGroup>(
                "the restored pin must point at the nested group again");
            errorConsole.Entries.ShouldBeEmpty(
                "a clean round trip reports nothing: "
                + string.Join(" | ", errorConsole.Entries.Select(e => e.Message)));
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task CorruptedGroupPin_Load_ReportsDiagnosticAndKeepsRestOfDesign()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"corrupt-pin-{Guid.NewGuid():N}.lun");
        try
        {
            var (saveVm, saveCanvas, _) = CreateSetup();
            BuildChipletWithNestedGatePin(saveCanvas);
            await SaveToFile(saveVm, tempFile);

            // Corrupt one of the chiplet's exposed pin references.
            var root = JsonNode.Parse(await File.ReadAllTextAsync(tempFile))!;
            var chipletData = root["Groups"]!.AsArray()
                .Single(g => g!["GroupDto"]!["GroupName"]!.GetValue<string>() == ChipletName)!;
            chipletData["GroupDto"]!["ExternalPins"]![0]!["InternalPinName"] = "no_such_pin";
            await File.WriteAllTextAsync(tempFile, root.ToJsonString());

            var (loadVm, loadCanvas, errorConsole) = CreateSetup();
            string? status = null;
            loadVm.UpdateStatus = s => status = s;
            await LoadFromFile(loadVm, tempFile);

            loadCanvas.Components.ShouldNotBeEmpty(
                "a corrupted group must not empty the canvas — the standalone component stays");
            status.ShouldNotBeNull("the load must surface the failure instead of staying silent");
            status.ShouldContain("could not be restored");
            errorConsole.Entries.ShouldContain(
                e => e.Message.Contains(ChipletName),
                "the error console names the group that failed to restore");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a chiplet that nests a gate group and exposes one of the gate's pins,
    /// plus a standalone source wired to that exposed pin. Returns the chiplet and
    /// the physical pin behind the exposed nested-group pin.
    /// </summary>
    private (ComponentGroup Chiplet, PhysicalPin NestedPin) BuildChipletWithNestedGatePin(
        DesignCanvasViewModel canvas)
    {
        var template = _library.First(t => t.Name == "1x2 MMI Splitter");
        var innerA = Place(template, canvas, "inner_a", 0, 0);
        var innerB = Place(template, canvas, "inner_b", 0, 300);
        var sibling = Place(template, canvas, "sibling", 400, 0);
        var source = Place(template, canvas, "source", 900, 0);

        var gate = Group(canvas, GateName, innerA, innerB);
        // Production materializes the group's pins via the S-matrix sync; on load
        // that sync has not run, which is exactly the gap the fix covers.
        gate.SyncPhysicalPinsFromExternalPins();
        var chiplet = Group(canvas, ChipletName, gate, sibling);

        var exposed = chiplet.ExternalPins.First(p => p.InternalPin?.ParentComponent == gate);
        var sourcePin = source.PhysicalPins.First(p => p.Name == "out1");
        var connection = canvas.ConnectPins(sourcePin, exposed.InternalPin);
        connection.ShouldNotBeNull("the wire onto the exposed nested-group pin must be created");

        return (chiplet, exposed.InternalPin!);
    }

    private Component Place(
        ComponentTemplate template, DesignCanvasViewModel canvas, string identifier, double x, double y)
    {
        var component = ComponentTemplates.CreateFromTemplate(template, x, y);
        component.Identifier = identifier;
        canvas.AddComponent(component, template.Name);
        return component;
    }

    private static ComponentGroup Group(DesignCanvasViewModel canvas, string name, params Component[] children)
    {
        var command = new CreateGroupCommand(
            canvas,
            children.Select(c => canvas.Components.Single(vm => vm.Component == c)).ToList(),
            name);
        command.Execute();
        return command.CreatedGroup.ShouldNotBeNull($"grouping '{name}' must succeed");
    }

    private (FileOperationsViewModel Vm, DesignCanvasViewModel Canvas, ErrorConsoleService ErrorConsole)
        CreateSetup()
    {
        var canvas = new DesignCanvasViewModel();
        var errorConsole = new ErrorConsoleService();
        var vm = new FileOperationsViewModel(
            canvas,
            new CommandManager(),
            new SimpleNazcaExporter(),
            new SaxExporter(),
            _library,
            new GdsExportViewModel(new GdsExportService()),
            new PhotonTorchExportViewModel(new PhotonTorchExporter(), canvas),
            null!,
            errorConsole: errorConsole);
        return (vm, canvas, errorConsole);
    }

    private static async Task SaveToFile(FileOperationsViewModel vm, string filePath)
    {
        var dialog = new Mock<IFileDialogService>();
        dialog.Setup(f => f.ShowSaveFileDialogAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(filePath);
        vm.FileDialogService = dialog.Object;
        await vm.SaveDesignAsCommand.ExecuteAsync(null);
        File.Exists(filePath).ShouldBeTrue("the real save path must write the temp .lun");
    }

    private static async Task LoadFromFile(FileOperationsViewModel vm, string filePath)
    {
        var dialog = new Mock<IFileDialogService>();
        dialog.Setup(f => f.ShowOpenFileDialogAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(filePath);
        vm.FileDialogService = dialog.Object;
        await vm.LoadDesignCommand.ExecuteAsync(null);
    }
}
