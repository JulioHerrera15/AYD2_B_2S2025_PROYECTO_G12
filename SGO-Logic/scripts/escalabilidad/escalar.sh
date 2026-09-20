#!/usr/bin/env bash
# =============================================================================
# SGO-Logic — HU-025 / EAC-04: Escalado horizontal del Backbone
# Levanta N replicas de ordenes-api (sabor API) y auditoria-worker (sabor
# Worker) usando el override que libera sus puertos de host fijos, y espera a
# que todas las replicas queden sanas.
#
# Uso:
#   ./escalar.sh [REPLICAS]     # por defecto 3
#
# Requiere estar sobre el resto del stack ya arriba o lo levanta de una vez
# (docker compose up ya trae todo lo demas con --no-recreate no forzando
# recreacion de lo que no cambio).
# =============================================================================

set -euo pipefail

REPLICAS="${1:-3}"
DIR_SCRIPT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DIR_SGO="$(cd "${DIR_SCRIPT}/../.." && pwd)"
OVERRIDE="${DIR_SCRIPT}/docker-compose.scale.yml"

echo "====================================================================="
echo " SGO-Logic — Escalado horizontal (HU-025 / EAC-04)                    "
echo "====================================================================="
echo "Directorio base : ${DIR_SGO}"
echo "Replicas por servicio: ${REPLICAS}"
echo "ordenes-api (API) y auditoria-worker (Worker)"
echo ""

cd "${DIR_SGO}"

docker compose -f docker-compose.yml -f "${OVERRIDE}" \
  up -d --scale ordenes-api="${REPLICAS}" --scale auditoria-worker="${REPLICAS}" --no-recreate

echo ""
echo "Esperando a que las replicas reporten 'healthy'..."
ESPERA_MAX_SEG=90
transcurrido=0
while true; do
  no_sanos="$(docker compose ps --format '{{.Service}} {{.Health}}' \
    | awk '($1=="ordenes-api" || $1=="auditoria-worker") && $2!="healthy" {print}')"
  if [ -z "${no_sanos}" ]; then
    echo "Todas las replicas de ordenes-api y auditoria-worker estan 'healthy'."
    break
  fi
  if [ "${transcurrido}" -ge "${ESPERA_MAX_SEG}" ]; then
    echo "" >&2
    echo "Advertencia: algunas replicas no llegaron a 'healthy' tras ${ESPERA_MAX_SEG}s:" >&2
    echo "${no_sanos}" >&2
    break
  fi
  sleep 5
  transcurrido=$((transcurrido + 5))
done

echo ""
echo "Estado de las replicas:"
docker compose ps ordenes-api auditoria-worker

echo ""
echo "---------------------------------------------------------------------"
echo "Siguiente paso: confirma en Prometheus (http://localhost:9090/targets,"
echo "job 'sgo-logic') que cada replica aparece como un target 'instance'"
echo "distinto para ordenes-api y auditoria-worker."
echo ""
echo "Luego corre la carga (ver carga.js) y, al terminar, verificar.sh."
echo ""
echo "Para volver a 1 replica:"
echo "  docker compose up -d --scale ordenes-api=1 --scale auditoria-worker=1"
echo "====================================================================="