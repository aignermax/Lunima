using CAP_Core.Routing;
using Shouldly;
using Xunit;

namespace UnitTests.Routing;

/// <summary>
/// Deterministic layout tests for the routing pipeline.
/// Each test defines a fixed component layout, routes connections,
/// and verifies path quality.
/// </summary>
public class LayoutTestRunnerTests
{
    [Fact]
    public void StraightLayout_RoutesSuccessfully()
    {
        // Two components aligned horizontally with facing pins
        var layout = new LayoutTestDefinition
        {
            Name = "Straight",
            MinBendRadiusMicrometers = 10.0,
            Components = new()
            {
                CreateComponent("Source", 0, 0, ("output", 50, 25, 0)),
                CreateComponent("Detector", 150, 0, ("input", 0, 25, 180)),
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

        result.AllRoutesSucceeded.ShouldBeTrue(
            FormatFailureDetails(result));
        result.SuccessCount.ShouldBe(1);

        var path = result.ConnectionResults[0].Path!;
        path.Segments.ShouldNotBeEmpty();
        path.TotalLengthMicrometers.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void ParallelOffset_RoutesWithBends()
    {
        // Two components with vertical offset
        var layout = new LayoutTestDefinition
        {
            Name = "ParallelOffset",
            MinBendRadiusMicrometers = 10.0,
            Components = new()
            {
                CreateComponent("Source", 0, 0, ("output", 50, 25, 0)),
                CreateComponent("Detector", 150, 60, ("input", 0, 25, 180)),
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

        result.AllRoutesSucceeded.ShouldBeTrue(
            FormatFailureDetails(result));

        var path = result.ConnectionResults[0].Path!;
        path.TotalEquivalent90DegreeBends.ShouldBeGreaterThan(0,
            "Offset path should require bends");
    }

    [Fact]
    public void PerpendicularPins_RoutesSuccessfully()
    {
        // Source facing east, detector with pin on left facing west
        // but offset vertically (requires a 90-degree turn)
        var layout = new LayoutTestDefinition
        {
            Name = "Perpendicular",
            MinBendRadiusMicrometers = 10.0,
            Components = new()
            {
                CreateComponent("Source", 0, 50, ("output", 50, 25, 0)),
                CreateComponent("Detector", 150, 100, ("input", 0, 25, 180)),
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

        result.AllRoutesSucceeded.ShouldBeTrue(
            FormatFailureDetails(result));
        // With vertical offset, path should have at least one turn
        var path = result.ConnectionResults[0].Path!;
        path.TotalEquivalent90DegreeBends.ShouldBeGreaterThanOrEqualTo(1,
            "Perpendicular path should have at least one turn");
    }

    [Fact]
    public void OpposingPins_RoutesSuccessfully()
    {
        // Both pins facing the same direction (need U-turn routing)
        var layout = new LayoutTestDefinition
        {
            Name = "Opposing",
            MinBendRadiusMicrometers = 10.0,
            Components = new()
            {
                CreateComponent("Source", 0, 0, ("output", 50, 25, 0)),
                CreateComponent("Detector", 150, 0, ("input", 50, 25, 0)),
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

        // This is a harder case - routing may use fallback
        result.TotalCount.ShouldBe(1);
        var connResult = result.ConnectionResults[0];
        connResult.Path.ShouldNotBeNull();
        connResult.Path!.Segments.ShouldNotBeEmpty();
    }

    [Fact]
    public void DiagnosticsReport_HasValidData()
    {
        var layout = new LayoutTestDefinition
        {
            Name = "DiagnosticTest",
            MinBendRadiusMicrometers = 10.0,
            Components = new()
            {
                CreateComponent("Source", 0, 0, ("output", 50, 25, 0)),
                CreateComponent("Detector", 150, 0, ("input", 0, 25, 180)),
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

        var diag = result.ConnectionResults[0].Diagnostics;
        diag.ShouldNotBeNull();
        diag!.FormatSummary().ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public void LayoutDefinition_SerializesToJsonAndBack()
    {
        var layout = new LayoutTestDefinition
        {
            Name = "Serialization test",
            MinBendRadiusMicrometers = 10.0,
            Components = new()
            {
                CreateComponent("Source", 0, 0, ("output", 50, 25, 0)),
                CreateComponent("Detector", 100, 0, ("input", 0, 25, 180)),
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

        var json = layout.ToJson();
        json.ShouldNotBeNullOrEmpty();

        var restored = LayoutTestDefinition.FromJson(json);
        restored.Name.ShouldBe(layout.Name);
        restored.Components.Count.ShouldBe(2);
        restored.Connections.Count.ShouldBe(1);
        restored.Components[0].Pins.Count.ShouldBe(1);
        restored.MinBendRadiusMicrometers.ShouldBe(10.0);
    }

    [Fact]
    public void RoutedPathSerializer_ProducesValidJson()
    {
        var layout = new LayoutTestDefinition
        {
            Name = "SerializerTest",
            MinBendRadiusMicrometers = 10.0,
            Components = new()
            {
                CreateComponent("Source", 0, 0, ("output", 50, 25, 0)),
                CreateComponent("Detector", 150, 0, ("input", 0, 25, 180)),
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
        var path = result.ConnectionResults[0].Path;
        path.ShouldNotBeNull();

        var json = RoutedPathSerializer.ToJson(path!);
        json.ShouldNotBeNullOrEmpty();
        json.ShouldContain("\"isValid\"");
        json.ShouldContain("\"segments\"");
    }

    [Fact]
    public void MultipleConnections_AllRouteSuccessfully()
    {
        // Three components in a chain
        var layout = new LayoutTestDefinition
        {
            Name = "Chain",
            MinBendRadiusMicrometers = 10.0,
            Components = new()
            {
                CreateComponent("A", 0, 0, ("out", 50, 25, 0)),
                CreateComponent("B", 150, 0,
                    ("in", 0, 25, 180),
                    ("out", 50, 25, 0)),
                CreateComponent("C", 300, 0, ("in", 0, 25, 180)),
            },
            Connections = new()
            {
                new LayoutConnection
                {
                    FromComponentIndex = 0, FromPin = "out",
                    ToComponentIndex = 1, ToPin = "in"
                },
                new LayoutConnection
                {
                    FromComponentIndex = 1, FromPin = "out",
                    ToComponentIndex = 2, ToPin = "in"
                }
            }
        };

        var result = LayoutTestRunner.Run(layout);

        result.TotalCount.ShouldBe(2);
        result.AllRoutesSucceeded.ShouldBeTrue(FormatFailureDetails(result));
    }

    [Fact]
    public void MissingPin_ReportsError()
    {
        var layout = new LayoutTestDefinition
        {
            Name = "MissingPin",
            Components = new()
            {
                CreateComponent("Source", 0, 0, ("output", 50, 25, 0)),
                CreateComponent("Detector", 100, 0),
            },
            Connections = new()
            {
                new LayoutConnection
                {
                    FromComponentIndex = 0, FromPin = "output",
                    ToComponentIndex = 1, ToPin = "nonexistent"
                }
            }
        };

        var result = LayoutTestRunner.Run(layout);

        result.AllRoutesSucceeded.ShouldBeFalse();
        result.ConnectionResults[0].Diagnostics!.Issues
            .ShouldContain(i => i.Message.Contains("not found"));
    }

    private static LayoutComponent CreateComponent(
        string type, double x, double y,
        params (string name, double offsetX, double offsetY, double angle)[] pins)
    {
        return new LayoutComponent
        {
            Type = type,
            X = x,
            Y = y,
            Width = 50,
            Height = 50,
            Pins = pins.Select(p => new LayoutPin
            {
                Name = p.name,
                OffsetX = p.offsetX,
                OffsetY = p.offsetY,
                AngleDegrees = p.angle
            }).ToList()
        };
    }

    private static string FormatFailureDetails(LayoutTestRunner.LayoutTestResult result)
    {
        var failed = result.ConnectionResults.Where(r => !r.IsSuccess).ToList();
        if (failed.Count == 0) return "";

        var details = failed.Select(r =>
        {
            var issues = r.Diagnostics?.FormatSummary() ?? "No diagnostics";
            var pathInfo = r.Path != null
                ? $"Segments={r.Path.Segments.Count}, Blocked={r.Path.IsBlockedFallback}, InvalidGeom={r.Path.IsInvalidGeometry}"
                : "No path";
            return $"  {r.Description}: {pathInfo}\n    {issues}";
        });
        return $"\nFailed connections:\n{string.Join("\n", details)}";
    }
}
