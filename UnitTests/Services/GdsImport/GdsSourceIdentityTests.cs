using System.Text.Json;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services.GdsImport;
using CAP.Avalonia.Services.GdsImport.DesignScope;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;
using CAP_DataAccess.Import.Gds;
using CAP_DataAccess.Import.Gds.Interpretation;
using Shouldly;
using UnitTests.Import.Gds;
using Xunit;

namespace UnitTests.Services.GdsImport;

public class GdsSourceIdentityTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task RenamedParentExpandsOriginalSourceEvenWhenDisplayNameMatchesAnotherCell(
        bool collidingSourceCell, bool saveAndReload, bool legacyReviewedDraft)
    {
        var writer = GdsTestWriter.Create().StandardPrologue()
            .BeginCell("real-child")
            .Boundary(20, 3, (0, 0), (10000, 0), (10000, 4000), (0, 4000), (0, 0)).EndCell()
            .BeginCell("parent").SRef("real-child", 0, 0).EndCell();
        if (collidingSourceCell)
            writer.BeginCell("wrong-child")
                .Boundary(99, 0, (0, 0), (10000, 0), (10000, 4000), (0, 4000), (0, 0)).EndCell()
                .BeginCell("Friendly").SRef("wrong-child", 0, 0).EndCell();
        var bytes = writer.EndLibrary().ToArray();
        using var stream = new MemoryStream(bytes);
        var library = await new GdsReader().ReadAsync(stream);
        var imported = await GdsHierarchyImporter.ImportAsync(library, "parent",
            new GdsHierarchyImportOptions { Mode = GdsHierarchyImportMode.BlackBox });
        var draft = GdsCellDraftMapper.Map(imported.ImportedCellDrafts.Single(), GdsHierarchyImporter.GdsFileNameToken);
        draft.Name = "Friendly";
        if (legacyReviewedDraft) draft.ImportedSourceCellName = null;
        using var host = new GdsDesignScopeTestHost();
        host.Scope.AddAndRegister(new DesignScopedGdsSet
        {
            PdkName = "GDS Import - identity", GdsFileName = "identity.gds", GdsBytes = bytes, Drafts = [draft],
            Interpretation = new GdsImportInterpretation { TopCellName = "parent" }
        });
        if (saveAndReload)
        {
            var saved = host.Scope.CaptureForSave([host.Scope.Sets.Single().PdkName]);
            var restored = JsonSerializer.Deserialize<List<ImportedGdsComponentSetData>>(JsonSerializer.Serialize(saved))!;
            host.Scope.ClearDesignScope();
            host.Scope.RestoreDesignScope(restored);
        }
        var canvas = new DesignCanvasViewModel();
        var placement = PlaceComponentCommand.CreateExact(canvas, host.Templates.Single(), 100, 200);
        placement.Execute();
        var service = new GdsHierarchyExpansionService(host.Scope);
        if (legacyReviewedDraft)
        {
            var error = await Should.ThrowAsync<InvalidOperationException>(() => service.PrepareAsync(canvas, placement.CreatedViewModel!));
            error.Message.ShouldContain("re-import");
            canvas.Components.ShouldHaveSingleItem().ShouldBeSameAs(placement.CreatedViewModel);
            return;
        }
        var command = await service.PrepareAsync(canvas, placement.CreatedViewModel!);
        command.Execute();
        var layers = canvas.Components.SelectMany(c => c.Component.OutlinePolygons ?? []).Select(p => p.Layer).ToList();
        layers.ShouldContain(20);
        layers.ShouldNotContain(99);
        command.Undo();
        canvas.Components.ShouldHaveSingleItem().ShouldBeSameAs(placement.CreatedViewModel);
    }
}
