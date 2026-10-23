using Avalonia.Headless.XUnit;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;
using Shouldly;

namespace UnitTests.Analysis.Lifetime;

public sealed class LogicSubscriptionsRetirementTests
{
    [AvaloniaFact]
    public async Task Retirement_StopsClockAndDetachesItsCallback()
    {
        await using var fixture = new LogicLifetimeFixture();
        fixture.Assembler.Barrier.Release.TrySetResult();
        await fixture.BuildAsync();
        fixture.Model.ToggleRunCommand.Execute(null);
        fixture.Clock.IsRunning.ShouldBeTrue();
        var stepped = 0;
        fixture.Model.ClockStepped += (_, _) => stepped++;

        await fixture.Model.StopAsync();
        fixture.Clock.Fire();

        fixture.Clock.IsRunning.ShouldBeFalse();
        fixture.Clock.Subscribers.ShouldBe(0);
        fixture.Model.IsRunning.ShouldBeFalse();
        fixture.Model.HasNetwork.ShouldBeFalse();
        fixture.Provider.Network.ShouldBeNull();
        stepped.ShouldBe(0);
    }

    [AvaloniaFact]
    public async Task Retirement_DetachesCanvasEditsAndRejectsReconfigure()
    {
        await using var fixture = new LogicLifetimeFixture();
        await fixture.Model.StopAsync();
        fixture.Model.Configure(fixture.Canvas);
        fixture.Canvas.Components.Add(new ComponentViewModel(new ComponentGroup("late candidate")));
        fixture.Model.GateCandidates.ShouldBeEmpty();
        fixture.Model.HasGateCandidates.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task Retirement_LeavesAnotherOwnersPublishedNetworkIntact()
    {
        await using var fixture = new LogicLifetimeFixture();
        fixture.Assembler.Barrier.Release.TrySetResult();
        await fixture.BuildAsync();
        var other = LogicLifetimeFixture.Network();
        fixture.Provider.Publish(other);

        await fixture.Model.StopAsync();

        fixture.Provider.Network.ShouldBeSameAs(other);
    }
}
