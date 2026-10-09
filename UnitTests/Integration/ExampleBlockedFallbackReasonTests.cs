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

namespace UnitTests.Integration;

/// <summary>
/// Routing-honesty invariant over the quick-loading shipped examples (issue #1266):
/// every blocked-fallback wire carries a classification
/// (<see cref="RoutingFailureReason.EndpointBlocked"/> or
/// <see cref="RoutingFailureReason.Contention"/>), never
/// <see cref="RoutingFailureReason.None"/>. The design checks (#1236/#1241) use the
/// reason to tell the user WHY a wire is blocked — an unclassified wire gets no
/// actionable advice. The examples load with their cached routes as shipped (no re-bake),
/// so this pins the cached-route restore path on the real files the census flagged.
/// </summary>
public class ExampleBlockedFallbackReasonTests
{
    /// <summary>Quick-loading shipped examples (cached routes) the invariant runs over.</summary>
    public static TheoryData<string> QuickExamples { get; } = new()
    {
        "Mach-Zehnder Interferometer.lun",
        "Logic Gate Half Adder.lun",
        "Logic Gate MUX.lun",
        "Logic Gate SR-Latch.lun",
        "Logic Gate Bit.lun",
        "Logic Gate ALU 1-bit.lun",
        "Logic Gate Register 2-bit.lun",
    };

    [Theory]
    [MemberData(nameof(QuickExamples))]
    public async Task BlockedFallbackWire_AlwaysCarriesFailureReason(string exampleFileName)
    {
        var path = Path.Combine(ExampleDesignFilesTests.ExamplesDirectory(), exampleFileName);
        var canvas = new DesignCanvasViewModel();
        var fileOps = CreateFileOperations(canvas);

        (await fileOps.OpenDesignAsCopyAsync(path)).ShouldBeTrue($"'{exampleFileName}' must open from the Home screen");
        await fileOps.PostLoadRouting;

        foreach (var vm in canvas.Connections)
        {
            var connection = vm.Connection;
            if (!connection.IsBlockedFallback)
                continue;

            var label = $"{connection.StartPin.ParentComponent.Identifier}.{connection.StartPin.Name} → " +
                        $"{connection.EndPin.ParentComponent.Identifier}.{connection.EndPin.Name}";
            connection.FailureReason.ShouldNotBe(RoutingFailureReason.None,
                $"'{exampleFileName}': blocked wire {label} is unclassified — the design checks cannot say why it is blocked");
        }
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
