using UnitTests.Helpers;
using System.Diagnostics;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Logic.IsaPlayground;
using CAP_Core.Components.Core;
using CAP_Core.Logic.Isa;
using Shouldly;
using UnitTests.Integration.RamScale;
using Xunit;

namespace UnitTests.Integration;

/// <summary>
/// Rung-5 acceptance for the shipped <c>examples/Logic Gate ALU + RAM.lun</c> (issue
/// #1463): the first chip that holds ALU and data RAM together. The example keeps the
/// shipped 4-bit adder untouched (plain names A0–A3, B0–B3, Cin → S0–S3, Cout) and wraps
/// the shipped RAM 4x4 in one group with every persisted signal name under the
/// <c>RAM.</c> prefix (<see cref="IsaDataMemorySignalMap.WithPrefix"/>), so the one
/// assembled network is accepted by <see cref="PhotonicAdderAlu"/> with its default map
/// AND by <see cref="PhotonicDataMemory"/> with the prefixed map. The end-to-end proof
/// runs the shipped <c>multiply-3x4.asm</c> on an <see cref="IsaEmulator"/> whose ALU and
/// data memory are both photonic over this network: the per-step trace (PC/ACC/RAM) must
/// equal the golden run and the machine must halt with ACC = 12 — the adder and the
/// memory are both light.
/// </summary>
public class AluRamChipExampleTests : IClassFixture<AluRamChipExampleTests.AluRamFixture>
{
    private const int StepBudget = 500;

    /// <summary>STORE 1, STORE 3 plus one STORE 0 per loop round (4 rounds) in multiply-3x4.</summary>
    private const int ExpectedMultiplyStoreCount = 6;

    /// <summary>ADD 1 and ADD 3 per round (4 + 4), ADD 0 per non-final round (3) and once after the loop.</summary>
    private const int ExpectedMultiplyRamOperandReadCount = 12;

    /// <summary>Pinned wall-clock budget for the full photonic multiply run (measured ~2 s locally; generous CI headroom).</summary>
    private static readonly TimeSpan MultiplyRunBudget = TimeSpan.FromSeconds(60);

    private static readonly IsaDataMemorySignalMap RamMap =
        IsaDataMemorySignalMap.WithPrefix(AluRamChipExampleAuthoringTests.RamSignalPrefix);

    private readonly AluRamFixture _fixture;

    /// <summary>Attaches the shared combined-example fixture (loads and assembles once).</summary>
    public AluRamChipExampleTests(AluRamFixture fixture) => _fixture = fixture;

    [Fact]
    public void Example_LoadsBothBlocksSideBySide_OnOneChip()
    {
        var groups = _fixture.Canvas.Components
            .Select(c => c.Component).OfType<ComponentGroup>().ToList();
        groups.Count.ShouldBe(345,
            "the adder's 344 top-level gate groups plus the wrapped RAM block");
        groups.Select(g => g.GroupName).ShouldContain(AluRamChipExampleAuthoringTests.RamGroupName);
        ExampleWires.LogicalWireCount(_fixture.Canvas).ShouldBe(339,
            "the adder's 339 wires stay top level; the RAM's 84 inter-cell wires are frozen inside the RAM group");
    }

    [Fact]
    public void AssembledNetwork_IsAcceptedByThePhotonicAlu_AndThePrefixedPhotonicDataMemory()
    {
        PhotonicAdderAlu.Accepts(_fixture.Network).ShouldBeTrue(
            "the adder keeps its plain persisted signal names (A0–A3, B0–B3, Cin → S0–S3, Cout)");
        PhotonicDataMemory.Accepts(_fixture.Network, RamMap).ShouldBeTrue(
            "the RAM's signals live under the 'RAM.' prefix (RAM.A0/A1, RAM.LOAD, RAM.D0–D3 → RAM.Q0–Q3)");
        PhotonicDataMemory.Accepts(_fixture.Network).ShouldBeFalse(
            "the default map must NOT match — the plain names belong to the adder now");
        _fixture.Network.RegisterState.Count.ShouldBe(IsaMachine.RamWords * IsaMachine.DataBits,
            "the sixteen RAM register bits survive the wrap");
    }

    [Fact]
    public void Multiply3x4_OnPhotonicAluAndPhotonicDataMemory_ProducesTheGoldenTrace_AndHaltsWithTwelve()
    {
        var program = AssembleShippedMultiplySample();
        var memory = new PhotonicDataMemory(_fixture.Network, RamMap);

        memory.Reset();
        var watch = Stopwatch.StartNew();
        var countingRun = new IsaEmulator(program, new PhotonicAdderAlu(_fixture.Network), dataMemory: memory);
        countingRun.Run(StepBudget);
        watch.Stop();
        countingRun.IsHalted.ShouldBeTrue("multiply-3x4 must halt within the step budget with both units on light");
        countingRun.Accumulator.ShouldBe(12, "3 × 4 on the photonic ALU and the photonic RAM");
        memory.WriteCount.ShouldBe(ExpectedMultiplyStoreCount, "one write per executed STORE");
        memory.ReadCount.ShouldBe(ExpectedMultiplyRamOperandReadCount, "one read per executed RAM operand");
        watch.Elapsed.ShouldBeLessThan(MultiplyRunBudget,
            $"the photonic multiply ran in {watch.Elapsed} — the budget pins it with headroom");

        memory.Reset();
        var photonicTrace = RunToHalt(new IsaEmulator(
            program, new PhotonicAdderAlu(_fixture.Network), dataMemory: memory));
        var goldenTrace = RunToHalt(new IsaEmulator(program));
        photonicTrace.ShouldBe(goldenTrace,
            "multiply-3x4.asm with ALU and RAM on light must execute exactly as on the golden model");
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

    private static byte[] AssembleShippedMultiplySample()
    {
        var sample = IsaSampleProgramCatalog.LoadDefault().Samples
            .Single(s => s.FileName == "multiply-3x4.asm");
        return new IsaAssembler().Assemble(sample.Source);
    }

    /// <summary>
    /// Shared fixture: loads the shipped combined example once through the real load path
    /// and assembles its logic network through the production assembler exactly as the
    /// Logic panel runs it, so every fact asserts against the same loaded design.
    /// </summary>
    public class AluRamFixture : IAsyncLifetime
    {
        /// <summary>The canvas the shipped example loaded onto.</summary>
        public DesignCanvasViewModel Canvas { get; private set; } = null!;

        /// <summary>The logic network assembled from the loaded design.</summary>
        public CAP_Core.Analysis.LogicAnalysis.LogicNetworkEvaluator Network { get; private set; } = null!;

        /// <summary>Loads the shipped example and assembles its logic network.</summary>
        public async Task InitializeAsync()
        {
            var path = Path.Combine(
                ExampleDesignFilesTests.ExamplesDirectory(), AluRamChipExampleAuthoringTests.ExampleFileName);
            Canvas = new DesignCanvasViewModel();
            var fileOps = Ram4x4FeasibilityTests.CreateFileOperations(Canvas);
            fileOps.ApplyChipSizeAfterLoad = (w, h) => Ram4x4FeasibilityTests.ApplyChipSize(Canvas, w, h);
            (await fileOps.LoadDesignFromPathAsync(path)).ShouldBeTrue(
                $"'{AluRamChipExampleAuthoringTests.ExampleFileName}' must load through the real load path");
            await fileOps.PostLoadRouting;
            Canvas.Connections.Count(c => c.Connection.RoutedPath == null).ShouldBe(0,
                "the combined example must load fully routed from its cache — opening never re-routes");

            Network = await LogicGateFourBitAdderExampleTests.AssembleNetwork(Canvas);
        }

        /// <summary>No shared state to release.</summary>
        public Task DisposeAsync() => Task.CompletedTask;
    }
}
