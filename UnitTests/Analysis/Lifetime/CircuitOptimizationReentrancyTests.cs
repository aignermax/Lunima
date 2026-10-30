using System.Collections.Specialized;
using Avalonia.Headless.XUnit;
using CAP.Avalonia.ViewModels.Analysis.CircuitOptimization;
using CAP.Avalonia.ViewModels.Canvas;
using Shouldly;

namespace UnitTests.Analysis.Lifetime;

public sealed class CircuitOptimizationReentrancyTests
{
    [AvaloniaFact]
    public async Task RunStartedInsideTargetNotification_AwaitsQueuedDisplayBeforeDisposingItsLease()
    {
        await using var fixture = new CircuitOptimizationLifetimeFixture();
        fixture.Runner.Release.TrySetResult(); // The worker can complete without an asynchronous suspension.
        Task? running = null;
        NotifyCollectionChangedEventHandler observer = (_, change) =>
        {
            if (running == null && change.Action == NotifyCollectionChangedAction.Add)
                running = fixture.RunAsync();
        };
        fixture.Panel.Targets.CollectionChanged += observer;
        try
        {
            fixture.Panel.RefreshFromCanvas();
            running.ShouldNotBeNull();
            await running.WaitAsync(TimeSpan.FromSeconds(5));
            fixture.Runner.Calls.ShouldBe(1);
            fixture.Panel.Variants.Count.ShouldBe(1);
            fixture.Panel.Variants[0].ApplyCommand.Execute(null);
            fixture.Target.SliderValue.ShouldBe(0.8);
        }
        finally { fixture.Panel.Targets.CollectionChanged -= observer; }
    }

    [AvaloniaFact]
    public async Task DispatchedOldCollectionCallback_CannotInvalidateReplacementRun()
    {
        await using var fixture = new CircuitOptimizationLifetimeFixture();
        var replacement = new DesignCanvasViewModel();
        Task running = Task.CompletedTask;
        try
        {
            CircuitOptimizationLifetimeFixture.Populate(replacement, 0.4);
            // Put an external subscriber ahead of the panel's named subscriber.
            // Removing it during dispatch cannot remove it from the current invocation list.
            fixture.Panel.ConfigureForCanvas(replacement);
            NotifyCollectionChangedEventHandler observer = (_, _) =>
            {
                fixture.Panel.ConfigureForCanvas(replacement);
                running = fixture.RunAsync();
            };
            fixture.Canvas.Components.CollectionChanged += observer;
            fixture.Panel.ConfigureForCanvas(fixture.Canvas);
            try { fixture.Canvas.Components.Remove(fixture.Target); }
            finally { fixture.Canvas.Components.CollectionChanged -= observer; }
            await fixture.Runner.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await fixture.FinishAsync(running);

            fixture.Panel.Variants.Count.ShouldBe(1,
                "an already-dispatched event from the detached canvas cannot revoke the new run");
            fixture.Panel.Variants[0].ApplyCommand.Execute(null);
            replacement.Components.Single(value => value.HasSliders).SliderValue.ShouldBe(0.8);
        }
        finally
        {
            await fixture.FinishAsync(running);
            await replacement.Document.RetireAsync();
            replacement.DisposeDocumentBindings();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReconfigureInsideCollectionNotification_WithBindingSubscriberPreservesReplacement(bool variants)
    {
        await using var fixture = new CircuitOptimizationLifetimeFixture();
        var replacement = new DesignCanvasViewModel();
        CircuitOptimizationLifetimeFixture.Populate(replacement, 0.4);
        INotifyCollectionChanged collection = variants ? fixture.Panel.Variants : fixture.Panel.Targets;
        bool reconfigured = false;
        Exception? callbackError = null;
        NotifyCollectionChangedEventHandler bindingObserver = (_, _) => { };
        NotifyCollectionChangedEventHandler configureObserver = (_, change) =>
        {
            if (reconfigured || change.Action != NotifyCollectionChangedAction.Add) return;
            reconfigured = true;
            // Record failure instead of letting an event exception masquerade as a worker failure.
            try { fixture.Panel.ConfigureForCanvas(replacement); }
            catch (Exception error) { callbackError = error; }
        };
        collection.CollectionChanged += bindingObserver;
        collection.CollectionChanged += configureObserver;
        try
        {
            if (variants)
            {
                fixture.Runner.Release.TrySetResult();
                await fixture.RunAsync();
            }
            else fixture.Panel.RefreshFromCanvas();

            reconfigured.ShouldBeTrue();
            callbackError.ShouldBeNull("reconfiguration must remain safe with a real binding subscriber present");
            fixture.Panel.Variants.ShouldBeEmpty();
            fixture.Panel.BaselineText.ShouldBeEmpty();
            var expected = OptimizationTargetFactory.Build(replacement.Components);
            expected.ShouldNotBeEmpty();
            fixture.Panel.Targets.SelectMany(value => value.PinIds)
                .ShouldBe(expected.SelectMany(value => value.PinIds));
            fixture.Panel.SelectedTarget.ShouldNotBeNull();
            fixture.Panel.SelectedTarget.PinIds.ShouldBe(expected[0].PinIds);
        }
        finally
        {
            collection.CollectionChanged -= bindingObserver;
            collection.CollectionChanged -= configureObserver;
            await replacement.Document.RetireAsync();
            replacement.DisposeDocumentBindings();
        }
    }
}
