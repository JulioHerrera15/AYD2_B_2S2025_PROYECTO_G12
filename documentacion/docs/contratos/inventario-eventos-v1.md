# Contrato de eventos del contexto Inventario — v1.1

**Proyecto:** SGO-Logic — Fase 2
**Curso:** Análisis y Diseño de Sistemas 2, Sección B
**Contexto emisor:** Inventario
**Autor:** 202300335 — Jose Pirir
**Estado:** propuesto para congelamiento
**Fecha:** 2026-09-09 (evento 3 agregado el 2026-09-12, HU-002; v1.1 el 2026-09-23, HU-003 y HU-014, ADR-022)
**Depende de:** `sgo-eventos-envelope-v1.md`, `ordenes-eventos-v1.md`

| Versión | Cambio |
|---|---|
| v1 | Eventos 1 y 2 (HU-001); evento 3 agregado de forma aditiva (HU-002). Inventario consume `ordenes.reserva.solicitada`. |
| v1.1 | Cambio aditivo exigido por la regla 3 de ADR-021 y registrado en ADR-022. Inventario consume además `ordenes.reserva.liberacion-solicitada` (evento 4 de `ordenes-eventos-v1`) y `ordenes.devolucion.registrada` (evento 11), y publica los eventos 4 (`ReservaInventarioLiberada`) y 5 (`StockReingresado`). Los eventos 1 a 3 no cambian. |

---

## 0. Por qué este documento existe

Inventario cierra HU-001: consume `ordenes.reserva.solicitada` (evento 3 de
`ordenes-eventos-v1`), verifica existencia disponible en la ubicación candidata,
reserva con **bloqueo optimista** por columna de versión (ADR-005) y crea la
reserva con una marca de expiración (TTL, EV-23). Publica el resultado por Outbox.
Órdenes necesita ese resultado para avanzar la saga (CU-01, CU-02): con la reserva
confirmada la orden pasa a `INVENTARIO_RESERVADO`; sin stock suficiente pasa a
`RECHAZADA` con motivo `STOCK_INSUFICIENTE` y no se cobra. Auditoría y Consulta de
Estado también consumen estos eventos.

Desde la v1.1 Inventario también cierra la compensación de la saga: consume
`ordenes.reserva.liberacion-solicitada` (HU-003) para liberar la reserva en el
momento, sin esperar el TTL, y `ordenes.devolucion.registrada` (HU-014) para
reingresar al stock físico las unidades devueltas (ADR-022).

Este documento define el contenido de `data` de los eventos que **publica**
Inventario y, desde la v1.1, los campos que Inventario lee de los eventos que
**consume** (sección 4.3). El `data` completo de esos eventos lo define
`ordenes-eventos-v1.md`. El sobre, las reglas de idempotencia y ordenamiento, las
enumeraciones compartidas y la topología del bus están en
`sgo-eventos-envelope-v1.md` y no se repiten aquí.

Inventario es el único emisor de eventos con routing key `inventario.*`.

Trazabilidad al DDA: ADR-003, ADR-005, ADR-006, ADR-022, EV-22, EV-23, EV-05.

---

## 1. Alcance de la v1.1

Cubre el desenlace de una solicitud de reserva (HU-001), la liberación automática
por vencimiento del TTL (HU-002, EV-23), la liberación explícita por compensación
de la saga (HU-003) y el reingreso de stock por devolución (HU-014).

La v1 dejaba fuera la liberación por compensación y el reingreso por devolución.
La v1.1 los agrega como cambio aditivo (envelope, sección 8), igual que se agregó el
evento 3: no cambia nada de lo ya congelado para los eventos 1 a 3. El reingreso se
publica como `StockReingresado` (`inventario.stock.reingresado`) y no como el
`inventario.existencia.ajustada` que la v1 anticipaba; ese nombre no se usa.

---

## 2. Enumeraciones propias del contexto

Las compartidas (`MetodoEntrega`, `TipoUbicacion`) están en el documento de
envelope.

### 2.1 `MotivoRechazoReserva`

Motivo del evento `ReservaInventarioRechazada`. Lista cerrada.

| Valor | Significado |
|---|---|
| `STOCK_INSUFICIENTE` | Existe registro de existencia para todos los SKU, pero ninguna ubicación tiene disponible suficiente para todas las líneas. |
| `SIN_UBICACION_ELEGIBLE` | Algún SKU pedido no tiene ninguna existencia registrada en ninguna ubicación. |

Órdenes mapea ambos a la transición `RESERVA_SOLICITADA -> RECHAZADA` con
`MotivoCancelacion.STOCK_INSUFICIENTE`.

### 2.2 `EstadoReserva`

Interno de Inventario, **no viaja en eventos**. Se documenta para entender el
flujo: `ACTIVA`, `EXPIRADA`, `LIBERADA`. Una `Reserva` se crea `ACTIVA` con
`expiraEn = creadaEn + ttlSegundos` y pasa a `EXPIRADA` cuando el barrido de HU-002
la alcanza, o a `LIBERADA` cuando llega una solicitud de liberación de Órdenes
(HU-003). Solo una reserva `ACTIVA` cambia de estado: las dos transiciones son
excluyentes y cada una incrementa en 1 la `sequence` de la reserva. Los consumidores
se enteran por los eventos 3 y 4, no por este enum.

### 2.3 `MotivoLiberacionReserva`

No es propio de Inventario: lo define `ordenes-eventos-v1` (sección de
enumeraciones) y llega en `ordenes.reserva.liberacion-solicitada`. Inventario lo
copia sin cambios al evento 4. Valores: `COBRO_RECHAZADO`, `COBRO_SIN_RESPUESTA`,
`CANCELACION_CLIENTE`, `TIMEOUT_SAGA`.

---

## 3. Catálogo de eventos

Cinco eventos. Ninguno más se publica bajo `inventario.*` en la v1.1. Los eventos 4 y
5 son los agregados en la v1.1.

| # | `eventType` | Routing key | `aggregateType` | Se emite cuando |
|---|---|---|---|---|
| 1 | `InventarioReservado` | `inventario.reserva.confirmada` | `Reserva` | La reserva se creó con éxito y su TTL está vigente. |
| 2 | `ReservaInventarioRechazada` | `inventario.reserva.rechazada` | `Reserva` | No hay stock suficiente en ninguna ubicación elegible. No se crea reserva. |
| 3 | `ReservaInventarioExpirada` | `inventario.reserva.expirada` | `Reserva` | El TTL de una reserva `ACTIVA` venció y sus unidades volvieron al disponible (HU-002, EV-23). |
| 4 | `ReservaInventarioLiberada` | `inventario.reserva.liberada` | `Reserva` | La saga pidió liberar la reserva antes del TTL: cobro rechazado, sin respuesta, cancelación del cliente o timeout (HU-003). |
| 5 | `StockReingresado` | `inventario.stock.reingresado` | `Devolucion` | Las unidades de una devolución volvieron al stock físico de una ubicación (HU-014). |

**El conflicto de bloqueo optimista no produce evento.** Si al escribir la reserva
la columna `version` de una existencia cambió (otra reserva concurrente ganó), la
operación se aborta y el **mensaje se reintenta** desde la cola quorum
`inventario.ordenes`. Órdenes no se entera del reintento; solo del desenlace
final. Es la contrapartida que ADR-005 acepta a cambio del rendimiento de
escritura bajo pico.

---

## 4. Bindings

### 4.1 Cola que consume Inventario

| Cola | Dueño | Binding | Origen |
|---|---|---|---|
| `inventario.ordenes` | Jose | `ordenes.reserva.solicitada`, `ordenes.reserva.liberacion-solicitada`, `ordenes.devolucion.registrada` | `ordenes-eventos-v1`, eventos 3, 4 y 11 |

Cola quorum, durable, con dead-letter hacia `sgo.eventos.dlx` y
`x-delivery-limit = 5` (envelope, sección 5). Consumo idempotente por `eventId`
sobre `mensajes_procesados` (envelope, sección 2.2).

> **v1.1.** Los tres bindings se declaran en `OrdenesEventosConsumidor`
> (`SgoLogic.Inventario.Api/Consumers/OrdenesEventosConsumidor.cs`), que reemplaza a
> `ReservaSolicitadaConsumidor`. Es un único consumidor para los tres eventos:
> despacha por `eventType`, registra el `eventId` en `mensajes_procesados` en el mismo
> `SaveChanges` que el efecto de negocio y propaga `transactionId` y `causationId` a
> lo que publica. Un sobre que no se puede deserializar y una falla transitoria (base
> caída, choque de versión) se rechazan con reencolado; el sobre ilegible termina en
> la dead letter al agotar `x-delivery-limit`. Un `eventType` no esperado se confirma
> sin aplicarse.

### 4.2 Colas que consumen los eventos de Inventario

| Cola | Dueño | Binding |
|---|---|---|
| `ordenes.inventario` | Angel | `inventario.reserva.confirmada`, `inventario.reserva.rechazada`, `inventario.reserva.expirada` |
| `auditoria.eventos` | Julio | `#` |
| `consulta-estado.eventos` | Jonathan | `inventario.reserva.confirmada`, `inventario.reserva.rechazada`, `inventario.reserva.expirada` |

> **Binding nuevo con HU-002.** `inventario.reserva.expirada` requiere que Órdenes y
> Consulta de Estado agreguen ese binding a sus colas existentes. Mientras no lo
> hagan, el evento se publica igual (Auditoría lo recibe por `#`) y nada del flujo
> actual se rompe: es un binding aditivo, no un cambio en los eventos 1 y 2.

> **Eventos 4 y 5 (v1.1).** `inventario.reserva.liberada` e
> `inventario.stock.reingresado` no tienen hoy ningún binding explícito: solo los
> recibe `auditoria.eventos` por `#`. En el código, `ordenes.inventario` está ligada
> únicamente a `inventario.reserva.confirmada`, `.rechazada` y `.expirada`
> (`InventarioEventosConsumidor` de Órdenes), y la cola de Consulta de Estado
> (`ConsultaEstadoOrdenesConsumidor`) no declara bindings a eventos `inventario.*`.

### 4.3 Esquema de los eventos consumidos

El `data` completo lo define `ordenes-eventos-v1.md`; aquí se reproduce tal como lo
publica Órdenes y se indica qué campos usa Inventario. La serialización es la común
(`SgoJsonSerializerOptions`): nombres en camelCase, enumeraciones como texto y fechas
ISO 8601 UTC.

#### 4.3.1 `LiberacionReservaSolicitada` (v1.1)

Routing key `ordenes.reserva.liberacion-solicitada`, evento 4 de
`ordenes-eventos-v1`. Lo publica Órdenes en `EmitirLiberacionReservaSolicitada`
(`SgoLogic.Ordenes.Api/Application/Ordenes/Eventos/MapaEventosSagaOrden.cs:264`)
cuando una transición marcada como compensación afecta a una orden que ya tiene
`reservaId`. Si la orden no tiene reserva, Órdenes no lo publica y la reserva que
Inventario haya llegado a crear se libera por TTL.

| Campo de `data` | Tipo | Obligatorio | Uso en Inventario |
|---|---|---|---|
| `ordenId` | string | sí | Clave de búsqueda de la reserva: una orden tiene una sola reserva (regla 3 de la sección 6). |
| `reservaId` | string | sí | No se usa: la reserva se localiza por `ordenId`. |
| `motivo` | string | sí | Enum `MotivoLiberacionReserva`. Se copia al evento 4. Órdenes envía el motivo explícito del evento de origen o, sin él, el derivado del estado destino (`PAGO_PENDIENTE` da `COBRO_SIN_RESPUESTA`; `CANCELADA` y `CANCELADA_RESERVA_VENCIDA` dan `CANCELACION_CLIENTE`; cualquier otro, `TIMEOUT_SAGA`). |
| `solicitadaEn` | string | sí | No se usa. ISO 8601 UTC. |

Efecto (`ServicioLiberacionReservas`): si la reserva de la orden está `ACTIVA`,
devuelve cada línea al disponible, pasa la reserva a `LIBERADA` y publica el
evento 4. Si la orden no tiene reserva o la reserva ya no está `ACTIVA`, no cambia
nada ni publica.

#### 4.3.2 `DevolucionRegistrada` (v1.1)

Routing key `ordenes.devolucion.registrada`, evento 11 de `ordenes-eventos-v1`
(`aggregateType` `Devolucion`). Lo publica Órdenes en `ServicioDevolucion`
(`SgoLogic.Ordenes.Api/Application/Ordenes/ServicioDevolucion.cs:152`), con
`sequence` igual a la de la orden más 1.

| Campo de `data` | Tipo | Obligatorio | Uso en Inventario |
|---|---|---|---|
| `devolucionId` | string | sí | `aggregateId` del evento 5 y referencia en el log. |
| `ordenOriginalId` | string | sí | Se copia al evento 5. |
| `clienteId` | string | sí | No se usa. |
| `tipo` | string | sí | Enum `TipoDevolucion`. No se usa: `CAMBIO` y `DEVOLUCION` reingresan igual. |
| `esParcial` | boolean | sí | No se usa: se reingresa exactamente lo que traen las líneas. |
| `ubicacionReingresoId` | string | sí | Ubicación de respaldo para las líneas sin `ubicacionOrigenReservaId`. Se copia como `ubicacionId` del evento 5. |
| `lineas` | array | sí | Una entrada por SKU devuelto. |
| `lineas[].sku` | string | sí | SKU que reingresa. |
| `lineas[].cantidad` | integer | sí | Unidades que se suman a la cantidad física. Una línea con cantidad cero o negativa se omite. |
| `lineas[].motivo` | string | sí | No se usa. |
| `lineas[].skuReemplazo` | string \| null | sí | No se usa. |
| `lineas[].ubicacionOrigenReservaId` | string | sí (desde v1.3 de Órdenes) | Ubicación a la que vuelve la línea; tiene prioridad sobre `ubicacionReingresoId`. Si llega vacía se usa la del encabezado. Órdenes la llena con la ubicación de la reserva original de la orden. |
| `lineas[].reservaId` | string | sí (desde v1.3 de Órdenes) | No se usa. |
| `moneda` | string | sí | No se usa. |
| `montoReembolso` | string | sí | No se usa. |
| `pagoOriginalId` | string | sí | No se usa. |
| `documentoFiscalOriginalId` | string | sí | No se usa. |
| `registradaPor` | string | sí | No se usa. |
| `registradaEn` | string | sí | No se usa. ISO 8601 UTC. |

Efecto (`ServicioReingresoStock`): por cada línea útil suma la cantidad a la
cantidad física de la existencia de ese SKU en la ubicación resuelta, deja un
movimiento con motivo `REINGRESO_DEVOLUCION` en el ledger (HU-016) y publica el
evento 5. Si la existencia no está registrada en esa ubicación, la crea con cantidad
cero y tipo `TIENDA` antes de sumar. Si no reingresa ninguna línea, no publica.

---

## 5. Envelope aplicado a este contexto

| Campo | Valor en este contexto |
|---|---|
| `source` | `"inventario"` |
| `aggregateType` | `"Reserva"` en los eventos 1 a 4; `"Devolucion"` en el evento 5 |
| `aggregateId` | `reservaId` con formato `RSV-<año>-<correlativo de 6 dígitos>` en los eventos 1, 3 y 4; `ordenId` en el evento 2 (no hay agregado Reserva creado); `devolucionId` en el evento 5 |
| `sequence` | Asignado por Inventario, monotónico por `aggregateId`, empieza en 1. Los eventos 3 y 4 son excluyentes y emiten con `2` sobre el mismo `reservaId` del evento 1. El evento 5 emite siempre con `1` sobre el `devolucionId`. |
| `transactionId` | Heredado del evento de Órdenes que se está procesando: `ordenes.reserva.solicitada` en los eventos 1 y 2, `ordenes.reserva.liberacion-solicitada` en el 4 y `ordenes.devolucion.registrada` en el 5. Propagación automática (envelope, sección 4). En el evento 3 no hay mensaje entrante: el barrido es un proceso propio de Inventario, así que el `transactionId` se genera nuevo. |
| `causationId` | `eventId` del evento de Órdenes recibido, en los eventos 1, 2, 4 y 5. Vacío en el evento 3, por lo mismo. |

---

## 6. Reglas de emisión

1. **Toda publicación pasa por Outbox** (ADR-003). La existencia reservada, la
   reserva y la fila de outbox se persisten en un único `SaveChanges`, en la misma
   transacción que agrega la marca de mensaje procesado.
2. **El TTL efectivo lo fija Inventario.** El `ttlReservaSegundos` que llega en
   `ordenes.reserva.solicitada` es una sugerencia. Inventario lo recorta al rango
   `[60, 3600]` segundos (config `Inventario:TtlSegundos*`) y emite el valor
   aplicado en `ttlSegundosAplicado`. Cierra el supuesto 2 de `ordenes-eventos-v1`.
3. **Idempotencia de negocio por orden.** Una orden reserva una sola vez. Un
   `ordenes.reserva.solicitada` reentregado, o una llamada REST repetida, devuelve
   la reserva ya hecha y **no** re-emite `InventarioReservado`.
4. **Una sola ubicación por reserva.** La v1 elige una única ubicación que cubra
   todas las líneas. El origen óptimo multi-ubicación es HU-011 (Despacho), no
   Inventario.
5. **La expiración la decide PostgreSQL, no Redis.** El barrido de HU-002
   (`ExpiracionReservasWorker`, cada `Inventario:ExpiracionPollingMs`) consulta las
   reservas `ACTIVA` con `expiraEn <= now()` sobre el índice
   `ix_reserva_estado_expira_en`. Se descartó depender de las notificaciones de
   expiración de claves de Redis (`notify-keyspace-events`) como mecanismo
   autoritativo: son fire-and-forget, y si el servicio está caído en el instante en
   que la clave expira, la liberación se pierde para siempre. Cierra el supuesto 1
   de la sección 10.
6. **Una transacción por reserva expirada.** El barrido confirma reserva por reserva,
   no por lote. Si una falla (choque de bloqueo optimista contra una reserva
   concurrente sobre la misma existencia), se descartan sus cambios, se sigue con las
   demás del lote y esa reserva se vuelve a tomar en el siguiente ciclo porque sigue
   `ACTIVA` y vencida. Ninguna unidad se libera a medias.
7. **La liberación explícita solo actúa sobre una reserva `ACTIVA` (v1.1).** Una
   solicitud de liberación para una orden sin reserva, o cuya reserva ya expiró o ya
   se liberó, no cambia nada ni publica el evento 4. Así la expiración por TTL y la
   liberación explícita nunca devuelven dos veces las mismas unidades, y un mensaje
   repetido tampoco. La liberación, el evento y la marca de mensaje procesado se
   persisten en un único `SaveChanges`.
8. **El reingreso sube la cantidad física (v1.1).** A diferencia de liberar una
   reserva, que solo baja la cantidad reservada, el reingreso por devolución suma a
   la cantidad física y deja un asiento `REINGRESO_DEVOLUCION` en el ledger. Su
   idempotencia depende de la deduplicación por `eventId`: no hay control de negocio
   por `devolucionId`.

---

## 7. Catálogo detallado

En las secciones siguientes solo se muestra el contenido de `data`.

### 7.1 `InventarioReservado`

Routing key `inventario.reserva.confirmada`.

| Campo de `data` | Tipo | Obligatorio | Notas |
|---|---|---|---|
| `reservaId` | string | sí | `RSV-<año>-<correlativo de 6 dígitos>`. Igual que `aggregateId`. |
| `ordenId` | string | sí | Orden que originó la reserva. |
| `ubicacionId` | string | sí | Ubicación desde la que se reservó. |
| `tipoUbicacion` | string | sí | Enum `TipoUbicacion`. |
| `ttlSegundosAplicado` | integer | sí | TTL que Inventario aplicó de verdad (EV-23). |
| `lineas` | array | sí | Mínimo un elemento. |
| `lineas[].sku` | string | sí | |
| `lineas[].cantidad` | integer | sí | Mayor que cero. |
| `reservadaEn` | string | sí | ISO 8601 UTC. Momento del hecho. |
| `expiraEn` | string | sí | ISO 8601 UTC. `reservadaEn + ttlSegundosAplicado`. |

```json
{
  "reservaId": "RSV-2026-000042",
  "ordenId": "ORD-2026-000123",
  "ubicacionId": "CD-01",
  "tipoUbicacion": "CENTRO_DISTRIBUCION",
  "ttlSegundosAplicado": 900,
  "lineas": [
    { "sku": "SKU-LB-00871", "cantidad": 1 }
  ],
  "reservadaEn": "2026-09-09T14:30:06.000Z",
  "expiraEn": "2026-09-09T14:45:06.000Z"
}
```

### 7.2 `ReservaInventarioRechazada`

Routing key `inventario.reserva.rechazada`. No se crea reserva; `aggregateId` es el
`ordenId`.

| Campo de `data` | Tipo | Obligatorio | Notas |
|---|---|---|---|
| `ordenId` | string | sí | |
| `motivo` | string | sí | Enum `MotivoRechazoReserva`. |
| `lineas` | array | sí | Solo las líneas que no se pudieron cubrir. Puede venir vacío si el motivo es `SIN_UBICACION_ELEGIBLE`. |
| `lineas[].sku` | string | sí | |
| `lineas[].cantidadSolicitada` | integer | sí | |
| `lineas[].cantidadDisponible` | integer | sí | Mejor disponible encontrado entre las ubicaciones evaluadas. |
| `rechazadaEn` | string | sí | ISO 8601 UTC. |

```json
{
  "ordenId": "ORD-2026-000999",
  "motivo": "STOCK_INSUFICIENTE",
  "lineas": [
    { "sku": "SKU-LB-00871", "cantidadSolicitada": 5, "cantidadDisponible": 1 }
  ],
  "rechazadaEn": "2026-09-09T14:30:06.000Z"
}
```

### 7.3 `ReservaInventarioExpirada`

Routing key `inventario.reserva.expirada`. `aggregateId` es el `reservaId`, con
`sequence` 2 (la creación consumió la 1).

| Campo de `data` | Tipo | Obligatorio | Notas |
|---|---|---|---|
| `reservaId` | string | sí | Igual que `aggregateId`. La misma reserva del evento 1. |
| `ordenId` | string | sí | Orden que Órdenes debe cancelar. |
| `ubicacionId` | string | sí | Ubicación a la que volvieron las unidades. |
| `lineas` | array | sí | Las mismas líneas de la reserva: lo que se devolvió al disponible. |
| `lineas[].sku` | string | sí | |
| `lineas[].cantidad` | integer | sí | Mayor que cero. |
| `expiraEn` | string | sí | ISO 8601 UTC. El vencimiento que se cumplió. |
| `expiradaEn` | string | sí | ISO 8601 UTC. Momento en que el barrido la liberó. Posterior a `expiraEn` por el intervalo de polling. |

```json
{
  "reservaId": "RSV-2026-000042",
  "ordenId": "ORD-2026-000123",
  "ubicacionId": "CD-01",
  "lineas": [
    { "sku": "SKU-LB-00871", "cantidad": 1 }
  ],
  "expiraEn": "2026-09-09T14:45:06.000Z",
  "expiradaEn": "2026-09-09T14:45:09.000Z"
}
```

### 7.4 `ReservaInventarioLiberada` (v1.1)

Routing key `inventario.reserva.liberada`. `aggregateId` es el `reservaId`, con
`sequence` 2 (la creación consumió la 1). Se publica al procesar
`ordenes.reserva.liberacion-solicitada` sobre una reserva `ACTIVA` (HU-003). Se
distingue del evento 3 a propósito: allá el disparador es el reloj y aquí una
decisión de la saga.

| Campo de `data` | Tipo | Obligatorio | Notas |
|---|---|---|---|
| `reservaId` | string | sí | Igual que `aggregateId`. La misma reserva del evento 1. |
| `ordenId` | string | sí | Orden cuya compensación pidió la liberación. |
| `ubicacionId` | string | sí | Ubicación a la que volvieron las unidades. |
| `motivo` | string | sí | Enum `MotivoLiberacionReserva` (sección 2.3), copiado de la solicitud de Órdenes. |
| `lineas` | array | sí | Las mismas líneas de la reserva: lo que se devolvió al disponible. |
| `lineas[].sku` | string | sí | |
| `lineas[].cantidad` | integer | sí | Mayor que cero. |
| `liberadaEn` | string | sí | ISO 8601 UTC. Momento en que Inventario procesó la solicitud. |

```json
{
  "reservaId": "RSV-2026-000042",
  "ordenId": "ORD-2026-000123",
  "ubicacionId": "CD-01",
  "motivo": "COBRO_RECHAZADO",
  "lineas": [
    { "sku": "SKU-LB-00871", "cantidad": 1 }
  ],
  "liberadaEn": "2026-09-09T14:31:02.000Z"
}
```

### 7.5 `StockReingresado` (v1.1)

Routing key `inventario.stock.reingresado`. `aggregateType` es `Devolucion` y
`aggregateId` el `devolucionId`, con `sequence` 1. Se publica al procesar
`ordenes.devolucion.registrada` cuando al menos una línea reingresó (HU-014).

| Campo de `data` | Tipo | Obligatorio | Notas |
|---|---|---|---|
| `devolucionId` | string | sí | Igual que `aggregateId`. |
| `ordenOriginalId` | string | sí | Copiado de la devolución. |
| `ubicacionId` | string | sí | La `ubicacionReingresoId` del encabezado de la devolución. Cada línea pudo reingresar en otra ubicación (sección 4.3.2); este campo no la identifica. |
| `lineas` | array | sí | Solo las líneas que reingresaron. Mínimo un elemento. |
| `lineas[].sku` | string | sí | |
| `lineas[].cantidad` | integer | sí | Unidades sumadas a la cantidad física. Mayor que cero. |
| `lineas[].cantidadFisicaResultante` | integer | sí | Cantidad física de la existencia después del reingreso. |
| `reingresadoEn` | string | sí | ISO 8601 UTC. |

```json
{
  "devolucionId": "DEV-2026-000031",
  "ordenOriginalId": "ORD-2026-000123",
  "ubicacionId": "TDA-014",
  "lineas": [
    { "sku": "SKU-LB-00871", "cantidad": 1, "cantidadFisicaResultante": 12 }
  ],
  "reingresadoEn": "2026-09-12T10:15:03.000Z"
}
```

---

## 8. Matriz de consumo

| Consumidor | `InventarioReservado` | `ReservaInventarioRechazada` | `ReservaInventarioExpirada` | `ReservaInventarioLiberada` (v1.1) | `StockReingresado` (v1.1) |
|---|---|---|---|---|---|
| Órdenes | Avanza la saga a `INVENTARIO_RESERVADO`. Guarda `reservaId` para el despacho y la compensación. | Transición a `RECHAZADA`, no cobra. | Transición a `CANCELADA_RESERVA_VENCIDA` con `MotivoCancelacion.RESERVA_VENCIDA`. Ambos valores ya existen en los enums de Órdenes. | Sin binding: no lo consume. | Sin binding: no lo consume. |
| Auditoría | Inserta sin sobrescribir. | Inserta sin sobrescribir. | Inserta sin sobrescribir (llega por `#`, no requiere binding nuevo). | Inserta sin sobrescribir (llega por `#`). | Inserta sin sobrescribir (llega por `#`). |
| Consulta de Estado | Enriquece la vista 360 con la ubicación y el TTL. No materializa estado de Orden. | Marca la orden como rechazada por stock en la vista. | Marca la reserva como vencida y devuelve el disponible en la vista. | Sin binding: no lo consume. | Sin binding: no lo consume. |

**Pendiente del lado de Órdenes (Angel).** Inventario ya publica el evento; hasta que
Órdenes lo consuma, la reserva se libera igual y el inventario queda correcto, pero
**la orden no se cancela sola**: el tercer criterio de aceptación de HU-002 queda
cubierto por Inventario en su mitad y pendiente en la de Órdenes.

La cola `ordenes.inventario` de la sección 4.2 **todavía no existe en código**: hoy
Órdenes solo declara `ordenes.pagos` y `ordenes.facturacion`. Así que no es agregar un
binding a una cola existente, sino crear el consumidor completo.

Conviene declararla con los **tres** bindings de una vez
(`inventario.reserva.confirmada`, `.rechazada` y `.expirada`), siguiendo el patrón de
`PagosEventosConsumidor`: un `BackgroundService` que despacha por `eventType` dentro de
una sola cola. No hace falta una clase consumidora por evento — `PagosEventosConsumidor`
ya atiende cinco eventos de Pagos así. Consumir también `.confirmada` en el mismo paso
resuelve además el `reservaId` real, que hoy es el placeholder `"DES-PENDIENTE-{ordenId}"`
en `MapaEventosSagaOrden` y que HU-011 (Despacho) va a necesitar poblado.

Lo que del lado de Órdenes **ya está listo** y no hay que construir:

- `EstadoOrden.CANCELADA_RESERVA_VENCIDA` y `MotivoCancelacion.RESERVA_VENCIDA` existen.
- `MaquinaEstadosOrden` acepta la transición desde `INVENTARIO_RESERVADO` y desde
  `PAGO_PENDIENTE`.
- `MapaEventosSagaOrden` ya emite `OrdenCancelada` con el motivo correcto al llegar a
  ese estado.

> **Hueco a decidir antes de escribir el consumidor.** La transición a
> `CANCELADA_RESERVA_VENCIDA` **no** es legal desde `COBRO_SOLICITADO`, que es
> justamente el estado donde es más probable que venza el TTL (la orden está esperando
> a PayKlik mientras el reloj de la reserva corre). Con la máquina actual, un
> `inventario.reserva.expirada` que llegue en ese estado será rechazado por la
> transición. Hay que decidir si se agrega `COBRO_SOLICITADO -> CANCELADA_RESERVA_VENCIDA`,
> o si el cobro en vuelo debe ganarle al vencimiento (y entonces el consumidor ignora
> el evento en ese estado y compensa por otra vía). Es decisión de Órdenes, no de
> Inventario: el evento se publica igual en ambos casos.

---

## 9. Congelamiento

| Persona | Contextos | Estado |
|---|---|---|
| Jose Pirir (202300335) | Inventario, Despacho | autor |
| Angel Martínez (202300410) | Órdenes | pendiente de firma (consume ambos eventos, binding `ordenes.inventario`) |
| Julio Herrera | Auditoría, base técnica | pendiente de firma |
| Jonathan Jerónimo (202300353) | Consulta de Estado | pendiente de firma |

---

## 10. Supuestos abiertos

1. ~~**Redis y el TTL.**~~ **Cerrado en HU-002.** El barrido autoritativo corre
   contra PostgreSQL (regla 5 de la sección 6). Redis queda disponible como mejora
   opcional de latencia de detección, nunca como la única fuente de la liberación.
2. **Reasignación de ubicación.** Si el método de entrega cambia después de
   reservada (EV-31), la reserva de la v1 no se mueve de ubicación. Lo resuelve
   Despacho en HU-011.
