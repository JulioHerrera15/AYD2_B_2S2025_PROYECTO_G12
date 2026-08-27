# Backlog de Historias de Usuario — SGO-Logic

**Responsable:** Jose Pirir (202300335) — Administrador del Tablero Kanban
**Fuente:** `DDA_02_Casos_Uso_Negocio`, `DDA_04_Drivers_Funcionales_CDU`, `DDA_05_Drivers_de_Calidad_EAC`, `DDA_01_Stakeholders_Preocupaciones`

> Nota: el DDA no exige historias de usuario como artefacto (exige CDU); este backlog es la traducción operativa de esos CDU/EAC/PRE para poblar el Tablero Kanban, priorizado por Ola del Backbone (Ola 1 primero). Cada historia referencia su CU-##/EV-##/PRE-## de origen para trazabilidad.

---

## Ola 1 — Núcleo transaccional

### HU-001 — Reservar inventario para una orden
**Estimación:** 5 SP
**Como:** sistema de Órdenes
**Quiero:** reservar el inventario solicitado con control de concurrencia
**Para:** garantizar que ninguna unidad se comprometa a dos clientes a la vez

**Criterios de aceptación:**
- Verifica existencia disponible en la ubicación candidata antes de reservar.
- Aplica bloqueo optimista (`version_column`); si la versión cambió, rechaza y responde que debe reintentarse.
- Si no hay stock suficiente, responde sin reservar e indica la cantidad disponible.
- Toda reserva exitosa tiene marca de expiración (TTL).
- El evento "inventario reservado" se publica para Auditoría y Consulta de Estado.

**Tareas técnicas:**
- Modelar tabla de existencias con columna de versión (bloqueo optimista).
- Implementar endpoint/handler de reserva con validación de stock.
- Implementar manejo de conflicto de versión (409 + reintento).
- Configurar TTL de reserva y su persistencia.
- Publicar evento de dominio "InventarioReservado" (Outbox).

*Referencia: CU-01, EV-22, EV-23, EV-05*

---

### HU-002 — Liberar reserva vencida
**Estimación:** 3 SP
**Como:** sistema (proceso automático)
**Quiero:** liberar automáticamente las reservas cuyo TTL expiró
**Para:** devolver al inventario disponible las unidades de compras no concluidas

**Criterios de aceptación:**
- Al vencer el TTL, la reserva se marca expirada sin intervención manual.
- Las unidades vuelven a contarse como disponibles.
- La orden asociada pasa a "Cancelada - Reserva Vencida".
- Si la actualización falla, se reintenta desde cola sin liberar prematuramente.

**Tareas técnicas:**
- Implementar temporizador/worker de expiración de reservas.
- Actualizar contador de stock disponible al expirar.
- Publicar evento de expiración para Órdenes y Auditoría.
- Manejar reintento ante falla de la base operacional.

*Referencia: CU-06, EV-23, EV-22*

---

### HU-003 — Confirmar orden (orquestación de la saga)
**Estimación:** 8 SP
**Como:** cliente
**Quiero:** confirmar mi compra y que el sistema coordine reserva, cobro, facturación y despacho
**Para:** que mi orden avance de forma confiable sin perderse ante una falla

**Criterios de aceptación:**
- Crea la orden en estado inicial y registra el evento en Auditoría.
- Orquesta en secuencia: reserva de inventario → cobro → certificación fiscal (async) → despacho.
- Si falla la reserva, la orden no avanza y no se cobra.
- Si falla el cobro, se libera la reserva y la orden queda en "Pago Pendiente" (compensación).
- La orden nunca desaparece: siempre queda en un estado consultable.
- Permite modificar el método de entrega después de confirmada.

**Tareas técnicas:**
- Implementar orquestador de saga (estado + transiciones) en el contexto Órdenes.
- Integrar llamadas a Inventario, Pagos, Facturación, Despacho.
- Implementar lógica de compensación (liberar reserva ante falla de cobro).
- Persistir y publicar cada transición de estado (Auditoría + Consulta de Estado).
- Endpoint para modificar método de entrega post-confirmación.

*Referencia: CU-02, EV-04, EV-26, EV-28, EV-29, EV-31*

---

### HU-004 — Procesar cobro con preservación de la orden
**Estimación:** 8 SP
**Como:** sistema de Órdenes
**Quiero:** procesar el cobro registrando cada intento (éxito o fallo)
**Para:** no perder la venta si PayKlik falla y poder auditar cada intento

**Criterios de aceptación:**
- Registra el intento (orden, canal, fecha, estado) antes de invocar a PayKlik (patrón Outbox).
- Invoca a PayKlik vía REST; tolera latencia de hasta 5 s.
- Si el proveedor falla/no responde, la orden pasa a "Pago Pendiente" sin bloquear el flujo, y se genera un enlace de reintento.
- Todo intento (exitoso o fallido) queda registrado en Auditoría.
- No se almacena número completo de tarjeta ni CVV.

**Tareas técnicas:**
- Implementar tabla Outbox de intentos de pago en SQL-Finanzas.
- Integrar cliente REST hacia PayKlik con timeout configurable.
- Implementar publicador que consume el Outbox y envía a RabbitMQ (dead-letter + TTL para reintentos).
- Generar y enviar enlace de reintento de pago al cliente.
- Registrar cada intento en Auditoría vía evento.

*Referencia: CU-03, EV-17, EV-24, EV-25, EV-26, EV-28, EV-11*

---

### HU-005 — Certificar documento fiscal ante la SAT
**Estimación:** 8 SP
**Como:** sistema de Órdenes
**Quiero:** generar y certificar el documento fiscal de forma asíncrona
**Para:** no bloquear la confirmación de la compra al cliente mientras la SAT responde

**Criterios de aceptación:**
- Genera el XML fiscal conservando CUI y serie autorizada.
- Encola el envío (patrón MOM) sin bloquear la confirmación al cliente.
- La orden pasa a "Confirmada - Pendiente Fiscal" mientras se certifica.
- Si la SAT tarda/falla, reintenta con back-off exponencial; tras agotar reintentos, genera alerta interna.
- Al certificar, actualiza la orden a "Confirmada Fiscalmente" y registra el resultado en Auditoría.

**Tareas técnicas:**
- Implementar generador de XML fiscal con CUI/serie.
- Configurar cola MOM en RabbitMQ para el envío a la SAT.
- Implementar worker con reintento y back-off exponencial + alerta al agotar reintentos.
- Endpoint/callback de recepción de certificación.
- Registrar resultado en Auditoría.

*Referencia: CU-04, EV-19, EV-29, EV-30, EV-43*

---

### HU-006 — Registrar evento de auditoría
**Estimación:** 5 SP
**Como:** cualquier contexto de escritura del sistema
**Quiero:** registrar cada operación relevante en una bitácora inmutable
**Para:** poder reconstruir quién hizo qué y cuándo ante una auditoría

**Criterios de aceptación:**
- El registro es de solo inserción (`INSERT`), sin `UPDATE` ni `DELETE`.
- Cada evento se indexa por identificador de transacción.
- La recuperación de evidencia por transacción responde en menos de 1 segundo.
- Si la inserción falla, el evento se reintenta desde cola sin pérdida.

**Tareas técnicas:**
- Crear base/instancia de Auditoría separada, con permisos restringidos a solo-inserción.
- Implementar consumidor de eventos desde RabbitMQ hacia Auditoría.
- Indexar por ID de transacción para lectura < 1s.
- Endpoint de consulta de evidencia por transacción.

*Referencia: CU-05, EV-30, EV-26*

---

## Ola 2

### HU-007 — Registro y reconocimiento de cliente / carrito unificado
**Estimación:** 5 SP
**Como:** cliente
**Quiero:** registrarme y que el sistema me reconozca en cualquier canal, manteniendo mi carrito
**Para:** continuar mi compra sin perder lo que ya había agregado, sin importar el canal

**Criterios de aceptación:**
- El formulario de registro contiene: Nombre, Apellido, Teléfono, Correo, Contraseña.
- La contraseña se ingresa dos veces y debe cumplir una política mínima de seguridad.
- Se confirma el correo con un código de 6 dígitos tras el registro; se solicita en cada inicio de sesión si el usuario no está verificado.
- El cliente puede continuar como invitado y crear cuenta al confirmar la compra.
- El carrito persiste en Redis con TTL y se sincroniza entre canales (web, app, tienda, call center) para el mismo cliente.
- Si Redis no está disponible, el sistema informa de forma controlada y registra el incidente en Auditoría.

**Tareas técnicas:**
- Implementar formulario y endpoint de registro con validación de campos.
- Implementar hashing seguro de contraseña (no texto plano).
- Implementar envío de código de verificación de 6 dígitos (correo) y su validación.
- Implementar bloqueo de inicio de sesión hasta verificar correo.
- Implementar carrito en Redis con TTL y sincronización cross-canal.
- Implementar flujo de "invitado" con asociación posterior de cuenta.

*Referencia: CU-07, EV-04, EV-21, EV-35, EV-36*

---

### HU-008 — Publicar eventos pendientes (Outbox transversal)
**Estimación:** 5 SP
**Como:** sistema
**Quiero:** publicar de forma confiable los eventos pendientes de cada contexto
**Para:** que ningún evento se pierda aunque el broker esté temporalmente inaccesible

**Criterios de aceptación:**
- Cada contexto de escritura registra su evento en su propia tabla outbox, en la misma transacción del cambio.
- El publicador marca el evento como "publicado" solo tras confirmación del broker.
- Ante fallo del broker, el evento permanece "pendiente" y se reintenta.
- Al superar el máximo de reintentos, el evento pasa a dead-letter exchange para revisión.

**Tareas técnicas:**
- Diseñar esquema de tabla outbox reutilizable por contexto.
- Implementar proceso publicador (polling periódico) por contexto.
- Configurar dead-letter exchange y política de reintentos en RabbitMQ.
- Instrumentar métricas de eventos pendientes/publicados/fallidos.

*Referencia: CU-08, EV-16, EV-28*

---

### HU-009 — Propagar evento de venta al POS
**Estimación:** 5 SP
**Como:** sistema de Integración POS
**Quiero:** enviar el evento de venta a la instancia de POS correspondiente con reintentos e idempotencia
**Para:** que la tienda refleje la venta sin duplicados ni pérdidas, sin modificar el POS existente

**Criterios de aceptación:**
- El evento se envía con identificador único para que el POS ignore duplicados.
- Si el POS no responde, el mensaje va a dead-letter y se reintenta con back-off.
- La venta no se revierte por la inaccesibilidad temporal del POS.
- El resultado del envío queda registrado en Auditoría.

**Tareas técnicas:**
- Implementar mapeo de tienda destino → instancia de POS.
- Implementar envío idempotente (ID único por evento).
- Configurar reintentos con back-off y dead-letter exchange.
- Registrar en Auditoría el resultado de cada envío.

*Referencia: CU-09, EV-16*

---

### HU-010 — Consultar estado de una orden
**Estimación:** 5 SP
**Como:** cliente o agente de atención
**Quiero:** consultar el estado de una orden desde cualquier dispositivo
**Para:** conocer su situación sin llamar ni esperar

**Criterios de aceptación:**
- La respuesta llega en menos de 5 segundos (incluso bajo carga pico).
- Consolida en una sola vista: compra, cobro, documento fiscal, despacho y entrega.
- Si no hay vista cacheada (cache miss), se construye desde las bases de solo lectura y se persiste en Redis, respetando el límite de 5 s.
- El agente puede ver el historial completo (vista 360°) de un cliente, no solo una orden.
- Si se supera el presupuesto de tiempo, se registra la degradación en Auditoría.

**Tareas técnicas:**
- Implementar modelo de lectura materializado en Redis (Consulta de Estado).
- Implementar consumidores de eventos que actualizan la vista materializada.
- Implementar endpoint de consulta con fallback a SQL en cache miss.
- Implementar endpoint de historial paginado por cliente.
- Instrumentar medición de latencia y alerta si supera 5 s.

*Referencia: CU-10, EV-06, EV-09, EV-33*

---

## Ola 3

### HU-011 — Determinar origen de despacho y solicitar guía
**Estimación:** 8 SP
**Como:** sistema de Órdenes
**Quiero:** decidir desde dónde se despacha el pedido y solicitar la guía a LogiExpress
**Para:** que logística sepa qué preparar sin que un timeout del proveedor bloquee el despacho

**Criterios de aceptación:**
- Determina el origen: centro de distribución, tienda física o recogida por el cliente.
- Notifica al operador de logística la instrucción de preparación.
- Si LogiExpress responde con timeout (~2% de solicitudes), el pedido queda en "Guía Pendiente" y el despacho físico puede iniciar igual.
- Permite reasignar el origen/guía si el método de entrega cambia después de confirmada la compra.
- Si el cliente eligió recogida, no se solicita guía.

**Tareas técnicas:**
- Implementar lógica de selección de origen de despacho según inventario/ubicación.
- Integrar cliente REST hacia LogiExpress con manejo de timeout.
- Configurar dead-letter exchange y reintento para solicitudes de guía fallidas.
- Notificar a logística (evento/consulta) con la instrucción de preparación.
- Manejar reasignación de guía ante cambio de método de entrega.

*Referencia: CU-11, EV-31, EV-32, EV-18, EV-12*

---

### HU-012 — Actualizar estado de entrega
**Estimación:** 3 SP
**Como:** sistema de Despacho
**Quiero:** incorporar los estados que comunica LogiExpress (preparación, tránsito, entrega, incidencia)
**Para:** mantener informado al cliente hasta la recepción del pedido

**Criterios de aceptación:**
- Cada estado recibido actualiza la orden y se publica para Consulta de Estado y Notificaciones.
- El evento queda registrado en Auditoría.

**Tareas técnicas:**
- Implementar endpoint/webhook de recepción de estados de LogiExpress.
- Publicar evento de cambio de estado de entrega.
- Registrar en Auditoría.

*Referencia: CU-12, EV-18, EV-34*

---

### HU-013 — Consolidar y transferir ventas al ERP
**Estimación:** 8 SP
**Como:** sistema de Integración ERP
**Quiero:** consolidar las ventas del período y transferirlas al ERP AS/400 en ventanas de 30 minutos
**Para:** que Finanzas concilie sin intervención manual, sin modificar el ERP

**Criterios de aceptación:**
- Genera archivo CSV/XML consolidado por canal y tienda.
- Se transfiere dentro de la ventana de 30 minutos que admite el ERP.
- Concilia diferencias entre lo registrado y lo transferido de forma automática.
- No requiere ni realiza ninguna modificación sobre el ERP.

**Tareas técnicas:**
- Implementar job/temporizador de consolidación cada 30 min.
- Implementar generador de archivo CSV/XML según formato del ERP.
- Implementar mecanismo de transferencia de archivo (según protocolo disponible del ERP).
- Implementar proceso de conciliación automática de diferencias.
- Registrar resultado del lote en Auditoría.

*Referencia: CU-13, EV-10, EV-15*

---

## Ola 4

### HU-014 — Registrar devolución o cambio
**Estimación:** 8 SP
**Como:** agente de atención
**Quiero:** registrar una devolución o cambio con vista consolidada de la compra
**Para:** reingresar inventario y generar la documentación fiscal de reversión sin llamadas adicionales

**Criterios de aceptación:**
- Permite consultar el historial consolidado de la orden antes de registrar la devolución.
- Reingresa las unidades devueltas a la ubicación correspondiente.
- Genera el documento fiscal de reversión ligado a la venta original.
- Inicia el reembolso al cliente por el monto correspondiente.
- Soporta devolución parcial (solo algunos ítems) y cambio por otro producto (nueva reserva + despacho).

**Tareas técnicas:**
- Implementar endpoint de registro de devolución ligado a orden original.
- Implementar reingreso de inventario (ajuste de stock).
- Integrar generación de documento fiscal de reversión (reutiliza HU-005).
- Implementar flujo de reembolso (reutiliza HU-004 en sentido inverso).
- Implementar soporte de devolución parcial y cambio de producto.

*Referencia: CU-14, EV-06, EV-09, EV-29*

---

### HU-015 — Notificar cambio de estado de orden
**Estimación:** 3 SP
**Como:** cliente
**Quiero:** recibir notificaciones del estado de mi orden (confirmación, preparación, tránsito, entrega, incidencia)
**Para:** estar informado sin tener que consultar manualmente

**Criterios de aceptación:**
- Se envía notificación (SMS, correo o push) ante cada cambio relevante de estado.
- La falla del proveedor de notificaciones no bloquea ni revierte la operación principal.

**Tareas técnicas:**
- Implementar consumidor de eventos de cambio de estado (pub/sub).
- Integrar con el proveedor externo de SMS/correo/push.
- Manejar falla del proveedor de forma aislada (no propagar al flujo principal).

*Referencia: CU-15, EV-20, EV-34*

---

## Historias de subprocesos sin CDU propio (gaps identificados en DDA_02)

### HU-016 — Vista unificada de existencias
**Estimación:** 5 SP
**Como:** operador de logística
**Quiero:** ver una sola vista de existencias entre las 32 tiendas y los 2 centros de distribución
**Para:** no depender de llamadas telefónicas para confirmar disponibilidad

**Criterios de aceptación:**
- Muestra existencias, ubicaciones, reservas, entradas y salidas de todas las ubicaciones en una sola pantalla/API.

**Tareas técnicas:**
- Diseñar modelo de datos de inventario con ubicación lógica (tienda/CD).
- Implementar endpoint/vista consolidada de existencias.

*Referencia: CUN-02.1, EV-05, EV-07*

---

### HU-017 — Consulta de disponibilidad en tiempo real
**Estimación:** 3 SP
**Como:** vendedor u operador de logística
**Quiero:** consultar disponibilidad de inventario en tiempo real por ubicación
**Para:** decidir de dónde despachar un pedido

**Criterios de aceptación:**
- La consulta refleja el stock disponible actualizado (post-reservas).

**Tareas técnicas:**
- Exponer endpoint de disponibilidad por ubicación sobre el modelo de Inventario.

*Referencia: CUN-02.4, EV-08, EV-32*

---

### HU-018 — Selección de medio de pago por canal
**Estimación:** 3 SP
**Como:** cliente
**Quiero:** elegir entre tarjeta, transferencia bancaria o billetera digital según el canal
**Para:** pagar con el medio que prefiera

**Criterios de aceptación:**
- El checkout ofrece los medios de pago habilitados para el canal de compra usado.

**Tareas técnicas:**
- Implementar selector de medio de pago en el flujo de checkout.
- Parametrizar medios de pago disponibles por canal.

*Referencia: CUN-03.1, EV-24, EV-35*

---

### HU-019 — Preparación de pedido en ubicación de origen
**Estimación:** 3 SP
**Como:** operador de logística
**Quiero:** recibir la instrucción de preparación de un pedido en la ubicación asignada
**Para:** alistarlo físicamente para el despacho

**Criterios de aceptación:**
- El operador recibe la instrucción (productos, cantidades, ubicación) apenas se determina el origen de despacho.

**Tareas técnicas:**
- Implementar notificación/vista de instrucción de preparación para logística.

*Referencia: CUN-05.2, EV-32*

---

### HU-020 — Recogida de pedido en tienda
**Estimación:** 2 SP
**Como:** cliente
**Quiero:** recoger mi pedido en tienda cuando elegí esa opción
**Para:** no depender de un envío

**Criterios de aceptación:**
- La orden marcada como "recogida en tienda" no genera solicitud de guía a LogiExpress.
- La tienda recibe la notificación para tener el pedido listo.

**Tareas técnicas:**
- Implementar bandera de "recogida en tienda" en el flujo de despacho.
- Notificar a la tienda seleccionada.

*Referencia: CUN-05.4, EV-31*

---

### HU-021 — Gestión de incidencias sobre una orden
**Estimación:** 5 SP
**Como:** agente de atención
**Quiero:** registrar y dar seguimiento a una incidencia reportada sobre una orden
**Para:** resolverla sin perder el contexto de la compra

**Criterios de aceptación:**
- La incidencia queda ligada a la orden y visible en la vista consolidada (HU-010).
- Permite actualizar el estado de la incidencia hasta su resolución.

**Tareas técnicas:**
- Modelar entidad "Incidencia" ligada a Orden.
- Implementar endpoints de creación/actualización de incidencia.
- Integrar la incidencia en la vista de Consulta de Estado.

*Referencia: CUN-06.4, EV-34, EV-06*

---

### HU-022 — Conciliación automática de diferencias con el ERP
**Estimación:** 5 SP
**Como:** analista de Finanzas
**Quiero:** que el sistema concilie automáticamente las diferencias entre lo registrado y lo transferido al ERP
**Para:** no investigarlas manualmente

**Criterios de aceptación:**
- El sistema identifica y reporta diferencias entre el lote enviado y la confirmación del ERP.

**Tareas técnicas:**
- Implementar comparación entre lote enviado y resumen de confirmación del ERP.
- Generar reporte de diferencias para Finanzas.

*Referencia: CUN-07.3, EV-10*

---

## Historias de calidad (EAC-01 a EAC-05, ver `DDA_05`)

### HU-023 — Resiliencia ante falla de PayKlik en Black Friday
**Estimación:** 5 SP (infraestructura/config, no funcional nuevo — se apoya en HU-004)
**Como:** Jefe de Operaciones
**Quiero:** que el sistema no pierda ninguna orden si PayKlik falla durante el pico de Black Friday (8x)
**Para:** no repetir la pérdida histórica de USD 150,000

**Criterios de aceptación:**
- 0 órdenes perdidas por indisponibilidad del proveedor de pago en pruebas de carga a 8x.
- 100% de los intentos fallidos quedan auditados.

**Tareas técnicas:**
- Configurar autoescalado horizontal (HPA) del servicio de Pagos.
- Ejecutar pruebas de carga simulando caída de PayKlik a 8x tráfico.
- Validar dead-letter exchange y reintentos bajo carga.

*Referencia: EAC-01, EV-11, EV-27, EV-28*

---

### HU-024 — Rendimiento de consulta de estado bajo carga
**Estimación:** 3 SP
**Como:** cliente o agente
**Quiero:** que la consulta de estado responda en menos de 5 segundos incluso en pico de tráfico
**Para:** no esperar innecesariamente

**Criterios de aceptación:**
- P100 (o el percentil acordado) de consultas responde en < 5 s bajo carga simulada de Black Friday.

**Tareas técnicas:**
- Configurar autoescalado independiente del servicio Consulta de Estado.
- Ejecutar pruebas de carga y medir latencia end-to-end.

*Referencia: EAC-02 / EAC-03, EV-33*

---

### HU-025 — Escalabilidad ante crecimiento sostenido y picos
**Estimación:** 5 SP
**Como:** Gerente de TI
**Quiero:** que el sistema escale tanto por el crecimiento anual del canal digital como por los picos de Black Friday
**Para:** no requerir un rediseño arquitectónico cada año

**Criterios de aceptación:**
- Cada servicio del Backbone escala horizontalmente de forma independiente según su propio patrón de carga.
- La capacidad base es ajustable dentro del presupuesto operativo (≤ USD 1,200/mes) sin cambios de arquitectura.

**Tareas técnicas:**
- Definir políticas de autoescalado por servicio (no un único umbral global).
- Documentar procedimiento de ajuste de capacidad base anual.

*Referencia: EAC-04, EV-01, EV-02, EV-27*

---

### HU-026 — Consistencia de inventario bajo reservas concurrentes
**Estimación:** 3 SP (validación — la lógica ya está en HU-001)
**Como:** Jefe de Logística
**Quiero:** verificar que ninguna unidad se comprometa a dos clientes a la vez, incluso con alta concurrencia
**Para:** evitar sobreventa y quejas de clientes

**Criterios de aceptación:**
- Pruebas de concurrencia (N solicitudes simultáneas sobre la misma unidad) resultan en una sola reserva exitosa.

**Tareas técnicas:**
- Diseñar y ejecutar prueba de concurrencia sobre el endpoint de reserva.
- Verificar liberación correcta de reservas rechazadas.

*Referencia: EAC-05, EV-22, EV-23*

---

## Resumen de estimación

| Ola / Bloque | Historias | SP total |
|---|---|---|
| Ola 1 (núcleo transaccional) | HU-001 a HU-006 | 37 |
| Ola 2 | HU-007 a HU-010 | 20 |
| Ola 3 | HU-011 a HU-013 | 19 |
| Ola 4 | HU-014, HU-015 | 11 |
| Gaps sin CDU | HU-016 a HU-022 | 26 |
| Calidad (EAC) | HU-023 a HU-026 | 16 |
| **Total** | **26 historias** | **129 SP** |

Este backlog es el insumo directo para poblar el Tablero Kanban (columna `To Do`), priorizando siempre Ola 1 antes que cualquier funcionalidad cosmética, tal como exige el enunciado.
