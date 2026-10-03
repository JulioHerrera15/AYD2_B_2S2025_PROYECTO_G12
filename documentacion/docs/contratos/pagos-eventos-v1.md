# Contrato de eventos del contexto Pagos — v1.4

**Proyecto:** SGO-Logic — Fase 2
**Curso:** Análisis y Diseño de Sistemas 2, Sección B
**Contexto emisor:** Pagos
**Estado:** propuesto para congelamiento
**Fecha:** 2026-09-22
**Depende de:** `sgo-eventos-envelope-v1.md`

---

## 0. Cambios de v1.4 respecto a v1.3 (crédito, bloque 3)

Pagos cobra los abonos de las cuentas por cobrar de Crédito. Consume
`CobroAbonoCreditoSolicitado` (`credito.abono.cobro-solicitado`) en la cola nueva
`pagos.credito` y publica tres eventos nuevos (7, 8 y 9) bajo
`pagos.abono-credito.*`, fuera de `pagos.pago.*` para que Órdenes y Consulta de
Estado no los reciban. Reutiliza el cliente de PayKlik, el disyuntor, la bóveda de
tokens y el registro de intentos del cobro de órdenes.

- Un `Pago` por abono, con la columna nueva `concepto` (`ORDEN` por defecto,
  `ABONO_CREDITO`); migración `ConceptoPago`. Para un abono, `pago.orden_id`
  guarda la `referencia` del abono (`ABO-...`). Las consultas y reembolsos por
  orden filtran `concepto = ORDEN`, así que nunca ven abonos.
- La clave de idempotencia hacia PayKlik es el id del `Pago`, estable para todos
  los intentos del mismo abono: un reintento no puede cobrar dos veces.
- Un abono nunca emite `PagoAplicado`, `PagoRechazado`, `PagoPendiente` ni
  `IntentoPagoFallido`, ni crea enlace de reintento: el reintento lo pide el
  cliente desde "Mi crédito" y llega como otro `CobroAbonoCreditoSolicitado` con el
  mismo `abonoId`.

El cobro de órdenes no cambia. Cambio aditivo: no toca los seis eventos existentes.

## 0.0.0 Cambios de v1.3 respecto a v1.2

Se agrega el evento 6, `IntentoPagoFallido` (`pagos.intento.fallido`), solo para
auditoría. EAC-01 exige el 100% de intentos fallidos auditados, pero con el
reintento con backoff (ADR-009) un cobro puede hacer dos llamadas a PayKlik: las
dos quedaban como intento `FALLIDO` en `pagos.intento_pago` y solo la última
llegaba a Auditoría, dentro de `PagoPendiente`. La prueba de carga del 2026-09-19
medía 90.30% con ese criterio.

Desde v1.3, cada intento de cobro que termina `FALLIDO` (timeout, 5xx, red o
circuito abierto), intermedio o final, emite un `IntentoPagoFallido` por el
Outbox, en la misma transacción que registra el intento. No es un evento de
resultado: la regla 1 de la sección 6 no cambia y cada cobro sigue produciendo
exactamente un `PagoAplicado`, `PagoRechazado` o `PagoPendiente`, con la misma
`sequence` que antes. La routing key queda fuera de `pagos.pago.*` para que
ningún consumidor de resultados lo reciba; solo lo recibe Auditoría por `#`.

Cambio aditivo (envelope sección 8): agrega un evento nuevo y no toca los cinco
existentes. No requiere renegociar con los consumidores.

## 0.0 Cambios de v1.2 respecto a v1.1

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

## 0.00 Cambios de v1.1 respecto a v1

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

Nueve eventos: cinco de resultado de órdenes, uno de auditoría y tres de
resultado de abonos a crédito (v1.4). Ninguno más se publica bajo `pagos.*` en
esta versión.

| # | `eventType` | Routing key | CU | Consumidores |
|---|---|---|---|---|
| 1 | `PagoAplicado` | `pagos.pago.aplicado` | CU-03 | Órdenes, Consulta de Estado |
| 2 | `PagoRechazado` | `pagos.pago.rechazado` | CU-03 | Órdenes, Consulta de Estado |
| 3 | `PagoPendiente` | `pagos.pago.pendiente` | CU-03, HU-004 | Órdenes, Consulta de Estado |
| 4 | `ReembolsoAplicado` | `pagos.reembolso.aplicado` | HU-014 | Órdenes, Consulta de Estado |
| 5 | `ReembolsoFallido` | `pagos.reembolso.fallido` | HU-014 | Órdenes, Consulta de Estado |
| 6 | `IntentoPagoFallido` | `pagos.intento.fallido` | CU-03, HU-004, EAC-01 | Solo Auditoría |
| 7 | `AbonoCreditoCobrado` | `pagos.abono-credito.cobrado` | Crédito bloque 3 | Crédito |
| 8 | `AbonoCreditoRechazado` | `pagos.abono-credito.rechazado` | Crédito bloque 3 | Crédito |
| 9 | `AbonoCreditoPendiente` | `pagos.abono-credito.pendiente` | Crédito bloque 3 | Crédito |

Auditoría consume los nueve por su binding `#` y no se lista fila por fila.

---

## 4. Bindings

| Cola | Dueño | Bindings |
|---|---|---|
| `ordenes.pagos` | Angel (Órdenes) | `pagos.pago.aplicado`, `pagos.pago.rechazado`, `pagos.pago.pendiente`, `pagos.reembolso.aplicado`, `pagos.reembolso.fallido` |
| `consulta-estado.pagos` | Jonathan | `pagos.pago.*` |
| `auditoria.eventos` | Julio | `#` |
| `credito.pagos` | Crédito | `pagos.abono-credito.cobrado`, `pagos.abono-credito.rechazado`, `pagos.abono-credito.pendiente` |

Ni `ordenes.pagos` ni `consulta-estado.pagos` (`pagos.pago.*`) reciben los
eventos 7 a 9.

Pagos también consume `credito.abono.cobro-solicitado` en su cola `pagos.credito`
(`CreditoAbonosConsumidor`, `EventConsumerBase` con un solo `data`). La fuente de
verdad de ese evento es `credito-eventos-v1.md`, sección 2.7.

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
| `aggregateType` | `"Pago"` en los eventos 1 a 5. `"IntentoPago"` en el evento 6. `"PagoAbonoCredito"` en los eventos 7 a 9. |
| `aggregateId` | En los eventos 1 a 3, el `ordenId` de la orden que se está cobrando (formato `ORD-<año>-<correlativo de 6 dígitos>`); Pagos es un agregado por orden. En los eventos 4 y 5 (reembolso), el `devolucionId` (formato `DEV-<año>-<correlativo de 6 dígitos>`): el reembolso se identifica por la devolución que lo origina, no por la orden, porque una orden puede tener más de una devolución. |
| `sequence` | En los eventos 1 a 3, asignado por Pagos, monotónico por `ordenId`, empieza en 1 con el primer evento de resultado de ese pago; un pago que pasa por `PENDIENTE` y luego `APLICADO` emite dos eventos con `sequence` 1 y 2. En los eventos 4 y 5, siempre `1`: cada devolución dispara un único intento de reembolso, sin reintento automático. En el evento 6, el `intentoNumero` del intento fallido; no consume la `sequence` del pago, así que los eventos 1 a 3 conservan la numeración de v1.2. El `aggregateId` del evento 6 es el `ordenId`. En los eventos 7 a 9, `aggregateId` es el `abonoId` y `sequence` la del `Pago` del abono. |

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

   Aparte del evento de resultado, cada llamada que termina en intento `FALLIDO`
   emite un `IntentoPagoFallido` (evento 6), incluidas las que un reintento
   automático supera. Es solo de auditoría y no cuenta como evento de resultado.
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
   (`CobroSolicitado`, `DevolucionRegistrada` o `CobroAbonoCreditoSolicitado`,
   según corresponda). Los puebla el
   consumidor en `EventContext`; `IOutboxWriter` los lee de ahí. El código de
   Pagos nunca los pasa a mano.
7. **Un cobro de abono produce exactamente un evento de abono** (7, 8 o 9):
   aprobado → `AbonoCreditoCobrado`; rechazado por el proveedor, o token
   inválido, vencido, ya usado o de otro cliente → `AbonoCreditoRechazado` con su
   `codigoRespuesta` (`TOKEN_INVALIDO` para el token ajeno, sin invocar a
   PayKlik); timeout, 5xx, red o circuito abierto (tras los reintentos
   automáticos) → `AbonoCreditoPendiente`. Si el `Pago` del abono ya está
   `APLICADO` o `RECHAZADO`, Pagos reemite ese resultado sin llamar a PayKlik.

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

### 7.6 `IntentoPagoFallido`

Routing key `pagos.intento.fallido`. Solo de auditoría. Se emite una vez por cada
intento de cobro que termina `FALLIDO` (timeout, 5xx, red o circuito abierto),
incluidos los intermedios que un reintento automático supera. Se encola en la
misma transacción que el intento. No se emite para intentos `EXITOSO` ni
`RECHAZADO`, ni en reembolsos (`ReembolsoFallido` ya cubre su único intento).

| Campo de `data` | Tipo | Obligatorio | Notas |
|---|---|---|---|
| `ordenId` | string | sí | |
| `clienteId` | string | sí | |
| `canal` | string | sí | Enum `Canal`. |
| `moneda` | string | sí | Enum `Moneda`. |
| `monto` | string | sí | Decimal como cadena. |
| `medioPago` | string | sí | Enum `MedioPago`. |
| `intentoNumero` | integer | sí | Número del intento fallido dentro del pago. |
| `codigoRespuesta` | string | sí | `"TIMEOUT"`, `"PROVEEDOR_5XX"`, `"RED"` o `"CIRCUITO_ABIERTO"`. |
| `motivoFalla` | string | sí | Texto corto legible de la causa. |
| `latenciaMs` | integer | sí | Duración de la llamada; cercana a cero con el circuito abierto. |
| `seReintenta` | boolean | sí | `true` si Pagos reintenta el cobro a continuación; `false` si es el último intento, que además produce `PagoPendiente`. |
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
  "codigoRespuesta": "PROVEEDOR_5XX",
  "motivoFalla": "PayKlik respondio 503",
  "latenciaMs": 42,
  "seReintenta": true,
  "ocurridoEn": "2026-09-22T14:31:04.000Z"
}
```

### 7.7 Eventos de abono a crédito (7, 8 y 9)

`AbonoCreditoCobrado`:

| Campo de `data` | Tipo | Obligatorio | Notas |
|---|---|---|---|
| `abonoId` | string | sí | Id del abono en Crédito. |
| `referencia` | string | sí | `ABO-<año>-<correlativo de 6 dígitos>`. |
| `clienteId` | string | sí | |
| `moneda` | string | sí | |
| `monto` | string | sí | Decimal como cadena. |
| `autorizacionId` | string | sí | Autorización de PayKlik. |
| `ultimosCuatro` | string | no | Para el historial de "Mi crédito". |
| `marca` | string | no | |
| `ocurridoEn` | string | sí | ISO 8601 UTC. |

`AbonoCreditoRechazado`: `abonoId`, `referencia`, `clienteId`, `codigoRespuesta`
(código del proveedor o `TOKEN_*`) y `ocurridoEn`.

`AbonoCreditoPendiente`: `abonoId`, `referencia`, `clienteId`, `motivoFalla`
(`"<TIMEOUT|PROVEEDOR_5XX|RED|CIRCUITO_ABIERTO>: <texto corto>"`) y `ocurridoEn`.
Sin enlace de reintento.

```json
{
  "abonoId": "0f3c2a9d8e7b4c6a9b1d2e3f4a5b6c7d",
  "referencia": "ABO-2026-000001",
  "clienteId": "cliente1@sgo.local",
  "moneda": "GTQ",
  "monto": "200.00",
  "autorizacionId": "PK-000123",
  "ultimosCuatro": "1111",
  "marca": "VISA",
  "ocurridoEn": "2026-10-02T18:00:00.000Z"
}
```

---

## 8. Matriz de consumo

| Consumidor | Dueño | HU | Eventos | Para qué |
|---|---|---|---|---|
| Auditoría | Julio | HU-006 | Los nueve, por binding `#` | Bitácora append-only indexada por `transactionId` (EV-30, EAC-03). Con el evento 6, cada intento de cobro fallido queda en la bitácora (EAC-01). |
| Órdenes | Angel | CU-03, HU-014 | 1, 2, 3, 4, 5 | Avanzar la saga: `PAGO_APLICADO` con el evento 1; `PAGO_PENDIENTE` con el evento 3 (y emitir el enlace al cliente); tratar el rechazo con el evento 2. Con los eventos 4 y 5, cerrar la devolución (`MarcarReembolsoAplicado` / `MarcarReembolsoFallido`) sin cambiar el estado de la orden. |
| Consulta de Estado | Jonathan | HU-010 | `pagos.pago.*` | Enriquecer la vista 360 con el estado del cobro. No materializa estado de Orden. |
| Crédito | Angel | Crédito bloque 3 | 7, 8, 9 | Aplicar el abono a la cuenta por cobrar y a la línea, o dejarlo rechazado o pendiente de reintento. |

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
