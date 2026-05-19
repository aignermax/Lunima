using CAP.Avalonia.ViewModels.Canvas;

namespace CAP.Avalonia.ViewModels.Properties;

/// <summary>
/// Picks the right editor ViewModel for the currently selected canvas
/// component by asking each registered <see cref="IComponentEditorProvider"/>
/// in turn. First non-null wins; a fallback provider is expected to be
/// registered last so every component yields some editor.
/// </summary>
public class ComponentEditorFactory
{
    private readonly IReadOnlyList<IComponentEditorProvider> _providers;

    /// <summary>
    /// Initialises the factory with all registered editor providers in DI
    /// registration order. Providers should be registered most-specific
    /// first, generic-fallback last.
    /// </summary>
    public ComponentEditorFactory(IEnumerable<IComponentEditorProvider> providers)
    {
        _providers = providers.ToList();
    }

    /// <summary>
    /// Returns the editor ViewModel for the given selected component, or
    /// null if no component is selected. Never returns null when a component
    /// is selected provided a generic fallback provider is registered.
    /// </summary>
    public object? CreateEditor(ComponentViewModel? componentVm)
    {
        if (componentVm == null) return null;
        foreach (var provider in _providers)
        {
            var editor = provider.TryCreateEditor(componentVm);
            if (editor != null) return editor;
        }
        return null;
    }
}
