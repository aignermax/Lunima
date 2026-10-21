using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace CAP.Avalonia.Selection;

/// <summary>Publishes one completed selection instead of rebuilding panels for every item.</summary>
internal sealed class SelectionCollection<T> : ObservableCollection<T>
{
    private int _depth;
    private bool _changed;

    internal IDisposable DeferNotifications()
    {
        _depth++;
        return new Batch(this);
    }

    protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        if (_depth > 0) _changed = true;
        else base.OnCollectionChanged(e);
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        if (_depth == 0) base.OnPropertyChanged(e);
    }

    private void Finish()
    {
        if (--_depth != 0 || !_changed) return;
        _changed = false;
        base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        base.OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        base.OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    private sealed class Batch(SelectionCollection<T> owner) : IDisposable
    {
        private SelectionCollection<T>? _owner = owner;
        public void Dispose()
        {
            var owner = _owner;
            _owner = null;
            owner?.Finish();
        }
    }
}
