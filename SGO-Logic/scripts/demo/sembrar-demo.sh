#!/usr/bin/env bash
set -euo pipefail

BASE_URL="${BASE_URL:-http://localhost:8080}"
EMAIL="${DEMO_EMAIL:-demo@sgo.local}"
PASSWORD="${DEMO_PASSWORD:-Demo1234!}"
NOMBRE="${DEMO_NOMBRE:-Usuario Demo}"
VERIFICATION_CODE="${DEMO_VERIFICATION_CODE:-}"

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

printf 'Preparando usuario demo %s\n' "$EMAIL"
request POST "$BASE_URL/api/auth/registro" \
  "{\"email\":\"$EMAIL\",\"contrasena\":\"$PASSWORD\",\"nombreCompleto\":\"$NOMBRE\"}"
require_success

if [[ -z "$VERIFICATION_CODE" ]]; then
  printf 'Usuario creado o ya existente. Define DEMO_VERIFICATION_CODE con el codigo del log de auth-api y vuelve a ejecutar.\n'
  printf 'Ejemplo: docker compose logs auth-api | grep -i codigo\n'
  exit 0
fi

request POST "$BASE_URL/api/auth/verificar" \
  "{\"email\":\"$EMAIL\",\"codigo\":\"$VERIFICATION_CODE\"}"
require_success

request POST "$BASE_URL/api/auth/login" \
  "{\"email\":\"$EMAIL\",\"contrasena\":\"$PASSWORD\"}"
if [[ "$HTTP_CODE" != "200" ]]; then
  printf 'ERROR login %s: %s\n' "$HTTP_CODE" "$HTTP_BODY" >&2
  exit 1
fi

printf 'Cargando existencias de demo\n'
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

printf 'Seed de demo completado. Usuario: %s | SKUs: SKU-DEMO-001..003\n' "$EMAIL"
