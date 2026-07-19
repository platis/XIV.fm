#!/usr/bin/env bash
set -Eeuo pipefail

repository_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repository_root"

for required_command in docker openssl curl jq ss; do
    command -v "$required_command" >/dev/null 2>&1 || {
        echo "$required_command is required" >&2
        exit 1
    }
done

docker compose version >/dev/null 2>&1 || {
    echo 'Docker Compose is required' >&2
    exit 1
}

port="${XIVFM_TEST_PORT:-15080}"
if ss -H -ltn "sport = :$port" | grep -q .; then
    echo "loopback test port $port is already in use" >&2
    exit 1
fi

project="xivfm-integration-$$"
env_file="$(mktemp)"
credential="$(openssl rand -base64 32 | tr '+/' '-_' | tr -d '=')"
password="$(openssl rand -hex 24)"
printf 'XIVFM_TEST_PORT=%s\nPOSTGRES_PASSWORD=%s\nXIVFM_DEV_INSTALLATION_CREDENTIAL=%s\n' \
    "$port" "$password" "$credential" >"$env_file"
chmod 600 "$env_file"

compose=(docker compose --project-name "$project" --env-file "$env_file" -f deploy/compose.integration.yaml)
cleanup() {
    "${compose[@]}" down --volumes --remove-orphans >/dev/null 2>&1 || true
    docker image rm "${project}-api:latest" >/dev/null 2>&1 || true
    rm -f "$env_file"
}
trap cleanup EXIT

"${compose[@]}" config --quiet
"${compose[@]}" up --build --detach --wait

postgres_container="$("${compose[@]}" ps --quiet postgres)"
redis_container="$("${compose[@]}" ps --quiet redis)"
for container in "$postgres_container" "$redis_container"; do
    bindings="$(docker inspect --format '{{json .HostConfig.PortBindings}}' "$container")"
    [[ "$bindings" == '{}' || "$bindings" == 'null' ]] || {
        echo 'database or Redis unexpectedly published a host port' >&2
        exit 1
    }
done

api_container="$("${compose[@]}" ps --quiet api)"
api_host_ip="$(docker inspect --format '{{(index (index .HostConfig.PortBindings "8080/tcp") 0).HostIp}}' "$api_container")"
[[ "$api_host_ip" == '127.0.0.1' ]] || {
    echo "API test port is not loopback-only: $api_host_ip" >&2
    exit 1
}

base_url="http://127.0.0.1:$port"
curl -fsS "$base_url/health/ready" | jq -e '.status == "healthy"' >/dev/null
curl -fsS \
    -H "Authorization: Bearer $credential" \
    -H 'Content-Type: application/json' \
    --data '{"pluginVersion":"0.1.2.0","character":{"name":"Alice Cat","homeWorldId":54},"location":{"currentWorldId":63,"territoryId":129,"mapId":130,"instanceId":2},"visibility":{"mode":"private","relayIds":[]},"knownSnapshotVersion":null}' \
    "$base_url/v1/sync" \
    | jq -e '.ownListening.status == "unavailable" and .nextSyncAfterSeconds == 30' >/dev/null

hash_length="$("${compose[@]}" exec --no-TTY postgres \
    psql --username xivfm --dbname xivfm --tuples-only --no-align \
    --command 'select length(credential_hash) from installation_credentials limit 1')"
[[ "$hash_length" == '64' ]] || {
    echo 'PostgreSQL did not persist a SHA-256 credential hash' >&2
    exit 1
}

presence_key="$("${compose[@]}" exec --no-TTY redis \
    redis-cli --raw --scan --pattern 'xivfm:presence:installation:*' | head -1)"
[[ -n "$presence_key" ]] || {
    echo 'Redis did not store the presence heartbeat' >&2
    exit 1
}
presence_ttl="$("${compose[@]}" exec --no-TTY redis redis-cli --raw ttl "$presence_key")"
(( presence_ttl > 0 && presence_ttl <= 60 )) || {
    echo "Redis presence TTL is invalid: $presence_ttl" >&2
    exit 1
}

printf 'XIV.fm disposable container integration test passed.\n'
