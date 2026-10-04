using CAP_Core.Logic.Isa;
using Shouldly;
using Xunit;

namespace UnitTests.Integration;

/// <summary>
/// Rung-5 acceptance (issue #1307): <see cref="IsaEmulator"/> delegates the <c>JZ</c>
/// branch decision to the photonic zero-detect network through the
/// <see cref="IIsaZeroFlag"/> seam, so the program's control flow is decided by light.
/// The shipped <c>count-to-5.asm</c> — whose loop exits exactly on that check — must
/// run on the <see cref="PhotonicZeroFlag"/> (over the assembled network of the shipped
/// "Logic Gate Zero Detect 4-bit" example, with the golden ALU) to HALT with a trace
/// identical to the all-golden run, and the flag must actually be consulted once per
/// executed <c>JZ</c>. The flag itself is additionally pinned against the golden model
/// over all 16 accumulator values on the real example network.
/// </summary>
public class IsaPhotonicZeroFlagTraceTests
    : IClassFixture<LogicGateZeroDetect4BitExampleTests.ZeroDetectFixture>
{
    private const int StepBudget = 500;

    private readonly LogicGateZeroDetect4BitExampleTests.ZeroDetectFixture _fixture;

    /// <summary>Attaches the shared zero-detect fixture (assembles the network once).</summary>
    public IsaPhotonicZeroFlagTraceTests(LogicGateZeroDetect4BitExampleTests.ZeroDetectFixture fixture) =>
        _fixture = fixture;

    [Fact]
    public void CountTo5_OnPhotonicZeroFlag_ProducesTheGoldenTrace_AndConsultsTheFlagOnEveryJz()
    {
        var program = new IsaAssembler().Assemble(File.ReadAllText(SamplePath("count-to-5.asm")));

        var goldenTrace = RunToHalt(new IsaEmulator(program), program, out var goldenJzCount);
        var countingFlag = new CountingZeroFlag(new PhotonicZeroFlag(_fixture.Network));
        var photonicTrace = RunToHalt(new IsaEmulator(program, zeroFlag: countingFlag), program, out _);

        photonicTrace.ShouldBe(goldenTrace,
            "count-to-5.asm must loop and exit on the photonic zero flag exactly as on the golden model");
        goldenJzCount.ShouldBeGreaterThan(0,
            "count-to-5.asm must execute JZ at least once — its loop exits on the zero check");
        countingFlag.Calls.ShouldBe(goldenJzCount,
            "the photonic flag must be consulted exactly once per executed JZ instruction");
    }

    [Fact]
    public void IsZero_All16Values_OnTheExampleNetwork_MatchTheGoldenFlag()
    {
        var photonic = new PhotonicZeroFlag(_fixture.Network);
        var golden = new GoldenIsaZeroFlag();
        for (var a = 0; a <= IsaMachine.MaxDataValue; a++)
        {
            photonic.IsZero(a).ShouldBe(golden.IsZero(a),
                $"Logic Gate Zero Detect 4-bit.lun: A={a}");
        }
    }

    /// <summary>Steps the machine to HALT, recording PC, ACC and RAM after every step
    /// and counting the executed JZ instructions (decoded at the pre-step PC).</summary>
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

    /// <summary>Counts the calls the wrapped flag receives, to pin the JZ consultation.</summary>
    private sealed class CountingZeroFlag : IIsaZeroFlag
    {
        private readonly IIsaZeroFlag _inner;

        public CountingZeroFlag(IIsaZeroFlag inner) => _inner = inner;

        public int Calls { get; private set; }

        public bool IsZero(int value)
        {
            Calls++;
            return _inner.IsZero(value);
        }
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
