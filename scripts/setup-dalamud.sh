#!/usr/bin/env bash
set -Eeuo pipefail

destination="${DALAMUD_HOME:-${1:-$PWD/.cache/dalamud}}"
archive="${destination%/}.zip"
source_url="${DALAMUD_DISTRIBUTION_URL:-https://goatcorp.github.io/dalamud-distrib/latest.zip}"

if [[ -f "$destination/Dalamud.dll" && -f "$destination/Dalamud.Bindings.ImGui.dll" ]]; then
    printf 'Dalamud development files already available at %s\n' "$destination"
    exit 0
fi

command -v curl >/dev/null 2>&1 || { echo 'curl is required' >&2; exit 1; }
command -v unzip >/dev/null 2>&1 || { echo 'unzip is required' >&2; exit 1; }

install -d -m 755 "$(dirname -- "$destination")"
rm -rf -- "$destination"
install -d -m 755 "$destination"

printf 'Downloading Dalamud development distribution from %s\n' "$source_url"
curl --fail --location --silent --show-error "$source_url" --output "$archive"
unzip -q "$archive" -d "$destination"
rm -f -- "$archive"

[[ -f "$destination/Dalamud.dll" && -f "$destination/Dalamud.Bindings.ImGui.dll" ]] || {
    echo 'downloaded distribution is missing required assemblies' >&2
    exit 1
}

printf 'Installed Dalamud development files at %s\n' "$destination"
