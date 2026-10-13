using CAP.Avalonia.Controls.Canvas.CutTool;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Routing;
using CAP_Core.Routing.CrossingInsertion;
using CAP_Core.Routing.CrossingInsertion.ManualCrossing;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Repair.Cut;

/// <summary>Where a crossing could be inserted, and which wire would then pass through it.</summary>
/// <param name="Kind"><c>wire_intersection</c> (two wires cross here — for a blocked wire, its
/// straight placeholder line) or <c>pin_guide</c> (the Cut tool's guide line from a pin meets a wire).</param>
/// <param name="Cut">The wire that is split, and where.</param>
/// <param name="Point">The crossing centre in µm.</param>
/// <param name="Through">The wire that would be re-routed through the crossing's free ports, or null.</param>
/// <param name="GuidePin">The pin whose guide line produced a <c>pin_guide</c> spot.</param>
/// <param name="Candidate">The Cut-tool candidate to insert, or null when a crossing does not fit.</param>
/// <param name="Reason">Why a crossing does not fit (null when it does).</param>
public sealed record CutSpot(
    string Kind, WaveguideConnection Cut, (double X, double Y) Point, WaveguideConnection? Through,
    PhysicalPin? GuidePin, ManualCrossingCandidate? Candidate, string? Reason)
{
    /// <summary>Whether a crossing fits (enough straight run each side, clear footprint).</summary>
    public bool Fits => Candidate != null;
}

/// <summary>
/// Finds crossing spots on the whole design (no viewport) by reusing the Cut tool's own
/// candidate logic: <see cref="ManualCrossingCandidateFinder"/> for pin-guide intersections and
/// free cuts, and <see cref="CutToolCandidateComputer.CollectEligibleConnections"/> for which
/// wires may be split. Wire-wire intersections are added on top: where two wires (or a blocked
/// wire's placeholder line and a routed wire) cross, a crossing lets one pass straight through
/// the other — the usual way to swap the order of two wires.
/// </summary>
public sealed class CutSpotFinder
{
    private readonly ManualCrossingCandidateFinder _finder = new();
    private readonly AgentDesignSession _session;
    private readonly CrossingSupply _supply;

    /// <summary>Creates the finder for the open design.</summary>
    public CutSpotFinder(AgentDesignSession session, CrossingSupply supply)
    {
        _session = session;
        _supply = supply;
    }

    /// <summary>All spots, wire intersections first, optionally only those touching <paramref name="focus"/>.</summary>
    public IReadOnlyList<CutSpot> Find(WaveguideConnection? focus = null)
    {
        var eligible = CutToolCandidateComputer.CollectEligibleConnections(_session.Canvas)
            .Where(c => !c.IsElectrical).ToList();
        var spots = WireIntersections(eligible).Concat(PinGuideSpots(eligible));
        return focus == null
            ? spots.ToList()
            : spots.Where(s => s.Cut == focus || s.Through == focus).ToList();
    }

    /// <summary>A spot on <paramref name="cut"/> at the point nearest <paramref name="near"/> (a free cut).</summary>
    public CutSpot? FreeSpot(WaveguideConnection cut, (double X, double Y) near, WaveguideConnection? through)
    {
        var segment = cut.GetPathSegments().OfType<StraightSegment>()
            .OrderBy(s => DistanceToSegment(s, near)).FirstOrDefault();
        if (segment == null)
            return null;
        return BuildSpot("free", cut, segment, near, through, null);
    }

    private IEnumerable<CutSpot> WireIntersections(IReadOnlyList<WaveguideConnection> eligible)
    {
        foreach (var cut in eligible.Where(c => !c.IsBlockedFallback))
            foreach (var segment in cut.GetPathSegments().OfType<StraightSegment>())
                foreach (var through in eligible.Where(t => t != cut))
                    foreach (var other in through.GetPathSegments().OfType<StraightSegment>())
                    {
                        if (!CrossingGeometry.TryGetIntersection(segment, other, out var point))
                            continue;
                        yield return BuildSpot("wire_intersection", cut, segment, point, through, null);
                    }
    }

    private IEnumerable<CutSpot> PinGuideSpots(IReadOnlyList<WaveguideConnection> eligible)
    {
        var pins = _session.Canvas.Components.SelectMany(vm => vm.Component.PhysicalPins);
        var guides = _finder.BuildGuideLines(pins);
        var candidates = _finder.FindCandidates(guides, eligible, _supply.RequiredRunMicrometers, _supply.Footprint);
        foreach (var candidate in candidates)
        {
            var pin = candidate.GuideLine!.Pin;
            var through = eligible.FirstOrDefault(c => c.StartPin == pin || c.EndPin == pin);
            yield return new CutSpot("pin_guide", candidate.Connection, candidate.IntersectionPoint,
                through, pin, candidate, null);
        }
    }

    private CutSpot BuildSpot(string kind, WaveguideConnection cut, StraightSegment segment,
        (double X, double Y) point, WaveguideConnection? through, PhysicalPin? guidePin)
    {
        var candidate = _finder.TryCreateFreeCandidate(cut, segment, point, _supply.RequiredRunMicrometers);
        if (candidate == null)
            return new CutSpot(kind, cut, point, through, guidePin, null,
                "not on a straight, axis-aligned run long enough for the crossing");
        if (!_supply.IsFootprintClear(candidate.IntersectionPoint, cut, through))
            return new CutSpot(kind, cut, candidate.IntersectionPoint, through, guidePin, null,
                "the crossing's footprint would overlap other geometry");
        return new CutSpot(kind, cut, candidate.IntersectionPoint, through, guidePin, candidate, null);
    }

    private static double DistanceToSegment(StraightSegment segment, (double X, double Y) point)
    {
        var (ax, ay) = segment.StartPoint;
        var (bx, by) = segment.EndPoint;
        double dx = bx - ax, dy = by - ay;
        double lengthSquared = dx * dx + dy * dy;
        double t = lengthSquared < 1e-12 ? 0 : Math.Clamp(((point.X - ax) * dx + (point.Y - ay) * dy) / lengthSquared, 0, 1);
        double px = ax + t * dx - point.X, py = ay + t * dy - point.Y;
        return Math.Sqrt(px * px + py * py);
    }
}
