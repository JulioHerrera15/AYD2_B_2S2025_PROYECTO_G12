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
- Esquema `ordenes`. Tablas `orden`, `linea_orden`, `transicion_orden`,
  `mensaje_saliente`, columnas en snake_case. Enumeraciones como texto
  (`HasConversion<string>()`). Timestamps `timestamptz` y en UTC (converter
  `ConvertidorFechaUtc`). Indice unico `(orden_id, secuencia)` en
  `transicion_orden`. Indice `(estado, creado_en)` en `mensaje_saliente` para el
  publicador por polling.
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
- `POST /api/ordenes/{id}/transiciones` aplica una transicion arbitraria (estado
  destino, paso de saga, motivo opcional, bandera de compensacion) para recorrer el
  resto de la maquina desde la demo. Misma logica que la confirmacion.
- La validacion de si la transicion es legal la hace el dominio
  (`Orden.TransicionarA` -> `ExcepcionTransicionInvalida`); el servicio de
  aplicacion no la captura, la deja subir sin escribir nada.
- Idempotencia: si la orden ya esta en el estado destino, la operacion no escribe
  nada y responde como exitosa.
- El tipo del dato del evento (`OrdenEstadoCambiadoData`, evento
  `orden.estado-cambiado`) vive local en `Application/Ordenes/Eventos`, no en
  `SgoLogic.Contracts`. Solo se reutiliza el envelope `IntegrationEvent<TData>`.
- Una sola `SaveChangesAsync` (via `IUnidadDeTrabajo`) cubre orden + transicion +
  `mensaje_saliente`. `DbUpdateConcurrencyException` se traduce a
  `ExcepcionConflictoConcurrencia` -> HTTP 409, distinguible del 409 por transicion
  invalida mediante el campo `codigo` en el ProblemDetails.

## Herramientas y despliegue

- Paquetes fijados a version exacta `8.0.11`
  (`Npgsql.EntityFrameworkCore.PostgreSQL`, `Microsoft.EntityFrameworkCore.Design`)
  para no chocar con el `8.0.11` del proyecto de pruebas (evita MSB3277).
- `OrdenesDbContextFactory` (`IDesignTimeDbContextFactory`) permite que `dotnet ef`
  construya el modelo sin ejecutar `Program.cs` (y su `Database.Migrate()` de
  arranque). Lee `ConnectionStrings__Postgres` de entorno y si no esta usa
  `appsettings.Development.json`.
- Falta generar la migracion inicial (en el entorno de trabajo no habia .NET SDK,
  solo runtime):
  `dotnet ef migrations add InicialOrdenes -p src/Services/SgoLogic.Ordenes.Api -o Infrastructure/Persistencia/Migraciones`
  El script `scripts/ordenes/00-crear-base.sql` ya crea la base `sgo_ordenes`, el
  rol `ordenes_app` y el esquema `ordenes`.
