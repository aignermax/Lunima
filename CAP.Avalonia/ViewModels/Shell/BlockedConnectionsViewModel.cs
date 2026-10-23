using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Panels;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.Shell;

/// <summary>
/// Status-bar warning field: counts the connections the router could only draw as a
/// blocked fallback and walks through them one click at a time — each click selects the
/// next blocked connection and centers the canvas on it.
/// </summary>
public partial class BlockedConnectionsViewModel : ObservableObject, IDisposable
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly CanvasInteractionViewModel _interaction;
    private readonly Action<double, double> _centerCanvasOn;
    private int _cursor = -1;
    private readonly Dictionary<WaveguideConnectionViewModel, bool> _observed = new();

    /// <summary>Number of connections currently drawn as blocked fallbacks.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBlocked))]
    private int _blockedCount;

    /// <summary>True when at least one connection is blocked (the chip is hidden otherwise).</summary>
    public bool HasBlocked => BlockedCount > 0;

    /// <summary>Creates the field and starts tracking the canvas connections.</summary>
    /// <param name="canvas">Canvas whose connections are counted.</param>
    /// <param name="interaction">Selection owner; the found connection becomes its selection.</param>
    /// <param name="centerCanvasOn">Pans the viewport so the given design point is centered.</param>
    public BlockedConnectionsViewModel(
        DesignCanvasViewModel canvas,
        CanvasInteractionViewModel interaction,
        Action<double, double> centerCanvasOn)
    {
        _canvas = canvas;
        _interaction = interaction;
        _centerCanvasOn = centerCanvasOn;
        _canvas.Connections.CollectionChanged += OnConnectionsChanged;
        _canvas.PropertyChanged += OnCanvasPropertyChanged;
        foreach (var connection in _canvas.Connections)
            Observe(connection);
        Refresh();
    }

    public void Dispose()
    {
        _canvas.Connections.CollectionChanged -= OnConnectionsChanged;
        _canvas.PropertyChanged -= OnCanvasPropertyChanged;
        foreach (var connection in _observed.Keys) connection.PropertyChanged -= OnConnectionPropertyChanged;
        _observed.Clear();
    }

    private void Observe(WaveguideConnectionViewModel connection)
    {
        if (!_observed.TryAdd(connection, connection.IsBlockedFallback)) return;
        connection.PropertyChanged += OnConnectionPropertyChanged;
        if (connection.IsBlockedFallback) BlockedCount++;
    }

    /// <summary>Recounts the blocked connections.</summary>
    public void Refresh()
    {
        foreach (var connection in _observed.Keys.ToArray())
            _observed[connection] = connection.IsBlockedFallback;
        BlockedCount = _observed.Values.Count(blocked => blocked);
    }

    /// <summary>
    /// Selects the blocked connection after the previously visited one (wrapping around)
    /// and centers the canvas on its midpoint. Does nothing when no connection is blocked.
    /// </summary>
    [RelayCommand]
    private void SelectNextBlocked()
    {
        var blocked = _canvas.Connections.Where(c => c.IsBlockedFallback).ToList();
        if (blocked.Count == 0)
            return;
        _cursor = (_cursor + 1) % blocked.Count;
        var target = blocked[_cursor];
        Select(target);
        _centerCanvasOn((target.StartX + target.EndX) / 2, (target.StartY + target.EndY) / 2);
    }

    private void Select(WaveguideConnectionViewModel target)
    {
        if (_interaction.SelectedWaveguideConnection is { } previous)
            previous.IsSelected = false;
        _canvas.Selection.ClearSelection();
        _interaction.SelectedComponent = null;
        _interaction.SelectedCanvasFrozenPath = null;
        _canvas.SelectedComponent = null;
        target.IsSelected = true;
        _interaction.SelectedWaveguideConnection = target;
    }

    private void OnConnectionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
            foreach (WaveguideConnectionViewModel removed in e.OldItems)
            {
                removed.PropertyChanged -= OnConnectionPropertyChanged;
                if (_observed.Remove(removed, out var wasBlocked) && wasBlocked) BlockedCount--;
            }
        if (e.NewItems != null)
            foreach (WaveguideConnectionViewModel added in e.NewItems)
                Observe(added);
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var connection in _observed.Keys) connection.PropertyChanged -= OnConnectionPropertyChanged;
            _observed.Clear();
            BlockedCount = 0;
            foreach (var connection in _canvas.Connections) Observe(connection);
        }
    }

    private void OnConnectionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(WaveguideConnectionViewModel.IsBlockedFallback)
            || sender is not WaveguideConnectionViewModel connection
            || !_observed.TryGetValue(connection, out var wasBlocked)) return;
        var blocked = connection.IsBlockedFallback;
        if (blocked == wasBlocked) return;
        _observed[connection] = blocked;
        BlockedCount += blocked ? 1 : -1;
    }

    private void OnCanvasPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DesignCanvasViewModel.IsRouting) && !_canvas.IsRouting)
            Refresh();
    }
}
