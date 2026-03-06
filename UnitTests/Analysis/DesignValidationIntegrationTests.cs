using CAP_Core.Analysis;
using CAP_Core.Components;
using CAP_Core.Routing;
using CAP.Avalonia.ViewModels;
using Shouldly;

namespace UnitTests.Analysis;

/// <summary>
/// Integration tests for DesignValidator + DesignValidationViewModel data flow.
/// </summary>
public class DesignValidationIntegrationTests
{
    [Fact]
    public void ViewModel_RunValidation_PopulatesIssues()
    {
        // Arrange
        var vm = new DesignValidationViewModel();
        var connection = CreateConnectionWithInvalidGeometry();

        // Act
        vm.RunValidation(new[] { connection });

        // Assert
        vm.Issues.Count.ShouldBe(1);
        vm.HasIssues.ShouldBeTrue();
        // After RunValidation, auto-navigates to first issue (StatusText = issue description)
        vm.StatusText.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public void ViewModel_RunValidation_NoIssues_ClearsState()
    {
        var vm = new DesignValidationViewModel();
        var validConnection = CreateValidConnection();

        vm.RunValidation(new[] { validConnection });

        vm.Issues.Count.ShouldBe(0);
        vm.HasIssues.ShouldBeFalse();
        vm.StatusText.ShouldContain("No issues");
    }

    [Fact]
    public void ViewModel_NextIssue_NavigatesForward()
    {
        var vm = new DesignValidationViewModel();
        var connections = new[]
        {
            CreateConnectionWithInvalidGeometry(x1: 0, x2: 100),
            CreateConnectionWithBlockedPath(x1: 200, x2: 300)
        };

        vm.RunValidation(connections);

        // After RunValidation, auto-navigates to first issue (index 0)
        vm.CurrentIndex.ShouldBe(0);

        vm.NextIssueCommand.Execute(null);
        vm.CurrentIndex.ShouldBe(1);

        // Wraps around
        vm.NextIssueCommand.Execute(null);
        vm.CurrentIndex.ShouldBe(0);
    }

    [Fact]
    public void ViewModel_PreviousIssue_NavigatesBackward()
    {
        var vm = new DesignValidationViewModel();
        var connections = new[]
        {
            CreateConnectionWithInvalidGeometry(x1: 0, x2: 100),
            CreateConnectionWithBlockedPath(x1: 200, x2: 300)
        };

        vm.RunValidation(connections);

        // Wraps to last
        vm.PreviousIssueCommand.Execute(null);
        vm.CurrentIndex.ShouldBe(1);

        vm.PreviousIssueCommand.Execute(null);
        vm.CurrentIndex.ShouldBe(0);
    }

    [Fact]
    public void ViewModel_NavigateToPosition_CalledOnIssueNavigation()
    {
        var vm = new DesignValidationViewModel();
        double navigatedX = 0, navigatedY = 0;
        vm.NavigateToPosition = (x, y) => { navigatedX = x; navigatedY = y; };

        var connection = CreateConnectionWithInvalidGeometry(x1: 100, y1: 200, x2: 300, y2: 400);
        vm.RunValidation(new[] { connection });

        // Auto-navigates to first issue → midpoint is (200, 300)
        navigatedX.ShouldBe(200, 0.1);
        navigatedY.ShouldBe(300, 0.1);
    }

    [Fact]
    public void ViewModel_HighlightConnection_CalledOnNavigation()
    {
        var vm = new DesignValidationViewModel();
        WaveguideConnection? highlighted = null;
        vm.HighlightConnection = c => highlighted = c;

        var connection = CreateConnectionWithInvalidGeometry();
        vm.RunValidation(new[] { connection });

        highlighted.ShouldBe(connection);
    }

    [Fact]
    public void ViewModel_RunValidation_ClearsPreviousHighlight()
    {
        var vm = new DesignValidationViewModel();
        WaveguideConnection? highlighted = new WaveguideConnection
        {
            StartPin = CreateTestPin(),
            EndPin = CreateTestPin()
        };
        vm.HighlightConnection = c => highlighted = c;

        // First run with issue
        vm.RunValidation(new[] { CreateConnectionWithInvalidGeometry() });

        // Second run with no issues — should clear highlight
        vm.RunValidation(Array.Empty<WaveguideConnection>());
        highlighted.ShouldBeNull();
    }

    [Fact]
    public void ViewModel_NavigationText_ShowsCorrectFormat()
    {
        var vm = new DesignValidationViewModel();
        var connections = new[]
        {
            CreateConnectionWithInvalidGeometry(x1: 0, x2: 100),
            CreateConnectionWithBlockedPath(x1: 200, x2: 300)
        };

        vm.RunValidation(connections);
        vm.NavigationText.ShouldBe("1 / 2");

        vm.NextIssueCommand.Execute(null);
        vm.NavigationText.ShouldBe("2 / 2");
    }

    private static WaveguideConnection CreateConnectionWithInvalidGeometry(
        double x1 = 0, double y1 = 0, double x2 = 100, double y2 = 0)
    {
        var connection = CreateTestConnection(x1, y1, x2, y2);
        var path = new RoutedPath();
        path.Segments.Add(new StraightSegment(x1, y1, x2, y2, 0));
        path.IsInvalidGeometry = true;
        connection.RestoreCachedPath(path);
        return connection;
    }

    private static WaveguideConnection CreateConnectionWithBlockedPath(
        double x1 = 0, double y1 = 0, double x2 = 100, double y2 = 0)
    {
        var connection = CreateTestConnection(x1, y1, x2, y2);
        var path = new RoutedPath();
        path.Segments.Add(new StraightSegment(x1, y1, x2, y2, 0));
        path.IsBlockedFallback = true;
        connection.RestoreCachedPath(path);
        return connection;
    }

    private static WaveguideConnection CreateValidConnection()
    {
        var connection = CreateTestConnection(0, 0, 100, 0);
        var path = new RoutedPath();
        path.Segments.Add(new StraightSegment(0, 0, 100, 0, 0));
        connection.RestoreCachedPath(path);
        return connection;
    }

    private static WaveguideConnection CreateTestConnection(
        double x1, double y1, double x2, double y2)
    {
        var comp1 = TestComponentFactory.CreateStraightWaveGuide();
        comp1.PhysicalX = x1;
        comp1.PhysicalY = y1;
        var pin1 = new PhysicalPin
        {
            Name = "out",
            OffsetXMicrometers = 0,
            OffsetYMicrometers = 0,
            ParentComponent = comp1
        };
        comp1.PhysicalPins.Add(pin1);

        var comp2 = TestComponentFactory.CreateStraightWaveGuide();
        comp2.PhysicalX = x2;
        comp2.PhysicalY = y2;
        var pin2 = new PhysicalPin
        {
            Name = "in",
            OffsetXMicrometers = 0,
            OffsetYMicrometers = 0,
            ParentComponent = comp2
        };
        comp2.PhysicalPins.Add(pin2);

        return new WaveguideConnection
        {
            StartPin = pin1,
            EndPin = pin2
        };
    }

    private static PhysicalPin CreateTestPin()
    {
        var comp = TestComponentFactory.CreateStraightWaveGuide();
        var pin = new PhysicalPin
        {
            Name = "test",
            OffsetXMicrometers = 0,
            OffsetYMicrometers = 0,
            ParentComponent = comp
        };
        comp.PhysicalPins.Add(pin);
        return pin;
    }
}
