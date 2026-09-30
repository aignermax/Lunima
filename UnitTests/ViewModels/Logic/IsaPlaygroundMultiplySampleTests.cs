using System.Linq;
using CAP.Avalonia.ViewModels.Logic.IsaPlayground;
using CAP_Core.Logic.Isa;
using Shouldly;
using UnitTests.Integration;
using Xunit;

namespace UnitTests.ViewModels.Logic;

/// <summary>
/// Acceptance for the multiply-3x4 sample (issue #1239): the shipped
/// <c>examples/isa/multiply-3x4.asm</c> must appear in the catalog with a localized
/// name, compute 3 x 4 = 12 by repeated addition on the golden model (halting with
/// ACC = 12 after an exact, bounded step count), and produce the identical per-step
/// trace (PC, ACC, RAM) when every ADD runs on <see cref="PhotonicAdderAlu"/> over
/// the shipped 4-bit adder network — the playground's first "real" program, the
/// NAND2TETRIS moment of building an operation the hardware does not have.
/// </summary>
[Collection("LocalizationSingleton")]
public class IsaPlaygroundMultiplySampleTests
    : IClassFixture<LogicGateFourBitAdderExampleTests.FourBitAdderFixture>
{
    /// <summary>Generous bound far above the expected 33 steps; guards against an endless loop.</summary>
    private const int StepBudget = 100;

    /// <summary>The exact number of instructions the sample executes until HALT.</summary>
    private const int ExpectedStepCount = 33;

    private const string SampleFileName = "multiply-3x4.asm";
    private const string SampleDisplayNameKey = "IsaPlayground.SampleMultiply3x4";

    private readonly LogicGateFourBitAdderExampleTests.FourBitAdderFixture _fixture;

    /// <summary>Attaches the shared 4-bit-adder fixture (assembles the network once).</summary>
    public IsaPlaygroundMultiplySampleTests(LogicGateFourBitAdderExampleTests.FourBitAdderFixture fixture) =>
        _fixture = fixture;

    [Fact]
    public void Catalog_LoadDefault_ContainsMultiplySample_WithLocalizedName()
    {
        var catalog = IsaSampleProgramCatalog.LoadDefault();

        var sample = catalog.Samples.SingleOrDefault(s => s.FileName == SampleFileName);
        sample.ShouldNotBeNull("the multiply sample must ship in examples/isa and reach the picker");
        sample.DisplayNameKey.ShouldBe(SampleDisplayNameKey);
        sample.DisplayName.ShouldNotBe(SampleDisplayNameKey,
            "the display name must resolve through the string tables, not fall back to the key");
        sample.DisplayName.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void GoldenModel_Multiply3x4_HaltsWithAcc12_AfterExactStepCount()
    {
        var program = AssembleShippedSample();
        var emulator = new IsaEmulator(program);

        var steps = emulator.Run(StepBudget);

        emulator.IsHalted.ShouldBeTrue("multiply-3x4 must halt within the step budget");
        steps.ShouldBe(ExpectedStepCount);
        emulator.Accumulator.ShouldBe(12, "3 x 4 by repeated addition must end with ACC = 12");
    }

    [Fact]
    public void PhotonicAdder_Multiply3x4_PerStepTraceMatchesGoldenModel()
    {
        PhotonicAdderAlu.Accepts(_fixture.Network).ShouldBeTrue(
            "the shipped 4-bit adder must drive the photonic ALU");
        var program = AssembleShippedSample();
        var photonicAlu = new PhotonicAdderAlu(_fixture.Network);

        var photonicTrace = RunWithTrace(new IsaEmulator(program, photonicAlu));
        var goldenTrace = RunWithTrace(new IsaEmulator(program));

        photonicTrace.ShouldBe(goldenTrace,
            "every loop ADD runs on the photonic adder, yet the machine must evolve identically");
        photonicTrace.Count.ShouldBe(ExpectedStepCount);
        photonicAlu.LastAddTrace.ShouldNotBeNull("at least one ADD must have run photonically");
        photonicAlu.LastAddTrace.Sum.ShouldBe(12,
            "the last photonic ADD (0 + RAM[0] reloading the total) produces the final 12");
    }

    /// <summary>Assembles the shipped multiply sample exactly as the playground loads it.</summary>
    private static byte[] AssembleShippedSample()
    {
        var sample = IsaSampleProgramCatalog.LoadDefault().Samples.Single(s => s.FileName == SampleFileName);
        return new IsaAssembler().Assemble(sample.Source);
    }

    /// <summary>Runs the machine to HALT, recording PC, ACC and RAM after every step.</summary>
    private static System.Collections.Generic.List<string> RunWithTrace(IsaEmulator emulator)
    {
        var trace = new System.Collections.Generic.List<string>();
        while (!emulator.IsHalted && trace.Count < StepBudget)
        {
            emulator.Step();
            trace.Add($"PC={emulator.ProgramCounter} ACC={emulator.Accumulator} " +
                $"RAM=[{string.Join("  ", emulator.Ram)}]");
        }

        emulator.IsHalted.ShouldBeTrue("multiply-3x4 must halt within the step budget");
        emulator.Accumulator.ShouldBe(12);
        return trace;
    }
}
