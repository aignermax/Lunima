# Issue #561 — Override-Pins fertigstellen (PR #562 vervollständigen)

**Datum:** 2026-06-12
**Scope:** Teile 1+2 aus Issue #561 — Pins aus dem Nazca-Override neu ableiten und
In-App-Connections korrekt nachziehen. Aufbau auf PR #562
(Branch `agent/issue-561-1781241133`), der Teil 1 + Export-Wiring bereits enthält.

**Nicht im Scope:** S-Matrix-Neuberechnung für neue Ports (eigener Themenkomplex,
siehe Issue #561 Teil 4), Auto-Re-Routing-Dialog bei Geometrieänderungen.

## Ausgangslage (PR #562)

Der PR ersetzt beim Apply die `Component.PhysicalPins` aus `NazcaPreviewResult.Pins`
(`BuildOverridePins`: Nazca→lokal-µm mit Y-Flip), persistiert `OverridePins` +
`TemplatePins` in `NazcaCodeOverride`, stellt sie beim Laden wieder her
(`FileOperationsViewModel.RestorePinsFromOverride`) und hebt den Export-Skip aus
#559 auf. CI ist rot, drei Lücken bleiben:

1. **Dateigrößen-Limit:** `InstanceNazcaCodeEditorViewModel.cs` hat 604 Zeilen,
   `FileSizeLimitTests` erlaubt hart 500.
2. **Teil 2 unverdrahtet:** Der neue `onPinsChanged`-Callback des Editor-VMs wird
   von `ComponentSettingsDialogViewModel.Configure` / `MainWindow.axaml.cs` nie
   übergeben. Bestehende `WaveguideConnection`s referenzieren nach Apply weiter die
   alten, aus der Komponente entfernten `PhysicalPin`-Objekte — sie rendern an
   alten Positionen und speisen über deren alte `LogicalPin`s weiter die Simulation.
3. **`LogicalPin` geht immer verloren:** Die neuen Pins haben `LogicalPin = null`,
   auch bei namensgleichen Ports. `GetConnectionTransfers()` überspringt solche
   Pins stillschweigend → Komponente fällt lautlos aus der Simulation, obwohl
   `HasNoSimulationModel = false` meldet.

## Design

### 1. `OverridePinMapper` extrahieren (fixt CI)

Neue Klasse `CAP.Avalonia/ViewModels/ComponentSettings/InstanceOverride/OverridePinMapper.cs`
(statisch, < 250 Zeilen) übernimmt aus dem Editor-VM:

- `BuildOverridePins(NazcaPreviewResult)` — Preview-Pins → `OverridePinData`
- `PinNamesMatch(a, b)` — Namens-Set-Vergleich
- `CaptureAsPinData(pins)` — Snapshot der aktuellen Pins
- `ApplyPinsToComponent(component, pinData)` — Pins ersetzen, dabei **`LogicalPin`
  per Name vom alten Pin übernehmen** (siehe 3.)

`FileOperationsViewModel.RestorePinsFromOverride` (Duplikat von
`ApplyPinsToComponent`) entfällt und ruft den Mapper auf — damit gilt die
LogicalPin-Übernahme auch beim Projekt-Laden.

### 2. Connection-Re-Anchoring verdrahten (Teil 2)

Callback-Kette: `MainWindow.ShowComponentSettingsDialog` baut einen
`nazcaPinsChanged`-Callback → `ComponentSettingsDialogViewModel.Configure`
(neuer Parameter) → `InstanceNazcaCodeEditorViewModel` (Parameter existiert schon).

Verhalten beim Apply/Reset (Kernlogik als testbare Klasse
`ConnectionPinReanchorService` in `Connect-A-Pic-Core`, nicht im UI-Code):

- Für jede Connection, deren Start- oder End-Pin zur überschriebenen Komponente
  gehört, aber nicht mehr in `Component.PhysicalPins` liegt:
  - Existiert im neuen Pin-Set ein Pin **gleichen Namens** → Connection-Endpunkt
    auf den neuen Pin umhängen (Re-Anchor). Verdrahtung bleibt erhalten.
  - Sonst → Connection trennen (`RemoveConnectionDeferred`) und Warnung über die
    `ErrorConsoleService` ausgeben („Connection X–Y getrennt: Pin ‚b0' existiert
    im Override nicht mehr").
- Danach `RecalculateAllTransmissionsAsync` anstoßen und Canvas neu zeichnen
  (wie `nazcaDimensionsChanged` es bereits tut).

### 3. `LogicalPin`-Übernahme per Name

`ApplyPinsToComponent` bekommt die alten Pins als Lookup: trägt ein neuer Pin
denselben Namen wie ein alter, wird dessen `LogicalPin` übernommen. Damit bleibt
die Template-S-Matrix für namensgleiche Overrides gültig — konsistent mit der
bestehenden `HasNoSimulationModel`-Semantik (nur `true` bei abweichenden Namen).
Pins ohne Namens-Match behalten `LogicalPin = null` (kein Simulationsmodell,
Flag ist dann ohnehin gesetzt).

Beim Projekt-Laden gilt dieselbe Logik: Die Komponente wird zuerst mit
Template-Pins (inkl. `LogicalPin`s) erzeugt, dann ersetzt der Mapper sie durch
die Override-Pins und übernimmt die `LogicalPin`s namensbasiert.

## Tests

- `OverridePinMapperTests`: Koordinaten-Mapping (bestehende Fälle aus
  `InstanceNazcaCodeEditorPinOverrideTests` wandern mit), LogicalPin-Übernahme
  bei Namens-Match, `null` bei neuem Namen.
- `ConnectionPinReanchorServiceTests`: Re-Anchor bei gleichem Namen, Drop +
  Warnung bei entferntem Pin, Connections fremder Komponenten unangetastet.
- Load-Pfad: Override-Pins nach Laden mit LogicalPin-Verknüpfung bei Namens-Match.
- Bestehende Tests aus PR #562 bleiben erhalten (ggf. an Mapper-API angepasst).

## Akzeptanzkriterien

- [ ] CI grün (insbesondere `FileSizeLimitTests`).
- [ ] Nach Apply zeigen Canvas/Export die Override-Pins; Connections mit
      namensgleichen Pins bleiben verbunden, andere werden mit Warnung getrennt.
- [ ] Namensgleiche Overrides simulieren weiter korrekt (LogicalPin übernommen);
      abweichende Ports melden `HasNoSimulationModel`.
- [ ] Verhalten überlebt Speichern + Laden (.lun-Round-Trip).
