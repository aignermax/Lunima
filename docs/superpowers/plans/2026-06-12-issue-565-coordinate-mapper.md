# Issue #565 NazcaCoordinateMapper Implementation Plan

> **For agentic workers:** Tasks werden sequenziell von Workflow-Agents implementiert; nach jedem Task läuft ein Verifikations-Agent. Spec (VERBINDLICH, zuerst lesen): `docs/superpowers/specs/2026-06-12-issue-565-nazca-coordinate-mapper-design.md`.

**Goal:** Single Source of Truth `NazcaCoordinateMapper` für alle App→Nazca-Koordinaten + selbstverifizierender GDS-Export; fixt Rotations-Misalignment für PDK- und Override-Komponenten.

**Branch:** `feature/issue-565-nazca-coordinate-mapper` (bereits ausgecheckt). Tests: `$env:PYTHONUTF8='1'; py "$env:USERPROFILE\.cap-tools\smart_test.py" <Pattern>` — NIE `dotnet test`. Harte Limits: kein File > 500 Zeilen (CI), neue Files ≤ 250 Zeilen, XML-Doku auf allen public Members, Kommentare erklären WARUM (nie Änderungshistorie).

---

### Task 1: `NazcaCoordinateMapper` + Unit-Tests (TDD)

**Files:**
- Create: `Connect-A-Pic-Core/Export/NazcaCoordinateMapper.cs`
- Create: `UnitTests/Export/NazcaCoordinateMapperTests.cs`

API, Platzierungsformel, bbox-Parameterisierungstabelle, unrotierte Dimensionen: exakt wie im Spec-Abschnitt „Lever A". Die Erkennungslogik (PDK-Namensmuster `IsPdkFunction`-Liste, expliziter Offset, Parametric-Straight, Legacy-Fallback) wird aus `CAP.Avalonia/Services/SimpleNazcaExporter.cs::CalculateOriginOffset` (~Zeile 415) und `IsPdkFunction`/`IsParametricStraight` VERHALTENSGLEICH übernommen (Code dort lesen!). `IsPdkFunction`/`IsParametricStraight` ziehen mit in den Mapper um (public/internal), der Exporter referenziert sie von dort (Task 2 entfernt die Originale).

Methodensignaturen (verbindlich):
```csharp
public record CellPlacement(double X, double Y, double RotationDegrees);
public static class NazcaCoordinateMapper
{
    public static CellPlacement GetCellPlacement(Component comp, (double XMin, double YMax)? rawOverrideAnchor);
    public static (double X, double Y) GetPinNazcaPosition(PhysicalPin pin);
    public static double GetPinNazcaAngle(PhysicalPin pin);
    public static (double X, double Y) ToNazca(double appX, double appY);
}
```

TDD-Reihenfolge: Tests zuerst (rot), dann implementieren. Pflicht-Testfälle (Erwartungswerte von Hand herleiten, im Test als Kommentar begründen):
1. Override-Anker (XMin=−3, YMax=10, W₀=45, H₀=11, comp 100/50) × Rotation 0/90/180/270 — bei 0°: T=(103, −60). Für 90/180/270 die vier rotierten bbox-Ecken von Hand rechnen (r = −RotationDegrees!), Component.Width/Height VOR dem Assert rotationsgerecht tauschen (App-Semantik!).
2. PDK expliziter Offset (ox=0, oy=30, W₀=250, H₀=60) × 0/90 — bei 0°: T=(PhysX, −(PhysY+30))… ACHTUNG Spec: T=(PhysX+ox−…)= bei ox=0: minx′=−0 ⇒ T.x=PhysX; maxy′=30 ⇒ T.y=−PhysY−30 ✓ (= heutiges Verhalten).
3. Legacy-Fallback (kein PDK-Name, Offsets 0): bei 0° T=(PhysX, −(PhysY+H₀)).
4. Parametric Straight: ox/oy = Offset des ersten Pins (Regel aus Exporter übernehmen).
5. `GetPinNazcaPosition` = (app.X, −app.Y); `GetPinNazcaAngle` = −(AngleDegrees+RotationDegrees) normalisiert auf (−180, 180] oder [0,360) — Konvention der bestehenden Ausgaben beibehalten (Exporter emittiert „F0", `NormalizeZero`).
6. rawOverrideAnchor=null + kein PDK-Name → Legacy; rawOverrideAnchor=null + PDK-Name → Kalibrier-Pfad.

Helper: `TestComponentFactory` für Components; für Rotation die Offsets/Dims von Hand setzen (NICHT RotateComponentCommand im Core-Test — der liegt in CAP.Avalonia; stattdessen App-Semantik im Test nachbilden: Offsets rotiert, Dims getauscht, RotationDegrees gesetzt — als Helper im Testfile).

Commit: `(+) NazcaCoordinateMapper: Single Source of Truth fuer App->Nazca-Koordinaten (#565)`

---

### Task 2: Exporter-Migration

**Files:**
- Modify: `CAP.Avalonia/Services/SimpleNazcaExporter.cs`
- Modify: `UnitTests/Services/SimpleNazcaExporterTests.cs` (Erwartungen für rotierte/oy≠H/2-Fälle bewusst anpassen)

Migrationsliste exakt wie Spec-Tabelle „Konsumenten-Migration": Placement (`AppendSingleComponent`) via `GetCellPlacement` (Override-mit-Anker → org-Put; Alt-Override ohne Anker → bisheriger Default-Put bleibt!); `# COORD`/`# PIN`-Diagnosen via Mapper; `AppendSegmentExport`-Offset-Maschinerie ersatzlos streichen (nur noch `ToNazca` je Segmentpunkt); `FormatStraightSegmentFromPins` + `BuildEndpointReference` via Mapper; lokale Helfer `CalculateOriginOffset`, `RotateOffset`, `GetOverrideBboxAnchor` (Anker-Extraktion in den Aufruf inline/Mapper), `GetNazcaPinPosition`, `IsPdkFunction`, `IsParametricStraight` LÖSCHEN (Mapper ist Heimat).

WICHTIG: Bestehende Tests, die heutiges Verhalten asserten, einzeln durchgehen: Tests für UNROTIERTE kalibrierte Fälle MÜSSEN unverändert grün bleiben (Formel ist bei rot=0 verhaltensgleich — wenn so ein Test bricht, ist die Implementierung falsch, NICHT der Test!). Nur Tests, deren Fixture oy ≠ H/2-Pin-Mathe oder Rotation prüft, dürfen angepasst werden — je mit Begründung im Assert-Kommentar.

Danach: `py …smart_test.py SimpleNazcaExporter`, `NazcaOverrideFullFlow`, `NazcaCoordinateMapper` grün; `dotnet build` 0 Fehler.

Commit: `(~) Exporter auf NazcaCoordinateMapper migriert; Formelkopie geloescht (#565)`

---

### Task 3: `PhysicalPin`-Delegation + Aufrufer-Audit

**Files:**
- Modify: `Connect-A-Pic-Core/Components/Core/PhysicalPin.cs`
- Modify: betroffene Aufrufer (Audit!)

`GetAbsoluteNazcaPosition()` → Body = `NazcaCoordinateMapper.GetPinNazcaPosition(this)`; private `CalculateOriginOffset` + „Must match…"-Kommentar löschen; XML-Doku ehrlich neu (Y-Negation, Verweis auf Mapper). DANN: `grep GetAbsoluteNazcaPosition` über das ganze Repo — jeden Aufrufer lesen und entscheiden: bleibt korrekt (dokumentieren im Report) oder migrieren. `WaveguideConnection.ExportToNazca` prüfen: wird es noch aufgerufen? Wenn tot → löschen (mit Aufrufer-Beweis im Report). PdkOffset-Editor-Code prüfen, dass keine Abhängigkeit auf die alte Formel-Semantik bricht (PdkOffsetEditorViewModel*).

Volle Suite + FileSizeLimit prüfen. Commit: `(~) PhysicalPin.GetAbsoluteNazcaPosition delegiert an Mapper; Legacy-Formel entfernt (#565)`

---

### Task 4: Verifikations-Epilog + `GdsExportAlignmentTests` (Lever B)

**Files:**
- Modify: `CAP.Avalonia/Services/SimpleNazcaExporter.cs` (`Export(..., bool emitVerification = false)`, Epilog exakt wie Spec; componentNames-Map liefert die Variablennamen)
- Create: `UnitTests/Integration/GdsExportAlignmentTests.cs`
- Modify (falls nötig): `UnitTests/Services/SimpleNazcaExporterTests.cs` (Test: Epilog wird nur bei Flag emittiert)

Testaufbau exakt wie Spec „Lever B": Env-Skip-Muster + Python-Discovery aus `UnitTests/Integration/NazcaOverrideFullFlowIntegrationTests.cs` kopieren (gleiches Muster). Zwei Facts:
- `PdkComponent_AllRotations_PinsAlignInGds`: `demo.mmi2x2_dp`-Komponente aus der Template-Library (`TestPdkLoader.LoadAllTemplates`, Name „2x2 MMI Coupler"? — exakten Namen im Library-Code nachschlagen; sonst das im Repo vorhandene Template mit nazcaFunction `demo.mmi2x2_dp`), verbunden mit einem zweiten MMI; Schleife k=0..3: Rotation via `RotateComponentCommand` (CAP.Avalonia-Referenz ist im UnitTests-Projekt vorhanden), Export mit `emitVerification: true` in Temp-Dir, `GdsExportService` (SetCustomPythonPath!) ausführen, `.pins.json` lesen (System.Text.Json), für jeden `comp.PhysicalPins`-Pin: Ist-Position der gleichnamigen Instanz-Pins (nazca-interne Namen org/cc/lb/lc/lt/tl/tc/tr/rt/rc/rb/br/bc/bl ignorieren) gegen `NazcaCoordinateMapper.GetPinNazcaPosition` mit ε=0,01 asserten. Aussagekräftige Fehlermeldung: Pin-Name, Rotation, Ist, Soll, Skriptpfad.
- `OverrideComponent_AllRotations_PinsAlignInGds`: wie Full-Flow-Test ein Override per Editor-VM-Apply (Showcase-Code) aufbauen, dann dieselbe Rotationsschleife.
Beide Facts loggen pro Rotation die Dauer nicht — schlict halten. Erwartete Gesamtlaufzeit ≈ 1 Minute.

ACHTUNG Erwartung: Diese Tests sind die EIGENTLICHE Abnahme. Wenn eine Rotations-Zelle FAILT, ist das ein echter Befund: Mapper-Mathe gegen Nazca-Realität prüfen (Skript + .pins.json im Report zitieren), Implementierung fixen, NICHT den Test aufweichen. Genau dafür gibt es diese Schleife.

Commit: `(+) Selbstverifizierender GDS-Export: Pin-Epilog + Alignment-Testmatrix (#565)`

---

### Task 5: Gesamtverifikation

`dotnet build` (0 Fehler), komplette Suite (`py …smart_test.py`, 0 Failures), FileSizeLimit, Diff-Review (keine Fremddateien, keine Change-Narration-Kommentare). Tool-Usage-Report gemäß CLAUDE.md §8.1 in den Task-Report.

---

### Danach (macht der Orchestrator, nicht die Task-Agents)

PR gegen main, Review-Run, Findings fixen, CI.
