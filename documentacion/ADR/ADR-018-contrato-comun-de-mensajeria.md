# ADR-018: Contrato común de mensajería: envelope único, topología pub/sub con colas quorum y bibliotecas compartidas

**Estado:** Aceptada
**Responsable:** Angel Martinez (202300410)
**Fecha:** 2026-09-19

## Contexto

Los contextos del backbone se comunican exclusivamente por eventos (ADR-001, ADR-003). ADR-003 fijó cómo se publica de forma confiable, mediante Outbox con polling, pero no definió el contrato común que todos los contextos comparten: el formato del mensaje, la topología del broker, la política de reintentos, la correlación entre eventos y la forma de compartir ese código sin romper el aislamiento de datos de ADR-001.

Auditoría debe reconstruir la evidencia completa de una transacción en menos de un segundo, indexada por identificador de transacción y sin transacciones sin respaldo (EAC-03, EV-30). Ese requisito no se cumple si cada contexto define su propio sobre y su propio nombre de correlación: la bitácora quedaría con vacíos. Además, el broker entrega cada mensaje al menos una vez y no garantiza el orden entre eventos, lo que obliga a que todos los consumidores apliquen las mismas reglas de idempotencia y de ordenamiento.

## Decisión

Se adopta un contrato de mensajería único para todos los contextos, con seis elementos:

1. **Envelope común.** Todo evento viaja en un sobre con `eventId`, `eventType`, `eventVersion`, `occurredAt`, `sequence`, `transactionId`, `causationId`, `aggregateType`, `aggregateId`, `source` y `data`. Cada contexto define únicamente el contenido de `data` en su propio contrato. Los montos viajan como cadena decimal, las fechas en ISO 8601 UTC, los identificadores como cadena y los valores de enumeración en MAYUSCULA_CON_GUION_BAJO.
2. **Reglas de consumo.** Toda publicación pasa por Outbox. Todo consumidor deduplica por `eventId`. El orden se decide por `sequence`, monotónica por `aggregateId`, con el criterio de que gana el último evento aplicado: un evento con secuencia menor o igual a la ya aplicada se descarta sin error.
3. **Correlación automática.** `transactionId` y `causationId` no se asignan a mano: un contexto ambiente los hereda del evento que se está procesando y los escribe en el evento derivado, de modo que toda una transacción de negocio comparte el mismo `transactionId` sin importar cuántos contextos atraviese.
4. **Topología del broker.** El exchange `topic` `sgo.eventos` transporta los eventos de dominio con routing key `<contexto>.<entidad>.<hecho-en-kebab-case>`. Los canales dedicados `sgo.sat` y `sgo.erp` son de tipo `direct`. El exchange `fanout` `sgo.eventos.dlx` concentra la dead letter. Cada consumidor declara su propia cola, siempre de tipo quorum, durable y con dead letter y límite de entregas (`x-delivery-limit`). La cola de descarte final, `sgo.eventos.dlq`, también es quorum y no expira mensajes.
5. **Reintentos en dos niveles.** El de publicación vive en el Outbox: backoff exponencial de `2^n` segundos con techo de 300 segundos y hasta 5 intentos, tras los cuales la fila pasa a la dead letter. El de consumo vive en el broker: el consumidor rechaza el mensaje con reencolado y, al superar el límite de entregas, la cola lo envía a la dead letter.
6. **Versionado y bibliotecas compartidas.** Un cambio aditivo (campo opcional o evento nuevo) incrementa la versión menor y no se renegocia. Un cambio rompiente exige `eventVersion: 2`, routing key con sufijo `.v2` y publicación simultánea de ambas versiones hasta que todos los consumidores migren. El código común vive en dos bibliotecas de la solución: `SgoLogic.Contracts` (sobre, eventos, enumeraciones y contexto de correlación) y `SgoLogic.BuildingBlocks` (Outbox, publicador, suscriptor, topología, consumidor base con idempotencia, comprobaciones de salud y métricas). Cada contexto las referencia como proyecto. El cliente de mensajería es RabbitMQ.Client, sin capa de abstracción de terceros.

## Alternativas consideradas

Se evaluaron cinco opciones: (1) contrato único con envelope común, colas quorum y bibliotecas compartidas (elegida); (2) que cada contexto defina su propio sobre y su propia correlación, descartada porque Auditoría no podría reconstruir una transacción completa y EAC-03 no se cumpliría; (3) colas clásicas, descartadas porque el límite de entregas solo existe en colas quorum y, en una cola clásica, un mensaje problemático se reintentaría indefinidamente; (4) reintento de publicación mediante TTL de mensaje en el broker, descartada porque las colas quorum no admiten TTL por mensaje, de modo que el backoff se resuelve del lado de la aplicación, en el Outbox; y (5) una biblioteca de mensajería de terceros sobre RabbitMQ, descartada por sumar una tecnología y una curva de aprendizaje al equipo (RES-12, CON-06) cuando el cliente oficial de RabbitMQ cubre lo necesario.

## Consecuencias

### Positivas
- Auditoría reconstruye una transacción completa con una sola clave de correlación, sin depender de cómo cada contexto haya nombrado sus campos (EAC-03).
- Un mensaje que no puede procesarse no se reintenta sin fin: tras el límite de entregas llega a la dead letter y queda disponible para revisión, sin bloquear a los demás mensajes.
- Los contratos evolucionan por adición sin coordinar despliegues entre contextos, y la regla de cambio rompiente protege a los consumidores existentes.
- Un servicio nuevo hereda el envelope, la correlación, el Outbox y la idempotencia sin reimplementarlos.

### Negativas
- Las bibliotecas compartidas acoplan la compilación de los contextos: un cambio en `SgoLogic.Contracts` o `SgoLogic.BuildingBlocks` se propaga a todos los que las referencian. La regla de cambios aditivos acota ese riesgo, pero no lo elimina.
- Los consumidores que atienden varios tipos de evento por cola no pueden usar el consumidor base, que está tipado a un único tipo de dato, y resuelven la deduplicación con su propio registro. La garantía es la misma, pero el mecanismo no es uno solo.
- El reintento de publicación agrega la latencia del ciclo de polling del Outbox a cada salto entre contextos, y la consistencia entre contextos es eventual (ADR-014).
- La dead letter no tiene consumidor automático: los mensajes que llegan a ella requieren revisión operativa.

## Preocupaciones y evidencia que atiende

PRE-01, PRE-02, PRE-05, PRE-07, PRE-11. Evidencia: EV-16, EV-28, EV-30.
