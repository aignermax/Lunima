using Avalonia.Controls;
using Avalonia.Interactivity;

namespace CAP.Avalonia.Views.Shell;

/// <summary>Owns toolbar layout, shortcut popups, and flyouts; emits shell actions.</summary>
public partial class MainToolbarView : UserControl
{
    public MainToolbarView() => InitializeComponent();

    public void HideExportFlyout() => ExportMenuButton.Flyout?.Hide();

    public event EventHandler<RoutedEventArgs>? ZoomToFitRequested;
    private void ZoomToFitButton_Click(object? sender, RoutedEventArgs e) => ZoomToFitRequested?.Invoke(sender, e);

    public event EventHandler<RoutedEventArgs>? GridSnapChanged;
    private void GridSnapToggle_Click(object? sender, RoutedEventArgs e) => GridSnapChanged?.Invoke(sender, e);

    public event EventHandler<RoutedEventArgs>? ClipboardExportRequested;
    private void ExportMenuItem_Click(object? sender, RoutedEventArgs e) => ClipboardExportRequested?.Invoke(sender, e);

    public event EventHandler<RoutedEventArgs>? DesignChecksRequested;
    private void CheckDesignMenuItem_Click(object? sender, RoutedEventArgs e) => DesignChecksRequested?.Invoke(sender, e);

    public event EventHandler<RoutedEventArgs>? EblCheckRequested;
    private void CheckOpenEblMenuItem_Click(object? sender, RoutedEventArgs e) => EblCheckRequested?.Invoke(sender, e);

    public event EventHandler<RoutedEventArgs>? ModeSolverRequested;
    private void OpenModeSolverDialog_Click(object? sender, RoutedEventArgs e) => ModeSolverRequested?.Invoke(sender, e);

    public event EventHandler<RoutedEventArgs>? PdkResolutionCheckRequested;
    private void OpenPdkResolutionCheckDialog_Click(object? sender, RoutedEventArgs e) => PdkResolutionCheckRequested?.Invoke(sender, e);

    public event EventHandler<RoutedEventArgs>? RegistryBrowserRequested;
    private void OpenRegistryBrowser_Click(object? sender, RoutedEventArgs e) => RegistryBrowserRequested?.Invoke(sender, e);

    public event EventHandler<RoutedEventArgs>? IsaPlaygroundRequested;
    private void OpenIsaPlayground_Click(object? sender, RoutedEventArgs e) => IsaPlaygroundRequested?.Invoke(sender, e);

    public event EventHandler<RoutedEventArgs>? AiAssistantRequested;
    private void OpenAiAssistant_Click(object? sender, RoutedEventArgs e) => AiAssistantRequested?.Invoke(sender, e);
}
