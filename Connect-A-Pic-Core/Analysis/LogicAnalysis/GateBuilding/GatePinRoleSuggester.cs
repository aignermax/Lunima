using System.Globalization;
using CAP_Core.Components.Core;
using CAP_Core.Components.GroupPins;

namespace CAP_Core.Analysis.LogicAnalysis.GateBuilding;

/// <summary>
/// Suggests a gate role and a short name for every external pin of a group, so turning
/// a grouped circuit into a gate starts from a sensible proposal instead of a blank
/// form. The heuristic reads the circuit the way it is drawn — light flows left to right:
/// <list type="number">
/// <item>Electrical pins and pins without a simulatable optical pin are <b>unused</b>.</item>
/// <item>A west-facing pin of a phase shifter is a <b>bias</b> input (the constant
/// reference light an inverting gate tunes with its phase), wherever it sits.</item>
/// <item>A west-facing pin on the group's west edge, or one fed by a canvas connection
/// from outside, is an <b>input</b> (at most <see cref="TruthTableExtractor.MaxLogicInputs"/>,
/// top to bottom).</item>
/// <item>An east-facing pin on the group's east edge, or one wired onwards, is an
/// <b>output</b>.</item>
/// <item>Everything else — the unused drop ports of couplers inside the circuit,
/// north/south pins — is <b>unused</b>.</item>
/// </list>
/// Names: inputs A, B, C, D top to bottom; one output Y (several: Y1, Y2…); biases
/// BIAS, BIAS2… left to right. The user can override every suggestion.
/// </summary>
public sealed class GatePinRoleSuggester
{
    /// <summary>A pin within this distance of the outermost optical pin counts as on the edge (µm).</summary>
    public const double EdgeToleranceMicrometers = 5.0;

    /// <summary>Base name of suggested bias pins.</summary>
    public const string BiasName = "BIAS";

    /// <summary>Name of a single suggested output.</summary>
    public const string OutputName = "Y";

    private static readonly string[] PhaseShifterMarkers = { "phase", "eopm" };

    /// <summary>Suggests roles and names for every external pin of <paramref name="group"/>.</summary>
    /// <param name="group">The group to turn into a gate.</param>
    /// <param name="externallyConnectedPins">Names of external pins wired to something outside the group.</param>
    /// <returns>One suggestion per external pin, in the group's pin order.</returns>
    public IReadOnlyList<GatePinSuggestion> Suggest(
        ComponentGroup group, IReadOnlySet<string>? externallyConnectedPins = null)
    {
        ArgumentNullException.ThrowIfNull(group);
        var connected = externallyConnectedPins ?? new HashSet<string>();
        var optical = group.ExternalPins.Where(IsOptical).ToList();
        if (optical.Count == 0)
            return group.ExternalPins.Select(Unused).ToList();

        var westEdge = optical.Min(p => p.RelativeX);
        var eastEdge = optical.Max(p => p.RelativeX);
        var roles = group.ExternalPins.ToDictionary(
            pin => pin, pin => RoleOf(pin, westEdge, eastEdge, connected));
        CapInputs(roles);
        return BuildSuggestions(group.ExternalPins, roles);
    }

    /// <summary>The raw (uncapped) role of one pin.</summary>
    private static GatePinRole RoleOf(GroupPin pin, double westEdge, double eastEdge, IReadOnlySet<string> connected)
    {
        if (!IsOptical(pin))
            return GatePinRole.Unused;
        var facing = PinFacingClassifier.Classify(pin.AngleDegrees);
        if (facing == PinFacing.West && IsPhaseShifter(pin.InternalPin.ParentComponent))
            return GatePinRole.Bias;
        if (facing == PinFacing.West && (pin.RelativeX <= westEdge + EdgeToleranceMicrometers || connected.Contains(pin.Name)))
            return GatePinRole.Input;
        if (facing == PinFacing.East && (pin.RelativeX >= eastEdge - EdgeToleranceMicrometers || connected.Contains(pin.Name)))
            return GatePinRole.Output;
        return GatePinRole.Unused;
    }

    /// <summary>Keeps the top-most inputs within the extractor's input limit; the rest become unused.</summary>
    private static void CapInputs(Dictionary<GroupPin, GatePinRole> roles)
    {
        var surplus = roles.Where(r => r.Value == GatePinRole.Input)
            .Select(r => r.Key)
            .OrderBy(p => p.RelativeY).ThenBy(p => p.RelativeX)
            .Skip(TruthTableExtractor.MaxLogicInputs)
            .ToList();
        foreach (var pin in surplus)
            roles[pin] = GatePinRole.Unused;
    }

    private static List<GatePinSuggestion> BuildSuggestions(
        IReadOnlyList<GroupPin> pins, Dictionary<GroupPin, GatePinRole> roles)
    {
        var names = new Dictionary<GroupPin, string>();
        NameInOrder(roles, GatePinRole.Input, VerticalOrder, (index, _) => ((char)('A' + index)).ToString());
        NameInOrder(roles, GatePinRole.Output, VerticalOrder, (index, count) => count == 1 ? OutputName : Numbered(OutputName, index + 1));
        NameInOrder(roles, GatePinRole.Bias, HorizontalOrder, (index, _) => index == 0 ? BiasName : Numbered(BiasName, index + 1));
        return pins.Select(pin => new GatePinSuggestion(
            pin.Name, roles[pin], names.TryGetValue(pin, out var name) ? name : pin.Name)).ToList();

        void NameInOrder(
            Dictionary<GroupPin, GatePinRole> all, GatePinRole role,
            Func<IEnumerable<GroupPin>, IEnumerable<GroupPin>> order, Func<int, int, string> nameAt)
        {
            var members = order(all.Where(r => r.Value == role).Select(r => r.Key)).ToList();
            for (var i = 0; i < members.Count; i++)
                names[members[i]] = nameAt(i, members.Count);
        }
    }

    private static IEnumerable<GroupPin> VerticalOrder(IEnumerable<GroupPin> pins) =>
        pins.OrderBy(p => p.RelativeY).ThenBy(p => p.RelativeX);

    private static IEnumerable<GroupPin> HorizontalOrder(IEnumerable<GroupPin> pins) =>
        pins.OrderBy(p => p.RelativeX).ThenBy(p => p.RelativeY);

    private static string Numbered(string baseName, int number) =>
        baseName + number.ToString(CultureInfo.InvariantCulture);

    private static GatePinSuggestion Unused(GroupPin pin) => new(pin.Name, GatePinRole.Unused, pin.Name);

    /// <summary>True for pins that can carry light through the simulation.</summary>
    private static bool IsOptical(GroupPin pin) =>
        pin.InternalPin?.LogicalPin != null && pin.InternalPin.MatterType != MatterType.Electricity;

    /// <summary>Recognizes a phase shifter by its template, display or Nazca function name.</summary>
    private static bool IsPhaseShifter(Component? component)
    {
        if (component == null)
            return false;
        var names = new[] { component.TemplateName, component.HumanReadableName, component.NazcaFunctionName };
        return names.Any(name => name != null
            && PhaseShifterMarkers.Any(marker => name.Contains(marker, StringComparison.OrdinalIgnoreCase)));
    }
}
