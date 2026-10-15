using CAP.Avalonia.Commands;
using CAP.Avalonia.Commands.Dies;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;
using CAP_Core.Components.Dies;
using CAP_Core.Routing;
using static UnitTests.Dies.DieTestFixtures;

namespace UnitTests.Dies.WorkSurface;

/// <summary>
/// A two-die assembly built through the real New-die command: "Die 1" (the former chip)
/// holds parts a and b joined by a routed wire, "Die 2" sits to its right, empty.
/// </summary>
internal sealed class WorkSurfaceCanvas
{
    /// <summary>Single-die chip size before New die.</summary>
    public const double ChipSize = 2000;

    /// <summary>The canvas, at the assembly root.</summary>
    public DesignCanvasViewModel Canvas { get; } = new();

    /// <summary>Undo history the commands of a test run through.</summary>
    public CommandManager Commands { get; } = new();

    /// <summary>The die the chip became.</summary>
    public ComponentGroup Die1 { get; }

    /// <summary>The new, empty die.</summary>
    public ComponentGroup Die2 { get; }

    /// <summary>Part a on Die 1.</summary>
    public Component A { get; }

    /// <summary>Part b on Die 1.</summary>
    public Component B { get; }

    /// <summary>The wire a → b (live on the assembly, owned by Die 1).</summary>
    public WaveguideConnectionViewModel Wire { get; }

    public WorkSurfaceCanvas()
    {
        Canvas.SetRootChipSize(ChipSize, ChipSize);
        var a = Canvas.AddComponent(Part("a", 100, 100));
        var b = Canvas.AddComponent(Part("b", 400, 100));
        A = a.Component;
        B = b.Component;
        Wire = Canvas.ConnectPinsWithCachedRoute(A.PhysicalPins[1], B.PhysicalPins[0], StraightBetween(A.PhysicalPins[1], B.PhysicalPins[0]))!;

        var newDie = new CreateDieCommand(Canvas,
            new DieSpec("Die 2", null, 1000, 1000),
            new DieSpec("Die 1", null, ChipSize, ChipSize));
        Commands.ExecuteCommand(newDie);
        Die1 = newDie.ConvertedChipDie!;
        Die2 = newDie.CreatedDie!;
    }

    /// <summary>The die-surface view model of <paramref name="part"/>.</summary>
    public ComponentViewModel Vm(Component part) => Canvas.Surface.ViewModelFor(part);

    /// <summary>The top-level view model of a die.</summary>
    public ComponentViewModel DieVm(ComponentGroup die) => Canvas.Components.Single(c => ReferenceEquals(c.Component, die));

    /// <summary>A straight routed path between two facing pins.</summary>
    public static RoutedPath StraightBetween(PhysicalPin start, PhysicalPin end)
    {
        var (x1, y1) = start.GetAbsolutePosition();
        var (x2, y2) = end.GetAbsolutePosition();
        var path = new RoutedPath();
        path.Segments.Add(new StraightSegment(x1, y1, x2, y2, start.GetAbsoluteAngle()));
        return path;
    }

    /// <summary>Start/end points of every segment of a path, for exact geometry comparisons.</summary>
    public static List<(double, double, double, double)> Geometry(RoutedPath? path) =>
        path?.Segments.Select(s => (s.StartPoint.X, s.StartPoint.Y, s.EndPoint.X, s.EndPoint.Y)).ToList() ?? new();

    /// <summary>The outline of a die.</summary>
    public static DieOutline Outline(ComponentGroup die) => DieAssembly.OutlineOf(die);
}
