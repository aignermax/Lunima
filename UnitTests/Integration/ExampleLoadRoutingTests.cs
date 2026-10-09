using System.Collections.ObjectModel;
using System.Text.Json;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Export;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Panels;
using CAP_Core;
using CAP_Core.Export;
using Shouldly;
using Xunit;

namespace UnitTests.Integration;

/// <summary>
/// Opening a shipped example must leave every connection with a real route, never the
/// pin-to-pin fallback line the canvas draws while a route is missing. The files ship with
/// cached routes; a file without them is routed by the loader's post-load pass, and a design
/// with more path-less wires than the loader's auto-routing limit keeps its fallback lines
/// and says so in the console. Wires the router cannot place today are pinned per file
/// (issue #1166) — the number may only shrink as the router improves.
/// </summary>
public class ExampleLoadRoutingTests
{
    private const string ManifestFileName = "examples.json";

    /// <summary>Blocked wires per example the router leaves behind on the shipped layouts.</summary>
    private static readonly Dictionary<string, int> KnownBlockedWires = new()
    {
        ["Logic Gate Zero Detect 4-bit.lun"] = 0,
        ["Logic Gate ALU 1-bit.lun"] = 0,
        ["Logic Gate Register 2-bit.lun"] = 0,
        ["Logic Gate Counter 2-bit.lun"] = 1,
        ["Logic Gate Full Adder.lun"] = 0,
        ["Logic Gate PC 2-bit.lun"] = 3,
        ["Logic Gate RAM 2x2.lun"] = 2,
        // Top-level inter-cell wires only; each word-cell instance freezes 1 more blocked
        // intra-cell wire (counted in ExampleFrozenBlockedPathTests.KnownBlockedPathCounts).
        ["Logic Gate RAM 2x4.lun"] = 2,
        // Top-level wires only; its word-cell instances carry no blocked wire.
        ["Logic Gate RAM 4x4.lun"] = 24,
        ["Logic Gate 4-Bit Adder.lun"] = 12,
        // Same blocked top-level wires as the 4-bit adder — the RAM block's wires
        // are frozen inside the RAM group (#1463), so the top level is the adder's alone.
        ["Logic Gate ALU + RAM.lun"] = 12,
        // Same again — the ACC register's intra-bit wires are frozen inside the
        // ACC group, so the top level is still the adder's alone.
        ["Logic Gate ALU + RAM + ACC.lun"] = 12,
    };

    /// <summary>File names of every example listed in the manifest.</summary>
    public static TheoryData<string> ExampleFiles { get; } = LoadExampleFiles();

    [Theory]
    [MemberData(nameof(ExampleFiles))]
    public async Task Example_OpensWithEveryConnectionRouted(string exampleFileName)
    {
        var path = Path.Combine(ExampleDesignFilesTests.ExamplesDirectory(), exampleFileName);
        var canvas = new DesignCanvasViewModel();
        var errorConsole = new ErrorConsoleService();
        var fileOps = CreateFileOperations(canvas, errorConsole);

        (await fileOps.OpenDesignAsCopyAsync(path)).ShouldBeTrue($"'{exampleFileName}' must open from the Home screen");
        await fileOps.PostLoadRouting;

        var unrouted = canvas.Connections.Count(c => c.Connection.RoutedPath == null);
        if (unrouted > FileOperationsViewModel.MaxConnectionsRoutedOnLoad)
        {
            AssertRoutingWasSkippedHonestly(canvas, errorConsole, exampleFileName);
            return;
        }
        AssertEveryWireRouted(canvas, exampleFileName);
        errorConsole.Entries.ShouldBeEmpty($"'{exampleFileName}' must load and route without console entries");
    }

    /// <summary>
    /// Every shipped example carries cached routes, so no manifest entry exercises the
    /// loader's large-design guard (<see cref="FileOperationsViewModel.MaxConnectionsRoutedOnLoad"/>)
    /// anymore. Stripping the 4-bit adder's cached geometry into a temp copy recreates the
    /// situation the guard exists for: hundreds of path-less wires must NOT start a
    /// minutes-long routing pass on open — the design shows its fallback lines and says why.
    /// </summary>
    [Fact]
    public async Task HugeDesignWithoutCachedRoutes_SkipsRoutingHonestly()
    {
        var source = Path.Combine(ExampleDesignFilesTests.ExamplesDirectory(), "Logic Gate 4-Bit Adder.lun");
        var tempPath = ExampleRouteBakeTests.StripRouteGeometryToTempFile(source);
        try
        {
            var canvas = new DesignCanvasViewModel();
            var errorConsole = new ErrorConsoleService();
            var fileOps = CreateFileOperations(canvas, errorConsole);

            (await fileOps.OpenDesignAsCopyAsync(tempPath)).ShouldBeTrue();
            await fileOps.PostLoadRouting;

            AssertRoutingWasSkippedHonestly(canvas, errorConsole, "Logic Gate 4-Bit Adder.lun (geometry stripped)");
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    private static void AssertEveryWireRouted(DesignCanvasViewModel canvas, string exampleFileName)
    {
        var blocked = 0;
        foreach (var vm in canvas.Connections)
        {
            var connection = vm.Connection;
            var route = connection.RoutedPath;
            var label = $"'{exampleFileName}': {connection.StartPin.ParentComponent.Identifier}.{connection.StartPin.Name} → " +
                        $"{connection.EndPin.ParentComponent.Identifier}.{connection.EndPin.Name}";
            route.ShouldNotBeNull($"{label} has no route after load — the canvas would draw a straight fallback line");
            if (route.IsBlockedFallback)
            {
                blocked++;
                continue;
            }
            route.IsValid.ShouldBeTrue($"{label} route is invalid");
            route.IsPlaceholderGeometry.ShouldBeFalse($"{label} route is only a placeholder");
        }
        blocked.ShouldBe(KnownBlockedWires.GetValueOrDefault(exampleFileName),
            $"'{exampleFileName}': blocked wires differ from the pinned count — update KnownBlockedWires only when the router got better or the layout changed on purpose");
    }

    private static void AssertRoutingWasSkippedHonestly(DesignCanvasViewModel canvas, ErrorConsoleService errorConsole, string exampleFileName)
    {
        canvas.Connections.ShouldAllBe(c => c.Connection.RoutedPath == null,
            $"'{exampleFileName}' exceeds the auto-routing limit, so the loader must not have started routing");
        errorConsole.Entries.Count.ShouldBe(1,
            $"'{exampleFileName}' must tell the user that routing was skipped for this large design");
    }

    private static FileOperationsViewModel CreateFileOperations(DesignCanvasViewModel canvas, ErrorConsoleService errorConsole) =>
        new(
            canvas,
            new CommandManager(),
            new SimpleNazcaExporter(),
            new SaxExporter(),
            new ObservableCollection<ComponentTemplate>(TestPdkLoader.LoadAllTemplates()),
            new GdsExportViewModel(new GdsExportService()),
            new PhotonTorchExportViewModel(new PhotonTorchExporter(), canvas),
            null!,
            errorConsole: errorConsole);

    private static TheoryData<string> LoadExampleFiles()
    {
        var data = new TheoryData<string>();
        var manifestPath = Path.Combine(ExampleDesignFilesTests.ExamplesDirectory(), ManifestFileName);
        using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
        foreach (var entry in doc.RootElement.GetProperty("examples").EnumerateArray())
            data.Add(entry.GetProperty("file").GetString()!);
        return data;
    }
}
