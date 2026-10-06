using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;
using CAP_Core.Routing;
using Shouldly;
using Xunit;

namespace UnitTests.Commands;

/// <summary>Undoing a flat GDS import also removes the geometry it put directly on the canvas.</summary>
public class AddCanvasFrozenPathsCommandTests
{
    private static CanvasFrozenPathViewModel NewPath()
    {
        var route = new RoutedPath();
        route.Segments.Add(new StraightSegment(0, 0, 10, 0, 0));
        return new CanvasFrozenPathViewModel(new FrozenWaveguidePath { Path = route });
    }

    [Fact]
    public void ExecuteUndoRedo_AddsRemovesAndRestoresTheSameInstances()
    {
        var canvas = new DesignCanvasViewModel();
        var paths = new[] { NewPath(), NewPath() };
        var command = new AddCanvasFrozenPathsCommand(canvas, paths);

        command.Execute();
        canvas.CanvasFrozenPaths.ShouldBe(paths);

        command.Undo();
        canvas.CanvasFrozenPaths.ShouldBeEmpty();

        command.Execute();
        canvas.CanvasFrozenPaths.ShouldBe(paths);
    }
}
