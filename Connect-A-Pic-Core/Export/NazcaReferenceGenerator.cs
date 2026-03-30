using System.Globalization;
using System.Text;

namespace CAP_Core.Export;

/// <summary>
/// Generates a Nazca reference Python script and provides ground-truth
/// expected coordinates for GDS export validation (Issue #329 debugging).
///
/// The reference design consists of two 100×50 µm box components connected
/// by a 200 µm straight waveguide. Both the Python script and the C# design
/// in <c>GdsTestDesigns</c> use these same constants so the outputs are
/// directly comparable.
/// </summary>
public class NazcaReferenceGenerator
{
    // ── Reference design constants (micrometers) ──────────────────────────
    // These MUST stay in sync with Scripts/generate_reference_nazca.py

    /// <summary>Component width in µm.</summary>
    public const double ComponentWidth = 100.0;

    /// <summary>Component height in µm.</summary>
    public const double ComponentHeight = 50.0;

    /// <summary>Physical X position of component 1.</summary>
    public const double Component1X = 0.0;

    /// <summary>Physical Y position of component 1.</summary>
    public const double Component1Y = 0.0;

    /// <summary>Physical X position of component 2.</summary>
    public const double Component2X = 300.0;

    /// <summary>Physical Y position of component 2.</summary>
    public const double Component2Y = 0.0;

    /// <summary>Output pin X offset from component origin.</summary>
    public const double PinOffsetX = 100.0;

    /// <summary>Output/input pin Y offset from component origin (mid-height).</summary>
    public const double PinOffsetY = 25.0;

    /// <summary>Waveguide length between the two components.</summary>
    public const double WaveguideLength = Component2X - ComponentWidth; // 200.0

    // ── Derived Nazca coordinate values ──────────────────────────────────
    // With NazcaOriginOffset=(0, ComponentHeight), pin Nazca positions account for the shifted origin:
    // Component Nazca Y = -(PhysicalY + ComponentHeight)
    // Pin local Y in stub = (ComponentHeight - PinOffsetY) - ComponentHeight = -PinOffsetY
    // Pin absolute Nazca Y = Component Y + Pin local Y = -ComponentHeight + (-PinOffsetY) = -(ComponentHeight + PinOffsetY)

    /// <summary>Nazca Y for component 1 placement.</summary>
    public const double NazcaComp1Y = -(Component1Y + ComponentHeight); // -50.0

    /// <summary>Nazca Y for component 2 placement.</summary>
    public const double NazcaComp2Y = -(Component2Y + ComponentHeight); // -50.0

    /// <summary>Nazca X for waveguide start point.</summary>
    public const double NazcaWgStartX = Component1X + PinOffsetX;      // 100.0

    /// <summary>Nazca Y for waveguide start point (with NazcaOriginOffset=(0,ComponentHeight)).</summary>
    public const double NazcaWgStartY = -(Component1Y + ComponentHeight + PinOffsetY);   // -(0 + 50 + 25) = -75.0

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the expected ground-truth coordinates in physical (editor) µm.
    /// These must match the values in Scripts/generate_reference_nazca.py exactly.
    /// </summary>
    /// <returns>Dictionary mapping coordinate names to (X, Y) tuples in physical µm.</returns>
    public Dictionary<string, (double X, double Y)> GetExpectedCoordinates()
    {
        return new Dictionary<string, (double X, double Y)>
        {
            ["comp1_position"]  = (Component1X,               Component1Y),
            ["comp1_pin_out"]   = (Component1X + PinOffsetX,  Component1Y + PinOffsetY),
            ["comp1_pin_in"]    = (Component1X,                Component1Y + PinOffsetY),
            ["comp2_position"]  = (Component2X,               Component2Y),
            ["comp2_pin_out"]   = (Component2X + PinOffsetX,  Component2Y + PinOffsetY),
            ["comp2_pin_in"]    = (Component2X,                Component2Y + PinOffsetY),
            ["waveguide_start"] = (Component1X + PinOffsetX,  Component1Y + PinOffsetY),
            ["waveguide_end"]   = (Component2X,               Component2Y + PinOffsetY),
        };
    }

    /// <summary>
    /// Returns the expected Nazca coordinate values produced by <c>SimpleNazcaExporter</c>.
    /// These are the Y-flipped coordinates written into the Python script.
    /// </summary>
    /// <returns>Dictionary mapping coordinate names to (X, Y) tuples in Nazca µm.</returns>
    public Dictionary<string, (double X, double Y)> GetExpectedNazcaCoordinates()
    {
        return new Dictionary<string, (double X, double Y)>
        {
            ["comp1_nazca"]     = (Component1X,   NazcaComp1Y),
            ["comp2_nazca"]     = (Component2X,   NazcaComp2Y),
            ["wg_start_nazca"]  = (NazcaWgStartX, NazcaWgStartY),
        };
    }

    /// <summary>
    /// Generates the Python reference script content that produces the ground-truth GDS.
    /// The script is equivalent to what <c>SimpleNazcaExporter</c> should generate for
    /// the reference design created by <c>GdsTestDesigns.CreateReferenceDesign()</c>.
    /// </summary>
    /// <param name="outputGdsPath">Path where the reference GDS will be written when the script runs.</param>
    /// <param name="outputCoordsPath">Path where the expected-coordinates JSON will be written.</param>
    /// <returns>Python script content as a string.</returns>
    public string GeneratePythonScript(string outputGdsPath, string outputCoordsPath)
    {
        var ci = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();

        AppendHeader(sb, ci);
        AppendStubDefinition(sb, ci);
        AppendDesignFunction(sb, ci);
        AppendEntryPoint(sb, outputGdsPath, outputCoordsPath);

        return sb.ToString();
    }

    // ── Private helpers ───────────────────────────────────────────────────

    private static void AppendHeader(StringBuilder sb, CultureInfo ci)
    {
        sb.AppendLine("\"\"\"");
        sb.AppendLine("Connect-A-PIC Nazca reference script (auto-generated by NazcaReferenceGenerator).");
        sb.AppendLine("Ground-truth design for GDS coordinate validation — Issue #329.");
        sb.AppendLine("\"\"\"");
        sb.AppendLine("import nazca as nd");
        sb.AppendLine("import json, sys");
        sb.AppendLine();
        sb.AppendLine($"COMP_WIDTH   = {ComponentWidth.ToString("F1", ci)}");
        sb.AppendLine($"COMP_HEIGHT  = {ComponentHeight.ToString("F1", ci)}");
        sb.AppendLine($"COMP1_X      = {Component1X.ToString("F1", ci)}");
        sb.AppendLine($"COMP2_X      = {Component2X.ToString("F1", ci)}");
        sb.AppendLine($"PIN_OFFSET_X = {PinOffsetX.ToString("F1", ci)}");
        sb.AppendLine($"PIN_OFFSET_Y = {PinOffsetY.ToString("F1", ci)}");
        sb.AppendLine($"WG_LENGTH    = {WaveguideLength.ToString("F1", ci)}");
        sb.AppendLine();
    }

    private static void AppendStubDefinition(StringBuilder sb, CultureInfo ci)
    {
        var w  = ComponentWidth.ToString("F2", ci);
        var h  = ComponentHeight.ToString("F2", ci);
        var py = (ComponentHeight - PinOffsetY).ToString("F2", ci); // stub Y = height - offsetY
        var px = PinOffsetX.ToString("F2", ci);

        sb.AppendLine($"with nd.Cell(name='reference_component') as _reference_component_cell:");
        sb.AppendLine($"    nd.Polygon(points=[(0,0),({w},0),({w},{h}),(0,{h})], layer=1).put(0, 0)");
        sb.AppendLine($"    nd.Pin('out').put({px}, {py},   0)");
        sb.AppendLine($"    nd.Pin('in').put(  0.00, {py}, 180)");
        sb.AppendLine();
        sb.AppendLine("def reference_component(**kwargs):");
        sb.AppendLine("    return _reference_component_cell");
        sb.AppendLine();
    }

    private static void AppendDesignFunction(StringBuilder sb, CultureInfo ci)
    {
        var c1x = Component1X.ToString("F2", ci);
        var c1y = NazcaComp1Y.ToString("F2", ci);
        var c2x = Component2X.ToString("F2", ci);
        var c2y = NazcaComp2Y.ToString("F2", ci);
        var wgX = NazcaWgStartX.ToString("F2", ci);
        var wgY = NazcaWgStartY.ToString("F2", ci);
        var wgL = WaveguideLength.ToString("F2", ci);

        sb.AppendLine("def create_reference_design():");
        sb.AppendLine("    with nd.Cell('ReferenceDesign') as cell:");
        sb.AppendLine($"        comp_0 = reference_component().put({c1x}, {c1y}, 0)");
        sb.AppendLine($"        comp_1 = reference_component().put({c2x}, {c2y}, 0)");
        sb.AppendLine($"        nd.strt(length={wgL}).put({wgX}, {wgY}, 0.00)");
        sb.AppendLine();
        sb.AppendLine("    expected = {");
        sb.AppendLine($"        'comp1_position':  [{Component1X.ToString("F1", ci)}, {Component1Y.ToString("F1", ci)}],");
        sb.AppendLine($"        'comp1_pin_out':   [{(Component1X + PinOffsetX).ToString("F1", ci)}, {(Component1Y + PinOffsetY).ToString("F1", ci)}],");
        sb.AppendLine($"        'comp1_pin_in':    [{Component1X.ToString("F1", ci)}, {(Component1Y + PinOffsetY).ToString("F1", ci)}],");
        sb.AppendLine($"        'comp2_position':  [{Component2X.ToString("F1", ci)}, {Component2Y.ToString("F1", ci)}],");
        sb.AppendLine($"        'comp2_pin_out':   [{(Component2X + PinOffsetX).ToString("F1", ci)}, {(Component2Y + PinOffsetY).ToString("F1", ci)}],");
        sb.AppendLine($"        'comp2_pin_in':    [{Component2X.ToString("F1", ci)}, {(Component2Y + PinOffsetY).ToString("F1", ci)}],");
        sb.AppendLine($"        'waveguide_start': [{(Component1X + PinOffsetX).ToString("F1", ci)}, {(Component1Y + PinOffsetY).ToString("F1", ci)}],");
        sb.AppendLine($"        'waveguide_end':   [{Component2X.ToString("F1", ci)}, {(Component2Y + PinOffsetY).ToString("F1", ci)}],");
        sb.AppendLine("    }");
        sb.AppendLine("    return cell, expected");
        sb.AppendLine();
    }

    private static void AppendEntryPoint(StringBuilder sb, string gdsPath, string coordsPath)
    {
        var gds    = gdsPath.Replace("\\", "/");
        var coords = coordsPath.Replace("\\", "/");

        sb.AppendLine("if __name__ == '__main__':");
        sb.AppendLine("    args = sys.argv[1:]");
        sb.AppendLine($"    gds_path    = args[0] if len(args) > 0 else r'{gds}'");
        sb.AppendLine($"    coords_path = args[1] if len(args) > 1 else r'{coords}'");
        sb.AppendLine("    cell, expected = create_reference_design()");
        sb.AppendLine("    nd.export_gds(topcells=cell, filename=gds_path)");
        sb.AppendLine("    print(f'Reference GDS:   {gds_path}')");
        sb.AppendLine("    with open(coords_path, 'w') as f:");
        sb.AppendLine("        json.dump(expected, f, indent=2)");
        sb.AppendLine("    print(f'Expected coords: {coords_path}')");
    }
}
