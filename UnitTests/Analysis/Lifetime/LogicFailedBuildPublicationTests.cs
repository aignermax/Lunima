using Avalonia.Headless.XUnit;
using Shouldly;

namespace UnitTests.Analysis.Lifetime;

public sealed class LogicFailedBuildPublicationTests
{
    [AvaloniaTheory]
    [InlineData(false, "unchanged")]
    [InlineData(true, "unchanged")]
    [InlineData(false, "newer")]
    [InlineData(true, "newer")]
    [InlineData(false, "republished")]
    [InlineData(true, "republished")]
    public async Task CurrentUnsuccessfulBuild_ClearsItsPriorHandoffButPreservesNewerPublications(bool cancel, string publication)
    {
        await using var fixture = new LogicLifetimeFixture();
        // A transient panel has not built a network of its own. The singleton handoff
        // still carries the successful evaluator from the previously displayed design.
        var previous = LogicLifetimeFixture.Network();
        fixture.Provider.Publish(previous);
        fixture.Assembler.Fail = !cancel;
        var running = fixture.BuildAsync();
        var latest = previous;
        try
        {
            await fixture.Assembler.Barrier.AwaitStartAsync(running);
            if (publication != "unchanged")
            {
                latest = publication == "newer" ? LogicLifetimeFixture.Network() : previous;
                fixture.Provider.Publish(latest);
            }
            if (cancel) fixture.Model.CancelCommand.Execute(null);
        }
        finally { await fixture.Assembler.Barrier.FinishAsync(running); }

        fixture.Model.HasNetwork.ShouldBeFalse();
        if (publication == "unchanged")
            fixture.Provider.Network.ShouldBeNull("a failed or cancelled current build must remove the previous design's evaluator");
        else
            fixture.Provider.Network.ShouldBeSameAs(latest,
                "another publication supersedes this build even when it reuses the same evaluator object");
    }
}
