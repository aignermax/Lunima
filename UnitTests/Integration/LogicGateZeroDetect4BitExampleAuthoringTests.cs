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
/// Authoring utility for the shipped rung-5 stone
/// <c>examples/Logic Gate Zero Detect 4-bit.lun</c> (issue #1307, the photonic zero flag
/// the ISA <c>JZ</c> branches on): three OR slices in a tree — OR01 reads A0 and A1, OR23
/// reads A2 and A3, ORALL combines the two partial ORs — feeding one NOT slice, so the
/// flag tap Z reads NOT(A0 OR A1 OR A2 OR A3): Z = 1 exactly when the 4-bit word is zero.
/// The OR slices are clones of the shipped <c>Logic Gate OR-AND.lun</c> gate at its OR
/// reading (inputs A and B, threshold 0.25), the NOT slice a clone of the shipped
/// <c>Logic Gate NOT-NAND.lun</c> gate at its NOT reading (input A, BIAS constantly on,
/// threshold 0.375). The operand bits arrive through the persisted signal names
/// (issues #1025/#1034), the three inter-slice wires route through the real router, and
/// the output carries the name Z (#1046). The file is written through the real save
/// command — byte-for-byte what the product serializer produces.
/// <para>
/// Gated by <c>CAP_AUTHOR_EXAMPLES=1</c> (unset, the test is a no-op) and
/// <c>Category=Slow</c>:
/// <c>SMART_TEST_EXCLUDE_CATEGORY= CAP_AUTHOR_EXAMPLES=1 dotnet test UnitTests/UnitTests.csproj --filter "FullyQualifiedName~LogicGateZeroDetect4BitExampleAuthoring"</c>
/// </para>
/// </summary>
[Trait("Category", "Slow")]
public class LogicGateZeroDetect4BitExampleAuthoringTests
{
    /// <summary>Environment variable that arms the authoring pass ("1"); unset, the test is a no-op.</summary>
    private const string AuthorEnableVariable = "CAP_AUTHOR_EXAMPLES";

    /// <summary>File name of the example inside <c>examples/</c>.</summary>
    public const string ExampleFileName = "Logic Gate Zero Detect 4-bit.lun";

    private const string OrSourceExampleFileName = "Logic Gate OR-AND.lun";
    private const string NotSourceExampleFileName = "Logic Gate NOT-NAND.lun";
    private const double OrThreshold = 0.25;
    private const double NotThreshold = 0.375;

    /// <summary>Left edge of the first-stage column — the same margin the AND 4-bit example uses.</summary>
    private const double FirstColumnX = 400;

    /// <summary>Top edge of the first OR slice — the same row the AND 4-bit example uses.</summary>
    private const double FirstRowY = 100;

    /// <summary>Vertical gap between the two first-stage OR slices; leaves room for the bends.</summary>
    private const double RowGapY = 200;

    /// <summary>Horizontal gap between the cascade columns; leaves room for the bends.</summary>
    private const double ColumnGapX = 300;

    /// <summary>Right margin behind the NOT slice, mirroring the left one.</summary>
    private const double ChipMarginRight = 400;

    /// <summary>Bottom margin below the lowest slice, mirroring the top one.</summary>
    private const double ChipMarginBottom = 100;

    /// <summary>Height of the OR slice's Y pin above its origin (matches the A pin's).</summary>
    private const double OrOutputPinOffsetY = 26;

    /// <summary>Height of the NOT slice's A pin above its origin.</summary>
    private const double NotInputPinOffsetY = 23.5;

    [Fact]
    public async Task Author_LogicGateZeroDetect4Bit_Example()
    {
        if (Environment.GetEnvironmentVariable(AuthorEnableVariable) != "1")
            return;

        var orSource = await LoadSourceGroup(OrSourceExampleFileName);
        var notSource = await LoadSourceGroup(NotSourceExampleFileName);

        var orWidth = SliceExtent(orSource, c => c.PhysicalX, c => c.WidthMicrometers);
        var orHeight = SliceExtent(orSource, c => c.PhysicalY, c => c.HeightMicrometers);
        var notWidth = SliceExtent(notSource, c => c.PhysicalX, c => c.WidthMicrometers);
        var notHeight = SliceExtent(notSource, c => c.PhysicalY, c => c.HeightMicrometers);

        var or01Y = FirstRowY;
        var or23Y = FirstRowY + orHeight + RowGapY;
        var orAllX = FirstColumnX + orWidth + ColumnGapX;
        var orAllY = (or01Y + or23Y) / 2; // ORALL's pins sit midway between the two drivers
        var notX = orAllX + orWidth + ColumnGapX;
        var notY = orAllY + OrOutputPinOffsetY - NotInputPinOffsetY; // ORALL.Y and NOTZ.A level

        var canvas = new DesignCanvasViewModel();
        var or01 = AddSlice(canvas, orSource, "OR01", OrSliceDescription("OR01", "A0", "A1"),
            FirstColumnX, or01Y, OrRoles(new Dictionary<string, string> { ["A"] = "A0", ["B"] = "A1" }));
        var or23 = AddSlice(canvas, orSource, "OR23", OrSliceDescription("OR23", "A2", "A3"),
            FirstColumnX, or23Y, OrRoles(new Dictionary<string, string> { ["A"] = "A2", ["B"] = "A3" }));
        var orAll = AddSlice(canvas, orSource, "ORALL", OrAllSliceDescription(),
            orAllX, orAllY, OrRoles(null));
        var notZ = AddSlice(canvas, notSource, "NOTZ", NotSliceDescription(),
            notX, notY, new TruthTablePinAssignment
            {
                InputPinNames = new List<string> { "A" },
                OutputPinNames = new List<string> { "Y" },
                BiasPinNames = new List<string> { "BIAS" },
                Threshold = NotThreshold,
                OutputSignalNames = new Dictionary<string, string> { ["Y"] = "Z" },
            });

        await Connect(canvas, or01, "Y", orAll, "A");
        await Connect(canvas, or23, "Y", orAll, "B");
        await Connect(canvas, orAll, "Y", notZ, "A");

        canvas.ChipMaxX = notX + notWidth + ChipMarginRight;
        canvas.ChipMaxY = Math.Max(or23Y + orHeight, notY + notHeight) + ChipMarginBottom;

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

    /// <summary>The OR reading of the shipped OR/AND gate: inputs A and B, threshold 0.25.</summary>
    private static TruthTablePinAssignment OrRoles(Dictionary<string, string>? inputSignalNames) =>
        new()
        {
            InputPinNames = new List<string> { "A", "B" },
            OutputPinNames = new List<string> { "Y" },
            BiasPinNames = new List<string>(),
            Threshold = OrThreshold,
            InputSignalNames = inputSignalNames,
        };

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

    /// <summary>Clones the source gate into one named slice at the given origin.</summary>
    private static ComponentGroup AddSlice(
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
        slice.EnsureSMatrixComputed();

        canvas.AddComponent(slice);

        // MoveGroup aligns the model; sync the VM so the persisted CanvasX/Y match
        // the model origin like a UI drag would.
        var vm = canvas.Components.Single(c => c.Component == slice);
        vm.X = slice.PhysicalX;
        vm.Y = slice.PhysicalY;
        return slice;
    }

    /// <summary>Routes one inter-slice wire through the real router and fails on a fallback.</summary>
    private static async Task Connect(
        DesignCanvasViewModel canvas,
        ComponentGroup driver,
        string driverPin,
        ComponentGroup load,
        string loadPin)
    {
        var vm = await canvas.ConnectPinsAsync(
            driver.PhysicalPins.Single(p => p.Name == driverPin),
            load.PhysicalPins.Single(p => p.Name == loadPin));
        vm.ShouldNotBeNull($"{driver.GroupName}.{driverPin} → {load.GroupName}.{loadPin} must connect");
        vm!.Connection.RoutedPath.ShouldNotBeNull(
            $"{driver.GroupName}.{driverPin} → {load.GroupName}.{loadPin} must route");
        vm.Connection.RoutedPath!.IsBlockedFallback.ShouldBeFalse(
            $"{driver.GroupName}.{driverPin} → {load.GroupName}.{loadPin} must not fall back to a blocked line");
    }

    /// <summary>The education note every first-stage OR slice carries: its OR reading and role.</summary>
    private static string OrSliceDescription(string name, string firstBit, string secondBit) =>
        $"OR reading (inputs A and B, threshold {OrThreshold.ToString(CultureInfo.InvariantCulture)}) " +
        $"of the shipped 'Logic Gate OR-AND' gate. Role in the 4-bit zero detect: {name} reads the " +
        $"operand bits {firstBit} and {secondBit} — its output is 1 when either bit is lit. The " +
        "cascade composes at the logic layer, not optically: every gate's truth table is extracted " +
        "once in isolation, so each stage restores clean 0/1 levels by construction.";

    /// <summary>The education note the combining OR slice carries: it merges the two partial ORs.</summary>
    private static string OrAllSliceDescription() =>
        $"OR reading (inputs A and B, threshold {OrThreshold.ToString(CultureInfo.InvariantCulture)}) " +
        "of the shipped 'Logic Gate OR-AND' gate. Role in the 4-bit zero detect: ORALL combines the " +
        "two partial ORs — its output is 1 when any of A0–A3 is lit. The cascade composes at the " +
        "logic layer, not optically: every gate's truth table is extracted once in isolation, so " +
        "each stage restores clean 0/1 levels by construction.";

    /// <summary>The education note the NOT slice carries: it turns any-light into the zero flag.</summary>
    private static string NotSliceDescription() =>
        $"NOT reading (input A, BIAS constantly on, threshold {NotThreshold.ToString(CultureInfo.InvariantCulture)}) " +
        "of the shipped 'Logic Gate NOT-NAND' gate. Role in the 4-bit zero detect: NOTZ inverts the " +
        "combined OR — its output Z is 1 exactly when A0–A3 are all dark, the zero flag the ISA JZ " +
        "branches on. The cascade composes at the logic layer, not optically: every gate's truth " +
        "table is extracted once in isolation, so each stage restores clean 0/1 levels by construction.";
}
