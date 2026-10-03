using System.Globalization;
using System.Text;
using CAP_Core.Components.Core;
using CAP_Core.Export;
using CAP_Core.Routing;

namespace CAP.Avalonia.Services;

/// <summary>
/// Emits the SiEPIC-conformant waveguide cells of an EBeam-only export (openEBL
/// gap #4). Every routed optical connection becomes its own cell
/// (<c>Waveguide_&lt;n&gt;</c>) placed at the origin of the top cell: the routed
/// geometry (Si polygons on 1/0) is written inside the cell by the nazca script,
/// and a klayout post-pass (<c>_lunima_add_waveguide_spines</c>, running after the
/// foundry-cell upgrade of <see cref="SiepicCellUpgradeWriter"/>) adds the SiEPIC
/// conventions SiEPIC-Tools' <c>layout_check</c> / <c>identify_nets</c> expects —
/// modelled on the EBeam <c>Waveguide</c> PCell output:
/// <list type="bullet">
/// <item>a Waveguide (1/99) guide along the route centreline, stored as the path's
/// outline polygon exactly like the PCell (a &gt;2-point PATH on 1/99 trips the
/// "Waveguide: Path" rule),</item>
/// <item>a DevRec (68/0) guide-outline polygon ending exactly at the pin
/// centres, so it touches — never overlaps — the partner components' DevRec
/// (a plain bbox would falsely overlap waveguides passing through a long
/// detour route's bbox),</item>
/// <item>at each end a PinRec (1/10) optical pin: the exact REVERSED copy of the
/// matched component pin's 2-point path (identical centre, 180°-opposite
/// direction — the two conditions <c>identify_nets</c> requires for a net) plus an
/// <c>opt1</c>/<c>opt2</c> label inside the pin bbox.</item>
/// </list>
/// The spine endpoints are snapped in the klayout pass to the REAL foundry pin
/// centres read back from the upgraded GDS (the exported coordinates are
/// F2-rounded, but net detection compares integer pin centres for equality).
/// Only created for EBeam-only designs (<see cref="SiepicEBeamExportProfile"/>);
/// every other export stays byte-identical.
/// </summary>
internal sealed class SiepicWaveguideCellWriter
{
    /// <summary>Arc-length step for sampling bend centrelines into the spine polyline (µm).</summary>
    private const double SpineArcStepMicrometers = 0.25;

    private readonly double _widthMicrometers;
    private readonly List<(string CellName, string PointsLiteral)> _spines = new();
    private int _nextIndex;

    public SiepicWaveguideCellWriter(double widthMicrometers)
    {
        _widthMicrometers = widthMicrometers;
    }

    /// <summary>
    /// Wraps one routed connection's already-generated segment lines in its own
    /// nazca cell placed at the top-cell origin (the segment coordinates are
    /// absolute, so an origin placement keeps them exactly), and records the
    /// route's centreline for the spine post-pass.
    /// </summary>
    public void AppendWaveguideCell(
        StringBuilder sb, StringBuilder cellContent, IReadOnlyList<PathSegment> segments,
        PhysicalPin? startPin, PhysicalPin? endPin)
    {
        var index = _nextIndex++;
        var cellName = $"Waveguide_{index}";
        sb.AppendLine($"        with nd.Cell(name='{cellName}') as waveguide_{index}:");
        foreach (var line in cellContent.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries))
            sb.AppendLine("    " + line.TrimEnd('\r'));
        sb.AppendLine($"        waveguide_{index}.put(0, 0)");

        var points = BuildSpinePoints(segments, startPin, endPin);
        if (points.Count >= 2)
        {
            var ci = CultureInfo.InvariantCulture;
            var literal = string.Join(", ", points.Select(
                p => $"({NazcaCoordinateMapper.NormalizeZero(p.X).ToString("F2", ci)}, " +
                     $"{NazcaCoordinateMapper.NormalizeZero(p.Y).ToString("F2", ci)})"));
            _spines.Add((cellName, literal));
        }
    }

    /// <summary>
    /// Appends the klayout post-pass that adds the Waveguide (1/99) guide, the
    /// DevRec (68/0) envelope and the PinRec (1/10) pins to every emitted
    /// waveguide cell. Must run AFTER the foundry-cell upgrade pass: the pin
    /// matching reads the real foundry PinRec paths from the upgraded GDS.
    /// Emits nothing when no waveguide cell was emitted.
    /// </summary>
    public void AppendSpineBlock(StringBuilder sb)
    {
        if (_spines.Count == 0)
            return;
        var ci = CultureInfo.InvariantCulture;
        var entries = string.Join(", ", _spines.Select(
            s => $"'{s.CellName}': {{'width': {_widthMicrometers.ToString("0.0###", ci)}, 'points': [{s.PointsLiteral}]}}"));
        sb.AppendLine();
        sb.AppendLine("# --- Lunima: SiEPIC waveguide spines (Waveguide 1/99 + DevRec + PinRec pins) ---");
        sb.AppendLine(PythonBlock);
        sb.AppendLine($"_lunima_add_waveguide_spines(gds_filename, {{{entries}}})");
        sb.AppendLine();
    }

    /// <summary>
    /// The route centreline as a polyline in nazca coordinates (Y negated),
    /// endpoints snapped to the pins' world positions so the spine lands exactly
    /// where the exported single-straight geometry lands. Consecutive duplicate
    /// points (segment joints, bend sample endpoints) are dropped.
    /// </summary>
    private static List<(double X, double Y)> BuildSpinePoints(
        IReadOnlyList<PathSegment> segments, PhysicalPin? startPin, PhysicalPin? endPin)
    {
        var points = new List<(double X, double Y)>();
        foreach (var segment in segments)
        {
            IEnumerable<(double X, double Y)> segmentPoints = segment switch
            {
                StraightSegment straight => new[] { straight.StartPoint, straight.EndPoint },
                BendSegment bend => ArcSampling.SamplePoints(bend, SpineArcStepMicrometers),
                _ => Enumerable.Empty<(double X, double Y)>(),
            };
            foreach (var point in segmentPoints)
            {
                if (points.Count > 0 && NearlyEqual(points[^1], point))
                    continue;
                points.Add(point);
            }
        }
        if (points.Count == 0)
            return points;
        if (startPin != null)
            points[0] = startPin.GetAbsolutePosition();
        if (endPin != null)
            points[^1] = endPin.GetAbsolutePosition();
        return points.Select(p => NazcaCoordinateMapper.ToNazca(p.X, p.Y)).ToList();
    }

    private static bool NearlyEqual((double X, double Y) a, (double X, double Y) b) =>
        Math.Abs(a.X - b.X) < 1e-9 && Math.Abs(a.Y - b.Y) < 1e-9;

    /// <summary>
    /// klayout pass that turns each emitted waveguide cell into a SiEPIC-conformant
    /// waveguide component. Pin matching reads the component cells' REAL PinRec
    /// (1/10) 2-point paths in top-cell coordinates (i.e. after the foundry
    /// upgrade): the closest pin within a 2 µm snap radius of a spine endpoint is
    /// the route's partner, and the waveguide's own pin is that pin's exact
    /// reversed copy — identical integer centre and 180°-opposite
    /// direction, the two conditions <c>identify_nets</c> nets a connection on.
    /// An endpoint without a pin in range is left as-is and reported on stderr
    /// (the pin then honestly stays "Disconnected pin" in the verification).
    /// </summary>
    private const string PythonBlock = """
def _lunima_add_waveguide_spines(gds_path, spines):
    import sys as _sys
    try:
        import klayout.db as _kdb
        _ly = _kdb.Layout()
        _ly.read(gds_path)
        _top = next(iter(_ly.top_cells()), None)
        if _top is None:
            return

        def _layer(_l, _d):
            _lp = _kdb.LayerInfo(_l, _d)
            _li = _ly.find_layer(_lp)
            return _li if _li is not None else _ly.layer(_lp)

        _li_wg = _layer(1, 99)   # Waveguide guide
        _li_dr = _layer(68, 0)   # DevRec
        _li_pr = _layer(1, 10)   # PinRec
        _dbu = _ly.dbu

        # The components' optical pins: PinRec 2-point paths in top-cell space
        # (a PinRec path points OUT of its component), excluding the waveguide
        # cells this pass created.
        _comp_pins = []
        _it = _top.begin_shapes_rec(_li_pr)
        while not _it.at_end():
            _s = _it.shape()
            if _s.is_path() and _it.cell().name not in spines:
                _p = _s.path.transformed(_it.itrans())
                _pts = list(_p.each_point())
                if len(_pts) == 2:
                    _comp_pins.append((_pts[0], _pts[1], _p.width))
            _it.next()

        _added = 0
        for _name, _spec in spines.items():
            _c = _ly.cell(_name)
            if _c is None:
                continue
            _width = _spec['width']
            _pts = [_kdb.DPoint(_x, _y) for _x, _y in _spec['points']]
            if len(_pts) < 2:
                continue
            # Snap each spine end onto its partner pin and take an exact reversed
            # copy of that pin's path as the waveguide's own PinRec pin.
            _pins_here = []
            for _end in (0, len(_pts) - 1):
                _best = None
                _best_dist = 2.0  # µm — covers export coordinate rounding only
                for _p0, _p1, _pw in _comp_pins:
                    _cx = (_p0.x + _p1.x) / 2.0 * _dbu
                    _cy = (_p0.y + _p1.y) / 2.0 * _dbu
                    _dist = ((_cx - _pts[_end].x) ** 2 + (_cy - _pts[_end].y) ** 2) ** 0.5
                    if _dist < _best_dist:
                        _best_dist = _dist
                        _best = (_p0, _p1, _pw)
                if _best is None:
                    print(f"[Lunima] WARN: no component pin within snap radius of {_name} end "
                          f"({_pts[_end].x}, {_pts[_end].y}); that pin stays disconnected.", file=_sys.stderr)
                    continue
                _p0, _p1, _pw = _best
                _pts[_end] = _kdb.DPoint((_p0.x + _p1.x) / 2.0 * _dbu, (_p0.y + _p1.y) / 2.0 * _dbu)
                _pins_here.append((_p1, _p0, _pw))

            # Waveguide (1/99): the centreline path's outline polygon — the shape
            # the SiEPIC Waveguide PCell stores (a >2-point PATH on 1/99 trips
            # layout_check's "Waveguide: Path" rule).
            _guide = _kdb.DPath(_pts, _width).polygon()
            _c.shapes(_li_wg).insert(_guide)
            # DevRec: the same guide-outline polygon (a bbox would falsely overlap
            # the waveguides passing through a long detour route's bbox). It ends
            # exactly at the pin centres, so it touches (never overlaps) the
            # partner components' DevRec.
            _c.shapes(_li_dr).insert(_guide)
            # PinRec pins + labels (opt1/opt2, inside the pin bbox).
            for _i, (_q0, _q1, _qw) in enumerate(_pins_here):
                _c.shapes(_li_pr).insert(_kdb.Path([_q0, _q1], _qw))
                _mx = (_q0.x + _q1.x) // 2
                _my = (_q0.y + _q1.y) // 2
                _c.shapes(_li_pr).insert(_kdb.Text('opt%d' % (_i + 1), _kdb.Trans(_kdb.Point(_mx, _my))))
            _added += 1

        if _added:
            import os as _os
            _tmp = _os.path.splitext(gds_path)[0] + '.tmp.gds'  # .gds suffix — klayout sniffs the format from the extension
            _ly.write(_tmp)
            _os.replace(_tmp, gds_path)  # atomic — a failed write never truncates the export
            print(f"[Lunima] {_added} waveguide cell(s) received a Waveguide (1/99) guide, DevRec and PinRec pins.")
    except Exception as _exc:
        print(f"[Lunima] WARN: Waveguide spine emission skipped ({_exc}).", file=_sys.stderr)

""";
}
