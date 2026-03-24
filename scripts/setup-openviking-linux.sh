#!/bin/bash
# OpenViking Setup for Linux (Home PC)
# Run this script once to set up OpenViking with your OpenAI API key

set -e

echo "🚀 OpenViking Setup for Connect-A-PIC-Pro"
echo ""

# Check if API key is provided
if [ -z "$1" ]; then
    echo "❌ Error: OpenAI API key required"
    echo ""
    echo "Usage:"
    echo "  ./scripts/setup-openviking-linux.sh YOUR_OPENAI_API_KEY"
    echo ""
    echo "Example:"
    echo "  ./scripts/setup-openviking-linux.sh sk-proj-..."
    exit 1
fi

OPENAI_API_KEY="$1"

echo "📦 Step 1/5: Installing pipx (if needed)..."
if ! command -v pipx &> /dev/null; then
    sudo apt update
    sudo apt install -y pipx
    pipx ensurepath
fi

echo ""
echo "📦 Step 2/5: Installing OpenViking..."
pipx install openviking --force

echo ""
echo "⚙️  Step 3/5: Creating config with your API key..."
mkdir -p ~/.openviking

cat > ~/.openviking/ov.conf <<EOF
{
  "storage": {
    "workspace": "$HOME/.openviking/workspace"
  },
  "log": {
    "level": "INFO",
    "output": "stdout"
  },
  "embedding": {
    "dense": {
      "api_base": "https://api.openai.com/v1",
      "api_key": "$OPENAI_API_KEY",
      "provider": "openai",
      "dimension": 1536,
      "model": "text-embedding-3-small"
    },
    "max_concurrent": 10
  },
  "vlm": {
    "api_base": "https://api.openai.com/v1",
    "api_key": "$OPENAI_API_KEY",
    "provider": "openai",
    "model": "gpt-4o-mini"
  }
}
EOF

# Also create CLI config
cat > ~/.openviking/ovcli.conf <<EOF
{
  "url": "http://localhost:1933"
}
EOF

echo ""
echo "📚 Step 4/5: Indexing Connect-A-PIC-Pro codebase..."
echo "   This will take ~30-60 seconds..."

cd ~/dev/Connect-A-PIC-Pro  # Adjust if your path is different
~/.local/bin/openviking add-resource . --to viking://resources/connect-a-pic --wait

echo ""
echo "🚀 Step 5/5: Starting OpenViking server..."
echo ""
echo "To start the server, run:"
echo "  ~/.local/bin/openviking-server"
echo ""
echo "Or run in background:"
echo "  nohup ~/.local/bin/openviking-server > ~/.openviking/server.log 2>&1 &"
echo ""
echo "✅ Setup complete!"
echo ""
echo "📝 Next steps:"
echo "   1. Start the server: ~/.local/bin/openviking-server"
echo "   2. Test it: curl http://localhost:1933/api/v1/health"
echo "   3. Use Claude Code with MCP server integration"
echo ""
