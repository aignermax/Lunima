using System.Globalization;
using System.Text.RegularExpressions;

namespace CAP_Core.CodeExporter;

/// <summary>
/// Parses Nazca Python code to extract component placements, waveguide segments, and pin positions.
/// Used for end-to-end validation of the export pipeline.
/// </summary>
public class NazcaCodeParser
{
    // Component placement: optionally accepts a leading anchor-pin string
    // (e.g. ``.put('org', x, y, angle)``) which Lunima now emits to force
    // Nazca to anchor on the cell origin instead of its default first-pin
    // anchor. The anchor argument is captured but not exposed in the
    // parsed result — downstream consumers only need the (x, y, angle).
    private static readonly Regex ComponentPlacementRegex = new(
        @"^\s*(\w+)\s*=\s*([^\s\(]+(?:\([^\)]*\))?)\s*\.put\((?:'[^']*',\s*)?(-?\d+(?:\.\d+)?),\s*(-?\d+(?:\.\d+)?),\s*(-?\d+(?:\.\d+)?)\)",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex WaveguideStrtRegex = new(
        @"nd\.strt\(length=(-?\d+(?:\.\d+)?)\)\.put\((-?\d+(?:\.\d+)?),\s*(-?\d+(?:\.\d+)?),\s*(-?\d+(?:\.\d+)?)\)",
        RegexOptions.Compiled);

    private static readonly Regex WaveguideBendRegex = new(
        @"nd\.bend\(radius=(-?\d+(?:\.\d+)?),\s*angle=(-?\d+(?:\.\d+)?)\)\.put\((-?\d+(?:\.\d+)?),\s*(-?\d+(?:\.\d+)?),\s*(-?\d+(?:\.\d+)?)\)",
        RegexOptions.Compiled);

    private static readonly Regex PinDefinitionRegex = new(
        @"nd\.Pin\('([^']+)'\)\.put\((-?\d+(?:\.\d+)?),\s*(-?\d+(?:\.\d+)?),\s*(-?\d+(?:\.\d+)?)\)",
        RegexOptions.Compiled);

    /// <summary>
    /// Parses Nazca Python code and extracts structured design data.
    /// </summary>
    public ParsedNazcaDesign Parse(string nazcaCode)
    {
        var components = ParseComponentPlacements(nazcaCode);
        var waveguideStubs = ParseWaveguideStubs(nazcaCode);
        var pins = ParsePinDefinitions(nazcaCode);

        return new ParsedNazcaDesign
        {
            Components = components,
            WaveguideStubs = waveguideStubs,
            PinDefinitions = pins
        };
    }

    /// <summary>
    /// Extracts component placement data from Nazca code.
    /// </summary>
    private List<ComponentPlacement> ParseComponentPlacements(string nazcaCode)
    {
        var placements = new List<ComponentPlacement>();
        var ci = CultureInfo.InvariantCulture;

        var matches = ComponentPlacementRegex.Matches(nazcaCode);
        foreach (Match match in matches)
        {
            var varName = match.Groups[1].Value;
            var functionName = match.Groups[2].Value.Trim();
            var x = double.Parse(match.Groups[3].Value, ci);
            var y = double.Parse(match.Groups[4].Value, ci);
            var rotation = double.Parse(match.Groups[5].Value, ci);

            placements.Add(new ComponentPlacement
            {
                VariableName = varName,
                FunctionName = functionName,
                X = x,
                Y = y,
                RotationDegrees = rotation
            });
        }

        return placements;
    }

    /// <summary>
    /// Extracts waveguide stub positions (first segment of each waveguide with absolute coordinates).
    /// </summary>
    private List<WaveguideStub> ParseWaveguideStubs(string nazcaCode)
    {
        var stubs = new List<WaveguideStub>();
        var ci = CultureInfo.InvariantCulture;

        // Parse straight waveguide stubs
        var strtMatches = WaveguideStrtRegex.Matches(nazcaCode);
        foreach (Match match in strtMatches)
        {
            var length = double.Parse(match.Groups[1].Value, ci);
            var x = double.Parse(match.Groups[2].Value, ci);
            var y = double.Parse(match.Groups[3].Value, ci);
            var angle = double.Parse(match.Groups[4].Value, ci);

            stubs.Add(new WaveguideStub
            {
                StartX = x,
                StartY = y,
                StartAngle = angle,
                Length = length,
                Type = "straight"
            });
        }

        // Parse bend waveguide stubs
        var bendMatches = WaveguideBendRegex.Matches(nazcaCode);
        foreach (Match match in bendMatches)
        {
            var radius = double.Parse(match.Groups[1].Value, ci);
            var sweepAngle = double.Parse(match.Groups[2].Value, ci);
            var x = double.Parse(match.Groups[3].Value, ci);
            var y = double.Parse(match.Groups[4].Value, ci);
            var angle = double.Parse(match.Groups[5].Value, ci);

            stubs.Add(new WaveguideStub
            {
                StartX = x,
                StartY = y,
                StartAngle = angle,
                Radius = radius,
                SweepAngle = sweepAngle,
                Type = "bend"
            });
        }

        return stubs;
    }

    /// <summary>
    /// Extracts pin definitions from component stub definitions.
    /// </summary>
    private List<PinDefinition> ParsePinDefinitions(string nazcaCode)
    {
        var pins = new List<PinDefinition>();
        var ci = CultureInfo.InvariantCulture;

        var matches = PinDefinitionRegex.Matches(nazcaCode);
        foreach (Match match in matches)
        {
            var name = match.Groups[1].Value;
            var x = double.Parse(match.Groups[2].Value, ci);
            var y = double.Parse(match.Groups[3].Value, ci);
            var angle = double.Parse(match.Groups[4].Value, ci);

            pins.Add(new PinDefinition
            {
                Name = name,
                X = x,
                Y = y,
                AngleDegrees = angle
            });
        }

        return pins;
    }
}

/// <summary>
/// Represents a parsed Nazca design with extracted data.
/// </summary>
public class ParsedNazcaDesign
{
    public List<ComponentPlacement> Components { get; init; } = new();
    public List<WaveguideStub> WaveguideStubs { get; init; } = new();
    public List<PinDefinition> PinDefinitions { get; init; } = new();
}

/// <summary>
/// Represents a component placement in Nazca code.
/// </summary>
public class ComponentPlacement
{
    public string VariableName { get; init; } = "";
    public string FunctionName { get; init; } = "";
    public double X { get; init; }
    public double Y { get; init; }
    public double RotationDegrees { get; init; }
}

/// <summary>
/// Represents a waveguide stub (first segment with absolute coordinates).
/// </summary>
public class WaveguideStub
{
    public double StartX { get; init; }
    public double StartY { get; init; }
    public double StartAngle { get; init; }
    public double Length { get; init; }
    public double Radius { get; init; }
    public double SweepAngle { get; init; }
    public string Type { get; init; } = ""; // "straight" or "bend"
}

/// <summary>
/// Represents a pin definition in a component stub.
/// </summary>
public class PinDefinition
{
    public string Name { get; init; } = "";
    public double X { get; init; }
    public double Y { get; init; }
    public double AngleDegrees { get; init; }
}
