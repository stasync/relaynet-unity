#!/bin/sh
# Replaces the vendored Keel DLLs with a netstandard2.1 build from https://github.com/stasync/keel-dotnet/releases.
# Usage: sh "Tools~/update-keel.sh" [tag]    (default tag: latest-build)
# The .meta files are left alone, so the DLL GUIDs don't change.
set -eu

tag="${1:-latest-build}"
root="$(cd "$(dirname "$0")/.." && pwd)"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

curl -fsSL -o "$tmp/keel.zip" "https://github.com/stasync/keel-dotnet/releases/download/$tag/keel-$tag-netstandard2.1.zip"
unzip -q "$tmp/keel.zip" -d "$tmp/out"

cp "$tmp/out/Keel.Networking.dll" "$tmp/out/Keel.Utils.dll" "$root/Plugins/Keel/"
cp "$tmp/out/LICENSE" "$root/Plugins/Keel/LICENSE.md"

echo "Keel '$tag' copied into Plugins/Keel. Record the version and the SHA-256 in CHANGELOG.md:"
shasum -a 256 "$root/Plugins/Keel/"*.dll
