using System.ComponentModel;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.GdsImport;
using CAP.Avalonia.ViewModels.Panels;

namespace CAP.Avalonia.ViewModels.Shell;

/// <summary>Projects canvas selection into the panels that describe that selection.</summary>
internal sealed class SelectionPanelCoordinator : IDisposable
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly CanvasInteractionViewModel _interaction;
    private readonly LeftPanelViewModel _left;
    private readonly RightPanelViewModel _right;
    private readonly BottomPanelViewModel _bottom;
    private readonly GdsHierarchyActionsViewModel _imported;

    public SelectionPanelCoordinator(DesignCanvasViewModel canvas, CanvasInteractionViewModel interaction,
        LeftPanelViewModel left, RightPanelViewModel right, BottomPanelViewModel bottom,
        GdsHierarchyActionsViewModel imported)
    {
        (_canvas, _interaction, _left, _right, _bottom, _imported) = (canvas, interaction, left, right, bottom, imported);
        interaction.OnSelectionChanged = SelectionChanged;
        interaction.PropertyChanged += InteractionChanged;
    }

    private void SelectionChanged(ComponentViewModel? component)
    {
        _imported.ConfigureForSelection(component);
        _right.Sweep.ConfigureForComponent(component, _canvas);
        _right.TruthTable.ConfigureForSelection(component, _canvas);
        _right.MakeGate.ConfigureForSelection(component, _canvas);
        _bottom.Analysis.Optimization.RefreshFromCanvas();
        _left.HierarchyPanel.SyncSelectionFromCanvas(component);
    }

    private void InteractionChanged(object? sender, PropertyChangedEventArgs change)
    {
        if (change.PropertyName != nameof(CanvasInteractionViewModel.SelectedWaveguideConnection)) return;
        var connection = _interaction.SelectedWaveguideConnection;
        _bottom.ConnectionRouting.SelectedConnection = connection;
        _bottom.RerouteImported.SelectedConnection = connection;
        _bottom.LengthMatching.SelectedConnection = connection;
        _bottom.ConnectionLoss.SelectedConnection = connection;
    }

    public void Dispose()
    {
        _interaction.PropertyChanged -= InteractionChanged;
        if (_interaction.OnSelectionChanged == SelectionChanged) _interaction.OnSelectionChanged = null;
    }
}
