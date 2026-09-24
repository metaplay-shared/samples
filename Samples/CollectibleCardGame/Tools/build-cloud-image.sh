#!/usr/bin/env bash
set -euo pipefail

# Builds the standard SDK server/dashboard, then adds the browser client.
# Run from anywhere: Tools/build-cloud-image.sh [tag]
SAMPLE_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$SAMPLE_ROOT"
BUILD_TAG="${1:-$(date -u +%Y%m%d-%H%M%S)}"
PROJECT_ID="$(sed -n 's/^projectID: *//p' metaplay-project.yaml)"
ARCHITECTURE="${STICKYPAWS_IMAGE_ARCHITECTURE:-amd64}"
SERVER_IMAGE="$PROJECT_ID:$BUILD_TAG-server"
FINAL_IMAGE="$PROJECT_ID:$BUILD_TAG"

# EF can leave a literal backslash-named output directory on macOS. It breaks MSBuild globs in Linux.
# Move this ignored build output aside rather than including it in the SDK Docker context.
if [[ -d 'Backend/Server/bin\Debug' ]]; then
    EF_OUTPUT_BACKUP="$(mktemp -d "${TMPDIR:-/tmp}/stickypaws-ef-output.XXXXXX")"
    mv 'Backend/Server/bin\Debug' "$EF_OUTPUT_BACKUP/"
fi

# Release excludes per-checkout appsettings.Development.json; cloud endpoints follow the page hostname.
# The publish directory is wiped first: `dotnet publish` adds and overwrites but never deletes, so a static
# asset removed from wwwroot/ survives there and ships in the image long after it left the repository.
PUBLISH_DIR='Client/bin/Release/net10.0-browser/publish'
rm -rf "$PUBLISH_DIR"
dotnet publish Client/Client.csproj -c Release
metaplay build image "$SERVER_IMAGE" --architecture="$ARCHITECTURE"

WEB_CONTEXT="$(mktemp -d "${TMPDIR:-/tmp}/stickypaws-web-image.XXXXXX")"
trap 'rm -rf "$WEB_CONTEXT"' EXIT
mkdir -p "$WEB_CONTEXT/publicwebapp"
cp -R Client/bin/Release/net10.0-browser/publish/wwwroot/. "$WEB_CONTEXT/publicwebapp/"

docker build --platform="linux/$ARCHITECTURE" --build-arg "SERVER_IMAGE=$SERVER_IMAGE" \
    -f Backend/Dockerfile.web-client -t "$FINAL_IMAGE" "$WEB_CONTEXT"
printf "\nBuilt %s\nDeploy with: metaplay deploy server <environment> %s\n" "$FINAL_IMAGE" "$FINAL_IMAGE"
