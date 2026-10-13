using CAP_Core.Analysis.LogicAnalysis.GateBuilding;
using CAP_Core.Components.Core;
using Shouldly;
using Xunit;

namespace UnitTests.Analysis.LogicAnalysis.MakeGate;

/// <summary>
/// The pin-role heuristic of <see cref="GatePinRoleSuggester"/> on the shipped gate
/// examples: west edge → input (A, B…), phase-shifter input → bias, east edge →
/// output (Y), interior coupler drop ports and electrical pins → unused.
/// </summary>
public class GatePinRoleSuggesterTests
{
    private readonly GatePinRoleSuggester _suggester = new();

    [Fact]
    public async Task ShippedNandGroup_SuggestsItsPersistedRoles()
    {
        var canvas = await GateKitFixture.LoadAsync(GateKitFixture.NotNandExample);
        var group = canvas.Components.Select(c => c.Component).OfType<ComponentGroup>().Single();

        var roles = _suggester.Suggest(group).ToDictionary(s => s.PinName);

        roles["A"].Role.ShouldBe(GatePinRole.Input);
        roles["B"].Role.ShouldBe(GatePinRole.Input);
        roles["BIAS"].Role.ShouldBe(GatePinRole.Bias);
        roles["Y"].Role.ShouldBe(GatePinRole.Output);
    }

    [Fact]
    public async Task RegroupedNandParts_SuggestInputsBiasOutput_AndSkipSparePorts()
    {
        var (_, groupVm) = await GateKitFixture.RegroupedAsync(GateKitFixture.NotNandExample);
        var group = (ComponentGroup)groupVm.Component;

        var suggestions = _suggester.Suggest(group);
        Suggestion(suggestions, group, "input_a", "a0").ShouldBe((GatePinRole.Input, "A"));
        Suggestion(suggestions, group, "input_b", "a0").ShouldBe((GatePinRole.Input, "B"));
        Suggestion(suggestions, group, "phase", "in").ShouldBe((GatePinRole.Bias, "BIAS"));
        Suggestion(suggestions, group, "output", "b0").ShouldBe((GatePinRole.Output, "Y"));
        Suggestion(suggestions, group, "combine", "out2").Role.ShouldBe(GatePinRole.Unused,
            "a coupler's spare drop port inside the circuit is not part of the gate");
        Suggestion(suggestions, group, "recombine", "out2").Role.ShouldBe(GatePinRole.Unused);
        Suggestion(suggestions, group, "phase", "elec1").Role.ShouldBe(GatePinRole.Unused,
            "electrical pins carry no light");
    }

    [Fact]
    public async Task RegroupedAndParts_SuggestTwoBiasPinsLeftToRight()
    {
        var (_, groupVm) = await GateKitFixture.RegroupedAsync(GateKitFixture.AndExample);
        var group = (ComponentGroup)groupVm.Component;

        var suggestions = _suggester.Suggest(group);
        Suggestion(suggestions, group, "input_a", "a0").ShouldBe((GatePinRole.Input, "A"));
        Suggestion(suggestions, group, "input_b", "a0").ShouldBe((GatePinRole.Input, "B"));
        Suggestion(suggestions, group, "phase_nand", "in").ShouldBe((GatePinRole.Bias, "BIAS"));
        Suggestion(suggestions, group, "phase_inv", "in").ShouldBe((GatePinRole.Bias, "BIAS2"),
            "a phase-shifter input is a bias even inside the circuit");
        Suggestion(suggestions, group, "output", "b0").ShouldBe((GatePinRole.Output, "Y"));
        suggestions.Count(s => s.Role == GatePinRole.Input).ShouldBe(2);
        suggestions.Count(s => s.Role == GatePinRole.Output).ShouldBe(1);
    }

    [Fact]
    public async Task GateKit_SuggestsNandInterface()
    {
        var (_, groupVm) = await GateKitFixture.GroupedKitAsync();
        var group = (ComponentGroup)groupVm.Component;

        var suggestions = _suggester.Suggest(group);
        Suggestion(suggestions, group, "combine", "in1").ShouldBe((GatePinRole.Input, "A"));
        Suggestion(suggestions, group, "combine", "in2").ShouldBe((GatePinRole.Input, "B"));
        Suggestion(suggestions, group, "phase", "in").ShouldBe((GatePinRole.Bias, "BIAS"));
        Suggestion(suggestions, group, "output", "b0").ShouldBe((GatePinRole.Output, "Y"));
        suggestions.Count(s => s.Role != GatePinRole.Unused).ShouldBe(4);
    }

    [Fact]
    public async Task ExternallyConnectedWestPin_IsAnInputEvenInsideTheCircuit()
    {
        var (_, groupVm) = await GateKitFixture.RegroupedAsync(GateKitFixture.NotNandExample);
        var group = (ComponentGroup)groupVm.Component;
        var spare = GateKitFixture.PinExposing(group, "recombine", "out2");

        var plain = _suggester.Suggest(group).Single(s => s.PinName == spare.Name);
        var wired = _suggester.Suggest(group, new HashSet<string> { spare.Name }).Single(s => s.PinName == spare.Name);

        plain.Role.ShouldBe(GatePinRole.Unused);
        wired.Role.ShouldBe(GatePinRole.Output, "an east pin wired onwards carries the gate's output");
    }

    [Fact]
    public void GroupWithoutOpticalPins_SuggestsAllUnused()
    {
        var group = new ComponentGroup("empty");

        _suggester.Suggest(group).ShouldBeEmpty();
    }

    private static (GatePinRole Role, string Name) Suggestion(
        IReadOnlyList<GatePinSuggestion> suggestions, ComponentGroup group, string componentId, string pinName)
    {
        var pin = GateKitFixture.PinExposing(group, componentId, pinName);
        var suggestion = suggestions.Single(s => s.PinName == pin.Name);
        return (suggestion.Role, suggestion.SuggestedName);
    }
}
