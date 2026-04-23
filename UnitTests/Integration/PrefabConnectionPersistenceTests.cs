using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Export;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Panels;
using CAP_Core.Components.Core;
using Moq;
using Shouldly;
using System.Collections.ObjectModel;

namespace UnitTests.Integration;

/// <summary>
/// Regression tests for issue #358: Prefab connection persistence.
/// Waveguides should reconnect to the correct prefab instance after save/load.
/// Root cause: ConnectionData used integer indices that became wrong because
/// standalone components are loaded before groups, changing all indices.
/// Fix: ConnectionData now stores component identifiers (StartComponentId/EndComponentId)
/// for robust lookup instead of relying on positional indices.
/// </summary>
public class PrefabConnectionPersistenceTests
{
    private readonly ObservableCollection<ComponentTemplate> _library;

    /// <summary>Initializes the test suite with the full component library.</summary>
    public PrefabConnectionPersistenceTests()
    {
        _library = new ObservableCollection<ComponentTemplate>(TestPdkLoader.LoadAllTemplates());
    }

    /// <summary>
    /// Core regression test for issue #358.
    /// Creates 3 prefab instances + 1 standalone component where the groups are added
    /// to the canvas BEFORE the standalone (creating the index mismatch on load).
    /// Connects the standalone to instance1, saves, loads, and verifies
    /// the waveguide reconnects to instance1 — not instance2 or instance3.
    /// </summary>
    [Fact]
    public async Task PrefabInstances_SaveLoadRoundtrip_WaveguideReconnectsToCorrectInstance()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"prefab_conn_{Guid.NewGuid():N}.cappro");
        try
        {
            // ── Arrange: build save state ──────────────────────────────────────────
            var (saveVm, saveCanvas) = CreateSetup();

            // Create 3 distinct prefab instances (ComponentGroups)
            var instance1 = CreateGroupWithExternalPin("instance_1", "ext_pin");
            var instance2 = CreateGroupWithExternalPin("instance_2", "ext_pin");
            var instance3 = CreateGroupWithExternalPin("instance_3", "ext_pin");

            // Add groups BEFORE standalone — this is the critical ordering that triggers
            // the index-mismatch bug on load (standalones are loaded first during load).
            saveCanvas.AddComponent(instance1);
            saveCanvas.AddComponent(instance2);
            saveCanvas.AddComponent(instance3);

            // Create and add a standalone component
            var mmiTemplate = _library.First(t => t.Name == "1x2 MMI Splitter");
            var standalone = ComponentTemplates.CreateFromTemplate(mmiTemplate, 200, 200);
            standalone.Identifier = "standalone_mmi";
            saveCanvas.AddComponent(standalone, mmiTemplate.Name);

            // Canvas order at save time: [instance1(0), instance2(1), instance3(2), standalone(3)]
            // Connection: standalone → instance1.ExternalPin
            var externalPin1 = instance1.ExternalPins.First();
            var standalonePin = standalone.PhysicalPins.First();

            saveCanvas.ConnectPins(externalPin1.InternalPin, standalonePin);

            saveCanvas.Connections.Count.ShouldBe(1, "One connection must be created before save");

            // Record what we expect to survive the roundtrip
            var expectedGroupId = instance1.Identifier;
            var expectedPinName = externalPin1.Name;
            var expectedStandaloneId = standalone.Identifier;
            var expectedStandalonePinName = standalonePin.Name;

            // ── Act: save and load ─────────────────────────────────────────────────
            await SaveToFile(saveVm, tempFile);

            var (loadVm, loadCanvas) = CreateSetup();
            await LoadFromFile(loadVm, tempFile);

            // ── Assert ─────────────────────────────────────────────────────────────
            loadCanvas.Connections.Count.ShouldBe(1,
                "Exactly one waveguide connection must survive roundtrip");

            var loadedConn = loadCanvas.Connections[0].Connection;

            // Resolve which canvas component owns each pin
            var startComp = loadCanvas.Components
                .FirstOrDefault(c => c.Component.PhysicalPins.Contains(loadedConn.StartPin)
                    || (c.Component is ComponentGroup g
                        && g.ExternalPins.Any(ep => ep.InternalPin == loadedConn.StartPin)));

            var endComp = loadCanvas.Components
                .FirstOrDefault(c => c.Component.PhysicalPins.Contains(loadedConn.EndPin)
                    || (c.Component is ComponentGroup g
                        && g.ExternalPins.Any(ep => ep.InternalPin == loadedConn.EndPin)));

            startComp.ShouldNotBeNull("Start component must be found on canvas");
            endComp.ShouldNotBeNull("End component must be found on canvas");

            // One end must be group instance1, the other must be standalone
            var groupEnd = startComp!.Component is ComponentGroup ? startComp : endComp;
            var standaloneEnd = startComp.Component is ComponentGroup ? endComp : startComp;

            groupEnd.ShouldNotBeNull("One endpoint must be a ComponentGroup");
            standaloneEnd.ShouldNotBeNull("One endpoint must be the standalone component");

            groupEnd!.Component.Identifier.ShouldBe(expectedGroupId,
                "Waveguide must reconnect to INSTANCE 1 (the original), not instance 2 or 3!");

            standaloneEnd!.Component.Identifier.ShouldBe(expectedStandaloneId,
                "The standalone end must still reference the original standalone component");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    /// <summary>
    /// Verifies that a standalone-to-group connection with the standalone added AFTER groups
    /// still resolves correctly — the exact ordering that causes the index mismatch bug.
    /// Standalone is at index 3 during save but index 0 during load.
    /// </summary>
    [Fact]
    public async Task StandaloneAddedAfterGroups_SaveLoadRoundtrip_IndexMismatchDoesNotOccur()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"index_mismatch_{Guid.NewGuid():N}.cappro");
        try
        {
            var (saveVm, saveCanvas) = CreateSetup();

            // Add two groups first (indices 0, 1 at save time)
            var groupA = CreateGroupWithExternalPin("group_a_id", "pin_out");
            var groupB = CreateGroupWithExternalPin("group_b_id", "pin_out");
            saveCanvas.AddComponent(groupA);
            saveCanvas.AddComponent(groupB);

            // Add standalone last (index 2 at save time, will be index 0 after load!)
            var mmiTemplate = _library.First(t => t.Name == "1x2 MMI Splitter");
            var standalone = ComponentTemplates.CreateFromTemplate(mmiTemplate, 300, 100);
            standalone.Identifier = "the_standalone";
            saveCanvas.AddComponent(standalone, mmiTemplate.Name);

            // Connect: standalone → groupA
            saveCanvas.ConnectPins(
                groupA.ExternalPins[0].InternalPin,
                standalone.PhysicalPins.First());

            await SaveToFile(saveVm, tempFile);

            var (loadVm, loadCanvas) = CreateSetup();
            await LoadFromFile(loadVm, tempFile);

            loadCanvas.Connections.Count.ShouldBe(1, "Connection must survive roundtrip");

            var conn = loadCanvas.Connections[0].Connection;

            // Both ends must resolve to real, correct components
            var allPins = loadCanvas.Components.SelectMany(c =>
            {
                if (c.Component is ComponentGroup grp)
                    return grp.ExternalPins.Select(ep => ep.InternalPin).Cast<object>();
                return c.Component.PhysicalPins.Cast<object>();
            }).ToList();

            allPins.ShouldContain(conn.StartPin,
                "StartPin must belong to a loaded component");
            allPins.ShouldContain(conn.EndPin,
                "EndPin must belong to a loaded component");

            // The group end must be groupA — not groupB
            var groupEndComp = loadCanvas.Components.FirstOrDefault(c =>
                c.Component is ComponentGroup g
                && g.ExternalPins.Any(ep => ep.InternalPin == conn.StartPin || ep.InternalPin == conn.EndPin));

            groupEndComp.ShouldNotBeNull("One end must be a group");
            groupEndComp!.Component.Identifier.ShouldBe("group_a_id",
                "Connection must be to groupA, not groupB");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a ComponentGroup with a single child MMI component and one external pin.
    /// The group identifier is set to <paramref name="groupId"/> for stable test assertions.
    /// </summary>
    private ComponentGroup CreateGroupWithExternalPin(string groupId, string externalPinName)
    {
        var mmiTemplate = _library.First(t => t.Name == "1x2 MMI Splitter");
        var child = ComponentTemplates.CreateFromTemplate(mmiTemplate, 0, 0);
        child.Identifier = $"child_{groupId}";

        var group = new ComponentGroup($"Group_{groupId}")
        {
            PhysicalX = 0,
            PhysicalY = 0
        };
        group.AddChild(child);
        group.Identifier = groupId;

        var externalPin = new GroupPin
        {
            Name = externalPinName,
            InternalPin = child.PhysicalPins.First(),
            RelativeX = child.PhysicalPins.First().OffsetXMicrometers,
            RelativeY = child.PhysicalPins.First().OffsetYMicrometers,
            AngleDegrees = child.PhysicalPins.First().AngleDegrees
        };
        group.AddExternalPin(externalPin);
        return group;
    }

    private (FileOperationsViewModel vm, DesignCanvasViewModel canvas) CreateSetup()
    {
        var canvas = new DesignCanvasViewModel();
        var vm = new FileOperationsViewModel(
            canvas,
            new CommandManager(),
            new SimpleNazcaExporter(),
            new CAP_Core.Export.PicWaveExporter(),
            _library,
            new GdsExportViewModel(new CAP_Core.Export.GdsExportService()),
            new CAP.Avalonia.ViewModels.Export.PhotonTorchExportViewModel(new CAP_Core.Export.PhotonTorchExporter(), canvas));
        return (vm, canvas);
    }

    private async Task SaveToFile(FileOperationsViewModel vm, string filePath)
    {
        var dialog = new Mock<IFileDialogService>();
        dialog.Setup(f => f.ShowSaveFileDialogAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(filePath);
        vm.FileDialogService = dialog.Object;
        await vm.SaveDesignAsCommand.ExecuteAsync(null);
        File.Exists(filePath).ShouldBeTrue("Design file must be created during save");
    }

    private async Task LoadFromFile(FileOperationsViewModel vm, string filePath)
    {
        var dialog = new Mock<IFileDialogService>();
        dialog.Setup(f => f.ShowOpenFileDialogAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(filePath);
        vm.FileDialogService = dialog.Object;
        await vm.LoadDesignCommand.ExecuteAsync(null);
    }
}
