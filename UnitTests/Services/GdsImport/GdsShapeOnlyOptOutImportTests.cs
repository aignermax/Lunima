using System.Text.Json;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.GdsImport;
using CAP.Avalonia.ViewModels.Library;
using CAP_Core.Components.Connections;
using CAP_Core.ExternalPorts;
using CAP_Core.Grid;
using CAP_Core.LightCalculation;
using CAP_Core.Routing;
using CAP_Core.Tiles;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;
using CAP_DataAccess.Import.Gds.Interpretation;
using Shouldly;
using UnitTests.Import.Gds;
using Xunit;

namespace UnitTests.Services.GdsImport;

/// <summary>
/// Issue #1572: the review's "Shape only · no simulation" decision must really
/// opt a 2-optical-pin cell out of the assumed lossless pass-through — the
/// imported draft is geometry-only and the simulation refuses it. Without the
/// explicit decision the same cell keeps the pass-through default; a mapped
/// model always wins over the flag.
/// </summary>
public class GdsShapeOnlyOptOutImportTests
{
    [Fact]
    public async Task ShapeOnlyDecisionOptsTheTwoOpticalCellOutOfThePassThrough()
    {
        using var host = new GdsDesignScopeTestHost();
        var path = await WriteFixture(host);
        var analysis = await GdsImportService.AnalyzeAsync(path);
        var review = Review(analysis) with
        {
            Cells = [new() { CellName = "device", GeometryOnly = true }]
        };
        await new GdsImportService(host.Scope).ImportAsync(path, "TOP",
            preParsedLibrary: analysis.Library, sourceBytes: analysis.SourceBytes, interpretation: review);

        var draft = host.Scope.Sets.Single().Drafts.Single();
        draft.Pins.Count.ShouldBe(2);
        draft.ImportedModelStatus.ShouldBe("geometry-only",
            "the explicit shape-only decision beats the 2-optical-pin pass-through default");
        draft.SMatrix.ShouldBeNull();

        // The simulation refuses the opted-out cell instead of silently
        // simulating it as an ideal lossless pass-through.
        var template = PdkTemplateConverter.ConvertToTemplate(draft, "review", null);
        var component = ComponentTemplates.CreateFromTemplate(template, 0, 0);
        component.SimulationModelStatus.ShouldBe("geometry-only");
        var tileManager = new ComponentListTileManager();
        tileManager.AddComponent(component);
        var grid = GridManager.CreateForSimulation(tileManager,
            new WaveguideConnectionManager(new WaveguideRouter()), new PhysicalExternalPortManager());
        var refused = Should.Throw<InvalidDataException>(
            () => new SystemMatrixBuilder(grid).GetSystemSMatrix(1550));
        refused.Message.ShouldContain("geometry-only");
    }

    [Fact]
    public async Task DefaultUnmappedTwoOpticalCellKeepsThePassThrough()
    {
        using var host = new GdsDesignScopeTestHost();
        var path = await WriteFixture(host);
        var analysis = await GdsImportService.AnalyzeAsync(path);
        var review = Review(analysis) with { Cells = [new() { CellName = "device" }] };
        await new GdsImportService(host.Scope).ImportAsync(path, "TOP",
            preParsedLibrary: analysis.Library, sourceBytes: analysis.SourceBytes, interpretation: review);
        var draft = host.Scope.Sets.Single().Drafts.Single();
        draft.ImportedModelStatus.ShouldBeNull(
            "a missing flag keeps today's behaviour: the lossless pass-through default applies");
        draft.SMatrix.ShouldBeNull();
    }

    [Fact]
    public async Task MappedModelWinsOverTheShapeOnlyFlag()
    {
        using var host = new GdsDesignScopeTestHost();
        var path = await WriteFixture(host);
        var analysis = await GdsImportService.AnalyzeAsync(path);
        var template = ModelTemplate();
        var service = new GdsImportService(host.Scope, () => [template]);
        var review = Review(analysis) with
        {
            Cells = [new()
            {
                CellName = "device", GeometryOnly = true,
                ModelPdkName = "models", ModelComponentName = "device"
            }]
        };
        await service.ImportAsync(path, "TOP",
            preParsedLibrary: analysis.Library, sourceBytes: analysis.SourceBytes, interpretation: review);
        var draft = host.Scope.Sets.Single().Drafts.Single();
        draft.ImportedModelStatus.ShouldBe("mapped", "choosing a model retracts the shape-only decision");
        draft.SMatrix!.Connections.Single().Magnitude.ShouldBe(0.5);
    }

    [Fact]
    public void ShapeOnlyDecisionSurvivesSerializationAndDefaultsToAbsent()
    {
        var interpretation = new GdsImportInterpretation
        {
            TopCellName = "TOP",
            Cells = [new() { CellName = "device", GeometryOnly = true }]
        };
        var restored = JsonSerializer.Deserialize<GdsImportInterpretation>(
            JsonSerializer.Serialize(interpretation))!;
        restored.Cells.Single().GeometryOnly.ShouldBeTrue(
            "the decision survives save/load of the interpretation");

        var legacy = JsonSerializer.Deserialize<GdsCellInterpretation>("""{"CellName":"device"}""")!;
        legacy.GeometryOnly.ShouldBeFalse(
            "a missing value keeps today's behaviour (pass-through default)");
    }

    private static async Task<string> WriteFixture(GdsDesignScopeTestHost host)
    {
        Directory.CreateDirectory(host.GdsCacheDirectory);
        var path = Path.Combine(host.GdsCacheDirectory, "source.gds");
        await File.WriteAllBytesAsync(path, GdsTestWriter.Create().StandardPrologue()
            .BeginCell("TOP").SRef("device", 0, 0).EndCell()
            .BeginCell("device")
            .Boundary(1, 0, (0, 1750), (10000, 1750), (10000, 2250), (0, 2250), (0, 1750))
            .Boundary(111, 0, (0, 0), (10000, 0), (10000, 4000), (0, 4000), (0, 0))
            .Text(1, 10, "in", 0, 2000).Text(1, 10, "out", 10000, 2000).EndCell()
            .EndLibrary().ToArray());
        return path;
    }

    private static GdsImportInterpretation Review(GdsImportAnalysis analysis) => new()
    {
        TopCellName = "TOP",
        SourceSha256 = analysis.SourceSha256,
        Layers = [new(1, 0, CAP_DataAccess.Import.Gds.GdsLayerRole.OpticalCore),
            new(1, 10, CAP_DataAccess.Import.Gds.GdsLayerRole.PortLabel)],
    };

    private static ComponentTemplate ModelTemplate() => PdkTemplateConverter.ConvertToTemplate(new()
    {
        Name = "device",
        WidthMicrometers = 999,
        HeightMicrometers = 999,
        Pins = [new() { Name = "in" }, new() { Name = "out" }],
        SMatrix = new() { Connections = [new() { FromPin = "in", ToPin = "out", Magnitude = 0.5 }] }
    }, "models", null);
}
