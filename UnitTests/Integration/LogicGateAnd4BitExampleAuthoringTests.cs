using System.Globalization;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;
using CAP_Core.Components.Process;
using Moq;
using Shouldly;
using Xunit;

namespace UnitTests.Integration;

/// <summary>
/// Authoring utility for the shipped rung-5 datapath stone
/// <c>examples/Logic Gate AND 4-bit.lun</c> (issue #1265, ISA <c>AND</c>): loads the
/// shipped single-slice <c>Logic Gate AND-from-NAND.lun</c> (#971), clones its gate group
/// into four bit slices AND0–AND3 standing side by side without wires, and persists each
/// slice's pin roles at the AND reading (inputs A and B, biases BIAS and BIAS2, threshold
/// 0.25) with the network signal names that merge the operand pins into A0–A3 and B0–B3
/// (issues #1025/#1034) and name the output taps Y0–Y3 (#1046). The file is written
/// through the real save command — byte-for-byte what the product serializer produces.
/// <para>
/// Gated by <c>CAP_AUTHOR_EXAMPLES=1</c> (unset, the test is a no-op) and
/// <c>Category=Slow</c>:
/// <c>SMART_TEST_EXCLUDE_CATEGORY= CAP_AUTHOR_EXAMPLES=1 dotnet test UnitTests/UnitTests.csproj --filter "FullyQualifiedName~LogicGateAnd4BitExampleAuthoring"</c>
/// </para>
/// </summary>
[Trait("Category", "Slow")]
public class LogicGateAnd4BitExampleAuthoringTests
{
    /// <summary>Environment variable that arms the authoring pass ("1"); unset, the test is a no-op.</summary>
    private const string AuthorEnableVariable = "CAP_AUTHOR_EXAMPLES";

    /// <summary>File name of the example inside <c>examples/</c>.</summary>
    public const string ExampleFileName = "Logic Gate AND 4-bit.lun";

    private const string SourceExampleFileName = "Logic Gate AND-from-NAND.lun";
    private const double AndThreshold = 0.25;

    /// <summary>Left edge of the first slice — the same margin the NOT 4-bit example uses.</summary>
    private const double FirstSliceX = 400;

    /// <summary>Row height of the slices — the same row the NOT 4-bit example uses.</summary>
    private const double SliceY = 100;

    /// <summary>Horizontal pitch between slice origins; a slice is 1440 µm wide, leaving 360 µm gaps.</summary>
    private const double SlicePitchX = 1800;

    /// <summary>Right margin behind the last slice, mirroring the left one.</summary>
    private const double ChipMarginRight = 400;

    /// <summary>Chip height — matches the NOT 4-bit example so the row sits identically.</summary>
    private const double ChipHeight = 607.5;

    [Fact]
    public async Task Author_LogicGateAnd4Bit_Example()
    {
        if (Environment.GetEnvironmentVariable(AuthorEnableVariable) != "1")
            return;

        var sourcePath = Path.Combine(ExampleDesignFilesTests.ExamplesDirectory(), SourceExampleFileName);
        var sourceCanvas = await LogicGateHalfAdderExampleTests.LoadCanvas(sourcePath);
        var sourceGroup = sourceCanvas.Components
            .Select(c => c.Component)
            .OfType<ComponentGroup>()
            .Single();

        var canvas = new DesignCanvasViewModel();
        for (var index = 0; index < 4; index++)
        {
            var slice = (ComponentGroup)sourceGroup.Clone();
            slice.GroupName = $"AND{index}";
            slice.Description = SliceDescription(index);
            slice.MoveGroup(
                FirstSliceX + index * SlicePitchX - slice.PhysicalX,
                SliceY - slice.PhysicalY);
            slice.TruthTablePinAssignment = new TruthTablePinAssignment
            {
                InputPinNames = new List<string> { "A", "B" },
                OutputPinNames = new List<string> { "Y" },
                BiasPinNames = new List<string> { "BIAS", "BIAS2" },
                Threshold = AndThreshold,
                InputSignalNames = new Dictionary<string, string>
                {
                    ["A"] = $"A{index}",
                    ["B"] = $"B{index}",
                },
                OutputSignalNames = new Dictionary<string, string> { ["Y"] = $"Y{index}" },
            };

            canvas.AddComponent(slice);

            // MoveGroup aligns the model; sync the VM so the persisted CanvasX/Y match
            // the model origin like a UI drag would.
            var vm = canvas.Components.Single(c => c.Component == slice);
            vm.X = slice.PhysicalX;
            vm.Y = slice.PhysicalY;
        }

        var sliceWidth = sourceGroup.ChildComponents.Max(c => c.PhysicalX + c.WidthMicrometers)
            - sourceGroup.PhysicalX;
        canvas.ChipMaxX = FirstSliceX + 3 * SlicePitchX + sliceWidth + ChipMarginRight;
        canvas.ChipMaxY = ChipHeight;

        var examplePath = Path.Combine(ExampleDesignFilesTests.ExamplesDirectory(), ExampleFileName);
        var fileOps = LogicGateHalfAdderExampleTests.CreateFileOperations(canvas);
        fileOps.SetActiveProcess(ActiveProcessSelection.Playground());
        var dialog = new Mock<IFileDialogService>();
        dialog.Setup(f => f.ShowSaveFileDialogAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(examplePath);
        fileOps.FileDialogService = dialog.Object;

        await fileOps.SaveDesignAsCommand.ExecuteAsync(null);

        File.Exists(examplePath).ShouldBeTrue($"the example must be written to {examplePath}");
    }

    /// <summary>The education note every slice carries: its AND reading and its bit-slice role.</summary>
    private static string SliceDescription(int index) =>
        $"AND reading (inputs A and B, BIAS and BIAS2 constantly on, threshold " +
        $"{AndThreshold.ToString(CultureInfo.InvariantCulture)}) of the " +
        $"shipped 'Logic Gate AND-from-NAND' gate. Role in the 4-bit AND: bit slice {index} of the " +
        $"ISA AND datapath — Y{index} = A{index} AND B{index}. The 4-bit word operation composes at " +
        "the logic layer, not optically: every gate's truth table is extracted once in isolation, " +
        "so each slice restores clean 0/1 levels by construction and the four slices run side by " +
        "side without a single wire between them.";
}
