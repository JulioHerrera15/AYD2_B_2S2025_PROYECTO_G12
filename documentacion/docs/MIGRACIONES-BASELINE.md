# Baseline de migraciones EF en bases persistentes

## Por que existe este procedimiento

`SgoLogic.IntegracionErp.Worker` creo originalmente su esquema con
`Database.EnsureCreated()`. La aplicacion ahora usa `Database.Migrate()` y la
migracion inicial es `20260922231117_InicialIntegracionErp`.

Una base ERP que ya fue creada con `EnsureCreated()` tiene las tablas, pero no
tiene la tabla `__EFMigrationsHistory`. Si se despliega directamente una
version que llama a `Migrate()`, EF intentara ejecutar `CREATE TABLE` sobre
tablas existentes. En una base persistente se debe registrar primero la
migracion como baseline, sin ejecutar de nuevo su `Up()`.

## Precondiciones

- Ejecutar el procedimiento con el usuario propietario de la base o con
  permisos para crear tablas e insertar en `__EFMigrationsHistory`.
- Confirmar que la base es `sgo_integracion_erp` y que la migracion desplegada
  corresponde a `20260922231117_InicialIntegracionErp`.
- Hacer respaldo de la base antes de modificar el historial.
- Usar este procedimiento solo si el esquema ya fue creado por la version que
  usaba `EnsureCreated()`. Para una base nueva se debe dejar que el worker
  ejecute `Database.Migrate()` normalmente.

## 1. Validar el esquema existente

Desde la raiz `SGO-Logic/`, con el stack levantado:

```bash
docker compose exec -T db-operacional psql \
  -U sgo_admin -d sgo_integracion_erp \
  -c "SELECT tablename
      FROM pg_tables
      WHERE schemaname = 'public'
        AND tablename IN (
          'conciliaciones_erp', 'lotes_erp', 'mensajes_procesados',
          'outbox_messages', 'registros_lote_erp', 'ventas_erp'
        )
      ORDER BY tablename;"
```

El resultado debe contener exactamente estas seis tablas:

```text
conciliaciones_erp
lotes_erp
mensajes_procesados
outbox_messages
registros_lote_erp
ventas_erp
```

Antes de continuar, validar tambien los indices y las restricciones si la
base fue modificada manualmente. El baseline no repara diferencias de esquema;
si falta una tabla, columna, indice o restriccion, se debe corregir primero o
crear una migracion de transicion especifica.

## 2. Registrar el baseline

Ejecutar una sola vez contra `sgo_integracion_erp`:

```bash
docker compose exec -T db-operacional psql \
  -U sgo_admin -d sgo_integracion_erp <<'SQL'
BEGIN;

CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260922231117_InicialIntegracionErp', '8.0.11')
ON CONFLICT ("MigrationId") DO NOTHING;

COMMIT;
SQL
```

El `ON CONFLICT` hace que el comando sea repetible, pero no sustituye la
validacion del esquema. No registrar una migracion si no se comprobo que su
modelo ya esta aplicado.

## 3. Verificar y arrancar el worker

```bash
docker compose exec -T db-operacional psql \
  -U sgo_admin -d sgo_integracion_erp \
  -c 'SELECT "MigrationId", "ProductVersion" FROM "__EFMigrationsHistory" ORDER BY "MigrationId";'

docker compose up -d --build integracion-erp-worker
docker compose logs --since 2m integracion-erp-worker
```

Debe aparecer la migracion `20260922231117_InicialIntegracionErp` con version
`8.0.11`, y el worker debe permanecer saludable sin errores de tablas ya
existentes. A partir de ese momento, las migraciones futuras se aplican de
forma normal con `Database.Migrate()`.

## Rollback del baseline

No borrar `__EFMigrationsHistory` como mecanismo de rollback: eso haria que EF
intentara recrear todas las tablas. Si el despliegue debe revertirse, volver a
la version anterior del worker conserva el esquema; si se necesita revertir
estructura, usar una migracion `Down` revisada y un respaldo, nunca
`EnsureCreated()` sobre una base que ya usa migraciones.