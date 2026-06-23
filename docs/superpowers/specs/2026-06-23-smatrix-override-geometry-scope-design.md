# S-Matrix-Override nach Geometrie-Identität (+ Garbage Collection)

**Datum:** 2026-06-23
**Status:** Design genehmigt, bereit für Implementierungsplanung

## Problem

Eine per FDTD neuberechnete (oder importierte) S-Matrix wird heute pro **Instanz**
gespeichert — Schlüssel = `component.Identifier` (Canvas-/Hierarchy-Pfad). Dupliziert
man das Bauteil (Strg+C/V), bekommt die Kopie einen neuen Identifier und damit keinen
Override → der Override „verschwindet" für die Kopie, und andere baugleiche Instanzen
erben ihn nie.

`Component.Clone()` selbst überträgt die S-Matrix-Werte korrekt (Repro-Test
`ComponentCloneOrphanPinTests.Clone_PreservesOverriddenSMatrixValue_NotJustStructure`).
Der Verlust sitzt also nicht im Klonen, sondern im **per-Identifier-Schlüssel** des
Override-Stores (vgl. Issues #583, #580E).

## Grundprinzip

Die S-Matrix ist eine Funktion der **Geometrie** (gegeben der Prozess), nicht des
Namens oder der Instanz. Deckungsgleiche Geometrie ⇒ gleiche S-Matrix. Also ist der
natürliche Override-Schlüssel die **Geometrie-Identität** des Bauteils.

## Design

### 1. Geometrie-Identität als Override-Schlüssel

Für ein Bauteil:
- **Aktiver Raw-Code-Override (#561)** vorhanden → Schlüssel = stabiler Hash des
  Override-Codes (gleicher Code ⇒ gleicher Schlüssel ⇒ gleiche S-Matrix).
- **Sonst** → `module | function | parameters` (keine/gleiche Parameter ⇒ gleicher
  Schlüssel). Parametrisierte Varianten (z. B. unterschiedliche Koppler-Länge) bekommen
  dadurch automatisch verschiedene Schlüssel ⇒ eigene S-Matrix.

Dies ist dieselbe Identität, die `GdsPreviewKey` (GDS-Thumbnails) bereits bildet
(`module|function|parameters` + Format-Version-Hash). Die Identitäts-Bildung wird
geteilt/verallgemeinert, damit Preview und S-Matrix-Scope konsistent dieselbe
Definition von „deckungsgleicher Geometrie" verwenden. Der Raw-Code-Fall wird ergänzt.

### 2. Vererbung über `ApplyAll`

`SMatrixOverrideApplicator.ApplyAll` matcht Overrides bisher per `Identifier` mit
`templateKeyResolver` (`{PdkSource}::{Name}`) als Fallback. Der Fallback-Resolver wird
durch einen **Geometrie-Identitäts-Resolver** ersetzt: `geometryKeyResolver(component)`
liefert die Geometrie-Identität (siehe 1). Dadurch:
- erhalten **alle deckungsgleichen Instanzen + Kopien** den Override beim Platzieren
  und beim Projekt-Laden,
- löst sich das Orphan-Key-Problem (#583) auf, weil der Schlüssel konsistent zur
  Geometrie statt zum Anzeigenamen/Identifier ist.

`Identifier`-Match bleibt als erster Lookup erhalten (Abwärtskompatibilität, siehe 5).

### 3. Schreiben des Overrides

FDTD-Recompute **und** „Load S-matrix from file" (beide schreiben in denselben
project-local Store `StoredSMatrices`) speichern künftig unter dem **Geometrie-Schlüssel**
statt unter `Identifier`. Konkret bekommt der Component-Settings-Dialog für den
S-Matrix-Teil einen `smatrixKey` (= Geometrie-Identität), getrennt vom Identifier, der
weiter den **Nazca-Geometrie-Override** (#561) adressiert — denn dieser *definiert* die
Geometrie einer Einzelinstanz und ist daher zu Recht per-Instanz.

### 4. Garbage Collection (Save-Sweep)

Beim Projekt-Speichern werden nur S-Matrix-Overrides persistiert, deren
Geometrie-Identität von **mindestens einer Canvas-Komponente** verwendet wird
(refcount > 0). Verwaiste Einträge (z. B. nach Parameteränderung, sodass kein Bauteil
mehr die alte Geometrie hat) fallen beim Speichern raus.

Während der Session bleiben verwaiste Overrides erhalten — so sind Undo und
„Parameter ändern und wieder zurücksetzen" sicher. Kein sofortiges Löschen, kein
persistentes Markierungsfeld nötig: der Refcount wird beim Speichern aus den aktuell
platzierten Komponenten berechnet (eine Komponente trägt 1 für ihre Geometrie-Identität).

### 5. Migration / Abwärtskompatibilität

Bestehende `.lun`-Projekte enthalten Overrides unter `Identifier`. Diese bleiben
funktionsfähig, weil `ApplyAll` zuerst per `Identifier` matcht und erst dann den
Geometrie-Resolver konsultiert. Nur **neue** Recomputes/Importe schreiben unter dem
Geometrie-Schlüssel. Kein Bruch alter Projekte; keine aktive Datei-Migration nötig.

Hinweis Save-Sweep: Der Refcount-Sweep darf einen alten per-Identifier-Eintrag nicht
fälschlich als verwaist verwerfen, solange seine Instanz existiert. Der Sweep behält
daher einen Eintrag, wenn entweder (a) seine Geometrie-Identität von ≥1 Komponente
genutzt wird **oder** (b) sein Schlüssel ein `Identifier` einer noch vorhandenen
Komponente ist.

## Abgrenzung / Nicht-Ziele

- Keine UI für Refcounts/„welche Bauteile teilen diese S-Matrix" (rein interne Mechanik).
- Kein user-global Scope (bewusst project-local; siehe gewählter Scope „Typ, im Projekt").
- Keine Änderung am Nazca-Geometrie-Override-Scope (#561 bleibt per-Instanz).
- Time-Domain-Panel-UX ist separat (eigenes Roadmap-Thema).

## Tests

- Geometrie-Identität: gleicher `module|function|parameters` ⇒ gleicher Schlüssel;
  abweichende Parameter ⇒ anderer Schlüssel; aktiver Raw-Code-Override ⇒ Code-Hash-Schlüssel.
- `ApplyAll`: zwei deckungsgleiche Instanzen (gleiche Geometrie, verschiedene Identifier)
  erben beide einen unter dem Geometrie-Schlüssel gespeicherten Override; eine
  umparametrisierte Instanz erbt ihn nicht.
- Kopie-Szenario (Regression zum gemeldeten Bug): Recompute auf Instanz A → Kopie B
  (Clone) wird platziert → B trägt den Override (über `ApplyAll`-Geometrie-Match).
- Save-Sweep: Override, dessen Geometrie keine Komponente mehr nutzt, wird beim Speichern
  entfernt; ein noch genutzter (und ein per-Identifier-Altbestand mit vorhandener Instanz)
  bleibt erhalten.
- Bestehende Override-Tests (`SMatrixOverrideApplicatorTests`) bleiben grün
  (Identifier-Match-Pfad unverändert).

## Offene/abhängige Punkte

- `GdsPreviewKey` liegt in `CAP.Avalonia/Controls/Canvas/ComponentPreview/`. Für die
  Wiederverwendung als allgemeine Geometrie-Identität wird die Identitäts-Bildung an
  einen feature-neutralen Ort gehoben oder als gemeinsame Hilfsfunktion exponiert
  (Detail im Implementierungsplan).
- Exakte Stelle des Save-Sweep: im Projekt-Speicherpfad von `FileOperationsViewModel`
  (Detail im Plan).
