# SGO-Logic — DDA Fase 1 (Proyecto G12)

**Curso:** Análisis y Diseño de Sistemas 2, Sección A — USAC

SGO-Logic es el sistema de registro de órdenes y operación omnicanal que se propone para MegaStore, una cadena de tiendas por departamentos con presencia en Guatemala, El Salvador y Costa Rica. Su objetivo es que un cliente pueda iniciar, continuar y completar una compra desde tienda física, web, aplicación móvil o centro de atención con experiencia consistente, sobre una única visión de inventario, cobro, facturación fiscal y despacho — reemplazando los silos actuales entre canales y sistemas.

Este repositorio contiene la Fase 1 del proyecto: el Documento de Definición de Arquitectura (DDA), organizado por apartados en carpetas numeradas, más los Architecture Decision Records (ADR) y los diagramas del prototipo arquitectónico.

---

## Índice

### 0. Base del DDA
- [Catálogo de Evidencia (EV-##) y convención de identificadores](0.%20Base%20del%20DDA/DDA_00_Catalogo_Evidencia.md)
- [Diagrama de Bloques](0.%20Base%20del%20DDA/DDA_09_Diagrama_de_Bloques.md)

### 1. Enfoque de Negocio y Stakeholders
- [Stakeholders y Preocupaciones Arquitectónicas](1.%20Enfoque%20de%20Negocio%20y%20Stakeholders/DDA_01_Stakeholders_Preocupaciones.md)
- [Caso de Negocio: Casos de Uso de Negocio y Primera Descomposición](1.%20Enfoque%20de%20Negocio%20y%20Stakeholders/DDA_02_Casos_Uso_Negocio.md)

### 2. Funcionalidad y Experiencia
- [Características del Sistema Priorizadas](2.%20Funcionalidad%20y%20Experiencia/DDA_03_Caracteristicas_Priorizadas.md)
- [Drivers de Requisitos Funcionales y Casos de Uso Expandidos (CU-01 a CU-15)](2.%20Funcionalidad%20y%20Experiencia/DDA_04_Drivers_Funcionales_CDU.md)

### 3. Calidad y Despliegue
- [Drivers de Calidad — Escenarios de Atributos de Calidad (EAC-##)](3.%20Calidad%20y%20Despliegue/DDA_05_Drivers_de_Calidad_EAC.md)
- [Despliegue (vistas lógica y física)](3.%20Calidad%20y%20Despliegue/DDA_06_Despliegue.md)

### 4. Límites del Sistema
- [Drivers de Restricción (RES-##)](4.%20L%C3%ADmites%20del%20Sistema/DDA_07_Drivers_de_Restriccion_RES.md)
- [Restricciones de Infraestructura](4.%20L%C3%ADmites%20del%20Sistema/DDA_08_Drivers_de_Restriccion_Infraestructura.md)

### 5. Coherencia del Proyecto
- [Matriz de trazabilidad consolidada](5.%20Coherencia%20del%20Proyecto/Docu5.md) *(pendiente de elaboración)*

### 6. Prototipo Arquitectónico
- [Diagramas de casos de uso expandidos CU-01 a CU-15](6.%20Prototipo%20Arquitectonico/diagramas_CDU/)

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

### Diagramas de imagen
- [img/](img/) — Diagrama de Contexto, Diagrama de Bloques, Diagrama de Casos de Uso de Negocio de Alto Nivel, Diagrama de Despliegue Lógico, Diagrama de Despliegue Físico.
