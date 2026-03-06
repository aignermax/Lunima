using CAP.Avalonia.ViewModels;
using CAP_Core.Routing;
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
