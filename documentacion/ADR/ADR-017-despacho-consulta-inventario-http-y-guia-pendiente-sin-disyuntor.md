# ADR-017: Despacho consulta a Inventario por HTTP síncrono y trata la guía de LogiExpress como degradación, sin circuit breaker

**Estado:** Aceptada
**Responsable:** Angel Martinez (202300410)
**Fecha:** 2026-09-19

## Contexto

ADR-014 declaró que el desacople exigido al sistema se cubre con el patrón Outbox (ADR-003) y el disyuntor con backoff (ADR-009), y afirmó como consecuencia que ningún contexto del backbone invoca a otro por HTTP síncrono. ADR-009, por su parte, estableció que los adaptadores hacia PayKlik y LogiExpress aplican reintentos con backoff y que el de LogiExpress incorpora un circuit breaker.

Al construir el contexto de Despacho (HU-011, HU-012, HU-019, HU-020) aparecieron dos casos en los que aplicar esas decisiones al pie de la letra produce un resultado peor que apartarse de ellas de forma acotada:

- **Origen del despacho.** Despacho debe saber desde qué ubicación se descontaron realmente las unidades. Esa información pertenece a Inventario, que la fija al reservar (ADR-005). Reproducir en Despacho la selección de ubicación, o mantener allí una copia del dato, duplicaría una decisión ya comprometida físicamente y ambas copias terminarían divergiendo.
- **Guía de LogiExpress.** El criterio de aceptación de HU-011 establece que, ante un timeout del proveedor, cerca de una de cada 50 solicitudes (EV-18), el despacho físico continúa igual y la guía se resuelve después. El timeout es un resultado de negocio tolerado, no un error que deba abortar el proceso.

## Decisión

Se aceptan dos excepciones acotadas, que precisan ADR-014 y ADR-009 sin modificarlos:

1. **Consulta síncrona de lectura a Inventario.** Al determinar el origen de un despacho a domicilio, Despacho invoca `GET /api/inventario/reservas/{ordenId}` con un tiempo máximo de 3 segundos (`Despacho:InventarioTimeoutMs`). Es la única llamada HTTP síncrona entre contextos propios del backbone; es de solo lectura y no ocurre en la ruta del cobro. Sin disyuntor: si Inventario no responde, la excepción sube al consumidor, que rechaza el mensaje con reencolado, y la cola lo reintenta hasta el límite de entregas, tras el cual pasa a la dead letter (ADR-018). Una respuesta 404 no es un fallo: indica que Inventario no tiene reserva para esa orden y Despacho usa entonces la ubicación sugerida que trae el evento `DespachoSolicitado`; si tampoco viene, el mensaje se reintenta. En recogida en tienda no se consulta: el origen es la tienda elegida por el cliente.
2. **Guía de LogiExpress sin circuit breaker.** Despacho hace una sola llamada, con un tiempo máximo de 3 segundos (`Despacho:LogiExpressTimeoutMs`), sin reintentos y sin disyuntor. Si el proveedor no responde o responde sin emitir la guía, el despacho pasa a `GUIA_PENDIENTE`, se publica `GuiaPendiente` y la preparación y la salida del pedido continúan. La degradación es visible en el estado del despacho y en el evento.
3. **Anulación de guías.** Al cambiar el método de entrega o cancelarse la orden, Despacho solicita la anulación de la guía emitida; si el proveedor no la acepta o no responde, el cambio se aplica igual.

## Alternativas consideradas

Se evaluaron seis opciones: (1) consulta HTTP de lectura a Inventario y guía de LogiExpress con degradación a `GUIA_PENDIENTE` (elegida); (2) tomar la ubicación únicamente del evento `DespachoSolicitado`, descartada como fuente principal porque refleja lo que Órdenes conoce y no lo que Inventario comprometió, aunque se conserva como respaldo cuando Inventario no reporta reserva; (3) que Despacho consuma los eventos de Inventario y mantenga una copia local de la ubicación, descartada porque duplica un dato de otro contexto y agrega un consumidor y una tabla para evitar una lectura puntual; (4) aplicar el circuit breaker de ADR-009 a LogiExpress, descartada en Despacho porque la llamada es única, corta y sin reintentos, de modo que no genera presión sostenida sobre un proveedor caído que el disyuntor deba contener, y porque añadiría un estado compartido y umbrales que calibrar para un resultado que ya es una degradación aceptada; (5) reintentar la guía en línea con backoff, descartada porque retrasaría la salida del pedido, que es justamente lo que HU-011 quiere evitar; y (6) rechazar el despacho hasta obtener guía, descartada porque contradice el criterio de aceptación de HU-011.

## Consecuencias

### Positivas
- El origen del despacho coincide siempre con la ubicación de la que Inventario descontó las unidades, sin copias que mantener alineadas.
- Una falla de LogiExpress no detiene ni retrasa el despacho físico, y no se pierde ninguna orden por ese motivo (PRE-02).
- El comportamiento ante cada falla es simple y verificable con el mock de LogiExpress, que provoca timeouts de forma determinística (ADR-019).

### Negativas
- **Acoplamiento de disponibilidad Despacho-Inventario.** Mientras Inventario no responde, Despacho no puede determinar el origen de un despacho a domicilio. El mensaje se reintenta y, agotado el límite de entregas, pasa a la dead letter. La orden no se pierde, pero su despacho se demora hasta que Inventario se recupere y el mensaje se reprocese.
- **Excepción a ADR-014.** La afirmación de que no existen llamadas HTTP síncronas entre contextos deja de ser exacta. Esta decisión establece la única excepción, limitada a lecturas de Inventario desde Despacho; cualquier otra llamada síncrona entre contextos exige su propio ADR.
- **Sin disyuntor durante una caída prolongada de LogiExpress.** Cada despacho a domicilio consume hasta el tiempo máximo de la llamada en el consumidor. Con un volumen alto, el atraso se acumula en la cola `despacho.ordenes` hasta que el proveedor se recupere o se aumenten las réplicas del consumidor.
- **La guía en `GUIA_PENDIENTE` no se reintenta automáticamente.** El dominio permite asignar la guía desde ese estado, pero el MVP no incluye un proceso que la vuelva a solicitar. Mientras no se asigne, el cliente no recibe el enlace de rastreo. Este alcance se declara en los límites del MVP del DDA.
- **Condición de revisión.** Antes del despliegue de producción conviene reevaluar el circuit breaker hacia LogiExpress y un reintento programado de guías, con umbrales calibrados en las pruebas de carga, tal como ya anticipa ADR-009.

## Preocupaciones y evidencia que atiende

PRE-01, PRE-02, PRE-15. Evidencia: EV-18, EV-31, EV-32.
