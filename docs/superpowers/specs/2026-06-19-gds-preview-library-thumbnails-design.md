# GDS-Geometrie in Library-Thumbnails

**Datum:** 2026-06-19
**Status:** Design genehmigt, bereit für Implementierungsplanung

## Ziel

Die Komponenten-Thumbnails in der **Component Library** (linkes Panel) sollen die
echte gerenderte GDS-Geometrie **plus Pin-Marker** anzeigen — so wie eine platzierte
Komponente auf dem Canvas — statt nur der schematischen Rechteck-mit-Pins-Box. Das
Rendering läuft im Hintergrund (lazy, nur für sichtbare Einträge), wird in-memory und
zusätzlich persistent auf Festplatte gecacht, und fällt sauber auf die bestehende
Schema-Box zurück, wenn keine Geometrie verfügbar ist.

## Kontext / bestehende Infrastruktur

Der Canvas rendert die GDS-Geometrie platzierter Komponenten bereits über eine
vorhandene Pipeline, die größtenteils wiederverwendet wird:

- `NazcaComponentPreviewService` (Core) — rendert via Python/KLayout die Geometrie
  einer Nazca-Funktion zu einem `NazcaPreviewResult` (Polygone je Layer + Bounding-Box).
- `GdsPreviewRenderService` (CAP.Avalonia) — async Fetch + In-Memory-LRU-Cache
  (`GdsPreviewCache`, 50 Einträge, Session-Lifetime). Schlüssel:
  `"{NazcaFunctionName}|{Width:F2}|{Height:F2}"`. Arbeitet aktuell pro `ComponentViewModel`.
- `GdsPolygonRenderer` — zeichnet Polygone **vektoriell** in einen `DrawingContext`
  (`DrawPolygonsAsGeometry`, scharf bei jeder Größe) oder rasterisiert zu einem Bitmap
  (`RasterizeToBitmap`). Y-Flip, Layer-Farben, bbox-Transform sind hier gekapselt.
- Die Library-Thumbnails verwenden heute `ComponentPreview` (Avalonia-`Control`), die in
  `Render()` ein Rechteck + Pins zeichnet. Datenquelle ist `ComponentTemplate` (vmLib),
  das `NazcaModuleName`, `NazcaFunctionName`, `PinDefinitions`, `WidthMicrometers`,
  `HeightMicrometers` trägt.

Kernidee: die vorhandene Render-/Cache-Pipeline für die Library-Thumbnails
wiederverwenden — **kein** zweites Rendering-System.

## Architektur (vier Einheiten)

### 1. `GdsPreviewDiskCache` (neu)

Persistiert `NazcaPreviewResult` (Polygone + bbox) als JSON, damit Previews über
App-Neustarts hinweg sofort verfügbar sind.

- **Speicherort:** plattformneutral über
  `Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)` +
  `/Lunima/gds-preview-cache/`. Das löst auf jeder Plattform zum passenden Ort auf:
  - **Windows:** `%LOCALAPPDATA%` (z.B. `C:\Users\<user>\AppData\Local\Lunima\gds-preview-cache\`)
  - **Linux:** `$XDG_DATA_HOME` bzw. `~/.local/share/Lunima/gds-preview-cache/`
  - **macOS:** das von .NET gelieferte App-Daten-Äquivalent (`~/.local/share/...`)

  **Kein** hartcodiertes `%LOCALAPPDATA%` — ausschließlich die .NET-API. Das Verzeichnis
  wird beim ersten Schreiben angelegt (`Directory.CreateDirectory`). Eine JSON-Datei je
  Cache-Key.
- **Key:** stabiler Hash (SHA-256, gekürzt) aus `module|function|parameters|formatVersion`.
  **Auflösungsunabhängig** — bewusst *ohne* Breite/Höhe (die Geometrie hängt nur von der
  Nazca-Funktion + Parametern ab; Breite/Höhe sind reine Anzeige-Transforms). Das ist eine
  Verbesserung gegenüber dem aktuellen In-Memory-Key, der Breite/Höhe enthält.
- **Format-Version:** Konstante im Key-Material. Erhöhen invalidiert alle Einträge, falls
  sich das serialisierte Format oder die Render-Semantik ändert.
- **Leer-Markierung:** Ein erfolgloser Render (kein Ergebnis / 0 Polygone) wird als
  „leer" persistiert, damit kein Dauer-Retry über Sessions entsteht.
- **Robustheit:** Lese-/Schreibfehler sind best-effort (kaputte/teilweise Dateien werden
  ignoriert und neu erzeugt) — ein Cache-Fehler darf das Rendering nie crashen.

### 2. `GdsPreviewRenderService` (erweitern)

- **Input generalisieren:** Statt fest `ComponentViewModel` ein minimaler Render-Identitäts-
  Input (`module`, `function`, `parameters`). Canvas (über `ComponentViewModel`) **und**
  Library (über `ComponentTemplate`) liefern denselben Input. Bestehende Canvas-Aufrufer
  bleiben über eine dünne Überladung/Adapter erhalten.
- **Lookup-Kette:** In-Memory-LRU → `GdsPreviewDiskCache` → Python-Render. Ein erfolgreicher
  Python-Render wird in beide Caches geschrieben; ein Disk-Treffer füllt den In-Memory-Cache.
- **Concurrency-Drossel:** `SemaphoreSlim` (Standard 3 gleichzeitige Renders), damit nicht
  viele gleichzeitig sichtbare neue Thumbnails dutzende Python-Prozesse starten (analog zum
  FDTD-Solve-Gate).
- `OnPreviewLoaded`-Callback bleibt; Library und Canvas hängen ihr jeweiliges
  `InvalidateVisual` daran.

### 3. `ComponentPreview`-Control (erweitern)

- **Service-Zugriff:** über eine Avalonia-`AttachedProperty` (in `MainWindow.axaml` an die
  Thumbnails gebunden, aus `MainViewModel.GdsPreviewRenderService`). So bleibt die Control
  DI-frei und testbar, ohne globalen Singleton.
- **Render():**
  1. Render-Input aus den (vorhandenen) Template-Properties bilden.
  2. `TryGetPreview(...)` → bei vorhandenen Polygonen die Geometrie **vektoriell** in die
     Control-Bounds skaliert zeichnen (über `GdsPolygonRenderer`), darüber die bestehende
     **Pin-Marker**-Zeichnung.
  3. Sonst (Fetch pending / kein Preview) → die bestehende Schema-Box zeichnen und bei
     Fertigstellung über `OnPreviewLoaded` → `InvalidateVisual` neu zeichnen.
- **Lazy:** Da die Library-Liste virtualisiert, ruft nur ein sichtbares Thumbnail `Render()`
  auf → es wird nur gerendert/gefetcht, was tatsächlich sichtbar ist.

### 4. `NazcaPreviewResult`-Serialisierung (neu, klein)

- Ein DTO + `System.Text.Json`-Konverter für `NazcaPreviewResult` (Polygone als
  Vertex-Listen + Layer, bbox `XMin/XMax/YMin/YMax`). Wird vom Disk-Cache genutzt.
- Falls `NazcaPreviewResult` direkt serialisierbar gemacht werden kann (reine Daten), ein
  schlankes DTO bevorzugen, um das Disk-Format vom internen Typ zu entkoppeln (stabile
  Versionierung).

## Datenfluss

```
Thumbnail sichtbar
  → ComponentPreview.Render()
    → GdsPreviewRenderService.TryGetPreview(renderInput, w, h)
        → In-Memory-LRU?  ── ja → Polygone zurück
        → Disk-Cache?     ── ja → In-Memory füllen, Polygone zurück
        → sonst: Python-Render (gedrosselt) → Disk + In-Memory schreiben → OnPreviewLoaded
  → Polygone vorhanden → GDS vektoriell zeichnen + Pins drüber
  → sonst → Schema-Box (Fallback)
```

## Fehlerbehandlung / Fallback

- Kein `NazcaFunctionName` (Built-in / External-Port-Komponenten) → sofort Schema-Box,
  kein Fetch.
- Python nicht verfügbar / Skript-Timeout / 0 Polygone → Schema-Box, als „leer" in beide
  Caches geschrieben (kein Retry-Spam, auch nicht über Sessions).
- Disk-Cache-I/O-Fehler → still ignorieren, auf In-Memory + Render zurückfallen.

## Abgrenzung / Nicht-Ziele

- Kein neues Rendering-System; `GdsPolygonRenderer`/`NazcaComponentPreviewService` werden
  wiederverwendet.
- Kein Vorab-Rendern aller Templates (nur lazy/sichtbar).
- Keine Änderung am Canvas-Verhalten außer der Service-Input-Generalisierung
  (verhaltensneutral für bestehende Canvas-Aufrufer).

## Tests

- `GdsPreviewDiskCache`: Roundtrip (schreiben → lesen ergibt gleiche Polygone/bbox),
  Leer-Markierung wird respektiert, Format-Versions-Wechsel invalidiert, kaputte Datei
  wird tolerant behandelt. Tests nutzen ein temporäres Cache-Verzeichnis (kein echtes
  LocalAppData).
- Render-Identitäts-Key: stabil für gleiche Inputs, verschieden bei abweichendem
  Modul/Funktion/Parameter, unabhängig von Breite/Höhe.
- `GdsPreviewRenderService`-Lookup-Pfad: In-Memory-Treffer überspringt Disk/Python;
  Disk-Treffer füllt In-Memory; Drossel begrenzt gleichzeitige Renders.
- `NazcaPreviewResult`-Serialisierung: Roundtrip-Gleichheit.
- UI-Zeichnen (`ComponentPreview.Render`) bleibt manuell/visuell verifiziert (Avalonia-
  Rendering ist nicht sinnvoll unit-testbar).

## Offene/abhängige Punkte

- Baut konzeptionell auf dem aktuellen `ComponentPreview`/Library-Layout auf; falls das
  Left-Panel-PR (#587) zuerst merged, wird dieser Branch darauf rebased.
- Standardwerte (Drossel = 3, Cache-Format-Version = 1, Cache-Pfad) sind im Design fixiert
  und können bei Bedarf in der Implementierung als Konstanten zentralisiert werden.
