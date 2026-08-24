# Sección 3 — Calidad y Despliegue


**Responsable:** Julio Herrera (202300850)

Cómo los Escenarios de Atributos de Calidad (EAC) de disponibilidad y rendimiento se resuelven en la arquitectura de despliegue de SGO-Logic (vista lógica y física), a partir de las decisiones de arquitectura ya acordadas por el equipo.

---

## Decisiones de arquitectura aplicadas

- **Punto de entrada:** API Gateway — único punto de entrada de todos los canales (Web, App, Centro de Atención). Autentica la petición contra Auth y enruta hacia el servicio correspondiente; ningún canal llega directo a un servicio de dominio.
- **Servicios:** catálogo cerrado, separados por dominio, bajo acoplamiento / alta cohesión — Auth, Órdenes (orquestador de la saga, escribe), Consulta de Estado (modelo de lectura, separado de Órdenes), Inventario, Pagos, Facturación, Despacho, Auditoría, Notificaciones, Integración ERP e Integración POS. El **Catálogo Comercial** (precios, promociones, combos y financiamiento) es una **dependencia externa**, no un servicio propio de SGO-Logic.
- **Orquestación:** Kubernetes on-premises, con capacitación del equipo presupuestada ($1,500–$4,000, dentro de la inversión inicial).
- **Mensajería:** un único clúster de RabbitMQ, configurado de tres formas según el caso — pub/sub (topic/fanout) para Inventario, Notificaciones, PayKlik, POS y LogiExpress; MOM para SAT; broker (cola) para el batch hacia ERP-Core cada 30 minutos. Ningún servicio publica directo a RabbitMQ: cada servicio de escritura persiste el evento en su propia base (patrón Outbox, ver más abajo) y un **Publicador** dedicado lo toma de ahí y lo envía a la cola.
- **Datos:** Redis (NoSQL) para lecturas rápidas (Consulta de Estado); una instancia SQL operacional para Órdenes/Inventario/Despacho; una instancia SQL separada para Finanzas (Pagos/Facturación). **Auditoría tiene su propia base de datos**, en una instancia separada de las dos anteriores, de solo inserción — es dueña exclusiva de sus datos, igual que cualquier otro contexto delimitado, y no comparte instancia con Finanzas.

---

## 1. Disponibilidad en Black Friday

**EAC:** el sistema no debe perder la orden si PayKlik falla o no responde durante el pico de demanda (hasta 8x lo normal); cada intento fallido queda auditado.

**Cómo se resuelve:**

- El servicio **Pagos** corre en Kubernetes con autoescalado horizontal (HPA) y al menos 2 réplicas activas en todo momento, para tolerar tanto la caída de PayKlik como la caída de un nodo propio.
- Antes de llamar a PayKlik, Pagos escribe el intento **y** un registro en su propia tabla-outbox dentro de SQL-Finanzas, en la misma transacción. Un **Publicador** lee esa tabla por consultas periódicas y recién ahí publica el evento en RabbitMQ (patrón Outbox real: la publicación nunca es directa ni depende de que RabbitMQ esté disponible en el instante del intento). Si PayKlik falla, la orden queda en estado pendiente/reintento sin bloquear el resto del flujo; RabbitMQ reintenta automáticamente (dead-letter exchange + TTL).
- Cada intento, exitoso o fallido, llega a **Auditoría** por ese mismo evento y se inserta en su propia base — sin tocar SQL-Finanzas.

## 2. Performance de consulta de estado (< 5 s)

**EAC:** una consulta de estado de orden (compra, pago, despacho, entrega) responde en menos de 5 segundos.

**Cómo se resuelve:**

- **Consulta de Estado** es un servicio separado de **Órdenes**: Órdenes escribe y orquesta la saga; Consulta de Estado solo lee. Mantiene un modelo materializado en **Redis**, actualizado de forma asíncrona al consumir los eventos que Órdenes, Inventario, Pagos, Facturación y Despacho publican vía RabbitMQ, en vez de orquestar en tiempo real llamadas a ERP-Core, POS, PayKlik y LogiExpress.
- Redis corre en el mismo datacenter/segmento de red que Consulta de Estado, evitando saltos de red entre sitios.
- Consulta de Estado escala horizontalmente en Kubernetes de forma independiente de Órdenes, porque su carga (lectura) y su patrón de escalado son distintos a los de escritura.

## 3. Performance de auditoría / Trazabilidad (< 1 s)

**EAC:** recuperar la evidencia completa de una transacción específica toma menos de 1 segundo; el 100% de las operaciones fiscalmente relevantes queda registrado de forma inmutable.

**Cómo se resuelve:**

- El servicio **Auditoría** consume, vía RabbitMQ, los eventos generados por todos los demás servicios y los inserta en **su propia base de datos** — una instancia separada, sin compartirla con SQL-Operacional ni con SQL-Finanzas — indexada por transacción. Auditoría es dueña exclusiva de sus datos; ningún otro contexto escribe ahí.
- Esa base tiene permisos restringidos a solo `INSERT` (sin `UPDATE` ni `DELETE`), lo que garantiza inmutabilidad sin necesitar una tecnología de auditoría especializada.
- Al recibir los datos de forma asíncrona, sus lecturas no compiten con la carga transaccional de Pagos ni de Facturación durante Black Friday, lo que sostiene el <1s.

---

## Diagrama de Despliegue Lógico

![Diagrama de Despliegue Lógico](../img/Diagrama%20de%20Despliegue%20Lógico.png)

## Diagrama de Despliegue Físico

![Diagrama de Despliegue Físico](../img/Diagrama%20de%20Despliegue%20Físico.png)


## Pendiente

- Definir el mínimo/máximo de réplicas de Pagos con pruebas de carga que simulen el pico de 8x, antes de la fecha fija de Black Friday.
- Formalizar estas decisiones como ADR (Contexto / Decisión / Alternativas / Consecuencias) y anexar estos diagramas a la entrega final.
- Revisar esta misma vista contra los EAC de Escalabilidad ante picos de demanda y Consistencia de inventario (prioridad Alta, no desarrollados aquí por no estar asignados a esta sección).
- Validar con el stakeholder el contrato exacto (formato, latencia) con el Catálogo Comercial externo, según lo señalado en `DDA_04_Drivers_Funcionales_CDU`.
