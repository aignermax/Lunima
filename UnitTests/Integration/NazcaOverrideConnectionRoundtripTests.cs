using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Export;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Panels;
using CAP_DataAccess.Persistence.PIR;
using Moq;
using Shouldly;
using System.Collections.ObjectModel;

namespace UnitTests.Integration;

/// <summary>
/// Save/load roundtrip tests for waveguide connections to components with a
/// per-instance Nazca pin override (issue #561). On load, connections bind to the
/// template pins first; applying the stored override replaces the pin objects, so
/// connections must be re-anchored onto the same-named new pins (or dropped when
/// the pin name no longer exists) — otherwise the GDS export references pins the
/// override cell does not define (KeyError).
/// </summary>
public class NazcaOverrideConnectionRoundtripTests
{
    private readonly ObservableCollection<ComponentTemplate> _library;

    /// <summary>Initializes the test suite with the full component library.</summary>
    public NazcaOverrideConnectionRoundtripTests()
    {
        _library = new ObservableCollection<ComponentTemplate>(TestPdkLoader.LoadAllTemplates());
    }

    [Fact]
    public async Task Roundtrip_OverrideWithSamePinNames_ConnectionReanchoredToNewPins()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"roundtrip_ovr_same_{Guid.NewGuid():N}.cappro");
        try
        {
            var (saveVm, saveCanvas) = CreateSetup();
            var (comp1, comp2) = AddConnectedMmiPair(saveCanvas);

            // Override on comp2 keeps the pin names but moves them slightly.
            saveVm.StoredNazcaOverrides[comp2.Identifier] = BuildOverride(
                comp2, p => p.Name, offsetShift: 1.0);

            await SaveToFile(saveVm, tempFile);

            var (loadVm, loadCanvas) = CreateSetup();
            await LoadFromFile(loadVm, tempFile);

            loadCanvas.Connections.Count.ShouldBe(1,
                "connection to same-named override pins must survive the roundtrip");
            var loadedConn = loadCanvas.Connections[0].Connection;
            var loadedComp2 = loadCanvas.Components
                .First(c => c.Component.Identifier == comp2.Identifier).Component;
            loadedComp2.PhysicalPins.ShouldContain(loadedConn.EndPin,
                "EndPin must be re-anchored onto the component's CURRENT (override) pin object, " +
                "not the replaced template pin");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task Roundtrip_OverrideWithRenamedPins_ConnectionDropped()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"roundtrip_ovr_renamed_{Guid.NewGuid():N}.cappro");
        try
        {
            var (saveVm, saveCanvas) = CreateSetup();
            var (_, comp2) = AddConnectedMmiPair(saveCanvas);

            // Override on comp2 renames every pin — the connection target vanishes.
            saveVm.StoredNazcaOverrides[comp2.Identifier] = BuildOverride(
                comp2, p => "renamed_" + p.Name, offsetShift: 0.0);

            await SaveToFile(saveVm, tempFile);

            var (loadVm, loadCanvas) = CreateSetup();
            await LoadFromFile(loadVm, tempFile);

            loadCanvas.Connections.ShouldBeEmpty(
                "connection to a pin name that the override removed must be dropped on load — " +
                "a stale pin reference would make the GDS export emit an undefined pin (KeyError)");
            loadCanvas.ConnectionManager.Connections.ShouldBeEmpty(
                "dropped connection must also leave the connection manager");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Adds two 1x2 MMI splitters connected out1 → in and returns their components.</summary>
    private (CAP_Core.Components.Core.Component comp1, CAP_Core.Components.Core.Component comp2)
        AddConnectedMmiPair(DesignCanvasViewModel canvas)
    {
        var mmiTemplate = _library.First(t => t.Name == "1x2 MMI Splitter");

        var comp1 = ComponentTemplates.CreateFromTemplate(mmiTemplate, 0, 27.5);
        comp1.Identifier = "ovr_mmi_1";
        canvas.AddComponent(comp1, mmiTemplate.Name);

        var comp2 = ComponentTemplates.CreateFromTemplate(mmiTemplate, 200, 27.5);
        comp2.Identifier = "ovr_mmi_2";
        canvas.AddComponent(comp2, mmiTemplate.Name);

        canvas.ConnectPins(
            comp1.PhysicalPins.First(p => p.Name == "out1"),
            comp2.PhysicalPins.First(p => p.Name == "in"));

        return (comp1, comp2);
    }

    /// <summary>
    /// Builds a raw-code override whose <see cref="NazcaCodeOverride.OverridePins"/> mirror the
    /// component's current pins, with names mapped via <paramref name="nameSelector"/> and X
    /// offsets shifted by <paramref name="offsetShift"/> µm.
    /// </summary>
    private static NazcaCodeOverride BuildOverride(
        CAP_Core.Components.Core.Component component,
        Func<CAP_Core.Components.Core.PhysicalPin, string> nameSelector,
        double offsetShift)
    {
        return new NazcaCodeOverride
        {
            RawCode = "def component():\n    pass\n",
            OverridePins = component.PhysicalPins.Select(p => new OverridePinData
            {
                Name = nameSelector(p),
                OffsetXMicrometers = p.OffsetXMicrometers + offsetShift,
                OffsetYMicrometers = p.OffsetYMicrometers,
                AngleDegrees = p.AngleDegrees,
            }).ToList(),
        };
    }

    private (FileOperationsViewModel vm, DesignCanvasViewModel canvas) CreateSetup()
    {
        var canvas = new DesignCanvasViewModel();
        var vm = new FileOperationsViewModel(
            canvas,
            new CommandManager(),
            new SimpleNazcaExporter(),
            new CAP_Core.Export.SaxExporter(),
            _library,
            new GdsExportViewModel(new CAP_Core.Export.GdsExportService()),
            new PhotonTorchExportViewModel(new CAP_Core.Export.PhotonTorchExporter(), canvas),
            null!);
        return (vm, canvas);
    }

    private static async Task SaveToFile(FileOperationsViewModel vm, string filePath)
    {
        var dialog = new Mock<IFileDialogService>();
        dialog.Setup(f => f.ShowSaveFileDialogAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(filePath);
        vm.FileDialogService = dialog.Object;
        await vm.SaveDesignAsCommand.ExecuteAsync(null);
        File.Exists(filePath).ShouldBeTrue("Design file must be created during save");
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
