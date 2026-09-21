# Persistencia y confirmacion en SgoLogic.Ordenes.Api

Fecha: 2026-09-08. Rama: feature/persistenciaOrdenes.

Decisiones tomadas al agregar la capa de persistencia (EF Core + PostgreSQL) y el
caso de uso de confirmacion de orden. No son obvias desde el codigo.

## Modelo y mapeo

- El dominio `Orden` no expone una propiedad `Version`. El contador entero que la
  entidad incrementa en cada `TransicionarA` es `Orden.Secuencia`. Se mapea
  `Secuencia` a la columna `version` con `IsConcurrencyToken()` para el bloqueo
  optimista (ADR del DDA). No se agrego una propiedad nueva al dominio.
- `Orden.Id` es `string` en el dominio (por ejemplo `ORD-2026-000777`), no `Guid`.
  Por eso `MensajeSaliente.AgregadoId` es `string` y no `Guid` como decia el
  enunciado: debe coincidir con `Orden.Id` (la prueba lo verifica).
- Esquema `ordenes`. Tablas `orden`, `linea_orden`, `transicion_orden`, columnas
  en snake_case. Enumeraciones como texto (`HasConversion<string>()`). Timestamps
  `timestamptz` y en UTC (converter `ConvertidorFechaUtc`). Indice unico
  `(orden_id, secuencia)` en `transicion_orden`. La tabla del outbox
  (`outbox_messages`) es la de BuildingBlocks y vive en el esquema `public`, no en
  `ordenes` (ver "Outbox transversal" mas abajo).
- `LineaOrden` se mapea con FK sombra `orden_id`; `TransicionOrden` ya tiene
  `OrdenId` en el dominio. Ambas navegaciones se leen por campo (`_lineas`,
  `_transiciones`).

## Confirmacion

- El estado inicial de una orden recien creada es `EstadoOrden.CREADA`. Segun
  `MaquinaEstadosOrden`, las transiciones validas desde `CREADA` son
  `RESERVA_SOLICITADA`, `RECHAZADA` y `CANCELADA`. La unica de avance de la saga es
  **`CREADA -> RESERVA_SOLICITADA`**, paso `PasoSaga.RESERVA`.
- Por lo tanto `POST /api/ordenes/{id}/confirmar` aplica `CREADA -> RESERVA_SOLICITADA`.
  (Una version anterior aplicaba `CONFIRMADA_PENDIENTE_FISCAL -> CONFIRMADA`, que no
  sale del estado inicial; se corrigio.)
- El resto de la maquina lo recorren los consumidores de eventos (ADR-016). El
  endpoint `POST /api/ordenes/{id}/transiciones`, que aplicaba una transicion
  arbitraria desde la demo, se elimino el 2026-09-21.
- La validacion de si la transicion es legal la hace el dominio
  (`Orden.TransicionarA` -> `ExcepcionTransicionInvalida`); el servicio de
  aplicacion no la captura, la deja subir sin escribir nada.
- Idempotencia: si la orden ya esta en el estado destino, la operacion no escribe
  nada y responde como exitosa.
- Una sola `SaveChangesAsync` (via `IUnidadDeTrabajo`) cubre orden + transicion +
  las filas del outbox. `DbUpdateConcurrencyException` se traduce a
  `ExcepcionConflictoConcurrencia` -> HTTP 409, distinguible del 409 por transicion
  invalida mediante el campo `codigo` en el ProblemDetails.

## Outbox transversal (HU-008)

Ordenes ya no lleva su propio outbox. Se elimino la entidad `MensajeSaliente`, su
configuracion, su repositorio y la tabla `ordenes.mensaje_saliente`, y en su lugar
se usa el outbox transversal de `SgoLogic.BuildingBlocks`
(`AddSgoMessaging()` + `AddOutbox<OrdenesDbContext>()`, `IOutboxWriter`,
`OutboxMessageConfiguration`, `OutboxPublisher`). Motivo: EAC-03 exige que
Auditoria reconstruya la evidencia completa de una transaccion con un unico sobre
y una unica correlacion; cada contexto con su propio outbox y su propio formato
dejaba agujeros en la bitacora. El sobre, la propagacion de `transactionId` /
`causationId` por `EventContext` y el publicador son del proyecto, no de Ordenes
(ver `docs/contratos/sgo-eventos-envelope-v1.md` y `PLANTILLA-SERVICIO.md` seccion
6). Los records de datos de cada evento (`OrdenEstadoCambiadoData` incluido) se
movieron a `SgoLogic.Contracts/Eventos/Ordenes/` porque los deserializan Pagos,
Facturacion, Auditoria y Consulta de Estado. La tabla `outbox_messages` se mapea
al esquema `public` (y no al esquema por defecto `ordenes` del contexto) para que
el `SELECT * FROM outbox_messages` sin calificar del `OutboxPublisher` la
encuentre, igual que en el resto de servicios. La migracion que hace este cambio
es `MigrarAOutboxTransversal`.

## Migracion

- La migracion inicial `InicialOrdenes` ya esta generada (en
  `Infrastructure/Persistencia/Migraciones`). El paso a outbox transversal agrega
  la migracion `MigrarAOutboxTransversal`, que borra `ordenes.mensaje_saliente` y
  crea `public.outbox_messages`. Ambas se generan con el contenedor del SDK via
  `scripts/dev/dn.ps1` (`dn ef migrations add ...`); no se instala `dotnet-ef`
  global, se usa el del manifiesto `.config/dotnet-tools.json`.

## Herramientas y despliegue

- Paquetes fijados a version exacta `8.0.11`
  (`Npgsql.EntityFrameworkCore.PostgreSQL`, `Microsoft.EntityFrameworkCore.Design`)
  para no chocar con el `8.0.11` del proyecto de pruebas (evita MSB3277).
- `OrdenesDbContextFactory` (`IDesignTimeDbContextFactory`) permite que `dotnet ef`
  construya el modelo sin ejecutar `Program.cs` (y su `Database.Migrate()` de
  arranque). Lee `ConnectionStrings__Postgres` de entorno y si no esta usa
  `appsettings.Development.json`.
- Las migraciones (`InicialOrdenes` y `MigrarAOutboxTransversal`) ya estan
  generadas en `Infrastructure/Persistencia/Migraciones` y se aplican solas al
  arrancar (`contexto.Database.Migrate()` en `Program.cs`).
- La base `sgo_ordenes` la crea `scripts/postgres/crear-bases-adicionales.sh` a
  partir de `POSTGRES_OPERACIONAL_MULTIPLE_DATABASES` en `.env` (se agrego
  `sgo_ordenes` a esa lista). El antiguo `scripts/ordenes/00-crear-base.sql`, que
  creaba la base, el rol `ordenes_app` y el esquema aparte, quedo obsoleto y se
  elimino; el esquema `ordenes` lo crea la propia migracion (`EnsureSchema`).
