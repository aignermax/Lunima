# OpenViking MCP Server for Connect-A-PIC-Pro

MCP (Model Context Protocol) server that bridges Claude Code to OpenViking's semantic codebase index.

## Setup

1. **Install dependencies:**
   ```bash
   pip install -r requirements.txt
   ```

2. **Make sure OpenViking server is running:**
   ```bash
   openviking-server
   ```

3. **Add to Claude Code MCP settings:**

   **Windows:**
   Edit `%APPDATA%\Code\User\settings.json` or VS Code settings:
   ```json
   {
     "mcp.servers": {
       "openviking-cap": {
         "command": "python",
         "args": ["c:/dev/Akhetonics/Connect-A-PIC-Pro/mcp-servers/openviking/server.py"]
       }
     }
   }
   ```

   **Linux:**
   Edit `~/.config/Code/User/settings.json`:
   ```json
   {
     "mcp.servers": {
       "openviking-cap": {
         "command": "python3",
         "args": ["/home/user/dev/Connect-A-PIC-Pro/mcp-servers/openviking/server.py"]
       }
     }
   }
   ```

4. **Restart Claude Code**

## Available Tools

### `ov_search`
Search the codebase semantically.

**Example:**
```
ov_search("ComponentGroup serialization")
```

**Returns:** List of files with L0 summaries (one-sentence descriptions).

### `ov_read`
Read a file with L0/L1/L2 tier.

**Example:**
```
ov_read("Connect-A-Pic-Core/Components/Core/ComponentGroup.cs", tier="L1")
```

**Tiers:**
- `L0`: One-sentence summary (~10 tokens)
- `L1`: Planning context (~2000 tokens) - default
- `L2`: Full file content (all tokens)

### `ov_ls`
List directory contents.

**Example:**
```
ov_ls("Connect-A-Pic-Core/Routing")
```

## Testing

Test manually with curl:

```bash
# Start server
python server.py

# In another terminal, test OpenViking API directly
curl http://localhost:1933/api/v1/fs/ls?uri=viking://resources/

# Search
curl -X POST http://localhost:1933/api/v1/search/find \
  -H "Content-Type: application/json" \
  -d '{"query": "waveguide routing", "path": "viking://resources/", "limit": 5}'
```

## Troubleshooting

**Error: Connection refused**
- Make sure `openviking-server` is running
- Check it's on port 1933: `netstat -an | grep 1933` (Linux) or `netstat -an | findstr 1933` (Windows)

**Error: ModuleNotFoundError: No module named 'mcp'**
```bash
pip install mcp httpx
```

**Error: OpenViking not indexed**
```bash
cd c:/dev/Akhetonics/Connect-A-PIC-Pro
ov index .
```

## Performance Impact

**Without OpenViking:**
- Claude reads 152 files (~76,000 lines) = 200,000 tokens
- Takes ~30 seconds per request
- Costs ~$0.60 per request

**With OpenViking:**
- Claude searches → gets 5 relevant files
- Reads L1 summaries (~2000 tokens each) = 10,000 tokens
- Only loads L2 (full file) when editing
- Takes ~2 seconds per request
- Costs ~$0.03 per request

**Result:** 93% token reduction, 15x faster, 20x cheaper!
