#!/usr/bin/env bash
set -Eeuo pipefail

repository_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repository_root"

command -v dotnet >/dev/null 2>&1 || {
    echo 'dotnet is required; add the .NET 10 SDK to PATH' >&2
    exit 1
}

if [[ -z "${DALAMUD_HOME:-}" ]]; then
    if [[ -f /srv/cache/dalamud/api15/Dalamud.dll ]]; then
        export DALAMUD_HOME=/srv/cache/dalamud/api15
    else
        export DALAMUD_HOME="$repository_root/.cache/dalamud"
    fi
fi

if [[ ! -f "$DALAMUD_HOME/Dalamud.dll" ]]; then
    "$repository_root/scripts/setup-dalamud.sh" "$DALAMUD_HOME"
fi

printf 'Restoring locked dependencies...\n'
dotnet restore XIV.fm.slnx --locked-mode

printf 'Checking formatting...\n'
dotnet format XIV.fm.slnx --verify-no-changes --no-restore

printf 'Running core tests...\n'
dotnet test tests/XIV.fm.Plugin.Core.Tests/XIV.fm.Plugin.Core.Tests.csproj \
    --no-restore \
    --configuration Release \
    --verbosity minimal

printf 'Building the Dalamud plugin...\n'
dotnet build src/XIV.fm.Plugin/XIV.fm.Plugin.csproj \
    --no-restore \
    --configuration Release \
    --verbosity minimal

printf 'XIV.fm quality gates passed.\n'
