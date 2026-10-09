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
using CAP_Core.Export;
using Shouldly;
using Xunit;

namespace UnitTests.Integration;

/// <summary>
/// Issue #1380: after load + post-load routing, <see cref="DesignValidator"/> must report
/// every connection×connection waveguide crossing in a shipped example. The crossing
/// count is pinned per example: a count above zero is a real finding (a crossing without
/// a crossing component fails foundry overlap checks) — it may only change when the
/// layout or the router changes on purpose. Routes are never edited by this test.
/// </summary>
public class ExampleWaveguideCrossingTests
{
    private const string ManifestFileName = "examples.json";

    /// <summary>Pinned crossing-issue count per shipped example (defaults to 0).</summary>
    private static readonly Dictionary<string, int> KnownCrossingCounts = new()
    {
    };

    /// <summary>File names of every example listed in the manifest.</summary>
    public static TheoryData<string> ExampleFiles { get; } = LoadExampleFiles();

    [Theory]
    [MemberData(nameof(ExampleFiles))]
    public async Task Example_ReportsPinnedWaveguideCrossingCount(string exampleFileName)
    {
        var path = Path.Combine(ExampleDesignFilesTests.ExamplesDirectory(), exampleFileName);
        var canvas = new DesignCanvasViewModel();
        var errorConsole = new ErrorConsoleService();
        var fileOps = CreateFileOperations(canvas, errorConsole);

        (await fileOps.OpenDesignAsCopyAsync(path)).ShouldBeTrue($"'{exampleFileName}' must open from the Home screen");
        await fileOps.PostLoadRouting;

        var connections = canvas.Connections.Select(vm => vm.Connection).ToList();
        var issues = new DesignValidator().Validate(connections);
        var crossings = issues.Where(i => i.Type == DesignIssueType.WaveguideCrossing).ToList();

        crossings.Count.ShouldBe(KnownCrossingCounts.GetValueOrDefault(exampleFileName),
            $"'{exampleFileName}': crossing count differs from the pinned count — update " +
            $"KnownCrossingCounts only when the layout or the router changed on purpose. " +
            $"Crossings: {string.Join(" | ", crossings.Select(c => c.Description))}");
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
