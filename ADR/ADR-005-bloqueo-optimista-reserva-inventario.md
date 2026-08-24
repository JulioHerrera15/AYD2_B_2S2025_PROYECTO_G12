# ADR-005: Bloqueo optimista con columna de versión + Redis TTL para reserva de inventario

**Estado:** Aceptada
**Responsable:** Angel Martinez (202300410)
**Fecha:** 2026-08-24

## Contexto

Antes de confirmar una venta debe verificarse la disponibilidad del producto, evitando que una misma unidad se comprometa a más de un cliente (EV-22) — el único punto del caso que exige consistencia fuerte. Cuando una compra no concluye, el inventario reservado debe liberarse en un tiempo acorde con la operación comercial (EV-23). Este mecanismo debe sostenerse incluso bajo picos de hasta 8 veces la demanda normal (EV-27), con muchos clientes intentando reservar el mismo producto de forma simultánea.

## Decisión

La reserva de inventario usa bloqueo optimista mediante una columna de versión (`version_column`) sobre el registro de existencias en SQL Operacional: el conflicto se detecta al escribir, sin bloquear el registro por adelantado. Cada reserva se crea con una marca de expiración (TTL) gestionada junto a Redis, y un proceso automático libera las reservas vencidas devolviendo las unidades al inventario disponible (ver `CU-01` y `CU-06` en `DDA_04_Drivers_Funcionales_CDU.md`).

## Alternativas consideradas

[PENDIENTE-ANGEL: alternativas evaluadas para esta decisión]

## Consecuencias

### Positivas
- No requiere bloqueos pesimistas de larga duración sobre el inventario, lo que favorece el throughput durante los picos de demanda declarados por el cliente.
- La expiración automática por TTL evita reservas huérfanas sin necesitar intervención manual.

### Negativas
- Bajo alta contención (por ejemplo, muchos clientes compitiendo por el mismo producto en Black Friday) puede generar reintentos frecuentes por conflicto de versión, que el contexto solicitante (Órdenes) debe manejar explícitamente.
- El mecanismo de expiración por TTL depende de la disponibilidad de Redis y/o del temporizador del sistema; una falla ahí retrasa la liberación de unidades reservadas.

## Preocupaciones y evidencia que atiende

PRE-09, PRE-10. Evidencia: EV-22, EV-23.
