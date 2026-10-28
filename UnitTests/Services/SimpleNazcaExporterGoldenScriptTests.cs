using System.Globalization;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.Routing.InterconnectRouting;
using Shouldly;
using static UnitTests.Services.NazcaGoldenScriptFixture;

namespace UnitTests.Services;

/// <summary>Complete emitted script contract, independent of Python and host culture.</summary>
public class SimpleNazcaExporterGoldenScriptTests
{
    [Fact]
    public void Export_CalibratedCouplerWithMixedPins_MatchesCompleteScriptUnderGermanCulture()
    {
        var canvas = new DesignCanvasViewModel();
        var coupler = Cell("GC", "demo_pdk.grating", 10.25, 20.5, 20, 8);
        coupler.NazcaOriginOffsetX = 2.25;
        coupler.NazcaOriginOffsetY = 3.5;
        AddPin(coupler, "in", 0, 4, 180, MatterType.Light);
        AddPin(coupler, "out", 20, 4, 0, MatterType.Light);
        AddPin(coupler, "heater", 10, 0, 270, MatterType.Electricity);
        canvas.Components.Add(new ComponentViewModel(coupler));
        var exporter = new SimpleNazcaExporter
        {
            SettingsSource = () => new InterconnectSettings
            {
                WidthMicrometers = 0.65,
                BendRadiusMicrometers = 25.5,
                GdsLayer = 7,
            },
        };

        // Hand-derived from the calibrated cell frame: local pin = (x-2.25, 3.5-y),
        // world pin = (10.25+x, -20.5-y). Electrical pins get labels, not optical ports.
        // The entire string also fixes stub/placement/registry/export/verification order.
        const string expected = """"
        import nazca as nd
        import nazca.demofab as demo
        from nazca.interconnects import Interconnect

        # PDK Configuration
        WG_WIDTH = 0.65  # Waveguide width in µm
        BEND_RADIUS = 25.5  # Minimum bend radius in µm
        WG_LAYER = 7  # Waveguide GDS layer

        # Electrical metal routing (process-derived)
        METAL_WIDTH = 10.00  # Metal trace width in µm
        METAL_LAYER = (11, 0)
        BRIDGE_LAYER = (12, 0)

        # Create interconnect for waveguide routing
        ic = Interconnect(width=WG_WIDTH, radius=BEND_RADIUS, layer=WG_LAYER)

        with nd.Cell(name='demo_pdk.grating') as _demo_pdk_grating_cell:
            """Auto-generated stub for demo_pdk.grating (20x8 µm)."""
            nd.Polygon(points=[(-2.25,-4.50),(17.75,-4.50),(17.75,3.50),(-2.25,3.50)], layer=1).put(0, 0)
            nd.Pin('in').put(-2.25, -0.50, -180)
            nd.Annotation(text='in', layer=(1, 10)).put(-2.25, -0.50)
            nd.Pin('out').put(17.75, -0.50, 0)
            nd.Annotation(text='out', layer=(1, 10)).put(17.75, -0.50)
            nd.Annotation(text='heater', layer=(1, 10)).put(7.75, 3.50)

        def demo_pdk_grating(**kwargs):
            return _demo_pdk_grating_cell

        def create_design():
            with nd.Cell(name='ConnectAPIC_Design') as design:

                # Components
                # COORD: GC editor=(10.25,20.50) originOffset=(2.25,3.50) nazca=(12.50,-24.00) rot=0
                # PIN: in expected_nazca=(10.25,-24.50)
                # PIN: out expected_nazca=(30.25,-24.50)
                # PIN: heater expected_nazca=(20.25,-20.50)
                comp_0 = demo_pdk_grating().put('org', 12.50, -24.00, 0)  # GC
                nd.Annotation(text='GC_in', layer=(1, 10)).put(10.25, -24.50)
                nd.Annotation(text='GC_out', layer=(1, 10)).put(30.25, -24.50)

                # Instance registry for the alignment verification epilog.
                global _verify_instances
                _verify_instances = [('comp_0', comp_0)]

            return design

        # Create and export the design
        design = create_design()

        # Export GDS with filename matching this script
        import os
        import sys
        script_path = os.path.abspath(__file__)
        gds_filename = os.path.splitext(script_path)[0] + '.gds'
        nd.export_gds(topcells=[design], filename=gds_filename)
        print(f'GDS exported to: {gds_filename}')

        # --- Alignment verification (machine-readable) ---
        import json as _json
        _verify = {}
        for _name, _inst in _verify_instances:
            _pins = {}
            for _pn, _pin in _inst.pin.items():
                _px, _py, _pa = _pin.xya()
                # float() unwraps numpy scalars, which json cannot serialize.
                _pins[_pn] = [float(_px), float(_py), float(_pa)]
            _verify[_name] = _pins
        with open(os.path.splitext(script_path)[0] + '.pins.json', 'w') as _f:
            _json.dump(_verify, _f)
        """";
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");

            var actual = exporter.Export(canvas, emitVerification: true);

            actual.ReplaceLineEndings("\n").ShouldBe(expected.ReplaceLineEndings("\n") + "\n");
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }
}
