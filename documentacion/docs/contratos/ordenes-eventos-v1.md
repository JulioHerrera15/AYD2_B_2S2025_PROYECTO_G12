# Contrato de eventos del contexto Órdenes — v1.2

**Proyecto:** SGO-Logic — Fase 2
**Curso:** Análisis y Diseño de Sistemas 2, Sección B
**Contexto emisor:** Órdenes
**Autor:** 202300410 — Angel David Martínez García
**Estado:** listo para congelamiento
**Fecha:** 2026-09-12
**Depende de:** `sgo-eventos-envelope-v1.md`

---

## 0. Cambios respecto a v1.1

Los eventos 11 (`DevolucionRegistrada`) y 12 (`OrdenCertificadaFiscalmente`) pasan de definidos-pero-no-emitidos a **implementados**: ambos se publican desde el código de Órdenes contra la topología descrita aquí. Esto trae tres cambios de bindings:

| # | Cambio | Detalle |
|---|---|---|
| 1 | Cola nueva `ordenes.facturacion` | Órdenes ahora consume `facturacion.documento-fiscal.certificado` y `facturacion.documento-fiscal.certificacion-fallida` (eventos 1 y 2 de `facturacion-eventos-v1`) para cerrar el tramo `CONFIRMADA_PENDIENTE_FISCAL` → `CONFIRMADA` y para levantar la bandera de alerta fiscal (ADR-011, ADR-015). Ver sección 4. |
| 2 | `pagos.ordenes` liga `ordenes.devolucion.registrada` | El binding que la v1.1 dejaba fijado ahora está declarado en código: `PagosEventosConsumidor` procesa la devolución y genera el reembolso contra PayKlik. |
| 3 | `facturacion.ordenes` liga `ordenes.devolucion.registrada` | Igual que el punto anterior, del lado de Facturación: genera la nota de crédito. |

Sigue vigente todo lo resuelto en v1.1 (objeciones 1 a 10, tabla de la sección "Cambios respecto a v1" de esa versión, no repetida aquí).

## 0.1 Cambios respecto a v1

Esta versión incorpora las objeciones de Julio Herrera (Auditoría, base técnica), Jonathan Jerónimo (Consulta de Estado, Notificaciones) y Francisco Castillo (Integración POS, Integración ERP).

| # | Objeción | Resolución |
|---|---|---|
| 1 | El envelope y las enumeraciones compartidas no pueden pertenecer a Órdenes | Extraídos a `sgo-eventos-envelope-v1.md`, propiedad de la base técnica. Auditoría pasa a binding `#`. |
| 2 | Falta secuencia monotónica por agregado | `sequence` agregado al envelope compartido, con last-writer-wins. Ver sección 3 de ese documento. |
| 3 | `causationId` no se llena solo | Propagación automática por contexto ambiente en `SgoLogic.Contracts`. Ver sección 4 de ese documento. |
| 4 | `x-delivery-limit` solo aplica a colas quorum | Los argumentos de cola salen de este contrato. Aquí solo quedan los bindings; el tipo de cola lo decide la base técnica (HU-008). |
| 5 | Nadie es dueño de la DLQ | Registrado como pendiente con dueño en el documento de envelope, sección 5. |
| 6 | Casing inconsistente entre enumeraciones | Todo valor de enumeración pasa a MAYUSCULA_CON_GUION_BAJO. `eventType` sigue en PascalCase por ser nombre de tipo. |
| 7 | Ningún evento entrega el `documentoFiscalId` final | Evento 12 nuevo: `OrdenCertificadaFiscalmente`. Ver sección 6 para por qué no se agregó al evento 2. |
| 8 | El mismo hecho llega por dos rutas sin orden garantizado | Sección 6: fuente autoritativa declarada por consumidor, reforzada por topología. |
| 9 | `erp.ordenes` no recibe `OrdenCancelada`: riesgo de venta fantasma en el lote | Binding agregado. Ver sección 4. |
| 10 | El ERP nunca se entera del folio fiscal | Binding al evento 12 agregado. Ver sección 4. |

Los supuestos 1 y 2 de la v1 quedan cerrados: RabbitMQ.Client directo y PostgreSQL.

---

## 1. Alcance

Este documento define únicamente el contenido de `data` de los eventos que **publica** el contexto Órdenes. El sobre, las reglas de idempotencia y ordenamiento, las enumeraciones compartidas y la topología del bus están en `sgo-eventos-envelope-v1.md` y no se repiten aquí.

Órdenes es el único emisor de eventos con routing key `ordenes.*`. Ningún otro contexto publica bajo ese prefijo.

Trazabilidad al DDA: ADR-001, ADR-002, ADR-003, ADR-004, ADR-008, ADR-010, ADR-011, ADR-012, ADR-015.

---

## 2. Enumeraciones propias del contexto

Las compartidas (`Canal`, `Moneda`, `Pais`, `MetodoEntrega`, `TipoUbicacion`, `MedioPago`) están en el documento de envelope.

### 2.1 `EstadoOrden`

Estados de la máquina de la saga (ADR-002). Lista cerrada.

| Valor | Significado | Terminal |
|---|---|---|
| `CREADA` | Orden registrada, aún sin reserva. | no |
| `RESERVA_SOLICITADA` | Se pidió reserva a Inventario, sin respuesta. | no |
| `INVENTARIO_RESERVADO` | Reserva confirmada con TTL vigente. | no |
| `COBRO_SOLICITADO` | Se pidió el cobro a Pagos, sin respuesta. | no |
| `PAGO_PENDIENTE` | El cobro falló o no respondió. La orden se preserva y se emite enlace de reintento (EV-28, ADR-010). | no |
| `PAGO_APLICADO` | Cobro aceptado. | no |
| `CONFIRMADA_PENDIENTE_FISCAL` | Confirmada para el cliente, con certificación SAT en curso (ADR-011). | no |
| `CONFIRMADA` | Certificada fiscalmente. | no |
| `DESPACHO_SOLICITADO` | Se pidió despacho al contexto Despacho. | no |
| `EN_PREPARACION` | Preparación física en la ubicación de origen. | no |
| `EN_TRANSITO` | En ruta al cliente. | no |
| `LISTA_PARA_RECOGER` | Disponible para recogida en tienda. | no |
| `ENTREGADA` | Recibida por el cliente. | sí |
| `CANCELADA_RESERVA_VENCIDA` | Cancelada por expiración de la reserva (HU-002, EV-23). | sí |
| `CANCELADA` | Cancelada por el cliente, por un agente o por fraude. | sí |
| `RECHAZADA` | No se pudo crear por validación o falta de stock. | sí |
| `DEVOLUCION_EN_PROCESO` | Devolución o cambio en curso sobre una orden entregada. | no |
| `DEVUELTA` | Devolución completada con reembolso aplicado. | sí |

**Fuera de esta lista a propósito:** `GUIA_PENDIENTE` no es estado de Orden. Es estado interno de Despacho (HU-011) y lo publica ese contexto. Órdenes no lo conoce ni lo replica. Límite de propiedad de datos según ADR-001.

### 2.2 Resto de enumeraciones

| Enumeración | Valores permitidos |
|---|---|
| `TipoDocumentoFiscal` | `FACTURA`, `NOTA_CREDITO` |
| `TipoDevolucion` | `DEVOLUCION`, `CAMBIO` |
| `MotivoLiberacionReserva` | `COBRO_RECHAZADO`, `COBRO_SIN_RESPUESTA`, `CANCELACION_CLIENTE`, `TIMEOUT_SAGA` |
| `MotivoCancelacion` | `CLIENTE`, `AGENTE`, `RESERVA_VENCIDA`, `STOCK_INSUFICIENTE`, `FRAUDE` |
| `PasoSaga` | `RESERVA`, `COBRO`, `FISCAL`, `DESPACHO`, `DEVOLUCION` |
| `SolicitanteCambio` | `CLIENTE`, `AGENTE` |

---

## 3. Catálogo de eventos

Doce eventos. Ninguno más se publica bajo `ordenes.*` en la versión 1.

| # | `eventType` | Routing key | CU | Consumidores |
|---|---|---|---|---|
| 1 | `OrdenCreada` | `ordenes.orden.creada` | CU-02 | Consulta de Estado |
| 2 | `OrdenEstadoCambiado` | `ordenes.orden.estado-cambiado` | CU-02 | Consulta de Estado, Notificaciones |
| 3 | `ReservaInventarioSolicitada` | `ordenes.reserva.solicitada` | CU-01 | Inventario |
| 4 | `LiberacionReservaSolicitada` | `ordenes.reserva.liberacion-solicitada` | CU-02 (compensación) | Inventario |
| 5 | `CobroSolicitado` | `ordenes.cobro.solicitado` | CU-03 | Pagos |
| 6 | `CertificacionFiscalSolicitada` | `ordenes.fiscal.certificacion-solicitada` | CU-04 | Facturación |
| 7 | `DespachoSolicitado` | `ordenes.despacho.solicitado` | CU-11 | Despacho |
| 8 | `OrdenConfirmada` | `ordenes.orden.confirmada` | CU-02, CU-09, CU-13 | Integración POS, Integración ERP, Consulta de Estado |
| 9 | `OrdenCancelada` | `ordenes.orden.cancelada` | CU-02 | Integración POS, Integración ERP, Despacho, Consulta de Estado |
| 10 | `MetodoEntregaModificado` | `ordenes.orden.metodo-entrega-modificado` | CU-01.5 (EV-31) | Despacho, Consulta de Estado |
| 11 | `DevolucionRegistrada` | `ordenes.devolucion.registrada` | CU-14 | Inventario, Facturación, Pagos, Integración ERP, Consulta de Estado |
| 12 | `OrdenCertificadaFiscalmente` | `ordenes.orden.certificada-fiscalmente` | CU-04, CU-13 | Integración ERP, Consulta de Estado |

Auditoría consume los doce por su binding `#` y no se lista fila por fila.

**Estado de implementación.** Los doce eventos están implementados y en producción. Los eventos 11 y 12 eran, en v1.1, contrato sin código: el primero esperaba a que Pagos y Facturación tuvieran manejadores de devolución, y el segundo esperaba al consumo de la certificación SAT. Ambos se implementaron en el mismo bloque de trabajo (registro de devoluciones, nota de crédito fiscal y reembolso contra PayKlik) y ya publican contra la topología descrita en este documento.

---

## 4. Bindings

Cada consumidor declara su propia cola durable. El nombre de la cola y sus argumentos pertenecen al consumidor y a la base técnica; los bindings los fija este contrato.

| Cola | Dueño | Bindings |
|---|---|---|
| `consulta-estado.ordenes` | Jonathan | `ordenes.orden.*`, `ordenes.devolucion.*` |
| `notificaciones.ordenes` | Jonathan | `ordenes.orden.estado-cambiado` |
| `inventario.ordenes` | Jose | `ordenes.reserva.*`, `ordenes.devolucion.registrada` |
| `despacho.ordenes` | Jose | `ordenes.despacho.solicitado`, `ordenes.orden.metodo-entrega-modificado`, `ordenes.orden.cancelada` |
| `pagos.ordenes` | Angel | `ordenes.cobro.solicitado`, `ordenes.devolucion.registrada` |
| `facturacion.ordenes` | Angel | `ordenes.fiscal.certificacion-solicitada`, `ordenes.devolucion.registrada` |
| `pos.ordenes` | Francisco | `ordenes.orden.confirmada`, `ordenes.orden.cancelada` |
| `erp.ordenes` | Francisco | `ordenes.orden.confirmada`, `ordenes.orden.cancelada`, `ordenes.orden.certificada-fiscalmente`, `ordenes.devolucion.registrada` |

**`pagos.ordenes` y `facturacion.ordenes` ya escuchan `ordenes.devolucion.registrada`, en código, no solo en el papel.** `PagosEventosConsumidor` (Pagos) y `FacturacionEventosConsumidor` (Facturación) declaran ambos bindings sobre su cola y despachan el evento 11 al manejador correspondiente (reembolso contra PayKlik en Pagos, nota de crédito en Facturación). Ninguno usa `EventConsumerBase` para esto: esa clase base está tipada a un único `TData` por cola, y estas dos colas reciben dos tipos de `data` distintos sobre bindings distintos.

**Cola nueva `ordenes.facturacion`, dueño Órdenes.** No es un binding a `ordenes.*`: es la cola propia de Órdenes para **consumir** los eventos que publica Facturación (`facturacion-eventos-v1`), simétrica a como `facturacion.ordenes` consume los de Órdenes. Se documenta aquí porque cierra el tramo `CONFIRMADA_PENDIENTE_FISCAL` → `CONFIRMADA` de este mismo contrato (evento 12) y porque el consumidor tampoco usa `EventConsumerBase`, por la misma razón de las dos colas anteriores.

| Cola | Dueño | Bindings | Exchange de origen |
|---|---|---|---|
| `ordenes.facturacion` | Angel (Órdenes) | `facturacion.documento-fiscal.certificado`, `facturacion.documento-fiscal.certificacion-fallida` | `sgo.eventos` (propiedad de Facturación, ver `facturacion-eventos-v1.md`) |

El primer binding (`DocumentoFiscalCertificado`) cierra la transición y emite el evento 12 de este documento. El segundo (`CertificacionSatFallida`) levanta la bandera de alerta fiscal sobre la orden sin cambiarle el estado (ADR-011, ADR-015); no produce ningún evento nuevo de Órdenes, solo un cambio interno visible por consulta.

**Por qué `notificaciones.ordenes` solo recibe el evento 2.** Es la respuesta a la objeción 8. Si la cola nunca recibe los eventos 8 y 9, es imposible que Notificaciones emita dos avisos del mismo hecho, aunque alguien se equivoque programando. La restricción vive en la topología, no en la disciplina de quien la implementa.

**Por qué `erp.ordenes` recibe ahora `OrdenCancelada` (objeción 9).** El catálogo de estados permite cancelar una orden ya confirmada, por ejemplo con motivo `FRAUDE`. Si eso ocurre dentro de la ventana de 30 minutos anterior a la corrida del worker de consolidación (HU-013, ADR-008), el lote incluiría una venta que ya no existe y la conciliación (HU-022) tendría que descubrirlo después. El diseño no espera que la conciliación absorba ese caso: la conciliación existe para detectar lo que no se pudo prevenir, no para limpiar lo que sí. Con el binding, Integración ERP excluye la venta antes de armar el lote.

**Por qué `erp.ordenes` recibe el evento 12 (objeción 10).** La sección 4.5 del DDA delimita CUN-07 dejando el cierre contable en el ERP. Un cierre contable necesita el folio fiscal. `OrdenConfirmada` se emite antes de la certificación y por eso lleva `documentoFiscalId` en `null`; el evento 12 entrega el número definitivo cuando la SAT responde.

---

## 5. Envelope aplicado a este contexto

Valores fijos para todos los eventos de Órdenes. El resto de campos y sus reglas están en el documento de envelope.

| Campo | Valor en este contexto |
|---|---|
| `source` | `"ordenes"` |
| `aggregateType` | `"Orden"` en los eventos 1 a 10 y 12; `"Devolucion"` en el evento 11 |
| `aggregateId` | `ordenId` con formato `ORD-<año>-<correlativo de 6 dígitos>`; `devolucionId` en el evento 11 |
| `sequence` | Asignado por Órdenes, monotónico por `ordenId`, empieza en 1 con `OrdenCreada` |

---

## 6. Fuente autoritativa del estado

Respuesta a la objeción 8. Regla normativa, no recomendación.

1. **El estado inicial lo establece `OrdenCreada` (evento 1), siempre con `sequence` 1.**
2. **A partir de ahí, el único evento que cambia el estado de una orden es `OrdenEstadoCambiado` (evento 2).**
3. **Los eventos 8, 9 y 12 están prohibidos como fuente de estado.** Llevan carga comercial y fiscal para POS y ERP, y llegan acompañados de su `OrdenEstadoCambiado` correspondiente. Un consumidor que use el evento 8 para escribir `CONFIRMADA_PENDIENTE_FISCAL` en su modelo de lectura está violando este contrato.

| Consumidor | Ruta autoritativa para el estado | Para qué usa las demás rutas |
|---|---|---|
| Consulta de Estado | Eventos 1 y 2 | Eventos 8, 9, 10, 11 y 12 enriquecen la vista 360 con datos comerciales, `documentoFiscalId` y `tiendaDestinoId`, sin tocar el campo de estado. |
| Notificaciones | Evento 2, único | No recibe otra ruta por topología. |
| Integración POS | No materializa estado | Eventos 8 y 9 como hechos comerciales. |
| Integración ERP | No materializa estado | Eventos 8, 9, 11 y 12 para armar y corregir el lote. |
| Inventario, Pagos, Facturación, Despacho | No materializan estado de Orden | Reciben órdenes de trabajo, no proyecciones. |
| Auditoría | No materializa estado | Inserta los doce sin sobrescribir. |

**Por qué `documentoFiscalId` no se agregó al evento 2.** Jonathan propuso como alternativa incluirlo en `OrdenEstadoCambiado` cuando `estadoNuevo` fuera `CONFIRMADA`. Se descartó porque reintroduce el problema de su propia objeción 8: dos rutas cargando el mismo dato, con orden no garantizado entre ellas. El evento 12 es el portador único, y el binding `ordenes.orden.*` de Consulta de Estado ya se lo entrega sin cambiar nada de su lado.

---

## 7. Catálogo detallado

En las secciones siguientes solo se muestra el contenido de `data`.

### 7.1 `OrdenCreada`

Routing key `ordenes.orden.creada`. Se emite al persistir la orden, antes de solicitar reserva. Siempre con `sequence` 1.

| Campo de `data` | Tipo | Obligatorio | Notas |
|---|---|---|---|
| `ordenId` | string | sí | `ORD-2026-000123`. |
| `numeroOrden` | string | sí | Correlativo visible al cliente. |
| `clienteId` | string | sí | Identificador de Auth. |
| `estado` | string | sí | Siempre `"CREADA"`. Presente para que el consumidor use un mapeador uniforme. |
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
| `direccionEntrega.pais` | string | condicional | Enum `Pais`. |
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
  "estado": "CREADA",
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

### 7.2 `OrdenEstadoCambiado`

Routing key `ordenes.orden.estado-cambiado`. Se emite en **toda** transición posterior a la creación, sin excepción. Fuente autoritativa del estado.

| Campo de `data` | Tipo | Obligatorio | Notas |
|---|---|---|---|
| `ordenId` | string | sí | |
| `numeroOrden` | string | sí | |
| `clienteId` | string | sí | Notificaciones lo necesita para resolver destinatario. |
| `estadoAnterior` | string | sí | Enum `EstadoOrden`. |
| `estadoNuevo` | string | sí | Enum `EstadoOrden`. |
| `motivo` | string \| null | sí | Texto corto de negocio. `null` en el camino feliz. |
| `pasoSaga` | string \| null | sí | Enum `PasoSaga` o `null`. |
| `esCompensacion` | boolean | sí | `true` cuando la transición proviene de una compensación. |
| `cambiadoEn` | string | sí | ISO 8601 UTC. |

```json
{
  "ordenId": "ORD-2026-000123",
  "numeroOrden": "000123",
  "clienteId": "CLI-000045",
  "estadoAnterior": "COBRO_SOLICITADO",
  "estadoNuevo": "PAGO_PENDIENTE",
  "motivo": "PayKlik no respondio dentro del timeout configurado",
  "pasoSaga": "COBRO",
  "esCompensacion": true,
  "cambiadoEn": "2026-09-07T14:32:10.123Z"
}
```

---

### 7.3 `ReservaInventarioSolicitada`

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

### 7.4 `LiberacionReservaSolicitada`

Routing key `ordenes.reserva.liberacion-solicitada`. Compensación de la saga.

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

### 7.5 `CobroSolicitado`

Routing key `ordenes.cobro.solicitado`. Órdenes solicita el cobro a Pagos (CU-03).

**Restricción dura (RES-08, EV-25, ADR-010):** este payload no contiene ni contendrá PAN, CVV ni fecha de vencimiento completa. Cualquier revisión que encuentre esos campos aquí invalida el contrato.

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

### 7.6 `CertificacionFiscalSolicitada`

Routing key `ordenes.fiscal.certificacion-solicitada`. Órdenes pide a Facturación generar y certificar el documento ante la SAT de forma asíncrona (CU-04, ADR-011, RES-07).

| Campo de `data` | Tipo | Obligatorio | Notas |
|---|---|---|---|
| `ordenId` | string | sí | |
| `tipoDocumento` | string | sí | Enum `TipoDocumentoFiscal`. |
| `documentoOrigenId` | string \| null | sí | Documento original cuando es `NOTA_CREDITO`; `null` en factura. |
| `receptor` | object | sí | Datos fiscales del receptor. |
| `receptor.cui` | string | sí | CUI o identificador fiscal según país. |
| `receptor.nombre` | string | sí | |
| `receptor.direccion` | string | sí | |
| `receptor.pais` | string | sí | Enum `Pais`. |
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

### 7.7 `DespachoSolicitado`

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

### 7.8 `OrdenConfirmada`

Routing key `ordenes.orden.confirmada`. La venta quedó firme. Alimenta Integración POS (CU-09, ADR-012) e Integración ERP (CU-13, ADR-008).

Se emite al pasar a `CONFIRMADA_PENDIENTE_FISCAL`, **no** al certificar. La venta ya es firme para el negocio y ni el POS ni el ERP deben esperar a la SAT (ADR-011). El folio definitivo llega después en el evento 12.

Prohibido como fuente de estado. Ver sección 6.

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
| `documentoFiscalId` | string \| null | sí | Siempre `null` en este evento. El folio viaja en el evento 12. |
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

### 7.9 `OrdenCancelada`

Routing key `ordenes.orden.cancelada`. Cancelación por cualquier causa, incluida la cancelación de una orden ya confirmada.

Prohibido como fuente de estado. Ver sección 6.

| Campo de `data` | Tipo | Obligatorio | Notas |
|---|---|---|---|
| `ordenId` | string | sí | |
| `numeroOrden` | string | sí | |
| `clienteId` | string | sí | |
| `tiendaDestinoId` | string \| null | sí | Necesario si el POS ya recibió la venta. |
| `estabaConfirmada` | boolean | sí | `true` si ya se había emitido `OrdenConfirmada`. Permite al ERP excluir la venta del lote sin consultar su propio histórico. |
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
  "estabaConfirmada": true,
  "motivo": "FRAUDE",
  "detalle": "Cobro revertido por sospecha de fraude antes del cierre del lote",
  "requiereReembolso": true,
  "pagoId": "PAG-2026-008877",
  "documentoFiscalId": null,
  "moneda": "GTQ",
  "montoReembolso": "1400.00",
  "canceladaEn": "2026-09-07T14:47:00.000Z"
}
```

---

### 7.10 `MetodoEntregaModificado`

Routing key `ordenes.orden.metodo-entrega-modificado`. La orden es un agregado de larga vida y mutable: el método de entrega puede cambiar después de confirmada (EV-31, CUN-01.5). Despacho lo usa para reasignar guía u origen (HU-011).

| Campo de `data` | Tipo | Obligatorio | Notas |
|---|---|---|---|
| `ordenId` | string | sí | |
| `metodoEntregaAnterior` | string | sí | Enum `MetodoEntrega`. |
| `metodoEntregaNuevo` | string | sí | Enum `MetodoEntrega`. |
| `ubicacionRecogidaId` | string \| null | sí | Requerido si el nuevo método es `RECOGIDA_TIENDA`. |
| `direccionEntrega` | object \| null | sí | Requerido si el nuevo método es `DOMICILIO`. Misma forma que en `OrdenCreada`. |
| `solicitadoPor` | string | sí | Enum `SolicitanteCambio`. |
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

### 7.11 `DevolucionRegistrada`

Routing key `ordenes.devolucion.registrada`. Devolución o cambio sobre una orden entregada (CU-14, HU-014). Único evento con `aggregateType: "Devolucion"`.

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

### 7.12 `OrdenCertificadaFiscalmente`

Routing key `ordenes.orden.certificada-fiscalmente`. Se emite cuando la SAT devuelve la certificación y la orden pasa de `CONFIRMADA_PENDIENTE_FISCAL` a `CONFIRMADA` (ADR-011).

Portador único del folio fiscal. Cierra las objeciones 7 y 10.

Prohibido como fuente de estado. La transición viaja en su `OrdenEstadoCambiado`; este evento entrega el dato fiscal.

| Campo de `data` | Tipo | Obligatorio | Notas |
|---|---|---|---|
| `ordenId` | string | sí | |
| `numeroOrden` | string | sí | |
| `tiendaDestinoId` | string \| null | sí | Para que el ERP ubique la venta dentro del lote. |
| `documentoFiscalId` | string | sí | Identificador interno del documento. |
| `tipoDocumento` | string | sí | Enum `TipoDocumentoFiscal`. |
| `serie` | string | sí | Serie asignada. |
| `numeroDocumento` | string | sí | Número correlativo del documento. |
| `numeroAutorizacion` | string | sí | Autorización devuelta por la SAT. |
| `moneda` | string | sí | Enum `Moneda`. |
| `total` | string | sí | Decimal como cadena. Debe cuadrar con el `total` del evento 8. |
| `certificadaEn` | string | sí | ISO 8601 UTC, momento de la respuesta de la SAT. |

```json
{
  "ordenId": "ORD-2026-000123",
  "numeroOrden": "000123",
  "tiendaDestinoId": "TDA-014",
  "documentoFiscalId": "DOC-2026-005510",
  "tipoDocumento": "FACTURA",
  "serie": "A",
  "numeroDocumento": "005510",
  "numeroAutorizacion": "4F2A-9C81-7B03-D65E",
  "moneda": "GTQ",
  "total": "1400.00",
  "certificadaEn": "2026-09-07T14:39:12.400Z"
}
```

---

## 8. Matriz de consumo

| Consumidor | Dueño | HU | Eventos | Para qué |
|---|---|---|---|---|
| Auditoría | Julio | HU-006 | Los doce, por binding `#` | Bitácora append-only indexada por `transactionId` (EV-30, EAC-03). |
| Consulta de Estado | Jonathan | HU-010 | 1, 2, 8, 9, 10, 11, 12 | Vista materializada en Redis y vista 360 (ADR-004, EAC-02, CAR-07). Estado solo desde 1 y 2. |
| Notificaciones | Jonathan | HU-015 | 2 | Aviso al cliente por cambio de estado (EV-34, RES-06). Fuente única. |
| Inventario | Jose | HU-001, HU-002, HU-014 | 3, 4, 11 | Reservar, liberar por compensación y reingresar por devolución. |
| Despacho | Jose | HU-011, HU-012, HU-020 | 7, 9, 10 | Origen, guía, reasignación, recogida en tienda y cancelación de despacho. |
| Pagos | Angel | HU-004, HU-014 | 5, 11 | Intento de cobro y reembolso. |
| Facturación | Angel | HU-005, HU-014 | 6, 11 | Certificación SAT y nota de crédito. |
| Integración POS | Francisco | HU-009 | 8, 9 | Propagación idempotente a la instancia de tienda (EV-16, ADR-012). |
| Integración ERP | Francisco | HU-013, HU-022 | 8, 9, 11, 12 | Consolidación del lote de 30 minutos, exclusión de ventas canceladas, folio fiscal y conciliación (EV-15, ADR-008). |

**Qué no está aquí y por qué.** Auth no consume eventos de Órdenes. El carrito unificado (HU-007) vive en Redis y no genera eventos de este contrato.

---

## 9. Congelamiento

Una vez congelado, cualquier cambio se avisa al grupo antes de hacerlo, sin excepción.

| Revisor | Contexto | Estado | Fecha |
|---|---|---|---|
| Angel Martínez (202300410) | Órdenes, Pagos, Facturación | autor | 2026-09-12 |
| Julio Herrera | Auditoría, base técnica | objeciones 1 a 6 incorporadas, pendiente de firma | |
| Jonathan Jerónimo (202300353) | Consulta de Estado, Notificaciones | objeciones 1 a 8 incorporadas, pendiente de firma | |
| Francisco Castillo | Integración POS, Integración ERP | objeciones 9 y 10 incorporadas, pendiente de firma | |
| Jose Pirir | Inventario, Despacho | pendiente | |

---

## 10. Supuestos abiertos

Ninguno bloquea el congelamiento. Los supuestos 1 y 2 de la v1 quedaron cerrados.

1. **Mapeo de `tiendaDestinoId`.** Órdenes lo emite; la tabla tienda a instancia de POS es propiedad de Integración POS (HU-009). Se asume que se resuelve del lado receptor.
2. **TTL de reserva.** Se emite como parámetro en el evento 3; el valor definitivo lo fija Inventario (HU-001).
3. **Precios del Catálogo Comercial.** Viajan congelados al momento de la compra. El Catálogo es dependencia externa fuera del alcance (CUN-08) y no es fuente de verdad después de creada la orden.
4. **Ventana de exclusión del lote ERP.** El binding a `OrdenCancelada` permite excluir la venta antes de consolidar, pero una cancelación que llegue **después** de enviado el lote sigue siendo trabajo de la conciliación (HU-022). Ese caso no se puede prevenir por topología.
5. **`reservaId` sigue siendo un valor de relleno; `pagoId` y el CUI del receptor ya no lo son.** Órdenes todavía no consume las respuestas de Inventario, así que `reservaId` (eventos 4 y 7) y `tiendaDestinoId` se siguen rellenando con un valor derivado del identificador de la orden (`RSV-PENDIENTE-<id>`) o con `null`, marcados `PENDIENTE` en el código hasta que exista el consumidor de Inventario. `pagoId` (eventos 8 y 9) dejó de ser placeholder: se toma de `Orden.PagoId`, que `ManejadorEventosPagos` persiste al consumir `PagoAplicado`. El CUI del receptor (evento 6) también dejó de ser placeholder: viene de `Orden.ReceptorCui`, capturado desde la creación de la orden (`"CF"` por defecto si el cliente no da uno).
