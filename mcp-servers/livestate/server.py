#!/usr/bin/env python3
"""
MCP stdio bridge to the live-state agent server of a RUNNING Lunima instance.

The Lunima app exposes its live state (canvas, routing, errors, simulation)
on a localhost-only HTTP endpoint (Tools -> Agent Server, or launch the app
with LUNIMA_AGENT_SERVER=1). This bridge translates MCP tool calls into
requests against that endpoint so an external coding agent can both read the
running design and drive it (move components, load a .lun, run a simulation,
take a screenshot).

Configuration (env vars):
    LUNIMA_AGENT_PORT   Port of the running app's agent server (default 5252).

Claude Code MCP settings:
    {
      "mcpServers": {
        "lunima-live": {
          "command": "python3",
          "args": ["<repo>/mcp-servers/livestate/server.py"],
          "env": { "LUNIMA_AGENT_PORT": "5252" }
        }
      }
    }
"""
import json
import os
from typing import Any

import httpx
from mcp.server import Server
from mcp.server.stdio import stdio_server

PORT = os.environ.get("LUNIMA_AGENT_PORT", "5252")
BASE_URL = f"http://127.0.0.1:{PORT}"

app = Server("lunima-live-state")

TOOLS = [
    {
        "name": "lunima_get_status",
        "description": "Quick health check of the running Lunima app: open design file, component/connection counts, routing activity.",
        "inputSchema": {"type": "object", "properties": {}, "required": []},
    },
    {
        "name": "lunima_get_design",
        "description": "Full snapshot of the design on the canvas: components (id, position, rotation, size, lock, group, light-source) and connections with live routing state (path length, loss, blocked-fallback flag).",
        "inputSchema": {"type": "object", "properties": {}, "required": []},
    },
    {
        "name": "lunima_get_errors",
        "description": "Entries of the app's error console (errors, warnings, info), oldest first.",
        "inputSchema": {"type": "object", "properties": {}, "required": []},
    },
    {
        "name": "lunima_get_simulation",
        "description": "State of the most recent CW simulation run (success, wavelengths, source/component/connection counts, overlay visibility).",
        "inputSchema": {"type": "object", "properties": {}, "required": []},
    },
    {
        "name": "lunima_run_simulation",
        "description": "Runs the CW S-Matrix simulation on the current design and returns the result summary. Also updates the app's power-flow overlay data.",
        "inputSchema": {"type": "object", "properties": {}, "required": []},
    },
    {
        "name": "lunima_move_component",
        "description": "Moves a component to an absolute canvas position (micrometers) through the app's undoable command pipeline; connections re-route.",
        "inputSchema": {
            "type": "object",
            "properties": {
                "componentId": {"type": "string", "description": "Component identifier as returned by lunima_get_design"},
                "x": {"type": "number", "description": "New absolute X in micrometers"},
                "y": {"type": "number", "description": "New absolute Y in micrometers"},
            },
            "required": ["componentId", "x", "y"],
        },
    },
    {
        "name": "lunima_load_design",
        "description": "Loads a .lun design file into the running app (absolute path).",
        "inputSchema": {
            "type": "object",
            "properties": {
                "path": {"type": "string", "description": "Absolute path to the .lun file"},
            },
            "required": ["path"],
        },
    },
    {
        "name": "lunima_screenshot",
        "description": "Renders the app's main window to a PNG for visual verification. Returns the file path (default: a temp file).",
        "inputSchema": {
            "type": "object",
            "properties": {
                "path": {"type": "string", "description": "Optional absolute output path for the PNG"},
            },
            "required": [],
        },
    },
]

# tool name -> (HTTP method, route)
ROUTES = {
    "lunima_get_status": ("GET", "/status"),
    "lunima_get_design": ("GET", "/design"),
    "lunima_get_errors": ("GET", "/errors"),
    "lunima_get_simulation": ("GET", "/simulation"),
    "lunima_run_simulation": ("POST", "/simulation/run"),
    "lunima_move_component": ("POST", "/component/move"),
    "lunima_load_design": ("POST", "/design/load"),
    "lunima_screenshot": ("POST", "/screenshot"),
}


@app.list_tools()
async def list_tools() -> list[dict[str, Any]]:
    return TOOLS


@app.call_tool()
async def call_tool(name: str, arguments: dict) -> dict[str, Any]:
    if name not in ROUTES:
        return _error(f"Unknown tool: {name}")

    method, route = ROUTES[name]
    async with httpx.AsyncClient(timeout=120.0) as client:
        try:
            if method == "GET":
                resp = await client.get(f"{BASE_URL}{route}")
            else:
                resp = await client.post(f"{BASE_URL}{route}", json=arguments or {})
            body = _pretty(resp.text)
            if resp.status_code >= 400:
                return _error(f"Lunima returned HTTP {resp.status_code}:\n{body}")
            return {"content": [{"type": "text", "text": body}]}
        except httpx.RequestError as e:
            return _error(
                f"Cannot reach Lunima at {BASE_URL}: {e}\n\n"
                "Is the app running with the Agent Server started?\n"
                "  - In the app: Tools -> Agent Server (MCP) -> Start\n"
                "  - Or launch with env LUNIMA_AGENT_SERVER=1"
            )


def _pretty(text: str) -> str:
    try:
        return json.dumps(json.loads(text), indent=2)
    except (json.JSONDecodeError, ValueError):
        return text


def _error(message: str) -> dict[str, Any]:
    return {"content": [{"type": "text", "text": message}], "isError": True}


if __name__ == "__main__":
    stdio_server(app)
