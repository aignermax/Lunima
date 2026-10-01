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
/// <c>examples/Logic Gate Logic Unit 4-bit.lun</c> (issue #1286): one chip that computes
/// two ISA operations from the same operands — four AND-from-NAND slices AND0–AND3 in the
/// top row and four NOT-NAND slices NOT0–NOT3 in the bottom row, one column per bit. Each
/// AND slice keeps its AND reading (inputs A and B, biases BIAS and BIAS2, threshold 0.25)
/// and each NOT slice its NOT reading (input A, bias BIAS, threshold 0.375). The persisted
/// signal names merge the operand pins into A0–A3 and B0–B3 (issues #1025/#1034) — every
/// A{bit} names both the AND slice's and the NOT slice's A pin, so the operand bit fans out
/// to exactly two gates (the ideal 1×2 split leaves 0.5 per branch, above both thresholds)
/// — and name the output taps Y0–Y3 (AND) and N0–N3 (NOT) (#1046). The file is written
/// through the real save command — byte-for-byte what the product serializer produces.
/// <para>
/// Gated by <c>CAP_AUTHOR_EXAMPLES=1</c> (unset, the test is a no-op) and
/// <c>Category=Slow</c>:
/// <c>SMART_TEST_EXCLUDE_CATEGORY= CAP_AUTHOR_EXAMPLES=1 dotnet test UnitTests/UnitTests.csproj --filter "FullyQualifiedName~LogicGateLogicUnit4BitExampleAuthoring"</c>
/// </para>
/// </summary>
[Trait("Category", "Slow")]
public class LogicGateLogicUnit4BitExampleAuthoringTests
{
    /// <summary>Environment variable that arms the authoring pass ("1"); unset, the test is a no-op.</summary>
    private const string AuthorEnableVariable = "CAP_AUTHOR_EXAMPLES";

    /// <summary>File name of the example inside <c>examples/</c>.</summary>
    public const string ExampleFileName = "Logic Gate Logic Unit 4-bit.lun";

    private const string AndSourceExampleFileName = "Logic Gate AND-from-NAND.lun";
    private const string NotSourceExampleFileName = "Logic Gate NOT-NAND.lun";
    private const double AndThreshold = 0.25;
    private const double NotThreshold = 0.375;

    /// <summary>Bit count of the two operand words — one AND + one NOT slice per bit.</summary>
    private const int BitCount = 4;

    /// <summary>Left edge of the first bit column — the same margin the AND 4-bit example uses.</summary>
    private const double FirstColumnX = 400;

    /// <summary>Top edge of the AND row — the same row the AND 4-bit example uses.</summary>
    private const double AndRowY = 100;

    /// <summary>Horizontal pitch between bit columns; an AND slice is 1440 µm wide, leaving 360 µm gaps.</summary>
    private const double ColumnPitchX = 1800;

    /// <summary>Vertical gap between the AND row's bottom edge and the NOT row's top edge.</summary>
    private const double RowGapY = 100;

    /// <summary>Right margin behind the last column, mirroring the left one.</summary>
    private const double ChipMarginRight = 400;

    /// <summary>Bottom margin below the NOT row, mirroring the top one.</summary>
    private const double ChipMarginBottom = 100;

    [Fact]
    public async Task Author_LogicGateLogicUnit4Bit_Example()
    {
        if (Environment.GetEnvironmentVariable(AuthorEnableVariable) != "1")
            return;

        var andSource = await LoadSourceGroup(AndSourceExampleFileName);
        var notSource = await LoadSourceGroup(NotSourceExampleFileName);

        var andSliceWidth = SliceExtent(andSource, c => c.PhysicalX, c => c.WidthMicrometers);
        var andSliceHeight = SliceExtent(andSource, c => c.PhysicalY, c => c.HeightMicrometers);
        var notSliceHeight = SliceExtent(notSource, c => c.PhysicalY, c => c.HeightMicrometers);
        var notRowY = AndRowY + andSliceHeight + RowGapY;

        var canvas = new DesignCanvasViewModel();
        for (var index = 0; index < BitCount; index++)
        {
            AddSlice(canvas, andSource, $"AND{index}", AndSliceDescription(index),
                FirstColumnX + index * ColumnPitchX, AndRowY,
                new TruthTablePinAssignment
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
                });
            AddSlice(canvas, notSource, $"NOT{index}", NotSliceDescription(index),
                FirstColumnX + index * ColumnPitchX, notRowY,
                new TruthTablePinAssignment
                {
                    InputPinNames = new List<string> { "A" },
                    OutputPinNames = new List<string> { "Y" },
                    BiasPinNames = new List<string> { "BIAS" },
                    Threshold = NotThreshold,
                    InputSignalNames = new Dictionary<string, string> { ["A"] = $"A{index}" },
                    OutputSignalNames = new Dictionary<string, string> { ["Y"] = $"N{index}" },
                });
        }

        canvas.ChipMaxX = FirstColumnX + (BitCount - 1) * ColumnPitchX + andSliceWidth + ChipMarginRight;
        canvas.ChipMaxY = notRowY + notSliceHeight + ChipMarginBottom;

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

    /// <summary>Loads a single-gate example and returns its one top-level gate group.</summary>
    private static async Task<ComponentGroup> LoadSourceGroup(string exampleFileName)
    {
        var sourcePath = Path.Combine(ExampleDesignFilesTests.ExamplesDirectory(), exampleFileName);
        var sourceCanvas = await LogicGateHalfAdderExampleTests.LoadCanvas(sourcePath);
        return sourceCanvas.Components
            .Select(c => c.Component)
            .OfType<ComponentGroup>()
            .Single();
    }

    /// <summary>The slice's extent along one axis, from its origin to the furthest child edge.</summary>
    private static double SliceExtent(
        ComponentGroup group,
        Func<Component, double> origin,
        Func<Component, double> size) =>
        group.ChildComponents.Max(c => origin(c) + size(c)) - origin(group);

    /// <summary>Clones the source gate into one named bit slice at the given origin.</summary>
    private static void AddSlice(
        DesignCanvasViewModel canvas,
        ComponentGroup source,
        string name,
        string description,
        double x,
        double y,
        TruthTablePinAssignment roles)
    {
        var slice = (ComponentGroup)source.Clone();
        slice.GroupName = name;
        slice.Description = description;
        slice.MoveGroup(x - slice.PhysicalX, y - slice.PhysicalY);
        slice.TruthTablePinAssignment = roles;

        canvas.AddComponent(slice);

        // MoveGroup aligns the model; sync the VM so the persisted CanvasX/Y match
        // the model origin like a UI drag would.
        var vm = canvas.Components.Single(c => c.Component == slice);
        vm.X = slice.PhysicalX;
        vm.Y = slice.PhysicalY;
    }

    /// <summary>The education note every AND slice carries: its AND reading and its bit-slice role.</summary>
    private static string AndSliceDescription(int index) =>
        $"AND reading (inputs A and B, BIAS and BIAS2 constantly on, threshold " +
        $"{AndThreshold.ToString(CultureInfo.InvariantCulture)}) of the " +
        $"shipped 'Logic Gate AND-from-NAND' gate. Role in the 4-bit logic unit: bit slice {index} of " +
        $"the shared operand word — Y{index} = A{index} AND B{index}, while the NOT slice below reads " +
        $"the same A{index}. The word operations compose at the logic layer, not optically: every " +
        "gate's truth table is extracted once in isolation, so each slice restores clean 0/1 levels " +
        "by construction and the eight slices run side by side without a single wire between them.";

    /// <summary>The education note every NOT slice carries: its NOT reading and its bit-slice role.</summary>
    private static string NotSliceDescription(int index) =>
        $"NOT reading (input A, BIAS constantly on, threshold " +
        $"{NotThreshold.ToString(CultureInfo.InvariantCulture)}) of the " +
        $"shipped 'Logic Gate NOT-NAND' gate. Role in the 4-bit logic unit: bit slice {index} — " +
        $"N{index} = NOT A{index}, reading the same operand bit as the AND slice above it. The word " +
        "operations compose at the logic layer, not optically: every gate's truth table is extracted " +
        "once in isolation, so each slice restores clean 0/1 levels by construction and the eight " +
        "slices run side by side without a single wire between them.";
}
