#!/bin/bash
# Index Connect-A-PIC-Pro with OpenViking

REPO_ROOT="/mnt/c/dev/Akhetonics/Connect-A-PIC-Pro"
OV_BIN="/home/max_a/.local/bin/openviking"

echo "Indexing Connect-A-PIC-Pro with OpenViking..."
echo "This will take ~20-30 seconds with OpenAI embeddings..."
cd "$REPO_ROOT"
"$OV_BIN" add-resource "$REPO_ROOT" --to viking://resources/connect-a-pic --wait
