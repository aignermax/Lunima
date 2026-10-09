#!/usr/bin/env bash
# throttle.sh — run a command (typically a local test run) under a SHARED CPU and
# memory cap so it cannot take the desktop down with it.
#
# Why: `dotnet test` fans out across every core and spawns test hosts of ~2-3 GB
# each. On a 16 GB dev box the parallel hosts fill RAM, the machine swaps, freezes
# and the kernel OOM-killer starts shooting desktop apps. Every run joins one
# systemd slice (captest.slice) whose limits are an AGGREGATE budget: several
# concurrent runs (e.g. parallel agents) share the same cores and RAM instead of
# each getting its own. A run that exceeds MemoryMax is killed on its own; the
# rest of the session keeps working.
#
# Linux only (systemd user slices). On macOS/Windows, or without a systemd user
# session (CI containers), the command runs unthrottled.
#
# Tunables (env; defaults sized for a 20-core / 16 GB machine):
#   SAFE_TEST_CPUS      "0-9"   cores the runs may use
#   SAFE_TEST_QUOTA     "600%"  total CPU across all runs (≈6 cores)
#   SAFE_TEST_MEM_HIGH  "6G"    slice starts reclaiming/throttling here
#   SAFE_TEST_MEM_MAX   "8G"    hard cap — the test run is OOM-killed, not the desktop
#   SAFE_TEST_SWAP_MAX  "1G"    keeps runs from thrashing swap
#
# Usage:
#   scripts/throttle.sh dotnet test UnitTests/UnitTests.csproj --filter "Category!=Slow"
set -euo pipefail

if ! command -v systemd-run >/dev/null 2>&1 || ! systemctl --user show-environment >/dev/null 2>&1; then
  echo "throttle: no systemd user session — running unthrottled" >&2
  exec "$@"
fi

CPUS="${SAFE_TEST_CPUS:-0-9}"
QUOTA="${SAFE_TEST_QUOTA:-600%}"
MEM_HIGH="${SAFE_TEST_MEM_HIGH:-6G}"
MEM_MAX="${SAFE_TEST_MEM_MAX:-8G}"
SWAP_MAX="${SAFE_TEST_SWAP_MAX:-1G}"
SLICE="captest.slice"

systemctl --user set-property "$SLICE" \
  "CPUQuota=$QUOTA" "AllowedCPUs=$CPUS" "CPUWeight=20" \
  "MemoryHigh=$MEM_HIGH" "MemoryMax=$MEM_MAX" "MemorySwapMax=$SWAP_MAX" >/dev/null 2>&1 || true

echo "throttle: shared $SLICE → CPUQuota=$QUOTA AllowedCPUs=$CPUS MemoryMax=$MEM_MAX (aggregate over every concurrent run)" >&2

exec systemd-run --user --slice="$SLICE" --scope --quiet \
  -p "CPUWeight=20" \
  nice -n 15 "$@"
