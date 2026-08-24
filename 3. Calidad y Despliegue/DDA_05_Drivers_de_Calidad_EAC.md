# Sección 3 — Calidad y Despliegue: Drivers de Calidad (EAC-##)

**Curso:** Análisis y Diseño de Sistemas 2, Sección A — USAC
**Documento:** Apartado del Documento de Definición de Arquitectura (DDA)
**Elaborado por:** Jose Pirir (202300335)
**Basado en:** `DDA_00_Catálogo_Evidencia`
**Se refleja en:** `DDA_06_Despliegue` (Julio Herrera)

---

## 1. Propósito

Este documento formaliza como Escenarios de Atributos de Calidad (`EAC-##`) las filas del catálogo de evidencia cuya columna "Driver candidato" incluye `EAC`, usando el formato estándar de seis partes: **fuente del estímulo, estímulo, ambiente, artefacto, respuesta y medida de respuesta**. Cada escenario referencia su evidencia (`EV-##`) para mantener la trazabilidad mecánica exigida por `DDA_00`.

Los EAC-01, EAC-02 y EAC-03 ya están resueltos arquitectónicamente en `DDA_06_Despliegue`; aquí se documentan en su formato formal. EAC-04 y EAC-05 son los dos escenarios de prioridad Alta que `DDA_06` señaló como pendientes de desarrollar en esta sección.

---

## 2. Tabla resumen

| ID | Atributo de calidad | Enunciado resumen | Evidencia | Prioridad |
|---|---|---|---|---|
| EAC-01 | Disponibilidad | El sistema no pierde la orden si PayKlik falla durante el pico de Black Friday (hasta 8x); cada intento fallido queda auditado | EV-11, EV-27, EV-28 | Alta |
| EAC-02 | Rendimiento (consulta) | Consultar el estado de una orden responde en menos de 5 segundos | EV-33 | Alta |
| EAC-03 | Rendimiento (auditoría) | Recuperar la evidencia completa de una transacción toma menos de 1 segundo | EV-30 | Alta |
| EAC-04 | Escalabilidad | El sistema escala tanto ante el crecimiento sostenido del canal digital (15% anual) como ante picos estacionales (8x en Black Friday), sin rediseño | EV-01, EV-02, EV-27 | Alta |
| EAC-05 | Consistencia de inventario | Ninguna unidad de inventario se compromete a más de un cliente al mismo tiempo, incluso con reservas concurrentes | EV-22, EV-23 | Alta |

---

## 3. Detalle por escenario

### EAC-01 — Disponibilidad ante falla del proveedor de pago (Black Friday)

| Parte | Descripción |
|---|---|
| **Fuente del estímulo** | Pasarela de pago externa PayKlik (contrato vigente, latencia variable, fuera del control de MegaStore) |
| **Estímulo** | Indisponibilidad o falla al procesar un cobro durante el pico de demanda |
| **Ambiente** | Operación en producción durante Black Friday, con demanda de hasta **8 veces** la de un día normal |
| **Artefacto** | Servicio de Pagos (y su ruta de reintento vía Outbox/RabbitMQ, ver `DDA_06_Despliegue`) |
| **Respuesta** | La orden no se pierde: el intento de cobro queda registrado antes de invocar a PayKlik; si falla, el evento se reintenta de forma asíncrona sin bloquear el resto del flujo; cada intento (éxito o fallo) se envía a Auditoría |
| **Medida de respuesta** | 0 órdenes perdidas por indisponibilidad del proveedor de pago; 100% de los intentos fallidos quedan auditados (orden, canal, fecha, resultado); disponibilidad del servicio de Pagos ≥ 99.9% durante el evento |
| **Evidencia** | EV-11 (caída histórica de 45 min, ≈USD 150,000 perdidos), EV-27 (demanda 8x), EV-28 (no perder la orden, procesar sin depender del proveedor, auditar fallos) |

---

### EAC-02 — Rendimiento en consulta de estado de orden

| Parte | Descripción |
|---|---|
| **Fuente del estímulo** | Cliente o personal de atención, desde cualquier dispositivo/canal |
| **Estímulo** | Solicitud de consulta del estado de una orden (confirmación, preparación, tránsito, entrega o incidencia) |
| **Ambiente** | Operación normal y en pico de tráfico (Black Friday) |
| **Artefacto** | Servicio de Consulta de Estado (modelo de lectura materializado, separado del servicio de Órdenes) |
| **Respuesta** | El sistema retorna el estado actual de la orden desde su modelo de lectura, sin orquestar llamadas en tiempo real a ERP, POS, PayKlik o LogiExpress |
| **Medida de respuesta** | Tiempo de respuesta **< 5 segundos** para el 100% de las consultas (límite declarado como inaceptable de superar), incluso bajo carga pico |
| **Evidencia** | EV-33 |

---

### EAC-03 — Rendimiento en recuperación de evidencia transaccional

| Parte | Descripción |
|---|---|
| **Fuente del estímulo** | Directora de Finanzas / proceso de auditoría anual de la SAT |
| **Estímulo** | Solicitud de reconstrucción de una transacción: quién la ejecutó, cuándo y qué cambios se realizaron |
| **Ambiente** | Operación normal, en cualquier momento posterior a la venta (incluye ventanas de auditoría) |
| **Artefacto** | Servicio de Auditoría (registro append-only, indexado por transacción) |
| **Respuesta** | El sistema recupera la evidencia completa de la transacción desde su propia base, sin competir con la carga transaccional de Pagos o Facturación |
| **Medida de respuesta** | Recuperación de evidencia de cualquier transacción en **< 1 segundo**; 0 transacciones sin respaldo |
| **Evidencia** | EV-30 |

---

### EAC-04 — Escalabilidad ante crecimiento sostenido y picos de demanda

| Parte | Descripción |
|---|---|
| **Fuente del estímulo** | Volumen de usuarios y transacciones del canal digital de MegaStore |
| **Estímulo** | (a) Crecimiento sostenido del comercio electrónico (~15% anual, ya es el 25% de las ventas); (b) pico estacional de hasta 8x en Black Friday |
| **Ambiente** | Operación en producción, tanto en condiciones normales con tendencia de crecimiento anual como en la ventana de alta demanda de Black Friday |
| **Artefacto** | Servicios del Backbone desplegados en el clúster de contenedores (Órdenes, Inventario, Pagos, Consulta de Estado) |
| **Respuesta** | Los servicios escalan horizontalmente de forma independiente entre sí según su propio patrón de carga (autoescalado en el pico; capacidad base ajustable para el crecimiento anual), sin requerir rediseño de la arquitectura ni migración forzada fuera de la ventana de mantenimiento |
| **Medida de respuesta** | El sistema sostiene una demanda de 8x sin degradar EAC-01/EAC-02/EAC-03; la capacidad base puede incrementarse año a año dentro del presupuesto operativo (≤ USD 1,200/mes) sin cambios arquitectónicos |
| **Evidencia** | EV-01 (32 tiendas, USD 450M facturación anual), EV-02 (25% de ventas por e-commerce, crece ~15% anual), EV-27 (demanda 8x en Black Friday) |

---

### EAC-05 — Consistencia de inventario ante reservas concurrentes

| Parte | Descripción |
|---|---|
| **Fuente del estímulo** | Múltiples clientes intentando comprar el mismo producto de forma simultánea, desde distintos canales |
| **Estímulo** | Dos o más solicitudes de reserva concurrentes sobre la misma unidad de inventario |
| **Ambiente** | Operación normal y en pico de tráfico (Black Friday) |
| **Artefacto** | Servicio de Inventario (reserva con bloqueo optimista y expiración/TTL) |
| **Respuesta** | El sistema detecta el conflicto de versión al escribir (bloqueo optimista) y rechaza la segunda reserva en lugar de comprometer la misma unidad dos veces; si una compra no concluye, la reserva se libera automáticamente al expirar su TTL |
| **Medida de respuesta** | 0 unidades comprometidas a más de un cliente al mismo tiempo; reservas no concluidas liberadas dentro del tiempo de expiración definido para la operación comercial |
| **Evidencia** | EV-22 (evitar comprometer una unidad a más de un cliente — único punto del caso que exige consistencia fuerte), EV-23 (liberación de reserva no concluida) |

---

## 4. Trazabilidad con el Diagrama de Despliegue

- **EAC-01, EAC-02, EAC-03:** ya resueltos en `DDA_06_Despliegue` (Julio Herrera) — Outbox/RabbitMQ para Pagos, Redis para Consulta de Estado, base de solo-inserción para Auditoría.
- **EAC-04:** exige que el diagrama de despliegue muestre autoescalado horizontal (HPA) independiente por servicio, no solo para Pagos.
- **EAC-05:** exige que el diagrama de despliegue muestre el mecanismo de bloqueo optimista (`version_column`) y el temporizador de expiración de reservas como parte del servicio de Inventario.

## Pendiente

- Validar con Julio Herrera que EAC-04 y EAC-05 queden reflejados explícitamente en `DDA_06_Despliegue` (actualmente solo están listados ahí como pendientes).
- Confirmar con Angel Martinez la inclusión de EAC-04 y EAC-05 en la Matriz de Trazabilidad.
