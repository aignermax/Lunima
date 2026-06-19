# Issue #565 — NazcaCoordinateMapper + selbstverifizierender GDS-Export

**Datum:** 2026-06-12 (Design vom User freigegeben; User für 6h abwesend, Umsetzung autonom)
**Branch:** `feature/issue-565-nazca-coordinate-mapper` (von main nach Merge von #562)

## Problem

Die App→Nazca-Koordinaten-Transformation existiert doppelt (`PhysicalPin.GetAbsoluteNazcaPosition`
+ `SimpleNazcaExporter.CalculateOriginOffset`, synchron gehalten nur per Kommentar), ist ein
Heuristik-Stapel (Namens-Mustererkennung, Fallbacks, seit #561 eine dritte Konvention) und hat
keinen maschinellen Verifikations-Regelkreis. Folge: wiederkehrende Alignment-Bugs
(#329/#334/#338/#355/#456/#458, zweimal in #561). Akut: **rotierte Komponenten** (90°) sind im
GDS-Export falsch platziert — Override- UND reguläre PDK-Komponenten.

## Ground Truth: App-Semantik (verbindlich für alle Formeln)

- App-Raum ist Y-down. Komponenten-Box: obere linke Ecke (PhysicalX, PhysicalY), Breite/Höhe
  sind die AKTUELLEN (rotations-getauschten) Werte.
- Rotation (`RotateComponentCommand`): Pin-OFFSETS werden physisch um das Box-Zentrum rotiert,
  Breite/Höhe getauscht, Box-Topleft bleibt fix. `RotationDegrees` (+90 je Schritt) ist reine
  Buchhaltung für Pin-Winkel und Export.
- Pin-Weltposition (App) = (PhysicalX + OffsetX, PhysicalY + OffsetY) — OHNE Rotationsterm,
  weil Offsets vorrotiert sind (`PhysicalPin.GetAbsolutePosition`).
- Pin-Weltwinkel (App) = AngleDegrees + RotationDegrees (`GetAbsoluteAngle`).
- Nazca-Raum ist Y-up. Punktkonversion: (x, y)app → (x, −y)nazca. Winkel: a → −a.
  Nazca-Put-Rotation = −RotationDegrees (bestehende Konvention).

## Lever A: `NazcaCoordinateMapper` (Single Source of Truth)

Neue statische Klasse `Connect-A-Pic-Core/Export/NazcaCoordinateMapper.cs` (< 250 Zeilen).
ALLE App→Nazca-Antworten kommen von hier:

### API

```csharp
/// Where and how the component's Nazca cell is .put(...) in the export.
public record CellPlacement(double X, double Y, double RotationDegrees);
// Anchor ist immer der org-Pin der Zelle: factory().put('org', X, Y, RotationDegrees).

public static class NazcaCoordinateMapper
{
    // (1) Zell-Platzierung. rawOverrideAnchor = (XMin, YMax) aus NazcaCodeOverride,
    //     null für PDK/Legacy. unrotatedW/H siehe "Unrotierte Dimensionen".
    public static CellPlacement GetCellPlacement(Component comp, (double XMin, double YMax)? rawOverrideAnchor);

    // (2) Pin-Weltposition in Nazca — UNIVERSAL: einfache Y-Negation der App-Position.
    public static (double X, double Y) GetPinNazcaPosition(PhysicalPin pin);

    // (3) Pin-Weltwinkel in Nazca.
    public static double GetPinNazcaAngle(PhysicalPin pin);   // = −(AngleDegrees + RotationDegrees), normalisiert

    // (4) Punktkonversion für Pfadsegmente.
    public static (double X, double Y) ToNazca(double appX, double appY);   // = (appX, −appY)
}
```

### Platzierungsformel (generisch, kein Quadranten-Casework)

Eingabe: zellinterne UNROTIERTE bbox `B = [xmin, ymin, xmax, ymax]` (Nazca-Y-up, org = Zellursprung),
Put-Rotation `r = −RotationDegrees`.

1. Vier Ecken von B mit R(r) rotieren → rotierte bbox `[minx′, miny′, maxx′, maxy′]`.
2. Ziel: rotierte Geometrie-bbox liegt auf der App-Box → Nazca-Topleft = (PhysicalX, −PhysicalY).
3. Put-Position `T = (PhysicalX − minx′, −PhysicalY − maxy′)`.
4. Emit: `put('org', T.x, T.y, r)`.

Begründung Konsistenz mit Pins: Die App rotiert Pin-Offsets um das Box-Zentrum und fixiert das
Topleft; Rotation um einen anderen Punkt unterscheidet sich nur um eine Translation, die das
bbox-Re-Anchoring exakt aufhebt. Für 90°-Vielfache (einzige App-Rotationen) gilt
rotierte bbox = getauschte Dimensionen ⇒ Zellgeometrie und App-Pins decken sich exakt.

### bbox-Parameterisierung je Komponentenart (die EINZIGE Verzweigung, an EINER Stelle)

| Art | bbox B |
|---|---|
| Raw-Code-Override (Anker persistiert) | `[XMin, YMax − H₀, XMin + W₀, YMax]` aus `NazcaCodeOverride` (OverrideBboxXMin/YMax + OverrideWidth/Height = unrotierte Werte) |
| PDK / expliziter Kalibrier-Offset (ox, oy) | `[−ox, oy − H₀, −ox + W₀, oy]` — Herleitung: bei rot=0 muss org auf (PhysicalX + ox, −(PhysicalY + oy)) liegen (bestehende, kalibrierte Konvention) |
| Parametric Straight (bestehender Sonderfall) | ox/oy = Offset des ersten Pins (Regel zieht 1:1 aus `SimpleNazcaExporter.CalculateOriginOffset` um) |
| Legacy-Fallback (kein PDK-Name, kein expliziter Offset) | ox=0, oy=H₀ (entspricht heutigem `(0, Height)`-Fallback) |
| Alt-Override OHNE persistierten Anker | wie bisher: Default-Anker-Put ohne org (Legacy-Pfad bleibt im Exporter, dokumentiert) |

Erkennungslogik (PDK-Namensmuster, expliziter Offset, Parametric Straight) zieht aus
`SimpleNazcaExporter.CalculateOriginOffset` UNVERÄNDERT in den Mapper um.

**Unrotierte Dimensionen:** `Component.Width/Height` sind rotations-getauscht. Mapper:
`(W₀, H₀) = RotationDegrees % 180 == 0 ? (W, H) : (H, W)`.

### Pin-Positionen: universelle Y-Negation (bewusste Vereinfachung)

`GetPinNazcaPosition` = `ToNazca(pin.GetAbsolutePosition())` für ALLE Komponentenarten.
Begründung: Das App-Modell ist die Wahrheit dafür, wo Pins SIND (so zeichnet sie der Canvas).
Aufgabe der Kalibrierung (Offset-Editor) ist, die ZELLE so zu platzieren, dass ihre gerenderten
Pins mit den App-Pins zusammenfallen — Kalibrierdaten fließen in die Platzierung (oben), nicht
in eine zweite Pin-Formel. Die bisherige Legacy-Pin-Formel stimmte mit der Y-Negation überein,
wo oy = H/2 (z.B. demofab mmi2x2: org vertikal mittig) — wo sie abwich, war sie Teil des
Problems. Abweichungen Zelle↔App-Pins sind ab jetzt Kalibrierfehler, die Lever B PRO TEMPLATE
sichtbar macht, statt sie in Formelpaaren zu verstecken.

`PhysicalPin.GetAbsoluteNazcaPosition()` wird zum dünnen Delegaten auf den Mapper (Verhalten =
Y-Negation); seine private `CalculateOriginOffset`-Kopie und der „Must match exactly!"-Kommentar
werden GELÖSCHT. Alle Aufrufer werden auditiert und auf den Mapper migriert (bekannt:
SimpleNazcaExporter mehrfach, evtl. PdkOffset-Editor, WaveguideConnection.ExportToNazca —
Letzteres auf toten Code prüfen).

## Lever B: Selbstverifizierender Export

### Verifikations-Epilog im Exporter

`SimpleNazcaExporter.Export(..., bool emitVerification = false)`. Bei true hängt der Footer an
(nach `design.put()` / `export_gds`):

```python
# --- Alignment verification (machine-readable) ---
import json as _json
_verify = {}
for _name, _inst in [('comp_0', comp_0), ('comp_1', comp_1), ...]:   # aus componentNames generiert
    _pins = {}
    for _pn, _pin in _inst.pin.items():
        _px, _py, _pa = _pin.xya()
        _pins[_pn] = [_px, _py, _pa]
    _verify[_name] = _pins
with open(os.path.splitext(script_path)[0] + '.pins.json', 'w') as _f:
    _json.dump(_verify, _f)
```

Liefert die TATSÄCHLICHEN Welt-Pinpositionen aller platzierten Instanzen — von derselben
Nazca-Engine, die das GDS schreibt. (GDS selbst kennt keine Pins; die Wahrheit kommt aus der
Nazca-Introspektion, genau wie im Preview-Skript.)

### Integrationstest `UnitTests/Integration/GdsExportAlignmentTests.cs`

Env-Skip-Muster wie `NazcaEditorPreviewIntegrationTests` (kein nazca-Python → return, CI grün).
Matrix: **{demo-PDK `demo.mmi2x2_dp`, Raw-Code-Override (Showcase-Beispiel)} × {0°, 90°, 180°, 270°}**,
je mit einer Verbindung zu einer zweiten Komponente. Pro Zelle der Matrix:

1. Design im Canvas bauen (Override-Fall: Editor-VM-Apply wie in `NazcaOverrideFullFlowIntegrationTests`),
   Komponente k-mal via `RotateComponentCommand` drehen.
2. Export mit `emitVerification: true`, Skript ausführen (`GdsExportService`, discovered python).
3. Asserts: (a) Exit 0 + .gds existiert; (b) `.pins.json`: für jeden App-Pin der Komponente
   stimmt die Nazca-Ist-Position mit `NazcaCoordinateMapper.GetPinNazcaPosition` überein
   (ε = 0,01 µm; Instanz-Pins per Name gematcht, nazca-interne Namen org/cc/Ecken ignoriert);
   (c) die im Skript emittierten Segment-Endkoordinaten treffen die erwarteten Pin-Positionen (ε).

Rotationen laufen NICHT als Theory-Einzeltests gegen python (Laufzeit), sondern als Schleife in
wenigen Facts (~2 Facts × 4 Rotationen, ≈ 8 Skript-Läufe, grob 1 Minute auf Dev-PC).

### Unit-Tests `UnitTests/Export/NazcaCoordinateMapperTests.cs`

Reine Mathematik, ohne Python: Platzierungsformel für beide Parameterisierungen × 4 Rotationen
(Erwartungswerte von Hand hergeleitet), Pin-Position/Winkel-Konversion, unrotierte Dimensionen,
Legacy-/Parametric-Regeln, Alt-Override-Fallback.

## Konsumenten-Migration (vollständige Liste)

| Stelle | Änderung |
|---|---|
| `SimpleNazcaExporter.AppendSingleComponent` | Platzierung + `# COORD`/`# PIN`-Diagnosen via Mapper; eigene `CalculateOriginOffset`/`RotateOffset`/`GetOverrideBboxAnchor`-Helfer entfallen bzw. wandern in den Mapper |
| `SimpleNazcaExporter.AppendSegmentExport` | Start-Pin-Offset-Maschinerie ENTFÄLLT (universelle Y-Negation ⇒ Offset ist immer 0): Segmente direkt via `ToNazca` |
| `SimpleNazcaExporter.FormatStraightSegmentFromPins` | beide Endpunkte via `GetPinNazcaPosition` |
| `SimpleNazcaExporter.BuildEndpointReference` (p2p) | PDK-Tupel via `GetPinNazcaPosition`/`GetPinNazcaAngle`; Override weiterhin Pin-Referenz |
| `SimpleNazcaExporter.GetNazcaPinPosition` (Helfer aus a0d26201) | entfällt zugunsten des Mappers |
| `PhysicalPin.GetAbsoluteNazcaPosition` + private `CalculateOriginOffset` | Delegat auf Mapper (Y-Negation); Kopie gelöscht |
| Aufrufer-Audit (`GetAbsoluteNazcaPosition`-Verwendungen außerhalb) | migrieren oder als korrekt bestätigen; `WaveguideConnection.ExportToNazca` auf toten Code prüfen, ggf. löschen |
| PdkOffset-Editor | UNVERÄNDERT (Kalibrierdaten sind Mapper-Input); nur prüfen, dass er nicht von der alten Pin-Formel abhängt |

## Bewusste Verhaltensänderungen

1. Exporte ROTIERTER Komponenten (PDK + Override) ändern sich — das ist der Fix.
2. Segment-/Pin-Koordinaten von Komponenten mit oy ≠ H/2 ändern sich (Y-Negation statt
   Legacy-Formel) — Lever-B-Matrix weist die Korrektheit auf dem Dev-PC nach; bestehende
   Exporter-Unit-Tests, die das alte Verhalten asserten, werden bewusst angepasst.
3. Alt-Overrides ohne bbox-Anker: unverändert Legacy-Placement (kein Anspruch auf Alignment).

## Akzeptanzkriterien

- [ ] Eine einzige Koordinaten-Implementierung; „Must match exactly!"-Kommentarpaar entfernt.
- [ ] `GdsExportAlignmentTests`-Matrix (2 Arten × 4 Rotationen) grün auf nazca-fähigem Rechner;
      sauberer Env-Skip ohne nazca.
- [ ] Mapper-Unit-Tests decken Platzierungsformel und alle Parameterisierungen ab.
- [ ] Komplette Suite grün; `FileSizeLimitTests` eingehalten (kein File > 500 Zeilen).
- [ ] PR gegen main mit Review-Run; bestätigte Findings gefixt.

## Folgerichtung (nicht dieses Issue)

Ansatz 3 „volle Render-Wahrheit": Auch PDK-Pin-/Platzierungsdaten pro Template aus echten
Renders ableiten (Preview-Skript liefert sie bereits) und die Namens-Heuristik abschaffen.
Der Mapper kapselt die Verzweigung so, dass dieser Schritt später nur EINE Stelle ändert.
