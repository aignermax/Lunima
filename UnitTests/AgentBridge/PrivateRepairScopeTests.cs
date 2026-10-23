using Avalonia.Headless.XUnit;
using CAP.Avalonia.Services.AgentBridge.Tools.Repair.Suggest;
using CAP_Core.Components.Core;
using CAP_Core.Routing.Snapshots;
using Shouldly;

namespace UnitTests.AgentBridge;

public sealed class PrivateRepairScopeTests
{
    [AvaloniaFact]
    public async Task Workbench_AdoptsPrivateScopeWithoutReplacingCapturedRoutesOrManager()
    {
        var fixture = new RepairTrialFixture();
        try
        {
            var group = new ComponentGroup("scope");
            group.AddChildren(fixture.Canvas.Components.Select(view => view.Component).ToArray());
            var original = new FrozenWaveguidePath
            {
                PathId = fixture.Wire.Id, StartPin = fixture.Wire.StartPin, EndPin = fixture.Wire.EndPin,
                Path = fixture.Wire.RoutedPath!.DeepCopy()
            };
            original.CaptureSettingsFrom(fixture.Wire);
            group.AddInternalPaths([original]);
            var input = RoutingInputSnapshot.Capture(fixture.Canvas.Components.Select(view => view.Component).ToArray(),
                fixture.Canvas.Router, fixture.Canvas.ConnectionManager);
            await using var permission = new RepairTrialPermission(fixture.Session, null);
            await using var trial = await RepairWorkbench.CreateAsync(input, null, permission, null, (0, 0, 500, 300), group.Id);
            var canvas = trial.Session.Canvas;
            var privateGroup = canvas.Groups.CurrentEditGroup.ShouldNotBeNull();
            privateGroup.Id.ShouldBe(group.Id);
            privateGroup.ShouldNotBeSameAs(group);
            canvas.CurrentEditGroup.ShouldBeSameAs(privateGroup);
            canvas.Groups.BreadcrumbPath.Last().ShouldBeSameAs(privateGroup);
            canvas.Connections.Single().Connection.Id.ShouldBe(fixture.Wire.Id);
            canvas.ConnectionManager.Connections.Single().ShouldBeSameAs(canvas.Connections.Single().Connection);
            canvas.Connections.Single().Connection.IsBlockedFallback.ShouldBeTrue();
            var liveWidth = original.WidthMicrometers;
            var privateWire = canvas.Connections.Single();
            privateWire.Connection.WidthMicrometers = 2.75;
            canvas.Groups.PublishRouteChanges([privateWire]);
            privateGroup.InternalPaths.Single().WidthMicrometers.ShouldBe(2.75);
            original.WidthMicrometers.ShouldBe(liveWidth);
            privateGroup.InternalPaths.Single().ShouldNotBeSameAs(original);
            fixture.AssertLiveUnchanged();
        }
        finally { await fixture.FinishAsync(Task.CompletedTask); }
    }
}
