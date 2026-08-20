# Drivers de Calidad — Escenarios de Atributos de Calidad (EAC)

**Responsable:** Jose Pirir (202300335) — Arquitecto de Resiliencia y DevOps
**Entregable:** 1 — Documento de Definición de Arquitectura (DDA)
**Sección:** Drivers de Calidad
**Caso:** SGO-Logic — MegaStore (Análisis y Diseño de Sistemas 2A)

---

## EAC-01 — Disponibilidad durante Black Friday

| Parte | Descripción |
|---|---|
| **Fuente del estímulo** | Usuarios finales (clientes) accediendo simultáneamente desde web, app móvil, centro de atención y tienda física |
| **Estímulo** | Incremento súbito y sostenido del volumen de tráfico y transacciones durante el evento Black Friday |
| **Ambiente** | Operación en producción durante Black Friday, con demanda de **hasta 8 veces** la de un día normal (dato explícito del caso: Carlos Méndez / jefe de operaciones) |
| **Artefacto** | Backbone del sistema omnicanal (carrito, checkout, catálogo, integración con POS/ERP) |
| **Respuesta** | El sistema escala horizontalmente los componentes críticos y mantiene el servicio activo sin caídas totales; ante fallo de una instancia, el tráfico se redirige automáticamente a instancias sanas |
| **Medida de respuesta** | Disponibilidad ≥ **99.9%** durante el evento; 0 caídas totales del servicio de checkout ante una demanda 8x |

---

## EAC-02 — Tolerancia a falla de la pasarela de pago (PayKlik)

| Parte | Descripción |
|---|---|
| **Fuente del estímulo** | Pasarela de pago externa PayKlik (proveedor con contrato vigente de 3 años, fuera del control de MegaStore) |
| **Estímulo** | Indisponibilidad o falla de PayKlik durante el procesamiento de un pago (ya ocurrió: 45 min caída en Black Friday anterior, ≈USD 150,000 en ventas perdidas) |
| **Ambiente** | Operación en pico de demanda (Black Friday, hasta 8x tráfico normal) |
| **Artefacto** | Servicio de Checkout / Pagos del Backbone |
| **Respuesta** | El sistema no pierde la orden ante la falla del proveedor: encola/reintenta el cobro o habilita una ruta alterna de pago sin depender exclusivamente de PayKlik; cada intento fallido queda registrado y es auditable |
| **Medida de respuesta** | 0 órdenes perdidas por indisponibilidad del proveedor de pago; 100% de los intentos fallidos quedan auditados (orden, canal, fecha, resultado) |

---

## EAC-03 — Rendimiento en consulta de estado de orden

| Parte | Descripción |
|---|---|
| **Fuente del estímulo** | Cliente o personal de atención consultando el estado de una orden desde cualquier dispositivo/canal |
| **Estímulo** | Solicitud de consulta del estado de una orden (confirmación, preparación, tránsito, entrega o incidencia) |
| **Ambiente** | Operación normal y en pico de tráfico (Black Friday) |
| **Artefacto** | Servicio de consulta de estado de órdenes del Backbone |
| **Respuesta** | El sistema retorna el estado actual de la orden al usuario |
| **Medida de respuesta** | Tiempo de respuesta **< 5 segundos** (el caso indica explícitamente que superar los 5s es inaceptable), incluso bajo carga pico |

---

## EAC-04 — Recuperación de evidencia transaccional para auditoría fiscal

| Parte | Descripción |
|---|---|
| **Fuente del estímulo** | Directora de Finanzas (Ana Lucía Fuentes) / auditoría anual de la SAT |
| **Estímulo** | Solicitud de reconstrucción de una transacción: quién la ejecutó, cuándo, y qué cambios se realizaron |
| **Ambiente** | Operación normal, en cualquier momento posterior a la venta (incluye ventanas de auditoría) |
| **Artefacto** | Registro/bitácora transaccional del Backbone, sincronizado con el ERP |
| **Respuesta** | El sistema recupera la evidencia completa de la transacción y la mantiene sincronizada con el ERP-Core (AS/400) |
| **Medida de respuesta** | Recuperación de evidencia de cualquier transacción en **< 1 segundo**; 0 transacciones sin respaldo (evita multas/inconsistencias fiscales) |

---

## Trazabilidad con el Diagrama de Despliegue

Estos 4 EAC deben verse reflejados por Julio Herrera y Jose Pirir en el **Diagrama de Despliegue**:

- **EAC-01 (Disponibilidad 8x):** múltiples réplicas/contenedores de servicios críticos, balanceo de carga, auto-recuperación (health checks, restart automático), compatible con **on-premises** y presupuesto de **$1,200/mes**.
- **EAC-02 (Falla de PayKlik):** cola de reintentos/circuit breaker en la Capa Anticorrupción frente a PayKlik, almacenamiento local de intentos fallidos para auditoría, sin bloquear el flujo de checkout.
- **EAC-03 (Consulta de estado < 5s):** caché y cercanía de la BD al servicio de consulta de órdenes.
- **EAC-04 (Evidencia transaccional < 1s):** bitácora/registro transaccional de baja latencia, sincronización periódica con el ERP (que procesa lotes cada 30 min vía CSV/XML).
