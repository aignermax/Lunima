using System.Collections.Specialized;
using System.ComponentModel;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Analysis.AnalysisOutput;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using Component = CAP_Core.Components.Core.Component;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.Analysis.AnalysisReadiness;

/// <summary>
/// Why an empty optical analysis tab cannot show anything yet, and the one-click fix:
/// without a light source no optical analysis runs (fix: arm placement of a light-source
/// coupler); the time-domain tabs additionally evaluate one output coupler, which falls
/// back to an automatic pick until the user picks one on the canvas. The dock's empty
/// states bind these flags so every tab explains itself the same way.
/// </summary>
public partial class AnalysisReadinessViewModel : ObservableObject
{
    private readonly AnalysisOutputPanelViewModel _output;
    private DesignCanvasViewModel? _canvas;

    /// <summary>True when the design contains at least one light source (top level or inside a group).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsLightSource), nameof(NeedsOutputPick), nameof(IsReadyForTimeDomain))]
    private bool _hasLightSource;

    /// <summary>Creates the readiness view over the shared analysis-output header.</summary>
    public AnalysisReadinessViewModel(AnalysisOutputPanelViewModel output)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _output.PropertyChanged += OnOutputChanged;
    }

    /// <summary>Optical analyses cannot run: there is no light source.</summary>
    public bool NeedsLightSource => !HasLightSource;

    /// <summary>A light source exists but no output coupler is picked (time-domain tabs auto-pick one).</summary>
    public bool NeedsOutputPick => HasLightSource && !_output.HasOutput;

    /// <summary>Light source present and output picked: only a run is missing.</summary>
    public bool IsReadyForTimeDomain => HasLightSource && _output.HasOutput;

    /// <summary>Arms placement of a light-source component; wired by the main window's ViewModel.</summary>
    public Action? PlaceLightSourceRequested { get; set; }

    /// <summary>Watches <paramref name="canvas"/> so the flags follow placements and deletions.</summary>
    public void Configure(DesignCanvasViewModel canvas)
    {
        if (_canvas != null)
            _canvas.Components.CollectionChanged -= OnComponentsChanged;
        _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
        _canvas.Components.CollectionChanged += OnComponentsChanged;
        Refresh();
    }

    /// <summary>Recomputes <see cref="HasLightSource"/> from the canvas.</summary>
    public void Refresh()
    {
        HasLightSource = _canvas != null && _canvas.Components.Any(IsOrContainsLightSource);
        NotifyOutputDependents();
    }

    [RelayCommand]
    private void PlaceLightSource() => PlaceLightSourceRequested?.Invoke();

    [RelayCommand]
    private void PickOutput() => _output.PickCommand.Execute(null);

    private static bool IsOrContainsLightSource(ComponentViewModel vm) =>
        vm.IsLightSource || ContainsLightSource(vm.Component);

    private static bool ContainsLightSource(Component component)
    {
        if (SimulationService.IsLightSource(component))
            return true;
        return component is ComponentGroup group && group.ChildComponents.Any(ContainsLightSource);
    }

    private void OnComponentsChanged(object? sender, NotifyCollectionChangedEventArgs e) => Refresh();

    private void OnOutputChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AnalysisOutputPanelViewModel.HasOutput))
            NotifyOutputDependents();
    }

    private void NotifyOutputDependents()
    {
        OnPropertyChanged(nameof(NeedsOutputPick));
        OnPropertyChanged(nameof(IsReadyForTimeDomain));
    }
}
