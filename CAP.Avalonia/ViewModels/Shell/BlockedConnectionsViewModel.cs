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
public partial class BlockedConnectionsViewModel : ObservableObject
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly CanvasInteractionViewModel _interaction;
    private readonly Action<double, double> _centerCanvasOn;
    private int _cursor = -1;

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
            connection.PropertyChanged += OnConnectionPropertyChanged;
        Refresh();
    }

    /// <summary>Recounts the blocked connections.</summary>
    public void Refresh() => BlockedCount = _canvas.Connections.Count(c => c.IsBlockedFallback);

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
                removed.PropertyChanged -= OnConnectionPropertyChanged;
        if (e.NewItems != null)
            foreach (WaveguideConnectionViewModel added in e.NewItems)
                added.PropertyChanged += OnConnectionPropertyChanged;
        if (e.Action == NotifyCollectionChangedAction.Reset)
            foreach (var connection in _canvas.Connections)
            {
                connection.PropertyChanged -= OnConnectionPropertyChanged;
                connection.PropertyChanged += OnConnectionPropertyChanged;
            }
        Refresh();
    }

    private void OnConnectionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WaveguideConnectionViewModel.IsBlockedFallback))
            Refresh();
    }

    private void OnCanvasPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DesignCanvasViewModel.IsRouting) && !_canvas.IsRouting)
            Refresh();
    }
}
