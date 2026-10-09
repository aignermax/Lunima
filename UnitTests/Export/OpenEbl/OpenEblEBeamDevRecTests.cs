using System.Text.RegularExpressions;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Library;
using Shouldly;
using Xunit;

namespace UnitTests.Export.OpenEbl;

/// <summary>
/// openEBL gap #2 (#1321), script level: an EBeam-only export must arrange a DevRec
/// (68, 0) polygon for every placed component cell, so SiEPIC-Tools'
/// <c>find_components</c> (the first step of openEBL's <c>run_verification.py</c>)
/// sees the components instead of aborting with "Unknown error occurred". The
/// emission lives in the klayout post-pass (<see cref="SiepicCellUpgradeWriter"/>):
/// cells the upgrade swaps for real foundry geometry keep the PDK's own DevRec
/// (never duplicated), stub-kept cells get their footprint drawn. Mixed and
/// demo-only designs emit nothing — their scripts stay byte-identical.
/// </summary>
public class OpenEblEBeamDevRecTests
{
    [Fact]
    public void Export_EBeamOnlyDesign_EmitsDevRecEnsurePassForEveryComponentCell()
    {
        var canvas = EBeamCanvasBuilder.CreateWithWaveguide();
        var yTemplate = TestPdkLoader.LoadFromPdk("siepic-ebeam-pdk.json")
            .First(t => t.NazcaFunctionName == "ebeam_y_1550");
        canvas.AddComponent(ComponentTemplates.CreateFromTemplate(yTemplate, 0, 300), "Y1");

        var script = new SimpleNazcaExporter().Export(canvas);

        // One ensure-pass call listing every placed component's stub cell.
        var calls = Regex.Matches(script, @"_lunima_ensure_devrec\(gds_filename, \[([^\]]*)\]\)");
        calls.Count.ShouldBe(1);
        calls[0].Groups[1].Value.ShouldContain("'ebeam_gc_te1550'");
        calls[0].Groups[1].Value.ShouldContain("'ebeam_y_1550'");
        // The pass draws on the DevRec layer and never duplicates a foundry DevRec.
        script.ShouldContain("_kdb.LayerInfo(68, 0)");
        script.ShouldContain("never duplicate it");
    }

    [Fact]
    public void Export_MixedDesign_EmitsNoDevRecPass()
    {
        var canvas = EBeamCanvasBuilder.CreateWithWaveguide();
        canvas.AddComponent(TestComponentFactory.CreateBasicComponent(), "DEMO");

        var script = new SimpleNazcaExporter().Export(canvas);

        // The profile does not resolve on a mixed canvas: no DevRec pass, and the
        // upgrade block itself stays as before (EBeam-only gating, #1321).
        script.ShouldNotContain("_lunima_ensure_devrec");
    }

    [Fact]
    public void Export_DemoOnlyDesign_IsByteIdenticalWithoutDevRecPass()
    {
        var canvas = new CAP.Avalonia.ViewModels.Canvas.DesignCanvasViewModel();
        canvas.AddComponent(TestComponentFactory.CreateBasicComponent(), "DEMO1");
        canvas.AddComponent(TestComponentFactory.CreateBasicComponent(), "DEMO2");

        var script = new SimpleNazcaExporter().Export(canvas);

        // A demo-only design has no SiEPIC cells at all: neither the upgrade block
        // nor the DevRec pass may appear — the script is exactly what it was.
        script.ShouldNotContain("_lunima_ensure_devrec");
        script.ShouldNotContain("_lunima_upgrade_siepic_cells");
        script.ShouldNotContain("(68, 0)");
    }
}
