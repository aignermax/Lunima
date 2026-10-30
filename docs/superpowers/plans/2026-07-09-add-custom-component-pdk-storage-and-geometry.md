# Storage, matrices and geometry

> Historical July 2026 implementation plan. [Plan index](2026-07-09-add-custom-component-pdk.md).
> For current design rules, see [Architecture](../../../ARCHITECTURE.md) and [Codebase map](../../CODEBASE_MAP.md).

# Bring-your-own-Component → eigenes PDK (v1) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ein „+ Neue Komponente"-Fenster in der PDK-Library, mit dem Nutzer eine eigene Komponente über eine gdsfactory-/nazca-Funktionsreferenz anlegen, ihre S-Matrix mit dem vorhandenen FDTD/Meep-Solver berechnen (oder als Blackbox/2-Port-Ideal speichern) und in ein automatisch angelegtes User-PDK pro Prozess ablegen.

**Architecture:** Neuer Vertical Slice `AddCustomComponent`. Reine Persistenz (`UserPdkStore`) liegt in CAP-DataAccess; UI-koordinierende Services (`ComponentGeometryExtractor`, `FdtdSMatrixToDraftConverter`) und das ViewModel/Fenster in CAP.Avalonia. Wiederverwendet die bestehende FDTD-Pipeline (`IFdtdSMatrixService`, `FdtdSMatrixConverter`), die Preview-Services (`NazcaComponentPreviewService`, `GdsFactoryComponentPreviewService`), `OverridePinMapper`, `PdkJsonSaver`/`PdkLoader`, `PdkTemplateConverter` und die `LeftPanelViewModel`-Library.

**Tech Stack:** C# / .NET 10 / Avalonia 11 / CommunityToolkit.Mvvm; xUnit + Shouldly + Moq; Python-Subprozess-Preview + Docker-Meep-FDTD (beide bereits vorhanden).

## Global Constraints

- Keine erfundene Physik: S-Matrix nur aus FDTD-Meep (real), Blackbox (leer), oder verlustfreiem 2-Port-Ideal. Nie erfundene Werte; FDTD-Fehler → nichts speichern.
- Cross-Platform-Parität: Pfade via `Path.Combine` + `Environment.GetFolderPath(SpecialFolder.LocalApplicationData)`; keine Drive-Letter/`\`-Literale. Kein direkter `Process.Start` (nur die vorhandenen Abstraktionen). Maschinennahe Strings (Slug, JSON) mit `CultureInfo.InvariantCulture`.
- Gebündelte Foundry-JSONs (`CAP-DataAccess/PDKs/*.json`) werden NIE geschrieben. User-PDKs liegen ausschließlich unter `%LOCALAPPDATA%/Lunima/user-pdks/`.
- Max. 250 Zeilen pro NEUER Datei. XML-Doku auf public members. `[ObservableProperty]`/`[RelayCommand]` im ViewModel. Neuer Service in DI via Feature-Extension in `CAP.Avalonia/DI/`, aufgerufen aus `App.axaml.cs`.
- Tests deterministisch; plattform-spezifische Pfad-Assertions mit `OperatingSystem.IsX()` guarden (Linux-CI grün). Preview-/FDTD-Subprozesse in Unit-Tests immer mocken.
- Vertical-Slice-Import-Regeln (CLAUDE.md): der Slice importiert nur eigene Namespaces, den Shared-Kernel und Framework-Namespaces.

## File Structure

- `CAP-DataAccess/Components/AddCustomComponent/UserPdkStore.cs` — Auflösung des User-PDK-Pfads pro Prozess; Laden-oder-Anlegen, Komponente hinzufügen/ersetzen, Speichern.
- `CAP.Avalonia/Services/AddCustomComponent/GeometryReference.cs` — Wertobjekt (Backend, Modul, Funktion, Parameter) + Erzeugung des gdsfactory-Rawcode-Wrappers.
- `CAP.Avalonia/Services/AddCustomComponent/ComponentGeometryExtractor.cs` — rendert eine Referenz über den passenden Preview-Service → bbox + Pins + rohes `NazcaPreviewResult`.
- `CAP.Avalonia/Services/AddCustomComponent/FdtdSMatrixToDraftConverter.cs` — `ComponentSMatrixData` → `PdkSMatrixDraft`; Blackbox → null; 2-Port-Ideal → verlustfreies Pass-through.
- `CAP.Avalonia/Services/Solvers/ComponentFdtdRequestFactory.cs` (MODIFY) — die private Polygon-/Port-Baulogik in eine statische `BuildFromPreview(...)` herauslösen, damit der neue Flow denselben Code nutzt.
- `CAP.Avalonia/ViewModels/Components/AddCustomComponent/NewComponentViewModel.cs` — Orchestrierung (Name/Backend/Referenz/Prozess/Preview/FDTD/Speichern).
- `CAP.Avalonia/Views/NewComponentWindow.axaml` (+ `.axaml.cs`) — das Fenster.
- `CAP.Avalonia/DI/AddCustomComponentFeature.cs` — DI-Extension.
- `CAP.Avalonia/ViewModels/Panels/LeftPanelViewModel.cs` (MODIFY) — `[RelayCommand] OpenNewComponent` + Registrierung des gespeicherten Drafts in `AllTemplates`.
- `CAP.Avalonia/Views/MainWindow.axaml` (MODIFY) — „+"-Button an der Komponenten-Library.
- `CAP.Avalonia/App.axaml.cs` (MODIFY) — `services.AddAddCustomComponentFeature();`
- Tests unter `UnitTests/Components/AddCustomComponent/` und `UnitTests/Architecture/`.

---

### Task 1: `UserPdkStore` (Persistenz pro Prozess)

**Files:**
- Create: `CAP-DataAccess/Components/AddCustomComponent/UserPdkStore.cs`
- Test: `UnitTests/Components/AddCustomComponent/UserPdkStoreTests.cs`

**Interfaces:**
- Consumes: `PdkJsonSaver.SaveToFile(PdkDraft pdk, string filePath) : void`; `PdkLoader.LoadFromFileForEditing(string) : PdkDraft`; DTOs `PdkDraft` (Props: `Name`, `Foundry`, `Backend`, `Process`, `GdsFactoryRoutingCrossSection`, `Components`), `PdkComponentDraft` (Prop `Name`), `ProcessDefinition` (Prop `Name`).
- Produces:
  - `UserPdkStore(string userPdkRootDirectory, PdkJsonSaver saver, PdkLoader loader)`
  - `static UserPdkStore CreateDefault()` — root = `%LOCALAPPDATA%/Lunima/user-pdks`
  - `string ResolvePath(ProcessDefinition process) : string`
  - `bool ComponentExists(ProcessDefinition process, string componentName) : bool`
  - `string Save(ProcessDefinition process, PdkComponentDraft component, string backend, string? routingCrossSection) : string` — legt an/lädt, ersetzt-oder-fügt-hinzu (Name-Match, Ordinal-IgnoreCase), speichert, gibt den Pfad zurück.

- [ ] **Step 1: Read the real signatures**

Lies vor dem Schreiben:
- `CAP-DataAccess/Components/ComponentDraftMapper/PdkJsonSaver.cs` (Methode `SaveToFile`).
- `CAP-DataAccess/Components/ComponentDraftMapper/PdkLoader.cs` (`LoadFromFileForEditing`, ctor — ist er parameterlos?).
- `CAP-DataAccess/Components/ComponentDraftMapper/DTOs/PdkDraft.cs` (exakte Property-Namen von `PdkDraft`, `PdkComponentDraft`, `ProcessDefinition`).
Verwende die tatsächlichen Namen; unten stehende sind aus der Inventur, aber verifiziere sie.

- [ ] **Step 2: Write the failing tests**

```csharp
using System;
using System.IO;
using CAP_DataAccess.Components.AddCustomComponent;
using CAP_DataAccess.Components.ComponentDraftMapper;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;
using Shouldly;
using Xunit;

namespace UnitTests.Components.AddCustomComponent;

public class UserPdkStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lunima-userpdk-" + Guid.NewGuid().ToString("N"));
    private UserPdkStore CreateStore() => new(_root, new PdkJsonSaver(), new PdkLoader());

    private static ProcessDefinition Process(string name) => new() { Name = name };
    private static PdkComponentDraft Comp(string name) => new() { Name = name, GdsFactoryFunction = "cspdk.sin300.coupler" };

    [Fact]
    public void ResolvePath_is_under_user_root_and_slugified()
    {
        var store = CreateStore();
        var path = store.ResolvePath(Process("CornerStone SiN 300"));
        path.ShouldStartWith(_root);
        Path.GetFileName(path).ShouldBe("cornerstone-sin-300.json");
    }

    [Fact]
    public void Save_creates_file_and_roundtrips_the_component()
    {
        var store = CreateStore();
        var path = store.Save(Process("CornerStone SiN 300"), Comp("My Coupler"), backend: "gdsfactory", routingCrossSection: "xs_nc");

        File.Exists(path).ShouldBeTrue();
        var reloaded = new PdkLoader().LoadFromFileForEditing(path);
        reloaded.Components.ShouldContain(c => c.Name == "My Coupler");
        reloaded.Process!.Name.ShouldBe("CornerStone SiN 300");
        reloaded.Backend.ShouldBe("gdsfactory");
    }

    [Fact]
    public void Save_twice_same_name_replaces_not_duplicates()
    {
        var store = CreateStore();
        store.Save(Process("P"), Comp("X"), "gdsfactory", null);
        var path = store.Save(Process("P"), Comp("X"), "gdsfactory", null);

        var reloaded = new PdkLoader().LoadFromFileForEditing(path);
        reloaded.Components.FindAll(c => c.Name == "X").Count.ShouldBe(1);
    }

    [Fact]
    public void ComponentExists_reflects_saved_state()
    {
        var store = CreateStore();
        store.ComponentExists(Process("P"), "X").ShouldBeFalse();
        store.Save(Process("P"), Comp("X"), "gdsfactory", null);
        store.ComponentExists(Process("P"), "X").ShouldBeTrue();
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `$env:PYTHONUTF8='1'; py "$env:USERPROFILE\.cap-tools\smart_test.py" UserPdkStore`
Expected: FAIL (Typ `UserPdkStore` existiert nicht).

- [ ] **Step 4: Implement `UserPdkStore`**

```csharp
using System.Globalization;
using System.Text.RegularExpressions;
using CAP_DataAccess.Components.ComponentDraftMapper;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;

namespace CAP_DataAccess.Components.AddCustomComponent;

/// <summary>
/// Persists user-authored components into a writable, per-process user PDK file under
/// the user's local app-data directory. Never touches the bundled foundry PDK JSONs.
/// One PDK file per fabrication process (the S-matrix is process-specific, #570).
/// </summary>
public sealed class UserPdkStore
{
    private readonly string _root;
    private readonly PdkJsonSaver _saver;
    private readonly PdkLoader _loader;

    /// <summary>Creates a store rooted at an explicit directory (used by tests).</summary>
    public UserPdkStore(string userPdkRootDirectory, PdkJsonSaver saver, PdkLoader loader)
    {
        _root = userPdkRootDirectory;
        _saver = saver;
        _loader = loader;
    }

    /// <summary>Creates a store rooted at %LOCALAPPDATA%/Lunima/user-pdks.</summary>
    public static UserPdkStore CreateDefault() => new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lunima", "user-pdks"),
        new PdkJsonSaver(), new PdkLoader());

    /// <summary>The user-PDK file path for a process (creates no file).</summary>
    public string ResolvePath(ProcessDefinition process) =>
        Path.Combine(_root, Slug(process.Name) + ".json");

    /// <summary>True when a component of that name is already stored for the process.</summary>
    public bool ComponentExists(ProcessDefinition process, string componentName)
    {
        var path = ResolvePath(process);
        if (!File.Exists(path)) return false;
        var pdk = _loader.LoadFromFileForEditing(path);
        return pdk.Components.Exists(c => string.Equals(c.Name, componentName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Adds or replaces (by name, case-insensitive) the component in the process's user PDK,
    /// creating the PDK file on first use. Returns the file path written.
    /// </summary>
    public string Save(ProcessDefinition process, PdkComponentDraft component, string backend, string? routingCrossSection)
    {
        var path = ResolvePath(process);
        Directory.CreateDirectory(_root);

        var pdk = File.Exists(path) ? _loader.LoadFromFileForEditing(path) : NewPdk(process, backend, routingCrossSection);
        pdk.Components.RemoveAll(c => string.Equals(c.Name, component.Name, StringComparison.OrdinalIgnoreCase));
        pdk.Components.Add(component);

        _saver.SaveToFile(pdk, path);
        return path;
    }

    private static PdkDraft NewPdk(ProcessDefinition process, string backend, string? routingCrossSection) => new()
    {
        Name = $"My {process.Name} Components",
        Foundry = process.Foundry,
        Backend = backend,
        Process = process,
        GdsFactoryRoutingCrossSection = routingCrossSection,
        Components = new()
    };

    private static string Slug(string name)
    {
        var lower = (name ?? string.Empty).ToLower(CultureInfo.InvariantCulture);
        var slug = Regex.Replace(lower, "[^a-z0-9]+", "-").Trim('-');
        return slug.Length == 0 ? "custom" : slug;
    }
}
```

Falls `PdkLoader` keinen parameterlosen ctor hat, passe die Tests + `CreateDefault` an den echten ctor an (Step 1 hat ihn ermittelt). Falls `ProcessDefinition.Foundry` nicht existiert, lasse `Foundry` weg.

- [ ] **Step 5: Run tests to verify they pass**

Run: `$env:PYTHONUTF8='1'; py "$env:USERPROFILE\.cap-tools\smart_test.py" UserPdkStore`
Expected: PASS (4/4).

- [ ] **Step 6: Commit**

```bash
git add CAP-DataAccess/Components/AddCustomComponent/UserPdkStore.cs UnitTests/Components/AddCustomComponent/UserPdkStoreTests.cs
git commit -m "(+) UserPdkStore: per-process, writable user PDK persistence (never touches foundry JSONs)"
```

---

### Task 2: `FdtdSMatrixToDraftConverter` (ehrliche S-Matrix → Draft)

**Files:**
- Create: `CAP.Avalonia/Services/AddCustomComponent/FdtdSMatrixToDraftConverter.cs`
- Test: `UnitTests/Components/AddCustomComponent/FdtdSMatrixToDraftConverterTests.cs`

**Interfaces:**
- Consumes: `ComponentSMatrixData` (`Dictionary<string,SMatrixWavelengthEntry> Wavelengths`, `string? SourceNote`), `SMatrixWavelengthEntry` (`int Rows`, `int Cols`, `List<double> Real`, `List<double> Imag`, `List<string>? PortNames`). Ziel-DTO `PdkSMatrixDraft` (`int WavelengthNm`, `List<SMatrixConnection> Connections`, `List<WavelengthSMatrixEntry>? WavelengthData`) und `WavelengthSMatrixEntry`.
- Produces:
  - `static PdkSMatrixDraft? FromFdtd(ComponentSMatrixData data)` — null bei leeren Wavelengths.
  - `static PdkSMatrixDraft? BlackBox()` — gibt `null` zurück (kein Modell → Draft-Feld `SMatrix` bleibt null).
  - `static PdkSMatrixDraft LosslessTwoPort(string inPin, string outPin, int wavelengthNm)` — Betrag 1, Phase 0 in beide Richtungen.

- [ ] **Step 1: Read the real signatures**

Lies `CAP-DataAccess/Persistence/PIR/ComponentSMatrixData.cs`, `CAP-DataAccess/Components/ComponentDraftMapper/DTOs/PdkDraft.cs` (Typen `PdkSMatrixDraft`, `SMatrixConnection`, `WavelengthSMatrixEntry`). Bestätige die exakte Form von `WavelengthSMatrixEntry` (Feldnamen für Wellenlänge/Real/Imag/Ports) und `SMatrixConnection` (`FromPin`, `ToPin`, `Magnitude`, `PhaseDegrees`).

- [ ] **Step 2: Write the failing tests**

```csharp
using System.Collections.Generic;
using CAP.Avalonia.Services.AddCustomComponent;
using CAP_DataAccess.Persistence.PIR;
using Shouldly;
using Xunit;

namespace UnitTests.Components.AddCustomComponent;

public class FdtdSMatrixToDraftConverterTests
{
    [Fact]
    public void FromFdtd_maps_each_wavelength_entry()
    {
        var data = new ComponentSMatrixData
        {
            SourceNote = "FDTD Meep 2D",
            Wavelengths = new()
            {
                ["1550"] = new SMatrixWavelengthEntry
                {
                    Rows = 2, Cols = 2,
                    Real = new() { 0, 1, 1, 0 },
                    Imag = new() { 0, 0, 0, 0 },
                    PortNames = new() { "o1", "o2" }
                }
            }
        };

        var draft = FdtdSMatrixToDraftConverter.FromFdtd(data);

        draft.ShouldNotBeNull();
        draft!.WavelengthData!.Count.ShouldBe(1);
    }

    [Fact]
    public void FromFdtd_returns_null_when_no_wavelengths()
    {
        var data = new ComponentSMatrixData { Wavelengths = new() };
        FdtdSMatrixToDraftConverter.FromFdtd(data).ShouldBeNull();
    }

    [Fact]
    public void BlackBox_is_null_so_the_component_has_no_model()
    {
        FdtdSMatrixToDraftConverter.BlackBox().ShouldBeNull();
    }

    [Fact]
    public void LosslessTwoPort_is_unit_magnitude_both_directions()
    {
        var draft = FdtdSMatrixToDraftConverter.LosslessTwoPort("o1", "o2", 1550);
        draft.Connections.Count.ShouldBe(2);
        draft.Connections.ShouldContain(c => c.FromPin == "o1" && c.ToPin == "o2" && c.Magnitude == 1.0);
        draft.Connections.ShouldContain(c => c.FromPin == "o2" && c.ToPin == "o1" && c.Magnitude == 1.0);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `$env:PYTHONUTF8='1'; py "$env:USERPROFILE\.cap-tools\smart_test.py" FdtdSMatrixToDraftConverter`
Expected: FAIL (Typ existiert nicht).

- [ ] **Step 4: Implement the converter**

Mappe jeden `ComponentSMatrixData.Wavelengths`-Eintrag (Key = nm-String) auf einen `WavelengthSMatrixEntry` des Drafts. Übertrage `Rows/Cols/Real/Imag/PortNames` 1:1 in die Feldnamen, die Step 1 ermittelt hat. Beispiel-Skelett (Feldnamen ggf. anpassen):

```csharp
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CAP_DataAccess.Components.ComponentDraftMapper.DTOs;
using CAP_DataAccess.Persistence.PIR;

namespace CAP.Avalonia.Services.AddCustomComponent;

/// <summary>
/// Converts an FDTD-computed <see cref="ComponentSMatrixData"/> into the PDK draft's
/// <see cref="PdkSMatrixDraft"/>, and provides the two honest no-FDTD fallbacks:
/// a black-box (no model) and a lossless two-port pass-through. Never fabricates values.
/// </summary>
public static class FdtdSMatrixToDraftConverter
{
    /// <summary>Multi-wavelength draft from FDTD output; null if there is no data.</summary>
    public static PdkSMatrixDraft? FromFdtd(ComponentSMatrixData data)
    {
        if (data.Wavelengths == null || data.Wavelengths.Count == 0)
            return null;

        var entries = new List<WavelengthSMatrixEntry>();
        foreach (var kv in data.Wavelengths)
        {
            int nm = int.Parse(kv.Key, CultureInfo.InvariantCulture);
            entries.Add(new WavelengthSMatrixEntry
            {
                // Map to the real field names verified in Step 1:
                WavelengthNm = nm,
                Rows = kv.Value.Rows,
                Cols = kv.Value.Cols,
                Real = kv.Value.Real,
                Imag = kv.Value.Imag,
                PortNames = kv.Value.PortNames
            });
        }

        int firstNm = entries[0].WavelengthNm;
        return new PdkSMatrixDraft { WavelengthNm = firstNm, WavelengthData = entries };
    }

    /// <summary>No simulation model — the draft's SMatrix stays null (black box).</summary>
    public static PdkSMatrixDraft? BlackBox() => null;

    /// <summary>The honest lossless two-port pass-through (routing components).</summary>
    public static PdkSMatrixDraft LosslessTwoPort(string inPin, string outPin, int wavelengthNm) => new()
    {
        WavelengthNm = wavelengthNm,
        Connections = new()
        {
            new SMatrixConnection { FromPin = inPin, ToPin = outPin, Magnitude = 1.0, PhaseDegrees = 0.0 },
            new SMatrixConnection { FromPin = outPin, ToPin = inPin, Magnitude = 1.0, PhaseDegrees = 0.0 },
        }
    };
}
```

Falls `WavelengthSMatrixEntry` andere Feldnamen hat (z.B. `Wavelength` in µm statt `WavelengthNm`), passe Mapping + Test an die echten Namen an.

- [ ] **Step 5: Run tests to verify they pass**

Run: `$env:PYTHONUTF8='1'; py "$env:USERPROFILE\.cap-tools\smart_test.py" FdtdSMatrixToDraftConverter`
Expected: PASS (4/4).

- [ ] **Step 6: Commit**

```bash
git add CAP.Avalonia/Services/AddCustomComponent/FdtdSMatrixToDraftConverter.cs UnitTests/Components/AddCustomComponent/FdtdSMatrixToDraftConverterTests.cs
git commit -m "(+) FdtdSMatrixToDraftConverter: FDTD result / black-box / lossless-2-port -> PdkSMatrixDraft"
```

---

### Task 3: FDTD-Request aus einem Preview-Ergebnis (DRY-Refactor)

**Files:**
- Modify: `CAP.Avalonia/Services/Solvers/ComponentFdtdRequestFactory.cs`
- Test: `UnitTests/Components/AddCustomComponent/PreviewFdtdRequestTests.cs`

**Interfaces:**
- Consumes: `NazcaPreviewResult` (`IReadOnlyList<NazcaPreviewPolygon> Polygons` mit `int Layer` + `Vertices`; `IReadOnlyList<NazcaPreviewPin> Pins` mit `Name`), `FdtdSMatrixRequest`, `FdtdPolygon`, `FdtdPort`.
- Produces (neue statische Methode auf `ComponentFdtdRequestFactory`):
  - `public static FdtdSMatrixRequest BuildFromPreview(NazcaPreviewResult preview, IReadOnlyList<string> portNames, int siliconLayer = 1, double portWidthUm = 0.5)`

**Rationale:** `ComponentFdtdRequestFactory.BuildAsync` besitzt private `BuildPolygons`/`BuildPorts`. Der neue Flow hat bereits ein `NazcaPreviewResult` (aus dem Preview-Render) und darf nicht doppelt rendern. Wir heben die Polygon-/Port-Erzeugung in eine statische, wiederverwendbare Methode und lassen `BuildAsync` sie aufrufen (DRY).

- [ ] **Step 1: Read the current factory**

Lies `CAP.Avalonia/Services/Solvers/ComponentFdtdRequestFactory.cs` vollständig — insbesondere `BuildPolygons` (Layer-Filter auf `siliconLayer`) und `BuildPorts` (Index-Matching Pin-Namen). Lies `CAP_Core/Solvers/Fdtd/FdtdSMatrixRequest.cs` für die exakte Form von `FdtdPolygon`/`FdtdPort`.

- [ ] **Step 2: Write the failing test**

```csharp
using System.Collections.Generic;
using System.Linq;
using CAP.Avalonia.Services.Solvers;
using CAP_Core.Export;
using Shouldly;
using Xunit;

namespace UnitTests.Components.AddCustomComponent;

public class PreviewFdtdRequestTests
{
    private static NazcaPreviewResult TwoPortPreview() => new()
    {
        Success = true,
        XMin = 0, YMin = 0, XMax = 10, YMax = 2,
        Polygons = new List<NazcaPreviewPolygon>
        {
            new() { Layer = 1, Vertices = new List<(double X, double Y)> { (0,0),(10,0),(10,2),(0,2) } }
        },
        Pins = new List<NazcaPreviewPin>
        {
            new() { Name = "o1", X = 0, Y = 1, Angle = 180 },
            new() { Name = "o2", X = 10, Y = 1, Angle = 0 },
        }
    };

    [Fact]
    public void BuildFromPreview_keeps_layer1_polygons_and_named_ports()
    {
        var req = ComponentFdtdRequestFactory.BuildFromPreview(TwoPortPreview(), new[] { "o1", "o2" });
        req.Polygons.Count.ShouldBe(1);
        req.Ports.Count.ShouldBe(2);
    }
}
```

Passe die Konstruktion von `NazcaPreviewResult`/`NazcaPreviewPolygon`/`NazcaPreviewPin` an deren echte (evtl. `init`-only) Member an (aus Step 1).

- [ ] **Step 3: Run test to verify it fails**

Run: `$env:PYTHONUTF8='1'; py "$env:USERPROFILE\.cap-tools\smart_test.py" PreviewFdtdRequest`
Expected: FAIL (`BuildFromPreview` existiert nicht).

- [ ] **Step 4: Refactor — extract `BuildFromPreview`**

Verschiebe die Logik aus den privaten `BuildPolygons`/`BuildPorts` in eine neue statische `BuildFromPreview(NazcaPreviewResult preview, IReadOnlyList<string> portNames, int siliconLayer = 1, double portWidthUm = 0.5)`, die einen vollständigen `FdtdSMatrixRequest` zurückgibt (Polygone gefiltert auf `siliconLayer`, Ports index-gematcht auf `portNames`, `LayerNumber = siliconLayer`, `Is3D = false`, restliche Defaults wie bisher). Lasse die bestehende `BuildAsync` diese Methode aufrufen (mit `component.PhysicalPins.Select(p => p.Name)` als `portNames`), sodass sich das bisherige Verhalten nicht ändert. XML-Doku auf die neue public-Methode.

- [ ] **Step 5: Run the focused test AND the existing factory tests**

Run: `$env:PYTHONUTF8='1'; py "$env:USERPROFILE\.cap-tools\smart_test.py" PreviewFdtdRequest`
Run: `$env:PYTHONUTF8='1'; py "$env:USERPROFILE\.cap-tools\smart_test.py" ComponentFdtdRequestFactory`
Expected: beide PASS (bestehendes Verhalten unverändert).

- [ ] **Step 6: Commit**

```bash
git add CAP.Avalonia/Services/Solvers/ComponentFdtdRequestFactory.cs UnitTests/Components/AddCustomComponent/PreviewFdtdRequestTests.cs
git commit -m "(~) ComponentFdtdRequestFactory: extract reusable BuildFromPreview(NazcaPreviewResult, portNames)"
```

---

### Task 4: `GeometryReference` + `ComponentGeometryExtractor`

**Files:**
- Create: `CAP.Avalonia/Services/AddCustomComponent/GeometryReference.cs`
- Create: `CAP.Avalonia/Services/AddCustomComponent/ComponentGeometryExtractor.cs`
- Test: `UnitTests/Components/AddCustomComponent/ComponentGeometryExtractorTests.cs`

**Interfaces:**
- Consumes: `NazcaComponentPreviewService.RenderAsync(string? module, string func, string? parameters, CancellationToken) : Task<NazcaPreviewResult>` und `.RenderRawCodeAsync(string code, CancellationToken) : Task<NazcaPreviewResult>`; `GdsFactoryComponentPreviewService` (Subtyp, für gdsfactory); `OverridePinMapper.BuildOverridePins(NazcaPreviewResult) : List<OverridePinData>`; `NazcaPreviewResult` (`Success`, `Error`, `XMin/YMin/XMax/YMax`, `Pins`).
- Produces:
  - `enum GeometryBackend { Nazca, GdsFactory }`
  - `record GeometryReference(GeometryBackend Backend, string? Module, string Function, string? Parameters)` mit `string ToGdsFactoryRawCode()`.
  - `record GeometryExtractResult(bool Success, string? Error, double WidthUm, double HeightUm, IReadOnlyList<OverridePinData> Pins, NazcaPreviewResult Raw)`
  - `ComponentGeometryExtractor(NazcaComponentPreviewService nazcaPreview, GdsFactoryComponentPreviewService gdsFactoryPreview)`
  - `Task<GeometryExtractResult> ExtractAsync(GeometryReference reference, CancellationToken ct = default)`

- [ ] **Step 1: Read the real signatures**

Lies `Connect-A-Pic-Core/Export/NazcaComponentPreviewService.cs` (`RenderAsync`, `RenderRawCodeAsync`, `NazcaPreviewResult`-Member) und `CAP.Avalonia/ViewModels/ComponentSettings/InstanceOverride/OverridePinMapper.cs` (`BuildOverridePins`, `OverridePinData`-Member). Prüfe, ob `RenderAsync`/`RenderRawCodeAsync` `virtual` sind (für Moq) — laut Inventur ja (`public virtual`).

- [ ] **Step 2: Write `GeometryReference` first (pure, unit-testable)**

Erzeuge `GeometryReference.cs`:

```csharp
namespace CAP.Avalonia.Services.AddCustomComponent;

/// <summary>Which geometry engine renders/exports a custom component.</summary>
public enum GeometryBackend { Nazca, GdsFactory }

/// <summary>
/// A reference to a component-producing function in a Python module, e.g.
/// module "cspdk.sin300", function "coupler". Parameters is an optional Python
/// kwargs fragment (e.g. "length=10"). Renders in one of two ways depending on backend.
/// </summary>
public sealed record GeometryReference(GeometryBackend Backend, string? Module, string Function, string? Parameters)
{
    /// <summary>The fully-qualified call, e.g. "cspdk.sin300.coupler" or "coupler".</summary>
    public string QualifiedFunction => string.IsNullOrWhiteSpace(Module) ? Function : $"{Module}.{Function}";

    /// <summary>
    /// Wraps the reference in a raw-code snippet the gdsfactory preview script understands:
    /// it imports the module and assigns the built cell to a variable named `component`.
    /// </summary>
    public string ToGdsFactoryRawCode()
    {
        var import = string.IsNullOrWhiteSpace(Module) ? "import gdsfactory as gf" : $"import {Module}";
        return $"{import}\ncomponent = {QualifiedFunction}({Parameters ?? string.Empty})";
    }
}
```

- [ ] **Step 3: Write the failing extractor tests**

```csharp
using System.Threading;
using System.Threading.Tasks;
using CAP.Avalonia.Services.AddCustomComponent;
using CAP_Core.Export;
using Moq;
using Shouldly;
using Xunit;

namespace UnitTests.Components.AddCustomComponent;

public class ComponentGeometryExtractorTests
{
    private static NazcaPreviewResult Ok() => new()
    {
        Success = true, XMin = 0, YMin = 0, XMax = 12, YMax = 3,
        Polygons = new System.Collections.Generic.List<NazcaPreviewPolygon>(),
        Pins = new System.Collections.Generic.List<NazcaPreviewPin>
        {
            new() { Name = "o1", X = 0, Y = 1.5, Angle = 180 },
            new() { Name = "o2", X = 12, Y = 1.5, Angle = 0 },
        }
    };

    private static (Mock<NazcaComponentPreviewService> nazca, Mock<GdsFactoryComponentPreviewService> gds) Mocks()
    {
        // Both preview services take (pythonExecutable, scriptPath, timeout?, launchFactory?); Moq needs ctor args.
        var nazca = new Mock<NazcaComponentPreviewService>("python", "render_component_preview.py", null, null) { CallBase = false };
        var gds = new Mock<GdsFactoryComponentPreviewService>("python", "render_gdsfactory_preview.py", null, null) { CallBase = false };
        return (nazca, gds);
    }

    [Fact]
    public async Task GdsFactory_reference_renders_via_raw_code_wrapper()
    {
        var (nazca, gds) = Mocks();
        gds.Setup(g => g.RenderRawCodeAsync(It.Is<string>(s => s.Contains("cspdk.sin300.coupler")), It.IsAny<CancellationToken>()))
           .ReturnsAsync(Ok());
        var extractor = new ComponentGeometryExtractor(nazca.Object, gds.Object);

        var result = await extractor.ExtractAsync(
            new GeometryReference(GeometryBackend.GdsFactory, "cspdk.sin300", "coupler", null));

        result.Success.ShouldBeTrue();
        result.WidthUm.ShouldBe(12);
        result.HeightUm.ShouldBe(3);
        result.Pins.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Nazca_reference_renders_via_module_function()
    {
        var (nazca, gds) = Mocks();
        nazca.Setup(n => n.RenderAsync("mymod", "mycell", null, It.IsAny<CancellationToken>()))
             .ReturnsAsync(Ok());
        var extractor = new ComponentGeometryExtractor(nazca.Object, gds.Object);

        var result = await extractor.ExtractAsync(
            new GeometryReference(GeometryBackend.Nazca, "mymod", "mycell", null));

        result.Success.ShouldBeTrue();
        result.Pins.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Failed_render_surfaces_error_and_no_pins()
    {
        var (nazca, gds) = Mocks();
        gds.Setup(g => g.RenderRawCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync(new NazcaPreviewResult { Success = false, Error = "boom" });
        var extractor = new ComponentGeometryExtractor(nazca.Object, gds.Object);

        var result = await extractor.ExtractAsync(
            new GeometryReference(GeometryBackend.GdsFactory, "m", "f", null));

        result.Success.ShouldBeFalse();
        result.Error.ShouldBe("boom");
    }
}
```

Falls `NazcaComponentPreviewService`/`GdsFactoryComponentPreviewService` von Moq nicht mockbar sind (nicht-virtuelle Methoden oder `sealed`): `GdsFactoryComponentPreviewService` IST `sealed` — führe stattdessen ein schmales Interface `IComponentPreviewRenderer` mit `RenderAsync`/`RenderRawCodeAsync` ein, implementiert als dünner Adapter über die zwei Services, und injiziere zwei `IComponentPreviewRenderer` in den Extractor. Passe die Tests entsprechend an (Mock<IComponentPreviewRenderer>). Bevorzuge diesen Interface-Weg, wenn Moq an den konkreten Typen scheitert.

- [ ] **Step 4: Run tests to verify they fail**

Run: `$env:PYTHONUTF8='1'; py "$env:USERPROFILE\.cap-tools\smart_test.py" ComponentGeometryExtractor`
Expected: FAIL.

- [ ] **Step 5: Implement `ComponentGeometryExtractor`**

```csharp
using CAP.Avalonia.ViewModels.ComponentSettings.InstanceOverride;
using CAP_Core.Export;

namespace CAP.Avalonia.Services.AddCustomComponent;

/// <summary>Result of rendering a geometry reference: bounding-box size + extracted pins.</summary>
public sealed record GeometryExtractResult(
    bool Success, string? Error, double WidthUm, double HeightUm,
    IReadOnlyList<OverridePinData> Pins, NazcaPreviewResult Raw);

/// <summary>
/// Renders a <see cref="GeometryReference"/> to geometry via the appropriate preview service
/// (nazca in module mode, gdsfactory via a raw-code wrapper) and extracts the bounding-box
/// size and physical pins — the same extraction the per-instance override "Apply" performs.
/// </summary>
public sealed class ComponentGeometryExtractor
{
    private readonly NazcaComponentPreviewService _nazca;
    private readonly GdsFactoryComponentPreviewService _gdsFactory;

    /// <summary>Creates the extractor from the two shared preview services.</summary>
    public ComponentGeometryExtractor(NazcaComponentPreviewService nazcaPreview, GdsFactoryComponentPreviewService gdsFactoryPreview)
    {
        _nazca = nazcaPreview;
        _gdsFactory = gdsFactoryPreview;
    }

    /// <summary>Renders the reference and extracts size + pins. On render failure, Success is false.</summary>
    public async Task<GeometryExtractResult> ExtractAsync(GeometryReference reference, CancellationToken ct = default)
    {
        NazcaPreviewResult preview = reference.Backend == GeometryBackend.GdsFactory
            ? await _gdsFactory.RenderRawCodeAsync(reference.ToGdsFactoryRawCode(), ct)
            : await _nazca.RenderAsync(reference.Module, reference.Function, reference.Parameters, ct);

        if (!preview.Success)
            return new GeometryExtractResult(false, preview.Error, 0, 0, System.Array.Empty<OverridePinData>(), preview);

        double width = preview.XMax - preview.XMin;
        double height = preview.YMax - preview.YMin;
        var pins = OverridePinMapper.BuildOverridePins(preview);
        return new GeometryExtractResult(true, null, width, height, pins, preview);
    }
}
```

Wenn du in Step 3 den Interface-Weg gewählt hast, injiziere `IComponentPreviewRenderer nazca, IComponentPreviewRenderer gdsFactory` statt der konkreten Typen.

- [ ] **Step 6: Run tests to verify they pass**

Run: `$env:PYTHONUTF8='1'; py "$env:USERPROFILE\.cap-tools\smart_test.py" ComponentGeometryExtractor`
Expected: PASS (3/3).

- [ ] **Step 7: Commit**

```bash
git add CAP.Avalonia/Services/AddCustomComponent/GeometryReference.cs CAP.Avalonia/Services/AddCustomComponent/ComponentGeometryExtractor.cs UnitTests/Components/AddCustomComponent/ComponentGeometryExtractorTests.cs
git commit -m "(+) ComponentGeometryExtractor: render a gdsfactory/nazca function reference -> bbox + pins"
```

---

