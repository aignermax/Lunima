using CAP_Core.Analysis.LogicAnalysis;
using CAP_Core.Logic.Isa;
using Shouldly;
using UnitTests.Analysis.LogicAnalysis;
using Xunit;

namespace UnitTests.Logic.Isa;

/// <summary>
/// Validation of <see cref="PhotonicDataMemory"/> (issue #1436): <see cref="PhotonicDataMemory.Accepts"/>
/// never throws and is false on a network that is not a RAM (the 4-bit adder shape),
/// and the constructor error names the first missing signal — the same contract
/// <see cref="PhotonicZeroFlag"/> pins for the zero-detect network.
/// </summary>
public class PhotonicDataMemoryTests
{
    [Fact]
    public void Accepts_OnFourBitAdderNetwork_IsFalse()
    {
        var adder = BuildNamedNetwork(
            inputs: new[] { "A0", "A1", "A2", "A3", "B0", "B1", "B2", "B3" },
            outputs: new[] { "S0", "S1", "S2", "S3", "Cout" });

        PhotonicDataMemory.Accepts(adder).ShouldBeFalse(
            "the 4-bit adder has no LOAD/D/Q signals and must not be taken for a data memory");
    }

    [Fact]
    public void Accepts_OnRamShapedNetwork_IsTrue()
    {
        PhotonicDataMemory.Accepts(BuildRamShapedNetwork()).ShouldBeTrue();
    }

    [Fact]
    public void Accepts_OnNull_IsFalse()
    {
        PhotonicDataMemory.Accepts(null).ShouldBeFalse();
    }

    [Fact]
    public void Constructor_OnFourBitAdderNetwork_ThrowsNamingTheFirstMissingSignal()
    {
        var adder = BuildNamedNetwork(
            inputs: new[] { "A0", "A1", "A2", "A3", "B0", "B1", "B2", "B3" },
            outputs: new[] { "S0", "S1", "S2", "S3", "Cout" });

        var exception = Should.Throw<ArgumentException>(() => new PhotonicDataMemory(adder));

        // A0/A1 exist on the adder, so LOAD is the first missing signal.
        exception.Message.ShouldContain("'LOAD'");
        // The message lists the declared inputs so the mismatch is diagnosable.
        exception.Message.ShouldContain("B0");
    }

    [Fact]
    public void Constructor_OnNetworkMissingReadTaps_ThrowsNamingTheMissingTap()
    {
        var noTaps = BuildNamedNetwork(
            inputs: new[] { "A0", "A1", "LOAD", "D0", "D1", "D2", "D3" },
            outputs: new[] { "S0" });

        var exception = Should.Throw<ArgumentException>(() => new PhotonicDataMemory(noTaps));

        exception.Message.ShouldContain("'Q0'");
        exception.Message.ShouldContain("output signal");
    }

    [Fact]
    public void OutOfRangeAccess_FaultsLikeTheGoldenMemory()
    {
        var memory = new PhotonicDataMemory(BuildRamShapedNetwork());

        Should.Throw<InvalidOperationException>(() => memory.Read(IsaMachine.RamWords))
            .Message.ShouldContain("out of range");
        Should.Throw<InvalidOperationException>(() => memory.Write(-1, 0))
            .Message.ShouldContain("out of range");
    }

    /// <summary>
    /// One NAND gate named like a RAM (A0, A1, LOAD, D0–D3 in; Q0–Q3 out): enough for
    /// name validation, never evaluated. Behaviour is pinned on the real shipped
    /// example in the integration trace tests.
    /// </summary>
    private static LogicNetworkEvaluator BuildRamShapedNetwork() =>
        BuildNamedNetwork(
            inputs: new[] { "A0", "A1", "LOAD", "D0", "D1", "D2", "D3" },
            outputs: new[] { "Q0", "Q1", "Q2", "Q3" });

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
