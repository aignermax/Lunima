# Lunima Live-State MCP Server

Bridges external coding agents (Claude Code, etc.) to the **running** Lunima
instance: read the live design, routing state, error console and simulation
results, and drive the app (move components, load a `.lun`, run a simulation,
take a screenshot). This complements `mcp-servers/openviking`, which only
indexes the *source code* — this server exposes *app state*.

## How it works

```
agent (MCP client) ──stdio──> server.py ──HTTP (127.0.0.1 only)──> running Lunima app
```

The app hosts a localhost-only HTTP endpoint (`CAP.Avalonia/Services/LiveState/`).
This script is a thin stdio↔HTTP translator.

## Setup

1. **Install dependencies:**
   ```bash
   pip install -r requirements.txt
   ```

2. **Start the endpoint in the app** (either way):
   - Tools → 🤖 *Agent Server (MCP)* → **Start**, or
   - launch the app with `LUNIMA_AGENT_SERVER=1` (auto-start).

   Default port `5252`; override with `LUNIMA_AGENT_PORT` (both app and bridge).

3. **Add to your MCP client settings:**
   ```json
   {
     "mcpServers": {
       "lunima-live": {
         "command": "python3",
         "args": ["<repo>/mcp-servers/livestate/server.py"],
         "env": { "LUNIMA_AGENT_PORT": "5252" }
       }
     }
   }
   ```

## Tools

| Tool | Verb | What it does |
|---|---|---|
| `lunima_get_status` | read | App health: open file, counts, routing activity |
| `lunima_get_design` | read | Components + connections incl. routing state (blocked-fallback, loss, length) |
| `lunima_get_errors` | read | Error-console entries |
| `lunima_get_simulation` | read | Most recent CW simulation summary |
| `lunima_run_simulation` | control | Runs the CW S-Matrix simulation |
| `lunima_move_component` | control | Moves a component (undoable, re-routes) |
| `lunima_load_design` | control | Loads a `.lun` file |
| `lunima_screenshot` | control | Renders the main window to a PNG, returns the path |

## HTTP API (for curl debugging)

```bash
curl http://127.0.0.1:5252/status
curl http://127.0.0.1:5252/design
curl -X POST http://127.0.0.1:5252/component/move \
     -d '{"componentId": "straight_1", "x": 120.0, "y": 40.0}'
curl -X POST http://127.0.0.1:5252/screenshot -d '{}'
```

## Security

The listener binds `127.0.0.1` exclusively and is off by default — it is never
reachable from the network, and nothing is exposed unless the user (or the
`LUNIMA_AGENT_SERVER=1` launcher) opts in.
