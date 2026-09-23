# ADR-021: En el MVP la compensación de stock ocurre por expiración del TTL de la reserva

**Estado:** Aceptada
**Responsable:** Angel Martinez (202300410)
**Fecha:** 2026-09-22

## Contexto

La saga de Órdenes compensa la reserva de inventario cuando la orden no se completa. Al cancelarse una orden que ya tiene reserva, por ejemplo tras un pago rechazado, Órdenes publica `LiberacionReservaSolicitada` (routing key `ordenes.reserva.liberacion-solicitada`, evento 4 de `ordenes-eventos-v1`) con el `reservaId` real. Al registrar una devolución publica `DevolucionRegistrada` (`ordenes.devolucion.registrada`, evento 11), con la ubicación de origen y la reserva de cada línea, para que el stock devuelto pueda reingresar.

Inventario no consume ninguno de los dos eventos. `SgoLogic.Inventario.Api` tiene un único consumidor, `ReservaSolicitadaConsumidor`, sobre la cola `inventario.ordenes`, ligada solo a `ordenes.reserva.solicitada`. Los eventos de liberación y de devolución llegan al exchange `sgo.eventos` y los reciben Auditoría y, en el caso de la devolución, Pagos y Facturación, pero ninguna cola de Inventario. El contrato `inventario-eventos-v1` deja fuera de la v1 el evento `inventario.reserva.liberada` (HU-003) y el reingreso por devolución (HU-014).

El único mecanismo que devuelve unidades al disponible es la expiración por TTL (HU-002): toda reserva nace `ACTIVA` con un vencimiento de 900 segundos por defecto (`Inventario:TtlSegundosPorDefecto`, entre 60 y 3600), y `ExpiracionReservasWorker` barre cada 5 segundos (`Inventario:ExpiracionPollingMs`) las reservas activas vencidas, las marca `EXPIRADA`, libera sus unidades y publica `ReservaInventarioExpirada`. Faltaba decidir si el MVP se cierra con ese mecanismo o si se bloquea hasta contar con la compensación explícita.

## Decisión

En el MVP la compensación de stock se apoya en la expiración por TTL y no en la liberación explícita. La decisión comprende cuatro reglas:

1. **Liberación tras un pago rechazado o una cancelación.** Órdenes sigue publicando `LiberacionReservaSolicitada` según su contrato, sin cambios. Las unidades de esa reserva vuelven al disponible cuando vence el TTL de 900 segundos, no en el momento de la cancelación.
2. **Devoluciones.** Una devolución registrada no reingresa stock de forma automática. El ajuste de la cantidad física se hace a mano, por la vía que ya existe para corregir existencias (`PUT /api/inventario/existencias`).
3. **Sin cambios de contrato.** Los eventos de Órdenes no se modifican. Cuando Inventario incorpore los consumidores, lo hará como un cambio aditivo de `inventario-eventos-v1` (v1.1), sin renegociar lo congelado.
4. **Límite visible.** La sección 14.9 del DDA registra este límite y remite a este ADR.

## Alternativas consideradas

Se evaluaron tres opciones: (1) cerrar el MVP con la liberación por TTL y dejar el reingreso por devolución como ajuste manual (elegida); (2) implementar en Inventario los consumidores de `ordenes.reserva.liberacion-solicitada` y `ordenes.devolucion.registrada`, con el evento `inventario.reserva.liberada`, descartada para el cierre del MVP porque Inventario es un contexto de otro responsable, el cambio exige nueva cola o bindings, idempotencia sobre `mensajes_procesados` y pruebas de concurrencia contra el bloqueo optimista (ADR-005), y no hay margen para validarlo antes de la entrega; y (3) que Órdenes libere la reserva llamando por HTTP a Inventario, descartada porque agrega una llamada síncrona entre contextos que ADR-014 no admite y que ADR-017 solo acepta como excepción acotada para Despacho. Se eligió la primera porque el TTL ya garantiza que ninguna reserva no concluida retenga stock de forma indefinida, que es lo que exige EAC-05, y porque no introduce código nuevo en un contexto ajeno al cierre.

## Consecuencias

### Positivas
- Ninguna unidad reservada queda retenida más allá del TTL: la liberación ocurre aunque Inventario no reciba la solicitud, y el barrido contra PostgreSQL no depende de que un mensaje llegue.
- El error es conservador: mientras la reserva no vence, las unidades se ven como no disponibles, lo que puede rechazar una venta posible pero nunca compromete una unidad a dos clientes.
- Los contratos no cambian y la saga de Órdenes no depende de la respuesta de Inventario para cancelar.

### Negativas
- Tras un pago rechazado o una cancelación, las unidades quedan fuera del disponible hasta 900 segundos. En un pico como Black Friday, con órdenes rechazadas por PayKlik, el disponible se subestima durante ese lapso y se pueden perder ventas de artículos con poco stock.
- Una devolución no aumenta el disponible hasta que alguien ajusta la existencia. El producto devuelto no se puede vender en línea mientras tanto.
- La reserva no tiene un estado de consumo: la de una orden pagada también vence por TTL y devuelve sus unidades al disponible, y la cantidad física solo cambia por un ajuste explícito. Entre el vencimiento y ese ajuste, el disponible puede sobrestimarse.
- `LiberacionReservaSolicitada` y `DevolucionRegistrada` se publican y se auditan, pero no tienen efecto en Inventario. Quien lea la bitácora de Auditoría no debe interpretar la solicitud como una liberación efectiva.

### Cómo se resolvería después
Inventario agregaría a la cola `inventario.ordenes` los bindings `ordenes.reserva.liberacion-solicitada` y `ordenes.devolucion.registrada`, liberaría la reserva con `Reserva.Liberar()` y `Existencia.LiberarReserva` (el estado `LIBERADA` ya existe) y publicaría `inventario.reserva.liberada`; para las devoluciones, reingresaría la cantidad en la ubicación de origen de cada línea y publicaría `inventario.existencia.ajustada`. Ambos eventos serían aditivos en `inventario-eventos-v1` v1.1. Al consumir la confirmación del pago, Inventario también podría marcar la reserva como consumida para que no vuelva al disponible por TTL.

## Preocupaciones y evidencia que atiende

PRE-09, PRE-10. Evidencia: EV-22, EV-23, EAC-05, sección 14.9 del DDA.
