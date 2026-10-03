# Contrato del contexto Crédito — v1

**Proyecto:** SGO-Logic — Fase 3
**Curso:** Análisis y Diseño de Sistemas 2, Sección B
**Contexto:** Crédito ("Compra ahora, paga en 30 días")
**Servicio:** `credito-api` (puerto 8099 en el host, base `sgo_credito` en `db-finanzas`, esquema `credito`)
**Estado:** bloques 1 a 4 (perfil, política, oferta, "Mi crédito", evaluación dentro de la saga, cobranza y reversos de dinero)
**Fecha:** 2026-10-03
**Depende de:** `sgo-eventos-envelope-v1.md`

---

## 1. Alcance

Crédito mantiene un **perfil crediticio precalculado** por cliente a partir del
historial comercial que entrega el ERP. La consulta de oferta solo lee ese perfil:
no recalcula ni llama a otro servicio, así que el checkout puede consultarla en
cada carga sin costo.

**Bloque 2.** La saga de la orden tiene una segunda ruta de pago: con
`medioPago = CREDITO`, Órdenes pide la evaluación (`EvaluacionCreditoSolicitada`)
en lugar del cobro. Crédito aprueba (aparta el monto en la línea y emite una
cuenta por cobrar a 30 días) o rechaza, y anula el cargo si la orden se cancela.
Estados, transiciones y timeout del lado de Órdenes: `ordenes-eventos-v1.md`,
secciones 2.3 y 7.13.

**Bloque 3 (cobranza).** El cliente paga sus cuentas por cobrar desde "Mi
crédito", total o por partes (abonos). Crédito registra el abono y pide el cobro
a Pagos por evento (`CobroAbonoCreditoSolicitado`); nunca llama a PayKlik ni a
Pagos por HTTP. Pagos cobra con la tarjeta tokenizada del cliente y responde con
`AbonoCreditoCobrado`, `AbonoCreditoRechazado` o `AbonoCreditoPendiente`. Un
worker envía recordatorios antes del vencimiento y marca las cuentas vencidas,
lo que pone la línea en pausa (`BLOQUEADA`) hasta que el cliente se ponga al día.
No hay intereses ni recargos.

**Bloque 4 (reversos de dinero).** Una devolución de una compra a crédito
(`DevolucionRegistrada` con `medioPago = CREDITO`) la aplica Crédito: baja la
deuda y libera el cupo; si lo devuelto supera lo que faltaba pagar, pide a Pagos
el reembolso a la tarjeta del excedente sobre los abonos ya cobrados
(`ReembolsoAbonoCreditoSolicitado`) y avisa a Órdenes con
`DevolucionCreditoAplicada`. Los abonos de una cuenta anulada (o cobrados con la
cuenta ya anulada) también piden su reembolso. Pagos responde con
`AbonoCreditoReembolsado` o `ReembolsoAbonoCreditoFallido`. No hay saldo a favor:
lo pagado de más siempre vuelve a la tarjeta.

El cliente se identifica siempre por su **email en minúsculas**, el mismo
identificador que usan Órdenes y Pagos.

## 2. Eventos

Ambos viajan en el envelope congelado (`IntegrationEvent<TData>`) por el exchange
`sgo.eventos`, publicados vía Outbox. Montos como cadena con dos decimales,
fechas UTC ISO 8601.

### 2.1 `HistorialClienteErpActualizado` (lo consume Crédito)

| Campo del envelope | Valor |
|---|---|
| `eventType` | `HistorialClienteErpActualizado` |
| `routingKey` | `integracion-erp.cliente.historial-actualizado` |
| `aggregateType` | `ClienteErp` |
| `aggregateId` | email del cliente en minúsculas |
| `source` | `integracion-erp` |

| Campo de `data` | Tipo | Descripción |
|---|---|---|
| `clienteId` | string | Email del cliente en minúsculas. |
| `codigoClienteErp` | string | Código del cliente en el ERP. |
| `clienteDesde` | fecha | Alta del cliente. |
| `comprasUltimos12Meses` | int | Compras de los últimos 12 meses. |
| `montoCompradoUltimos12Meses` | decimal | Monto comprado en los últimos 12 meses. |
| `moneda` | string | `GTQ`. |
| `pagosPuntuales` | int | Pagos hechos a tiempo. |
| `pagosTardios` | int | Pagos hechos con atraso. |
| `maxDiasAtraso` | int | Mayor atraso registrado, en días (informativo, no puntúa en v1). |
| `devoluciones` | int | Compras que terminaron en devolución. |
| `incidentesGraves` | int | Casos graves abiertos (descartan el crédito). |
| `otrosIncidentes` | int | Reclamos menores. |
| `fechaCorte` | fecha | Corte del ERP. Un historial con `fechaCorte` igual o anterior al ya aplicado se ignora. |
| `loteOrigen` | string? | Identificador opcional del lote o ciclo de origen. |

Crédito lo consume en la cola propia `credito.erp` (quorum, con dead letter) con
`EventConsumerBase`: deduplicación por `eventId` en `credito.mensajes_procesados`,
y el historial, perfil, línea, outbox y marca de procesado se guardan en una sola
transacción.

### 2.2 `PerfilCrediticioActualizado` (lo publica Crédito)

| Campo del envelope | Valor |
|---|---|
| `eventType` | `PerfilCrediticioActualizado` |
| `routingKey` | `credito.perfil.actualizado` |
| `aggregateType` | `PerfilCrediticio` |
| `aggregateId` | email del cliente en minúsculas |
| `source` | `credito` |
| `sequence` | Contador propio del perfil; sube solo cuando se emite el evento. |

| Campo de `data` | Tipo | Descripción |
|---|---|---|
| `clienteId` | string | Email del cliente. |
| `puntaje` | int | 0 a 100. Uso interno; nunca se muestra al cliente. |
| `nivel` | string | `EXCELENTE`, `BUENO`, `REGULAR` o `NO_CALIFICA`. |
| `elegible` | bool | Puede comprar a crédito. |
| `codigoMotivo` | string? | Motivo del descarte si no es elegible (sección 4.1); null si es elegible. |
| `limiteAprobado` | decimal | Límite de la línea; 0 si no es elegible. |
| `moneda` | string | `GTQ`. |
| `versionPolitica` | string | `2026.10-v1`. |
| `fechaCorteHistorial` | fecha | `fechaCorte` del historial evaluado. |
| `calculadoEn` | fecha | Momento del cálculo. |

Se emite **solo si cambió** el puntaje, el nivel, la elegibilidad, el motivo o el
límite. Un recálculo con el mismo resultado no emite nada. Hoy no tiene
consumidores además de Auditoría (por `#`).

### 2.3 Eventos consumidos de Órdenes (bloque 2)

Cola propia `credito.ordenes` (quorum, con dead letter), `EventConsumerBase`
tipado a `JsonElement` y despacho por `eventType`; deduplicación por `eventId` en
`credito.mensajes_procesados`.

| Routing key | Evento | Qué hace Crédito |
|---|---|---|
| `ordenes.credito.evaluacion-solicitada` | `EvaluacionCreditoSolicitada` (evento 13 de Órdenes) | Evalúa la compra (sección 4.4). |
| `ordenes.orden.cancelada` | `OrdenCancelada` (evento 9 de Órdenes) | Si `medioPago = CREDITO`, anula el cargo (sección 4.5). Cualquier otro medio se ignora. |

En esta cola los servicios confirman su propia transacción, porque ante un choque
de `Version` de la línea releen y reintentan; la marca de procesado se guarda
después. Una redelivery entre ambos pasos es inofensiva: la decisión es única por
orden y anular una cuenta ya anulada no hace nada.

### 2.4 `CreditoAprobado`

| Campo del envelope | Valor |
|---|---|
| `eventType` | `CreditoAprobado` |
| `routingKey` | `credito.evaluacion.aprobada` |
| `aggregateType` | `EvaluacionCredito` |
| `aggregateId` | `ordenId` |
| `source` | `credito` |

| Campo de `data` | Tipo | Descripción |
|---|---|---|
| `ordenId` | string | |
| `clienteId` | string | Email en minúsculas. |
| `cargoId` | string | Id de la cuenta por cobrar (32 hex). Órdenes guarda `"CRE-" + cargoId` en `pagoId`. |
| `decisionId` | string | Id de la decisión (32 hex). |
| `monto` | decimal | Total de la orden. |
| `moneda` | string | `GTQ`. |
| `fechaVencimiento` | fecha | `emitidaEn` + 30 días. |
| `limiteAprobado` | decimal | Límite de la línea. |
| `disponibleRestante` | decimal | Disponible después de apartar el monto. |
| `decididoEn` | fecha | |

Lo consume Órdenes (cola `ordenes.credito`): `CREDITO_APROBADO` y
`CONFIRMADA_PENDIENTE_FISCAL` en la misma transacción.

### 2.5 `CreditoRechazado`

| Campo del envelope | Valor |
|---|---|
| `eventType` | `CreditoRechazado` |
| `routingKey` | `credito.evaluacion.rechazada` |
| `aggregateType` | `EvaluacionCredito` |
| `aggregateId` | `ordenId` |
| `source` | `credito` |

| Campo de `data` | Tipo | Descripción |
|---|---|---|
| `ordenId` | string | |
| `clienteId` | string | |
| `decisionId` | string | |
| `codigoMotivo` | string | Sección 4.4. Uso interno; no se muestra al cliente. |
| `mensajeCliente` | string | Texto llano de `CatalogoMensajesCredito` (mensaje + recomendación). Órdenes lo usa como detalle visible del rechazo. |
| `decididoEn` | fecha | |

Lo consume Órdenes: `RECHAZADA` como compensación, con `LiberacionReservaSolicitada`
(`CREDITO_RECHAZADO`).

### 2.6 `CargoCreditoAnulado`

| Campo del envelope | Valor |
|---|---|
| `eventType` | `CargoCreditoAnulado` |
| `routingKey` | `credito.cargo.anulado` |
| `aggregateType` | `CuentaPorCobrar` |
| `aggregateId` | `ordenId` |
| `source` | `credito` |

| Campo de `data` | Tipo | Descripción |
|---|---|---|
| `ordenId` | string | |
| `clienteId` | string | |
| `cargoId` | string | |
| `monto` | decimal | Monto devuelto al disponible. |
| `moneda` | string | |
| `motivo` | string | `Orden cancelada (<MotivoCancelacion>)`. |
| `anuladoEn` | fecha | |

Hoy solo lo registra Auditoría.

### 2.7 `CobroAbonoCreditoSolicitado` (lo publica Crédito, lo consume Pagos)

| Campo del envelope | Valor |
|---|---|
| `eventType` | `CobroAbonoCreditoSolicitado` |
| `routingKey` | `credito.abono.cobro-solicitado` |
| `aggregateType` | `AbonoCredito` |
| `aggregateId` | `abonoId` |
| `source` | `credito` |

| Campo de `data` | Tipo | Descripción |
|---|---|---|
| `abonoId` | string | Id del abono (32 hex). Estable entre reintentos. |
| `referencia` | string | `ABO-{yyyy}-{6 dígitos}`; Pagos la usa como referencia del cargo. |
| `clienteId` | string | Email en minúsculas. |
| `moneda` | string | `GTQ`. |
| `monto` | decimal | Mayor que 0 y no mayor que el saldo de la cuenta. |
| `tokenMedioPago` | string | Token de la bóveda de Pagos, del mismo cliente. Nunca PAN ni CVV. |
| `intentoNumero` | int | 1 al crear; sube en 1 con cada reintento del mismo abono. |
| `solicitadoEn` | fecha | |

Cola en Pagos: `pagos.credito`.

### 2.8 Resultados del cobro (los publica Pagos, los consume Crédito)

`AbonoCreditoCobrado` (`pagos.abono-credito.cobrado`), `AbonoCreditoRechazado`
(`pagos.abono-credito.rechazado`) y `AbonoCreditoPendiente`
(`pagos.abono-credito.pendiente`). Detalle de campos en `pagos-eventos-v1.md`,
sección 7.7. Cola en Crédito: `credito.pagos`, idempotente por `eventId` y,
además, por `abonoId`: un resultado sobre un abono que ya está en estado final
no cambia nada.

| Resultado | Efecto en Crédito |
|---|---|
| Cobrado | Abono `APLICADO`; resta el monto del saldo de la cuenta y del `saldoUtilizado` de la línea; emite `AbonoCreditoAplicado`. Con saldo 0, la cuenta pasa a `PAGADA` (`pagadaEn`, `pagoPuntual = pagadaEn <= venceEn`), emite `CuentaCreditoPagada` y recalcula el perfil. Si ya no quedan cuentas `VENCIDA` con saldo, la línea sale de la pausa. |
| Cobrado con la cuenta `ANULADA` | Abono `REEMBOLSO_PENDIENTE`; no toca la línea (la anulación ya liberó el cupo). En la misma transacción pide el reembolso completo del abono (2.14). |
| Rechazado | Abono `RECHAZADO`. Nada más cambia. |
| Pendiente | Abono `PENDIENTE`. El cliente puede reintentar el mismo abono (3.6). |

### 2.9 `AbonoCreditoAplicado`

| Campo del envelope | Valor |
|---|---|
| `eventType` | `AbonoCreditoAplicado` |
| `routingKey` | `credito.abono.aplicado` |
| `aggregateType` | `AbonoCredito` |
| `aggregateId` | `abonoId` |

| Campo de `data` | Tipo | Descripción |
|---|---|---|
| `abonoId` | string | |
| `clienteId` | string | |
| `cuentaId` | string | Id de la cuenta por cobrar (32 hex). |
| `ordenId` | string | |
| `monto` | decimal | |
| `moneda` | string | |
| `saldoPendienteCuenta` | decimal | Saldo de la cuenta después del abono. |
| `disponibleLinea` | decimal | Disponible de la línea después del abono. |
| `aplicadoEn` | fecha | |

### 2.10 `CuentaCreditoPagada`

| Campo del envelope | Valor |
|---|---|
| `eventType` | `CuentaCreditoPagada` |
| `routingKey` | `credito.cuenta.pagada` |
| `aggregateType` | `CuentaPorCobrar` |
| `aggregateId` | `cuentaId` |

`data`: `cuentaId`, `ordenId`, `clienteId`, `pagadaEn` y `puntual` (bool,
`pagadaEn <= venceEn`).

### 2.11 `CuentaCreditoPorVencer`

Mismo envelope que 2.10 con `routingKey` `credito.cuenta.por-vencer`. Se emite una
sola vez por cuenta (`recordatorio_enviado_en`), cuando una cuenta `VIGENTE` con
saldo vence dentro de `Credito:Cobranza:DiasRecordatorio` días.

`data`: `cuentaId`, `ordenId`, `numeroOrden`, `clienteId`, `saldoPendiente`,
`moneda`, `venceEn` y `diasRestantes`.

### 2.12 `CuentaCreditoVencida`

Mismo envelope que 2.10 con `routingKey` `credito.cuenta.vencida`. Se emite al
pasar una cuenta `VIGENTE` con saldo a `VENCIDA` (una sola vez).

`data`: `cuentaId`, `ordenId`, `numeroOrden`, `clienteId`, `saldoPendiente`,
`moneda`, `venceEn` y `vencidaEn`.

### 2.13 Bindings del bloque 3

| Cola | Routing keys |
|---|---|
| `pagos.credito` | `credito.abono.cobro-solicitado` |
| `credito.pagos` | `pagos.abono-credito.cobrado`, `pagos.abono-credito.rechazado`, `pagos.abono-credito.pendiente` |

Ninguna cola con comodín recibe estos eventos salvo la de Auditoría (`#`). En
particular, Órdenes no escucha `pagos.abono-credito.*`: un abono nunca emite
`PagoAplicado`, `PagoRechazado` ni `PagoPendiente`. `AbonoCreditoAplicado` y los
tres eventos de cuenta hoy solo los registra Auditoría.

### 2.14 `ReembolsoAbonoCreditoSolicitado` (lo publica Crédito, lo consume Pagos)

| Campo del envelope | Valor |
|---|---|
| `eventType` | `ReembolsoAbonoCreditoSolicitado` |
| `routingKey` | `credito.abono.reembolso-solicitado` |
| `aggregateType` | `AbonoCredito` |
| `aggregateId` | `abonoId` |
| `source` | `credito` |

| Campo de `data` | Tipo | Descripción |
|---|---|---|
| `reembolsoId` | string | Id de esta solicitud (32 hex). Pagos es idempotente por él. |
| `abonoId` | string | Abono cuyo cobro se devuelve. |
| `referencia` | string | `ABO-...` del abono; Pagos encuentra el cobro por ella. |
| `clienteId` | string | Email en minúsculas. |
| `moneda` | string | `GTQ`. |
| `monto` | decimal | Lo que se devuelve; nunca más que lo cobrado en el abono menos lo ya pedido. |
| `motivo` | string | `CANCELACION` (cuenta anulada) o `DEVOLUCION` (excedente de una devolución). |
| `solicitadoEn` | fecha | |

Se emite en la misma transacción que el cambio que lo origina:

- Al pasar un abono a `REEMBOLSO_PENDIENTE` (cuenta anulada con abonos aplicados, o
  cobro que llegó con la cuenta ya anulada): por todo el abono.
- Al aplicar una devolución que supera el saldo pendiente: por el excedente,
  repartido sobre los abonos `APLICADO` de la cuenta, del más reciente al más
  antiguo.
- `ReembolsosAbonoCreditoWorker` (cada `Credito:Cobranza:IntervaloReembolsosSegundos`)
  lo emite para los abonos en `REEMBOLSO_PENDIENTE` que todavía no tienen solicitud
  (`reembolso_solicitado_en` nulo), por ejemplo los que quedaron así antes de este
  bloque.

Cola en Pagos: `pagos.credito`.

### 2.15 Resultados del reembolso (los publica Pagos, los consume Crédito)

`AbonoCreditoReembolsado` (`pagos.abono-credito.reembolsado`) y
`ReembolsoAbonoCreditoFallido` (`pagos.abono-credito.reembolso-fallido`). Campos en
`pagos-eventos-v1.md`, sección 7.9. Cola en Crédito: `credito.pagos`.

| Resultado | Efecto en Crédito |
|---|---|
| Reembolsado | Suma `monto` a `montoReembolsado` del abono; si ya se devolvió todo lo cobrado, el abono pasa a `REEMBOLSADO`. Registra un movimiento `REEMBOLSO_ABONO` con `referencia = reembolsoId`: una confirmación repetida no suma dos veces. |
| Fallido | El abono queda marcado para atención manual (`reembolso_fallido_en`, `motivo_falla_reembolso`) y sube `credito_reembolsos_fallidos_total`. No se vuelve a pedir solo. |

### 2.16 `DevolucionCreditoAplicada` (lo publica Crédito, lo consume Órdenes)

| Campo del envelope | Valor |
|---|---|
| `eventType` | `DevolucionCreditoAplicada` |
| `routingKey` | `credito.devolucion.aplicada` |
| `aggregateType` | `CuentaPorCobrar` |
| `aggregateId` | `cuentaId` |
| `source` | `credito` |

| Campo de `data` | Tipo | Descripción |
|---|---|---|
| `devolucionId` | string | `DEV-...` de Órdenes. |
| `ordenOriginalId` | string | |
| `clienteId` | string | |
| `moneda` | string | |
| `montoDescontadoDeuda` | decimal | Lo que bajó la deuda (y volvió al disponible). |
| `montoAReembolsar` | decimal | Lo ya pagado que se pidió reembolsar a la tarjeta (0 si no hubo). |
| `aplicadaEn` | fecha | |

Órdenes la consume en `ordenes.credito` y cierra la devolución igual que con
`ReembolsoAplicado` (sección 7.11 de `ordenes-eventos-v1.md`). No espera el
reembolso a tarjeta del excedente.

### 2.17 Bindings del bloque 4

| Cola | Routing keys nuevas |
|---|---|
| `credito.ordenes` | `ordenes.devolucion.registrada` (solo actúa con `medioPago = CREDITO`) |
| `credito.pagos` | `pagos.abono-credito.reembolsado`, `pagos.abono-credito.reembolso-fallido` |
| `pagos.credito` | `credito.abono.reembolso-solicitado` |
| `ordenes.credito` | `credito.devolucion.aplicada` |

Ninguna cola con comodín recibe estos eventos salvo la de Auditoría (`#`).

## 3. Endpoints

### 3.1 `GET /api/credito/elegibilidad?monto={decimal opcional}`

Enrutado por el gateway con `RequireAuthenticatedUser`. Requiere JWT; el cliente
sale **siempre** del claim `email`, nunca de la query ni del cuerpo (un
`clienteId` en la query se ignora). `monto` menor o igual a 0 responde 400. Sin
claim de email, 401.

Respuesta 200 (`OfertaCreditoDto`, sin puntaje):

```json
{
  "estado": "DISPONIBLE",
  "ofrecer": true,
  "codigoMotivo": null,
  "titulo": "Tienes crédito disponible",
  "mensaje": "Tienes Q3,500.00 disponibles de un límite de Q3,500.00. ...",
  "recomendacion": "Elige crédito al pagar y cancela el total desde Mi crédito dentro de los 30 días.",
  "limiteAprobado": "3500.00",
  "disponible": "3500.00",
  "montoSolicitado": "1200.00",
  "moneda": "GTQ",
  "plazoDias": 30,
  "fechaPagoEstimada": "2026-11-01T00:00:00.000Z",
  "calculadoEn": "2026-10-02T17:10:00.000Z"
}
```

| `estado` | Cuándo |
|---|---|
| `DISPONIBLE` | Elegible, línea `ACTIVA` y el disponible cubre el monto (o no se envió monto). Único estado con `ofrecer = true` y `fechaPagoEstimada` (hoy + 30 días). |
| `CUPO_INSUFICIENTE` | Elegible y línea activa, pero el disponible es menor que el monto. |
| `NO_ELEGIBLE` | Hay perfil y no es elegible; `codigoMotivo` dice por qué. |
| `SIN_HISTORIAL` | No hay perfil del cliente. |
| `LINEA_BLOQUEADA` | La línea está en pausa por una compra vencida sin pagar (bloque 3). Se revisa antes que la elegibilidad del perfil; `ofrecer = false`. |

`titulo`, `mensaje` y `recomendacion` salen de `CatalogoMensajesCredito`: lenguaje
llano, sin códigos internos, sin hablar de puntaje.

### 3.2 `GET /api/credito/mi-credito`

Mismo enrutamiento y misma regla de identidad. Respuesta 200 (`MiCreditoDto`, sin
puntaje): `estado`, `nivel`, `etiquetaNivel`, `titulo`, `mensaje`,
`recomendacion`, `limiteAprobado`, `saldoUtilizado`, `disponible`, `moneda`,
`plazoDias`, `actualizadoEn`, `factores[]` con `nombre`, `valoracion` y
`descripcion`, y (bloque 2) `compras[]` con `numeroOrden`, `ordenId`, `monto`,
`saldoPendiente`, `emitidaEn`, `venceEn`, `diasRestantes` (solo vigentes),
`estado` (`VIGENTE`, `PAGADA`, `VENCIDA`, `ANULADA`) y `etiquetaEstado` ("Por
pagar", "Pagada", "Vencida", "Anulada"). Primero las vigentes, de la más próxima
a vencer; después el resto, de la más reciente. `saldoUtilizado` y `disponible`
son reales: suben al aprobar y bajan al anular.

**Bloque 3.** Se agregan `totalPorPagar` (suma de saldos `VIGENTE` y `VENCIDA`),
`proximoVencimiento` (el `venceEn` más próximo con saldo, o null) y `avisos[]`
(`tipo`, `titulo`, `mensaje`, `recomendacion`, `cuentaId`), primero los más
urgentes: `LINEA_EN_PAUSA`, `VENCIDA` (una por compra vencida) y `POR_VENCER`
(compras que vencen dentro de `DiasRecordatorio`). `tipo` solo sirve para dar
estilo; la Web nunca lo muestra. Cada compra agrega `cuentaId`, `puedePagar`
(cuenta `VIGENTE` o `VENCIDA` con saldo y sin abono en curso), `abonoEnCursoId`,
`abonoEnCursoPuedeReintentar` y `abonos[]` (`abonoId`, `referencia`, `fecha`,
`monto`, `tarjeta` como "Visa terminada en 4242", `estado`, `etiquetaEstado`).
`etiquetaEstado` de la compra: "Por pagar", "Vence pronto", "Vencida", "Pagada"
o "Anulada". Primero las pagables, de la más próxima a vencer.

**Bloque 4.** Cada compra agrega `movimientos[]` con sus devoluciones (`tipo` =
`DEVOLUCION`, `referencia` = `DEV-...`, `fecha`, `monto`, `montoDescontadoDeuda`,
`montoAReembolsar` y `descripcion` en lenguaje llano, por ejemplo "Devolviste
productos por Q100.00. Tu deuda bajó Q100.00."). Cada elemento de `abonos[]`
agrega `estadoReembolso` (null si de ese pago no se devuelve nada; si no, "Reembolso
a tu tarjeta en proceso", "Reembolsado a tu tarjeta" o "No pudimos completar el
reembolso; contáctanos"), `montoReembolsado` y `montoReembolsoSolicitado`. El
`etiquetaEstado` del abono suma "Reembolsado" y el de la compra "Devuelta" (cuenta
anulada por devolución total sin pagos). `totalPorPagar`, `saldoUtilizado` y
`disponible` ya reflejan las devoluciones.

Los cuatro factores para el cliente agrupan los de la política: "Pagos a tiempo"
(puntualidad), "Monto de tus compras" (monto), "Tiempo y frecuencia como cliente"
(antigüedad + frecuencia) y "Devoluciones y reclamos" (devoluciones + otros
incidentes). `valoracion` según puntos obtenidos sobre el máximo del factor: 80% o
más `FAVORABLE`, 50% o más `ACEPTABLE`, menos `A_MEJORAR`.

### 3.3 `POST /interno/credito/historiales`

Solo existe con `Credito:EndpointsInternos=true` (falso por defecto, verdadero en
`docker-compose.yml`). **No se enruta por el gateway**: se llama directo a
`http://localhost:8099`. Sirve para demo y pruebas mientras no exista el worker
del ERP.

Cuerpo: igual al `data` de `HistorialClienteErpActualizado`. Aplica el historial
con la misma lógica que el consumidor y responde 200 con `clienteId`, `aplicado`
(false si la fecha de corte no es posterior a la vigente), `huboCambios`,
`puntaje`, `nivel`, `elegible`, `codigoMotivo`, `limiteAprobado`, `moneda`,
`versionPolitica`, `fechaCorteHistorial` y `calculadoEn`. 400 si el cuerpo falta,
no trae cliente o trae valores negativos.

### 3.4 `POST /interno/credito/demo/reiniciar`

Mismas condiciones que 3.3 (`Credito:EndpointsInternos=true`, no enrutado por el
gateway). Borra todas las cuentas por cobrar y decisiones y deja el
`saldoUtilizado` de todas las líneas en 0, sin tocar perfiles, historiales ni
límites. Responde 200 con `cuentasBorradas`, `decisionesBorradas` y
`lineasReiniciadas`. Para repetir pruebas y demos.

### 3.5 `POST /api/credito/abonos`

Enrutado por el gateway con `RequireAuthenticatedUser`; el cliente sale del claim
`email`. Cuerpo: `{ "cuentaId": "...", "monto": "200.00", "tokenMedioPago": "tok_..." }`.

Respuesta 202 con `AbonoDto`: `id`, `referencia`, `cuentaId`, `estado`
(`SOLICITADO`, `PENDIENTE`, `APLICADO`, `RECHAZADO`, `REEMBOLSO_PENDIENTE`),
`enCurso`, `titulo`, `mensaje`, `recomendacion`, `monto`, `moneda`,
`saldoPendienteCuenta` y `puedeReintentar`. Los textos salen de
`CatalogoMensajesCredito`; la Web muestra esos y nunca `estado`.

Errores (`ProblemDetails` con `codigo` y `detail` legible para el cliente):

| Status | `codigo` | Cuándo |
|---|---|---|
| 404 | `cuenta_no_encontrada` | No existe o es de otro cliente (no se distingue). |
| 409 | `abono_en_curso` | La cuenta ya tiene un abono `SOLICITADO` o `PENDIENTE`. Lo garantiza también el índice único parcial `ux_abono_credito_cuenta_en_curso`. |
| 409 | `cuenta_no_pagable` | La cuenta está `PAGADA` o `ANULADA`. |
| 422 | `monto_invalido` | Monto menor o igual a 0, mayor que el saldo, con más de dos decimales o menor que `MontoMinimoAbono` (10.00). Si el saldo es menor que el mínimo, solo se acepta el saldo completo. |
| 422 | `token_requerido` | Falta `tokenMedioPago`. |

El abono, la referencia y `CobroAbonoCreditoSolicitado` se guardan en una sola
transacción.

### 3.6 `GET /api/credito/abonos/{id}` y `POST /api/credito/abonos/{id}/reintentar`

`GET` devuelve el `AbonoDto` (404 `abono_no_encontrado` si no existe o es de otro
cliente). La Web lo consulta cada 2 s hasta que sale de `SOLICITADO`.

`POST .../reintentar` con `{ "tokenMedioPago": "tok_..." }` (un token nuevo: los
de tarjeta nueva son de un solo uso). Solo para abonos `PENDIENTE` (si no, 409
`abono_no_reintentable`): vuelve a `SOLICITADO` con el **mismo** `abonoId`,
`intentoNumero + 1`, y emite otra vez `CobroAbonoCreditoSolicitado`. Pagos reutiliza
el mismo pago y la misma clave de idempotencia, así que PayKlik no cobra dos veces.

### 3.7 `POST /interno/credito/demo/cuentas/{ordenId}/vencimiento`

Mismas condiciones que 3.3. Cuerpo `{ "diasDesdeHoy": -1 }`: fija `venceEn` de la
cuenta de la orden a hoy + `diasDesdeHoy` días (negativo = ya vencida) y limpia
el recordatorio enviado. 404 si la orden no tiene cuenta.

### 3.8 `POST /interno/credito/demo/procesar-vencimientos`

Mismas condiciones que 3.3. Ejecuta una pasada del worker de vencimientos sin
esperar el intervalo y responde 200 con `recordadas` y `vencidas`.

`POST /interno/credito/demo/reiniciar` (3.4) además borra los abonos, desbloquea
las líneas y recalcula los perfiles; responde también `abonosBorrados`. Desde el
bloque 4 borra también los movimientos de cuenta y responde `movimientosBorrados`.

## 4. Política `2026.10-v2`

**Cambio respecto a `2026.10-v1` (bloque 3).** La política considera el
comportamiento propio del cliente con su crédito:

- Las cuentas `PAGADA` a tiempo y tarde se suman a los pagos puntuales y tardíos
  del ERP en el factor de puntualidad.
- Una cuenta `VENCIDA` con saldo descarta con `DEUDA_VENCIDA`, inmediatamente
  después de `SIN_HISTORIAL`.
- Sin comportamiento propio (cero pagadas y cero vencidas) el resultado es
  idéntico al de `v1`.

Todos los números son configurables en `Credito:Politica` (`appsettings.json`);
estos son los valores por defecto. Cambiarlos es una política nueva y debe ir con
otra `VersionPolitica`.

### 4.1 Descartes directos

Se evalúan en este orden; el primero que aplica define `codigoMotivo`:

1. `SIN_HISTORIAL`: no hay historial del cliente.
2. `DEUDA_VENCIDA`: el cliente tiene una cuenta `VENCIDA` con saldo (v2).
3. `INCIDENTE_GRAVE`: `incidentesGraves > 0`.
4. `ANTIGUEDAD_INSUFICIENTE`: menos de 90 días desde `clienteDesde`.
5. `DEVOLUCIONES_EXCESIVAS`: `devoluciones / comprasUltimos12Meses > 0.30`.
6. `PUNTAJE_INSUFICIENTE`: puntaje menor que 60.

Un cliente descartado queda `NO_CALIFICA`, con límite 0 y línea `INACTIVA`,
aunque su puntaje sea alto.

### 4.2 Puntaje (0 a 100)

Se calcula siempre, aunque haya descarte.

| Factor | Máximo | Cálculo |
|---|---|---|
| Puntualidad | 40 | `40 * puntuales / (puntuales + tardíos)`; sin pagos registrados, 20. En v2, puntuales y tardíos incluyen las cuentas propias pagadas a tiempo y tarde. |
| Monto | 30 | `30 * min(monto12m / 12000, 1)`. |
| Antigüedad | 8 | `8 * min(díasComoCliente / 730, 1)`. |
| Frecuencia | 7 | `7 * min(compras12m / 12, 1)`. |
| Devoluciones | 10 | `10 * (1 - min(tasa / 0.30, 1))`, con tasa = devoluciones / compras (0 sin compras). |
| Incidentes | 5 | 5 con 0 `otrosIncidentes`, 2.5 con 1, 0 con 2 o más. |

Puntaje = suma redondeada a entero con `MidpointRounding.AwayFromZero`.

### 4.3 Nivel y límite

| Puntaje | Nivel | Límite (GTQ) |
|---|---|---|
| 90 o más | `EXCELENTE` | 7,500 |
| 75 a 89 | `BUENO` | 3,500 |
| 60 a 74 | `REGULAR` | 1,500 |
| menos de 60 | `NO_CALIFICA` | 0 |

Plazo: 30 días. La línea conserva `saldoUtilizado` al recalcular;
`disponible = limiteAprobado - saldoUtilizado`, nunca menor que 0.

### 4.4 Evaluación de una compra (bloque 2)

Idempotente por `ordenId`: si ya hay decisión para la orden no se hace nada (salvo
el caso `ANULADA_SIN_CARGO` de 4.5). Se aprueba solo si el perfil es elegible, la
línea está `ACTIVA` y el disponible cubre el monto. Si no, el primer impedimento
define `codigoMotivo`:

| Código | Cuándo |
|---|---|
| `SIN_HISTORIAL` | El cliente no tiene perfil. |
| Motivo del perfil | `DEUDA_VENCIDA`, `INCIDENTE_GRAVE`, `ANTIGUEDAD_INSUFICIENTE`, `DEVOLUCIONES_EXCESIVAS` o `PUNTAJE_INSUFICIENTE` (sección 4.1). |
| `LINEA_BLOQUEADA` | La línea está `BLOQUEADA` (cuenta vencida sin pagar). Se revisa antes que el motivo del perfil. |
| `CUPO_INSUFICIENTE` | Sin línea activa, o el disponible no cubre el monto. |
| `ORDEN_CANCELADA` | La orden se canceló antes de que llegara la evaluación. |

Aprobar es una sola transacción: suma el monto a `saldoUtilizado`, crea la cuenta
por cobrar (`VIGENTE`, vence a los 30 días), guarda la decisión `APROBADA` y
escribe `CreditoAprobado` en el outbox. Rechazar guarda la decisión `RECHAZADA` y
escribe `CreditoRechazado`.

**Concurrencia.** Dos compras simultáneas del mismo cliente compiten por la
`Version` de la línea. La que pierde recibe un choque de concurrencia, descarta lo
pendiente, relee el saldo y se reevalúa (hasta 5 intentos). Entre las dos nunca
superan el límite.

### 4.5 Anulación del cargo (bloque 2)

Al consumir `OrdenCancelada` con `medioPago = CREDITO`:

- Si hay cuenta `VIGENTE` o `VENCIDA`: pasa a `ANULADA`, el saldo pendiente vuelve
  al disponible de la línea y se emite `CargoCreditoAnulado` (`monto` = saldo
  liberado). Anular dos veces no libera dos veces. Los abonos `APLICADO` de esa
  cuenta pasan a `REEMBOLSO_PENDIENTE` (lo ya pagado no se libera otra vez) y, en
  la misma transacción, piden su reembolso a la tarjeta con
  `ReembolsoAbonoCreditoSolicitado` (motivo `CANCELACION`). Se reevalúa la pausa
  de la línea.
- Si no hay decisión todavía (la orden se canceló o rechazó antes de que Crédito
  la evaluara): se guarda una decisión `ANULADA_SIN_CARGO`. Una evaluación que
  llegue después se rechaza con `ORDEN_CANCELADA` sin apartar cupo, y la decisión
  pasa a `RECHAZADA`.
- Si la decisión fue un rechazo: no hay nada que devolver.

Así se cubre el timeout de la saga: si Crédito estaba caído, al volver procesa en
orden la evaluación (aprueba y aparta) y luego la cancelación del rechazo por
timeout (anula y devuelve), y el saldo termina igual que antes.

### 4.6 Tablas del bloque 2

| Tabla | Clave | Contenido |
|---|---|---|
| `credito.cuenta_por_cobrar` | `id`; `orden_id` único | `numero_orden`, `cliente_id`, `monto_original`, `saldo_pendiente`, `moneda`, `emitida_en`, `vence_en`, `estado` (`VIGENTE`, `PAGADA`, `VENCIDA`, `ANULADA`), `anulada_en`, `motivo_anulacion`, `version`. |
| `credito.decision_credito` | `id`; `orden_id` único | `cliente_id`, `monto`, `resultado` (`APROBADA`, `RECHAZADA`, `ANULADA_SIN_CARGO`), `codigo_motivo`, `puntaje`, `nivel`, `limite_aprobado`, `disponible_antes`, `version_politica`, `decidido_en`. Sustento auditable de cada solicitud. |

Migración `CuentasPorCobrarYDecisiones`.

### 4.7 Vencimientos y recordatorios (bloque 3)

`VencimientosCreditoWorker` corre cada `Credito:Cobranza:IntervaloSegundos` (60) y
delega en `ServicioVencimientos`:

1. **Recordatorio.** Cuentas `VIGENTE` con saldo, sin recordatorio y que vencen
   dentro de `DiasRecordatorio` (3) días: guarda `recordatorio_enviado_en` y emite
   `CuentaCreditoPorVencer`. Una sola vez por cuenta.
2. **Vencimiento.** Cuentas `VIGENTE` con saldo y `venceEn` ya pasado: pasan a
   `VENCIDA`, la línea del cliente pasa a `BLOQUEADA`, se recalcula el perfil
   (`DEUDA_VENCIDA`) y se emite `CuentaCreditoVencida`. Una sola vez por cuenta.

Sin intereses ni recargos. Pagar todas las cuentas vencidas devuelve la línea a
`ACTIVA` (o `INACTIVA` si el límite es 0) y recalcula el perfil; ese pago cuenta
como tardío.

### 4.8 Estados

| Entidad | Estados |
|---|---|
| Cuenta por cobrar | `VIGENTE` -> `PAGADA` (saldo 0), `VIGENTE` -> `VENCIDA` -> `PAGADA`, `VIGENTE`/`VENCIDA` -> `ANULADA`. Bloque 4: una devolución que deja el saldo en 0 la cierra en `ANULADA` (motivo `DEVOLUCION`, devolución total sin pagos) o en `PAGADA` sin marca de puntualidad. |
| Abono | `SOLICITADO` -> `APLICADO` / `RECHAZADO` / `PENDIENTE`; `PENDIENTE` -> `SOLICITADO` (reintento); `SOLICITADO`/`PENDIENTE`/`APLICADO` -> `REEMBOLSO_PENDIENTE` (cuenta anulada); `APLICADO`/`REEMBOLSO_PENDIENTE` -> `REEMBOLSADO` (se devolvió todo lo cobrado). |
| Línea | `ACTIVA` <-> `BLOQUEADA` mientras haya cuentas vencidas con saldo. |

### 4.9 Tablas del bloque 3

| Tabla | Clave | Contenido |
|---|---|---|
| `credito.abono_credito` | `id`; `referencia` única; índice único parcial `ux_abono_credito_cuenta_en_curso` sobre `cuenta_id` con `estado IN ('SOLICITADO','PENDIENTE')` | `cuenta_id`, `cliente_id`, `monto`, `moneda`, `estado`, `intento_numero`, `autorizacion_id`, `ultimos_cuatro`, `marca`, `codigo_respuesta`, fechas y `version`. |
| `credito.cuenta_por_cobrar` | | Nuevas columnas `pagada_en`, `pago_puntual`, `recordatorio_enviado_en`. |
| Secuencia `credito.abono_correlativo` | | Correlativo de `ABO-{yyyy}-{6 dígitos}`. |

Migración `Cobranza`.

### 4.10 Devolución de una compra a crédito (bloque 4)

Al consumir `DevolucionRegistrada` con `medioPago = CREDITO` (cualquier otro medio
se ignora: lo reembolsa Pagos). Idempotente por `devolucionId` (movimiento
`DEVOLUCION` único). Todo en una transacción, con reintento ante choque de versión:

1. Descuenta del saldo pendiente el mínimo entre el monto devuelto y el saldo, y
   lo libera en la línea.
2. Si el monto devuelto supera el saldo, el excedente ya lo pagó el cliente: pide su
   reembolso con `ReembolsoAbonoCreditoSolicitado` (motivo `DEVOLUCION`) sobre los
   abonos `APLICADO` de la cuenta, del más reciente al más antiguo, hasta cubrirlo.
3. Si el saldo queda en 0: devolución total y sin pagos deja la cuenta `ANULADA`
   con motivo `DEVOLUCION`; en otro caso `PAGADA` con `pagoPuntual` nulo. Una cuenta
   cerrada por devolución no cuenta como pago puntual ni tardío.
4. Si la cuenta estaba `VENCIDA` y ya no quedan vencidas, la línea sale de la pausa
   y se recalcula el perfil.
5. Registra el movimiento (`monto`, `montoDescontadoDeuda`, `montoAReembolsar`) y
   emite `DevolucionCreditoAplicada`.

Sin cuenta por cobrar para la orden, se confirma el mensaje sin cambios.

### 4.11 Tablas del bloque 4

| Tabla | Clave | Contenido |
|---|---|---|
| `credito.movimiento_cuenta` | `id`; único (`tipo`, `referencia`) | `cuenta_id`, `cliente_id`, `tipo` (`DEVOLUCION`, `REEMBOLSO_ABONO`), `monto`, `referencia` (`devolucionId` o `reembolsoId`), `fecha`, `monto_descontado_deuda`, `monto_a_reembolsar`, `abono_id`. |
| `credito.abono_credito` | | Nuevas columnas `monto_reembolsado`, `monto_reembolso_solicitado`, `reembolso_solicitado_en`, `reembolso_fallido_en`, `motivo_falla_reembolso`; estado nuevo `REEMBOLSADO`. |

Migración `DevolucionesYReembolsos`.

## 5. Contrato para Integración ERP y mock ERP (pendiente, lo implementa su dueño)

Crédito ya consume `HistorialClienteErpActualizado` y, mientras tanto, se alimenta
con la siembra de demo (`Credito:SembrarDemo`) y con `POST /interno/credito/historiales`.
Lo siguiente queda escrito para que lo implemente el dueño de `Mocks/mock-erp` y de
`SgoLogic.IntegracionErp.Worker`.

### 5.1 Mock ERP

**`GET /erp/clientes/historial?modificadosDesde={ISO-8601 opcional}`**

Responde 200 con los clientes cuyo historial cambió desde `modificadosDesde` (o
todos si no viene):

```json
{
  "fechaCorte": "2026-10-02T06:00:00.000Z",
  "total": 6,
  "clientes": [
    {
      "codigoCliente": "ERP-CLI-0001",
      "email": "cliente1@sgo.local",
      "clienteDesde": "2023-09-28T00:00:00.000Z",
      "comprasUltimos12Meses": 18,
      "montoCompradoUltimos12Meses": 14500.00,
      "moneda": "GTQ",
      "pagosPuntuales": 18,
      "pagosTardios": 0,
      "maxDiasAtraso": 0,
      "devoluciones": 1,
      "incidentesGraves": 0,
      "otrosIncidentes": 0,
      "actualizadoEn": "2026-10-02T06:00:00.000Z"
    }
  ]
}
```

Respeta el modo caída igual que `POST /erp/lotes` (`POST /erp/admin/caida`):
`timeout` no responde a tiempo (504 tardío), `error500` responde 500 y
`no_disponible` responde 503.

Datos iniciales: los mismos seis clientes de la siembra de demo de Crédito
(`clienteDesde` = hoy menos los días indicados):

| email | días como cliente | compras 12m | monto 12m | puntuales | tardíos | máx. atraso | devoluciones | graves | otros |
|---|---|---|---|---|---|---|---|---|---|
| cliente1@sgo.local | 1100 | 18 | 14500 | 18 | 0 | 0 | 1 | 0 | 0 |
| cliente2@sgo.local | 500 | 9 | 8400 | 8 | 1 | 6 | 1 | 0 | 0 |
| cliente3@sgo.local | 300 | 5 | 6000 | 4 | 1 | 12 | 1 | 0 | 0 |
| cliente4@sgo.local | 800 | 10 | 9000 | 3 | 7 | 45 | 2 | 0 | 1 |
| cliente5@sgo.local | 40 | 2 | 900 | 2 | 0 | 0 | 0 | 0 | 0 |
| cliente6@sgo.local | 900 | 14 | 11000 | 14 | 0 | 0 | 1 | 1 | 0 |

`demo@sgo.local` no está a propósito: es el caso `SIN_HISTORIAL`.

**`PUT /erp/admin/clientes/{email}/historial`**: reemplaza el historial de un
cliente para pruebas (mismo cuerpo que un elemento de `clientes`, sin `email`) y
actualiza su `actualizadoEn`.

**`POST /erp/admin/reset`**: además de lo que ya reinicia, restaura los seis
clientes iniciales.

### 5.2 Integración ERP

- Worker nuevo `ErpHistorialClientesWorker`: corre al arrancar y luego cada
  `Erp:IntervaloHistorialMinutos` (por defecto 30).
- Método nuevo en `IClienteErp` para `GET /erp/clientes/historial`, con la misma
  política de reintentos que `EnviarLoteAsync` (`Erp:MaxReintentos`, backoff
  exponencial de 250 ms hasta 4 s, reintenta 500, 503, 504, timeout y error de red).
- Lock propio de PostgreSQL `sgo-logic.integracion-erp.historial-clientes`. No
  reutilizar el de lotes (`sgo-logic.integracion-erp.batch`): son ciclos
  independientes.
- Tabla propia con la última versión publicada por cliente (por ejemplo, email,
  `actualizadoEn` y un hash del contenido) para publicar solo los clientes que
  cambiaron.
- Por cada cliente que cambió, un `HistorialClienteErpActualizado` vía outbox, con
  `aggregateId` = email en minúsculas y `data.fechaCorte` = `fechaCorte` de la
  respuesta; `data.clienteId` = email en minúsculas y
  `data.codigoClienteErp` = `codigoCliente`.
- Si el ERP está caído (reintentos agotados) no publica nada y lo intenta en el
  siguiente ciclo. Crédito sigue respondiendo con el último perfil calculado.
- Recomendación: incluir `MEDIO_PAGO` en cada registro del lote de ventas que se
  envía al ERP. `OrdenConfirmada` ya lo trae (`medioPago`), así que no hace falta
  otra consulta. Con él el ERP distingue las ventas a crédito (cuya cobranza sigue
  abierta y cuyas devoluciones bajan una deuda en vez de reembolsar) y puede
  calcular el historial de pagos que luego devuelve a Crédito.

## 6. Configuración

| Clave | Por defecto | En `docker-compose.yml` |
|---|---|---|
| `Credito:SembrarDemo` | `false` | `true` |
| `Credito:EndpointsInternos` | `false` | `true` |
| `Credito:Politica:*` | sección 4 (`VersionPolitica` = `2026.10-v2`) | sin cambios |
| `Credito:Cobranza:MontoMinimoAbono` | `10` | sin cambios |
| `Credito:Cobranza:IntervaloSegundos` | `60` | sin cambios |
| `Credito:Cobranza:DiasRecordatorio` | `3` | sin cambios |
| `Credito:Cobranza:IntervaloReembolsosSegundos` | `60` | sin cambios |
| `Auth:JwtSecret` | vacío (todo 401) | `${JWT_SECRET}` |
| `Outbox:Source` | `credito` | `credito` |

`Credito:EndpointsInternos` habilita los endpoints internos (3.3, 3.4, 3.7 y 3.8). En
Órdenes, `Credito:TimeoutEvaluacionSegundos` (30) fija el timeout de la
evaluación y `Auth:JwtSecret` la validación del JWT para órdenes a crédito.

Métricas propias en `/metrics`: `credito_consultas_elegibilidad_total{estado}`,
`credito_perfiles_recalculados_total`, `credito_evaluaciones_total{resultado}` y
`credito_cargos_anulados_total`, `credito_abonos_total{estado}`,
`credito_cuentas_vencidas_total` y `credito_recordatorios_total`; desde el bloque 4,
`credito_devoluciones_total`, `credito_reembolsos_solicitados_total{motivo}`,
`credito_reembolsos_aplicados_total` y `credito_reembolsos_fallidos_total`, además de las transversales de outbox y consumo.
Órdenes expone `ordenes_credito_timeouts_total`.
