#!/usr/bin/env bash
# =============================================================================
# SGO-Logic — HU-025 / EAC-04: Verificacion SQL de escalabilidad horizontal
# Confirma, con consultas directas contra las bases reales, que escalar
# ordenes-api y auditoria-worker a varias replicas NO produjo:
#
#   1) filas de outbox publicadas dos veces
#   2) mensajes procesados dos veces por los consumidores DENTRO de
#      ordenes-api (PagosEventosConsumidor sobre ordenes.mensaje_procesado;
#      InventarioEventosConsumidor y DocumentoFiscalCertificadoConsumidor
#      sobre ordenes.mensajes_procesados) — cada uno corre una instancia por
#      replica, todas compitiendo por la misma cola
#   3) eventos de auditoria (eventos_auditoria) registrados dos veces por
#      auditoria-worker, cuyas N replicas compiten por la misma cola
#      "auditoria-worker.eventos" (binding "#": recibe todo el trafico)
#   4) eventos publicados por ordenes-api que nunca llegaron a auditoria (es
#      decir: que SKIP LOCKED con varios publicadores compitiendo no perdio
#      ninguna fila)
#
# Esquemas y bases reales (verificado contra los snapshots de EF Core de cada
# contexto, no de memoria):
#   - sgo_ordenes (host db-operacional):
#       public.outbox_messages         <- OJO: NO esta en el esquema "ordenes"
#                                          pese a que OrdenesDbContext declara
#                                          HasDefaultSchema("ordenes"): la
#                                          configuracion de BuildingBlocks
#                                          (OutboxMessageConfiguration) fija
#                                          la tabla con ToTable(nombre) sin
#                                          pasar el esquema, y eso la deja en
#                                          "public" en el modelo compilado
#                                          (confirmado en
#                                          OrdenesDbContextModelSnapshot.cs:
#                                          b.ToTable("outbox_messages", "public")).
#                                          Esto es una inconsistencia del
#                                          esquema entre BuildingBlocks y el
#                                          resto de tablas de Ordenes, no algo
#                                          que este script deba corregir.
#       ordenes.mensajes_procesados    <- InventarioEventosConsumidor y
#                                          DocumentoFiscalCertificadoConsumidor
#                                          (EventConsumerBase / BuildingBlocks)
#       ordenes.mensaje_procesado      <- PagosEventosConsumidor (tabla propia
#                                          de Ordenes.Api, singular, columna
#                                          message_id en vez de event_id)
#   - sgo_auditoria (host db-auditoria):
#       public.eventos_auditoria       <- AuditoriaConsumidor / RegistradorEventoAuditoria
#       public.mensajes_procesados     <- AuditoriaConsumidor (EventConsumerBase)
#
# Uso:
#   DESDE="2026-09-17T10:00:00Z" ./verificar.sh
#
# DESDE es la marca de tiempo UTC (ISO 8601) anotada justo antes de correr
# carga.js. Sin DESDE se revisa toda la tabla desde siempre, lo cual mezcla
# datos de pruebas o demos anteriores si los hay: se recomienda pasarlo
# siempre que la base no este recien creada.
#
# Cada consulta corre de forma independiente: si una falla (por ejemplo, un
# nombre de tabla o esquema incorrecto, o un contenedor caido), el script lo
# reporta por esa consulta puntual y sigue con las demas — un solo chequeo
# roto no invalida a los demas. El dictamen final distingue tres resultados:
# APROBADO (todo corrio y no hubo inconsistencias), RECHAZADO (todo corrio
# pero se encontraron duplicados o eventos perdidos) e INCOMPLETO (una o mas
# consultas no se pudieron evaluar).
#
# Requiere el stack de docker compose arriba (ver escalar.sh) y que carga.js
# ya haya terminado, dejando al menos 60s para que el outbox drene antes de
# correr esto.
# =============================================================================

set -uo pipefail
# Sin "set -e" a proposito: cada consulta se ejecuta y se evalua por su
# cuenta (funcion "ejecutar" abajo); un fallo puntual no debe abortar el
# resto de las comprobaciones.

RAIZ="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
COMPOSE_FILE="${RAIZ}/docker-compose.yml"
DESDE="${DESDE:-1970-01-01T00:00:00Z}"

if [ ! -f "$COMPOSE_FILE" ]; then
  echo "No se encontro docker-compose.yml en $RAIZ" >&2
  exit 1
fi

TMP_DIR="$(mktemp -d)"
trap 'rm -rf "$TMP_DIR"' EXIT

compose() {
  docker compose -f "$COMPOSE_FILE" "$@"
}

# Credenciales reales de cada contenedor, leidas de su propio entorno (evita
# parsear el .env del repo dos veces y andar anidando comillas de shell).
# Esto es un requisito duro: sin credenciales no se puede evaluar ninguna
# consulta, asi que si esto falla el script se detiene aqui con un mensaje
# claro (a diferencia de las consultas de negocio de mas abajo, que no
# detienen nada).
leer_credencial() {
  local servicio="$1" variable="$2"
  compose exec -T "$servicio" sh -c "printf '%s' \"\$$variable\""
}

USUARIO_OPERACIONAL="$(leer_credencial db-operacional POSTGRES_USER)" || { echo "No se pudo leer credenciales de db-operacional. ¿Esta el stack arriba?" >&2; exit 1; }
CLAVE_OPERACIONAL="$(leer_credencial db-operacional POSTGRES_PASSWORD)" || { echo "No se pudo leer credenciales de db-operacional. ¿Esta el stack arriba?" >&2; exit 1; }
USUARIO_AUDITORIA="$(leer_credencial db-auditoria POSTGRES_USER)" || { echo "No se pudo leer credenciales de db-auditoria. ¿Esta el stack arriba?" >&2; exit 1; }
CLAVE_AUDITORIA="$(leer_credencial db-auditoria POSTGRES_PASSWORD)" || { echo "No se pudo leer credenciales de db-auditoria. ¿Esta el stack arriba?" >&2; exit 1; }

psql_operacional() {
  local base="$1"; shift
  compose exec -T -e PGPASSWORD="$CLAVE_OPERACIONAL" db-operacional \
    psql -U "$USUARIO_OPERACIONAL" -d "$base" -v ON_ERROR_STOP=1 -tA "$@"
}

psql_auditoria() {
  local base="$1"; shift
  compose exec -T -e PGPASSWORD="$CLAVE_AUDITORIA" db-auditoria \
    psql -U "$USUARIO_AUDITORIA" -d "$base" -v ON_ERROR_STOP=1 -tA "$@"
}

# ---------------------------------------------------------------------------
# Cada consulta vive en su propia funcion; "ejecutar" la corre, redirige su
# salida al archivo indicado y, si falla, lo reporta y lo anota en
# FALLAS_EJECUCION sin detener el script. Un archivo cuya consulta fallo
# queda vacio (0 filas), pero se distingue de "0 filas porque se evaluo y
# dio cero" via el arreglo de fallas.
# ---------------------------------------------------------------------------
declare -a FALLAS_EJECUCION=()

ejecutar() {
  local funcion="$1" archivo="$2" etiqueta="$3"
  if "$funcion" > "$archivo" 2>"${archivo}.stderr"; then
    rm -f "${archivo}.stderr"
    return 0
  else
    echo "ADVERTENCIA: la consulta '${etiqueta}' fallo, no se pudo evaluar:" >&2
    sed 's/^/    /' "${archivo}.stderr" >&2
    rm -f "${archivo}.stderr"
    : > "$archivo"
    FALLAS_EJECUCION+=("${etiqueta}")
    return 1
  fi
}

consulta_outbox_duplicado() {
  psql_operacional sgo_ordenes -c \
    "SELECT event_id, count(*) FROM public.outbox_messages
     WHERE fecha_creacion >= '${DESDE}'::timestamptz
     GROUP BY event_id HAVING count(*) > 1;" \
    | tr -d '\r' | sed '/^$/d'
}

consulta_ordenes_procesado_duplicado() {
  psql_operacional sgo_ordenes -c \
    "SELECT event_id, nombre_servicio, count(*) FROM ordenes.mensajes_procesados
     WHERE fecha_proceso >= '${DESDE}'::timestamptz
     GROUP BY event_id, nombre_servicio HAVING count(*) > 1;" \
    | tr -d '\r' | sed '/^$/d'
}

consulta_ordenes_pagos_procesado_duplicado() {
  psql_operacional sgo_ordenes -c \
    "SELECT message_id, count(*) FROM ordenes.mensaje_procesado
     WHERE fecha >= '${DESDE}'::timestamptz
     GROUP BY message_id HAVING count(*) > 1;" \
    | tr -d '\r' | sed '/^$/d'
}

consulta_auditoria_duplicado() {
  psql_auditoria sgo_auditoria -c \
    "SELECT event_id, count(*) FROM public.eventos_auditoria
     WHERE fecha_recepcion >= '${DESDE}'::timestamptz
     GROUP BY event_id HAVING count(*) > 1;" \
    | tr -d '\r' | sed '/^$/d'
}

consulta_auditoria_procesado_duplicado() {
  psql_auditoria sgo_auditoria -c \
    "SELECT event_id, nombre_servicio, count(*) FROM public.mensajes_procesados
     WHERE fecha_proceso >= '${DESDE}'::timestamptz
     GROUP BY event_id, nombre_servicio HAVING count(*) > 1;" \
    | tr -d '\r' | sed '/^$/d'
}

consulta_ordenes_publicados() {
  psql_operacional sgo_ordenes -c \
    "SELECT event_id FROM public.outbox_messages
     WHERE estado = 'Publicado' AND fecha_creacion >= '${DESDE}'::timestamptz
     ORDER BY event_id;" \
    | tr -d '\r' | sed '/^$/d' | sort -u
}

consulta_auditoria_recibidos() {
  psql_auditoria sgo_auditoria -c \
    "SELECT event_id FROM public.eventos_auditoria
     WHERE fecha_recepcion >= '${DESDE}'::timestamptz
     ORDER BY event_id;" \
    | tr -d '\r' | sed '/^$/d' | sort -u
}

echo "====================================================================="
echo " SGO-Logic — Verificacion SQL de escalabilidad (HU-025 / EAC-04)      "
echo "====================================================================="
echo "Desde: ${DESDE}"
echo ""

OUTBOX_DUP_OK=1;        ejecutar consulta_outbox_duplicado               "$TMP_DIR/outbox_duplicado.txt"               "outbox_messages publicado dos veces (public, sgo_ordenes)"               || OUTBOX_DUP_OK=0
ORDENES_PROC_OK=1;      ejecutar consulta_ordenes_procesado_duplicado    "$TMP_DIR/ordenes_procesado_duplicado.txt"    "ordenes.mensajes_procesados duplicado (sgo_ordenes)"                     || ORDENES_PROC_OK=0
ORDENES_PAGOS_PROC_OK=1; ejecutar consulta_ordenes_pagos_procesado_duplicado "$TMP_DIR/ordenes_pagos_procesado_duplicado.txt" "ordenes.mensaje_procesado duplicado (sgo_ordenes)"                  || ORDENES_PAGOS_PROC_OK=0
AUDITORIA_DUP_OK=1;     ejecutar consulta_auditoria_duplicado            "$TMP_DIR/auditoria_duplicado.txt"             "eventos_auditoria duplicado (public, sgo_auditoria)"                     || AUDITORIA_DUP_OK=0
AUDITORIA_PROC_OK=1;    ejecutar consulta_auditoria_procesado_duplicado  "$TMP_DIR/auditoria_procesado_duplicado.txt"  "mensajes_procesados duplicado (public, sgo_auditoria)"                   || AUDITORIA_PROC_OK=0
ORDENES_PUB_OK=1;       ejecutar consulta_ordenes_publicados             "$TMP_DIR/ordenes_publicados.txt"             "outbox_messages publicados (public, sgo_ordenes)"                        || ORDENES_PUB_OK=0
AUDITORIA_RECIB_OK=1;   ejecutar consulta_auditoria_recibidos            "$TMP_DIR/auditoria_recibidos.txt"            "eventos_auditoria recibidos (public, sgo_auditoria)"                     || AUDITORIA_RECIB_OK=0

contar() { wc -l < "$1" | tr -d ' '; }

TOTAL_OUTBOX_DUP=$(contar "$TMP_DIR/outbox_duplicado.txt")
TOTAL_ORDENES_PROC_DUP=$(contar "$TMP_DIR/ordenes_procesado_duplicado.txt")
TOTAL_ORDENES_PAGOS_PROC_DUP=$(contar "$TMP_DIR/ordenes_pagos_procesado_duplicado.txt")
TOTAL_AUDITORIA_DUP=$(contar "$TMP_DIR/auditoria_duplicado.txt")
TOTAL_AUDITORIA_PROC_DUP=$(contar "$TMP_DIR/auditoria_procesado_duplicado.txt")
TOTAL_PUBLICADOS=$(contar "$TMP_DIR/ordenes_publicados.txt")
TOTAL_AUDITADOS=$(contar "$TMP_DIR/auditoria_recibidos.txt")

# La reconciliacion (evento publicado que nunca llego a auditoria) solo tiene
# sentido si las dos consultas de las que depende corrieron: si cualquiera de
# las dos fallo, un archivo vacio no significa "cero perdidos", significa
# "no se pudo saber". Se marca como no evaluada en vez de reportar un falso
# cero (o un falso positivo comparando contra un archivo vacio).
if [ "$ORDENES_PUB_OK" -eq 1 ] && [ "$AUDITORIA_RECIB_OK" -eq 1 ]; then
  comm -23 "$TMP_DIR/ordenes_publicados.txt" "$TMP_DIR/auditoria_recibidos.txt" \
    > "$TMP_DIR/publicados_sin_auditar.txt" || true
  TOTAL_SIN_AUDITAR=$(contar "$TMP_DIR/publicados_sin_auditar.txt")
  RECONCILIACION_OK=1
else
  TOTAL_SIN_AUDITAR="N/D"
  RECONCILIACION_OK=0
  FALLAS_EJECUCION+=("reconciliacion publicados-vs-auditados (depende de las dos consultas anteriores)")
fi

echo "outbox_ordenes_event_id_publicados_dos_veces=${TOTAL_OUTBOX_DUP}$( [ "$OUTBOX_DUP_OK" -eq 0 ] && echo ' (NO EVALUADO)')"
echo "ordenes_mensajes_procesados_dos_veces=${TOTAL_ORDENES_PROC_DUP}$( [ "$ORDENES_PROC_OK" -eq 0 ] && echo ' (NO EVALUADO)')"
echo "ordenes_pagos_mensaje_procesado_dos_veces=${TOTAL_ORDENES_PAGOS_PROC_DUP}$( [ "$ORDENES_PAGOS_PROC_OK" -eq 0 ] && echo ' (NO EVALUADO)')"
echo "auditoria_eventos_registrados_dos_veces=${TOTAL_AUDITORIA_DUP}$( [ "$AUDITORIA_DUP_OK" -eq 0 ] && echo ' (NO EVALUADO)')"
echo "auditoria_mensajes_procesados_dos_veces=${TOTAL_AUDITORIA_PROC_DUP}$( [ "$AUDITORIA_PROC_OK" -eq 0 ] && echo ' (NO EVALUADO)')"
echo "---"
echo "outbox_ordenes_publicados_total_desde=${TOTAL_PUBLICADOS}$( [ "$ORDENES_PUB_OK" -eq 0 ] && echo ' (NO EVALUADO)')"
echo "auditoria_eventos_recibidos_total_desde=${TOTAL_AUDITADOS}$( [ "$AUDITORIA_RECIB_OK" -eq 0 ] && echo ' (NO EVALUADO)') (incluye eventos de todos los contextos, no solo ordenes)"
echo "outbox_ordenes_publicados_sin_llegar_a_auditoria=${TOTAL_SIN_AUDITAR}"
echo "==="

if [ "${#FALLAS_EJECUCION[@]}" -gt 0 ]; then
  echo ""
  echo "Consultas que no se pudieron evaluar (${#FALLAS_EJECUCION[@]}):" >&2
  for f in "${FALLAS_EJECUCION[@]}"; do echo "  - $f" >&2; done
fi

# Duplicados/perdidas solo se suman entre las consultas que SI corrieron;
# una consulta fallida ya quedo señalada arriba como NO EVALUADO y cuenta
# para el veredicto INCOMPLETO, no como si hubiera dado cero.
TOTAL_INCONSISTENCIAS=$((TOTAL_OUTBOX_DUP + TOTAL_ORDENES_PROC_DUP + TOTAL_ORDENES_PAGOS_PROC_DUP + TOTAL_AUDITORIA_DUP + TOTAL_AUDITORIA_PROC_DUP))
if [ "$RECONCILIACION_OK" -eq 1 ]; then
  TOTAL_INCONSISTENCIAS=$((TOTAL_INCONSISTENCIAS + TOTAL_SIN_AUDITAR))
fi

if [ "${#FALLAS_EJECUCION[@]}" -gt 0 ]; then
  echo ""
  echo "RESULTADO=INCOMPLETO: ${#FALLAS_EJECUCION[@]} consulta(s) no se pudieron evaluar (detalle arriba)." >&2
  echo "Corrige la causa (esquema/tabla/permiso/conexion) y vuelve a correr antes de dar por buena esta HU." >&2
  exit 2
elif [ "$TOTAL_INCONSISTENCIAS" -eq 0 ]; then
  echo ""
  echo "RESULTADO=APROBADO: 0 duplicados y 0 eventos perdidos con multiples replicas compitiendo."
  exit 0
else
  echo ""
  echo "RESULTADO=RECHAZADO: se encontraron inconsistencias (detalle abajo)." >&2
  if [ "$TOTAL_OUTBOX_DUP" -gt 0 ]; then echo "outbox_duplicado (event_id|count):" >&2; cat "$TMP_DIR/outbox_duplicado.txt" >&2; fi
  if [ "$TOTAL_ORDENES_PROC_DUP" -gt 0 ]; then echo "ordenes_procesado_duplicado (event_id|nombre_servicio|count):" >&2; cat "$TMP_DIR/ordenes_procesado_duplicado.txt" >&2; fi
  if [ "$TOTAL_ORDENES_PAGOS_PROC_DUP" -gt 0 ]; then echo "ordenes_pagos_procesado_duplicado (message_id|count):" >&2; cat "$TMP_DIR/ordenes_pagos_procesado_duplicado.txt" >&2; fi
  if [ "$TOTAL_AUDITORIA_DUP" -gt 0 ]; then echo "auditoria_duplicado (event_id|count):" >&2; cat "$TMP_DIR/auditoria_duplicado.txt" >&2; fi
  if [ "$TOTAL_AUDITORIA_PROC_DUP" -gt 0 ]; then echo "auditoria_procesado_duplicado (event_id|nombre_servicio|count):" >&2; cat "$TMP_DIR/auditoria_procesado_duplicado.txt" >&2; fi
  if [ "$RECONCILIACION_OK" -eq 1 ] && [ "$TOTAL_SIN_AUDITAR" -gt 0 ]; then
    echo "publicados_sin_auditar (primeros 20; si la corrida termino hace <60s, espera a que drene y vuelve a correr antes de tratarlo como fallo real):" >&2
    head -n 20 "$TMP_DIR/publicados_sin_auditar.txt" >&2
  fi
  exit 1
fi