using System.Globalization;
using System.Text.RegularExpressions;
using CAP.Avalonia.Services;
using CAP_Core.Export;
using Shouldly;
using Xunit;

namespace UnitTests.Export.OpenEbl;

/// <summary>
/// openEBL gap #4 (#1336), script level: an EBeam-only export must emit every routed
/// optical connection as its own SiEPIC-conformant <c>Waveguide_&lt;n&gt;</c> cell —
/// the routed Si polygons wrapped in the cell, plus a klayout spine pass
/// (<see cref="SiepicWaveguideCellWriter"/>) that adds the Waveguide (1/99) guide, the
/// DevRec (68/0) outline and PinRec (1/10) pins after the foundry-cell upgrade.
/// Mixed and demo-only designs emit nothing — their scripts stay byte-identical.
/// </summary>
public class OpenEblEBeamWaveguideCellTests
{
    [Fact]
    public void Export_EBeamOnlyDesign_WrapsRoutedConnectionInWaveguideCell()
    {
        var canvas = EBeamCanvasBuilder.CreateWithWaveguide();

        var script = new SimpleNazcaExporter().Export(canvas);

        // One waveguide cell wraps the connection's segments (extra indentation
        // inside the with-block), placed at the top-cell origin.
        script.ShouldContain("with nd.Cell(name='Waveguide_0') as waveguide_0:");
        script.ShouldContain("    nd.strt(length=127.00, width=0.5, layer=1).put(");
        script.ShouldContain("waveguide_0.put(0, 0)");
    }

    [Fact]
    public void Export_EBeamOnlyDesign_EmitsSpinePassWithPinSnappedCentreline()
    {
        var canvas = EBeamCanvasBuilder.CreateWithWaveguide();
        var gc1 = EBeamCanvasBuilder.FindComponentAtX(canvas, 0);
        var gc2 = EBeamCanvasBuilder.FindComponentAtX(canvas, 127);

        var script = new SimpleNazcaExporter().Export(canvas);

        // The spine pass runs after the foundry-cell upgrade (its pin matching
        // reads the real foundry PinRec paths from the upgraded GDS).
        var upgradeAt = script.IndexOf("_lunima_upgrade_siepic_cells(gds_filename", StringComparison.Ordinal);
        var spineAt = script.IndexOf("_lunima_add_waveguide_spines(gds_filename", StringComparison.Ordinal);
        upgradeAt.ShouldBeGreaterThanOrEqualTo(0);
        spineAt.ShouldBeGreaterThan(upgradeAt);

        // The spine dict: one entry with the EBeam strip width, on the SiEPIC layers.
        var match = Regex.Match(
            script,
            @"_lunima_add_waveguide_spines\(gds_filename, \{'Waveguide_0': \{'width': (0\.5), 'points': \[([^\]]+)\]\}");
        match.Success.ShouldBeTrue("the spine pass must carry the Waveguide_0 centreline");
        script.ShouldContain("_li_wg = _layer(1, 99)");
        script.ShouldContain("_li_dr = _layer(68, 0)");
        script.ShouldContain("_li_pr = _layer(1, 10)");

        // The centreline's ends land exactly on the two component pins (nazca space).
        var points = Regex.Matches(match.Groups[2].Value, @"\((-?[\d.]+), (-?[\d.]+)\)")
            .Select(m => (
                double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)))
            .ToList();
        points.Count.ShouldBeGreaterThanOrEqualTo(2);
        var ci = CultureInfo.InvariantCulture;
        var (x1, y1) = NazcaCoordinateMapper.GetPinNazcaPosition(gc1.PhysicalPins[0]);
        var (x2, y2) = NazcaCoordinateMapper.GetPinNazcaPosition(gc2.PhysicalPins[0]);
        points[0].ShouldBe((double.Parse(x1.ToString("F2", ci), ci), double.Parse(y1.ToString("F2", ci), ci)));
        points[^1].ShouldBe((double.Parse(x2.ToString("F2", ci), ci), double.Parse(y2.ToString("F2", ci), ci)));
    }

    [Fact]
    public void Export_MixedDesign_EmitsNoWaveguideCells()
    {
        var canvas = EBeamCanvasBuilder.CreateWithWaveguide();
        canvas.AddComponent(TestComponentFactory.CreateBasicComponent(), "DEMO");

        var script = new SimpleNazcaExporter().Export(canvas);

        // The profile does not resolve on a mixed canvas: routes flatten into the
        // top cell exactly as before, no spine pass.
        script.ShouldNotContain("with nd.Cell(name='Waveguide_");
        script.ShouldNotContain("_lunima_add_waveguide_spines");
    }

    [Fact]
    public void Export_DemoOnlyDesign_IsByteIdenticalWithoutWaveguideCells()
    {
        var canvas = new CAP.Avalonia.ViewModels.Canvas.DesignCanvasViewModel();
        canvas.AddComponent(TestComponentFactory.CreateBasicComponent(), "DEMO1");
        canvas.AddComponent(TestComponentFactory.CreateBasicComponent(), "DEMO2");

        var script = new SimpleNazcaExporter().Export(canvas);

        script.ShouldNotContain("Waveguide_");
        script.ShouldNotContain("_lunima_add_waveguide_spines");
        script.ShouldNotContain("(1, 99)");
    }
}
