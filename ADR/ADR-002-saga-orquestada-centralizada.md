# ADR-002: Saga orquestada centralizada para el ciclo de vida de la orden

**Estado:** Aceptada
**Responsable:** Angel Martinez (202300410)
**Fecha:** 2026-08-24

## Contexto

Confirmar una orden requiere coordinar reserva de inventario, cobro, certificación fiscal y despacho — pasos que pertenecen a contextos delimitados distintos (ADR-001) y que pueden fallar de forma independiente entre sí (pago rechazado, SAT lenta, LogiExpress con timeout). La organización exige explícitamente no perder la orden si el proveedor de pago falla, poder procesar pagos sin depender por completo del proveedor externo, y que cada intento fallido sea auditable (EV-28). Además, el método de entrega puede modificarse incluso después de confirmada la compra (EV-31), lo que implica que la orden es un agregado de larga vida y mutable, no una transacción de un solo paso.

## Decisión

El contexto de Órdenes actúa como orquestador centralizado de una saga (máquina de estados) que coordina de forma explícita las llamadas a Inventario, Pagos, Facturación y Despacho, decide las compensaciones ante fallas (por ejemplo, liberar la reserva de inventario si el cobro falla) y es el único contexto que conoce la secuencia completa del proceso; el resto de contextos solo conoce su propio paso (ver `CU-02` en `DDA_04_Drivers_Funcionales_CDU.md`).

## Alternativas consideradas

[PENDIENTE-ANGEL: alternativas evaluadas para esta decisión]

## Consecuencias

### Positivas
- Concentra en un único lugar la lógica de compensación y el estado de negocio de la orden, lo que simplifica razonar sobre en qué estado quedó una compra ante cualquier falla parcial (soporta PRE-02).
- Permite exponer al cliente estados intermedios explícitos ("Confirmada - Pendiente Fiscal", "Pago Pendiente") en vez de bloquear la experiencia mientras se resuelve una dependencia externa lenta.
- Da un punto único de instrumentación para auditar cada transición de la orden (PRE-05).

### Negativas
- Órdenes se convierte en un punto de alto acoplamiento lógico con el resto de los contextos; su disponibilidad y correctitud son críticas para todo el flujo de venta.
- La máquina de estados debe mantenerse y probarse exhaustivamente conforme crecen los flujos alternativos (modificación post-confirmación, compensaciones parciales, reintentos), lo que incrementa su complejidad de prueba respecto a un flujo lineal.

## Preocupaciones y evidencia que atiende

PRE-02, PRE-05, PRE-06, PRE-13; conflictos CON-01, CON-02, CON-03. Evidencia: EV-04, EV-26, EV-28, EV-29, EV-31.
