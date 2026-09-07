# SGO-Logic (Proyecto G12)

**Curso:** Análisis y Diseño de Sistemas 2, Sección B — USAC

SGO-Logic es el sistema de registro de órdenes y operación omnicanal que se propone para MegaStore, una cadena de tiendas por departamentos con presencia en Guatemala, El Salvador y Costa Rica. Su objetivo es que un cliente pueda iniciar, continuar y completar una compra desde tienda física, web, aplicación móvil o centro de atención con experiencia consistente, sobre una única visión de inventario, cobro, facturación fiscal y despacho — reemplazando los silos actuales entre canales y sistemas.

El repositorio se organiza en dos partes:

- [`documentacion/`](documentacion/) — Documento de Definición de Arquitectura (DDA), Architecture Decision Records (ADR), diagramas, gestión de proyecto y contratos de eventos entre contextos.
- [`SGO-Logic/`](SGO-Logic/) — prototipo del sistema en ASP.NET Core (.NET 8), construido a partir de las decisiones de arquitectura documentadas. En desarrollo activo.

---

## Índice

### Documentación de arquitectura
- [docs/DDA_SGOLogic.md](documentacion/docs/DDA_SGOLogic.md) — Documento de Definición de Arquitectura completo: catálogo de evidencia, stakeholders y preocupaciones, casos de uso de negocio, características priorizadas, drivers funcionales y casos de uso expandidos (CU-01 a CU-15), drivers de calidad, drivers de restricción, restricciones de infraestructura, ADR, vistas arquitectónicas y matrices de trazabilidad.
- [docs/contratos/](documentacion/docs/contratos/) — Contratos de eventos publicados entre contextos (p. ej. [ordenes-eventos-v1.md](documentacion/docs/contratos/ordenes-eventos-v1.md)).

#### ADR — Architecture Decision Records
- [ADR-001 Estilo de microservicios por contexto delimitado](documentacion/ADR/ADR-001-estilo-microservicios-por-contexto-delimitado.md)
- [ADR-002 Saga orquestada centralizada para el ciclo de vida de la orden](documentacion/ADR/ADR-002-saga-orquestada-centralizada.md)
- [ADR-003 Patrón Outbox con polling publisher](documentacion/ADR/ADR-003-patron-outbox-polling-publisher.md)
- [ADR-004 CQRS ligero con Redis como modelo de lectura](documentacion/ADR/ADR-004-cqrs-redis-modelo-lectura.md)
- [ADR-005 Bloqueo optimista con versión + Redis TTL para reserva de inventario](documentacion/ADR/ADR-005-bloqueo-optimista-reserva-inventario.md)
- [ADR-006 Almacén de auditoría append-only en base separada](documentacion/ADR/ADR-006-auditoria-append-only-base-separada.md)
- [ADR-007 Capa anticorrupción para el ERP AS/400](documentacion/ADR/ADR-007-capa-anticorrupcion-erp-as400.md)
- [ADR-008 Integración batch CSV/XML cada 30 minutos con el ERP](documentacion/ADR/ADR-008-integracion-batch-erp-30-minutos.md)
- [ADR-009 Circuit breaker y reintentos para PayKlik y LogiExpress](documentacion/ADR/ADR-009-circuit-breaker-reintentos-payklik-logiexpress.md)
- [ADR-010 Tokenización y no almacenamiento de datos de tarjeta](documentacion/ADR/ADR-010-tokenizacion-no-almacenamiento-datos-tarjeta.md)
- [ADR-011 Certificación fiscal asíncrona ante la SAT](documentacion/ADR/ADR-011-certificacion-fiscal-asincrona-sat.md)
- [ADR-012 Propagación confiable de eventos al POS](documentacion/ADR/ADR-012-propagacion-eventos-pos.md)

#### Diagramas
- [diagramas_CDU/](documentacion/diagramas_CDU/) — Diagramas de los casos de uso expandidos CU-01 a CU-15 (formatos editable `.drawio` y de imagen).
- [img/](documentacion/img/) — Diagrama de Contexto, Diagrama de Bloques, Diagrama de Casos de Uso de Negocio de Alto Nivel, Diagrama de Despliegue Lógico, Diagrama de Despliegue Físico, Flujo Principal del Sistema, mockups de UI.

#### Gestión de Proyecto
- [Gestion_Proyecto/Plan_Tablero_Trello.md](documentacion/Gestion_Proyecto/Plan_Tablero_Trello.md)
- [Gestion_Proyecto/Backlog_Historias_Usuario.md](documentacion/Gestion_Proyecto/Backlog_Historias_Usuario.md)

### Implementación
- [SGO-Logic/](SGO-Logic/) — Prototipo ASP.NET Core (.NET 8) del sistema, alineado con el estilo de microservicios por contexto delimitado definido en ADR-001.
