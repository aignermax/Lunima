using CAP_Core.Analysis.LogicAnalysis;
using CAP_Core.Logic.Isa;
using Shouldly;
using UnitTests.Analysis.LogicAnalysis;
using Xunit;

namespace UnitTests.Logic.Isa;

/// <summary>
/// Validation of <see cref="PhotonicAccumulator"/> (issue #1464):
/// <see cref="PhotonicAccumulator.Accepts"/> never throws and is false on a
/// network that is not a 4-bit register (the 4-bit adder shape), and the
/// constructor error names the first missing signal — the same contract
/// <see cref="PhotonicDataMemory"/> pins for the RAM network.
/// </summary>
public class PhotonicAccumulatorTests
{
    [Fact]
    public void Accepts_OnFourBitAdderNetwork_IsFalse()
    {
        var adder = BuildNamedNetwork(
            inputs: new[] { "A0", "A1", "A2", "A3", "B0", "B1", "B2", "B3" },
            outputs: new[] { "S0", "S1", "S2", "S3", "Cout" });

        PhotonicAccumulator.Accepts(adder).ShouldBeFalse(
            "the 4-bit adder has no ACC.LOAD/ACC.D/ACC.Q signals and must not be taken for an accumulator");
    }

    [Fact]
    public void Accepts_OnRegisterShapedNetwork_IsTrue()
    {
        PhotonicAccumulator.Accepts(BuildRegisterShapedNetwork()).ShouldBeTrue();
    }

    [Fact]
    public void Accepts_OnNull_IsFalse()
    {
        PhotonicAccumulator.Accepts(null).ShouldBeFalse();
    }

    [Fact]
    public void Constructor_OnFourBitAdderNetwork_ThrowsNamingTheFirstMissingSignal()
    {
        var adder = BuildNamedNetwork(
            inputs: new[] { "A0", "A1", "A2", "A3", "B0", "B1", "B2", "B3" },
            outputs: new[] { "S0", "S1", "S2", "S3", "Cout" });

        var exception = Should.Throw<ArgumentException>(() => new PhotonicAccumulator(adder));

        exception.Message.ShouldContain("'ACC.D0'");
        // The message lists the declared inputs so the mismatch is diagnosable.
        exception.Message.ShouldContain("B0");
    }

    [Fact]
    public void Constructor_OnNetworkMissingReadTaps_ThrowsNamingTheMissingTap()
    {
        var noTaps = BuildNamedNetwork(
            inputs: new[] { "ACC.D0", "ACC.D1", "ACC.D2", "ACC.D3", "ACC.LOAD" },
            outputs: new[] { "S0" });

        var exception = Should.Throw<ArgumentException>(() => new PhotonicAccumulator(noTaps));

        exception.Message.ShouldContain("'ACC.Q0'");
        exception.Message.ShouldContain("output signal");
    }

    /// <summary>
    /// One NAND gate named like a register (ACC.D0–ACC.D3, ACC.LOAD in; ACC.Q0–ACC.Q3
    /// out): enough for name validation, never evaluated. Behaviour is pinned on a
    /// hand-built register evaluator in <see cref="IsaAccumulatorSignalMapTests"/>.
    /// </summary>
    private static LogicNetworkEvaluator BuildRegisterShapedNetwork() =>
        BuildNamedNetwork(
            inputs: new[] { "ACC.D0", "ACC.D1", "ACC.D2", "ACC.D3", "ACC.LOAD" },
            outputs: new[] { "ACC.Q0", "ACC.Q1", "ACC.Q2", "ACC.Q3" });

    /// <summary>One NAND gate (inputs A, B; output Y) wearing the given signal names.</summary>
    private static LogicNetworkEvaluator BuildNamedNetwork(string[] inputs, string[] outputs) =>
        new(
            inputPinNames: inputs.ToList(),
            gates: new Dictionary<string, LogicGateModel> { ["nand"] = PinnedGateTables.NandGate() },
            inputWiring: new Dictionary<LogicPinRef, LogicNetDriver>
            {
                [new LogicPinRef("nand", "A")] = new LogicNetDriver.NetworkInput(inputs[0]),
                [new LogicPinRef("nand", "B")] = new LogicNetDriver.NetworkInput(inputs[1]),
            },
            outputTaps: outputs.ToDictionary(name => name, _ => new LogicPinRef("nand", "Y")));
}
