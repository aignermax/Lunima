using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Diagnostics;
using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.LightCalculation;
using CAP_Core.Routing;
using CAP_Core.Tiles;
using Shouldly;
using Xunit;

namespace UnitTests.Routing;

/// <summary>
/// Integration tests verifying the RoutingDiagnosticsViewModel works
/// with the core RoutingDiagnostics and RoutedPathSerializer.
/// </summary>
public class RoutingDiagnosticsIntegrationTests
{
    [Fact]
    public void ViewModel_InitialState_IsIdle()
    {
        var vm = new RoutingDiagnosticsViewModel();

        vm.IsAnalyzing.ShouldBeFalse();
        vm.ResultText.ShouldBeEmpty();
        vm.StatusText.ShouldBeEmpty();
        vm.TotalConnections.ShouldBe(0);
        vm.ValidConnections.ShouldBe(0);
        vm.IssueCount.ShouldBe(0);
    }

    [Fact]
    public void ViewModel_RunDiagnostics_WithoutCanvas_DoesNotThrow()
    {
        var vm = new RoutingDiagnosticsViewModel();

        // Should not throw - canvas is null
        vm.RunDiagnosticsCommand.Execute(null);

        vm.IsAnalyzing.ShouldBeFalse();
    }

    [Fact]
    public void LayoutTestRunner_ProducesDiagnosticData_ForViewModel()
    {
        // Integration: run layout test, validate diagnostics data can be consumed
        var layout = new LayoutTestDefinition
        {
            Name = "ViewModel integration test",
            MinBendRadiusMicrometers = 10.0,
            Components = new()
            {
                new LayoutComponent
                {
                    Type = "Source", X = 0, Y = 0, Width = 50, Height = 50,
                    Pins = new() { new LayoutPin { Name = "output", OffsetX = 50, OffsetY = 25, AngleDegrees = 0 } }
                },
                new LayoutComponent
                {
                    Type = "Detector", X = 150, Y = 0, Width = 50, Height = 50,
                    Pins = new() { new LayoutPin { Name = "input", OffsetX = 0, OffsetY = 25, AngleDegrees = 180 } }
                }
            },
            Connections = new()
            {
                new LayoutConnection
                {
                    FromComponentIndex = 0, FromPin = "output",
                    ToComponentIndex = 1, ToPin = "input"
                }
            }
        };

        var result = LayoutTestRunner.Run(layout);

        // Verify the diagnostic data chain works end-to-end
        result.ConnectionResults.ShouldNotBeEmpty();
        var connResult = result.ConnectionResults[0];
        connResult.Diagnostics.ShouldNotBeNull();
        connResult.Path.ShouldNotBeNull();

        // Verify path can be serialized
        var json = RoutedPathSerializer.ToJson(connResult.Path!);
        json.ShouldNotBeNullOrEmpty();

        // Verify diagnostics report is meaningful
        var summary = connResult.Diagnostics!.FormatSummary();
        summary.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public void RunDiagnostics_WithGroupHavingFrozenPaths_IncludesFrozenPathsInCount()
    {
        // Arrange: create a canvas with a ComponentGroup that has an internal frozen path
        var canvas = new DesignCanvasViewModel();

        var child = CreateMinimalComponent("Child1", 0, 0);
        var group = new ComponentGroup("TestGroup");
        group.AddChild(child);

        var startPin = new PhysicalPin { Name = "in", ParentComponent = child, OffsetXMicrometers = 0, OffsetYMicrometers = 0, AngleDegrees = 180 };
        var endPin = new PhysicalPin { Name = "out", ParentComponent = child, OffsetXMicrometers = 100, OffsetYMicrometers = 0, AngleDegrees = 0 };

        var frozenPath = new FrozenWaveguidePath
        {
            Path = new RoutedPath(),
            StartPin = startPin,
            EndPin = endPin
        };
        frozenPath.Path.Segments.Add(new StraightSegment(0, 0, 100, 0, 0));
        group.AddInternalPath(frozenPath);

        canvas.AddComponent(group);

        var vm = new RoutingDiagnosticsViewModel();
        vm.Configure(canvas);

        // Act
        vm.RunDiagnosticsCommand.Execute(null);

        // Assert: TotalConnections includes the 1 frozen path from the group
        vm.TotalConnections.ShouldBe(1);
        vm.ValidConnections.ShouldBe(1);
        vm.ResultText.ShouldContain("[group]");
    }

    [Fact]
    public void RunDiagnostics_WithNestedGroups_IncludesAllFrozenPaths()
    {
        // Arrange: nested groups with frozen paths
        var canvas = new DesignCanvasViewModel();

        var child = CreateMinimalComponent("Child", 0, 0);
        var innerGroup = new ComponentGroup("Inner");
        innerGroup.AddChild(child);

        var pin1 = new PhysicalPin { Name = "in", ParentComponent = child };
        var pin2 = new PhysicalPin { Name = "out", ParentComponent = child };
        var innerFrozen = new FrozenWaveguidePath { Path = new RoutedPath(), StartPin = pin1, EndPin = pin2 };
        innerFrozen.Path.Segments.Add(new StraightSegment(0, 0, 50, 0, 0));
        innerGroup.AddInternalPath(innerFrozen);

        var outerGroup = new ComponentGroup("Outer");
        outerGroup.AddChild(innerGroup);

        var pin3 = new PhysicalPin { Name = "a", ParentComponent = child };
        var pin4 = new PhysicalPin { Name = "b", ParentComponent = child };
        var outerFrozen = new FrozenWaveguidePath { Path = new RoutedPath(), StartPin = pin3, EndPin = pin4 };
        outerFrozen.Path.Segments.Add(new StraightSegment(0, 0, 80, 0, 0));
        outerGroup.AddInternalPath(outerFrozen);

        canvas.AddComponent(outerGroup);

        var vm = new RoutingDiagnosticsViewModel();
        vm.Configure(canvas);

        // Act
        vm.RunDiagnosticsCommand.Execute(null);

        // Assert: both frozen paths (inner + outer) are included
        vm.TotalConnections.ShouldBe(2);
    }

    [Fact]
    public void RunDiagnostics_WithCanvasConnectionsAndGroups_CountsAll()
    {
        // Arrange: canvas has one direct connection and one group with a frozen path
        var canvas = new DesignCanvasViewModel();

        var compA = CreateMinimalComponent("A", 0, 0);
        var pinA = new PhysicalPin { Name = "out", ParentComponent = compA, OffsetXMicrometers = 50, OffsetYMicrometers = 0, AngleDegrees = 0 };
        compA.PhysicalPins.Add(pinA);
        canvas.AddComponent(compA);

        var compB = CreateMinimalComponent("B", 200, 0);
        var pinB = new PhysicalPin { Name = "in", ParentComponent = compB, OffsetXMicrometers = 0, OffsetYMicrometers = 0, AngleDegrees = 180 };
        compB.PhysicalPins.Add(pinB);
        canvas.AddComponent(compB);

        var directPath = new RoutedPath();
        directPath.Segments.Add(new StraightSegment(50, 0, 200, 0, 0));
        canvas.ConnectPinsWithCachedRoute(pinA, pinB, directPath);

        var groupChild = CreateMinimalComponent("GChild", 0, 100);
        var group = new ComponentGroup("Group1");
        group.AddChild(groupChild);
        var gp1 = new PhysicalPin { Name = "x", ParentComponent = groupChild };
        var gp2 = new PhysicalPin { Name = "y", ParentComponent = groupChild };
        var frozen = new FrozenWaveguidePath { Path = new RoutedPath(), StartPin = gp1, EndPin = gp2 };
        frozen.Path.Segments.Add(new StraightSegment(0, 100, 50, 100, 0));
        group.AddInternalPath(frozen);
        canvas.AddComponent(group);

        var vm = new RoutingDiagnosticsViewModel();
        vm.Configure(canvas);

        // Act
        vm.RunDiagnosticsCommand.Execute(null);

        // Assert: 1 canvas connection + 1 frozen group path = 2 total
        vm.TotalConnections.ShouldBe(2);
    }

    private static Component CreateMinimalComponent(string id, double x, double y)
    {
        var parts = new Part[1, 1];
        parts[0, 0] = new Part(new List<Pin>());
        var comp = new Component(
            laserWaveLengthToSMatrixMap: new Dictionary<int, SMatrix>(),
            sliders: new List<Slider>(),
            nazcaFunctionName: $"test_{id.ToLower()}",
            nazcaFunctionParams: "",
            parts: parts,
            typeNumber: 0,
            identifier: id,
            rotationCounterClock: DiscreteRotation.R0,
            physicalPins: new List<PhysicalPin>()
        );
        comp.PhysicalX = x;
        comp.PhysicalY = y;
        return comp;
    }

    [Fact]
    public void RoutedPathSerializer_MultiPath_ProducesValidJson()
    {
        var layout = new LayoutTestDefinition
        {
            Name = "Multi-path serialization",
            MinBendRadiusMicrometers = 10.0,
            Components = new()
            {
                new LayoutComponent
                {
                    Type = "A", X = 0, Y = 0, Width = 50, Height = 50,
                    Pins = new() { new LayoutPin { Name = "out", OffsetX = 50, OffsetY = 25, AngleDegrees = 0 } }
                },
                new LayoutComponent
                {
                    Type = "B", X = 150, Y = 0, Width = 50, Height = 50,
                    Pins = new()
                    {
                        new LayoutPin { Name = "in", OffsetX = 0, OffsetY = 25, AngleDegrees = 180 },
                        new LayoutPin { Name = "out", OffsetX = 50, OffsetY = 25, AngleDegrees = 0 }
                    }
                },
                new LayoutComponent
                {
                    Type = "C", X = 300, Y = 0, Width = 50, Height = 50,
                    Pins = new() { new LayoutPin { Name = "in", OffsetX = 0, OffsetY = 25, AngleDegrees = 180 } }
                }
            },
            Connections = new()
            {
                new LayoutConnection { FromComponentIndex = 0, FromPin = "out", ToComponentIndex = 1, ToPin = "in" },
                new LayoutConnection { FromComponentIndex = 1, FromPin = "out", ToComponentIndex = 2, ToPin = "in" }
            }
        };

        var result = LayoutTestRunner.Run(layout);
        var paths = new Dictionary<string, RoutedPath>();
        foreach (var conn in result.ConnectionResults)
        {
            if (conn.Path != null)
                paths[conn.Description] = conn.Path;
        }

        var json = RoutedPathSerializer.ToJson(paths);
        json.ShouldNotBeNullOrEmpty();
        json.ShouldContain("\"isValid\"");
    }
}
