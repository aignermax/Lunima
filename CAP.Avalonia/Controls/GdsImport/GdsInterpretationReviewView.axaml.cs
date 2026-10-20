using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using CAP.Avalonia.ViewModels.GdsImport.Interpretation;

namespace CAP.Avalonia.Controls.GdsImport;

public partial class GdsInterpretationReviewView : UserControl
{
    public GdsInterpretationReviewView()
    {
        InitializeComponent();
        GdsReviewLayerList.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            if (e.GetCurrentPoint(GdsReviewLayerList).Properties.IsLeftButtonPressed)
                (DataContext as GdsInterpretationReviewViewModel)?.MarkLayerInteraction();
        }, RoutingStrategies.Tunnel);
        GdsReviewLayerList.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (e.Key is Key.Up or Key.Down or Key.Home or Key.End or Key.Enter or Key.Space)
                (DataContext as GdsInterpretationReviewViewModel)?.MarkLayerInteraction();
        }, RoutingStrategies.Tunnel);
        ReviewPreview.LayerPicked += pair => (DataContext as GdsInterpretationReviewViewModel)?.PickLayer(pair);
        ReviewPreview.CellPathPicked += path => (DataContext as GdsInterpretationReviewViewModel)?.PickCell(path);
        ReviewPreview.PortPicked += index => (DataContext as GdsInterpretationReviewViewModel)?.PickPort(index);
        ReviewPreview.PortMoved += (index, x, y) => (DataContext as GdsInterpretationReviewViewModel)?.MovePort(index, x, y);
        ReviewPreview.PortDragStarted += () => (DataContext as GdsInterpretationReviewViewModel)?.BeginPortDrag();
        ReviewPreview.PortDragCompleted += () => (DataContext as GdsInterpretationReviewViewModel)?.EndPortDrag();
    }
    private void OnFit(object? sender, RoutedEventArgs e) => ReviewPreview.Fit();
    private void OnFocusLayer(object? sender, RoutedEventArgs e) => ReviewPreview.FocusHighlightedLayer();
}
