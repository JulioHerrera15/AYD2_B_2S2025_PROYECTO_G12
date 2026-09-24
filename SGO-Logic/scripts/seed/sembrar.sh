#!/bin/sh
# Siembra datos de demostracion en las bases de todos los servicios. Lo ejecuta
# el contenedor `seed-datos` de docker-compose.yml una vez que los servicios
# estan sanos (sus migraciones ya corrieron al arrancar).
#
# Reglas:
#   - Usuarios (auth): siempre se aseguran; ON CONFLICT (email) no duplica.
#   - El resto de bases solo se siembra si su tabla principal esta vacia, para
#     no mezclar datos de demo con datos reales en un volumen ya usado.
#   - Cada archivo SQL corre en una sola transaccion (psql -1): o entra todo o nada.
#
# Contrasena de TODOS los usuarios sembrados: 1234
set -eu

if [ "${SEED_HABILITADO:-true}" != "true" ]; then
    echo "[seed] SEED_HABILITADO=${SEED_HABILITADO}; no se siembra nada."
    exit 0
fi

: "${POSTGRES_USER:?falta POSTGRES_USER}"
: "${POSTGRES_PASSWORD:?falta POSTGRES_PASSWORD}"
export PGPASSWORD="$POSTGRES_PASSWORD"

SQL_DIR="$(dirname "$0")/sql"
HOST_OPERACIONAL="${HOST_OPERACIONAL:-db-operacional}"
HOST_FINANZAS="${HOST_FINANZAS:-db-finanzas}"
DB_MANTENIMIENTO="${POSTGRES_OPERACIONAL_DB:-sgo_operacional}"

consultar() {
    # consultar <host> <base> <sql>
    psql -h "$1" -U "$POSTGRES_USER" -d "$2" -v ON_ERROR_STOP=1 -tAc "$3"
}

esperar_tabla() {
    # esperar_tabla <host> <base> <esquema.tabla>: la crea la migracion del servicio.
    intentos=0
    until [ "$(consultar "$1" "$2" "SELECT to_regclass('$3') IS NOT NULL" 2>/dev/null)" = "t" ]; do
        intentos=$((intentos + 1))
        if [ "$intentos" -ge 60 ]; then
            echo "[seed] ERROR: la tabla $3 no aparecio en $2 tras 120s." >&2
            exit 1
        fi
        sleep 2
    done
}

ejecutar() {
    # ejecutar <host> <base> <archivo> [args extra de psql]
    host="$1"; base="$2"; archivo="$3"; shift 3
    psql -h "$host" -U "$POSTGRES_USER" -d "$base" -v ON_ERROR_STOP=1 -q -1 "$@" -f "$SQL_DIR/$archivo"
}

sembrar_si_vacia() {
    # sembrar_si_vacia <host> <base> <esquema.tabla centinela> <archivo>
    esperar_tabla "$1" "$2" "$3"
    filas="$(consultar "$1" "$2" "SELECT count(*) FROM $3")"
    if [ "$filas" != "0" ]; then
        echo "[seed] $2: $3 ya tiene $filas filas, se omite."
        return 0
    fi
    ejecutar "$1" "$2" "$4"
    echo "[seed] $2: sembrada ($4)."
}

# ── Auth ─────────────────────────────────────────────────────────────────────
# El hash BCrypt se calcula con pgcrypto en la base de mantenimiento (no la usa
# ningun servicio) para no instalar extensiones en la base de Auth. pgcrypto
# genera hashes $2a$, que BCrypt.Net verifica sin problema.
esperar_tabla "$HOST_OPERACIONAL" sgo_auth auth.usuarios
consultar "$HOST_OPERACIONAL" "$DB_MANTENIMIENTO" "SET client_min_messages TO warning; CREATE EXTENSION IF NOT EXISTS pgcrypto" >/dev/null
HASH_1234="$(consultar "$HOST_OPERACIONAL" "$DB_MANTENIMIENTO" "SELECT crypt('1234', gen_salt('bf', 10))")"
ejecutar "$HOST_OPERACIONAL" sgo_auth 01-auth.sql -v hash="$HASH_1234"
echo "[seed] sgo_auth: usuarios asegurados."

# ── Resto de contextos ───────────────────────────────────────────────────────
sembrar_si_vacia "$HOST_OPERACIONAL" sgo_inventario      inventario.existencia          02-inventario.sql
sembrar_si_vacia "$HOST_OPERACIONAL" sgo_ordenes         ordenes.orden                  03-ordenes.sql
sembrar_si_vacia "$HOST_OPERACIONAL" sgo_despacho        despacho.despacho              04-despacho.sql
sembrar_si_vacia "$HOST_OPERACIONAL" sgo_consulta_estado consulta_estado.ordenes_vista  05-consulta-estado.sql
sembrar_si_vacia "$HOST_FINANZAS"    sgo_pagos           pagos.pago                     06-pagos.sql
sembrar_si_vacia "$HOST_FINANZAS"    sgo_facturacion     facturacion.documento_fiscal   07-facturacion.sql

cat <<'EOF'
[seed] Listo. Usuarios de demo (contrasena 1234 para todos):
         admin@sgo.local        ADMIN
         admin2@sgo.local       ADMIN
         operador@sgo.local     OPERADOR
         cliente1@sgo.local     CLIENTE
         cliente2@sgo.local     CLIENTE
         cliente3@sgo.local     CLIENTE
EOF
