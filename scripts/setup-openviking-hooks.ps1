# Setup git hooks for automatic OpenViking re-indexing after git pull
# Windows PowerShell version

$RepoRoot = Split-Path -Parent $PSScriptRoot

Write-Host "Setting up OpenViking git hooks for Connect-A-PIC-Pro..." -ForegroundColor Cyan
Write-Host ""

# Create post-merge hook (runs after git pull)
$PostMergeHook = @'
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
'@

$PostMergeHook | Out-File -FilePath "$RepoRoot\.git\hooks\post-merge" -Encoding UTF8 -NoNewline

# Create post-checkout hook (runs after git checkout)
$PostCheckoutHook = @'
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
'@

$PostCheckoutHook | Out-File -FilePath "$RepoRoot\.git\hooks\post-checkout" -Encoding UTF8 -NoNewline

Write-Host "✅ Git hooks installed:" -ForegroundColor Green
Write-Host "   - .git/hooks/post-merge   (runs after git pull)"
Write-Host "   - .git/hooks/post-checkout (runs after git checkout)"
Write-Host ""
Write-Host "ℹ️  These hooks will automatically re-index OpenViking after you pull changes." -ForegroundColor Yellow
Write-Host "   They are non-blocking - if OpenViking is not installed, they skip silently."
Write-Host ""
Write-Host "Note: On Windows, these hooks run in WSL2 (where you have Ollama installed)." -ForegroundColor Gray
Write-Host ""
