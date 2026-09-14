# Observabilidad — Prometheus y Grafana

Stack de observabilidad del MVP: Prometheus raspa `/metrics` de cada servicio y
Grafana lo visualiza en dos tableros. Todo se levanta con `docker compose up
--build` desde `SGO-Logic/`, sin ningun paso manual de configuracion.

No hay Loki, Tempo, Alertmanager ni exporters de PostgreSQL/RabbitMQ: es
deliberado, es un MVP de curso y la instrumentacion ya expuesta (prometheus-net)
cubre lo que las HU y los EAC piden medir.

## Que se raspa y cada cuanto

Prometheus (`observabilidad/prometheus.yml`) tiene un job (`sgo-logic`) con un
target por cada servicio que llama `AddSgoDefaults()` / `UseSgoDefaults()` en su
`Program.cs`, que es lo que registra el middleware de prometheus-net y expone
`/metrics` en el puerto interno 8080:

| Servicio             | Target                    |
|----------------------|---------------------------|
| sample-api           | sample-api:8080           |
| sample-worker        | sample-worker:8080        |
| auth-api             | auth-api:8080             |
| ordenes-api          | ordenes-api:8080          |
| inventario-api       | inventario-api:8080       |
| pagos-api            | pagos-api:8080             |
| facturacion-api      | facturacion-api:8080      |
| notificaciones-api   | notificaciones-api:8080   |
| mock-mensajeria      | mock-mensajeria:8080      |
| consulta-estado-api  | consulta-estado-api:8080  |
| api-gateway          | api-gateway:8080          |
| web                  | web:8080                  |
| auditoria-worker     | auditoria-worker:8080     |

**payklik-mock y sat-mock quedan fuera a proposito**: son servicios standalone
que no referencian `SgoLogic.BuildingBlocks`, no llaman `AddSgoDefaults()` y no
exponen `/metrics`. Agregarlos como target los mostraria permanentemente en
rojo en el tablero de salud sin que eso sea un problema real.

Cada target lleva la etiqueta `servicio` (el nombre del contenedor en el
compose), que es la que usan todos los paneles de Grafana para separar series
por servicio.

El intervalo de scrape es **5 segundos** (`global.scrape_interval` en
`prometheus.yml`). La demo dura minutos, no dias: con el default de Prometheus
(1 minuto) o incluso 15 segundos, un pico como el crecimiento de
`outbox_pendientes` durante una caida de PayKlik de un par de minutos se ve
como una linea casi plana en vez de una curva.

## Metricas usadas (ya expuestas por el codigo, ninguna nueva)

Definidas en `src/BuildingBlocks/SgoLogic.BuildingBlocks/Metricas/MetricasSgo.cs`:

- `outbox_pendientes` (gauge) — filas Pendiente en la tabla outbox del servicio.
- `outbox_publicados_total` (counter) — mensajes de outbox confirmados por el broker.
- `outbox_fallidos_total` (counter) — mensajes que agotaron reintentos y fueron a `sgo.eventos.dlx`.
- `outbox_latencia_publicacion_segundos` (histogram) — segundos entre creacion y publicacion confirmada de una fila de outbox.
- `mensajes_consumidos_total` (counter) — mensajes procesados exitosamente por un `EventConsumerBase`.
- `mensajes_duplicados_total` (counter) — mensajes descartados por `eventId` ya procesado.
- `alertas_fiscales_levantadas_total` (counter, solo Ordenes/Facturacion) — no tiene panel dedicado en este MVP (dos tableros, no diez); queda disponible en Prometheus para quien quiera graficarla ad-hoc.

Mas las metricas HTTP estandar de `prometheus-net.AspNetCore` (`UseHttpMetrics()`),
confirmadas contra el binario del paquete (`prometheus-net.AspNetCore` 8.2.1):

- `http_requests_received_total` (counter) — labels `code`, `method`, `endpoint` (siempre presente), `action`/`controller` (solo con MVC/Razor Pages; los servicios de SGO-Logic usan minimal APIs, asi que estas dos quedan vacias).
- `http_request_duration_seconds` (histogram) — mismas labels.
- `http_requests_in_progress` (gauge) — no usada en los tableros actuales.

## Tableros

Ambos se aprovisionan por archivo (ver seccion siguiente) y aparecen ya
cargados la primera vez que se entra a Grafana, en la carpeta **SGO-Logic**.

### "SGO-Logic - Salud del sistema"

`observabilidad/grafana/provisioning/dashboards/json/salud-del-sistema.json`

1. **Estado de los servicios (1 = activo, 0 = caido)** — un stat por servicio
   con `up{job="sgo-logic"}`. Verde = ACTIVO, rojo = CAIDO. Con el stack recien
   levantado todos deben quedar en verde; si alguno queda en rojo o "SIN
   DATOS", ese contenedor no esta exponiendo `/metrics` o no arranco.
2. **Latencia HTTP por endpoint (p50, p95, p99)** — `histogram_quantile` sobre
   `http_request_duration_seconds_bucket`, agrupado por `servicio` y `endpoint` (la ruta real, p. ej. `/api/ordenes/{id}`).
   Sostiene **EAC-02** (Consulta de Estado responde en menos de 5 s) y
   **EAC-03** (Auditoria recupera evidencia en menos de 1 s): tras generar
   trafico contra esos dos servicios, sus curvas de p95/p99 deben quedarse muy
   por debajo de esos umbrales.
3. **Tasa de peticiones y de errores 5xx por servicio** — `rate()` sobre
   `http_requests_received_total`, total y filtrado a `code=~"5.."`. Sin
   trafico o sin fallos inducidos, la linea de errores 5xx debe quedarse en
   cero.

### "SGO-Logic - Mensajeria y resiliencia"

`observabilidad/grafana/provisioning/dashboards/json/mensajeria-resiliencia.json`

1. **Outbox pendientes por servicio** — `outbox_pendientes` directo (es un
   gauge, no necesita `rate()`). Es el panel que hace visible **EAC-01**: al
   detener RabbitMQ (o el mock de PayKlik) la fila de pendientes de
   ordenes-api/pagos-api debe crecer; al restablecerlo, debe drenar de vuelta
   a cero sin que ninguna fila termine en `outbox_fallidos_total` mientras
   quede dentro del limite de reintentos configurado (`OUTBOX_MAX_REINTENTOS`).
2. **Eventos de outbox publicados y fallidos por servicio** — `rate()` de
   `outbox_publicados_total` y `outbox_fallidos_total`. Durante una caida
   corta y dentro del limite de reintentos, fallidos debe quedarse en cero;
   solo sube si el broker estuvo caido mas tiempo del que tolera la politica
   de reintentos.
3. **Latencia de publicacion del outbox (p50, p95)** — `histogram_quantile`
   sobre `outbox_latencia_publicacion_segundos_bucket`. Durante la caida debe
   dispararse (los mensajes esperan en la tabla); al recuperarse vuelve a su
   valor normal.
4. **Mensajes consumidos y duplicados descartados por servicio** — `rate()` de
   `mensajes_consumidos_total` y `mensajes_duplicados_total`. Un valor
   distinto de cero en duplicados durante el drenado post-caida es la
   evidencia visual de que la idempotencia (`EventConsumerBase`) esta
   funcionando: los reintentos de publicacion no producen procesamiento doble.

## Como se aprovisiona (sin tocar nada por la UI)

```
observabilidad/
├── prometheus.yml                                   # scrape config, versionado
└── grafana/
    └── provisioning/
        ├── datasources/
        │   └── prometheus.yml                       # datasource "Prometheus" -> http://prometheus:9090
        └── dashboards/
            ├── sgo-logic.yml                         # provider: lee .json de ./json
            └── json/
                ├── salud-del-sistema.json
                └── mensajeria-resiliencia.json
```

En `docker-compose.yml`:

- `prometheus` monta `./observabilidad/prometheus.yml` en
  `/etc/prometheus/prometheus.yml` (solo lectura) y un volumen (`sgo_prometheus_data`)
  para los datos de series de tiempo.
- `grafana` monta **toda** la carpeta `./observabilidad/grafana/provisioning`
  en `/etc/grafana/provisioning` (solo lectura). Por eso los `.json` de los
  tableros viven *dentro* del arbol de `provisioning/` (en
  `dashboards/json/`) y no en una carpeta hermana: montar un segundo volumen
  anidado dentro de un mount de solo lectura falla al arrancar el contenedor
  (`read-only file system` al crear el punto de montaje), asi que todo cuelga
  de un unico bind mount.
- La contraseña de Grafana sale de `GRAFANA_ADMIN_USER` / `GRAFANA_ADMIN_PASSWORD`
  en `.env` (ver `.env.example`). Sin esas variables, Grafana usa su propio
  default (`admin`/`admin`), asi que conviene definirlas siempre.
- `grafana` depende de `prometheus` (`depends_on: condition: service_healthy`).

No hay `networks:` explicita en el compose: todos los servicios, incluidos
`prometheus` y `grafana`, comparten la red default que Docker Compose crea
para el proyecto, por lo que se resuelven entre si por nombre de servicio.

## Como agregar un servicio nuevo al scrape

Cuando el equipo cree el servicio de Logistica u otro nuevo que use
`AddSgoDefaults()`:

1. Agregar el servicio al `docker-compose.yml` como ya se hace con los demas
   (imagen/build, puerto, red default implicita).
2. Agregar una entrada a `static_configs.targets` en
   `observabilidad/prometheus.yml`, con el nombre del servicio y el puerto
   interno 8080, y su propia etiqueta `servicio`:

   ```yaml
   - targets: ["logistica-api:8080"]
     labels: { servicio: logistica-api }
   ```

3. Reiniciar el contenedor de Prometheus (`docker compose up -d prometheus`,
   o `docker compose restart prometheus` si la imagen no cambio) para que
   recargue el archivo montado.
4. Nada que tocar en Grafana: los paneles ya agrupan por la etiqueta
   `servicio` (`by (servicio)` / `by (servicio, endpoint)`), asi que el nuevo
   servicio aparece solo en cuanto Prometheus lo raspa.

Si el servicio nuevo **no** usa `AddSgoDefaults()` (por ejemplo, otro mock
standalone), no expone `/metrics` y no debe agregarse como target — igual que
`payklik-mock` y `sat-mock`.

## Verificacion

### 1. Levantar el stack

```bash
docker compose up --build
```

### 2. Confirmar que Prometheus ve todos los targets arriba

Entrar a `http://localhost:9090/targets`. Los trece targets del job
`sgo-logic` deben quedar en **UP** en cuanto sus contenedores respectivos
pasan el healthcheck (puede tomar unos segundos tras el arranque). Si alguno
queda en `DOWN`, revisar `docker compose logs <servicio>`.

### 3. Entrar a Grafana

`http://localhost:3000`, usuario/clave desde `.env`
(`GRAFANA_ADMIN_USER` / `GRAFANA_ADMIN_PASSWORD`). El datasource "Prometheus"
y la carpeta "SGO-Logic" con los dos tableros ya deben estar ahi, sin crear
nada a mano.

- En **Salud del sistema**, con el stack recien levantado y sin trafico
  todavia, el panel de estado debe mostrar los trece servicios en verde
  (ACTIVO); latencia y tasa de peticiones estaran en "No data" hasta el
  siguiente paso.
- En **Mensajeria y resiliencia**, `outbox_pendientes` deberia estar en cero o
  muy cerca, salvo que ya haya trafico corriendo.

### 4. Generar trafico

Cualquier flujo que cree ordenes sirve para poblar los paneles: por ejemplo,
correr la prueba de carga de EAC-01 ya existente en el repo
(`documentacion/docs/PRUEBA-CARGA-EAC01.md`) o simplemente crear unas cuantas
ordenes a mano contra `api-gateway`/`ordenes-api`. Tras uno o dos minutos de
trafico, "Salud del sistema" debe mostrar latencia y tasa de peticiones con
datos reales, y "Mensajeria y resiliencia" debe mostrar movimiento en
publicados/consumidos.

### 5. Provocar movimiento en `outbox_pendientes` (evidencia de EAC-01)

```bash
docker compose stop rabbitmq
```

Con trafico corriendo (o generando ordenes durante la caida), `outbox_pendientes`
de `ordenes-api` (y de cualquier otro servicio que este publicando en ese
momento) debe **crecer visiblemente** en el panel — los outbox no pueden
publicar sin broker, asi que las filas se acumulan en estado Pendiente en vez
de perderse. `outbox_fallidos_total` debe seguir en cero mientras la caida
dure menos que lo que tolera `OUTBOX_MAX_REINTENTOS`.

```bash
docker compose start rabbitmq
```

Al volver RabbitMQ, el poller de outbox retoma la publicacion y
`outbox_pendientes` debe **drenar** de vuelta hacia cero en los siguientes
segundos (el intervalo depende de `OUTBOX_POLLING_MS`), con un pico
correspondiente en la tasa de `outbox_publicados_total` mientras se pone al
dia.