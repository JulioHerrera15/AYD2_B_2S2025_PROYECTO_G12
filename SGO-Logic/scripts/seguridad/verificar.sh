#!/usr/bin/env bash
# =============================================================================
# SGO-Logic — Pruebas de seguridad del MVP (RES-08, tokens JWT, permisos de
# Auditoria). Corre contra el sistema levantado (docker compose up), no contra
# el codigo fuente: lo que se defiende es el comportamiento real corriendo,
# no un test unitario.
#
# Tres bloques independientes, cada uno con sus propios chequeos:
#
#   1) RES-08 — ningun PAN, CVV ni fecha de vencimiento de tarjeta se persiste
#      en ninguna base ni aparece en los logs. Desde que PayKlik tokeniza de
#      verdad (ADR-010): tokeniza una tarjeta de prueba contra el mock
#      (POST /v1/tokens, el unico punto de todo el sistema que ve un PAN/CVV,
#      y corre dentro del proceso del mock), genera una orden real con ese
#      token y con un marcador de texto libre reconocible, y barre las tres
#      instancias de Postgres (db-operacional, db-finanzas, db-auditoria) y
#      los logs de todos los contenedores. Complementa (no duplica) los tests
#      ya existentes en tests/SgoLogic.Pagos.Tests/Application/ServicioCobroTests.cs
#      CobroTokenizadoTests.cs y Infrastructure/TokenizacionMockTests.cs:
#      esos prueban la FORMA de los tipos y el comportamiento contra un EF
#      Core InMemory/doble de PayKlik, en tiempo de compilacion o con dobles;
#      este bloque prueba el CONTENIDO real de las bases y del mock real
#      corriendo, de punta a punta.
#   2) Validacion de JWT — el unico endpoint protegido por [Authorize] en todo
#      el sistema (POST /api/carrito/asociar) rechaza sin token, con token
#      malformado, expirado y firmado con otra clave, y acepta uno valido.
#      Ademas confirma y reporta (no corrige) que el API Gateway no exige
#      autenticacion en ninguna ruta, ni siquiera pagos o facturacion. ADR-020
#      (2026-09-21) ya documenta la decision de validar el JWT en el borde con
#      una bandera Gateway:ExigirJwt por defecto apagada, pero esa bandera y
#      su validacion todavia no existen en el codigo (ver SEGURIDAD.md) — el
#      comportamiento medido aqui sigue siendo el de sin ninguna proteccion.
#   3) Permisos de Auditoria — reusa la evidencia ya demostrada por
#      PermisosInmutabilidadTests (rol restringido sin UPDATE/DELETE, trigger
#      que bloquea incluso al dueno) contra la base real, sin reimplementar
#      esa logica.
#
# Igual que scripts/escalabilidad/verificar.sh: cada chequeo corre de forma
# independiente (sin "set -e"), un chequeo que no se pudo evaluar no aborta
# a los demas, y el dictamen final es de tres estados:
#   APROBADO   — todos los chequeos bloqueantes corrieron y no hay hallazgos.
#   RECHAZADO  — todos corrieron, pero se encontro al menos un hallazgo real
#                (PAN/CVV persistido, o el endpoint protegido acepto un
#                token invalido).
#   INCOMPLETO — una o mas consultas/pasos no se pudieron evaluar.
#
# El hallazgo del gateway sin autenticacion es una excepcion deliberada: se
# reporta siempre, por separado, y NO participa en el dictamen — ya se sabe
# que existe, no se corrige aqui (ver documentacion/docs/SEGURIDAD.md).
#
# Requiere el stack de docker compose arriba (docker compose up -d) y curl,
# psql (via los contenedores) y python3 en la maquina que corre el script.
# =============================================================================

set -uo pipefail

RAIZ="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
COMPOSE_FILE="${RAIZ}/docker-compose.yml"
DIR_SCRIPT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
GATEWAY="${GATEWAY:-http://localhost:8080}"

if [ ! -f "$COMPOSE_FILE" ]; then
  echo "No se encontro docker-compose.yml en $RAIZ" >&2
  exit 1
fi

TMP_DIR="$(mktemp -d)"
trap 'rm -rf "$TMP_DIR"' EXIT

compose() { docker compose -f "$COMPOSE_FILE" "$@"; }

leer_env() {
  local servicio="$1" variable="$2"
  compose exec -T "$servicio" sh -c "printf '%s' \"\$$variable\""
}

# --- credenciales (requisito duro: sin esto no se puede evaluar nada) ------
USUARIO_OPERACIONAL="$(leer_env db-operacional POSTGRES_USER)" || { echo "No se pudo leer credenciales de db-operacional. Esta el stack arriba?" >&2; exit 1; }
CLAVE_OPERACIONAL="$(leer_env db-operacional POSTGRES_PASSWORD)" || { echo "No se pudo leer credenciales de db-operacional." >&2; exit 1; }
USUARIO_FINANZAS="$(leer_env db-finanzas POSTGRES_USER)" || { echo "No se pudo leer credenciales de db-finanzas." >&2; exit 1; }
CLAVE_FINANZAS="$(leer_env db-finanzas POSTGRES_PASSWORD)" || { echo "No se pudo leer credenciales de db-finanzas." >&2; exit 1; }
USUARIO_AUDITORIA_OWNER="$(leer_env db-auditoria POSTGRES_USER)" || { echo "No se pudo leer credenciales de db-auditoria." >&2; exit 1; }
CLAVE_AUDITORIA_OWNER="$(leer_env db-auditoria POSTGRES_PASSWORD)" || { echo "No se pudo leer credenciales de db-auditoria." >&2; exit 1; }
USUARIO_AUDITORIA_APP="$(leer_env auditoria-worker Auditoria__AppUsuario)" || { echo "No se pudo leer Auditoria__AppUsuario de auditoria-worker." >&2; exit 1; }
CLAVE_AUDITORIA_APP="$(leer_env auditoria-worker Auditoria__AppPassword)" || { echo "No se pudo leer Auditoria__AppPassword de auditoria-worker." >&2; exit 1; }
JWT_SECRET_REAL="$(leer_env auth-api Auth__JwtSecret)" || { echo "No se pudo leer Auth__JwtSecret de auth-api." >&2; exit 1; }

psql_en() {
  # psql_en <contenedor> <usuario> <clave> <base> -c "..."
  local contenedor="$1" usuario="$2" clave="$3" base="$4"; shift 4
  compose exec -T -e PGPASSWORD="$clave" "$contenedor" \
    psql -U "$usuario" -d "$base" -v ON_ERROR_STOP=1 -tA "$@"
}

listar_bases() {
  # Bases de usuario en el contenedor Postgres dado (excluye las de sistema).
  local contenedor="$1" usuario="$2" clave="$3"
  compose exec -T -e PGPASSWORD="$clave" "$contenedor" \
    psql -U "$usuario" -d postgres -v ON_ERROR_STOP=1 -tA \
    -c "SELECT datname FROM pg_database WHERE NOT datistemplate AND datname <> 'postgres';" \
    | tr -d '\r' | sed '/^$/d'
}

declare -a FALLAS_EJECUCION=()
declare -a HALLAZGOS_BLOQUEANTES=()
declare -a HALLAZGOS_INFORMATIVOS=()

ejecutar() {
  # ejecutar <funcion> <archivo-salida> <etiqueta>
  local funcion="$1" archivo="$2" etiqueta="$3"
  if "$funcion" > "$archivo" 2>"${archivo}.stderr"; then
    rm -f "${archivo}.stderr"
    return 0
  else
    echo "ADVERTENCIA: '${etiqueta}' fallo, no se pudo evaluar:" >&2
    sed 's/^/    /' "${archivo}.stderr" >&2
    rm -f "${archivo}.stderr"
    : > "$archivo"
    FALLAS_EJECUCION+=("${etiqueta}")
    return 1
  fi
}

extraer_campo() {
  python3 -c "
import sys, json
try:
    d = json.loads(sys.argv[1])
    v = d.get(sys.argv[2], '')
    print('' if v is None else v)
except Exception:
    print('')
" "$1" "$2" 2>/dev/null
}

# llamar_http <metodo> <url> [cuerpo] -- deja el resultado en HTTP_CODE/HTTP_BODY
# (globales) y devuelve 0 si el codigo es 2xx. A diferencia de "curl -sf", nunca
# oculta el cuerpo de un error: quien llama puede imprimirlo con
# reportar_fallo_http para diagnosticar sin tener que repetir la llamada a mano.
llamar_http() {
  local metodo="$1" url="$2" cuerpo="${3:-}"
  local salida
  if [ -n "$cuerpo" ]; then
    salida=$(curl -s -w '\n%{http_code}' -X "$metodo" "$url" -H "Content-Type: application/json" -d "$cuerpo")
  else
    salida=$(curl -s -w '\n%{http_code}' -X "$metodo" "$url" -H "Content-Type: application/json")
  fi
  HTTP_CODE="${salida##*$'\n'}"
  HTTP_BODY="${salida%$'\n'*}"
  case "$HTTP_CODE" in
    2??) return 0 ;;
    *) return 1 ;;
  esac
}

reportar_fallo_http() {
  local paso="$1"
  echo "  FALLO en '${paso}': HTTP ${HTTP_CODE:-sin respuesta}" >&2
  echo "  Cuerpo: ${HTTP_BODY:-<vacio>}" >&2
}

echo "====================================================================="
echo " SGO-Logic — Pruebas de seguridad del MVP                             "
echo "====================================================================="
echo ""

# =============================================================================
# BLOQUE 1 — RES-08: ningun PAN/CVV/vencimiento se persiste ni se loguea
# =============================================================================
echo "--- Bloque 1: RES-08 (no persistencia de PAN/CVV) ---"

RUN_ID="segsweep-$(date +%s)"
PAYKLIK_URL="${PAYKLIK_URL:-http://localhost:8084}"
PAN_PRUEBA="4111111111111111"
CVV_PRUEBA="123"
MES_EXPIRACION=12
ANIO_EXPIRACION=$(( $(date +%Y) + 3 ))
VENC_PRUEBA="${MES_EXPIRACION}/$(printf '%02d' $((ANIO_EXPIRACION % 100)))"
MARCADOR_CVV="CVV_PRUEBA=${CVV_PRUEBA}"
MARCADOR_VENC="VENC_PRUEBA=${VENC_PRUEBA}"
DESCRIPCION_CANARIO="Prueba RES-08 ${RUN_ID}: NUMERO_PRUEBA=${PAN_PRUEBA} ${MARCADOR_CVV} ${MARCADOR_VENC}"
SKU_CANARIO="SKU-SEGURIDAD-${RUN_ID}"

echo "PAN de prueba (Visa de prueba estandar, no real) usado para tokenizar Y como canario de texto libre: ${PAN_PRUEBA}"

# --- 1.0a Tokenizar una tarjeta de prueba contra PayKlik, igual que lo haria
# el navegador (ADR-010, ADR-020 en README/tokenizacion): POST /v1/tokens es
# el UNICO punto de todo el sistema que ve el PAN/CVV, y corre dentro del
# proceso del mock, nunca dentro de SGO-Logic. Complementa (no duplica) los
# tests unitarios de Angel en
# tests/SgoLogic.Pagos.Tests/Infrastructure/TokenizacionMockTests.cs
# y tests/SgoLogic.Pagos.Tests/Application/CobroTokenizadoTests.cs, que
# prueban lo mismo contra un EF Core InMemory y un doble de PayKlik: este
# bloque lo prueba contra el sistema real, de punta a punta, con Postgres de
# verdad y el mock corriendo en su propio contenedor.
TOKEN_OK=1
TOKEN_PAGO=""
if llamar_http POST "${PAYKLIK_URL}/v1/tokens" "{\"numeroTarjeta\":\"${PAN_PRUEBA}\",\"cvv\":\"${CVV_PRUEBA}\",\"mesExpiracion\":${MES_EXPIRACION},\"anioExpiracion\":${ANIO_EXPIRACION}}"; then
  TOKEN_PAGO=$(extraer_campo "$HTTP_BODY" "token")
  TOKEN_ULTIMOS_CUATRO=$(extraer_campo "$HTTP_BODY" "ultimosCuatro")
  TOKEN_MARCA=$(extraer_campo "$HTTP_BODY" "marca")
  if [ -z "$TOKEN_PAGO" ]; then
    TOKEN_OK=0
    echo "ADVERTENCIA: POST /v1/tokens respondio ${HTTP_CODE} pero sin campo 'token' en el cuerpo." >&2
    reportar_fallo_http "tokenizar tarjeta de prueba"
  fi
else
  TOKEN_OK=0
  reportar_fallo_http "tokenizar tarjeta de prueba contra ${PAYKLIK_URL}/v1/tokens"
fi
if [ "$TOKEN_OK" -eq 1 ]; then
  echo "Tarjeta tokenizada contra PayKlik: token=${TOKEN_PAGO} ultimosCuatro=${TOKEN_ULTIMOS_CUATRO} marca=${TOKEN_MARCA}"
else
  FALLAS_EJECUCION+=("tokenizacion de tarjeta de prueba contra PayKlik")
fi

# --- 1.0b Confirmar que TARJETA sin token se sigue rechazando (limite duro:
# ExcepcionTokenMedioPagoRequerido). Si esto alguna vez responde 201, alguien
# quito la validacion y volvio a abrir la puerta a cobrar sin tokenizar.
# metodoEntrega=DOMICILIO (ver nota en 1.0c): esta orden nunca deberia llegar a
# reservar inventario porque se espera que la falte de token la rechace antes,
# pero si algun dia deja de rechazarla, no queremos que el 400 real venga de
# RECOGIDA_TIENDA exigiendo ubicacionSolicitadaId y enmascare lo que aqui se
# prueba (que TARJETA sin token esta prohibido).
llamar_http POST "${GATEWAY}/api/ordenes" \
  "{\"clienteId\":\"SEGURIDAD-${RUN_ID}-sin-token\",\"canal\":\"WEB\",\"metodoEntrega\":\"DOMICILIO\",\"moneda\":\"GTQ\",\"medioPago\":\"TARJETA\",\"impuestos\":12.0,\"lineas\":[{\"sku\":\"${SKU_CANARIO}\",\"descripcion\":\"sin token\",\"cantidad\":1,\"precioUnitario\":100.0}]}" || true
CODIGO_SIN_TOKEN_ORDEN="$HTTP_CODE"
echo "POST /api/ordenes con TARJETA y sin tokenMedioPago -> ${CODIGO_SIN_TOKEN_ORDEN} (se espera 400)"
if [ "$CODIGO_SIN_TOKEN_ORDEN" != "400" ]; then
  reportar_fallo_http "orden TARJETA sin token (se esperaba 400)"
  HALLAZGOS_BLOQUEANTES+=("POST /api/ordenes con medioPago=TARJETA y sin tokenMedioPago respondio ${CODIGO_SIN_TOKEN_ORDEN}, se esperaba 400 (ExcepcionTokenMedioPagoRequerido)")
fi

# --- 1.0c Generar la orden real con el token obtenido en 1.0a, cargando
# ademas el marcador canario en la descripcion de linea (el unico campo de
# texto libre que un cliente controla y que efectivamente viaja hasta el
# evento de dominio -> Auditoria). El token SI es un dato real que ahora
# cruza el sistema (Ordenes -> Pagos -> PayKlik): por eso 1.3/1.4 mas abajo
# verifican su ciclo de vida, no solo el marcador de texto libre.
#
# metodoEntrega=DOMICILIO (y no RECOGIDA_TIENDA): Orden exige
# ubicacionSolicitadaId para RECOGIDA_TIENDA (Domain/Orden.cs), lo que
# obligaria ademas a que el SKU canario tuviera stock en esa tienda exacta.
# Este bloque prueba RES-08 (no persistencia de PAN/CVV), no logica de
# despacho, asi que DOMICILIO es la opcion mas simple: Orden.ValidarDireccion
# acepta direccionEntrega ausente sin exigir ningun campo adicional, y sigue
# reservando el stock ya sembrado en CD-01/CENTRO_DISTRIBUCION sin tener que
# introducir una ubicacion de tienda solo para este script (mismo patron que
# scripts/pruebas-carga/eac01-hu023-carga.js y scripts/pruebas/camino-feliz.ps1).
ORDEN_ID=""
CANARIO_OK=$TOKEN_OK
if [ "$CANARIO_OK" -eq 1 ]; then
  if ! llamar_http PUT "${GATEWAY}/api/inventario/existencias" \
      "{\"sku\":\"${SKU_CANARIO}\",\"ubicacionId\":\"CD-01\",\"tipoUbicacion\":\"CENTRO_DISTRIBUCION\",\"cantidadFisica\":10}"; then
    CANARIO_OK=0
    reportar_fallo_http "sembrar existencias del SKU canario"
  fi
fi

if [ "$CANARIO_OK" -eq 1 ]; then
  CREAR_BODY=$(python3 -c "
import json, sys
cliente_id, sku, descripcion, token = sys.argv[1], sys.argv[2], sys.argv[3], sys.argv[4]
print(json.dumps({
    'clienteId': cliente_id,
    'canal': 'WEB',
    'metodoEntrega': 'DOMICILIO',
    'moneda': 'GTQ',
    'medioPago': 'TARJETA',
    'impuestos': 12.0,
    'tokenMedioPago': token,
    'lineas': [{
        'sku': sku,
        'descripcion': descripcion,
        'cantidad': 1,
        'precioUnitario': 100.0
    }]
}))
" "SEGURIDAD-${RUN_ID}" "$SKU_CANARIO" "$DESCRIPCION_CANARIO" "$TOKEN_PAGO")
  if llamar_http POST "${GATEWAY}/api/ordenes" "$CREAR_BODY"; then
    ORDEN_ID=$(extraer_campo "$HTTP_BODY" "id")
    if [ -z "$ORDEN_ID" ]; then
      CANARIO_OK=0
      echo "ADVERTENCIA: POST /api/ordenes respondio ${HTTP_CODE} pero sin campo 'id' en el cuerpo." >&2
      reportar_fallo_http "crear orden canario"
    fi
  else
    CANARIO_OK=0
    reportar_fallo_http "crear orden canario"
  fi
fi

if [ "$CANARIO_OK" -eq 1 ]; then
  if ! llamar_http POST "${GATEWAY}/api/ordenes/${ORDEN_ID}/confirmar" '{"motivo":"prueba de seguridad RES-08"}'; then
    CANARIO_OK=0
    reportar_fallo_http "confirmar orden canario ${ORDEN_ID}"
  fi
fi

ESTADOS_ACEPTABLES="PAGO_APLICADO CONFIRMADA_PENDIENTE_FISCAL CONFIRMADA DESPACHO_SOLICITADO EN_PREPARACION EN_TRANSITO ENTREGADA"
ESTADO_ALCANZADO=""
if [ "$CANARIO_OK" -eq 1 ]; then
  LIMITE=$((SECONDS + 60))
  while [ "$SECONDS" -lt "$LIMITE" ]; do
    if llamar_http GET "${GATEWAY}/api/ordenes/${ORDEN_ID}"; then
      ESTADO=$(extraer_campo "$HTTP_BODY" "estado")
      for e in $ESTADOS_ACEPTABLES; do
        if [ "$ESTADO" = "$e" ]; then ESTADO_ALCANZADO="$ESTADO"; fi
      done
    fi
    if [ -n "$ESTADO_ALCANZADO" ]; then break; fi
    sleep 1
  done
  if [ -z "$ESTADO_ALCANZADO" ]; then
    CANARIO_OK=0
    echo "  Ultima respuesta de GET /api/ordenes/${ORDEN_ID}:" >&2
    reportar_fallo_http "esperar a que la orden canario ${ORDEN_ID} llegue a un estado aceptable"
  fi
fi

if [ "$CANARIO_OK" -eq 1 ]; then
  echo "Orden canario ${ORDEN_ID} llego a ${ESTADO_ALCANZADO}. Esperando 5s a que Auditoria drene el evento..."
  sleep 5
else
  echo "ADVERTENCIA: no se pudo generar/confirmar la orden canario (orden=${ORDEN_ID:-ninguna}, estado=${ESTADO_ALCANZADO:-ninguno})." >&2
  FALLAS_EJECUCION+=("generacion de orden canario para RES-08")
fi

# --- 1.1 Barrido dinamico de las tres instancias de Postgres ----------------
# Recorre TODAS las bases de cada contenedor (no solo sgo_ordenes/sgo_pagos/
# sgo_auditoria) y, dentro de cada una, TODAS las columnas de texto/jsonb de
# TODOS los esquemas -- no solo las tablas donde uno esperaria encontrar algo.
declare -a RESULTADOS_BARRIDO_ARCHIVOS=()
BARRIDO_OK=1

barrer_contenedor() {
  local contenedor="$1" usuario="$2" clave="$3" etiqueta_contenedor="$4"
  local bases
  bases="$(listar_bases "$contenedor" "$usuario" "$clave")" || return 1
  local base
  for base in $bases; do
    local archivo="${TMP_DIR}/barrido_${etiqueta_contenedor}_${base}.txt"
    local crudo="${archivo}.crudo"
    if compose exec -T -e PGPASSWORD="$clave" "$contenedor" \
        psql -U "$usuario" -d "$base" -v ON_ERROR_STOP=1 -tA \
        -v patron1="%${PAN_PRUEBA}%" -v patron2="%${MARCADOR_CVV}%" -v patron3="%${MARCADOR_VENC}%" \
        < "${DIR_SCRIPT}/barrido-columnas.sql" > "$crudo" 2>"${archivo}.err"; then
      rm -f "${archivo}.err"
      # barrido-columnas.sql corre 3 "SET" y un "DO" antes del SELECT final;
      # -tA no suprime la etiqueta de comando de sentencias que no son SELECT
      # (el mismo motivo por el que antes "UPDATE 0"/"DELETE 0" aparecian
      # como texto plano en Bloque 3), asi que psql imprime "SET"/"DO" como
      # lineas propias. Sin filtrarlas, el interprete de abajo las toma como
      # filas de resultado con esquema="SET"/"DO" y columnas vacias. Nos
      # quedamos solo con lineas que de verdad tienen la forma
      # "esquema|tabla|columna|numero|numero|numero" que produce el SELECT.
      grep -E '^[^|]+\|[^|]+\|[^|]+\|-?[0-9]+\|-?[0-9]+\|-?[0-9]+$' "$crudo" > "$archivo"
      rm -f "$crudo"
      RESULTADOS_BARRIDO_ARCHIVOS+=("${etiqueta_contenedor}|${base}|${archivo}")
    else
      echo "ADVERTENCIA: barrido de ${etiqueta_contenedor}/${base} fallo:" >&2
      sed 's/^/    /' "${archivo}.err" >&2
      rm -f "${archivo}.err" "$crudo"
      return 1
    fi
  done
  return 0
}

barrer_contenedor db-operacional "$USUARIO_OPERACIONAL" "$CLAVE_OPERACIONAL" operacional || { BARRIDO_OK=0; FALLAS_EJECUCION+=("barrido de db-operacional"); }
barrer_contenedor db-finanzas "$USUARIO_FINANZAS" "$CLAVE_FINANZAS" finanzas || { BARRIDO_OK=0; FALLAS_EJECUCION+=("barrido de db-finanzas"); }
barrer_contenedor db-auditoria "$USUARIO_AUDITORIA_OWNER" "$CLAVE_AUDITORIA_OWNER" auditoria || { BARRIDO_OK=0; FALLAS_EJECUCION+=("barrido de db-auditoria"); }

# --- 1.2 Interpretar el barrido ---------------------------------------------
# El canario se inyecta a proposito en la descripcion de la linea de orden, y
# esa descripcion viaja LEGITIMAMENTE a Facturacion (el documento fiscal
# describe lo vendido), al outbox, a Despacho y a Auditoria: encontrar el
# marcador ahi no es una violacion de RES-08, es la prueba de que el barrido
# funciona (control positivo / mapa de propagacion). RES-08 exige que el
# PAN/CVV no lleguen a Pagos ni a las columnas de datos de tarjeta -- lo
# segundo ya lo cubre 1.3 (consulta_pan_shaped_pagos) buscando corridas de
# digitos con forma de tarjeta en pagos.pago/intento_pago; lo primero se
# verifica aqui: CUALQUIER aparicion del marcador dentro de la base sgo_pagos
# (sin importar la tabla) SI es un hallazgo real, porque Pagos solo deberia
# ver el token opaco, nunca el texto libre de la orden. Todo lo demas
# (Facturacion, Despacho, Auditoria, outbox, ERP, etc.) es propagacion
# esperada y se reporta como informativo.
CONTROL_POSITIVO_ORDENES=0
CONTROL_POSITIVO_AUDITORIA=0
echo ""
echo "Mapa de propagacion del marcador de prueba (informativo, no todo aqui es un hallazgo):"
for entrada in "${RESULTADOS_BARRIDO_ARCHIVOS[@]}"; do
  IFS='|' read -r contenedor base archivo <<< "$entrada"
  while IFS='|' read -r esquema tabla columna c1 c2 c3; do
    [ -z "$esquema" ] && continue
    echo "  ${contenedor}/${base}: ${esquema}.${tabla}.${columna} -> PAN=${c1} CVV=${c2} VENC=${c3}"
    if [ "$base" = "sgo_ordenes" ] && [ "$tabla" = "linea_orden" ]; then
      CONTROL_POSITIVO_ORDENES=1
    fi
    if [ "$base" = "sgo_auditoria" ] && [ "$tabla" = "eventos_auditoria" ]; then
      CONTROL_POSITIVO_AUDITORIA=1
    fi
    if [ "$base" = "sgo_pagos" ]; then
      HALLAZGOS_BLOQUEANTES+=("Marcador de prueba (PAN/CVV/vencimiento) encontrado en el contexto de Pagos: ${base}.${esquema}.${tabla}.${columna} (PAN=${c1} CVV=${c2} VENC=${c3}) — Pagos solo deberia ver el token opaco, nunca el texto libre de la orden")
    else
      HALLAZGOS_INFORMATIVOS+=("Propagacion esperada del texto libre de la orden (no es un hallazgo de RES-08): ${base}.${esquema}.${tabla}.${columna} (PAN=${c1} CVV=${c2} VENC=${c3})")
    fi
  done < "$archivo"
done
echo ""

if [ "$CANARIO_OK" -eq 1 ]; then
  if [ "$CONTROL_POSITIVO_ORDENES" -eq 0 ]; then
    FALLAS_EJECUCION+=("control positivo: el marcador no aparecio en ordenes.linea_orden pese a haberse inyectado — el barrido o la inyeccion no son confiables")
  fi
  if [ "$CONTROL_POSITIVO_AUDITORIA" -eq 0 ]; then
    FALLAS_EJECUCION+=("control positivo: el marcador no aparecio en eventos_auditoria pese a esperar 5s de drenaje — el barrido, el consumidor de Auditoria, o el drenaje no son confiables")
  fi
fi

# --- 1.3 Chequeo dirigido: nada con forma de PAN (13-19 digitos seguidos) en
# las columnas de pago/intento_pago mas alla del "ultimos_cuatro" esperado.
consulta_pan_shaped_pagos() {
  psql_en db-finanzas "$USUARIO_FINANZAS" "$CLAVE_FINANZAS" sgo_pagos -c "
    SELECT 'intento_pago' AS tabla, 'token_medio_pago' AS columna, token_medio_pago AS valor FROM pagos.intento_pago WHERE token_medio_pago ~ '[0-9]{13,19}'
    UNION ALL
    SELECT 'intento_pago', 'autorizacion_id', autorizacion_id FROM pagos.intento_pago WHERE autorizacion_id ~ '[0-9]{13,19}'
    UNION ALL
    SELECT 'intento_pago', 'codigo_respuesta', codigo_respuesta FROM pagos.intento_pago WHERE codigo_respuesta ~ '[0-9]{13,19}'
    UNION ALL
    SELECT 'intento_pago', 'mensaje_respuesta', mensaje_respuesta FROM pagos.intento_pago WHERE mensaje_respuesta ~ '[0-9]{13,19}'
    UNION ALL
    SELECT 'intento_pago', 'marca', marca FROM pagos.intento_pago WHERE marca ~ '[0-9]{13,19}'
    UNION ALL
    SELECT 'pago', 'orden_id', orden_id FROM pagos.pago WHERE orden_id ~ '[0-9]{13,19}'
    UNION ALL
    SELECT 'pago', 'cliente_id', cliente_id FROM pagos.pago WHERE cliente_id ~ '[0-9]{13,19}';
  " | tr -d '\r' | sed '/^$/d'
}
ejecutar consulta_pan_shaped_pagos "$TMP_DIR/pan_shaped_pagos.txt" "columnas de pagos.pago/intento_pago sin corridas de 13-19 digitos" || true
TOTAL_PAN_SHAPED=$(wc -l < "$TMP_DIR/pan_shaped_pagos.txt" | tr -d ' ')
if [ "$TOTAL_PAN_SHAPED" -gt 0 ]; then
  HALLAZGOS_BLOQUEANTES+=("Se encontraron ${TOTAL_PAN_SHAPED} valor(es) con forma de numero de tarjeta (13-19 digitos seguidos) en pagos.pago/intento_pago — ver ${TMP_DIR}/pan_shaped_pagos.txt")
fi

# --- 1.4 Confirmar que Pagos resolvio la tarjeta REAL tokenizada (no un
# valor fijo de mock): ahora que hay una tarjeta de verdad detras del token,
# ultimos_cuatro y marca deben coincidir exactamente con lo que devolvio
# POST /v1/tokens en 1.0a, no con cualquier valor.
if [ "$CANARIO_OK" -eq 1 ] && [ "$TOKEN_OK" -eq 1 ]; then
  consulta_intento_canario() {
    psql_en db-finanzas "$USUARIO_FINANZAS" "$CLAVE_FINANZAS" sgo_pagos -c "
      SELECT i.token_medio_pago, i.ultimos_cuatro, i.marca, i.estado
      FROM pagos.intento_pago i JOIN pagos.pago p ON p.id = i.pago_id
      WHERE p.orden_id = '${ORDEN_ID}'
      ORDER BY i.intento_numero DESC LIMIT 1;
    " | tr -d '\r'
  }
  if ejecutar consulta_intento_canario "$TMP_DIR/intento_canario.txt" "intento de pago de la orden canario"; then
    IFS='|' read -r TOKEN_EN_INTENTO ULTIMOS_EN_INTENTO MARCA_EN_INTENTO ESTADO_EN_INTENTO < "$TMP_DIR/intento_canario.txt"
    echo "Intento de pago de la orden canario (token|ultimos_cuatro|marca|estado): $(cat "$TMP_DIR/intento_canario.txt")"
    if [ "$ULTIMOS_EN_INTENTO" != "$TOKEN_ULTIMOS_CUATRO" ] || [ "$MARCA_EN_INTENTO" != "$TOKEN_MARCA" ]; then
      HALLAZGOS_BLOQUEANTES+=("pagos.intento_pago quedo con ultimos_cuatro=${ULTIMOS_EN_INTENTO}/marca=${MARCA_EN_INTENTO}, pero la tokenizacion real devolvio ultimos_cuatro=${TOKEN_ULTIMOS_CUATRO}/marca=${TOKEN_MARCA} — Pagos no esta resolviendo la tarjeta tokenizada de verdad")
    fi
    if [ "$TOKEN_EN_INTENTO" != "$TOKEN_PAGO" ]; then
      HALLAZGOS_BLOQUEANTES+=("pagos.intento_pago.token_medio_pago (${TOKEN_EN_INTENTO}) no coincide con el token emitido por PayKlik (${TOKEN_PAGO})")
    fi
  fi
fi

# --- 1.4b El token es opaco (no es el riesgo que previene RES-08), pero
# igual se verifica su ciclo de vida documentado: Orden.TomarTokenMedioPago()
# lo suelta al emitir CobroSolicitado, asi que no debe quedar en la orden
# despues de que la saga curso el cobro.
if [ "$CANARIO_OK" -eq 1 ] && [ "$TOKEN_OK" -eq 1 ]; then
  consulta_token_orden() {
    psql_en db-operacional "$USUARIO_OPERACIONAL" "$CLAVE_OPERACIONAL" sgo_ordenes -c "
      SELECT token_medio_pago FROM ordenes.orden WHERE id = '${ORDEN_ID}';
    " | tr -d '\r'
  }
  if ejecutar consulta_token_orden "$TMP_DIR/token_orden.txt" "token_medio_pago de la orden canario tras el cobro"; then
    TOKEN_RESIDUAL="$(cat "$TMP_DIR/token_orden.txt")"
    if [ -n "$TOKEN_RESIDUAL" ]; then
      HALLAZGOS_BLOQUEANTES+=("ordenes.orden.token_medio_pago sigue con un valor (${TOKEN_RESIDUAL}) despues de que la saga curso el cobro; TomarTokenMedioPago() deberia haberlo dejado en NULL")
    else
      echo "ordenes.orden.token_medio_pago quedo en NULL tras el cobro, como documenta Orden.TomarTokenMedioPago()."
    fi
  fi
fi

# --- 1.5 Barrido de logs de TODO el stack -----------------------------------
# Corre siempre, tenga o no exito el canario de arriba: MARCADOR_CVV,
# MARCADOR_VENC y PAN_PRUEBA se definen incondicionalmente al inicio del
# bloque, asi que no hay variable sin definir que haga fallar el "grep" bajo
# "set -u" (antes referenciaba MARCADOR_PAN, que nunca llego a definirse).
consulta_logs() {
  compose logs --no-color 2>/dev/null | grep -F -i -e "$MARCADOR_CVV" -e "$MARCADOR_VENC" -e "$PAN_PRUEBA"
}
LOGS_MATCH="$(consulta_logs || true)"
TOTAL_LOGS_MATCH=$(printf '%s' "$LOGS_MATCH" | sed '/^$/d' | wc -l | tr -d ' ')
if [ "$TOTAL_LOGS_MATCH" -gt 0 ]; then
  echo "$LOGS_MATCH" > "$TMP_DIR/logs_con_marcador.txt"
  HALLAZGOS_BLOQUEANTES+=("El marcador de prueba (PAN/CVV/vencimiento) aparece en los logs de algun contenedor (${TOTAL_LOGS_MATCH} linea(s)) — ver ${TMP_DIR}/logs_con_marcador.txt")
fi
echo "Lineas de log con el marcador de prueba: ${TOTAL_LOGS_MATCH}"

echo ""

# =============================================================================
# BLOQUE 2 — Validacion de JWT
# =============================================================================
echo "--- Bloque 2: validacion de JWT ---"

crear_jwt() {
  local secret="$1" exp_epoch="$2"
  python3 - "$secret" "$exp_epoch" <<'PYEOF'
import sys, json, hmac, hashlib, base64, uuid

secret, exp = sys.argv[1], int(sys.argv[2])

def b64url(data: bytes) -> str:
    return base64.urlsafe_b64encode(data).rstrip(b'=').decode()

header = {"alg": "HS256", "typ": "JWT"}
payload = {
    "sub": "00000000-0000-0000-0000-000000000000",
    "email": "prueba-seguridad@sgo-logic.local",
    "nombre": "Prueba Seguridad",
    "jti": str(uuid.uuid4()),
    "exp": exp,
}
h = b64url(json.dumps(header, separators=(",", ":")).encode())
p = b64url(json.dumps(payload, separators=(",", ":")).encode())
signing_input = f"{h}.{p}".encode()
sig = hmac.new(secret.encode(), signing_input, hashlib.sha256).digest()
print(f"{signing_input.decode()}.{b64url(sig)}")
PYEOF
}

EXP_FUTURO=$(( $(date +%s) + 3600 ))
EXP_PASADO=$(( $(date +%s) - 3600 ))
TOKEN_EXPIRADO="$(crear_jwt "$JWT_SECRET_REAL" "$EXP_PASADO")"
TOKEN_OTRA_CLAVE="$(crear_jwt "una-clave-completamente-distinta-de-32-caracteres-o-mas" "$EXP_FUTURO")"

codigo_http() {
  # codigo_http <metodo> <url> [header-auth]
  local metodo="$1" url="$2" auth="${3:-}"
  if [ -n "$auth" ]; then
    curl -s -o /dev/null -w "%{http_code}" -X "$metodo" "$url" \
      -H "Content-Type: application/json" -H "Authorization: $auth" -d '{"sesionId":"sesion-prueba-seguridad"}'
  else
    curl -s -o /dev/null -w "%{http_code}" -X "$metodo" "$url" \
      -H "Content-Type: application/json" -d '{"sesionId":"sesion-prueba-seguridad"}'
  fi
}

RUTA_ASOCIAR="${GATEWAY}/api/carrito/asociar"

CODIGO_SIN_TOKEN=$(codigo_http POST "$RUTA_ASOCIAR")
CODIGO_MALFORMADO=$(codigo_http POST "$RUTA_ASOCIAR" "Bearer esto-no-es-un-jwt")
CODIGO_EXPIRADO=$(codigo_http POST "$RUTA_ASOCIAR" "Bearer ${TOKEN_EXPIRADO}")
CODIGO_OTRA_CLAVE=$(codigo_http POST "$RUTA_ASOCIAR" "Bearer ${TOKEN_OTRA_CLAVE}")

echo "POST /api/carrito/asociar sin token           -> ${CODIGO_SIN_TOKEN} (se espera 401)"
echo "POST /api/carrito/asociar token malformado     -> ${CODIGO_MALFORMADO} (se espera 401)"
echo "POST /api/carrito/asociar token expirado       -> ${CODIGO_EXPIRADO} (se espera 401)"
echo "POST /api/carrito/asociar token de otra clave  -> ${CODIGO_OTRA_CLAVE} (se espera 401)"

for par in "sin_token:$CODIGO_SIN_TOKEN" "malformado:$CODIGO_MALFORMADO" "expirado:$CODIGO_EXPIRADO" "otra_clave:$CODIGO_OTRA_CLAVE"; do
  caso="${par%%:*}"; codigo="${par##*:}"
  if [ "$codigo" != "401" ]; then
    HALLAZGOS_BLOQUEANTES+=("POST /api/carrito/asociar con token '${caso}' respondio ${codigo}, se esperaba 401")
  fi
done

# --- Token valido real: registro -> codigo en logs -> verificar -> login ----
EMAIL_PRUEBA="seguridad-${RUN_ID}@sgo-logic-pruebas.local"
CONTRASENA_PRUEBA="Prueba-Seguridad-2026!"
TOKEN_VALIDO=""
LOGIN_OK=1

if ! llamar_http POST "${GATEWAY}/api/auth/registro" \
    "{\"email\":\"${EMAIL_PRUEBA}\",\"contrasena\":\"${CONTRASENA_PRUEBA}\",\"nombreCompleto\":\"Prueba Seguridad\"}"; then
  LOGIN_OK=0
  reportar_fallo_http "registrar usuario de prueba para el flujo JWT"
fi

if [ "$LOGIN_OK" -eq 1 ]; then
  sleep 1
  CODIGO_VERIF=""
  # AuthService.cs loguea "...para {Email}: {Codigo}" con Serilog, que
  # renderiza los parametros de tipo string entre comillas dobles (verificado
  # contra AuthService.cs:56). Como ademas ese mensaje viaja dentro de un
  # campo JSON ("RenderedMessage":"...para \"correo@x\": \"854492\""), las
  # comillas llegan escapadas con una barra invertida (\"), no sueltas. Un
  # sed/grep con comillas literales se enreda al escapar la barra invertida
  # dos veces (una para bash, otra para el regex), asi que se resuelve con
  # python3 (ya es la convencion del script para JSON/regex, ver crear_jwt())
  # con un regex que tolera \, ", ambas o ninguna alrededor del correo y del
  # codigo, sin asumir que el codigo es lo ultimo en la linea.
  for _ in 1 2 3 4 5; do
    CODIGO_VERIF=$(compose logs --no-color auth-api 2>/dev/null | python3 -c '
import re
import sys

Q = chr(34)
email = sys.argv[1]
patron = re.compile(
    r"Codigo de verificacion para \\?" + Q + "?" +
    re.escape(email) +
    r"\\?" + Q + r"?:\s*\\?" + Q + r"?([0-9]{4,8})"
)
codigo = ""
for linea in sys.stdin:
    m = patron.search(linea)
    if m:
        codigo = m.group(1)
print(codigo)
' "$EMAIL_PRUEBA")
    [ -n "$CODIGO_VERIF" ] && break
    sleep 1
  done
  if [ -z "$CODIGO_VERIF" ]; then
    LOGIN_OK=0
    echo "  FALLO en 'leer codigo de verificacion de los logs de auth-api': ninguna linea matcheo 'Codigo de verificacion para ${EMAIL_PRUEBA}' tras 5 intentos (1s cada uno)." >&2
    echo "  Lineas de auth-api que mencionan ese correo (si alguna):" >&2
    compose logs --no-color auth-api 2>/dev/null | grep -F "${EMAIL_PRUEBA}" | tail -n3 | sed 's/^/    /' >&2
  fi
fi

if [ "$LOGIN_OK" -eq 1 ]; then
  if ! llamar_http POST "${GATEWAY}/api/auth/verificar" \
      "{\"email\":\"${EMAIL_PRUEBA}\",\"codigo\":\"${CODIGO_VERIF}\"}"; then
    LOGIN_OK=0
    reportar_fallo_http "verificar usuario de prueba para el flujo JWT"
  fi
fi

if [ "$LOGIN_OK" -eq 1 ]; then
  if llamar_http POST "${GATEWAY}/api/auth/login" \
      "{\"email\":\"${EMAIL_PRUEBA}\",\"contrasena\":\"${CONTRASENA_PRUEBA}\"}"; then
    TOKEN_VALIDO=$(extraer_campo "$HTTP_BODY" "token")
    if [ -z "$TOKEN_VALIDO" ]; then
      LOGIN_OK=0
      echo "ADVERTENCIA: POST /api/auth/login respondio ${HTTP_CODE} pero sin campo 'token' en el cuerpo." >&2
      reportar_fallo_http "login de usuario de prueba para el flujo JWT"
    fi
  else
    LOGIN_OK=0
    reportar_fallo_http "login de usuario de prueba para el flujo JWT"
  fi
fi

if [ "$LOGIN_OK" -eq 1 ]; then
  CODIGO_VALIDO=$(codigo_http POST "$RUTA_ASOCIAR" "Bearer ${TOKEN_VALIDO}")
  echo "POST /api/carrito/asociar token valido real    -> ${CODIGO_VALIDO} (se espera != 401)"
  if [ "$CODIGO_VALIDO" = "401" ]; then
    HALLAZGOS_BLOQUEANTES+=("POST /api/carrito/asociar rechazo (401) un token real recien emitido por login — el mecanismo de validacion rechaza tokens validos")
  fi
else
  echo "ADVERTENCIA: no se pudo completar registro/verificacion/login para obtener un token valido real." >&2
  FALLAS_EJECUCION+=("flujo registro-verificar-login para token JWT valido")
fi

echo ""
echo "Hallazgo a reportar (no se corrige aqui): exposicion del resto de las rutas"
echo "sin exigir ningun token, vía el API Gateway."
RUTAS_SIN_AUTORIZACION=(
  "GET|${GATEWAY}/api/pagos/ordenes/orden-inexistente-${RUN_ID}"
  "GET|${GATEWAY}/api/facturacion/documentos/orden-inexistente-${RUN_ID}"
  "GET|${GATEWAY}/api/inventario/existencias"
  "POST|${GATEWAY}/api/ordenes"
)
for ruta in "${RUTAS_SIN_AUTORIZACION[@]}"; do
  metodo="${ruta%%|*}"; url="${ruta##*|}"
  codigo=$(curl -s -o /dev/null -w "%{http_code}" -X "$metodo" "$url" -H "Content-Type: application/json" -d '{}')
  echo "  ${metodo} ${url#$GATEWAY} sin token -> ${codigo} (no es 401/403: la ruta no exige autenticacion)"
  HALLAZGOS_INFORMATIVOS+=("${metodo} ${url#$GATEWAY} respondio ${codigo} sin ningun token (esperado dado el hallazgo confirmado; no es un chequeo bloqueante)")
done

echo ""

# =============================================================================
# BLOQUE 3 — Permisos de Auditoria (reusa la evidencia ya demostrada)
# =============================================================================
echo "--- Bloque 3: permisos de Auditoria (reutilizando PermisosInmutabilidadTests) ---"

# Las tres consultas de este bloque apuntan a "la ultima fila de
# eventos_auditoria"; si la tabla esta vacia, esa subconsulta da NULL,
# "WHERE id = NULL" no matchea ninguna fila, y psql devuelve el mismo tag
# de siempre ("UPDATE 0"/"DELETE 0") sin ningun texto de error. Sin filas
# que tocar:
#   - Los dos chequeos con el usuario RESTRINGIDO no dependen de que haya
#     filas: Postgres revisa el privilegio UPDATE/DELETE sobre la tabla al
#     planear la sentencia, ANTES de evaluar el WHERE, asi que "permission
#     denied" (42501) sale igual con la tabla vacia o llena. No comparten
#     el defecto de abajo, pero igual se clasifican con la misma funcion
#     por consistencia y como defensa adicional.
#   - El chequeo con el usuario DUENO SI depende de que haya una fila: el
#     trigger es "FOR EACH ROW", o sea que solo se dispara si el UPDATE
#     afecta al menos una fila. Con la tabla vacia, el UPDATE de 0 filas
#     tiene exito sin pasar por el trigger, y el resultado ("UPDATE 0") es
#     indistinguible en forma de un "no se disparo el trigger" real salvo
#     por el conteo de filas afectadas. Reportar eso como hallazgo seria
#     un falso positivo: no se probo nada, no que la inmutabilidad fallo.
clasificar_bloqueo_auditoria() {
  # clasificar_bloqueo_auditoria <salida> <patron_bloqueo> <comando>
  # Devuelve por stdout: bloqueado | sin_filas | no_bloqueado
  local salida="$1" patron_bloqueo="$2" comando="$3"
  if echo "$salida" | grep -qiE "$patron_bloqueo"; then
    echo "bloqueado"
  elif echo "$salida" | tr -d '\r' | grep -qE "^${comando} 0[[:space:]]*$"; then
    echo "sin_filas"
  else
    echo "no_bloqueado"
  fi
}

consulta_update_restringido() {
  psql_en db-auditoria "$USUARIO_AUDITORIA_APP" "$CLAVE_AUDITORIA_APP" sgo_auditoria -c "
    UPDATE eventos_auditoria SET event_type = event_type
    WHERE id = (SELECT id FROM eventos_auditoria ORDER BY fecha_recepcion DESC LIMIT 1);
  " 2>&1
}
SALIDA_UPDATE_RESTRINGIDO="$(consulta_update_restringido)"
case "$(clasificar_bloqueo_auditoria "$SALIDA_UPDATE_RESTRINGIDO" "permission denied|42501" "UPDATE")" in
  bloqueado)
    echo "UPDATE con usuario restringido (${USUARIO_AUDITORIA_APP}) -> bloqueado por permisos (42501), como se esperaba."
    ;;
  sin_filas)
    FALLAS_EJECUCION+=("UPDATE con usuario restringido sobre eventos_auditoria no evaluado: la tabla esta vacia, no hay fila sobre la cual probar el bloqueo de permisos")
    ;;
  *)
    HALLAZGOS_BLOQUEANTES+=("UPDATE sobre eventos_auditoria con el usuario restringido de la aplicacion NO fue bloqueado por permisos: ${SALIDA_UPDATE_RESTRINGIDO}")
    ;;
esac

consulta_delete_restringido() {
  psql_en db-auditoria "$USUARIO_AUDITORIA_APP" "$CLAVE_AUDITORIA_APP" sgo_auditoria -c "
    DELETE FROM eventos_auditoria
    WHERE id = (SELECT id FROM eventos_auditoria ORDER BY fecha_recepcion DESC LIMIT 1);
  " 2>&1
}
SALIDA_DELETE_RESTRINGIDO="$(consulta_delete_restringido)"
case "$(clasificar_bloqueo_auditoria "$SALIDA_DELETE_RESTRINGIDO" "permission denied|42501" "DELETE")" in
  bloqueado)
    echo "DELETE con usuario restringido (${USUARIO_AUDITORIA_APP}) -> bloqueado por permisos (42501), como se esperaba."
    ;;
  sin_filas)
    FALLAS_EJECUCION+=("DELETE con usuario restringido sobre eventos_auditoria no evaluado: la tabla esta vacia, no hay fila sobre la cual probar el bloqueo de permisos")
    ;;
  *)
    HALLAZGOS_BLOQUEANTES+=("DELETE sobre eventos_auditoria con el usuario restringido de la aplicacion NO fue bloqueado por permisos: ${SALIDA_DELETE_RESTRINGIDO}")
    ;;
esac

consulta_update_dueno() {
  psql_en db-auditoria "$USUARIO_AUDITORIA_OWNER" "$CLAVE_AUDITORIA_OWNER" sgo_auditoria -c "
    UPDATE eventos_auditoria SET event_type = event_type
    WHERE id = (SELECT id FROM eventos_auditoria ORDER BY fecha_recepcion DESC LIMIT 1);
  " 2>&1
}
SALIDA_UPDATE_DUENO="$(consulta_update_dueno)"
case "$(clasificar_bloqueo_auditoria "$SALIDA_UPDATE_DUENO" "solo insercion|bitacora inmutable|P0001" "UPDATE")" in
  bloqueado)
    echo "UPDATE con usuario dueno (${USUARIO_AUDITORIA_OWNER}) -> bloqueado por el trigger (P0001), como se esperaba."
    ;;
  sin_filas)
    FALLAS_EJECUCION+=("UPDATE con usuario dueno sobre eventos_auditoria no evaluado: la tabla esta vacia, no hay fila sobre la cual disparar el trigger FOR EACH ROW")
    ;;
  *)
    HALLAZGOS_BLOQUEANTES+=("UPDATE sobre eventos_auditoria con el usuario DUENO de la tabla NO fue bloqueado por el trigger: ${SALIDA_UPDATE_DUENO}")
    ;;
esac

echo ""
echo "====================================================================="
echo " RESUMEN                                                              "
echo "====================================================================="

if [ "${#HALLAZGOS_INFORMATIVOS[@]}" -gt 0 ]; then
  echo ""
  echo "Hallazgos informativos (no bloquean el dictamen, ya conocidos y fuera de alcance para corregir aqui):"
  for h in "${HALLAZGOS_INFORMATIVOS[@]}"; do echo "  - $h"; done
fi

if [ "${#FALLAS_EJECUCION[@]}" -gt 0 ]; then
  echo ""
  echo "Chequeos que no se pudieron evaluar (${#FALLAS_EJECUCION[@]}):" >&2
  for f in "${FALLAS_EJECUCION[@]}"; do echo "  - $f" >&2; done
fi

if [ "${#HALLAZGOS_BLOQUEANTES[@]}" -gt 0 ]; then
  echo ""
  echo "Hallazgos que SI afectan el dictamen (${#HALLAZGOS_BLOQUEANTES[@]}):" >&2
  for h in "${HALLAZGOS_BLOQUEANTES[@]}"; do echo "  - $h" >&2; done
fi

echo ""
if [ "${#FALLAS_EJECUCION[@]}" -gt 0 ]; then
  echo "RESULTADO=INCOMPLETO: ${#FALLAS_EJECUCION[@]} chequeo(s) no se pudieron evaluar (detalle arriba)." >&2
  echo "Corrige la causa y vuelve a correr antes de dar por buena esta HU." >&2
  exit 2
elif [ "${#HALLAZGOS_BLOQUEANTES[@]}" -gt 0 ]; then
  echo "RESULTADO=RECHAZADO: se encontraron ${#HALLAZGOS_BLOQUEANTES[@]} hallazgo(s) reales (detalle arriba)." >&2
  exit 1
else
  echo "RESULTADO=APROBADO: sin PAN/CVV/vencimiento persistido ni logueado, JWT valida correctamente en el unico endpoint protegido, y los permisos de Auditoria siguen bloqueando UPDATE/DELETE (incluso al dueno)."
  exit 0
fi