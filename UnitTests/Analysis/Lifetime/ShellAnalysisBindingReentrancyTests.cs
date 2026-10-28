using System.ComponentModel;
using Avalonia.Headless.XUnit;
using CAP.Avalonia.ViewModels.Analysis.EyeDiagram;
using CAP.Avalonia.ViewModels.Canvas;
using Shouldly;
using UnitTests.Analysis.Snapshots;
using UnitTests.Helpers;

namespace UnitTests.Analysis.Lifetime;

public sealed class ShellAnalysisBindingReentrancyTests
{
    [AvaloniaFact]
    public async Task RetirementDuringPanelRebind_PreventsLaterShellPublication()
    {
        await using var fixture = new TransientPanelFixture(eye: false);
        await using var main = MainViewModelTestHelper.CreateMainViewModel(
            canvas: fixture.Canvas, transientViewModel: fixture.Transient);
        var running = fixture.RunAsync();
        Task stopping = Task.CompletedTask;
        var retired = false;
        PropertyChangedEventHandler observer = (_, change) =>
        {
            if (retired || change.PropertyName != nameof(fixture.Transient.StatusText)
                || fixture.Transient!.StatusText.Length != 0) return;
            retired = true;
            stopping = main.StopAsync();
        };
        try
        {
            await fixture.Runner.Barrier.AwaitStartAsync(running);
            main.Home.IsHomeVisible.ShouldBeTrue();
            fixture.Transient!.PropertyChanged += observer;
            (await main.FileOperations.TryNewProjectAsync()).ShouldBeTrue();
            retired.ShouldBeTrue();
            main.Home.IsHomeVisible.ShouldBeTrue("the retired shell must not publish the remaining ProjectOpened UI changes");
        }
        finally
        {
            fixture.Transient!.PropertyChanged -= observer;
            await fixture.Runner.Barrier.FinishAsync(running, stopping);
        }
    }

    [AvaloniaFact]
    public async Task ThrowingPanelObserver_DoesNotPermanentlyStrandTheOtherPersistentPanels()
    {
        await using var fixture = new TransientPanelFixture(eye: false);
        var eye = new EyeDiagramViewModel(fixture.Errors, fixture.Runner) { BitRateGbps = 100 };
        await using var main = MainViewModelTestHelper.CreateMainViewModel(
            canvas: fixture.Canvas, transientViewModel: fixture.Transient, eyeViewModel: eye);
        var running = fixture.RunAsync();
        var threw = false;
        PropertyChangedEventHandler observer = (_, change) =>
        {
            if (threw || change.PropertyName != nameof(fixture.Transient.StatusText)
                || fixture.Transient!.StatusText.Length != 0) return;
            threw = true;
            throw new InvalidOperationException("controlled Configure observer failure");
        };
        try
        {
            await fixture.Runner.Barrier.AwaitStartAsync(running);
            fixture.Transient!.PropertyChanged += observer;
            var error = await Should.ThrowAsync<InvalidOperationException>(
                () => main.FileOperations.TryNewProjectAsync());
            error.Message.ShouldBe("controlled Configure observer failure");
            threw.ShouldBeTrue();
            fixture.Transient.PropertyChanged -= observer;
            await fixture.Runner.Barrier.FinishAsync(running);

            // The accepted document already exists despite the surfaced observer
            // error. Other persistent panels must be usable without manufacturing
            // another ProjectOpened notification to repair their bindings.
            var replacement = new SweepSnapshotFixture();
            replacement.First.IsUserMarkedLightSource = true;
            replacement.First.LaserEnabled = true;
            replacement.Second.IsUserMarkedLightSource = true;
            replacement.Second.LaserEnabled = false;
            fixture.Canvas.Components.Add(new ComponentViewModel(replacement.First));
            fixture.Canvas.Components.Add(new ComponentViewModel(replacement.Second));
            fixture.Canvas.RestoreConnectionAndViewModel(replacement.Wire);
            await eye.RunEyeAnalysisCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(15));
            fixture.Runner.Calls.ShouldBe(2, "a failed early Configure must not prevent independent panels from binding");
            eye.HasResult.ShouldBeTrue();
        }
        finally
        {
            fixture.Transient!.PropertyChanged -= observer;
            await fixture.Runner.Barrier.FinishAsync(running);
        }
    }
}
