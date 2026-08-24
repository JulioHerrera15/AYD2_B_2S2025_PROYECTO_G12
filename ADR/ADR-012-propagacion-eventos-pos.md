# ADR-012: Propagación confiable de eventos al POS con reintentos e idempotencia

**Estado:** Aceptada
**Responsable:** Francisco Gerardo Castillo Sagastume (202300653)
**Fecha:** 2026-08-24

## Contexto

Las 32 instancias de POS no forman parte de un programa de reemplazo (EV-16, ratificado por la Junta en EV-40) y deben recibir eventos desde SGO-Logic con manejo adecuado de errores y reintentos para garantizar la confiabilidad del proceso. El POS es consumidor de eventos, nunca fuente de datos hacia el sistema (RES-03), y puede estar intermitentemente inaccesible por tienda.

## Decisión

El contexto de Integración POS consume los eventos de venta publicados por Órdenes vía RabbitMQ (patrón pub/sub) y los envía a la instancia de POS correspondiente con un identificador único por evento que permite al POS ignorar duplicados (idempotencia); ante falta de respuesta, el mensaje va a dead-letter exchange y se reintenta con backoff (ver `RES-03` en `DDA_07_Drivers_de_Restriccion_RES.md` y `CU-09` en `DDA_04_Drivers_Funcionales_CDU.md`).

## Alternativas consideradas

[PENDIENTE-ANGEL: alternativas evaluadas para esta decisión]

## Consecuencias

### Positivas
- Ninguna venta se pierde ni se duplica en el POS aunque una tienda esté temporalmente sin conexión.
- El diseño no exige que el POS exponga ninguna API propia ni consulte inventario central, respetando la restricción de no modificarlo (RES-03).

### Negativas
- La reconciliación de una tienda que estuvo fuera de línea depende de que el mecanismo de reintento eventualmente la alcance; no hay una garantía de tiempo máximo declarada para ese escenario.
- El manejo correcto de duplicados recae también del lado del POS, lo cual está fuera del control directo de SGO-Logic.

## Preocupaciones y evidencia que atiende

PRE-11. Evidencia: EV-16.
