# Contrato de eventos del contexto Órdenes — v1

**Proyecto:** SGO-Logic — Fase 2
**Curso:** Análisis y Diseño de Sistemas 2, Sección B
**Contexto emisor:** Órdenes
**Autor:** 202300410 — Angel David Martínez García
**Estado:** propuesto para congelamiento
**Fecha de publicación:** 2026-09-07
**Fecha límite de objeciones:** 2026-09-07, fin del día

---

## 1. Alcance y reglas del contrato

Este documento define los eventos que **publica** el contexto Órdenes. Es la fuente única de verdad para todo consumidor. Un contexto no infiere campos, no asume campos adicionales y no consume eventos de Órdenes que no estén aquí.

Trazabilidad al DDA: ADR-001 (microservicios por contexto delimitado), ADR-002 (saga orquestada centralizada en Órdenes), ADR-003 (Outbox con polling publisher), ADR-004 (CQRS ligero con Redis), ADR-010 (tokenización, sin procesador propio), ADR-011 (certificación fiscal asíncrona).

### Reglas obligatorias

1. **Órdenes es el único emisor de estos eventos.** Ningún otro contexto publica con routing key `ordenes.*`.
2. **Toda publicación pasa por Outbox** (ADR-003). Está prohibido publicar directo al broker desde el código de aplicación. El evento se persiste en la tabla outbox del contexto dentro de la misma transacción que el cambio de datos.
3. **Consumo idempotente obligatorio.** El broker garantiza entrega *al menos una vez*. Todo consumidor deduplica por `eventId`. Un `eventId` procesado dos veces no puede producir dos efectos.
4. **No hay garantía de orden global.** Solo se garantiza orden por `aggregateId` en condiciones normales. El consumidor que necesite ordenar usa `occurredAt` y, en caso de empate, el par (`estadoAnterior`, `estadoNuevo`).
5. **Prohibido en todo payload:** número completo de tarjeta (PAN), código de seguridad (CVV/CVC) y fecha de vencimiento completa. Restricción dura de EV-25 y ADR-010. Se transmite únicamente token, últimos cuatro dígitos y marca.
6. **Montos como cadena decimal**, nunca número de punto flotante. Ejemplo: `"1250.00"`. Moneda en ISO 4217 (`GTQ`, `USD`, `CRC`) por la operación en tres países (EV-01).
7. **Fechas en ISO 8601 UTC** con sufijo `Z` y milisegundos. Ejemplo: `"2026-09-07T14:32:10.123Z"`.
8. **Identificadores como cadena**, nunca entero. Formato de orden: `ORD-<año>-<correlativo de 6 dígitos>`.

---

## 2. Topología de mensajería

Modo pub/sub del bus (ADR-001, sección 11 del DDA).

| Elemento | Valor |
|---|---|
| Exchange | `sgo.eventos` |
| Tipo | `topic`, `durable: true`, `auto_delete: false` |
| Dead letter exchange | `sgo.eventos.dlx` (`fanout`, durable) |
| Cola de descarte | `sgo.eventos.dlq` (durable, sin TTL) |
| Persistencia de mensaje | `delivery_mode: 2` |
| `content_type` | `application/json` |
| `content_encoding` | `utf-8` |

Los otros dos modos del bus no aplican a este contrato: el canal MOM dedicado a la SAT es interno de Facturación (ADR-011) y el canal `direct` de lote hacia el ERP es interno de Integración ERP (ADR-008).

### Convención de routing key

```
ordenes.<entidad>.<hecho-en-kebab-case>
```

### Colas y bindings sugeridos por consumidor

Cada consumidor declara su propia cola durable, con `x-dead-letter-exchange: sgo.eventos.dlx` y `x-delivery-limit: 5`. El nombre de cola pertenece al consumidor; los bindings son los que exige este contrato.

| Cola | Dueño | Bindings |
|---|---|---|
| `auditoria.ordenes` | Julio | `ordenes.#` |
| `consulta-estado.ordenes` | Jonathan | `ordenes.orden.*`, `ordenes.devolucion.*` |
| `notificaciones.ordenes` | Jonathan | `ordenes.orden.estado-cambiado`, `ordenes.orden.confirmada`, `ordenes.orden.cancelada` |
| `inventario.ordenes` | Jose | `ordenes.reserva.*`, `ordenes.devolucion.registrada` |
| `despacho.ordenes` | Jose | `ordenes.despacho.solicitado`, `ordenes.orden.metodo-entrega-modificado`, `ordenes.orden.cancelada` |
| `pagos.ordenes` | Angel | `ordenes.cobro.solicitado`, `ordenes.devolucion.registrada` |
| `facturacion.ordenes` | Angel | `ordenes.fiscal.certificacion-solicitada`, `ordenes.devolucion.registrada` |
| `pos.ordenes` | Francisco | `ordenes.orden.confirmada`, `ordenes.orden.cancelada` |
| `erp.ordenes` | Francisco | `ordenes.orden.confirmada`, `ordenes.devolucion.registrada` |

`auditoria.ordenes` se liga con `ordenes.#` a propósito: Auditoría registra todo, sin excepción (EV-30, EV-38).

---

## 3. Envelope común

Los once eventos comparten el mismo sobre. La única parte que varía es `data`.

| Campo | Tipo | Obligatorio | Descripción |
|---|---|---|---|
| `eventId` | string (UUID v4) | sí | Identificador único e irrepetible del evento. Ancla de idempotencia del consumidor. |
| `eventType` | string | sí | Nombre del evento. Coincide con la sección de este documento. |
| `eventVersion` | integer | sí | Versión del contrato de este evento. Actualmente `1` en todos. |
| `occurredAt` | string (ISO 8601 UTC) | sí | Momento en que ocurrió el hecho de negocio, no el de publicación. |
| `transactionId` | string (UUID v4) | sí | Correlación. Es el mismo valor en todos los eventos de una misma saga. Auditoría indexa por este campo (EV-30, EAC-03). |
| `causationId` | string (UUID) \| null | sí | `eventId` del evento que provocó este. `null` si el origen fue una acción directa del usuario. |
| `aggregateType` | string | sí | `"Orden"` o `"Devolucion"`. |
| `aggregateId` | string | sí | Identificador del agregado. Para `Orden` es el `ordenId`. |
| `source` | string | sí | Siempre `"ordenes"`. |
| `data` | object | sí | Cuerpo específico del evento. |

### Ejemplo del envelope

```json
{
  "eventId": "3f5b2c9e-8a41-4d7e-9c02-1b6e5a7d4f30",
  "eventType": "OrdenEstadoCambiado",
  "eventVersion": 1,
  "occurredAt": "2026-09-07T14:32:10.123Z",
  "transactionId": "a1c4e8b2-77d3-4f19-b5aa-0e93c62d1845",
  "causationId": "9d21f0ab-3c55-4e88-8f14-77b0a2c9e611",
  "aggregateType": "Orden",
  "aggregateId": "ORD-2026-000123",
  "source": "ordenes",
  "data": {}
}
```

En las secciones siguientes solo se muestra el contenido de `data`, para no repetir el sobre.

---

## 4. Enumeraciones cerradas

Ningún consumidor debe recibir un valor fuera de estas listas. Si necesitan uno nuevo, se agrega por versión aditiva del contrato.

### 4.1 `EstadoOrden`

Estados de la máquina de la saga (ADR-002). Jonathan mapea notificaciones sobre esta lista; Consulta de Estado la materializa tal cual.

| Estado | Significado | Terminal |
|---|---|---|
| `Creada` | Orden registrada, aún sin reserva. | no |
| `ReservaSolicitada` | Se pidió reserva a Inventario, sin respuesta. | no |
| `InventarioReservado` | Reserva confirmada con TTL vigente. | no |
| `CobroSolicitado` | Se pidió el cobro a Pagos, sin respuesta. | no |
| `PagoPendiente` | El cobro falló o no respondió. La orden se preserva y se emite enlace de reintento (EV-28, ADR-010). | no |
| `PagoAplicado` | Cobro aceptado. | no |
| `ConfirmadaPendienteFiscal` | Confirmada para el cliente, con certificación SAT en curso (ADR-011). | no |
| `Confirmada` | Certificada fiscalmente. | no |
| `DespachoSolicitado` | Se pidió despacho al contexto Despacho. | no |
| `EnPreparacion` | Preparación física en la ubicación de origen. | no |
| `EnTransito` | En ruta al cliente. | no |
| `ListaParaRecoger` | Disponible para recogida en tienda. | no |
| `Entregada` | Recibida por el cliente. | sí |
| `CanceladaReservaVencida` | Cancelada por expiración de la reserva (HU-002, EV-23). | sí |
| `Cancelada` | Cancelada por el cliente o por un agente. | sí |
| `Rechazada` | No se pudo crear por validación o falta de stock. | sí |
| `DevolucionEnProceso` | Devolución o cambio en curso sobre una orden entregada. | no |
| `Devuelta` | Devolución completada con reembolso aplicado. | sí |

**Fuera de esta lista a propósito:** `GuiaPendiente` no es estado de Orden. Es estado interno de Despacho (HU-011) y lo publica ese contexto en su propio evento. Órdenes no lo conoce ni lo replica. Límite de propiedad de datos según ADR-001.

### 4.2 Otras enumeraciones

| Enumeración | Valores permitidos |
|---|---|
| `Canal` | `WEB`, `MOVIL`, `CALL_CENTER`, `TIENDA` |
| `MetodoEntrega` | `DOMICILIO`, `RECOGIDA_TIENDA` |
| `TipoUbicacion` | `CENTRO_DISTRIBUCION`, `TIENDA` |
| `MedioPago` | `TARJETA`, `TRANSFERENCIA`, `BILLETERA_DIGITAL` |
| `Moneda` | `GTQ`, `USD`, `CRC` |
| `TipoDocumentoFiscal` | `FACTURA`, `NOTA_CREDITO` |
| `TipoDevolucion` | `DEVOLUCION`, `CAMBIO` |
| `MotivoLiberacionReserva` | `COBRO_RECHAZADO`, `COBRO_SIN_RESPUESTA`, `CANCELACION_CLIENTE`, `TIMEOUT_SAGA` |
| `MotivoCancelacion` | `CLIENTE`, `AGENTE`, `RESERVA_VENCIDA`, `STOCK_INSUFICIENTE`, `FRAUDE` |

---

## 5. Catálogo de eventos

Once eventos. Ninguno más se publica bajo `ordenes.*` en la versión 1.

| # | `eventType` | Routing key | CU | Consumidores |
|---|---|---|---|---|
| 1 | `OrdenCreada` | `ordenes.orden.creada` | CU-02 | Auditoría, Consulta de Estado |
| 2 | `OrdenEstadoCambiado` | `ordenes.orden.estado-cambiado` | CU-02 | Auditoría, Consulta de Estado, Notificaciones |
| 3 | `ReservaInventarioSolicitada` | `ordenes.reserva.solicitada` | CU-01 | Inventario, Auditoría |
| 4 | `LiberacionReservaSolicitada` | `ordenes.reserva.liberacion-solicitada` | CU-02 (compensación) | Inventario, Auditoría |
| 5 | `CobroSolicitado` | `ordenes.cobro.solicitado` | CU-03 | Pagos, Auditoría |
| 6 | `CertificacionFiscalSolicitada` | `ordenes.fiscal.certificacion-solicitada` | CU-04 | Facturación, Auditoría |
| 7 | `DespachoSolicitado` | `ordenes.despacho.solicitado` | CU-11 | Despacho, Auditoría |
| 8 | `OrdenConfirmada` | `ordenes.orden.confirmada` | CU-02, CU-09, CU-13 | Integración POS, Integración ERP, Consulta de Estado, Notificaciones, Auditoría |
| 9 | `OrdenCancelada` | `ordenes.orden.cancelada` | CU-02 | Integración POS, Despacho, Consulta de Estado, Notificaciones, Auditoría |
| 10 | `MetodoEntregaModificado` | `ordenes.orden.metodo-entrega-modificado` | CU-01.5 (EV-31) | Despacho, Consulta de Estado, Auditoría |
| 11 | `DevolucionRegistrada` | `ordenes.devolucion.registrada` | CU-14 | Inventario, Facturación, Pagos, Integración ERP, Consulta de Estado, Auditoría |

---

### 5.1 `OrdenCreada`

Routing key `ordenes.orden.creada`. Se emite al persistir la orden en estado `Creada`, antes de solicitar reserva.

| Campo de `data` | Tipo | Obligatorio | Notas |
|---|---|---|---|
| `ordenId` | string | sí | `ORD-2026-000123`. |
| `numeroOrden` | string | sí | Correlativo visible al cliente. |
| `clienteId` | string | sí | Identificador de Auth. |
| `canal` | string | sí | Enum `Canal`. |
| `metodoEntrega` | string | sí | Enum `MetodoEntrega`. |
| `ubicacionSolicitadaId` | string \| null | sí | Tienda de recogida cuando aplica; `null` en domicilio. |
| `moneda` | string | sí | Enum `Moneda`. |
| `subtotal` | string | sí | Decimal como cadena. |
| `impuestos` | string | sí | Decimal como cadena. |
| `total` | string | sí | Decimal como cadena. |
| `lineas` | array | sí | Mínimo un elemento. |
| `lineas[].sku` | string | sí | Del Catálogo Comercial (dependencia externa). |
| `lineas[].descripcion` | string | sí | Descripción al momento de la compra. |
| `lineas[].cantidad` | integer | sí | Mayor que cero. |
| `lineas[].precioUnitario` | string | sí | Decimal como cadena. |
| `lineas[].subtotal` | string | sí | Decimal como cadena. |
| `direccionEntrega` | object \| null | sí | `null` si es recogida en tienda. |
| `direccionEntrega.pais` | string | condicional | ISO 3166-1 alfa-2: `GT`, `SV`, `CR`. |
| `direccionEntrega.departamento` | string | condicional | |
| `direccionEntrega.municipio` | string | condicional | |
| `direccionEntrega.direccion` | string | condicional | |
| `direccionEntrega.referencia` | string \| null | condicional | |
| `creadaEn` | string | sí | ISO 8601 UTC. |

```json
{
  "ordenId": "ORD-2026-000123",
  "numeroOrden": "000123",
  "clienteId": "CLI-000045",
  "canal": "WEB",
  "metodoEntrega": "DOMICILIO",
  "ubicacionSolicitadaId": null,
  "moneda": "GTQ",
  "subtotal": "1250.00",
  "impuestos": "150.00",
  "total": "1400.00",
  "lineas": [
    {
      "sku": "SKU-LB-00871",
      "descripcion": "Refrigeradora 12 pies",
      "cantidad": 1,
      "precioUnitario": "1250.00",
      "subtotal": "1250.00"
    }
  ],
  "direccionEntrega": {
    "pais": "GT",
    "departamento": "Guatemala",
    "municipio": "San Juan Sacatepequez",
    "direccion": "5a avenida 3-21 zona 2",
    "referencia": "Porton azul"
  },
  "creadaEn": "2026-09-07T14:30:00.000Z"
}
```

---

### 5.2 `OrdenEstadoCambiado`

Routing key `ordenes.orden.estado-cambiado`. Se emite en **toda** transición de la máquina de estados, sin excepción. Es el evento del que dependen Auditoría, Consulta de Estado y Notificaciones.

| Campo de `data` | Tipo | Obligatorio | Notas |
|---|---|---|---|
| `ordenId` | string | sí | |
| `numeroOrden` | string | sí | |
| `clienteId` | string | sí | Notificaciones lo necesita para resolver destinatario. |
| `estadoAnterior` | string | sí | Enum `EstadoOrden`. |
| `estadoNuevo` | string | sí | Enum `EstadoOrden`. |
| `motivo` | string \| null | sí | Texto corto de negocio. `null` en transiciones del camino feliz. |
| `pasoSaga` | string \| null | sí | `RESERVA`, `COBRO`, `FISCAL`, `DESPACHO`, `DEVOLUCION` o `null`. |
| `esCompensacion` | boolean | sí | `true` cuando la transición es producto de una compensación. |
| `cambiadoEn` | string | sí | ISO 8601 UTC. |

```json
{
  "ordenId": "ORD-2026-000123",
  "numeroOrden": "000123",
  "clienteId": "CLI-000045",
  "estadoAnterior": "CobroSolicitado",
  "estadoNuevo": "PagoPendiente",
  "motivo": "PayKlik no respondio dentro del timeout configurado",
  "pasoSaga": "COBRO",
  "esCompensacion": true,
  "cambiadoEn": "2026-09-07T14:32:10.123Z"
}
```

---

### 5.3 `ReservaInventarioSolicitada`

Routing key `ordenes.reserva.solicitada`. Órdenes solicita a Inventario la reserva con control de concurrencia (CU-01, ADR-005).

| Campo de `data` | Tipo | Obligatorio | Notas |
|---|---|---|---|
| `ordenId` | string | sí | |
| `metodoEntrega` | string | sí | Enum `MetodoEntrega`. Condiciona la ubicación candidata. |
| `ubicacionPreferidaId` | string \| null | sí | Sugerencia; Inventario decide. |
| `ttlReservaSegundos` | integer | sí | TTL solicitado para la reserva (EV-23). |
| `lineas` | array | sí | Mínimo un elemento. |
| `lineas[].sku` | string | sí | |
| `lineas[].cantidad` | integer | sí | Mayor que cero. |
| `solicitadaEn` | string | sí | ISO 8601 UTC. |

```json
{
  "ordenId": "ORD-2026-000123",
  "metodoEntrega": "DOMICILIO",
  "ubicacionPreferidaId": null,
  "ttlReservaSegundos": 900,
  "lineas": [
    {
      "sku": "SKU-LB-00871",
      "cantidad": 1
    }
  ],
  "solicitadaEn": "2026-09-07T14:30:05.000Z"
}
```

---

### 5.4 `LiberacionReservaSolicitada`

Routing key `ordenes.reserva.liberacion-solicitada`. Compensación de la saga. Órdenes pide a Inventario devolver las unidades al stock disponible.

Distinto de la expiración por TTL: esa la dispara el temporizador de Inventario (CU-06, HU-002) y la publica Inventario, no Órdenes.

| Campo de `data` | Tipo | Obligatorio | Notas |
|---|---|---|---|
| `ordenId` | string | sí | |
| `reservaId` | string | sí | El que devolvió Inventario al reservar. |
| `motivo` | string | sí | Enum `MotivoLiberacionReserva`. |
| `solicitadaEn` | string | sí | ISO 8601 UTC. |

```json
{
  "ordenId": "ORD-2026-000123",
  "reservaId": "RSV-2026-004512",
  "motivo": "COBRO_SIN_RESPUESTA",
  "solicitadaEn": "2026-09-07T14:32:11.000Z"
}
```

---

### 5.5 `CobroSolicitado`

Routing key `ordenes.cobro.solicitado`. Órdenes solicita el cobro a Pagos (CU-03).

**Restricción dura (EV-25, ADR-010):** este payload no contiene ni contendrá PAN, CVV ni fecha de vencimiento completa. Cualquier revisión que encuentre esos campos aquí invalida el contrato.

| Campo de `data` | Tipo | Obligatorio | Notas |
|---|---|---|---|
| `ordenId` | string | sí | |
| `clienteId` | string | sí | |
| `canal` | string | sí | Enum `Canal`. Se audita por EV-26. |
| `moneda` | string | sí | Enum `Moneda`. |
| `monto` | string | sí | Decimal como cadena. |
| `medioPago` | string | sí | Enum `MedioPago` (HU-018). |
| `tokenMedioPago` | string \| null | sí | Token del proveedor. `null` si el medio no requiere tokenización. |
| `ultimosCuatro` | string \| null | sí | Exactamente cuatro dígitos, o `null`. |
| `marca` | string \| null | sí | `VISA`, `MASTERCARD`, `AMEX` o `null`. |
| `intentoNumero` | integer | sí | Empieza en 1. Se incrementa en cada reintento (EV-26). |
| `solicitadoEn` | string | sí | ISO 8601 UTC. |

```json
{
  "ordenId": "ORD-2026-000123",
  "clienteId": "CLI-000045",
  "canal": "WEB",
  "moneda": "GTQ",
  "monto": "1400.00",
  "medioPago": "TARJETA",
  "tokenMedioPago": "tok_pk_9f2a7c41d8",
  "ultimosCuatro": "4417",
  "marca": "VISA",
  "intentoNumero": 1,
  "solicitadoEn": "2026-09-07T14:31:00.000Z"
}
```

---

### 5.6 `CertificacionFiscalSolicitada`

Routing key `ordenes.fiscal.certificacion-solicitada`. Órdenes pide a Facturación generar y certificar el documento ante la SAT de forma asíncrona (CU-04, ADR-011). La orden queda en `ConfirmadaPendienteFiscal` mientras tanto.

| Campo de `data` | Tipo | Obligatorio | Notas |
|---|---|---|---|
| `ordenId` | string | sí | |
| `tipoDocumento` | string | sí | Enum `TipoDocumentoFiscal`. |
| `documentoOrigenId` | string \| null | sí | Documento fiscal original cuando es `NOTA_CREDITO`; `null` en factura. |
| `receptor` | object | sí | Datos fiscales del receptor. |
| `receptor.cui` | string | sí | CUI o identificador fiscal según país. |
| `receptor.nombre` | string | sí | |
| `receptor.direccion` | string | sí | |
| `receptor.pais` | string | sí | `GT`, `SV`, `CR`. |
| `moneda` | string | sí | Enum `Moneda`. |
| `subtotal` | string | sí | Decimal como cadena. |
| `impuestos` | string | sí | Decimal como cadena. |
| `total` | string | sí | Decimal como cadena. |
| `lineas` | array | sí | Mínimo un elemento. |
| `lineas[].sku` | string | sí | |
| `lineas[].descripcion` | string | sí | |
| `lineas[].cantidad` | integer | sí | |
| `lineas[].precioUnitario` | string | sí | |
| `lineas[].impuesto` | string | sí | Decimal como cadena. |
| `solicitadaEn` | string | sí | ISO 8601 UTC. |

```json
{
  "ordenId": "ORD-2026-000123",
  "tipoDocumento": "FACTURA",
  "documentoOrigenId": null,
  "receptor": {
    "cui": "2547896320101",
    "nombre": "Angel David Martinez Garcia",
    "direccion": "5a avenida 3-21 zona 2, San Juan Sacatepequez",
    "pais": "GT"
  },
  "moneda": "GTQ",
  "subtotal": "1250.00",
  "impuestos": "150.00",
  "total": "1400.00",
  "lineas": [
    {
      "sku": "SKU-LB-00871",
      "descripcion": "Refrigeradora 12 pies",
      "cantidad": 1,
      "precioUnitario": "1250.00",
      "impuesto": "150.00"
    }
  ],
  "solicitadaEn": "2026-09-07T14:31:30.000Z"
}
```

---

### 5.7 `DespachoSolicitado`

Routing key `ordenes.despacho.solicitado`. Órdenes pide a Despacho determinar el origen y coordinar la guía (CU-11).

| Campo de `data` | Tipo | Obligatorio | Notas |
|---|---|---|---|
| `ordenId` | string | sí | |
| `reservaId` | string | sí | Ata el despacho a la reserva vigente. |
| `metodoEntrega` | string | sí | Enum `MetodoEntrega`. |
| `ubicacionOrigenSugerida` | object \| null | sí | Sugerencia; Despacho decide el origen final. |
| `ubicacionOrigenSugerida.id` | string | condicional | |
| `ubicacionOrigenSugerida.tipo` | string | condicional | Enum `TipoUbicacion`. |
| `ubicacionRecogidaId` | string \| null | sí | Tienda de recogida; `null` si es domicilio (HU-020). |
| `direccionEntrega` | object \| null | sí | Misma forma que en `OrdenCreada`. `null` si es recogida. |
| `lineas` | array | sí | Mínimo un elemento. |
| `lineas[].sku` | string | sí | |
| `lineas[].descripcion` | string | sí | |
| `lineas[].cantidad` | integer | sí | |
| `solicitadoEn` | string | sí | ISO 8601 UTC. |

```json
{
  "ordenId": "ORD-2026-000123",
  "reservaId": "RSV-2026-004512",
  "metodoEntrega": "DOMICILIO",
  "ubicacionOrigenSugerida": {
    "id": "CD-01",
    "tipo": "CENTRO_DISTRIBUCION"
  },
  "ubicacionRecogidaId": null,
  "direccionEntrega": {
    "pais": "GT",
    "departamento": "Guatemala",
    "municipio": "San Juan Sacatepequez",
    "direccion": "5a avenida 3-21 zona 2",
    "referencia": "Porton azul"
  },
  "lineas": [
    {
      "sku": "SKU-LB-00871",
      "descripcion": "Refrigeradora 12 pies",
      "cantidad": 1
    }
  ],
  "solicitadoEn": "2026-09-07T14:33:00.000Z"
}
```

---

### 5.8 `OrdenConfirmada`

Routing key `ordenes.orden.confirmada`. La venta quedó firme. Es el evento del que se alimentan Integración POS (CU-09) e Integración ERP (CU-13).

Se emite al pasar a `ConfirmadaPendienteFiscal`, **no** al certificar. Motivo: la venta ya es firme para el negocio y el POS y el ERP no deben esperar a la SAT (ADR-011). Cuando llegue la certificación, el cambio viaja por `OrdenEstadoCambiado` con `estadoNuevo: "Confirmada"`.

| Campo de `data` | Tipo | Obligatorio | Notas |
|---|---|---|---|
| `ordenId` | string | sí | |
| `numeroOrden` | string | sí | |
| `clienteId` | string | sí | |
| `canal` | string | sí | Enum `Canal`. El ERP consolida por canal y tienda. |
| `tiendaDestinoId` | string \| null | sí | Tienda a cuya instancia de POS corresponde la venta. `null` si el canal no mapea a tienda. |
| `moneda` | string | sí | Enum `Moneda`. |
| `subtotal` | string | sí | Decimal como cadena. |
| `impuestos` | string | sí | Decimal como cadena. |
| `total` | string | sí | Decimal como cadena. |
| `pagoId` | string | sí | Identificador del pago aplicado. |
| `documentoFiscalId` | string \| null | sí | `null` mientras la certificación esté pendiente. |
| `lineas` | array | sí | Mínimo un elemento. |
| `lineas[].sku` | string | sí | |
| `lineas[].descripcion` | string | sí | |
| `lineas[].cantidad` | integer | sí | |
| `lineas[].precioUnitario` | string | sí | |
| `lineas[].subtotal` | string | sí | |
| `confirmadaEn` | string | sí | ISO 8601 UTC. |

```json
{
  "ordenId": "ORD-2026-000123",
  "numeroOrden": "000123",
  "clienteId": "CLI-000045",
  "canal": "WEB",
  "tiendaDestinoId": "TDA-014",
  "moneda": "GTQ",
  "subtotal": "1250.00",
  "impuestos": "150.00",
  "total": "1400.00",
  "pagoId": "PAG-2026-008877",
  "documentoFiscalId": null,
  "lineas": [
    {
      "sku": "SKU-LB-00871",
      "descripcion": "Refrigeradora 12 pies",
      "cantidad": 1,
      "precioUnitario": "1250.00",
      "subtotal": "1250.00"
    }
  ],
  "confirmadaEn": "2026-09-07T14:31:45.000Z"
}
```

---

### 5.9 `OrdenCancelada`

Routing key `ordenes.orden.cancelada`. Cancelación de la orden por cualquier causa.

| Campo de `data` | Tipo | Obligatorio | Notas |
|---|---|---|---|
| `ordenId` | string | sí | |
| `numeroOrden` | string | sí | |
| `clienteId` | string | sí | |
| `tiendaDestinoId` | string \| null | sí | Necesario si el POS ya recibió la venta. |
| `motivo` | string | sí | Enum `MotivoCancelacion`. |
| `detalle` | string \| null | sí | Texto libre corto. |
| `requiereReembolso` | boolean | sí | `true` si ya se había aplicado un cobro. |
| `pagoId` | string \| null | sí | `null` si nunca hubo cobro aplicado. |
| `documentoFiscalId` | string \| null | sí | `null` si nunca se certificó. |
| `moneda` | string \| null | sí | `null` si `requiereReembolso` es `false`. |
| `montoReembolso` | string \| null | sí | Decimal como cadena o `null`. |
| `canceladaEn` | string | sí | ISO 8601 UTC. |

```json
{
  "ordenId": "ORD-2026-000123",
  "numeroOrden": "000123",
  "clienteId": "CLI-000045",
  "tiendaDestinoId": "TDA-014",
  "motivo": "RESERVA_VENCIDA",
  "detalle": "TTL de reserva expirado sin cobro aplicado",
  "requiereReembolso": false,
  "pagoId": null,
  "documentoFiscalId": null,
  "moneda": null,
  "montoReembolso": null,
  "canceladaEn": "2026-09-07T14:47:00.000Z"
}
```

---

### 5.10 `MetodoEntregaModificado`

Routing key `ordenes.orden.metodo-entrega-modificado`. La orden es un agregado de larga vida y mutable: el método de entrega puede cambiar después de confirmada (EV-31, CUN-01.5). Despacho usa este evento para reasignar guía u origen (HU-011).

| Campo de `data` | Tipo | Obligatorio | Notas |
|---|---|---|---|
| `ordenId` | string | sí | |
| `metodoEntregaAnterior` | string | sí | Enum `MetodoEntrega`. |
| `metodoEntregaNuevo` | string | sí | Enum `MetodoEntrega`. |
| `ubicacionRecogidaId` | string \| null | sí | Requerido si el nuevo método es `RECOGIDA_TIENDA`. |
| `direccionEntrega` | object \| null | sí | Requerido si el nuevo método es `DOMICILIO`. Misma forma que en `OrdenCreada`. |
| `solicitadoPor` | string | sí | `CLIENTE` o `AGENTE`. |
| `modificadoEn` | string | sí | ISO 8601 UTC. |

```json
{
  "ordenId": "ORD-2026-000123",
  "metodoEntregaAnterior": "DOMICILIO",
  "metodoEntregaNuevo": "RECOGIDA_TIENDA",
  "ubicacionRecogidaId": "TDA-014",
  "direccionEntrega": null,
  "solicitadoPor": "CLIENTE",
  "modificadoEn": "2026-09-07T18:05:00.000Z"
}
```

---

### 5.11 `DevolucionRegistrada`

Routing key `ordenes.devolucion.registrada`. Devolución o cambio sobre una orden entregada (CU-14, HU-014). Es el único evento con `aggregateType: "Devolucion"`.

Dispara tres reacciones en paralelo: reingreso de inventario, nota de crédito y reembolso.

| Campo de `data` | Tipo | Obligatorio | Notas |
|---|---|---|---|
| `devolucionId` | string | sí | `DEV-2026-000031`. |
| `ordenOriginalId` | string | sí | Mantiene la cadena documental (EV-29). |
| `clienteId` | string | sí | |
| `tipo` | string | sí | Enum `TipoDevolucion`. |
| `esParcial` | boolean | sí | `true` si no se devuelven todas las líneas. |
| `ubicacionReingresoId` | string | sí | Dónde reingresan las unidades. |
| `lineas` | array | sí | Mínimo un elemento. |
| `lineas[].sku` | string | sí | |
| `lineas[].cantidad` | integer | sí | Menor o igual a lo comprado. |
| `lineas[].motivo` | string | sí | Texto corto. |
| `lineas[].skuReemplazo` | string \| null | sí | Solo cuando `tipo` es `CAMBIO`. |
| `moneda` | string | sí | Enum `Moneda`. |
| `montoReembolso` | string | sí | Decimal como cadena. `"0.00"` en cambio sin diferencia. |
| `pagoOriginalId` | string | sí | Pago sobre el que se aplica el reembolso. |
| `documentoFiscalOriginalId` | string | sí | Factura que la nota de crédito reversa. |
| `registradaPor` | string | sí | Identificador del agente. |
| `registradaEn` | string | sí | ISO 8601 UTC. |

```json
{
  "devolucionId": "DEV-2026-000031",
  "ordenOriginalId": "ORD-2026-000123",
  "clienteId": "CLI-000045",
  "tipo": "DEVOLUCION",
  "esParcial": false,
  "ubicacionReingresoId": "TDA-014",
  "lineas": [
    {
      "sku": "SKU-LB-00871",
      "cantidad": 1,
      "motivo": "Producto con abolladura en la puerta",
      "skuReemplazo": null
    }
  ],
  "moneda": "GTQ",
  "montoReembolso": "1400.00",
  "pagoOriginalId": "PAG-2026-008877",
  "documentoFiscalOriginalId": "DOC-2026-005510",
  "registradaPor": "AGT-000009",
  "registradaEn": "2026-09-12T10:15:00.000Z"
}
```

---

## 6. Matriz de consumo

Fila por consumidor. Si algo falta para su historia de usuario, se objeta antes del congelamiento.

| Consumidor | Dueño | HU | Eventos que consume | Para qué |
|---|---|---|---|---|
| Auditoría | Julio | HU-006 | Los once (`ordenes.#`) | Bitácora append-only indexada por `transactionId` (EV-30, EAC-03). |
| Consulta de Estado | Jonathan | HU-010 | 1, 2, 8, 9, 10, 11 | Vista materializada en Redis y vista 360 (ADR-004, EAC-02). |
| Notificaciones | Jonathan | HU-015 | 2, 8, 9 | Aviso al cliente por cada cambio relevante (EV-34). |
| Inventario | Jose | HU-001, HU-002, HU-014 | 3, 4, 11 | Reservar, liberar por compensación y reingresar por devolución. |
| Despacho | Jose | HU-011, HU-012, HU-020 | 7, 9, 10 | Origen, guía, reasignación y recogida en tienda. |
| Pagos | Angel | HU-004, HU-014 | 5, 11 | Intento de cobro y reembolso. |
| Facturación | Angel | HU-005, HU-014 | 6, 11 | Certificación SAT y nota de crédito. |
| Integración POS | Francisco | HU-009 | 8, 9 | Propagación idempotente a la instancia de tienda (EV-16). |
| Integración ERP | Francisco | HU-013, HU-022 | 8, 11 | Consolidación del lote de 30 minutos y conciliación (EV-15). |

**Qué no está aquí y por qué:** Auth no consume eventos de Órdenes. El carrito unificado (HU-007) es propiedad de Auth y Órdenes sobre Redis y no genera eventos de este contrato.

---

## 7. Versionado

**Cambio aditivo (v1.x).** Se permite agregar un campo opcional o un evento nuevo. No requiere renegociar. Se anuncia al grupo y se actualiza este documento. Los consumidores existentes siguen funcionando sin tocarse.

**Cambio rompiente (v2).** Quitar un campo, renombrarlo, cambiar su tipo, o agregar un valor a una enumeración cerrada. Requiere:

1. Nuevo `eventVersion: 2`.
2. Nueva routing key con sufijo, por ejemplo `ordenes.orden.confirmada.v2`.
3. Publicación simultánea de v1 y v2 hasta que todos los consumidores migren.
4. Acuerdo explícito del grupo, registrado como comentario en la tarjeta HU-003.

**Deprecación.** Un evento no se elimina mientras exista un consumidor ligado a él. Se marca como obsoleto en este documento con la fecha de retiro.

---

## 8. Congelamiento

Una vez congelado, cualquier cambio a este documento se avisa al grupo antes de hacerlo, sin excepción. Cuatro contextos dependen de él y un cambio silencioso rompe el trabajo de otras personas.

| Revisor | Contexto | Estado | Fecha |
|---|---|---|---|
| Julio Herrera | Auditoría | pendiente | |
| Jonathan Jerónimo | Consulta de Estado, Notificaciones | pendiente | |
| Jose Pirir | Inventario, Despacho | pendiente | |
| Francisco Castillo | Integración POS, Integración ERP | pendiente | |
| Angel Martínez | Órdenes, Pagos, Facturación | autor | 2026-09-07 |

---

## 9. Supuestos abiertos

Cosas que este contrato asume y que conviene confirmar. Ninguna bloquea el congelamiento, pero todas pueden generar una v1.1.

1. **Cliente de mensajería.** El envelope está diseñado para RabbitMQ.Client directo. Si el grupo adopta MassTransit, ese cliente impone su propio sobre y este contrato pasa a v1.1.
2. **Motor SQL.** El DDA sostiene el techo de USD 1,200 mensuales afirmando que los motores SQL son software libre (EV-42, sección 10 del DDA). Eso apunta a PostgreSQL. Si se elige SQL Server, hay que revisar esa afirmación del DDA.
3. **Mapeo de `tiendaDestinoId`.** Órdenes lo emite, pero la tabla tienda a instancia de POS es propiedad de Integración POS (HU-009). Se asume que Francisco resuelve el mapeo del lado receptor.
4. **TTL de reserva.** Se emite como parámetro en `ReservaInventarioSolicitada`; el valor definitivo lo fija Inventario (HU-001).
5. **Precios del Catálogo Comercial.** Los precios viajan congelados al momento de la compra. El Catálogo es dependencia externa fuera del alcance y no es fuente de verdad después de creada la orden.
