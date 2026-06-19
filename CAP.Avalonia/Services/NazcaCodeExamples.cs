namespace CAP.Avalonia.Services;

/// <summary>
/// Ready-to-run Nazca code examples for the per-instance code editor's quick help
/// (issue #556). Each string is a self-contained snippet that defines
/// <c>component()</c> returning a cell, verified to render through the preview script
/// (see <c>NazcaEditorPreviewIntegrationTests</c>). The official Nazca manual is very
/// text-heavy, so these give a code-first starting point showing the common elements.
/// </summary>
public static class NazcaCodeExamples
{
    /// <summary>Minimal runnable starter.</summary>
    public const string Starter = """
        import nazca as nd

        def component():
            with nd.Cell() as C:
                nd.strt(length=20).put()
                nd.bend(radius=10, angle=90).put()
                return C
        """;

    /// <summary>
    /// Showcase circuit chaining the common Nazca geometry elements
    /// (taper, straight, bend, S-bend, Euler bend, free curve) with pins.
    /// Pin convention: cell pins point OUTWARD (input 'in' faces 180°, away
    /// from the structure) so connections and exports anchor correctly.
    /// </summary>
    public const string Complex = """
        import nazca as nd

        # Showcase: common Nazca elements chained into one cell. Each .put()
        # chains onto the previous element's output pin; pass (x, y, angle) to
        # place explicitly. See https://nazca-design.org/manual/ for the full set.
        def component():
            with nd.Cell() as C:
                nd.Pin('in').put(0, 0, 180)                               # input port, faces outward
                nd.taper(length=10, width1=1.0, width2=2.0).put(0, 0, 0)  # widen 1->2um
                nd.strt(length=20, width=2.0).put()                       # straight
                nd.bend(radius=15, angle=90, width=2.0).put()             # 90 deg circular bend
                nd.sinebend(width=2.0, distance=30, offset=12).put()      # S-bend (lateral offset)
                nd.euler(width=2.0, radius=15, angle=-90).put()           # adiabatic Euler bend
                nd.cobra(xya=(40, -10, 0), width1=2.0, width2=2.0).put()  # free curve to (x, y, angle)
                nd.taper(length=10, width1=2.0, width2=1.0).put()         # narrow 2->1um
                nd.strt(length=8).put()
                nd.Pin('out').put()                                       # output port, faces outward
                return C
        """;
}
