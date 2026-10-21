using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels;

public partial class MainViewModel
{
    [RelayCommand]
    private void SelectAll()
    {
        SetSelectModeCommand.Execute(null);
        using var update = Canvas.Selection.BeginUpdate();
        if (CanvasInteraction.SelectedWaveguideConnection is { } previousRoute)
            previousRoute.IsSelected = false;
        CanvasInteraction.SelectedWaveguideConnection = null;
        Canvas.SelectedComponent = null;
        var components = Canvas.InteractiveComponents;
        Canvas.Selection.SelectAll(components, Canvas.Connections,
            Canvas.IsInGroupEditMode ? [] : Canvas.CanvasFrozenPaths);
        // Keep the property panel and single-selection commands in sync with the batch.
        Canvas.SelectedComponent = components.FirstOrDefault();
    }
}
