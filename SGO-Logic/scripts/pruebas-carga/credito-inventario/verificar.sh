#!/usr/bin/env bash
# Verificador de las pruebas de carga y estres de credito + inventario
# (c1..c5, e1..e5). Corre contra las bases reales del stack, igual que
# eac01-hu023-verificar.sh: no instala nada, usa el psql de los contenedores.
#
# Que hace:
#   1. (opcional, ESPERAR=1) espera el drenado: ninguna orden de la corrida en un
#      estado intermedio de la saga y las colas de credito/inventario vacias.
#   2. Extrae lo de la corrida de cuatro bases en dos servidores distintos:
#        sgo_inventario (reservas y existencia del SKU de la corrida)
#        sgo_auditoria  (ReservaInventarioLiberada / Expirada: cuando volvio el stock)
#        sgo_credito    (decisiones, cuentas por cobrar, lineas)
#   3. Las carga como tablas temporales en una sesion de sgo_ordenes y cruza todo
#      con ordenes.orden y ordenes.transicion_orden en SQL (una sola sesion, sin
#      dependencias de Python ni jq en la maquina).
#   4. Imprime las medidas y aplica los umbrales comunes y los de la prueba.
#
# Uso (desde SGO-Logic/):
#   RUN_ID=c3-1011170512 bash scripts/pruebas-carga/credito-inventario/verificar.sh
#   RUN_ID=c3-1011170512 ESPERAR=1 bash scripts/pruebas-carga/credito-inventario/verificar.sh
#
# Salida: lineas clave=valor, la tabla por minuto y al final
#   RESULTADO=APROBADO | RESULTADO=RECHAZADO (codigo de salida 0 / 1).

set -euo pipefail

RAIZ="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
COMPOSE_FILE="$RAIZ/docker-compose.yml"
[ -f "$COMPOSE_FILE" ] || { echo "No se encontro docker-compose.yml en $RAIZ" >&2; exit 2; }

RUN_ID="$(printf '%s' "${RUN_ID:-}" | tr 'A-Z' 'a-z')"
[ -n "$RUN_ID" ] || { echo "Falta RUN_ID (el que imprimio ejecutar.sh, p. ej. c3-1011170512)" >&2; exit 2; }
PRUEBA="${PRUEBA:-${RUN_ID%%-*}}"
PREFIJO="cci-${RUN_ID}"
SKU="SKU-CCI-$(printf '%s' "$RUN_ID" | tr 'a-z' 'A-Z')"
UBICACION="CD-01"

# Umbrales (segundos). Se pueden ajustar por variable de entorno; los valores por
# defecto se justifican en documentacion/docs/PRUEBAS-CARGA-ESTRES-CREDITO.md.
UMBRAL_DECISION_P95="${UMBRAL_DECISION_P95:-5}"     # decision de credito "rapida"
UMBRAL_RETENCION_MAX="${UMBRAL_RETENCION_MAX:-45}"  # timeout 30 s + ciclo 5 s + margen
UMBRAL_LIBERACION_P95="${UMBRAL_LIBERACION_P95:-5}" # rechazo -> stock de vuelta
VENTANA_RECUPERACION_SEG="${VENTANA_RECUPERACION_SEG:-120}"
DRENADO_MAX_SEG="${DRENADO_MAX_SEG:-600}"

TMP_DIR="$(mktemp -d)"
trap 'rm -rf "$TMP_DIR"' EXIT

compose() { docker compose -f "$COMPOSE_FILE" "$@"; }

leer_credencial() {
  compose exec -T "$1" sh -c "printf '%s' \"\$$2\""
}

USUARIO_OPERACIONAL="$(leer_credencial db-operacional POSTGRES_USER)"
CLAVE_OPERACIONAL="$(leer_credencial db-operacional POSTGRES_PASSWORD)"
USUARIO_FINANZAS="$(leer_credencial db-finanzas POSTGRES_USER)"
CLAVE_FINANZAS="$(leer_credencial db-finanzas POSTGRES_PASSWORD)"
USUARIO_AUDITORIA="$(leer_credencial db-auditoria POSTGRES_USER)"
CLAVE_AUDITORIA="$(leer_credencial db-auditoria POSTGRES_PASSWORD)"

psql_en() {
  # psql_en <servicio> <usuario> <clave> <base> [args...]
  local servicio="$1" usuario="$2" clave="$3" base="$4"; shift 4
  compose exec -T -e PGPASSWORD="$clave" "$servicio" \
    psql -U "$usuario" -d "$base" -v ON_ERROR_STOP=1 -q -tA -F '|' "$@" | tr -d '\r'
}
psql_ordenes()    { psql_en db-operacional "$USUARIO_OPERACIONAL" "$CLAVE_OPERACIONAL" sgo_ordenes "$@"; }
psql_inventario() { psql_en db-operacional "$USUARIO_OPERACIONAL" "$CLAVE_OPERACIONAL" sgo_inventario "$@"; }
psql_credito()    { psql_en db-finanzas "$USUARIO_FINANZAS" "$CLAVE_FINANZAS" sgo_credito "$@"; }
psql_auditoria()  { psql_en db-auditoria "$USUARIO_AUDITORIA" "$CLAVE_AUDITORIA" sgo_auditoria "$@"; }

ESTADOS_TRANSITO="'RESERVA_SOLICITADA','INVENTARIO_RESERVADO','EVALUACION_CREDITO_SOLICITADA','CREDITO_APROBADO','COBRO_SOLICITADO'"

# ---------------------------------------------------------------------------
# 1) Drenado
# ---------------------------------------------------------------------------
mensajes_en_colas() {
  compose exec -T rabbitmq rabbitmqctl list_queues -q name messages 2>/dev/null | tr -d '\r' \
    | awk '$1 ~ /^(credito|inventario|ordenes)\./ { s += $2 } END { print s + 0 }'
}

if [ "${ESPERAR:-0}" = "1" ]; then
  echo "Esperando el drenado de ${PREFIJO} (maximo ${DRENADO_MAX_SEG}s)..."
  inicio=$(date +%s)
  while :; do
    en_transito=$(psql_ordenes -c "SELECT count(*) FROM ordenes.orden WHERE cliente_id LIKE '${PREFIJO}-%' AND estado IN (${ESTADOS_TRANSITO});")
    colas=$(mensajes_en_colas || echo "?")
    transcurrido=$(( $(date +%s) - inicio ))
    echo "  t=${transcurrido}s ordenes_en_transito=${en_transito} mensajes_en_colas=${colas}"
    if [ "$en_transito" = "0" ] && [ "$colas" = "0" ]; then
      # Los outbox publican cada OUTBOX_POLLING_MS: un margen corto para que
      # Auditoria registre los ultimos eventos de liberacion.
      sleep 10
      break
    fi
    if [ "$transcurrido" -ge "$DRENADO_MAX_SEG" ]; then
      echo "  El drenado no termino en ${DRENADO_MAX_SEG}s; se verifica igual (lo pendiente cuenta como atascado)."
      break
    fi
    sleep 10
  done
fi

# ---------------------------------------------------------------------------
# 2) Extraccion de las otras bases
# ---------------------------------------------------------------------------
# Ventana temporal de la corrida (para acotar Auditoria, que no tiene el cliente).
DESDE_EPOCH=$(psql_ordenes -c "SELECT coalesce(floor(extract(epoch FROM min(creada_en))) - 120, 0) FROM ordenes.orden WHERE cliente_id LIKE '${PREFIJO}-%';")

psql_inventario -c "
  SELECT r.orden_id, r.estado::text, extract(epoch FROM r.creada_en), sum(l.cantidad)
  FROM inventario.reserva r JOIN inventario.linea_reserva l ON l.reserva_id = r.id
  WHERE l.sku = '${SKU}'
  GROUP BY r.orden_id, r.estado, r.creada_en;" > "$TMP_DIR/inv_reserva.csv"

psql_inventario -c "
  SELECT cantidad_fisica, cantidad_reservada FROM inventario.existencia
  WHERE sku = '${SKU}' AND ubicacion_id = '${UBICACION}';" > "$TMP_DIR/inv_existencia.csv"

psql_auditoria -c "
  SELECT payload->>'ordenId', extract(epoch FROM occurred_at), event_type, coalesce(payload->>'motivo', '')
  FROM eventos_auditoria
  WHERE event_type IN ('ReservaInventarioLiberada', 'ReservaInventarioExpirada')
    AND occurred_at >= to_timestamp(${DESDE_EPOCH});" > "$TMP_DIR/aud_liberacion.csv"

psql_credito -c "
  SELECT orden_id, cliente_id, resultado::text, coalesce(codigo_motivo, ''), monto, extract(epoch FROM decidido_en)
  FROM credito.decision_credito WHERE cliente_id LIKE '${PREFIJO}-%';" > "$TMP_DIR/cr_decision.csv"

psql_credito -c "
  SELECT orden_id, cliente_id, estado::text, monto_original, saldo_pendiente
  FROM credito.cuenta_por_cobrar WHERE cliente_id LIKE '${PREFIJO}-%';" > "$TMP_DIR/cr_cuenta.csv"

psql_credito -c "
  SELECT cliente_id, limite_aprobado, saldo_utilizado, estado::text
  FROM credito.linea_credito WHERE cliente_id LIKE '${PREFIJO}-%';" > "$TMP_DIR/cr_linea.csv"

# ---------------------------------------------------------------------------
# 3) Cruce en una sola sesion de sgo_ordenes
# ---------------------------------------------------------------------------
cargar() {
  # cargar <tabla> <definicion de columnas> <archivo>
  echo "CREATE TEMP TABLE $1 ($2);"
  echo "COPY $1 FROM STDIN WITH (FORMAT csv, DELIMITER '|');"
  sed '/^$/d' "$3"
  echo '\.'
}

{
  cargar inv_reserva "orden_id text, estado text, creada double precision, unidades int" "$TMP_DIR/inv_reserva.csv"
  cargar inv_existencia "fisica int, reservada int" "$TMP_DIR/inv_existencia.csv"
  cargar aud_liberacion "orden_id text, momento double precision, tipo text, motivo text" "$TMP_DIR/aud_liberacion.csv"
  cargar cr_decision "orden_id text, cliente_id text, resultado text, codigo_motivo text, monto numeric, decidido double precision" "$TMP_DIR/cr_decision.csv"
  cargar cr_cuenta "orden_id text, cliente_id text, estado text, monto_original numeric, saldo_pendiente numeric" "$TMP_DIR/cr_cuenta.csv"
  cargar cr_linea "cliente_id text, limite numeric, saldo numeric, estado text" "$TMP_DIR/cr_linea.csv"
  sed -e "s/__PREFIJO__/${PREFIJO}/g" -e "s/__VENTANA__/${VENTANA_RECUPERACION_SEG}/g" "$(dirname "${BASH_SOURCE[0]}")/lib/analisis.sql"
} > "$TMP_DIR/analisis_completo.sql"

psql_ordenes < "$TMP_DIR/analisis_completo.sql" > "$TMP_DIR/salida.txt"

# ---------------------------------------------------------------------------
# 4) Reporte y umbrales
# ---------------------------------------------------------------------------
valor() { awk -F'=' -v k="$1" '$1 == k { print $2; exit }' "$TMP_DIR/salida.txt"; }

echo "=== Credito + Inventario — prueba ${PRUEBA} — RUN_ID=${RUN_ID} (prefijo ${PREFIJO}-, SKU ${SKU}) ==="
grep -v '^minuto|' "$TMP_DIR/salida.txt" | grep -v '^$'
echo "--- por minuto desde la primera orden ---"
echo "minuto | ordenes | credito | decision_credito_p95_s | decision_credito_max_s | timeouts | rechazos_stock | unidades_esperando_credito_al_cierre"
grep '^minuto|' "$TMP_DIR/salida.txt" | cut -d'|' -f2- | sed 's/|/ | /g'

FALLAS=()
exigir_cero() { # exigir_cero <clave> <descripcion>
  local v; v="$(valor "$1")"; v="${v:-NA}"
  if [ "$v" != "0" ]; then FALLAS+=("$2 ($1=$v, se esperaba 0)"); fi
}
exigir_max() { # exigir_max <clave> <maximo> <descripcion>
  local v; v="$(valor "$1")"
  if [ -z "$v" ]; then return 0; fi   # sin datos (p. ej. sin ordenes de esa ruta)
  if awk -v a="$v" -v b="$2" 'BEGIN { exit !(a > b) }'; then FALLAS+=("$3 ($1=$v > $2)"); fi
}
exigir_min() { # exigir_min <clave> <minimo> <descripcion>
  local v; v="$(valor "$1")"; v="${v:-0}"
  if awk -v a="$v" -v b="$2" 'BEGIN { exit !(a < b) }'; then FALLAS+=("$3 ($1=$v < $2)"); fi
}

# --- Umbrales comunes: integridad del inventario y de la linea de credito ---
exigir_min ordenes_total 1 "La corrida no creo ordenes"
exigir_cero ordenes_en_transito "Ordenes atascadas en un paso intermedio de la saga"
exigir_cero sobreventa "Sobreventa: reservado mayor que fisico o negativo"
exigir_cero inventario_dif_existencia_vs_reservas "existencia.cantidad_reservada no cuadra con las reservas ACTIVA/CONFIRMADA"
exigir_cero inventario_dif_existencia_vs_ordenes "El stock retenido no cuadra con las ordenes que deberian retenerlo"
exigir_cero fugas_stock "Fugas: ordenes rechazadas/canceladas con reserva todavia activa"
exigir_cero huecos_stock "Ordenes vivas sin reserva que las respalde"
exigir_cero lineas_sobre_limite "Lineas de credito con saldo utilizado mayor al limite"
exigir_cero lineas_incoherentes "Saldo utilizado distinto de la suma de cuentas VIGENTE/VENCIDA"
exigir_cero cargos_huerfanos "Cuentas por cobrar vigentes de ordenes que terminaron rechazadas/canceladas"

# --- Umbrales por prueba ---
case "$PRUEBA" in
  c1|c2|c3|c4|c5)
    exigir_max decision_credito_p95_s "$UMBRAL_DECISION_P95" "Decision de credito p95 por encima del umbral de carga"
    exigir_max retencion_credito_max_s "$UMBRAL_RETENCION_MAX" "Stock retenido por credito mas que el timeout de la saga"
    ;;
esac
case "$PRUEBA" in
  c1) exigir_cero rechazadas_grupo_b "Clientes elegibles rechazados en la linea base"
      exigir_cero timeouts_credito "Timeouts de credito en operacion normal" ;;
  c2) exigir_cero rechazadas_grupo_b "Ruta credito rechazada con clientes elegibles en el pico"
      exigir_cero rechazadas_grupo_t "Ruta tarjeta rechazada en el pico mixto"
      exigir_cero timeouts_credito "Timeouts de credito en el pico mixto" ;;
  c3|c4)
      exigir_cero rechazadas_grupo_b "Clientes elegibles rechazados"
      exigir_cero no_rechazadas_grupo_x "Clientes no elegibles que no terminaron RECHAZADA"
      exigir_max liberacion_tras_rechazo_p95_s "$UMBRAL_LIBERACION_P95" "Liberacion del stock tras el rechazo p95 por encima del umbral"
      exigir_cero rechazos_sin_evento_liberacion "Rechazos de credito con reserva sin evento ReservaInventarioLiberada" ;;
  c5) exigir_min rechazos_credito 1 "La prueba no llego a agotar el cupo de ningun cliente" ;;
  e2) exigir_max recuperacion_decision_credito_p95_s "$UMBRAL_DECISION_P95" "No se recupero la decision de credito despues del pico" ;;
  e3) exigir_cero rechazadas_grupo_t "La caida de Credito afecto a la ruta de tarjeta (EAC-06)"
      exigir_min timeouts_credito 1 "No hubo timeouts: la caida de Credito no se ejercito"
      exigir_max retencion_credito_max_s "$UMBRAL_RETENCION_MAX" "Stock retenido por credito mas que el timeout de la saga"
      exigir_cero rechazos_sin_evento_liberacion "Timeouts con reserva sin evento ReservaInventarioLiberada" ;;
  e5) exigir_min rechazos_credito 1 "La rafaga no agoto el cupo (no se ejercito la contencion)" ;;
esac

echo "==="
if [ "${#FALLAS[@]}" -eq 0 ]; then
  echo "RESULTADO=APROBADO (${PRUEBA}, $(valor ordenes_total) ordenes)"
  exit 0
fi
echo "RESULTADO=RECHAZADO (${PRUEBA}): ${#FALLAS[@]} criterio(s) no cumplido(s)" >&2
for f in "${FALLAS[@]}"; do echo "  - $f" >&2; done
exit 1
