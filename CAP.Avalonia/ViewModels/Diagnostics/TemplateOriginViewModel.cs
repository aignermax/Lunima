using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CAP.Avalonia.ViewModels.Canvas;

namespace CAP.Avalonia.ViewModels.Diagnostics;

/// <summary>
/// ViewModel for displaying template origin information and managing template instance selection.
/// Shows which components were placed from templates and allows selecting all components from the same instance.
/// </summary>
public partial class TemplateOriginViewModel : ObservableObject
{
    private readonly DesignCanvasViewModel _canvas;

    /// <summary>
    /// Information about template instances on the canvas.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<TemplateInstanceInfo> _templateInstances = new();

    /// <summary>
    /// Whether any template instances exist on the canvas.
    /// </summary>
    [ObservableProperty]
    private bool _hasTemplateInstances;

    /// <summary>
    /// Currently selected template instance info (for details panel).
    /// </summary>
    [ObservableProperty]
    private TemplateInstanceInfo? _selectedInstance;

    public TemplateOriginViewModel(DesignCanvasViewModel canvas)
    {
        _canvas = canvas;
    }

    /// <summary>
    /// Scans the canvas and builds list of template instances.
    /// </summary>
    [RelayCommand]
    private void RefreshTemplateInstances()
    {
        var instances = _canvas.Components
            .Where(c => c.IsFromTemplate)
            .GroupBy(c => c.TemplateInstanceId)
            .Select(g => new TemplateInstanceInfo
            {
                InstanceId = g.Key ?? Guid.Empty,
                TemplateName = g.First().SourceTemplateName ?? "Unknown",
                ComponentCount = g.Count(),
                Components = g.ToList()
            })
            .OrderBy(i => i.TemplateName)
            .ToList();

        TemplateInstances.Clear();
        foreach (var instance in instances)
        {
            TemplateInstances.Add(instance);
        }

        HasTemplateInstances = TemplateInstances.Count > 0;
    }

    /// <summary>
    /// Selects all components from a specific template instance.
    /// </summary>
    [RelayCommand]
    private void SelectTemplateInstance(TemplateInstanceInfo? instance)
    {
        if (instance == null) return;

        _canvas.Selection.ClearSelection();
        foreach (var component in instance.Components)
        {
            _canvas.Selection.AddToSelection(component);
        }
    }

    /// <summary>
    /// Highlights (temporarily shows) all components from a template instance without changing selection.
    /// </summary>
    public void HighlightTemplateInstance(Guid? instanceId)
    {
        // This will be implemented when hover functionality is added to the rendering layer
        // For now, it's a placeholder for future enhancement
    }
}

/// <summary>
/// Information about a single template instance (all components placed at once from a template).
/// </summary>
public class TemplateInstanceInfo
{
    /// <summary>
    /// Unique ID for this template instance.
    /// </summary>
    public Guid InstanceId { get; set; }

    /// <summary>
    /// Name of the template that was instantiated.
    /// </summary>
    public string TemplateName { get; set; } = "";

    /// <summary>
    /// Number of components in this instance.
    /// </summary>
    public int ComponentCount { get; set; }

    /// <summary>
    /// Components that belong to this instance.
    /// </summary>
    public List<ComponentViewModel> Components { get; set; } = new();

    /// <summary>
    /// Display text for the UI list.
    /// </summary>
    public string DisplayText => $"{TemplateName} ({ComponentCount} components)";
}
