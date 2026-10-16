using System.Runtime.CompilerServices;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;
using CAP_Core.Components.Dies;
using Shouldly;
using Xunit;

namespace UnitTests.Dies.WorkSurface;

public class DieSurfaceLifetimeTests
{
    [Fact]
    public void ReplacingDesign_ReleasesSurfaceViewModelsDespiteOldParentLinks()
    {
        var canvas = new DesignCanvasViewModel();
        var die = new ComponentGroup("old") { Die = new DieDefinition(500, 500) };
        var part = TestComponentFactory.CreatePinlessComponent(20, 20);
        die.AddChildren(new[] { part });
        canvas.AddComponent(die);
        var reference = CacheViewModel(canvas, part);
        canvas.Surface.LastWorkedDie = die;

        canvas.Components.Clear();
        canvas.Surface.Items.ShouldBeEmpty();
        part.ParentGroup.ShouldBeSameAs(die, "replacement does not dismantle the old model graph");
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        reference.IsAlive.ShouldBeFalse("the live surface must not retain a removed design's view models");
        canvas.Surface.LastWorkedDie.ShouldBeNull();
        GC.KeepAlive(part);
        GC.KeepAlive(canvas);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CacheViewModel(DesignCanvasViewModel canvas, Component component) =>
        new(canvas.Surface.ViewModelFor(component));
}
