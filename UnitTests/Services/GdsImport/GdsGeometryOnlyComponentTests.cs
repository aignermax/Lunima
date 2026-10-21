using CAP.Avalonia.Services.GdsImport;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Library;
using CAP_Core.Components.Core;
using CAP_DataAccess.Components.ComponentDraftMapper;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;
using Shouldly;
using UnitTests.Import.Gds;
using Xunit;

namespace UnitTests.Services.GdsImport;

/// <summary>
/// Geometry-only (0-pin) GDS imports: foundry marker/pad/logo cells carry no pin
/// labels at all. Their drafts register as pin-less components (the PDK loader
/// accepts pin-less components that carry outlines), place with their outlines,
/// and still join the import group so frozen route polygons attach. Harness
/// mirrors <see cref="GdsImportServiceTests"/>.
/// </summary>
public class GdsGeometryOnlyComponentTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "lunima-gds-geomonly-" + Guid.NewGuid().ToString("N"));
    private readonly GdsDesignScopeTestHost _host = new();

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
        _host.Dispose();
    }

    // ── Fixtures ─────────────────────────────────────────────────────────────

    /// <summary>
    /// TOP with one pinned waveguide (wgA) and one pin-less geometry cell
    /// ("logo": only an extent rectangle on (111,0) — no labels, no waveguide
    /// geometry, so no label pins and no heuristic pins), plus a top-cell route
    /// stub on (1,0) that touches no pin and must survive as a frozen path.
    /// </summary>
    private static byte[] MixedLibrary() => GdsTestWriter.Create()
        .StandardPrologue()
        .BeginCell("TOP")
            .SRef("wgA", 0, 0)
            .SRef("logo", 20000, 0)
            .Boundary(1, 0, (10000, 250), (12000, 250), (12000, 750), (10000, 750), (10000, 250))
        .EndCell()
        .WaveguideCell("wgA")
        .BeginCell("logo")
            .Boundary(111, 0, (0, 0), (5000, 0), (5000, 5000), (0, 5000), (0, 0))
        .EndCell()
        .EndLibrary()
        .ToArray();

    /// <summary>TOP whose only instance is pin-less (the logo cell alone).</summary>
    private static byte[] PinlessOnlyLibrary() => GdsTestWriter.Create()
        .StandardPrologue()
        .BeginCell("TOP")
            .SRef("logo", 0, 0)
        .EndCell()
        .BeginCell("logo")
            .Boundary(111, 0, (0, 0), (5000, 0), (5000, 5000), (0, 5000), (0, 0))
        .EndCell()
        .EndLibrary()
        .ToArray();

    private string WriteGds(byte[] content, string fileName = "circuit.gds")
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, fileName);
        File.WriteAllBytes(path, content);
        return path;
    }

    // ── Service level: register, warn, store in the design scope ─────────────

    [Fact]
    public async Task ExecuteAsync_UnlabelledPad_RemainsRoutingObstacle()
    {
        var outcome = await _host.CreateService().ImportAsync(
            WriteGds(PinlessOnlyLibrary()), "TOP", null, null);
        var canvas = new DesignCanvasViewModel();
        canvas.InitializeAStarRouting(-20, -20, 100, 100);
        await new GdsPlacementExecutor(canvas, null, () => _host.Templates.ToList())
            .ExecuteAsync(GdsPlacementPlan.FromOutcome(outcome), groupImport: false);

        var pad = canvas.Components.ShouldHaveSingleItem().Component;
        pad.PhysicalPins.ShouldBeEmpty();
        pad.IsRoutingObstacle.ShouldBeTrue("missing port metadata does not make physical geometry nonblocking");
        var grid = canvas.Router.PathfindingGrid.ShouldNotBeNull();
        var (x, y) = grid.PhysicalToGrid(pad.PhysicalX + pad.WidthMicrometers / 2,
            pad.PhysicalY + pad.HeightMicrometers / 2);
        grid.IsBlockedByComponent(x, y).ShouldBeTrue();
    }

    [Fact]
    public async Task ImportAsync_GeometryOnlyCell_RegistersWithOutlinesWarningAndDesignScopedDraft()
    {
        var service = _host.CreateService();

        var outcome = await service.ImportAsync(WriteGds(PinlessOnlyLibrary()), "TOP", null, null);

        // Registered, not skipped: the geometry-only warning names the cell.
        outcome.RegisteredComponents.ShouldContain(r => r.CellDraftName == "logo");
        var warning = outcome.Warnings.Where(w => w.Contains("'logo'")).ShouldHaveSingleItem();
        warning.ShouldContain("geometry-only");
        outcome.Warnings.ShouldNotContain(w => w.Contains("was not registered"));

        // The registered template carries outlines and no pins; the raw-code
        // snippet still points at the materialized cache .gds (component body source).
        var template = _host.Templates.ShouldHaveSingleItem();
        template.PinDefinitions.ShouldBeEmpty();
        template.OutlinePolygons.ShouldNotBeNull().ShouldNotBeEmpty();

        // The design-scoped set keeps the pin-less draft with its outlines.
        var set = _host.Scope.Sets.ShouldHaveSingleItem();
        var component = set.Drafts.ShouldHaveSingleItem();
        component.Pins.ShouldBeEmpty();
        component.OutlinePolygons.ShouldNotBeNull().ShouldNotBeEmpty();
    }

    // ── Placement: mixed pinned + pin-less, group, frozen paths ─────────────

    [Fact]
    public async Task ExecuteAsync_MixedPinnedAndPinless_BothPlaced_GroupHoldsFrozenRoutePath()
    {
        var service = _host.CreateService();
        var outcome = await service.ImportAsync(WriteGds(MixedLibrary()), "TOP", null, null);
        outcome.RegisteredComponents.Count.ShouldBe(2);
        outcome.Warnings.ShouldContain(w => w.Contains("'logo'") && w.Contains("geometry-only"));

        var canvas = new DesignCanvasViewModel();
        var report = await new GdsPlacementExecutor(canvas, null, () => _host.Templates.ToList())
            .ExecuteAsync(GdsPlacementPlan.FromOutcome(outcome));

        report.PlacedCount.ShouldBe(2, "the pin-less instance places like any other");
        report.SkippedPlacements.ShouldBeEmpty();
        report.GroupCreated.ShouldBeTrue("the group is created even though one child has no pins");

        var group = canvas.Components.ShouldHaveSingleItem().Component.ShouldBeOfType<ComponentGroup>();
        group.ChildComponents.Count.ShouldBe(2);
        var logo = group.ChildComponents.Single(c => c.PhysicalX == 20);
        logo.PhysicalPins.ShouldBeEmpty("geometry-only component: outlines, no pins");
        logo.OutlinePolygons.ShouldNotBeNull().ShouldNotBeEmpty();

        // The top-cell route stub (touches no pins) attaches to the group as a
        // frozen pin-less path — it must not vanish with the pin-less child.
        var frozen = group.InternalPaths.ShouldHaveSingleItem();
        frozen.StartPin.ShouldBeNull();
        frozen.EndPin.ShouldBeNull();
        report.FrozenRoutePathCount.ShouldBe(1);
    }

    [Fact]
    public async Task ExecuteAsync_AllInstancesPinless_GroupStillCreated_FrozenPathsAttach()
    {
        // Two instances of the same pin-less cell plus a top-cell route stub:
        // the group must be created even though NOT ONE placed component has a
        // pin — the frozen route polygons need it as their carrier.
        var library = GdsTestWriter.Create()
            .StandardPrologue()
            .BeginCell("TOP")
                .SRef("logo", 0, 0)
                .SRef("logo", 20000, 0)
                .Boundary(1, 0, (10000, 250), (12000, 250), (12000, 750), (10000, 750), (10000, 250))
            .EndCell()
            .BeginCell("logo")
                .Boundary(111, 0, (0, 0), (5000, 0), (5000, 5000), (0, 5000), (0, 0))
            .EndCell()
            .EndLibrary()
            .ToArray();
        var service = _host.CreateService();
        var outcome = await service.ImportAsync(WriteGds(library), "TOP", null, null);
        outcome.RegisteredComponents.ShouldContain(r => r.CellDraftName == "logo");

        var canvas = new DesignCanvasViewModel();
        var report = await new GdsPlacementExecutor(canvas, null, () => _host.Templates.ToList())
            .ExecuteAsync(GdsPlacementPlan.FromOutcome(outcome));

        report.PlacedCount.ShouldBe(2);
        report.GroupCreated.ShouldBeTrue("≥2 placed components group even when all are pin-less");
        var group = canvas.Components.ShouldHaveSingleItem().Component.ShouldBeOfType<ComponentGroup>();
        group.ChildComponents.Count.ShouldBe(2);
        group.ChildComponents.ShouldAllBe(c => c.PhysicalPins.Count == 0);
        var frozen = group.InternalPaths.ShouldHaveSingleItem(
            "the frozen route stub attaches to the all-pin-less group");
        frozen.StartPin.ShouldBeNull();
    }

    // ── Round-trip: import → persist → reload (issue #829) ──────────────────

    [Fact]
    public async Task ImportAsync_GeometryOnlyCell_PersistedDraft_ReloadsThroughPdkLoader()
    {
        // The contract that broke in the field: an imported 0-pin component must
        // survive being written to a PDK JSON and loaded back — otherwise the
        // whole PDK (including its valid components) is skipped on reload.
        var service = _host.CreateService();
        await service.ImportAsync(WriteGds(PinlessOnlyLibrary()), "TOP", null, null);
        var drafts = _host.Scope.Sets.ShouldHaveSingleItem().Drafts;

        var pdkDraft = new PdkDraft { Name = "GDS Import - circuit", Components = drafts.ToList() };
        var json = System.Text.Json.JsonSerializer.Serialize(pdkDraft);

        var reloaded = new PdkLoader().LoadFromJson(json);
        var component = reloaded.Components.ShouldHaveSingleItem();
        component.Pins.ShouldBeEmpty();
        component.OutlinePolygons.ShouldNotBeNull().ShouldNotBeEmpty();
    }

    [Fact]
    public async Task ImportAsync_GeometryOnlyCellWithoutOutlines_PersistedDraft_ReloadsThroughPdkLoader()
    {
        // The exact field shape: a pin-less cell whose geometry never became
        // outline polygons (text-only marker content). The raw code is the only
        // geometry source left, and the loader must accept that.
        var service = _host.CreateService();
        await service.ImportAsync(WriteGds(PinlessOnlyLibrary()), "TOP", null, null);
        var drafts = _host.Scope.Sets.ShouldHaveSingleItem().Drafts;
        foreach (var draft in drafts)
            draft.OutlinePolygons = null;

        var pdkDraft = new PdkDraft { Name = "GDS Import - circuit", Components = drafts.ToList() };
        var json = System.Text.Json.JsonSerializer.Serialize(pdkDraft);

        var reloaded = new PdkLoader().LoadFromJson(json);
        var component = reloaded.Components.ShouldHaveSingleItem();
        component.Pins.ShouldBeEmpty();
        component.RawCode.ShouldNotBeNullOrWhiteSpace();
    }

    // ── PDK loader rule: pins OR outlines ────────────────────────────────────

    [Fact]
    public void PdkLoader_ComponentWithoutPinsButWithOutlines_LoadsAsGeometryOnly()
    {
        const string json = """
            {
              "name": "GDS Import PDK",
              "components": [
                {
                  "name": "logo",
                  "category": "GDS Import",
                  "widthMicrometers": 5,
                  "heightMicrometers": 5,
                  "rawCode": "import nazca as nd\ndef component():\n    return nd.Cell(name=\"logo\")\n",
                  "rawCodeBackend": "nazca",
                  "outlinePolygons": [
                    {
                      "layer": 111,
                      "dataType": 0,
                      "points": [
                        { "x": 0, "y": 0 }, { "x": 5, "y": 0 },
                        { "x": 5, "y": 5 }, { "x": 0, "y": 5 }, { "x": 0, "y": 0 }
                      ]
                    }
                  ],
                  "pins": []
                }
              ]
            }
            """;

        var pdk = new PdkLoader().LoadFromJson(json);

        var component = pdk.Components.ShouldHaveSingleItem();
        component.Pins.ShouldBeEmpty();
        component.OutlinePolygons.ShouldNotBeNull().ShouldHaveSingleItem();
    }
}

/// <summary>GDS fixture cell builder for the geometry-only tests.</summary>
file static class GdsGeometryOnlyTestCells
{
    /// <summary>10×4 µm gdsfactory-style waveguide (same shape as GdsImportServiceTests').</summary>
    public static GdsTestWriter WaveguideCell(this GdsTestWriter writer, string name) =>
        writer
            .BeginCell(name)
                .Boundary(1, 0, (0, 1750), (10000, 1750), (10000, 2250), (0, 2250), (0, 1750))
                .Boundary(111, 0, (0, 0), (10000, 0), (10000, 4000), (0, 4000), (0, 0))
                .Text(1, 10, "in", 0, 2000)
                .Text(1, 10, "out", 10000, 2000)
            .EndCell();
}
