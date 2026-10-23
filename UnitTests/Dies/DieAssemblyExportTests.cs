using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Dies;
using CAP_Core.Export;
using Shouldly;
using UnitTests.Integration;
using Xunit;

namespace UnitTests.Dies;

/// <summary>
/// Nazca export of a die assembly: one cell per die in die-local coordinates with its outline
/// on the frame layer, an assembly top cell placing the dies, die-to-die links never exported
/// as waveguides; "one file per die" makes a single die the top cell.
/// </summary>
public class DieAssemblyExportTests
{
    private const string Example = "Two Chiplets - Edge-Coupler Link.lun";

    [Fact]
    public async Task Assembly_ExportsOneCellPerDie_UnderAnAssemblyTopCell()
    {
        var canvas = new DesignCanvasViewModel();
        await TwoChipletsExampleLoader.LoadAsync(canvas, Example);

        var script = new SimpleNazcaExporter().Export(canvas);

        var dies = canvas.Dies.RootDies;
        dies.Count.ShouldBe(2);
        foreach (var die in dies)
        {
            script.ShouldContain($"with nd.Cell(name='{SimpleNazcaExporter.DieCellName(die)}') as die:");
            script.ShouldContain($"with nd.Cell(name='{SimpleNazcaExporter.DieCellName(die)}_content') as design:");
        }
        script.ShouldContain($"layer=({SimpleNazcaExporter.DieFrameLayer}, 0)");
        script.ShouldContain("create_die_0().put(");
        script.ShouldContain("create_die_1().put(");
        script.ShouldContain("# Cross-chiplet facet link");
        script.ShouldContain("nd.export_gds(topcells=[design]");
    }

    [Fact]
    public async Task OneFilePerDie_MakesTheDieTheTopCell_InDieLocalCoordinates()
    {
        var canvas = new DesignCanvasViewModel();
        await TwoChipletsExampleLoader.LoadAsync(canvas, Example);
        var die = canvas.Dies.RootDies[1];
        var outline = DieAssembly.OutlineOf(die);

        var script = new SimpleNazcaExporter().ExportAssembly(canvas, die);

        script.ShouldContain("def create_die_0():");
        script.ShouldNotContain("create_die_1");
        script.ShouldContain("    design = create_die_0()");
        script.ShouldContain(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"content.put({-outline.X:F3}, {outline.Y + outline.Height:F3}, 0)"));
    }

    /// <summary>
    /// Runs the exported assembly script through the real Nazca (when installed): the GDS
    /// holds the assembly top cell and one cell per die. Skips silently without Python/Nazca.
    /// </summary>
    [Fact]
    [Trait("Category", "Slow")]
    public async Task Assembly_RendersToGds_WithOneCellPerDie()
    {
        var service = new GdsExportService();
        bool required = Environment.GetEnvironmentVariable("CAP_REQUIRE_NAZCA") == "1";
        if (!(await service.CheckPythonEnvironmentAsync()).IsReady)
        {
            required.ShouldBeFalse("CAP_REQUIRE_NAZCA=1 but no Python/Nazca environment was found");
            return;
        }
        var canvas = new DesignCanvasViewModel();
        await TwoChipletsExampleLoader.LoadAsync(canvas, Example);
        var script = Path.Combine(Path.GetTempPath(), $"die_assembly_{Guid.NewGuid():N}.py");
        await File.WriteAllTextAsync(script, new SimpleNazcaExporter().Export(canvas));
        try
        {
            var viewModel = new CAP.Avalonia.ViewModels.Export.GdsExportViewModel(service, new global::CAP_Core.Export.PythonDiscoveryService()) { GenerateGdsEnabled = true };
            var result = await viewModel.ExportScriptToGdsAsync(script);

            result.Success.ShouldBeTrue(result.ErrorMessage);
            var gds = System.Text.Encoding.ASCII.GetString(await File.ReadAllBytesAsync(result.GdsPath!));
            gds.ShouldContain("ConnectAPIC_Design");
            foreach (var die in canvas.Dies.RootDies)
                gds.ShouldContain(SimpleNazcaExporter.DieCellName(die));
        }
        finally
        {
            File.Delete(script);
            File.Delete(Path.ChangeExtension(script, ".gds"));
        }
    }

    [Fact]
    public void SingleDieDesign_KeepsTheFlatExport()
    {
        var canvas = new DesignCanvasViewModel();
        canvas.AddComponent(DieTestFixtures.Part("p", 100, 100));

        var script = new SimpleNazcaExporter().Export(canvas);

        script.ShouldNotContain("create_die_");
        script.ShouldContain("def create_design():");
    }
}
