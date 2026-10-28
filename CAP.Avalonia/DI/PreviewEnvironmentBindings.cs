using System.Collections.Specialized;
using CAP.Avalonia.Controls.Canvas.ComponentPreview;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Library;

namespace CAP.Avalonia.DI;

/// <summary>Owns interpreter and PDK subscriptions for one preview-service lifetime.</summary>
internal sealed class PreviewEnvironmentBindings : IDisposable
{
    private readonly GdsPreviewRenderService _previews;
    private readonly UserPreferencesService _preferences;
    private readonly PdkManagerViewModel _pdks;

    public PreviewEnvironmentBindings(GdsPreviewRenderService previews, UserPreferencesService preferences,
        PdkManagerViewModel pdks)
    {
        _previews = previews;
        _preferences = preferences;
        _pdks = pdks;
        preferences.PythonInterpreterActivated += previews.RetryFailedPreviews;
        pdks.LoadedPdks.CollectionChanged += OnLoadedPdksChanged;
        pdks.DefinitionsChanged += previews.InvalidatePdkPreviews;
    }

    private void OnLoadedPdksChanged(object? sender, NotifyCollectionChangedEventArgs args) =>
        _previews.InvalidatePdkPreviews();

    public void Dispose()
    {
        _preferences.PythonInterpreterActivated -= _previews.RetryFailedPreviews;
        _pdks.LoadedPdks.CollectionChanged -= OnLoadedPdksChanged;
        _pdks.DefinitionsChanged -= _previews.InvalidatePdkPreviews;
    }
}
