using Avalonia.Headless.XUnit;
using CAP_Core.Components.Core;
using Shouldly;
using UnitTests.Solvers.Lifetime;

namespace UnitTests.Analysis.Lifetime;

public sealed class OnaOpenWindowRetirementTests
{
    [AvaloniaFact]
    public async Task Retirement_DrainsAcceptedWindowFactory()
    {
        await using var fixture = new SweepLifetimeFixture(spectrum: false);
        fixture.Canvas.Components[0].Component.NazcaFunctionName = Component.AnalysisToolNazcaSentinel;
        fixture.Canvas.SelectedComponent = fixture.Canvas.Components[0];
        var factory = new RetirementBarrier();
        fixture.Ona!.OpenWindowAsync = _ => factory.WaitAsync(CancellationToken.None);
        var running = fixture.Ona.OpenWindowCommand.ExecuteAsync(null);
        var stopping = Task.CompletedTask;
        try
        {
            await factory.AwaitStartAsync(running);
            stopping = fixture.StopAsync();
            stopping.IsCompleted.ShouldBeFalse();
        }
        finally { await factory.FinishAsync(running, stopping); }
    }

    [AvaloniaFact]
    public async Task RetiredPanel_RejectsReboundWindowCommandWithoutDiagnostics()
    {
        await using var fixture = new SweepLifetimeFixture(spectrum: false);
        await fixture.StopAsync();
        var opened = 0;
        fixture.Ona!.OpenWindowAsync = _ => { opened++; return Task.CompletedTask; };
        await fixture.Ona.OpenWindowCommand.ExecuteAsync(null);
        opened.ShouldBe(0);
        fixture.Errors.Entries.ShouldBeEmpty();
    }
}
