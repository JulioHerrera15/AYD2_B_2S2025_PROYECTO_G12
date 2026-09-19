# Contrato de eventos del contexto Despacho — v1

Eventos que publica `SgoLogic.Despacho.Api` (HU-011, HU-012, HU-019, HU-020).
Mismo envelope que el resto (`sgo-eventos-envelope-v1`), exchange `sgo.eventos`,
serializacion camelCase, fechas ISO-8601 UTC.

## 1. Catalogo

| # | Evento | Routing key | Cuando |
|---|---|---|---|
| 1 | `OrigenDespachoDeterminado` | `despacho.origen.determinado` | Siempre, al resolver desde donde sale el pedido (domicilio o recogida). |
| 2 | `GuiaAsignada` | `despacho.guia.asignada` | LogiExpress emitio la guia. |
| 3 | `GuiaPendiente` | `despacho.guia.pendiente` | LogiExpress no respondio. El despacho fisico continua igual. |
| 4 | `PreparacionSolicitada` | `despacho.preparacion.solicitada` | Junto con el origen: instruccion para el operador (HU-019). |
| 5 | `DespachoEstadoActualizado` | `despacho.estado.actualizado` | Cada cambio de estado de entrega (HU-012). |
| 6 | `PedidoListoParaRecoger` | `despacho.pedido.listo-para-recoger` | El pedido quedo listo en tienda (HU-020). |

## 2. Cola que consume Despacho

| Cola | Bindings |
|---|---|
| `despacho.ordenes` | `ordenes.despacho.solicitado`, `ordenes.orden.metodo-entrega-modificado`, `ordenes.orden.cancelada` |

Una sola cola con despacho por `eventType`, siguiendo `PagosEventosConsumidor` de
Ordenes. No usa `EventConsumerBase` porque esa clase esta tipada a un unico
`TData` y aqui llegan tres.

## 3. Quien deberia consumir lo de Despacho

| Cola | Responsable | Eventos | Estado |
|---|---|---|---|
| `ordenes.despacho` | Angel | `despacho.estado.actualizado` | **pendiente** — sin esto la orden no avanza a EN_TRANSITO/ENTREGADA |
| `consulta-estado.*` | Jonathan | `despacho.*` | **pendiente** — para la vista 360 |
| `notificaciones.*` | Jonathan | `despacho.estado.actualizado`, `despacho.pedido.listo-para-recoger` | **pendiente** — avisarle al cliente |
| Auditoria | Julio | todos | automatico, liga con `#` |

## 4. Decisiones de diseno

**El origen lo dice Inventario, no Ordenes.** Despacho llama a
`GET /api/inventario/reservas/{ordenId}` y toma de ahi la ubicacion, porque es
donde realmente se descontaron las unidades. La `ubicacionOrigenSugerida` del
evento solo se usa de respaldo si Inventario no reporta reserva. Reimplementar
aqui `SelectorUbicacionReserva` duplicaria una decision ya comprometida
fisicamente, y las dos copias derivarian.

**Sin circuit breaker, por razones opuestas en cada cliente.** A Inventario no se
le pone porque si esta caido no hay plan B: la excepcion sube y la cola reintenta.
A LogiExpress tampoco, porque su timeout es un resultado de negocio valido — una
sola llamada, timeout corto, y si falla se sigue con `GUIA_PENDIENTE`. Reintentar
solo retrasaria algo que de todos modos va a salir sin guia.

**Un despacho por orden**, con indice unico sobre `orden_id`. Es lo que hace
idempotente el consumo del evento.

## 5. Estados

`ORIGEN_PENDIENTE` → `RECOGIDA_TIENDA` | `GUIA_SOLICITADA` → `GUIA_ASIGNADA` |
`GUIA_PENDIENTE` → `EN_PREPARACION` → `EN_TRANSITO` | `LISTA_PARA_RECOGER` →
`ENTREGADA`. `INCIDENCIA` es alcanzable desde casi cualquier punto y se puede
resolver de vuelta. `ENTREGADA` y `ANULADA` son terminales: **un estado tardio del
transportista no revive un despacho cerrado**.

`ENTREGADA` tambien es alcanzable directo desde `GUIA_ASIGNADA` y
`GUIA_PENDIENTE`: un transportista puede reportar solo el estado final, y perder
esa entrega por rigidez seria peor que aceptar el salto.
