# Contrato de eventos del contexto Pagos — v1.2

**Proyecto:** SGO-Logic — Fase 2
**Curso:** Análisis y Diseño de Sistemas 2, Sección B
**Contexto emisor:** Pagos
**Estado:** propuesto para congelamiento
**Fecha:** 2026-09-20
**Depende de:** `sgo-eventos-envelope-v1.md`

---

## 0. Cambios respecto a v1.1

Se ejecuta la tokenización de ADR-010: el cobro con `TARJETA` se hace con el token
que PayKlik emite al tokenizar la tarjeta en el navegador, que Órdenes propaga en
`CobroSolicitado.tokenMedioPago` (`ordenes-eventos-v1`, evento 5). El contrato de
eventos de Pagos cambia en un solo punto: el campo `codigoRespuesta` de
`PagoRechazado` admite cuatro códigos internos nuevos, que distinguen un problema
con el token de un rechazo del proveedor.

| Código | Cuándo se emite |
|---|---|
| `TOKEN_REQUERIDO` | El cobro es con `TARJETA` y la solicitud no trae token. Pagos lo rechaza sin invocar a PayKlik. |
| `TOKEN_INVALIDO` | PayKlik respondió 422: no conoce el token. |
| `TOKEN_VENCIDO` | PayKlik respondió 422: el token expiró antes del cobro. |
| `TOKEN_YA_USADO` | PayKlik respondió 422: el token ya se usó en otro cargo (son de un solo uso). |

Cambio aditivo (envelope sección 8): no agrega ni quita campos, no cambia sus
tipos ni la lista de eventos, y ningún consumidor actual interpreta el valor de
`codigoRespuesta` de `PagoRechazado`; el consumidor de Órdenes trata cualquier
`PagoRechazado` igual. No requiere renegociar con los consumidores.

Un problema con el token es un error del cliente, no una falla del proveedor: el
pago queda `RECHAZADO` (no `PENDIENTE`), no hay enlace de reintento, no hay
reintento automático y no cuenta para el disyuntor (regla 1 de la sección 6).

## 0.0 Cambios de v1.1 respecto a v1

Se agregan dos eventos de resultado de reembolso (`ReembolsoAplicado`,
`ReembolsoFallido`), que Pagos publica al consumir `ordenes.devolucion.registrada`
(evento 11 de `ordenes-eventos-v1`, HU-014) y procesar el reembolso contra
PayKlik. Cierra el supuesto 2 de la v1 ("Reembolsos") y, junto con el binding que
ya declaraba la cola `pagos.ordenes`, el supuesto 1 ("Binding a devoluciones").

Cambio aditivo (envelope sección 8): agrega eventos nuevos, no toca los tres
existentes. No requiere renegociar con los consumidores actuales.

## 0.1 Por qué este documento existe

Pagos cierra HU-004: consume `ordenes.cobro.solicitado`, registra cada intento de
cobro **antes** de invocar al proveedor (PayKlik), lo invoca por REST con timeout,
y publica el resultado por Outbox. Órdenes necesita ese resultado para avanzar la
saga (CU-03): con el cobro aplicado pasa a `PAGO_APLICADO`, con el cobro
rechazado o sin respuesta pasa a `PAGO_PENDIENTE` y emite el enlace de reintento
al cliente (EV-28, ADR-010). Auditoría y Consulta de Estado también consumen estos
eventos.

Con esta versión, Pagos también cierra la mitad de HU-014 que le corresponde:
consume `ordenes.devolucion.registrada`, invoca el reembolso contra PayKlik con el
mismo patrón de registro-antes-de-invocar que el cobro, y publica el resultado.

Este documento define únicamente el contenido de `data` de los eventos que
**publica** Pagos. El sobre, las reglas de idempotencia y ordenamiento, las
enumeraciones compartidas y la topología del bus están en
`sgo-eventos-envelope-v1.md` y no se repiten aquí.

Pagos es el único emisor de eventos con routing key `pagos.*`.

Trazabilidad al DDA: ADR-003, ADR-009, ADR-010, RES-08, EV-25, EV-26, EV-28.

---

## 1. Restricción regulatoria dura

**Prohibido en todo payload de todo evento de Pagos, y en toda tabla del
contexto:** número completo de tarjeta (PAN), código de seguridad (CVV/CVC) y
fecha de vencimiento completa (RES-08, EV-25, ADR-010). Solo se transmite y se
persiste: token del proveedor, últimos cuatro dígitos y marca. El token del medio
de pago **no viaja** en los eventos de resultado; se queda en la base de Pagos.

El número de tarjeta y el CVV existen únicamente en el navegador y en PayKlik, que
tokeniza la tarjeta (`POST /v1/tokens`) y descarta esos datos. Pagos recibe el
token en `CobroSolicitado.tokenMedioPago` (`ordenes-eventos-v1`, evento 5), lo
envía a PayKlik en el cargo y persiste el token, los últimos cuatro dígitos y la
marca que PayKlik devuelve. El flujo completo está en la sección 14.3.1 del DDA.

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

Cinco eventos. Ninguno más se publica bajo `pagos.*` en esta versión.

| # | `eventType` | Routing key | CU | Consumidores |
|---|---|---|---|---|
| 1 | `PagoAplicado` | `pagos.pago.aplicado` | CU-03 | Órdenes, Consulta de Estado |
| 2 | `PagoRechazado` | `pagos.pago.rechazado` | CU-03 | Órdenes, Consulta de Estado |
| 3 | `PagoPendiente` | `pagos.pago.pendiente` | CU-03, HU-004 | Órdenes, Consulta de Estado |
| 4 | `ReembolsoAplicado` | `pagos.reembolso.aplicado` | HU-014 | Órdenes, Consulta de Estado |
| 5 | `ReembolsoFallido` | `pagos.reembolso.fallido` | HU-014 | Órdenes, Consulta de Estado |

Auditoría consume los cinco por su binding `#` y no se lista fila por fila.

---

## 4. Bindings

| Cola | Dueño | Bindings |
|---|---|---|
| `ordenes.pagos` | Angel (Órdenes) | `pagos.pago.aplicado`, `pagos.pago.rechazado`, `pagos.pago.pendiente`, `pagos.reembolso.aplicado`, `pagos.reembolso.fallido` |
| `consulta-estado.pagos` | Jonathan | `pagos.pago.*` |
| `auditoria.eventos` | Julio | `#` |

Pagos, además, **consume** `ordenes.cobro.solicitado` y `ordenes.devolucion.registrada`
en su cola `pagos.ordenes`, ambos bindings ya declarados en código
(`PagosEventosConsumidor`, del lado de Pagos). El contrato de Órdenes
(`ordenes-eventos-v1.md`, sección 4) es la fuente de verdad de esos dos bindings.
Como `EventConsumerBase` está tipado a un único `TData` por cola y esta cola
recibe dos `data` distintos, `pagos.ordenes` no usa esa clase base: despacha por
`eventType` a `ServicioCobro` o a `ServicioReembolso` según corresponda.

---

## 5. Envelope aplicado a este contexto

| Campo | Valor en este contexto |
|---|---|
| `source` | `"pagos"` |
| `aggregateType` | `"Pago"` en los cinco eventos. |
| `aggregateId` | En los eventos 1 a 3, el `ordenId` de la orden que se está cobrando (formato `ORD-<año>-<correlativo de 6 dígitos>`); Pagos es un agregado por orden. En los eventos 4 y 5 (reembolso), el `devolucionId` (formato `DEV-<año>-<correlativo de 6 dígitos>`): el reembolso se identifica por la devolución que lo origina, no por la orden, porque una orden puede tener más de una devolución. |
| `sequence` | En los eventos 1 a 3, asignado por Pagos, monotónico por `ordenId`, empieza en 1 con el primer evento de resultado de ese pago; un pago que pasa por `PENDIENTE` y luego `APLICADO` emite dos eventos con `sequence` 1 y 2. En los eventos 4 y 5, siempre `1`: cada devolución dispara un único intento de reembolso, sin reintento automático. |

---

## 6. Reglas de emisión

1. **Un intento de cobro produce exactamente un evento de resultado**, según el
   desenlace de la invocación a PayKlik:
   - Aprobado → `PagoAplicado`.
   - Rechazado → `PagoRechazado`.
   - Cobro con `TARJETA` sin token, o token que PayKlik declara inexistente,
     vencido o ya usado (422) → `PagoRechazado` con `codigoRespuesta`
     `TOKEN_REQUERIDO`, `TOKEN_INVALIDO`, `TOKEN_VENCIDO` o `TOKEN_YA_USADO`. Es un
     error del cliente y no una falla del proveedor: no hay reintento, no hay
     enlace de reintento y el disyuntor no lo cuenta. Con `TOKEN_REQUERIDO` Pagos
     ni siquiera invoca a PayKlik.
   - Timeout, 5xx o excepción de red → `PagoPendiente` (con enlace de reintento).
2. **Un reembolso produce exactamente un evento de resultado**, con la misma
   lógica de desenlace que el cobro pero sin enlace de reintento ni estado
   `PENDIENTE`: aprobado → `ReembolsoAplicado`; rechazado, o sin respuesta útil
   del proveedor (timeout, 5xx, red, circuito abierto) → `ReembolsoFallido`. A
   diferencia del cobro, una falla técnica y un rechazo de negocio producen el
   mismo evento; el campo `codigoRespuesta` distingue la causa.
3. **El evento se encola en la misma transacción** que registra el resultado del
   intento y el cambio de estado del pago o de la devolución (el consumidor de la
   cola hace un único `SaveChangesAsync` que cubre el intento, el pago, el enlace
   cuando aplica, la fila de outbox y la marca de mensaje procesado).
4. **Una falla del proveedor no es un error de proceso.** El consumidor no
   nackea el mensaje por un cobro o un reembolso fallido: registra el resultado
   de negocio y ackea. Solo nackea ante errores técnicos de su propio lado (base
   caída).
5. **`sequence`** lo lleva el agregado, no el publicador. No se inventa. En
   reembolso siempre vale `1` (regla 5 del envelope aplicado, sección 5).
6. **`transactionId` y `causationId`** se heredan del evento consumido
   (`CobroSolicitado` o `DevolucionRegistrada`, según corresponda). Los puebla el
   consumidor en `EventContext`; `IOutboxWriter` los lee de ahí. El código de
   Pagos nunca los pasa a mano.

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
| `codigoRespuesta` | string | sí | Código del proveedor (`"51"` fondos insuficientes, etc.) o, desde v1.2, uno de los códigos internos de Pagos `"TOKEN_REQUERIDO"`, `"TOKEN_INVALIDO"`, `"TOKEN_VENCIDO"` y `"TOKEN_YA_USADO"` (sección 0), que indican un problema con el token del medio de pago y no un rechazo del proveedor. |
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

### 7.4 `ReembolsoAplicado`

Routing key `pagos.reembolso.aplicado`. Se emite cuando PayKlik aprueba el
reembolso de una devolución (HU-014, ADR-009 para el disyuntor que protege esta
llamada igual que la de cobro).

| Campo de `data` | Tipo | Obligatorio | Notas |
|---|---|---|---|
| `devolucionId` | string | sí | `DEV-2026-000031`. Identificador de la devolución que origina el reembolso; ver `ordenes-eventos-v1.md` evento 11. |
| `ordenOriginalId` | string | sí | `ORD-2026-000123`. Orden sobre la que se aplicó el cobro original. |
| `clienteId` | string | sí | |
| `moneda` | string | sí | Enum `Moneda`. |
| `monto` | string | sí | Decimal como cadena. |
| `codigoRespuesta` | string | sí | Código del proveedor (`"00"` en aprobación). |
| `autorizacionId` | string | sí | Identificador de autorización devuelto por el proveedor. |
| `ocurridoEn` | string | sí | ISO 8601 UTC. |

```json
{
  "devolucionId": "DEV-2026-000031",
  "ordenOriginalId": "ORD-2026-000123",
  "clienteId": "CLI-000045",
  "moneda": "GTQ",
  "monto": "1400.00",
  "codigoRespuesta": "00",
  "autorizacionId": "PK-9C817B034F2A",
  "ocurridoEn": "2026-09-12T10:16:20.400Z"
}
```

---

### 7.5 `ReembolsoFallido`

Routing key `pagos.reembolso.fallido`. Se emite cuando PayKlik rechaza el
reembolso de forma explícita, o cuando no responde: timeout, 5xx, excepción de
red o circuito abierto (ADR-009). A diferencia de `PagoPendiente`, no hay estado
intermedio ni enlace de reintento: la devolución queda marcada como reembolso
fallido y su resolución es manual, fuera de este contrato.

| Campo de `data` | Tipo | Obligatorio | Notas |
|---|---|---|---|
| `devolucionId` | string | sí | |
| `ordenOriginalId` | string | sí | |
| `clienteId` | string | sí | |
| `moneda` | string | sí | Enum `Moneda`. |
| `monto` | string | sí | Decimal como cadena. |
| `codigoRespuesta` | string | sí | Código del proveedor en un rechazo explícito, o código interno de Pagos ante falla técnica (`"CIRCUITO_ABIERTO"`, `"PROVEEDOR_5XX"`, `"RED"`, `"TIMEOUT"`). |
| `motivoFalla` | string | sí | Texto corto legible de la causa. |
| `ocurridoEn` | string | sí | ISO 8601 UTC. |

```json
{
  "devolucionId": "DEV-2026-000031",
  "ordenOriginalId": "ORD-2026-000123",
  "clienteId": "CLI-000045",
  "moneda": "GTQ",
  "monto": "1400.00",
  "codigoRespuesta": "CIRCUITO_ABIERTO",
  "motivoFalla": "El disyuntor de PayKlik esta abierto; no se invoco al proveedor.",
  "ocurridoEn": "2026-09-12T10:16:20.400Z"
}
```

---

## 8. Matriz de consumo

| Consumidor | Dueño | HU | Eventos | Para qué |
|---|---|---|---|---|
| Auditoría | Julio | HU-006 | Los cinco, por binding `#` | Bitácora append-only indexada por `transactionId` (EV-30, EAC-03). |
| Órdenes | Angel | CU-03, HU-014 | 1, 2, 3, 4, 5 | Avanzar la saga: `PAGO_APLICADO` con el evento 1; `PAGO_PENDIENTE` con el evento 3 (y emitir el enlace al cliente); tratar el rechazo con el evento 2. Con los eventos 4 y 5, cerrar la devolución (`MarcarReembolsoAplicado` / `MarcarReembolsoFallido`) sin cambiar el estado de la orden. |
| Consulta de Estado | Jonathan | HU-010 | `pagos.pago.*` | Enriquecer la vista 360 con el estado del cobro. No materializa estado de Orden. |

---

## 9. Congelamiento

Una vez congelado, cualquier cambio se avisa al grupo antes de hacerlo.

| Revisor | Contexto | Estado | Fecha |
|---|---|---|---|
| Angel Martínez (202300410) | Pagos, Órdenes | autor | 2026-09-12 |
| Julio Herrera | Auditoría, base técnica | pendiente | |
| Jonathan Jerónimo | Consulta de Estado | pendiente | |

---

## 10. Supuestos abiertos

1. **`intentoNumero`.** Lo lleva Pagos con su propio contador por `ordenId`. El
   `intentoNumero` que trae `CobroSolicitado` desde Órdenes es informativo; la
   fuente de verdad del número de intento es Pagos. Los eventos de reembolso no
   llevan `intentoNumero`: cada devolución dispara un único intento, sin
   reintento automático.
