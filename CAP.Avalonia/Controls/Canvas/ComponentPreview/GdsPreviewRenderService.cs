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
/// Failed renders are remembered until interpreter activation or restart, avoiding retries
/// every frame. Failures are never persisted to disk.
/// </para>
/// </remarks>
public sealed partial class GdsPreviewRenderService : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
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
    private readonly GdsGeometryCache _memGeometry;
    private readonly OversizedPreviewHandoff _oversizedGeometry = new();

    /// <summary>Tracks in-flight fetches (geometry and canvas previews) by cache key.</summary>
    private readonly ConcurrentDictionary<string, Task> _pending = new();

    /// <summary>
    /// Geometry keys whose render failed, remembered until interpreter activation. Kept outside the
    /// LRU geometry cache on purpose: a large GDS import can carry more unique (failing) keys
    /// than the LRU holds, and evicting a failure marker would re-spawn a doomed Python render
    /// for that key forever. Not persisted, so the next launch retries a transient failure.
    /// </summary>
    private readonly ConcurrentDictionary<string, int> _failedGeometryKeys = new();
    private int _environmentGeneration;
    private readonly object _environmentGate = new();
    private string? _environmentIdentity;
    private readonly string _memoryNamespace = Guid.NewGuid().ToString("N");

    /// <summary>Retries all previews after interpreter activation, including successful old geometry.</summary>
    public void RetryFailedPreviews()
    {
        lock (_environmentGate)
        {
            _diskCache.InvalidateEnvironment();
            InvalidateMemory();
        }
        PublishPreviewFailure(null, Volatile.Read(ref _environmentGeneration));
        RaisePreviewLoaded();
    }

    /// <summary>Invalidates loaded PDK/code revisions without changing the persistent activation token.</summary>
    public void InvalidatePdkPreviews()
    {
        lock (_environmentGate) InvalidateMemory();
        RaisePreviewLoaded();
    }

    private void InvalidateMemory()
    {
        _environmentGeneration++;
        _environmentIdentity = null;
        _previewService.InvalidateCache();
        _gdsFactoryPreviewService?.InvalidateCache();
        _memGeometry.Clear();
        _oversizedGeometry.Clear();
        _cache.Clear();
        _failedGeometryKeys.Clear();
        _layerRolesByFunction.Clear();
    }

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
    public Func<string?, string?, string?>? InlineNazcaCodeLookup
    {
        get => _inlineNazcaCodeLookup;
        set
        {
            lock (_environmentGate)
            {
                if (ReferenceEquals(_inlineNazcaCodeLookup, value)) return;
                _inlineNazcaCodeLookup = value;
                InvalidateMemory();
            }
        }
    }

    private Func<string?, string?, string?>? _inlineNazcaCodeLookup;

    /// <summary>
    /// Resolves a placed component's core/metal preview layer roles from its fabrication
    /// process (issue #1494). Called on the UI thread when a canvas preview is requested.
    /// Unset (or returning null), every component falls back to the demo-PDK roles
    /// (<see cref="GdsPreviewLayerRoles.Default"/>).
    /// </summary>
    public Func<Component, GdsPreviewLayerRoles?>? LayerRolesLookup
    {
        get => _layerRolesLookup;
        set { _layerRolesLookup = value; _layerRolesByFunction.Clear(); }
    }

    private Func<Component, GdsPreviewLayerRoles?>? _layerRolesLookup;

    // TryGetPreview runs per component per frame and the process lookup scans the library,
    // so resolve once per (gdsfactory, nazca) function pair — the inputs the lookup keys on.
    private readonly Dictionary<(string?, string?), GdsPreviewLayerRoles> _layerRolesByFunction = new();

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
        _memGeometry = new GdsGeometryCache(previewService.GeometryCache);
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
        lock (_environmentGate)
        {
            var key = GdsPreviewKey.ForComponent(component);
            var geometry = TryGetGeometry(key);
            if (geometry == null || geometry.Polygons.Count == 0)
                return null;

            var (width, height) = GdsPolygonRenderer.GetUnrotatedSize(
                component.RotationDegrees, component.WidthMicrometers, component.HeightMicrometers);
            var roles = ResolveLayerRoles(component);
            var previewKey = BuildPreviewKey(key, width, height, roles);
            if (_cache.TryGet(previewKey, out var cached) && cached != null && ReferenceEquals(cached.Result, geometry))
                return cached;

            var data = new GdsPreviewData(geometry, width, height) { LayerRoles = roles };
            _cache.Set(previewKey, data);
            ScheduleRasterization(previewKey, data);
            return data;
        }
    }

    private GdsPreviewLayerRoles ResolveLayerRoles(Component component)
    {
        if (_layerRolesLookup == null)
            return GdsPreviewLayerRoles.Default;
        var functionKey = (component.GdsFactoryFunction, component.NazcaFunctionName);
        if (!_layerRolesByFunction.TryGetValue(functionKey, out var roles))
            _layerRolesByFunction[functionKey] = roles = _layerRolesLookup(component) ?? GdsPreviewLayerRoles.Default;
        return roles;
    }

    /// <summary>
    /// Builds the bitmap-cache key: the render identity plus the UNROTATED footprint. The
    /// cached bitmap holds unrotated geometry, so keying on the live (rotation-swapped) size
    /// would rasterise again on every rotation and with a distorted aspect ratio. The layer
    /// roles are part of the key: the same geometry under two processes must not share one
    /// (wrongly coloured) rasterised bitmap (issue #1494).
    /// </summary>
    internal static string BuildPreviewKey(GdsPreviewKey key, double unrotatedWidth, double unrotatedHeight,
        GdsPreviewLayerRoles? roles = null) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{key.Hash()}|{unrotatedWidth:F2}|{unrotatedHeight:F2}|{(roles ?? GdsPreviewLayerRoles.Default).Signature}");

    /// <summary>
    /// Rasterises the preview outside the render pass; until the bitmap is ready the canvas
    /// draws the polygons directly from <see cref="GdsPreviewData.Result"/>.
    /// </summary>
    private void ScheduleRasterization(string previewKey, GdsPreviewData data)
    {
        var generation = Volatile.Read(ref _environmentGeneration);
        var (bitmapW, bitmapH) = GdsPolygonRenderer.GetBitmapSize(data.WidthMicrometers, data.HeightMicrometers);
        try
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (generation != Volatile.Read(ref _environmentGeneration)) return;
                var bitmap = GdsPolygonRenderer.RasterizeToBitmap(data.Result, bitmapW, bitmapH, data.LayerRoles);
                if (bitmap == null) return;
                lock (_environmentGate)
                {
                    if (generation != _environmentGeneration) { bitmap.Dispose(); return; }
                    _cache.Set(previewKey, data with { Bitmap = bitmap });
                }
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
        var renderKey = key.Hash();
        lock (_environmentGate)
        {
            // Frame lookups use only memory. Resolve interpreter/file revisions on background fetches.
            var cacheKey = "geometry|" + _memoryNamespace + "|" + _environmentGeneration + "|" + renderKey;
            if (_memGeometry.TryGet(cacheKey, out var cached)) return cached;
            if (_oversizedGeometry.TryGet(cacheKey, out var oversized)) return oversized;
            if (_failedGeometryKeys.TryGetValue(cacheKey, out var failedGeneration)
                && failedGeneration == _environmentGeneration) return null;
            if (_pending.ContainsKey(cacheKey)) return null;
            // Template saves explicitly invalidate this generation. Scan the library only
            // for a real miss; the backend hashes inline code only when actually rendered.
            var inlineCode = InlineNazcaCodeLookup?.Invoke(key.Module, key.Function);
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var generation = _environmentGeneration;
            if (_pending.TryAdd(cacheKey, completion.Task))
                _ = Task.Run(() => FetchGeometryAsync(key, cacheKey, inlineCode, generation, completion));
        }
        return null;
    }

    /// <summary>Awaits currently pending preview fetches.</summary>
    public Task WaitForPendingAsync() => Task.WhenAll(_pending.Values.ToArray());

    private async Task FetchGeometryAsync(GdsPreviewKey key, string cacheKey, string? inlineCode,
        int generation, TaskCompletionSource completion)
    {
        try
        {
            // Bound disk parsing and Python together. Neither runs on the UI thread or under its gate.
            await _renderGate.WaitAsync();
            try
            {
                if (generation != Volatile.Read(ref _environmentGeneration)) return;
                var environment = CurrentEnvironmentIdentity();
                lock (_environmentGate)
                {
                    if (generation != _environmentGeneration) return;
                    if (_environmentIdentity != null && _environmentIdentity != environment)
                    {
                        InvalidateMemory();
                        return;
                    }
                    _environmentIdentity = environment;
                }
                if (inlineCode == null && _diskCache.TryRead(key, out var disk, environment))
                {
                    lock (_environmentGate)
                    {
                        if (generation != _environmentGeneration) return;
                        StoreGeometry(cacheKey, disk);
                    }
                    disk = null;
                    RaisePreviewLoaded();
                    return;
                }
                if (generation != Volatile.Read(ref _environmentGeneration)) return;
                NazcaPreviewResult? result = inlineCode != null
                    ? await _previewService.RenderRawCodeAsync(inlineCode)
                    : string.IsNullOrWhiteSpace(key.Function)
                        ? await RenderGdsFactoryAsync(key.GdsFactoryFunction)
                        : await _previewService.RenderAsync(key.Module, key.Function!, key.Parameters);
                var completedEnvironment = CurrentEnvironmentIdentity();
                lock (_environmentGate)
                {
                    if (generation != _environmentGeneration) return;
                    if (environment != completedEnvironment)
                    {
                        InvalidateMemory();
                        return;
                    }
                }
                // An immutable environment tag makes even a racing obsolete disk write unusable
                // by the new environment. Serialization and I/O never hold the UI state gate.
                if (result.Success && inlineCode == null &&
                    PreviewGeometryCache.EstimatePayloadBytes(cacheKey, result) <= PreviewGeometryCache.MaxPayloadBytes)
                {
                    if (result.Polygons.Count > 0) _diskCache.Write(key, result, environment);
                    else _diskCache.WriteEmpty(key, environment);
                }
                lock (_environmentGate)
                {
                    if (generation != _environmentGeneration) return;
                    if (result.Success) StoreGeometry(cacheKey, result.Polygons.Count > 0 ? result : null);
                    else
                    {
                        _failedGeometryKeys[cacheKey] = generation;
                        PublishPreviewFailure(result.Error ?? "Preview render failed.", generation);
                    }
                }
                // The weak handoff must not be kept alive by this completed async state machine.
                result = null;
                RaisePreviewLoaded();
            }
            finally { _renderGate.Release(); }
        }
        catch (Exception ex)
        {
            lock (_environmentGate)
            {
                if (generation == _environmentGeneration)
                {
                    _failedGeometryKeys[cacheKey] = generation;
                    PublishPreviewFailure(ex.Message, generation);
                }
            }
        }
        finally
        {
            _pending.TryRemove(cacheKey, out _);
            completion.TrySetResult();
            if (generation != Volatile.Read(ref _environmentGeneration)) RaisePreviewLoaded();
        }
    }

    private void StoreGeometry(string key, NazcaPreviewResult? result)
    {
        if (result != null && PreviewGeometryCache.EstimatePayloadBytes(key, result) > PreviewGeometryCache.MaxPayloadBytes)
            _oversizedGeometry.Set(key, result);
        else
            _memGeometry.Set(key, result);
    }

    private string CurrentEnvironmentIdentity() => PreviewEnvironmentIdentity.Hash(
        _previewService.EnvironmentIdentity + "|" + _gdsFactoryPreviewService?.EnvironmentIdentity +
        "|" + _diskCache.ActivationIdentity);

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
