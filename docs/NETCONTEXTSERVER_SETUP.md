# NetContextServer Setup for Connect-A-PIC-Pro

## Overview

NetContextServer is a .NET-native MCP (Model Context Protocol) server that provides deep codebase understanding for AI assistants. Unlike generic text-based tools (like OpenViking), NetContextServer uses **Roslyn** (the .NET Compiler Platform) to semantically understand C# code structure, types, references, and dependencies.

## Why NetContextServer over OpenViking?

| Feature | NetContextServer ✅ | OpenViking ❌ |
|---------|---------------------|---------------|
| **C# Semantic Understanding** | ✅ Uses Roslyn compiler | ❌ Text-based only |
| **Code Structure Analysis** | ✅ Classes, methods, interfaces | ❌ No code awareness |
| **Dependency Analysis** | ✅ NuGet packages, project refs | ❌ Not available |
| **Semantic Search** | ✅ Works (with Azure OpenAI) | ❌ Broken (Float infinity bug) |
| **Test Coverage Analysis** | ✅ Coverlet, LCOV, Cobertura | ❌ Not available |
| **File Reading** | ✅ Works | ✅ Works |
| **Directory Listing** | ✅ Works | ✅ Works |
| **L0/L1 Summaries** | N/A (different approach) | ❌ VLM not configured |
| **.NET Integration** | ✅ Native .NET 9.0 | ❌ Python-based |
| **Production Ready** | ✅ Yes | ❌ v0.2.10 not ready |

## Installation

### 1. Clone and Build NetContextServer

```bash
cd /home/max/Projects
git clone https://github.com/willibrandon/NetContextServer.git
cd NetContextServer
dotnet build
```

**Requirements:**
- .NET 9.0 SDK (already installed)
- Linux, macOS, or Windows

### 2. Configure MCP Integration for Claude Code

Create or update `~/.config/claude-code/.mcp.json`:

```json
{
  "mcpServers": {
    "net-context": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "/home/max/Projects/NetContextServer/src/NetContextServer/NetContextServer.csproj"
      ],
      "env": {
        "BASE_DIRECTORY": "/home/max/Projects/Connect-A-PIC-Pro",
        "AZURE_OPENAI_ENDPOINT": "",
        "AZURE_OPENAI_API_KEY": ""
      }
    }
  }
}
```

**Configuration options:**
- `BASE_DIRECTORY`: Root directory of your .NET project (required)
- `AZURE_OPENAI_ENDPOINT`: Azure OpenAI endpoint for semantic search (optional)
- `AZURE_OPENAI_API_KEY`: Azure OpenAI API key for semantic search (optional)

### 3. Restart Claude Code

After creating the MCP config, restart Claude Code to load the NetContextServer MCP server.

## Available Tools

Once configured, NetContextServer provides these tools to Claude:

### File Operations
- **list-files**: List all .NET source files (`.cs`, `.csproj`, `.sln`)
- **read-file**: Read file contents with safety checks
- **search-code**: Search for exact text matches in code files

### Project Management
- **list-projects**: List all `.csproj` files in the codebase
- **analyze-packages**: Analyze NuGet dependencies with update recommendations
- **dependency-graph**: Visualize project dependencies

### Code Analysis
- **semantic-search**: Find code by describing intent (requires Azure OpenAI)
- **analyze-coverage**: Parse test coverage reports (Coverlet JSON, LCOV, Cobertura XML)

### Security & Configuration
- **get-base-directory**: Show current base directory
- **list-ignore-patterns**: Show files/patterns being ignored
- **add-ignore-patterns**: Add patterns to ignore (e.g., `*.generated.cs`, `bin/*`)

### Thinking & Reasoning
- **think**: Document reasoning about complex operations (based on Anthropic's research)

## Usage Examples

### Via CLI (Testing)

```bash
# Set base directory
cd /home/max/Projects/NetContextServer
dotnet run --project src/NetContextClient/NetContextClient.csproj -- \
  set-base-dir --directory "/home/max/Projects/Connect-A-PIC-Pro"

# List all .NET projects
dotnet run --project src/NetContextClient/NetContextClient.csproj -- list-projects

# Analyze NuGet packages
dotnet run --project src/NetContextClient/NetContextClient.csproj -- analyze-packages

# Search code (exact match)
dotnet run --project src/NetContextClient/NetContextClient.csproj -- \
  search-code --query "ComponentGroup"
```

### Via Claude Code (MCP)

Once configured, simply ask Claude questions like:

- "List all .NET source files in this project"
- "Show me all projects and their dependencies"
- "Search for 'ComponentGroup' in the codebase"
- "Analyze the NuGet packages and tell me what's outdated"
- "Show me the test coverage for ComponentGroup.cs"
- "What files have low test coverage?"

## Semantic Search (Optional)

Semantic search allows you to find code by describing intent, not just exact text matches.

### Setup Azure OpenAI

1. Create an Azure OpenAI resource: https://portal.azure.com
2. Deploy an embedding model (e.g., `text-embedding-ada-002`)
3. Get your endpoint and API key
4. Update `.mcp.json` with credentials:

```json
{
  "mcpServers": {
    "net-context": {
      "env": {
        "BASE_DIRECTORY": "/home/max/Projects/Connect-A-PIC-Pro",
        "AZURE_OPENAI_ENDPOINT": "https://your-resource.openai.azure.com/",
        "AZURE_OPENAI_API_KEY": "your-api-key-here"
      }
    }
  }
}
```

### Using Semantic Search

```bash
# Via CLI
dotnet run --project src/NetContextClient/NetContextClient.csproj -- \
  semantic-search --query "find authentication logic"

# Via Claude Code (MCP)
"Find code related to user authentication"
"Show me where waveguide routing is implemented"
```

## Test Coverage Analysis

NetContextServer can parse and analyze test coverage reports from:
- **Coverlet JSON** (default .NET coverage format)
- **LCOV** (text format)
- **Cobertura XML** (xUnit/MSTest format)

### Generate Coverage Report

```bash
# Using Coverlet (built into dotnet test)
dotnet test --collect:"XPlat Code Coverage" --results-directory ./TestResults

# Convert to JSON if needed
reportgenerator -reports:"./TestResults/**/coverage.cobertura.xml" \
  -targetdir:"./TestResults/coverage" -reporttypes:Cobertura
```

### Analyze Coverage via Claude

"Analyze test coverage for the UnitTests project"
"Which files have the lowest test coverage?"
"Show me uncovered lines in CreateGroupCommand.cs"

## Architecture

NetContextServer is built on:
- **.NET 9.0** (C# 13)
- **Roslyn APIs** (Microsoft.CodeAnalysis) for semantic C# parsing
- **Model Context Protocol (MCP)** for AI tool integration
- **NuGet.Protocol** for package analysis
- **Built-in security** (automatic ignore patterns for sensitive files)

## Files & Locations

| Item | Location |
|------|----------|
| NetContextServer repo | `/home/max/Projects/NetContextServer/` |
| MCP config | `~/.config/claude-code/.mcp.json` |
| Server project | `NetContextServer/src/NetContextServer/NetContextServer.csproj` |
| CLI client | `NetContextServer/src/NetContextClient/NetContextClient.csproj` |
| Our codebase | `/home/max/Projects/Connect-A-PIC-Pro/` |

## Troubleshooting

### Server not starting
```bash
# Check if server process is running
ps aux | grep NetContextServer

# Kill old processes
pkill -f NetContextServer

# Check logs
cd /home/max/Projects/NetContextServer
dotnet run --project src/NetContextServer/NetContextServer.csproj
```

### "Access to this directory is not allowed"
- Verify `BASE_DIRECTORY` is set correctly in `.mcp.json`
- Ensure the directory exists and is readable
- Check that you're not trying to access files outside the base directory

### Semantic search not working
- Verify Azure OpenAI credentials are set in `.mcp.json`
- Check endpoint format: `https://your-resource.openai.azure.com/`
- Ensure embedding model is deployed in Azure

### Claude Code doesn't see MCP tools
- Verify `.mcp.json` exists at `~/.config/claude-code/.mcp.json`
- Restart Claude Code completely
- Check MCP server logs for errors

## Resources

- **NetContextServer GitHub**: https://github.com/willibrandon/NetContextServer
- **Model Context Protocol**: https://modelcontextprotocol.io/
- **Roslyn APIs**: https://github.com/dotnet/roslyn
- **MCP C# SDK**: https://github.com/modelcontextprotocol/csharp-sdk

## Status

**Installed**: ✅ 2026-03-24
**Location**: `/home/max/Projects/NetContextServer/`
**Version**: 1.0.0.0
**Runtime**: .NET 9.0.13
**MCP Config**: `~/.config/claude-code/.mcp.json`
**Base Directory**: `/home/max/Projects/Connect-A-PIC-Pro/`
**Semantic Search**: ❌ Disabled (no Azure OpenAI credentials)
**Production Ready**: ✅ Yes
