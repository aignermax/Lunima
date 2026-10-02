using CAP_Core.Logic.Isa;
using Shouldly;
using Xunit;

namespace UnitTests.Integration;

/// <summary>
/// Rung-5 acceptance (issue #1307): the ISA <c>JZ</c> branches on the photonic zero
/// flag — the shipped <c>Logic Gate Zero Detect 4-bit.lun</c> loaded through the real
/// load → LogicNetworkAssembler path — via the <see cref="IsaEmulator"/> zero-flag
/// seam, closing the last gap where the machine decided control flow in C#. The
/// shipped <c>count-to-5.asm</c> (whose loop exits exactly on the zero check) runs
/// with the photonic flag and the golden ALU and must produce a cycle-by-cycle trace
/// identical to the all-golden run, and the flag's evaluation count must equal the
/// number of executed <c>JZ</c> instructions — proof the branch really asked light.
/// </summary>
public class IsaPhotonicZeroFlagTraceTests
    : IClassFixture<LogicGateZeroDetect4BitExampleTests.ZeroDetect4BitFixture>
{
    private const int StepBudget = 500;

    private readonly LogicGateZeroDetect4BitExampleTests.ZeroDetect4BitFixture _fixture;

    /// <summary>Attaches the shared zero-detect fixture (assembles the network once).</summary>
    public IsaPhotonicZeroFlagTraceTests(
        LogicGateZeroDetect4BitExampleTests.ZeroDetect4BitFixture fixture) =>
        _fixture = fixture;

    [Fact]
    public void CountTo5_WithPhotonicZeroFlag_ProducesTheGoldenTrace_AndConsultsTheFlagOnEveryJz()
    {
        PhotonicZeroFlag.Accepts(_fixture.Network).ShouldBeTrue(
            "the shipped Logic Gate Zero Detect 4-bit example must expose A0–A3 and Z");
        var program = new IsaAssembler().Assemble(
            File.ReadAllText(SamplePath("count-to-5.asm")));
        var flag = new PhotonicZeroFlag(_fixture.Network);

        var goldenTrace = RunToHalt(new IsaEmulator(program), program, out var goldenJzCount);
        var photonicTrace = RunToHalt(
            new IsaEmulator(program, zeroFlag: flag.IsZero), program, out var photonicJzCount);

        photonicTrace.ShouldBe(goldenTrace,
            "JZ must branch on the photonic zero flag exactly as on the golden model");
        photonicTrace[^1].ShouldBe("PC=15 ACC=5 RAM=[5,1,0,11]",
            "count-to-5 halts with the counter at 5");
        photonicJzCount.ShouldBeGreaterThan(0, "count-to-5 must execute JZ — the loop's exit check");
        flag.EvaluationCount.ShouldBe(photonicJzCount,
            "every executed JZ consulted the photonic flag — none slipped through the golden check");
        photonicJzCount.ShouldBe(goldenJzCount);
    }

    [Fact]
    public void ZeroFlag_All16Words_OnTheExampleNetwork_MatchTheGoldenCheck()
    {
        var flag = new PhotonicZeroFlag(_fixture.Network);
        for (var a = 0; a <= IsaMachine.MaxDataValue; a++)
        {
            flag.IsZero(a).ShouldBe(a == 0, $"Logic Gate Zero Detect 4-bit.lun: A={a}");
        }
    }

    /// <summary>Steps the machine to HALT, recording PC, ACC and RAM after every step
    /// and counting the executed JZ instructions.</summary>
    private static List<string> RunToHalt(IsaEmulator emulator, byte[] program, out int jzCount)
    {
        var trace = new List<string>();
        jzCount = 0;
        while (!emulator.IsHalted && trace.Count < StepBudget)
        {
            if (IsaInstruction.Decode(program[emulator.ProgramCounter], out _)?.Opcode == IsaOpcode.Jz)
            {
                jzCount++;
            }

            emulator.Step();
            trace.Add($"PC={emulator.ProgramCounter} ACC={emulator.Accumulator} " +
                $"RAM=[{string.Join(",", emulator.Ram)}]");
        }

        emulator.IsHalted.ShouldBeTrue($"the program did not halt within {StepBudget} steps");
        return trace;
    }

    private static string SamplePath(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var gitPath = Path.Combine(dir.FullName, ".git");
            if (Directory.Exists(gitPath) || File.Exists(gitPath))
            {
                return Path.Combine(dir.FullName, "examples", "isa", fileName);
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root from the test binary.");
    }
}
