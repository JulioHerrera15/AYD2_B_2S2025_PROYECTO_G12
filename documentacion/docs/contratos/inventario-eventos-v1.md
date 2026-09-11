# Contrato de eventos del contexto Inventario — v1

**Proyecto:** SGO-Logic — Fase 2
**Curso:** Análisis y Diseño de Sistemas 2, Sección B
**Contexto emisor:** Inventario
**Autor:** 202300335 — Jose Pirir
**Estado:** propuesto para congelamiento
**Fecha:** 2026-09-09
**Depende de:** `sgo-eventos-envelope-v1.md`, `ordenes-eventos-v1.md`

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

Este documento define únicamente el contenido de `data` de los eventos que
**publica** Inventario. El sobre, las reglas de idempotencia y ordenamiento, las
enumeraciones compartidas y la topología del bus están en
`sgo-eventos-envelope-v1.md` y no se repiten aquí.

Inventario es el único emisor de eventos con routing key `inventario.*`.

Trazabilidad al DDA: ADR-003, ADR-005, ADR-006, EV-22, EV-23, EV-05.

---

## 1. Alcance de la v1

Cubre solo el desenlace de una solicitud de reserva (HU-001). Quedan **fuera**,
para versiones aditivas posteriores:

- `inventario.reserva.expirada` — liberación por TTL (HU-002, EV-23).
- `inventario.reserva.liberada` — respuesta a `ordenes.reserva.liberacion-solicitada` (compensación de la saga, HU-003).
- `inventario.existencia.ajustada` — reingreso por devolución (HU-014).

Agregarlos no renegocia este contrato (envelope, sección 8: cambio aditivo).

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

Interno de Inventario, **no viaja en eventos** en la v1. Se documenta para
entender el flujo: `ACTIVA`, `EXPIRADA`, `LIBERADA`. Una `Reserva` se crea
`ACTIVA` con `expiraEn = creadaEn + ttlSegundos`.

---

## 3. Catálogo de eventos

Dos eventos. Ninguno más se publica bajo `inventario.*` en la v1.

| # | `eventType` | Routing key | `aggregateType` | Se emite cuando |
|---|---|---|---|---|
| 1 | `InventarioReservado` | `inventario.reserva.confirmada` | `Reserva` | La reserva se creó con éxito y su TTL está vigente. |
| 2 | `ReservaInventarioRechazada` | `inventario.reserva.rechazada` | `Reserva` | No hay stock suficiente en ninguna ubicación elegible. No se crea reserva. |

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
| `inventario.ordenes` | Jose | `ordenes.reserva.solicitada` | `ordenes-eventos-v1`, evento 3 |

Cola quorum, durable, con dead-letter hacia `sgo.eventos.dlx` y
`x-delivery-limit = 5` (envelope, sección 5). Consumo idempotente por `eventId`
sobre `mensajes_procesados` (envelope, sección 2.2).

> La liberación por compensación (`ordenes.reserva.liberacion-solicitada`, evento 4
> de Órdenes) se ligará a esta misma cola en la v1.1, junto con el evento
> `inventario.reserva.liberada`.

### 4.2 Colas que consumen los eventos de Inventario

| Cola | Dueño | Binding |
|---|---|---|
| `ordenes.inventario` | Angel | `inventario.reserva.confirmada`, `inventario.reserva.rechazada` |
| `auditoria.eventos` | Julio | `#` |
| `consulta-estado.eventos` | Jonathan | `inventario.reserva.confirmada`, `inventario.reserva.rechazada` |

---

## 5. Envelope aplicado a este contexto

| Campo | Valor en este contexto |
|---|---|
| `source` | `"inventario"` |
| `aggregateType` | `"Reserva"` en ambos eventos |
| `aggregateId` | `reservaId` con formato `RSV-<año>-<correlativo de 6 dígitos>` en el evento 1; `ordenId` en el evento 2 (no hay agregado Reserva creado) |
| `sequence` | Asignado por Inventario, monotónico por `aggregateId`, empieza en 1 |
| `transactionId` | Heredado del evento `ordenes.reserva.solicitada` que originó la reserva. Propagación automática (envelope, sección 4). |
| `causationId` | `eventId` del `ordenes.reserva.solicitada` recibido |

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

---

## 8. Matriz de consumo

| Consumidor | `InventarioReservado` | `ReservaInventarioRechazada` |
|---|---|---|
| Órdenes | Avanza la saga a `INVENTARIO_RESERVADO`. Guarda `reservaId` para el despacho y la compensación. | Transición a `RECHAZADA`, no cobra. |
| Auditoría | Inserta sin sobrescribir. | Inserta sin sobrescribir. |
| Consulta de Estado | Enriquece la vista 360 con la ubicación y el TTL. No materializa estado de Orden. | Marca la orden como rechazada por stock en la vista. |

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

1. **Redis y el TTL.** ADR-005 prevé gestionar la expiración "junto a Redis". La v1
   persiste `expiraEn` en PostgreSQL y deja el barrido de expirados para HU-002; el
   uso de `notify-keyspace-events` de Redis (ya configurado en `docker-compose`) se
   decide en esa historia.
2. **Reasignación de ubicación.** Si el método de entrega cambia después de
   reservada (EV-31), la reserva de la v1 no se mueve de ubicación. Lo resuelve
   Despacho en HU-011.
