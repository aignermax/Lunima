using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Library;
using CAP_Core.Analysis.LogicAnalysis;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Components.Creation;
using CAP_Core.Routing;
using UnitTests.Helpers;

namespace UnitTests.Analysis.LogicAnalysis;

/// <summary>
/// Shared fixture plumbing for <see cref="LogicCellInstanceAssemblyTests"/>: builds the
/// two-gate buffer cell (BUF1 → BUF2 chained by a frozen intra-cell path, the ends
/// exposed as the cell ports "in"/"out"), the buffer gates themselves (the 50/50
/// combiner fixture read single-input: y = a at the threshold), and the assemble /
/// save-load helpers that run the real library and canvas paths.
/// </summary>
internal static class LogicCellInstanceTestBed
{
    /// <summary>Threshold of the single-input buffer reading of the combiner fixture.</summary>
    public const double BufferThreshold = 0.25;

    /// <summary>
    /// The cell: two buffer gates wired by a frozen intra-cell path, the chain's ends
    /// exposed as the cell ports "in" and "out".
    /// </summary>
    public static ComponentGroup BuildBufferCell(
        string firstGate, string secondGate, Func<string, ComponentGroup> gateFactory)
    {
        var first = gateFactory(firstGate);
        var second = gateFactory(secondGate);
        second.PhysicalX = 400;
        var cell = new ComponentGroup("BUFFER-CELL");
        cell.AddChild(first);
        cell.AddChild(second);
        cell.AddInternalPath(FrozenPath(InternalPin(first, "y"), InternalPin(second, "a")));
        cell.AddExternalPin(new GroupPin { Name = "in", InternalPin = InternalPin(first, "a") });
        cell.AddExternalPin(new GroupPin { Name = "out", InternalPin = InternalPin(second, "y") });
        return cell;
    }

    /// <summary>The combiner fixture group re-read as a one-input buffer: y = a at threshold 0.25.</summary>
    public static ComponentGroup RawBufferGate(string name)
    {
        var gate = LogicGateFixtureFactory.CreateCombinerGroup();
        gate.GroupName = name;
        AssignBufferRoles(gate);
        gate.EnsureSMatrixComputed();
        return gate;
    }

    /// <summary>
    /// A buffer gate wrapping a combiner created from the persistence-capable fixture
    /// template, so the gate survives the .lun design round trip.
    /// </summary>
    public static ComponentGroup TemplateBufferGate(string name)
    {
        var combiner = ComponentTemplates.CreateFromTemplate(
            LogicGateFixtureFactory.CreateCombinerTemplate(), 0, 0);
        combiner.Identifier = $"combine_{name}";
        var gate = new ComponentGroup(name);
        gate.AddChild(combiner);
        gate.AddExternalPin(new GroupPin { Name = "a", InternalPin = Pin(combiner, "in1") });
        gate.AddExternalPin(new GroupPin { Name = "y", InternalPin = Pin(combiner, "out1") });
        AssignBufferRoles(gate);
        gate.EnsureSMatrixComputed();
        return gate;
    }

    /// <summary>Assigns the one-in/one-out buffer roles to a gate group.</summary>
    public static void AssignBufferRoles(ComponentGroup gate)
    {
        gate.TruthTablePinAssignment = new TruthTablePinAssignment
        {
            InputPinNames = new List<string> { "a" },
            OutputPinNames = new List<string> { "y" },
            BiasPinNames = new List<string>(),
            Threshold = BufferThreshold,
        };
    }

    /// <summary>Wraps one gate in a role-less group — the same-named-wrapper duplicate shape.</summary>
    public static ComponentGroup WrapperWith(string name, ComponentGroup gate)
    {
        var wrapper = new ComponentGroup(name);
        wrapper.AddChild(gate);
        return wrapper;
    }

    /// <summary>The physical pin behind one of a group's external pins (the cell ports).</summary>
    public static PhysicalPin Port(ComponentGroup group, string name) =>
        group.ExternalPins.Single(p => p.Name == name).InternalPin!;

    /// <summary>The internal component pin behind one of a gate's external pins.</summary>
    public static PhysicalPin InternalPin(ComponentGroup group, string name) =>
        group.ExternalPins.Single(p => p.Name == name).InternalPin!;

    /// <summary>Looks up a component's physical pin by name.</summary>
    public static PhysicalPin Pin(Component component, string name) =>
        component.PhysicalPins.Single(p => p.Name == name);

    /// <summary>Freezes a straight path between two internal pins, as grouping a routed wire does.</summary>
    public static FrozenWaveguidePath FrozenPath(PhysicalPin from, PhysicalPin to) =>
        new() { Path = StraightPath(from, to), StartPin = from, EndPin = to };

    /// <summary>A straight routed path between the absolute positions of two pins.</summary>
    public static RoutedPath StraightPath(PhysicalPin from, PhysicalPin to)
    {
        var (x1, y1) = from.GetAbsolutePosition();
        var (x2, y2) = to.GetAbsolutePosition();
        var path = new RoutedPath();
        path.Segments.Add(new StraightSegment(x1, y1, x2, y2, 0));
        return path;
    }

    /// <summary>Runs the assembler at the fixture wavelength.</summary>
    public static Task<LogicNetworkEvaluator> Assemble(
        IReadOnlyList<Component> components, IReadOnlyList<WaveguideConnection> connections) =>
        new LogicNetworkAssembler().AssembleAsync(
            components, connections, LogicGateFixtureFactory.WavelengthNm);

    /// <summary>Assembles straight from the canvas, the way the Logic panel runs it.</summary>
    public static Task<LogicNetworkEvaluator> AssembleCanvas(DesignCanvasViewModel canvas) =>
        Assemble(
            canvas.Components.Select(c => c.Component).ToList(),
            canvas.Connections.Select(c => c.Connection).ToList());

    /// <summary>Creates the real MainViewModel wiring with the fixture templates registered.</summary>
    public static MainViewModel CreateMainViewModel(DesignCanvasViewModel canvas)
    {
        var mainVm = MainViewModelTestHelper.CreateMainViewModel(canvas: canvas);
        // FileOperations resolves saved components against LeftPanel.AllTemplates.
        mainVm.LeftPanel.AllTemplates.Add(LogicGateFixtureFactory.CreateCombinerTemplate());
        return mainVm;
    }

    /// <summary>Builds an input-bit dictionary from (name, bit) pairs.</summary>
    public static Dictionary<string, bool> Bits(params (string Name, bool Bit)[] bits) =>
        bits.ToDictionary(pair => pair.Name, pair => pair.Bit);
}
