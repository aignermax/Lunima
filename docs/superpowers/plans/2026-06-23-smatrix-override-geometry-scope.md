# S-Matrix-Override nach Geometrie-Identität (+ GC) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Einen per FDTD neuberechneten (oder importierten) S-Matrix-Override an die **Geometrie-Identität** des Bauteils binden (statt an `component.Identifier`), sodass alle deckungsgleichen Instanzen + Kopien ihn erben; verwaiste Overrides beim Speichern per Refcount aufräumen.

**Architecture:** Neuer Geometrie-Identitäts-Schlüssel (`module|function|parameters`, bzw. Raw-Code-Hash bei #561-Override). `SMatrixOverrideApplicator.ApplyAll` matcht zusätzlich über diesen Schlüssel (Identifier-Match bleibt als erster Lookup → Abwärtskompatibilität). FDTD-Recompute/Load schreiben unter dem Geometrie-Schlüssel. `SaveDesign` persistiert nur Overrides mit refcount > 0.

**Tech Stack:** C# / .NET 10, Avalonia 11.2.1, xUnit + Shouldly + Moq.

**Spec:** `docs/superpowers/specs/2026-06-23-smatrix-override-geometry-scope-design.md`

---

## File Structure

| Datei | Verantwortung | Aktion |
|---|---|---|
| `CAP.Avalonia/Services/ComponentGeometryKey.cs` | Geometrie-Identität eines Bauteils (raw-code-hash oder module\|function\|parameters) | Create |
| `CAP.Avalonia/Services/SMatrixOverrideApplicator.cs` | `ApplyAll` matcht zusätzlich per Geometrie-Schlüssel | Modify |
| `CAP.Avalonia/ViewModels/ComponentSettings/ComponentSettingsDialogViewModel(.Fdtd).cs` | S-Matrix-Override unter Geometrie-Schlüssel statt Identifier | Modify |
| `CAP.Avalonia/Views/MainWindow.axaml.cs` | Geometrie-Schlüssel berechnen + an Dialog übergeben | Modify |
| `CAP.Avalonia/ViewModels/Panels/FileOperationsViewModel.cs` | Resolver auf Geometrie-Identität + Save-Sweep-GC | Modify |
| `UnitTests/Services/ComponentGeometryKeyTests.cs` | Identitäts-Bildung | Create |
| `UnitTests/ComponentSettings/SMatrixOverrideApplicatorTests.cs` | Geometrie-Match (erben/nicht erben) | Modify |
| `UnitTests/Import/SMatrixOverrideGcTests.cs` | Save-Sweep | Create |

**Branch:** `feat/smatrix-override-geometry-scope` (bereits angelegt; enthält Spec + Repro-Test `ComponentCloneOrphanPinTests.Clone_PreservesOverriddenSMatrixValue_NotJustStructure`).

---

## Task 1: `ComponentGeometryKey`

Geometrie-Identität eines Bauteils. Raw-Code-Override (#561) → Hash des Codes; sonst `module|function|parameters`. Konzeptionell dieselbe Identität wie `GdsPreviewKey` (Thumbnails), hier feature-neutral für den S-Matrix-Scope.

**Files:**
- Create: `CAP.Avalonia/Services/ComponentGeometryKey.cs`
- Test:   `UnitTests/Services/ComponentGeometryKeyTests.cs`

Relevant: `CAP_Core.Components.Core.Component` hat `string NazcaModuleName`, `string NazcaFunctionName`, `string NazcaFunctionParameters`. Der Raw-Code wird über einen Lookup `Func<Component,string?>` (Identifier→RawCode, später aus `StoredNazcaOverrides`) geliefert.

- [ ] **Step 1: Failing test schreiben**

```csharp
// UnitTests/Services/ComponentGeometryKeyTests.cs
using CAP.Avalonia.Services;
using CAP_Core.Components.Core;
using Shouldly;
using UnitTests.Helpers;
using Xunit;

namespace UnitTests.Services;

public class ComponentGeometryKeyTests
{
    private static Component Wg(string module, string function, string parameters)
    {
        var c = TestComponentFactory.CreateStraightWaveGuide();
        c.NazcaModuleName = module;
        c.NazcaFunctionName = function;
        c.NazcaFunctionParameters = parameters;
        return c;
    }

    [Fact]
    public void SameModuleFunctionParameters_SameKey()
    {
        var a = ComponentGeometryKey.For(Wg("siepic", "ebeam_dc", "Lc=5"), _ => null);
        var b = ComponentGeometryKey.For(Wg("siepic", "ebeam_dc", "Lc=5"), _ => null);
        a.ShouldBe(b);
    }

    [Fact]
    public void DifferentParameters_DifferentKey()
    {
        var a = ComponentGeometryKey.For(Wg("siepic", "ebeam_dc", "Lc=5"), _ => null);
        var b = ComponentGeometryKey.For(Wg("siepic", "ebeam_dc", "Lc=9"), _ => null);
        a.ShouldNotBe(b);
    }

    [Fact]
    public void RawCodeOverride_UsesCodeHash_IndependentOfFunction()
    {
        var comp = Wg("siepic", "ebeam_dc", "Lc=5");
        var withOverride = ComponentGeometryKey.For(comp, _ => "import nazca; def component(): ...");
        var noOverride = ComponentGeometryKey.For(comp, _ => null);
        withOverride.ShouldNotBe(noOverride);
        // same raw code → same key regardless of the underlying function name
        var other = Wg("other", "different_fn", "");
        ComponentGeometryKey.For(other, _ => "import nazca; def component(): ...")
            .ShouldBe(withOverride);
    }

    [Fact]
    public void Prefixed_RawVsGeo_NeverCollide()
    {
        ComponentGeometryKey.For(Wg("m", "f", "p"), _ => null).ShouldStartWith("geo:");
        ComponentGeometryKey.For(Wg("m", "f", "p"), _ => "x").ShouldStartWith("raw:");
    }
}
```

- [ ] **Step 2: Test ausführen (muss fehlschlagen)**

Run: `$env:PYTHONUTF8='1'; py "$env:USERPROFILE\.cap-tools\smart_test.py" ComponentGeometryKey`
Expected: FAIL (Typ existiert nicht).

- [ ] **Step 3: Implementieren**

```csharp
// CAP.Avalonia/Services/ComponentGeometryKey.cs
using System;
using System.Security.Cryptography;
using System.Text;
using CAP_Core.Components.Core;

namespace CAP.Avalonia.Services;

/// <summary>
/// Geometry identity of a component, used to scope S-matrix overrides: components with
/// the same geometry must share the same (recomputed) S-matrix. When a raw-code Nazca
/// override (#561) is active the code itself defines the geometry; otherwise the Nazca
/// call (module|function|parameters) does. Same identity ⇒ same key.
/// </summary>
public static class ComponentGeometryKey
{
    /// <summary>Bump to invalidate all geometry-scoped override keys.</summary>
    public const int FormatVersion = 1;

    /// <summary>
    /// Builds the geometry key. <paramref name="rawCodeLookup"/> returns the active raw-code
    /// override for the component (e.g. from StoredNazcaOverrides), or null if none.
    /// </summary>
    public static string For(Component component, Func<Component, string?> rawCodeLookup)
    {
        var raw = rawCodeLookup(component);
        if (!string.IsNullOrWhiteSpace(raw))
            return $"raw:v{FormatVersion}-{Hash(raw)}";

        var material = $"{component.NazcaModuleName}{component.NazcaFunctionName}{component.NazcaFunctionParameters}";
        return $"geo:v{FormatVersion}-{Hash(material)}";
    }

    private static string Hash(string s)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(s));
        return Convert.ToHexString(bytes, 0, 12).ToLowerInvariant();
    }
}
```

- [ ] **Step 4: Test ausführen (muss bestehen)**

Run: `$env:PYTHONUTF8='1'; py "$env:USERPROFILE\.cap-tools\smart_test.py" ComponentGeometryKey`
Expected: PASS (4 Tests).

- [ ] **Step 5: Commit**

```bash
git add CAP.Avalonia/Services/ComponentGeometryKey.cs UnitTests/Services/ComponentGeometryKeyTests.cs
git commit -m "(+) ComponentGeometryKey: geometry identity for S-matrix override scoping"
```
(Commit-Body endet mit: `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`)

---

## Task 2: `ApplyAll` matcht per Geometrie-Schlüssel

Heute matcht `ApplyAll` per `comp.Identifier`, dann optional `templateKeyResolver`. Wir erweitern die Match-Kette: `Identifier` → `geometryKeyResolver(comp)` → (bestehender `templateKeyResolver`). So erben deckungsgleiche Instanzen einen unter dem Geometrie-Schlüssel gespeicherten Override; Identifier-Altbestände bleiben matchbar.

**Files:**
- Modify: `CAP.Avalonia/Services/SMatrixOverrideApplicator.cs` (Methode `ApplyAll`, aktuelle Signatur: `ApplyAll(components, storedSMatrices, templateKeyResolver=null, errorConsole=null, keyMatchesKnownTemplate=null, reportOrphans=false)`; die Match-Schleife resolved `comp.Identifier` zuerst, dann `templateKeyResolver`)
- Test: `UnitTests/ComponentSettings/SMatrixOverrideApplicatorTests.cs`

- [ ] **Step 1: Failing test schreiben** (anhängen an die bestehende Testklasse)

```csharp
    [Fact]
    public void ApplyAll_GeometryKeyResolver_AppliesToAllMatchingInstances()
    {
        // Two distinct instances (different Identifiers) with the SAME geometry key.
        var a = TestComponentFactory.CreateSimpleTwoPortComponent(); a.Identifier = "inst_A";
        var b = TestComponentFactory.CreateSimpleTwoPortComponent(); b.Identifier = "inst_B";

        var store = new Dictionary<string, ComponentSMatrixData> { ["geo:v1-shared"] = MakeData("1550", 2) };

        var result = SMatrixOverrideApplicator.ApplyAll(
            new[] { a, b }, store,
            geometryKeyResolver: _ => "geo:v1-shared");

        result.PerComponent["inst_A"].Applied.ShouldBe(1);
        result.PerComponent["inst_B"].Applied.ShouldBe(1);   // the copy inherits it too
        result.OrphanKeys.ShouldBeEmpty();
    }

    [Fact]
    public void ApplyAll_GeometryKeyResolver_DifferentGeometryDoesNotInherit()
    {
        var a = TestComponentFactory.CreateSimpleTwoPortComponent(); a.Identifier = "inst_A";
        var b = TestComponentFactory.CreateSimpleTwoPortComponent(); b.Identifier = "inst_B";
        var store = new Dictionary<string, ComponentSMatrixData> { ["geo:v1-A"] = MakeData("1550", 2) };

        var result = SMatrixOverrideApplicator.ApplyAll(
            new[] { a, b }, store,
            geometryKeyResolver: c => c.Identifier == "inst_A" ? "geo:v1-A" : "geo:v1-B");

        result.PerComponent.ContainsKey("inst_A").ShouldBeTrue();
        result.PerComponent.ContainsKey("inst_B").ShouldBeFalse();   // different geometry → no override
    }
```

- [ ] **Step 2: Test ausführen (muss fehlschlagen)**

Run: `$env:PYTHONUTF8='1'; py "$env:USERPROFILE\.cap-tools\smart_test.py" SMatrixOverrideApplicator`
Expected: FAIL (`geometryKeyResolver`-Parameter existiert nicht).

- [ ] **Step 3: `ApplyAll` erweitern**

Signatur um einen optionalen Parameter ergänzen (NACH `templateKeyResolver`, vor `errorConsole`, damit benannte Argumente bestehender Aufrufer gültig bleiben):

```csharp
public static ApplyAllResult ApplyAll(
    IEnumerable<Component> components,
    IReadOnlyDictionary<string, ComponentSMatrixData> storedSMatrices,
    Func<Component, string?>? templateKeyResolver = null,
    Func<Component, string?>? geometryKeyResolver = null,
    ErrorConsoleService? errorConsole = null,
    Func<string, bool>? keyMatchesKnownTemplate = null,
    bool reportOrphans = false)
```

In der Match-Schleife (heute: erst `storedSMatrices.TryGetValue(comp.Identifier)`, dann `templateKeyResolver`) den Geometrie-Resolver als zusätzlichen Lookup einfügen — Reihenfolge: **Identifier → geometryKey → templateKey**:

```csharp
string? resolvedKey = null;
if (storedSMatrices.TryGetValue(comp.Identifier, out var data))
    resolvedKey = comp.Identifier;
else
{
    var geomKey = geometryKeyResolver?.Invoke(comp);
    if (geomKey != null && storedSMatrices.TryGetValue(geomKey, out data))
        resolvedKey = geomKey;
    else
    {
        var templateKey = templateKeyResolver?.Invoke(comp);
        if (templateKey != null && storedSMatrices.TryGetValue(templateKey, out data))
            resolvedKey = templateKey;
    }
}
if (resolvedKey != null && data != null)
{
    perComponent[comp.Identifier] = Apply(comp, data, errorConsole);
    matchedKeys.Add(resolvedKey);
}
```

- [ ] **Step 4: Test ausführen (muss bestehen)**

Run: `$env:PYTHONUTF8='1'; py "$env:USERPROFILE\.cap-tools\smart_test.py" SMatrixOverrideApplicator`
Expected: PASS (alle bestehenden + 2 neue).

- [ ] **Step 5: Commit**

```bash
git add CAP.Avalonia/Services/SMatrixOverrideApplicator.cs UnitTests/ComponentSettings/SMatrixOverrideApplicatorTests.cs
git commit -m "(+) SMatrixOverrideApplicator: match overrides by geometry key (inherit across identical instances)"
```

---

## Task 3: Override unter Geometrie-Schlüssel schreiben + verdrahten

Der S-Matrix-Override soll unter dem Geometrie-Schlüssel landen (FDTD-Recompute + Load-from-file). Der Settings-Dialog nutzt heute `_entityKey` (= `component.Identifier`) für `_storedSMatrices` UND der Nazca-Override nutzt denselben Identifier. Wir führen einen separaten `_smatrixKey` ein; der Nazca-Teil behält den Identifier.

**Files:**
- Modify: `CAP.Avalonia/ViewModels/ComponentSettings/ComponentSettingsDialogViewModel.cs` (Feld `_entityKey`; `Configure(...)` setzt `_entityKey`; alle `_storedSMatrices[_entityKey]` / `TryGetValue(_entityKey)` Stellen — diese auf ein neues Feld `_smatrixKey` umstellen) und `...Fdtd.cs` (`_storedSMatrices[_entityKey] = data` → `_smatrixKey`)
- Modify: `CAP.Avalonia/Views/MainWindow.axaml.cs` (`ShowComponentSettingsDialog`/`Configure`-Aufruf: `smatrixKey` berechnen und übergeben)

Vorgehen (der ausführende Subagent liest die genauen Stellen):
1. Feld `private string _smatrixKey = string.Empty;` ergänzen; `Configure(...)` um Parameter `string smatrixKey` erweitern und `_smatrixKey = smatrixKey;` setzen. Alle Stellen, die `_storedSMatrices` per `_entityKey` indizieren (Recompute, Load, effective-S-matrix-Anzeige, Delete), auf `_smatrixKey` umstellen. Der **Nazca-Override-Teil** (`NazcaCodeEditor`, `storedNazcaOverrides`) bleibt auf `_entityKey` (Identifier).
2. In `MainWindow.axaml.cs` den `smatrixKey` bilden:
   - Im Per-Instance-Pfad (liveComponent != null): `smatrixKey = ComponentGeometryKey.For(liveComponent, c => vm.FileOperations.StoredNazcaOverrides.TryGetValue(c.Identifier, out var o) ? o.RawCode : null)`.
   - Im Template-Pfad (liveComponent == null, Library-Kontextmenü): `smatrixKey = entityKey` (der bestehende `{PdkSource}::{Name}`-Key bleibt für den user-global Scope).
   und an `Configure(...)` übergeben.

- [ ] **Step 1: Build + bestehende ComponentSettings-Tests grün halten**

Run: `dotnet build CAP.Avalonia/CAP.Avalonia.csproj -clp:ErrorsOnly` → 0 Fehler.
Run: `$env:PYTHONUTF8='1'; py "$env:USERPROFILE\.cap-tools\smart_test.py" ComponentSettings`
Expected: PASS (bestehende Dialog-Tests; ggf. Configure-Aufrufe in Tests um `smatrixKey:` ergänzen — Default = der jeweils genutzte Key).

- [ ] **Step 2: Regressionstest „Kopie erbt FDTD-Override"** (Integration, ohne Docker)

```csharp
// In UnitTests/ComponentSettings/SMatrixOverrideApplicatorTests.cs anhängen:
[Fact]
public void CopyScenario_OverrideStoredByGeometry_AppliesToClone()
{
    // Original A and its clone B share geometry → both must receive the override
    // that a recompute stored under the geometry key.
    var a = TestComponentFactory.CreateSimpleTwoPortComponent(); a.Identifier = "A";
    a.NazcaFunctionName = "ebeam_edge_coupler";
    var b = TestComponentFactory.CreateSimpleTwoPortComponent(); b.Identifier = "B";
    b.NazcaFunctionName = "ebeam_edge_coupler";

    string GeoKey(Component c) => ComponentGeometryKey.For(c, _ => null);
    var store = new Dictionary<string, ComponentSMatrixData> { [GeoKey(a)] = MakeData("1550", 2) };

    var result = SMatrixOverrideApplicator.ApplyAll(
        new[] { a, b }, store, geometryKeyResolver: GeoKey);

    result.PerComponent["A"].Applied.ShouldBe(1);
    result.PerComponent["B"].Applied.ShouldBe(1);
}
```

Run: `$env:PYTHONUTF8='1'; py "$env:USERPROFILE\.cap-tools\smart_test.py" SMatrixOverrideApplicator` → PASS.

- [ ] **Step 3: Commit**

```bash
git add CAP.Avalonia/ViewModels/ComponentSettings/ CAP.Avalonia/Views/MainWindow.axaml.cs UnitTests/ComponentSettings/SMatrixOverrideApplicatorTests.cs
git commit -m "(+) FDTD/import S-matrix override stored + applied by geometry key, not per-instance"
```

---

## Task 4: `FileOperationsViewModel` — Geometrie-Resolver + Save-Sweep-GC

Den Geometrie-Resolver an die `ApplyAll`-Aufrufe durchreichen und beim Speichern verwaiste Overrides verwerfen.

**Files:**
- Modify: `CAP.Avalonia/ViewModels/Panels/FileOperationsViewModel.cs`
  - neuer privater Helper `private string ResolveGeometryKey(Component c) => ComponentGeometryKey.For(c, comp => StoredNazcaOverrides.TryGetValue(comp.Identifier, out var o) ? o.RawCode : null);`
  - die `ApplyAll(...)`-Aufrufe (in `OnComponentsChangedApplyStoredOverrides` ~Z.168 und im Projekt-Load ~Z.765) um `geometryKeyResolver: ResolveGeometryKey` ergänzen.
  - Save-Sweep: im `SaveDesign`-Pfad, dort wo `designData.SMatrices = new Dictionary<...>(StoredSMatrices)` gesetzt wird (~Z.310), stattdessen nur die genutzten Einträge übernehmen (siehe Step 3).
- Test: `UnitTests/Import/SMatrixOverrideGcTests.cs`

- [ ] **Step 1: Failing test schreiben** (Sweep als reine Funktion testbar machen)

```csharp
// UnitTests/Import/SMatrixOverrideGcTests.cs
using System.Collections.Generic;
using CAP.Avalonia.Services;
using CAP_DataAccess.Persistence.PIR;
using Shouldly;
using Xunit;

namespace UnitTests.Import;

public class SMatrixOverrideGcTests
{
    private static ComponentSMatrixData Data() => new() { SourceNote = "x" };

    [Fact]
    public void Sweep_KeepsUsedKeys_DropsOrphans()
    {
        var store = new Dictionary<string, ComponentSMatrixData>
        {
            ["geo:v1-used"]   = Data(),   // a placed component resolves to this
            ["geo:v1-orphan"] = Data(),   // no component uses this geometry anymore
            ["legacy_identifier"] = Data(),// an existing component still has this Identifier
        };
        var usedGeometryKeys = new HashSet<string> { "geo:v1-used" };
        var liveIdentifiers  = new HashSet<string> { "legacy_identifier" };

        var kept = SMatrixOverrideGc.Sweep(store, usedGeometryKeys, liveIdentifiers);

        kept.Keys.ShouldBe(new[] { "geo:v1-used", "legacy_identifier" }, ignoreOrder: true);
    }
}
```

- [ ] **Step 2: Test ausführen (muss fehlschlagen)**

Run: `$env:PYTHONUTF8='1'; py "$env:USERPROFILE\.cap-tools\smart_test.py" SMatrixOverrideGc`
Expected: FAIL (`SMatrixOverrideGc` existiert nicht).

- [ ] **Step 3: `SMatrixOverrideGc` implementieren + im Save-Pfad nutzen**

```csharp
// CAP.Avalonia/Services/SMatrixOverrideGc.cs
using CAP_DataAccess.Persistence.PIR;

namespace CAP.Avalonia.Services;

/// <summary>
/// Garbage-collects project-local S-matrix overrides at save time: keeps only entries
/// whose geometry key is still used by a placed component, plus legacy per-Identifier
/// entries whose component still exists. Orphans (e.g. after a parameter change) are dropped.
/// </summary>
public static class SMatrixOverrideGc
{
    /// <summary>Returns the subset of <paramref name="store"/> that should be persisted.</summary>
    public static Dictionary<string, ComponentSMatrixData> Sweep(
        IReadOnlyDictionary<string, ComponentSMatrixData> store,
        IReadOnlySet<string> usedGeometryKeys,
        IReadOnlySet<string> liveIdentifiers)
    {
        var kept = new Dictionary<string, ComponentSMatrixData>();
        foreach (var (key, value) in store)
            if (usedGeometryKeys.Contains(key) || liveIdentifiers.Contains(key))
                kept[key] = value;
        return kept;
    }
}
```

In `SaveDesign` (FileOperationsViewModel), wo `designData.SMatrices` gesetzt wird, ersetzen durch:

```csharp
if (StoredSMatrices.Count > 0)
{
    var live = _canvas.Components.Select(vm => vm.Component).ToList();
    var usedGeometryKeys = live.Select(ResolveGeometryKey).Where(k => k != null).ToHashSet()!;
    var liveIdentifiers = live.Select(c => c.Identifier).ToHashSet();
    var swept = Services.SMatrixOverrideGc.Sweep(StoredSMatrices, usedGeometryKeys!, liveIdentifiers);
    if (swept.Count > 0)
        designData.SMatrices = swept;
}
```
(Template-scoped `::`-Keys, falls vorhanden, separat behalten falls `KeyMatchesKnownLibraryTemplate` — der ausführende Subagent prüft, ob solche im project-local Store vorkommen; falls ja, zur `usedGeometryKeys`-Bedingung ein `|| IsTemplateScopedKey(key)` ergänzen, damit user-global migrierte Keys nicht fälschlich gekehrt werden.)

- [ ] **Step 4: Test ausführen (muss bestehen)**

Run: `$env:PYTHONUTF8='1'; py "$env:USERPROFILE\.cap-tools\smart_test.py" SMatrixOverrideGc` → PASS.
Run: `dotnet build CAP.Avalonia/CAP.Avalonia.csproj -clp:ErrorsOnly` → 0 Fehler.

- [ ] **Step 5: Commit**

```bash
git add CAP.Avalonia/Services/SMatrixOverrideGc.cs CAP.Avalonia/ViewModels/Panels/FileOperationsViewModel.cs UnitTests/Import/SMatrixOverrideGcTests.cs
git commit -m "(+) S-matrix override GC: drop orphaned overrides at save time (refcount sweep)"
```

---

## Task 5: Voller Testlauf + PR

- [ ] **Step 1: Komplette Suite**

Run: `$env:PYTHONUTF8='1'; py "$env:USERPROFILE\.cap-tools\smart_test.py"`
Expected: alle grün (0 failed). Besonders `SMatrixOverrideApplicatorTests`, `ComponentCloneOrphanPinTests`, `ComponentGeometryKeyTests`, `SMatrixOverrideGcTests`.

- [ ] **Step 2: PR** gegen `main`, Titel `(+) S-matrix override scoped by geometry identity + save-time GC (fixes copy/paste override loss)`, verlinkt Spec + Issues #583/#580E. Manueller Hinweis: FDTD auf einem Edge Coupler → kopieren → Kopie zeigt denselben Override; Parameter ändern → eigener Override; Speichern → verwaiste raus.

---

## Self-Review-Notizen

- **Spec-Abdeckung:** Geometrie-Identität (T1) ✓, Vererbung via ApplyAll (T2) ✓, Schreiben unter Geometrie-Schlüssel + Nazca bleibt per-Instanz (T3) ✓, Save-Sweep-GC mit Identifier-Altbestand-Schutz (T4) ✓, Migration = Identifier-first-Match (T2-Schleife) ✓.
- **Konsistenz:** `geometryKeyResolver`-Parametername identisch in T2/T3/T4; `ComponentGeometryKey.For(component, rawCodeLookup)`-Signatur identisch überall; `raw:`/`geo:`-Präfixe in T1 definiert, in T4-Sweep nur als opake Keys genutzt.
- **Bewusst offen für den Implementierer:** exakte `_storedSMatrices`-Indizierungsstellen im Dialog (T3) und die genaue `SaveDesign`-Zeile (T4) liest der Subagent in der Datei — die Änderung ist je präzise beschrieben.
