using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using Shouldly;
using Xunit;
using static UnitTests.TestComponentFactory;

namespace UnitTests.Connections;

/// <summary>
/// Tests for re-anchoring waveguide connections after a component's physical
/// pins were replaced by a Nazca raw-code override (issue #561).
/// </summary>
public class ConnectionPinReanchorServiceTests
{
    /// <summary>Replaces the component's pins with same-named copies (new objects).</summary>
    private static void ReplacePinsWithSameNames(Component comp)
    {
        var copies = comp.PhysicalPins.Select(p => new PhysicalPin
        {
            Name = p.Name,
            OffsetXMicrometers = p.OffsetXMicrometers + 1,
            OffsetYMicrometers = p.OffsetYMicrometers,
            AngleDegrees = p.AngleDegrees,
            ParentComponent = comp,
        }).ToList();
        comp.PhysicalPins.Clear();
        comp.PhysicalPins.AddRange(copies);
    }

    [Fact]
    public void Reanchor_SamePinName_ReassignsConnectionToNewPin()
    {
        var compA = CreateStraightWaveGuideWithPhysicalPins();   // "in"/"out"
        var compB = CreateStraightWaveGuideWithPhysicalPins();
        var conn = new WaveguideConnection
        {
            StartPin = compA.PhysicalPins.First(p => p.Name == "out"),
            EndPin = compB.PhysicalPins.First(p => p.Name == "in"),
        };

        ReplacePinsWithSameNames(compA);
        var result = ConnectionPinReanchorService.Reanchor(compA, new[] { conn });

        result.DroppedConnections.ShouldBeEmpty();
        result.ReanchoredCount.ShouldBe(1);
        conn.StartPin.ShouldBeSameAs(compA.PhysicalPins.First(p => p.Name == "out"));
        conn.EndPin.ParentComponent.ShouldBeSameAs(compB);   // fremde Seite unangetastet
    }

    [Fact]
    public void Reanchor_RemovedPinName_DropsConnectionWithWarning()
    {
        var compA = CreateStraightWaveGuideWithPhysicalPins();
        var compB = CreateStraightWaveGuideWithPhysicalPins();
        var conn = new WaveguideConnection
        {
            StartPin = compA.PhysicalPins.First(p => p.Name == "out"),
            EndPin = compB.PhysicalPins.First(p => p.Name == "in"),
        };

        // Override entfernt "out": nur noch ein Pin "a0".
        compA.PhysicalPins.Clear();
        compA.PhysicalPins.Add(new PhysicalPin { Name = "a0", ParentComponent = compA });

        var result = ConnectionPinReanchorService.Reanchor(compA, new[] { conn });

        result.ReanchoredCount.ShouldBe(0);
        result.DroppedConnections.ShouldHaveSingleItem().ShouldBeSameAs(conn);
        result.Warnings.ShouldHaveSingleItem().ShouldContain("out");
    }

    [Fact]
    public void Reanchor_ConnectionOfOtherComponents_IsUntouched()
    {
        var compA = CreateStraightWaveGuideWithPhysicalPins();
        var compB = CreateStraightWaveGuideWithPhysicalPins();
        var compC = CreateStraightWaveGuideWithPhysicalPins();
        var foreignConn = new WaveguideConnection
        {
            StartPin = compB.PhysicalPins[0],
            EndPin = compC.PhysicalPins[1],
        };
        var originalStart = foreignConn.StartPin;

        ReplacePinsWithSameNames(compA);
        var result = ConnectionPinReanchorService.Reanchor(compA, new[] { foreignConn });

        result.ReanchoredCount.ShouldBe(0);
        result.DroppedConnections.ShouldBeEmpty();
        foreignConn.StartPin.ShouldBeSameAs(originalStart);
    }

    [Fact]
    public void Reanchor_BothEndpointsOnSameComponent_HandlesBothSides()
    {
        var comp = CreateStraightWaveGuideWithPhysicalPins();   // "in"/"out"
        var conn = new WaveguideConnection
        {
            StartPin = comp.PhysicalPins.First(p => p.Name == "in"),
            EndPin = comp.PhysicalPins.First(p => p.Name == "out"),
        };

        ReplacePinsWithSameNames(comp);
        var result = ConnectionPinReanchorService.Reanchor(comp, new[] { conn });

        result.ReanchoredCount.ShouldBe(1);
        conn.StartPin.ShouldBeSameAs(comp.PhysicalPins.First(p => p.Name == "in"));
        conn.EndPin.ShouldBeSameAs(comp.PhysicalPins.First(p => p.Name == "out"));
    }

    [Fact]
    public void Reanchor_OneEndReanchorsOtherEndDropped_DropsConnection()
    {
        var comp = CreateStraightWaveGuideWithPhysicalPins();   // "in"/"out"
        var conn = new WaveguideConnection
        {
            StartPin = comp.PhysicalPins.First(p => p.Name == "in"),
            EndPin = comp.PhysicalPins.First(p => p.Name == "out"),
        };

        // Override behält "in", entfernt "out".
        comp.PhysicalPins.Clear();
        comp.PhysicalPins.Add(new PhysicalPin { Name = "in", ParentComponent = comp });

        var result = ConnectionPinReanchorService.Reanchor(comp, new[] { conn });

        result.DroppedConnections.ShouldHaveSingleItem().ShouldBeSameAs(conn);
    }
}
