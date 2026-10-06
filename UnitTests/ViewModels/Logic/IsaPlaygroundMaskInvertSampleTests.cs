using CAP.Avalonia.ViewModels.Logic.IsaPlayground;
using CAP_Core.Logic.Isa;
using Shouldly;
using UnitTests.Integration;
using Xunit;

namespace UnitTests.ViewModels.Logic;

/// <summary>
/// Acceptance for the "Mask &amp; invert" sample (issue #1295): the shipped
/// <c>examples/isa/mask-and-invert.asm</c> must appear in the catalog with a
/// localized name and compute ~(1100 &amp; 1010) &amp; 0xF = 7 on the golden
/// model, halting with ACC = 7 (RAM[0] = 10, RAM[1] = 7) after an exact, bounded
/// step count. On the shipped Logic Unit 4-bit network the identical program runs
/// with AND and NOT both photonic (<see cref="PhotonicAndAlu"/> on the Y0–Y3
/// taps, <see cref="PhotonicNotAlu"/> on the N0–N3 taps of
/// <see cref="IsaAluSignalMap.CombinedLogicUnitNot"/>) and produces the identical
/// per-step trace (PC, ACC, RAM).
/// </summary>
[Collection("LocalizationSingleton")]
public class IsaPlaygroundMaskInvertSampleTests
    : IClassFixture<LogicGateLogicUnit4BitExampleTests.LogicUnit4BitFixture>
{
    /// <summary>Generous bound far above the expected 7 steps; guards against an endless loop.</summary>
    private const int StepBudget = 100;

    /// <summary>The exact number of instructions the sample executes until HALT.</summary>
    private const int ExpectedStepCount = 7;

    private const string SampleFileName = "mask-and-invert.asm";
    private const string SampleDisplayNameKey = "IsaPlayground.SampleMaskAndInvert";

    private readonly LogicGateLogicUnit4BitExampleTests.LogicUnit4BitFixture _fixture;

    /// <summary>Attaches the shared logic-unit fixture (assembles the network once).</summary>
    public IsaPlaygroundMaskInvertSampleTests(
        LogicGateLogicUnit4BitExampleTests.LogicUnit4BitFixture fixture) =>
        _fixture = fixture;

    [Fact]
    public void Catalog_LoadDefault_ContainsMaskInvertSample_WithLocalizedName()
    {
        var catalog = IsaSampleProgramCatalog.LoadDefault();

        var sample = catalog.Samples.SingleOrDefault(s => s.FileName == SampleFileName);
        sample.ShouldNotBeNull("the Mask & invert sample must ship in examples/isa and reach the picker");
        sample.DisplayNameKey.ShouldBe(SampleDisplayNameKey);
        sample.DisplayName.ShouldNotBe(SampleDisplayNameKey,
            "the display name must resolve through the string tables, not fall back to the key");
        sample.DisplayName.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void GoldenModel_MaskAndInvert_HaltsWithAcc7_AfterExactStepCount()
    {
        var program = AssembleShippedSample();
        var emulator = new IsaEmulator(program);

        var steps = emulator.Run(StepBudget);

        emulator.IsHalted.ShouldBeTrue("mask-and-invert must halt within the step budget");
        steps.ShouldBe(ExpectedStepCount);
        emulator.Accumulator.ShouldBe(7, "~(1100 & 1010) & 0xF = 0111");
        emulator.Ram[0].ShouldBe(10, "RAM[0] keeps the mask 1010");
        emulator.Ram[1].ShouldBe(7, "RAM[1] keeps the masked-and-inverted result");
    }

    [Fact]
    public void PhotonicLogicUnit_MaskAndInvert_PerStepTraceMatchesGoldenModel()
    {
        PhotonicAndAlu.Accepts(_fixture.Network).ShouldBeTrue(
            "the shipped Logic Unit 4-bit must drive the photonic AND");
        PhotonicNotAlu.Accepts(_fixture.Network, IsaAluSignalMap.CombinedLogicUnitNot).ShouldBeTrue(
            "the shipped Logic Unit 4-bit must drive the photonic NOT on its N0–N3 taps");
        var program = AssembleShippedSample();
        var composite = new CompositeIsaAlu(
            new GoldenIsaAlu(),
            new PhotonicNotAlu(_fixture.Network, IsaAluSignalMap.CombinedLogicUnitNot),
            new PhotonicAndAlu(_fixture.Network));

        var photonicTrace = RunWithTrace(new IsaEmulator(program, composite));
        var goldenTrace = RunWithTrace(new IsaEmulator(program));

        photonicTrace.ShouldBe(goldenTrace,
            "AND and NOT both run on the photonic chip, yet the machine must evolve identically");
        photonicTrace.Count.ShouldBe(ExpectedStepCount);
    }

    /// <summary>Assembles the shipped Mask &amp; invert sample exactly as the playground loads it.</summary>
    private static byte[] AssembleShippedSample()
    {
        var sample = IsaSampleProgramCatalog.LoadDefault().Samples.Single(s => s.FileName == SampleFileName);
        return new IsaAssembler().Assemble(sample.Source);
    }

    /// <summary>Runs the machine to HALT, recording PC, ACC and RAM after every step.</summary>
    private static List<string> RunWithTrace(IsaEmulator emulator)
    {
        var trace = new List<string>();
        while (!emulator.IsHalted && trace.Count < StepBudget)
        {
            emulator.Step();
            trace.Add($"PC={emulator.ProgramCounter} ACC={emulator.Accumulator} " +
                $"RAM=[{string.Join("  ", emulator.Ram)}]");
        }

        emulator.IsHalted.ShouldBeTrue("mask-and-invert must halt within the step budget");
        emulator.Accumulator.ShouldBe(7);
        return trace;
    }
}
