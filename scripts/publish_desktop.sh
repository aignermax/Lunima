#!/usr/bin/env bash
# =============================================================================
# publish_desktop.sh — the release publish of CAP.Desktop (portable + MSI folder)
#
# Build_Exe.yaml (the release) and PackagedAppSmoke.yaml (the PR smoke of the
# packaged app) both publish through this script, so the binary the smoke test
# starts is built exactly like the one users download. The macOS .app has its
# own script, scripts/build_macos_bundle.sh, shared the same way.
#
# Usage:
#   scripts/publish_desktop.sh LAYOUT RID OUTPUT_DIR [extra dotnet publish args]
#
# Layouts:
#   portable  self-contained single file, executable renamed to Lunima / Lunima.exe
#             (the win-x64 .zip and linux-x64 .tar.gz)
#   msi       framework-dependent folder the WiX installer harvests (CAP.Desktop.exe)
#
# Both layouts get the repo's examples/ merged into OUTPUT_DIR/examples/.
#
# Examples:
#   scripts/publish_desktop.sh portable linux-x64 publish
#   scripts/publish_desktop.sh msi win-x64 publish/win-x64-msi -p:Version=0.23.0
# =============================================================================

set -euo pipefail

usage() {
  echo "Usage: $0 portable|msi RID OUTPUT_DIR [extra dotnet publish args]" >&2
  exit 2
}

[[ $# -ge 3 ]] || usage
LAYOUT="$1"
RID="$2"
OUTPUT_DIR="$3"
shift 3

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
PROJECT="${REPO_ROOT}/CAP.Desktop/CAP.Desktop.csproj"

case "${LAYOUT}" in
  portable)
    dotnet publish "${PROJECT}" \
      -c Release \
      -r "${RID}" \
      --self-contained true \
      -p:PublishSingleFile=true \
      -p:IncludeNativeLibrariesForSelfExtract=true \
      -p:DebugType=none \
      -o "${OUTPUT_DIR}" \
      "$@"
    if [[ -f "${OUTPUT_DIR}/CAP.Desktop" ]]; then
      mv "${OUTPUT_DIR}/CAP.Desktop" "${OUTPUT_DIR}/Lunima"
      chmod +x "${OUTPUT_DIR}/Lunima"
    fi
    if [[ -f "${OUTPUT_DIR}/CAP.Desktop.exe" ]]; then
      mv "${OUTPUT_DIR}/CAP.Desktop.exe" "${OUTPUT_DIR}/Lunima.exe"
    fi
    ;;
  msi)
    dotnet publish "${PROJECT}" \
      -c Release \
      -r "${RID}" \
      --self-contained false \
      -o "${OUTPUT_DIR}" \
      "$@"
    ;;
  *)
    usage
    ;;
esac

# Ship the example designs beside the binary: the Home screen's Examples section and
# the ISA playground find examples/ and examples/isa/ by walking up from the app base
# directory. The publish already holds examples/*.lun (csproj Content), so merge the
# folder's contents: `cp -R examples OUTPUT_DIR/examples` would nest a second copy at
# examples/examples/ and leave examples/isa/ out of reach.
mkdir -p "${OUTPUT_DIR}/examples"
cp -R "${REPO_ROOT}/examples/." "${OUTPUT_DIR}/examples/"
echo "Published ${LAYOUT} ${RID} build to ${OUTPUT_DIR} (examples/ merged in)."
