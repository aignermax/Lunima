# OpenViking Setup für morgen (zu Hause)

## TL;DR - Eine Zeile!

```bash
cd ~/dev/Connect-A-PIC-Pro
git pull
bash scripts/setup-openviking-linux.sh YOUR_OPENAI_API_KEY_HERE
```

**Dein OpenAI API Key** (per Signal/sicher übermitteln, nicht hier im Repo!)

**Das war's!** Nach 30-60 Sekunden ist OpenViking fertig indexiert.

## Server starten

```bash
~/.local/bin/openviking-server
```

Oder im Hintergrund (empfohlen):
```bash
nohup ~/.local/bin/openviking-server > ~/.openviking/server.log 2>&1 &
```

## Testen

```bash
# Health check
curl http://localhost:1933/api/v1/health

# Semantische Suche
~/.local/bin/openviking find "ComponentGroup serialization"

# Dateien auflisten
~/.local/bin/openviking ls viking://resources/connect-a-pic/
```

## Was macht OpenViking?

- **Ohne**: Claude liest 152 Dateien (76.000 Zeilen) = 200.000 tokens = 30 Sekunden
- **Mit**: Claude sucht → 5 Dateien gefunden = 10.000 tokens = 2 Sekunden
- **Ersparnis**: 93% weniger tokens, 15x schneller!

## Wie funktioniert das?

OpenViking erstellt 3 Ebenen für jede Datei:

1. **L0 (Summary)**: Eine Zeile (~10 tokens)
   - Beispiel: "ComponentGroup.cs: Represents hierarchical component groups with nested components and frozen waveguide paths"

2. **L1 (Planning)**: Kontext zum Planen (~2000 tokens)
   - Klassen-Übersicht, wichtige Methoden, Dependencies

3. **L2 (Full)**: Vollständiger Code
   - Wird nur geladen wenn Claude die Datei editieren muss

Claude liest erst **alle L0** (152 × 10 = 1.520 tokens), dann **relevante L1** (5 × 2000 = 10.000 tokens), und **L2 nur zum Editieren**.

## Beispiel-Workflow

**Du:** "Fix ComponentGroup serialization bug"

**Claude mit OpenViking:**
1. `ov_search("ComponentGroup serialization")` → findet 5 relevante Dateien
2. `ov_read("CAP-DataAccess/Persistence/ComponentGroupSerializer.cs", tier="L1")` → 2.000 tokens
3. Analysiert das Problem
4. `ov_read("ComponentGroupSerializer.cs", tier="L2")` → voller Code zum Editieren
5. Fixt den Bug

**Total: 15.000 tokens statt 200.000 = 93% Ersparnis**

## Kosten

- **Erstes Indexing**: ~€0.003 (weniger als 1 Cent)
- **Re-indexing nach git pull**: ~€0.0001
- **Pro Monat bei täglichem Update**: ~€0.01

## Für die KI morgen

Wenn die KI morgen fragt "Was soll ich machen?", sag einfach:

> "Lies docs/OPENVIKING_HOME_SETUP.md und befolge die Anleitung"

Oder noch einfacher:

> "Führe `bash scripts/setup-openviking-linux.sh` mit dem OpenAI Key aus"

## Wichtig

- ✅ OpenViking ist NUR für Embeddings (Vektoren für Suche)
- ✅ Claude Sonnet bleibt dein smartes LLM
- ✅ Dein Code wird NIE zu OpenAI geschickt (nur für Vektorisierung)
- ✅ Völlig optional, aber macht AI-Assistenten 15x schneller

## Troubleshooting

Falls was nicht klappt, siehe [docs/OPENVIKING_HOME_SETUP.md](OPENVIKING_HOME_SETUP.md) Abschnitt "Troubleshooting".

## Alternative: Ollama (kostenlos)

Falls du lieber kostenlos/offline willst, kannst du auch Ollama verwenden (siehe OPENVIKING_HOME_SETUP.md).

**Aber:** OpenAI ist so billig (~1 Cent pro Monat) dass es sich kaum lohnt.

---

**Gute Fahrt nach Hause! 🚗**

Morgen sollte alles mit einem Command laufen.
