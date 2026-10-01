# Guía de sustentación — Calificación Proyecto Fase 2

**Proyecto:** SGO-Logic  
**Duración:** 40 minutos  
**Modalidad:** Presencial  
**Rúbrica base:** 80 puntos  
**Evaluación individual:** 20 puntos por integrante  

> Esta guía organiza la exposición del MVP de SGO-Logic alrededor de los apartados de la rúbrica de Fase 2. Cada sección debe demostrarse con código, diagrama, persistencia, despliegue o evidencia de prueba.

---

## 1. Objetivo de la presentación

Demostrar que SGO-Logic:

1. Implementa los drivers funcionales solicitados.
2. Mantiene trazabilidad entre arquitectura, diagramas, persistencia, despliegue y código.
3. Resuelve los atributos de calidad definidos en el DDA.
4. Integra correctamente con sistemas externos y mocks.
5. Puede operarse y demostrarse de extremo a extremo.
6. Cuenta con evidencia de pruebas, gestión Scrum y decisiones justificadas.

---

# 2. Guion de presentación de 40 minutos

## 0:00–2:00 — Apertura y problema

### Minutaje

- Presentación del equipo.
- Presentación del problema de negocio.
- Objetivo de SGO-Logic.
- Alcance del sistema.

### Guion sugerido

> MegaStore posee múltiples canales de venta: tiendas físicas, web, aplicación móvil y centro de atención. Sin embargo, la información de inventario, pagos, facturación y despachos se encuentra distribuida entre sistemas independientes. Esto provoca doble venta de inventario, pérdida de órdenes, devoluciones lentas y conciliación manual.
>
> SGO-Logic es un sistema de registro y operación omnicanal que permite iniciar, continuar y completar una compra desde distintos canales, utilizando una visión unificada del inventario, del cobro, de la documentación fiscal y del despacho.
>
> La solución implementa un MVP ejecutable con ASP.NET Core 8, microservicios por contexto delimitado, Docker Compose, RabbitMQ, PostgreSQL, Redis, Prometheus y Grafana. Los sistemas externos se representan mediante mocks configurables para poder demostrar también sus fallas.

### Evidencia para mostrar

- `README.md`
- `documentacion/docs/DDA_SGOLogic.md`
- `documentacion/img/Diagrama de Contexto.png`
- `documentacion/img/Diagrama de Casos de Uso de Negocio de Alto Nivel.png`

### Puntos de la rúbrica cubiertos

- Objetivo de negocio.
- Gestión de venta omnicanal.
- Continuidad entre canales.
- Consolidación de inventario.
- Integración con ERP, POS y proveedores.

---

## 2:00–5:00 — Arquitectura general

### Guion sugerido

> La arquitectura adoptada es un estilo de microservicios organizado por contexto delimitado. Cada contexto es responsable de sus propios datos y no modifica directamente la base de datos de otro contexto.
>
> El sistema utiliza un API Gateway como punto único de entrada. Los servicios se comunican principalmente mediante eventos publicados en RabbitMQ. Cuando una operación requiere una respuesta inmediata, se utiliza una llamada HTTP explícita, evitando acoplamientos innecesarios.
>
> El ciclo de vida de la orden se coordina mediante una saga orquestada por el contexto de Órdenes. La saga solicita la reserva de inventario, procesa el pago, solicita la certificación fiscal y posteriormente el despacho.
>
> Cada contexto posee su propia base de datos lógica. Además, existe una base separada para Auditoría, cuyo propósito es conservar evidencia append-only de las operaciones.

### Contextos que deben señalarse

- **API Gateway:** punto único de entrada.
- **Auth:** autenticación, JWT y carrito.
- **Órdenes:** ciclo de vida y saga.
- **Inventario:** reservas, stock y compensaciones.
- **Pagos:** cobro y reembolsos.
- **Facturación:** documentos fiscales.
- **Auditoría:** bitácora append-only.
- **Consulta de Estado:** modelo de lectura CQRS.
- **Despacho:** asignación de origen, guía y entrega.
- **Integración ERP:** consolidación batch.
- **Integración POS:** propagación de eventos.
- **Notificaciones:** comunicación desacoplada.
- **Web:** interfaz de demostración.

### Evidencia

- `documentacion/docs/DDA_SGOLogic.md`
- `documentacion/img/Diagrama de Contexto.png`
- `documentacion/img/Diagrama de Bloques.png`
- `documentacion/img/Diagrama de Componentes.png`
- `documentacion/img/Diagrama de Despliegue Lógico.png`
- `SGO-Logic/docker-compose.yml`

### Puntos de la rúbrica cubiertos

- Arquitectura y vistas.
- Diagramas de despliegue.
- Trazabilidad entre arquitectura y código.
- Patrones y frameworks.
- Organización modular.

---

## 5:00–12:00 — Gestión de venta

### 5:00–7:00 — Carrito omnicanal

> El carrito se mantiene en Redis con una clave asociada al cliente y un TTL configurable. Esto permite que el carrito no dependa de la sesión de un canal específico. Cuando un usuario continúa su compra en otro canal, el sistema puede reconocer el carrito compartido y fusionarlo con la identidad autenticada.
>
> El sistema también permite trabajar con un carrito anónimo y asociarlo posteriormente a una cuenta verificada.

### Evidencia

- `SGO-Logic/src/Services/SgoLogic.Auth.Api/`
- `documentacion/diagramas_CDU/CU-07.svg`
- `documentacion/docs/contratos/`
- `SGO-Logic/README.md`, sección de carrito y autenticación.

### 7:00–9:00 — Confirmación de orden

> Al confirmar la orden, el contexto de Órdenes registra la orden y coordina el proceso mediante la saga. Cada transición se persiste y se publica como evento.
>
> El proceso normal es:
>
> 1. Se crea la orden.
> 2. Se solicita la reserva de inventario.
> 3. Se procesa el cobro.
> 4. Se genera y certifica el documento fiscal.
> 5. Se solicita el despacho.
> 6. Se actualiza el estado visible para el cliente.

### Evidencia

- `SGO-Logic/src/Services/SgoLogic.Ordenes.Api/`
- `MaquinaEstadosOrden.cs`
- `ServicioConfirmacionOrden.cs`
- Consumidores de Inventario, Pagos, Facturación y Despacho.
- `documentacion/diagramas_CDU/CU-02.svg`
- `documentacion/ADR/ADR-002-saga-orquestada-centralizada.md`
- `documentacion/ADR/ADR-016-saga-dirigida-por-eventos-de-resultado.md`

### 9:00–10:30 — Pago y reintento

> El contexto de Pagos registra cada intento de cobro, incluyendo la orden, el medio de pago, el resultado y la información relevante del intento. La información sensible de tarjeta no se almacena: se utiliza tokenización delegada.
>
> Si PayKlik presenta latencia o falla, la orden no se pierde. El intento queda registrado y la saga conserva la orden en un estado consultable, por ejemplo `PAGO_PENDIENTE`. El procesamiento puede reintentarse de forma asíncrona.
>
> El mecanismo de Outbox garantiza que el cambio de estado y la publicación del evento ocurren en la misma transacción.

### Evidencia

- `SGO-Logic/src/Services/SgoLogic.Pagos.Api/`
- `SGO-Logic/src/BuildingBlocks/SgoLogic.BuildingBlocks/Outbox/`
- `documentacion/ADR/ADR-009-circuit-breaker-reintentos-payklik-logiexpress.md`
- `documentacion/ADR/ADR-010-tokenizacion-no-almacenamiento-datos-tarjeta.md`
- `documentacion/docs/PRUEBA-CARGA-EAC01.md`
- `SGO-Logic/scripts/pruebas/evidencia/eac01-verificacion-2026-09-22.txt`

### Datos de la evidencia documentada

- 379 órdenes creadas.
- 0 órdenes perdidas.
- 100 % de los intentos fallidos auditados.
- 130 intentos fallidos auditados.
- 379 órdenes consultables.

### 10:30–12:00 — Facturación y consulta de factura

> La facturación se realiza de manera asíncrona. Primero se genera el documento fiscal y se conserva la relación con la orden. Luego se envía a la SAT mediante el mecanismo definido por la arquitectura.
>
> La orden no espera de manera bloqueante a la certificación fiscal. Si la SAT falla o tarda, el documento permanece en reintento controlado y la orden queda en un estado pendiente fiscal.
>
> El documento fiscal se puede consultar posteriormente mediante un endpoint vinculado al identificador de la orden.

### Evidencia

- `SGO-Logic/src/Services/SgoLogic.Facturacion.Api/`
- `documentacion/diagramas_CDU/CU-04.svg`
- `documentacion/ADR/ADR-011-certificacion-fiscal-asincrona-sat.md`
- `documentacion/ADR/ADR-015-bandera-alerta-fiscal-sin-estado-nuevo.md`
- Migraciones y modelo de datos de Facturación.

### Puntos cubiertos

- Confirmar orden: **5 puntos**.
- Recibir pago: **3 puntos**.
- Reintentar pago: **2 puntos**.
- Emitir factura: **3 puntos**.
- Consultar factura: **2 puntos**.
- Continuar compra en otro canal: **3 puntos**.
- Agregar o quitar productos: **2 puntos**.

---

## 12:00–15:00 — Gestión de inventario

### Guion sugerido

> El contexto de Inventario administra las existencias de las tiendas y centros de distribución. La consistencia de stock es uno de los requisitos más importantes del sistema.
>
> Para evitar que dos clientes comprometan la misma unidad, la reserva utiliza bloqueo optimista mediante una columna de versión. Si la versión cambia antes de confirmar la reserva, el sistema rechaza la segunda operación y solicita reintento.
>
> Las reservas tienen una fecha de expiración. Cuando una compra no se completa, el sistema libera automáticamente las unidades reservadas.
>
> El sistema también contempla la compensación inmediata de inventario cuando la saga falla después de reservar, por ejemplo, cuando el pago es rechazado. De esta forma no se espera únicamente al TTL para liberar el stock.

### Evidencia

- `SGO-Logic/src/Services/SgoLogic.Inventario.Api/`
- `ServicioReservas.cs`
- `ServicioExpiracionReservas.cs`
- `ServicioLiberacionReservas.cs`
- `ServicioReingresoStock.cs`
- `SGO-Logic/tests/SgoLogic.Inventario.IntegrationTests/`
- `documentacion/ADR/ADR-005-bloqueo-optimista-reserva-inventario.md`
- `documentacion/ADR/ADR-022-compensacion-explicita-de-stock.md`
- `documentacion/docs/CONCURRENCIA-EAC05.md`

### Pruebas que deben mencionarse

- Reserva con existencia suficiente.
- Reserva con stock insuficiente.
- Conflicto de versión.
- Reservas concurrentes.
- Expiración de reserva.
- Liberación por compensación.
- Reingreso por devolución.

### Puntos cubiertos

- Consultar inventario: **3 puntos**.
- Reservar producto: **2 puntos**.
- Liberar inventario: **2 puntos**.
- Actualizar inventario: **3 puntos**.

---

## 15:00–18:00 — Logística, entrega y atención al cliente

### 15:00–16:30 — Despacho

> El contexto de Despacho determina el origen de la orden según la disponibilidad del inventario. Puede iniciar el despacho desde un centro de distribución o desde una tienda.
>
> Cuando corresponde, el sistema solicita una guía a LogiExpress. Si el proveedor presenta un timeout, la orden no se pierde: puede quedar en estado de guía pendiente y el resto de la operación continúa.
>
> Los cambios de estado de la guía actualizan la orden, el modelo de consulta y las notificaciones.

### 16:30–18:00 — Atención y consulta de estado

> Consulta de Estado implementa un modelo de lectura separado de los modelos de escritura. Redis se utiliza como caché de lectura y PostgreSQL como respaldo.
>
> De esta forma se evita consultar en tiempo real a todos los proveedores externos para mostrar el estado de una orden.
>
> El módulo también permite consultar historial, incidencias, devoluciones y atenciones relacionadas con una orden o un cliente.

### Evidencia

- `SGO-Logic/src/Services/SgoLogic.Despacho.Api/`
- `SGO-Logic/src/Services/SgoLogic.ConsultaEstado.Api/`
- `SGO-Logic/src/Services/SgoLogic.Notificaciones.Api/`
- `documentacion/diagramas_CDU/CU-10.svg`
- `documentacion/diagramas_CDU/CU-11.svg`
- `documentacion/diagramas_CDU/CU-12.svg`
- `documentacion/diagramas_CDU/CU-14.svg`
- `documentacion/diagramas_CDU/CU-15.svg`
- `documentacion/docs/PRUEBA-CARGA-EAC02.md`
- `documentacion/docs/OBSERVABILIDAD.md`

### Métricas recomendadas

La documentación de EAC-02 presenta:

- 17,955 consultas procesadas.
- Aproximadamente 450 req/s.
- P95: 9.27 ms.
- P100: 232.09 ms.

Esto cumple el criterio de responder en menos de cinco segundos.

### Puntos cubiertos

- Solicitar envío a LogiExpress: **3 puntos**.
- Asignar origen y destino: **3 puntos**.
- Actualizar estado de envío: **2 puntos**.
- Consultar estado de la orden: **2 puntos**.
- Registrar atención: **1 punto**.
- Consultar historial: **2 puntos**.

---

## 18:00–21:00 — Auditoría y consolidación

### 18:00–19:30 — Auditoría

> Auditoría es un consumidor independiente que recibe todos los eventos del sistema. Cada evento se conserva con su EventId, tipo de evento, momento, aggregate, transactionId, payload y fuente.
>
> La información se almacena en una base separada. El rol de la aplicación solamente tiene permisos de INSERT y SELECT sobre la bitácora. Además, existe un trigger que bloquea operaciones UPDATE y DELETE incluso para el propietario de la tabla.
>
> Esto permite reconstruir una transacción completa y responder consultas de evidencia con una latencia menor a un segundo.

### 19:30–21:00 — ERP y POS

> La integración con el ERP se realiza mediante un worker que consume eventos de órdenes y consolida las ventas en lotes CSV o XML. El lote se transfiere posteriormente al ERP, dentro de la ventana de 30 minutos definida por la restricción de negocio.
>
> También se realiza conciliación de totales y conteos.
>
> La integración con el POS es orientada a eventos. Los mensajes utilizan identificadores únicos y el consumidor aplica idempotencia para evitar duplicados. Las fallas se reintentan y posteriormente pueden=deadir a una dead-letter queue.

### Evidencia

- `SGO-Logic/src/Services/SgoLogic.Auditoria.Worker/`
- `documentacion/docs/AUDITORIA.md`
- `SGO-Logic/src/Services/SgoLogic.IntegracionErp.Worker/`
- `SGO-Logic/src/Services/SgoLogic.IntegracionPos.Worker/`
- `documentacion/ADR/ADR-006-auditoria-append-only-base-separada.md`
- `documentacion/ADR/ADR-007-capa-anticorrupcion-erp-as400.md`
- `documentacion/ADR/ADR-008-integracion-batch-erp-30-minutos.md`
- `documentacion/ADR/ADR-012-propagacion-confiable-eventos-pos.md`
- `documentacion/docs/decisiones/ordenes-persistencia.md`

### Puntos cubiertos

- Sincronizar ventas con ERP: **3 puntos**.
- Informe de ventas conciliado y auditable: **4 puntos**.

---

## 21:00–24:00 — Pruebas y atributos de calidad

### Guion sugerido

> La implementación no se limita a demostrar el camino feliz. También se ejecutaron pruebas para los atributos de calidad definidos en el DDA.

### Tabla de evidencias

| Escenario | Evidencia | Resultado |
|---|---|---|
| EAC-01: PayKlik caído | Carga con 80 órdenes/minuto | 0 órdenes perdidas; 100 % de intentos fallidos auditados |
| EAC-02: Consulta de estado | Carga sobre Consulta de Estado | P95 9.27 ms; P100 232.09 ms |
| EAC-03: Auditoría | Smoke test con aproximadamente 1,000 eventos | Recuperación menor a 1 segundo |
| EAC-04: Escalabilidad | Réplicas de órdenes y auditoría | Escalado horizontal documentado |
| EAC-05: Concurrencia | Reservas concurrentes | Sin doble compromiso de stock |

### Archivos importantes

- `SGO-Logic/scripts/pruebas/`
- `SGO-Logic/scripts/pruebas-carga/`
- `SGO-Logic/scripts/escalabilidad/`
- `documentacion/docs/PRUEBA-CARGA-EAC01.md`
- `documentacion/docs/PRUEBA-CARGA-EAC02.md`
- `documentacion/docs/CONCURRENCIA-EAC05.md`
- `documentacion/docs/ESCALABILIDAD.md`
- `documentacion/docs/OBSERVABILIDAD.md`

### Puntos cubiertos

- Evidencia de concurrentencia.
- Evidencia de rendimiento.
- Evidencia de auditoría.
- Evidencia de escalabilidad.
- Evidencia de resiliencia.

---

## 24:00–29:00 — Cambios aplicados según feedback de Fase 1

### Guion sugerido

> Durante la implementación encontramos diferencias entre el diseño inicial y las condiciones reales del MVP. Cada diferencia relevante fue registrada como ADR o documentada en la sección 14 del DDA.

### Cambios que conviene explicar

1. **Separación de documentación y código**
   - Se separó la arquitectura del prototipo ejecutable.
   - Se estableció la estructura `SGO-Logic/`.

2. **Migraciones EF Core**
   - Se reemplazó la inicialización basada únicamente en `EnsureCreated()`.
   - Se desarrollaron factories y migraciones.
   - Se adoptó `Database.Migrate()`.

3. **Corrección del gateway**
   - Se corrigieron rutas duplicadas mediante `PathRemovePrefix` y `PathPrefix`.
   - Se verificó el acceso a los contextos desde el puerto 8080.

4. **Notificaciones duplicadas**
   - Se corrigió el binding para evitar que un mismo cambio de estado generara varias notificaciones.

5. **Auditoría append-only**
   - Se vinculó la auditoría a todos los eventos.
   - Se agregaron permisos restringidos.
   - Se agregó trigger contra `UPDATE` y `DELETE`.

6. **Inventario**
   - Se sustituyó una estrategia frágil de expiración por un mecanismo persistente.
   - Se implementó compensación explícita de reservas.

7. **ERP y POS**
   - Se incorporaron workers independientes.
   - Se implementaron reintentos, idempotencia, lotes y conciliación.

8. **Seguridad**
   - Se confirmó el uso de tokenización.
   - No se almacenan PAN ni CVV.
   - Se agregó autenticación en el borde mediante JWT.

### Evidencia

- `documentacion/ADR/ADR-013` a `ADR-022`
- `documentacion/docs/DDA_SGOLogic.md`, sección 14
- `documentacion/docs/bitacora.md`, sección de retrospectiva.

### Puntos cubiertos

- Cambios justificados según feedback: **6 puntos**.
- Coherencia entre documentación y código.
- Uso de ADR.

---

## 29:00–32:00 — Gestión Scrum y trabajo colaborativo

### Guion sugerido

> La gestión del proyecto se apoyó en Trello, con seis columnas: To Do, Blocked, In Progress, Ready for Testing, Test/QA y Done.
>
> Cada tarjeta correspondió a una historia de usuario e incluyó criterios de aceptación, tareas técnicas, responsables y referencias a evidencias, ADR o escenarios de calidad.
>
> El tablero fue utilizado como radiador de información. Las tarjetas solo se movieron a Done después de contar con implementación, revisión y evidencia.

### Evidencia

- Captura inicial: `documentacion/img/TrelloInicial.png`.
- Captura final: `documentacion/img/TrelloFinal.png`.
- Plan del tablero: `documentacion/Gestion_Proyecto/Plan_Tablero_Trello.md`.
- Historias: `documentacion/Gestion_Proyecto/Backlog_Historias_Usuario.md`.
- Bitácora y dailies: `documentacion/docs/bitacora.md`.

### Resultados documentados

- 47 tarjetas en `Done`.
- 0 tarjetas en `In Progress`.
- 0 tarjetas en `To-Do`.
- 0 tarjetas en `Blocked`.
- 0 tarjetas en `Test/QA`.

### Puntos cubiertos

- Daily standups: **1 punto**.
- Sprint Planning: **4 puntos**.
- Sprint Retrospective: **4 puntos**, compartida con Burndown.
- Burndown Chart: **4 puntos**.
- Release: **1 punto**.

### Evidencia recomendada para Release

```bash
git branch -a
git log --oneline --decorate -n 20
git log --all --oneline --grep="#carnet"
```

Mostrar, si están disponibles, el commit o PR donde se integró `develop` hacia `release` y `main`.

---

## 32:00–36:00 — Demostración funcional

### Guion sugerido

> Para cerrar la sustentación, demostraremos el recorrido principal de SGO-Logic utilizando los servicios y la interfaz web.

### Flujo recomendado

1. Iniciar sesión o registrar un usuario de demostración.
2. Mostrar el carrito compartido.
3. Agregar productos.
4. Confirmar la orden.
5. Mostrar la reserva de inventario.
6. Mostrar el pago.
7. Mostrar la factura.
8. Mostrar el estado de despacho.
9. Consultar el estado de la orden.
10. Mostrar la auditoría.
11. Forzar una falla de un mock externo.
12. Mostrar que la orden permanece consultable y que el sistema registra el fallo.

### Servicios visibles

- UI Web: `http://localhost:8092`
- API Gateway: `http://localhost:8080`
- RabbitMQ: `http://localhost:15672`
- Grafana: `http://localhost:3000`
- Prometheus: `http://localhost:9090`

### Evidencia

- `SGO-Logic/scripts/pruebas/camino-feliz.ps1`
- `SGO-Logic/scripts/demo/sembrar-demo.sh`
- `documentacion/docs/PRUEBA-CARGA-EAC01.md`
- `documentacion/docs/bitacora.md`

---

## 36:00–38:00 — Limitaciones y riesgos

> El alcance de esta entrega es un MVP funcional sobre Docker Compose. Existen algunas limitaciones que delimitan el entorno de demostración.

### Limitaciones

1. **No existe autoescalado HPA:** el entorno utiliza Docker Compose, no Kubernetes.
2. **RabbitMQ es una instancia única:** las colas quorum permiten distribuir consumidores, pero no equivalen a un clúster de brokers.
3. **PostgreSQL no está replicado:** el escalado demostrado corresponde a servicios, no al clúster de datos.
4. **La disponibilidad de 99.9 % no fue medida en una ventana real:** se probó la no pérdida de órdenes y la auditoría de intentos fallidos.
5. **Los proveedores externos son mocks:** la arquitectura se valida contra contratos simulados.
6. **El catálogo comercial permanece como dependencia externa:** el sistema consume productos, precios o promociones, pero no administra el catálogo completo.

Estas limitaciones deben presentarse como decisiones consciente del alcance del MVP, no como functionality faltante.

---

## 38:00–40:00 — Cierre y preguntas

> Para concluir, SGO-Logic materializa la arquitectura definida en la Fase 1 en un sistema ejecutable, modular y trazable. El proyecto cubre la gestión de ventas, inventario, pagos, facturación, despacho, atención, auditoría, integración con ERP y POS, observabilidad y atributos de calidad.
>
> La evidencia demuestra que los principales riesgos fueron atendidos mediante Outbox, idempotencia, reintentos, circuit breaker, compensación de inventario, auditoría append-only, pruebas de concurrencia y pruebas de carga.
>
> Gracias por su atención. Estamos preparados para responder preguntas sobre la arquitectura, el código, las pruebas o la gestión del proyecto.

---

# 3. Evaluación contra la rúbrica

## I. Funcionalidades — 50 puntos

| Apartado | Requisito | Puntos | Estado observado | Evidencia principal |
|---|---|---:|---|---|
| Gestión de venta | Confirmar orden | 5 | Cumple | Saga de Órdenes, estados, `CU-02`, código |
| Gestión de venta | Recibir pago | 3 | Cumple | Integración REST con PayKlik, contrato de Pagos |
| Gestión de venta | Reintentar pago | 2 | Cumple | Outbox, reintentos, `PAGO_PENDIENTE` |
| Gestión de venta | Emitir factura | 3 | Cumple | Facturación asíncrona y modelo fiscal |
| Gestión de venta | Consultar factura | 2 | Cumple | Endpoint de documentos por orden y persistencia |
| Gestión de venta | Continuar compra en otro canal | 3 | Cumple con alcance MVP | Carrito Redis compartido, Auth y Web |
| Gestión de venta | Agregar/quitar productos | 2 | Cumple | Controlador de carrito y persistencia Redis |
| Inventario | Consultar inventario | 3 | Cumple | Existencias por ubicación y contexto Inventario |
| Inventario | Reservar producto | 2 | Cumple | Bloqueo optimista y pruebas concurrentes |
| Inventario | Liberar inventario | 2 | Cumple | Expiración, compensación y reingreso |
| Inventario | Actualizar inventario | 3 | Cumple | Movimientos, existencias, POS y eventos |
| Logística | Solicitar envío | 3 | Cumple con mock | Integración con LogiExpress mock |
| Logística | Asignar origen/destino | 3 | Cumple | Lógica de despacho y selección por inventario |
| Logística | Actualizar estado de envío | 2 | Cumple | Persistencia de estados y eventos |
| Atención | Consultar estado | 2 | Cumple | CQRS, Redis, PostgreSQL y pruebas EAC-02 |
| Atención | Registrar atención | 1 | Cumple | Incidencias y devoluciones auditables |
| Atención | Consultar historial | 2 | Cumple con alcance MVP | Historial de órdenes e incidencias |
| Auditoría | Sincronizar ventas con ERP | 3 | Cumple con mock | Worker batch y consolidación |
| Auditoría | Informe de ventas | 4 | Cumple parcialmente | Lotes y conciliación documentados; debe demostrarse el reporte |

### Verificación prioritaria del informe de ventas

Para asegurar el puntaje completo de 4 puntos, mostrar explícitamente:

- Lote generado.
- Totales enviados.
- Respuesta del ERP.
- Diferencias detectadas.
- Reporte o consulta de conciliación.

Si no existe una pantalla visible, presentar el endpoint, consulta SQL o archivo de evidencia relacionado con el lote y la conciliación.

---

## II. Marco de trabajo Scrum — 10 puntos

| Apartado | Puntos | Estado | Evidencia |
|---|---:|---|---|
| Daily standups / bitácora | 1 | Cumple | `bitacora.md` con múltiples dailies individuales |
| Sprint Planning | 4 | Cumple | Bitácora, tablero inicial, backlog y responsables |
| Sprint Retrospective | 4 | Cumple | Sección de retrospectiva individual en `bitacora.md` |
| Burndown Chart | 4 | Cumple | Tabla y gráfico Mermaid en `bitacora.md` |
| Release | 1 | Cumple con evidencia visible pendiente | Menciona `develop`, `release`, code freeze y suite final |

**Total esperado:** 10/10, preparando la evidencia de ramas, merges y commits con número de carnet.

---

## III. Documentación técnica — 20 puntos

| Apartado | Puntos | Estado | Observación |
|---|---:|---|---|
| Diagramas y coherencia con código | 10 | Cumple en términos generales | El repositorio contiene DDA, bloques, componentes, despliegue y casos expandidos |
| Cambios justificados según feedback | 6 | Cumple | ADR-013 a ADR-022 y sección 14 del DDA |
| Organización modular/capas | 2 | Cumple | Separación por contexto, Domain, Application e Infrastructure |
| Uso de frameworks y patrones | 1 | Cumple | .NET 8, YARP, RabbitMQ, Redis, PostgreSQL, Outbox, CQRS y Saga |
| Legibilidad y mantenibilidad | 1 | Cumple | README, contratos, documentación y convenciones de nombres |

### Verificación recomendada

Comprobar que los diagramas reflejen el código actual, incluyendo:

- `integracion-erp-worker`.
- `integracion-pos-worker`.
- `api-gateway`.
- `web`.
- `auditoria-worker`.
- Mocks de ERP, POS, PayKlik, SAT, LogiExpress y mensajería.
- Docker Compose.
- Bases separadas.

---

# 4. Evaluación individual — 20 puntos por integrante

## Presentación y sustentación técnica — 10 puntos

Cada integrante debe poder demostrar conocimiento de:

- Su componente.
- El flujo de datos.
- Las decisiones de arquitectura.
- Los endpoints principales.
- El modelo de persistencia.
- Las pruebas de su módulo.
- Los problemas encontrados y cómo los resolvió.

## Comunicación y defensa de la arquitectura — 5 puntos

Debe explicar:

- Por qué se eligió el patrón.
- Qué problema resuelve.
- Qué alternativas se descartaron.
- Cómo se relaciona con el DDA.
- Cómo se relaciona con los drivers de calidad.

## Asunción de funciones y responsabilidades — 5 puntos

Debe tener claro:

- Qué historias asume.
- Qué servicios mantiene.
- Qué contratos de eventos posee.
- Qué pruebas ejecutó.
- Qué decisiones tomó.
- Qué riesgos dejó pendientes.

### Reparto sugerido

| Tiempo | Responsable | Tema |
|---:|---|---|
| 0:00–5:00 | Integrante 1 | Problema y arquitectura |
| 5:00–12:00 | Integrante 2 | Saga, órdenes, pagos y facturación |
| 12:00–18:00 | Integrante 3 | Inventario, despacho y atención |
| 18:00–24:00 | Integrante 4 | Auditoría, ERP, POS, pruebas y observabilidad |
| 24:00–32:00 | Integrante 5 | Scrum, documentación, diagramas y retrospectiva |
| 32:00–40:00 | Todos | Demo, preguntas y cierre |

Si hay menos integrantes, distribuir las responsabilidades y mencionar claramente quién responde cada pregunta.

---

# 5. Áreas que deben vigilarse o reforzarse

## 5.1 Informe de ventas conciliado

El repositorio documenta consolidación, lotes y conciliaciones, pero la presentación debe mostrar un resultado concreto, no solo la existencia del worker.

## 5.2 Evidencia del release

Tener preparado el historial de ramas, merges y commits con número de carnet para evitar que el punto de Release quede solo en narrativa.

## 5.3 Correspondencia de diagramas y código

La rúbrica puede reducir el puntaje si una funcionalidad está en código, pero no aparece en el diagrama de despliegue o en un caso de uso expandido.

## 5.4 Prueba concurrente de inventario

Mostrar la prueba de dos o más reservas sobre la misma unidad y el resultado del rechazo por conflicto de versión.

## 5.5 Factura vinculada a la orden

Mostrar el identificador de la orden, el documento fiscal, su estado y la consulta posterior.

## 5.6 Reintento de pago

Mostrar Outbox, intento fallido, estado `PAGO_PENDIENTE`, reintento posterior y auditoría.

## 5.7 Actualización de inventario y POS

Mostrar existencia inicial, reserva, movimiento, liberación o reingreso, evento enviado al POS y resultado del envío.

---

# 6. Checklist final antes de la calificación

## Documentación

- [ ] Rúbrica disponible.
- [ ] README abierto.
- [ ] DDA abierto.
- [ ] Diagrama de contexto.
- [ ] Diagrama de bloques.
- [ ] Diagrama de componentes.
- [ ] Diagrama de despliegue.
- [ ] CU-01 a CU-15.
- [ ] ADR principales.
- [ ] Evidencia de EAC-01 a EAC-05.

## Scrum

- [ ] Captura inicial de Trello.
- [ ] Captura final de Trello.
- [ ] Burndown.
- [ ] Retrospectiva individual.
- [ ] Dailies.
- [ ] Evidencia de ramas.
- [ ] Commits con carnet.

## Demostración

- [ ] Usuario demo.
- [ ] Carrito con productos.
- [ ] Confirmación de orden.
- [ ] Inventario reservado.
- [ ] Pago exitoso.
- [ ] Factura certificada.
- [ ] Guía de despacho.
- [ ] Estado de entrega.
- [ ] Consulta de estado.
- [ ] Auditoría.
- [ ] Fallo de proveedor.
- [ ] Reintento o estado pendiente.
- [ ] Release visible.

---

# 7. Conclusión de cumplimiento

Con la evidencia disponible en el repositorio, SGO-Logic presenta un cumplimiento fuerte de la rúbrica:

- **Funcionalidades:** cobertura alta y código alineado con el DDA.
- **Scrum:** planificación, dailies, retrospectiva y burndown documentados.
- **Documentación técnica:** DDA, ADR, contratos, diagramas y evidencias.
- **Atributos de calidad:** pruebas para EAC-01, EAC-02, EAC-03, EAC-04 y EAC-05.
- **Seguridad:** tokenización, JWT y no persistencia de datos sensibles de tarjeta.
- **Resiliencia:** Outbox, reintentos, circuit breaker, DLQ e idempotencia.
- **Auditoría:** base separada, permisos append-only y reconstrucción por transacción.

La diferencia entre tener el sistema implementado y obtener el máximo puntaje está en la **trazabilidad demostrable durante la exposición**. Cada apartado debe responder:

1. **¿Qué requisito se cumple?**
2. **¿Dónde está implementado?**
3. **¿Qué diagrama o modelo lo respalda?**
4. **¿Qué prueba demuestra que funciona?**
