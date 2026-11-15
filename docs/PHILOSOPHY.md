# Connect-A-PIC Philosophy & Positioning

> **Connect-A-PIC is an architecture-level photonic design tool for fast concept validation and system exploration, using abstracted component models to reason about feasibility before committing to full physical design flows.**

---

## Core Idea

Connect-A-PIC ist ein **Architecture-Level Design Tool** für Photonic Integrated Circuits.

Es beginnt bei der **frühen Phase des Denkens und Entwerfens** photonischer Systeme und reicht bis zu einem **digitalen Produktionsrun**: einer modellbasierten Prüfung, ob ein Entwurf physikalisch funktionieren würde. Echte Foundry-Sign-off-Flows ersetzt es nicht.

**Ziel:** Schnell prüfen, ob eine photonic architecture prinzipiell funktionieren kann, bevor Zeit und Geld in detaillierte PDK-basierte Simulationen investiert werden.

---

## Zweck des Tools

Connect-A-PIC ermöglicht es, photonische Systeme schnell aus Komponenten zusammenzusetzen, diese frei zu platzieren und über abstrakte Verbindungen zu koppeln, um:

- **Architektur-Ideen zu explorieren**
- **Topologien zu vergleichen**
- **Skalierbarkeit und Stabilität abzuschätzen**
- **frühe Designfehler zu erkennen**

Das Tool ist explizit ein **Thinking Tool**, kein klassisches EDA-Verifikationswerkzeug.

---

## Physikalisches Modell (bewusst abstrahiert)

Komponenten werden durch **vereinfachte / parametrisierte S-Matrizen** beschrieben.

Modelle sind:
- idealisiert
- heuristisch
- oder grob kalibriert

**Fokus liegt auf:**
- Relationen zwischen Komponenten
- Interferenz- und Kopplungseffekten
- systemischem Verhalten

**Keine Garantie auf Foundry-Genauigkeit.**
**Aussagen über Funktion und Ausbeute sind modellbasiert:** Jeder Prüfbericht nennt die verwendeten Modelle, ihre Quelle (`foundry`, `literature`, `generic`) und ihre Grenzen. Eine Tape-out-Garantie gibt es nicht.

Diese Abstraktion ist **Absicht, nicht Limitation**.

---

## Digitaler Produktionsrun (seit November 2026)

Ein echter MPW-Run ist derzeit nicht bezahlbar. Damit Lunima trotzdem glaubwürdig bleibt, prüft es Entwürfe gegen die **Physik** und nicht nur gegen ideale Logik: absolute Lichtleistung, Detektorempfindlichkeit, Verstärkung und Pegelregeneration, Kaskadierbarkeit über viele Gatter, Rauschen und Fertigungsstreuung. Am Ende steht ein reproduzierbarer Prüfbericht für „virtuelle Dies“ (ROADMAP Stufe 8, #1638).

Ehrlichkeitsregeln:
- Ein Logikgatter gilt erst als funktionierend, wenn seine Pegel die Kette physikalisch überstehen. Eine Tabelle, die nach jedem Gatter perfekte Pegel wiederherstellt, ist eine Abstraktion, kein Nachweis.
- Jedes Modell trägt seine Quelle. Ohne gekennzeichnetes Modell gibt es kein Urteil.
- Vorhandene Open-Source-Werkzeuge werden genutzt oder zum Gegenprüfen herangezogen, bevor eigene Modelle entstehen.

Die schnelle, abstrahierte Exploration bleibt der Einstieg; der digitale Produktionsrun ist die zusätzliche, strengere Sicht darauf.

---

## Explizite Abgrenzung zu Tools wie Ansys INTERCONNECT

Connect-A-PIC ist **kein Ersatz** für:
- Ansys Lumerical / INTERCONNECT
- Synopsys OptoCompiler
- andere PDK-basierte PIC-EDA-Tools

**Diese Werkzeuge adressieren:**
- Verifikation
- Risikoabsicherung
- industrielle Reproduzierbarkeit

**Connect-A-PIC adressiert:**
- Architektur-Exploration
- Konzeptvalidierung
- kognitives Design
- schnelle Iteration

Beide Tool-Kategorien sind **komplementär, nicht konkurrierend**.

---

## Bridge to Physical Design

While Connect-A-PIC remains focused on architecture exploration, recent additions enable smoother handoff to physical design flows:

- **Nazca PDK Export**: Export designs to Python/GDS via Nazca framework
- **Multi-PDK Support**: Load and manage multiple Process Design Kits (SiEPIC, Demo PDK)
- **GDS Generation**: Generate GDSII layout files for verification
- **Component Dimension Validation**: Ensure components match PDK specifications

These features don't change Connect-A-PIC's core mission as a thinking tool, but they reduce friction when moving successful concepts into detailed design environments.

---

## Typische Use Cases

- *"Kann diese photonic processor architecture überhaupt funktionieren?"*
- *"Wie verhält sich ein interferometrisches Mesh auf Systemebene?"*
- *"Welche Topologie skaliert besser?"*
- *"Lohnt es sich, dieses Konzept überhaupt in einen schweren Tool-Flow zu bringen?"*
- *"Export validated architectures to Nazca/GDS for physical verification"*
- *"Compare multiple PDK implementations of the same architecture"*
- *"Validate component dimensions against PDK specifications"*

---

## Design-Philosophie

| Priorität | über |
|-----------|------|
| Geschwindigkeit | Genauigkeit |
| Verständlichkeit | Vollständigkeit |
| Architektur | Implementierung |
| Exploration | Verifikation |

**Oder kurz:**

> Connect-A-PIC optimiert für **Denken** — und prüft ehrlich, ob das Gedachte physikalisch tragen würde.

---

## Workflow-Position

```
┌─────────────────────────────────────────────────────────────────────┐
│                        PIC Design Workflow                          │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│   ┌─────────────────┐     ┌─────────────────┐     ┌─────────────┐  │
│   │   EXPLORATION   │ --> │   VALIDATION    │ --> │  TAPE-OUT   │  │
│   │                 │     │                 │     │             │  │
│   │  Connect-A-PIC  │ --> │  INTERCONNECT   │     │  Foundry    │  │
│   │                 │  │  │  OptoCompiler   │     │  PDK Flow   │  │
│   │  "Does this     │  │  │  "Will this     │     │  "Build     │  │
│   │   make sense?"  │  │  │   work?"        │     │   this"     │  │
│   │                 │  │  │                 │     │             │  │
│   │  Now with:      │  │  │                 │     │             │  │
│   │  • Nazca export │  └─>│  (or Python/    │     │             │  │
│   │  • GDS output   │     │   GDS tools)    │     │             │  │
│   └─────────────────┘     └─────────────────┘     └─────────────┘  │
│                                                                     │
│   Fast iteration          Accurate simulation      Manufacturing   │
│   Architecture focus      Physical verification    GDS/Mask        │
│   Abstracted models       PDK-based models         Yield analysis  │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

---

## Recent Enhancements (2026)

Connect-A-PIC has evolved to include:

- **Nazca PDK Integration**: Export designs as Python scripts using Nazca framework
- **GDS Generation**: Generate GDSII layout files from designs
- **Multi-PDK Support**: Load and switch between different Process Design Kits
- **Advanced Routing**: Manhattan and A* routing with collision detection
- **Component Validation**: Dimension checks against PDK specifications
- **Parameter Sweeps**: Systematic analysis of design variations
- **Alignment Guides**: Figma-style snapping for precise component placement
- **Undo/Redo**: Full command history for design exploration
- **Locked Elements**: Fix components and connections to prevent accidental modification

These features extend Connect-A-PIC's reach while preserving its core focus on fast architectural exploration.

---

*Last updated: November 2026*
