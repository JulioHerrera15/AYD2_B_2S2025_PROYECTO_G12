# SGO-Logic — DDA Fase 1 (Proyecto G12)

**Curso:** Análisis y Diseño de Sistemas 2, Sección B — USAC

SGO-Logic es el sistema de registro de órdenes y operación omnicanal que se propone para MegaStore, una cadena de tiendas por departamentos con presencia en Guatemala, El Salvador y Costa Rica. Su objetivo es que un cliente pueda iniciar, continuar y completar una compra desde tienda física, web, aplicación móvil o centro de atención con experiencia consistente, sobre una única visión de inventario, cobro, facturación fiscal y despacho — reemplazando los silos actuales entre canales y sistemas.

Este repositorio contiene la Fase 1 del proyecto: el Documento de Definición de Arquitectura (DDA) consolidado, los Architecture Decision Records (ADR), los diagramas de imagen y los diagramas del prototipo arquitectónico.

---

## Índice

### Documento principal
- [DDA_SGOLogic.md](docs/DDA_SGOLogic.md) — Documento de Definición de Arquitectura completo: catálogo de evidencia, stakeholders y preocupaciones, casos de uso de negocio, características priorizadas, drivers funcionales y casos de uso expandidos (CU-01 a CU-15), drivers de calidad, drivers de restricción, restricciones de infraestructura, ADR, vistas arquitectónicas y matrices de trazabilidad.

### ADR — Architecture Decision Records
- [ADR-001 Estilo de microservicios por contexto delimitado](ADR/ADR-001-estilo-microservicios-por-contexto-delimitado.md)
- [ADR-002 Saga orquestada centralizada para el ciclo de vida de la orden](ADR/ADR-002-saga-orquestada-centralizada.md)
- [ADR-003 Patrón Outbox con polling publisher](ADR/ADR-003-patron-outbox-polling-publisher.md)
- [ADR-004 CQRS ligero con Redis como modelo de lectura](ADR/ADR-004-cqrs-redis-modelo-lectura.md)
- [ADR-005 Bloqueo optimista con versión + Redis TTL para reserva de inventario](ADR/ADR-005-bloqueo-optimista-reserva-inventario.md)
- [ADR-006 Almacén de auditoría append-only en base separada](ADR/ADR-006-auditoria-append-only-base-separada.md)
- [ADR-007 Capa anticorrupción para el ERP AS/400](ADR/ADR-007-capa-anticorrupcion-erp-as400.md)
- [ADR-008 Integración batch CSV/XML cada 30 minutos con el ERP](ADR/ADR-008-integracion-batch-erp-30-minutos.md)
- [ADR-009 Circuit breaker y reintentos para PayKlik y LogiExpress](ADR/ADR-009-circuit-breaker-reintentos-payklik-logiexpress.md)
- [ADR-010 Tokenización y no almacenamiento de datos de tarjeta](ADR/ADR-010-tokenizacion-no-almacenamiento-datos-tarjeta.md)
- [ADR-011 Certificación fiscal asíncrona ante la SAT](ADR/ADR-011-certificacion-fiscal-asincrona-sat.md)
- [ADR-012 Propagación confiable de eventos al POS](ADR/ADR-012-propagacion-eventos-pos.md)

### Diagramas de casos de uso expandidos
- [diagramas_CDU/](diagramas_CDU/) — Diagramas de los casos de uso expandidos CU-01 a CU-15 (formatos editable `.drawio` y de imagen).

### Diagramas de imagen
- [img/](img/) — Diagrama de Contexto, Diagrama de Bloques, Diagrama de Componentes, Diagrama de Casos de Uso de Negocio de Alto Nivel, Diagrama de Despliegue Lógico, Diagrama de Despliegue Físico, Flujo Principal del Sistema.
