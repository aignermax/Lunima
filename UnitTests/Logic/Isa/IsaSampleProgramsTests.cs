using CAP_Core.Logic.Isa;
using Shouldly;

namespace UnitTests.Logic.Isa;

/// <summary>
/// End-to-end acceptance for the shipped sample programs in <c>examples/isa/</c>:
/// each must assemble through <see cref="IsaAssembler"/> and halt on
/// <see cref="IsaEmulator"/> with the expected register value within a bounded
/// number of steps.
/// </summary>
public class IsaSampleProgramsTests
{
    private const int StepBudget = 500;

    [Theory]
    [InlineData("count-to-5.asm", 5)]
    [InlineData("add-two-numbers.asm", 13)]
    public void SampleProgram_AssemblesAndHalts_WithExpectedAccumulator(string fileName, int expectedAccumulator)
    {
        string source = File.ReadAllText(SamplePath(fileName));
        byte[] program = new IsaAssembler().Assemble(source);
        var emulator = new IsaEmulator(program);

        int steps = emulator.Run(StepBudget);

        emulator.IsHalted.ShouldBeTrue($"{fileName} did not halt within {StepBudget} steps");
        steps.ShouldBeLessThan(StepBudget);
        emulator.Accumulator.ShouldBe(expectedAccumulator);
    }

    [Fact]
    public void CountTo5_LeavesCounterInRam()
    {
        string source = File.ReadAllText(SamplePath("count-to-5.asm"));
        var emulator = new IsaEmulator(new IsaAssembler().Assemble(source));

        emulator.Run(StepBudget);

        emulator.Ram[0].ShouldBe(5);
    }

    private static string SamplePath(string fileName)
    {
        return Path.Combine(FindRepoRoot(), "examples", "isa", fileName);
    }

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
