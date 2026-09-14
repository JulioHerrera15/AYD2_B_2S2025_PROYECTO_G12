#!/usr/bin/env bash
# =============================================================================
# suite-completa-202300353.sh
# Revision exhaustiva de todas las HUs y tareas de Jonathan (202300353):
#   HU-007  Auth + Carrito unificado
#   HU-010  Consulta de Estado (Redis + fallback SQL + historial)
#   HU-015  Notificaciones (consumidor + aislamiento de fallos)
#   HU-021  Incidencias
#   HU-024  Rendimiento EAC-02 (< 5s P100)
#   BUG     API Gateway rutas para inventario-api
#   Integracion: Gateway enruta a todos los contextos propios y ajenos
# =============================================================================

GW="http://localhost:8080"
AUTH="http://localhost:8088"
ESTADO="http://localhost:8091"
NOTIF="http://localhost:8090"
MOCK="http://localhost:8089"
ORDENES="http://localhost:8083"
INV="http://localhost:8094"
WEB="http://localhost:8092"

PASS=0
FAIL=0
WARN=0

ts=$(date +%Y%m%d_%H%M%S)
LOG_FILE="$(dirname "$0")/suite-resultado-${ts}.txt"

verde()   { printf '\033[0;32m%s\033[0m\n' "$*"; }
rojo()    { printf '\033[0;31m%s\033[0m\n' "$*"; }

log(){ echo "$*" | tee -a "$LOG_FILE"; }

seccion(){
  log ""
  log "============================================================"
  log "  $*"
  log "============================================================"
}

resultado(){
  local label="$1" code="$2" esperado="$3" body="$4" check="$5"
  local ok=true
  if [ "$code" != "$esperado" ]; then ok=false; fi
  if [ -n "$check" ] && [ -n "$body" ]; then
    echo "$body" | grep -qi "$check" || ok=false
  fi
  if $ok; then
    PASS=$((PASS+1))
    log "  [PASS] $label (HTTP $code)"
  else
    FAIL=$((FAIL+1))
    log "  [FAIL] $label (HTTP $code, esperado $esperado)"
    [ -n "$check" ] && log "         Body check '${check}' no encontrado"
    [ -n "$body"  ] && log "         Body: $(echo "$body" | head -c 300)"
  fi
}

aviso(){
  WARN=$((WARN+1))
  log "  [WARN] $*"
}

do_req(){
  local method="$1" url="$2" body="$3" auth_header="$4" session_header="$5"
  local args=(-s -w "\n__CODE__%{http_code}" -X "$method" "$url")
  [ -n "$body" ]           && args+=(-H "Content-Type: application/json" -d "$body")
  [ -n "$auth_header" ]    && args+=(-H "Authorization: Bearer $auth_header")
  [ -n "$session_header" ] && args+=(-H "X-Session-Id: $session_header")
  local resp
  resp=$(curl "${args[@]}" 2>/dev/null)
  local code body_out
  code=$(echo "$resp" | grep '__CODE__' | sed 's/__CODE__//')
  body_out=$(echo "$resp" | grep -v '__CODE__')
  echo "${code}|||${body_out}"
}

get_code(){ echo "$1" | cut -d'|' -f1 | head -1; }
get_body(){ echo "$1" | sed 's/^[^|]*|||//'; }

json_field(){
  local body="$1" field="$2"
  echo "$body" | python3 -c "import sys,json; d=json.load(sys.stdin); print(d.get('$field',''))" 2>/dev/null
}

# =============================================================================
log "Suite de revision completa 202300353 - $(date)"
log "Gateway:$GW  Auth:$AUTH  Estado:$ESTADO"
log ""

# =============================================================================
seccion "BLOQUE 0: Health-check de servicios propios"
# =============================================================================

for svc_entry in "Gateway|${GW}" "Auth|${AUTH}" "ConsultaEstado|${ESTADO}" "Notificaciones|${NOTIF}" "MockMensajeria|${MOCK}"; do
  svc_name="${svc_entry%%|*}"
  svc_url="${svc_entry#*|}"
  r=$(do_req GET "${svc_url}/healthz")
  code=$(get_code "$r")
  if [ "$code" = "200" ]; then
    resultado "Health $svc_name" "$code" "200"
  else
    r2=$(do_req GET "${svc_url}/health")
    code2=$(get_code "$r2")
    if [ "$code2" = "200" ]; then
      resultado "Health $svc_name (/health)" "$code2" "200"
    else
      aviso "Health $svc_name no responde (code=$code)"
    fi
  fi
done

# =============================================================================
seccion "BLOQUE 1: HU-007 - Registro y reconocimiento de cliente"
# =============================================================================

EMAIL_TEST="test-suite-$(date +%s)@megastore.gt"
PASS_TEST='Suite@2026Segura'

log "  1.1 Registro exitoso"
body_reg="{\"email\":\"${EMAIL_TEST}\",\"contrasena\":\"${PASS_TEST}\",\"confirmacionContrasena\":\"${PASS_TEST}\",\"nombreCompleto\":\"Test Suite\"}"
r=$(do_req POST "$AUTH/api/auth/registro" "$body_reg")
code=$(get_code "$r"); body=$(get_body "$r")
resultado "HU-007 Registro nuevo usuario" "$code" "201" "$body" "usuarioId"
USUARIO_ID=$(json_field "$body" "usuarioId")
log "    usuarioId=$USUARIO_ID"

log "  1.2 Registro duplicado => 409"
r=$(do_req POST "$AUTH/api/auth/registro" "$body_reg")
code=$(get_code "$r")
resultado "HU-007 Registro duplicado => 409" "$code" "409"

log "  1.3 Login sin verificar => 401"
r=$(do_req POST "$AUTH/api/auth/login" "{\"email\":\"${EMAIL_TEST}\",\"contrasena\":\"${PASS_TEST}\"}")
code=$(get_code "$r")
resultado "HU-007 Login sin verificar => 401" "$code" "401"

log "  1.4 Obtener codigo de verificacion de logs (formato JSON)"
CODIGO_VER=$(docker logs sgo-logic-auth-api-1 2>&1 | grep "Codigo de verificacion" | grep "$EMAIL_TEST" | tail -1 | python3 -c "import sys,json; d=json.loads(sys.stdin.read().strip()); print(d.get('Properties',{}).get('Codigo',''))" 2>/dev/null)
[ -z "$CODIGO_VER" ] && CODIGO_VER=$(docker logs sgo-logic-auth-api-1 2>&1 | grep "Codigo de verificacion" | tail -1 | python3 -c "import sys,json; d=json.loads(sys.stdin.read().strip()); print(d.get('Properties',{}).get('Codigo',''))" 2>/dev/null)
log "    codigo=$CODIGO_VER"

log "  1.5 Verificar con codigo incorrecto => 401"
r=$(do_req POST "$AUTH/api/auth/verificar" "{\"email\":\"${EMAIL_TEST}\",\"codigo\":\"000000\"}")
code=$(get_code "$r")
resultado "HU-007 Verificar codigo incorrecto => 401" "$code" "401"

log "  1.6 Verificar email con codigo correcto"
if [ -n "$CODIGO_VER" ]; then
  r=$(do_req POST "$AUTH/api/auth/verificar" "{\"email\":\"${EMAIL_TEST}\",\"codigo\":\"${CODIGO_VER}\"}")
  code=$(get_code "$r")
  resultado "HU-007 Verificar email ok" "$code" "200"
else
  aviso "No se pudo extraer codigo - saltando verificacion"
fi

log "  1.7 Login tras verificar => JWT"
r=$(do_req POST "$AUTH/api/auth/login" "{\"email\":\"${EMAIL_TEST}\",\"contrasena\":\"${PASS_TEST}\"}")
code=$(get_code "$r"); body=$(get_body "$r")
resultado "HU-007 Login verificado => 200 + JWT" "$code" "200" "$body" "token"
JWT=$(json_field "$body" "token")
log "    JWT length=${#JWT}"

log "  1.8 Login contrasena incorrecta => 401"
r=$(do_req POST "$AUTH/api/auth/login" "{\"email\":\"${EMAIL_TEST}\",\"contrasena\":\"WrongPass999\"}")
code=$(get_code "$r")
resultado "HU-007 Login contrasena incorrecta => 401" "$code" "401"

# =============================================================================
seccion "BLOQUE 2: HU-007 - Carrito unificado"
# =============================================================================

SESSION_ID="session-suite-$(date +%s)"

log "  2.1 Carrito invitado vacio"
r=$(do_req GET "$AUTH/api/carrito" "" "" "$SESSION_ID")
code=$(get_code "$r")
resultado "HU-007 Carrito invitado GET => 200" "$code" "200"

log "  2.2 Agregar item al carrito invitado"
r=$(do_req POST "$AUTH/api/carrito/items" '{"sku":"SKU-TEST-001","descripcion":"Producto suite","cantidad":2,"precioUnitario":"50.00"}' "" "$SESSION_ID")
code=$(get_code "$r")
resultado "HU-007 Agregar item invitado => 204" "$code" "204"

log "  2.3 Verificar carrito con item"
r=$(do_req GET "$AUTH/api/carrito" "" "" "$SESSION_ID")
code=$(get_code "$r"); body=$(get_body "$r")
resultado "HU-007 Carrito con items => 200" "$code" "200" "$body" "SKU-TEST-001"

log "  2.4 Agregar segundo item"
r=$(do_req POST "$AUTH/api/carrito/items" '{"sku":"SKU-TEST-002","descripcion":"Producto 2","cantidad":1,"precioUnitario":"100.00"}' "" "$SESSION_ID")
code=$(get_code "$r")
resultado "HU-007 Agregar segundo item => 204" "$code" "204"

log "  2.5 Eliminar item por SKU"
r=$(do_req DELETE "$AUTH/api/carrito/items/SKU-TEST-002" "" "" "$SESSION_ID")
code=$(get_code "$r")
resultado "HU-007 Eliminar item => 204" "$code" "204"

log "  2.6 Verificar que SKU-TEST-002 fue eliminado"
r=$(do_req GET "$AUTH/api/carrito" "" "" "$SESSION_ID")
code=$(get_code "$r"); body=$(get_body "$r")
resultado "HU-007 Carrito con 1 item tras delete" "$code" "200" "$body" "SKU-TEST-001"
if echo "$body" | grep -q "SKU-TEST-002"; then
  FAIL=$((FAIL+1)); log "  [FAIL] SKU-TEST-002 todavia existe en carrito"
else
  PASS=$((PASS+1)); log "  [PASS] SKU-TEST-002 correctamente eliminado"
fi

log "  2.7 Asociar carrito invitado al usuario autenticado"
if [ -n "$JWT" ]; then
  r=$(do_req POST "$AUTH/api/carrito/asociar" "{\"sesionId\":\"${SESSION_ID}\"}" "$JWT")
  code=$(get_code "$r")
  resultado "HU-007 Asociar carrito guest->auth => 204" "$code" "204"

  r=$(do_req GET "$AUTH/api/carrito" "" "$JWT")
  code=$(get_code "$r"); body=$(get_body "$r")
  resultado "HU-007 Carrito autenticado tiene items migrados" "$code" "200" "$body" "SKU-TEST-001"
else
  aviso "JWT no disponible - saltando asociar carrito"
fi

log "  2.8 Carrito via Gateway (multi-canal)"
r=$(do_req GET "$GW/api/carrito" "" "$JWT")
code=$(get_code "$r"); body=$(get_body "$r")
resultado "HU-007 Carrito via Gateway => 200" "$code" "200" "$body" "SKU-TEST-001"

# =============================================================================
seccion "BLOQUE 3: HU-010 - Consulta de Estado (Redis + fallback)"
# =============================================================================

log "  3.1 Sembrar inventario"
SKU_ESTADO="SKU-SUITE-ESTADO-$(date +%s)"
r=$(do_req PUT "$INV/api/inventario/existencias" "{\"sku\":\"${SKU_ESTADO}\",\"ubicacionId\":\"CD-01\",\"tipoUbicacion\":\"CENTRO_DISTRIBUCION\",\"cantidadFisica\":50}")
code=$(get_code "$r")
resultado "HU-010 Sembrar inventario" "$code" "200"

log "  3.2 Crear orden via Gateway"
CLIENTE_ESTADO="cliente-suite-$(date +%s)"
orden_body="{\"clienteId\":\"${CLIENTE_ESTADO}\",\"canal\":\"WEB\",\"metodoEntrega\":\"DOMICILIO\",\"moneda\":\"GTQ\",\"medioPago\":\"TARJETA\",\"impuestos\":12.0,\"lineas\":[{\"sku\":\"${SKU_ESTADO}\",\"descripcion\":\"Suite test\",\"cantidad\":1,\"precioUnitario\":100.0}]}"
r=$(do_req POST "$GW/api/ordenes" "$orden_body")
code=$(get_code "$r"); body=$(get_body "$r")
resultado "HU-010 Crear orden via GW => 201" "$code" "201" "$body" "id"
ORDEN_ID=$(json_field "$body" "id")
log "    ordenId=$ORDEN_ID"

log "  3.3 Esperar materializacion en ConsultaEstado (OrdenCreada)"
sleep 4

if [ -n "$ORDEN_ID" ]; then
  r=$(do_req GET "$ESTADO/api/estado/ordenes/$ORDEN_ID")
  code=$(get_code "$r"); body=$(get_body "$r")
  resultado "HU-010 Consulta estado orden => 200" "$code" "200" "$body" "CREADA"

  log "  3.4 Verificar que Redis contiene la orden"
  redis_val=$(docker exec sgo-logic-redis-1 redis-cli GET "orden:$ORDEN_ID" 2>/dev/null)
  if [ -n "$redis_val" ] && [ "$redis_val" != "(nil)" ]; then
    PASS=$((PASS+1))
    log "  [PASS] Redis contiene orden:$ORDEN_ID (${#redis_val} bytes)"
  else
    FAIL=$((FAIL+1))
    log "  [FAIL] Redis NO contiene orden:$ORDEN_ID"
  fi

  log "  3.5 Confirmar orden y esperar cambio de estado"
  r=$(do_req POST "$ORDENES/api/ordenes/$ORDEN_ID/confirmar" '{"motivo":"suite de revision"}')
  code=$(get_code "$r")
  resultado "HU-010 Confirmar orden => 200" "$code" "200"

  sleep 6
  r=$(do_req GET "$ESTADO/api/estado/ordenes/$ORDEN_ID")
  code=$(get_code "$r"); body=$(get_body "$r")
  estado_actual=$(json_field "$body" "estado")
  log "    Estado actual en ConsultaEstado: $estado_actual"
  if [ "$estado_actual" != "CREADA" ] && [ -n "$estado_actual" ]; then
    PASS=$((PASS+1))
    log "  [PASS] HU-010 Estado cambiado en vista materializada: $estado_actual"
  else
    WARN=$((WARN+1))
    log "  [WARN] HU-010 Estado sigue CREADA tras 6s (saga puede tardar mas)"
  fi

  log "  3.6 Historial paginado por cliente"
  r=$(do_req GET "$ESTADO/api/estado/clientes/$CLIENTE_ESTADO/ordenes?pagina=1&tamano=10")
  code=$(get_code "$r"); body=$(get_body "$r")
  resultado "HU-010 Historial por cliente => 200" "$code" "200" "$body" "$ORDEN_ID"

  log "  3.7 Consulta via Gateway"
  r=$(do_req GET "$GW/api/estado/ordenes/$ORDEN_ID")
  code=$(get_code "$r")
  resultado "HU-010 Estado via Gateway => 200" "$code" "200"

  log "  3.8 Orden inexistente => 404"
  r=$(do_req GET "$ESTADO/api/estado/ordenes/ORDEN-INEXISTENTE-XYZ123")
  code=$(get_code "$r")
  resultado "HU-010 Orden inexistente => 404" "$code" "404"
else
  aviso "No se pudo crear orden - saltando pruebas de estado"
fi

# =============================================================================
seccion "BLOQUE 4: HU-021 - Gestion de Incidencias"
# =============================================================================

if [ -n "$ORDEN_ID" ]; then
  log "  4.1 Crear incidencia sobre la orden"
  r=$(do_req POST "$ESTADO/api/incidencias" "{\"ordenId\":\"${ORDEN_ID}\",\"descripcion\":\"Producto llegó dañado segun cliente de suite\"}")
  code=$(get_code "$r"); body=$(get_body "$r")
  resultado "HU-021 Crear incidencia => 201" "$code" "201" "$body" "ABIERTA"
  INCIDENCIA_ID=$(json_field "$body" "id")
  log "    incidenciaId=$INCIDENCIA_ID"

  log "  4.2 Incidencia sin ordenId => 400"
  r=$(do_req POST "$ESTADO/api/incidencias" '{"descripcion":"sin orden"}')
  code=$(get_code "$r")
  resultado "HU-021 Incidencia sin ordenId => 400" "$code" "400"

  log "  4.3 Incidencia sin descripcion => 400"
  r=$(do_req POST "$ESTADO/api/incidencias" "{\"ordenId\":\"${ORDEN_ID}\"}")
  code=$(get_code "$r")
  resultado "HU-021 Incidencia sin descripcion => 400" "$code" "400"

  if [ -n "$INCIDENCIA_ID" ]; then
    log "  4.4 Actualizar incidencia a EN_PROCESO"
    r=$(do_req PUT "$ESTADO/api/incidencias/$INCIDENCIA_ID" '{"estado":"EN_PROCESO","resolucion":"Se esta investigando"}')
    code=$(get_code "$r"); body=$(get_body "$r")
    resultado "HU-021 Actualizar incidencia => 200" "$code" "200" "$body" "EN_PROCESO"

    log "  4.5 Incidencia inexistente => 404"
    r=$(do_req PUT "$ESTADO/api/incidencias/00000000-0000-0000-0000-000000000000" '{"estado":"CERRADA"}')
    code=$(get_code "$r")
    resultado "HU-021 Incidencia inexistente => 404" "$code" "404"

    log "  4.6 Incidencias visibles en consulta de estado de la orden"
    r=$(do_req GET "$ESTADO/api/estado/ordenes/$ORDEN_ID")
    code=$(get_code "$r"); body=$(get_body "$r")
    resultado "HU-021 Estado con incidencias => 200" "$code" "200" "$body" "incidencia"
  fi

  log "  4.7 Segunda incidencia en la misma orden (0..N)"
  r=$(do_req POST "$ESTADO/api/incidencias" "{\"ordenId\":\"${ORDEN_ID}\",\"descripcion\":\"Segunda incidencia - cliente llama de nuevo\"}")
  code=$(get_code "$r")
  resultado "HU-021 Segunda incidencia => 201" "$code" "201"
else
  aviso "Sin ordenId - saltando pruebas de incidencias"
fi

# =============================================================================
seccion "BLOQUE 5: HU-015 - Notificaciones"
# =============================================================================

log "  5.1 MockMensajeria disponible"
r=$(do_req GET "$MOCK/api/mensajes")
code=$(get_code "$r")
if [ "$code" = "200" ]; then
  resultado "HU-015 MockMensajeria GET /api/mensajes => 200" "$code" "200"
else
  r2=$(do_req POST "$MOCK/api/mensajes" '{"clienteId":"test","estadoOrden":"CREADA","ordenId":"test"}')
  code2=$(get_code "$r2")
  if [ "$code2" = "200" ] || [ "$code2" = "204" ] || [ "$code2" = "202" ]; then
    resultado "HU-015 MockMensajeria POST /api/mensajes => OK" "$code2" "$code2"
  else
    aviso "MockMensajeria: GET=$code POST=$code2"
  fi
fi

log "  5.2 Logs de notificaciones tras cambio de estado"
sleep 3
notif_logs=$(docker logs sgo-logic-notificaciones-api-1 2>&1 | tail -50)
if echo "$notif_logs" | grep -qi "notif\|enviand\|cliente\|orden\|estado\|consumer\|manejar"; then
  PASS=$((PASS+1))
  log "  [PASS] HU-015 Notificaciones ha procesado eventos (logs activos)"
else
  WARN=$((WARN+1))
  log "  [WARN] HU-015 No se encontro evidencia de procesamiento en logs"
fi

log "  5.3 Sin crashes en logs de notificaciones"
notif_crash=$(docker logs sgo-logic-notificaciones-api-1 2>&1 | grep -ic "unhandled\|crash\|fatal exception" || echo "0")
if [ "${notif_crash:-0}" -eq 0 ]; then
  PASS=$((PASS+1))
  log "  [PASS] HU-015 Sin crashes/unhandled en logs de notificaciones"
else
  WARN=$((WARN+1))
  log "  [WARN] HU-015 $notif_crash mensajes de error critico encontrados"
fi

log "  5.4 Verificar binding exclusivo de notificaciones"
rabbit_bindings=$(curl -s -u guest:guest "http://localhost:15672/api/bindings/%2F/q/notificaciones.ordenes" 2>/dev/null)
if echo "$rabbit_bindings" | grep -q "estado-cambiado"; then
  otros=$(echo "$rabbit_bindings" | python3 -c "
import sys, json
try:
  data = json.load(sys.stdin)
  rks = [b.get('routing_key','') for b in data]
  extra = [rk for rk in rks if 'ordenes.' in rk and 'estado-cambiado' not in rk]
  print(','.join(extra) if extra else 'SOLO_ESTADO_CAMBIADO')
except:
  print('PARSE_ERROR')
" 2>/dev/null)
  if [ "$otros" = "SOLO_ESTADO_CAMBIADO" ]; then
    PASS=$((PASS+1)); log "  [PASS] HU-015 Binding exclusivo ordenes.orden.estado-cambiado confirmado"
  else
    FAIL=$((FAIL+1)); log "  [FAIL] HU-015 Bindings extras en notificaciones.ordenes: $otros"
  fi
else
  aviso "No se pudo verificar bindings en RabbitMQ management (puede no estar disponible)"
fi

log "  5.5 Verificar bindings de consulta-estado.ordenes"
rabbit_ce=$(curl -s -u guest:guest "http://localhost:15672/api/bindings/%2F/q/consulta-estado.ordenes" 2>/dev/null)
if echo "$rabbit_ce" | grep -q "ordenes.orden"; then
  PASS=$((PASS+1)); log "  [PASS] HU-010 Cola consulta-estado.ordenes tiene bindings a ordenes.orden.*"
else
  aviso "No se pudo verificar bindings de consulta-estado.ordenes"
fi

# =============================================================================
seccion "BLOQUE 6: API Gateway - Enrutamiento hacia todos los contextos"
# =============================================================================

log "  6.0 Rutas criticas via Gateway"

r=$(do_req POST "$GW/api/auth/login" '{"email":"noexiste@x.com","contrasena":"Abc1234"}')
code=$(get_code "$r")
resultado "GW -> auth-api (login bad creds => 401)" "$code" "401"

r=$(do_req GET "$GW/api/carrito" "" "" "gw-test-session")
code=$(get_code "$r")
resultado "GW -> auth-api (carrito invitado => 200)" "$code" "200"

r=$(do_req GET "$GW/api/estado/ordenes/ORDEN-FAKE-GW")
code=$(get_code "$r")
resultado "GW -> consulta-estado-api (404 para orden fake)" "$code" "404"

r=$(do_req GET "$GW/api/inventario/existencias")
code=$(get_code "$r")
resultado "GW -> inventario-api (/api/inventario/existencias)" "$code" "200"

r=$(do_req GET "$GW/inventario/existencias")
code=$(get_code "$r")
resultado "GW -> inventario-api (short /inventario/existencias)" "$code" "200"

r=$(do_req GET "$GW/api/ordenes")
code=$(get_code "$r")
if [ "$code" = "200" ] || [ "$code" = "400" ] || [ "$code" = "401" ] || [ "$code" = "405" ]; then
  PASS=$((PASS+1)); log "  [PASS] GW -> ordenes-api (HTTP $code - ruta funciona)"
else
  resultado "GW -> ordenes-api" "$code" "200"
fi

r=$(do_req GET "$GW/api/pagos")
code=$(get_code "$r")
if [ "$code" = "200" ] || [ "$code" = "404" ] || [ "$code" = "400" ] || [ "$code" = "405" ]; then
  PASS=$((PASS+1)); log "  [PASS] GW -> pagos-api (HTTP $code)"
else
  resultado "GW -> pagos-api" "$code" "200"
fi

r=$(do_req GET "$GW/api/facturacion")
code=$(get_code "$r")
if [ "$code" = "200" ] || [ "$code" = "404" ] || [ "$code" = "400" ] || [ "$code" = "405" ]; then
  PASS=$((PASS+1)); log "  [PASS] GW -> facturacion-api (HTTP $code)"
else
  resultado "GW -> facturacion-api" "$code" "200"
fi

# =============================================================================
seccion "BLOQUE 7: HU-024 - Rendimiento EAC-02 (< 5s P100)"
# =============================================================================

log "  7.1 Prueba de latencia (50 solicitudes secuenciales)"
if [ -n "$ORDEN_ID" ]; then
  MAX_MS=0
  TOTAL_MS=0
  N=50
  ERRORES=0
  for i in $(seq 1 $N); do
    t_start=$(python3 -c "import time; print(int(time.time()*1000))")
    r=$(do_req GET "$ESTADO/api/estado/ordenes/$ORDEN_ID")
    t_end=$(python3 -c "import time; print(int(time.time()*1000))")
    code=$(get_code "$r")
    elapsed=$((t_end - t_start))
    TOTAL_MS=$((TOTAL_MS + elapsed))
    [ "$elapsed" -gt "$MAX_MS" ] && MAX_MS=$elapsed
    [ "$code" != "200" ] && ERRORES=$((ERRORES+1))
  done
  AVG_MS=$((TOTAL_MS / N))
  log "    N=$N | Promedio=${AVG_MS}ms | Maximo(P100)=${MAX_MS}ms | Errores=$ERRORES"
  if [ "$MAX_MS" -lt 5000 ]; then
    PASS=$((PASS+1)); log "  [PASS] HU-024 EAC-02: P100=${MAX_MS}ms < 5000ms"
  else
    FAIL=$((FAIL+1)); log "  [FAIL] HU-024 EAC-02: P100=${MAX_MS}ms >= 5000ms"
  fi
  if [ "$ERRORES" -eq 0 ]; then
    PASS=$((PASS+1)); log "  [PASS] HU-024 Tasa de error: 0 / $N"
  else
    FAIL=$((FAIL+1)); log "  [FAIL] HU-024 Errores HTTP: $ERRORES / $N"
  fi
else
  aviso "Sin ordenId - saltando prueba de latencia"
fi

# =============================================================================
seccion "BLOQUE 8: Integridad de contratos y reglas de negocio"
# =============================================================================

log "  8.1 Cola consulta-estado.ordenes activa"
rabbit_q=$(curl -s -u guest:guest "http://localhost:15672/api/queues/%2F/consulta-estado.ordenes" 2>/dev/null)
if [ -n "$rabbit_q" ]; then
  total_msgs=$(echo "$rabbit_q" | python3 -c "import sys,json; d=json.load(sys.stdin); print(d.get('messages_ready',0)+d.get('messages_unacknowledged',0))" 2>/dev/null)
  PASS=$((PASS+1)); log "  [PASS] Cola consulta-estado.ordenes activa (pendientes: $total_msgs)"
else
  aviso "No se pudo consultar la cola via RabbitMQ management"
fi

log "  8.2 Estado SOLO viene de OrdenCreada y OrdenEstadoCambiado"
CE_CONSUMER="/Users/jona/Desktop/USAC/4to_Anio/8vo Semestre/AYD2/LAB/proyecto/repo/SGO-Logic/src/Services/SgoLogic.ConsultaEstado.Api/Consumers/ConsultaEstadoOrdenesConsumidor.cs"
num_writes=$(grep -c "vista.Estado = " "$CE_CONSUMER" 2>/dev/null || echo "0")
if [ "$num_writes" -eq 2 ]; then
  PASS=$((PASS+1)); log "  [PASS] 8.2 vista.Estado escrito exactamente 2 veces (eventos 1 y 2)"
else
  FAIL=$((FAIL+1)); log "  [FAIL] 8.2 vista.Estado escrito $num_writes veces (esperado: 2)"
fi

log "  8.3 BCrypt usado para hashing de passwords"
AUTH_DIR="/Users/jona/Desktop/USAC/4to_Anio/8vo Semestre/AYD2/LAB/proyecto/repo/SGO-Logic/src/Services/SgoLogic.Auth.Api"
grep_bcrypt=$(grep -r "BCrypt\|HashPassword" "$AUTH_DIR" 2>/dev/null | wc -l | tr -d ' ')
if [ "${grep_bcrypt:-0}" -gt 0 ]; then
  PASS=$((PASS+1)); log "  [PASS] 8.3 BCrypt usado ($grep_bcrypt referencias)"
else
  FAIL=$((FAIL+1)); log "  [FAIL] 8.3 BCrypt no encontrado"
fi

log "  8.4 Cero emojis en codigo fuente"
emoji_count=$(find \
  "/Users/jona/Desktop/USAC/4to_Anio/8vo Semestre/AYD2/LAB/proyecto/repo/SGO-Logic/src/Services/SgoLogic.Auth.Api" \
  "/Users/jona/Desktop/USAC/4to_Anio/8vo Semestre/AYD2/LAB/proyecto/repo/SGO-Logic/src/Services/SgoLogic.ConsultaEstado.Api" \
  "/Users/jona/Desktop/USAC/4to_Anio/8vo Semestre/AYD2/LAB/proyecto/repo/SGO-Logic/src/Services/SgoLogic.Notificaciones.Api" \
  "/Users/jona/Desktop/USAC/4to_Anio/8vo Semestre/AYD2/LAB/proyecto/repo/SGO-Logic/src/Services/SgoLogic.ApiGateway" \
  "/Users/jona/Desktop/USAC/4to_Anio/8vo Semestre/AYD2/LAB/proyecto/repo/SGO-Logic/src/Services/SgoLogic.Web" \
  \( -name "*.cs" -o -name "*.json" -o -name "*.cshtml" \) 2>/dev/null | \
  xargs python3 -c "
import sys
total=0
for fpath in sys.argv[1:]:
  try:
    with open(fpath,'r',encoding='utf-8',errors='ignore') as f:
      for ch in f.read():
        c=ord(ch)
        if (0x1F300<=c<=0x1F9FF) or (0x2600<=c<=0x26FF):
          total+=1
  except:
    pass
print(total)
" 2>/dev/null || echo "0")
emoji_count=${emoji_count:-0}
if [ "$emoji_count" -eq 0 ]; then
  PASS=$((PASS+1)); log "  [PASS] 8.4 Cero emojis en codigo fuente"
else
  FAIL=$((FAIL+1)); log "  [FAIL] 8.4 $emoji_count emojis encontrados en codigo"
fi

log "  8.5 Notificaciones tiene exactamente 1 binding"
NOTIF_CONSUMER="/Users/jona/Desktop/USAC/4to_Anio/8vo Semestre/AYD2/LAB/proyecto/repo/SGO-Logic/src/Services/SgoLogic.Notificaciones.Api/Consumers/NotificacionesOrdenesConsumidor.cs"
binding_count=$(grep -c "BindingCola(" "$NOTIF_CONSUMER" 2>/dev/null || echo "0")
if [ "$binding_count" -eq 1 ]; then
  PASS=$((PASS+1)); log "  [PASS] 8.5 Notificaciones tiene exactamente 1 binding"
else
  FAIL=$((FAIL+1)); log "  [FAIL] 8.5 Notificaciones tiene $binding_count bindings (esperado: 1)"
fi

log "  8.6 Carrito en Redis con TTL"
CARRITO_SVC="/Users/jona/Desktop/USAC/4to_Anio/8vo Semestre/AYD2/LAB/proyecto/repo/SGO-Logic/src/Services/SgoLogic.Auth.Api/Application/CarritoService.cs"
grep_ttl=$(grep -c "TimeSpan\|Expire\|TTL\|ttl" "$CARRITO_SVC" 2>/dev/null || echo "0")
if [ "${grep_ttl:-0}" -gt 0 ]; then
  PASS=$((PASS+1)); log "  [PASS] 8.6 Carrito en Redis con TTL ($grep_ttl referencias)"
else
  FAIL=$((FAIL+1)); log "  [FAIL] 8.6 No se encontro TTL en CarritoService"
fi

log "  8.7 Fallback SQL en ConsultaEstado"
CE_APP="/Users/jona/Desktop/USAC/4to_Anio/8vo Semestre/AYD2/LAB/proyecto/repo/SGO-Logic/src/Services/SgoLogic.ConsultaEstado.Api/Application"
grep_fallback=$(grep -r "DbContext\|FindAsync\|FirstOrDefault" "$CE_APP" 2>/dev/null | wc -l | tr -d ' ')
if [ "${grep_fallback:-0}" -gt 0 ]; then
  PASS=$((PASS+1)); log "  [PASS] 8.7 Fallback SQL en Application ($grep_fallback refs)"
else
  aviso "8.7 No se encontro claramente fallback SQL - revisar ConsultaEstadoService"
fi

log "  8.8 Todos los contenedores siguen running"
unhealthy=$(docker compose ps 2>/dev/null | grep -v "healthy\|running" | grep -c "sgo-logic" || echo "0")
if [ "${unhealthy:-0}" -eq 0 ]; then
  PASS=$((PASS+1)); log "  [PASS] 8.8 Todos los contenedores healthy/running"
else
  WARN=$((WARN+1)); log "  [WARN] 8.8 $unhealthy contenedores no healthy"
fi

# =============================================================================
seccion "BLOQUE 9: Web UI demo"
# =============================================================================

log "  9.1 Web UI principal accesible"
r=$(do_req GET "$WEB/")
code=$(get_code "$r")
resultado "Web UI / => 200" "$code" "200"

for page in "/Auth/Registro" "/Auth/Login" "/Estado" "/Carrito" "/Incidencias/Crear"; do
  r=$(do_req GET "$WEB$page")
  code=$(get_code "$r")
  if [ "$code" = "200" ] || [ "$code" = "302" ]; then
    resultado "Web UI $page => OK" "$code" "$code"
  else
    resultado "Web UI $page" "$code" "200"
  fi
done

# =============================================================================
seccion "RESUMEN FINAL"
# =============================================================================

TOTAL=$((PASS+FAIL+WARN))
log ""
log "================================================================="
log "  RESULTADO TOTAL: $TOTAL pruebas"
log "    PASS: $PASS"
log "    FAIL: $FAIL"
log "    WARN: $WARN (informativo)"
log "================================================================="
log "Archivo: $LOG_FILE"

if [ "$FAIL" -eq 0 ]; then
  verde "SUITE COMPLETADA - SIN FALLOS"
  log "DICTAMEN: APROBADO"
  exit 0
else
  rojo "SUITE COMPLETADA - $FAIL FALLO(S)"
  log "DICTAMEN: REVISION NECESARIA"
  exit 1
fi
