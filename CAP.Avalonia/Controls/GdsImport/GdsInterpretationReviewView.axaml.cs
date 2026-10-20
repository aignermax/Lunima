using Avalonia.Controls;
using Avalonia.Interactivity;
using CAP.Avalonia.ViewModels.GdsImport.Interpretation;

namespace CAP.Avalonia.Controls.GdsImport;

public partial class GdsInterpretationReviewView : UserControl
{
    public GdsInterpretationReviewView()
    {
        InitializeComponent();
        ReviewPreview.LayerPicked += pair => (DataContext as GdsInterpretationReviewViewModel)?.PickLayer(pair);
        ReviewPreview.CellPathPicked += path => (DataContext as GdsInterpretationReviewViewModel)?.PickCell(path);
        ReviewPreview.PortPicked += index => (DataContext as GdsInterpretationReviewViewModel)?.PickPort(index);
        ReviewPreview.PortMoved += (index, x, y) => (DataContext as GdsInterpretationReviewViewModel)?.MovePort(index, x, y);
        ReviewPreview.PortDragStarted += () => (DataContext as GdsInterpretationReviewViewModel)?.BeginPortDrag();
        ReviewPreview.PortDragCompleted += () => (DataContext as GdsInterpretationReviewViewModel)?.EndPortDrag();
    }
    private void OnFit(object? sender, RoutedEventArgs e) => ReviewPreview.Fit();
}
