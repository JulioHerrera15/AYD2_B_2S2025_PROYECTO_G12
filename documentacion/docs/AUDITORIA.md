# Auditoria (HU-006)

Bitacora inmutable del sistema. Consume TODOS los eventos publicados en
`sgo.eventos` (no solo los de un contexto) y los persiste sin modificarlos, para
poder reconstruir que paso en una transaccion completa. Sostiene EAC-03:
recuperar toda la evidencia de una transaccion en menos de 1 segundo.

> Nota de alcance: esta historia se construyo sin `documentacion/docs/PLANTILLA-SERVICIO.md`
> ni `PG_AUDIT_USER`/`PG_AUDIT_PASS` en `.env.example`, porque ninguno de los dos
> existia todavia en `develop` en el momento de implementarla (se verifico en
> `develop`, `main` y todas las ramas remotas). El servicio se construyo copiando
> la estructura real de `SgoLogic.Sample.Worker`, y las variables de entorno se
> agregaron aca mismo.

## Que registra y desde donde

`SgoLogic.Auditoria.Worker` es un Worker con un endpoint de consulta:

- Cola propia `auditoria-worker.eventos` (quorum, durable, dead-letter hacia
  `sgo.eventos.dlx`, `x-delivery-limit`), declarada con el helper transversal
  `DeclaradorColas`.
- Binding **`#`** sobre el exchange `sgo.eventos` (no `ordenes.#` ni ningun
  prefijo): recibe eventos de todos los contextos. Si se limitara a un prefijo,
  la bitacora quedaria incompleta y EAC-03 no se podria demostrar sobre una
  transaccion que cruza contextos.
- Consume con `EventConsumerBase<AuditoriaDbContext, JsonElement>`
  (`Consumers/AuditoriaConsumidor.cs`). Usar `JsonElement` como `TData` (en vez de
  un tipo concreto de `Contracts`) es la clave de por que Auditoria no se rompe
  con un evento de un tipo que todavia no existe en `Contracts`: cualquier forma
  de `data` deserializa igual, sin que el consumidor conozca su forma.
- `EventConsumerBase` ya resuelve deduplicacion (tabla transversal
  `mensajes_procesados`, clave `event_id` + `nombre_servicio = "auditoria-worker"`)
  y el ack/nack: si la insercion falla (por ejemplo, con la base caida),
  `ManejarAsync` propaga la excepcion, se hace nack con requeue, y el mensaje
  vuelve a `auditoria-worker.eventos` sin perderse hasta agotar
  `x-delivery-limit` (entonces cae a `sgo.eventos.dlx`).
- La insercion real la hace `Application/RegistradorEventoAuditoria.cs`, separada
  del consumidor para poder probarla sin RabbitMQ. Antes de insertar, chequea si
  el `EventId` ya existe en `eventos_auditoria` (defensa en profundidad, ver
  seccion de metricas) y solo entonces agrega la fila; nunca llama a
  `SaveChangesAsync` — eso lo hace `EventConsumerBase`, junto con la marca de
  `mensajes_procesados`, en una sola transaccion.

## Esquema de la tabla

Tabla `eventos_auditoria` en `db-auditoria` (base propia, `sgo_auditoria`;
Auditoria nunca se conecta a `db-operacional` ni a `db-finanzas`). Persiste el
envelope completo, columna por campo, salvo `data` que va crudo:

| Columna           | Tipo                     | Nota                                   |
|-------------------|--------------------------|-----------------------------------------|
| `id`              | uuid (PK)                | surrogate, generado en la app           |
| `event_id`        | varchar(64)              | unico                                    |
| `event_type`      | varchar(200)              |                                          |
| `event_version`   | int                      |                                          |
| `occurred_at`     | timestamptz              | cuando el origen genero el evento        |
| `sequence`        | int                      |                                          |
| `transaction_id`  | varchar(64)              | correlaciona la saga completa            |
| `causation_id`    | varchar(64), nullable    |                                          |
| `aggregate_type`  | varchar(200)              |                                          |
| `aggregate_id`    | varchar(200)              |                                          |
| `source`          | varchar(100)              | contexto que origino el evento           |
| `payload`         | jsonb                    | `data` crudo, `evento.Data.GetRawText()`, sin normalizar ni transformar |
| `fecha_recepcion` | timestamptz              | cuando Auditoria lo persistio (distinto de `occurred_at`) |

Indices:

- `event_id` unico: ancla de idempotencia (y del chequeo defensivo en
  `RegistradorEventoAuditoria`).
- `transaction_id`: es la consulta principal, la que sostiene EAC-03.
- `aggregate_id`: consulta secundaria por agregado (por ejemplo, una orden).
- **No hay indice GIN sobre `payload`.** Ninguno de los dos endpoints de
  consulta filtra por contenido del JSON: ambos localizan filas por
  `transaction_id` o `aggregate_id` y devuelven `payload` completo. Un GIN ahi
  solo costaria escritura en cada insercion sin acelerar ninguna consulta real,
  asi que no se agrego.

## Inmutabilidad a nivel de motor

Este es el punto central de la historia: la inmutabilidad no depende de que el
codigo de aplicacion se porte bien, la impone PostgreSQL con dos mecanismos
independientes, aplicados por `Infrastructure/PermisosAuditoriaAplicador.cs`:

1. **Permisos de rol.** El servicio corre en produccion con un rol de
   PostgreSQL (`PG_AUDIT_USER` / `auditoria_app` en desarrollo local) que tiene
   **solo** `INSERT, SELECT` sobre `eventos_auditoria` (y sobre
   `mensajes_procesados`, la tabla transversal de idempotencia que
   `EventConsumerBase` necesita leer y escribir para deduplicar — esa tabla no
   es la bitacora y no necesita ser inmutable, pero sin el grant el consumidor
   no puede operar en absoluto). Nunca se le otorga `UPDATE`, `DELETE` ni
   `TRUNCATE`; el aplicador ademas los revoca explicitamente (`REVOKE ALL ...
   FROM PUBLIC` y `FROM` el propio rol antes de otorgar) como cinturon y
   tirantes contra un grant heredado de una version anterior del script. Un
   `UPDATE` o `DELETE` con este usuario falla con `permission denied` (SQLSTATE
   `42501`), verificado tanto en los tests de integracion como manualmente
   contra el contenedor real:

   ```
   $ docker compose exec db-auditoria psql -U auditoria_app -d sgo_auditoria \
       -c "UPDATE eventos_auditoria SET event_type = 'hackeado' WHERE event_id = '...'"
   ERROR:  permission denied for table eventos_auditoria
   ```

2. **Trigger `BEFORE UPDATE OR DELETE`.** Ademas del permiso, existe un trigger
   (`trg_bloquear_modificacion_eventos_auditoria`, funcion
   `fn_bloquear_modificacion_eventos_auditoria`) que lanza una excepcion
   (`RAISE EXCEPTION`, SQLSTATE `P0001`) ante cualquier intento de `UPDATE` o
   `DELETE` sobre la tabla — **incluso con el usuario propietario**, que si
   tendria el permiso. Es defensa en profundidad real: dos mecanismos
   independientes, verificados por separado en
   `PermisosInmutabilidadTests` (uno de los tres tests ahi corre el `UPDATE`
   con el usuario propietario especificamente para probar que el trigger, no
   el permiso, es quien lo bloquea).

Todo esto lo aplica `PermisosAuditoriaAplicador.AplicarAsync(...)` en
`Program.cs`, **despues** de correr las migraciones, con la cadena de conexion
del usuario propietario. El SQL es idempotente (usa `IF NOT EXISTS` / `CREATE OR
REPLACE` / `DROP ... IF EXISTS`): correrlo en cada arranque del servicio no
falla ni duplica nada.

## Migraciones con un usuario distinto al de la aplicacion

El rol restringido de la aplicacion no tiene permiso para `CREATE TABLE`, asi
que **no puede** correr las migraciones. Por eso el servicio usa dos cadenas de
conexion distintas, configuradas por separado:

- `ConnectionStrings:Postgres` — usuario restringido (`PG_AUDIT_USER`/
  `PG_AUDIT_PASS`). Es el que usan, en tiempo de ejecucion, tanto el consumidor
  (via el `AuditoriaDbContext` registrado en DI) como la API de consulta. Es
  tambien el que usa el health check de `/health/ready` (una consulta `SELECT
  1` no necesita ningun permiso sobre tablas).
- `ConnectionStrings:PostgresMigracion` — usuario propietario
  (`POSTGRES_USER`/`POSTGRES_PASSWORD`, el superusuario del contenedor
  `db-auditoria`, igual que en los demas Postgres del compose). Se usa **solo**
  al arrancar (`Program.cs`), para instanciar un `AuditoriaDbContext` de un solo
  uso, correr `Database.Migrate()`, y luego aplicar
  `PermisosAuditoriaAplicador`.

```
                    ConnectionStrings:PostgresMigracion (owner)
Program.cs  ────────────────────────────────────────────────────▶  Migrate() + permisos/trigger
                    ConnectionStrings:Postgres (PG_AUDIT_USER)
DI (consumidor, API) ───────────────────────────────────────────▶  INSERT / SELECT en tiempo de ejecucion
```

En docker-compose, `auditoria-worker` recibe ambas cadenas como variables de
entorno separadas (`ConnectionStrings__Postgres` y
`ConnectionStrings__PostgresMigracion`) mas `Auditoria__AppUsuario` y
`Auditoria__AppPassword` (los mismos valores de `PG_AUDIT_USER`/`PG_AUDIT_PASS`,
pasados de nuevo porque `PermisosAuditoriaAplicador` necesita el nombre del rol
para crearlo/otorgarle permisos, no solo para conectarse con el).

## Endpoints de consulta

Ambos de solo lectura y paginados (`pagina`, `tamano`, tope de 200 por pagina),
ordenados cronologicamente por `occurred_at` y luego `sequence` (para
desempatar eventos con el mismo timestamp):

- `GET /auditoria/transacciones/{transactionId}` — toda la evidencia de una
  transaccion. Es el que sostiene EAC-03.
- `GET /auditoria/agregados/{aggregateId}` — todos los eventos de un agregado
  (por ejemplo, una orden).

Verificado en vivo (`docker compose up --build`) con `sample-api` +
`sample-worker`: el evento original (`source: sample-api`) y el derivado
(`source: sample-worker`, `causationId` apuntando al original) aparecen juntos,
en orden, bajo el mismo `transactionId`.

## Metricas

- `auditoria_eventos_registrados_total{source}` — counter, incrementado en
  `RegistradorEventoAuditoria` cada vez que se agrega una fila nueva, con
  etiqueta por `source` de origen del evento.
- `auditoria_consulta_evidencia_segundos` — histogram, mide la latencia de
  `ObtenerPorTransaccionAsync` (el metodo detras de
  `GET /auditoria/transacciones/{transactionId}`). Es la metrica con la que se
  demuestra EAC-03: en el smoke test de carga (`CargaTransaccionTests`, ~1000
  eventos de una misma transaccion), la consulta responde en menos de 1
  segundo.
- `auditoria_eventos_descartados_total` — counter, incrementado por el chequeo
  defensivo de `EventId` ya existente que hace `RegistradorEventoAuditoria`
  antes de insertar. **No es el contador principal de duplicados**: el
  descarte real de duplicados en el camino normal lo hace
  `EventConsumerBase` (BuildingBlocks) *antes* de llamar a `ManejarAsync`, y
  esa clase no expone ningun hook para incrementar un contador propio del
  servicio que la extiende — se decidio explicitamente no tocar
  `BuildingBlocks` para agregar uno (ver decision registrada en la sesion de
  implementacion). Ese descarte SI queda observable, solo que bajo el nombre
  transversal `mensajes_duplicados_total` (emitido por
  `EventConsumerBase`), que en este proceso — Auditoria es el unico consumidor
  que corre en `auditoria-worker` — equivale exactamente a "duplicados
  descartados por Auditoria". `auditoria_eventos_descartados_total` cubre en
  cambio la segunda capa de proteccion (el `EventId` unico de
  `eventos_auditoria`), que en el flujo normal casi nunca se dispara porque el
  primer chequeo ya filtro el duplicado antes de llegar aca.

Ademas del propio proceso, `auditoria-worker` expone las metricas transversales
de siempre (`mensajes_consumidos_total`, `mensajes_duplicados_total`,
`/health/live`, `/health/ready`) via `UseSgoDefaults()`.

## Tests de integracion

`tests/SgoLogic.Auditoria.Tests` (Testcontainers, Postgres real, sin RabbitMQ:
la logica de insercion se prueba directo contra `RegistradorEventoAuditoria` y
`AuditoriaConsultaService`, sin pasar por el broker):

- `InsercionYRecuperacionTests` — envelope completo insertado y recuperado por
  `transactionId` y por `aggregateId`.
- `PermisosInmutabilidadTests` — el usuario de la aplicacion no puede `UPDATE`
  ni `DELETE` (42501); el trigger bloquea un `UPDATE` incluso del propietario
  (P0001); el usuario de la aplicacion SI puede leer y escribir
  `mensajes_procesados` (este ultimo existe porque la falta de ese grant
  especifico rompio el consumidor real la primera vez que se corrio todo el
  stack con `docker compose up`, y no lo detecto ningun test hasta entonces).
- `IdempotenciaTests` — el mismo evento "entregado" dos veces (dos llamadas a
  `RegistrarAsync` con el mismo `EventId`, cada una con su propio
  `SaveChangesAsync`) se registra una sola vez.
- `EventoDesconocidoTests` — un evento con un `EventType`/`data` que no existe
  en `Contracts` se persiste igual, sin errores de deserializacion.
- `CargaTransaccionTests` — smoke test de EAC-03: ~1000 eventos de una misma
  transaccion, la consulta por `transaction_id` responde en menos de 1
  segundo. La prueba de carga real (volumen de produccion, concurrencia) la
  hace el dueno de la historia aparte — ver "Evidencia medida de EAC-03 bajo
  volumen real" mas abajo.

## Evidencia medida de EAC-03 bajo volumen real (HU-025)

`CargaTransaccionTests` prueba EAC-03 con ~1000 eventos sintéticos de una sola
transacción. Esta sección es la medición que faltaba: contra la base real,
con el volumen que dejó la corrida de carga de HU-025 (ver
[`ESCALABILIDAD.md`](ESCALABILIDAD.md), sección 6), no contra un fixture de
test. Medido el 2026-09-20.

### Contexto de la medición

- Total de filas en `eventos_auditoria` en el momento de medir: **167,895**.
- Eventos por transacción en ese volumen: **8**, tanto el máximo como el
  valor típico — la saga de HU-025 genera la misma secuencia de eventos por
  orden, así que no hay variación entre transacciones en esta muestra (ver
  limitación más abajo).
- `transaction_id` usado: `000e8cfe-6a43-4055-934b-9d51188a0fd3`.

### Plan de ejecución de la consulta

```
Sort  (cost=32.15..32.17 rows=7 width=531) (actual time=0.756..0.757 rows=8 loops=1)
  Sort Key: occurred_at
  ->  Bitmap Heap Scan on eventos_auditoria  (actual time=0.134..0.694 rows=8 loops=1)
        Recheck Cond: ((transaction_id)::text = '000e8cfe-...'::text)
        Heap Blocks: exact=8
        ->  Bitmap Index Scan on "IX_eventos_auditoria_transaction_id"
              (actual time=0.043..0.043 rows=8 loops=1)
Planning Time: 1.396 ms
Execution Time: 0.845 ms
```

Lo relevante del plan: usa `IX_eventos_auditoria_transaction_id` (Bitmap
Index Scan), no un recorrido secuencial de la tabla, pese a las 167,895
filas. Tiempo de ejecución en el motor: **0.845 ms**.

### Latencia HTTP del endpoint

`GET /auditoria/transacciones/{transactionId}`, 5 corridas consecutivas con
`curl` contra el mismo `transaction_id`:

| # | Latencia | Nota |
|---|---|---|
| 1 | 0.013019 s | arranque en frío |
| 2 | 0.005361 s | |
| 3 | 0.005201 s | |
| 4 | 0.004470 s | |
| 5 | 0.004095 s | |

**Peor caso medido: ≈13 ms (0.013019 s), contra el umbral de HU-006 de
1,000 ms.** Se usa el peor caso, no el promedio, porque es el número que
importa para un criterio de aceptación de latencia: dos órdenes de magnitud
de margen frente al umbral, incluso contando el arranque en frío.

### Por qué el margen es estructural, no casualidad

La consulta resuelve por índice sobre `transaction_id`
(`IX_eventos_auditoria_transaction_id`) y devuelve solo los eventos de esa
transacción — el plan de ejecución lo confirma: `Bitmap Index Scan` seguido
de un `Heap Scan` que toca exactamente los 8 bloques de esa transacción, no
las 167,895 filas de la tabla. El costo de esta consulta escala con el
tamaño del índice (`O(log n)` para la búsqueda) y con el número de eventos
de la transacción consultada (el trabajo de traer y ordenar esas filas), no
con el total de filas de `eventos_auditoria`. Por eso el margen frente al
umbral de 1 segundo no es un resultado frágil que dependa de que la tabla
siga teniendo este tamaño: una transacción con muchos más de 8 eventos
seguiría resolviendo por el mismo índice y muy por debajo del umbral: el
volumen total de la bitácora puede seguir creciendo sin acercarse a 1
segundo, porque esta consulta nunca lo recorre completo.

### Limitación de la medición, con honestidad

Las 167,895 filas de esta muestra provienen todas de la corrida de carga de
HU-025, cuya saga genera exactamente 8 eventos por orden — por diseño de esa
prueba, no del sistema. Esto significa que **no se midió una transacción con
un volumen de eventos atípicamente alto**: todas las transacciones
disponibles en esta base tienen el mismo tamaño (8), así que esta medición
no dice nada por sí sola sobre cómo escala la latencia con transacciones de,
por ejemplo, 50 o 200 eventos. El argumento de la sección anterior (el costo
escala con el índice y el tamaño de la transacción, no con el total de la
tabla) es un argumento estructural sobre el plan de ejecución, no una
extrapolación medida — sostiene por qué debería seguir siendo rápido, pero
no reemplaza medirlo directamente si en el futuro aparece una transacción
con muchos más eventos.

### Cómo reproducir esta medición

Plan de ejecución (contra `db-auditoria`, mismo `transaction_id`):

```bash
docker compose exec -T db-auditoria psql -U auditoria_app -d sgo_auditoria -c \
  "EXPLAIN ANALYZE SELECT * FROM eventos_auditoria WHERE transaction_id = '000e8cfe-6a43-4055-934b-9d51188a0fd3' ORDER BY occurred_at;"
```

Latencia HTTP (ajustar el host/puerto si no es el del compose local; el
endpoint no pasa por el API Gateway, se expone directo en el puerto de host
de `auditoria-worker`):

```bash
for i in 1 2 3 4 5; do
  curl -s -o /dev/null -w "%{time_total}\n" \
    http://localhost:8093/auditoria/transacciones/000e8cfe-6a43-4055-934b-9d51188a0fd3
done
```

Conteo de filas y de eventos por transacción, para reproducir el contexto de
la medición:

```bash
docker compose exec -T db-auditoria psql -U auditoria_app -d sgo_auditoria -c \
  "SELECT count(*) FROM eventos_auditoria;"

docker compose exec -T db-auditoria psql -U auditoria_app -d sgo_auditoria -c \
  "SELECT transaction_id, count(*) FROM eventos_auditoria GROUP BY transaction_id ORDER BY count(*) DESC LIMIT 5;"
```

## Decisiones que quedaron fuera de alcance a proposito

- No se agrego ningun hook a `EventConsumerBase` para exponer un contador de
  duplicados por servicio (se pregunto explicitamente antes de tocar
  `BuildingBlocks` y se opto por no modificarlo).
- No hay indice GIN sobre `payload` (ver seccion de esquema).
- No se creo `documentacion/docs/PLANTILLA-SERVICIO.md`: no existia en
  ninguna rama al momento de implementar esta historia, asi que el servicio se
  modelo directamente sobre la estructura de `SgoLogic.Sample.Worker`.