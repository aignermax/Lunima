using System.ComponentModel;
using System.Globalization;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Panels;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CAP.Avalonia.ViewModels.Shell;

/// <summary>
/// Fixed status-bar fields that summarize the editor state: the active canvas mode and
/// what is selected. Both re-read live on selection, mode and UI-language changes.
/// </summary>
public partial class StatusBarViewModel : ObservableObject
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly CanvasInteractionViewModel _interaction;
    private readonly LocalizationService _localization;

    /// <summary>Localized name of the active canvas mode.</summary>
    [ObservableProperty]
    private string _modeLabel = "";

    /// <summary>Localized one-line summary of the current selection.</summary>
    [ObservableProperty]
    private string _selectionSummary = "";

    /// <summary>True while anything (component, connection or frozen path) is selected.</summary>
    [ObservableProperty]
    private bool _hasSelection;

    /// <summary>Creates the fields and subscribes to the selection and mode sources.</summary>
    /// <param name="canvas">Canvas owning the multi-selection set.</param>
    /// <param name="interaction">Owner of the mode and the single clicked selection.</param>
    /// <param name="localization">Source of the localized labels.</param>
    public StatusBarViewModel(
        DesignCanvasViewModel canvas,
        CanvasInteractionViewModel interaction,
        LocalizationService localization)
    {
        _canvas = canvas;
        _interaction = interaction;
        _localization = localization;
        _canvas.Selection.SelectedComponents.CollectionChanged += (_, _) => UpdateSelection();
        _canvas.Selection.SelectedConnections.CollectionChanged += (_, _) => UpdateSelection();
        _interaction.PropertyChanged += OnInteractionPropertyChanged;
        LocalizationWeakSubscription.Subscribe(this, static (self, _) => self.Refresh(), _localization);
        Refresh();
    }

    /// <summary>Localization key of the label for <paramref name="mode"/>.</summary>
    public static string ModeKey(InteractionMode mode) => mode switch
    {
        InteractionMode.PlaceComponent or InteractionMode.PlaceGroupTemplate => "Canvas.Mode.Place",
        InteractionMode.Connect => "Canvas.Mode.Connect",
        InteractionMode.Delete => "Canvas.Mode.Delete",
        InteractionMode.Probe => "Canvas.Mode.Probe",
        InteractionMode.PickAnalysisOutput => "Canvas.Mode.PickOutput",
        InteractionMode.Cut => "Canvas.Mode.Cut",
        _ => "Canvas.Mode.Select",
    };

    private void Refresh()
    {
        ModeLabel = _localization.Translate(ModeKey(_interaction.CurrentMode));
        UpdateSelection();
    }

    private void OnInteractionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(CanvasInteractionViewModel.CurrentMode):
                ModeLabel = _localization.Translate(ModeKey(_interaction.CurrentMode));
                break;
            case nameof(CanvasInteractionViewModel.SelectedComponent):
            case nameof(CanvasInteractionViewModel.SelectedWaveguideConnection):
            case nameof(CanvasInteractionViewModel.SelectedCanvasFrozenPath):
                UpdateSelection();
                break;
        }
    }

    private void UpdateSelection()
    {
        SelectionSummary = BuildSelectionSummary();
        HasSelection = _canvas.Selection.SelectedComponents.Count > 0
            || _canvas.Selection.SelectedConnections.Count > 0
            || _interaction.SelectedComponent != null
            || _interaction.SelectedWaveguideConnection != null
            || _interaction.SelectedCanvasFrozenPath != null;
    }

    private string BuildSelectionSummary()
    {
        var selection = _canvas.Selection;
        int components = selection.SelectedComponents.Count;
        if (components == 0 && _interaction.SelectedComponent != null)
            components = 1;
        int connections = selection.SelectedConnections.Count;
        var clicked = _interaction.SelectedWaveguideConnection;
        if (clicked != null && !selection.SelectedConnections.Contains(clicked))
            connections++;

        if (components == 0 && connections == 0)
            return _localization.Translate("StatusBar.Selection.None");
        if (components > 0 && connections > 0)
            return Format("StatusBar.Selection.Mixed", components, connections);
        if (components == 1)
            return SingleComponentName();
        if (components > 1)
            return Format("StatusBar.Selection.Components", components);
        if (connections == 1 && clicked != null)
            return Format("StatusBar.Selection.Connection", clicked.PathLength.ToString("F1", CultureInfo.CurrentCulture));
        return Format("StatusBar.Selection.Connections", connections);
    }

    private string SingleComponentName()
    {
        var selection = _canvas.Selection;
        var component = selection.SelectedComponents.Count > 0
            ? selection.SelectedComponents[0]
            : _interaction.SelectedComponent;
        return component?.DisplayName ?? _localization.Translate("StatusBar.Selection.None");
    }

    private string Format(string key, params object[] args) =>
        string.Format(CultureInfo.CurrentCulture, _localization.Translate(key), args);
}
