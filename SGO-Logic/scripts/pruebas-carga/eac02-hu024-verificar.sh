#!/usr/bin/env bash
# =============================================================================
# SGO-Logic — HU-024 / EAC-02: Verificacion de Rendimiento bajo Carga
# Ejecuta la prueba k6 en contenedor, recolecta percentiles de latencia
# y certifica el cumplimiento del SLA P100 < 5.0 s (5000 ms).
# =============================================================================

set -e

DIR_SCRIPT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DIR_SGO="$(cd "${DIR_SCRIPT}/../.." && pwd)"
NETWORK_NAME="${DOCKER_NETWORK:-sgo-logic_default}"
VUS="${VUS:-25}"
DURACION_SEG="${DURACION_SEG:-30}"

echo "====================================================================="
echo " SGO-Logic — Verificacion de Carga EAC-02 / HU-024 (Black Friday)    "
echo "====================================================================="
echo "Red Docker: ${NETWORK_NAME}"
echo "Usuarios Virtuales (VUs): ${VUS}"
echo "Duracion: ${DURACION_SEG} segundos"
echo ""

# 1. Comprobar disponibilidad de servicios
echo "Comprobando disponibilidad de servicios..."
if ! docker ps --format '{{.Names}}' | grep -q "api-gateway"; then
    echo "[AVISO] Los contenedores de SGO-Logic no estan activos. Levantando stack..."
    cd "${DIR_SGO}" && docker compose up -d api-gateway consulta-estado-api redis db-operacional
    sleep 5
fi

# 2. Sembrar pool de 50 ordenes demostrativas en Redis
echo "Sembrando pool de 50 ordenes para alta concurrencia..."
REDIS_CONTAINER=$(docker ps -q -f name=redis | head -n1)
python3 -c '
import subprocess

redis_id = "'"${REDIS_CONTAINER}"'"
cmds = []
for i in range(1, 51):
    ord_id = f"ORD-2026-{i:03d}"
    cli_id = f"CLI-{(i%4)+1:03d}"
    num_orden = f"{i:06d}"
    val = f"{{\"OrdenId\":\"{ord_id}\",\"NumeroOrden\":\"{num_orden}\",\"ClienteId\":\"{cli_id}\",\"Estado\":\"CONFIRMADA\",\"Total\":1250.00,\"Canal\":\"WEB\",\"MetodoEntrega\":\"DOMICILIO\",\"Moneda\":\"GTQ\",\"CreadaEn\":\"2026-09-14T10:00:00.000Z\",\"ActualizadaEn\":\"2026-09-14T10:00:00.000Z\"}}"
    cmds.append(f"SET orden:{ord_id} '\''{val}'\''")

pipe_script = "\n".join(cmds)
p = subprocess.Popen(f"docker exec -i {redis_id} redis-cli -a change_me", shell=True, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
p.communicate(input=pipe_script.encode())
'

echo "Pool de ordenes preparado exitosamente."
echo ""

# 3. Ejecutar prueba con k6 en contenedor
echo "Iniciando simulacion de carga con Grafana k6 en contenedor..."
docker run --rm \
    --network "${NETWORK_NAME}" \
    -v "${DIR_SCRIPT}:/scripts" \
    -e VUS="${VUS}" \
    -e DURACION_SEG="${DURACION_SEG}" \
    -e GATEWAY_HOST="api-gateway:8080" \
    grafana/k6:latest run /scripts/eac02-hu024-carga.js

# 4. Procesar y certificar resultados
RESULTADO_JSON="${DIR_SCRIPT}/eac02-resultado.json"

if [ -f "${RESULTADO_JSON}" ]; then
    echo ""
    echo "====================================================================="
    echo " CERTIFICACION FORMAL DE CRITERIOS DE ACEPTACION (HU-024)            "
    echo "====================================================================="
    
    python3 -c '
import json, sys

with open("'"${RESULTADO_JSON}"'") as f:
    data = json.load(f)

metrics = data.get("metrics", {})
duration = metrics.get("http_req_duration", {}).get("values", {})
reqs = metrics.get("http_reqs", {}).get("values", {}).get("count", 0)
fails = metrics.get("http_req_failed", {}).get("values", {}).get("rate", 0.0) * 100

max_ms = duration.get("max", 99999)
p99_ms = duration.get("p(99)", 0)
p95_ms = duration.get("p(95)", 0)
p50_ms = duration.get("p(50)", 0)
avg_ms = duration.get("avg", 0)

min_ms = duration.get("min", 0)
print(f"Total consultas procesadas : {reqs}")
print(f"Tasa de error HTTP         : {fails:.2f}%")
print(f"Latencia Minima            : {min_ms:.2f} ms")
print(f"Latencia Mediana (P50)     : {p50_ms:.2f} ms")
print(f"Latencia Percentil 95 (P95): {p95_ms:.2f} ms")
print(f"Latencia Percentil 99 (P99): {p99_ms:.2f} ms")
print(f"Latencia Maxima (P100)     : {max_ms:.2f} ms")
print("")

sla_limite = 5000.0 # 5.0 segundos
if max_ms < sla_limite and fails == 0:
    print(f"DICTAMEN: APROBADO")
    print(f"-> El 100% de las consultas respondio en menos de 5 segundos (P100 = {max_ms:.2f} ms < 5000 ms).")
    print(f"-> Criterio de aceptacion de HU-024 y SLA EAC-02 CUMPLIDOS AL 100%.")
    sys.exit(0)
else:
    print(f"DICTAMEN: NO APROBADO (P100 = {max_ms:.2f} ms >= 5000 ms o tasa de fallas = {fails}%)")
    sys.exit(1)
'
else
    echo "Error: No se encontro el archivo de resultados ${RESULTADO_JSON}"
    exit 1
fi
