using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Workspaces;

namespace CAP.Avalonia.Views.Panels.WorkspaceRail;

/// <summary>
/// The left rail's Chiplets pane (System workspace default): the design's dies with their
/// process, size and PDKs, the die-to-die links and the New die flyout. A click selects a
/// die and centers the canvas on it; a double-click opens it.
/// </summary>
public partial class ChipletsPane : UserControl
{
    /// <summary>Initializes the pane.</summary>
    public ChipletsPane()
    {
        InitializeComponent();
    }

    private void NewDieFlyout_Opening(object? sender, EventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.NewDie.Prepare();
    }

    private void CreateDie_Click(object? sender, RoutedEventArgs e)
    {
        NewDieButton.Flyout?.Hide();
    }

    private void ChipletRow_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is MainViewModel vm
            && (sender as Control)?.DataContext is ChipletSummaryRow { Group: { IsDie: true } die })
            vm.ZoomToDie(die);
    }
}
