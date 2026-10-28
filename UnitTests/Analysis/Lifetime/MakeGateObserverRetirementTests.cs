using System.ComponentModel;
using System.Reflection;
using Avalonia.Headless.XUnit;
using Shouldly;

namespace UnitTests.Analysis.Lifetime;

public sealed class MakeGateObserverRetirementTests
{
    [AvaloniaFact]
    public async Task ThrowingProcessingCompletionObserver_CannotSkipExtractionLeaseCleanup()
    {
        await using var fixture = new MakeGateLifetimeFixture();
        fixture.Extractor.Release.TrySetResult();
        IAsyncDisposable? rescue = null;
        var observed = false;
        PropertyChangedEventHandler observer = (_, change) =>
        {
            if (change.PropertyName != nameof(fixture.Panel.IsProcessing) || fixture.Panel.IsProcessing) return;
            observed = true;
            var session = fixture.Panel.GetType().GetField("_session", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(fixture.Panel)!;
            rescue = (IAsyncDisposable?)session.GetType().GetField("_active", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(session);
            throw new InvalidOperationException("processing completion observer failed");
        };
        fixture.Panel.PropertyChanged += observer;
        try
        {
            var failure = await Should.ThrowAsync<InvalidOperationException>(() =>
                fixture.Panel.SimulateCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(10)));
            failure.Message.ShouldBe("processing completion observer failed");
            observed.ShouldBeTrue();
            await fixture.Canvas.Document.RetireAsync().WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally
        {
            fixture.Panel.PropertyChanged -= observer;
            // Rescue only the failing baseline; the assertion uses the public document drain.
            if (rescue != null) await rescue.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        }
    }
}
