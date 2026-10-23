using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Hierarchy;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Panels;
using CAP_Core;
using CAP_Core.Components.Creation;
using CAP_DataAccess.Components.AddCustomComponent;
using CAP_DataAccess.Components.ComponentDraftMapper;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;
using Shouldly;
using UnitTests.Import.Gds;
using Xunit;

namespace UnitTests.Services.GdsImport;

/// <summary>
/// Issue #1530, real-fixture round trip: a GDS with (a) a cell with detected
/// pins, (b) a pin-less cell with boundary polygons, and (c) a pin-less cell
/// with only paths and ghost text labels (no boundary polygons at all) is
/// imported via <see cref="CAP.Avalonia.Services.GdsImport.GdsImportService"/>,
/// persisted through the user PDK store (<see cref="UserPdkStore"/> — no
/// hand-rolled serialization), then reloaded the way startup does it
/// (<c>LeftPanelViewModel.ReloadUserPdksAtStartupAsync</c>). All three
/// components must be in the library afterwards.
/// </summary>
public class GdsUserPdkStartupRoundTripTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "lunima-gds-roundtrip-" + Guid.NewGuid().ToString("N"));
    private readonly string _prefsPath =
        Path.Combine(Path.GetTempPath(), "lunima-gds-roundtrip-prefs-" + Guid.NewGuid().ToString("N") + ".json");
    private readonly GdsDesignScopeTestHost _host = new();

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
        if (File.Exists(_prefsPath)) File.Delete(_prefsPath);
        _host.Dispose();
    }

    /// <summary>
    /// TOP referencing the three fixture cells: wg_pins (gdsfactory-style
    /// waveguide, labels on (1,10) → detected pins), logo_poly (boundary
    /// polygons on (111,0), no labels → pin-less with outlines) and
    /// marker_pathlabel (a PATH on (111,0) plus nazca helper labels only —
    /// anchor/section-header ghosts never become pins — so the cell carries no
    /// boundary polygons at all).
    /// </summary>
    private static byte[] FixtureLibrary() => GdsTestWriter.Create()
        .StandardPrologue()
        .BeginCell("TOP")
            .SRef("wg_pins", 0, 0)
            .SRef("logo_poly", 20000, 0)
            .SRef("marker_pathlabel", 40000, 0)
        .EndCell()
        .BeginCell("wg_pins")
            .Boundary(1, 0, (0, 1750), (10000, 1750), (10000, 2250), (0, 2250), (0, 1750))
            .Boundary(111, 0, (0, 0), (10000, 0), (10000, 4000), (0, 4000), (0, 0))
            .Text(1, 10, "in", 0, 2000)
            .Text(1, 10, "out", 10000, 2000)
        .EndCell()
        .BeginCell("logo_poly")
            .Boundary(111, 0, (0, 0), (5000, 0), (5000, 5000), (0, 5000), (0, 0))
        .EndCell()
        .BeginCell("marker_pathlabel")
            .Path(111, 0, 500, 0, (0, 500), (8000, 500))
            .Text(111, 0, "cc", 4000, 500)
            .Text(111, 0, "Parameters:", 1000, 2000)
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

    [Fact]
    public async Task ImportedPinnedPolygonOnlyAndPathLabelOnlyCells_AllSurviveUserPdkStartupReload()
    {
        var userPdkRoot = Path.Combine(_root, "user-pdks");

        // 1. Import the fixture through the real service.
        var service = _host.CreateService();
        var outcome = await service.ImportAsync(WriteGds(FixtureLibrary()), "TOP", null, null);
        var drafts = _host.Scope.Sets.ShouldHaveSingleItem().Drafts;
        drafts.Select(d => d.Name).ShouldBe(
            new[] { "wg_pins", "logo_poly", "marker_pathlabel" }, ignoreOrder: true);

        // The fixture shape: (a) detected pins, (b) pin-less with outlines,
        // (c) pin-less path/label-only.
        drafts.Single(d => d.Name == "wg_pins").Pins.ShouldNotBeEmpty();
        var logo = drafts.Single(d => d.Name == "logo_poly");
        logo.Pins.ShouldBeEmpty();
        logo.OutlinePolygons.ShouldNotBeNull().ShouldNotBeEmpty();
        var marker = drafts.Single(d => d.Name == "marker_pathlabel");
        marker.Pins.ShouldBeEmpty("helper labels (anchors / section headers) never become pins");

        // 2. Persist through the user PDK store (strict load-modify-save per
        // component — a draft that fails validation would throw right here).
        var store = new UserPdkStore(userPdkRoot, new PdkJsonSaver(), new PdkLoader());
        var process = new ProcessDefinition { Name = "Test Process" };
        foreach (var draft in drafts)
        {
            store.SaveToNamedPdk("Foundry Fixtures", process, draft, "nazca", null);
        }

        // 3. Reload the way startup does it.
        var errorConsole = new ErrorConsoleService();
        var canvas = new DesignCanvasViewModel();
        var groupLibrary = new GroupLibraryManager();
        var leftPanel = new LeftPanelViewModel(
            canvas, groupLibrary, new PdkLoader(), new UserPreferencesService(_prefsPath),
            new HierarchyPanelViewModel(canvas), new PdkManagerViewModel(),
            new ComponentLibraryViewModel(groupLibrary), errorConsole, userPdkLibrary: store);

        await leftPanel.ReloadUserPdksAtStartupAsync(userPdkRoot);

        leftPanel.PdkManager.LoadedPdks.ShouldContain(
            p => p.Name == "Foundry Fixtures" && p.ComponentCount == 3);
        leftPanel.AllTemplates.Select(t => t.Name).ShouldBe(
            new[] { "wg_pins", "logo_poly", "marker_pathlabel" }, ignoreOrder: true);
        leftPanel.AllTemplates.Single(t => t.Name == "wg_pins").PinDefinitions.ShouldNotBeEmpty();
        leftPanel.AllTemplates.Single(t => t.Name == "logo_poly").PinDefinitions.ShouldBeEmpty();
        leftPanel.AllTemplates.Single(t => t.Name == "marker_pathlabel").PinDefinitions.ShouldBeEmpty();
        errorConsole.Entries.ShouldBeEmpty(
            "every imported component is valid — nothing may be dropped or reported");
    }
}
