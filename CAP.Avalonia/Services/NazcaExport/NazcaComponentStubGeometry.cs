using System.Globalization;
using System.Text;
using CAP.Avalonia.Services;
using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.Export;
using static CAP.Avalonia.Services.NazcaExport.NazcaPythonText;

namespace CAP.Avalonia.Services.NazcaExport;

/// <summary>Emits standard and parametric cell geometry with calibrated local pins and port labels.</summary>
internal static class NazcaComponentStubGeometry
{
    /// <summary>
    /// Generates a parametric straight waveguide stub that uses nd.strt() with length parameter.
    /// The cell-internal layout follows the <see cref="NazcaCoordinateMapper"/> contract:
    /// the cell is org-anchored on the offset (ox, oy) the mapper places it by — for a
    /// parametric straight that is the FIRST pin's offset, NOT NazcaOriginOffsetY. The
    /// straight's centre line coincides with its pins (same OffsetY on a straight), so it
    /// sits at oy - firstPin.OffsetY, and every pin renders at the plain Y negation of its
    /// app offset relative to org (oy - OffsetY). Using the mapper's own anchor keeps the
    /// rendered pins coincident with <see cref="NazcaCoordinateMapper.GetPinNazcaPosition"/>;
    /// the old NazcaOriginOffsetY-based anchor differed from the placement and shifted the
    /// rendered geometry off the pins (issue #565).
    /// </summary>
    /// <param name="omitBlackBoxFrame">
    /// True for EBeam-only designs (<see cref="SiepicEBeamExportProfile"/>): the demofab
    /// bb_body frame layer (1003, 0) does not exist in EBeam.lyp, so the frame polygon
    /// is left out of the stub.
    /// </param>
    internal static void AppendParametricStraightStub(
        StringBuilder sb, string funcName, Component comp, CultureInfo ci,
        bool omitBlackBoxFrame = false)
    {
        // The cell is rotation-independent (placement applies .put(rot)); use the
        // UNROTATED first-pin offset as the org anchor (oy), mirroring the mapper. The
        // straight's centre line coincides with its pins, so it sits at oy - firstPin.oy.
        var (anchorX, anchorY) = NazcaCoordinateMapper.GetStubAnchor(comp);
        var firstPin = comp.PhysicalPins.FirstOrDefault();
        var firstPinY = firstPin != null
            ? NazcaCoordinateMapper.GetUnrotatedPinOffset(comp, firstPin).OffsetY
            : 0;
        var strtY = NazcaCoordinateMapper.NormalizeZero(anchorY - firstPinY).ToString("F2", ci);

        // Sanitize function name for valid Python identifier (replace non-alphanumeric/underscore chars)
        var pythonFuncName = System.Text.RegularExpressions.Regex.Replace(funcName, @"[^a-zA-Z0-9_]", "_");

        sb.AppendLine($"def {pythonFuncName}(length=100, **kwargs):");
        sb.AppendLine($"    \"\"\"Auto-generated parametric straight waveguide stub for {funcName}.\"\"\"");
        // The cell name is an f-string so each length gets its OWN cell
        // ('demo.shallow.strt_100') — a plain string would bake the literal
        // "{length}" into the name, collapsing every length into one cell.
        sb.AppendLine($"    with nd.Cell(name=f'{funcName}_{{length}}') as cell:");
        sb.AppendLine($"        # Use nd.strt() for proper waveguide with specified length");
        sb.AppendLine($"        nd.strt(length=length, width=0.45, layer=1).put(0, {strtY})");

        // The black-box body frame on demofab's bb_body layer (1003, 0) — the
        // same documentation layer demofab's own cells draw their frames on:
        // the bare straight is 0.45 µm tall while the app component is W×H, so
        // without the frame the cell bbox (and with it the re-imported
        // placement position) would sit ~(H−0.45)/2 off the original.
        // EBeam-only designs skip it: 1003/0 does not exist in EBeam.lyp.
        if (!omitBlackBoxFrame)
        {
            var w = comp.WidthMicrometers;
            var h = comp.HeightMicrometers;
            var bx0 = NazcaCoordinateMapper.NormalizeZero(-anchorX).ToString("F2", ci);
            var by0 = NazcaCoordinateMapper.NormalizeZero(anchorY - h).ToString("F2", ci);
            var bx1 = NazcaCoordinateMapper.NormalizeZero(w - anchorX).ToString("F2", ci);
            var by1 = NazcaCoordinateMapper.NormalizeZero(anchorY).ToString("F2", ci);
            sb.AppendLine(
                $"        nd.Polygon(points=[({bx0},{by0}),({bx1},{by0}),({bx1},{by1}),({bx0},{by1})], " +
                "layer=(1003, 0)).put(0, 0)  # bb_body frame (documentation layer)");
        }

        // Generate pins from the UNROTATED offsets, relative to org (the mapper anchor);
        // a straight's pins share the centre line, so their local Y is oy - OffsetY = 0.
        foreach (var pin in comp.PhysicalPins)
        {
            var (uox, uoy) = NazcaCoordinateMapper.GetUnrotatedPinOffset(comp, pin);
            var py = NazcaCoordinateMapper.NormalizeZero(anchorY - uoy).ToString("F2", ci);
            var pa = NazcaCoordinateMapper.NormalizeZero(-pin.AngleDegrees).ToString("F0", ci);

            // For straight waveguides: input pin at x=0, output pin at x=length.
            // The label anchors exactly on the pin so re-import detects it there (#808).
            // nd.Pin is an optical Nazca port; electrical pins are not optical ports and
            // must not be emitted as waveguide stubs (#519) — but they still get the
            // port label, or a re-import could never see the pin.
            if (uox == 0)
            {
                if (pin.MatterType == MatterType.Light)
                    sb.AppendLine($"        nd.Pin('{pin.Name}').put(0, {py}, {pa})");
                sb.AppendLine($"        nd.Annotation(text='{EscapePythonString(pin.Name)}', layer={PortLabelLayer}).put(0, {py})");
            }
            else
            {
                if (pin.MatterType == MatterType.Light)
                    sb.AppendLine($"        nd.Pin('{pin.Name}').put(length, {py}, {pa})");
                sb.AppendLine($"        nd.Annotation(text='{EscapePythonString(pin.Name)}', layer={PortLabelLayer}).put(length, {py})");
            }
        }

        sb.AppendLine($"    return cell");
        sb.AppendLine();
    }

    /// <summary>
    /// Generates a standard non-parametric component stub using a polygon box.
    /// The cell-internal layout follows the placement contract of
    /// <see cref="NazcaCoordinateMapper"/>: the geometry bbox is
    /// [-ox, oy-H] .. [W-ox, oy] around the cell origin, because at rotation 0
    /// the org pin is put at (PhysicalX+ox, -(PhysicalY+oy)) and the box top edge
    /// must lie oy above org. Pins render exactly where the app model places them
    /// (plain Y negation), so exported waveguides meet the stub pins.
    /// The stub box doubles as the anchor the klayout post-pass
    /// (<see cref="SiepicCellUpgradeWriter"/>) fills with real foundry geometry
    /// for SiEPIC cells — same cell name, so instances keep their placement.
    /// Every optical pin additionally gets a TEXT label on <see cref="PortLabelLayer"/>
    /// anchored on the pin, so a re-import of the cell detects named pins there
    /// (issue #808).
    /// </summary>
    /// <param name="stubName">
    /// Cell/function name to emit: <paramref name="funcName"/> plus the parameters
    /// hash for parameterized components (<see cref="NazcaStubNaming"/>, issue #783),
    /// so two parameter sets never share one cell.
    /// </param>
    /// <param name="anchorOverride">
    /// Optional cell anchor replacing the calibrated origin offset: the raw-code
    /// fallback stub anchors on <see cref="NazcaCoordinateMapper.GetStubAnchor"/>
    /// (the same value the placement derives its bbox from) because raw-code
    /// components carry no calibrated offset — the plain (0, 0) default would
    /// render the box and its pins one cell height below the placed position.
    /// </param>
    internal static void AppendStandardComponentStub(
        StringBuilder sb, string funcName, string stubName, Component comp, CultureInfo ci,
        (double X, double Y)? anchorOverride = null)
    {
        var w = comp.WidthMicrometers;
        var h = comp.HeightMicrometers;

        // Sanitize stub name for valid Python identifier (replace non-alphanumeric/underscore chars)
        var pythonFuncName = System.Text.RegularExpressions.Regex.Replace(stubName, @"[^a-zA-Z0-9_]", "_");

        // Define cell once, return cached instance on each call
        sb.AppendLine($"with nd.Cell(name='{stubName}') as _{pythonFuncName}_cell:");
        // funcName is PDK-controlled (a raw-code template's Name in fallback mode) —
        // sanitize so a hostile name cannot break out of the docstring.
        sb.AppendLine($"    \"\"\"Auto-generated stub for {SanitizePythonComment(funcName)} ({comp.WidthMicrometers.ToString(ci)}x{comp.HeightMicrometers.ToString(ci)} µm).\"\"\"");

        // Stubs are only generated for PDK-named components (see RequiresStub), whose
        // placement always uses the calibrated origin offset — (0,0) means org at the
        // box top-left. Example: GC with offset (0, 9.5), H=19 → polygon (0,-9.5)..(W,9.5).
        double offsetX = anchorOverride?.X ?? comp.NazcaOriginOffsetX;
        double offsetY = anchorOverride?.Y ?? comp.NazcaOriginOffsetY;

        var px0 = NazcaCoordinateMapper.NormalizeZero(-offsetX).ToString("F2", ci);
        var py0 = NazcaCoordinateMapper.NormalizeZero(offsetY - h).ToString("F2", ci);
        var px1 = NazcaCoordinateMapper.NormalizeZero(w - offsetX).ToString("F2", ci);
        var py1 = NazcaCoordinateMapper.NormalizeZero(offsetY).ToString("F2", ci);

        // Purely electrical components (probe/bond pads, #682) are metal structures —
        // draw their body on the metal layer instead of the waveguide layer.
        var isMetalComponent = comp.PhysicalPins.Count > 0
            && comp.PhysicalPins.All(p => p.MatterType == MatterType.Electricity);
        var bodyLayer = isMetalComponent ? "METAL_LAYER" : "1";

        sb.AppendLine($"    nd.Polygon(points=[({px0},{py0}),({px1},{py0}),({px1},{py1}),({px0},{py1})], layer={bodyLayer}).put(0, 0)");

        // Pins relative to org: local = (UnrotatedOffsetX-ox, oy-UnrotatedOffsetY), the
        // plain Y negation of the app pin offsets (NazcaCoordinateMapper.GetPinNazcaPosition
        // contract). The UNROTATED offsets matter: the cell is rotation-independent
        // (placement applies .put(rot)) and one stub is shared by instances at different
        // rotations — live offsets would bake the first instance's rotation into the
        // shared cell (NazcaCoordinateMapper.GetUnrotatedPinOffset).
        foreach (var pin in comp.PhysicalPins)
        {
            var (pinOffsetX, pinOffsetY) = NazcaCoordinateMapper.GetUnrotatedPinOffset(comp, pin);
            var px = NazcaCoordinateMapper.NormalizeZero(pinOffsetX - offsetX).ToString("F2", ci);
            var py = NazcaCoordinateMapper.NormalizeZero(offsetY - pinOffsetY).ToString("F2", ci);
            var pa = NazcaCoordinateMapper.NormalizeZero(-pin.AngleDegrees).ToString("F0", ci);
            // nd.Pin is an optical Nazca port: electrical pins get no Pin (#519) —
            // but they DO get the port label, or a re-import could never see them
            // (a purely electrical component like a bond pad carries no other pin trace).
            if (pin.MatterType == MatterType.Light)
                sb.AppendLine($"    nd.Pin('{pin.Name}').put({px}, {py}, {pa})");
            // Pin label at the same anchor — re-import detects the named pin there (#808).
            sb.AppendLine($"    nd.Annotation(text='{EscapePythonString(pin.Name)}', layer={PortLabelLayer}).put({px}, {py})");
        }

        sb.AppendLine();
        sb.AppendLine($"def {pythonFuncName}(**kwargs):");
        sb.AppendLine($"    return _{pythonFuncName}_cell");
        sb.AppendLine();
    }
}
