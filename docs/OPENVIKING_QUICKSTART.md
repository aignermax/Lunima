# OpenViking Quick Start — Local Installation (Free)

## Why OpenViking?

**Problem**: Claude reads 152 files and ~76,000 lines every time → slow, expensive
**Solution**: OpenViking L0/L1/L2 tiers → read only 5-10 relevant files → 93% token reduction

## Installation (5 Minutes)

### Step 1: Install Ollama (Free, Offline)

**Windows (at work):**
```powershell
# Open PowerShell as Administrator and enable WSL2 if not already done
wsl --install

# After restart, open WSL2 terminal
wsl

# Install Ollama in WSL2
curl -fsSL https://ollama.com/install.sh | sh
ollama pull nomic-embed-text
```

**Linux (home PC / Agent PC):**
```bash
curl -fsSL https://ollama.com/install.sh | sh
ollama pull nomic-embed-text
```

**Verify installation:**
```bash
ollama list
# Should show: nomic-embed-text
```

**Important:** Ollama is only used for creating embeddings (vectors for search), NOT as the LLM. Claude Sonnet remains your smart AI!

### Step 2: Install OpenViking

```bash
pip install openviking
```

If you don't have pip:
```bash
# Ubuntu/Debian
sudo apt install python3-pip

# Windows (in WSL2)
sudo apt update && sudo apt install python3-pip
```

### Step 3: Configure OpenViking

```bash
mkdir -p ~/.openviking
cat > ~/.openviking/ov.conf <<EOF
[DEFAULT]
embedding_provider = ollama
ollama_model = nomic-embed-text
ollama_base_url = http://localhost:11434

[SERVER]
host = localhost
port = 1933
EOF
```

---

**Alternative: OpenAI Embeddings (Faster Indexing)**

If indexing is too slow (~2-3 minutes), you can use OpenAI instead (costs ~€0.002 once):

```bash
cat > ~/.openviking/ov.conf <<EOF
[DEFAULT]
embedding_provider = openai
openai_api_key = sk-your-key-here
openai_embedding_model = text-embedding-3-small

[SERVER]
host = localhost
port = 1933
EOF
```

### Step 4: Index Connect-A-PIC-Pro

**Windows (WSL2):**
```bash
cd /mnt/c/dev/Akhetonics/Connect-A-PIC-Pro
ov index .
```

**Linux:**
```bash
cd ~/dev/Connect-A-PIC-Pro  # Or wherever you cloned the repo
ov index .
```

**Expected output:**
```
Indexing 152 files...
Creating embeddings...
✓ Indexed 152 files in 2m 30s
```

### Step 5: Start OpenViking Server

```bash
openviking-server
```

**Expected output:**
```
OpenViking server running on http://localhost:1933
```

**Keep this terminal open** or run in background:
```bash
nohup openviking-server > ~/.openviking/server.log 2>&1 &
```

### Step 6: Test the API

Open a new terminal:
```bash
# List resources
curl http://localhost:1933/api/v1/fs/ls?uri=viking://resources/

# Search for ComponentGroup code
curl -X POST http://localhost:1933/api/v1/search/find \
  -H "Content-Type: application/json" \
  -d '{"query": "ComponentGroup serialization", "path": "viking://resources/"}'
```

## Auto-Sync After Git Pull

Create a git hook to re-index after pulling changes:

```bash
# In your Connect-A-PIC-Pro repository:
cat > .git/hooks/post-merge <<'EOF'
#!/bin/bash
echo "Re-indexing codebase for OpenViking..."
ov index . --incremental
EOF

chmod +x .git/hooks/post-merge
```

Now every `git pull` automatically updates the index.

## MCP Server Integration (Claude Code)

Create MCP server to connect Claude Code to OpenViking:

**File: `mcp-servers/openviking/server.py`**

```python
#!/usr/bin/env python3
"""
MCP Server for OpenViking integration.
Allows Claude Code to query Connect-A-PIC-Pro context efficiently.
"""
import httpx
from mcp.server import Server
from mcp.server.stdio import stdio_server

OPENVIKING_URL = "http://localhost:1933"

app = Server("openviking-connect-a-pic")

@app.list_tools()
async def list_tools():
    return [
        {
            "name": "ov_search",
            "description": "Search Connect-A-PIC-Pro codebase semantically",
            "inputSchema": {
                "type": "object",
                "properties": {
                    "query": {"type": "string", "description": "Search query (e.g., 'ComponentGroup serialization')"}
                },
                "required": ["query"]
            }
        },
        {
            "name": "ov_read",
            "description": "Read file with L0/L1/L2 tiers",
            "inputSchema": {
                "type": "object",
                "properties": {
                    "path": {"type": "string", "description": "File path relative to repo root"},
                    "tier": {"type": "string", "enum": ["L0", "L1", "L2"], "default": "L1"}
                },
                "required": ["path"]
            }
        }
    ]

@app.call_tool()
async def call_tool(name: str, arguments: dict):
    async with httpx.AsyncClient() as client:
        if name == "ov_search":
            resp = await client.post(
                f"{OPENVIKING_URL}/api/v1/search/find",
                json={
                    "query": arguments["query"],
                    "path": "viking://resources/",
                    "limit": 10
                }
            )
            return {"content": [{"type": "text", "text": resp.text}]}

        elif name == "ov_read":
            tier = arguments.get("tier", "L1")
            uri = f"viking://resources/{arguments['path']}@{tier}"
            resp = await client.get(
                f"{OPENVIKING_URL}/api/v1/fs/cat",
                params={"uri": uri}
            )
            return {"content": [{"type": "text", "text": resp.text}]}

if __name__ == "__main__":
    stdio_server(app)
```

**Add to Claude Code MCP settings:**

```json
{
  "mcpServers": {
    "openviking": {
      "command": "python",
      "args": ["c:/dev/Akhetonics/Connect-A-PIC-Pro/mcp-servers/openviking/server.py"]
    }
  }
}
```

## Usage Example

**Before OpenViking:**
```
User: "Fix ComponentGroup serialization bug"
Claude: Reads 152 files (76,000 lines) → 200,000 tokens → 30 seconds → $0.60
```

**After OpenViking:**
```
User: "Fix ComponentGroup serialization bug"
Claude:
  1. ov_search("ComponentGroup serialization") → finds 5 relevant files
  2. ov_read("CAP-DataAccess/Persistence/ComponentGroupSerializer.cs", tier="L1") → 2,000 tokens
  3. Only loads L2 (full file) when editing
Total: 15,000 tokens → 2 seconds → $0.04 (93% reduction!)
```

## Troubleshooting

**Issue: `ollama: command not found`**
```bash
# Verify PATH
echo $PATH
# Add to ~/.bashrc or ~/.zshrc:
export PATH=$PATH:/usr/local/bin
```

**Issue: `Connection refused to localhost:1933`**
```bash
# Check if server is running
ps aux | grep openviking
# Restart server
pkill openviking-server
openviking-server
```

**Issue: `Slow indexing (>5 minutes)`**
```bash
# Check Ollama is running
ollama list
# Use OpenAI API instead (faster):
cat > ~/.openviking/ov.conf <<EOF
[DEFAULT]
embedding_provider = openai
openai_api_key = your-key-here
openai_embedding_model = text-embedding-3-small
EOF
```

**Issue: `Outdated context after git pull`**
```bash
# Manually re-index
ov index . --force
```

## Cost Comparison

| Option | Initial Cost | Per-Update | Total (1 year) |
|--------|-------------|------------|----------------|
| **Ollama (Local)** | FREE | FREE | **€0** |
| OpenAI API | €0.002 | €0.0002 | €0.10 |
| Cloud GPU | €50-100/month | - | €600-1200 |

## Team Rollout

1. **Week 1**: One person tests locally (you)
2. **Week 2**: Agent PC setup + test with Claude Code
3. **Week 3**: Home Linux setup + verify git hook
4. **Week 4**: Team documentation + training

Each person installs independently in ~5 minutes.

## Next Steps

1. ✅ Install Ollama
2. ✅ Install OpenViking
3. ✅ Index codebase
4. ✅ Test HTTP API
5. ⏳ Create MCP server
6. ⏳ Test with Claude Code
7. ⏳ Setup git hook
8. ⏳ Rollout to team

## Resources

- OpenViking GitHub: https://github.com/volcengine/OpenViking
- Ollama: https://ollama.com
- MCP Protocol: https://modelcontextprotocol.io
