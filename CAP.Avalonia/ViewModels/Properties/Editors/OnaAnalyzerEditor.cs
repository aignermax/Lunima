using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.Properties.Editors;

/// <summary>
/// Editor ViewModel for an ONA Analyzer component. Surfaces a single
/// "Open ONA Sweep…" button; the actual sweep parameters live in the
/// dedicated tool window so the right panel stays compact.
/// </summary>
public partial class OnaAnalyzerEditorViewModel : ObservableObject
{
    private readonly Component _analyzer;

    /// <summary>Wired by MainWindow to open the ONA tool window for this analyzer.</summary>
    public Func<Component, Task>? OpenSweepAsync { get; set; }

    /// <summary>Display name shown in the editor header.</summary>
    public string AnalyzerName => !string.IsNullOrEmpty(_analyzer.HumanReadableName)
        ? _analyzer.HumanReadableName!
        : _analyzer.Name;

    /// <summary>Creates the editor for the given analyzer instance.</summary>
    public OnaAnalyzerEditorViewModel(Component analyzer)
    {
        _analyzer = analyzer;
    }

    /// <summary>Opens the dedicated ONA sweep tool window.</summary>
    [RelayCommand]
    private async Task OpenSweep()
    {
        if (OpenSweepAsync != null)
            await OpenSweepAsync(_analyzer);
    }
}

/// <summary>Provider that supplies <see cref="OnaAnalyzerEditorViewModel"/> for analysis tools.</summary>
public class OnaAnalyzerEditorProvider : IComponentEditorProvider
{
    /// <summary>Delegate set by MainWindow that knows how to open the tool window.</summary>
    public Func<Component, Task>? OpenSweepAsync { get; set; }

    /// <inheritdoc/>
    public object? TryCreateEditor(ComponentViewModel componentVm)
    {
        if (!componentVm.Component.IsAnalysisTool) return null;
        return new OnaAnalyzerEditorViewModel(componentVm.Component)
        {
            OpenSweepAsync = OpenSweepAsync
        };
    }
}
