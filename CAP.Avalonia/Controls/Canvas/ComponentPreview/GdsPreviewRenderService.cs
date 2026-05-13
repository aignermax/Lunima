using System.Collections.Concurrent;
using Avalonia.Threading;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Export;

namespace CAP.Avalonia.Controls.Canvas.ComponentPreview;

/// <summary>
/// Manages async fetching and caching of GDS preview thumbnails for canvas components.
/// </summary>
/// <remarks>
/// <para>
/// The first call to <see cref="TryGetPreview"/> for a given template triggers a
/// background fetch via <see cref="NazcaComponentPreviewService"/>.  While the fetch
/// is in progress the method returns <c>null</c> so the caller can fall back to the
/// legacy rectangle renderer.  Once the result arrives <see cref="OnPreviewLoaded"/>
/// is fired on the UI thread so the canvas can call <c>InvalidateVisual()</c>.
/// </para>
/// <para>
/// Failures (Python unavailable, script timeout, 0 polygons) are cached as <c>null</c>
/// so no further retries are attempted during the session — the component simply stays
/// as a legacy rectangle.
/// </para>
/// </remarks>
public sealed class GdsPreviewRenderService
{
    private readonly NazcaComponentPreviewService _previewService;
    private readonly GdsPreviewCache _cache = new();

    /// <summary>Tracks keys for which a fetch is currently in flight.</summary>
    private readonly ConcurrentDictionary<string, byte> _pendingFetches = new();

    /// <summary>
    /// Invoked on the UI thread whenever a previously-pending preview finishes
    /// loading.  Assign <c>() => canvas.InvalidateVisual()</c> from
    /// <see cref="CAP.Avalonia.Controls.DesignCanvas"/> to trigger a repaint.
    /// </summary>
    public Action? OnPreviewLoaded { get; set; }

    /// <summary>
    /// Initializes the service with the shared Nazca preview back-end.
    /// </summary>
    public GdsPreviewRenderService(NazcaComponentPreviewService previewService)
    {
        _previewService = previewService ?? throw new ArgumentNullException(nameof(previewService));
    }

    /// <summary>
    /// Returns cached <see cref="GdsPreviewData"/> for the given component template,
    /// or <c>null</c> while a background fetch is pending or when no preview is
    /// available (unknown Nazca function, Python unavailable, empty polygon list).
    /// </summary>
    /// <param name="comp">The component for which to fetch/retrieve the preview.</param>
    public GdsPreviewData? TryGetPreview(ComponentViewModel comp)
    {
        var cacheKey = BuildCacheKey(comp);
        if (cacheKey == null)
            return null;

        if (_cache.TryGet(cacheKey, out var cached))
            return cached;

        // Enqueue a background fetch only once per key
        if (_pendingFetches.TryAdd(cacheKey, 0))
            _ = FetchAndCacheAsync(cacheKey, comp);

        return null;
    }

    /// <summary>
    /// Builds the cache key for a component.  Returns <c>null</c> when the
    /// component has no Nazca function name (built-in or external-port components).
    /// </summary>
    internal static string? BuildCacheKey(ComponentViewModel comp)
    {
        var fn = comp.Component.NazcaFunctionName;
        if (string.IsNullOrWhiteSpace(fn))
            return null;
        return $"{fn}|{comp.Width:F2}|{comp.Height:F2}";
    }

    private async Task FetchAndCacheAsync(string cacheKey, ComponentViewModel comp)
    {
        var module = comp.Component.NazcaModuleName;
        var function = comp.Component.NazcaFunctionName;
        var parameters = comp.Component.NazcaFunctionParameters;

        NazcaPreviewResult result;
        try
        {
            result = await _previewService.RenderAsync(module, function, parameters);
        }
        catch
        {
            result = NazcaPreviewResult.Fail("Unexpected error during GDS preview fetch.");
        }
        finally
        {
            _pendingFetches.TryRemove(cacheKey, out _);
        }

        var data = result.Success && result.Polygons.Count > 0
            ? new GdsPreviewData(result, comp.Width, comp.Height)
            : null;

        _cache.Set(cacheKey, data);

        if (data != null)
            Dispatcher.UIThread.Post(() => OnPreviewLoaded?.Invoke());
    }
}
