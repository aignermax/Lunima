using CAP_Core.Logic.Isa;
using Shouldly;
using Xunit;

namespace UnitTests.Integration;

/// <summary>
/// Rung-5 acceptance (issue #1274): the ISA <c>AND</c> executes on the photonic
/// 4-bit AND network — the shipped <c>Logic Gate AND 4-bit.lun</c> loaded through
/// the real load → LogicNetworkAssembler path — via the <see cref="IIsaAlu"/> seam.
/// The full trace (PC, ACC, RAM after every step) must equal the golden trace, and
/// the photonic AND must match the golden AND over all 256 operand pairs on the
/// real example network.
/// </summary>
public class IsaPhotonicAndTraceTests : IClassFixture<LogicGateAnd4BitExampleTests.And4BitFixture>
{
    private const int StepBudget = 100;

    private readonly LogicGateAnd4BitExampleTests.And4BitFixture _fixture;

    /// <summary>Attaches the shared AND-4-bit fixture (assembles the network once).</summary>
    public IsaPhotonicAndTraceTests(LogicGateAnd4BitExampleTests.And4BitFixture fixture) =>
        _fixture = fixture;

    [Fact]
    public void AndProgram_OnPhotonicAnd_ProducesTheGoldenTrace()
    {
        PhotonicAndAlu.Accepts(_fixture.Network).ShouldBeTrue(
            "the shipped Logic Gate AND 4-bit example must expose A0–A3, B0–B3 and Y0–Y3");
        var program = new IsaAssembler().Assemble("LOAD 12\nSTORE 1\nLOAD 10\nAND 1\nSTORE 0\nHALT");

        var goldenTrace = RunToHalt(new IsaEmulator(program));
        var photonicTrace = RunToHalt(new IsaEmulator(program, new PhotonicAndAlu(_fixture.Network)));

        photonicTrace.ShouldBe(goldenTrace,
            "AND must execute on the photonic network exactly as on the golden model");
        photonicTrace[^1].ShouldBe("PC=6 ACC=8 RAM=[8,12,0,0]",
            "12 & 10 = 8, stored to RAM word 0");
    }

    [Fact]
    public void And_All256OperandPairs_OnTheExampleNetwork_MatchTheGoldenAlu()
    {
        var photonic = new PhotonicAndAlu(_fixture.Network);
        var golden = new GoldenIsaAlu();
        for (var a = 0; a <= IsaMachine.MaxDataValue; a++)
        for (var b = 0; b <= IsaMachine.MaxDataValue; b++)
        {
            photonic.And(a, b).ShouldBe(golden.And(a, b),
                $"Logic Gate AND 4-bit.lun: A={a}, B={b}");
        }
    }

    /// <summary>Steps the machine to HALT, recording PC, ACC and RAM after every step.</summary>
    private static List<string> RunToHalt(IsaEmulator emulator)
    {
        var trace = new List<string>();
        while (!emulator.IsHalted && trace.Count < StepBudget)
        {
            emulator.Step();
            trace.Add($"PC={emulator.ProgramCounter} ACC={emulator.Accumulator} " +
                $"RAM=[{string.Join(",", emulator.Ram)}]");
        }

        emulator.IsHalted.ShouldBeTrue($"the program did not halt within {StepBudget} steps");
        return trace;
    }
}
