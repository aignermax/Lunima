using CAP.Avalonia.Services;
using CAP.Avalonia.Services.GdsImport;
using CAP.Avalonia.ViewModels.Library;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;
using CAP_DataAccess.Import.Gds;
using CAP_DataAccess.Import.Gds.Interpretation;
using Shouldly;
using UnitTests.Import.Gds;
using Xunit;

namespace UnitTests.Services.GdsImport;

public class GdsReviewedImportTests
{
    [Fact]
    public async Task ReviewedImportEmbedsSnapshotAndNeverSubstitutesKnownGeometry()
    {
        using var host = new GdsDesignScopeTestHost();
        var path = Path.Combine(host.GdsCacheDirectory, "source.gds");
        Directory.CreateDirectory(host.GdsCacheDirectory);
        var bytes = Fixture();
        await File.WriteAllBytesAsync(path, bytes);
        var analysis = await GdsImportService.AnalyzeAsync(path);
        await File.WriteAllBytesAsync(path, [0, 1, 2]);
        var template = ModelTemplate();
        var service = new GdsImportService(host.Scope, () => [template]);
        var result = await service.ImportAsync(path, "TOP", preParsedLibrary: analysis.Library,
            sourceBytes: analysis.SourceBytes, interpretation: Review(analysis));
        result.RegisteredComponents.Count.ShouldBe(1);
        host.Scope.Sets.Single().GdsBytes.ShouldBe(bytes);
        var draft = host.Scope.Sets.Single().Drafts.Single();
        draft.WidthMicrometers.ShouldBe(10);
        draft.RawCode.ShouldContain("nd.load_gds");
        draft.ImportedModelStatus.ShouldBe("geometry-only");
        draft.SMatrix.ShouldBeNull();
    }

    [Fact]
    public async Task ModelMappingCopiesOnlyModelAndRejectsUnmappedPortsBeforeRegistration()
    {
        using var host = new GdsDesignScopeTestHost();
        Directory.CreateDirectory(host.GdsCacheDirectory);
        var path = Path.Combine(host.GdsCacheDirectory, "source.gds");
        await File.WriteAllBytesAsync(path, Fixture());
        var analysis = await GdsImportService.AnalyzeAsync(path);
        var template = ModelTemplate();
        var service = new GdsImportService(host.Scope, () => [template]);
        var review = Review(analysis) with
        {
            Cells = [new() { CellName = "device", ModelPdkName = "models", ModelComponentName = "device" }]
        };
        await service.ImportAsync(path, "TOP", preParsedLibrary: analysis.Library,
            sourceBytes: analysis.SourceBytes, interpretation: review);
        var draft = host.Scope.Sets.Single().Drafts.Single();
        draft.ImportedModelStatus.ShouldBe("mapped");
        draft.SMatrix!.Connections.Single().Magnitude.ShouldBe(0.5);
        draft.WidthMicrometers.ShouldBe(10);
        draft.RawCode.ShouldContain("nd.load_gds");
        var invalid = review with
        {
            Cells = [review.Cells[0] with { ModelPinMap = new() { ["in"] = "missing" } }]
        };
        await Should.ThrowAsync<InvalidDataException>(() => service.ImportAsync(path, "TOP",
            preParsedLibrary: analysis.Library, sourceBytes: analysis.SourceBytes, interpretation: invalid));
        host.Scope.Sets.Count.ShouldBe(1);
    }

    [Fact]
    public async Task InvalidReviewedPortIsRejectedBeforeScopeRegistration()
    {
        using var host = new GdsDesignScopeTestHost();
        Directory.CreateDirectory(host.GdsCacheDirectory);
        var path = Path.Combine(host.GdsCacheDirectory, "source.gds");
        await File.WriteAllBytesAsync(path, Fixture());
        var analysis = await GdsImportService.AnalyzeAsync(path);
        var review = Review(analysis) with
        {
            Cells = [new() { CellName = "device", Pins = [new() { Name = "outside", XUm = 1000 }] }]
        };
        await Should.ThrowAsync<InvalidDataException>(() => new GdsImportService(host.Scope).ImportAsync(path,
            "TOP", preParsedLibrary: analysis.Library, sourceBytes: analysis.SourceBytes, interpretation: review));
        host.Scope.Sets.ShouldBeEmpty();
    }

    [Fact]
    public async Task SplitKeepsOriginalLayoutFrameWhenContainerAnnotationsExtendBounds()
    {
        using var host = new GdsDesignScopeTestHost();
        Directory.CreateDirectory(host.GdsCacheDirectory);
        var path = Path.Combine(host.GdsCacheDirectory, "source.gds");
        var bytes = GdsTestWriter.Create().StandardPrologue()
            .BeginCell("TOP").SRef("container", 0, 0).EndCell()
            .BeginCell("container").SRef("device", 0, 0).Text(99, 0, "annotation", 100000, 100000).EndCell()
            .BeginCell("device").Boundary(1, 0, (0, 0), (10000, 0), (10000, 4000), (0, 4000), (0, 0)).EndCell()
            .EndLibrary().ToArray();
        await File.WriteAllBytesAsync(path, bytes);
        var analysis = await GdsImportService.AnalyzeAsync(path);
        var review = Review(analysis) with
        {
            Cells = [new() { CellName = "container", ExpandChildren = true }, new() { CellName = "device", Pins = [] }]
        };
        var result = await new GdsImportService(host.Scope).ImportAsync(path, "TOP",
            preParsedLibrary: analysis.Library, sourceBytes: analysis.SourceBytes, interpretation: review);
        result.Instances.Single().PositionYUm.ShouldBe(96, 1e-9);
    }

    private static GdsImportInterpretation Review(GdsImportAnalysis analysis) => new()
    {
        TopCellName = "TOP",
        SourceSha256 = analysis.SourceSha256,
        Layers = [new(1, 0, CAP_DataAccess.Import.Gds.GdsLayerRole.OpticalCore),
            new(1, 10, CAP_DataAccess.Import.Gds.GdsLayerRole.PortLabel)],
    };

    private static byte[] Fixture() => GdsTestWriter.Create().StandardPrologue()
        .BeginCell("TOP").SRef("device", 0, 0).EndCell()
        .BeginCell("device")
        .Boundary(1, 0, (0, 1750), (10000, 1750), (10000, 2250), (0, 2250), (0, 1750))
        .Boundary(111, 0, (0, 0), (10000, 0), (10000, 4000), (0, 4000), (0, 0))
        .Text(1, 10, "in", 0, 2000).Text(1, 10, "out", 10000, 2000).EndCell()
        .EndLibrary().ToArray();

    private static ComponentTemplate ModelTemplate() => PdkTemplateConverter.ConvertToTemplate(new()
    {
        Name = "device",
        WidthMicrometers = 999,
        HeightMicrometers = 999,
        Pins = [new() { Name = "in" }, new() { Name = "out" }],
        SMatrix = new() { Connections = [new() { FromPin = "in", ToPin = "out", Magnitude = 0.5 }] }
    }, "models", null);
}
