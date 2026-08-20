# Drivers de Requisitos Funcionales y Casos de Uso Expandidos — SGO-Logic

**Curso:** Análisis y Diseño de Sistemas 2, Sección A — USAC
**Documento:** Apartado del Documento de Definición de Arquitectura (DDA)
**Elaborado por:** Angel Martinez (202300410), con modelado de casos de uso expandidos por Jonathan Jeronimo (202300353)

---

## 1. Propósito

Este apartado define los drivers de requisitos funcionales del sistema SGO-Logic y los documenta como casos de uso expandidos (CDU), derivados de los casos de uso de negocio y de las características priorizadas. El enunciado exige que los CDU se acompañen de su descripción textual y se deriven de los casos de uso del negocio.

Cada caso de uso expandido corresponde a una interacción concreta de un actor con el sistema y produce un resultado observable. Se organizan por contexto delimitado (bounded context), y el orden de presentación reproduce la prioridad del Backbone definida en el documento de características.

---

## 2. Contextos delimitados del sistema

El sistema se organiza en los siguientes contextos. Cada uno es dueño exclusivo de sus datos; ningún contexto accede a la base de otro. Cuando un contexto necesita información o una acción de otro, la solicita mediante un evento o una llamada explícita. Esta es la aplicación concreta del principio de bajo acoplamiento y alta cohesión, y se hace visible en cada caso de uso a través de la sección "Interacciones con otros contextos".

| Contexto | Responsabilidad única | Es dueño de |
|---|---|---|
| Borde / API Gateway | Punto único de entrada de todos los canales; autenticación de la petición y ruteo. | Nada de dominio; solo enrutamiento y validación de acceso. |
| Auth | Identidad y sesión de usuarios y personal. | Credenciales, tokens, roles. |
| Órdenes | Ciclo de vida de la orden; es el orquestador de la saga. | Orden, sus estados y sus transiciones. |
| Inventario | Existencias unificadas, reservas y su expiración. | Existencias por ubicación, reservas. |
| Pagos | Intentos de cobro y su resultado; ruta de reintento. | Intentos de pago, estado de pago. |
| Facturación | Generación y certificación fiscal ante la SAT. | Documentos fiscales y su estado de certificación. |
| Auditoría | Registro inmutable de quién hizo qué y cuándo. | Bitácora append-only. |
| Consulta de Estado | Modelo de lectura consolidado para responder rápido. | Vista materializada de órdenes (caché de lectura). |
| Despacho | Origen del despacho y seguimiento de la entrega. | Instrucción de despacho, estado de entrega. |
| Integración ERP | Consolidación y envío batch al AS/400. | Lotes de resúmenes de venta. |
| Integración POS | Envío confiable de eventos a las 32 instancias de POS. | Cola de eventos hacia POS. |
| Notificaciones | Envío de avisos al cliente. | Solicitudes de notificación. |

**Contexto externo (fuera del alcance):** Catálogo Comercial, la administración de productos, precios, promociones y combos no entra al alcance de SGO-Logic. Se modela como una dependencia externa a la que el sistema consulta para resolver precios; su fuente definitiva se validará con el stakeholder. En consecuencia, no se escriben casos de uso de administración de catálogo en este documento.

---

## 3. Lista de casos de uso expandidos

Orden según las olas del Backbone. La descripción textual completa se desarrolla en la sección 4 para los casos del núcleo; los de menor prioridad se completan en la versión 1.0 con la misma estructura.

| ID | Caso de uso | Contexto | Actor primario | Ola | Modela Jonathan |
|---|---|---|---|---|---|
| CU-01 | Reservar inventario para una orden | Inventario | Órdenes (sistema) | 1 | Sí |
| CU-02 | Confirmar orden | Órdenes | Cliente | 1 | Sí |
| CU-03 | Procesar cobro con preservación de la orden | Pagos | Órdenes (sistema) | 1 | Sí |
| CU-04 | Certificar documento fiscal ante la SAT | Facturación | Órdenes (sistema) | 1 | Sí |
| CU-05 | Registrar evento de auditoría | Auditoría | Cualquier contexto (sistema) | 1 | No |
| CU-06 | Liberar reserva vencida | Inventario | Temporizador (sistema) | 1 | No |
| CU-07 | Reconocer cliente y mantener carrito | Órdenes / Auth | Cliente | 2 | Sí |
| CU-08 | Publicar eventos pendientes (Outbox) | Todos los de escritura | Publicador (sistema) | 2 | No |
| CU-09 | Propagar evento de venta al POS | Integración POS | Órdenes (sistema) | 2 | No |
| CU-10 | Consultar estado de una orden | Consulta de Estado | Cliente / Agente | 2 | Sí |
| CU-11 | Determinar origen de despacho y solicitar guía | Despacho | Órdenes (sistema) | 3 | Sí |
| CU-12 | Actualizar estado de entrega | Despacho | LogiExpress | 3 | No |
| CU-13 | Consolidar y transferir ventas al ERP | Integración ERP | Temporizador (sistema) | 3 | No |
| CU-14 | Registrar devolución o cambio | Órdenes | Agente | 4 | Sí |
| CU-15 | Notificar cambio de estado de orden | Notificaciones | Órdenes (sistema) | 4 | No |

---

## 4. Descripción textual de los casos de uso del núcleo

### CU-01 Reservar inventario para una orden

| Campo | Contenido |
|---|---|
| **Contexto** | Inventario |
| **Actor primario** | Contexto de Órdenes (actúa como sistema solicitante) |
| **Característica** | Inventario único con reserva y expiración |
| **Objetivo** | Comprometer las unidades solicitadas para una orden, garantizando que una misma unidad no se asigne a dos clientes. |
| **Precondición** | Existe una orden en construcción con una lista de productos y una ubicación candidata de origen. |
| **Disparador** | El contexto de Órdenes solicita la reserva al confirmar la intención de compra. |
| **Flujo principal** | 1. El sistema recibe la solicitud de reserva con los productos, cantidades y la versión actual conocida del registro de existencias. 2. Verifica que la existencia disponible cubre la cantidad solicitada. 3. Aplica la reserva incrementando el número de versión del registro. 4. Crea una reserva con marca de tiempo de expiración. 5. Devuelve la reserva confirmada con su identificador. |
| **Flujo alternativo A — conflicto de versión** | Si al aplicar la reserva la versión del registro ya cambió (otra operación tomó unidades primero), el sistema rechaza la operación y responde que debe reintentarse con la existencia actualizada. Esto es el bloqueo optimista en acción: no se bloquea el registro, se detecta el conflicto al escribir. |
| **Flujo alternativo B — existencia insuficiente** | Si no hay unidades suficientes en la ubicación, el sistema responde sin reservar e indica la cantidad disponible, para que Órdenes decida otra ubicación o informe al cliente. |
| **Poscondición** | Las unidades quedan comprometidas y ligadas a la orden, con una expiración definida; o bien no se compromete nada y se informa el motivo. |
| **Interacciones con otros contextos** | Recibe la solicitud de Órdenes. Emite un evento de "inventario reservado" que Auditoría registra y que el modelo de Consulta de Estado consume. No accede a la base de ningún otro contexto. |
| **Preocupación que atiende** | No comprometer una unidad a más de un cliente. |

### CU-02 Confirmar orden

| Campo | Contenido |
|---|---|
| **Contexto** | Órdenes (orquestador de la saga) |
| **Actor primario** | Cliente |
| **Característica** | Orquestación del ciclo de vida de la orden |
| **Objetivo** | Cerrar la intención de compra y coordinar el resto del proceso (cobro, facturación, despacho) como un flujo con compensación ante fallas. |
| **Precondición** | El cliente tiene un carrito con productos y un medio de pago seleccionado según su canal. |
| **Disparador** | El cliente confirma la compra. |
| **Flujo principal** | 1. Órdenes crea la orden en estado inicial y registra el evento en Auditoría. 2. Solicita la reserva de inventario (CU-01). 3. Con la reserva confirmada, solicita el cobro (CU-03). 4. Con el cobro aceptado, pasa la orden a "Confirmada - Pendiente Fiscal" y solicita la certificación (CU-04) de forma asíncrona. 5. Solicita el despacho (CU-11). 6. Cada transición queda registrada en Auditoría y publicada para el modelo de lectura. |
| **Flujo alternativo A — falla la reserva** | Si el inventario no puede reservarse, la orden no avanza; queda en un estado que permite reintento o cambio de ubicación, sin haber cobrado. |
| **Flujo alternativo B — falla el cobro** | Si el cobro no se completa, la saga ejecuta la compensación: libera la reserva de inventario y deja la orden en "Pago Pendiente". La orden no se pierde. |
| **Flujo alternativo C — falla la certificación fiscal** | La orden ya está confirmada para el cliente; la certificación se reintenta en segundo plano. El estado "Confirmada - Pendiente Fiscal" hace visible esa situación sin bloquear al cliente. |
| **Poscondición** | La orden queda confirmada y en curso, o preservada en un estado intermedio con su reserva liberada según corresponda. En ningún caso se pierde. |
| **Interacciones con otros contextos** | Orquesta a Inventario, Pagos, Facturación y Despacho mediante solicitudes explícitas; registra todo en Auditoría; publica eventos para Consulta de Estado, Integración POS y Notificaciones. Es el único contexto que conoce la secuencia completa; los demás solo conocen su propio paso. |
| **Preocupación que atiende** | No perder la orden ante fallas de un sistema externo; reconstruir quién ejecutó cada operación. |

### CU-03 Procesar cobro con preservación de la orden

| Campo | Contenido |
|---|---|
| **Contexto** | Pagos |
| **Actor primario** | Contexto de Órdenes (sistema solicitante); el cliente es el origen del medio de pago |
| **Característica** | Cobro resiliente y auditable |
| **Objetivo** | Ejecutar el cobro dejando constancia de cada intento y sin perder la orden si el proveedor falla. |
| **Precondición** | Existe una orden con un monto y un medio de pago seleccionado. No se almacenan números completos de tarjeta ni códigos de seguridad. |
| **Disparador** | Órdenes solicita el cobro dentro de la saga. |
| **Flujo principal** | 1. Pagos registra un intento con la orden, el canal, la fecha y el estado "en proceso". 2. Invoca a la pasarela externa. 3. Al recibir respuesta positiva, marca el intento como exitoso y responde a Órdenes. 4. El intento, exitoso o no, queda registrado en Auditoría. |
| **Flujo alternativo A — el proveedor no responde o falla** | El intento se marca como fallido con su motivo. La orden pasa a "Pago Pendiente" y se genera un enlace de reintento que se envía al cliente por correo. La operación no se bloquea a la espera del proveedor. |
| **Flujo alternativo B — reintento del cliente** | Cuando el cliente usa el enlace de reintento, se genera un nuevo intento ligado a la misma orden, conservando el historial de intentos anteriores. |
| **Poscondición** | El cobro queda aplicado y registrado, o la orden queda en "Pago Pendiente" con todos los intentos auditados. |
| **Interacciones con otros contextos** | Responde a Órdenes; solicita el envío del correo de reintento a Notificaciones; registra en Auditoría. No decide el ciclo de vida de la orden: eso lo hace Órdenes con la respuesta que Pagos le devuelve. |
| **Preocupación que atiende** | No perder la orden ante falla del proveedor; auditar cada intento; no almacenar datos sensibles de tarjeta. |

### CU-04 Certificar documento fiscal ante la SAT

| Campo | Contenido |
|---|---|
| **Contexto** | Facturación |
| **Actor primario** | Contexto de Órdenes (sistema solicitante) |
| **Característica** | Facturación electrónica asíncrona ante la SAT |
| **Objetivo** | Generar el documento fiscal y obtener su certificación sin bloquear la confirmación de la orden. |
| **Precondición** | Existe una orden confirmada con los datos fiscales necesarios. |
| **Disparador** | Órdenes solicita la certificación tras aceptar el cobro. |
| **Flujo principal** | 1. Facturación genera el documento conservando el identificador tributario y la serie autorizada. 2. Lo encola hacia el servicio de la SAT a través del canal de mensajería dedicado. 3. Al recibir la certificación, marca el documento como certificado y notifica a Órdenes que la orden puede pasar de "Pendiente Fiscal" a plenamente cerrada. 4. Registra el resultado en Auditoría. |
| **Flujo alternativo A — la SAT tarda o no responde** | El documento permanece en cola con reintentos. La orden se mantiene en "Confirmada - Pendiente Fiscal" y el cliente no percibe espera, porque su confirmación ya ocurrió. |
| **Flujo alternativo B — devolución o reembolso** | Ante una devolución, Facturación genera el documento fiscal de reversión conservando la relación con el documento de la venta original. |
| **Poscondición** | El documento queda certificado y ligado a la orden, o en reintento controlado, siempre con su relación fiscal conservada. |
| **Interacciones con otros contextos** | Responde a Órdenes; se comunica con la SAT por el canal de mensajería dedicado; registra en Auditoría. La tolerancia a la lentitud de la SAT vive aquí y no contamina al resto del flujo. |
| **Preocupación que atiende** | Documentación fiscal válida y oportuna; relación entre venta, devoluciones y reembolsos. |

### CU-05 Registrar evento de auditoría

| Campo | Contenido |
|---|---|
| **Contexto** | Auditoría |
| **Actor primario** | Cualquier contexto que ejecute una operación relevante (sistema) |
| **Característica** | Registro inmutable de auditoría y trazabilidad |
| **Objetivo** | Conservar de forma inmutable quién ejecutó cada operación, cuándo y qué cambió, recuperable en menos de un segundo. |
| **Precondición** | Ocurre una operación fiscal u operativamente relevante en cualquier contexto. |
| **Disparador** | El contexto de origen emite el evento correspondiente. |
| **Flujo principal** | 1. Auditoría recibe el evento con actor, momento, operación y datos del cambio. 2. Lo inserta en la bitácora, que solo admite inserción: no permite modificar ni borrar. 3. Indexa el registro por identificador de transacción para permitir la recuperación inmediata. |
| **Flujo alternativo — consulta de evidencia** | Ante una solicitud de auditoría, el sistema recupera por identificador de transacción todos los registros asociados y los devuelve en menos de un segundo. |
| **Poscondición** | El evento queda registrado de forma permanente e inalterable. |
| **Interacciones con otros contextos** | Recibe eventos de todos los contextos de escritura; no escribe en ninguna otra base. Su carácter de solo-inserción es lo que sostiene el veto de Finanzas: nadie, ni siquiera el propio sistema, puede alterar el registro. |
| **Preocupación que atiende** | Reconstruir quién hizo qué y cuándo; ninguna transacción sin respaldo. |

### CU-06 Liberar reserva vencida

| Campo | Contenido |
|---|---|
| **Contexto** | Inventario |
| **Actor primario** | Temporizador del sistema |
| **Característica** | Inventario único con reserva y expiración |
| **Objetivo** | Devolver al inventario disponible las unidades reservadas por compras que no concluyeron. |
| **Precondición** | Existen reservas con su marca de expiración. |
| **Disparador** | La reserva alcanza su tiempo de expiración. |
| **Flujo principal** | 1. Al vencer el tiempo de vida de la reserva, esta se marca como expirada automáticamente. 2. Las unidades vuelven a contarse como disponibles. 3. Se registra el evento en Auditoría y se publica para el modelo de lectura. |
| **Flujo alternativo — la orden se concretó antes de vencer** | Si la orden se confirmó, la reserva se consumió y no expira. |
| **Poscondición** | El inventario refleja la disponibilidad real sin retener unidades de compras abandonadas. |
| **Interacciones con otros contextos** | Notifica a Órdenes que la reserva expiró, para que la orden asociada refleje ese cambio. Registra en Auditoría. |
| **Preocupación que atiende** | Liberar el inventario reservado de compras no concluidas. |

---

## 5. Casos de uso de olas posteriores

Los casos CU-07 a CU-15 corresponden a las olas 2, 3 y 4 del Backbone. Se enumeran con su objetivo para que Jonathan planifique el modelado; su descripción textual completa sigue la misma estructura de la sección 4 y se desarrolla conforme el equipo avanza en esas olas.

| ID | Objetivo en una línea |
|---|---|
| CU-07 | Reconocer al cliente en cualquier canal y conservar su carrito de forma continua. |
| CU-08 | Publicar de forma confiable los eventos pendientes que cada contexto dejó registrados junto a su cambio, mediante consultas periódicas. |
| CU-09 | Enviar el evento de venta a la instancia de POS correspondiente con reintentos, sin perder ni duplicar. |
| CU-10 | Responder el estado consolidado de una orden en menos de cinco segundos desde el modelo de lectura. |
| CU-11 | Decidir desde qué ubicación se despacha y solicitar la guía al proveedor logístico. |
| CU-12 | Incorporar los estados de entrega que comunica el proveedor hasta la recepción. |
| CU-13 | Consolidar las ventas del periodo y entregarlas al ERP en la ventana que admite. |
| CU-14 | Registrar una devolución o cambio ligada a la venta original y reingresar inventario. |
| CU-15 | Enviar al cliente el aviso del cambio de estado, sin que su falla detenga la operación. |




