using System.Globalization;
using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Analysis;
using CAP_Core.Components.Core;
using CAP_Core.Routing;
using Shouldly;
using Xunit;

namespace UnitTests.ViewModels.LengthMatching;

/// <summary>
/// The length-matching panel's bend-radius field, undo/redo of Apply and Clear, the live
/// current-length row, and the design check for a target the route does not meet.
/// </summary>
public class LengthMatchingUndoAndRadiusTests
{
    private const double Target = 400.0;

    private static (DesignCanvasViewModel Canvas, WaveguideConnectionViewModel Connection, CommandManager Commands) CreateDesign()
    {
        var canvas = new DesignCanvasViewModel();
        var commands = new CommandManager();
        canvas.CommandManager = commands;
        var startComp = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        startComp.WidthMicrometers = 250;
        startComp.HeightMicrometers = 250;
        var endComp = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        endComp.WidthMicrometers = 250;
        endComp.HeightMicrometers = 250;
        endComp.PhysicalX = 400;
        canvas.AddComponent(startComp);
        canvas.AddComponent(endComp);

        var startPin = startComp.PhysicalPins.First(p => p.Name == "out");
        var endPin = endComp.PhysicalPins.First(p => p.Name == "in");
        var (sx, sy) = startPin.GetAbsolutePosition();
        var (ex, ey) = endPin.GetAbsolutePosition();
        var path = new RoutedPath();
        path.Segments.Add(new StraightSegment(sx, sy, ex, ey, 0));
        var connection = canvas.ConnectPinsWithCachedRoute(startPin, endPin, path)!;
        return (canvas, connection, commands);
    }

    private static LengthMatchingViewModel Panel(DesignCanvasViewModel canvas, WaveguideConnectionViewModel connection)
    {
        var vm = new LengthMatchingViewModel(canvas) { SelectedConnection = connection };
        vm.TargetLengthText = Target.ToString(CultureInfo.InvariantCulture);
        return vm;
    }

    [Fact]
    public void Apply_ThenUndo_RestoresTheRouterRouteAndDropsTheTarget()
    {
        var (canvas, connection, commands) = CreateDesign();
        double directLength = connection.Connection.PathLengthMicrometers;
        var vm = Panel(canvas, connection);

        vm.ApplyCommand.Execute(null);
        connection.Connection.PathLengthMicrometers.ShouldBe(Target, 0.1);
        commands.Undo().ShouldBeTrue();

        connection.Connection.TargetLengthMicrometers.ShouldBeNull();
        connection.Connection.IsRouteFrozen.ShouldBeFalse();
        connection.Connection.PathLengthMicrometers.ShouldBe(directLength, 1e-6);
        vm.CurrentLengthMicrometers.ShouldBe(directLength, 1e-6);

        commands.Redo().ShouldBeTrue();
        connection.Connection.PathLengthMicrometers.ShouldBe(Target, 0.1);
        connection.Connection.TargetLengthMicrometers.ShouldBe(Target);
    }

    [Fact]
    public void Apply_WithBendRadius_EveryMeanderBendUsesIt()
    {
        var (canvas, connection, _) = CreateDesign();
        var vm = Panel(canvas, connection);
        vm.BendRadiusText = "15";

        vm.ApplyCommand.Execute(null);

        vm.HasErrorStatus.ShouldBeFalse(vm.StatusMessage);
        connection.Connection.BendRadiusMicrometers.ShouldBe(15);
        connection.Connection.RoutedPath!.Segments.OfType<BendSegment>()
            .ShouldAllBe(bend => Math.Abs(bend.RadiusMicrometers - 15) < 1e-9);
        connection.Connection.PathLengthMicrometers.ShouldBe(Target, 0.1);
    }

    [Fact]
    public void Apply_BendRadiusBelowProcessMinimum_ShowsErrorAndKeepsTheRoute()
    {
        var (canvas, connection, _) = CreateDesign();
        canvas.Router.ProcessMinBendRadiusMicrometers = 20;
        var routeBefore = connection.Connection.RoutedPath;
        var vm = Panel(canvas, connection);
        vm.BendRadiusText = "5";

        vm.ApplyCommand.Execute(null);

        vm.HasErrorStatus.ShouldBeTrue();
        vm.StatusMessage.ShouldContain("20");
        connection.Connection.RoutedPath.ShouldBeSameAs(routeBefore);
    }

    [Fact]
    public void Selection_PrefillsBendRadiusWithAtLeastTheProcessMinimum()
    {
        var (canvas, connection, _) = CreateDesign();
        canvas.Router.ProcessMinBendRadiusMicrometers = 25;

        var vm = new LengthMatchingViewModel(canvas) { SelectedConnection = connection };

        double.Parse(vm.BendRadiusText, CultureInfo.InvariantCulture).ShouldBe(25);
    }

    [Fact]
    public void CurrentLength_FollowsLaterPathChanges()
    {
        var (canvas, connection, _) = CreateDesign();
        var vm = Panel(canvas, connection);
        vm.ApplyCommand.Execute(null);

        var (sx, sy) = connection.Connection.StartPin.GetAbsolutePosition();
        var shorter = new RoutedPath();
        shorter.Segments.Add(new StraightSegment(sx, sy, sx + 10, sy, 0));
        connection.Connection.ReplaceRoutedPath(shorter);
        connection.NotifyPathChanged();

        vm.CurrentLengthMicrometers.ShouldBe(10, 1e-9);
    }

    [Fact]
    public async Task RoutingPass_AfterEndpointMove_RestoresTheTargetLength()
    {
        var (canvas, connection, _) = CreateDesign();
        Panel(canvas, connection).ApplyCommand.Execute(null);
        var endComponent = connection.Connection.EndPin.ParentComponent;

        endComponent.PhysicalX += 30;
        await canvas.RecalculateRoutesAsync();

        connection.Connection.FrozenPathStillMatchesPins().ShouldBeTrue("the route follows the moved pin");
        connection.Connection.PathLengthMicrometers.ShouldBe(Target, 0.1);
        connection.Connection.TargetLengthMicrometers.ShouldBe(Target);
    }

    [Fact]
    public void DesignChecks_RouteNoLongerMeetsItsTarget_ReportsIt()
    {
        var (canvas, connection, _) = CreateDesign();
        Panel(canvas, connection).ApplyCommand.Execute(null);
        new DesignValidator().Validate(new[] { connection.Connection })
            .ShouldNotContain(issue => issue.Type == DesignIssueType.LengthTargetNotMet);

        connection.Connection.TargetLengthMicrometers = Target + 50;

        new DesignValidator().Validate(new[] { connection.Connection })
            .ShouldContain(issue => issue.Type == DesignIssueType.LengthTargetNotMet);
    }
}
