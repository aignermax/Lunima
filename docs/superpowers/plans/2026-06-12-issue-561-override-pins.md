# Issue #561 Override-Pins fertigstellen — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** PR #562 (Branch `agent/issue-561-1781241133`) vervollständigen: CI-grün durch Datei-Split, Connection-Re-Anchoring nach Pin-Override verdrahten, LogicalPin-Übernahme per Name.

**Architecture:** Die Pin-Mapping-Helfer wandern aus dem 604-Zeilen-Editor-VM in eine eigene statische Klasse `OverridePinMapper` (CAP.Avalonia), die auch der Lade-Pfad nutzt. Ein neuer Core-Service `ConnectionPinReanchorService` hängt Connections namensbasiert auf neue Pins um bzw. meldet nicht zuordenbare als „dropped". `DesignCanvasViewModel.OnComponentPinsChanged` orchestriert Re-Anchor + PinViewModel-Refresh + Re-Routing; der Callback wird von `MainWindow` über `ComponentSettingsDialogViewModel.Configure` zum Editor-VM durchgereicht (Parameter existiert dort schon).

**Tech Stack:** C# / .NET, Avalonia, CommunityToolkit.Mvvm, xUnit + Shouldly + Moq. Tests IMMER via `python3 tools/smart_test.py <Pattern>` (NIE `dotnet test`).

**Spec:** `docs/superpowers/specs/2026-06-12-issue-561-override-pins-design.md`

**Voraussetzung:** Branch `agent/issue-561-1781241133` ist ausgecheckt (ist bereits der Fall). Alle Pfade relativ zu `C:\dev\Akhetonics\Lunima`.

**Bekannte, akzeptierte Einschränkung (dokumentieren, nicht fixen):** Reset nach einem Override mit *umbenannten* Pins kann die `LogicalPin`-Verknüpfung in der laufenden Session nicht wiederherstellen (die Zuordnung Name→LogicalPin der Template-Pins ist dann verloren; logische Pin-Namen ≠ physische Pin-Namen, daher kein zuverlässiger Fallback). Nach Projekt-Reload ist sie wieder intakt. Der Reset-Statustext weist darauf hin (Task 2, Schritt 5).

---

### Task 1: `OverridePinMapper` extrahieren (fixt FileSizeLimit-CI-Failure)

Reiner Move-Refactor — Verhalten unverändert, bestehende Tests laufen nach Umbenennung der Aufrufstellen weiter.

**Files:**
- Create: `CAP.Avalonia/ViewModels/ComponentSettings/InstanceOverride/OverridePinMapper.cs`
- Modify: `CAP.Avalonia/ViewModels/ComponentSettings/InstanceOverride/InstanceNazcaCodeEditorViewModel.cs` (Helfer-Block am Dateiende, ca. Zeilen 530–604, entfernen; 4 Aufrufstellen umstellen)
- Modify: `CAP.Avalonia/ViewModels/Panels/FileOperationsViewModel.cs` (privates `RestorePinsFromOverride` löschen, Aufruf umstellen)
- Create: `UnitTests/ComponentSettings/InstanceOverride/OverridePinMapperTests.cs`
- Modify: `UnitTests/ComponentSettings/InstanceOverride/InstanceNazcaCodeEditorPinOverrideTests.cs` (reine Mapper-Tests dorthin verschieben)

- [ ] **Step 1: Neue Klasse `OverridePinMapper` anlegen**

Inhalt = die vier Helfer, die aktuell am Ende von `InstanceNazcaCodeEditorViewModel.cs` stehen (`BuildOverridePins`, `PinNamesMatch`, `CaptureAsPinData`, `ApplyPinsToComponent`), als `public static`. XML-Doku von dort übernehmen.

```csharp
using CAP_Core.Components.Core;
using CAP_Core.Export;
using CAP_DataAccess.Persistence.PIR;

namespace CAP.Avalonia.ViewModels.ComponentSettings.InstanceOverride;

/// <summary>
/// Maps Nazca preview pins to component-local <see cref="OverridePinData"/> and applies
/// persisted pin overrides to a live <see cref="Component"/> (issue #561). Shared by the
/// per-instance Nazca code editor (Apply/Reset) and the project-load path.
/// </summary>
public static class OverridePinMapper
{
    /// <summary>
    /// Converts the preview's pin stubs to component-local <see cref="OverridePinData"/>
    /// using a bounding-box–relative coordinate transform:
    /// <list type="bullet">
    /// <item><c>OffsetX = previewPin.X − bbox.XMin</c></item>
    /// <item><c>OffsetY = bbox.YMax − previewPin.Y</c> (Y-axis flip to Y-down app space)</item>
    /// <item><c>AngleDegrees = −previewPin.Angle</c> (Y-axis flip)</item>
    /// </list>
    /// </summary>
    public static List<OverridePinData> BuildOverridePins(NazcaPreviewResult preview)
    {
        return preview.Pins.Select(p => new OverridePinData
        {
            Name = p.Name,
            OffsetXMicrometers = p.X - preview.XMin,
            OffsetYMicrometers = preview.YMax - p.Y,
            AngleDegrees = -p.Angle,
        }).ToList();
    }

    /// <summary>
    /// Returns true when both lists have the same set of pin names (order-independent).
    /// An empty or null list is considered "matching" to avoid false positives when
    /// no pins are defined.
    /// </summary>
    public static bool PinNamesMatch(
        IReadOnlyList<OverridePinData>? a, IReadOnlyList<OverridePinData>? b)
    {
        if ((a == null || a.Count == 0) && (b == null || b.Count == 0))
            return true;
        if (a == null || b == null)
            return false;
        if (a.Count != b.Count)
            return false;
        var namesA = a.Select(p => p.Name).OrderBy(n => n).ToList();
        var namesB = b.Select(p => p.Name).OrderBy(n => n).ToList();
        return namesA.SequenceEqual(namesB);
    }

    /// <summary>
    /// Snapshots the component's current physical pins as <see cref="OverridePinData"/> DTOs.
    /// </summary>
    public static List<OverridePinData> CaptureAsPinData(IEnumerable<PhysicalPin> pins)
        => pins.Select(p => new OverridePinData
        {
            Name = p.Name,
            OffsetXMicrometers = p.OffsetXMicrometers,
            OffsetYMicrometers = p.OffsetYMicrometers,
            AngleDegrees = p.AngleDegrees,
        }).ToList();

    /// <summary>
    /// Replaces the component's physical pin list with pins derived from
    /// <paramref name="pinData"/>. <c>LogicalPin</c> links are not restored
    /// (override pins have no S-matrix tie-in).
    /// </summary>
    public static void ApplyPinsToComponent(Component comp, IReadOnlyList<OverridePinData> pinData)
    {
        comp.PhysicalPins.Clear();
        foreach (var pd in pinData)
        {
            comp.PhysicalPins.Add(new PhysicalPin
            {
                Name = pd.Name,
                OffsetXMicrometers = pd.OffsetXMicrometers,
                OffsetYMicrometers = pd.OffsetYMicrometers,
                AngleDegrees = pd.AngleDegrees,
                ParentComponent = comp,
            });
        }
    }
}
```

Hinweis: `ApplyPinsToComponent` nimmt `IReadOnlyList<OverridePinData>` (statt `List<>` wie bisher) — das deckt beide Aufrufer ab. Die `LogicalPin`-Übernahme kommt erst in Task 2 (TDD).

- [ ] **Step 2: Helfer aus `InstanceNazcaCodeEditorViewModel.cs` entfernen und Aufrufstellen umstellen**

Den kompletten Region-Block `// ─── Pin override helpers ───…` am Dateiende (die vier Methoden `BuildOverridePins`, `PinNamesMatch`, `CaptureAsPinData`, `ApplyPinsToComponent`) löschen. Die vier Aufrufstellen in `ApplyOverride()` und `ResetToTemplate()` umstellen:

```csharp
// in ApplyOverride():
if (overrideData.TemplatePins == null && _liveComponent != null)
    overrideData.TemplatePins = OverridePinMapper.CaptureAsPinData(_liveComponent.PhysicalPins);

var overridePins = OverridePinMapper.BuildOverridePins(_lastSuccessfulPreview);
// ...
overrideData.HasNoSimulationModel = !OverridePinMapper.PinNamesMatch(overrideData.TemplatePins, overridePins);
// ...
    OverridePinMapper.ApplyPinsToComponent(_liveComponent, overridePins);

// in ResetToTemplate():
    OverridePinMapper.ApplyPinsToComponent(_liveComponent, templatePinsToRestore);
```

Das `using System.Linq;` im Editor-VM stehen lassen — Task 2 (Step 5) braucht es wieder (`.Any(...)`).

- [ ] **Step 3: `FileOperationsViewModel.RestorePinsFromOverride` durch Mapper ersetzen**

In `CAP.Avalonia/ViewModels/Panels/FileOperationsViewModel.cs` (ca. Zeile 905–935): den Aufruf

```csharp
if (nazcaOverride.OverridePins?.Count > 0)
    RestorePinsFromOverride(component, nazcaOverride.OverridePins);
```

ersetzen durch

```csharp
if (nazcaOverride.OverridePins?.Count > 0)
    ComponentSettings.InstanceOverride.OverridePinMapper.ApplyPinsToComponent(
        component, nazcaOverride.OverridePins);
```

und die private Methode `RestorePinsFromOverride` komplett löschen. (Namespace-Präfix nach Bedarf — `FileOperationsViewModel` liegt in `CAP.Avalonia.ViewModels.Panels`, der Mapper in `CAP.Avalonia.ViewModels.ComponentSettings.InstanceOverride`; ggf. stattdessen ein `using CAP.Avalonia.ViewModels.ComponentSettings.InstanceOverride;` ergänzen und unqualifiziert aufrufen.)

- [ ] **Step 4: Reine Mapper-Tests in neue Testdatei verschieben**

Neue Datei `UnitTests/ComponentSettings/InstanceOverride/OverridePinMapperTests.cs`. Die Tests `BuildOverridePins_ConvertsNazcaCoordsToComponentLocal`, `BuildOverridePins_NonZeroBboxOffset_SubtractsXMin`, `PinNamesMatch_SameNames_ReturnsTrue`, `PinNamesMatch_DifferentNames_ReturnsFalse`, `PinNamesMatch_DifferentCounts_ReturnsFalse`, `PinNamesMatch_BothEmpty_ReturnsTrue` aus `InstanceNazcaCodeEditorPinOverrideTests.cs` UNVERÄNDERT hierher verschieben (Klassenname `OverridePinMapperTests`, gleiche Namespace-Zeile `namespace UnitTests.ComponentSettings.InstanceOverride;`) und darin alle `InstanceNazcaCodeEditorViewModel.`-Präfixe durch `OverridePinMapper.` ersetzen. Benötigte usings:

```csharp
using CAP.Avalonia.ViewModels.ComponentSettings.InstanceOverride;
using CAP_Core.Export;
using CAP_DataAccess.Persistence.PIR;
using Shouldly;
using Xunit;
```

Die verschobenen Tests (samt Helper `OkResultWithPins`, falls nur dort genutzt — er wird auch von den verbleibenden Tests gebraucht, also in beiden Dateien je eine Kopie bzw. dort lassen) aus `InstanceNazcaCodeEditorPinOverrideTests.cs` löschen.

- [ ] **Step 5: Build + betroffene Tests laufen lassen**

```bash
dotnet build
python3 tools/smart_test.py PinOverride
python3 tools/smart_test.py OverridePinMapper
python3 tools/smart_test.py FileSizeLimit
```

Erwartung: Build ok, alle drei Läufe PASS. `FileSizeLimit` ist der eigentliche CI-Fix — `InstanceNazcaCodeEditorViewModel.cs` muss jetzt unter 500 Zeilen liegen (vorher 604).

- [ ] **Step 6: Commit**

```bash
git add CAP.Avalonia/ViewModels/ComponentSettings/InstanceOverride/OverridePinMapper.cs CAP.Avalonia/ViewModels/ComponentSettings/InstanceOverride/InstanceNazcaCodeEditorViewModel.cs CAP.Avalonia/ViewModels/Panels/FileOperationsViewModel.cs UnitTests/ComponentSettings/InstanceOverride/
git commit -m "(~) Pin-Override-Helfer in OverridePinMapper extrahieren (#561, fixt FileSizeLimit)"
```

---

### Task 2: LogicalPin-Übernahme per Name (TDD)

**Files:**
- Modify: `CAP.Avalonia/ViewModels/ComponentSettings/InstanceOverride/OverridePinMapper.cs` (`ApplyPinsToComponent`)
- Modify: `CAP.Avalonia/ViewModels/ComponentSettings/InstanceOverride/InstanceNazcaCodeEditorViewModel.cs` (Reset-Statustext)
- Test: `UnitTests/ComponentSettings/InstanceOverride/OverridePinMapperTests.cs`

- [ ] **Step 1: Failing Tests schreiben**

In `OverridePinMapperTests.cs` ergänzen (using `CAP_Core.Components.Core;` und `using static UnitTests.TestComponentFactory;` hinzufügen):

```csharp
// ─── ApplyPinsToComponent: LogicalPin carry-over ──────────────────────────

[Fact]
public void ApplyPinsToComponent_SamePinName_CarriesLogicalPinOver()
{
    var comp = CreateStraightWaveGuideWithPhysicalPins();   // pins "in"/"out" mit LogicalPins
    var logicalIn = comp.PhysicalPins.First(p => p.Name == "in").LogicalPin;
    logicalIn.ShouldNotBeNull();

    var pinData = new List<OverridePinData>
    {
        new() { Name = "in",  OffsetXMicrometers = 0,  OffsetYMicrometers = 5, AngleDegrees = 180 },
        new() { Name = "out", OffsetXMicrometers = 20, OffsetYMicrometers = 5, AngleDegrees = 0 },
    };

    OverridePinMapper.ApplyPinsToComponent(comp, pinData);

    comp.PhysicalPins.First(p => p.Name == "in").LogicalPin.ShouldBeSameAs(logicalIn);
}

[Fact]
public void ApplyPinsToComponent_NewPinName_LeavesLogicalPinNull()
{
    var comp = CreateStraightWaveGuideWithPhysicalPins();   // pins "in"/"out"

    var pinData = new List<OverridePinData>
    {
        new() { Name = "a0", OffsetXMicrometers = 0, OffsetYMicrometers = 5, AngleDegrees = 180 },
    };

    OverridePinMapper.ApplyPinsToComponent(comp, pinData);

    comp.PhysicalPins.Single().LogicalPin.ShouldBeNull();
}

[Fact]
public void ApplyPinsToComponent_RepeatedApply_KeepsCarryingLogicalPin()
{
    // Zweimal anwenden (typisch: Preview → Apply → Code ändern → Apply):
    // der LogicalPin muss über beide Applies hinweg erhalten bleiben.
    var comp = CreateStraightWaveGuideWithPhysicalPins();
    var logicalIn = comp.PhysicalPins.First(p => p.Name == "in").LogicalPin;
    var pinData = new List<OverridePinData>
    {
        new() { Name = "in",  OffsetXMicrometers = 1, OffsetYMicrometers = 5, AngleDegrees = 180 },
        new() { Name = "out", OffsetXMicrometers = 19, OffsetYMicrometers = 5, AngleDegrees = 0 },
    };

    OverridePinMapper.ApplyPinsToComponent(comp, pinData);
    OverridePinMapper.ApplyPinsToComponent(comp, pinData);

    comp.PhysicalPins.First(p => p.Name == "in").LogicalPin.ShouldBeSameAs(logicalIn);
}
```

- [ ] **Step 2: Tests laufen lassen — müssen FAILEN**

```bash
python3 tools/smart_test.py OverridePinMapper
```

Erwartung: Die drei neuen Tests FAIL (`LogicalPin` ist nach `ApplyPinsToComponent` immer null), die übrigen PASS.

- [ ] **Step 3: `ApplyPinsToComponent` implementieren**

In `OverridePinMapper.cs` die Methode ersetzen:

```csharp
/// <summary>
/// Replaces the component's physical pin list with pins derived from
/// <paramref name="pinData"/>. The <see cref="PhysicalPin.LogicalPin"/> link
/// (S-matrix tie-in) is carried over by pin name from the pins being replaced,
/// so same-named overrides keep simulating against the template S-matrix.
/// Pins whose name has no predecessor get <c>LogicalPin = null</c> — the
/// component then has no simulation model for that port (issue #561).
/// </summary>
public static void ApplyPinsToComponent(Component comp, IReadOnlyList<OverridePinData> pinData)
{
    var logicalByName = comp.PhysicalPins
        .Where(p => p.LogicalPin != null)
        .GroupBy(p => p.Name)
        .ToDictionary(g => g.Key, g => g.First().LogicalPin);

    comp.PhysicalPins.Clear();
    foreach (var pd in pinData)
    {
        comp.PhysicalPins.Add(new PhysicalPin
        {
            Name = pd.Name,
            OffsetXMicrometers = pd.OffsetXMicrometers,
            OffsetYMicrometers = pd.OffsetYMicrometers,
            AngleDegrees = pd.AngleDegrees,
            ParentComponent = comp,
            LogicalPin = logicalByName.GetValueOrDefault(pd.Name),
        });
    }
}
```

- [ ] **Step 4: Tests laufen lassen — müssen PASSEN**

```bash
python3 tools/smart_test.py OverridePinMapper
python3 tools/smart_test.py PinOverride
```

Erwartung: alle PASS. (`PinOverride` deckt die Editor-VM-Pfade ab, die jetzt indirekt den Carry-over nutzen.)

- [ ] **Step 5: Reset-Statustext um Reload-Hinweis ergänzen**

In `InstanceNazcaCodeEditorViewModel.ResetToTemplate()` den Statustext-Zweig ersetzen — wenn nach dem Restore Template-Pins ohne `LogicalPin` dastehen (Folge eines zuvor umbenennenden Overrides), das ehrlich sagen:

```csharp
bool simulationLinkLost = templatePinsToRestore?.Count > 0
    && _liveComponent != null
    && _liveComponent.PhysicalPins.Any(p => p.LogicalPin == null);
StatusText = templatePinsToRestore?.Count > 0
    ? simulationLinkLost
        ? "Reset to original source — template pins restored. Reload the project to restore the simulation link."
        : "Reset to original source — template pins restored. Run a preview before applying."
    : "Reset to original source. Run a preview before applying.";
```

- [ ] **Step 6: Bestehende Reset-Tests prüfen und Commit**

```bash
python3 tools/smart_test.py PinOverride
git add CAP.Avalonia/ViewModels/ComponentSettings/InstanceOverride/ UnitTests/ComponentSettings/InstanceOverride/OverridePinMapperTests.cs
git commit -m "(*) LogicalPin-Uebernahme per Name beim Pin-Override (#561)"
```

Erwartung vor Commit: alle Tests PASS. Falls `Reset_WithTemplatePins_RestoresLiveComponentPins` am neuen Statustext scheitert: Der Test prüft den Statustext nicht — er sollte unverändert PASSen.

---

### Task 3: `ConnectionPinReanchorService` (Core, TDD)

**Files:**
- Create: `Connect-A-Pic-Core/Components/Connections/ConnectionPinReanchorService.cs`
- Test: `UnitTests/Connections/ConnectionPinReanchorServiceTests.cs` (Ordner `UnitTests/Connections/` existiert ggf. noch nicht — anlegen)

- [ ] **Step 1: Failing Tests schreiben**

```csharp
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using Shouldly;
using Xunit;
using static UnitTests.TestComponentFactory;

namespace UnitTests.Connections;

/// <summary>
/// Tests for re-anchoring waveguide connections after a component's physical
/// pins were replaced by a Nazca raw-code override (issue #561).
/// </summary>
public class ConnectionPinReanchorServiceTests
{
    /// <summary>Replaces the component's pins with same-named copies (new objects).</summary>
    private static void ReplacePinsWithSameNames(Component comp)
    {
        var copies = comp.PhysicalPins.Select(p => new PhysicalPin
        {
            Name = p.Name,
            OffsetXMicrometers = p.OffsetXMicrometers + 1,
            OffsetYMicrometers = p.OffsetYMicrometers,
            AngleDegrees = p.AngleDegrees,
            ParentComponent = comp,
        }).ToList();
        comp.PhysicalPins.Clear();
        comp.PhysicalPins.AddRange(copies);
    }

    [Fact]
    public void Reanchor_SamePinName_ReassignsConnectionToNewPin()
    {
        var compA = CreateStraightWaveGuideWithPhysicalPins();   // "in"/"out"
        var compB = CreateStraightWaveGuideWithPhysicalPins();
        var conn = new WaveguideConnection
        {
            StartPin = compA.PhysicalPins.First(p => p.Name == "out"),
            EndPin = compB.PhysicalPins.First(p => p.Name == "in"),
        };

        ReplacePinsWithSameNames(compA);
        var result = ConnectionPinReanchorService.Reanchor(compA, new[] { conn });

        result.DroppedConnections.ShouldBeEmpty();
        result.ReanchoredCount.ShouldBe(1);
        conn.StartPin.ShouldBeSameAs(compA.PhysicalPins.First(p => p.Name == "out"));
        conn.EndPin.ParentComponent.ShouldBeSameAs(compB);   // fremde Seite unangetastet
    }

    [Fact]
    public void Reanchor_RemovedPinName_DropsConnectionWithWarning()
    {
        var compA = CreateStraightWaveGuideWithPhysicalPins();
        var compB = CreateStraightWaveGuideWithPhysicalPins();
        var conn = new WaveguideConnection
        {
            StartPin = compA.PhysicalPins.First(p => p.Name == "out"),
            EndPin = compB.PhysicalPins.First(p => p.Name == "in"),
        };

        // Override entfernt "out": nur noch ein Pin "a0".
        compA.PhysicalPins.Clear();
        compA.PhysicalPins.Add(new PhysicalPin { Name = "a0", ParentComponent = compA });

        var result = ConnectionPinReanchorService.Reanchor(compA, new[] { conn });

        result.ReanchoredCount.ShouldBe(0);
        result.DroppedConnections.ShouldHaveSingleItem().ShouldBeSameAs(conn);
        result.Warnings.ShouldHaveSingleItem().ShouldContain("out");
    }

    [Fact]
    public void Reanchor_ConnectionOfOtherComponents_IsUntouched()
    {
        var compA = CreateStraightWaveGuideWithPhysicalPins();
        var compB = CreateStraightWaveGuideWithPhysicalPins();
        var compC = CreateStraightWaveGuideWithPhysicalPins();
        var foreignConn = new WaveguideConnection
        {
            StartPin = compB.PhysicalPins[0],
            EndPin = compC.PhysicalPins[1],
        };
        var originalStart = foreignConn.StartPin;

        ReplacePinsWithSameNames(compA);
        var result = ConnectionPinReanchorService.Reanchor(compA, new[] { foreignConn });

        result.ReanchoredCount.ShouldBe(0);
        result.DroppedConnections.ShouldBeEmpty();
        foreignConn.StartPin.ShouldBeSameAs(originalStart);
    }

    [Fact]
    public void Reanchor_BothEndpointsOnSameComponent_HandlesBothSides()
    {
        var comp = CreateStraightWaveGuideWithPhysicalPins();   // "in"/"out"
        var conn = new WaveguideConnection
        {
            StartPin = comp.PhysicalPins.First(p => p.Name == "in"),
            EndPin = comp.PhysicalPins.First(p => p.Name == "out"),
        };

        ReplacePinsWithSameNames(comp);
        var result = ConnectionPinReanchorService.Reanchor(comp, new[] { conn });

        result.ReanchoredCount.ShouldBe(1);
        conn.StartPin.ShouldBeSameAs(comp.PhysicalPins.First(p => p.Name == "in"));
        conn.EndPin.ShouldBeSameAs(comp.PhysicalPins.First(p => p.Name == "out"));
    }

    [Fact]
    public void Reanchor_OneEndReanchorsOtherEndDropped_DropsConnection()
    {
        var comp = CreateStraightWaveGuideWithPhysicalPins();   // "in"/"out"
        var conn = new WaveguideConnection
        {
            StartPin = comp.PhysicalPins.First(p => p.Name == "in"),
            EndPin = comp.PhysicalPins.First(p => p.Name == "out"),
        };

        // Override behält "in", entfernt "out".
        comp.PhysicalPins.Clear();
        comp.PhysicalPins.Add(new PhysicalPin { Name = "in", ParentComponent = comp });

        var result = ConnectionPinReanchorService.Reanchor(comp, new[] { conn });

        result.DroppedConnections.ShouldHaveSingleItem().ShouldBeSameAs(conn);
    }
}
```

- [ ] **Step 2: Tests laufen lassen — müssen FAILEN (Compile-Error: Service existiert nicht)**

```bash
python3 tools/smart_test.py ConnectionPinReanchor
```

Erwartung: Build-Fehler `ConnectionPinReanchorService not found` (oder Test-FAIL nach Stub).

- [ ] **Step 3: Service implementieren**

`Connect-A-Pic-Core/Components/Connections/ConnectionPinReanchorService.cs`:

```csharp
using CAP_Core.Components.Core;

namespace CAP_Core.Components.Connections;

/// <summary>
/// Result of <see cref="ConnectionPinReanchorService.Reanchor"/>: how many connections
/// were re-anchored onto same-named replacement pins, which connections could not be
/// preserved (a referenced pin name no longer exists) and the matching user-facing warnings.
/// The caller is responsible for actually removing <see cref="DroppedConnections"/> from
/// its connection manager / view-model collections.
/// </summary>
public class ReanchorResult
{
    /// <summary>Number of connections whose endpoint(s) were moved to a same-named new pin.</summary>
    public int ReanchoredCount { get; init; }

    /// <summary>Connections referencing a pin name that no longer exists; must be removed by the caller.</summary>
    public IReadOnlyList<WaveguideConnection> DroppedConnections { get; init; } = Array.Empty<WaveguideConnection>();

    /// <summary>One user-facing warning per dropped connection.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Re-anchors waveguide connections after a component's <see cref="Component.PhysicalPins"/>
/// list was replaced by a per-instance Nazca override (issue #561). Endpoints that point at a
/// replaced pin object are moved to the new pin with the same name; if no same-named pin
/// exists, the connection is reported as dropped.
/// </summary>
public static class ConnectionPinReanchorService
{
    /// <summary>
    /// Walks <paramref name="connections"/> and re-anchors every endpoint that belongs to
    /// <paramref name="component"/> but is no longer in its pin list. Does not mutate the
    /// connection collection itself — dropped connections are only reported.
    /// </summary>
    public static ReanchorResult Reanchor(
        Component component, IReadOnlyList<WaveguideConnection> connections)
    {
        var currentPins = new HashSet<PhysicalPin>(component.PhysicalPins);
        var pinsByName = component.PhysicalPins
            .GroupBy(p => p.Name)
            .ToDictionary(g => g.Key, g => g.First());

        int reanchored = 0;
        var dropped = new List<WaveguideConnection>();
        var warnings = new List<string>();

        foreach (var conn in connections)
        {
            var staleStart = IsStaleEndpoint(conn.StartPin, component, currentPins);
            var staleEnd = IsStaleEndpoint(conn.EndPin, component, currentPins);
            if (!staleStart && !staleEnd)
                continue;

            var missingName = FindMissingPinName(conn, staleStart, staleEnd, pinsByName);
            if (missingName != null)
            {
                dropped.Add(conn);
                warnings.Add(
                    $"Connection {conn.StartPin.Name}–{conn.EndPin.Name} removed: " +
                    $"pin '{missingName}' no longer exists after the Nazca override of " +
                    $"'{component.Name}'.");
                continue;
            }

            if (staleStart)
                conn.StartPin = pinsByName[conn.StartPin.Name];
            if (staleEnd)
                conn.EndPin = pinsByName[conn.EndPin.Name];
            reanchored++;
        }

        return new ReanchorResult
        {
            ReanchoredCount = reanchored,
            DroppedConnections = dropped,
            Warnings = warnings,
        };
    }

    /// <summary>True when the pin belongs to the component but was replaced (not in its pin list anymore).</summary>
    private static bool IsStaleEndpoint(
        PhysicalPin pin, Component component, HashSet<PhysicalPin> currentPins)
        => pin.ParentComponent == component && !currentPins.Contains(pin);

    /// <summary>
    /// Returns the first stale endpoint pin name that has no same-named replacement,
    /// or null when every stale endpoint can be re-anchored.
    /// </summary>
    private static string? FindMissingPinName(
        WaveguideConnection conn, bool staleStart, bool staleEnd,
        IReadOnlyDictionary<string, PhysicalPin> pinsByName)
    {
        if (staleStart && !pinsByName.ContainsKey(conn.StartPin.Name))
            return conn.StartPin.Name;
        if (staleEnd && !pinsByName.ContainsKey(conn.EndPin.Name))
            return conn.EndPin.Name;
        return null;
    }
}
```

- [ ] **Step 4: Tests laufen lassen — müssen PASSEN**

```bash
python3 tools/smart_test.py ConnectionPinReanchor
```

Erwartung: alle 5 PASS.

- [ ] **Step 5: Commit**

```bash
git add Connect-A-Pic-Core/Components/Connections/ConnectionPinReanchorService.cs UnitTests/Connections/
git commit -m "(+) ConnectionPinReanchorService: Connections nach Pin-Override umhaengen (#561)"
```

---

### Task 4: Canvas-Integration + Callback-Verdrahtung (TDD für die Canvas-Methode)

**Files:**
- Modify: `CAP.Avalonia/ViewModels/Canvas/DesignCanvasViewModel.cs` (neue Methode `OnComponentPinsChanged`, am Ende des Connection-Management-Abschnitts, nach `GetConnectionForPin`)
- Modify: `CAP.Avalonia/ViewModels/ComponentSettings/ComponentSettingsDialogViewModel.cs` (`Configure`-Parameter `nazcaPinsChanged`, Durchreichung an Editor-VM)
- Modify: `CAP.Avalonia/Views/MainWindow.axaml.cs` (`ShowComponentSettingsDialog`: Callback bauen + übergeben)
- Test: `UnitTests/Canvas/DesignCanvasPinsChangedTests.cs` (neu)

- [ ] **Step 1: Failing Tests für `OnComponentPinsChanged` schreiben**

`UnitTests/Canvas/DesignCanvasPinsChangedTests.cs`:

```csharp
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;
using Shouldly;
using Xunit;
using static UnitTests.TestComponentFactory;

namespace UnitTests.Canvas;

/// <summary>
/// Tests for <see cref="DesignCanvasViewModel.OnComponentPinsChanged"/> (issue #561):
/// after a Nazca override replaced a component's pins, connections are re-anchored or
/// dropped and the canvas pin view-models are refreshed.
/// </summary>
public class DesignCanvasPinsChangedTests
{
    /// <summary>Replaces the component's pins with same-named copies (new objects).</summary>
    private static void ReplacePinsWithSameNames(Component comp)
    {
        var copies = comp.PhysicalPins.Select(p => new PhysicalPin
        {
            Name = p.Name,
            OffsetXMicrometers = p.OffsetXMicrometers,
            OffsetYMicrometers = p.OffsetYMicrometers,
            AngleDegrees = p.AngleDegrees,
            ParentComponent = comp,
        }).ToList();
        comp.PhysicalPins.Clear();
        comp.PhysicalPins.AddRange(copies);
    }

    [Fact]
    public void PinsChanged_SameNames_ConnectionSurvivesOnNewPins()
    {
        var canvas = new DesignCanvasViewModel();
        var compA = CreateStraightWaveGuideWithPhysicalPins();
        var compB = CreateStraightWaveGuideWithPhysicalPins();
        compB.PhysicalX = 500;
        canvas.AddComponent(compA);
        canvas.AddComponent(compB);
        canvas.ConnectPins(
            compA.PhysicalPins.First(p => p.Name == "out"),
            compB.PhysicalPins.First(p => p.Name == "in"));

        ReplacePinsWithSameNames(compA);
        var warnings = canvas.OnComponentPinsChanged(compA);

        warnings.ShouldBeEmpty();
        canvas.Connections.Count.ShouldBe(1);
        canvas.Connections[0].Connection.StartPin
            .ShouldBeSameAs(compA.PhysicalPins.First(p => p.Name == "out"));
    }

    [Fact]
    public void PinsChanged_RemovedPin_ConnectionDroppedEverywhere()
    {
        var canvas = new DesignCanvasViewModel();
        var compA = CreateStraightWaveGuideWithPhysicalPins();
        var compB = CreateStraightWaveGuideWithPhysicalPins();
        compB.PhysicalX = 500;
        canvas.AddComponent(compA);
        canvas.AddComponent(compB);
        canvas.ConnectPins(
            compA.PhysicalPins.First(p => p.Name == "out"),
            compB.PhysicalPins.First(p => p.Name == "in"));

        compA.PhysicalPins.Clear();
        compA.PhysicalPins.Add(new PhysicalPin { Name = "a0", ParentComponent = compA });
        var warnings = canvas.OnComponentPinsChanged(compA);

        warnings.ShouldHaveSingleItem();
        canvas.Connections.ShouldBeEmpty();
        canvas.ConnectionManager.Connections.ShouldBeEmpty();
    }

    [Fact]
    public void PinsChanged_RefreshesPinViewModels()
    {
        var canvas = new DesignCanvasViewModel();
        var comp = CreateStraightWaveGuideWithPhysicalPins();
        canvas.AddComponent(comp);
        var staleVm = canvas.AllPins.First(p => p.Pin.ParentComponent == comp);

        ReplacePinsWithSameNames(comp);
        canvas.OnComponentPinsChanged(comp);

        canvas.AllPins.ShouldNotContain(staleVm);
        canvas.AllPins.Count(p => p.Pin.ParentComponent == comp).ShouldBe(2);
        canvas.AllPins.Where(p => p.Pin.ParentComponent == comp)
            .ShouldAllBe(p => comp.PhysicalPins.Contains(p.Pin));
    }
}
```

- [ ] **Step 2: Tests laufen lassen — müssen FAILEN (Methode existiert nicht)**

```bash
python3 tools/smart_test.py DesignCanvasPinsChanged
```

- [ ] **Step 3: `OnComponentPinsChanged` in `DesignCanvasViewModel` implementieren**

Nach `GetConnectionForPin` (Dateiende, vor der schließenden Klammer) einfügen; `using CAP_Core.Components.Connections;` und `using CAP_Core.Components.Core;` sind in der Datei bereits vorhanden (sonst ergänzen):

```csharp
    /// <summary>
    /// Re-anchors or drops waveguide connections after <paramref name="component"/>'s
    /// physical pins were replaced by a per-instance Nazca override (issue #561),
    /// refreshes the canvas pin view-models, re-routes and invalidates the simulation.
    /// Returns user-facing warnings for connections that had to be dropped.
    /// </summary>
    public IReadOnlyList<string> OnComponentPinsChanged(Component component)
    {
        var result = ConnectionPinReanchorService.Reanchor(
            component, ConnectionManager.Connections);

        foreach (var droppedConn in result.DroppedConnections)
        {
            ConnectionManager.RemoveConnectionDeferred(droppedConn);
            var droppedVm = Connections.FirstOrDefault(c => c.Connection == droppedConn);
            if (droppedVm != null) Connections.Remove(droppedVm);
        }

        RefreshPinViewModels(component);

        if (ConnectionManager.Connections.Count > 0) _ = RecalculateRoutesAsync();
        InvalidateSimulation();
        return result.Warnings;
    }

    /// <summary>
    /// Drops the pin view-models that still reference the component's replaced pins
    /// and re-creates them from the component's current <see cref="Component.PhysicalPins"/>.
    /// </summary>
    private void RefreshPinViewModels(Component component)
    {
        var staleVms = AllPins.Where(p => p.Pin.ParentComponent == component).ToList();
        foreach (var pinVm in staleVms) AllPins.Remove(pinVm);

        var compVm = Components.FirstOrDefault(c => c.Component == component);
        if (compVm == null) return;
        foreach (var pin in component.PhysicalPins)
            AllPins.Add(new PinViewModel(pin, compVm));
    }
```

- [ ] **Step 4: Tests laufen lassen — müssen PASSEN**

```bash
python3 tools/smart_test.py DesignCanvasPinsChanged
```

- [ ] **Step 5: Commit (Canvas-Teil)**

```bash
git add CAP.Avalonia/ViewModels/Canvas/DesignCanvasViewModel.cs UnitTests/Canvas/DesignCanvasPinsChangedTests.cs
git commit -m "(+) DesignCanvasViewModel.OnComponentPinsChanged: Re-Anchor/Drop nach Pin-Override (#561)"
```

- [ ] **Step 6: `Configure`-Parameter ergänzen und durchreichen**

`CAP.Avalonia/ViewModels/ComponentSettings/ComponentSettingsDialogViewModel.cs`, Methode `Configure` (Zeile 166): nach dem Parameter `Action? nazcaDimensionsChanged = null` ergänzen:

```csharp
        Action? nazcaDimensionsChanged = null,
        Action<IReadOnlyList<PhysicalPin>>? nazcaPinsChanged = null)
```

und im Body die Editor-VM-Erzeugung (Zeile ~221) erweitern:

```csharp
            NazcaCodeEditor = new InstanceNazcaCodeEditorViewModel(
                entityKey,
                storedNazcaOverrides,
                liveComponent,
                templateModuleName,
                templateFunctionName ?? string.Empty,
                templateFunctionParameters,
                nazcaTemplateCode,
                nazcaPreviewService,
                nazcaOverlapCheck,
                nazcaDimensionsChanged,
                onChanged,
                nazcaPinsChanged);
```

Falls `PhysicalPin` in der Datei nicht aufgelöst ist: `using CAP_Core.Components.Core;` ergänzen (vermutlich schon da wegen `Component`).

- [ ] **Step 7: Callback in `MainWindow.ShowComponentSettingsDialog` bauen**

`CAP.Avalonia/Views/MainWindow.axaml.cs`: Im Block `if (liveComponent != null && !isTemplateMode)` (Zeile ~625, wo `nazcaDimensionsChanged` gebaut wird) ergänzen — Deklaration zu den anderen lokalen Variablen (Zeile ~624):

```csharp
        Action<IReadOnlyList<CAP_Core.Components.Core.PhysicalPin>>? nazcaPinsChanged = null;
```

im Block:

```csharp
            nazcaPinsChanged = _ =>
            {
                // Issue #561: Connections auf die neuen Override-Pins umhaengen bzw.
                // mit Warnung trennen, Pin-VMs auffrischen, Routen + Simulation neu.
                var warnings = vm.Canvas.OnComponentPinsChanged(liveComponent);
                foreach (var warning in warnings)
                    errorConsole?.Log(CAP_Contracts.Logger.LogLevel.Warning, warning);
                DesignCanvasControl.InvalidateVisual();
            };
```

und im `dialogVm.Configure(...)`-Aufruf als letzten Parameter:

```csharp
            nazcaDimensionsChanged: nazcaDimensionsChanged,
            nazcaPinsChanged: nazcaPinsChanged);
```

Hinweis: die exakte `Log`-Signatur von `ErrorConsoleService` ist `Log(LogLevel level, string message)` (`Connect-A-Pic-Core/ErrorConsoleService.cs:32`); den `LogLevel`-Namespace übernehmen, den die Datei oben bereits verwendet.

- [ ] **Step 8: Build + kompletter Editor-Testlauf**

```bash
dotnet build
python3 tools/smart_test.py PinOverride
python3 tools/smart_test.py ComponentSettings
```

Erwartung: Build ohne neue Warnings, Tests PASS.

- [ ] **Step 9: Commit (Verdrahtung)**

```bash
git add CAP.Avalonia/ViewModels/ComponentSettings/ComponentSettingsDialogViewModel.cs CAP.Avalonia/Views/MainWindow.axaml.cs
git commit -m "(*) Pin-Override-Callback verdrahten: Dialog -> Canvas-Re-Anchor (#561)"
```

---

### Task 5: Gesamtverifikation + PR aktualisieren

**Files:** keine neuen — Verifikation und Push.

- [ ] **Step 1: Voller Build + komplette Testsuite**

```bash
dotnet build
python3 tools/smart_test.py
```

Erwartung: Build ok, **0 Failures** (vorher auf CI: 1 Failure `FileSizeLimitTests`). 5 Skips (MainWindow-UI-Tests) sind normal.

- [ ] **Step 2: Selbst-Review des Diffs**

```bash
git diff main...HEAD --stat
```

Prüfen: keine Formatierungsänderungen an unbeteiligten Dateien, keine Kommentare, die Änderungshistorie erzählen („was vorher hier stand"), XML-Doku auf allen neuen public Members.

- [ ] **Step 3: Push + PR-Beschreibung aktualisieren**

```bash
git push origin agent/issue-561-1781241133
```

Dann die PR-#562-Beschreibung ergänzen (via `gh pr edit 562 --body-file -`): kurz beschreiben, dass der ursprüngliche Agent-Stand um (1) OverridePinMapper-Extraktion (CI-Fix), (2) Connection-Re-Anchoring inkl. Canvas-Verdrahtung und (3) LogicalPin-Übernahme ergänzt wurde, und dass die alte Behauptung „pre-existing failures" falsch war (der FileSizeLimit-Failure kam vom PR selbst).

- [ ] **Step 4: CI abwarten und prüfen**

```bash
gh pr checks 562 --repo aignermax/Lunima --watch
```

Erwartung: `🔍 xUnit Tests` → pass. Bei Failure: Log analysieren (`gh run view <id> --log-failed`), fixen, zurück zu Step 1.

---

## Tool-Usage-Report (CLAUDE.md §8.1)

Am Ende des Issues berichten: verwendete Tools (`smart_test.py`-Läufe, Grep/Read statt MCP) und geschätzte Token-Ersparnis.
