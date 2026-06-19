# GDS-Preview in Library-Thumbnails — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Die Component-Library-Thumbnails zeigen die echte gerenderte GDS-Geometrie + Pins (wie auf dem Canvas) statt nur der Schema-Box — lazy gerendert, in-memory **und** auf Disk gecacht, mit Fallback auf die Schema-Box.

**Architecture:** Wiederverwendung der bestehenden Pipeline (`NazcaComponentPreviewService` → `GdsPreviewRenderService` → `GdsPolygonRenderer`). Neu: ein persistenter Disk-Cache für `NazcaPreviewResult` (auflösungsunabhängige Polygon-Daten als JSON), eine generalisierte Render-Identität (`module|function|parameters`) statt `ComponentViewModel`-Kopplung, eine Concurrency-Drossel, und die `ComponentPreview`-Control zeichnet die Geometrie vektoriell + Pins.

**Tech Stack:** C# / .NET 10, Avalonia 11.2.1, xUnit + Shouldly, System.Text.Json.

**Spec:** `docs/superpowers/specs/2026-06-19-gds-preview-library-thumbnails-design.md`

---

## File Structure

| Datei | Verantwortung | Aktion |
|---|---|---|
| `CAP.Avalonia/Controls/Canvas/ComponentPreview/GdsPreviewKey.cs` | Render-Identität (module/function/parameters) + stabiler Hash | Create |
| `CAP.Avalonia/Controls/Canvas/ComponentPreview/NazcaPreviewResultDto.cs` | JSON-DTO + Mapping `NazcaPreviewResult` ↔ DTO | Create |
| `CAP.Avalonia/Controls/Canvas/ComponentPreview/GdsPreviewDiskCache.cs` | Persistenter Disk-Cache (cross-platform LocalAppData) | Create |
| `CAP.Avalonia/Controls/Canvas/ComponentPreview/GdsPreviewRenderService.cs` | Lookup-Kette in-memory→disk→python, Drossel, generalisierter Key | Modify |
| `CAP.Avalonia/Controls/ComponentPreview.cs` | Thumbnail zeichnet GDS-Geometrie + Pins, async Fetch, Fallback | Modify |
| `CAP.Avalonia/Views/MainWindow.axaml` | AttachedProperty (Service) an die Thumbnails binden | Modify |
| `UnitTests/Canvas/ComponentPreview/GdsPreviewKeyTests.cs` | Key-Stabilität/Unabhängigkeit | Create |
| `UnitTests/Canvas/ComponentPreview/NazcaPreviewResultDtoTests.cs` | Serialisierungs-Roundtrip | Create |
| `UnitTests/Canvas/ComponentPreview/GdsPreviewDiskCacheTests.cs` | Disk-Cache Roundtrip/Versionierung/Toleranz | Create |
| `UnitTests/Canvas/ComponentPreview/GdsPreviewRenderServiceTests.cs` | Lookup-Pfad / Drossel (mit gemocktem Preview-Service) | Create/Modify |

**Hinweis Rebase:** Dieser Branch ist von `main`. Falls PR #587 (Left-Panel) zuerst merged, vor Implementierung `git fetch origin main && git rebase origin/main` — Konfliktstelle nur `MainWindow.axaml` (Thumbnail-Block, Task 6).

---

## Task 1: Render-Identität `GdsPreviewKey`

Eine wertbasierte Render-Identität, die Canvas und Library teilen. Der Hash ist der Disk-Cache-Dateiname (auflösungsunabhängig: ohne Breite/Höhe).

**Files:**
- Create: `CAP.Avalonia/Controls/Canvas/ComponentPreview/GdsPreviewKey.cs`
- Test: `UnitTests/Canvas/ComponentPreview/GdsPreviewKeyTests.cs`

- [ ] **Step 1: Failing test schreiben**

```csharp
// UnitTests/Canvas/ComponentPreview/GdsPreviewKeyTests.cs
using CAP.Avalonia.Controls.Canvas.ComponentPreview;
using Shouldly;
using Xunit;

namespace UnitTests.Canvas.ComponentPreview;

public class GdsPreviewKeyTests
{
    [Fact]
    public void Hash_IsStableForSameInputs()
    {
        var a = new GdsPreviewKey("siepic", "ebeam_dc", "Lc=5");
        var b = new GdsPreviewKey("siepic", "ebeam_dc", "Lc=5");
        a.Hash().ShouldBe(b.Hash());
    }

    [Fact]
    public void Hash_DiffersWhenFunctionOrParamsDiffer()
    {
        var baseKey = new GdsPreviewKey("siepic", "ebeam_dc", "Lc=5");
        baseKey.Hash().ShouldNotBe(new GdsPreviewKey("siepic", "ebeam_dc", "Lc=9").Hash());
        baseKey.Hash().ShouldNotBe(new GdsPreviewKey("siepic", "ebeam_gc", "Lc=5").Hash());
    }

    [Fact]
    public void Hash_IncludesFormatVersion_SoBumpInvalidates()
    {
        // The hash must change if the format version constant changes.
        var key = new GdsPreviewKey("m", "f", null);
        key.Hash().ShouldStartWith($"v{GdsPreviewKey.FormatVersion}-");
    }

    [Fact]
    public void IsRenderable_FalseWhenFunctionMissing()
    {
        new GdsPreviewKey("m", "", "p").IsRenderable.ShouldBeFalse();
        new GdsPreviewKey("m", "   ", "p").IsRenderable.ShouldBeFalse();
        new GdsPreviewKey("m", "f", "p").IsRenderable.ShouldBeTrue();
    }
}
```

- [ ] **Step 2: Test ausführen (muss fehlschlagen)**

Run: `$env:PYTHONUTF8='1'; py "$env:USERPROFILE\.cap-tools\smart_test.py" GdsPreviewKey`
Expected: FAIL (Typ `GdsPreviewKey` existiert nicht).

- [ ] **Step 3: `GdsPreviewKey` implementieren**

```csharp
// CAP.Avalonia/Controls/Canvas/ComponentPreview/GdsPreviewKey.cs
using System.Security.Cryptography;
using System.Text;

namespace CAP.Avalonia.Controls.Canvas.ComponentPreview;

/// <summary>
/// Resolution-independent render identity for a GDS preview: the geometry depends
/// only on the Nazca module/function/parameters, not on the display size. Used as
/// both the in-memory and on-disk cache key.
/// </summary>
public readonly record struct GdsPreviewKey(string? Module, string? Function, string? Parameters)
{
    /// <summary>Bump to invalidate every cached entry (format or render-semantics change).</summary>
    public const int FormatVersion = 1;

    /// <summary>True when there is a function to render (built-in components have none).</summary>
    public bool IsRenderable => !string.IsNullOrWhiteSpace(Function);

    /// <summary>Stable filesystem-safe hash, prefixed with the format version.</summary>
    public string Hash()
    {
        var material = $"{Module}{Function}{Parameters}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        var hex = Convert.ToHexString(bytes, 0, 12).ToLowerInvariant(); // 24 hex chars
        return $"v{FormatVersion}-{hex}";
    }
}
```

- [ ] **Step 4: Test ausführen (muss bestehen)**

Run: `$env:PYTHONUTF8='1'; py "$env:USERPROFILE\.cap-tools\smart_test.py" GdsPreviewKey`
Expected: PASS (4 Tests).

- [ ] **Step 5: Commit**

```bash
git add CAP.Avalonia/Controls/Canvas/ComponentPreview/GdsPreviewKey.cs UnitTests/Canvas/ComponentPreview/GdsPreviewKeyTests.cs
git commit -m "(+) GdsPreviewKey: resolution-independent render identity for preview caching"
```

---

## Task 2: Serialisierung `NazcaPreviewResultDto`

`NazcaPreviewResult` (Core) liegt in `CAP_Core.Export` und enthält `IReadOnlyList<(double X,double Y)>` (Tuples serialisieren mit System.Text.Json nicht stabil). Ein schlankes DTO entkoppelt das Disk-Format vom Core-Typ.

**Files:**
- Create: `CAP.Avalonia/Controls/Canvas/ComponentPreview/NazcaPreviewResultDto.cs`
- Test: `UnitTests/Canvas/ComponentPreview/NazcaPreviewResultDtoTests.cs`

Relevante Core-Typen (`Connect-A-Pic-Core/Export/NazcaComponentPreviewService.cs`):
- `NazcaPreviewResult { bool Success; double XMin/YMin/XMax/YMax; IReadOnlyList<NazcaPreviewPolygon> Polygons; ... }`
- `NazcaPreviewPolygon { int Layer; IReadOnlyList<(double X, double Y)> Vertices; }`

- [ ] **Step 1: Failing test schreiben**

```csharp
// UnitTests/Canvas/ComponentPreview/NazcaPreviewResultDtoTests.cs
using System.Collections.Generic;
using CAP.Avalonia.Controls.Canvas.ComponentPreview;
using CAP_Core.Export;
using Shouldly;
using Xunit;

namespace UnitTests.Canvas.ComponentPreview;

public class NazcaPreviewResultDtoTests
{
    [Fact]
    public void Roundtrip_PreservesPolygonsAndBbox()
    {
        var original = new NazcaPreviewResult
        {
            Success = true,
            XMin = -1, YMin = -2, XMax = 3, YMax = 4,
            Polygons = new List<NazcaPreviewPolygon>
            {
                new() { Layer = 1, Vertices = new List<(double, double)> { (0, 0), (1, 0), (1, 1) } },
                new() { Layer = 7, Vertices = new List<(double, double)> { (2, 2), (3, 3) } },
            }
        };

        var json = NazcaPreviewResultDto.Serialize(original);
        var restored = NazcaPreviewResultDto.Deserialize(json);

        restored.ShouldNotBeNull();
        restored!.XMin.ShouldBe(-1); restored.YMax.ShouldBe(4);
        restored.Polygons.Count.ShouldBe(2);
        restored.Polygons[0].Layer.ShouldBe(1);
        restored.Polygons[0].Vertices.Count.ShouldBe(3);
        restored.Polygons[0].Vertices[2].ShouldBe((1.0, 1.0));
        restored.Polygons[1].Layer.ShouldBe(7);
    }

    [Fact]
    public void Deserialize_GarbageReturnsNull()
    {
        NazcaPreviewResultDto.Deserialize("{ not valid json").ShouldBeNull();
        NazcaPreviewResultDto.Deserialize("").ShouldBeNull();
    }
}
```

- [ ] **Step 2: Test ausführen (muss fehlschlagen)**

Run: `$env:PYTHONUTF8='1'; py "$env:USERPROFILE\.cap-tools\smart_test.py" NazcaPreviewResultDto`
Expected: FAIL (Typ existiert nicht).

- [ ] **Step 3: DTO + Mapping implementieren**

```csharp
// CAP.Avalonia/Controls/Canvas/ComponentPreview/NazcaPreviewResultDto.cs
using System.Text.Json;
using CAP_Core.Export;

namespace CAP.Avalonia.Controls.Canvas.ComponentPreview;

/// <summary>
/// JSON-serialisable mirror of <see cref="NazcaPreviewResult"/> for the disk cache.
/// Decouples the on-disk format from the Core type (tuples don't serialise stably).
/// Only the geometry needed for thumbnail rendering is persisted (polygons + bbox).
/// </summary>
public sealed class NazcaPreviewResultDto
{
    public double XMin { get; set; }
    public double YMin { get; set; }
    public double XMax { get; set; }
    public double YMax { get; set; }
    public List<PolygonDto> Polygons { get; set; } = new();

    public sealed class PolygonDto
    {
        public int Layer { get; set; }
        public List<double> Xs { get; set; } = new();
        public List<double> Ys { get; set; } = new();
    }

    private static readonly JsonSerializerOptions Options = new() { IncludeFields = false };

    /// <summary>Serialises the geometry of a successful preview result to JSON.</summary>
    public static string Serialize(NazcaPreviewResult result)
    {
        var dto = new NazcaPreviewResultDto
        {
            XMin = result.XMin, YMin = result.YMin, XMax = result.XMax, YMax = result.YMax,
            Polygons = result.Polygons.Select(p => new PolygonDto
            {
                Layer = p.Layer,
                Xs = p.Vertices.Select(v => v.X).ToList(),
                Ys = p.Vertices.Select(v => v.Y).ToList(),
            }).ToList()
        };
        return JsonSerializer.Serialize(dto, Options);
    }

    /// <summary>Parses JSON back to a <see cref="NazcaPreviewResult"/>; returns null on any error.</summary>
    public static NazcaPreviewResult? Deserialize(string json)
    {
        try
        {
            var dto = JsonSerializer.Deserialize<NazcaPreviewResultDto>(json, Options);
            if (dto == null) return null;
            return new NazcaPreviewResult
            {
                Success = true,
                XMin = dto.XMin, YMin = dto.YMin, XMax = dto.XMax, YMax = dto.YMax,
                Polygons = dto.Polygons.Select(p => new NazcaPreviewPolygon
                {
                    Layer = p.Layer,
                    Vertices = ZipVertices(p.Xs, p.Ys),
                }).ToList()
            };
        }
        catch { return null; }
    }

    private static IReadOnlyList<(double X, double Y)> ZipVertices(List<double> xs, List<double> ys)
    {
        int n = Math.Min(xs.Count, ys.Count);
        var list = new List<(double, double)>(n);
        for (int i = 0; i < n; i++) list.Add((xs[i], ys[i]));
        return list;
    }
}
```

- [ ] **Step 4: Test ausführen (muss bestehen)**

Run: `$env:PYTHONUTF8='1'; py "$env:USERPROFILE\.cap-tools\smart_test.py" NazcaPreviewResultDto`
Expected: PASS (2 Tests).

- [ ] **Step 5: Commit**

```bash
git add CAP.Avalonia/Controls/Canvas/ComponentPreview/NazcaPreviewResultDto.cs UnitTests/Canvas/ComponentPreview/NazcaPreviewResultDtoTests.cs
git commit -m "(+) NazcaPreviewResultDto: JSON (de)serialisation for the GDS preview disk cache"
```

---

## Task 3: `GdsPreviewDiskCache`

Persistenter, cross-platform Disk-Cache. Verzeichnis ist injizierbar (Tests nutzen ein Temp-Verzeichnis; Produktion nutzt LocalAppData).

**Files:**
- Create: `CAP.Avalonia/Controls/Canvas/ComponentPreview/GdsPreviewDiskCache.cs`
- Test: `UnitTests/Canvas/ComponentPreview/GdsPreviewDiskCacheTests.cs`

- [ ] **Step 1: Failing test schreiben**

```csharp
// UnitTests/Canvas/ComponentPreview/GdsPreviewDiskCacheTests.cs
using System.Collections.Generic;
using System.IO;
using CAP.Avalonia.Controls.Canvas.ComponentPreview;
using CAP_Core.Export;
using Shouldly;
using Xunit;

namespace UnitTests.Canvas.ComponentPreview;

public class GdsPreviewDiskCacheTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lunima-gdscache-test-" + Guid.NewGuid().ToString("N"));

    private NazcaPreviewResult SampleResult() => new()
    {
        Success = true, XMin = 0, YMin = 0, XMax = 10, YMax = 5,
        Polygons = new List<NazcaPreviewPolygon>
        {
            new() { Layer = 1, Vertices = new List<(double, double)> { (0, 0), (10, 0), (10, 5) } }
        }
    };

    [Fact]
    public void WriteThenRead_ReturnsHit_WithSamePolygons()
    {
        var cache = new GdsPreviewDiskCache(_dir);
        var key = new GdsPreviewKey("m", "f", "p");

        cache.Write(key, SampleResult());
        cache.TryRead(key, out var result).ShouldBeTrue();

        result.ShouldNotBeNull();
        result!.Polygons.Count.ShouldBe(1);
        result.Polygons[0].Vertices.Count.ShouldBe(3);
    }

    [Fact]
    public void WriteEmpty_ReadReturnsHitWithNull_NoRetry()
    {
        var cache = new GdsPreviewDiskCache(_dir);
        var key = new GdsPreviewKey("m", "f", "p");

        cache.WriteEmpty(key);
        cache.TryRead(key, out var result).ShouldBeTrue(); // present...
        result.ShouldBeNull();                              // ...but empty
    }

    [Fact]
    public void TryRead_MissReturnsFalse()
    {
        var cache = new GdsPreviewDiskCache(_dir);
        cache.TryRead(new GdsPreviewKey("x", "y", "z"), out _).ShouldBeFalse();
    }

    [Fact]
    public void TryRead_CorruptFileTreatedAsMiss()
    {
        var cache = new GdsPreviewDiskCache(_dir);
        var key = new GdsPreviewKey("m", "f", "p");
        cache.Write(key, SampleResult());
        File.WriteAllText(Path.Combine(_dir, key.Hash() + ".json"), "{ corrupt");

        cache.TryRead(key, out _).ShouldBeFalse();
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
    }
}
```

- [ ] **Step 2: Test ausführen (muss fehlschlagen)**

Run: `$env:PYTHONUTF8='1'; py "$env:USERPROFILE\.cap-tools\smart_test.py" GdsPreviewDiskCache`
Expected: FAIL (Typ existiert nicht).

- [ ] **Step 3: `GdsPreviewDiskCache` implementieren**

```csharp
// CAP.Avalonia/Controls/Canvas/ComponentPreview/GdsPreviewDiskCache.cs
using CAP_Core.Export;

namespace CAP.Avalonia.Controls.Canvas.ComponentPreview;

/// <summary>
/// Persistent, cross-platform on-disk cache for GDS preview geometry.
/// One JSON file per <see cref="GdsPreviewKey"/> hash. A present-but-empty marker
/// records "render produced nothing" so no retry happens across sessions.
/// All I/O is best-effort: any failure is treated as a miss and never throws.
/// </summary>
public sealed class GdsPreviewDiskCache
{
    private const string EmptyMarker = "__EMPTY__";
    private readonly string _directory;

    /// <summary>Production ctor: uses the platform-neutral local app-data location.</summary>
    public GdsPreviewDiskCache()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Lunima", "gds-preview-cache"))
    {
    }

    /// <summary>Testable ctor: cache files live under <paramref name="directory"/>.</summary>
    public GdsPreviewDiskCache(string directory) => _directory = directory;

    private string PathFor(GdsPreviewKey key) => Path.Combine(_directory, key.Hash() + ".json");

    /// <summary>
    /// Reads a cached entry. Returns true when the key is present on disk (even when
    /// the stored value is the empty marker, in which case <paramref name="result"/>
    /// is null). Returns false on miss or any read/parse error (corrupt file).
    /// </summary>
    public bool TryRead(GdsPreviewKey key, out NazcaPreviewResult? result)
    {
        result = null;
        try
        {
            var path = PathFor(key);
            if (!File.Exists(path)) return false;
            var json = File.ReadAllText(path);
            if (json == EmptyMarker) return true;   // present, empty
            result = NazcaPreviewResultDto.Deserialize(json);
            return result != null;                   // corrupt parse → miss
        }
        catch { return false; }
    }

    /// <summary>Persists a successful render result.</summary>
    public void Write(GdsPreviewKey key, NazcaPreviewResult result)
    {
        try
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllText(PathFor(key), NazcaPreviewResultDto.Serialize(result));
        }
        catch { /* best-effort */ }
    }

    /// <summary>Records that a render produced nothing (no retry next session).</summary>
    public void WriteEmpty(GdsPreviewKey key)
    {
        try
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllText(PathFor(key), EmptyMarker);
        }
        catch { /* best-effort */ }
    }
}
```

- [ ] **Step 4: Test ausführen (muss bestehen)**

Run: `$env:PYTHONUTF8='1'; py "$env:USERPROFILE\.cap-tools\smart_test.py" GdsPreviewDiskCache`
Expected: PASS (4 Tests).

- [ ] **Step 5: Commit**

```bash
git add CAP.Avalonia/Controls/Canvas/ComponentPreview/GdsPreviewDiskCache.cs UnitTests/Canvas/ComponentPreview/GdsPreviewDiskCacheTests.cs
git commit -m "(+) GdsPreviewDiskCache: cross-platform persistent cache for GDS preview geometry"
```

---

## Task 4: `GdsPreviewRenderService` generalisieren + Disk-Cache + Drossel

Den bestehenden Service erweitern (siehe aktuelle Datei): eine key-basierte Überladung, Disk-Cache in der Lookup-Kette, und ein `SemaphoreSlim` als Concurrency-Drossel. Die bestehende `TryGetPreview(ComponentViewModel)`-API + das Canvas-Bitmap-Verhalten bleiben erhalten.

**Files:**
- Modify: `CAP.Avalonia/Controls/Canvas/ComponentPreview/GdsPreviewRenderService.cs`
- Test: `UnitTests/Canvas/ComponentPreview/GdsPreviewRenderServiceTests.cs`

Bestehende relevante Member: `TryGetPreview(ComponentViewModel)`, `BuildCacheKey(ComponentViewModel)`, `FetchAndCacheAsync(string, ComponentViewModel)`, `GdsPreviewCache _cache`, `ConcurrentDictionary _pendingFetches`, `Action? OnPreviewLoaded`, ctor `(NazcaComponentPreviewService)`. `NazcaComponentPreviewService.RenderAsync` ist `virtual` → mockbar mit Moq.

- [ ] **Step 1: Failing test schreiben** (Lookup-Pfad mit gemocktem Preview-Service)

```csharp
// UnitTests/Canvas/ComponentPreview/GdsPreviewRenderServiceTests.cs
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CAP.Avalonia.Controls.Canvas.ComponentPreview;
using CAP_Core.Export;
using Moq;
using Shouldly;
using Xunit;

namespace UnitTests.Canvas.ComponentPreview;

public class GdsPreviewRenderServiceTests
{
    private static NazcaPreviewResult Ok() => new()
    {
        Success = true, XMin = 0, YMin = 0, XMax = 4, YMax = 2,
        Polygons = new List<NazcaPreviewPolygon>
        {
            new() { Layer = 1, Vertices = new List<(double, double)> { (0, 0), (4, 0), (4, 2) } }
        }
    };

    [Fact]
    public async Task GetGeometry_RendersOnce_ThenServesFromMemory()
    {
        var mock = new Mock<NazcaComponentPreviewService>("python", "script.py") { CallBase = false };
        mock.Setup(s => s.RenderAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ok());

        var diskDir = Path.Combine(Path.GetTempPath(), "lunima-svc-" + Guid.NewGuid().ToString("N"));
        var svc = new GdsPreviewRenderService(mock.Object, new GdsPreviewDiskCache(diskDir));
        var key = new GdsPreviewKey("m", "f", "p");

        // First call: miss → triggers async render, returns null immediately.
        svc.TryGetGeometry(key).ShouldBeNull();
        await svc.WaitForPendingAsync();          // test hook: await in-flight fetches

        // Now cached in memory.
        svc.TryGetGeometry(key).ShouldNotBeNull();
        mock.Verify(s => s.RenderAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);

        try { Directory.Delete(diskDir, true); } catch { }
    }

    [Fact]
    public async Task GetGeometry_SecondInstance_ServesFromDisk_NoRender()
    {
        var diskDir = Path.Combine(Path.GetTempPath(), "lunima-svc-" + Guid.NewGuid().ToString("N"));
        var key = new GdsPreviewKey("m", "f", "p");

        var mock1 = new Mock<NazcaComponentPreviewService>("python", "script.py");
        mock1.Setup(s => s.RenderAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ok());
        var svc1 = new GdsPreviewRenderService(mock1.Object, new GdsPreviewDiskCache(diskDir));
        svc1.TryGetGeometry(key);
        await svc1.WaitForPendingAsync();   // populates disk

        // Fresh service (new in-memory cache), same disk dir, render must NOT be called.
        var mock2 = new Mock<NazcaComponentPreviewService>("python", "script.py");
        var svc2 = new GdsPreviewRenderService(mock2.Object, new GdsPreviewDiskCache(diskDir));
        svc2.TryGetGeometry(key);
        await svc2.WaitForPendingAsync();
        svc2.TryGetGeometry(key).ShouldNotBeNull();
        mock2.Verify(s => s.RenderAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);

        try { Directory.Delete(diskDir, true); } catch { }
    }
}
```

- [ ] **Step 2: Test ausführen (muss fehlschlagen)**

Run: `$env:PYTHONUTF8='1'; py "$env:USERPROFILE\.cap-tools\smart_test.py" GdsPreviewRenderService`
Expected: FAIL (`TryGetGeometry` / `WaitForPendingAsync` / 2-arg ctor existieren nicht).

- [ ] **Step 3: Service erweitern** — folgende Member hinzufügen/anpassen (bestehende `TryGetPreview(ComponentViewModel)`/Canvas-Pfad NICHT entfernen):

```csharp
// Zusätzliche Felder
private readonly GdsPreviewDiskCache _diskCache;
private readonly SemaphoreSlim _renderGate = new(3, 3);            // max 3 parallele Python-Renders
private readonly ConcurrentDictionary<string, Task> _pending = new();

// Zusätzlicher ctor (bestehenden ctor beibehalten; dieser ist die volle Form)
public GdsPreviewRenderService(NazcaComponentPreviewService previewService, GdsPreviewDiskCache diskCache)
{
    _previewService = previewService ?? throw new ArgumentNullException(nameof(previewService));
    _diskCache = diskCache ?? throw new ArgumentNullException(nameof(diskCache));
}

// Bestehenden 1-arg ctor auf den neuen delegieren lassen:
public GdsPreviewRenderService(NazcaComponentPreviewService previewService)
    : this(previewService, new GdsPreviewDiskCache()) { }

/// <summary>
/// Returns the cached preview geometry for a render identity, or null while a
/// background fetch is pending / when no geometry is available. Lookup chain:
/// in-memory LRU → disk cache → Python render (throttled).
/// </summary>
public NazcaPreviewResult? TryGetGeometry(GdsPreviewKey key)
{
    if (!key.IsRenderable) return null;
    var cacheKey = key.Hash();

    if (_memGeometry.TryGet(cacheKey, out var cached))
        return cached;

    if (_pending.TryAdd(cacheKey, FetchGeometryAsync(key, cacheKey)))
    { /* started */ }

    return null;
}

/// <summary>Test hook: awaits all in-flight geometry fetches.</summary>
public Task WaitForPendingAsync() => Task.WhenAll(_pending.Values.ToArray());

private async Task FetchGeometryAsync(GdsPreviewKey key, string cacheKey)
{
    try
    {
        // 1) disk
        if (_diskCache.TryRead(key, out var disk))
        {
            _memGeometry.Set(cacheKey, disk);
            await Dispatcher.UIThread.InvokeAsync(() => OnPreviewLoaded?.Invoke());
            return;
        }

        // 2) python (throttled)
        await _renderGate.WaitAsync();
        NazcaPreviewResult result;
        try { result = await _previewService.RenderAsync(key.Module, key.Function!, key.Parameters); }
        finally { _renderGate.Release(); }

        if (result.Success && result.Polygons.Count > 0)
        {
            _diskCache.Write(key, result);
            _memGeometry.Set(cacheKey, result);
        }
        else
        {
            _diskCache.WriteEmpty(key);
            _memGeometry.Set(cacheKey, null);
        }
        await Dispatcher.UIThread.InvokeAsync(() => OnPreviewLoaded?.Invoke());
    }
    catch
    {
        _memGeometry.Set(cacheKey, null);
    }
    finally
    {
        _pending.TryRemove(cacheKey, out _);
    }
}
```

Zusätzlich ein zweiter In-Memory-LRU-Cache für die rohe Geometrie (analog zu `_cache`, aber Wert = `NazcaPreviewResult?`):

```csharp
private readonly GdsGeometryCache _memGeometry = new();
```

Dafür eine schlanke Cache-Klasse (gleiches LRU-Muster wie `GdsPreviewCache`, Wert `NazcaPreviewResult?`) anlegen — `CAP.Avalonia/Controls/Canvas/ComponentPreview/GdsGeometryCache.cs`. (Alternativ `GdsPreviewCache` generisch machen; der einfachste, risikoärmste Weg ist eine separate Klasse, kopiertes LRU-Muster mit `NazcaPreviewResult?`-Wert.)

> Hinweis: `Dispatcher.UIThread.InvokeAsync` in Tests ohne laufende Avalonia-App — die Tests rufen `WaitForPendingAsync` ab; falls `Dispatcher` im Headless-Test hängt, im Test-Setup `OnPreviewLoaded` ungesetzt lassen und in `FetchGeometryAsync` den Dispatcher-Aufruf via `if (OnPreviewLoaded != null)` schützen, oder den Invoke in einen `try/catch` setzen. Verifizieren, dass die Tests grün laufen; falls Dispatcher-Probleme auftreten, den `OnPreviewLoaded`-Invoke nur ausführen, wenn ein Handler registriert ist.

- [ ] **Step 4: Test ausführen (muss bestehen)**

Run: `$env:PYTHONUTF8='1'; py "$env:USERPROFILE\.cap-tools\smart_test.py" GdsPreviewRenderService`
Expected: PASS (2 Tests). Auch Canvas-Tests gegenchecken: `... smart_test.py GdsPreview` → alle grün.

- [ ] **Step 5: DI-Wiring prüfen** — `App.axaml.cs:236` konstruiert `new GdsPreviewRenderService(sp.GetRequiredService<NazcaComponentPreviewService>())`. Das nutzt den delegierenden 1-arg ctor (Produktions-Disk-Cache) → keine Änderung nötig. Verifizieren, dass der Build grün ist.

- [ ] **Step 6: Commit**

```bash
git add CAP.Avalonia/Controls/Canvas/ComponentPreview/GdsPreviewRenderService.cs CAP.Avalonia/Controls/Canvas/ComponentPreview/GdsGeometryCache.cs UnitTests/Canvas/ComponentPreview/GdsPreviewRenderServiceTests.cs
git commit -m "(+) GdsPreviewRenderService: key-based geometry lookup with disk cache + render throttle"
```

---

## Task 5: `ComponentPreview`-Control zeichnet GDS-Geometrie + Pins

Die Library-Thumbnail-Control um eine AttachedProperty (Service) erweitern und in `Render()` die Geometrie vektoriell zeichnen, darüber die bestehenden Pin-Marker; Fallback auf die Schema-Box.

**Files:**
- Modify: `CAP.Avalonia/Controls/ComponentPreview.cs`

Bestehende Properties: `WidthMicrometers`, `HeightMicrometers`, `PinDefinitions` (`PinDefinition { OffsetX, OffsetY, AngleDegrees }`). Es müssen `NazcaModuleName` + `NazcaFunctionName` als neue StyledProperties ergänzt werden (Parameter bleiben leer → Default-Geometrie; siehe Plan-Kopf).

- [ ] **Step 1: StyledProperties + Service-AttachedProperty ergänzen**

```csharp
// In ComponentPreview.cs ergänzen:
public static readonly StyledProperty<string?> NazcaModuleNameProperty =
    AvaloniaProperty.Register<ComponentPreview, string?>(nameof(NazcaModuleName));
public static readonly StyledProperty<string?> NazcaFunctionNameProperty =
    AvaloniaProperty.Register<ComponentPreview, string?>(nameof(NazcaFunctionName));

public string? NazcaModuleName { get => GetValue(NazcaModuleNameProperty); set => SetValue(NazcaModuleNameProperty, value); }
public string? NazcaFunctionName { get => GetValue(NazcaFunctionNameProperty); set => SetValue(NazcaFunctionNameProperty, value); }

/// <summary>Attached service, set once on the window so all thumbnails share it.</summary>
public static readonly AttachedProperty<GdsPreviewRenderService?> RenderServiceProperty =
    AvaloniaProperty.RegisterAttached<ComponentPreview, Control, GdsPreviewRenderService?>("RenderService", inherits: true);
public static void SetRenderService(Control c, GdsPreviewRenderService? v) => c.SetValue(RenderServiceProperty, v);
public static GdsPreviewRenderService? GetRenderService(Control c) => c.GetValue(RenderServiceProperty);
```

`AffectsRender` um die zwei neuen Properties erweitern. Beim ersten Render `OnPreviewLoaded` an `InvalidateVisual` koppeln ist global (Canvas nutzt es auch) — stattdessen pro Control: in `Render()` bei pendingem Fetch ein einmaliges `InvalidateVisual` über einen `DispatcherTimer`-Poll **vermeiden**; einfacher: Da der Service-`OnPreviewLoaded` bereits `InvalidateVisual` des Canvas auslöst, für die Library einen leichten Mechanismus nutzen: Control registriert sich beim Service. **Einfachster robuster Weg:** Control fragt in `Render()` ab; wenn null (pending), nach Abschluss kommt kein automatisches Repaint. Daher: Service-`OnPreviewLoaded` ist ein `Action`-Multicast — die Control hängt in `OnAttachedToVisualTree` ein eigenes Handler an (`InvalidateVisual`) und entfernt es in `OnDetachedFromVisualTree`.

- [ ] **Step 2: Attach/Detach-Handler + Render() implementieren**

```csharp
private GdsPreviewRenderService? _service;

protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
{
    base.OnAttachedToVisualTree(e);
    _service = GetRenderService(this);
    if (_service != null) _service.OnPreviewLoaded += OnPreviewReady;
}

protected override void OnDetachedFromVisualTree(VisualTreeDetachmentEventArgs e)
{
    if (_service != null) _service.OnPreviewLoaded -= OnPreviewReady;
    base.OnDetachedFromVisualTree(e);
}

private void OnPreviewReady() => Dispatcher.UIThread.Post(InvalidateVisual);
```

> **Voraussetzung:** `GdsPreviewRenderService.OnPreviewLoaded` muss von `Action?` (single) auf ein **Event** (`event Action? OnPreviewLoaded`) umgestellt werden, damit mehrere Thumbnails + der Canvas gleichzeitig hören. In Task 4 daher `public Action? OnPreviewLoaded { get; set; }` ersetzen durch `public event Action? OnPreviewLoaded;` und im Canvas (`DesignCanvas.cs:283`) `newVm.GdsPreviewRenderService.OnPreviewLoaded += InvalidateVisual;` bzw. `-= InvalidateVisual` statt Zuweisung (Datei `DesignCanvas.cs:278`/`:283` anpassen). Diese Änderung in Task 4 mit aufnehmen.

`Render()` erweitern: vor der bestehenden Box-Zeichnung versuchen, Geometrie zu holen:

```csharp
public override void Render(DrawingContext context)
{
    var bounds = Bounds;
    if (bounds.Width <= 0 || bounds.Height <= 0) return;
    double compW = WidthMicrometers, compH = HeightMicrometers;
    if (compW <= 0 || compH <= 0) return;

    double pad = 4;
    double availW = bounds.Width - pad * 2, availH = bounds.Height - pad * 2;
    double scale = Math.Min(availW / compW, availH / compH);
    double drawW = compW * scale, drawH = compH * scale;
    double offsetX = pad + (availW - drawW) / 2, offsetY = pad + (availH - drawH) / 2;

    var geometry = _service?.TryGetGeometry(new GdsPreviewKey(NazcaModuleName, NazcaFunctionName, null));

    if (geometry != null && geometry.Polygons.Count > 0)
        DrawGdsGeometry(context, geometry, offsetX, offsetY, drawW, drawH);   // vektoriell
    else
        DrawSchematicBox(context, offsetX, offsetY, drawW, drawH);            // bestehender Box-Pfad

    DrawPins(context, offsetX, offsetY, scale);   // bestehende Pin-Zeichnung, ausgelagert
}
```

`DrawGdsGeometry` rasterisiert/zeichnet die Polygone in das `drawW×drawH`-Rechteck (Y-Flip + bbox-Transform analog zu `GdsPolygonRenderer.DrawPolygonsAsGeometry` / `TransformVertex`; die Farb-/Layer-Logik aus `GdsPolygonRenderer` wiederverwenden — ggf. die privaten Helfer dort auf `internal` heben und teilen, statt zu duplizieren). `DrawSchematicBox` + `DrawPins` sind der bestehende `Render()`-Code, in Methoden ausgelagert.

- [ ] **Step 3: Build**

Run: `dotnet build CAP.Avalonia/CAP.Avalonia.csproj -clp:ErrorsOnly`
Expected: 0 Fehler.

- [ ] **Step 4: Commit**

```bash
git add CAP.Avalonia/Controls/ComponentPreview.cs CAP.Avalonia/Controls/Canvas/ComponentPreview/GdsPolygonRenderer.cs
git commit -m "(+) ComponentPreview: draw real GDS geometry + pins in library thumbnails, box fallback"
```

---

## Task 6: Wiring in `MainWindow.axaml`

Den Service an die Thumbnails binden + die neuen Nazca-Properties setzen.

**Files:**
- Modify: `CAP.Avalonia/Views/MainWindow.axaml` (Thumbnail-Block, aktuell ~`:505`; nach Rebase auf #587 ggf. verschoben)

- [ ] **Step 1: ComponentPreview-Verwendung erweitern**

```xml
<controls:ComponentPreview
    Width="56" Height="36"
    WidthMicrometers="{Binding WidthMicrometers}"
    HeightMicrometers="{Binding HeightMicrometers}"
    PinDefinitions="{Binding PinDefinitions}"
    NazcaModuleName="{Binding NazcaModuleName}"
    NazcaFunctionName="{Binding NazcaFunctionName}"
    controls:ComponentPreview.RenderService="{Binding $parent[Window].((vm:MainViewModel)DataContext).GdsPreviewRenderService}"
    Margin="0,0,6,0"/>
```

> Da `RenderServiceProperty` `inherits: true` ist, genügt es alternativ, die AttachedProperty **einmal** weiter oben am Library-Container zu setzen statt an jedem Thumbnail. Bevorzugt: einmal am `ListBox`/Container der Component-Library-Liste setzen, um Wiederholung zu vermeiden. Prüfen, dass `MainViewModel.GdsPreviewRenderService` öffentlich erreichbar ist (ist als DI-Singleton registriert; ggf. als Property auf `MainViewModel` exponieren, falls nicht vorhanden).

- [ ] **Step 2: Build + manueller Smoke-Test**

Run: `dotnet build CAP.Desktop/CAP.Desktop.csproj -clp:ErrorsOnly` → 0 Fehler.
Dann App starten (`dotnet run --project CAP.Desktop`) und visuell prüfen:
- Library-Thumbnails zeigen nach kurzer Ladezeit die echte Geometrie + Pins.
- Komponenten ohne Nazca-Funktion / ohne Python: Schema-Box-Fallback.
- Zweiter App-Start: Thumbnails sofort da (Disk-Cache).
- Scrollen durch viele Komponenten ruckelt nicht (Drossel + lazy).

- [ ] **Step 3: Commit**

```bash
git add CAP.Avalonia/Views/MainWindow.axaml CAP.Avalonia/ViewModels/MainViewModel.cs
git commit -m "(+) Wire GDS preview render service into component-library thumbnails"
```

---

## Task 7: Voller Testlauf + PR

- [ ] **Step 1: Komplette Suite**

Run: `$env:PYTHONUTF8='1'; py "$env:USERPROFILE\.cap-tools\smart_test.py"`
Expected: alle grün (0 failed).

- [ ] **Step 2: PR erstellen** gegen `main` (Titel: `(+) GDS geometry preview in component-library thumbnails (+ persistent disk cache)`), Spec + Plan verlinken.

---

## Self-Review-Notizen (vom Plan-Autor)

- **Spec-Abdeckung:** Disk-Cache (T3) ✓, Service-Generalisierung + Drossel (T4) ✓, vektorielles GDS+Pins-Rendering mit Fallback (T5) ✓, Serialisierung (T2) ✓, auflösungsunabhängiger Key (T1) ✓, Lazy via ListBox-Virtualisierung (T5/T6) ✓, cross-platform Pfad (T3) ✓.
- **Bewusste Vereinfachung ggü. Spec:** Library nutzt leere Nazca-Parameter (Default-Geometrie), da `ComponentTemplate` keine Parameter trägt; parametrisierte Library-Previews = Follow-up (Template-Property + PDK-Befüllung).
- **Event-Umstellung:** `OnPreviewLoaded` wird von `Action?`-Property auf `event Action?` umgestellt (T4), Canvas-Aufrufer (`DesignCanvas.cs`) entsprechend auf `+=`/`-=` angepasst — sonst können mehrere Thumbnails nicht gleichzeitig hören.
- **Risiko Dispatcher in Tests:** In T4 abgesichert (Invoke nur bei registriertem Handler / im try/catch).
