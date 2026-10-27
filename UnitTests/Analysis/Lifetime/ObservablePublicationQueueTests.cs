using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CAP.Avalonia.ViewModels;
using Shouldly;

namespace UnitTests.Analysis.Lifetime;

public sealed class ObservablePublicationQueueTests
{
    [Fact]
    public void NestedCollectionRebuild_PreservesOldEventIndicesForEverySubscriber()
    {
        var publication = new ObservablePublicationQueue();
        var rows = new ObservableCollection<int> { 7 };
        var replaced = false;
        var observed = new List<int>();
        rows.CollectionChanged += (_, change) =>
        {
            if (replaced || change.Action != NotifyCollectionChangedAction.Add) return;
            replaced = true;
            publication.Publish(() => { rows.Clear(); rows.Add(9); });
        };
        rows.CollectionChanged += (_, change) =>
        {
            if (change.Action != NotifyCollectionChangedAction.Add) return;
            change.NewStartingIndex.ShouldBeInRange(0, rows.Count - 1);
            rows[change.NewStartingIndex].ShouldBe((int)change.NewItems![0]!);
            observed.Add(rows[change.NewStartingIndex]);
        };

        publication.Publish(() => rows.Add(8));

        rows.ToArray().ShouldBe([9]);
        observed.ToArray().ShouldBe([8, 9]);
    }

    [Fact]
    public void NestedCountObserver_DefersMutationUntilPropertyAndCollectionDispatchFinish()
    {
        var publication = new ObservablePublicationQueue();
        var rows = new ObservableCollection<int> { 7 };
        var replaced = false;
        ((INotifyPropertyChanged)rows).PropertyChanged += (_, change) =>
        {
            if (replaced || change.PropertyName != "Count") return;
            replaced = true;
            publication.Publish(() => { rows.Clear(); rows.Add(9); });
        };
        rows.CollectionChanged += (_, change) =>
        {
            if (change.Action == NotifyCollectionChangedAction.Add)
                rows[change.NewStartingIndex].ShouldBe((int)change.NewItems![0]!);
        };

        publication.Publish(() => rows.Add(8));

        rows.ToArray().ShouldBe([9]);
    }

    [Fact]
    public void ThrowingObserver_DoesNotStrandQueuedConfigurationOrPoisonFuturePublication()
    {
        var publication = new ObservablePublicationQueue();
        var displayed = "old";
        var failure = Should.Throw<InvalidOperationException>(() => publication.Publish(() =>
        {
            publication.Publish(() => displayed = "replacement");
            throw new InvalidOperationException("observer failed");
        }));
        failure.Message.ShouldBe("observer failed");
        displayed.ShouldBe("replacement");
        publication.Publish(() => displayed = "next");
        displayed.ShouldBe("next");
    }
}
