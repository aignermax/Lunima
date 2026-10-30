using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CAP_Core.Analysis.CircuitOptimization;
using Shouldly;

namespace UnitTests.Analysis.Lifetime;

public sealed class CircuitOptimizationRetirementTests
{
    [AvaloniaFact]
    public async Task Stop_CancelsAndDrainsActualWorkerBeforeRetiringAndRejectsLaterAdmission()
    {
        await using var fixture = new CircuitOptimizationLifetimeFixture();
        var running = fixture.RunAsync();
        await fixture.Runner.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = fixture.Runner.Token.Register(() => cancelled.TrySetResult());
        var stopping = fixture.Panel.StopAsync();
        try
        {
            await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            stopping.IsCompleted.ShouldBeFalse("retirement must await the held worker even after cancellation");
            fixture.Panel.StopAsync().ShouldBeSameAs(stopping);
        }
        finally
        {
            await fixture.FinishAsync(running);
            await stopping.WaitAsync(TimeSpan.FromSeconds(5));
        }
        fixture.Panel.Variants.ShouldBeEmpty();
        fixture.Panel.Targets.ShouldBeEmpty();
        fixture.Panel.IsOptimizing.ShouldBeFalse();
        await fixture.RunAsync();
        fixture.Runner.Calls.ShouldBe(1);
    }

    [AvaloniaFact]
    public async Task ProgressDeliveredAfterCompletion_CannotReplaceFinalStatus()
    {
        await using var fixture = new CircuitOptimizationLifetimeFixture();
        fixture.Runner.Release.TrySetResult();
        await fixture.RunAsync();
        var completed = fixture.Panel.StatusText;
        completed.ShouldNotBeEmpty();
        fixture.Runner.Progress.ShouldNotBeNull();
        fixture.Runner.Progress.Report(new OptimizationProgress(1, 999, -7));
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        fixture.Panel.StatusText.ShouldBe(completed);
        fixture.Panel.Variants.Count.ShouldBe(1);
    }

    [AvaloniaFact]
    public async Task UserCancellation_PreservesCompletedPartialVariantsAndTheirApplyOwnership()
    {
        await using var fixture = new CircuitOptimizationLifetimeFixture();
        fixture.Runner.Result = new(1, [new OptimizationCandidate(new[] { 0.8 }, 2)], 12, true);
        var running = fixture.RunAsync();
        await fixture.Runner.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        fixture.Panel.CancelOptimizationCommand.Execute(null);
        await fixture.FinishAsync(running);
        fixture.Panel.Variants.Count.ShouldBe(1);
        fixture.Panel.Variants[0].ApplyCommand.Execute(null);
        fixture.Target.SliderValue.ShouldBe(0.8);
        fixture.Commands.Undo();
        fixture.Target.SliderValue.ShouldBe(0.2);
    }

    [AvaloniaFact]
    public async Task ApplyingAnotherCurrentVariant_RemainsValidAfterOwnEarlierApply()
    {
        await using var fixture = new CircuitOptimizationLifetimeFixture();
        fixture.Runner.Result = new(1,
            [new OptimizationCandidate(new[] { 0.8 }, 3), new OptimizationCandidate(new[] { 0.6 }, 2)], 12, false);
        fixture.Runner.Release.TrySetResult();
        await fixture.RunAsync();
        fixture.Panel.Variants[0].ApplyCommand.Execute(null);
        fixture.Target.SliderValue.ShouldBe(0.8);
        fixture.Panel.Variants[1].ApplyCommand.Execute(null);
        fixture.Target.SliderValue.ShouldBe(0.6);
        fixture.Panel.Variants[0].IsApplied.ShouldBeFalse();
        fixture.Panel.Variants[1].IsApplied.ShouldBeTrue();
        fixture.Commands.Undo();
        fixture.Target.SliderValue.ShouldBe(0.8);
        fixture.Commands.Undo();
        fixture.Target.SliderValue.ShouldBe(0.2);
    }
}
