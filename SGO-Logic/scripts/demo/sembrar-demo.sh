#!/usr/bin/env bash
set -euo pipefail

BASE_URL="${BASE_URL:-http://localhost:8080}"
EMAIL="${DEMO_EMAIL:-demo@sgo.local}"
PASSWORD="${DEMO_PASSWORD:-Demo1234!}"
NOMBRE="${DEMO_NOMBRE:-Usuario Demo}"
VERIFICATION_CODE="${DEMO_VERIFICATION_CODE:-}"
MENSAJERIA_URL="${MENSAJERIA_URL:-http://localhost:8089}"

# Usuarios de demo: "correo|nombre". La web trata como ADMIN a cualquier correo
# que contenga "admin" (PermisosHelper), por eso los administrativos usan ese
# prefijo; en la base todos quedan con rol CLIENTE hasta que exista un endpoint
# de asignacion de rol.
USUARIOS_DEMO=(
  "admin@sgo.local|Administrador General"
  "admin.bodega@sgo.local|Administrador de Bodega"
  "admin.ventas@sgo.local|Administrador de Ventas"
  "cliente1@sgo.local|Ana Lopez"
  "cliente2@sgo.local|Carlos Ramirez"
  "cliente3@sgo.local|Maria Guzman"
  "cliente4@sgo.local|Luis Morales"
  "operador1@sgo.local|Operador de Bodega"
  "operador2@sgo.local|Operador de Despacho"
)

# Servicio de Postgres donde vive la base de identidad, para el paso que asigna
# los roles reales (ver asignar_roles).
DB_SERVICIO="${DB_SERVICIO:-db-operacional}"
DB_USUARIO="${POSTGRES_USER:-sgo_admin}"

request() {
  local method="$1"
  local url="$2"
  local body="${3:-}"
  local response
  local -a curl_args=(--silent --show-error --request "$method" "$url" --header 'Content-Type: application/json')

  if [[ -n "$body" ]]; then
    curl_args+=(--data "$body")
  fi

  response=$(curl "${curl_args[@]}" --write-out $'\n%{http_code}')

  HTTP_CODE="${response##*$'\n'}"
  HTTP_BODY="${response%$'\n'*}"
}

require_success() {
  if [[ "$HTTP_CODE" != 2* && "$HTTP_CODE" != "409" ]]; then
    printf 'ERROR %s: %s\n' "$HTTP_CODE" "$HTTP_BODY" >&2
    exit 1
  fi
}

# Asignacion de roles mediante el endpoint oficial /api/auth/asignar-rol
# y actualizacion de seguridad directa en Postgres como respaldo.
asignar_roles() {
  for u in "${USUARIOS_DEMO[@]}"; do
    local correo="${u%%|*}"
    local rol="CLIENTE"
    if [[ "$correo" == admin* ]]; then
      rol="ADMIN"
    elif [[ "$correo" == operador* ]]; then
      rol="OPERADOR"
    fi
    request POST "$BASE_URL/api/auth/asignar-rol" "{\"email\":\"$correo\",\"rol\":\"$rol\"}" || true
  done

  if command -v docker >/dev/null 2>&1; then
    docker compose exec -T "$DB_SERVICIO" psql -U "$DB_USUARIO" -d sgo_auth -v ON_ERROR_STOP=1 <<'SQL' >/dev/null 2>&1 || true
UPDATE auth.usuarios SET rol = 'ADMIN'    WHERE email LIKE 'admin%@sgo.local';
UPDATE auth.usuarios SET rol = 'OPERADOR' WHERE email LIKE 'operador%@sgo.local';
SQL
  fi
}


# Lee de la cola del mock de mensajeria el ultimo codigo enviado a un correo.
leer_codigo() {
  local email="$1"
  curl --silent "$MENSAJERIA_URL/api/mensajes" \
    | tr '}' '\n' \
    | grep -F "\"destinatario\":\"$email\"" \
    | tail -1 \
    | grep -oE '"cuerpo":"[^"]*"' \
    | grep -oE '[0-9]{6}' \
    | head -1
}

# Registra, verifica e inicia sesion. Devuelve 1 si no se pudo verificar, para
# que el seed siga con los demas usuarios en vez de abortar todo.
sembrar_usuario() {
  local email="$1" password="$2" nombre="$3" codigo="${4:-}"

  printf 'Preparando usuario %s\n' "$email"
  request POST "$BASE_URL/api/auth/registro" \
    "{\"email\":\"$email\",\"contrasena\":\"$password\",\"nombreCompleto\":\"$nombre\"}"
  require_success

  if [[ -z "$codigo" ]]; then
    # reenviar-codigo deja uno fresco en la cola; si la cuenta ya estaba
    # verificada responde 200 y el verificar de mas abajo no hace nada.
    request POST "$BASE_URL/api/auth/reenviar-codigo" "{\"email\":\"$email\"}"
    codigo=$(leer_codigo "$email") || true
  fi

  if [[ -z "$codigo" ]]; then
    printf '  AVISO: sin codigo de verificacion para %s (mensajeria en %s)\n' "$email" "$MENSAJERIA_URL"
    return 1
  fi

  request POST "$BASE_URL/api/auth/verificar" \
    "{\"email\":\"$email\",\"codigo\":\"$codigo\"}"
  require_success

  request POST "$BASE_URL/api/auth/login" \
    "{\"email\":\"$email\",\"contrasena\":\"$password\"}"
  if [[ "$HTTP_CODE" != "200" ]]; then
    printf 'ERROR login %s: %s\n' "$HTTP_CODE" "$HTTP_BODY" >&2
    return 1
  fi

  printf '  OK %s\n' "$email"
  return 0
}

fallidos=0
sembrar_usuario "$EMAIL" "$PASSWORD" "$NOMBRE" "$VERIFICATION_CODE" || fallidos=$((fallidos + 1))

for entrada in "${USUARIOS_DEMO[@]}"; do
  sembrar_usuario "${entrada%%|*}" "$PASSWORD" "${entrada##*|}" || fallidos=$((fallidos + 1))
done

if (( fallidos > 0 )); then
  printf '\n%d usuario(s) sin verificar. Revisa el mock de mensajeria o usa DEMO_VERIFICATION_CODE.\n' "$fallidos"
  printf 'Ejemplo: docker compose logs auth-api | grep -i codigo\n\n'
fi

printf 'Asignando roles ADMIN y OPERADOR\n'
asignar_roles

printf 'Cargando existencias de demo y catalogo web\n'
for existencia in \
  '{"sku":"SKU-DEMO-001","ubicacionId":"CD-CENTRAL","tipoUbicacion":"CENTRO_DISTRIBUCION","cantidadFisica":100}' \
  '{"sku":"SKU-DEMO-001","ubicacionId":"TIENDA-001","tipoUbicacion":"TIENDA","cantidadFisica":20}' \
  '{"sku":"SKU-DEMO-002","ubicacionId":"CD-CENTRAL","tipoUbicacion":"CENTRO_DISTRIBUCION","cantidadFisica":75}' \
  '{"sku":"SKU-DEMO-003","ubicacionId":"TIENDA-001","tipoUbicacion":"TIENDA","cantidadFisica":40}'; do
  request PUT "$BASE_URL/api/inventario/existencias" "$existencia"
  if [[ "$HTTP_CODE" != "200" ]]; then
    printf 'ERROR existencias %s: %s\n' "$HTTP_CODE" "$HTTP_BODY" >&2
    exit 1
  fi
  printf '  OK %s\n' "$existencia"
done

for sku in PROD-001 PROD-002 PROD-003 PROD-004 PROD-005 PROD-006 PROD-007 PROD-008; do
  for cd in CD-01 CD-CENTRAL; do
    request PUT "$BASE_URL/api/inventario/existencias" \
      "{\"sku\":\"$sku\",\"ubicacionId\":\"$cd\",\"tipoUbicacion\":\"CENTRO_DISTRIBUCION\",\"cantidadFisica\":100}"
    printf '  OK %s en %s\n' "$sku" "$cd"
  done
  for tienda in TIENDA-01-CENTRAL TIENDA-02-MIRAFLORES TIENDA-03-PORTALES TIENDA-04-PRADERA; do
    request PUT "$BASE_URL/api/inventario/existencias" \
      "{\"sku\":\"$sku\",\"ubicacionId\":\"$tienda\",\"tipoUbicacion\":\"TIENDA\",\"cantidadFisica\":50}"
    printf '  OK %s en %s\n' "$sku" "$tienda"
  done
done

# Productos con stock bajo (PROD-101..105) para probar las retenciones de carrito y
# la sugerencia de la tienda mas cercana con existencias. Roles de sucursal
# (distancia Haversine desde T1, coordenadas de inventario.sucursal):
#   T1     = TIENDA-01-CENTRAL     sucursal "casa" de las pruebas
#   TVACIA = TIENDA-09-FONTABELLA  la mas cercana a T1 (0.4 km); sin PROD-102
#   TCERCA = TIENDA-05-OAKLAND     la segunda mas cercana a T1 (0.65 km)
#   T2     = TIENDA-12-AMERICAS    otra tienda cercana a T1 (1.9 km)
#   TLEJOS = TIENDA-17-XELA        Quetzaltenango, otro departamento (112.7 km)
# PUT fija la cantidad fisica, asi que cada 'up' vuelve a este estado. Inventario
# responde 409 si la nueva cantidad queda por debajo de lo ya reservado (por
# ejemplo, un carrito con unidades apartadas): se avisa y se sigue con el resto.
T1="TIENDA-01-CENTRAL"
T2="TIENDA-12-AMERICAS"
TVACIA="TIENDA-09-FONTABELLA"
TCERCA="TIENDA-05-OAKLAND"
TLEJOS="TIENDA-17-XELA"

cargar_existencia() {
  local sku="$1" ubicacion="$2" tipo="$3" cantidad="$4"
  request PUT "$BASE_URL/api/inventario/existencias" \
    "{\"sku\":\"$sku\",\"ubicacionId\":\"$ubicacion\",\"tipoUbicacion\":\"$tipo\",\"cantidadFisica\":$cantidad}"
  case "$HTTP_CODE" in
    2*) printf '  OK %s en %s = %s\n' "$sku" "$ubicacion" "$cantidad" ;;
    409) printf '  AVISO 409 %s en %s: %s\n' "$sku" "$ubicacion" "$HTTP_BODY" ;;
    *)
      printf 'ERROR existencias %s %s en %s: %s\n' "$HTTP_CODE" "$sku" "$ubicacion" "$HTTP_BODY" >&2
      exit 1
      ;;
  esac
}

printf 'Cargando productos con stock bajo (PROD-101..105)\n'
# PROD-101: solo T1 y T2 tienen 2 unidades.
for fila in "CD-01|CENTRO_DISTRIBUCION|0" "CD-CENTRAL|CENTRO_DISTRIBUCION|0" "$T1|TIENDA|2" "$T2|TIENDA|2"; do
  IFS='|' read -r ubicacion tipo cantidad <<< "$fila"
  cargar_existencia PROD-101 "$ubicacion" "$tipo" "$cantidad"
done

# PROD-102: agotado en T1 y en la tienda mas cercana (TVACIA); hay en TCERCA y TLEJOS.
for fila in "CD-01|CENTRO_DISTRIBUCION|0" "CD-CENTRAL|CENTRO_DISTRIBUCION|0" "$T1|TIENDA|0" "$T2|TIENDA|0" \
            "$TVACIA|TIENDA|0" "$TCERCA|TIENDA|2" "$TLEJOS|TIENDA|2"; do
  IFS='|' read -r ubicacion tipo cantidad <<< "$fila"
  cargar_existencia PROD-102 "$ubicacion" "$tipo" "$cantidad"
done

# PROD-103: la unica unidad esta en TLEJOS.
for fila in "CD-01|CENTRO_DISTRIBUCION|0" "CD-CENTRAL|CENTRO_DISTRIBUCION|0" "$T1|TIENDA|0" "$T2|TIENDA|0" \
            "$TLEJOS|TIENDA|1"; do
  IFS='|' read -r ubicacion tipo cantidad <<< "$fila"
  cargar_existencia PROD-103 "$ubicacion" "$tipo" "$cantidad"
done

# PROD-104: sin existencias en ninguna parte.
for fila in "CD-01|CENTRO_DISTRIBUCION|0" "CD-CENTRAL|CENTRO_DISTRIBUCION|0" "$T1|TIENDA|0" "$T2|TIENDA|0"; do
  IFS='|' read -r ubicacion tipo cantidad <<< "$fila"
  cargar_existencia PROD-104 "$ubicacion" "$tipo" "$cantidad"
done

# PROD-105 (control): disponible en CD-CENTRAL, T1 y T2.
for fila in "CD-CENTRAL|CENTRO_DISTRIBUCION|2" "$T1|TIENDA|2" "$T2|TIENDA|2"; do
  IFS='|' read -r ubicacion tipo cantidad <<< "$fila"
  cargar_existencia PROD-105 "$ubicacion" "$tipo" "$cantidad"
done

printf 'Seed de demo completado. Usuarios: %s y %d mas (contrasena %s) | SKUs: SKU-DEMO-001..003, PROD-001..008 y PROD-101..105\n' \
  "$EMAIL" "${#USUARIOS_DEMO[@]}" "$PASSWORD"
