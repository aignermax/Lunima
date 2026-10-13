# Agent bridge (MCP)

Lunima can host a small [Model Context Protocol](https://modelcontextprotocol.io) server
inside the running desktop app. An AI agent such as **Claude Code** connects to it and can
then inspect and minimally edit the design that is currently open — list failed (red)
connections with their reasons, move a component, re-route, delete a crossing, take a
screenshot of a region — while you watch the canvas. Every edit goes through Lunima's undo
history, so **Ctrl+Z** reverts whatever the agent did.

## Enable it

1. **Settings → Agent bridge (MCP)** (the bridge is **off by default**).
2. Turn on **Enable agent bridge**. The status line shows
   `Listening on http://127.0.0.1:39271/mcp`.
3. Optional: change the **Port** (1024–65535) and press **Apply** if 39271 is taken. A port
   that is already in use is reported in the status line.

The setting is remembered; the bridge starts with Lunima until you turn it off.

## Connect Claude Code

Press **Copy** next to *Connect Claude Code* and run the command in a terminal:

```bash
claude mcp add --transport http lunima http://127.0.0.1:39271/mcp --header "Authorization: Bearer <token>"
```

`<token>` is your installation's access token (shown, masked, on the settings page). Then
start `claude` in any folder and ask, for example: *"Use the lunima tools: which connections
are blocked and why? Fix the one between the counter gates by moving a component."*

Any MCP client that speaks **Streamable HTTP** can connect the same way; it must send the
`Authorization: Bearer <token>` header on every request.

## Tools

The bridge's own tools:

| Tool | What it does |
|---|---|
| `get_design_summary` | File name, unsaved flag, component / group / connection / blocked / crossing-piece counts, routing state, undo depth |
| `list_connections` | Every connection: `id`, endpoints as `component.pin`, the top-level start/end component, length (µm), `blocked`, `failure_reason`, `is_crossing_piece`, `locked`. Filters: `blocked_only`, `component` |
| `list_components` | Top-level components and groups: `id`, name, type, position, rotation, group, child count, lock state, bounds. Filter: `name_filter` |
| `move_component` | Move by `dx`/`dy` or to absolute `x`/`y` (µm). Refuses overlapping moves unless `force=true`. Undoable; waits for re-routing and returns the blocked connections |
| `reroute_connections` | Run the router, wait for it, report blocked state (all, or the given `ids`) |
| `delete_component` | Delete a component/group with its connections. Deleting a crossing component restores the two crossed nets. Undoable |
| `delete_connection` | Delete one connection (a crossing piece dissolves its crossing). Undoable |
| `undo` | Undo the most recent step of the shared history (the same step Ctrl+Z would undo) |
| `get_routing_timings` | Phase timings of the last routing pass (`LastRoutingPassTimings`) |
| `screenshot` | PNG of the whole design, or of a µm region (`x`, `y`, `width`, `height`), returned as MCP image content; `width_px` up to 4096 |
| `load_example` | Open a shipped example (e.g. `Logic Gate Counter 2-bit.lun`) as an untitled copy; without a name it lists the examples. Lunima still asks you about unsaved changes |

All tools of the in-app **AI assistant** (`get_grid_state`, `place_component`,
`create_connection`, `run_simulation`, `get_light_values`, `inspect_group`, `copy_component`,
`create_group`, `ungroup`, `save_as_prefab`, `clear_grid`, `fit_to_view`,
`get_available_types`) are exposed as well — the tool list is generated from the same
registry, so a new assistant tool appears here automatically. Note that these assistant
tools are not all undoable (e.g. `clear_grid`).

Coordinates are micrometres; positions are a component's top-left corner.

## Security model

- **Loopback only.** The server binds a socket to `127.0.0.1`, never to a network
  interface, so other machines cannot reach it.
- **Bearer token.** Every request must carry `Authorization: Bearer <token>`. The token is
  256 random bits generated per installation and stored in the user preferences
  (`user-preferences.json` in Lunima's app-data folder). Without it the server answers
  `401`. **Regenerate** on the settings page invalidates every connected agent.
- **DNS-rebinding guard.** Requests whose `Host` is not a loopback name, or that carry a
  browser `Origin` other than a loopback origin, are rejected with `403` — a web page in
  your browser cannot drive Lunima.
- **Opt-in.** Nothing listens until you enable the bridge.
- **Undoable edits.** Design edits run as the same undoable commands the UI uses, on the UI
  thread. Anyone holding the token can still edit (and, via `load_example`/`clear_grid`,
  replace) the open design — treat the token like a password.

## Limits

- Requests are answered with a single JSON body; the optional server-to-client SSE stream
  (`GET /mcp`) is not offered (`405`), which the MCP spec allows. No sessions.
- Routing always routes all nets jointly; `reroute_connections` with `ids` only scopes the
  report.
- Tool calls run on the UI thread one after another; long routing passes are awaited off
  the UI thread (the UI stays responsive) and time out after 5 minutes with
  `routing_complete: false`.

## Implementation notes

`CAP.Avalonia/Services/AgentBridge/` (vertical slice, DI via `AddAgentBridgeFeature`):

- `Transport/` — loopback `TcpListener` HTTP/1.1 server (`McpHttpServer`), request parser,
  `/mcp` endpoint with the auth / Host / Origin gates.
- `Protocol/` — JSON-RPC 2.0 + MCP `initialize`, `ping`, `tools/list`, `tools/call`.
- `Tools/` — one class per tool, the `AgentToolCatalog` (bridge tools + assistant-registry
  adapters, marshalled to the UI thread), and the `AgentDesignSession`.
- `Hosting/` — `AgentBridgeHost` (start/stop per settings) and the UI dispatcher.

The server is hand-written instead of using ASP.NET Core / Kestrel: the Windows MSI is a
framework-dependent build that only requires the .NET *Desktop* runtime, so adding the
ASP.NET Core shared framework would break existing installs, and `HttpListener` on Windows
goes through http.sys (URL ACLs, Host-header based routing). A raw loopback socket behaves
the same on Windows, macOS and Linux and needs no admin rights.
