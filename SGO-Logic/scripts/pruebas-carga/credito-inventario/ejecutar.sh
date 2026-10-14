#!/usr/bin/env bash
# Orquestador de las pruebas de carga (c1..c5) y estres (e1..e5) de la ruta de
# credito y su impacto en Inventario. Por cada prueba:
#   1. genera el RUN_ID y lee el JWT_SECRET del stack (de ordenes-api);
#   2. corre el script k6 en contenedor, en la red del compose (no instala nada);
#   3. mientras corre, muestrea cada 10 s CPU/memoria de los contenedores clave y
#      la profundidad de las colas de credito e inventario;
#   4. en E3 detiene y vuelve a levantar credito-api en mitad de la carga;
#   5. espera el drenado de la saga y corre verificar.sh;
#   6. deja la evidencia en scripts/pruebas/evidencia/cci-<RUN_ID>-*.txt.
#
# Uso (desde SGO-Logic/, con el stack arriba: docker compose up -d):
#   bash scripts/pruebas-carga/credito-inventario/ejecutar.sh c1
#   bash scripts/pruebas-carga/credito-inventario/ejecutar.sh carga     # c1..c5
#   bash scripts/pruebas-carga/credito-inventario/ejecutar.sh estres    # e1..e5
#   bash scripts/pruebas-carga/credito-inventario/ejecutar.sh todas
#
# Variables utiles: K6_IMAGE (grafana/k6), RED (sgo-logic_default),
# PAUSA_ENTRE_PRUEBAS_SEG (60). Cualquier parametro de un script k6
# (ORDENES_POR_MINUTO, DURACION_SEG, STOCK, SOLO_TARJETA, ...) se pasa tal cual
# si esta definido en el entorno:
#   ORDENES_POR_MINUTO=120 bash scripts/pruebas-carga/credito-inventario/ejecutar.sh c2

set -euo pipefail

DIR_PRUEBAS="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
RAIZ="$(cd "$DIR_PRUEBAS/../../.." && pwd)"
COMPOSE_FILE="$RAIZ/docker-compose.yml"
EVIDENCIA="$RAIZ/scripts/pruebas/evidencia"
K6_IMAGE="${K6_IMAGE:-grafana/k6}"
RED="${RED:-sgo-logic_default}"
PAUSA_ENTRE_PRUEBAS_SEG="${PAUSA_ENTRE_PRUEBAS_SEG:-60}"

compose() { docker compose -f "$COMPOSE_FILE" "$@"; }

declare -A SCRIPTS=(
  [c1]=c1-linea-base-credito.js
  [c2]=c2-pico-mixto.js
  [c3]=c3-pico-rechazos.js
  [c4]=c4-checkout-lectura.js
  [c5]=c5-agotamiento-cupo.js
  [e1]=e1-rampa-quiebre.js
  [e2]=e2-pico-subito.js
  [e3]=e3-credito-caido.js
  [e4]=e4-stock-escaso.js
  [e5]=e5-rafaga-mismo-cliente.js
)

# Parametros de los scripts que se reenvian a k6 si estan definidos.
PARAMETROS_K6=(ORDENES_POR_MINUTO DURACION_SEG CLIENTES LECTORES UNIDADES_POR_ORDEN ESCALONES SEG_POR_ESCALON
  PROPORCION_CREDITO MAX_VUS TASA_BASE TASA_PICO ANTES_SEG PICO_SEG DESPUES_SEG CAIDA_INICIO_SEG CAIDA_SEG
  STOCK ORDENES_POR_RUTA VENTANA_SEG SOLO_TARJETA COMPRAS_POR_CLIENTE)

# En Git Bash (Windows) docker necesita la ruta en formato Windows y sin la
# conversion automatica de rutas de MSYS.
ruta_para_docker() {
  if pwd -W >/dev/null 2>&1; then (cd "$1" && pwd -W); else printf '%s' "$1"; fi
}

verificar_stack() {
  local faltan=()
  for s in ordenes-api inventario-api credito-api pagos-api payklik-mock db-operacional db-finanzas db-auditoria rabbitmq; do
    if [ -z "$(compose ps -q --status running "$s" 2>/dev/null)" ]; then faltan+=("$s"); fi
  done
  if [ "${#faltan[@]}" -gt 0 ]; then
    echo "Servicios sin correr: ${faltan[*]}. Levanta el stack con: docker compose up -d" >&2
    exit 2
  fi
  if ! docker network inspect "$RED" >/dev/null 2>&1; then
    echo "No existe la red $RED (docker network ls). Ajusta RED=..." >&2
    exit 2
  fi
}

muestrear_recursos() {
  # muestrear_recursos <archivo>: corre hasta que lo maten.
  local archivo="$1" ids
  ids="$(compose ps -q ordenes-api inventario-api credito-api pagos-api db-operacional db-finanzas rabbitmq | tr '\n' ' ')"
  echo "epoch|contenedor|cpu_pct|memoria|colas_credito_inventario_ordenes" > "$archivo"
  while :; do
    local ahora colas
    ahora=$(date +%s)
    colas=$(compose exec -T rabbitmq rabbitmqctl list_queues -q name messages 2>/dev/null | tr -d '\r' \
      | awk '$1 ~ /^(credito|inventario|ordenes)\./ { s += $2 } END { print s + 0 }' || echo "?")
    # shellcheck disable=SC2086
    docker stats --no-stream --format '{{.Name}}|{{.CPUPerc}}|{{.MemUsage}}' $ids 2>/dev/null \
      | tr -d '\r' | sed "s/^/${ahora}|/; s/$/|${colas}/" >> "$archivo" || true
    sleep 10
  done
}

resumir_recursos() {
  awk -F'|' 'NR > 1 {
      gsub(/%/, "", $3); split($4, m, " / "); mem = m[1];
      v = mem + 0; if (mem ~ /GiB/) v *= 1024; else if (mem ~ /KiB/) v /= 1024;
      if ($3 + 0 > cpu[$2]) cpu[$2] = $3 + 0;
      if (v > ram[$2]) ram[$2] = v;
      if ($5 + 0 > colas) colas = $5 + 0;
    }
    END {
      printf "%-45s %10s %12s\n", "contenedor", "cpu_max_%", "mem_max_MiB";
      for (c in cpu) printf "%-45s %10.1f %12.1f\n", c, cpu[c], ram[c];
      printf "max_mensajes_en_colas_credito_inventario_ordenes=%d\n", colas;
    }' "$1"
}

caida_de_credito() {
  # Solo E3: la linea de tiempo del script (CAIDA_INICIO_SEG, CAIDA_SEG). El
  # setup de k6 (historiales) tarda unos segundos: se espera a que arranque la
  # carga leyendo la salida de k6.
  local salida_k6="$1" inicio="${CAIDA_INICIO_SEG:-90}" duracion="${CAIDA_SEG:-90}"
  for _ in $(seq 1 120); do
    grep -q 'running (' "$salida_k6" 2>/dev/null && break
    grep -q 'clientes b: elegibilidad' "$salida_k6" 2>/dev/null && break
    sleep 1
  done
  sleep "$inicio"
  echo "[e3] $(date +%T) deteniendo credito-api (caida de ${duracion}s)" | tee -a "$salida_k6"
  compose stop credito-api >/dev/null 2>&1 || true
  sleep "$duracion"
  echo "[e3] $(date +%T) levantando credito-api" | tee -a "$salida_k6"
  compose start credito-api >/dev/null 2>&1 || true
}

correr_prueba() {
  local prueba="$1" script="${SCRIPTS[$1]:-}"
  [ -n "$script" ] || { echo "Prueba desconocida: $prueba" >&2; return 2; }

  local run_id base
  run_id="${prueba}-$(date +%m%d%H%M%S)"
  base="$EVIDENCIA/cci-${run_id}"
  mkdir -p "$EVIDENCIA"

  local jwt
  jwt="$(compose exec -T ordenes-api printenv Auth__JwtSecret | tr -d '\r')"
  [ -n "$jwt" ] || { echo "No se pudo leer Auth__JwtSecret de ordenes-api" >&2; return 2; }

  local args_env=(-e "RUN_ID=$run_id" -e "JWT_SECRET=$jwt")
  for p in "${PARAMETROS_K6[@]}"; do
    if [ -n "${!p:-}" ]; then args_env+=(-e "$p=${!p}"); fi
  done

  echo "=================================================================="
  echo " Prueba ${prueba} (${script}) — RUN_ID=${run_id}"
  echo " Evidencia: ${base}-*.txt"
  echo "=================================================================="

  muestrear_recursos "${base}-recursos.txt" &
  local pid_muestreo=$!
  local pid_caida=""
  : > "${base}-k6.txt"
  if [ "$prueba" = "e3" ]; then
    caida_de_credito "${base}-k6.txt" &
    pid_caida=$!
  fi

  local codigo_k6=0
  MSYS_NO_PATHCONV=1 docker run --rm -i --network "$RED" \
    -v "$(ruta_para_docker "$DIR_PRUEBAS"):/scripts" \
    -e K6_NO_USAGE_REPORT=true "${args_env[@]}" \
    "$K6_IMAGE" run --quiet "/scripts/${script}" 2>&1 | tee -a "${base}-k6.txt" || codigo_k6=$?

  if [ -n "$pid_caida" ]; then
    wait "$pid_caida" 2>/dev/null || true
  fi
  if [ "$prueba" = "e3" ]; then
    # Pase lo que pase con k6, Credito vuelve a quedar arriba.
    compose start credito-api >/dev/null 2>&1 || true
    echo "[e3] esperando a que credito-api este sano para procesar la cola atrasada..."
    for _ in $(seq 1 60); do
      [ "$(docker inspect -f '{{.State.Health.Status}}' "$(compose ps -q credito-api)" 2>/dev/null)" = "healthy" ] && break
      sleep 5
    done
  fi

  echo "--- k6 termino (codigo ${codigo_k6}); esperando el drenado y verificando ---"
  local codigo_verificacion=0
  RUN_ID="$run_id" ESPERAR=1 bash "$DIR_PRUEBAS/verificar.sh" 2>&1 | tee "${base}-verificacion.txt" \
    || codigo_verificacion=$?
  kill "$pid_muestreo" 2>/dev/null || true
  wait "$pid_muestreo" 2>/dev/null || true

  {
    echo "--- recursos (maximos durante la corrida, muestreo cada 10 s) ---"
    resumir_recursos "${base}-recursos.txt"
  } | tee -a "${base}-verificacion.txt"

  local veredicto="APROBADO"
  if [ "$codigo_verificacion" -ne 0 ]; then veredicto="RECHAZADO"; fi
  local umbrales_k6="cumplidos"
  if [ "$codigo_k6" -ne 0 ]; then umbrales_k6="NO cumplidos (codigo ${codigo_k6})"; fi
  echo "${prueba}|${run_id}|verificacion=${veredicto}|umbrales_k6=${umbrales_k6}" | tee -a "${EVIDENCIA}/cci-resumen.txt"
  [ "$codigo_verificacion" -eq 0 ]
}

main() {
  [ $# -ge 1 ] || { sed -n '2,25p' "${BASH_SOURCE[0]}"; exit 2; }
  verificar_stack

  local lista=()
  case "$1" in
    carga) lista=(c1 c2 c3 c4 c5) ;;
    estres) lista=(e1 e2 e3 e4 e5) ;;
    todas) lista=(c1 c2 c3 c4 c5 e1 e2 e3 e4 e5) ;;
    *) lista=("$@") ;;
  esac

  trap 'compose start credito-api >/dev/null 2>&1 || true' EXIT

  local fallidas=0 primera=1
  for prueba in "${lista[@]}"; do
    if [ "$primera" -eq 0 ]; then
      echo "Pausa de ${PAUSA_ENTRE_PRUEBAS_SEG}s entre pruebas..."
      sleep "$PAUSA_ENTRE_PRUEBAS_SEG"
    fi
    primera=0
    correr_prueba "$prueba" || fallidas=$((fallidas + 1))
  done
  echo "Pruebas con verificacion RECHAZADA: ${fallidas} de ${#lista[@]} (detalle en ${EVIDENCIA}/cci-resumen.txt)"
  [ "$fallidas" -eq 0 ]
}

main "$@"
