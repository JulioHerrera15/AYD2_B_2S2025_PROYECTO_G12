# ADR-022: La compensación de stock es explícita: Inventario libera la reserva por evento y reingresa las devoluciones

**Estado:** Aceptada. Reemplaza a ADR-021.
**Responsable:** Angel Martinez (202300410)
**Fecha:** 2026-09-23

## Contexto

ADR-021 cerró el MVP con la compensación de stock apoyada solo en la expiración por TTL: Inventario no consumía `LiberacionReservaSolicitada` ni `DevolucionRegistrada`, una reserva de una orden cancelada volvía al disponible hasta 900 segundos después y una devolución no reingresaba stock sin un ajuste manual. La regla 3 de ese ADR dejó prevista la salida: incorporar los consumidores como un cambio aditivo de `inventario-eventos-v1` (v1.1), sin tocar los eventos de Órdenes.

Esa salida se implementó en Inventario con HU-003 (liberación de la reserva por compensación de la saga) y HU-014 (reingreso de las unidades devueltas), integradas a `develop` en el merge `fa9b8e8` a partir del commit `7aa8378` del responsable de Inventario (202300335). Con ese cambio, lo que afirma ADR-021 deja de describir el código.

Órdenes no cambió. Sigue publicando los dos eventos de compensación según `ordenes-eventos-v1`:

- `LiberacionReservaSolicitada` (routing key `ordenes.reserva.liberacion-solicitada`, evento 4), desde `EmitirLiberacionReservaSolicitada` en `SGO-Logic/src/Services/SgoLogic.Ordenes.Api/Application/Ordenes/Eventos/MapaEventosSagaOrden.cs:264`. Solo se emite si la orden tiene `ReservaId`; sin él, la orden se canceló antes de `InventarioReservado` y no se publica nada. El `data` lleva `ordenId`, `reservaId`, `motivo` (`MotivoLiberacionReserva`: el motivo explícito del evento de origen o, en su defecto, el derivado del estado destino) y `solicitadaEn`.
- `DevolucionRegistrada` (routing key `ordenes.devolucion.registrada`, evento 11), desde `ServicioDevolucion` en `SGO-Logic/src/Services/SgoLogic.Ordenes.Api/Application/Ordenes/ServicioDevolucion.cs:152`. Cada línea lleva, desde la v1.3 de ese contrato, `ubicacionOrigenReservaId` y `reservaId`, que Órdenes llena con la ubicación y la reserva de la orden original.

## Decisión

La compensación de stock deja de depender del TTL: Inventario consume los dos eventos de compensación de Órdenes, libera la reserva en el momento en que se solicita y reingresa a la cantidad física las unidades devueltas. La expiración por TTL se mantiene como red de seguridad. La decisión comprende cinco reglas:

1. **Una sola cola con tres bindings.** La cola `inventario.ordenes` conserva su nombre y queda ligada a `ordenes.reserva.solicitada`, `ordenes.reserva.liberacion-solicitada` y `ordenes.devolucion.registrada` (`OrdenesEventosConsumidor.cs`, líneas 42 a 46). `OrdenesEventosConsumidor` reemplaza a `ReservaSolicitadaConsumidor`, que heredaba de `EventConsumerBase` y solo admitía un tipo de `data`. El consumidor sigue el patrón de colas multi-evento que ya usa Órdenes con `PagosEventosConsumidor`: despacha por `eventType`, deduplica por `eventId` sobre `mensajes_procesados` en el mismo `SaveChanges` que el efecto de negocio y propaga `transactionId` y `causationId`. Un sobre ilegible y una falla transitoria se rechazan con reencolado, y el sobre ilegible termina en la dead letter al agotar `x-delivery-limit`; un `eventType` no esperado se confirma sin aplicarse.
2. **Liberación explícita (HU-003).** Ante `LiberacionReservaSolicitada`, `ServicioLiberacionReservas` busca la reserva por `ordenId` (una orden tiene una sola reserva). Si la reserva está `ACTIVA`, devuelve cada línea al disponible con `Existencia.LiberarReserva`, la pasa a `LIBERADA` con `Reserva.Liberar()`, incrementa su `sequence` y publica por Outbox `ReservaInventarioLiberada` (`inventario.reserva.liberada`) con el motivo recibido. Si la orden no tiene reserva, o si la reserva ya no está `ACTIVA` porque venció o porque el mensaje llegó repetido, no cambia nada ni publica un evento.
3. **Reingreso por devolución (HU-014).** Ante `DevolucionRegistrada`, `ServicioReingresoStock` suma a la cantidad física cada línea devuelta, en la ubicación `ubicacionOrigenReservaId` de la línea o, si viene vacía, en la `ubicacionReingresoId` del encabezado. Si el SKU no tiene existencia en esa ubicación, la crea con cantidad cero y tipo `TIENDA` antes de sumar. Cada línea deja un asiento en el ledger de movimientos con motivo `REINGRESO_DEVOLUCION`, y al final se publica por Outbox `StockReingresado` (`inventario.stock.reingresado`, `aggregateType` `Devolucion`). Las líneas con cantidad no positiva o sin ubicación se omiten; si no se reingresa ninguna, no se publica el evento.
4. **El TTL se conserva.** `ExpiracionReservasWorker` y la regla de expiración de HU-002 no cambian. Cubren la reserva que Inventario llega a crear después de que Órdenes canceló sin conocer el `reservaId`, caso en el que Órdenes no publica la solicitud de liberación.
5. **Cambio aditivo de contrato.** `inventario-eventos-v1` pasa a v1.1 con los dos eventos consumidos y los dos eventos publicados nuevos. Los eventos de `ordenes-eventos-v1` no se modifican.

## Alternativas consideradas

Se evaluaron tres opciones: (1) mantener ADR-021 y compensar solo por TTL, descartada porque su justificación era la falta de margen para implementar y validar los consumidores antes de la entrega, y los consumidores ya están integrados en `develop`, de modo que conservar la decisión dejaría la documentación contradiciendo el código; (2) consumir los eventos de compensación en Inventario sobre la cola existente, con liberación inmediata y reingreso automático (elegida); y (3) que Órdenes libere la reserva llamando por HTTP a Inventario, descartada por las mismas razones de ADR-021: agrega una llamada síncrona entre contextos que ADR-014 no admite y que ADR-017 solo acepta como excepción acotada para Despacho. Dentro de la opción elegida se prefirió ampliar los bindings de `inventario.ordenes` en lugar de declarar colas nuevas, porque la topología declarada no cambia de nombre y el patrón de consumidor multi-evento ya existía en el equipo. Se eligió la segunda opción porque cumple el contrato que Órdenes ya publicaba, reduce a cero la espera del TTL en la compensación de la saga y no introduce acoplamiento síncrono.

## Consecuencias

### Positivas
- Tras un pago rechazado o una cancelación con reserva conocida, las unidades vuelven al disponible cuando Inventario procesa el evento, sin esperar los 900 segundos del TTL. Desaparece la subestimación del disponible que ADR-021 aceptaba en un pico de rechazos de PayKlik.
- Una devolución reingresa sus unidades sin intervención manual y deja un asiento en el ledger de movimientos, de modo que el aumento de la cantidad física es trazable.
- `LiberacionReservaSolicitada` y `DevolucionRegistrada` tienen ahora un efecto observable: Auditoría recibe por su binding `#` tanto la solicitud como su resultado (`ReservaInventarioLiberada` o `StockReingresado`), y la bitácora ya no muestra una solicitud sin respuesta.
- La liberación es idempotente en dos niveles: por `eventId` en `mensajes_procesados` y por el estado de la reserva, que solo se libera si está `ACTIVA`. La expiración por TTL y la liberación explícita no pueden devolver dos veces las mismas unidades.

### Negativas
- La reserva sigue sin un estado de consumo: la de una orden pagada también vence por TTL y devuelve sus unidades al disponible, igual que con ADR-021. Esta decisión no cambia ese comportamiento.
- En el reingreso, la ubicación de cada línea (`ubicacionOrigenReservaId`) tiene prioridad sobre la `ubicacionReingresoId` del encabezado. Como Órdenes llena ese campo en todas las líneas con la ubicación de la reserva original, las unidades devueltas vuelven a esa ubicación aunque la devolución se registre en otra tienda; la del encabezado solo se usa cuando la línea llega vacía.
- Una existencia creada por el reingreso toma siempre el tipo `TIENDA`, aunque la ubicación sea un centro de distribución.
- El reingreso no tiene idempotencia de negocio por `devolucionId`: se apoya únicamente en la deduplicación por `eventId`. Un mismo `DevolucionRegistrada` republicado con otro `eventId` reingresaría las unidades dos veces.
- Ningún contexto de la saga consume todavía `inventario.reserva.liberada` ni `inventario.stock.reingresado`: la cola `ordenes.inventario` solo está ligada a `inventario.reserva.confirmada`, `.rechazada` y `.expirada`, y Consulta de Estado no se liga a eventos de Inventario. Solo Auditoría los recibe.
- La liberación toca la versión de la existencia (ADR-005) en concurrencia con reservas nuevas sobre el mismo SKU; ante un choque, el mensaje se reencola. La evidencia de esta situación bajo carga es PENDIENTE DE EVIDENCIA.

## Preocupaciones y evidencia que atiende

PRE-09, PRE-10. Evidencia: EV-22, EV-23, EAC-05, sección 14.9 del DDA. Pruebas unitarias en `SGO-Logic/tests/SgoLogic.Inventario.Tests/Application/ServicioLiberacionReservasTests.cs` y `SGO-Logic/tests/SgoLogic.Inventario.Tests/Application/ServicioReingresoStockTests.cs`; resultado de su ejecución y corrida de punta a punta de la compensación: PENDIENTE DE EVIDENCIA.
