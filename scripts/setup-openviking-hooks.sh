#!/bin/bash
# Setup git hooks for automatic OpenViking re-indexing after git pull

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

echo "Setting up OpenViking git hooks for Connect-A-PIC-Pro..."

# Create post-merge hook (runs after git pull)
cat > "$REPO_ROOT/.git/hooks/post-merge" <<'EOF'
#!/bin/bash
# Auto-reindex OpenViking after git pull

echo ""
echo "🔄 Re-indexing codebase for OpenViking..."

# Check if OpenViking is installed
if ! command -v ov &> /dev/null; then
    echo "⚠️  OpenViking not installed. Skipping re-indexing."
    echo "   Install with: pip install openviking"
    exit 0
fi

# Get repo root
REPO_ROOT="$(git rev-parse --show-toplevel)"

# Incremental re-index (only changed files)
cd "$REPO_ROOT"
if ov index . --incremental; then
    echo "✅ OpenViking index updated successfully"
else
    echo "⚠️  OpenViking indexing failed (non-blocking)"
fi

echo ""
EOF

chmod +x "$REPO_ROOT/.git/hooks/post-merge"

# Create post-checkout hook (runs after git checkout)
cat > "$REPO_ROOT/.git/hooks/post-checkout" <<'EOF'
#!/bin/bash
# Auto-reindex OpenViking after branch switch

# Only run if we switched branches (not just updated files)
if [ "$3" == "1" ]; then
    echo ""
    echo "🔄 Branch switched, re-indexing OpenViking..."

    if command -v ov &> /dev/null; then
        REPO_ROOT="$(git rev-parse --show-toplevel)"
        cd "$REPO_ROOT"
        if ov index . --incremental; then
            echo "✅ OpenViking index updated"
        fi
    fi

    echo ""
fi
EOF

chmod +x "$REPO_ROOT/.git/hooks/post-checkout"

echo ""
echo "✅ Git hooks installed:"
echo "   - .git/hooks/post-merge   (runs after git pull)"
echo "   - .git/hooks/post-checkout (runs after git checkout)"
echo ""
echo "ℹ️  These hooks will automatically re-index OpenViking after you pull changes."
echo "   They are non-blocking - if OpenViking is not installed, they skip silently."
echo ""
