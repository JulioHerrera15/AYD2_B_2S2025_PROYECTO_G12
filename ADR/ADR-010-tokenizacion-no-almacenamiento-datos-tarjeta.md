# ADR-010: Tokenización y no almacenamiento de datos de tarjeta

**Estado:** Aceptada
**Responsable:** Francisco Gerardo Castillo Sagastume (202300653)
**Fecha:** 2026-08-24

## Contexto

Por políticas corporativas y de cumplimiento, MegaStore no puede almacenar números completos de tarjeta ni códigos CVV bajo ninguna circunstancia (EV-25). Al mismo tiempo, la organización busca no perder la orden si el proveedor de pago falla, poder procesar pagos sin depender totalmente de un único proveedor externo, y que cada intento fallido pueda auditarse (EV-28) — pero esa evidencia, combinada con la anterior, no autoriza construir un procesador de tarjetas propio.

## Decisión

SGO-Logic delega la tokenización de los datos de tarjeta a PayKlik; el contexto de Pagos únicamente almacena el resultado y los metadatos de cada intento (orden, canal de origen, fecha y resultado), nunca el número de tarjeta ni el CVV (ver `RES-08` en `DDA_07_Drivers_de_Restriccion_RES.md` y `CU-03` en `DDA_04_Drivers_Funcionales_CDU.md`).

## Alternativas consideradas

[PENDIENTE-ANGEL: alternativas evaluadas para esta decisión]

## Consecuencias

### Positivas
- Elimina el alcance de cumplimiento de construir y mantener un procesador de tarjetas propio.
- Resuelve el conflicto entre independencia del proveedor de pagos y la prohibición de almacenar datos sensibles (CON-03) sin violar la restricción regulatoria.

### Negativas
- La resiliencia de pagos queda limitada a rutas alternativas dentro del mismo proveedor (reintento del mismo medio, enlace de reintento) en vez de poder alternar libremente entre distintos procesadores de tarjeta, porque no existe un segundo procesador propio.
- El nivel real de independencia frente a PayKlik está acotado por esta misma restricción de datos.

## Preocupaciones y evidencia que atiende

PRE-08, PRE-02. Evidencia: EV-25, EV-28.
