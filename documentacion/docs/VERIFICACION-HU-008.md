# Guia de verificacion manual — HU-008 (Outbox transversal y mensajeria)

**Que es esto:** un guion de pruebas manuales, paso a paso, para validar con tus
propias manos el Outbox transversal, el publicador por polling, la idempotencia
de consumo, las metricas y el entorno de Docker Compose. No corre la suite
automatizada (esa ya existe y ya pasa) — cada prueba de aca es una peticion HTTP
real, una consulta SQL real, o una mirada directa a la consola de RabbitMQ.

**Dos bugs se encontraron y corrigieron durante el desarrollo, y valen una
mirada aparte:**

1. **El lock que se liberaba antes de tiempo.** La consulta `FOR UPDATE SKIP
   LOCKED` del publicador no estaba dentro de una transaccion explicita:
   Npgsql confirmaba el `SELECT` solo y liberaba el bloqueo de inmediato,
   dejando a una segunda instancia del publicador tomar las mismas filas.
   Se corrigio envolviendo todo el ciclo (leer, publicar, marcar) en una
   unica transaccion. Prueba dedicada: **Prueba 12**.
2. **El nack sin requeue.** El suscriptor le hacia `nack` a un mensaje fallido
   con `requeue: false`, lo que manda el mensaje a dead letter en el primer
   fallo sin que la cola cuente reintentos — `x-delivery-limit` quedaba sin
   efecto. Se corrigio cambiando a `requeue: true`, para que la cola quorum
   cuente cada redelivery y recien mande a dead letter al agotar el limite.
   Prueba dedicada: **Prueba 11**.

Si alguna de las dos regresara, es exactamente en las Pruebas 11 y 12 donde se
va a notar primero.

---

## 0. Antes de empezar

### 0.1 Herramientas necesarias

- Docker y Docker Compose v2 (`docker compose version`).
- `curl` (ya viene en macOS).
- `jq`, para leer JSON de forma legible. Si no lo tenes: `brew install jq`.
- `uuidgen`, ya viene en macOS (lo usamos en las Pruebas 7 y 12 para generar ids).

No necesitas `psql` instalado en tu maquina: todas las consultas SQL de esta
guia corren adentro del contenedor de PostgreSQL via `docker exec`.

### 0.2 Ubicacion

Todos los comandos de esta guia se ejecutan parado en la raiz del repositorio
(`SGO-Logic/`, donde esta `docker-compose.yml`):

```bash
cd "/Users/julio/Documents/USAC 8vo Semestre/Proyecto_AYD2/SGO-Logic"
```

### 0.3 Nombres de contenedor que usa esta guia

Docker Compose nombra los contenedores como `<proyecto>-<servicio>-1`. El
nombre de proyecto sale del nombre de la carpeta (`SGO-Logic` → `sgo-logic`).
Los nombres que usa esta guia son:

| Servicio en `docker-compose.yml` | Nombre real del contenedor |
|---|---|
| `db-operacional` | `sgo-logic-db-operacional-1` |
| `db-finanzas` | `sgo-logic-db-finanzas-1` |
| `db-auditoria` | `sgo-logic-db-auditoria-1` |
| `rabbitmq` | `sgo-logic-rabbitmq-1` |
| `redis` | `sgo-logic-redis-1` |
| `sample-api` | `sgo-logic-sample-api-1` |
| `sample-worker` | `sgo-logic-sample-worker-1` |

Si en tu maquina salen distintos (por ejemplo, si `docker-compose.yml` esta en
una carpeta con otro nombre, o si tenes `COMPOSE_PROJECT_NAME` seteado),
confirmalos con:

```bash
docker compose ps --format "{{.Service}}\t{{.Name}}"
```

y reemplaza el nombre en los comandos que siguen.

### 0.4 Credenciales (de `.env.example`, valores por defecto)

| Servicio | Usuario | Contrasena |
|---|---|---|
| PostgreSQL (los tres) | `sgo_admin` | `change_me` |
| RabbitMQ | `sgo_admin` | `change_me` |
| Redis | (sin usuario) | `change_me` |

Si tu `.env` local tiene otros valores, usa esos en su lugar en todos los
comandos.

### 0.5 Bases y puertos

| Base de datos | Contenedor | Usada por |
|---|---|---|
| `sgo_sample_api` | `sgo-logic-db-operacional-1` | Sample.Api |
| `sgo_sample_worker` | `sgo-logic-db-operacional-1` | Sample.Worker |

| Puerto en tu maquina | A donde va |
|---|---|
| `8081` | Sample.Api |
| `8082` | Sample.Worker |
| `5672` | RabbitMQ (AMQP) |
| `15672` | RabbitMQ (consola de administracion, http) |
| `6379` | Redis |

---

## Prueba 1 — Arranque limpio

**Que se verifica y por que importa:** que el `docker compose up` funciona de
punta a punta con una sola orden, que las dependencias (`depends_on` +
`condition: service_healthy`) hacen que cada servicio espere a lo que
necesita, y que los health checks de ambos servicios de ejemplo responden. Si
esto no arranca, ninguna prueba siguiente tiene sentido.

**Comandos:**

```bash
docker compose up -d --build
```

Espera unos 30-60 segundos (la primera vez incluye build de las dos imagenes)
y despues corre:

```bash
docker compose ps
```

**Resultado esperado:** siete servicios listados (`db-operacional`,
`db-finanzas`, `db-auditoria`, `rabbitmq`, `redis`, `sample-api`,
`sample-worker`), todos con estado `Up ... (healthy)`. Ninguno debe decir
`Restarting`, `Exit` ni quedar sin la palabra `healthy`.

Despues confirma los health checks HTTP directamente:

```bash
curl -sS -w "\n%{http_code}\n" http://localhost:8081/health/live
curl -sS -w "\n%{http_code}\n" http://localhost:8081/health/ready
curl -sS -w "\n%{http_code}\n" http://localhost:8082/health/live
curl -sS -w "\n%{http_code}\n" http://localhost:8082/health/ready
```

**Resultado esperado:** las cuatro devuelven el texto `Healthy` y el codigo
`200` al final.

**Que significa si falla:**
- Si algun contenedor queda en `Restarting` o `Exit`: mira sus logs
  (`docker compose logs <servicio>`) antes de asumir que es un bug de esta
  entrega — puede ser un puerto ocupado en tu maquina (ver seccion de
  "Problemas conocidos").
- Si los contenedores estan `healthy` pero `/health/ready` devuelve `503`:
  significa que ese servicio no puede conectar a su PostgreSQL o a RabbitMQ
  (el check valida ambos). Revisa `docker compose logs sample-api` o
  `sample-worker` buscando la excepcion.

---

## Prueba 2 — Escritura transaccional (entidad + fila de outbox en la misma transaccion)

**Que se verifica y por que importa:** que un POST no solo crea la entidad de
negocio, sino que en la MISMA transaccion de base de datos escribe la fila de
outbox con el envelope completo. Esta es la propiedad central del patron
Outbox: si algo llegara a fallar entre las dos escrituras, ninguna de las dos
deberia quedar.

**Comandos:**

```bash
curl -sS -X POST http://localhost:8081/api/items-ejemplo \
  -H "Content-Type: application/json" \
  -d '{"nombre":"prueba-transaccional"}'
```

Copia el `id` de la respuesta (o guardalo en una variable para las pruebas
siguientes):

```bash
ITEM_ID=$(curl -sS -X POST http://localhost:8081/api/items-ejemplo \
  -H "Content-Type: application/json" \
  -d '{"nombre":"prueba-transaccional-2"}' | jq -r '.id')
echo "$ITEM_ID"
```

Ahora mira la fila de negocio y la fila de outbox, las dos en la base
`sgo_sample_api`:

```bash
docker exec sgo-logic-db-operacional-1 psql -U sgo_admin -d sgo_sample_api -c \
  "SELECT id, nombre, creado_en FROM items_ejemplo WHERE id = '$ITEM_ID';"

docker exec sgo-logic-db-operacional-1 psql -U sgo_admin -d sgo_sample_api -c \
  "SELECT event_id, event_type, aggregate_id, transaction_id, estado, fecha_creacion FROM outbox_messages WHERE aggregate_id = '$ITEM_ID';"
```

**Resultado esperado:**
- La primera consulta devuelve exactamente una fila, con el `id` que
  respondio el POST y el `nombre` que enviaste.
- La segunda consulta tambien devuelve exactamente una fila, con
  `aggregate_id` igual a ese mismo `id`, `event_type = ejemplo.creado`, y
  `estado` en `Pendiente` (si corres la consulta muy rapido despues del POST)
  o `Publicado` (si paso mas de `OUTBOX_POLLING_MS`, 2000ms por defecto —
  ver Prueba 3).

Para ver el envelope completo tal como quedo guardado:

```bash
docker exec sgo-logic-db-operacional-1 psql -U sgo_admin -d sgo_sample_api -t -A -c \
  "SELECT payload FROM outbox_messages WHERE aggregate_id = '$ITEM_ID';" | jq .
```

**Resultado esperado:** un JSON con `eventId`, `eventType: "ejemplo.creado"`,
`eventVersion: 1`, `occurredAt`, `sequence: 1`, `transactionId`,
`causationId: null`, `aggregateType: "Ejemplo"`, `aggregateId` (igual a
`$ITEM_ID`), `source: "sample-api"`, y `data: {"nombre": "..."}`.

**Que significa si falla:**
- Si la fila de `items_ejemplo` existe pero NO hay fila en `outbox_messages`
  con ese `aggregate_id`: es una falla real, el corazon del patron Outbox no
  esta funcionando (revisa que `ItemEjemploService.CrearAsync` este llamando
  a `IOutboxWriter.Encolar` antes del `SaveChangesAsync`).
- Si el POST devuelve un error 500: revisa `docker compose logs sample-api`;
  probablemente la migracion de `outbox_messages` no se aplico (ver
  "Problemas conocidos").

---

## Prueba 3 — Publicacion (la fila pasa a Publicado y el evento llega al Worker)

**Que se verifica y por que importa:** que el `OutboxPublisher` realmente hace
polling, publica en `sgo.eventos` con confirms del broker, y solo entonces
marca la fila `Publicado`. Y que del otro lado, Sample.Worker consume ese
mismo evento.

**Comandos:**

Usando el `$ITEM_ID` de la Prueba 2 (o crea uno nuevo con el mismo POST de
antes), espera unos segundos y revisa el estado:

```bash
sleep 4
docker exec sgo-logic-db-operacional-1 psql -U sgo_admin -d sgo_sample_api -c \
  "SELECT event_id, estado, fecha_publicacion FROM outbox_messages WHERE aggregate_id = '$ITEM_ID';"
```

**Resultado esperado:** `estado = Publicado` y `fecha_publicacion` con un
timestamp (no `NULL`).

Ahora confirma que el Worker lo proceso y publico su propio evento derivado,
en la base `sgo_sample_worker`:

```bash
docker exec sgo-logic-db-operacional-1 psql -U sgo_admin -d sgo_sample_worker -c \
  "SELECT event_id, nombre_servicio, fecha_proceso FROM mensajes_procesados;"

docker exec sgo-logic-db-operacional-1 psql -U sgo_admin -d sgo_sample_worker -c \
  "SELECT event_id, event_type, aggregate_id, estado FROM outbox_messages WHERE aggregate_id = '$ITEM_ID';"
```

**Resultado esperado:** la primera consulta muestra una fila con
`nombre_servicio = sample-worker` (puede haber mas filas de pruebas
anteriores, esta bien). La segunda muestra una fila con
`event_type = EjemploProcesado`, `aggregate_id` igual al `$ITEM_ID` original,
y `estado = Publicado`.

Y en los logs, deberias ver ambos lados de la publicacion:

```bash
docker compose logs sample-api | grep "$ITEM_ID"
docker compose logs sample-worker | tail -50
```

Por ultimo, en la consola de RabbitMQ (`http://localhost:15672`, usuario
`sgo_admin`, contrasena `change_me`) anda a **Exchanges → sgo.eventos → Message
rates** y confirma que el contador de `publish` subio.

**Que significa si falla:**
- Si la fila queda `Pendiente` mas de 10-15 segundos con RabbitMQ sano: mira
  `docker compose logs sample-api` buscando `RabbitMQ no disponible` — si
  aparece con el broker arriba, es una falla real de conectividad, no del
  patron.
- Si `outbox_messages` en `sgo_sample_api` dice `Publicado` pero
  `mensajes_procesados` en `sgo_sample_worker` sigue vacio: revisa
  `docker compose logs sample-worker` — puede que la cola
  `sample-worker.ejemplo-creado` no se haya declarado (deberia declararse
  sola al arrancar el Worker).

---

## Prueba 4 — Propagacion de correlacion (transactionId y causationId)

**Que se verifica y por que importa:** que el evento derivado que publica el
Worker lleva el MISMO `transactionId` que el evento original, y el `eventId`
del evento original como su `causationId` — sin que ningun codigo de
aplicacion los haya tocado a mano. Esto es lo que permite reconstruir una
transaccion completa de punta a punta (EAC-03 del DDA).

**Comandos:**

Usando el `$ITEM_ID` de una prueba anterior (asegurate de haber esperado al
menos 5 segundos desde el POST para que el Worker ya haya publicado el
derivado):

```bash
echo "=== Evento original (sample-api) ==="
docker exec sgo-logic-db-operacional-1 psql -U sgo_admin -d sgo_sample_api -t -A -c \
  "SELECT payload FROM outbox_messages WHERE aggregate_id = '$ITEM_ID';" | jq .

echo "=== Evento derivado (sample-worker) ==="
docker exec sgo-logic-db-operacional-1 psql -U sgo_admin -d sgo_sample_worker -t -A -c \
  "SELECT payload FROM outbox_messages WHERE aggregate_id = '$ITEM_ID';" | jq .
```

**Resultado esperado:** compara los dos JSON:
- El campo `transactionId` es **identico** en ambos.
- El campo `causationId` del evento derivado (`EjemploProcesado`) es
  **igual al `eventId`** del evento original (`ejemplo.creado`).
- El `causationId` del evento original es `null` (nadie lo causo, fue una
  accion directa via HTTP).
- El `sequence` del derivado es el del original **mas uno** (empieza en 1,
  el derivado queda en 2).
- El `aggregateId` es el mismo en los dos.
- El `source` del original es `"sample-api"` y el del derivado
  `"sample-worker"`.

Para no tener que comparar a ojo, esta version hace la comparacion por vos:

```bash
TX_ORIGINAL=$(docker exec sgo-logic-db-operacional-1 psql -U sgo_admin -d sgo_sample_api -t -A -c \
  "SELECT payload FROM outbox_messages WHERE aggregate_id = '$ITEM_ID';" | jq -r '.transactionId')
EVENT_ID_ORIGINAL=$(docker exec sgo-logic-db-operacional-1 psql -U sgo_admin -d sgo_sample_api -t -A -c \
  "SELECT payload FROM outbox_messages WHERE aggregate_id = '$ITEM_ID';" | jq -r '.eventId')

TX_DERIVADO=$(docker exec sgo-logic-db-operacional-1 psql -U sgo_admin -d sgo_sample_worker -t -A -c \
  "SELECT payload FROM outbox_messages WHERE aggregate_id = '$ITEM_ID';" | jq -r '.transactionId')
CAUSATION_DERIVADO=$(docker exec sgo-logic-db-operacional-1 psql -U sgo_admin -d sgo_sample_worker -t -A -c \
  "SELECT payload FROM outbox_messages WHERE aggregate_id = '$ITEM_ID';" | jq -r '.causationId')

echo "transactionId igual: $([ "$TX_ORIGINAL" = "$TX_DERIVADO" ] && echo SI || echo NO)"
echo "causationId del derivado = eventId del original: $([ "$EVENT_ID_ORIGINAL" = "$CAUSATION_DERIVADO" ] && echo SI || echo NO)"
```

**Resultado esperado:** ambas lineas dicen `SI`.

**Que significa si falla:** si `transactionId` no coincide o `causationId` no
apunta al `eventId` original, revisa que `EventConsumerBase` este poblando
`EventContext.TransactionId`/`CausationId` ANTES de llamar a `ManejarAsync`, y
que `IOutboxWriter.Encolar` los este leyendo de ahi (nunca deberian estar
hardcodeados ni pasados a mano en el codigo de aplicacion).

---

## Prueba 5 — Broker caido (el POST sigue funcionando, nada se pierde)

**Que se verifica y por que importa:** que Sample.Api no depende de RabbitMQ
para responder un POST (la escritura es solo a PostgreSQL), que las filas
quedan `Pendiente` sin gastar reintentos mientras el broker esta abajo, y que
al recuperarse el broker todo lo pendiente se publica solo, sin perdida ni
duplicados.

**Comandos:**

```bash
docker compose stop rabbitmq
```

Con el broker frenado, manda un POST:

```bash
ITEM_ID_CAIDO=$(curl -sS -X POST http://localhost:8081/api/items-ejemplo \
  -H "Content-Type: application/json" \
  -d '{"nombre":"prueba-broker-caido"}' | jq -r '.id')
echo "$ITEM_ID_CAIDO"
```

**Resultado esperado:** el POST responde `201 Created` igual que siempre (si
queres verlo explicito, repeti con `-i` para ver el codigo de estado).

Espera unos segundos y confirma que la fila quedo pendiente, sin intentos
fallidos:

```bash
sleep 5
docker exec sgo-logic-db-operacional-1 psql -U sgo_admin -d sgo_sample_api -c \
  "SELECT event_id, estado, intentos_fallidos FROM outbox_messages WHERE aggregate_id = '$ITEM_ID_CAIDO';"
```

**Resultado esperado:** `estado = Pendiente` e `intentos_fallidos = 0`. El
`0` es importante: significa que la caida del broker NO cuenta como un
intento fallido del mensaje (eso solo pasa si el broker esta arriba pero
rechaza puntualmente una publicacion — ver Prueba 7).

Ahora levanta el broker de nuevo:

```bash
docker compose start rabbitmq
```

Espera a que vuelva a estar sano y confirma que se publico solo:

```bash
sleep 15
docker compose ps rabbitmq
docker exec sgo-logic-db-operacional-1 psql -U sgo_admin -d sgo_sample_api -c \
  "SELECT event_id, estado, fecha_publicacion FROM outbox_messages WHERE aggregate_id = '$ITEM_ID_CAIDO';"
```

`sgo_sample_api` y `sgo_sample_worker` son bases distintas (aunque compartan
contenedor): `psql` no puede resolver una subconsulta que cruce las dos, asi
que primero pedimos el `event_id` de una y con eso buscamos en la otra:

```bash
EVENT_ID_CAIDO=$(docker exec sgo-logic-db-operacional-1 psql -U sgo_admin -d sgo_sample_api -t -A -c \
  "SELECT event_id FROM outbox_messages WHERE aggregate_id = '$ITEM_ID_CAIDO';")

docker exec sgo-logic-db-operacional-1 psql -U sgo_admin -d sgo_sample_worker -c \
  "SELECT event_id, nombre_servicio, fecha_proceso FROM mensajes_procesados WHERE event_id = '$EVENT_ID_CAIDO';"
```

**Resultado esperado:** la fila de outbox paso a `Publicado` sola, sin que
hicieras nada mas que levantar el broker, y el Worker la proceso (aparece en
`mensajes_procesados`).

**Que significa si falla:**
- Si despues de levantar RabbitMQ la fila sigue `Pendiente` por mas de un
  minuto: revisa `docker compose logs sample-api` — el mensaje
  `RabbitMQ no disponible; se reintenta en el proximo ciclo de polling`
  deberia dejar de aparecer una vez que el broker esta arriba. Si sigue
  apareciendo con el broker sano, es una falla real de reconexion.
- Si `intentos_fallidos` es mayor a `0` apenas paraste RabbitMQ: revisa que
  el ciclo del publicador este abortando ANTES de tocar filas cuando no
  puede ni siquiera abrir un canal (esto es precisamente lo que corrige que
  una caida de infraestructura no consuma el presupuesto de reintentos de un
  mensaje).

---

## Prueba 6 — Idempotencia (el mismo evento entregado dos veces se procesa una sola vez)

**Que se verifica y por que importa:** el publicador entrega *al menos una
vez*. Todo consumidor tiene que deduplicar por `eventId`. Esta prueba fuerza
una redelivery real del mismo evento (tomando el payload ya publicado y
volviendo a publicarlo tal cual) y confirma que el Worker no lo procesa dos
veces.

**Comandos:**

Crea un item nuevo y espera a que se procese normalmente una vez:

```bash
ITEM_ID_DUP=$(curl -sS -X POST http://localhost:8081/api/items-ejemplo \
  -H "Content-Type: application/json" \
  -d '{"nombre":"prueba-duplicado"}' | jq -r '.id')

sleep 5

EVENT_ID_DUP=$(docker exec sgo-logic-db-operacional-1 psql -U sgo_admin -d sgo_sample_api -t -A -c \
  "SELECT event_id FROM outbox_messages WHERE aggregate_id = '$ITEM_ID_DUP';")
echo "$EVENT_ID_DUP"

docker exec sgo-logic-db-operacional-1 psql -U sgo_admin -d sgo_sample_worker -c \
  "SELECT COUNT(*) FROM mensajes_procesados WHERE event_id = '$EVENT_ID_DUP';"
```

**Resultado esperado de este primer paso:** el conteo da `1`.

Ahora toma el payload exacto que ya se publico y volve a publicarlo, tal
cual, contra el mismo exchange y la misma routing key — esto simula una
redelivery real del broker (por ejemplo, si un consumidor se cae despues de
procesar pero antes de confirmar):

```bash
PAYLOAD_DUP=$(docker exec sgo-logic-db-operacional-1 psql -U sgo_admin -d sgo_sample_api -t -A -c \
  "SELECT payload FROM outbox_messages WHERE aggregate_id = '$ITEM_ID_DUP';")

jq -n --arg rk "sample-api.item-ejemplo.creado" --arg payload "$PAYLOAD_DUP" \
  '{properties:{delivery_mode:2,content_type:"application/json",content_encoding:"utf-8"},routing_key:$rk,payload:$payload,payload_encoding:"string"}' \
  | curl -sS -u sgo_admin:change_me -X POST \
      http://localhost:15672/api/exchanges/%2f/sgo.eventos/publish \
      -H "Content-Type: application/json" -d @-
```

**Resultado esperado:** la respuesta es `{"routed":true}` (confirma que
RabbitMQ encontro la cola de Sample.Worker ligada a esa routing key).

Espera unos segundos y confirma que NO se proceso dos veces:

```bash
sleep 3
docker exec sgo-logic-db-operacional-1 psql -U sgo_admin -d sgo_sample_worker -c \
  "SELECT COUNT(*) FROM mensajes_procesados WHERE event_id = '$EVENT_ID_DUP';"
```

**Resultado esperado:** el conteo sigue dando `1` (no `2`).

Y confirma que la metrica de duplicados subio:

```bash
curl -sS http://localhost:8082/metrics | grep "^mensajes_duplicados_total"
```

**Resultado esperado:** un valor mayor o igual a `1` (va a ser exactamente
`1` si esta es la primera vez que corres esta prueba desde el ultimo
`docker compose up`, o mas si ya la corriste antes).

Tambien podes verlo en los logs del Worker:

```bash
docker compose logs sample-worker | grep "Mensaje duplicado descartado"
```

**Resultado esperado:** una linea mencionando el `$EVENT_ID_DUP`.

**Que significa si falla:**
- Si el conteo en `mensajes_procesados` pasa a `2`: es una falla real de
  idempotencia — revisa que `EventConsumerBase` este consultando
  `mensajes_procesados` por `EventId` + `NombreServicio` ANTES de llamar a
  `ManejarAsync`.
- Si `{"routed":true}` nunca aparece (da `{"routed":false}`): la cola de
  Sample.Worker no esta ligada a esa routing key; revisa que el Worker haya
  arrancado bien (Prueba 1) antes de reintentar.

---

## Prueba 7 — Dead-letter por agotar reintentos de publicacion

**Que se verifica y por que importa:** cuando el propio publicador no logra
confirmar una publicacion en repetidos intentos, la fila tiene que pasar a
`Fallido` Y el mensaje tiene que terminar en `sgo.eventos.dlq` — sin
reintentar para siempre. Como forzar que el broker rechace una publicacion de
verdad (sin apagarlo, que ya se probo en la Prueba 5) no es directo desde
afuera, esta prueba inserta a mano una fila de outbox que apunta a un
exchange que no existe: eso obliga al publicador a fallar en cada intento
real, exactamente el mismo camino de codigo que tomaria un fallo genuino del
broker.

**Comandos:**

```bash
DLQ_ROW_ID=$(uuidgen)
DLQ_EVENT_ID=$(uuidgen)
DLQ_TX_ID=$(uuidgen)

docker exec sgo-logic-db-operacional-1 psql -U sgo_admin -d sgo_sample_api -c "
INSERT INTO outbox_messages
  (id, event_id, event_type, payload, transaction_id, exchange, routing_key, aggregate_type, aggregate_id, fecha_creacion, intentos_fallidos, estado)
VALUES
  ('$DLQ_ROW_ID', '$DLQ_EVENT_ID', 'Prueba.DeadLetter', '{}', '$DLQ_TX_ID', 'exchange-que-no-existe', 'prueba.deadletter.forzado', 'Prueba', 'prueba-dlq-manual', now(), 0, 'Pendiente');
"
```

Con `OUTBOX_MAX_REINTENTOS=5` (valor por defecto de `.env.example`) y el
backoff exponencial (2, 4, 8, 16 segundos entre intentos), el ciclo completo
tarda alrededor de un minuto. Espera generosamente y despues revisa:

```bash
sleep 90
docker exec sgo-logic-db-operacional-1 psql -U sgo_admin -d sgo_sample_api -c \
  "SELECT event_id, estado, intentos_fallidos FROM outbox_messages WHERE id = '$DLQ_ROW_ID';"
```

**Resultado esperado:** `estado = Fallido` e `intentos_fallidos = 5`.

Ahora confirma que llego a la dead letter queue:

```bash
curl -sS -u sgo_admin:change_me http://localhost:15672/api/queues/%2f/sgo.eventos.dlq | jq '{name, messages, messages_ready}'
```

**Resultado esperado:** `messages` (o `messages_ready`) en al menos `1`.
Tambien podes verlo entrando a `http://localhost:15672/#/queues/%2F/sgo.eventos.dlq`
y mirando la fila de mensajes listos.

Y la metrica correspondiente:

```bash
curl -sS http://localhost:8081/metrics | grep "^outbox_fallidos_total"
```

**Resultado esperado:** un valor mayor o igual a `1`.

Y en los logs:

```bash
docker compose logs sample-api | grep "agoto sus reintentos"
```

**Resultado esperado:** una linea mencionando el `$DLQ_EVENT_ID` y
`sgo.eventos.dlx`.

**Limpieza de esta prueba en particular** (opcional, para no dejar la fila
`Fallido` dando vueltas):

```bash
docker exec sgo-logic-db-operacional-1 psql -U sgo_admin -d sgo_sample_api -c \
  "DELETE FROM outbox_messages WHERE id = '$DLQ_ROW_ID';"
```

Para vaciar la DLQ, entra a la consola (`http://localhost:15672` →
**Queues** → `sgo.eventos.dlq`) y usa el boton **Purge Messages**.

**Que significa si falla:**
- Si la fila queda `Pendiente` para siempre y nunca pasa a `Fallido`: revisa
  que estes usando un nombre de exchange que realmente no existe (si por
  casualidad coincide con uno real, el publicador la va a publicar sin
  problema).
- Si pasa a `Fallido` pero `sgo.eventos.dlq` no recibe nada: revisa que el
  binding entre `sgo.eventos.dlx` (fanout) y `sgo.eventos.dlq` siga existiendo
  (Prueba 8).

---

## Prueba 8 — Topologia (los cinco elementos declarados en RabbitMQ)

**Que se verifica y por que importa:** que la topologia transversal se
declaro completa y correctamente al arrancar los servicios: los cuatro
exchanges y la dead letter queue, con los tipos y argumentos correctos.

**Comandos:** abri `http://localhost:15672` en el navegador (usuario
`sgo_admin`, contrasena `change_me`).

### Exchanges

Anda a la pestana **Exchanges**. Busca (ignora los que empiezan con `amq.` y
el exchange sin nombre, esos son de RabbitMQ, no nuestros):

| Nombre | Tipo esperado | Durable |
|---|---|---|
| `sgo.eventos` | `topic` | si |
| `sgo.sat` | `direct` | si |
| `sgo.erp` | `direct` | si |
| `sgo.eventos.dlx` | `fanout` | si |

O desde la terminal:

```bash
curl -sS -u sgo_admin:change_me http://localhost:15672/api/exchanges/%2f \
  | jq '.[] | select(.name | startswith("sgo.")) | {name, type, durable}'
```

**Resultado esperado:** exactamente esos cuatro, con esos tipos.

### Colas

Anda a la pestana **Queues**. Deberias ver (ademas de cualquiera que hayas
creado a mano en pruebas anteriores):

| Nombre | Tipo | Bindeada a |
|---|---|---|
| `sgo.eventos.dlq` | quorum | `sgo.eventos.dlx` |
| `sample-worker.ejemplo-creado` | quorum | `sgo.eventos` con routing key `sample-api.item-ejemplo.creado` |

Sample.Api no declara ninguna cola propia (no consume eventos, solo publica),
asi que no deberias ver ninguna cola con nombre `sample-api.*`.

Hace click en `sample-worker.ejemplo-creado` y mira la seccion **Arguments**:

**Resultado esperado:**
- `x-queue-type: quorum`
- `x-dead-letter-exchange: sgo.eventos.dlx`
- `x-delivery-limit: 5`

O desde la terminal:

```bash
curl -sS -u sgo_admin:change_me http://localhost:15672/api/queues/%2f/sample-worker.ejemplo-creado \
  | jq '{name, arguments}'

curl -sS -u sgo_admin:change_me http://localhost:15672/api/queues/%2f/sgo.eventos.dlq \
  | jq '{name, arguments}'
```

**Resultado esperado:** ambas con `"x-queue-type": "quorum"` en `arguments`.
`sgo.eventos.dlq` no deberia tener `x-dead-letter-exchange` ni TTL (es el
final de la cadena, no reenvia a ningun otro lado).

**Que significa si falla:**
- Si falta algun exchange: revisa que `TopologiaInicialHostedService` haya
  corrido al arrancar (deberia verse en los logs de arranque de cualquiera de
  los dos servicios).
- Si `sample-worker.ejemplo-creado` no tiene `x-queue-type: quorum`: es una
  falla real — todo el mecanismo de reintentos de consumo (Prueba 11) depende
  de que sea quorum, las colas clasicas ignoran `x-delivery-limit` en
  silencio.

---

## Prueba 9 — Aislamiento de bases (nadie escribe en la base de otro)

**Que se verifica y por que importa:** ADR-006 y la regla dura de la
plantilla de servicio: cada servicio tiene su propia base, y ninguno puede
tocar las tablas de otro. Sample.Api y Sample.Worker corren en el mismo
contenedor de PostgreSQL (`db-operacional`) pero en bases separadas
(`sgo_sample_api` y `sgo_sample_worker`).

**Comandos:**

Lista las tablas de cada base:

```bash
echo "=== Tablas en sgo_sample_api ==="
docker exec sgo-logic-db-operacional-1 psql -U sgo_admin -d sgo_sample_api -c "\dt"

echo "=== Tablas en sgo_sample_worker ==="
docker exec sgo-logic-db-operacional-1 psql -U sgo_admin -d sgo_sample_worker -c "\dt"
```

**Resultado esperado:**
- `sgo_sample_api` tiene: `items_ejemplo`, `outbox_messages`, y la tabla
  tecnica `__EFMigrationsHistory`. **No** tiene `mensajes_procesados`
  (Sample.Api no consume eventos).
- `sgo_sample_worker` tiene: `outbox_messages`, `mensajes_procesados`, y
  `__EFMigrationsHistory`. **No** tiene `items_ejemplo` (esa entidad es solo
  de Sample.Api).

Confirma ademas que, conectado a una base, ni siquiera podes consultar una
tabla que solo existe en la otra (aislamiento real de PostgreSQL, no solo
convencion):

```bash
docker exec sgo-logic-db-operacional-1 psql -U sgo_admin -d sgo_sample_api -c \
  "SELECT * FROM mensajes_procesados LIMIT 1;"
```

**Resultado esperado:** un error de PostgreSQL,
`relation "mensajes_procesados" does not exist` — exactamente lo que
demuestra que son bases distintas, no un esquema compartido con menos
tablas.

**Que significa si falla:**
- Si `sgo_sample_api` tiene `mensajes_procesados`, o `sgo_sample_worker`
  tiene `items_ejemplo`: alguien aplico la migracion equivocada contra la
  base equivocada — revisa las cadenas de conexion en
  `docker-compose.yml` (`ConnectionStrings__Postgres` de cada servicio) y
  que cada `Program.cs` migre solo su propio `DbContext`.

---

## Prueba 10 — Metricas

**Que se verifica y por que importa:** que las seis metricas pedidas
existen, con esos nombres exactos, en formato Prometheus, y reflejan lo que
paso en las pruebas anteriores. Corre esto DESPUES de haber corrido al menos
las Pruebas 2, 3, 6 y 7, para que los contadores tengan algo que mostrar.

**Comandos:**

```bash
echo "=== Metricas de Sample.Api ==="
curl -sS http://localhost:8081/metrics | grep -E "^(outbox_pendientes|outbox_publicados_total|outbox_fallidos_total|outbox_latencia_publicacion_segundos_(sum|count)|mensajes_consumidos_total|mensajes_duplicados_total)"

echo "=== Metricas de Sample.Worker ==="
curl -sS http://localhost:8082/metrics | grep -E "^(outbox_pendientes|outbox_publicados_total|outbox_fallidos_total|outbox_latencia_publicacion_segundos_(sum|count)|mensajes_consumidos_total|mensajes_duplicados_total)"
```

**Resultado esperado**, si corriste las pruebas anteriores en orden:

| Metrica | Tipo | Donde deberia tener valor | Valor esperado |
|---|---|---|---|
| `outbox_pendientes` | gauge | ambos | `0` en un momento de calma (nadie con filas Pendiente sin resolver) |
| `outbox_publicados_total` | counter | ambos | `>= 1` en cada uno (cada POST exitoso deja un publicado en sample-api, cada consumo exitoso deja un publicado en sample-worker) |
| `outbox_fallidos_total` | counter | sample-api | `>= 1` si corriste la Prueba 7 |
| `outbox_latencia_publicacion_segundos_count` | histogram | ambos | igual a la cantidad de mensajes publicados por ese servicio |
| `mensajes_consumidos_total` | counter | sample-worker | `>= 1` (una vez por cada evento nuevo que proceso, sin contar duplicados) |
| `mensajes_duplicados_total` | counter | sample-worker | `>= 1` si corriste la Prueba 6 |

**Que significa si falla:**
- Si alguna metrica no aparece en absoluto (ni con valor `0`): revisa que
  `UseSgoDefaults()` este llamado en el `Program.cs` de ese servicio (expone
  `/metrics`), y que el servicio haya procesado al menos un mensaje (algunas
  metricas de Prometheus no aparecen hasta el primer incremento).
- Si `outbox_pendientes` nunca vuelve a `0`: puede haber una fila trabada —
  revisa `SELECT estado, COUNT(*) FROM outbox_messages GROUP BY estado;` en
  la base correspondiente.

---

## Prueba 11 — Verificacion especifica: nack con requeue (bug corregido #2)

**Por que esta prueba existe:** el suscriptor original le hacia `nack` a un
mensaje fallido con `requeue: false`. Eso manda el mensaje a dead letter
apenas falla la PRIMERA vez, sin que la cola cuente nada — `x-delivery-limit`
quedaba completamente sin efecto. La correccion fue pasar a
`requeue: true`, para que la propia cola quorum cuente cada redelivery y
recien mande a dead letter al superar el limite. Esta prueba fuerza un
mensaje que el Worker no puede procesar (JSON invalido) y confirma que se
redelivera VARIAS veces antes de llegar a la DLQ — si solo aparece una vez en
los logs y cae directo a la dead letter, el bug volvio.

**Comandos:**

Primero, mira cuantos mensajes tiene ahora mismo `sgo.eventos.dlq` (para
comparar el antes/despues):

```bash
curl -sS -u sgo_admin:change_me http://localhost:15672/api/queues/%2f/sgo.eventos.dlq | jq '.messages'
```

Publica un mensaje con un payload que no es JSON valido, contra la misma
routing key que consume Sample.Worker:

```bash
jq -n --arg rk "sample-api.item-ejemplo.creado" \
  '{properties:{delivery_mode:2,content_type:"application/json",content_encoding:"utf-8"},routing_key:$rk,payload:"esto no es un envelope valido",payload_encoding:"string"}' \
  | curl -sS -u sgo_admin:change_me -X POST \
      http://localhost:15672/api/exchanges/%2f/sgo.eventos/publish \
      -H "Content-Type: application/json" -d @-
```

**Resultado esperado de la publicacion:** `{"routed":true}`.

Espera unos segundos (las redeliveries son casi inmediatas, no hay backoff de
por medio en este camino) y cuenta cuantas veces el Worker intento
deserializarlo:

```bash
sleep 5
docker compose logs sample-worker | grep -c "Mensaje no deserializable"
```

**Resultado esperado:** el conteo deberia ser **5** (el `x-delivery-limit`
configurado por defecto), no `1`. Si te da `1`, es exactamente el sintoma del
bug: el mensaje se fue a dead letter en el primer intento sin que la cola
llegara a contar redeliveries.

Confirma que efectivamente llego a la DLQ y que el contador subio respecto a
la medicion de antes:

```bash
curl -sS -u sgo_admin:change_me http://localhost:15672/api/queues/%2f/sgo.eventos.dlq | jq '.messages'
```

**Resultado esperado:** un mensaje mas que el numero que anotaste al
principio de esta prueba.

Tambien podes verlo en vivo desde la consola: `http://localhost:15672` →
**Queues** → `sample-worker.ejemplo-creado` → pestana **Get messages**
(si llegas a tiempo, antes de que se termine de redeliverar) muestra el
campo `redelivered: true` y un `x-delivery-count` creciente en las
`properties` de cada intento.

**Que significa si falla:**
- Si el conteo de "Mensaje no deserializable" da `1` en vez de `5`: revisa
  `SuscriptorRabbitMq.cs` — el `BasicNack` tiene que llamarse con
  `requeue: true`, no `false`. Este es exactamente el bug que se corrigio;
  si volviste a ver esto, algo lo revirtio.
- Si el mensaje se queda dando vueltas para siempre sin llegar nunca a la
  DLQ: revisa que la cola `sample-worker.ejemplo-creado` tenga
  `x-delivery-limit` seteado (Prueba 8) y que `sgo.eventos.dlx` este
  bindeado a `sgo.eventos.dlq`.

**Limpieza:** purga `sgo.eventos.dlq` desde la consola si no queres dejar
este mensaje de prueba ahi (boton **Purge Messages**).

---

## Prueba 12 — Verificacion especifica: el lock liberado antes de tiempo (bug corregido #1)

**Por que esta prueba existe y su alcance real:** el bug era que
`FOR UPDATE SKIP LOCKED` no estaba dentro de una transaccion explicita en
`OutboxPublisher`, asi que Npgsql confirmaba el `SELECT` solo (sin
transaccion abierta) y liberaba el bloqueo de inmediato — dejando a una
segunda instancia del publicador tomar las mismas filas que la primera ya
habia "reclamado" pero todavia no habia terminado de publicar. La correccion
envuelve todo el ciclo (`SELECT ... FOR UPDATE SKIP LOCKED`, publicar, y el
`UPDATE` que marca `Publicado`) en una unica transaccion.

**Importante — esto es una verificacion parcial, no la reproduccion
completa.** Reproducir el escenario exacto (dos instancias reales de
`OutboxPublisher` corriendo a la vez contra la misma tabla) requeriria correr
dos replicas de `sample-api`, y el `docker-compose.yml` actual mapea el
puerto 8081 fijo, asi que `docker compose up --scale sample-api=2` va a fallar
con un error de puerto duplicado — escalarlo de verdad implica editar
`docker-compose.yml` (quitarle el mapeo de puerto fijo), y esta guia no
modifica codigo. Esa reproduccion completa con dos publicadores reales contra
Postgres ya esta cubierta por la suite automatizada (el test que valida
exactamente esto se llama `SkipLockedTests` y ya esta en verde).

Lo que SI podes verificar aca, con tus propias manos, es el mecanismo del que
depende la correccion: que `FOR UPDATE SKIP LOCKED` efectivamente respeta una
transaccion que sigue abierta. Abriendo dos sesiones de `psql` en paralelo
podes ver a Postgres saltarse filas bloqueadas por una transaccion que
todavia no cerro — que es exactamente la garantia que el codigo corregido
ahora usa durante todo el ciclo de publicacion (podes confirmar en el codigo,
archivo `src/BuildingBlocks/SgoLogic.BuildingBlocks/Outbox/OutboxPublisher.cs`,
que el `SELECT ... FOR UPDATE SKIP LOCKED`, la publicacion y el
`SaveChangesAsync` + `CommitAsync` estan todos dentro del mismo
`BeginTransactionAsync`).

**Comandos:**

Primero, para que haya filas `Pendiente` con las que jugar sin interferir con
el publicador real (que las va a tomar en cuanto pueda), para el
`sample-api` mientras haces esta prueba puntual:

```bash
docker compose stop sample-api
```

Inserta un par de filas de prueba directo en la base (generamos los ids en la
misma terminal, con `uuidgen`, para no depender de ninguna funcion especifica
de PostgreSQL):

```bash
LOCK_ID_1=$(uuidgen); LOCK_EVENT_1=$(uuidgen); LOCK_TX_1=$(uuidgen)
LOCK_ID_2=$(uuidgen); LOCK_EVENT_2=$(uuidgen); LOCK_TX_2=$(uuidgen)

docker exec sgo-logic-db-operacional-1 psql -U sgo_admin -d sgo_sample_api -c "
INSERT INTO outbox_messages
  (id, event_id, event_type, payload, transaction_id, exchange, routing_key, aggregate_type, aggregate_id, fecha_creacion, intentos_fallidos, estado)
VALUES
  ('$LOCK_ID_1', '$LOCK_EVENT_1', 'Prueba.Lock', '{}', '$LOCK_TX_1', 'sgo.eventos', 'prueba.lock.uno', 'Prueba', 'prueba-lock-1', now(), 0, 'Pendiente'),
  ('$LOCK_ID_2', '$LOCK_EVENT_2', 'Prueba.Lock', '{}', '$LOCK_TX_2', 'sgo.eventos', 'prueba.lock.dos', 'Prueba', 'prueba-lock-2', now(), 0, 'Pendiente');
"
```

Ahora abri **una terminal nueva** (dejala aparte, la vas a necesitar abierta)
y arranca ahi una sesion interactiva de `psql` contra `sgo_sample_api`:

```bash
docker exec -it sgo-logic-db-operacional-1 psql -U sgo_admin -d sgo_sample_api
```

Dentro de esa sesion, empeza una transaccion y reclama las filas — pero **no
la cierres todavia** (ni `COMMIT` ni `ROLLBACK`):

```sql
BEGIN;
SELECT id, aggregate_id FROM outbox_messages
  WHERE estado = 'Pendiente' AND aggregate_id LIKE 'prueba-lock-%'
  ORDER BY fecha_creacion
  FOR UPDATE SKIP LOCKED;
```

**Resultado esperado de este paso:** devuelve las dos filas (`prueba-lock-1`
y `prueba-lock-2`).

Ahora, en tu terminal ORIGINAL (sin cerrar la otra sesion), corre la misma
consulta desde una conexion nueva:

```bash
docker exec sgo-logic-db-operacional-1 psql -U sgo_admin -d sgo_sample_api -c "
SELECT id, aggregate_id FROM outbox_messages
  WHERE estado = 'Pendiente' AND aggregate_id LIKE 'prueba-lock-%'
  ORDER BY fecha_creacion
  FOR UPDATE SKIP LOCKED;
"
```

**Resultado esperado:** esta segunda consulta devuelve **cero filas**. Las
dos filas siguen bloqueadas por la primera sesion, que todavia no hizo
`COMMIT` ni `ROLLBACK` — exactamente el comportamiento que evita que dos
publicadores tomen la misma fila, siempre y cuando (como hace el codigo
corregido) la transaccion siga abierta durante todo el trabajo, no solo
durante el `SELECT`.

Volve a la sesion interactiva que dejaste abierta y liberala:

```sql
ROLLBACK;
```

(Usamos `ROLLBACK`, no `COMMIT`, para no marcar nada como publicado de
verdad — estas filas de prueba las vamos a borrar igual.)

Salí de esa sesion (`\q`) y en tu terminal original limpia las filas de
prueba y volve a levantar `sample-api`:

```bash
docker exec sgo-logic-db-operacional-1 psql -U sgo_admin -d sgo_sample_api -c \
  "DELETE FROM outbox_messages WHERE aggregate_id IN ('prueba-lock-1', 'prueba-lock-2');"

docker compose start sample-api
```

**Que significa si falla:**
- Si la segunda consulta (con la primera sesion todavia abierta) devuelve
  las mismas dos filas en vez de cero: algo esta mal en tu Postgres o en como
  corriste la prueba (revisa que de verdad no hayas hecho `COMMIT` ni
  `ROLLBACK` en la primera sesion todavia) — no es un problema del codigo de
  este repositorio, `SKIP LOCKED` es un mecanismo de PostgreSQL, no algo que
  implementamos nosotros.
- Esto NO prueba que el bug este arreglado en el propio `OutboxPublisher` —
  solo prueba que el mecanismo de Postgres del que depende la correccion
  funciona como se espera. Para la confirmacion completa de que
  `OutboxPublisher` lo usa correctamente durante todo su ciclo, la evidencia
  es doble: la lectura del codigo (la transaccion explicita que envuelve
  `SELECT` + publicar + `UPDATE`) y el test automatizado `SkipLockedTests`
  (dos instancias reales de `OutboxPublisher`, mismo Postgres, verificado en
  verde).

---

## Tabla de verificacion

Copia esta tabla y pegala como evidencia en el tablero, con el resultado de
cada prueba.

| # | Prueba | Resultado (OK / Falla / No corrida) | Notas |
|---|---|---|---|
| 1 | Arranque limpio | | |
| 2 | Escritura transaccional | | |
| 3 | Publicacion | | |
| 4 | Propagacion de correlacion | | |
| 5 | Broker caido | | |
| 6 | Idempotencia | | |
| 7 | Dead-letter (agotar reintentos de publicacion) | | |
| 8 | Topologia | | |
| 9 | Aislamiento de bases | | |
| 10 | Metricas | | |
| 11 | Bug corregido: nack con requeue | | |
| 12 | Bug corregido: lock liberado antes de tiempo | | |

---

## Problemas conocidos y como distinguirlos de una falla real

| Sintoma | Es normal si... | Es una falla real si... |
|---|---|---|
| Un contenedor tarda en pasar de `Up` a `Up (healthy)` | Pasaron menos de 30-40 segundos desde el `docker compose up` (RabbitMQ en particular tarda unos segundos en levantar el nodo de Erlang) | Pasaron varios minutos y sigue sin decir `healthy`, o dice `unhealthy` |
| `docker compose up` falla con `port is already allocated` | Ya tenes algo corriendo en 8081, 8082, 5672, 15672 o 6379 en tu maquina (otro proyecto, un RabbitMQ/Postgres local, etc.) | El puerto esta libre y aun asi falla — revisa el mensaje de error completo |
| Una fila de outbox tarda unos segundos en pasar de `Pendiente` a `Publicado` | Pasaron menos de `OUTBOX_POLLING_MS` (2000ms por defecto) desde que se creo — el publicador hace polling, no es instantaneo | Pasaron mas de 10-15 segundos con RabbitMQ sano y sigue `Pendiente` |
| Un POST devuelve error 500 apenas reconstruiste una imagen | Es la primera vez que corre esa imagen y la migracion todavia no se aplico del todo (`Database.Migrate()` corre al arrancar, antes de aceptar trafico — si ves el error es que el healthcheck paso pero la migracion seguia en curso, poco probable pero posible en una maquina lenta) | El error persiste despues de reiniciar el contenedor (`docker compose restart sample-api`) |
| Cambios de codigo no se reflejan al correr `docker compose up -d` | Te olvidaste del `--build`: sin eso, Compose reusa la imagen ya construida | Corriste `--build` y el cambio sigue sin aparecer — revisa que estes editando el proyecto correcto |
| `psql` pide contrasena en vez de conectar directo | No estas usando `docker exec` (estas intentando conectar desde tu maquina directo al puerto del contenedor) — todos los comandos de esta guia usan `docker exec ... psql` a proposito, adentro del contenedor la autenticacion local no pide contrasena | Usando `docker exec` igual te la pide — revisa que el nombre de usuario (`-U sgo_admin`) sea correcto |
| La consola de RabbitMQ (`localhost:15672`) tarda en cargar justo despues del arranque | Recien arranco el contenedor, dale unos segundos mas | Sigue sin responder pasado un minuto con el contenedor `healthy` |

---

## Limpieza

Para dejar todo detenido pero conservar los datos (podes retomar despues sin
perder lo que probaste):

```bash
docker compose stop
```

Para bajar los contenedores sin perder los volumenes (datos de Postgres,
RabbitMQ):

```bash
docker compose down
```

Para empezar completamente de cero — borra tambien los volumenes (todas las
bases y las colas quedan vacias en el proximo arranque):

```bash
docker compose down -v
```

Si ademas queres forzar que se reconstruyan las imagenes desde cero la
proxima vez (por ejemplo, si sospechas de una imagen vieja en cache):

```bash
docker compose down -v
docker rmi sgo-logic-sample-api sgo-logic-sample-worker
docker compose up -d --build
```