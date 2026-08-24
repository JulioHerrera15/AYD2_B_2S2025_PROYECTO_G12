
# Características del Sistema Priorizadas — SGO-Logic

**Curso:** Análisis y Diseño de Sistemas 2, Sección A — USAC
**Documento:** Apartado del Documento de Definición de Arquitectura (DDA)
**Elaborado por:** Angel Martinez (202300410) 


---

## 1. Propósito

Este apartado identifica las características del sistema SGO-Logic y las prioriza mediante criterios explícitos, tal como exige el enunciado, que pide priorizar "mediante criterios explícitos de valor, riesgo, urgencia e impacto sobre la arquitectura" y advierte contra asumir "una clasificación proporcionada de antemano".

Una característica del sistema (`CAR`) es una capacidad de alto nivel que el sistema ofrece al negocio. Se sitúa entre el caso de uso de negocio (qué hace el negocio) y el driver funcional (qué debe hacer el sistema en detalle). Las características son el insumo directo del backlog del tablero Kanban, cuyo enunciado exige priorizar los componentes base de la arquitectura (Backbone) antes que las funcionalidades cosméticas (EV-51).

La priorización de este documento define, por tanto, el orden del Backbone que administrará Jose en el tablero.

---

## 2. Criterios de priorización

Se evalúa cada característica en cuatro criterios, con la escala Alto (3), Medio (2), Bajo (1). El enunciado nombra los cuatro criterios de forma explícita.

| Criterio | Pregunta que responde | Anclaje en la evidencia |
|---|---|---|
| **Valor** | ¿Cuánto contribuye al objetivo estratégico omnicanal y al valor para el negocio? | EV-04, EV-11, EV-12 |
| **Riesgo** | ¿Qué tan grave es no atenderla, o qué tan incierta es su solución? | EV-11, EV-19, EV-30 |
| **Urgencia** | ¿La fecha fija de Black Friday obliga a tenerla antes del pico? | EV-43, EV-27 |
| **Impacto arquitectónico** | ¿Cuánto condiciona la estructura, el resto de componentes y las decisiones? | Decisiones de coordinación del equipo |

**Criterio declarado por el cliente.** El enunciado fija una prioridad explícita que ningún criterio interno puede contradecir: se espera contar durante el primer mes con una ruta viable para proteger los procesos asociados a pagos y facturación (EV-43). Toda característica ligada a pago y facturación hereda urgencia Alta por mandato del cliente, no por decisión del equipo.

**Regla de desempate.** Ante puntaje igual, precede la característica de la que dependen otras (habilitadora de Backbone) sobre la que no habilita a ninguna.

---

## 3. Catálogo de características

| ID | Característica | Descripción | CUN origen | Decisión de arquitectura que la soporta |
|---|---|---|---|---|
| CAR-01 | Identidad de cliente y carrito unificado | Reconocer al cliente en cualquier canal y conservar su intención de compra de forma continua entre canales y dispositivos. | CUN-01.1, CUN-01.2 | Redis como caché de carrito; API Gateway como punto de entrada |
| CAR-02 | Inventario único con reserva y expiración | Presentar existencias de tiendas y centros de distribución como una sola visión, reservar antes de confirmar y liberar reservas vencidas. | CUN-02 | Bloqueo optimista con version_column + Redis TTL |
| CAR-03 | Orquestación del ciclo de vida de la orden | Coordinar reserva, cobro, facturación y despacho como un flujo con compensación ante fallas, con registro de cada paso. | CUN-01.4, CUN-01.5 | Saga orquestada centralizada (state machine) |
| CAR-04 | Cobro resiliente y auditable | Procesar el cobro sin perder la orden ante fallas del proveedor, registrando cada intento con orden, canal, fecha y resultado. | CUN-03 | Estado "Pago Pendiente" con link de reintento; SQL de pagos + bitácora append-only |
| CAR-05 | Facturación electrónica asíncrona ante la SAT | Generar y certificar el documento fiscal sin bloquear la confirmación de la orden, conservando CUI y serie. | CUN-04 | Certificación asíncrona, estado "Confirmada - Pendiente Fiscal"; RabbitMQ MOM para SAT |
| CAR-06 | Registro inmutable de auditoría y trazabilidad | Recuperar quién ejecutó cada operación, cuándo y qué cambió, en menos de un segundo, sin poder alterar el registro. | CUN-07.4 | Bitácora append-only solo-inserción indexada por transacción |
| CAR-07 | Consulta de estado de orden y vista 360 | Responder en menos de cinco segundos el estado consolidado de una orden y el historial completo para el centro de atención. | CUN-06.1, CUN-09.2 | Redis como lectura materializada (caché de órdenes) |
| CAR-08 | Determinación de origen de despacho y seguimiento | Decidir desde qué ubicación se despacha, coordinar la guía con el proveedor y seguir el estado de entrega. | CUN-05 | RabbitMQ pub/sub para LogiExpress con reintentos y DLX |
| CAR-09 | Sincronización batch con el ERP | Consolidar ventas y entregarlas al ERP en la ventana de 30 minutos que admite, con conciliación verificable. | CUN-07.1, CUN-07.2 | RabbitMQ direct + worker de lotes CSV/XML |
| CAR-10 | Propagación confiable de eventos a POS | Enviar eventos a las 32 instancias de POS con reintentos e idempotencia, sin perder ni duplicar. | CUN-02.2 | Patrón Outbox con polling publisher; RabbitMQ pub/sub con DLX |
| CAR-11 | Catálogo, precios, promociones y financiamiento | Publicar catálogo consistente por canal y resolver el precio final con combos, promociones y planes a 3, 6 y 12 meses. | CUN-08 | Por definir (ligado a VAC-09) |
| CAR-12 | Observabilidad de sistemas externos | Monitorear de forma continua el comportamiento y la disponibilidad de PayKlik, LogiExpress, SAT y notificaciones. | Transversal | Prometheus + Grafana |
| CAR-13 | Postventa: devoluciones, cambios e incidencias | Iniciar reversiones ligadas a la venta original, reingresar inventario y dar seguimiento a incidencias. | CUN-06.2, CUN-06.3, CUN-06.4 | Saga de compensación; SQL de inventario |
| CAR-14 | Notificaciones al cliente | Informar confirmación, preparación, tránsito, entrega o incidencias, sin que su falla detenga la operación. | CUN-09.1 | RabbitMQ pub/sub; desacople total del flujo principal |

---

## 4. Evaluación y puntaje

| ID | Característica | Valor | Riesgo | Urgencia | Impacto | Total | Nivel |
|---|---|---|---|---|---|---|---|
| CAR-03 | Orquestación del ciclo de vida de la orden | 3 | 3 | 3 | 3 | 12 | Backbone |
| CAR-02 | Inventario único con reserva y expiración | 3 | 3 | 3 | 3 | 12 | Backbone |
| CAR-04 | Cobro resiliente y auditable | 3 | 3 | 3 | 3 | 12 | Backbone |
| CAR-06 | Registro inmutable de auditoría | 3 | 3 | 3 | 3 | 12 | Backbone |
| CAR-05 | Facturación electrónica asíncrona | 3 | 3 | 3 | 2 | 11 | Backbone |
| CAR-01 | Identidad de cliente y carrito unificado | 3 | 2 | 3 | 3 | 11 | Backbone |
| CAR-10 | Propagación confiable de eventos a POS | 2 | 3 | 2 | 3 | 10 | Backbone |
| CAR-07 | Consulta de estado y vista 360 | 3 | 2 | 2 | 2 | 9 | Alto |
| CAR-08 | Origen de despacho y seguimiento | 3 | 2 | 2 | 2 | 9 | Alto |
| CAR-12 | Observabilidad de sistemas externos | 2 | 3 | 2 | 2 | 9 | Alto |
| CAR-09 | Sincronización batch con el ERP | 2 | 2 | 2 | 2 | 8 | Medio |
| CAR-13 | Postventa | 2 | 2 | 1 | 2 | 7 | Medio |
| CAR-11 | Catálogo, precios y promociones | 2 | 2 | 1 | 2 | 7 | Medio |
| CAR-14 | Notificaciones al cliente | 2 | 1 | 1 | 1 | 5 | Bajo |

---

## 5. Justificación de los casos límite


**Por qué CAR-06 (auditoría) es Backbone y no un servicio de soporte.** Podría parecer una preocupación transversal de implementar al final. Se prioriza como Backbone porque el veto de Finanzas (PRE-05, PRE-07) y la auditoría de la SAT (EV-30) hacen que una operación sin registro sea inaceptable desde la primera transacción. La bitácora debe existir antes que las operaciones que registra, no después.

**Por qué CAR-05 (facturación) hereda urgencia Alta pese a su certificación asíncrona.** La decisión de certificación asíncrona reduce su acoplamiento temporal, pero no su urgencia: el cliente pidió explícitamente proteger pagos y facturación en el primer mes (EV-43). El desacople es una decisión de diseño; la prioridad es un mandato.

**Por qué CAR-01 (carrito) no es la primera pese a ser la cara visible del omnicanal.** Es de altísimo valor, pero el riesgo de no tenerla es de experiencia, no de integridad ni de cumplimiento. Las cuatro que la preceden protegen contra sobreventa, pérdida de órdenes y multas fiscales, cuyo costo está cuantificado en el caso (EV-11, EV-30). El carrito habilita la venta; las otras evitan pérdidas medibles.

**Por qué CAR-14 (notificaciones) es la última.** No es subestimación: es la traducción directa del propio enunciado, que establece que una falla en los canales de comunicación no debe impedir que la operación principal continúe (EV-34). El cliente autorizó explícitamente relegarla. Colocarla antes contradiría la evidencia.

**Por qué CAR-12 (observabilidad) sube a Alto pese a no ser una función de negocio.** Méndez pidió por nombre el monitoreo constante de los sistemas externos (EV-37) y su preocupación tiene poder de rechazo. Además, la resiliencia de CAR-04 y CAR-08 no es verificable sin medir el comportamiento de PayKlik y LogiExpress. Es habilitadora, no cosmética.

---

## 6. Secuencia del Backbone para el tablero Kanban


**Ola 1 — Núcleo transaccional (protege contra pérdidas medibles).**
1. CAR-06 Registro inmutable de auditoría (debe existir antes de registrar nada).
2. CAR-02 Inventario único con reserva y expiración (corazón de consistencia).
3. CAR-03 Orquestación del ciclo de vida de la orden (coordina el resto).
4. CAR-04 Cobro resiliente y auditable.
5. CAR-05 Facturación electrónica asíncrona.

**Ola 2 — Habilitadores de la operación omnicanal.**
6. CAR-01 Identidad de cliente y carrito unificado.
7. CAR-10 Propagación confiable de eventos a POS.
8. CAR-07 Consulta de estado y vista 360.

**Ola 3 — Operación extendida y visibilidad.**
9. CAR-08 Origen de despacho y seguimiento.
10. CAR-12 Observabilidad de sistemas externos.
11. CAR-09 Sincronización batch con el ERP.

**Ola 4 — Complementarias.**
12. CAR-13 Postventa.
13. CAR-11 Catálogo, precios y promociones.
14. CAR-14 Notificaciones al cliente.

La Ola 1 es exactamente la "ruta viable para proteger pagos y facturación" que el cliente exige en el primer mes (EV-43). Este es el argumento central para la defensa: el orden del Backbone no es arbitrario, reproduce la prioridad declarada por el negocio.

---

## 7. Trazabilidad de características

| CAR | Preocupaciones que atiende | Evidencia principal | Nivel |
|---|---|---|---|
| CAR-01 | PRE-13, PRE-14 | EV-21, EV-35, EV-36 | Backbone |
| CAR-02 | PRE-09, PRE-10, PRE-15 | EV-22, EV-23, EV-05 | Backbone |
| CAR-03 | PRE-02, PRE-05 | EV-28, EV-31 | Backbone |
| CAR-04 | PRE-02, PRE-05, PRE-08 | EV-28, EV-26, EV-11 | Backbone |
| CAR-05 | PRE-06, PRE-07 | EV-19, EV-29 | Backbone |
| CAR-06 | PRE-05, PRE-07 | EV-30 | Backbone |
| CAR-07 | PRE-14 | EV-33, EV-06, EV-09 | Alto |
| CAR-08 | PRE-15, PRE-04 | EV-31, EV-32, EV-12 | Alto |
| CAR-09 | PRE-11, PRE-12 | EV-15, EV-10 | Medio |
| CAR-10 | PRE-11 | EV-16 | Backbone |
| CAR-11 | PRE-13 | EV-03 | Medio |
| CAR-12 | PRE-04 | EV-37 | Alto |
| CAR-13 | PRE-14, PRE-06 | EV-09, EV-29 | Medio |
| CAR-14 | PRE-03 | EV-34, EV-20 | Bajo |

---

