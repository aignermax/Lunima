#!/usr/bin/env python3
"""
MCP Server for OpenViking integration with Connect-A-PIC-Pro.
Allows Claude Code to query the codebase efficiently via L0/L1/L2 tiers.

Usage:
    python server.py

Add to Claude Code MCP settings:
    {
      "mcpServers": {
        "openviking-cap": {
          "command": "python",
          "args": ["c:/dev/Akhetonics/Connect-A-PIC-Pro/mcp-servers/openviking/server.py"]
        }
      }
    }
"""
import httpx
import json
from typing import Any
from mcp.server import Server
from mcp.server.stdio import stdio_server

OPENVIKING_URL = "http://localhost:1933"

app = Server("openviking-connect-a-pic")

@app.list_tools()
async def list_tools() -> list[dict[str, Any]]:
    return [
        {
            "name": "ov_search",
            "description": "Search Connect-A-PIC-Pro codebase semantically. Returns L0 summaries of matching files.",
            "inputSchema": {
                "type": "object",
                "properties": {
                    "query": {
                        "type": "string",
                        "description": "Search query (e.g., 'ComponentGroup serialization', 'waveguide routing A* algorithm')"
                    },
                    "limit": {
                        "type": "integer",
                        "description": "Max number of results (default 10)",
                        "default": 10
                    }
                },
                "required": ["query"]
            }
        },
        {
            "name": "ov_read",
            "description": "Read a file with L0/L1/L2 tier. L0=one-sentence summary, L1=planning context (~2000 tokens), L2=full file content.",
            "inputSchema": {
                "type": "object",
                "properties": {
                    "path": {
                        "type": "string",
                        "description": "File path relative to repo root (e.g., 'Connect-A-Pic-Core/Components/Core/ComponentGroup.cs')"
                    },
                    "tier": {
                        "type": "string",
                        "enum": ["L0", "L1", "L2"],
                        "default": "L1",
                        "description": "Context tier: L0=summary, L1=planning, L2=full"
                    }
                },
                "required": ["path"]
            }
        },
        {
            "name": "ov_ls",
            "description": "List files/folders in a directory (like ls/dir command).",
            "inputSchema": {
                "type": "object",
                "properties": {
                    "path": {
                        "type": "string",
                        "description": "Directory path (e.g., 'Connect-A-Pic-Core/Routing')",
                        "default": ""
                    }
                },
                "required": []
            }
        }
    ]

@app.call_tool()
async def call_tool(name: str, arguments: dict) -> dict[str, Any]:
    async with httpx.AsyncClient(timeout=30.0) as client:
        try:
            if name == "ov_search":
                query = arguments["query"]
                limit = arguments.get("limit", 10)

                resp = await client.post(
                    f"{OPENVIKING_URL}/api/v1/search/find",
                    json={
                        "query": query,
                        "path": "viking://resources/",
                        "limit": limit
                    }
                )
                resp.raise_for_status()

                results = resp.json()
                formatted = f"Search results for '{query}':\n\n"

                if isinstance(results, list):
                    for i, item in enumerate(results, 1):
                        path = item.get("path", "unknown")
                        summary = item.get("l0_summary", "No summary available")
                        formatted += f"{i}. {path}\n   {summary}\n\n"
                else:
                    formatted = json.dumps(results, indent=2)

                return {"content": [{"type": "text", "text": formatted}]}

            elif name == "ov_read":
                path = arguments["path"]
                tier = arguments.get("tier", "L1")

                uri = f"viking://resources/{path}@{tier}"
                resp = await client.get(
                    f"{OPENVIKING_URL}/api/v1/fs/cat",
                    params={"uri": uri}
                )
                resp.raise_for_status()

                content = resp.text
                header = f"=== {path} ({tier}) ===\n\n"

                return {"content": [{"type": "text", "text": header + content}]}

            elif name == "ov_ls":
                path = arguments.get("path", "")
                uri = f"viking://resources/{path}" if path else "viking://resources/"

                resp = await client.get(
                    f"{OPENVIKING_URL}/api/v1/fs/ls",
                    params={"uri": uri}
                )
                resp.raise_for_status()

                items = resp.json()
                formatted = f"Contents of {path or '(root)'}:\n\n"

                if isinstance(items, list):
                    for item in items:
                        name = item.get("name", "unknown")
                        is_dir = item.get("is_directory", False)
                        prefix = "[DIR] " if is_dir else "[FILE]"
                        formatted += f"{prefix} {name}\n"
                else:
                    formatted = json.dumps(items, indent=2)

                return {"content": [{"type": "text", "text": formatted}]}

        except httpx.HTTPStatusError as e:
            error_msg = f"OpenViking API error: {e.response.status_code}\n{e.response.text}"
            return {"content": [{"type": "text", "text": error_msg}], "isError": True}
        except httpx.RequestError as e:
            error_msg = f"Connection error: {str(e)}\n\nIs openviking-server running? Start it with:\n  openviking-server"
            return {"content": [{"type": "text", "text": error_msg}], "isError": True}
        except Exception as e:
            error_msg = f"Unexpected error: {str(e)}"
            return {"content": [{"type": "text", "text": error_msg}], "isError": True}

if __name__ == "__main__":
    stdio_server(app)
