using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CAP.Avalonia.Controls.Rendering.DeviceCategories;
using CAP.Avalonia.ViewModels.Library;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CAP.Avalonia.ViewModels.Workspaces.ContextPanels;

/// <summary>
/// The Optics workspace's "Sources &amp; detectors" quick section on top of the component
/// library: the library entries where light enters or leaves the circuit (grating / edge
/// couplers, lasers, photodetectors, terminators, analyzers), mirrored live from the
/// library's filtered list so search and PDK enablement apply to it too.
/// </summary>
public partial class SourcesQuickSectionViewModel : ObservableObject
{
    private readonly ObservableCollection<ComponentTemplate> _source;

    /// <summary>The source and detector templates of the filtered library, in library order.</summary>
    public ObservableCollection<ComponentTemplate> Items { get; } = new();

    /// <summary>True when the filtered library offers at least one source or detector.</summary>
    [ObservableProperty]
    private bool _hasItems;

    /// <summary>Mirrors <paramref name="filteredTemplates"/> (the library's visible templates).</summary>
    public SourcesQuickSectionViewModel(ObservableCollection<ComponentTemplate> filteredTemplates)
    {
        _source = filteredTemplates ?? throw new ArgumentNullException(nameof(filteredTemplates));
        _source.CollectionChanged += OnSourceChanged;
        Rebuild();
    }

    /// <summary>True when <paramref name="template"/> injects or terminates light.</summary>
    public static bool IsSourceOrDetector(ComponentTemplate template)
    {
        var category = DeviceCategoryClassifier.ClassifyNames(
            template.Name, null, template.NazcaFunctionName, hasElectricalPin: false);
        return category is DeviceCategory.InputOutput or DeviceCategory.Detector;
    }

    private void OnSourceChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // The library refills its list with Clear + one Add per template; appending keeps
        // the mirror O(n) instead of rebuilding it on every Add.
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewStartingIndex == _source.Count - e.NewItems!.Count)
        {
            foreach (ComponentTemplate added in e.NewItems)
            {
                if (IsSourceOrDetector(added))
                    Items.Add(added);
            }
            HasItems = Items.Count > 0;
            return;
        }
        Rebuild();
    }

    private void Rebuild()
    {
        Items.Clear();
        foreach (var template in _source.Where(IsSourceOrDetector))
            Items.Add(template);
        HasItems = Items.Count > 0;
    }
}
