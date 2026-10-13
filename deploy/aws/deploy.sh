#!/usr/bin/env bash
# Se ejecuta EN la EC2 (lo invoca cd.yml por SSH). Requiere GHCR_USER y GHCR_TOKEN.
set -euo pipefail

cd ~/sgo-logic/SGO-Logic
COMPOSE="docker compose --env-file .env --env-file .env.deploy -f docker-compose.yml -f docker-compose.prod.yml"

echo "$GHCR_TOKEN" | docker login ghcr.io -u "$GHCR_USER" --password-stdin

echo ">> Descargando imagenes"
$COMPOSE pull --quiet

echo ">> Levantando servicios"
$COMPOSE up -d --no-build --remove-orphans

echo ">> Esperando a que gateway y web esten sanos (maximo 10 min)"
for servicio in api-gateway web; do
  id=$($COMPOSE ps -q "$servicio")
  for i in $(seq 1 60); do
    estado=$(docker inspect -f '{{.State.Health.Status}}' "$id" 2>/dev/null || echo "sin-estado")
    [ "$estado" = "healthy" ] && break
    sleep 10
  done
  echo "$servicio: $estado"
  if [ "$estado" != "healthy" ]; then
    $COMPOSE ps
    $COMPOSE logs --tail=60 "$servicio" || true
    docker logout ghcr.io || true
    exit 1
  fi
done

$COMPOSE ps
docker image prune -f >/dev/null
docker logout ghcr.io || true
echo ">> Despliegue completado: tag $(grep IMAGE_TAG .env.deploy | cut -d= -f2)"
