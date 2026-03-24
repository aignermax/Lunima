# OpenViking Setup für zu Hause (Linux)

## Quick Start - Eine einzige Zeile!

```bash
cd ~/dev/Connect-A-PIC-Pro
bash scripts/setup-openviking-linux.sh YOUR_OPENAI_API_KEY
```

**Das war's!** Das Skript macht alles automatisch:
- ✅ Installiert OpenViking
- ✅ Erstellt Config mit deinem API Key
- ✅ Indexiert die gesamte Codebase (~30 Sekunden)
- ✅ Zeigt dir wie du den Server startest

## Server starten

Nach dem Setup, starte den Server:

```bash
~/.local/bin/openviking-server
```

**Oder im Hintergrund:**
```bash
nohup ~/.local/bin/openviking-server > ~/.openviking/server.log 2>&1 &
```

## Testen ob es funktioniert

```bash
# Health check
curl http://localhost:1933/api/v1/health

# Semantische Suche testen
~/.local/bin/openviking find "ComponentGroup serialization"

# Datei-Liste anzeigen
~/.local/bin/openviking ls viking://resources/connect-a-pic/
```

## Für Claude Code (MCP Integration)

Die MCP-Server-Dateien sind bereits im Repo:
- `mcp-servers/openviking/server.py`
- `mcp-servers/openviking/requirements.txt`
- `mcp-servers/openviking/README.md`

Installiere die Dependencies:

```bash
cd ~/dev/Connect-A-PIC-Pro/mcp-servers/openviking
pip install -r requirements.txt
```

Füge zu deiner Claude Code Config hinzu (`~/.config/Code/User/settings.json`):

```json
{
  "mcp.servers": {
    "openviking-cap": {
      "command": "python3",
      "args": ["/home/max/dev/Connect-A-PIC-Pro/mcp-servers/openviking/server.py"]
    }
  }
}
```

## Nach git pull: Re-indexieren

Das git hook macht das automatisch, aber du kannst es auch manuell machen:

```bash
cd ~/dev/Connect-A-PIC-Pro
~/.local/bin/openviking add-resource . --to viking://resources/connect-a-pic --wait
```

## Kosten

- **Erstes Indexing**: ~€0.003 (weniger als 1 Cent)
- **Re-indexing nach git pull**: ~€0.0001 (nur geänderte Dateien)
- **Pro Monat bei täglichem Update**: ~€0.01

## Troubleshooting

### Server startet nicht

```bash
# Check config
cat ~/.openviking/ov.conf

# Check logs
tail -f ~/.openviking/server.log
```

### "url is required" Fehler

Die CLI braucht `~/.openviking/ovcli.conf`:

```bash
cat > ~/.openviking/ovcli.conf <<EOF
{
  "url": "http://localhost:1933"
}
EOF
```

### Workspace-Fehler

```bash
mkdir -p ~/.openviking/workspace
```

## Performance-Vergleich

**Ohne OpenViking:**
- Claude liest 152 Dateien (~76.000 Zeilen)
- 200.000 tokens
- ~30 Sekunden pro Request

**Mit OpenViking:**
- Claude sucht → findet 5 relevante Dateien
- 10.000 tokens (L1 summaries)
- ~2 Sekunden pro Request
- **93% weniger tokens, 15x schneller!**

## Alternative: Ollama (kostenlos, offline)

Falls du lieber Ollama statt OpenAI verwenden willst:

```bash
# Ollama installieren
curl -fsSL https://ollama.com/install.sh | sh
ollama pull nomic-embed-text

# Config anpassen
nano ~/.openviking/ov.conf
```

Ändere in der Config:

```json
{
  "embedding": {
    "dense": {
      "api_base": "http://localhost:11434",
      "api_key": "not-needed",
      "provider": "ollama",
      "dimension": 768,
      "model": "nomic-embed-text"
    }
  },
  "vlm": {
    "api_base": "http://localhost:11434",
    "api_key": "not-needed",
    "provider": "ollama",
    "model": "llama3.2"
  }
}
```

**Wichtig:** Ollama ist nur für Embeddings, Claude Sonnet bleibt dein LLM!

## Was OpenViking macht

1. **L0 (Summary)**: Eine Zeile pro Datei (~10 tokens)
2. **L1 (Planning)**: Kontext zum Planen (~2000 tokens)
3. **L2 (Full)**: Vollständiger Dateiinhalt (nur wenn editiert wird)

Claude liest erst L0 für alle Dateien, dann L1 für relevante, und L2 nur zum Editieren.

## Status prüfen

```bash
# OpenViking Status
~/.local/bin/openviking status

# Health check
~/.local/bin/openviking health

# Workspace-Größe
du -sh ~/.openviking/workspace
```
