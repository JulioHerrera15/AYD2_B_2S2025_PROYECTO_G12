#!/usr/bin/env bash
# EAC-01 / HU-023 — verificacion de "cero ordenes perdidas" al final de la
# prueba de carga (eac01-hu023-carga.js). Corre contra las bases reales via
# `docker compose exec`, sin agregar ninguna dependencia nueva: usa el
# cliente psql que ya trae la imagen postgres:16-alpine de db-operacional y
# db-finanzas (docker-compose.yml de este repo).
#
# Definicion operativa de "orden perdida" para esta prueba (fijada por el
# encargo, no inventada aqui):
#   - una orden creada que no tiene NINGUN intento de pago registrado en
#     pagos.intento_pago, o
#   - una orden que quedo en un estado no consultable (sin fila en
#     consulta_estado.ordenes_vista, el modelo de lectura de EAC-02).
# Cero ordenes en esa condicion es el criterio de aprobacion.
#
# Uso:
#   ./eac01-hu023-verificar.sh                  # todas las corridas de esta prueba (prefijo CARGA-EAC01-%)
#   RUN_ID=1234567890 ./eac01-hu023-verificar.sh  # solo la corrida con ese RUN_ID (el que imprimio eac01-hu023-carga.js)
#
# Requiere que el stack de docker compose (docker-compose.yml en la raiz del
# repo) este arriba: docker compose up -d

set -euo pipefail

RAIZ="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
COMPOSE_FILE="$RAIZ/docker-compose.yml"

if [ ! -f "$COMPOSE_FILE" ]; then
  echo "No se encontro docker-compose.yml en $RAIZ" >&2
  exit 1
fi

PREFIJO="CARGA-EAC01-%"
if [ -n "${RUN_ID:-}" ]; then
  PREFIJO="CARGA-EAC01-${RUN_ID}-%"
fi

TMP_DIR="$(mktemp -d)"
trap 'rm -rf "$TMP_DIR"' EXIT

compose() {
  docker compose -f "$COMPOSE_FILE" "$@"
}

# Credenciales reales del contenedor (las que trae su propio POSTGRES_USER /
# POSTGRES_PASSWORD via el .env del repo), leidas una sola vez para no andar
# anidando comillas de shell dentro de shell en cada consulta.
leer_credencial() {
  local servicio="$1" variable="$2"
  compose exec -T "$servicio" sh -c "printf '%s' \"\$$variable\""
}

USUARIO_OPERACIONAL="$(leer_credencial db-operacional POSTGRES_USER)"
CLAVE_OPERACIONAL="$(leer_credencial db-operacional POSTGRES_PASSWORD)"
USUARIO_FINANZAS="$(leer_credencial db-finanzas POSTGRES_USER)"
CLAVE_FINANZAS="$(leer_credencial db-finanzas POSTGRES_PASSWORD)"

psql_operacional() {
  local base="$1"; shift
  compose exec -T -e PGPASSWORD="$CLAVE_OPERACIONAL" db-operacional \
    psql -U "$USUARIO_OPERACIONAL" -d "$base" -v ON_ERROR_STOP=1 -tA "$@"
}

psql_finanzas() {
  local base="$1"; shift
  compose exec -T -e PGPASSWORD="$CLAVE_FINANZAS" db-finanzas \
    psql -U "$USUARIO_FINANZAS" -d "$base" -v ON_ERROR_STOP=1 -tA "$@"
}

# ---------------------------------------------------------------------------
# 1) ordenes.orden (db sgo_ordenes, host db-operacional)
# ---------------------------------------------------------------------------
psql_operacional sgo_ordenes -c \
  "SELECT id FROM ordenes.orden WHERE cliente_id LIKE '${PREFIJO}' ORDER BY id;" \
  | tr -d '\r' | sed '/^$/d' | sort -u > "$TMP_DIR/creadas.txt"

psql_operacional sgo_ordenes -c \
  "SELECT id FROM ordenes.orden WHERE cliente_id LIKE '${PREFIJO}' AND estado = 'COBRO_SOLICITADO' ORDER BY id;" \
  | tr -d '\r' | sed '/^$/d' | sort -u > "$TMP_DIR/cobro_solicitado.txt"

psql_operacional sgo_ordenes -c \
  "SELECT estado, count(*) FROM ordenes.orden WHERE cliente_id LIKE '${PREFIJO}' GROUP BY estado ORDER BY estado;" \
  | tr -d '\r' | sed '/^$/d' > "$TMP_DIR/por_estado.txt"

# ---------------------------------------------------------------------------
# 2) consulta_estado.ordenes_vista (db sgo_consulta_estado, host db-operacional)
# ---------------------------------------------------------------------------
psql_operacional sgo_consulta_estado -c \
  "SELECT orden_id FROM consulta_estado.ordenes_vista WHERE cliente_id LIKE '${PREFIJO}' ORDER BY orden_id;" \
  | tr -d '\r' | sed '/^$/d' | sort -u > "$TMP_DIR/consultables.txt"

# ---------------------------------------------------------------------------
# 3) pagos.pago / pagos.intento_pago (db sgo_pagos, host db-finanzas)
# ---------------------------------------------------------------------------
psql_finanzas sgo_pagos -c \
  "SELECT DISTINCT orden_id FROM pagos.pago WHERE cliente_id LIKE '${PREFIJO}' ORDER BY orden_id;" \
  | tr -d '\r' | sed '/^$/d' | sort -u > "$TMP_DIR/con_intento.txt"

psql_finanzas sgo_pagos -c \
  "SELECT i.estado, count(*) FROM pagos.intento_pago i JOIN pagos.pago p ON p.id = i.pago_id WHERE p.cliente_id LIKE '${PREFIJO}' GROUP BY i.estado ORDER BY i.estado;" \
  | tr -d '\r' | sed '/^$/d' > "$TMP_DIR/intentos_por_resultado.txt"

# ---------------------------------------------------------------------------
# Combinacion en el shell (las tres bases viven en dos instancias de
# Postgres distintas: no se pueden hacer estos cruces con un solo JOIN SQL).
# ---------------------------------------------------------------------------
comm -23 "$TMP_DIR/creadas.txt" "$TMP_DIR/con_intento.txt" > "$TMP_DIR/sin_intento.txt" || true
comm -23 "$TMP_DIR/creadas.txt" "$TMP_DIR/consultables.txt" > "$TMP_DIR/no_consultable.txt" || true
comm -23 "$TMP_DIR/cobro_solicitado.txt" "$TMP_DIR/con_intento.txt" > "$TMP_DIR/atascadas_cobro_solicitado.txt" || true
cat "$TMP_DIR/sin_intento.txt" "$TMP_DIR/no_consultable.txt" | sort -u > "$TMP_DIR/perdidas.txt"

contar() { wc -l < "$1" | tr -d ' '; }

TOTAL_CREADAS=$(contar "$TMP_DIR/creadas.txt")
TOTAL_CONSULTABLES=$(contar "$TMP_DIR/consultables.txt")
TOTAL_SIN_INTENTO=$(contar "$TMP_DIR/sin_intento.txt")
TOTAL_NO_CONSULTABLE=$(contar "$TMP_DIR/no_consultable.txt")
TOTAL_ATASCADAS_COBRO=$(contar "$TMP_DIR/atascadas_cobro_solicitado.txt")
TOTAL_PERDIDAS=$(contar "$TMP_DIR/perdidas.txt")

valor_estado() {
  # valor_estado <archivo> <estado> -> el count de esa fila "estado|count", o 0
  awk -F'|' -v est="$2" '$1 == est { print $2 }' "$1"
}

PAGO_APLICADO=$(valor_estado "$TMP_DIR/por_estado.txt" "PAGO_APLICADO")
PAGO_PENDIENTE=$(valor_estado "$TMP_DIR/por_estado.txt" "PAGO_PENDIENTE")
: "${PAGO_APLICADO:=0}"
: "${PAGO_PENDIENTE:=0}"

INTENTOS_INICIADO=$(valor_estado "$TMP_DIR/intentos_por_resultado.txt" "INICIADO"); : "${INTENTOS_INICIADO:=0}"
INTENTOS_EXITOSO=$(valor_estado "$TMP_DIR/intentos_por_resultado.txt" "EXITOSO"); : "${INTENTOS_EXITOSO:=0}"
INTENTOS_FALLIDO=$(valor_estado "$TMP_DIR/intentos_por_resultado.txt" "FALLIDO"); : "${INTENTOS_FALLIDO:=0}"
INTENTOS_RECHAZADO=$(valor_estado "$TMP_DIR/intentos_por_resultado.txt" "RECHAZADO"); : "${INTENTOS_RECHAZADO:=0}"

echo "=== EAC-01 / HU-023 — verificacion (prefijo cliente_id: ${PREFIJO}) ==="
echo "ordenes_creadas=${TOTAL_CREADAS}"
echo "ordenes_estado_consultable=${TOTAL_CONSULTABLES}"
echo "ordenes_pago_aplicado=${PAGO_APLICADO}"
echo "ordenes_pago_pendiente=${PAGO_PENDIENTE}"
echo "ordenes_cobro_solicitado_sin_intento_pago=${TOTAL_ATASCADAS_COBRO}"
echo "---"
echo "distribucion_completa_por_estado_orden:"
sed 's/^/  /; s/|/ = /' "$TMP_DIR/por_estado.txt"
echo "---"
echo "intentos_pago_por_resultado:"
echo "  INICIADO = ${INTENTOS_INICIADO}"
echo "  EXITOSO = ${INTENTOS_EXITOSO}"
echo "  FALLIDO = ${INTENTOS_FALLIDO}"
echo "  RECHAZADO = ${INTENTOS_RECHAZADO}"
echo "---"
echo "ordenes_sin_ningun_intento_de_pago=${TOTAL_SIN_INTENTO}"
echo "ordenes_en_estado_no_consultable=${TOTAL_NO_CONSULTABLE}"
echo "ordenes_perdidas_total=${TOTAL_PERDIDAS}"
echo "==="

if [ "$TOTAL_PERDIDAS" -eq 0 ]; then
  echo "RESULTADO=APROBADO (0 ordenes perdidas de ${TOTAL_CREADAS} creadas)"
  exit 0
else
  echo "RESULTADO=RECHAZADO (${TOTAL_PERDIDAS} ordenes perdidas de ${TOTAL_CREADAS} creadas)" >&2
  echo "ids_perdidos (primeras 20):" >&2
  head -n 20 "$TMP_DIR/perdidas.txt" >&2
  exit 1
fi
