using CAP.Avalonia.ViewModels.Canvas;

namespace CAP.Avalonia.ViewModels.Properties;

/// <summary>
/// Contributes a ViewModel that drives the "Selected Component Properties"
/// section of the right panel for one specific kind of component (e.g. ONA
/// Analyzer, phase shifter, grating coupler).
///
/// Providers are tried in DI registration order; the first one that returns
/// a non-null ViewModel wins. A generic fallback provider should always be
/// registered last so every selected component yields some editor.
/// </summary>
public interface IComponentEditorProvider
{
    /// <summary>
    /// Returns a ViewModel that the right-panel template selector will bind
    /// to a per-component-type editor view, or <c>null</c> if this provider
    /// does not handle the given component.
    /// </summary>
    object? TryCreateEditor(ComponentViewModel componentVm);
}
