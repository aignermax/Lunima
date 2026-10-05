using CAP_Core.Analysis.LogicAnalysis;
using CAP_Core.Logic.Isa;
using Shouldly;
using UnitTests.Analysis.LogicAnalysis;
using Xunit;

namespace UnitTests.Logic.Isa;

/// <summary>
/// The photonic data memory's network validation: a network that does not expose
/// the RAM 4x4 signal names (address A0/A1, LOAD, data D0–D3, read taps Q0–Q3) —
/// like the shipped 4-bit adder network — must be rejected by
/// <see cref="PhotonicDataMemory.Accepts"/> without throwing, and the constructor
/// must throw naming the first missing signal, exactly like
/// <see cref="PhotonicZeroFlag"/>.
/// </summary>
public class PhotonicDataMemoryTests
{
    [Fact]
    public void Accepts_AdderShapedNetwork_ReturnsFalse()
    {
        // The 4-bit adder exposes operand bits A0–A3 and B0–B3 plus sum taps, but no
        // LOAD strobe, no data inputs D0–D3 and no read taps Q0–Q3.
        PhotonicDataMemory.Accepts(BuildAdderShapedNetwork()).ShouldBeFalse();
    }

    [Fact]
    public void Accepts_RamShapedNetwork_ReturnsTrue_AndNullReturnsFalse()
    {
        PhotonicDataMemory.Accepts(BuildRamShapedNetwork()).ShouldBeTrue();
        PhotonicDataMemory.Accepts(null).ShouldBeFalse();
    }

    [Fact]
    public void Constructor_AdderShapedNetwork_ThrowsNamingTheFirstMissingSignal()
    {
        var exception = Should.Throw<ArgumentException>(
            () => new PhotonicDataMemory(BuildAdderShapedNetwork()));

        // The adder carries A0/A1 but no LOAD strobe — that is the first missing input.
        exception.Message.ShouldContain("LOAD");
    }

    [Fact]
    public void Constructor_NetworkMissingDataBit_ThrowsNamingTheSignal()
    {
        var exception = Should.Throw<ArgumentException>(
            () => new PhotonicDataMemory(BuildRamShapedNetwork(dropInput: "D2")));

        exception.Message.ShouldContain("D2");
    }

    [Fact]
    public void Constructor_NetworkMissingReadTap_ThrowsNamingTheSignal()
    {
        var exception = Should.Throw<ArgumentException>(
            () => new PhotonicDataMemory(BuildRamShapedNetwork(dropTap: "Q3")));

        exception.Message.ShouldContain("Q3");
    }

    [Fact]
    public void Constructor_NullNetwork_Throws()
    {
        Should.Throw<ArgumentNullException>(() => new PhotonicDataMemory(null!));
    }

    /// <summary>
    /// A network shaped like the shipped 4-bit adder: operand inputs A0–A3 and
    /// B0–B3 feeding one gate whose output is tapped as a sum bit — none of the RAM
    /// signals beyond the shared address-bit names.
    /// </summary>
    private static LogicNetworkEvaluator BuildAdderShapedNetwork()
    {
        var inputs = new List<string> { "A0", "A1", "A2", "A3", "B0", "B1", "B2", "B3" };
        var gates = new Dictionary<string, LogicGateModel> { ["SUM"] = PinnedGateTables.NotGate() };
        var wiring = new Dictionary<LogicPinRef, LogicNetDriver>
        {
            [new LogicPinRef("SUM", "A")] = new LogicNetDriver.NetworkInput("A0"),
        };
        var taps = new Dictionary<string, LogicPinRef> { ["S0"] = new("SUM", "Y") };
        return new LogicNetworkEvaluator(inputs, gates, wiring, taps);
    }

    /// <summary>
    /// A minimal network that exposes every RAM 4x4 signal name: A0/A1, LOAD and
    /// D0–D3 as inputs, Q0–Q3 as output taps (one NOT slice per data bit), so the
    /// constructor's signal check passes. Optionally drops one input or tap.
    /// </summary>
    private static LogicNetworkEvaluator BuildRamShapedNetwork(string? dropInput = null, string? dropTap = null)
    {
        var inputs = new List<string> { "A0", "A1", PhotonicDataMemory.LoadSignal, "D0", "D1", "D2", "D3" };
        inputs.Remove(dropInput ?? string.Empty);
        var gates = new Dictionary<string, LogicGateModel>();
        var wiring = new Dictionary<LogicPinRef, LogicNetDriver>();
        var taps = new Dictionary<string, LogicPinRef>();
        for (var bit = 0; bit < IsaMachine.DataBits; bit++)
        {
            var dataSignal = $"D{bit}";
            var gateId = $"SLICE{bit}";
            if (!inputs.Contains(dataSignal))
            {
                continue;
            }

            gates[gateId] = PinnedGateTables.NotGate();
            wiring[new LogicPinRef(gateId, "A")] = new LogicNetDriver.NetworkInput(dataSignal);
            var tap = $"Q{bit}";
            if (tap != dropTap)
            {
                taps[tap] = new LogicPinRef(gateId, "Y");
            }
        }

        return new LogicNetworkEvaluator(inputs, gates, wiring, taps);
    }
}
