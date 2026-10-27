using System.ComponentModel;
using System.Reflection;
using Avalonia.Headless.XUnit;
using CAP.Avalonia.ViewModels.Analysis;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Documents;
using Shouldly;

namespace UnitTests.Analysis.Lifetime;

public sealed class AnalysisObserverFailureRetirementTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ThrowingRunningObserver_DoesNotSkipDocumentLeaseRetirement(bool compression)
    {
        var canvas = new DesignCanvasViewModel();
        var component = TestComponentHelper.CreateComponentWithSlider(0, 1, 0.5);
        component.PhysicalX = 500;
        canvas.Components.Add(new ComponentViewModel(component));
        INotifyPropertyChanged panel;
        Func<Task> run;
        Func<Task> stop;
        Func<bool> isRunning;
        string property;
        if (compression)
        {
            var compressor = new CompressLayoutViewModel();
            compressor.Configure(canvas);
            panel = compressor;
            run = () => compressor.CompressLayoutCommand.ExecuteAsync(null);
            stop = compressor.StopAsync;
            isRunning = () => compressor.IsCompressing;
            property = nameof(compressor.IsCompressing);
        }
        else
        {
            var sweep = new ParameterSweepViewModel();
            sweep.ConfigureForComponent(canvas.Components[0], canvas);
            panel = sweep;
            run = () => sweep.RunSweepCommand.ExecuteAsync(null);
            stop = sweep.StopAsync;
            isRunning = () => sweep.IsSweeping;
            property = nameof(sweep.IsSweeping);
        }
        var observed = false;
        panel.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName != property || isRunning()) return;
            observed = true;
            throw new InvalidOperationException("controlled UI observer failure");
        };
        Task retirement = Task.CompletedTask;
        try
        {
            var error = await Record.ExceptionAsync(run);
            observed.ShouldBeTrue();
            error.ShouldBeOfType<InvalidOperationException>().Message.ShouldBe("controlled UI observer failure");
            retirement = canvas.Document.RetireAsync();
            await retirement.WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally
        {
            // Rescue only the deliberately failing baseline so the regression never leaves
            // a session lease behind. The assertion above uses public retirement behavior.
            var active = (HashSet<DocumentOperation>)typeof(DocumentSession)
                .GetField("_active", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(canvas.Document)!;
            foreach (var operation in active.ToArray()) await operation.DisposeAsync();
            await retirement.WaitAsync(TimeSpan.FromSeconds(5));
            await stop();
            canvas.DisposeDocumentBindings();
        }
    }
}
