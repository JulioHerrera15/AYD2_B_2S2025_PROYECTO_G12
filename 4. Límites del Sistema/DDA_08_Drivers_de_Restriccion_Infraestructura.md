# Sección 4 — Límites del Sistema: Restricciones de Infraestructura

**Responsable:** Julio Herrera (202300850)

---

## 1. Presupuesto

- **Inversión inicial:** $150,000 aprobados por la Junta Directiva para toda la iniciativa SGO-Logic (análisis, desarrollo e infraestructura de puesta en marcha). No es un techo por componente — es el total del proyecto, por lo que cada alternativa arquitectónica compite por una porción de ese mismo fondo.
- **Costo operativo mensual:** no debe superar $1,200 una vez el sistema esté en operación, sin justificación y aprobación adicional de la Junta. Esto cubre hosting/infraestructura propia de SGO-Logic (cómputo, almacenamiento, licencias, soporte) — no incluye las tarifas contractuales ya vigentes de PayKlik y LogiExpress, que son costos comerciales independientes ya aprobados.

**Cómo se cumple con las decisiones ya tomadas:**

- Kubernetes, RabbitMQ, Redis y los motores SQL elegidos son todos **software libre / sin costo de licenciamiento**, por lo que el gasto operativo mensual se concentra en cómputo y almacenamiento — ya cubiertos por el hardware on-premises existente — y no en tarifas de licencia ni en servicios administrados de nube con precio elástico.
- El único costo adicional identificado hasta ahora es la **capacitación en Kubernetes ($1,500–$4,000)**, que se carga a la inversión inicial de $150,000, no al techo operativo de $1,200/mes.

**Declaración de costos (para el ADR):**

| Concepto | Tipo | Monto |
|---|---|---|
| Capacitación Kubernetes del equipo | Inversión inicial | $1,500 – $4,000 |
| Infraestructura de cómputo/almacenamiento (Kubernetes, RabbitMQ, Redis, SQL) | Operativo mensual | Por definir — se estima dentro del techo de $1,200 al reutilizar hardware existente y no pagar licencias |
| Licenciamiento de software | — | $0 (todas las tecnologías elegidas son open source) |

Falta afinar el monto operativo mensual exacto una vez se dimensione el hardware necesario para sostener el pico de Black Friday (réplicas de Pagos vía HPA); si ese dimensionamiento superara $1,200/mes de forma sostenida, debe señalarse como riesgo abierto y llevarse a aprobación adicional de la Junta.

## 2. Despliegue on-premises

- **Requisito:** el despliegue inicial de SGO-Logic debe ser on-premises, usando la infraestructura física con la que ya cuenta MegaStore.
- **Migración a nube:** la arquitectura debe permitir moverse a la nube conforme crezca la demanda y la operación regional (Guatemala, El Salvador, Costa Rica), sin rediseñar los componentes.

**Cómo se cumple con las decisiones ya tomadas:**

- Todos los servicios corren empaquetados como contenedores en Kubernetes, lo que ya garantiza la portabilidad exigida: el mismo empaquetado puede moverse a un clúster Kubernetes administrado en la nube (EKS, AKS, GKE) sin rediseño de la aplicación.
- **Redis** es la pieza de datos que hoy corre on-premises con una ruta de migración ya definida: se reemplaza por **DynamoDB** cuando se ejecute el salto a nube — se descartó usar DynamoDB desde ahora porque es un servicio administrado que no es desplegable on-premises.
- Las instancias **SQL** (operacional, y finanzas/auditoría) seguirían el mismo principio en esa fase futura: migrar a un motor administrado equivalente en la nube, manteniendo los límites de esquema ya definidos por servicio.
- **RabbitMQ** también es portable: puede correr tanto on-premises como en un clúster Kubernetes en la nube sin cambios en la lógica de los servicios que lo consumen.

## 3. Relación entre ambas restricciones

Presupuesto y on-premises se refuerzan mutuamente: el techo de $1,200/mes es alcanzable porque el despliegue inicial reutiliza infraestructura ya pagada y tecnologías sin costo de licencia, en vez de contratar cómputo o servicios administrados en la nube por demanda. El único gasto que rompe con "cero costo adicional" es la capacitación en Kubernetes, y por eso se declaró explícitamente como parte de la inversión inicial — no del operativo mensual — para no comprometer el techo de $1,200.

Cualquier ADR que proponga adelantar componentes a la nube antes de lo planeado (por ejemplo, mover Redis a DynamoDB antes de tiempo) debe mostrar que sigue cumpliendo el techo mensual, o solicitar aprobación adicional de la Junta Directiva.
