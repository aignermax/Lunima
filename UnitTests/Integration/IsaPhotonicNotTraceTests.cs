using CAP_Core.Logic.Isa;
using Shouldly;
using Xunit;

namespace UnitTests.Integration;

/// <summary>
/// Rung-5 acceptance (issue #1264): the ISA <c>NOT</c> executes on the photonic
/// 4-bit NOT network — the shipped <c>Logic Gate NOT 4-bit.lun</c> loaded through
/// the real load → LogicNetworkAssembler path — via the <see cref="IIsaAlu"/> seam.
/// The full trace (PC, ACC, RAM after every step) must equal the golden trace, and
/// the photonic NOT must match the golden NOT over all 16 input words on the real
/// example network.
/// </summary>
public class IsaPhotonicNotTraceTests : IClassFixture<LogicGateNot4BitExampleTests.Not4BitFixture>
{
    private const int StepBudget = 100;

    private readonly LogicGateNot4BitExampleTests.Not4BitFixture _fixture;

    /// <summary>Attaches the shared NOT-4-bit fixture (assembles the network once).</summary>
    public IsaPhotonicNotTraceTests(LogicGateNot4BitExampleTests.Not4BitFixture fixture) =>
        _fixture = fixture;

    [Fact]
    public void NotProgram_OnPhotonicNot_ProducesTheGoldenTrace()
    {
        PhotonicNotAlu.Accepts(_fixture.Network).ShouldBeTrue(
            "the shipped Logic Gate NOT 4-bit example must expose A0–A3 and Y0–Y3");
        var program = new IsaAssembler().Assemble("LOAD 5\nNOT\nSTORE 0\nHALT");

        var goldenTrace = RunToHalt(new IsaEmulator(program));
        var photonicTrace = RunToHalt(new IsaEmulator(program, new PhotonicNotAlu(_fixture.Network)));

        photonicTrace.ShouldBe(goldenTrace,
            "NOT must execute on the photonic network exactly as on the golden model");
        photonicTrace[^1].ShouldBe("PC=4 ACC=10 RAM=[10,0,0,0]", "~5 & 0xF = 10, stored to RAM word 0");
    }

    [Fact]
    public void Not_All16Inputs_OnTheExampleNetwork_MatchTheGoldenAlu()
    {
        var photonic = new PhotonicNotAlu(_fixture.Network);
        var golden = new GoldenIsaAlu();
        for (var a = 0; a <= IsaMachine.MaxDataValue; a++)
        {
            photonic.Not(a).ShouldBe(golden.Not(a),
                $"Logic Gate NOT 4-bit.lun: A={a}");
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
