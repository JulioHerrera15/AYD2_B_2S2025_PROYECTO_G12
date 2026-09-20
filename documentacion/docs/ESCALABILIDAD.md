# Escalabilidad horizontal por servicio (HU-025 / EAC-04)

Esta historia de usuario no agrega funcionalidad de negocio: demuestra, con
evidencia medida, algo que el diseño ya soportaba desde que se construyeron
el outbox (`FOR UPDATE SKIP LOCKED`), las colas quorum de RabbitMQ y la
deduplicación por `mensajes_procesados` (ver
[`BuildingBlocks`](../../SGO-Logic/src/BuildingBlocks/SgoLogic.BuildingBlocks)).
Lo que faltaba no era construirlo, era probarlo: levantar varias réplicas de
un servicio del Backbone, generar carga concurrente real y confirmar que el
trabajo se reparte sin duplicarse ni perderse.

**Criterios de aceptación de HU-025:**
- Cada servicio del Backbone escala horizontalmente de forma independiente
  según su propio patrón de carga.
- La capacidad base es ajustable dentro del presupuesto operativo
  (≤ USD 1,200/mes, RES-11/EV-42 del DDA) sin cambios de arquitectura.

Los servicios elegidos para la demostración son **ordenes-api** (sabor API,
transaccional con pico) y **auditoria-worker** (sabor Worker, escritura
sostenida por el binding `#`) — los dos sabores de servicio del Backbone.
`consulta-estado-api` ya soporta escalado independiente desde HU-024/EAC-02;
esta HU consolida su archivo de override y cierra un hueco que dejó esa HU
en el scrape de Prometheus (ver más abajo).

---

## 1. Patrón de carga por servicio

No hay un umbral único: cada servicio del Backbone tiene un perfil de tráfico
distinto, determinado por su rol en la arquitectura de eventos y por su tipo
de acceso a datos. Esto es lo que dice el código y lo que ya hay medido; se
marca explícitamente qué es medición real y qué es una cifra derivada de
ingeniería, sin inventar ninguna.

### Auditoría (`auditoria-worker`) — escritura sostenida, la de mayor volumen

`AuditoriaConsumidor` liga su cola (`auditoria-worker.eventos`) al exchange
`sgo.eventos` con el binding **`#`**
([`AuditoriaConsumidor.cs`](../../SGO-Logic/src/Services/SgoLogic.Auditoria.Worker/Consumers/AuditoriaConsumidor.cs)):
recibe **todo evento publicado por todo contexto**, no un subconjunto. Esto
es estructural, no una medición puntual: el volumen de auditoría es, por
diseño, la suma del volumen de publicación de outbox de los demás servicios
combinados. Es el único servicio cuya carga crece automáticamente cada vez
que cualquier otro contexto agrega un evento nuevo.

Su perfil es **mixto**, no solo de escritura: además de la ingesta,
[`AuditoriaController.cs`](../../SGO-Logic/src/Services/SgoLogic.Auditoria.Worker/Controllers/AuditoriaController.cs)
expone dos endpoints de consulta de negocio —
`GET /auditoria/transacciones/{transactionId}` y
`GET /auditoria/agregados/{aggregateId}` — que sí golpea un cliente
directamente (no solo `/health` y `/metrics`). El primero tiene un
requisito de latencia propio y explícito en el código
(`// EAC-03: menos de 1 segundo`), que reproduce el criterio de aceptación
de HU-006: *"la recuperación de evidencia por transacción responde en menos
de 1 segundo"*.

La escritura sigue siendo el volumen dominante (sostenida, sin pico propio,
por el binding `#`) y es record-only (`RegistradorEventoAuditoria` solo hace
`INSERT`, nunca `UPDATE`), lo que simplifica escalarla: no hay contención de
escritura sobre una misma fila entre réplicas, cada evento entra en una fila
nueva. Pero al escalar `auditoria-worker` no solo se reparte esa ingesta:
también se escala la capacidad de **responder consultas de evidencia por
transacción** bajo el SLA de 1 segundo — la operación que Finanzas o un
auditor ejecutarían contra una transacción real. Es un argumento adicional a
favor de escalar este servicio que el documento no puede omitir: no es solo
"absorbe más eventos", es también "responde más rápido y a más volumen de
consultas de auditoría en paralelo".

### Órdenes (`ordenes-api`) — transaccional, con pico de Black Friday

Es el punto de entrada de la saga: crear orden, confirmar, transicionar,
devoluciones. Cada `POST /api/ordenes` y cada transición de estado escribe
en su propia base (`sgo_ordenes`) **y** encola en su outbox en la misma
transacción. Además corre tres consumidores propios
(`PagosEventosConsumidor`, `InventarioEventosConsumidor`,
`DocumentoFiscalCertificadoConsumidor`) que avanzan la saga de forma
asíncrona — cada uno con una instancia por réplica, compitiendo por la misma
cola.

El volumen base para el escenario de Black Friday ya está derivado y
documentado (no se repite aquí): 80 órdenes/minuto (~1.33/s) como piso de
demanda normal, con el factor 8x del DDA (EV-27) aplicado — ver
[`PRUEBA-CARGA-EAC01.md`](PRUEBA-CARGA-EAC01.md#que-mide). Es una estimación
de ingeniería documentada como tal (el DDA no fija un número absoluto), no
una medición de producción.

### Consulta de Estado (`consulta-estado-api`) — lectura intensiva, pico de Black Friday

CQRS de solo lectura (Redis cache-first con fallback a SQL, ADR-004). Ya
tiene una corrida de carga real y certificada bajo HU-024/EAC-02 — números
medidos, no estimados (ver
[`PRUEBA-CARGA-EAC02.md`](PRUEBA-CARGA-EAC02.md#6-evidencia-de-ejecución-reproducible)):

| Métrica medida (25 VUs, 40s) | Valor |
| --- | --- |
| Consultas procesadas | 17,955 |
| Throughput promedio | ~450 req/s |
| P95 | 9.27 ms |
| P100 | 232.09 ms |

Su perfil es opuesto al de Órdenes: lectura, no escritura; sin pico propio
disparado por el usuario final salvo el de Black Friday (que en un
e-commerce típico es entre 10x y 30x el volumen de escritura equivalente,
según esa misma prueba). Escala por CPU/memoria de instancias sin estado
(el estado vive en Redis, no en el proceso), no por contención de base.

### Integración ERP — **no implementada en este MVP** (aclaración necesaria)

El DDA (ADR-008, EV-15) diseña un servicio "Integración ERP" con patrón
**batch cada 30 minutos, carga plana** (consolidación de resúmenes de venta
por temporizador contra el AS/400 legacy). Ese patrón está en el diseño,
pero **no existe como microservicio en este repositorio**: no hay
`SgoLogic.IntegracionErp` en `src/Services/`, ni un servicio correspondiente
en `docker-compose.yml`. Lo único presente es el mock standalone `mock-erp`
(puerto 4000), que hoy no tiene ningún consumidor real apuntándole.

Se documenta esto explícitamente en vez de inventar una carga medida para un
servicio que no corre: si el criterio de aceptación de HU-025 pidiera
demostrar escalado de Integración ERP, sería un bloqueo de alcance a
resolver antes (implementar el servicio), no algo que esta HU pueda
completar con evidencia real.

Para no dejar dudas: `integracion-pos-worker` sí existe como servicio real
(`src/Services/SgoLogic.IntegracionPos.Worker`, dado de alta en
`docker-compose.yml`), pero **no es un ejemplo de perfil batch**. Su único
`BackgroundService` es
[`PosEventosConsumidor`](../../SGO-Logic/src/Services/SgoLogic.IntegracionPos.Worker/Consumers/PosEventosConsumidor.cs),
que consume eventos de RabbitMQ por suscripción — no hay temporizador ni
`PeriodicTimer` en el servicio. Es orientado a eventos, igual que el resto
del Backbone. Conclusión correcta: **ningún servicio con perfil batch existe
hoy en el repositorio**; el patrón batch de 30 minutos (ADR-008, EV-15) está
únicamente en el diseño, sin código que medir.

---

## 2. Qué permite escalar cada pieza del diseño

Todo esto ya estaba construido antes de HU-025; aquí se enumera qué garantía
de cada pieza es la que hace posible correr N réplicas sin coordinación
adicional entre ellas:

- **`FOR UPDATE SKIP LOCKED` en el outbox**
  ([`OutboxPublisher.cs`](../../SGO-Logic/src/BuildingBlocks/SgoLogic.BuildingBlocks/Outbox/OutboxPublisher.cs)):
  cuando N réplicas de un mismo servicio corren su propio `OutboxPublisher`
  contra la misma tabla, cada ciclo de polling toma un lote de filas
  `Pendiente` con lock exclusivo y `SKIP LOCKED` salta las que otra réplica
  ya está procesando. Ninguna fila se publica dos veces por dos publicadores
  compitiendo; todo el ciclo (leer, publicar, marcar) corre en una única
  transacción.
- **Colas quorum de RabbitMQ**: admiten múltiples consumidores compitiendo
  por la misma cola de forma nativa (a diferencia de una cola clásica sin
  configuración adicional). Cada réplica de `auditoria-worker` o de los
  consumidores internos de `ordenes-api` se suscribe a la misma cola; el
  broker reparte los mensajes entre las que están conectadas.
- **Deduplicación por `mensajes_procesados` /
  [`EventConsumerBase`](../../SGO-Logic/src/BuildingBlocks/SgoLogic.BuildingBlocks/Idempotencia/EventConsumerBase.cs)**:
  las colas quorum garantizan entrega al menos una vez, no exactamente una.
  Antes de ejecutar la lógica de negocio, cada consumidor revisa si el
  `eventId` ya está en su tabla de marcas (índice único sobre
  `event_id + nombre_servicio`, o `message_id` en el caso del consumidor de
  Pagos de Ordenes); si ya existe, hace `ack` y descarta sin duplicar
  trabajo. Esto es lo que hace segura una redelivery entre réplicas
  compitiendo, no solo entre reintentos de una sola instancia.
- **Ausencia de estado local en memoria**: ningún servicio del Backbone
  guarda sesión, caché en proceso ni contador que dependa de "esta
  instancia". El único estado compartido vive en PostgreSQL, Redis o
  RabbitMQ — de ahí que agregar una réplica no requiera ningún mecanismo de
  sincronización nuevo entre procesos.
- **Configuración 100% por variables de entorno**
  (`docker-compose.yml`, `Outbox__*`, `ConnectionStrings__*`): todas las
  réplicas de un mismo servicio arrancan con exactamente la misma
  configuración porque la reciben del entorno, no de un archivo por
  instancia. Añadir una réplica es repetir el mismo proceso, no configurar
  una copia distinta.
- **El API Gateway (YARP) y RabbitMQ ya resuelven por nombre de servicio**,
  no por IP fija: `http://ordenes-api:8080` en
  [`appsettings.json`](../../SGO-Logic/src/Services/SgoLogic.ApiGateway/appsettings.json)
  se resuelve contra el DNS embebido de Docker, que devuelve una dirección
  por cada réplica activa y rota entre ellas. Este mecanismo ya se validó en
  HU-024 para `consulta-estado-api`; HU-025 lo reutiliza sin tocarlo para
  `ordenes-api`.

## 3. Qué NO escala horizontalmente, y por qué

- **PostgreSQL (`db-operacional`, `db-finanzas`, `db-auditoria`)**: cada uno
  es una única instancia de escritura por contexto (ADR-006: auditoría en
  base separada, append-only). Múltiples réplicas de un servicio comparten
  la misma base — eso es correcto y necesario (es lo que permite que todas
  vean el mismo estado), pero la base en sí no gana instancias adicionales
  al escalar el servicio que la usa. En producción, esto se resolvería con
  réplicas de lectura de PostgreSQL (streaming replication) para descargar
  las consultas de solo lectura de `consulta-estado-api` y de reportes, y
  con particionamiento o un clúster gestionado (ej. Patroni) si la escritura
  misma se vuelve el cuello de botella — ninguna de las dos cosas es
  necesaria para el volumen de este MVP.
- **RabbitMQ**: una única instancia de broker. Las colas quorum ya reparten
  el *consumo* entre réplicas de un servicio, pero el broker mismo no se
  escala horizontalmente en este MVP. En producción, la ruta es un clúster
  de RabbitMQ (3 nodos, colas quorum con factor de réplica entre nodos, ya
  compatible con la topología actual sin cambiar el código de publicación o
  consumo) o migrar a un broker gestionado.
- Ambas exclusiones son intencionales para el MVP (ADR-013): agregar
  replicación de base o clúster de RabbitMQ no aporta nada a la
  demostración del escalado de los *servicios* del Backbone, que es lo que
  pide esta HU, y sí consume la ventana de entrega en infraestructura que el
  volumen de la demo no necesita.

## 4. Ajuste de capacidad base anual dentro de USD 1,200/mes

El techo operativo (RES-11/EV-42 del DDA) es sobre infraestructura on-premises
reutilizada, no gasto por contenedor de un proveedor cloud: el costo que
crece con más réplicas es el de aprovisionar más CPU/RAM en el hardware
propio, no una factura por instancia. El procedimiento para subir la
capacidad base año con año, sin tocar arquitectura:

1. **Medir el uso real por réplica** con lo que ya expone Prometheus/Grafana
   (`http_requests_received_total`, `outbox_publicados_total`,
   `mensajes_consumidos_total` por `instance`, panel "Reparto de carga entre
   instancias") más `docker stats` para CPU/memoria real contra los límites
   declarados en `deploy:` (ver `scripts/escalabilidad/docker-compose.scale.yml`).
2. **Decidir cuánta capacidad adicional cabe en el incremento de presupuesto
   del año** (hardware nuevo o reutilizado) sin superar el techo de
   USD 1,200/mes.
3. **Subir el número de réplicas** de los servicios cuyo patrón de carga
   (sección 1) lo justifique — típicamente Auditoría y Órdenes antes que
   Consulta de Estado, si el canal digital crece más rápido que el volumen
   de lectura. Esto es un argumento de línea de comandos (`--scale`) o, para
   dejarlo fijo entre reinicios, el atributo `deploy.replicas` de Compose.
4. **No se toca código ni configuración de aplicación**: los servicios ya
   leen su configuración de variables de entorno y no guardan estado local
   (sección 2), así que una réplica nueva arranca idéntica a las demás sin
   ningún paso manual adicional.

Cambiar el número de réplicas es, con este diseño, un cambio de
configuración de despliegue — no un rediseño arquitectónico. Es exactamente
la garantía que pide el criterio de aceptación de HU-025.

## 5. Docker Compose vs. Kubernetes

ADR-013 ya es explícito: Docker Compose es el entorno del MVP, no reemplaza
la decisión de producción de ADR-001 (Kubernetes on-premises), que sigue
vigente. Las consecuencias negativas que el propio ADR-013 reconoce son
justo lo que HU-025 acota: *"el escalado horizontal por HPA... no se
demuestra en el MVP: Docker Compose no tiene equivalente de HPA. Esa
capacidad queda como evidencia documental de la decisión de Kubernetes, no
como algo ejecutado en la demo"* — de ahí que esta HU demuestre réplicas
manuales (`--scale`) y NO introduzca autoescalado, Kubernetes, Swarm ni un
balanceador nuevo: eso está fuera del alcance declarado del MVP y de esta
historia.

La razón por la que migrar a Kubernetes en el futuro **no exige rediseño**
(el "sin rediseño cada año" del criterio de aceptación) es precisamente todo
lo listado en la sección 2: los servicios ya son *stateless*, ya se
configuran 100% por variables de entorno, y ya toleran múltiples instancias
compitiendo por la misma cola/tabla sin coordinación adicional. Migrar
cambia el **manifiesto de despliegue** (de `docker-compose.yml` con
`--scale`/`deploy.replicas` a un `Deployment` + `HorizontalPodAutoscaler` de
Kubernetes), no el código de los servicios. Como referencia de que esa
migración es mecánica y no estructural, ya existe un manifiesto de ejemplo
para uno de los servicios escalables en
[`deploy/k8s/consulta-estado-hpa.yaml`](../../deploy/k8s/consulta-estado-hpa.yaml)
(HU-024): documenta el mismo servicio que hoy se escala manualmente con
Compose, con reglas de HPA por CPU/memoria — evidencia documental, según lo
que ADR-013 previó, no algo desplegado en este MVP.

Lo que Compose **no** hace, y que sí haría Kubernetes, es exactamente lo que
ADR-013 anticipó: autoescalar según métrica en vivo y balancear tráfico de
verdad entre réplicas (Compose/YARP + DNS round-robin reparten, pero sin las
políticas de un balanceador real). Ninguna de las dos cosas la pide esta HU
en el MVP.

## 6. Resultados medidos

Corrida real, ejecutada y medida por el responsable de esta HU.

### Configuración de la corrida

- Fecha/hora de inicio (UTC, `DESDE`): `2026-09-20T01:43:40Z`
- Réplicas: `ordenes-api=3`, `auditoria-worker=3`
- Parámetros del outbox durante la corrida: `OUTBOX_BATCH_SIZE=200`,
  `OUTBOX_POLLING_MS=500` (los valores por defecto del proyecto son 20 y
  2000 — ver el hallazgo no planificado más abajo, que es justamente sobre
  este cambio)
- Carga k6 (`carga.js`): 20 VUs, 2m20s

### Salida de `carga.js`

```
RUN_ID                  : 1789868620
Peticiones HTTP totales : 35635
Ordenes creadas         : 17817
Ordenes confirmadas     : 17817
Fallos                  : 0
Duración                : 2m20.2s (00/20 VUs, 17817 iteraciones completas, 0 interrumpidas)
```

### Lectura del panel "Reparto de carga entre instancias" durante la corrida

- `ordenes-api`: pico de 32.9 K eventos pendientes en el outbox, drenado
  completo a cero en ~2.5 minutos, con tasa de publicación sostenida de
  ~220 eventos/s.
- Las tres réplicas de `ordenes-api` reportan su propio valor del gauge de
  pendientes sobre la misma tabla compartida (valores casi idénticos entre
  sí en cada instante), y las tres publican — evidencia de reparto, ninguna
  ociosa.
- `auditoria-worker`: tres réplicas consumiendo de la misma cola quorum, sin
  duplicación (confirmado por las consultas SQL de `verificar.sh`, abajo).
- El drenaje de un contexto propaga trabajo a los siguientes por la saga: al
  drenar Inventario, Órdenes subió a 13.9 K pendientes y luego Pagos a
  11.8 K, y ambos drenaron solos a cero sin intervención manual. La curva
  del panel muestra tres olas sucesivas que se absorben y se recuperan.

### Salida de `verificar.sh`

```
outbox_ordenes_event_id_publicados_dos_veces=0
ordenes_mensajes_procesados_dos_veces=0
ordenes_pagos_mensaje_procesado_dos_veces=0
auditoria_eventos_registrados_dos_veces=0
auditoria_mensajes_procesados_dos_veces=0
---
outbox_ordenes_publicados_total_desde=124719
auditoria_eventos_recibidos_total_desde=162875 (incluye eventos de todos los contextos, no solo ordenes)
outbox_ordenes_publicados_sin_llegar_a_auditoria=0
===
RESULTADO=APROBADO: 0 duplicados y 0 eventos perdidos con multiples replicas compitiendo.
```

**Dictamen: `APROBADO`.** Cero duplicados en las cinco tablas verificadas y
cero eventos publicados por `ordenes-api` que no hayan llegado a
`auditoria-worker`, con 3 réplicas de cada uno compitiendo durante toda la
corrida.

### Hallazgo no planificado: impacto medido de `OUTBOX_BATCH_SIZE` / `OUTBOX_POLLING_MS`

Esto no fue un experimento diseñado — fue un descuido de procedimiento
durante la corrida que terminó siendo el dato más útil de la prueba, y se
documenta como tal.

Durante la corrida, `inventario-api` y `pagos-api` quedaron con los valores
anteriores del `.env` (`OUTBOX_BATCH_SIZE=20`, `OUTBOX_POLLING_MS=2000`)
porque sus contenedores no se recrearon al editar el `.env`: Docker Compose
solo recrea los servicios cuya configuración cambió explícitamente en la
invocación, y esa corrida solo escaló `ordenes-api` y `auditoria-worker`.
El resultado es una comparación involuntaria pero real, mismo código y misma
carga, antes y después de recrear cada contenedor con los valores nuevos:

| Servicio | BatchSize / PollingMs | Tasa de publicación | Comportamiento |
| --- | --- | --- | --- |
| ordenes-api (3 réplicas) | 200 / 500 | ~220 eventos/s | 32.9 K drenados en ~2.5 min |
| inventario-api (1 réplica, config vieja) | 20 / 2000 | ~10 eventos/s | 13.5 K estancados, sin drenar |
| inventario-api (1 réplica, tras recrear) | 200 / 500 | ~200 eventos/s | 13.5 K drenados en ~3 min |
| pagos-api (1 réplica, config vieja) | 20 / 2000 | ~10 eventos/s | 11.8 K estancados |
| pagos-api (1 réplica, tras recrear) | 200 / 500 | ~145 eventos/s | 11.8 K drenados en ~1.5 min |

Esto es evidencia directa del segundo criterio de aceptación de HU-025 ("la
capacidad base es ajustable dentro del presupuesto operativo sin cambios de
arquitectura"): el throughput del outbox se multiplicó por un orden de
magnitud (~20x en los casos de la tabla) cambiando dos variables de entorno,
sin tocar una línea de código ni la arquitectura del outbox. Que el hallazgo
haya salido de un descuido (no recrear dos contenedores) en vez de un
experimento planeado no le resta valor a la evidencia — al contrario, deja
ver qué pasa cuando la configuración queda desalineada entre servicios, que
es exactamente el riesgo operativo que la advertencia agregada en la
sección 7 busca prevenir.

### Hallazgos de la corrida: esquema y deduplicación

Dos observaciones neutrales que salieron de consultar las bases
directamente al escribir `verificar.sh`, sin relación con el resultado de
esta corrida (no bloquean el escalado, no se proponen resolver aquí — son de
otros integrantes y quedan fuera de alcance de HU-025):

- La tabla `outbox_messages` se crea en el esquema `public` en **todos** los
  contextos que la usan (confirmado en los snapshots de EF Core de Órdenes,
  Inventario, Pagos y Facturación), no en el esquema propio de cada contexto
  (`ordenes`, `inventario`, `pagos`, `facturacion`), porque
  `OutboxMessageConfiguration` de BuildingBlocks llama a
  `ToTable("outbox_messages")` sin argumento de esquema, y eso no hereda el
  `HasDefaultSchema(...)` que cada `DbContext` declara.
- `Ordenes.Api` mantiene dos tablas de deduplicación distintas en el mismo
  esquema `ordenes`: `mensaje_procesado` (singular, del consumidor de Pagos,
  anterior a `EventConsumerBase`) y `mensajes_procesados` (plural, del
  patrón actual de BuildingBlocks, usado por los consumidores de Inventario
  y de certificación fiscal). Ambas se verificaron sin duplicados en esta
  corrida.

---

## 7. Guía de verificación

Pasos exactos para reproducir la demostración de escalado y su evidencia.

> **Advertencia — variables de `.env` y contenedores ya arrancados.** Docker
> Compose solo recrea un contenedor cuando su configuración cambia
> explícitamente en la invocación de `up`. Si editas `.env` (por ejemplo
> `OUTBOX_BATCH_SIZE` o `OUTBOX_POLLING_MS`) con el stack ya arriba y luego
> solo escalas `ordenes-api`/`auditoria-worker` con `escalar.sh`, los demás
> servicios (`inventario-api`, `pagos-api`, etc.) **siguen corriendo con los
> valores viejos** hasta que se recreen. Esto pasó de verdad en la corrida
> de la sección 6 (ver "Hallazgo no planificado") y produjo una comparación
> de throughput entre configuraciones distintas sin que fuera intencional.
> Para que la prueba sea reproducible y comparable entre servicios: edita el
> `.env` **antes** del `docker compose up -d` inicial, o si ya está arriba,
> corre `docker compose up -d --force-recreate` después de editarlo para que
> todos los contenedores relean la configuración nueva.

### 7.1. Arrancar el entorno sin escalar (arranque normal, sin cambios)

```bash
cd SGO-Logic
docker compose up -d
```

Todo debe arrancar exactamente igual que antes de esta HU: `docker-compose.yml`
no se tocó, solo se agregó `scripts/escalabilidad/` y se ajustó
`observabilidad/prometheus.yml` (el scrape, no los servicios).

### 7.2. Escalar ordenes-api y auditoria-worker

```bash
./scripts/escalabilidad/escalar.sh 3
```

Equivale a:

```bash
docker compose -f docker-compose.yml -f scripts/escalabilidad/docker-compose.scale.yml \
  up -d --scale ordenes-api=3 --scale auditoria-worker=3
```

**Por qué lleva `-f scripts/escalabilidad/docker-compose.scale.yml` y no solo
`docker compose up --scale ...`:** sin ese override, el `--scale` falla con
conflicto de puerto — `ordenes-api` publica `8083:8080` y `auditoria-worker`
publica `8093:8080` como puertos fijos de host en `docker-compose.yml`, y dos
contenedores no pueden publicar el mismo puerto de host. Esos puertos fijos
se dejaron **intactos** en `docker-compose.yml` a propósito: los usan
directamente (sin pasar por el API Gateway)
`scripts/pruebas/suite-completa-202300353.sh` y `camino-feliz.ps1` de otro
integrante del equipo. El override solo quita ese mapeo fijo cuando se
fusiona explícitamente con `-f`, así que el arranque normal (criterio de
aceptación 5) no cambia en absoluto.

Verificar:
```bash
docker compose ps ordenes-api auditoria-worker
```
Las 3+3 réplicas deben quedar en `healthy`.

### 7.3. Confirmar en Prometheus que cada réplica es un target distinto

Abrir `http://localhost:9090/targets`, job `sgo-logic`. Antes de escalar,
`ordenes-api`, `auditoria-worker` y `consulta-estado-api` aparecen como un
target cada uno; después de `--scale ... =3`, cada uno debe aparecer como
**3 targets independientes**, distinguibles por la etiqueta `instance`
(`ip:8080` distinto por réplica). Si solo aparece un target por servicio
tras escalar, algo no resolvió el DNS de Docker correctamente (revisar que
Prometheus y los servicios estén en la misma red por defecto del compose).

### 7.4. Generar carga concurrente sostenida

Anotar la hora de inicio (se usa en la verificación SQL del paso 7.6):

```bash
DESDE=$(date -u +%Y-%m-%dT%H:%M:%SZ)
RUN_ID=$(date +%s)

docker run --rm --network sgo-logic_default \
  -v "${PWD}/scripts/escalabilidad:/scripts" \
  -e VUS=20 -e DURACION_SEG=120 -e RUN_ID="${RUN_ID}" \
  grafana/k6 run /scripts/carga.js
```

La red se llama `sgo-logic_default`: `docker-compose.yml` no declara `name:`
de proyecto ni una red explícita, así que Compose deriva el nombre de
proyecto del directorio que contiene el archivo (`SGO-Logic` → `sgo-logic`)
y de ahí `<proyecto>_default` (confirmado corriendo `docker compose config`,
que imprime `name: sgo-logic` y, bajo `networks:`, `default: name:
sgo-logic_default`). Depende del nombre del directorio, así que si el
checkout local no se llama `SGO-Logic` la red tendrá otro nombre — en ese
caso, confírmalo con `docker compose config` antes de correr `carga.js`, no
con conjetura.

### 7.5. Ver el reparto de carga en Grafana durante o después de la corrida

Abrir `http://localhost:3000`, tablero **SGO-Logic - Mensajeria y
resiliencia**, panel **"Reparto de carga entre instancias (HU-025 / EAC-04)"**
(Grafana recarga los `.json` de tableros cada 30s sin reiniciar el
contenedor — no hace falta reiniciar Grafana para verlo). Qué mirar:

- Las líneas de `peticiones/s ordenes-api {instance}` deben mostrar
  actividad en **más de una** instancia durante la corrida — evidencia de
  que el tráfico no cae siempre en la misma réplica.
- Lo mismo para `outbox publicados/s` y `consumidos/s auditoria-worker`.
- Ninguna instancia debería quedar en cero mientras las demás trabajan (eso
  indicaría una réplica ociosa, lo contrario de lo que pide el criterio de
  aceptación 3).

### 7.6. Verificar por SQL que no hubo duplicación ni pérdida

Esperar unos 60 segundos después de que `carga.js` termine (drenaje
asíncrono del outbox y de las colas), luego:

```bash
DESDE="${DESDE}" ./scripts/escalabilidad/verificar.sh
```

Qué indica éxito: todas las líneas `*_dos_veces=` y
`outbox_ordenes_publicados_sin_llegar_a_auditoria=` en cero, y
`RESULTADO=APROBADO` al final. Cualquier valor mayor que cero es un fallo
real de esta HU (duplicación o pérdida bajo réplicas compitiendo) — no algo
para reinterpretar, salvo el caso ya señalado por el propio script de una
corrida recién terminada (esperar el drenaje y repetir).

`verificar.sh` puede terminar en tres dictámenes distintos, no solo dos:

- **`APROBADO`** (código de salida 0): todas las consultas corrieron y no
  hay inconsistencias.
- **`RECHAZADO`** (código de salida 1): todas las consultas corrieron, pero
  se encontró al menos un duplicado o un evento sin auditar.
- **`INCOMPLETO`** (código de salida 2): una o más consultas no se pudieron
  evaluar (por ejemplo, un esquema/tabla incorrecto o un contenedor caído).
  El script las lista por nombre. No es aprobación ni rechazo de la HU: hay
  que resolver la causa de esa consulta puntual y volver a correr
  `verificar.sh` antes de dar por buena la corrida — un `INCOMPLETO` nunca
  debe leerse como un `APROBADO` parcial.

### 7.7. Volver a 1 réplica

```bash
docker compose up -d --scale ordenes-api=1 --scale auditoria-worker=1
```

---

## Referencias

- [`scripts/escalabilidad/`](../../SGO-Logic/scripts/escalabilidad/) — override de Compose, script de escalado, carga k6 y verificación SQL de esta HU.
- [`observabilidad/prometheus.yml`](../../SGO-Logic/observabilidad/prometheus.yml) — scrape por DNS SD para los servicios escalables.
- [`observabilidad/grafana/provisioning/dashboards/json/mensajeria-resiliencia.json`](../../SGO-Logic/observabilidad/grafana/provisioning/dashboards/json/mensajeria-resiliencia.json) — panel de reparto de carga.
- [`PRUEBA-CARGA-EAC01.md`](PRUEBA-CARGA-EAC01.md) y [`PRUEBA-CARGA-EAC02.md`](PRUEBA-CARGA-EAC02.md) — pruebas de carga previas cuyos patrones y cifras se citan en la sección 1.
- [ADR-013](../ADR/ADR-013-docker-compose-entorno-ejecucion-mvp.md) — Docker Compose como entorno del MVP.