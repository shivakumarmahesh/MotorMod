#!/bin/bash
# Build MotorMod in Release and package it into dist/:
#   MotorMod-<version>.zip              the MotorMod folder for players (DLL + macOS and Windows bundles)
#   MotorMod-<version>-server.zip       the same without the macOS bundles (dedicated servers)
#   MotorMod-<version>-thunderstore.zip a Thunderstore / r2modman package
# Run the Unity bundle build first (docs/DEVELOPMENT.md).
set -euo pipefail
cd "$(dirname "$0")/../.."
ROOT=$PWD
VERSION=$(grep -oE 'PluginVersion = "[0-9]+\.[0-9]+\.[0-9]+"' src/MotorMod/MotorModPlugin.cs | grep -oE '[0-9]+\.[0-9]+\.[0-9]+')
BIKES=$(grep -oE 'Key = "[a-z]+"' src/MotorMod/BikeDefinition.cs | grep -oE '"[a-z]+"' | tr -d '"')
BUNDLES="$BIKES saddlebag"          # every bike, and the saddlebag model they share
DOTNET=$(command -v dotnet || echo "$HOME/.dotnet/dotnet")

echo "MotorMod $VERSION: bikes $(echo $BIKES | tr '\n' ' ')"
for bike in $BUNDLES; do
  for platform in osx win; do
    [ -f "src/MotorMod/Bundles/$platform/$bike" ] || { echo "missing bundle src/MotorMod/Bundles/$platform/$bike: build the bundles first"; exit 1; }
  done
done
grep -q "<Version>$VERSION</Version>" src/MotorMod/MotorMod.csproj || { echo "the project file's <Version> differs from PluginVersion $VERSION"; exit 1; }
grep -q "## $VERSION" CHANGELOG.md || { echo "CHANGELOG.md has no '## $VERSION' entry"; exit 1; }

"$DOTNET" build src/MotorMod -c Release -nologo -v quiet
DLL=src/MotorMod/bin/Release/net472/MotorMod.dll

STAGE=$(mktemp -d); trap 'rm -rf "$STAGE"' EXIT
plugin() {   # $1 = destination folder, $2 = platforms
  mkdir -p "$1/bundles"
  cp "$DLL" "$1/"
  for bike in $BUNDLES; do for platform in $2; do cp "src/MotorMod/Bundles/$platform/$bike" "$1/bundles/${bike}_$platform"; done; done
}
plugin "$STAGE/player/MotorMod" "osx win"
plugin "$STAGE/server/MotorMod" "win"
mkdir -p "$STAGE/thunderstore/plugins"
plugin "$STAGE/thunderstore/plugins/MotorMod" "osx win"
cp README.md CHANGELOG.md tools/release/icon.png "$STAGE/thunderstore/"
sed "s/__VERSION__/$VERSION/" tools/release/manifest.json > "$STAGE/thunderstore/manifest.json"

mkdir -p dist
rm -f "dist/MotorMod-$VERSION.zip" "dist/MotorMod-$VERSION-server.zip" "dist/MotorMod-$VERSION-thunderstore.zip"
(cd "$STAGE/player" && zip -qr "$ROOT/dist/MotorMod-$VERSION.zip" MotorMod)
(cd "$STAGE/server" && zip -qr "$ROOT/dist/MotorMod-$VERSION-server.zip" MotorMod)
(cd "$STAGE/thunderstore" && zip -qr "$ROOT/dist/MotorMod-$VERSION-thunderstore.zip" .)
ls -lh dist/MotorMod-$VERSION*.zip
