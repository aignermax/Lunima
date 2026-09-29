using CAP.Avalonia.Services;
using CAP_Core.Analysis.LogicAnalysis;
using Shouldly;
using UnitTests.Analysis.LogicAnalysis;
using Xunit;

namespace UnitTests.Services;

/// <summary>
/// The hand-off service between the Logic tab and the ISA playground (issue #1215):
/// publish makes the network visible to consumers and raises <see cref="BuiltLogicNetworkProvider.Changed"/>,
/// clear drops it again (and is a no-op, without an event, when nothing is held).
/// </summary>
public class BuiltLogicNetworkProviderTests
{
    [Fact]
    public void Initially_HoldsNoNetwork()
    {
        new BuiltLogicNetworkProvider().Network.ShouldBeNull();
    }

    [Fact]
    public void Publish_MakesNetworkVisible_AndRaisesChanged()
    {
        var provider = new BuiltLogicNetworkProvider();
        var raised = 0;
        provider.Changed += () => raised++;
        var network = BuildSingleNandNetwork();

        provider.Publish(network);

        provider.Network.ShouldBeSameAs(network);
        raised.ShouldBe(1);
    }

    [Fact]
    public void Clear_AfterPublish_DropsNetwork_AndRaisesChanged()
    {
        var provider = new BuiltLogicNetworkProvider();
        provider.Publish(BuildSingleNandNetwork());
        var raised = 0;
        provider.Changed += () => raised++;

        provider.Clear();

        provider.Network.ShouldBeNull();
        raised.ShouldBe(1);
    }

    [Fact]
    public void Clear_WhenEmpty_IsANoOp_WithoutEvent()
    {
        var provider = new BuiltLogicNetworkProvider();
        var raised = 0;
        provider.Changed += () => raised++;

        provider.Clear();

        provider.Network.ShouldBeNull();
        raised.ShouldBe(0);
    }

    /// <summary>One NAND gate with inputs A, B and output Y — enough to pin publish/clear.</summary>
    private static LogicNetworkEvaluator BuildSingleNandNetwork() =>
        new(
            inputPinNames: new List<string> { "A", "B" },
            gates: new Dictionary<string, LogicGateModel> { ["nand"] = PinnedGateTables.NandGate() },
            inputWiring: new Dictionary<LogicPinRef, LogicNetDriver>
            {
                [new LogicPinRef("nand", "A")] = new LogicNetDriver.NetworkInput("A"),
                [new LogicPinRef("nand", "B")] = new LogicNetDriver.NetworkInput("B"),
            },
            outputTaps: new Dictionary<string, LogicPinRef> { ["Y"] = new LogicPinRef("nand", "Y") });
}
