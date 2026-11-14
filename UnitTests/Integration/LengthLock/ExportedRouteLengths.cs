using System.Globalization;
using System.Text.RegularExpressions;
using CAP_Core.Components.Connections;
using CAP_Core.Routing;
using Shouldly;

namespace UnitTests.Integration.LengthLock;

/// <summary>One connection's route as written into the Nazca script.</summary>
/// <param name="LengthMicrometers">Sum of the straights' lengths and the bends' radius × sweep.</param>
/// <param name="StartX">Nazca X of the first call's <c>put</c>.</param>
/// <param name="StartY">Nazca Y of the first call's <c>put</c>.</param>
internal sealed record ExportedRoute(double LengthMicrometers, double StartX, double StartY);

/// <summary>
/// Reads every connection's route length back out of an exported Nazca script — the script is
/// the GDS geometry source, so this is the length the fab gets. The exporter writes one core
/// <c>nd.strt</c> / <c>nd.bend</c> call per routed segment, connection after connection in canvas
/// order (each optionally followed by its process-companion copy, which is skipped here).
/// </summary>
internal static class ExportedRouteLengths
{
    private const string ConnectionsHeader = "# Waveguide Connections";
    private const string CompanionXsectionMarker = "xs='LUNIMA_WG_COMPANIONS_";

    /// <summary>The exporter writes every number with two decimals.</summary>
    private const double HalfLastDigit = 0.005;

    private const string Number = @"-?\d+(?:\.\d+)?";

    private static readonly Regex CallRegex = new(
        $@"^\s*nd\.(?<kind>strt|bend)\((?<args>[^)]*)\)\.put\((?<x>{Number}),\s*(?<y>{Number})",
        RegexOptions.Compiled);

    private static readonly Regex StraightArgsRegex = new($@"length=(?<length>{Number})", RegexOptions.Compiled);

    private static readonly Regex BendArgsRegex = new(
        $@"radius=(?<radius>{Number}),\s*angle=(?<angle>{Number})", RegexOptions.Compiled);

    /// <summary>Splits the script's waveguide calls into one exported route per connection.</summary>
    public static IReadOnlyList<ExportedRoute> Read(string script, IReadOnlyList<WaveguideConnection> connections)
    {
        var calls = CoreCalls(script);
        var routes = new List<ExportedRoute>();
        int next = 0;
        foreach (var connection in connections)
        {
            int count = connection.GetPathSegments().Count;
            count.ShouldBeGreaterThan(0, "every connection must be routed before export");
            (next + count).ShouldBeLessThanOrEqualTo(calls.Count, "the script must hold a call per routed segment");
            var own = calls.GetRange(next, count);
            routes.Add(new ExportedRoute(own.Sum(c => c.Length), own[0].X, own[0].Y));
            next += count;
        }
        next.ShouldBe(calls.Count, "every exported waveguide call must belong to one connection");
        return routes;
    }

    /// <summary>
    /// The most the two-decimal formatting can move a route's exported length away from its canvas
    /// length: half a digit per straight; per bend, half a digit of radius over its sweep plus half a
    /// digit of sweep (degrees) over its radius.
    /// </summary>
    public static double RoundingSlack(WaveguideConnection connection) =>
        connection.GetPathSegments().Sum(segment => segment switch
        {
            BendSegment bend => HalfLastDigit * Math.Abs(bend.SweepAngleDegrees) * Math.PI / 180.0
                + (bend.RadiusMicrometers + HalfLastDigit) * HalfLastDigit * Math.PI / 180.0,
            _ => HalfLastDigit,
        });

    private static List<(double Length, double X, double Y)> CoreCalls(string script)
    {
        int header = script.IndexOf(ConnectionsHeader, StringComparison.Ordinal);
        header.ShouldBeGreaterThanOrEqualTo(0, "the export must contain the waveguide connections");
        var section = script[header..].Split('\n').Skip(1).TakeWhile(line => line.Trim().Length > 0);
        return section
            .Where(line => !line.Contains(CompanionXsectionMarker, StringComparison.Ordinal))
            .Select(line => CallRegex.Match(line))
            .Where(match => match.Success)
            .Select(match => (LengthOf(match), Parse(match.Groups["x"]), Parse(match.Groups["y"])))
            .ToList();
    }

    private static double LengthOf(Match call)
    {
        var args = call.Groups["args"].Value;
        if (call.Groups["kind"].Value == "strt")
        {
            var straight = StraightArgsRegex.Match(args);
            straight.Success.ShouldBeTrue($"a straight call must carry its length: {call.Value}");
            return Parse(straight.Groups["length"]);
        }
        var bend = BendArgsRegex.Match(args);
        bend.Success.ShouldBeTrue($"a bend call must carry radius and angle: {call.Value}");
        return Parse(bend.Groups["radius"]) * Math.Abs(Parse(bend.Groups["angle"])) * Math.PI / 180.0;
    }

    private static double Parse(Group group) => double.Parse(group.Value, CultureInfo.InvariantCulture);
}
