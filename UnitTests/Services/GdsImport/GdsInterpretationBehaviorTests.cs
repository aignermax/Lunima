using CAP.Avalonia.Services;
using CAP.Avalonia.Services.GdsImport;
using CAP.Avalonia.ViewModels.Library;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;
using CAP_DataAccess.Import.Gds;
using CAP_DataAccess.Import.Gds.Interpretation;
using Shouldly;
using Xunit;

namespace UnitTests.Services.GdsImport;

public class GdsInterpretationBehaviorTests
{
    [Fact]
    public async Task ReviewedPinsReplaceDetectionIncludingEmptyLists()
    {
        var library = Library();
        var review = new GdsImportInterpretation
        {
            TopCellName = "top",
            Cells = [new() { CellName = "device", Pins = [new() { Name = "fiber", XUm = 5, YUm = 1, PortType = "vertical" }] }]
        };
        var options = new GdsHierarchyImportOptions { Interpretation = review };
        var result = await GdsHierarchyImporter.ImportAsync(library, "top", options);
        result.ImportedCellDrafts.Single().Pins.Single().Name.ShouldBe("fiber");
        result.ImportedCellDrafts.Single().Pins.Single().PortType.ShouldBe("vertical");
        review.Cells[0] = review.Cells[0] with { Pins = [] };
        result = await GdsHierarchyImporter.ImportAsync(library, "top", options);
        result.ImportedCellDrafts.Single().Pins.ShouldBeEmpty();
    }

    [Fact]
    public void SplitPreservesSourceAndComposesChildPlacement()
    {
        var source = Library();
        var container = new GdsCell { Name = "container" };
        container.Elements.Add(new GdsReference { CellName = "device", Offset = new(3, 4) });
        source.Cells.Add("container", container);
        source.Cells["top"].Elements.Clear();
        source.Cells["top"].Elements.Add(new GdsReference { CellName = "container", Offset = new(10, 20), AngleDegrees = 90 });
        var expanded = GdsInterpretationExpander.Expand(source, new()
        {
            TopCellName = "top",
            Cells = [new() { CellName = "container", ExpandChildren = true }]
        });
        var reference = expanded.Cells["top"].Elements.OfType<GdsReference>().Single();
        reference.CellName.ShouldBe("device");
        reference.Offset.X.ShouldBe(6, 1e-9);
        reference.Offset.Y.ShouldBe(23, 1e-9);
        source.Cells["top"].Elements.OfType<GdsReference>().Single().CellName.ShouldBe("container");
    }

    [Fact]
    public void GeometryOnlyComponentsAreReportedUnresolvedAfterTemplateCreationAndClone()
    {
        var draft = new PdkComponentDraft
        {
            Name = "reviewed",
            ImportedModelStatus = "geometry-only",
            WidthMicrometers = 10,
            HeightMicrometers = 2,
            Pins = [new() { Name = "in" }, new() { Name = "out", OffsetXMicrometers = 10 }]
        };
        var template = PdkTemplateConverter.ConvertToTemplate(draft, "review", null);
        var component = ComponentTemplates.CreateFromTemplate(template, 0, 0);
        ImportedModelReadiness.FindUnresolved([component]).Count.ShouldBe(1);
        ImportedModelReadiness.FindUnresolved([(CAP_Core.Components.Core.Component)component.Clone()]).Count.ShouldBe(1);
    }

    [Fact]
    public async Task ReviewedRouteNamedCellIsNotDissolvedBehindTheUsersBack()
    {
        var library = Library();
        var cell = library.Cells["device"];
        library.Cells.Remove("device");
        cell.Name = "straight";
        library.Cells.Add("straight", cell);
        library.Cells["top"].Elements.Clear();
        library.Cells["top"].Elements.Add(new GdsReference { CellName = "straight" });
        var result = await GdsHierarchyImporter.ImportAsync(library, "top", new()
        {
            Interpretation = new() { TopCellName = "top", Cells = [new() { CellName = "straight", Pins = [] }] }
        });
        result.ImportedCellDrafts.Single().CellName.ShouldBe("straight");
    }

    private static GdsLibrary Library()
    {
        var library = new GdsLibrary();
        var top = new GdsCell { Name = "top" };
        top.Elements.Add(new GdsReference { CellName = "device" });
        var device = new GdsCell { Name = "device" };
        device.Elements.Add(new GdsPolygon { Layer = 1, Points = [new(0, 0), new(10, 0), new(10, 2), new(0, 2), new(0, 0)] });
        library.Cells.Add(top.Name, top);
        library.Cells.Add(device.Name, device);
        return library;
    }
}
