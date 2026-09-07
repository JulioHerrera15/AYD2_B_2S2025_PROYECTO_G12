# ADR-004: CQRS ligero con Redis como modelo de lectura para consulta de estado

**Estado:** Aceptada
**Responsable:** Angel Martinez (202300410)
**Fecha:** 2026-08-24

## Contexto

Los clientes y el personal de atención esperan consultar el estado consolidado de cualquier orden (compra, pago, despacho, entrega, incidencias) desde distintos dispositivos, y una consulta que supere los cinco segundos es inaceptable (EV-33). Esa consulta no puede depender de orquestar en tiempo real llamadas a los contextos de escritura ni a sistemas externos (ERP, POS, PayKlik, LogiExpress), que tienen su propia latencia y disponibilidad, especialmente durante picos de hasta 8 veces la demanda normal (EV-27).

## Decisión

Se separa un contexto de Consulta de Estado, distinto de Órdenes, que mantiene un modelo de lectura materializado en Redis. Ese modelo se actualiza de forma asíncrona al consumir los eventos que publican Órdenes, Inventario, Pagos, Facturación y Despacho, en vez de construir la respuesta en el momento consultando cada contexto de escritura (ver `CU-10` en `DDA_04_Drivers_Funcionales_CDU.md`).

## Alternativas consideradas

Se evaluaron cinco opciones: (1) Redis como base de lectura materializada; (2) base SQL de solo lectura, con mayor latencia que la memoria pura en picos altos; (3) MongoDB u otro NoSQL operacional, ideal para la vista 360 anidada pero que introduce otro motor a administrar; (4) consultas directas por fan-out, descartadas porque incumplen los 5 segundos si una dependencia externa está lenta; y (5) Elasticsearch, descartado por su alto consumo de recursos que amenaza el techo de USD 1,200/mes. Se eligió Redis materializado porque reutiliza un motor ya aprobado para el carrito, responde en el orden de milisegundos y mantiene el costo al mínimo. Se asume la necesidad de una estrategia de recarga del modelo ante la caída de un nodo.

## Consecuencias

### Positivas
- La consulta de estado no depende de la disponibilidad ni del rendimiento de los contextos de escritura ni de los sistemas externos, lo que sostiene el presupuesto de respuesta declarado por el cliente.
- Consulta de Estado escala de forma independiente de Órdenes, porque su patrón de carga (lectura) es distinto al de escritura.

### Negativas
- El modelo de lectura es eventualmente consistente, no en tiempo real: existe una ventana breve entre el cambio de estado real y su reflejo en Redis.
- Introduce un componente de datos adicional (Redis) que operar, respaldar y monitorear, junto con la lógica de reconstrucción de la vista ante un cache miss.

## Preocupaciones y evidencia que atiende

PRE-14, PRE-15. Evidencia: EV-06, EV-09, EV-33.
