using System.Collections.ObjectModel;
using System.Text.Json;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Export;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Panels;
using CAP_Core;
using CAP_Core.Analysis;
using CAP_Core.Components.Core;
using CAP_Core.Export;
using Shouldly;
using Xunit;

namespace UnitTests.Integration;

/// <summary>
/// Issue #1410: after load + post-load routing, <see cref="DesignValidator"/> must report
/// blocked fallback wires both at the top level AND frozen inside groups (recursively).
/// The count is pinned per shipped example: the RAM 2x4 ships three blocked top-level
/// inter-cell wires (pinned in <c>ExampleLoadRoutingTests.KnownBlockedWires</c>) plus the
/// blocked intra-cell wires frozen inside its two word-cell instances — DRC-lite used to
/// see only the top-level three. The shipped file currently has no flagged frozen paths
/// (the spike's template extraction dropped the flag — see the RAM 2x4 entry below), so
/// the pinned counts equal the top-level numbers until the examples are re-authored; any
/// count ABOVE the top-level number then is the finding this issue surfaces. Counts may
/// only change when the layout or the router changes on purpose. Routes are never edited.
/// </summary>
public class ExampleFrozenBlockedPathTests
{
    private const string ManifestFileName = "examples.json";

    /// <summary>
    /// Pinned total BlockedPath issue count per shipped example (top-level connections
    /// plus frozen group paths; defaults to the top-level-only count of
    /// <c>ExampleLoadRoutingTests.KnownBlockedWires</c> when no entry exists).
    /// </summary>
    private static readonly Dictionary<string, int> KnownBlockedPathCounts = new()
    {
        ["Logic Gate ALU 1-bit.lun"] = 1,
        ["Logic Gate Register 2-bit.lun"] = 1,
        ["Logic Gate Counter 2-bit.lun"] = 4,
        ["Logic Gate Full Adder.lun"] = 2,
        ["Logic Gate PC 2-bit.lun"] = 12,
        ["Logic Gate RAM 2x2.lun"] = 18,
        // Measured 3, not the expected 3 + 2×9 = 21: the shipped file carries 383 frozen
        // paths but ZERO IsBlockedFallback flags — the spike's template extraction
        // (RamWordCellTemplate) dropped the flag when freezing the word cell, so the
        // blocked intra-cell wires were baked unflagged. The plumbing (freeze → DTO →
        // save → load) is proven by FrozenBlockedPathCheckerTests.SerializerRoundTrip;
        // the extraction path now carries the flag for future re-authoring.
        ["Logic Gate RAM 2x4.lun"] = 3,
        // Top-level only (22 of them sealed by a component footprint); the word-cell
        // template it was baked from predates the flag-carrying extraction.
        ["Logic Gate RAM 4x4.lun"] = 43,
        ["Logic Gate 4-Bit Adder.lun"] = 90,
    };

    /// <summary>File names of every example listed in the manifest.</summary>
    public static TheoryData<string> ExampleFiles { get; } = LoadExampleFiles();

    [Theory]
    [MemberData(nameof(ExampleFiles))]
    public async Task Example_ReportsPinnedBlockedPathCount(string exampleFileName)
    {
        var path = Path.Combine(ExampleDesignFilesTests.ExamplesDirectory(), exampleFileName);
        var canvas = new DesignCanvasViewModel();
        var errorConsole = new ErrorConsoleService();
        var fileOps = CreateFileOperations(canvas, errorConsole);

        (await fileOps.OpenDesignAsCopyAsync(path)).ShouldBeTrue($"'{exampleFileName}' must open from the Home screen");
        await fileOps.PostLoadRouting;

        var connections = canvas.Connections.Select(vm => vm.Connection).ToList();
        var groups = canvas.Components.Select(c => c.Component).OfType<ComponentGroup>().ToList();
        var issues = new DesignValidator().Validate(connections, groups);
        var blocked = issues.Where(i => i.Type == DesignIssueType.BlockedPath).ToList();

        blocked.Count.ShouldBe(KnownBlockedPathCounts.GetValueOrDefault(exampleFileName),
            $"'{exampleFileName}': blocked-path count (top-level + frozen in groups) differs from the pinned count — update " +
            $"KnownBlockedPathCounts only when the layout or the router changed on purpose. " +
            $"Blocked: {string.Join(" | ", blocked.Select(b => b.Description))}");
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
