#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
JELLYFIN_PLUGINS_DIR="/mnt/programming-ssd/projects/exploring/jellyfin-all/config/jellyfin/data/plugins"
TARGET_DIR="${JELLYFIN_PLUGINS_DIR}/SubtitleDownloader_1.0.0.0"

echo "=== Building Jellyfin.Plugin.SubtitleDownloader ==="

docker run --rm \
  -v "${SCRIPT_DIR}:/src" \
  -w /src/Jellyfin.Plugin.SubtitleDownloader \
  mcr.microsoft.com/dotnet/sdk:10.0-preview \
  dotnet build -c Release

echo "=== Installing plugin to Jellyfin data plugins directory ==="
mkdir -p "${TARGET_DIR}"

cp "${SCRIPT_DIR}/Jellyfin.Plugin.SubtitleDownloader/bin/Release/net10.0/Jellyfin.Plugin.SubtitleDownloader.dll" "${TARGET_DIR}/"
cp "${SCRIPT_DIR}/Jellyfin.Plugin.SubtitleDownloader/meta.json" "${TARGET_DIR}/"
cp "${SCRIPT_DIR}/Jellyfin.Plugin.SubtitleDownloader/icon.png" "${TARGET_DIR}/"

# Build distribution ZIP
mkdir -p "${SCRIPT_DIR}/dist"
(
  cd "${TARGET_DIR}"
  zip -r "${SCRIPT_DIR}/dist/Jellyfin.Plugin.SubtitleDownloader_1.0.0.0.zip" ./*
)

echo "=== Plugin successfully installed to ${TARGET_DIR} ==="
echo "=== Distribution archive created at ${SCRIPT_DIR}/dist/Jellyfin.Plugin.SubtitleDownloader_1.0.0.0.zip ==="
