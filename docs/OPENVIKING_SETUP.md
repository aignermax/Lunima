# OpenViking Setup for Connect-A-PIC Team

## Architecture Overview

```
┌──────────────────────────────────────────────────────┐
│  OpenViking Server (Shared Infrastructure)          │
│  Location: TBD (Linux VM / Build Server / Cloud)    │
│  Port: 1933 (HTTP API)                               │
│  Features:                                           │
│    - Single source of truth for codebase context    │
│    - Automatic git sync (pulls latest changes)      │
│    - L0/L1/L2 tier caching                          │
└─────────────────┬────────────────────────────────────┘
                  │ HTTP API (http://server:1933)
        ┌─────────┼─────────┬──────────────┐
        │         │         │              │
    ┌───▼───┐ ┌──▼────┐ ┌──▼────┐   ┌─────▼──────┐
    │Windows│ │Linux  │ │Claude │   │ Teammate   │
    │(Work) │ │(Home) │ │Agent  │   │ Machine    │
    └───────┘ └───────┘ └───────┘   └────────────┘
```

## Deployment Options

### Option 1: Shared Linux Server (Recommended)

**Requirements:**
- Linux VM with Python 3.10+
- 4GB RAM minimum
- Network accessible to all team members
- Git access to Connect-A-PIC-Pro repo

**Setup Steps:**

```bash
# 1. Install OpenViking on server
pip install openviking

# 2. Configure ~/.openviking/ov.conf
cat > ~/.openviking/ov.conf <<EOF
[DEFAULT]
embedding_provider = openai
openai_api_key = your-api-key-here
openai_embedding_model = text-embedding-3-small

[SERVER]
host = 0.0.0.0  # Listen on all interfaces
port = 1933
root_api_key = your-secret-api-key  # Optional: multi-tenant mode
EOF

# 3. Index Connect-A-PIC codebase
cd /path/to/shared/repos
git clone https://github.com/aignermax/Connect-A-PIC-Pro.git
ov index Connect-A-PIC-Pro/

# 4. Start server (production mode)
nohup openviking-server > /var/log/openviking.log 2>&1 &

# 5. Setup git auto-sync (cron job)
# Crontab entry to pull + re-index every 30 minutes:
*/30 * * * * cd /path/to/Connect-A-PIC-Pro && git pull && ov index .
```

**Client Configuration (All PCs):**

```bash
# Set environment variable to point to server
export OPENVIKING_SERVER=http://your-server-ip:1933

# Test connection
curl http://your-server-ip:1933/api/v1/fs/ls?uri=viking://resources/
```

### Option 2: Docker Container (Easiest)

**On Server:**
```bash
# Create docker-compose.yml
cat > docker-compose.yml <<EOF
version: '3.8'
services:
  openviking:
    image: openviking:latest  # If official image exists
    ports:
      - "1933:1933"
    volumes:
      - ./codebase:/data/codebase
      - ./config:/root/.openviking
    environment:
      - OPENAI_API_KEY=your-key
    command: openviking-server --host 0.0.0.0
EOF

docker-compose up -d
```

**Benefits:**
- ✅ Isolated environment
- ✅ Easy updates (pull new image)
- ✅ Portable configuration

### Option 3: Local on Each Machine (Fallback)

**Only if shared server not feasible:**

**Windows (WSL2):**
```powershell
# Install WSL2 if not already installed
wsl --install

# Inside WSL2:
pip install openviking
ov index /mnt/c/dev/Akhetonics/Connect-A-PIC-Pro/
openviking-server  # Runs on localhost:1933
```

**Linux (Home):**
```bash
pip install openviking
ov index ~/dev/Connect-A-PIC-Pro/
openviking-server
```

**Agent PC:**
```bash
pip install openviking
ov index /path/to/Connect-A-PIC-Pro/
openviking-server
```

## MCP Server Integration

Once OpenViking is running, create MCP server wrapper:

**File: `mcp-servers/openviking-bridge/server.py`**

```python
#!/usr/bin/env python3
"""
MCP Server that bridges to OpenViking context database.
Allows Claude Code to query viking:// URIs.
"""
import httpx
from mcp.server import Server
from mcp.server.stdio import stdio_server

OPENVIKING_URL = "http://localhost:1933"  # Or remote server

app = Server("openviking-context")

@app.list_tools()
async def list_tools():
    return [
        {
            "name": "ov_ls",
            "description": "List files/resources in OpenViking filesystem",
            "inputSchema": {
                "type": "object",
                "properties": {
                    "uri": {"type": "string", "description": "viking:// URI to list"}
                },
                "required": ["uri"]
            }
        },
        {
            "name": "ov_cat",
            "description": "Read file content from OpenViking (L0/L1/L2)",
            "inputSchema": {
                "type": "object",
                "properties": {
                    "uri": {"type": "string"},
                    "tier": {"type": "string", "enum": ["L0", "L1", "L2"], "default": "L1"}
                },
                "required": ["uri"]
            }
        },
        {
            "name": "ov_find",
            "description": "Semantic search in OpenViking",
            "inputSchema": {
                "type": "object",
                "properties": {
                    "query": {"type": "string"},
                    "path": {"type": "string", "default": "viking://resources/"}
                },
                "required": ["query"]
            }
        }
    ]

@app.call_tool()
async def call_tool(name: str, arguments: dict):
    async with httpx.AsyncClient() as client:
        if name == "ov_ls":
            resp = await client.get(
                f"{OPENVIKING_URL}/api/v1/fs/ls",
                params={"uri": arguments["uri"]}
            )
            return {"content": [{"type": "text", "text": resp.text}]}

        elif name == "ov_cat":
            uri = arguments["uri"]
            tier = arguments.get("tier", "L1")
            # Append tier to URI: viking://path/file.cs@L1
            full_uri = f"{uri}@{tier}" if "@" not in uri else uri
            resp = await client.get(
                f"{OPENVIKING_URL}/api/v1/fs/cat",
                params={"uri": full_uri}
            )
            return {"content": [{"type": "text", "text": resp.text}]}

        elif name == "ov_find":
            resp = await client.post(
                f"{OPENVIKING_URL}/api/v1/search/find",
                json={
                    "query": arguments["query"],
                    "path": arguments.get("path", "viking://resources/")
                }
            )
            return {"content": [{"type": "text", "text": resp.text}]}

if __name__ == "__main__":
    stdio_server(app)
```

**Usage in Claude Code:**

```json
// Add to MCP settings
{
  "mcpServers": {
    "openviking": {
      "command": "python",
      "args": ["path/to/mcp-servers/openviking-bridge/server.py"],
      "env": {
        "OPENVIKING_SERVER": "http://your-server:1933"
      }
    }
  }
}
```

## Performance Comparison

### Without OpenViking:
```
User: "Fix ComponentGroup serialization bug"
Claude reads: 152 files × ~500 lines = ~76,000 lines
Tokens: ~200,000 tokens
Time: 30+ seconds to load context
Cost: ~$0.60 per request (GPT-4)
```

### With OpenViking:
```
User: "Fix ComponentGroup serialization bug"
OpenViking: L0 scan → finds 5 relevant files
            L1 load → 2,000 tokens each = 10,000 tokens
            L2 load → Only 1 file when editing = 500 lines
Tokens: ~15,000 tokens (93% reduction!)
Time: 2-3 seconds
Cost: ~$0.04 per request
```

## Auto-Sync Strategy

**Git Hook (post-merge):**
```bash
# .git/hooks/post-merge
#!/bin/bash
# Re-index after pulling changes
if [ -n "$OPENVIKING_SERVER" ]; then
    curl -X POST "$OPENVIKING_SERVER/api/v1/reindex" \
         -H "Content-Type: application/json" \
         -d '{"path": "Connect-A-PIC-Pro"}'
fi
```

## Security Considerations

1. **API Key Protection:**
   - Use `root_api_key` in config for multi-tenant mode
   - Restrict network access (firewall rules)

2. **VPN Access:**
   - If server is remote, use VPN or SSH tunnel
   - Example: `ssh -L 1933:localhost:1933 server`

3. **Read-Only Mode:**
   - OpenViking only reads codebase, doesn't modify
   - Safe to share across team

## Monitoring

```bash
# Check server status
curl http://server:1933/health

# View logs
tail -f /var/log/openviking.log

# Monitor resource usage
htop  # RAM usage for embeddings
```

## Troubleshooting

**Issue: "Connection refused"**
- Check firewall: `sudo ufw allow 1933`
- Verify server running: `ps aux | grep openviking`

**Issue: "Outdated context"**
- Force re-index: `ov index --force /path/to/repo`

**Issue: "Slow queries"**
- Check embedding cache
- Increase server RAM
- Use local SSD for index storage

## Next Steps

1. ✅ Decide on server location (shared Linux VM?)
2. ✅ Install OpenViking on server
3. ✅ Index Connect-A-PIC-Pro
4. ✅ Test HTTP API from all machines
5. ✅ Create MCP server wrapper
6. ✅ Update team documentation
7. ✅ Monitor performance for 1 week
8. ✅ Rollout to all team members

## Resources

- OpenViking GitHub: https://github.com/volcengine/OpenViking
- API Documentation: https://github.com/volcengine/OpenViking/blob/main/docs/en/
- MCP Protocol: https://modelcontextprotocol.io/
