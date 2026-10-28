using Avalonia.Headless.XUnit;
using CAP.Avalonia.Services.Lifetime;
using Shouldly;

namespace UnitTests.Analysis.Lifetime;

public sealed class LogicBuildRetirementTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Retirement_CancelsAndDrainsActualBuild(bool applicationOwner)
    {
        var application = new ApplicationWorkCoordinator();
        await using var fixture = new LogicLifetimeFixture(application);
        var running = fixture.BuildAsync();
        var stopping = Task.CompletedTask;
        var status = string.Empty;
        try
        {
            await fixture.Assembler.Barrier.AwaitStartAsync(running);
            status = fixture.Model.StatusText;
            stopping = applicationOwner ? application.StopAsync() : fixture.Model.StopAsync();
            await fixture.Assembler.Barrier.AssertDrainingAsync(stopping);
        }
        finally { await fixture.Assembler.Barrier.FinishAsync(running, stopping); }
        fixture.Model.HasNetwork.ShouldBeFalse();
        fixture.Provider.Network.ShouldBeNull();
        fixture.Model.IsProcessing.ShouldBeFalse();
        fixture.Model.StatusText.ShouldBe(status);
    }

    [AvaloniaTheory]
    [InlineData("edit", false)]
    [InlineData("edit", true)]
    [InlineData("replace", false)]
    [InlineData("replace", true)]
    [InlineData("configure", false)]
    [InlineData("configure", true)]
    public async Task SupersededBuild_CannotPublishSuccessOrFailure(string change, bool fail)
    {
        await using var fixture = new LogicLifetimeFixture();
        fixture.Assembler.Fail = fail;
        var running = fixture.BuildAsync();
        var status = string.Empty;
        try
        {
            await fixture.Assembler.Barrier.AwaitStartAsync(running);
            if (change == "edit") fixture.Canvas.Document.Invalidate();
            else if (change == "replace") fixture.Canvas.Document.ReplaceDocument();
            else fixture.Model.Configure(fixture.Canvas);
            status = fixture.Model.StatusText;
        }
        finally { await fixture.Assembler.Barrier.FinishAsync(running); }
        fixture.Model.HasNetwork.ShouldBeFalse();
        fixture.Provider.Network.ShouldBeNull();
        fixture.Model.StatusText.ShouldBe(status);
    }

    [AvaloniaFact]
    public async Task RetiredPanel_RejectsNewBuild()
    {
        await using var fixture = new LogicLifetimeFixture();
        fixture.Assembler.Barrier.Release.TrySetResult();
        await fixture.Model.StopAsync();
        await fixture.BuildAsync();
        fixture.Assembler.Calls.ShouldBe(0);
    }
}
