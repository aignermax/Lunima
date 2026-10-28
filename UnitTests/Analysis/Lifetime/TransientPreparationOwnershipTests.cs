using System.ComponentModel;
using Avalonia.Headless.XUnit;
using CAP.Avalonia.ViewModels.Analysis;
using CAP.Avalonia.ViewModels.Analysis.EyeDiagram;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core;
using Shouldly;
using UnitTests.Analysis.Snapshots;

namespace UnitTests.Analysis.Lifetime;

public sealed class TransientPreparationOwnershipTests
{
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ReconfigureDuringPreparation_DoesNotPublishOldRunIntoReplacement(bool eye, bool clearCanvas)
    {
        var first = CreateCircuit();
        var replacement = CreateCircuit();
        var errors = new ErrorConsoleService();
        object panel;
        Action<DesignCanvasViewModel?> configure;
        Func<Task> run;
        Func<string> status;
        Func<bool> hasResult;
        Func<bool> running;
        if (eye)
        {
            var model = new EyeDiagramViewModel(errors);
            panel = model;
            configure = model.Configure;
            run = () => model.RunEyeAnalysisCommand.ExecuteAsync(null);
            status = () => model.StatusText;
            hasResult = () => model.HasResult;
            running = () => model.IsRunning;
        }
        else
        {
            var model = new TimeDomainViewModel(errors) { FreqPoints = 64 };
            panel = model;
            configure = model.Configure;
            run = () => model.RunTransientCommand.ExecuteAsync(null);
            status = () => model.StatusText;
            hasResult = () => model.HasResult;
            running = () => model.IsRunning;
        }

        var changed = false;
        PropertyChangedEventHandler observer = (_, change) =>
        {
            if (changed || change.PropertyName != "StatusText" || !running()) return;
            changed = true;
            configure(clearCanvas ? null : replacement);
        };
        configure(first);
        ((INotifyPropertyChanged)panel).PropertyChanged += observer;
        try
        {
            await run().WaitAsync(TimeSpan.FromSeconds(15));
            changed.ShouldBeTrue();
            status().ShouldBeEmpty("the superseded run must preserve the newly configured panel's cleared status");
            hasResult().ShouldBeFalse();
            errors.Entries.ShouldBeEmpty("a superseded run must not report an error for its abandoned context");
        }
        finally
        {
            ((INotifyPropertyChanged)panel).PropertyChanged -= observer;
            if (panel is IAsyncDisposable owner) await owner.DisposeAsync();
            await first.Document.RetireAsync();
            first.DisposeDocumentBindings();
            await replacement.Document.RetireAsync();
            replacement.DisposeDocumentBindings();
        }
    }

    private static DesignCanvasViewModel CreateCircuit()
    {
        var fixture = new SweepSnapshotFixture();
        fixture.First.IsUserMarkedLightSource = true;
        fixture.First.LaserEnabled = true;
        var canvas = new DesignCanvasViewModel();
        canvas.Components.Add(new ComponentViewModel(fixture.First));
        canvas.Components.Add(new ComponentViewModel(fixture.Second));
        canvas.RestoreConnectionAndViewModel(fixture.Wire);
        return canvas;
    }
}
