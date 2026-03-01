#!/usr/bin/env bash
set -euo pipefail

for cmd in dotnet curl unzip; do
    if ! command -v "$cmd" &>/dev/null; then
        echo "Error: '$cmd' is required but not installed." >&2
        exit 1
    fi
done

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
OUTPUT="$SCRIPT_DIR/Release"

# Download libsteam_api.so from NuGet if not present
if [ ! -f "$OUTPUT/libsteam_api.so" ]; then
    echo "Downloading libsteam_api.so ..."
    mkdir -p "$OUTPUT"
    TMP="$(mktemp)"
    curl -sL -o "$TMP" 'https://www.nuget.org/api/v2/package/Facepunch.Steamworks.Dll/1.53.0'
    unzip -oj "$TMP" 'runtimes/linux-x64/native/libsteam_api.so' -d "$OUTPUT"
    rm -f "$TMP"
fi

# Clean only the executables
rm -f "$OUTPUT/SteamAchievementUnlocker" "$OUTPUT/Client"

dotnet publish Server/Server.csproj -c Release -r linux-x64 -o "$OUTPUT" --nologo -v quiet
dotnet publish Client/Client.csproj -c Release -r linux-x64 -o "$OUTPUT" --nologo -v quiet

rm -f "$OUTPUT"/*.pdb

echo "Build located at: $OUTPUT"
