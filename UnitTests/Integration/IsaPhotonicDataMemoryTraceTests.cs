using CAP_Core.Logic.Isa;
using Shouldly;
using Xunit;

namespace UnitTests.Integration;

/// <summary>
/// Rung-5 acceptance (issue #1420): <see cref="IsaEmulator"/> delegates the data
/// memory to the photonic RAM 4x4 network through the <see cref="IIsaDataMemory"/>
/// seam, so a program's <c>STORE</c>s and the RAM operands of <c>ADD</c>/<c>AND</c>
/// live in photonic registers. The shipped multiply sample (3 × 4 by repeated ADD,
/// <c>examples/isa/multiply-3x4.asm</c>) and a program that STOREs all four words
/// and reads them back must run on the <see cref="PhotonicDataMemory"/> (over the
/// assembled network of the shipped "Logic Gate RAM 4x4" example, with the golden
/// ALU and zero flag) to HALT with a trace identical to the all-golden run — PC,
/// ACC and RAM after every step — and the memory must be written exactly once per
/// executed <c>STORE</c> and read exactly once per executed RAM-operand instruction.
/// One photonic step must stay well under the 100 ms UI budget.
/// </summary>
public class IsaPhotonicDataMemoryTraceTests
    : IClassFixture<LogicGateRam4x4ExampleTests.Ram4x4Fixture>
{
    private const int StepBudget = 500;

    /// <summary>The 100 ms UI budget for one photonic step, asserted on the steady-state median.</summary>
    private static readonly TimeSpan UiBudget = TimeSpan.FromMilliseconds(100);

    /// <summary>Hard per-step ceiling so a single scheduling hiccup does not flake the budget test.</summary>
    private static readonly TimeSpan PerStepCeiling = TimeSpan.FromMilliseconds(400);

    private const int WarmupSteps = 2;

    private readonly LogicGateRam4x4ExampleTests.Ram4x4Fixture _fixture;

    /// <summary>Attaches the shared RAM 4x4 fixture (loads the example once).</summary>
    public IsaPhotonicDataMemoryTraceTests(LogicGateRam4x4ExampleTests.Ram4x4Fixture fixture) =>
        _fixture = fixture;

    [Fact]
    public void MultiplySample_OnPhotonicDataMemory_ProducesTheGoldenTrace_AndTouchesMemoryOncePerAccess()
    {
        var program = new IsaAssembler().Assemble(File.ReadAllText(SamplePath("multiply-3x4.asm")));
        var memory = new PhotonicDataMemory(_fixture.Network);

        var goldenTrace = RunToHalt(new IsaEmulator(program), program, null, out _, out _);
        var photonicTrace = RunToHalt(new IsaEmulator(program, dataMemory: memory), program, memory,
            out var operandReads, out var stores);

        photonicTrace.ShouldBe(goldenTrace,
            "multiply-3x4 must loop and halt on the photonic RAM exactly as on the golden memory");
        stores.ShouldBeGreaterThan(0, "multiply-3x4 stores its running total every round");
        operandReads.ShouldBeGreaterThan(0, "multiply-3x4 reads its RAM operands every round");
        memory.WriteCount.ShouldBe(stores,
            "the photonic RAM must be written exactly once per executed STORE");
        memory.ReadCount.ShouldBe(operandReads + IsaMachine.RamWords * photonicTrace.Count,
            "beyond the UI-facing RAM snapshot after every step, the photonic RAM must be read " +
            "exactly once per executed RAM-operand instruction");
        var readsBeforeGoldenRun = memory.ReadCount;
        new IsaEmulator(program).Run(StepBudget);
        memory.ReadCount.ShouldBe(readsBeforeGoldenRun,
            "a golden run on a fresh machine must not touch the photonic memory");
    }

    [Fact]
    public void StoreAllWordsAndReadBack_OnPhotonicDataMemory_ProducesTheGoldenTrace()
    {
        const string storeAllReadBack =
            "LOAD 3\nSTORE 0\nLOAD 5\nSTORE 1\nLOAD 10\nSTORE 2\nLOAD 12\nSTORE 3\n" +
            "LOAD 0\nADD 0\nADD 1\nADD 2\nADD 3\nHALT";
        var program = new IsaAssembler().Assemble(storeAllReadBack);
        var memory = new PhotonicDataMemory(_fixture.Network);

        var goldenTrace = RunToHalt(new IsaEmulator(program), program, null, out _, out _);
        var photonicTrace = RunToHalt(new IsaEmulator(program, dataMemory: memory), program, memory,
            out var operandReads, out var stores);

        photonicTrace.ShouldBe(goldenTrace,
            "every stored word must read back out of the photonic registers exactly as out of the golden array");
        stores.ShouldBe(IsaMachine.RamWords, "the program STOREs all four words");
        memory.WriteCount.ShouldBe(stores);
        memory.ReadCount.ShouldBe(operandReads + IsaMachine.RamWords * photonicTrace.Count,
            "beyond the UI-facing RAM snapshots, the read-back ADDs consult the photonic RAM once each");
    }

    [Fact]
    public void PhotonicStep_SteadyStateMedian_StaysUnderTheUiBudget()
    {
        var program = new IsaAssembler().Assemble(File.ReadAllText(SamplePath("multiply-3x4.asm")));
        var memory = new PhotonicDataMemory(_fixture.Network);
        var emulator = new IsaEmulator(program, dataMemory: memory);

        var samples = new List<TimeSpan>();
        while (!emulator.IsHalted && samples.Count < StepBudget)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            emulator.Step();
            watch.Stop();
            samples.Add(watch.Elapsed);
        }

        emulator.IsHalted.ShouldBeTrue($"the program did not halt within {StepBudget} steps");
        var steadyState = samples.Skip(WarmupSteps).OrderBy(elapsed => elapsed).ToList();
        steadyState.ShouldNotBeEmpty("the multiply loop must execute steps beyond the warmup");
        steadyState[steadyState.Count / 2].ShouldBeLessThan(UiBudget,
            "the steady-state median step on the photonic RAM must stay under the 100 ms UI budget");
        steadyState[^1].ShouldBeLessThan(PerStepCeiling,
            "no single step on the photonic RAM may blow the per-step ceiling");
    }

    /// <summary>
    /// Steps the machine to HALT, recording PC, ACC and RAM after every step. When a
    /// <see cref="PhotonicDataMemory"/> is given, asserts per step that the network
    /// was consulted exactly as the decoded instruction demands (one write per
    /// <c>STORE</c>, one read per <c>ADD</c>/<c>AND</c>) and reports the executed
    /// totals. The RAM trace snapshot goes through <see cref="IsaEmulator.Ram"/>
    /// after the per-step assertions, so the UI-facing reads do not pollute the
    /// instruction-access counts.
    /// </summary>
    private static List<string> RunToHalt(IsaEmulator emulator, byte[] program, PhotonicDataMemory? memory,
        out int operandReads, out int stores)
    {
        memory?.Reset();
        var trace = new List<string>();
        operandReads = 0;
        stores = 0;
        while (!emulator.IsHalted && trace.Count < StepBudget)
        {
            var opcode = IsaInstruction.Decode(program[emulator.ProgramCounter], out _)?.Opcode;
            var expectRead = opcode is IsaOpcode.Add or IsaOpcode.And;
            var expectWrite = opcode is IsaOpcode.Store;
            if (expectRead)
            {
                operandReads++;
            }

            if (expectWrite)
            {
                stores++;
            }

            var readsBefore = memory?.ReadCount ?? 0;
            var writesBefore = memory?.WriteCount ?? 0;
            emulator.Step();
            if (memory != null)
            {
                (memory.ReadCount - readsBefore).ShouldBe(expectRead ? 1 : 0,
                    $"step {trace.Count}: {opcode} must read the photonic RAM {(expectRead ? "once" : "never")}");
                (memory.WriteCount - writesBefore).ShouldBe(expectWrite ? 1 : 0,
                    $"step {trace.Count}: {opcode} must write the photonic RAM {(expectWrite ? "once" : "never")}");
            }

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
