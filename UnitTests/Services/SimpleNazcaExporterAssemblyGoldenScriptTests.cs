using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.Routing;
using CAP_Core.Routing.MetalRouting;
using Shouldly;
using UnitTests.Dies;
using static UnitTests.Services.NazcaGoldenScriptFixture;

namespace UnitTests.Services;

/// <summary>Complete assembly script contract for die-local placement and electrical geometry.</summary>
public class SimpleNazcaExporterAssemblyGoldenScriptTests
{
    [Fact]
    public void Export_TwoDiesWithTaggedElectricalFrozenPath_MatchesCompleteAssemblyScript()
    {
        var canvas = new DesignCanvasViewModel();
        var pad = Cell("PAD", "demo_pdk.pad", 110, 220, 10, 10);
        var first = AddPin(pad, "e1", 0, 5, 180, MatterType.Electricity);
        var second = AddPin(pad, "e2", 10, 5, 0, MatterType.Electricity);
        var die = DieTestFixtures.Die("Metal Die", 100, 200, 80, 60, children: new[] { pad });
        var route = new RoutedPath();
        route.Segments.Add(new StraightSegment(110, 225, 120, 225, 0));
        die.AddInternalPath(new FrozenWaveguidePath
        {
            StartPin = first,
            EndPin = second,
            Path = route,
            Layer = 45,
            DataType = 3,
        });
        canvas.Components.Add(new ComponentViewModel(die));
        canvas.Components.Add(new ComponentViewModel(DieTestFixtures.Die("Empty Die", 300, 200, 50, 40)));
        var metal = new MetalRoutingSpec(4.25, 41, 2, ElectricalCrossingPolicy.DirectCrossingAllowed, 47);
        var skipped = new List<string>();

        // The imported trace keeps layer 45/3 while retaining the process width 4.25.
        // Its all-electrical cell body uses METAL_LAYER, and neither pin emits nd.Pin.
        // Carrier placements exactly invert the die-local lower-left translations.
        const string expected = """"
        import nazca as nd
        import nazca.demofab as demo
        from nazca.interconnects import Interconnect

        # PDK Configuration
        WG_WIDTH = 0.45  # Waveguide width in µm
        BEND_RADIUS = 50  # Minimum bend radius in µm

        # Electrical metal routing (process-derived)
        METAL_WIDTH = 4.25  # Metal trace width in µm
        METAL_LAYER = (41, 2)
        BRIDGE_LAYER = (47, 0)

        # Create interconnect for waveguide routing
        ic = Interconnect(width=WG_WIDTH, radius=BEND_RADIUS)

        with nd.Cell(name='demo_pdk.pad') as _demo_pdk_pad_cell:
            """Auto-generated stub for demo_pdk.pad (10x10 µm)."""
            nd.Polygon(points=[(0.00,-10.00),(10.00,-10.00),(10.00,0.00),(0.00,0.00)], layer=METAL_LAYER).put(0, 0)
            nd.Annotation(text='e1', layer=(1, 10)).put(0.00, -5.00)
            nd.Annotation(text='e2', layer=(1, 10)).put(10.00, -5.00)

        def demo_pdk_pad(**kwargs):
            return _demo_pdk_pad_cell

        # Die 'Metal Die' (no process)
        def create_die_0_content():
            with nd.Cell(name='Metal_Die_content') as design:

                # Components
                # COORD: PAD editor=(110.00,220.00) originOffset=(0.00,0.00) nazca=(110.00,-220.00) rot=0
                # PIN: e1 expected_nazca=(110.00,-225.00)
                # PIN: e2 expected_nazca=(120.00,-225.00)
                comp_0 = demo_pdk_pad().put('org', 110.00, -220.00, 0)  # PAD

                nd.strt(length=10.00, width=4.25, layer=(45, 3)).put(110.00, -225.00, 0.00)
            return design

        def create_die_0():
            content = create_die_0_content()
            with nd.Cell(name='Metal_Die') as die:
                # Die-local coordinates: the die's lower-left corner is the cell origin.
                content.put(-100.000, 260.000, 0)
                nd.Polygon(points=[(0, 0), (80.000, 0), (80.000, 60.000), (0, 60.000)], layer=(999, 0)).put(0, 0)
            return die

        # Die 'Empty Die' (no process)
        def create_die_1_content():
            with nd.Cell(name='Empty_Die_content') as design:

                # Components

                pass
            return design

        def create_die_1():
            content = create_die_1_content()
            with nd.Cell(name='Empty_Die') as die:
                # Die-local coordinates: the die's lower-left corner is the cell origin.
                content.put(-300.000, 240.000, 0)
                nd.Polygon(points=[(0, 0), (50.000, 0), (50.000, 40.000), (0, 40.000)], layer=(999, 0)).put(0, 0)
            return die

        def create_design():
            with nd.Cell(name='ConnectAPIC_Design') as design:
                # Assembly: every die placed where it sits on the carrier
                create_die_0().put(100.000, -260.000, 0)
                create_die_1().put(300.000, -240.000, 0)
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
        """";

        var actual = new SimpleNazcaExporter().Export(canvas, metalSpec: metal, skippedConnections: skipped);

        actual.ReplaceLineEndings("\n").ShouldBe(expected.ReplaceLineEndings("\n") + "\n");
        skipped.ShouldBeEmpty();
    }
}
