# Contrato de eventos del contexto Pagos — v1

**Proyecto:** SGO-Logic — Fase 2
**Curso:** Análisis y Diseño de Sistemas 2, Sección B
**Contexto emisor:** Pagos
**Estado:** propuesto para congelamiento
**Fecha:** 2026-09-09
**Depende de:** `sgo-eventos-envelope-v1.md`

---

## 0. Por qué este documento existe

Pagos cierra HU-004: consume `ordenes.cobro.solicitado`, registra cada intento de
cobro **antes** de invocar al proveedor (PayKlik), lo invoca por REST con timeout,
y publica el resultado por Outbox. Órdenes necesita ese resultado para avanzar la
saga (CU-03): con el cobro aplicado pasa a `PAGO_APLICADO`, con el cobro
rechazado o sin respuesta pasa a `PAGO_PENDIENTE` y emite el enlace de reintento
al cliente (EV-28, ADR-010). Auditoría y Consulta de Estado también consumen estos
eventos.

Este documento define únicamente el contenido de `data` de los eventos que
**publica** Pagos. El sobre, las reglas de idempotencia y ordenamiento, las
enumeraciones compartidas y la topología del bus están en
`sgo-eventos-envelope-v1.md` y no se repiten aquí.

Pagos es el único emisor de eventos con routing key `pagos.*`.

Trazabilidad al DDA: ADR-003, ADR-010, RES-08, EV-25, EV-26, EV-28.

---

## 1. Restricción regulatoria dura

**Prohibido en todo payload de todo evento de Pagos, y en toda tabla del
contexto:** número completo de tarjeta (PAN), código de seguridad (CVV/CVC) y
fecha de vencimiento completa (RES-08, EV-25, ADR-010). Solo se transmite y se
persiste: token del proveedor, últimos cuatro dígitos y marca. El token del medio
de pago **no viaja** en los eventos de resultado; se queda en la base de Pagos.

---

## 2. Enumeraciones propias del contexto

Las compartidas (`Canal`, `Moneda`, `MedioPago`) están en el documento de
envelope.

### 2.1 `EstadoPago`

Estado del agregado `Pago`. Lista cerrada.

| Valor | Significado |
|---|---|
| `INICIADO` | Pago creado, con al menos un intento en curso, sin resolución todavía. |
| `APLICADO` | Un intento fue aprobado por el proveedor. Terminal feliz. |
| `PENDIENTE` | El último intento no obtuvo respuesta del proveedor (timeout, 5xx, red). Hay un enlace de reintento vigente. No es terminal: un reintento exitoso lo lleva a `APLICADO`. |
| `RECHAZADO` | Un intento fue rechazado explícitamente por el proveedor. |

### 2.2 `EstadoIntentoPago`

Interno de Pagos, **no viaja en eventos**. Se documenta para entender el flujo:
`INICIADO`, `EXITOSO`, `FALLIDO`, `RECHAZADO`. Un `IntentoPago` se registra en
`INICIADO` antes de invocar al proveedor y nunca se borra: el criterio de
aceptación de HU-004 exige que el intento quede registrado aunque el proveedor no
responda.

---

## 3. Catálogo de eventos

Tres eventos. Ninguno más se publica bajo `pagos.*` en la versión 1.

| # | `eventType` | Routing key | CU | Consumidores |
|---|---|---|---|---|
| 1 | `PagoAplicado` | `pagos.pago.aplicado` | CU-03 | Órdenes, Consulta de Estado |
| 2 | `PagoRechazado` | `pagos.pago.rechazado` | CU-03 | Órdenes, Consulta de Estado |
| 3 | `PagoPendiente` | `pagos.pago.pendiente` | CU-03, HU-004 | Órdenes, Consulta de Estado |

Auditoría consume los tres por su binding `#` y no se lista fila por fila.

---

## 4. Bindings

| Cola | Dueño | Bindings |
|---|---|---|
| `ordenes.pagos` | Angel (Órdenes) | `pagos.pago.aplicado`, `pagos.pago.rechazado`, `pagos.pago.pendiente` |
| `consulta-estado.pagos` | Jonathan | `pagos.pago.*` |
| `auditoria.eventos` | Julio | `#` |

Pagos, además, **consume** `ordenes.cobro.solicitado` en su cola `pagos.ordenes`.
El contrato de Órdenes (`ordenes-eventos-v1.md`, sección 4) fija que esa misma
cola también se liga a `ordenes.devolucion.registrada`. Ese evento todavía no
existe y `EventConsumerBase` está tipado a un solo `TData`, así que hoy la cola
declara **solo** el binding de cobro. El binding a devoluciones queda pendiente
explícito para cuando exista el evento 11 de Órdenes.

---

## 5. Envelope aplicado a este contexto

| Campo | Valor en este contexto |
|---|---|
| `source` | `"pagos"` |
| `aggregateType` | `"Pago"` |
| `aggregateId` | El `ordenId` de la orden que se está cobrando (formato `ORD-<año>-<correlativo de 6 dígitos>`). Pagos es un agregado por orden. |
| `sequence` | Asignado por Pagos, monotónico por `ordenId`, empieza en 1 con el primer evento de resultado de ese pago. Un pago que pasa por `PENDIENTE` y luego `APLICADO` emite dos eventos con `sequence` 1 y 2. |

---

## 6. Reglas de emisión

1. **Un intento de cobro produce exactamente un evento de resultado**, según el
   desenlace de la invocación a PayKlik:
   - Aprobado → `PagoAplicado`.
   - Rechazado → `PagoRechazado`.
   - Timeout, 5xx o excepción de red → `PagoPendiente` (con enlace de reintento).
2. **El evento se encola en la misma transacción** que registra el resultado del
   intento y el cambio de estado del pago (`EventConsumerBase` hace un único
   `SaveChangesAsync` que cubre el intento, el pago, el enlace, la fila de outbox
   y la marca de mensaje procesado).
3. **Una falla del proveedor no es un error de proceso.** El consumidor no
   nackea el mensaje por un cobro fallido: registra el resultado de negocio y
   ackea. Solo nackea ante errores técnicos de su propio lado (base caída).
4. **`sequence`** lo lleva el agregado `Pago`, no el publicador. No se inventa.
5. **`transactionId` y `causationId`** se heredan del evento `CobroSolicitado`
   consumido (los puebla `EventConsumerBase` en `EventContext`; `IOutboxWriter`
   los lee de ahí). El código de Pagos nunca los pasa a mano.

---

## 7. Catálogo detallado

En las secciones siguientes solo se muestra el contenido de `data`.

### 7.1 `PagoAplicado`

Routing key `pagos.pago.aplicado`. Se emite cuando PayKlik aprueba el cobro.

| Campo de `data` | Tipo | Obligatorio | Notas |
|---|---|---|---|
| `ordenId` | string | sí | `ORD-2026-000123`. |
| `clienteId` | string | sí | |
| `canal` | string | sí | Enum `Canal`. Se audita por EV-26. |
| `moneda` | string | sí | Enum `Moneda`. |
| `monto` | string | sí | Decimal como cadena. |
| `medioPago` | string | sí | Enum `MedioPago`. |
| `intentoNumero` | integer | sí | Número del intento que resultó aprobado. Empieza en 1. |
| `estado` | string | sí | Siempre `"APLICADO"`. Presente para que el consumidor use un mapeador uniforme. |
| `codigoRespuesta` | string | sí | Código del proveedor (`"00"` en aprobación). |
| `autorizacionId` | string | sí | Identificador de autorización devuelto por el proveedor. |
| `ultimosCuatro` | string \| null | sí | Exactamente cuatro dígitos, o `null`. |
| `marca` | string \| null | sí | `VISA`, `MASTERCARD`, `AMEX` o `null`. |
| `ocurridoEn` | string | sí | ISO 8601 UTC. Momento del hecho de negocio. |

```json
{
  "ordenId": "ORD-2026-000123",
  "clienteId": "CLI-000045",
  "canal": "WEB",
  "moneda": "GTQ",
  "monto": "1400.00",
  "medioPago": "TARJETA",
  "intentoNumero": 1,
  "estado": "APLICADO",
  "codigoRespuesta": "00",
  "autorizacionId": "PK-4F2A9C817B03",
  "ultimosCuatro": "4242",
  "marca": "VISA",
  "ocurridoEn": "2026-09-09T14:31:05.120Z"
}
```

---

### 7.2 `PagoRechazado`

Routing key `pagos.pago.rechazado`. Se emite cuando PayKlik rechaza el cobro de
forma explícita.

| Campo de `data` | Tipo | Obligatorio | Notas |
|---|---|---|---|
| `ordenId` | string | sí | |
| `clienteId` | string | sí | |
| `canal` | string | sí | Enum `Canal`. |
| `moneda` | string | sí | Enum `Moneda`. |
| `monto` | string | sí | Decimal como cadena. |
| `medioPago` | string | sí | Enum `MedioPago`. |
| `intentoNumero` | integer | sí | Número del intento rechazado. |
| `estado` | string | sí | Siempre `"RECHAZADO"`. |
| `codigoRespuesta` | string | sí | Código del proveedor (`"51"` fondos insuficientes, etc.). |
| `ocurridoEn` | string | sí | ISO 8601 UTC. |

```json
{
  "ordenId": "ORD-2026-000123",
  "clienteId": "CLI-000045",
  "canal": "WEB",
  "moneda": "GTQ",
  "monto": "1400.00",
  "medioPago": "TARJETA",
  "intentoNumero": 2,
  "estado": "RECHAZADO",
  "codigoRespuesta": "51",
  "ocurridoEn": "2026-09-09T14:33:40.900Z"
}
```

---

### 7.3 `PagoPendiente`

Routing key `pagos.pago.pendiente`. Se emite cuando el proveedor no resuelve el
cobro: timeout configurado, respuesta 5xx o excepción de red. El intento queda
`FALLIDO`, el pago `PENDIENTE`, y Pagos genera un enlace de reintento con token
opaco y expiración configurable.

| Campo de `data` | Tipo | Obligatorio | Notas |
|---|---|---|---|
| `ordenId` | string | sí | |
| `clienteId` | string | sí | |
| `canal` | string | sí | Enum `Canal`. |
| `moneda` | string | sí | Enum `Moneda`. |
| `monto` | string | sí | Decimal como cadena. |
| `medioPago` | string | sí | Enum `MedioPago`. |
| `intentoNumero` | integer | sí | Número del intento que no obtuvo respuesta. |
| `estado` | string | sí | Siempre `"PENDIENTE"`. |
| `codigoRespuesta` | string | sí | Código interno de Pagos para la falla (`"TIMEOUT"`, `"PROVEEDOR_5XX"`, `"RED"`). |
| `motivoFalla` | string | sí | Texto corto legible de la causa. |
| `enlaceReintento` | object | sí | Enlace para reintentar el cobro. |
| `enlaceReintento.token` | string | sí | Token opaco. No expone nada del medio de pago. |
| `enlaceReintento.url` | string | sí | Ruta relativa al servicio Pagos: `/api/pagos/reintentos/<token>`. |
| `enlaceReintento.expiraEn` | string | sí | ISO 8601 UTC. |
| `ocurridoEn` | string | sí | ISO 8601 UTC. |

```json
{
  "ordenId": "ORD-2026-000123",
  "clienteId": "CLI-000045",
  "canal": "WEB",
  "moneda": "GTQ",
  "monto": "1400.00",
  "medioPago": "TARJETA",
  "intentoNumero": 1,
  "estado": "PENDIENTE",
  "codigoRespuesta": "TIMEOUT",
  "motivoFalla": "PayKlik no respondio dentro de los 5000 ms configurados",
  "enlaceReintento": {
    "token": "1f3c8a2e5b7d4096a1c4e8b277d34f19",
    "url": "/api/pagos/reintentos/1f3c8a2e5b7d4096a1c4e8b277d34f19",
    "expiraEn": "2026-09-09T15:31:00.000Z"
  },
  "ocurridoEn": "2026-09-09T14:31:05.000Z"
}
```

---

## 8. Matriz de consumo

| Consumidor | Dueño | HU | Eventos | Para qué |
|---|---|---|---|---|
| Auditoría | Julio | HU-006 | Los tres, por binding `#` | Bitácora append-only indexada por `transactionId` (EV-30, EAC-03). |
| Órdenes | Angel | CU-03 (mañana) | 1, 2, 3 | Avanzar la saga: `PAGO_APLICADO` con el evento 1; `PAGO_PENDIENTE` con el evento 3 (y emitir el enlace al cliente); tratar el rechazo con el evento 2. |
| Consulta de Estado | Jonathan | HU-010 | `pagos.pago.*` | Enriquecer la vista 360 con el estado del cobro. No materializa estado de Orden. |

---

## 9. Congelamiento

Una vez congelado, cualquier cambio se avisa al grupo antes de hacerlo.

| Revisor | Contexto | Estado | Fecha |
|---|---|---|---|
| Angel Martínez (202300410) | Pagos, Órdenes | autor | 2026-09-09 |
| Julio Herrera | Auditoría, base técnica | pendiente | |
| Jonathan Jerónimo | Consulta de Estado | pendiente | |

---

## 10. Supuestos abiertos

1. **Binding a devoluciones.** `pagos.ordenes` se ligará a
   `ordenes.devolucion.registrada` cuando ese evento exista; requerirá un
   segundo consumidor tipado a su `data`.
2. **Reembolsos.** HU-014 (nota de crédito y reembolso) no está en esta versión;
   agregará al menos un evento `pagos.reembolso.*`.
3. **`intentoNumero`.** Lo lleva Pagos con su propio contador por `ordenId`. El
   `intentoNumero` que trae `CobroSolicitado` desde Órdenes es informativo; la
   fuente de verdad del número de intento es Pagos.
