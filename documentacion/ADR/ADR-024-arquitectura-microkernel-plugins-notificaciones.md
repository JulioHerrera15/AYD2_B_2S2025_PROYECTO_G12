# ADR-024: Estilo arquitectónico Microkernel basado en Plugins para el servicio de Notificaciones

**Estado:** Aceptada.
**Responsable:** Jonathan (202300353).
**Fecha:** 2026-10-06

## Contexto

En el enunciado de la Fase 3 del Sistema de Gestión de Órdenes y Logística Omnicanal (SGO-Logic), se exige explícitamente:
*"Implementar un nuevo estilo arquitectónico capaz de gestionar las notificaciones y que conviva con la arquitectura ya realizada en las anteriores fases"*.

En las Fases 1 y 2, el sistema consolidó una arquitectura basada en microservicios y eventos asíncronos con RabbitMQ (ADR-001, ADR-002, ADR-016 y ADR-018). El servicio de notificaciones consumía eventos de órdenes, despacho y pagos, pero realizaba el despacho mediante un cliente HTTP acoplado exclusivamente al envío de correos electrónicos (`CanalEmail`), limitando la capacidad de extender nuevos canales (SMS, notificaciones push móviles/web o webhooks) sin modificar y recompilar el servicio central. Adicionalmente, agregar únicamente nuevas colas no califica como un "nuevo estilo arquitectónico".

Se requiere incorporar un estilo arquitectónico formal para el subsistema de Notificaciones que:
1. Desacople el núcleo de procesamiento del ciclo de vida de los canales de entrega.
2. Permita incorporar nuevos medios de notificación (Email, SMS, Push, Webhooks) de forma modular y extensible sin alterar el flujo principal.
3. Garantice el aislamiento estricto de fallas por canal (evitando que la caída o lentitud de un canal como SMS afecte la entrega de correo o la recepción de eventos).
4. Conviva de manera nativa con los consumidores de RabbitMQ existentes y el almacenamiento de reintentos con backoff exponencial (B3).

## Decisión

Se adopta el **Estilo Arquitectónico Microkernel (Arquitectura basada en Plugins)** para el servicio `SgoLogic.Notificaciones.Api`.

La arquitectura se divide en dos componentes fundamentales:

### 1. Sistema Central (Microkernel Core)
El núcleo o Microkernel implementa la infraestructura mínima indispensable y las reglas de orquestación transversales:
- **`INotificacionesKernel` / `NotificacionesKernel`**: Orquestador central encargado de recibir solicitudes de notificación (`SolicitudNotificacion`), resolver los plugins correspondientes según el canal o destino, coordinar la ejecución aislada y despachar reintentos.
- **`IPluginRegistry` / `PluginRegistry`**: Registro en memoria con concurrencia segura (`ConcurrentDictionary`) que gestiona el ciclo de vida de los plugins, su prioridad, metadatos y estado activo/inactivo en tiempo de ejecución.
- **Aislamiento y Resiliencia**: Si un plugin falla (por error de red, timeout o fallo del proveedor), el Kernel captura la anomalía, registra la incidencia en logs, añade la notificación a la tabla `notificacion_pendiente` con el canal específico y continúa el despacho de los demás plugins sin propagar excepciones hacia los consumidores ni hacia RabbitMQ.
- **Controlador de observabilidad (`KernelPluginsController`)**: Expone endpoints perimetrales (`GET /api/kernel/plugins`, `POST /api/kernel/plugins/{id}/toggle`, `POST /api/kernel/despachar`) para inspeccionar el catálogo de plugins y manipular su disponibilidad en caliente.

### 2. Componentes de Extensión (Plugins)
Cada canal de notificación se implementa como un plugin desacoplado que implementa el contrato unificado `INotificacionPlugin`:
- **`EmailNotificacionPlugin`**: Canal `EMAIL` (prioridad 100). Despacha correos electrónicos transaccionales a través de `IMockMensajeriaClient` hacia el mock de mensajería o servidor SMTP.
- **`SmsNotificacionPlugin`**: Canal `SMS` (prioridad 80). Despacha mensajes cortos de texto formateados para dispositivos celulares.
- **`PushNotificacionPlugin`**: Canal `PUSH` (prioridad 70). Despacha notificaciones instantáneas dirigidas a aplicaciones cliente o navegadores web.
- **`WebhookNotificacionPlugin`**: Canal `WEBHOOK` (prioridad 50). Dispara eventos HTTP estructurados hacia sistemas externos o servicios suscriptores.

Todos los plugins implementan `Metadata`, `EstaHabilitado`, `ConfigurarHabilitado(bool)` y `DespacharAsync(SolicitudNotificacion, CancellationToken)`.

## Alternativas consideradas

1. **Arquitectura Microkernel basada en Plugins (Elegida).**
   - *Ventajas:* Desacoplamiento total entre canales, alta cohesión, extensibilidad sin tocar el núcleo, compatibilidad absoluta con Docker Compose y entornos locales/nube sin dependencias externas obligatorias, y alta facilidad de prueba unitaria mediante mocks de plugins.
   - *Desventajas:* Requiere una capa adicional de abstracción y registro dinámico en el inyector de dependencias.

2. **Arquitectura Serverless / FaaS (Cloud Functions o Lambdas).**
   - *Se descarta:* Si bien encaja conceptualmente con la nube, dificulta la ejecución y pruebas de integración locales en Docker Compose sin emuladores pesados (como LocalStack) y genera dependencia de un proveedor de nube específico (AWS/GCP/Azure).

3. **Arquitectura Push en tiempo real exclusiva (SSE / WebSockets).**
   - *Se descarta como estilo único:* Aunque el canal Push en tiempo real es deseable para el frontend web, no resuelve los canales asíncronos obligatorios (correo y SMS para comprobantes, pagos y despacho). Se integra mejor como un plugin (`PushNotificacionPlugin`) dentro del Microkernel.

## Consecuencias

### Positivas
- **Extensibilidad:** Se pueden incorporar canales adicionales (por ejemplo Slack, WhatsApp Business o Firebase Cloud Messaging) simplemente agregando una nueva clase `INotificacionPlugin` y registrándola en el contenedor de dependencias, sin modificar el Kernel ni los consumidores.
- **Aislamiento de fallas:** El fallo de un proveedor externo en un canal no degrada la entrega en otros canales. Un fallo de SMS no impide que el correo o la notificación push se entreguen con éxito.
- **Reintentos independientes:** La persistencia de reintentos (`NotificacionPendiente`) almacena el canal específico que falló, permitiendo que el worker (`ReintentoNotificacionesWorker`) reintente únicamente sobre el plugin afectado.
- **Observabilidad en tiempo de ejecución:** El equipo de operaciones puede inspeccionar la lista de plugins y desactivar temporalmente un canal con degradación mediante `POST /api/kernel/plugins/{id}/toggle`.

### Negativas
- **Mayor número de clases:** Aumenta la cantidad de abstracciones en el proyecto `SgoLogic.Notificaciones.Api`.
- **Complejidad de configuración:** Cada plugin requiere su propia configuración de endpoints y credenciales cuando se conectan a proveedores reales de mensajería.
