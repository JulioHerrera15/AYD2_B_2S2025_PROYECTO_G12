# ADR-003: Patrón Outbox con polling publisher para publicación confiable de eventos

**Estado:** Aceptada
**Responsable:** Angel Martinez (202300410)
**Fecha:** 2026-08-24

## Contexto

Cada contexto de escritura (Órdenes, Pagos, Inventario, Facturación) necesita publicar eventos hacia RabbitMQ para que otros contextos los consuman (Consulta de Estado, Auditoría, Integración POS, Notificaciones), sin perder eventos si RabbitMQ está temporalmente inaccesible y sin duplicar los que sí se publicaron. Las 32 instancias de POS, en particular, deben recibir eventos con manejo adecuado de errores y reintentos para garantizar confiabilidad (EV-16), y la organización no puede permitirse perder una orden por una falla de publicación (EV-28).

## Decisión

Cada contexto de escritura persiste el evento pendiente en una tabla outbox dentro de su propia base de datos, en la misma transacción de negocio que el cambio de datos correspondiente. Un Publicador dedicado consulta periódicamente (polling) esas tablas outbox y publica los eventos en RabbitMQ, marcándolos como publicados solo después de recibir confirmación del broker (ver `CU-08` en `DDA_04_Drivers_Funcionales_CDU.md`, y su aplicación concreta al servicio de Pagos en `DDA_06_Despliegue.md`).

## Alternativas consideradas

[PENDIENTE-ANGEL: alternativas evaluadas para esta decisión]

## Consecuencias

### Positivas
- Ningún evento se pierde aunque RabbitMQ no esté disponible en el instante de la escritura: la publicación nunca es directa ni bloqueante respecto a la transacción de negocio.
- Evita el problema clásico de "escritura dual" (escribir en la base de datos y publicar en la cola sin garantía atómica entre ambas operaciones).

### Negativas
- Introduce latencia entre el cambio de datos y la publicación efectiva del evento, dependiente del intervalo de polling del Publicador.
- El Publicador es un componente adicional que operar, escalar y monitorear.
- Exige disciplina en cada contexto de escritura para no publicar eventos por una vía distinta a este mecanismo, así como aplicar el mismo patrón de forma consistente en todos los contextos que lo requieran.

## Preocupaciones y evidencia que atiende

PRE-11, PRE-02. Evidencia: EV-16, EV-28.
