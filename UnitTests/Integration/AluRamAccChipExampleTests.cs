using System.Diagnostics;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Logic.IsaPlayground;
using CAP_Core.Analysis;
using CAP_Core.Components.Core;
using CAP_Core.Logic.Isa;
using Shouldly;
using UnitTests.Helpers;
using UnitTests.Integration.RamScale;
using Xunit;

namespace UnitTests.Integration;

/// <summary>
/// Rung-5 acceptance for the shipped <c>examples/Logic Gate ALU + RAM + ACC.lun</c>
/// (issue #1470): the ALU + RAM chip (#1463) plus a 4-bit load-enabled register built
/// from four shipped <c>Logic Gate Bit.lun</c> cells, wrapped in one <c>ACC</c> group
/// with every persisted signal name at the <see cref="IsaAccumulatorSignalMap"/>
/// defaults (<c>ACC.D0</c>–<c>ACC.D3</c>, <c>ACC.LOAD</c> → <c>ACC.Q0</c>–<c>ACC.Q3</c>).
/// The one assembled network is accepted by <see cref="PhotonicAdderAlu"/>,
/// <see cref="PhotonicDataMemory"/> (with the <c>RAM.</c> prefix map) AND
/// <see cref="PhotonicAccumulator"/>. The end-to-end proof runs the shipped
/// <c>multiply-3x4.asm</c> on an <see cref="IsaEmulator"/> whose ALU, data memory and
/// accumulator are all photonic over this network: the per-step trace (PC/ACC/RAM)
/// must equal the golden run, the machine must halt with ACC = 12 and the accumulator
/// write count must prove the ACC really ran on light.
/// </summary>
public class AluRamAccChipExampleTests : IClassFixture<AluRamAccChipExampleTests.AluRamAccFixture>
{
    private const int StepBudget = 500;

    /// <summary>STORE 1, STORE 3 plus one STORE 0 per loop round (4 rounds) in multiply-3x4.</summary>
    private const int ExpectedMultiplyStoreCount = 6;

    /// <summary>ADD 1 and ADD 3 per round (4 + 4), ADD 0 per non-final round (3) and once after the loop.</summary>
    private const int ExpectedMultiplyRamOperandReadCount = 12;

    /// <summary>Pinned wall-clock budget for the full photonic multiply run (generous CI headroom).</summary>
    private static readonly TimeSpan MultiplyRunBudget = TimeSpan.FromSeconds(120);

    private static readonly IsaDataMemorySignalMap RamMap =
        IsaDataMemorySignalMap.WithPrefix(AluRamChipExampleAuthoringTests.RamSignalPrefix);

    private readonly AluRamAccFixture _fixture;

    /// <summary>Attaches the shared combined-example fixture (loads and assembles once).</summary>
    public AluRamAccChipExampleTests(AluRamAccFixture fixture) => _fixture = fixture;

    [Fact]
    public void Example_LoadsAllThreeBlocks_OnOneChip()
    {
        var groups = _fixture.Canvas.Components
            .Select(c => c.Component).OfType<ComponentGroup>().ToList();
        groups.Count.ShouldBe(346,
            "the adder's 344 top-level gate groups plus the wrapped RAM block plus the wrapped ACC register");
        groups.Select(g => g.GroupName).ShouldContain(AluRamChipExampleAuthoringTests.RamGroupName);
        groups.Select(g => g.GroupName).ShouldContain(AluRamAccChipExampleAuthoringTests.AccGroupName);
        ExampleWires.LogicalWireCount(_fixture.Canvas).ShouldBe(339,
            "the adder's 339 wires stay top level; the RAM's and the register's wires are frozen inside their groups");
    }

    [Fact]
    public void AssembledNetwork_IsAcceptedByAllThreePhotonicUnits()
    {
        PhotonicAdderAlu.Accepts(_fixture.Network).ShouldBeTrue(
            "the adder keeps its plain persisted signal names (A0–A3, B0–B3, Cin → S0–S3, Cout)");
        PhotonicDataMemory.Accepts(_fixture.Network, RamMap).ShouldBeTrue(
            "the RAM's signals live under the 'RAM.' prefix (RAM.A0/A1, RAM.LOAD, RAM.D0–D3 → RAM.Q0–Q3)");
        PhotonicAccumulator.Accepts(_fixture.Network).ShouldBeTrue(
            "the register exposes the accumulator signals ACC.D0–ACC.D3, ACC.LOAD → ACC.Q0–ACC.Q3");
        PhotonicDataMemory.Accepts(_fixture.Network).ShouldBeFalse(
            "the default RAM map must NOT match — the plain names belong to the adder");
        _fixture.Network.RegisterState.Count.ShouldBe(
            IsaMachine.RamWords * IsaMachine.DataBits + IsaMachine.DataBits,
            "the sixteen RAM register bits plus the four accumulator register bits");
    }

    [Fact]
    public async Task DesignValidator_ReportsNoNewOverlapsOrGeometryViolations_VersusAluPlusRam()
    {
        var added = Validate(_fixture.Canvas);
        var baseline = Validate(await LoadCanvas(AluRamChipExampleAuthoringTests.ExampleFileName));

        foreach (var type in added.Keys.Concat(baseline.Keys).Distinct())
        {
            added.GetValueOrDefault(type).ShouldBe(baseline.GetValueOrDefault(type),
                $"placing the ACC register must not add {type} issues versus the ALU + RAM chip");
        }
    }

    /// <summary>Counts the validator's non-blocked issues by type (blocked wires are pinned separately).</summary>
    private static Dictionary<DesignIssueType, int> Validate(DesignCanvasViewModel canvas)
    {
        var issues = new DesignValidator().Validate(
            canvas.Connections.Select(vm => vm.Connection).ToList(),
            canvas.Components.Select(c => c.Component).OfType<ComponentGroup>().ToList());
        return issues.Where(i => i.Type != DesignIssueType.BlockedPath)
            .GroupBy(i => i.Type)
            .ToDictionary(g => g.Key, g => g.Count());
    }

    /// <summary>Loads one shipped example through the real load path, fully routed from its cache.</summary>
    private static async Task<DesignCanvasViewModel> LoadCanvas(string fileName)
    {
        var canvas = new DesignCanvasViewModel();
        var fileOps = Ram4x4FeasibilityTests.CreateFileOperations(canvas);
        fileOps.ApplyChipSizeAfterLoad = (w, h) => Ram4x4FeasibilityTests.ApplyChipSize(canvas, w, h);
        var path = Path.Combine(ExampleDesignFilesTests.ExamplesDirectory(), fileName);
        (await fileOps.LoadDesignFromPathAsync(path)).ShouldBeTrue($"'{fileName}' must load");
        await fileOps.PostLoadRouting;
        return canvas;
    }

    [Fact]
    public void Multiply3x4_OnThreePhotonicUnits_ProducesTheGoldenTrace_AndHaltsWithTwelve()
    {
        var program = AssembleShippedMultiplySample();
        var memory = new PhotonicDataMemory(_fixture.Network, RamMap);
        var accumulator = new PhotonicAccumulator(_fixture.Network);

        memory.Reset();
        accumulator.Reset();
        var watch = Stopwatch.StartNew();
        var countingRun = new IsaEmulator(program, new PhotonicAdderAlu(_fixture.Network),
            dataMemory: memory, accumulator: accumulator);
        countingRun.Run(StepBudget);
        watch.Stop();
        countingRun.IsHalted.ShouldBeTrue("multiply-3x4 must halt within the step budget with three units on light");
        countingRun.Accumulator.ShouldBe(12, "3 × 4 on the photonic ALU, RAM and accumulator");
        memory.WriteCount.ShouldBe(ExpectedMultiplyStoreCount, "one write per executed STORE");
        memory.ReadCount.ShouldBe(ExpectedMultiplyRamOperandReadCount, "one read per executed RAM operand");
        accumulator.WriteCount.ShouldBeGreaterThan(0,
            "every instruction writes the ACC — the accumulator really ran on light");
        watch.Elapsed.ShouldBeLessThan(MultiplyRunBudget,
            $"the photonic multiply ran in {watch.Elapsed} — the budget pins it with headroom");

        memory.Reset();
        accumulator.Reset();
        var photonicTrace = RunToHalt(new IsaEmulator(program, new PhotonicAdderAlu(_fixture.Network),
            dataMemory: memory, accumulator: accumulator));
        var goldenTrace = RunToHalt(new IsaEmulator(program));
        photonicTrace.ShouldBe(goldenTrace,
            "multiply-3x4.asm with ALU, RAM and ACC on light must execute exactly as on the golden model");
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
    public class AluRamAccFixture : IAsyncLifetime
    {
        /// <summary>The canvas the shipped example loaded onto.</summary>
        public DesignCanvasViewModel Canvas { get; private set; } = null!;

        /// <summary>The logic network assembled from the loaded design.</summary>
        public CAP_Core.Analysis.LogicAnalysis.LogicNetworkEvaluator Network { get; private set; } = null!;

        /// <summary>Loads the shipped example and assembles its logic network.</summary>
        public async Task InitializeAsync()
        {
            var path = Path.Combine(
                ExampleDesignFilesTests.ExamplesDirectory(), AluRamAccChipExampleAuthoringTests.ExampleFileName);
            Canvas = new DesignCanvasViewModel();
            var fileOps = Ram4x4FeasibilityTests.CreateFileOperations(Canvas);
            fileOps.ApplyChipSizeAfterLoad = (w, h) => Ram4x4FeasibilityTests.ApplyChipSize(Canvas, w, h);
            (await fileOps.LoadDesignFromPathAsync(path)).ShouldBeTrue(
                $"'{AluRamAccChipExampleAuthoringTests.ExampleFileName}' must load through the real load path");
            await fileOps.PostLoadRouting;
            Canvas.Connections.Count(c => c.Connection.RoutedPath == null).ShouldBe(0,
                "the combined example must load fully routed from its cache — opening never re-routes");

            Network = await LogicGateFourBitAdderExampleTests.AssembleNetwork(Canvas);
        }

        /// <summary>No shared state to release.</summary>
        public Task DisposeAsync() => Task.CompletedTask;
    }
}
