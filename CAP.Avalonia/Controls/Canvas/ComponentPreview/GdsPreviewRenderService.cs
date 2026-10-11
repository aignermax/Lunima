using System.Collections.Concurrent;
using System.Globalization;
using Avalonia.Threading;
using CAP.Avalonia.Services.GdsFactoryExport;
using CAP_Core.Components.Core;
using CAP_Core.Export;

namespace CAP.Avalonia.Controls.Canvas.ComponentPreview;

/// <summary>
/// Fetches and caches GDS preview geometry for library thumbnails and canvas components.
/// </summary>
/// <remarks>
/// <para>
/// Geometry is keyed by its render identity (<see cref="GdsPreviewKey"/>: module, function,
/// parameters) and looked up in memory, then on disk, then rendered via Python in the
/// background. Thumbnails and placed components share these entries, so placing a component
/// whose thumbnail is already on screen costs no second render, and previews survive restarts.
/// While a fetch is pending the callers get <c>null</c> and draw the plain rectangle body;
/// <see cref="OnPreviewLoaded"/> fires on the UI thread when the geometry arrives.
/// </para>
/// <para>
/// Failed renders (Python unavailable, script error) are remembered for the session so they
/// are not retried every frame, and are never persisted, so the next launch retries them.
/// </para>
/// </remarks>
public sealed class GdsPreviewRenderService
{
    /// <summary>Lower bound on bitmap dimensions to avoid zero-size bitmaps.</summary>
    internal const int MinBitmapPixels = 16;

    private readonly NazcaComponentPreviewService _previewService;

    /// <summary>Renders gdsfactory-native components (cspdk etc.); null falls back to no preview.
    /// Typed as the base so it can be mocked in tests; DI injects the
    /// <see cref="GdsFactoryComponentPreviewService"/> instance.</summary>
    private readonly NazcaComponentPreviewService? _gdsFactoryPreviewService;

    private readonly GdsPreviewCache _cache = new();

    /// <summary>Persistent on-disk cache for resolution-independent geometry.</summary>
    private readonly GdsPreviewDiskCache _diskCache;

    /// <summary>Throttles concurrent Python renders so the library can't spawn a flood.</summary>
    private readonly SemaphoreSlim _renderGate = new(3, 3);

    /// <summary>In-memory LRU of geometry keyed by <see cref="GdsPreviewKey.Hash"/>.</summary>
    private readonly GdsGeometryCache _memGeometry = new();

    /// <summary>Tracks in-flight fetches (geometry and canvas previews) by cache key.</summary>
    private readonly ConcurrentDictionary<string, Task> _pending = new();

    /// <summary>
    /// Geometry keys whose render failed, remembered for the whole session. Kept outside the
    /// LRU geometry cache on purpose: a large GDS import can carry more unique (failing) keys
    /// than the LRU holds, and evicting a failure marker would re-spawn a doomed Python render
    /// for that key forever. Not persisted, so the next launch retries a transient failure.
    /// </summary>
    private readonly ConcurrentDictionary<string, byte> _failedGeometryKeys = new();

    /// <summary>
    /// Raised on the UI thread whenever a previously-pending preview finishes
    /// loading.  Subscribe with <c>+= canvas.InvalidateVisual</c> from
    /// <see cref="CAP.Avalonia.Controls.DesignCanvas"/> (and from thumbnails) to
    /// trigger a repaint.
    /// </summary>
    public event Action? OnPreviewLoaded;

    /// <summary>
    /// Resolves the inline Nazca code of a component whose geometry its PDK template carries
    /// as code (module, function → code), or null for a component the PDK module renders.
    /// Called on the UI thread when a preview is requested. Unset, every component renders
    /// through its PDK module.
    /// </summary>
    public Func<string?, string?, string?>? InlineNazcaCodeLookup { get; set; }

    /// <summary>
    /// Initializes the service with the shared Nazca preview back-end and a
    /// default disk cache.
    /// </summary>
    public GdsPreviewRenderService(
        NazcaComponentPreviewService previewService,
        NazcaComponentPreviewService? gdsFactoryPreviewService = null)
        : this(previewService, new GdsPreviewDiskCache(), gdsFactoryPreviewService)
    {
    }

    /// <summary>
    /// Initializes the service with the shared Nazca preview back-end, an explicit disk cache
    /// (used by tests to redirect cache files), and an optional gdsfactory preview back-end
    /// for gdsfactory-native components (#570).
    /// </summary>
    public GdsPreviewRenderService(
        NazcaComponentPreviewService previewService, GdsPreviewDiskCache diskCache,
        NazcaComponentPreviewService? gdsFactoryPreviewService = null)
    {
        _previewService = previewService ?? throw new ArgumentNullException(nameof(previewService));
        _diskCache = diskCache ?? throw new ArgumentNullException(nameof(diskCache));
        _gdsFactoryPreviewService = gdsFactoryPreviewService;
    }

    /// <summary>
    /// Returns the canvas preview for a placed component, or <c>null</c> while its geometry
    /// is still being fetched or when none is available (unknown Nazca function, Python
    /// unavailable, empty polygon list).
    /// </summary>
    /// <remarks>
    /// The geometry comes from the same key-based cache as the library thumbnails
    /// (<see cref="TryGetGeometry"/>: memory, then disk, then Python), so a component whose
    /// template already has a library preview is drawn without a second render, and previews
    /// survive app restarts. Only the size-dependent bitmap is cached per footprint here.
    /// </remarks>
    /// <param name="component">The placed component (top-level or a group child).</param>
    public GdsPreviewData? TryGetPreview(Component component)
    {
        var key = GdsPreviewKey.ForComponent(component);
        var geometry = TryGetGeometry(key);
        if (geometry == null || geometry.Polygons.Count == 0)
            return null;

        var (width, height) = GdsPolygonRenderer.GetUnrotatedSize(
            component.RotationDegrees, component.WidthMicrometers, component.HeightMicrometers);
        var previewKey = BuildPreviewKey(key, width, height);
        if (_cache.TryGet(previewKey, out var cached) && cached != null)
            return cached;

        var data = new GdsPreviewData(geometry, width, height);
        _cache.Set(previewKey, data);
        ScheduleRasterization(previewKey, data);
        return data;
    }

    /// <summary>
    /// Builds the bitmap-cache key: the render identity plus the UNROTATED footprint. The
    /// cached bitmap holds unrotated geometry, so keying on the live (rotation-swapped) size
    /// would rasterise again on every rotation and with a distorted aspect ratio.
    /// </summary>
    internal static string BuildPreviewKey(GdsPreviewKey key, double unrotatedWidth, double unrotatedHeight) =>
        string.Create(CultureInfo.InvariantCulture, $"{key.Hash()}|{unrotatedWidth:F2}|{unrotatedHeight:F2}");

    /// <summary>
    /// Rasterises the preview outside the render pass; until the bitmap is ready the canvas
    /// draws the polygons directly from <see cref="GdsPreviewData.Result"/>.
    /// </summary>
    private void ScheduleRasterization(string previewKey, GdsPreviewData data)
    {
        int bitmapW = Math.Max(MinBitmapPixels, (int)Math.Ceiling(data.WidthMicrometers));
        int bitmapH = Math.Max(MinBitmapPixels, (int)Math.Ceiling(data.HeightMicrometers));
        try
        {
            Dispatcher.UIThread.Post(() =>
            {
                var bitmap = GdsPolygonRenderer.RasterizeToBitmap(data.Result, bitmapW, bitmapH);
                if (bitmap == null) return;
                _cache.Set(previewKey, data with { Bitmap = bitmap });
                OnPreviewLoaded?.Invoke();
            });
        }
        catch { /* no dispatcher in headless tests: the polygon fallback still draws */ }
    }

    /// <summary>
    /// Renders a gdsfactory-native component's geometry via the gdsfactory preview back-end,
    /// or a failure result when no service is wired / the function is not module-qualified (#570).
    /// </summary>
    private async Task<NazcaPreviewResult> RenderGdsFactoryAsync(string? gdsFactoryFunction)
    {
        var code = GdsFactoryPreviewCode.For(gdsFactoryFunction);
        if (code == null || _gdsFactoryPreviewService == null)
            return NazcaPreviewResult.Fail("No gdsfactory preview available for this component.");
        return await _gdsFactoryPreviewService.RenderRawCodeAsync(code);
    }

    /// <summary>
    /// Returns the cached preview geometry for a render identity, or null while a
    /// background fetch is pending / when no geometry is available. Lookup chain:
    /// in-memory LRU -> disk cache -> Python render (throttled).
    /// </summary>
    public NazcaPreviewResult? TryGetGeometry(GdsPreviewKey key)
    {
        if (!key.IsRenderable) return null;
        var cacheKey = key.Hash();
        if (_memGeometry.TryGet(cacheKey, out var cached)) return cached;
        if (_failedGeometryKeys.ContainsKey(cacheKey)) return null;
        // Reserve the slot BEFORE starting the fetch (mirrors the canvas TryGetPreview
        // path) so a duplicate fetch is never launched for the same key. Passing the
        // started task straight into TryAdd would run the task before TryAdd decides
        // to keep it, defeating the _pending dedup under concurrent callers.
        if (_pending.TryAdd(cacheKey, Task.CompletedTask))
            _pending[cacheKey] = FetchGeometryAsync(key, cacheKey, InlineNazcaCodeLookup?.Invoke(key.Module, key.Function));
        return null;
    }

    /// <summary>Test hook: awaits all in-flight geometry and canvas preview fetches.</summary>
    public Task WaitForPendingAsync() => Task.WhenAll(_pending.Values.ToArray());

    private async Task FetchGeometryAsync(GdsPreviewKey key, string cacheKey, string? inlineCode)
    {
        try
        {
            // Inline code is not part of the disk-cache key, so its renders are never persisted:
            // an edited template must not keep showing the old geometry.
            if (inlineCode == null && _diskCache.TryRead(key, out var disk))
            {
                _memGeometry.Set(cacheKey, disk);
                RaisePreviewLoaded();
                return;
            }
            await _renderGate.WaitAsync();
            NazcaPreviewResult result;
            try
            {
                result = inlineCode != null
                    ? await _previewService.RenderRawCodeAsync(inlineCode)
                    : string.IsNullOrWhiteSpace(key.Function)
                        ? await RenderGdsFactoryAsync(key.GdsFactoryFunction)
                        : await _previewService.RenderAsync(key.Module, key.Function!, key.Parameters);
            }
            finally { _renderGate.Release(); }

            if (result.Success && result.Polygons.Count > 0)
            {
                if (inlineCode == null) _diskCache.Write(key, result);
                _memGeometry.Set(cacheKey, result);
            }
            else if (result.Success)
            {
                // A genuinely empty render (0 polygons) — persist the empty marker so we don't
                // keep re-rendering a component that has no geometry.
                if (inlineCode == null) _diskCache.WriteEmpty(key);
                _memGeometry.Set(cacheKey, null);
            }
            else
            {
                // The render FAILED (Python/env/script error — e.g. cspdk not yet installed, a
                // broken or half-provisioned interpreter). Do NOT persist: a transient env failure
                // must not poison the disk cache permanently, or the component stays blank forever
                // even after the env is fixed. Remember null for this session only (like the catch
                // block below), so the next launch retries.
                _failedGeometryKeys.TryAdd(cacheKey, 0);
            }
            RaisePreviewLoaded();
        }
        catch
        {
            // Transient failure (e.g. Python hiccup): remember "empty" for this session
            // only — deliberately NOT WriteEmpty, so a restart can retry. A genuinely
            // empty render (above) persists the empty marker; a crash does not.
            _failedGeometryKeys.TryAdd(cacheKey, 0);
        }
        finally
        {
            _pending.TryRemove(cacheKey, out _);
        }
    }

    /// <summary>
    /// Raises <see cref="OnPreviewLoaded"/> on the UI thread. Safe in headless tests:
    /// when there are no subscribers the dispatcher is never touched.
    /// </summary>
    private void RaisePreviewLoaded()
    {
        var handler = OnPreviewLoaded;
        if (handler == null) return;
        try { Dispatcher.UIThread.Post(() => handler()); }
        catch { /* no dispatcher in headless tests */ }
    }
}
