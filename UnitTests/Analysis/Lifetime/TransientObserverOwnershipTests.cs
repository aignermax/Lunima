using System.Collections.Specialized;
using System.ComponentModel;
using System.Reflection;
using Avalonia.Headless.XUnit;
using Shouldly;

namespace UnitTests.Analysis.Lifetime;

public sealed class TransientObserverOwnershipTests
{
    [AvaloniaFact]
    public async Task SeriesPublication_ReconfigureWithTwoSubscribersLeavesNoOldRows()
    {
        await using var fixture = new TransientPanelFixture(eye: false);
        fixture.Runner.Barrier.Release.TrySetResult();
        var panel = fixture.Transient!;
        Exception? notificationError = null;
        var reconfigured = false;
        NotifyCollectionChangedEventHandler observer = (_, change) =>
        {
            if (reconfigured || change.Action != NotifyCollectionChangedAction.Add) return;
            reconfigured = true;
            try { panel.Configure(null); }
            catch (Exception error) { notificationError = error; }
        };
        NotifyCollectionChangedEventHandler binding = (_, _) => { };
        panel.Series.CollectionChanged += observer;
        panel.Series.CollectionChanged += binding;
        try
        {
            await fixture.RunAsync().WaitAsync(TimeSpan.FromSeconds(10));
            reconfigured.ShouldBeTrue();
            notificationError.ShouldBeNull("reconfiguration during a bound collection notification must be safe");
            panel.Series.ShouldBeEmpty();
            panel.PlotModel.Series.ShouldBeEmpty();
            panel.HasResult.ShouldBeFalse();
        }
        finally
        {
            panel.Series.CollectionChanged -= observer;
            panel.Series.CollectionChanged -= binding;
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ThrowingCompletionObserver_CannotPreventDocumentLeaseDrain(bool eye)
    {
        await using var fixture = new TransientPanelFixture(eye);
        fixture.Runner.Barrier.Release.TrySetResult();
        object panel = (object?)fixture.Eye ?? fixture.Transient!;
        IAsyncDisposable? rescue = null;
        var fired = false;
        PropertyChangedEventHandler observer = (_, change) =>
        {
            if (change.PropertyName != "IsRunning" || (fixture.Eye?.IsRunning ?? fixture.Transient!.IsRunning)) return;
            fired = true;
            // Failure-only rescue: inspect the accepted owner, without changing execution or admission.
            // A broken finally must not leave this deliberately leaked lease alive after the assertion.
            var lifetime = panel.GetType().GetField("_lifetime", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(panel)!;
            rescue = (IAsyncDisposable?)lifetime.GetType()
                .GetField("_active", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(lifetime);
            throw new InvalidOperationException("completion observer failed");
        };
        ((INotifyPropertyChanged)panel).PropertyChanged += observer;
        try
        {
            var failure = await Should.ThrowAsync<InvalidOperationException>(() =>
                fixture.RunAsync().WaitAsync(TimeSpan.FromSeconds(10)));
            failure.Message.ShouldBe("completion observer failed");
            fired.ShouldBeTrue();
            await fixture.Canvas.Document.RetireAsync().WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally
        {
            ((INotifyPropertyChanged)panel).PropertyChanged -= observer;
            if (rescue != null) await rescue.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        }
    }
}
