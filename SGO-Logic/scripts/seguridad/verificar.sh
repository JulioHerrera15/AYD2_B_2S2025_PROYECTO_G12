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
#      en ninguna base ni aparece en los logs. Genera una orden con pago real
#      (usando un marcador de prueba reconocible) y barre las tres instancias
#      de Postgres (db-operacional, db-finanzas, db-auditoria) y los logs de
#      todos los contenedores. Complementa (no duplica) el test por reflexion
#      ya existente en tests/SgoLogic.Pagos.Tests/Application/ServicioCobroTests.cs
#      (Ningun_tipo_del_contexto_Pagos_expone_campos_de_PAN_CVV_o_vencimiento):
#      ese test verifica la FORMA de los tipos de Pagos en tiempo de compilacion;
#      este bloque verifica el CONTENIDO real de las bases en tiempo de ejecucion.
#   2) Validacion de JWT — el unico endpoint protegido por [Authorize] en todo
#      el sistema (POST /api/carrito/asociar) rechaza sin token, con token
#      malformado, expirado y firmado con otra clave, y acepta uno valido.
#      Ademas confirma y reporta (no corrige) que el API Gateway no exige
#      autenticacion en ninguna ruta, ni siquiera pagos o facturacion.
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

echo "====================================================================="
echo " SGO-Logic — Pruebas de seguridad del MVP                             "
echo "====================================================================="
echo ""

# =============================================================================
# BLOQUE 1 — RES-08: ningun PAN/CVV/vencimiento se persiste ni se loguea
# =============================================================================
echo "--- Bloque 1: RES-08 (no persistencia de PAN/CVV) ---"

RUN_ID="segsweep-$(date +%s)"
PAN_PRUEBA="4111111111111111"
CVV_PRUEBA="123"
VENC_PRUEBA="12/29"
MARCADOR_PAN="NUMERO_PRUEBA=${PAN_PRUEBA}"
MARCADOR_CVV="CVV_PRUEBA=${CVV_PRUEBA}"
MARCADOR_VENC="VENC_PRUEBA=${VENC_PRUEBA}"
DESCRIPCION_CANARIO="Prueba RES-08 ${RUN_ID}: ${MARCADOR_PAN} ${MARCADOR_CVV} ${MARCADOR_VENC}"
SKU_CANARIO="SKU-SEGURIDAD-${RUN_ID}"

echo "Marcador de prueba inyectado (no es un PAN real, es un canario para el barrido): ${MARCADOR_PAN}"

# --- 1.0 Generar una orden real con pago, cargando el marcador en la
# descripcion de linea (el unico campo de texto libre que un cliente controla
# y que efectivamente viaja hasta el evento de dominio -> Auditoria). Esto NO
# simula un atacante metiendo un PAN en un campo de pago: se verifico en el
# codigo que ningun endpoint de creacion/confirmacion de orden ni de Pagos
# acepta numero de tarjeta o CVV como entrada (ver SEGURIDAD.md). El marcador
# sirve para demostrar que el barrido de abajo SI detecta datos sensibles en
# cualquier tabla/columna de texto o jsonb donde de verdad aparezcan.
ORDEN_ID=""
CANARIO_OK=1
{
  curl -sf -X PUT "${GATEWAY}/api/inventario/existencias" \
    -H "Content-Type: application/json" \
    -d "{\"sku\":\"${SKU_CANARIO}\",\"ubicacionId\":\"CD-01\",\"tipoUbicacion\":\"CENTRO_DISTRIBUCION\",\"cantidadFisica\":10}" \
    > /dev/null
} || CANARIO_OK=0

if [ "$CANARIO_OK" -eq 1 ]; then
  CREAR_BODY=$(python3 -c "
import json, sys
cliente_id, sku, descripcion = sys.argv[1], sys.argv[2], sys.argv[3]
print(json.dumps({
    'clienteId': cliente_id,
    'canal': 'WEB',
    'metodoEntrega': 'RECOGIDA_TIENDA',
    'moneda': 'GTQ',
    'medioPago': 'TARJETA',
    'impuestos': 12.0,
    'lineas': [{
        'sku': sku,
        'descripcion': descripcion,
        'cantidad': 1,
        'precioUnitario': 100.0
    }]
}))
" "SEGURIDAD-${RUN_ID}" "$SKU_CANARIO" "$DESCRIPCION_CANARIO")
  RESP_ORDEN=$(curl -sf -X POST "${GATEWAY}/api/ordenes" -H "Content-Type: application/json" -d "$CREAR_BODY") || CANARIO_OK=0
  if [ "$CANARIO_OK" -eq 1 ]; then
    ORDEN_ID=$(extraer_campo "$RESP_ORDEN" "id")
    if [ -z "$ORDEN_ID" ]; then CANARIO_OK=0; fi
  fi
fi

if [ "$CANARIO_OK" -eq 1 ]; then
  curl -sf -X POST "${GATEWAY}/api/ordenes/${ORDEN_ID}/confirmar" \
    -H "Content-Type: application/json" -d '{"motivo":"prueba de seguridad RES-08"}' > /dev/null || CANARIO_OK=0
fi

ESTADOS_ACEPTABLES="PAGO_APLICADO CONFIRMADA_PENDIENTE_FISCAL CONFIRMADA DESPACHO_SOLICITADO EN_PREPARACION EN_TRANSITO ENTREGADA"
ESTADO_ALCANZADO=""
if [ "$CANARIO_OK" -eq 1 ]; then
  LIMITE=$((SECONDS + 60))
  while [ "$SECONDS" -lt "$LIMITE" ]; do
    RESP=$(curl -sf "${GATEWAY}/api/ordenes/${ORDEN_ID}" 2>/dev/null) || RESP=""
    ESTADO=$(extraer_campo "$RESP" "estado")
    for e in $ESTADOS_ACEPTABLES; do
      if [ "$ESTADO" = "$e" ]; then ESTADO_ALCANZADO="$ESTADO"; fi
    done
    if [ -n "$ESTADO_ALCANZADO" ]; then break; fi
    sleep 1
  done
  if [ -z "$ESTADO_ALCANZADO" ]; then CANARIO_OK=0; fi
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
    if compose exec -T -e PGPASSWORD="$clave" "$contenedor" \
        psql -U "$usuario" -d "$base" -v ON_ERROR_STOP=1 -tA \
        -v patron1="%${MARCADOR_PAN}%" -v patron2="%${MARCADOR_CVV}%" -v patron3="%${MARCADOR_VENC}%" \
        < "${DIR_SCRIPT}/barrido-columnas.sql" > "$archivo" 2>"${archivo}.err"; then
      rm -f "${archivo}.err"
      RESULTADOS_BARRIDO_ARCHIVOS+=("${etiqueta_contenedor}|${base}|${archivo}")
    else
      echo "ADVERTENCIA: barrido de ${etiqueta_contenedor}/${base} fallo:" >&2
      sed 's/^/    /' "${archivo}.err" >&2
      rm -f "${archivo}.err"
      return 1
    fi
  done
  return 0
}

barrer_contenedor db-operacional "$USUARIO_OPERACIONAL" "$CLAVE_OPERACIONAL" operacional || { BARRIDO_OK=0; FALLAS_EJECUCION+=("barrido de db-operacional"); }
barrer_contenedor db-finanzas "$USUARIO_FINANZAS" "$CLAVE_FINANZAS" finanzas || { BARRIDO_OK=0; FALLAS_EJECUCION+=("barrido de db-finanzas"); }
barrer_contenedor db-auditoria "$USUARIO_AUDITORIA_OWNER" "$CLAVE_AUDITORIA_OWNER" auditoria || { BARRIDO_OK=0; FALLAS_EJECUCION+=("barrido de db-auditoria"); }

# --- 1.2 Interpretar el barrido ---------------------------------------------
CONTROL_POSITIVO_ORDENES=0
CONTROL_POSITIVO_AUDITORIA=0
FUGA_EN_FINANZAS=0
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
    if [ "$base" = "sgo_pagos" ] || [ "$base" = "sgo_facturacion" ]; then
      FUGA_EN_FINANZAS=1
      HALLAZGOS_BLOQUEANTES+=("Marcador de prueba encontrado en ${base}.${esquema}.${tabla}.${columna} (PAN=${c1} CVV=${c2} VENC=${c3}) — el contexto de Pagos/Facturacion no deberia ver texto libre de la orden")
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

# --- 1.4 Confirmar que lo permitido (token, ultimos 4, marca) es lo que
# efectivamente quedo para el intento generado por esta corrida.
if [ "$CANARIO_OK" -eq 1 ]; then
  consulta_intento_canario() {
    psql_en db-finanzas "$USUARIO_FINANZAS" "$CLAVE_FINANZAS" sgo_pagos -c "
      SELECT i.token_medio_pago, i.ultimos_cuatro, i.marca, i.estado
      FROM pagos.intento_pago i JOIN pagos.pago p ON p.id = i.pago_id
      WHERE p.orden_id = '${ORDEN_ID}'
      ORDER BY i.intento_numero DESC LIMIT 1;
    " | tr -d '\r'
  }
  ejecutar consulta_intento_canario "$TMP_DIR/intento_canario.txt" "intento de pago de la orden canario" || true
  echo "Intento de pago de la orden canario (token|ultimos_cuatro|marca|estado): $(cat "$TMP_DIR/intento_canario.txt")"
fi

# --- 1.5 Barrido de logs de TODO el stack -----------------------------------
consulta_logs() {
  compose logs --no-color 2>/dev/null | grep -F -i -e "$MARCADOR_PAN" -e "$MARCADOR_CVV" -e "$MARCADOR_VENC" -e "$PAN_PRUEBA"
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

curl -sf -X POST "${GATEWAY}/api/auth/registro" -H "Content-Type: application/json" \
  -d "{\"email\":\"${EMAIL_PRUEBA}\",\"contrasena\":\"${CONTRASENA_PRUEBA}\",\"nombreCompleto\":\"Prueba Seguridad\"}" \
  > /dev/null || LOGIN_OK=0

if [ "$LOGIN_OK" -eq 1 ]; then
  sleep 1
  CODIGO_VERIF=""
  for _ in 1 2 3 4 5; do
    CODIGO_VERIF=$(compose logs --no-color auth-api 2>/dev/null \
      | grep "Codigo de verificacion para ${EMAIL_PRUEBA}" \
      | tail -n1 \
      | sed -n 's/.*: \([0-9]\{4,8\}\)[ \t]*$/\1/p')
    [ -n "$CODIGO_VERIF" ] && break
    sleep 1
  done
  [ -z "$CODIGO_VERIF" ] && LOGIN_OK=0
fi

if [ "$LOGIN_OK" -eq 1 ]; then
  curl -sf -X POST "${GATEWAY}/api/auth/verificar" -H "Content-Type: application/json" \
    -d "{\"email\":\"${EMAIL_PRUEBA}\",\"codigo\":\"${CODIGO_VERIF}\"}" > /dev/null || LOGIN_OK=0
fi

if [ "$LOGIN_OK" -eq 1 ]; then
  RESP_LOGIN=$(curl -sf -X POST "${GATEWAY}/api/auth/login" -H "Content-Type: application/json" \
    -d "{\"email\":\"${EMAIL_PRUEBA}\",\"contrasena\":\"${CONTRASENA_PRUEBA}\"}") || LOGIN_OK=0
  if [ "$LOGIN_OK" -eq 1 ]; then
    TOKEN_VALIDO=$(extraer_campo "$RESP_LOGIN" "token")
    [ -z "$TOKEN_VALIDO" ] && LOGIN_OK=0
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

consulta_update_restringido() {
  psql_en db-auditoria "$USUARIO_AUDITORIA_APP" "$CLAVE_AUDITORIA_APP" sgo_auditoria -c "
    UPDATE eventos_auditoria SET event_type = event_type
    WHERE id = (SELECT id FROM eventos_auditoria ORDER BY fecha_recepcion DESC LIMIT 1);
  " 2>&1
}
SALIDA_UPDATE_RESTRINGIDO="$(consulta_update_restringido)"
if echo "$SALIDA_UPDATE_RESTRINGIDO" | grep -q "permission denied\|42501"; then
  echo "UPDATE con usuario restringido (${USUARIO_AUDITORIA_APP}) -> bloqueado por permisos (42501), como se esperaba."
else
  HALLAZGOS_BLOQUEANTES+=("UPDATE sobre eventos_auditoria con el usuario restringido de la aplicacion NO fue bloqueado por permisos: ${SALIDA_UPDATE_RESTRINGIDO}")
fi

consulta_delete_restringido() {
  psql_en db-auditoria "$USUARIO_AUDITORIA_APP" "$CLAVE_AUDITORIA_APP" sgo_auditoria -c "
    DELETE FROM eventos_auditoria
    WHERE id = (SELECT id FROM eventos_auditoria ORDER BY fecha_recepcion DESC LIMIT 1);
  " 2>&1
}
SALIDA_DELETE_RESTRINGIDO="$(consulta_delete_restringido)"
if echo "$SALIDA_DELETE_RESTRINGIDO" | grep -q "permission denied\|42501"; then
  echo "DELETE con usuario restringido (${USUARIO_AUDITORIA_APP}) -> bloqueado por permisos (42501), como se esperaba."
else
  HALLAZGOS_BLOQUEANTES+=("DELETE sobre eventos_auditoria con el usuario restringido de la aplicacion NO fue bloqueado por permisos: ${SALIDA_DELETE_RESTRINGIDO}")
fi

consulta_update_dueno() {
  psql_en db-auditoria "$USUARIO_AUDITORIA_OWNER" "$CLAVE_AUDITORIA_OWNER" sgo_auditoria -c "
    UPDATE eventos_auditoria SET event_type = event_type
    WHERE id = (SELECT id FROM eventos_auditoria ORDER BY fecha_recepcion DESC LIMIT 1);
  " 2>&1
}
SALIDA_UPDATE_DUENO="$(consulta_update_dueno)"
if echo "$SALIDA_UPDATE_DUENO" | grep -qi "solo insercion\|bitacora inmutable\|P0001"; then
  echo "UPDATE con usuario dueno (${USUARIO_AUDITORIA_OWNER}) -> bloqueado por el trigger (P0001), como se esperaba."
else
  HALLAZGOS_BLOQUEANTES+=("UPDATE sobre eventos_auditoria con el usuario DUENO de la tabla NO fue bloqueado por el trigger: ${SALIDA_UPDATE_DUENO}")
fi

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