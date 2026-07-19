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
    status=$?
    if (( status != 0 )); then
        "${compose[@]}" logs api >&2 || true
    fi
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

account_id='719dc22a-c560-4b23-bad6-8b7a90e807dd'
"${compose[@]}" exec --no-TTY postgres \
    psql --username xivfm --dbname xivfm --set ON_ERROR_STOP=1 \
    --command "insert into lastfm_accounts (account_id, canonical_name, normalized_name, created_at) values ('$account_id', 'ContainerListener', 'CONTAINERLISTENER', now()); update installation_credentials set account_id = '$account_id';" \
    >/dev/null

relay_id="$(curl -fsS \
    -H "Authorization: Bearer $credential" \
    -H 'Content-Type: application/json' \
    --data '{"name":"Container Relay","idempotencyKey":"66384462-b624-4a9a-bad8-aeb142403ba8"}' \
    "$base_url/v1/relays" | jq -er '.relayId')"
invitation_token="$(curl -fsS \
    -H "Authorization: Bearer $credential" \
    -H 'Content-Type: application/json' \
    --data '{}' \
    "$base_url/v1/relays/$relay_id/invitations" | jq -er '.token')"
member_credential="$(openssl rand -base64 32 | tr '+/' '-_' | tr -d '=')"
member_hash="$(printf '%s' "$member_credential" | sha256sum | cut -d' ' -f1 | tr '[:lower:]' '[:upper:]')"
member_account_id='9fc96aa1-a739-4de8-8f3d-3fce185d21e3'
member_installation_id='81c94fd6-a8b3-4285-be21-251a03d5fe36'
"${compose[@]}" exec --no-TTY postgres \
    psql --username xivfm --dbname xivfm --set ON_ERROR_STOP=1 \
    --command "insert into lastfm_accounts (account_id, canonical_name, normalized_name, created_at) values ('$member_account_id', 'ContainerMember', 'CONTAINERMEMBER', now()); insert into installation_credentials (installation_id, account_id, credential_hash, created_at) values ('$member_installation_id', '$member_account_id', '$member_hash', now());" \
    >/dev/null
curl -fsS \
    -H "Authorization: Bearer $member_credential" \
    -H 'Content-Type: application/json' \
    --data "{\"token\":\"$invitation_token\"}" \
    "$base_url/v1/relay-invitations/accept" \
    | jq -e --arg relay_id "$relay_id" '.relay.relayId == $relay_id and .relay.memberCount == 2' >/dev/null

curl -fsS \
    -H "Authorization: Bearer $credential" \
    -H 'Content-Type: application/json' \
    --data "{\"pluginVersion\":\"0.1.3.0\",\"character\":{\"name\":\"Alice Cat\",\"homeWorldId\":54},\"location\":{\"currentWorldId\":63,\"territoryId\":129,\"mapId\":130,\"instanceId\":2},\"visibility\":{\"mode\":\"custom\",\"relayIds\":[\"$relay_id\"]},\"knownSnapshotVersion\":null}" \
    "$base_url/v1/sync" >/dev/null
relay_response="$(curl -fsS \
    -H "Authorization: Bearer $member_credential" \
    -H 'Content-Type: application/json' \
    --data "{\"pluginVersion\":\"0.1.3.0\",\"character\":{\"name\":\"Bob Cat\",\"homeWorldId\":55},\"location\":{\"currentWorldId\":63,\"territoryId\":129,\"mapId\":130,\"instanceId\":2},\"visibility\":{\"mode\":\"custom\",\"relayIds\":[\"$relay_id\"]},\"knownSnapshotVersion\":null}" \
    "$base_url/v1/sync")"
jq -e '.locationPresence.snapshot.entries | length == 2' <<<"$relay_response" >/dev/null

member_membership_id="$(curl -fsS \
    -H "Authorization: Bearer $credential" \
    "$base_url/v1/relays/$relay_id/members" \
    | jq -er '.members[] | select(.isOwner == false) | .membershipId')"
curl -fsS -X DELETE \
    -H "Authorization: Bearer $credential" \
    "$base_url/v1/relays/$relay_id/members/$member_membership_id" >/dev/null
kicked_status="$(curl -sS -o /dev/null -w '%{http_code}' \
    -H "Authorization: Bearer $member_credential" \
    -H 'Content-Type: application/json' \
    --data "{\"pluginVersion\":\"0.1.3.0\",\"character\":{\"name\":\"Bob Cat\",\"homeWorldId\":55},\"location\":{\"currentWorldId\":63,\"territoryId\":129,\"mapId\":130,\"instanceId\":2},\"visibility\":{\"mode\":\"custom\",\"relayIds\":[\"$relay_id\"]},\"knownSnapshotVersion\":null}" \
    "$base_url/v1/sync")"
[[ "$kicked_status" == '403' ]] || {
    echo "Kicked Relay member unexpectedly synced with status $kicked_status" >&2
    exit 1
}
curl -fsS \
    -H "Authorization: Bearer $credential" \
    -H 'Content-Type: application/json' \
    --data "{\"pluginVersion\":\"0.1.3.0\",\"character\":{\"name\":\"Alice Cat\",\"homeWorldId\":54},\"location\":{\"currentWorldId\":63,\"territoryId\":129,\"mapId\":130,\"instanceId\":2},\"visibility\":{\"mode\":\"custom\",\"relayIds\":[\"$relay_id\"]},\"knownSnapshotVersion\":null}" \
    "$base_url/v1/sync" \
    | jq -e '.locationPresence.snapshot.entries | length == 1' >/dev/null

relay_snapshot_key="$("${compose[@]}" exec --no-TTY redis \
    redis-cli --raw --scan --pattern "xivfm:snapshot:relay:$relay_id:*" | head -1)"
[[ -n "$relay_snapshot_key" ]] || {
    echo 'Redis did not store the shared Relay/location snapshot' >&2
    exit 1
}
invitation_hash_length="$("${compose[@]}" exec --no-TTY postgres \
    psql --username xivfm --dbname xivfm --tuples-only --no-align \
    --command 'select length(token_hash) from relay_invitations limit 1')"
[[ "$invitation_hash_length" == '64' ]] || {
    echo 'PostgreSQL did not persist only the invitation token hash' >&2
    exit 1
}

public_response="$(curl -fsS \
    -H "Authorization: Bearer $credential" \
    -H 'Content-Type: application/json' \
    --data '{"pluginVersion":"0.1.3.0","character":{"name":"Alice Cat","homeWorldId":54},"location":{"currentWorldId":63,"territoryId":129,"mapId":130,"instanceId":2},"visibility":{"mode":"public","relayIds":[]},"knownSnapshotVersion":null}' \
    "$base_url/v1/sync")"
jq -e '.locationPresence.snapshot.entries | length == 1' <<<"$public_response" >/dev/null
jq -e '.locationPresence.snapshot.entries[0].character.name == "Alice Cat"' <<<"$public_response" >/dev/null

snapshot_key="$("${compose[@]}" exec --no-TTY redis \
    redis-cli --raw --scan --pattern 'xivfm:snapshot:public:*' | head -1)"
[[ -n "$snapshot_key" ]] || {
    echo 'Redis did not store the shared public snapshot' >&2
    exit 1
}
snapshot_ttl="$("${compose[@]}" exec --no-TTY redis redis-cli --raw ttl "$snapshot_key")"
(( snapshot_ttl > 0 && snapshot_ttl <= 20 )) || {
    echo "Redis snapshot TTL is invalid: $snapshot_ttl" >&2
    exit 1
}

curl -fsS \
    -H "Authorization: Bearer $credential" \
    -H 'Content-Type: application/json' \
    --data '{"pluginVersion":"0.1.3.0","character":{"name":"Alice Cat","homeWorldId":54},"location":{"currentWorldId":63,"territoryId":129,"mapId":130,"instanceId":2},"visibility":{"mode":"private","relayIds":[]},"knownSnapshotVersion":null}' \
    "$base_url/v1/sync" \
    | jq -e '.locationPresence.snapshot.entries | length == 0' >/dev/null

publication_count="$("${compose[@]}" exec --no-TTY redis \
    redis-cli --raw zcard 'xivfm:presence:public:63:129:130:2')"
[[ "$publication_count" == '0' ]] || {
    echo 'Private sync did not remove public publication' >&2
    exit 1
}

printf 'XIV.fm disposable container integration test passed.\n'
