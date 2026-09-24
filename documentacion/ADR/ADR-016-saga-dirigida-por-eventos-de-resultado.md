# ADR-016: La saga de Órdenes avanza por eventos de resultado de Inventario, Pagos, Facturación y Despacho, sin transiciones manuales

**Estado:** Aceptada
**Responsable:** Angel Martinez (202300410)
**Fecha:** 2026-09-19

## Contexto

ADR-002 fijó a Órdenes como orquestador centralizado de la saga y dijo que coordina las llamadas a Inventario, Pagos, Facturación y Despacho, sin precisar el mecanismo. ADR-003 y ADR-014 establecieron después que la comunicación entre contextos ocurre por eventos publicados a través del Outbox. Faltaba dejar explícito cómo se conjugan ambas decisiones: cómo Órdenes se entera del resultado de cada paso y qué hace con esa información.

Durante la construcción del MVP quedaron dos tramos del ciclo de la orden que no avanzaban por sí solos. El paso de `PAGO_APLICADO` a `CONFIRMADA_PENDIENTE_FISCAL` y el de `CONFIRMADA` a `DESPACHO_SOLICITADO` solo ocurrían si un operador invocaba de forma manual `POST /api/ordenes/{id}/transiciones`, un endpoint concebido para recorrer la máquina de estados en demostraciones. Además, Órdenes no consumía los estados de entrega que publica Despacho, de modo que ninguna orden llegaba a `EN_TRANSITO` ni a `ENTREGADA`. Una compra confirmada y cobrada no completaba su ciclo sin intervención humana, lo que contradice la expectativa de que la orden nunca quede detenida por un paso interno del sistema (EV-28, PRE-02).

## Decisión

Órdenes avanza el ciclo de vida de la orden exclusivamente en respuesta a eventos de resultado publicados por los contextos participantes, y emite sus propias solicitudes como eventos por Outbox. La decisión comprende siete reglas:

1. **Una cola propia por contexto de origen.** Órdenes declara `ordenes.inventario` (reserva confirmada, rechazada o expirada), `ordenes.pagos` (pago aplicado, rechazado o sin respuesta, y resultados de reembolso), `ordenes.facturacion` (documento certificado o certificación fallida) y `ordenes.despacho` (estado de entrega actualizado y pedido listo para recoger). Cada consumidor atiende varios tipos de evento por cola y despacha según el `eventType`.
2. **La máquina de estados sigue siendo la única autoridad.** Cada evento se traduce a un estado de destino y toda transición se valida contra `MaquinaEstadosOrden` antes de aplicarse. No se agregaron estados ni transiciones nuevas a la máquina.
3. **Transiciones encadenadas en una sola transacción.** Al aplicarse un pago, la orden pasa a `PAGO_APLICADO` y, en la misma transacción, a `CONFIRMADA_PENDIENTE_FISCAL`, lo que publica `OrdenConfirmada` y la solicitud de certificación a Facturación. Al certificarse el documento, la orden pasa a `CONFIRMADA` y, en la misma transacción, a `DESPACHO_SOLICITADO`, lo que publica `OrdenCertificadaFiscalmente` y `DespachoSolicitado`. El despacho se solicita después de `CONFIRMADA`, no antes: la máquina de estados no permite `DESPACHO_SOLICITADO` desde `CONFIRMADA_PENDIENTE_FISCAL`. Un pago que no se aplicó no encadena nada: una orden sin pago no se factura.
4. **Tolerancia a eventos fuera de orden.** Un estado que la orden ya superó se ignora sin error. Un estado adelantado aplica los estados intermedios en la misma transacción; por ejemplo, `ENTREGADA` con la orden en `DESPACHO_SOLICITADO` recorre `EN_PREPARACION`, la etapa de salida que corresponde al método de entrega (`EN_TRANSITO` a domicilio, `LISTA_PARA_RECOGER` en recogida en tienda) y `ENTREGADA`.
5. **Estados de Despacho sin equivalente en la orden.** Origen sin determinar, guía solicitada, asignada o sin emitir, recogida en tienda, incidencia y anulación no mueven la orden; se registran en el log.
6. **Política de errores y deduplicación.** Una transición inválida o un evento desconocido se confirman sin reencolar. Un fallo transitorio se rechaza con reencolado para que actúen el límite de entregas y la dead letter (ADR-018). La deduplicación por `eventId` se registra en `ordenes.mensaje_procesado`, dentro del mismo `SaveChanges` que las transiciones y las filas de Outbox, de modo que quedan todas o ninguna.
7. **Sin identificadores inventados.** Si la orden se cancela o se rechaza antes de que exista una reserva, no se publica `LiberacionReservaSolicitada`, porque no hay un `reservaId` real que liberar; cualquier reserva que llegue a crearse expira por TTL (ADR-005).

El endpoint `POST /api/ordenes/{id}/transiciones`, concebido en el MVP para recorrer la máquina de estados a mano, se eliminó de Órdenes el 2026-09-21: ningún script, prueba ni pantalla lo usa y contradice esta decisión, porque permite mover la orden sin que ningún evento de resultado la respalde. La máquina de estados sigue validando toda transición que aplican los consumidores.

## Alternativas consideradas

Se evaluaron cinco opciones: (1) orquestación por eventos de resultado, con cola propia por contexto de origen y transiciones encadenadas atómicas; (2) orquestación por llamadas HTTP síncronas de Órdenes a cada contexto, descartada porque acopla la disponibilidad de la venta a la de todos los participantes y contradice ADR-003 y ADR-014; (3) coreografía pura, descartada desde ADR-002 porque impide el rastreo centralizado que exige Finanzas; (4) mantener las transiciones manuales, descartada porque una orden cobrada no completa su ciclo sin operador; y (5) una única cola para todas las fuentes, descartada porque mezcla en una misma política de reintentos y dead letter a contextos de criticidad distinta y permite que los mensajes problemáticos de uno demoren a los demás. Dentro de la opción elegida se descartó además aplicar cada transición encadenada en una transacción propia, porque abre una ventana en la que un fallo del proceso deja a la orden en un estado intermedio sin ningún evento por procesar que la mueva.

## Consecuencias

### Positivas
- La orden recorre el ciclo completo desde la creación hasta la entrega sin intervención humana; los estados de entrega los informa el transportista a través de Despacho.
- Las transiciones encadenadas son atómicas: no existe un estado intermedio observable entre el pago y la solicitud fiscal, ni entre la certificación y la solicitud de despacho.
- El procesamiento es idempotente y tolera reentregas y desorden entre eventos, en línea con ADR-003 y con el modelo de entrega al menos una vez del broker.
- Órdenes sigue siendo el único contexto que conoce la secuencia completa de la saga (ADR-002); Inventario, Pagos, Facturación y Despacho solo conocen su propio paso.

### Negativas
- Cada salto entre contextos atraviesa un ciclo de polling del Outbox (2 segundos por defecto, ADR-014), por lo que el ciclo completo acumula esa latencia varias veces y el estado de la orden puede ir por detrás del estado interno del contexto de origen.
- Los cuatro consumidores repiten un mismo patrón de consumo. La clase base común no se usa porque está tipada a un único tipo de dato por cola, así que la alineación entre los cuatro depende de revisión conjunta.
- Una orden cancelada antes de reservar depende de la expiración por TTL para liberar cualquier reserva tardía, con el tiempo de espera que esa expiración implica.
- Una orden detenida por una falla ajena a la saga no cuenta con un endpoint de recuperación: requiere la intervención directa de un operador sobre la base de datos de Órdenes.

## Preocupaciones y evidencia que atiende

PRE-02, PRE-05, PRE-06, PRE-13, PRE-15; conflicto CON-02. Evidencia: EV-26, EV-28, EV-29, EV-31, EV-32.
