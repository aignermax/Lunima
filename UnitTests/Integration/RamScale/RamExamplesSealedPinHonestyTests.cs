using System.Collections.ObjectModel;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Export;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Panels;
using CAP_Core;
using CAP_Core.Export;
using CAP_Core.Routing;
using Shouldly;
using Xunit;

namespace UnitTests.Integration.RamScale;

/// <summary>
/// Issue #1419: the shipped RAM examples must not report "pin sealed in by a component
/// footprint" findings. The 22 verdicts on RAM 4x4 (2 on RAM 2x4) were never real:
/// the .lun format persists the blocked flag but not the reason, so the loader classified
/// each restored blocked wire against the pathfinding grid it happened to hold at that
/// moment — the small startup grid (-100..5100 µm), whose out-of-range pins clamp into
/// its blocked border column and read as sealed by a foreign body. Restoring the chip
/// size before the connections makes the classification honest: on the full-chip grid no
/// RAM pin is footprint-sealed (every blocked wire is ordinary contention). This test
/// pins the honest verdict so the artifact cannot return.
/// </summary>
[Trait("Category", "Slow")]
public class RamExamplesSealedPinHonestyTests
{
    [Theory]
    [InlineData("Logic Gate RAM 2x4.lun")]
    [InlineData("Logic Gate RAM 4x4.lun")]
    public async Task ShippedRam_ReportsNoFootprintSealedPins(string exampleFileName)
    {
        var path = Path.Combine(ExampleDesignFilesTests.ExamplesDirectory(), exampleFileName);
        var canvas = new DesignCanvasViewModel();
        var fileOps = CreateFileOperations(canvas);

        (await fileOps.OpenDesignAsCopyAsync(path)).ShouldBeTrue($"'{exampleFileName}' must open from the Home screen");
        await fileOps.PostLoadRouting;

        var sealedFindings = canvas.Connections
            .Select(vm => vm.Connection)
            .Where(c => c.IsBlockedFallback && c.FailureReason == RoutingFailureReason.EndpointBlocked)
            .Select(c => $"{c.StartPin.ParentComponent.Identifier}.{c.StartPin.Name} → {c.EndPin.ParentComponent.Identifier}.{c.EndPin.Name}")
            .ToList();
        sealedFindings.ShouldBeEmpty(
            $"'{exampleFileName}' must not report footprint-sealed pins — those verdicts were a load-time " +
            $"classification artifact of the small startup grid (issue #1419), not real seals");
    }

    private static FileOperationsViewModel CreateFileOperations(DesignCanvasViewModel canvas) =>
        new(
            canvas,
            new CommandManager(),
            new SimpleNazcaExporter(),
            new SaxExporter(),
            new ObservableCollection<ComponentTemplate>(TestPdkLoader.LoadAllTemplates()),
            new GdsExportViewModel(new GdsExportService()),
            new PhotonTorchExportViewModel(new PhotonTorchExporter(), canvas),
            null!,
            errorConsole: new ErrorConsoleService());
}
