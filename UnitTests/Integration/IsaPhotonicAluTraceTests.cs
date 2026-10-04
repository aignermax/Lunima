using CAP_Core.Logic.Isa;
using Shouldly;
using Xunit;

namespace UnitTests.Integration;

/// <summary>
/// Rung-5 core acceptance (issue #1205): <see cref="IsaEmulator"/> delegates ADD to
/// the photonic 4-bit adder network through the <see cref="IIsaAlu"/> seam, so a
/// whole .asm program executes on the designed chip. Both shipped
/// <c>examples/isa/*.asm</c> programs must run to HALT on the
/// <see cref="PhotonicAdderAlu"/> — wrapping the assembled network of the shipped
/// "Logic Gate 4-Bit Adder" example — and produce a trace (PC, ACC, RAM after every
/// step) identical to the golden run. The ALU itself is additionally pinned against
/// the golden model over all 256 operand pairs on the real example network.
/// </summary>
public class IsaPhotonicAluTraceTests : IClassFixture<LogicGateFourBitAdderExampleTests.FourBitAdderFixture>
{
    private const int StepBudget = 500;

    private readonly LogicGateFourBitAdderExampleTests.FourBitAdderFixture _fixture;

    /// <summary>Attaches the shared 4-bit-adder fixture (assembles the network once).</summary>
    public IsaPhotonicAluTraceTests(LogicGateFourBitAdderExampleTests.FourBitAdderFixture fixture) =>
        _fixture = fixture;

    [Theory]
    [InlineData("count-to-5.asm")]
    [InlineData("add-two-numbers.asm")]
    public void SampleProgram_OnPhotonicAlu_ProducesTheGoldenTrace(string fileName)
    {
        var program = new IsaAssembler().Assemble(File.ReadAllText(SamplePath(fileName)));

        var goldenTrace = RunToHalt(new IsaEmulator(program));
        var photonicTrace = RunToHalt(new IsaEmulator(program, new PhotonicAdderAlu(_fixture.Network)));

        photonicTrace.ShouldBe(goldenTrace,
            $"{fileName} must execute on the photonic adder exactly as on the golden model");
    }

    [Fact]
    public void Add_All256OperandPairs_OnTheExampleNetwork_MatchTheGoldenAlu()
    {
        var photonic = new PhotonicAdderAlu(_fixture.Network);
        var golden = new GoldenIsaAlu();
        for (var a = 0; a <= IsaMachine.MaxDataValue; a++)
        for (var b = 0; b <= IsaMachine.MaxDataValue; b++)
        {
            photonic.Add(a, b).ShouldBe(golden.Add(a, b),
                $"Logic Gate 4-Bit Adder.lun: A={a}, B={b}");
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

    private static string SamplePath(string fileName) =>
        Path.Combine(FindRepoRoot(), "examples", "isa", fileName);

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var gitPath = Path.Combine(dir.FullName, ".git");
            if (Directory.Exists(gitPath) || File.Exists(gitPath))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root (.git directory or file).");
    }
}
