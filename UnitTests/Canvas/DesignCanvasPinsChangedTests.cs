using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;
using Shouldly;
using Xunit;
using static UnitTests.TestComponentFactory;

namespace UnitTests.Canvas;

/// <summary>
/// Tests for <see cref="DesignCanvasViewModel.OnComponentPinsChanged"/> (issue #561):
/// after a Nazca override replaced a component's pins, connections are re-anchored or
/// dropped and the canvas pin view-models are refreshed.
/// </summary>
public class DesignCanvasPinsChangedTests
{
    /// <summary>Replaces the component's pins with same-named copies (new objects).</summary>
    private static void ReplacePinsWithSameNames(Component comp)
    {
        var copies = comp.PhysicalPins.Select(p => new PhysicalPin
        {
            Name = p.Name,
            OffsetXMicrometers = p.OffsetXMicrometers,
            OffsetYMicrometers = p.OffsetYMicrometers,
            AngleDegrees = p.AngleDegrees,
            ParentComponent = comp,
        }).ToList();
        comp.PhysicalPins.Clear();
        comp.PhysicalPins.AddRange(copies);
    }

    [Fact]
    public void PinsChanged_SameNames_ConnectionSurvivesOnNewPins()
    {
        var canvas = new DesignCanvasViewModel();
        var compA = CreateStraightWaveGuideWithPhysicalPins();
        var compB = CreateStraightWaveGuideWithPhysicalPins();
        compB.PhysicalX = 500;
        canvas.AddComponent(compA);
        canvas.AddComponent(compB);
        canvas.ConnectPins(
            compA.PhysicalPins.First(p => p.Name == "out"),
            compB.PhysicalPins.First(p => p.Name == "in"));

        ReplacePinsWithSameNames(compA);
        var warnings = canvas.OnComponentPinsChanged(compA);

        warnings.ShouldBeEmpty();
        canvas.Connections.Count.ShouldBe(1);
        canvas.Connections[0].Connection.StartPin
            .ShouldBeSameAs(compA.PhysicalPins.First(p => p.Name == "out"));
    }

    [Fact]
    public void PinsChanged_RemovedPin_ConnectionDroppedEverywhere()
    {
        var canvas = new DesignCanvasViewModel();
        var compA = CreateStraightWaveGuideWithPhysicalPins();
        var compB = CreateStraightWaveGuideWithPhysicalPins();
        compB.PhysicalX = 500;
        canvas.AddComponent(compA);
        canvas.AddComponent(compB);
        canvas.ConnectPins(
            compA.PhysicalPins.First(p => p.Name == "out"),
            compB.PhysicalPins.First(p => p.Name == "in"));

        compA.PhysicalPins.Clear();
        compA.PhysicalPins.Add(new PhysicalPin { Name = "a0", ParentComponent = compA });
        var warnings = canvas.OnComponentPinsChanged(compA);

        warnings.ShouldHaveSingleItem();
        canvas.Connections.ShouldBeEmpty();
        canvas.ConnectionManager.Connections.ShouldBeEmpty();
    }

    [Fact]
    public void PinsChanged_RefreshesPinViewModels()
    {
        var canvas = new DesignCanvasViewModel();
        var comp = CreateStraightWaveGuideWithPhysicalPins();
        canvas.AddComponent(comp);
        var staleVm = canvas.AllPins.First(p => p.Pin.ParentComponent == comp);

        ReplacePinsWithSameNames(comp);
        canvas.OnComponentPinsChanged(comp);

        canvas.AllPins.ShouldNotContain(staleVm);
        canvas.AllPins.Count(p => p.Pin.ParentComponent == comp).ShouldBe(2);
        canvas.AllPins.Where(p => p.Pin.ParentComponent == comp)
            .ShouldAllBe(p => comp.PhysicalPins.Contains(p.Pin));
    }
}
