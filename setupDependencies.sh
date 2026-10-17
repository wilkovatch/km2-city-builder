#!/usr/bin/env bash
# Downloads the editor plugins into the "addons" folder (same as setupDependencies.ps1, for Linux and macOS)
set -euo pipefail
cd "$(dirname "$0")"

pluginUrls=(
    "https://github.com/TokisanGames/Terrain3D/releases/download/v1.0.1/Terrain3D_v1.0.1.zip"
)

addonsDir="addons"
tempExtractDir="temp_plugin_extract"

for pluginUrl in "${pluginUrls[@]}"; do
    zipFile="$(basename "$pluginUrl")"

    # Clean up old temp directory
    rm -rf "$tempExtractDir"
    mkdir -p "$tempExtractDir"

    echo
    echo "Downloading $zipFile from $pluginUrl..."
    curl -fL -o "$zipFile" "$pluginUrl"

    echo "Extracting $zipFile to temporary directory..."
    unzip -o -q "$zipFile" -d "$tempExtractDir"

    foundAddons="$(find "$tempExtractDir" -type d -name addons | head -n 1)"

    if [ -z "$foundAddons" ]; then
        echo "No 'addons' folder found in $zipFile. Skipping."
        rm -f "$zipFile"
        rm -rf "$tempExtractDir"
        continue
    fi

    mkdir -p "$addonsDir"

    echo "Copying plugin from '$zipFile' to '$addonsDir'..."
    cp -R "$foundAddons"/. "$addonsDir"

    rm -f "$zipFile"
    rm -rf "$tempExtractDir"

    echo "$zipFile installed successfully in '$addonsDir'."
done
