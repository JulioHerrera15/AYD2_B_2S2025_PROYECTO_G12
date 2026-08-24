# Drivers de Requisitos Funcionales y Casos de Uso Expandidos — SGO-Logic

**Curso:** Análisis y Diseño de Sistemas 2, Sección B — USAC
**Documento:** Apartado del Documento de Definición de Arquitectura (DDA)
**Elaborado por:** Angel Martinez (202300410) — estructura, contextos y núcleo (CU-01 a CU-06); Jonathan Jeronimo (202300353) — diagramas, referencias de trazabilidad y fichas de olas 2 a 4 (CU-07, CU-10, CU-11, CU-14)

---

## 1. Propósito

Este apartado define los drivers de requisitos funcionales del sistema SGO-Logic y los documenta como casos de uso expandidos (CDU), derivados de los casos de uso de negocio y de las características priorizadas. El enunciado exige que los CDU se acompañen de su descripción textual y se deriven de los casos de uso del negocio.

Cada caso de uso expandido corresponde a una interacción concreta de un actor con el sistema y produce un resultado observable. Se organizan por contexto delimitado (bounded context), y el orden de presentación reproduce la prioridad del Backbone definida en el documento de características.

**Declaración de notación (requerida por EV-50):** Los diagramas de cada caso de uso utilizan la variante `flowchart LR` de Mermaid, siguiendo la convención visual ya establecida en el documento `Despliegue.md`. Convenciones gráficas declaradas:

| Elemento | Representación Mermaid | Significado |
|---|---|---|
| Actor externo humano | `Actor["Nombre"]` | Persona o rol que interactúa con el sistema |
| Actor externo sistema | `Sist[/"Sistema"/]` | Sistema externo al SGO-Logic |
| Caso de uso del sistema | `CU("Nombre")` | Función que el sistema provee |
| Subproceso / paso | `P["Paso o estado"]` | Acción interna del sistema |
| Relación `<<include>>` | `-- <<include>> -->` | Subproceso obligatorio |
| Relación `<<extend>>` | `-. <<extend>> .->` | Subproceso opcional o condicional |
| Relación de excepción | `-. falla .->` | Camino de error |

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

**Contexto externo (fuera del alcance):** Catálogo Comercial — la administración de productos, precios, promociones y combos no entra al alcance de SGO-Logic. Se modela como una dependencia externa a la que el sistema consulta para resolver precios; su fuente definitiva se validará con el stakeholder (VAC-09 en DDA_00). En consecuencia, no se escriben casos de uso de administración de catálogo en este documento.

---

## 3. Actores del sistema

Los actores del sistema derivan directamente de los actores de negocio (`AN-##`) y trabajadores de negocio (`TN-##`) declarados en `DDA_02`.

| ID | Actor | Tipo | Rol en los casos de uso | Equivalente negocio |
|---|---|---|---|---|
| AS-01 | Cliente | Humano externo | Inicia compras, consulta órdenes, solicita devoluciones | AN-01 |
| AS-02 | Vendedor de tienda | Humano interno | Opera desde POS; recibe eventos del sistema | TN-01 |
| AS-03 | Agente de atención | Humano interno | Consulta historial, gestiona devoluciones por teléfono | TN-02 |
| AS-04 | Operador de logística | Humano interno | Consulta inventario, prepara y despacha pedidos | TN-03 |
| AS-05 | PayKlik | Sistema externo | Procesa cobros con tarjeta de crédito o débito | AN-03 |
| AS-06 | SAT | Sistema externo | Recibe y certifica documentos fiscales XML | AN-02 |
| AS-07 | LogiExpress | Sistema externo | Genera guías y comunica estados de entrega | AN-04 |
| AS-08 | Proveedor de notificaciones | Sistema externo | Entrega SMS, correo y notificaciones push | AN-05 |
| AS-09 | Sistema (automático) | Proceso interno | Temporizador y publicador; no requiere interacción humana | — |

---

## 4. Lista de casos de uso expandidos

Orden según las olas del Backbone. Los casos del núcleo (Ola 1) tienen descripción completa en la sección 5. Los de olas posteriores se desarrollan en la sección 6.

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
| CU-14 | Registrar devolución o cambio | Órdenes | Agente de atención | 4 | Sí |
| CU-15 | Notificar cambio de estado de orden | Notificaciones | Órdenes (sistema) | 4 | No |

---

## 5. Casos de uso — Ola 1 (Núcleo transaccional)

---

### CU-01 — Reservar inventario para una orden

**Contexto:** Inventario | **CAR:** CAR-02 | **CUN origen:** CUN-01.3

#### CU-01.1 Diagrama


<p align="center">
  <img src="../6. Prototipo Arquitectonico/diagramas_CDU/CU-01.drawio.svg"
       alt="Diagrama CDU CU-01 — Reservar inventario para una orden"
       width="100%">
</p>


#### CU-01.2 Descripción textual

| Campo | Contenido |
|---|---|
| **Contexto** | Inventario |
| **Actor primario** | Contexto de Órdenes (actúa como sistema solicitante) |
| **Actores secundarios** | AS-09 Sistema automático (gestiona TTL de reservas) |
| **Característica** | CAR-02 — Inventario único con reserva y expiración |
| **Objetivo** | Comprometer las unidades solicitadas para una orden, garantizando que una misma unidad no se asigne a dos clientes. |
| **Precondición** | Existe una orden en construcción con una lista de productos y una ubicación candidata de origen. El catálogo de inventario está sincronizado. |
| **Disparador** | El contexto de Órdenes solicita la reserva al confirmar la intención de compra. |
| **Flujo principal** | 1. El sistema recibe la solicitud de reserva con los productos, cantidades y la versión actual conocida del registro de existencias. 2. Verifica que la existencia disponible cubre la cantidad solicitada en SQL Operacional. 3. Aplica la reserva incrementando el número de versión del registro (bloqueo optimista, `version_column`). 4. Crea una reserva con marca de tiempo de expiración (TTL). 5. Devuelve la reserva confirmada con su identificador. |
| **Flujo alternativo A — conflicto de versión** | Si al aplicar la reserva la versión del registro ya cambió (otra operación tomó unidades primero), el sistema rechaza la operación y responde que debe reintentarse con la existencia actualizada. Esto es el bloqueo optimista en acción: no se bloquea el registro, se detecta el conflicto al escribir. |
| **Flujo alternativo B — existencia insuficiente** | Si no hay unidades suficientes en la ubicación, el sistema responde sin reservar e indica la cantidad disponible, para que Órdenes decida otra ubicación o informe al cliente. |
| **Flujo de excepción** | Si SQL Operacional no está disponible, la operación falla de forma controlada; la orden no avanza y se informa a Órdenes para que lo propague al cliente. |
| **Postcondición** | Las unidades quedan comprometidas y ligadas a la orden, con una expiración definida; o bien no se compromete nada y se informa el motivo. |
| **Interacciones con otros contextos** | Recibe la solicitud de Órdenes. Emite un evento de "inventario reservado" que Auditoría registra y que el modelo de Consulta de Estado consume. No accede a la base de ningún otro contexto. |
| **Evidencia** | EV-22, EV-23, EV-05 |
| **Preocupaciones atendidas** | PRE-09 (no comprometer una unidad a más de un cliente), PRE-10 (liberar reservas vencidas), PRE-15 (visibilidad de inventario) |
| **Conflictos que toca** | — |

---

### CU-02 — Confirmar orden

**Contexto:** Órdenes | **CAR:** CAR-03 | **CUN origen:** CUN-01.4, CUN-01.5

#### CU-02.1 Diagrama


<p align="center">
  <img src="../6. Prototipo Arquitectonico/diagramas_CDU/CU-02.drawio.svg"
       alt="Diagrama CDU CU-02 — Confirmar orden"
       width="100%">
</p>


#### CU-02.2 Descripción textual

| Campo | Contenido |
|---|---|
| **Contexto** | Órdenes (orquestador de la saga) |
| **Actor primario** | Cliente (AS-01) |
| **Actores secundarios** | Contextos internos invocados: Inventario, Pagos, Facturación, Despacho |
| **Característica** | CAR-03 — Orquestación del ciclo de vida de la orden |
| **Objetivo** | Cerrar la intención de compra y coordinar el resto del proceso (cobro, facturación, despacho) como un flujo con compensación ante fallas. |
| **Precondición** | El cliente tiene un carrito con productos y un medio de pago seleccionado según su canal. |
| **Disparador** | El cliente confirma la compra. |
| **Flujo principal** | 1. Órdenes crea la orden en estado inicial y registra el evento en Auditoría. 2. Solicita la reserva de inventario (CU-01). 3. Con la reserva confirmada, solicita el cobro (CU-03). 4. Con el cobro aceptado, pasa la orden a "Confirmada - Pendiente Fiscal" y solicita la certificación (CU-04) de forma asíncrona. 5. Solicita el despacho (CU-11). 6. Cada transición queda registrada en Auditoría y publicada para el modelo de lectura en Redis. |
| **Flujo alternativo A — falla la reserva** | Si el inventario no puede reservarse, la orden no avanza; queda en un estado que permite reintento o cambio de ubicación, sin haber cobrado. |
| **Flujo alternativo B — falla el cobro** | Si el cobro no se completa, la saga ejecuta la compensación: libera la reserva de inventario y deja la orden en "Pago Pendiente". La orden no se pierde. |
| **Flujo alternativo C — falla la certificación fiscal** | La orden ya está confirmada para el cliente; la certificación se reintenta en segundo plano. El estado "Confirmada - Pendiente Fiscal" hace visible esa situación sin bloquear al cliente. |
| **Flujo alternativo D — modificación post-confirmación** | Incluso después de confirmada la compra, el método de entrega puede modificarse (EV-31). Órdenes gestiona esta transición y actualiza el contexto de Despacho. |
| **Postcondición** | La orden queda confirmada y en curso, o preservada en un estado intermedio con su reserva liberada según corresponda. En ningún caso se pierde. |
| **Interacciones con otros contextos** | Orquesta a Inventario, Pagos, Facturación y Despacho mediante solicitudes explícitas; registra todo en Auditoría; publica eventos para Consulta de Estado, Integración POS y Notificaciones. Es el único contexto que conoce la secuencia completa; los demás solo conocen su propio paso. |
| **Evidencia** | EV-04, EV-26, EV-28, EV-29, EV-31 |
| **Preocupaciones atendidas** | PRE-02 (no perder la orden ante fallas), PRE-05 (trazabilidad de cada paso), PRE-06 (documentación fiscal válida) |
| **Conflictos que toca** | CON-02 (obligación fiscal vs. tiempo de respuesta), CON-03 (independencia del proveedor de pagos) |

---

### CU-03 — Procesar cobro con preservación de la orden

**Contexto:** Pagos | **CAR:** CAR-04 | **CUN origen:** CUN-03.2, CUN-03.4

#### CU-03.1 Diagrama


<p align="center">
  <img src="../6. Prototipo Arquitectonico/diagramas_CDU/CU-03.drawio.svg"
       alt="Diagrama CDU CU-03 — Procesar cobro con preservación de la orden"
       width="100%">
</p>


#### CU-03.2 Descripción textual

| Campo | Contenido |
|---|---|
| **Contexto** | Pagos |
| **Actor primario** | Contexto de Órdenes (sistema solicitante); el cliente es el origen del medio de pago |
| **Actores secundarios** | PayKlik (AS-05), Servicio de Auditoría (interno) |
| **Característica** | CAR-04 — Cobro resiliente y auditable |
| **Objetivo** | Ejecutar el cobro dejando constancia de cada intento y sin perder la orden si el proveedor falla. |
| **Precondición** | Existe una orden con un monto y un medio de pago seleccionado. No se almacenan números completos de tarjeta ni códigos de seguridad (EV-25). |
| **Disparador** | Órdenes solicita el cobro dentro de la saga. |
| **Flujo principal** | 1. Pagos registra un intento con la orden, el canal, la fecha y el estado "en proceso" en SQL-Finanzas (patrón Outbox). 2. Publica el evento en RabbitMQ antes de invocar al proveedor externo. 3. Invoca a PayKlik vía REST (latencia ~800 ms, picos hasta 5 s — EV-17). 4. Al recibir respuesta positiva, marca el intento como exitoso y responde a Órdenes. 5. El intento, exitoso o no, queda registrado en Auditoría. |
| **Flujo alternativo A — el proveedor no responde o falla** | El intento se marca como fallido con su motivo. La orden pasa a "Pago Pendiente" y se genera un enlace de reintento que se envía al cliente. La operación no se bloquea a la espera del proveedor; RabbitMQ reintenta con dead-letter exchange y TTL. |
| **Flujo alternativo B — reintento del cliente** | Cuando el cliente usa el enlace de reintento, se genera un nuevo intento ligado a la misma orden, conservando el historial de intentos anteriores. |
| **Flujo de excepción** | Si el cobro es rechazado de forma definitiva (no timeout, sino rechazo), Pagos informa a Órdenes para que ejecute la compensación (liberar reserva). |
| **Postcondición** | El cobro queda aplicado y registrado, o la orden queda en "Pago Pendiente" con todos los intentos auditados. |
| **Interacciones con otros contextos** | Responde a Órdenes; solicita el envío del correo de reintento a Notificaciones; registra en Auditoría. No decide el ciclo de vida de la orden: eso lo hace Órdenes con la respuesta que Pagos le devuelve. |
| **Evidencia** | EV-17, EV-24, EV-25, EV-26, EV-28, EV-11 |
| **Preocupaciones atendidas** | PRE-02 (no perder la orden), PRE-05 (auditar cada intento), PRE-08 (no almacenar datos sensibles de tarjeta) |
| **Conflictos que toca** | CON-03 (independencia frente al proveedor sin violar restricción de datos), CON-01 (trazabilidad vs. rendimiento en Black Friday) |

---

### CU-04 — Certificar documento fiscal ante la SAT

**Contexto:** Facturación | **CAR:** CAR-05 | **CUN origen:** CUN-04.1, CUN-04.2

#### CU-04.1 Diagrama


<p align="center">
  <img src="../6. Prototipo Arquitectonico/diagramas_CDU/CU-04.drawio.svg"
       alt="Diagrama CDU CU-04 — Certificar documento fiscal ante la SAT"
       width="100%">
</p>


#### CU-04.2 Descripción textual

| Campo | Contenido |
|---|---|
| **Contexto** | Facturación |
| **Actor primario** | Contexto de Órdenes (sistema solicitante) |
| **Actores secundarios** | SAT (AS-06) |
| **Característica** | CAR-05 — Facturación electrónica asíncrona ante la SAT |
| **Objetivo** | Generar el documento fiscal y obtener su certificación sin bloquear la confirmación de la orden. |
| **Precondición** | Existe una orden confirmada con los datos fiscales necesarios (datos del comprador, ítems, montos). |
| **Disparador** | Órdenes solicita la certificación tras aceptar el cobro. |
| **Flujo principal** | 1. Facturación genera el documento fiscal XML conservando el CUI y la serie autorizada (EV-19). 2. Lo encola en RabbitMQ usando el patrón MOM, desacoplando el envío del flujo principal. 3. La orden pasa inmediatamente a "Confirmada - Pendiente Fiscal"; el cliente ya recibió su confirmación (decisión CON-02). 4. El worker consume el mensaje y hace el POST del XML a la SAT. 5. Al recibir la certificación, marca el documento como certificado y notifica a Órdenes para pasar a "Confirmada Fiscalmente". 6. Registra el resultado en Auditoría. |
| **Flujo alternativo A — la SAT tarda o no responde** | El documento permanece en cola con reintentos con back-off exponencial. La orden se mantiene en "Confirmada - Pendiente Fiscal"; el cliente no percibe espera. Si se agota el máximo de reintentos, se genera alerta interna para revisión manual. |
| **Flujo alternativo B — devolución o reembolso** | Ante una devolución, Facturación genera el documento fiscal de reversión conservando la relación con el documento de la venta original (EV-29). |
| **Postcondición** | El documento queda certificado con CUI y serie, ligado a la orden; o en reintento controlado, siempre con su relación fiscal conservada. |
| **Interacciones con otros contextos** | Responde a Órdenes; se comunica con la SAT por el canal MOM de RabbitMQ; registra en Auditoría. La tolerancia a la lentitud de la SAT vive aquí y no contamina al resto del flujo. |
| **Evidencia** | EV-19, EV-29, EV-30, EV-43 |
| **Preocupaciones atendidas** | PRE-06 (documentación fiscal válida y oportuna), PRE-07 (ninguna transacción sin respaldo fiscal) |
| **Conflictos que toca** | CON-02 (obligación fiscal sincrónica vs. experiencia de usuario fluida) |

---

### CU-05 — Registrar evento de auditoría

**Contexto:** Auditoría | **CAR:** CAR-06 | **CUN origen:** CUN-07.4

#### CU-05.1 Diagrama


<p align="center">
  <img src="../6. Prototipo Arquitectonico/diagramas_CDU/CU-05.drawio.svg"
       alt="Diagrama CDU CU-05 — Registrar evento de auditoría"
       width="100%">
</p>


#### CU-05.2 Descripción textual

| Campo | Contenido |
|---|---|
| **Contexto** | Auditoría |
| **Actor primario** | Cualquier contexto que ejecute una operación relevante (sistema) |
| **Actores secundarios** | — |
| **Característica** | CAR-06 — Registro inmutable de auditoría y trazabilidad |
| **Objetivo** | Conservar de forma inmutable quién ejecutó cada operación, cuándo y qué cambió, recuperable en menos de un segundo. |
| **Precondición** | Ocurre una operación fiscal u operativamente relevante en cualquier contexto. |
| **Disparador** | El contexto de origen emite el evento correspondiente vía RabbitMQ. |
| **Flujo principal** | 1. Auditoría recibe el evento con actor, momento, operación y datos del cambio. 2. Lo inserta en su propia base de datos de Auditoría — una instancia separada de SQL-Finanzas y de SQL Operacional, que solo admite inserción: no permite modificar ni borrar (`INSERT` únicamente, sin `UPDATE` ni `DELETE`). 3. Indexa el registro por identificador de transacción para permitir la recuperación inmediata. |
| **Flujo alternativo — consulta de evidencia** | Ante una solicitud de auditoría (ej. inspección de la SAT), el sistema recupera por identificador de transacción todos los registros asociados y los devuelve en menos de un segundo (EV-30). |
| **Flujo de excepción** | Si la inserción falla, el evento permanece en la cola de RabbitMQ y se reintenta. La bitácora no acepta pérdidas. |
| **Postcondición** | El evento queda registrado de forma permanente e inalterable. |
| **Interacciones con otros contextos** | Recibe eventos de todos los contextos de escritura; no escribe en ninguna otra base. Su carácter de solo-inserción es lo que sostiene el veto de Finanzas: nadie, ni siquiera el propio sistema, puede alterar el registro. |
| **Evidencia** | EV-30, EV-26 |
| **Preocupaciones atendidas** | PRE-05 (reconstruir quién hizo qué y cuándo), PRE-07 (ninguna transacción sin respaldo) |
| **Conflictos que toca** | CON-01 (trazabilidad exhaustiva vs. rendimiento — resuelto con registro asíncrono que no bloquea la ruta crítica) |

---

### CU-06 — Liberar reserva vencida

**Contexto:** Inventario | **CAR:** CAR-02 | **CUN origen:** CUN-01.6, CUN-02.3

#### CU-06.1 Diagrama


<p align="center">
  <img src="../6. Prototipo Arquitectonico/diagramas_CDU/CU-06.drawio.svg"
       alt="Diagrama CDU CU-06 — Liberar reserva vencida"
       width="100%">
</p>


#### CU-06.2 Descripción textual

| Campo | Contenido |
|---|---|
| **Contexto** | Inventario |
| **Actor primario** | AS-09 — Temporizador del sistema (proceso automático) |
| **Actores secundarios** | — |
| **Característica** | CAR-02 — Inventario único con reserva y expiración |
| **Objetivo** | Devolver al inventario disponible las unidades reservadas por compras que no concluyeron. |
| **Precondición** | Existen reservas con su marca de expiración (TTL). |
| **Disparador** | La reserva alcanza su tiempo de expiración. |
| **Flujo principal** | 1. Al vencer el TTL de la reserva, esta se marca como expirada automáticamente. 2. Las unidades vuelven a contarse como disponibles en SQL Operacional. 3. La orden asociada pasa a estado "Cancelada - Reserva Vencida". 4. Se registra el evento en Auditoría y se publica para el modelo de lectura. |
| **Flujo alternativo — la orden se concretó antes de vencer** | Si la orden se confirmó, la reserva se consumió y no expira; este caso de uso no se activa. |
| **Flujo de excepción** | Si la actualización en SQL Operacional falla, el sistema reintenta con el evento en cola. La reserva no se considera liberada hasta que SQL Operacional confirme la actualización, para evitar sobrecomprometer inventario. |
| **Postcondición** | El inventario refleja la disponibilidad real sin retener unidades de compras abandonadas. No quedan reservas huérfanas en el sistema. |
| **Interacciones con otros contextos** | Notifica a Órdenes que la reserva expiró, para que la orden asociada refleje ese cambio. Registra en Auditoría. |
| **Evidencia** | EV-23, EV-22, EV-05 |
| **Preocupaciones atendidas** | PRE-09 (no sobrecomprometer inventario), PRE-10 (liberar el inventario en tiempo acorde a la operación) |
| **Conflictos que toca** | — |

---

## 6. Casos de uso — Olas 2, 3 y 4

---

### CU-07 — Reconocer cliente y mantener carrito

**Contexto:** Órdenes / Auth | **CAR:** CAR-01 | **CUN origen:** CUN-01.1, CUN-01.2

#### CU-07.1 Diagrama


<p align="center">
  <img src="../6. Prototipo Arquitectonico/diagramas_CDU/CU-07.drawio.svg"
       alt="Diagrama CDU CU-07 — Reconocer cliente y mantener carrito unificado"
       width="100%">
</p>


#### CU-07.2 Descripción textual

| Campo | Contenido |
|---|---|
| **Contexto** | Órdenes / Auth |
| **Actor primario** | Cliente (AS-01) |
| **Actores secundarios** | Vendedor de tienda (AS-02), Agente de atención (AS-03) |
| **Característica** | CAR-01 — Identidad de cliente y carrito unificado |
| **Objetivo** | Reconocer al cliente en cualquier canal y conservar su intención de compra de forma continua entre canales y dispositivos. |
| **Precondición** | El cliente existe en el directorio de identidades o se registra en el acto. El catálogo de productos está disponible. |
| **Disparador** | El cliente se identifica ante SGO-Logic desde cualquier canal (web, móvil, POS, call center). |
| **Flujo principal** | 1. El cliente se identifica desde cualquier canal. 2. El sistema autentica la identidad y recupera el contexto de sesión (carrito previo, historial reciente) desde Redis. 3. Si el cliente tenía un carrito activo en otro canal, el sistema lo presenta unificado sin importar el canal de origen. 4. El cliente agrega, modifica o elimina ítems. 5. El sistema persiste el estado del carrito en Redis con TTL comercialmente acordado. 6. Cualquier cambio se sincroniza de forma inmediata: el siguiente canal que acceda verá el mismo estado. |
| **Flujo alternativo A — cliente invitado** | Si el cliente no tiene cuenta, puede iniciar el proceso como invitado. Al momento de confirmar la orden, se le ofrece crear la cuenta o asociar el carrito a una existente. |
| **Flujo alternativo B — cambio de canal** | El cliente inicia la compra en web y la continúa en la tienda física o por teléfono. El agente o vendedor recupera el mismo carrito sin fricción para el cliente (EV-21, EV-36). |
| **Flujo de excepción** | Si Redis no está disponible, el sistema informa de forma controlada; la sesión actual se mantiene en memoria del canal activo sin sincronización entre canales. El incidente se registra en Auditoría. |
| **Postcondición** | El carrito del cliente está disponible desde cualquier canal con el mismo estado. La identidad queda asociada a la sesión activa. |
| **Interacciones con otros contextos** | Auth valida la identidad; Órdenes mantiene el carrito. El estado del carrito en Redis es la fuente única de verdad para todos los canales. |
| **Evidencia** | EV-04, EV-21, EV-35, EV-36 |
| **Preocupaciones atendidas** | PRE-13 (experiencia equivalente en todos los canales), PRE-14 (consultas rápidas) |
| **Conflictos que toca** | — |

---

### CU-08 — Publicar eventos pendientes (Outbox)

**Contexto:** Todos los contextos de escritura | **CAR:** CAR-10 | **CUN origen:** CUN-01.4, CUN-02.2

#### CU-08.1 Diagrama


<p align="center">
  <img src="../6. Prototipo Arquitectonico/diagramas_CDU/CU-08.drawio.svg"
       alt="Diagrama CDU CU-08 — Publicar eventos pendientes (Outbox)"
       width="100%">
</p>


#### CU-08.2 Descripción textual

| Campo | Contenido |
|---|---|
| **Contexto** | Todos los contextos de escritura (Órdenes, Pagos, Inventario, Facturación) |
| **Actor primario** | AS-09 — Publicador automático (proceso del sistema) |
| **Actores secundarios** | — |
| **Característica** | CAR-10 — Propagación confiable de eventos |
| **Objetivo** | Publicar de forma confiable los eventos pendientes que cada contexto dejó registrados junto a su cambio, garantizando que ningún evento se pierda aunque el broker esté temporalmente inaccesible. |
| **Precondición** | El contexto de escritura registró un cambio y un evento pendiente en su tabla outbox, en la misma transacción de negocio. |
| **Disparador** | El publicador consulta periódicamente la tabla outbox de cada contexto. |
| **Flujo principal** | 1. El publicador detecta eventos en estado "pendiente". 2. Los publica en RabbitMQ. 3. Marca cada evento como "publicado" solo después de confirmación del broker. |
| **Flujo de excepción** | Si RabbitMQ no responde, el evento permanece en "pendiente" y se reintenta en el siguiente ciclo. Si se supera el máximo de reintentos, el evento pasa a dead-letter exchange para revisión. |
| **Postcondición** | Todos los eventos registrados en la tabla outbox quedan publicados exactamente una vez. |
| **Interacciones con otros contextos** | Lee de la base del contexto propietario; escribe en RabbitMQ. No accede a bases de otros contextos. |
| **Evidencia** | EV-16, EV-28 |
| **Preocupaciones atendidas** | PRE-11 (información propagada sin pérdida ni duplicación), PRE-02 (no perder la orden ante fallas) |
| **Conflictos que toca** | — |

---

### CU-09 — Propagar evento de venta al POS

**Contexto:** Integración POS | **CAR:** CAR-10 | **CUN origen:** CUN-02.2

#### CU-09.1 Diagrama


<p align="center">
  <img src="../6. Prototipo Arquitectonico/diagramas_CDU/CU-09.drawio.svg"
       alt="Diagrama CDU CU-09 — Propagar evento de venta al POS"
       width="100%">
</p>


#### CU-09.2 Descripción textual

| Campo | Contenido |
|---|---|
| **Contexto** | Integración POS |
| **Actor primario** | Contexto de Órdenes (sistema origen del evento) |
| **Actores secundarios** | AS-02 Vendedor de tienda (receptor final) |
| **Característica** | CAR-10 — Propagación confiable de eventos a POS |
| **Objetivo** | Enviar el evento de venta a la instancia de POS correspondiente con reintentos e idempotencia, sin perder ni duplicar. |
| **Precondición** | Se confirmó una venta originada en o que afecta a una tienda física. El POS no será modificado (EV-16). |
| **Disparador** | Órdenes publica el evento "venta confirmada" en RabbitMQ. |
| **Flujo principal** | 1. El contexto de Integración POS consume el evento desde RabbitMQ. 2. Determina la instancia de POS destino. 3. Envía el evento con identificador único para garantizar idempotencia (el POS ignora duplicados). 4. El POS reconoce la recepción. 5. El evento se registra en Auditoría. |
| **Flujo de excepción** | Si el POS no responde, el mensaje va al dead-letter exchange y se reintenta con back-off. La venta no se revierte por la inaccesibilidad del POS; la tienda podrá reconciliar cuando vuelva a estar disponible. |
| **Postcondición** | El POS refleja la venta confirmada. No quedan duplicados ni pérdidas. |
| **Interacciones con otros contextos** | Consume de RabbitMQ (publicado por Órdenes); escribe hacia los POS (sistemas externos). Registra en Auditoría. |
| **Evidencia** | EV-16 |
| **Preocupaciones atendidas** | PRE-11 (información propagada al legacy sin pérdida ni duplicación) |
| **Conflictos que toca** | — |

---

### CU-10 — Consultar estado de una orden

**Contexto:** Consulta de Estado | **CAR:** CAR-07 | **CUN origen:** CUN-06.1, CUN-09.2

#### CU-10.1 Diagrama


<p align="center">
  <img src="../6. Prototipo Arquitectonico/diagramas_CDU/CU-10.drawio.svg"
       alt="Diagrama CDU CU-10 — Consultar estado de una orden"
       width="100%">
</p>


#### CU-10.2 Descripción textual

| Campo | Contenido |
|---|---|
| **Contexto** | Consulta de Estado |
| **Actor primario** | Cliente (AS-01) |
| **Actores secundarios** | Agente de atención (AS-03) |
| **Característica** | CAR-07 — Consulta de estado de orden y vista 360 |
| **Objetivo** | Responder el estado consolidado de una orden en menos de cinco segundos desde el modelo de lectura en Redis. |
| **Precondición** | Existe al menos una orden asociada al cliente o al ID de orden consultado. La vista materializada en Redis está actualizada. |
| **Disparador** | El cliente o el agente solicitan el estado de una orden desde cualquier dispositivo o canal. |
| **Flujo principal** | 1. La consulta llega al servicio de Consulta de Estado vía el API Gateway. 2. El servicio lee la vista materializada precomputada desde Redis. 3. La respuesta consolida en una sola vista: datos de la compra, resultado del cobro, estado del documento fiscal, estado del despacho, estado de entrega e incidencias. 4. La respuesta se entrega en menos de 5 segundos (EAC declarado — EV-33). |
| **Flujo alternativo A — historial completo del cliente** | Si el agente requiere el historial de múltiples órdenes de un cliente (vista 360), el sistema retorna la lista paginada de órdenes con su estado resumido. |
| **Flujo alternativo B — cache miss** | Si Redis no tiene la vista cacheada, el sistema construye la vista consultando SQL Operacional y SQL-Finanzas, la persiste en Redis y la retorna. El presupuesto de 5 segundos sigue vigente. |
| **Flujo de excepción** | Si no se cumple el presupuesto de tiempo, se registra la degradación en Auditoría para análisis posterior. |
| **Postcondición** | El actor tiene la vista consolidada de la orden. El agente dispone de la información necesaria para gestionar postventa sin llamadas adicionales. |
| **Interacciones con otros contextos** | Lee de Redis (no accede a los contextos de escritura en tiempo real); en caso de miss, consulta las bases de SQL Operacional y SQL-Finanzas de solo lectura. |
| **Evidencia** | EV-06, EV-09, EV-33 |
| **Preocupaciones atendidas** | PRE-14 (consultas dentro del presupuesto de tiempo), PRE-15 (visibilidad oportuna de órdenes e incidencias) |
| **Conflictos que toca** | CON-01 (trazabilidad vs. rendimiento — resuelto desacoplando lectura de escritura) |

---

### CU-11 — Determinar origen de despacho y solicitar guía

**Contexto:** Despacho | **CAR:** CAR-08 | **CUN origen:** CUN-05.1, CUN-05.2, CUN-05.3

#### CU-11.1 Diagrama


<p align="center">
  <img src="../6. Prototipo Arquitectonico/diagramas_CDU/CU-11.drawio.svg"
       alt="Diagrama CDU CU-11 — Determinar origen de despacho y solicitar guía"
       width="100%">
</p>


#### CU-11.2 Descripción textual

| Campo | Contenido |
|---|---|
| **Contexto** | Despacho |
| **Actor primario** | Contexto de Órdenes (sistema solicitante) |
| **Actores secundarios** | Operador de logística (AS-04), LogiExpress (AS-07) |
| **Característica** | CAR-08 — Determinación de origen de despacho y seguimiento |
| **Objetivo** | Decidir desde qué ubicación se despacha el pedido y solicitar la guía de envío al proveedor logístico, sin que un timeout del proveedor bloquee el despacho. |
| **Precondición** | La orden está confirmada y cobrada. Se conoce la disponibilidad de inventario por ubicación. |
| **Disparador** | Órdenes solicita el despacho después de confirmar el cobro. |
| **Flujo principal** | 1. El contexto de Despacho determina el origen: centro de distribución, tienda física o recogida por el cliente (EV-31). 2. Notifica al operador de logística la instrucción de preparación (EV-32). 3. Solicita la guía a LogiExpress vía REST. 4. LogiExpress confirma la guía. 5. El estado de la orden se actualiza con el número de guía. 6. El evento se registra en Auditoría. |
| **Flujo alternativo A — modificación del método de entrega** | Incluso después de confirmada la compra, el método de entrega puede modificarse (EV-31). Despacho gestiona la re-asignación del origen y actualiza la guía si ya había sido emitida. |
| **Flujo alternativo B — recogida en tienda** | Si el cliente eligió recogida, no se solicita guía a LogiExpress. La tienda es notificada para preparar el pedido. |
| **Flujo de excepción** | Si LogiExpress devuelve timeout (~2% de solicitudes — EV-18), el mensaje va al dead-letter exchange de RabbitMQ y se reintenta. La orden queda en estado "Guía Pendiente" y el despacho físico puede comenzar. El sistema registra el intento fallido. |
| **Postcondición** | El pedido tiene un origen de despacho asignado y una guía generada (o en reintento). Logística sabe qué preparar, desde dónde y a quién entregar. |
| **Interacciones con otros contextos** | Responde a Órdenes; se comunica con LogiExpress vía RabbitMQ pub/sub; publica el estado de despacho para Consulta de Estado y Notificaciones; registra en Auditoría. |
| **Evidencia** | EV-31, EV-32, EV-18, EV-12 |
| **Preocupaciones atendidas** | PRE-15 (visibilidad oportuna de órdenes para logística), PRE-04 (observabilidad de sistemas externos) |
| **Conflictos que toca** | — |

---

### CU-12 — Actualizar estado de entrega

**Contexto:** Despacho | **CAR:** CAR-08 | **CUN origen:** CUN-05.5

#### CU-12.1 Diagrama


<p align="center">
  <img src="../6. Prototipo Arquitectonico/diagramas_CDU/CU-12.drawio.svg"
       alt="Diagrama CDU CU-12 — Actualizar estado de entrega"
       width="100%">
</p>


#### CU-12.2 Descripción textual

| Campo | Contenido |
|---|---|
| **Contexto** | Despacho |
| **Actor primario** | LogiExpress (AS-07) |
| **Actores secundarios** | Operador de logística (AS-04), Cliente (AS-01) — destinatarios de la actualización |
| **Característica** | CAR-08 — Determinación de origen de despacho y seguimiento |
| **Objetivo** | Incorporar los estados de entrega que comunica LogiExpress (Preparación / Tránsito / Entrega / Incidencia) hasta la recepción por el cliente. |
| **Precondición** | Existe una guía generada para la orden, emitida en CU-11, con su número de guía asociado. |
| **Disparador** | LogiExpress comunica un cambio de estado de la entrega. |
| **Flujo principal** | 1. Despacho recibe la actualización de estado desde LogiExpress, asociada al número de guía. 2. Valida que la guía corresponda a una orden activa. 3. Actualiza el estado de entrega de la orden (Preparación / Tránsito / Entrega / Incidencia). 4. Publica el evento de cambio de estado para que Consulta de Estado y Notificaciones lo consuman. 5. Registra el evento en Auditoría. |
| **Flujo alternativo A — incidencia reportada** | Si LogiExpress reporta una incidencia (por ejemplo, dirección no localizada), el estado de la orden refleja la incidencia y se dispara una notificación al cliente sin bloquear el resto del flujo (EV-34). |
| **Flujo alternativo B — entrega confirmada** | Al recibir el estado "Entrega", la orden se marca como entregada y se cierra su seguimiento de despacho. |
| **Flujo de excepción** | Si la actualización llega duplicada o fuera de orden — consecuencia posible de los reintentos que LogiExpress hace ante su propia tasa de timeout (EV-18) —, el sistema la trata de forma idempotente y no genera estados inconsistentes en la orden. [PENDIENTE-ANGEL: el mecanismo exacto por el que Despacho recibe las actualizaciones de LogiExpress (notificación push del proveedor vía webhook, o consulta periódica/polling desde SGO-Logic) no está definido en el repositorio; solo se sabe que la solicitud inicial de guía es vía REST (CU-11)]. |
| **Postcondición** | El estado de entrega de la orden refleja la última actualización confirmada por LogiExpress, visible desde Consulta de Estado. |
| **Interacciones con otros contextos** | Recibe la actualización de LogiExpress (sistema externo); publica el evento para Consulta de Estado y Notificaciones; registra en Auditoría. |
| **Evidencia** | EV-18, EV-34 |
| **Preocupaciones atendidas** | PRE-15 (visibilidad oportuna de órdenes para logística y cliente), PRE-04 (observabilidad de dependencias externas) |
| **Conflictos que toca** | — |

---

### CU-13 — Consolidar y transferir ventas al ERP

**Contexto:** Integración ERP | **CAR:** CAR-09 | **CUN origen:** CUN-07.1, CUN-07.2

#### CU-13.1 Diagrama


<p align="center">
  <img src="../6. Prototipo Arquitectonico/diagramas_CDU/CU-13.drawio.svg"
       alt="Diagrama CDU CU-13 — Consolidar y transferir ventas al ERP"
       width="100%">
</p>


#### CU-13.2 Descripción textual

| Campo | Contenido |
|---|---|
| **Contexto** | Integración ERP |
| **Actor primario** | AS-09 — Temporizador del sistema (proceso automático) |
| **Actores secundarios** | ERP-Core AS/400 (sistema externo; no se modela como actor del sistema porque no interactúa directamente con SGO-Logic, solo recibe archivos — RES-01) |
| **Característica** | CAR-09 — Sincronización batch con el ERP |
| **Objetivo** | Consolidar las ventas del período y entregarlas al ERP AS/400 en la ventana de 30 minutos que admite, con conciliación automática de diferencias y sin intervención manual (EV-10, EV-15). |
| **Precondición** | Existen ventas confirmadas registradas desde el último lote enviado. |
| **Disparador** | Se cumple el intervalo de 30 minutos de la ventana de procesamiento que admite el ERP (RES-02). |
| **Flujo principal** | 1. El worker de Integración ERP consulta las ventas confirmadas desde el último lote. 2. Consolida los resúmenes por canal y tienda. 3. Genera el archivo en el formato que el ERP admite (CSV o XML). 4. Transfiere el archivo al ERP-Core por el mecanismo de recepción por lotes que este admite (EV-15). 5. Registra el resultado de la transferencia en Auditoría. |
| **Flujo alternativo — conciliación de diferencias** | Si el resumen consolidado no coincide con lo confirmado por el ERP en un ciclo posterior, el sistema señala la diferencia para revisión, sin requerir que el personal de Finanzas concilie manualmente hoja por hoja (EV-10). [PENDIENTE-ANGEL: el mecanismo exacto de detección y resolución de diferencias de conciliación no está definido en el repositorio; solo está declarado el objetivo de que sea automática y verificable (CAR-09)]. |
| **Flujo de excepción** | Si el ERP no puede recibir el archivo en la ventana correspondiente, el lote se conserva y se reintenta en el siguiente ciclo de 30 minutos; ninguna venta consolidada se pierde. |
| **Postcondición** | El ERP-Core queda actualizado con las ventas del periodo, con diferencias explicadas de forma automática y trazabilidad conservada. |
| **Interacciones con otros contextos** | Lee eventos de venta confirmados publicados por Órdenes vía RabbitMQ (canal batch); escribe hacia el ERP-Core (sistema externo, sin modificarlo — RES-01); registra en Auditoría. |
| **Evidencia** | EV-10, EV-15 |
| **Preocupaciones atendidas** | PRE-11 (información propagada al legacy sin pérdida ni duplicación), PRE-12 (consistencia contable conciliable dentro de la ventana de 30 minutos) |
| **Conflictos que toca** | CON-05 (prohibición de modificar el legacy vs. necesidad de consistencia contable oportuna) |

---

### CU-14 — Registrar devolución o cambio

**Contexto:** Órdenes | **CAR:** CAR-13 | **CUN origen:** CUN-06.2, CUN-06.3, CUN-06.4

#### CU-14.1 Diagrama


<p align="center">
  <img src="../6. Prototipo Arquitectonico/diagramas_CDU/CU-14.drawio.svg"
       alt="Diagrama CDU CU-14 — Registrar devolución o cambio"
       width="100%">
</p>


#### CU-14.2 Descripción textual

| Campo | Contenido |
|---|---|
| **Contexto** | Órdenes (coordinador), Inventario, Facturación |
| **Actor primario** | Agente de atención (AS-03) |
| **Actores secundarios** | Cliente (AS-01), SAT (AS-06) — para el documento fiscal de reversión |
| **Característica** | CAR-13 — Postventa: devoluciones, cambios e incidencias |
| **Objetivo** | Registrar una devolución o cambio ligado a la venta original, reingresar inventario y generar la documentación fiscal de reversión, con vista consolidada para el agente. |
| **Precondición** | Existe una orden confirmada. El agente puede acceder al historial completo de la compra (CU-10). |
| **Disparador** | El cliente solicita una devolución o cambio; el agente inicia el proceso desde la vista 360. |
| **Flujo principal** | 1. El agente consulta el historial consolidado de la orden (CU-10). 2. Registra la devolución ligada a la venta original, incluyendo los productos a devolver y el motivo. 3. El sistema reinicia el inventario: las unidades devueltas reingresan a la ubicación correspondiente. 4. Facturación genera el documento fiscal de reversión, conservando la relación con el documento de venta original (EV-29). 5. El sistema inicia el reembolso al cliente por el monto correspondiente. 6. Cada paso queda registrado en Auditoría. |
| **Flujo alternativo A — cambio por otro producto** | En lugar de reembolso, el sistema genera una nueva reserva por el producto de reemplazo y coordina el despacho del nuevo pedido. |
| **Flujo alternativo B — devolución parcial** | Solo se devuelven algunos ítems de la orden. El sistema actualiza el monto reembolsable y genera el documento fiscal por el parcial. |
| **Flujo de excepción** | Si la certificación fiscal del documento de reversión falla ante la SAT, el proceso sigue el mismo patrón asíncrono de CU-04 (reintento con back-off). |
| **Postcondición** | La devolución queda registrada con trazabilidad completa hacia la venta original. El inventario refleja el reingreso. El cliente recibe el reembolso y la documentación fiscal correspondiente. |
| **Interacciones con otros contextos** | Coordina Inventario (reingreso), Facturación (documento de reversión), Pagos (reembolso) y Notificaciones (aviso al cliente). Registra en Auditoría. |
| **Evidencia** | EV-06, EV-09, EV-29 |
| **Preocupaciones atendidas** | PRE-14 (vista consolidada para resolución ágil de postventa), PRE-06 (documentación fiscal de reversión válida) |
| **Conflictos que toca** | — |

---

### CU-15 — Notificar cambio de estado de orden

**Contexto:** Notificaciones | **CAR:** CAR-14 | **CUN origen:** CUN-09.1

#### CU-15.1 Diagrama


<p align="center">
  <img src="../6. Prototipo Arquitectonico/diagramas_CDU/CU-15.drawio.svg"
       alt="Diagrama CDU CU-15 — Notificar cambio de estado de orden"
       width="100%">
</p>


#### CU-15.2 Descripción textual

| Campo | Contenido |
|---|---|
| **Contexto** | Notificaciones |
| **Actor primario** | Contextos de Órdenes, Pagos, Facturación y Despacho (sistema origen del evento) |
| **Actores secundarios** | Proveedor de notificaciones (AS-08), Cliente (AS-01) — receptor final |
| **Característica** | CAR-14 — Notificaciones al cliente |
| **Objetivo** | Enviar al cliente el aviso del cambio de estado de su orden (confirmación, preparación, tránsito, entrega, incidencia), sin que la falla del proveedor externo detenga la operación principal (EV-20, EV-34). |
| **Precondición** | Ocurrió un cambio de estado relevante en el ciclo de vida de la orden (confirmación, cobro, certificación fiscal, despacho, entrega o incidencia). |
| **Disparador** | El contexto de origen (Órdenes, Pagos, Facturación o Despacho) publica un evento de cambio de estado en RabbitMQ. |
| **Flujo principal** | 1. Notificaciones consume el evento de cambio de estado desde RabbitMQ (pub/sub). 2. Determina el canal de contacto y el contenido del mensaje según el tipo de evento. 3. Envía la notificación (SMS, correo o push) a través del proveedor externo. 4. Registra el resultado del envío en Auditoría. |
| **Flujo alternativo A — enlace de reintento de cobro** | Cuando el evento corresponde a un cobro fallido (CU-03), el mensaje incluye el enlace de reintento para que el cliente complete el pago pendiente. |
| **Flujo alternativo B — incidencia de entrega** | Cuando el evento proviene de una actualización de estado con incidencia (CU-12), el mensaje informa al cliente de la incidencia reportada. |
| **Flujo de excepción** | Si el proveedor de notificaciones no está disponible o falla el envío, el evento permanece en cola con reintento; si se agotan los reintentos, el mensaje se descarta sin afectar el estado de la orden ni el resto del flujo (EV-20, EV-34). El incidente se registra en Auditoría, pero no bloquea ni revierte la operación principal. |
| **Postcondición** | El cliente recibe el aviso correspondiente, o el intento fallido queda registrado sin impacto en la orden. |
| **Interacciones con otros contextos** | Consume eventos publicados por Órdenes, Pagos, Facturación y Despacho vía RabbitMQ; envía hacia el proveedor de notificaciones (sistema externo); registra en Auditoría. No responde de vuelta a ningún contexto: su falla es unidireccional y no propagable (PRE-03). |
| **Evidencia** | EV-20, EV-34 |
| **Preocupaciones atendidas** | PRE-03 (fallas de canales secundarios aisladas del flujo principal) |
| **Conflictos que toca** | — |

---

## 7. Tabla de trazabilidad — Drivers de Requisitos Funcionales (DRF)

Esta tabla cruza cada caso de uso con las evidencias, preocupaciones y características que habilita, y sirve como insumo directo para las Matrices de Trazabilidad del DDA (`5. Coherencia del Proyecto/Docu5.md`).

| ID | Nombre | Evidencia principal | Preocupaciones atendidas | CAR | Ola |
|---|---|---|---|---|---|
| CU-01 | Reservar inventario | EV-22, EV-23, EV-05 | PRE-09, PRE-10, PRE-15 | CAR-02 | 1 |
| CU-02 | Confirmar orden | EV-04, EV-26, EV-28, EV-29, EV-31 | PRE-02, PRE-05, PRE-06 | CAR-03 | 1 |
| CU-03 | Procesar cobro | EV-17, EV-24, EV-25, EV-26, EV-28, EV-11 | PRE-02, PRE-05, PRE-08 | CAR-04 | 1 |
| CU-04 | Certificar fiscal SAT | EV-19, EV-29, EV-30, EV-43 | PRE-06, PRE-07 | CAR-05 | 1 |
| CU-05 | Registrar auditoría | EV-30, EV-26 | PRE-05, PRE-07 | CAR-06 | 1 |
| CU-06 | Liberar reserva vencida | EV-23, EV-22, EV-05 | PRE-09, PRE-10 | CAR-02 | 1 |
| CU-07 | Carrito unificado | EV-04, EV-21, EV-35, EV-36 | PRE-13, PRE-14 | CAR-01 | 2 |
| CU-08 | Publicar eventos (Outbox) | EV-16, EV-28 | PRE-11, PRE-02 | CAR-10 | 2 |
| CU-09 | Propagar a POS | EV-16 | PRE-11 | CAR-10 | 2 |
| CU-10 | Consultar estado orden | EV-06, EV-09, EV-33 | PRE-14, PRE-15 | CAR-07 | 2 |
| CU-11 | Despacho y guía | EV-31, EV-32, EV-18, EV-12 | PRE-15, PRE-04 | CAR-08 | 3 |
| CU-12 | Actualizar estado entrega | EV-18, EV-34 | PRE-15 | CAR-08 | 3 |
| CU-13 | Transferir al ERP | EV-10, EV-15 | PRE-11, PRE-12 | CAR-09 | 3 |
| CU-14 | Devolución o cambio | EV-06, EV-09, EV-29 | PRE-14, PRE-06 | CAR-13 | 4 |
| CU-15 | Notificar al cliente | EV-20, EV-34 | PRE-03 | CAR-14 | 4 |

### 7.1 Drivers de Requisitos Funcionales registrados

| ID | Driver | CU que lo implementa | Evidencia |
|---|---|---|---|
| DRF-01 | El sistema debe reconocer al cliente en cualquier canal y persistir su carrito de forma unificada | CU-07 | EV-04, EV-21, EV-35 |
| DRF-02 | El sistema debe reservar inventario antes de confirmar la venta, evitando sobrecompromiso | CU-01 | EV-22 |
| DRF-03 | El sistema debe coordinar cobro, facturación y despacho como un flujo con compensación ante fallas | CU-02 | EV-28, EV-31 |
| DRF-04 | El sistema debe registrar todo intento de cobro con orden, canal, fecha y resultado | CU-03 | EV-26 |
| DRF-05 | El sistema debe procesar el cobro sin perder la orden si el proveedor falla | CU-03 | EV-28, EV-11 |
| DRF-06 | El sistema debe generar y certificar el documento fiscal sin bloquear la confirmación al cliente | CU-04 | EV-19, EV-29 |
| DRF-07 | El sistema debe responder consultas de estado de orden en menos de 5 segundos | CU-10 | EV-33 |
| DRF-08 | El sistema debe liberar automáticamente las reservas de compras no concluidas | CU-06 | EV-23 |
| DRF-09 | El sistema debe exponer disponibilidad de inventario en tiempo real por ubicación | CU-01, CU-11 | EV-05, EV-08, EV-32 |
| DRF-10 | El sistema debe registrar de forma inmutable cada operación fiscalmente relevante | CU-05 | EV-30 |
| DRF-11 | El sistema debe propagar eventos a los 32 POS con reintentos e idempotencia | CU-09 | EV-16 |
| DRF-12 | El sistema debe sincronizar ventas con el ERP en ventana de 30 minutos | CU-13 | EV-15 |
| DRF-13 | El sistema debe gestionar devoluciones y cambios con vista consolidada y documentación fiscal de reversión | CU-14 | EV-06, EV-09, EV-29 |
| DRF-14 | El sistema debe notificar al cliente sin que la falla del proveedor detenga la operación | CU-15 | EV-34, EV-20 |

---

*Elaborado por Angel Martinez (202300410) y Jonathan Jeronimo (202300353)*
*Referencias: `DDA_00_Catalogo_Evidencia.md`, `DDA_01_Stakeholders_Preocupaciones.md`, `DDA_02_Casos_Uso_Negocio.md`, `DDA_03_Caracteristicas_Priorizadas.md`*
