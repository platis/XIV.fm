#!/usr/bin/env bash
# Runs the PostgreSQL and Redis adapter tests against disposable loopback-only containers.
set -Eeuo pipefail

repository_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repository_root"

for required_command in docker dotnet openssl; do
    command -v "$required_command" >/dev/null 2>&1 || {
        echo "$required_command is required" >&2
        exit 1
    }
done

# Use exactly the images the integration stack pins.
postgres_image="$(sed -n 's/^ *image: \(postgres:.*\)$/\1/p' deploy/compose.integration.yaml)"
redis_image="$(sed -n 's/^ *image: \(redis:.*\)$/\1/p' deploy/compose.integration.yaml)"
postgres_container="xivfm-durable-postgres-$$"
redis_container="xivfm-durable-redis-$$"
password="$(openssl rand -hex 24)"

cleanup() {
    docker rm --force "$postgres_container" "$redis_container" >/dev/null 2>&1 || true
}
trap cleanup EXIT

docker run --detach --rm --name "$postgres_container" \
    --env POSTGRES_DB=xivfm --env POSTGRES_USER=xivfm --env POSTGRES_PASSWORD="$password" \
    --tmpfs /var/lib/postgresql/data --publish 127.0.0.1::5432 "$postgres_image" >/dev/null
docker run --detach --rm --name "$redis_container" \
    --tmpfs /data --publish 127.0.0.1::6379 "$redis_image" \
    redis-server --save '' --appendonly no >/dev/null

# TCP readiness: the image's initialization server listens only on its Unix socket.
for _ in $(seq 1 60); do
    if docker exec "$postgres_container" pg_isready --host 127.0.0.1 --username xivfm --dbname xivfm >/dev/null 2>&1 &&
        docker exec "$redis_container" redis-cli ping >/dev/null 2>&1; then
        ready=true
        break
    fi
    sleep 1
done
[[ "${ready:-false}" == 'true' ]] || {
    echo 'The disposable stores did not become ready' >&2
    exit 1
}

XIVFM_TEST_POSTGRES="Host=127.0.0.1;Port=$(docker port "$postgres_container" 5432/tcp | sed 's/.*://');Database=xivfm;Username=xivfm;Password=$password" \
XIVFM_TEST_REDIS="127.0.0.1:$(docker port "$redis_container" 6379/tcp | sed 's/.*://')" \
    dotnet test tests/XIV.fm.Server.DurableTests/XIV.fm.Server.DurableTests.csproj \
    --configuration Release \
    --verbosity minimal
