#!/bin/bash
# Crea, ademas de POSTGRES_DB, las bases listadas en POSTGRES_MULTIPLE_DATABASES
# (separadas por coma). La imagen oficial de postgres solo crea una base por
# contenedor via POSTGRES_DB; esto es lo que permite que cada servicio
# tenga su propia base dentro del mismo contenedor
# db-operacional, en vez de compartir una.
set -e
set -u

crear_base() {
    local base=$1
    echo "Creando base de datos adicional '$base'"
    psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-SQL
        CREATE DATABASE "$base";
SQL
}

if [ -n "${POSTGRES_MULTIPLE_DATABASES:-}" ]; then
    for base in $(echo "$POSTGRES_MULTIPLE_DATABASES" | tr ',' ' '); do
        crear_base "$base"
    done
fi