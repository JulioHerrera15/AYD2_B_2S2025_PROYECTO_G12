# Sección 4 — Límites del Sistema: Drivers de Restricción (RES-##)

**Curso:** Análisis y Diseño de Sistemas 2, Sección A — USAC
**Documento:** Apartado del Documento de Definición de Arquitectura (DDA)
**Elaborado por:** Francisco Gerardo Castillo Sagastume (202300653)
**Basado en:** `DDA_00_Catálogo_Evidencia`, `Restricciones_de_Infraestructura`

---

## 1. Propósito

Este documento formaliza como drivers de restricción (`RES-##`) todas las filas del catálogo de evidencia cuya columna "Driver candidato" incluye `RES`. A diferencia de un escenario de calidad (`EAC-##`), que es medible y admite negociaciones, una restricción **elimina alternativas de diseño sin negociación posible**.

Cada `RES-##` referencia su evidencia (`EV-##`), indica qué alternativas descarta y qué decisión de arquitectura ya la satisface (o si queda pendiente). Esta es la fuente que alimenta la matriz de trazabilidad.

---

## 2. Tabla resumen

| ID | Enunciado (resumen) | Fuente | Categoría | Responsable |
|---|---|---|---|---|
| RES-01 | No modificar ni reemplazar el ERP-Core (AS/400) | EV-13, EV-14, EV-40 | Técnica / gobierno | Francisco |
| RES-02 | Integración con el ERP solo vía archivo CSV/XML por lotes cada 30 min | EV-15 | Técnica | Francisco |
| RES-03 | No reemplazar los POS; recibir solo eventos salientes, con reintentos | EV-16, EV-40 | Técnica / gobierno | Francisco |
| RES-04 | PayKlik se integra tal cual, sin reemplazo, respetando el contrato vigente | EV-17, EV-45 | Contractual | Francisco |
| RES-05 | LogiExpress se integra tal cual, sin reemplazo, respetando el contrato vigente | EV-18, EV-45 | Contractual | Francisco |
| RES-06 | El proveedor de notificaciones es externo; su falla no puede bloquear el flujo principal | EV-20 | Técnica | Francisco |
| RES-07 | Facturación fiscal vía XML a la SAT es obligatoria antes de finalizar la orden | EV-19 | Regulatoria | Francisco |
| RES-08 | No se almacenan números completos de tarjeta ni CVV | EV-25 | Regulatoria / seguridad | Francisco |
| RES-09 | Cumplimiento de protección de datos y normativa SAT en los tres países de operación | EV-44 | Regulatoria | Francisco |
| RES-10 | Inversión inicial total del proyecto: USD 150,000 (no es techo por componente) | EV-41 | Económica | Julio |
| RES-11 | Costo operativo mensual post-producción ≤ USD 1,200 sin aprobación adicional | EV-42 | Económica | Julio |
| RES-12 | El equipo interno domina Java/.NET y Docker básico, no Kubernetes | EV-37, EV-46 | Organizacional | Julio |
| RES-13 | Minimizar el consumo energético; mantener la facturación totalmente electrónica | EV-47 | Sostenibilidad | Julio |
| RES-14 | Despliegue inicial on-premises, reutilizando infraestructura existente | EV-48 | Infraestructura | Julio |
| RES-15 | La arquitectura debe permitir una migración planificada a la nube sin rediseño | EV-49 | Infraestructura | Julio |
| RES-16 | Notación UML 2.5.1 obligatoria; toda notación complementaria debe declararse | EV-50 | Proceso | Angel |
| RES-17 | Tablero Kanban de 6 columnas fijas, priorizando componentes base | EV-51 | Proceso | Jose |
| RES-18 | Repositorio privado de GitHub centralizado, con nomenclatura oficial | EV-52 | Proceso | Jose |
| RES-19 | Los ADR son el registro vivo de decisiones; los diagramas son vistas del DDA | EV-53 | Proceso | Angel |

---

## 3. Detalle por restricción

### 3.1 Integración con sistemas legacy (no negociable)

**RES-01 — No modificar ni reemplazar el ERP-Core**
- **Enunciado:** SGO-Logic NO debe requerir modificación ni reemplazo del ERP-Core (AS/400). Toda integración se adapta a los mecanismos existentes del ERP, nunca al revés.
- **Fuente:** EV-13, EV-14 (evidencia del enunciado), EV-40 (confirmación de Junta Directiva).
- **Elimina:** acceso directo a la base de datos del ERP, API moderna, sincronización en tiempo real hacia contabilidad, cualquier propuesta que cambie el funcionamiento interno con el ERP.
- **Decisión que la satisface:** Broker + worker batch y Anti-Corruption Layer dedicada al ERP.
- **Nota:** Carlos Méndez tiene poder de rechazo explícito sobre cualquier propuesta que la viole (EV-37).

**RES-02 — Mecanismo único de integración con el ERP**
- **Enunciado:** La única vía para enviar información al ERP es un archivo CSV o XML, procesado en ventanas de 30 minutos.
- **Fuente:** EV-15.
- **Elimina:** Integración síncrona o en tiempo real hacia el ERP y consistencia contable inmediata.
- **Decisión que la satisface:** worker que consolida eventos de venta y genera el lote cada 30 min.

**RES-03 — No reemplazar los POS, solo recepción de eventos**
- **Enunciado:** Las 32 instancias de POS no se reemplazan. SGO-Logic únicamente les envía eventos salientes, con manejo de errores y reintentos; el POS no es fuente de datos hacia el sistema.
- **Fuente:** EV-16, EV-40 (ratificación de Junta), EV-37 (veto de Carlos Méndez).
- **Elimina:** cualquier diseño que asuma que el POS consulta inventario central o expone su propia API hacia SGO-Logic.
- **Decisión que la satisface:** Pub/Sub (RabbitMQ) con dead-letter exchange + TTL para reintentos.

### 3.2 Proveedores externos contractuales

**RES-04 — PayKlik se integra tal cual**
- **Enunciado:** PayKlik no se reemplaza durante el horizonte del proyecto y el contrato vigente (3 años) debe respetarse. Cualquier resiliencia se implementa del lado de SGO-Logic.
- **Fuente:** EV-17, EV-45.
- **Elimina:** cambio de pasarela de pago, procesador propio de tarjetas (ver también la restricción que detalla el pago RES-08).

**RES-05 — LogiExpress se integra tal cual**
- **Enunciado:** LogiExpress no se reemplaza, el contrato vigente (2 años) debe respetarse. La tasa de fallo declarada se compensa con resiliencia propia, no con cambio de proveedor.
- **Fuente:** EV-18, EV-45.
- **Elimina:** integración de un segundo proveedor logístico como sustituto (sí sería aceptable como redundancia aditiva, no sustitutiva).
- **Decisión que la satisface:** Anti-Corruption Layer + Circuit Breaker en el adaptador de LogiExpress.

**RES-06 — Falla del proveedor de notificaciones no bloquea el flujo**
- **Enunciado:** El proveedor de SMS/correo/push está fuera del control de la organización; su indisponibilidad nunca puede impedir que la orden avance.
- **Fuente:** EV-20; reforzado por EV-34.
- **Elimina:** cualquier paso síncrono de la saga de la orden que dependa de la confirmación de una notificación.
- **Decisión que la satisface:** notificaciones desacopladas vía Pub/Sub, al final del flujo.

### 3.3 Regulatorias y de cumplimiento

**RES-07 — Facturación fiscal obligatoria antes de finalizar la orden**
- **Enunciado:** Toda orden requiere una factura XML certificada por la SAT antes de considerarse finalizada.
- **Fuente:** EV-19.
- **Elimina:** confirmar una orden como "cerrada" sin certificación fiscal completa.
- **Decisión que la satisface:** canal MOM dedicado a la SAT; estado "Confirmada – Pendiente Fiscal" (ver CU-04 en `DDA_04_Drivers_Funcionales_CDU`).
- **Nota:** El equipo ya adoptó como supuesto que es el cierre fiscal.

**RES-08 — No se almacenan datos sensibles de tarjeta**
- **Enunciado:** No se guardan números completos de tarjeta ni códigos CVV bajo ninguna circunstancia.
- **Fuente:** EV-25.
- **Elimina:** cualquier diseño de "procesador de pagos propio" como forma de independizarse de PayKlik.
- **Decisión que la satisface:** tokenización delegada a PayKlik; Pagos solo almacena el resultado y metadatos del intento.

**RES-09 — Cumplimiento normativo en los tres países**
- **Enunciado:** La solución debe operar conforme a la normativa de protección de datos aplicable y a las disposiciones de facturación electrónica de la SAT.
- **Fuente:** EV-44.
- **Elimina:** un modelo de datos único sin distinción de residencia/tratamiento por país.
- **Decisión que la satisface:** Cifrado por país.

### 3.4 Económicas

**RES-10 — Techo de inversión inicial**
- **Enunciado:** USD 150,000 cubre todo el proyecto (análisis, desarrollo e infraestructura de arranque). No es un presupuesto por componente, sino un fondo compartido para todo el proyecto.
- **Fuente:** EV-41.
- **Elimina:** sumar alternativas sin verificar que compiten por el mismo fondo total.
- **Decisión que la satisface:** ver tabla de costos declarados en `Restricciones_de_Infraestructura` (capacitación Kubernetes USD 1,500–4,000 tomada en cuenta).

**RES-11 — Techo operativo mensual**
- **Enunciado:** El costo operativo mensual de la infraestructura propia de SGO-Logic no debe superar USD 1,200 sin aprobación adicional de la Junta. No incluye las tarifas contractuales de PayKlik ni LogiExpress, que son costos comerciales independientes ya aprobados por la dirección de MegaStore.
- **Fuente:** EV-42.
- **Elimina:** servicios administrados de nube con precio elástico, licenciamiento de software pago.
- **Decisión que la satisface:** stack 100% open source (Kubernetes, RabbitMQ, Redis, SQL) sobre hardware on-premises ya pagado (`Restricciones_de_Infraestructura`).
- **Riesgo abierto declarado:** El monto económico exacto depende del escalamiento del sistema (HPA) para el pico de Black Friday. Si lo supera de forma sostenida, requiere aprobación adicional de Junta debido a que no puede excederse los 1200$.

### 3.5 Organizacional

**RES-12 — Capacidad técnica del equipo interno**
- **Enunciado:** El equipo domina Java y .NET, y tiene solo conocimientos básicos de Docker (no de Kubernetes). Cualquier tecnología fuera de ese conjunto debe justificar impacto de adopción, capacitación y operación.
- **Fuente:** EV-46, reforzado por la exigencia de mantenibilidad interna de Carlos Méndez (EV-37).
- **Elimina:** tecnologías difíciles de aprender o tecnologías sin un plan de aprendizaje previo.
- **Decisión que la satisface:** Kubernetes se adopta igual, pero con capacitación presupuestada explícitamente (USD 1,500–4,000, ver RES-10) como justificación del salto de complejidad frente a Docker simple.

### 3.6 Sostenibilidad

**RES-13 — Eficiencia energética**
- **Enunciado:** Minimizar el consumo energético de la infraestructura y mantener la facturación totalmente electrónica.
- **Fuente:** EV-47.
- **Elimina:** Sobreuso permanente de cómputo "por si acaso" fuera de los picos declarados (Black Friday).
- **Decisión que la satisface:** escalado horizontal bajo demanda vía HPA en Kubernetes, en vez de capacidad fija sobredimensionada.

### 3.7 Infraestructura y despliegue

**RES-14 — Despliegue inicial on-premises**
- **Enunciado:** El despliegue inicial debe realizarse en infraestructura física propia de MegaStore.
- **Fuente:** EV-48.
- **Elimina:** cualquier servicio administrado de nube en la fase inicial.
- **Decisión que la satisface:** Kubernetes on-premises; Redis on-premises en vez de DynamoDB.

**RES-15 — Portabilidad hacia la nube sin rediseño**
- **Enunciado:** La arquitectura debe permitir una migración planificada a la nube conforme crezca la demanda, sin rediseñar los componentes.
- **Fuente:** EV-49.
- **Elimina:** dependencias directas de hardware físico específico en la lógica de negocio; acoplamiento a APIs no portables.
- **Decisión que la satisface:** todo empaquetado en contenedores Kubernetes (portable a EKS/AKS/GKE); ruta de migración ya definida para Redis→DynamoDB y para las instancias SQL (`Restricciones_de_Infraestructura`) . RabbitMQ es igualmente portable sin cambios en los servicios que lo consumen.

---

## 4. Trazabilidad RES → Decisión de arquitectura

| RES | Decisión que la satisface | Estado |
|---|---|---|
| RES-01 | Broker + worker batch, ACL dedicada al ERP (Decisiones §2.4) | Cubierta |
| RES-02 | Worker de consolidación cada 30 min (Decisiones §2.4) | Cubierta |
| RES-03 | Pub/Sub con dead-letter exchange + TTL (Decisiones §2.4) | Cubierta |
| RES-04 | Circuit Breaker + Outbox + Idempotencia en adaptador de Pagos | Cubierta |
| RES-05 | ACL + Circuit Breaker en adaptador de LogiExpress | Cubierta |
| RES-06 | Notificaciones desacopladas vía Pub/Sub | Cubierta |
| RES-07 | Canal MOM dedicado a SAT | Cubierta, con vacío VAC-03 abierto |
| RES-08 | Tokenización delegada a PayKlik | Cubierta |
| RES-09 | — | **Hueco: sin decisión explícita** |
| RES-10 | Tabla de costos declarados (`Restricciones_de_Infraestructura` §1) | Cubierta |
| RES-11 | Stack open source on-premises | Cubierta, con riesgo abierto de dimensionamiento |
| RES-12 | Capacitación Kubernetes presupuestada | Cubierta |
| RES-13 | HPA / escalado bajo demanda (Decisiones §2.5) | Cubierta |
| RES-14 | Kubernetes on-premises, Redis on-premises | Cubierta |
| RES-15 | Contenedores portables; ruta Redis→DynamoDB definida | Cubierta |

---

## 5. Vacíos y supuestos relacionados

| ID | Vacío | Afecta a | Estado |
|---|---|---|---|
| VAC-03 | Definición precisa de "finalizar la orden" respecto a la SAT | RES-07 | Supuesto adoptado (cierre fiscal) |
| VAC-06 | Inventario de hardware existente on-premises | RES-10, RES-14 | Sin resolver y afecta cómo se reparte el presupuesto entre capex y opex |

---
