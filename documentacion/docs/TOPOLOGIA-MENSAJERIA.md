# Topología de mensajería de SGO-Logic

**Proyecto:** SGO-Logic — Fase 2
**Alcance:** todos los contextos que publican o consumen eventos sobre RabbitMQ
**Propietario del documento:** base técnica (HU-008)
**Redactado por:** Julio Herrera
**Estado:** implementado

---

## 0. Por qué este documento existe

`docs/contratos/sgo-eventos-envelope-v1.md` congela el sobre de eventos y deja
dos puntos pendientes explícitamente a cargo de la base técnica (sección 5 de
ese documento): el tipo de cola para que `x-delivery-limit` funcione, y quién es
dueño de la dead letter compartida. Este documento resuelve ambos y describe
cómo cada contexto declara su propia cola sobre la topología ya declarada.

No repite las reglas del envelope (`eventId`, `sequence`, `transactionId`,
`causationId`, etc.) — esas viven en `sgo-eventos-envelope-v1.md` y siguen
congeladas. Esto es solo el transporte.

---

## 1. Exchanges

| Exchange | Tipo | Durable | Propósito | Quién publica |
|---|---|---|---|---|
| `sgo.eventos` | `topic` | sí, `auto_delete: false` | Eventos de dominio, pub/sub. Es el que usan los siete contextos emisores del envelope. | `OutboxPublisher` de cada contexto |
| `sgo.sat` | `direct` | sí | Canal MOM de certificación fiscal. Interno de Facturación (ADR-011). | Facturación |
| `sgo.erp` | `direct` | sí | Canal de lote hacia el ERP. Interno de Integración ERP (ADR-008). | Integración ERP |
| `sgo.eventos.dlx` | `fanout` | sí | Dead letter compartida por toda cola quorum del proyecto que la declare como `x-dead-letter-exchange`. | El broker, no una aplicación |

Los cuatro se declaran una sola vez, de forma idempotente, al arrancar cada
proceso (`TopologiaInicialHostedService`, en `SgoLogic.BuildingBlocks.Messaging`)
y de nuevo, defensivamente, en cada ciclo de `OutboxPublisher` y en cada
suscripción (`SuscriptorRabbitMq.Suscribir`), por si el proceso arrancó con el
broker caído y la declaración inicial no llegó a correr. Redeclarar algo
idéntico a lo que ya existe no falla: por eso es seguro repetirlo.

`sgo.sat` y `sgo.erp` los declara la base técnica (están en
`TopologiaMensajeria`, en `SgoLogic.BuildingBlocks.Messaging`) pero el contenido
que viaja por ellos, sus colas y sus bindings son responsabilidad de Facturación
e Integración ERP respectivamente. La base técnica no impone nada sobre esos
dos canales más allá de que el exchange exista.

## 2. Convención de routing key

```
<contexto>.<entidad>.<hecho-en-kebab-case>
```

- El primer segmento **siempre** coincide con el campo `source` del envelope
  (regla del propio contrato del envelope, sección 5).
- El segundo segmento es el agregado o entidad del hecho, en minúscula.
- El tercero es el hecho en sí, en kebab-case.

Ejemplos reales de este código base:

| Routing key | Exchange | Evento |
|---|---|---|
| `sample-api.item-ejemplo.creado` | `sgo.eventos` | `EjemploCreadoEvento` (`SgoLogic.Sample.Api`) |
| `sample-worker.item-ejemplo.procesado` | `sgo.eventos` | `EjemploProcesadoEvento` (`SgoLogic.Sample.Worker`) |

Cuando tu contexto empiece a publicar eventos reales, seguí el mismo patrón:
`ordenes.orden.estado-cambiado`, `pagos.pago.confirmado`, etc.

## 3. Cómo declarar tu propia cola

La base técnica declara los exchanges. **Vos declarás tu cola**, con el helper
`DeclaradorColas.DeclararCola` de `SgoLogic.BuildingBlocks.Messaging`:

```csharp
DeclaradorColas.DeclararCola(
    canal,
    nombre: "ordenes.orden-estado-cambiado",
    bindings: [ new BindingCola(TopologiaMensajeria.ExchangeEventos, "inventario.reserva.*") ],
    limiteReintentos: 5); // opcional, 5 es el default
```

Esto declara una cola **quorum**, durable, con
`x-dead-letter-exchange: sgo.eventos.dlx` y `x-delivery-limit` en el valor que
le pases (5 por defecto). El nombre de la cola y sus bindings son tuyos: la
base técnica no los impone, solo la forma.

Si estás escribiendo un consumidor con `EventConsumerBase<TDbContext, TData>`
(ver `docs/contratos/PLANTILLA-SERVICIO.md`), no llamás a `DeclararCola` a
mano: la clase base lo hace por vos en su `ExecuteAsync`, leyendo las
propiedades abstractas `Cola`, `Bindings` y `LimiteReintentos` que definís en tu
subtipo.

### Ejemplo real: la cola de Auditoría

`sgo-eventos-envelope-v1.md` (sección 6) ya fija que Auditoría se liga con `#`
para recibir absolutamente todo lo que pasa por `sgo.eventos`:

```csharp
DeclaradorColas.DeclararCola(
    canal,
    nombre: "auditoria.eventos",
    bindings: [ new BindingCola(TopologiaMensajeria.ExchangeEventos, "#") ]);
```

Ese es el único binding del proyecto que usa `#`. Cualquier binding más
estrecho en esa cola deja agujeros en la bitácora e incumple EAC-03.

## 4. Política de reintentos

Hay **dos** mecanismos de reintento en este proyecto, y no hay que confundirlos:

### 4.1 Reintento de publicación (Outbox → broker)

Vive enteramente en `OutboxPublisher` (`SgoLogic.BuildingBlocks.Outbox`), del
lado de cada contexto emisor, **no** en RabbitMQ. Si el broker no confirma una
publicación (o no está disponible), la fila de outbox queda `Pendiente` y
`ProximoIntentoEn` se calcula con backoff exponencial
(`2^intentos` segundos, techo de 300s). Al llegar a `Outbox:MaxReintentos`
(default 5, configurable por variable de entorno `OUTBOX_MAX_REINTENTOS`), la
fila pasa a `Fallido` y el `OutboxPublisher` la envía él mismo a
`sgo.eventos.dlx`.

Esto es así — y no por TTL de mensaje — porque las colas quorum **no soportan
TTL por mensaje**. Es la advertencia que el propio envelope deja abierta en su
sección 5.

### 4.2 Reintento de consumo (broker → consumidor)

Vive en RabbitMQ, vía `x-delivery-limit` sobre la cola quorum de cada
consumidor. Cuando `EventConsumerBase` (o cualquier suscriptor manual) le
devuelve `false` al mensaje, `SuscriptorRabbitMq` hace
`BasicNack(requeue: true)` **a propósito**: es lo que hace que la cola cuente
esa redelivery. Al superar `x-delivery-limit`, la propia cola quorum manda el
mensaje a `sgo.eventos.dlx` sin que ningún código de aplicación intervenga.

Si tu handler nackea con `requeue: false` en algún lugar, estás rompiendo este
mecanismo: el mensaje se iría a dead letter en el primer fallo, sin
redelivery ni conteo. No lo hagas salvo que sea exactamente la intención.

## 5. Por qué colas quorum

Resolviendo el punto pendiente de `sgo-eventos-envelope-v1.md` sección 5: el
proyecto usa **colas quorum** para toda cola propia de un contexto (nunca
colas clásicas), decisión de la base técnica:

- `x-delivery-limit` solo existe en colas quorum. En una cola clásica ese
  argumento se ignora en silencio y un mensaje envenenado se reintenta para
  siempre — exactamente el escenario que HU-008 quiere evitar.
- Son la recomendación activa de RabbitMQ por sobre las colas clásicas para
  cualquier caso que necesite durabilidad real (replicación por Raft, sin el
  historial de bugs de mirroring de las clásicas).
- La pérdida de TTL por mensaje no es un costo real acá: el reintento con
  backoff que sí necesitábamos (el de outbox) ya vive del lado de la
  aplicación, como se explica en 4.1.

`sgo.eventos.dlq`, la cola de descarte final detrás de `sgo.eventos.dlx`,
también es quorum, durable, y deliberadamente **sin TTL**: un mensaje que
llegó ahí se queda hasta que alguien lo revise, no expira solo.

## 6. Dueño de la dead letter

Resolviendo el segundo punto pendiente: **la base técnica (Julio)** es dueña de
`sgo.eventos.dlx` / `sgo.eventos.dlq` mientras el proyecto no tenga un rol de
operaciones dedicado. Cualquier mensaje que caiga ahí — sea por agotar
reintentos de publicación (4.1) o de consumo (4.2) — es candidato a revisión
manual.

Métrica de profundidad de la DLQ en Grafana y el criterio operativo de qué
hacer con lo que cae ahí quedan fuera de esta sesión (es la tarea de
observabilidad, todavía no construida). Lo que sí existe ya: `outbox_fallidos_total`
por servicio (ver `docs/contratos/PLANTILLA-SERVICIO.md`), que cuenta cuántos
mensajes de **ese** contexto llegaron a `Fallido` y fueron a dead letter.

## 7. Qué NO está en este documento

- El contenido del sobre de eventos y sus reglas de correlación: están en
  `docs/contratos/sgo-eventos-envelope-v1.md`.
- Cómo aplicar la configuración de outbox a tu propio `DbContext`, cómo
  publicar con `IOutboxWriter` y cómo escribir un consumidor con
  `EventConsumerBase`: están en `docs/contratos/PLANTILLA-SERVICIO.md`.
- El contrato de datos de `sgo.sat` y `sgo.erp`: son internos de Facturación e
  Integración ERP respectivamente, y cada uno documenta el suyo.