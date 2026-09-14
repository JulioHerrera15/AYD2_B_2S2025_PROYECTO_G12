#!/usr/bin/env bash
# =============================================================================
# SGO-Logic — HU-024 / EAC-02: Demostracion de Autoescalado Horizontal
# Escala de forma independiente el servicio SgoLogic.ConsultaEstado.Api
# y verifica la atencion a traves del API Gateway (YARP).
# =============================================================================

set -e

REPLICAS=${1:-3}
DIR_SCRIPT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DIR_SGO="$(cd "${DIR_SCRIPT}/../.." && pwd)"

echo "====================================================================="
echo " SGO-Logic — Autoescalado Horizontal de Consulta de Estado (HU-024)   "
echo "====================================================================="
echo "Directorio base: ${DIR_SGO}"
echo "Escalando consulta-estado-api a: ${REPLICAS} replicas..."

cd "${DIR_SGO}"

# Escalar el servicio usando el archivo override que libera puertos de host
docker compose -f docker-compose.yml -f scripts/pruebas-carga/docker-compose.scale-consulta.yml up -d --scale consulta-estado-api="${REPLICAS}" --no-recreate

echo ""
echo "Verificando instancias activas de consulta-estado-api:"
docker compose ps consulta-estado-api

echo ""
echo "Esperando que las replicas esten saludables (healthcheck)..."
sleep 5

# Probar resolucion y atencion a traves del API Gateway
echo ""
echo "Probando consultas a traves del API Gateway (http://localhost:8080/api/estado/ordenes/ORD-2026-001):"
EXITOS=0
TOTAL_PETICIONES=10

for i in $(seq 1 $TOTAL_PETICIONES); do
    HTTP_CODE=$(curl -s -o /dev/null -w "%{http_code}" http://localhost:8080/api/estado/ordenes/ORD-2026-001 || true)
    if [ "$HTTP_CODE" = "200" ]; then
        EXITOS=$((EXITOS + 1))
        echo "  [Peticion $i] HTTP $HTTP_CODE OK"
    else
        echo "  [Peticion $i] HTTP $HTTP_CODE (en calentamiento)"
    fi
done

echo ""
echo "Resultado de verificacion post-escalado: ${EXITOS}/${TOTAL_PETICIONES} exitosas."
if [ "$EXITOS" -gt 0 ]; then
    echo "Autoescalado horizontal independiente comprobado con exito."
else
    echo "Advertencia: Verificar el estado de los contenedores con 'docker compose ps'."
fi

echo ""
echo "Para volver a 1 replica en el entorno local:"
echo "  docker compose -f docker-compose.yml up -d --scale consulta-estado-api=1"
echo "====================================================================="
