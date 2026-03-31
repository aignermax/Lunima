using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Library;
using CAP_Core.Components;
using CAP_Core.Components.Core;

namespace CAP.Avalonia.Controls;

/// <summary>
/// Core DesignCanvas control - main entry point for the canvas control.
/// Rendering, mouse handling, and keyboard handling are in separate partial class files.
/// </summary>
public partial class DesignCanvas : Control
{
    public static readonly StyledProperty<DesignCanvasViewModel?> ViewModelProperty =
        AvaloniaProperty.Register<DesignCanvas, DesignCanvasViewModel?>(nameof(ViewModel));

    public static readonly StyledProperty<double> ZoomProperty =
        AvaloniaProperty.Register<DesignCanvas, double>(nameof(Zoom), 1.0);

    public static readonly StyledProperty<MainViewModel?> MainViewModelProperty =
        AvaloniaProperty.Register<DesignCanvas, MainViewModel?>(nameof(MainViewModel));

    public DesignCanvasViewModel? ViewModel
    {
        get => GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public double Zoom
    {
        get => GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    public MainViewModel? MainViewModel
    {
        get => GetValue(MainViewModelProperty);
        set => SetValue(MainViewModelProperty, value);
    }

    private readonly CanvasInteractionState _interactionState = new();

    /// <summary>
    /// Provides read-only access to interaction state for rendering partial classes.
    /// </summary>
    internal CanvasInteractionState InteractionState => _interactionState;

    // Public properties for rendering partial classes to access state
    internal WaveguideConnectionViewModel? HoveredConnection => _interactionState.HoveredConnection;
    internal bool ShowPlacementPreview => _interactionState.ShowPlacementPreview;
    internal ComponentTemplate? PlacementPreviewTemplate => _interactionState.PlacementPreviewTemplate;
    internal Point PlacementPreviewPosition => _interactionState.PlacementPreviewPosition;
    internal bool ShowDragPreview => _interactionState.ShowDragPreview;
    internal ComponentViewModel? DraggingComponent => _interactionState.DraggingComponent;
    internal Point DragPreviewPosition => _interactionState.DragPreviewPosition;
    internal bool DragPreviewValid => _interactionState.DragPreviewValid;
    internal PhysicalPin? ConnectionDragStartPin => _interactionState.ConnectionDragStartPin;
    internal Point ConnectionDragCurrentPoint => _interactionState.ConnectionDragCurrentPoint;

    /// <summary>
    /// Gets the last canvas position tracked by pointer movement (in canvas coordinates).
    /// Used for paste-at-cursor functionality.
    /// </summary>
    public Point LastCanvasPosition => _interactionState.LastCanvasPosition;

    static DesignCanvas()
    {
        AffectsRender<DesignCanvas>(ViewModelProperty, ZoomProperty);
        MainViewModelProperty.Changed.AddClassHandler<DesignCanvas>((canvas, e) => canvas.OnMainViewModelChanged(e));
        ViewModelProperty.Changed.AddClassHandler<DesignCanvas>((canvas, e) => canvas.OnViewModelChanged(e));
    }

    public DesignCanvas()
    {
        ClipToBounds = true;
        Focusable = true;
        InitGestures();
    }

    private void OnMainViewModelChanged(AvaloniaPropertyChangedEventArgs e)
    {
        if (e.OldValue is MainViewModel oldVm)
        {
            oldVm.CommandManager.StateChanged -= OnCommandStateChanged;
        }

        if (e.NewValue is MainViewModel newVm)
        {
            newVm.CommandManager.StateChanged += OnCommandStateChanged;
        }
    }

    private void OnViewModelChanged(AvaloniaPropertyChangedEventArgs e)
    {
        if (e.OldValue is DesignCanvasViewModel oldCanvas)
        {
            oldCanvas.PropertyChanged -= OnCanvasViewModelPropertyChanged;
            oldCanvas.RepaintRequested = null;
            oldCanvas.Components.CollectionChanged -= OnComponentsCollectionChanged;
            oldCanvas.Connections.CollectionChanged -= OnConnectionsCollectionChanged;
        }

        if (e.NewValue is DesignCanvasViewModel newCanvas)
        {
            newCanvas.PropertyChanged += OnCanvasViewModelPropertyChanged;
            newCanvas.RepaintRequested = () => InvalidateVisual();
            newCanvas.Components.CollectionChanged += OnComponentsCollectionChanged;
            newCanvas.Connections.CollectionChanged += OnConnectionsCollectionChanged;
        }
    }

    private void OnComponentsCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        InvalidateVisual();
    }

    private void OnConnectionsCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        InvalidateVisual();
    }

    private void OnCanvasViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DesignCanvasViewModel.ShowPowerFlow) ||
            e.PropertyName == nameof(DesignCanvasViewModel.IsRouting) ||
            e.PropertyName == nameof(DesignCanvasViewModel.PanX) ||
            e.PropertyName == nameof(DesignCanvasViewModel.PanY))
        {
            InvalidateVisual();
        }
    }

    private void OnCommandStateChanged(object? sender, EventArgs e)
    {
        InvalidateVisual();
    }
}
