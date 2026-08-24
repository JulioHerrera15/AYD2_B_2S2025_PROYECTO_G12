# ADR-001: Estilo de microservicios por contexto delimitado

**Estado:** Aceptada
**Responsable:** Angel Martinez (202300410)
**Fecha:** 2026-08-24

## Contexto

SGO-Logic debe integrar identidad de cliente, carrito, inventario, cobro, facturación fiscal, despacho, auditoría, notificaciones e integración con sistemas legacy (ERP-Core, POS) y externos (PayKlik, LogiExpress, SAT, proveedor de notificaciones), cada uno con reglas de negocio, ritmo de cambio y necesidades de escalado distintos. Hoy cada tienda y el canal de comercio electrónico operan con información separada, sin visión unificada (EV-07). El equipo interno domina Java, .NET y Docker básico (EV-46), y debe operar la solución dentro de un techo de USD 1,200/mes (EV-42) sosteniendo picos de hasta 8 veces la demanda normal (EV-27) sin que la falla de una parte del sistema arrastre al resto.

## Decisión

Se adopta un estilo de microservicios organizado por contexto delimitado (bounded context): Auth, Órdenes, Inventario, Pagos, Facturación, Despacho, Auditoría, Consulta de Estado, Integración ERP, Integración POS y Notificaciones. Cada contexto es dueño exclusivo de sus datos; ningún contexto accede directamente a la base de otro. La comunicación entre contextos ocurre mediante eventos (RabbitMQ) o llamadas explícitas, nunca por acceso compartido a esquema (ver `DDA_04_Drivers_Funcionales_CDU.md`, sección 2).

## Alternativas consideradas

[PENDIENTE-ANGEL: alternativas evaluadas para esta decisión]

## Consecuencias

### Positivas
- Cada servicio del Backbone escala de forma horizontal e independiente según su propio patrón de carga (soporta EAC-04, fuera del alcance de este documento pero habilitado por esta decisión).
- El aislamiento de fallas evita que la caída de un contexto bloquee a los demás (refuerza PRE-01, PRE-02).
- Permite repartir la responsabilidad de cada contexto entre distintos integrantes del equipo (Jonathan, Jose, Francisco, Julio, Angel) sin que sus cambios choquen entre sí.
- Resuelve de raíz el diagnóstico de silos de información entre tiendas y canal digital (EV-07).

### Negativas
- Incrementa la complejidad operativa: múltiples servicios, bases de datos y colas que desplegar, versionar y monitorear, frente al nivel de Docker básico que domina hoy el equipo (RES-12) — mitigado con capacitación en Kubernetes presupuestada explícitamente (USD 1,500–4,000).
- La consistencia entre contextos pasa a ser eventual, no transaccional; exige mecanismos explícitos de coordinación y compensación entre servicios (ver ADR-002).
- Aumenta la superficie a observar: cada contexto es un punto adicional de posible falla que debe monitorearse (CAR-12, Prometheus + Grafana).

## Preocupaciones y evidencia que atiende

PRE-01, PRE-02, PRE-11, PRE-17. Evidencia: EV-07, EV-27, EV-42, EV-46.
