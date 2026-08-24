# Diagrama de Bloques — SGO-Logic

**Curso:** Análisis y Diseño de Sistemas 2, Sección A — USAC
**Documento:** Apartado del Documento de Definición de Arquitectura (DDA)
**Elaborado por:** Angel Martinez (202300410)

---

## Declaración de notación (requerida por EV-50)

Este diagrama **no usa notación UML**. Usa una notación de bloques por capas, de estilo C4 (vista de contenedores/componentes agrupados por capa arquitectónica), tal como exige el enunciado declarar explícitamente cuando se usa una notación complementaria a UML 2.5.1 (EV-50, `DDA_00_Catalogo_Evidencia.md`).

El diagrama organiza el sistema en siete capas horizontales:

1. **Canales de Cliente y Personal** — Web, App Móvil, Centro de Atención, Punto de Venta en Tienda.
2. **Capa de Borde** — API Gateway y Auth como punto único de entrada y autenticación.
3. **Capa de Aplicación / Contextos Delimitados** (una base de datos por contexto) — Órdenes (orquestador de la saga), Inventario, Pagos, Facturación, Despacho, Consulta de Estado (lectura/CQRS), Auditoría (registro inmutable), Notificaciones, Integración ERP e Integración POS.
4. **Mensajería** — Clúster de RabbitMQ, operando en modo pub/sub, MOM y batch según el destino.
5. **Capa de Datos** (una base por contexto, sin esquemas compartidos) — Redis Carrito, Redis Lectura de Órdenes, SQL Operacional (Inventario/Órdenes), SQL Finanzas (Pagos/Facturación) y el almacén de Auditoría de solo inserción.
6. **Dependencias Externas** — PayKlik, LogiExpress, SAT, Proveedor de Notificaciones, ERP-Core AS/400, POS de las 32 tiendas, y el Catálogo Comercial (fuera del alcance de SGO-Logic).
7. **Observabilidad** — Prometheus + Grafana, monitoreando las dependencias externas.

## Diagrama

![Diagrama de Bloques](../img/Diagrama%20de%20bloques.png)

---

*Este diagrama es una vista de arquitectura general y complementa, sin sustituir, al Diagrama de Contexto (responsabilidad de Francisco) y a los Diagramas de Despliegue Lógico/Físico (`DDA_06_Despliegue.md`, responsabilidad de Jose/Julio).*
