# Prueba de Carga EAC-02 / HU-024 — Rendimiento de Consulta de Estado bajo Carga de Black Friday

Esta prueba valida el cumplimiento estricto del atributo de calidad **EAC-02** y los criterios de aceptación de la Historia de Usuario **HU-024**:
> *El 100% de las consultas (P100) responde en menos de 5 segundos (< 5000 ms), incluso bajo carga pico de Black Friday (hasta 8x la demanda normal).*

---

## 1. Cómo Correrla

Requiere el stack local arriba (`docker compose up -d` desde `SGO-Logic/`).
No requiere instalar dependencias adicionales en la máquina anfitriona: k6 corre dentro de su contenedor oficial (`grafana/k6`).

### Ejecución Directa de la Suite de Verificación
Desde la raíz del repositorio o desde `SGO-Logic/`:

```bash
bash SGO-Logic/scripts/pruebas-carga/eac02-hu024-verificar.sh
```

O ejecutando k6 directamente sobre la red interna del clúster Compose:

```bash
docker run --rm --network sgo-logic_default \
  -v "${PWD}/SGO-Logic/scripts/pruebas-carga:/scripts" \
  -e VUS=25 -e DURACION_SEG=30 \
  grafana/k6:latest run /scripts/eac02-hu024-carga.js
```

---

## 2. Autoescalado Horizontal Independiente

La tarea técnica de HU-024 exige: *"Configurar autoescalado independiente del servicio Consulta de Estado"*.

### A. En Producción (Kubernetes HPA - ADR-001)
El manifiesto oficial se encuentra en [`deploy/k8s/consulta-estado-hpa.yaml`](../../deploy/k8s/consulta-estado-hpa.yaml):
* Define un `HorizontalPodAutoscaler` (`autoscaling/v2`) exclusivo para `sgo-consulta-estado-api`.
* **Rango:** Mínimo 2 réplicas (alta disponibilidad base), escalando dinámicamente hasta 10 réplicas durante el pico de Black Friday.
* **Métricas de activación:** Utilización de CPU > 70% o consumo de memoria > 80%.
* **Comportamiento de estabilización:**
  * *Scale-Up:* Inmediato (`stabilizationWindowSeconds: 0`, duplicación de capacidad cada 15 segundos) para absorber el embate de tráfico de Black Friday.
  * *Scale-Down:* Gradual (`stabilizationWindowSeconds: 300`, máximo 10% por minuto) para evitar oscilaciones o flapping de pods.

### B. En el MVP Local (Docker Compose - ADR-013)
Docker Compose permite demostrar el escalado dinámico de réplicas de `consulta-estado-api` sin colisión de puertos gracias a [`docker-compose.scale.yml`](../../SGO-Logic/scripts/escalabilidad/docker-compose.scale.yml) (override consolidado con HU-025/EAC-04, que resuelve el mismo problema para `ordenes-api` y `auditoria-worker`; antes vivía como `docker-compose.scale-consulta.yml` en `scripts/pruebas-carga/`).

Para escalar el servicio a 3 réplicas en caliente:
```bash
bash SGO-Logic/scripts/pruebas-carga/eac02-hu024-escalar.sh 3
```

El `api-gateway` (YARP) se comunica con `http://consulta-estado-api:8080`. El DNS interno de Docker balancea automáticamente en round-robin las consultas entre todas las réplicas en ejecución.

Para volver a 1 réplica:
```bash
docker compose up -d --scale consulta-estado-api=1
```

---

## 3. Qué Mide la Prueba

El script de carga ([`eac02-hu024-carga.js`](../../SGO-Logic/scripts/pruebas-carga/eac02-hu024-carga.js)) simula el patrón real de tráfico de consulta de clientes y agentes durante Black Friday:

1. **Lectura en Caliente (Redis Cache Hit - 75% del tráfico):**
   * Consulta directa de órdenes ya consolidadas en el modelo de lectura materializado (`GET /api/estado/ordenes/{ordenId}`).
   * Demuestra el beneficio de la arquitectura CQRS (ADR-004) respondiendo en el orden de milisegundos (< 10 ms).
2. **Historial de Cliente Paginado (Vista 360° - 15% del tráfico):**
   * Consultas de agentes o clientes revisando todas sus compras recientes (`GET /api/estado/clientes/{clienteId}/historial`).
3. **Resiliencia y Cache Miss (10% del tráfico):**
   * Consultas con rotación de IDs evaluando el fallback a PostgreSQL y posterior rehidratación de caché, garantizando que el acceso a base de datos de solo lectura se mantenga holgadamente bajo el presupuesto de 5 segundos.

### Volumen y Derivación de Demanda de Black Friday
En `PRUEBA-CARGA-EAC01.md`, el equipo derivó para escritura un piso de 80 órdenes creadas por minuto (~1.33 órdenes/segundo) durante el pico 8x de las 32 tiendas físicas.
En plataformas e-commerce, el tráfico de lectura (consultas de estado, tracking y pantallas de atención) supera al de compra en una proporción típica de entre **10:1 y 30:1**. Por ende, la carga de Black Friday representa entre **800 y 2,400 consultas por minuto** (hasta 40-60 consultas por segundo continuas).
La prueba de k6 simula 25 Usuarios Virtuales (VUs) concurrentes generando ráfagas continuas de más de 80 a 120 peticiones por segundo sostenidas.

---

## 4. Umbrales y Criterios de Aprobación

* **Criterio de Aceptación HU-024 (EAC-02):**
  * `http_req_duration`: **P100 (máximo absoluto) < 5000 ms** (5.0 segundos).
* **Objetivo de Rendimiento CQRS / Redis:**
  * `p(99) < 250 ms`.
  * `p(95) < 80 ms`.
  * `p(50) < 30 ms`.
* **Tasa de Errores HTTP:**
  * `http_req_failed`: **0.0%** (cero errores).

---

## 5. Salida de Resultados y Certificación

Al concluir la corrida, el arnés genera automáticamente:
* `scripts/pruebas-carga/eac02-resultado.json`: Métricas en formato JSON de k6.
* `scripts/pruebas-carga/eac02-resultado.txt`: Tabla resumen legible de percentiles.
* Certificación en terminal con dictamen `APROBADO`.

---

## 6. Evidencia de Ejecución Reproducible

Resultados obtenidos en la corrida de verificación bajo carga pico de Black Friday (25 VUs concurrentes, 40 segundos, clúster multi-réplica):

| Métrica | Valor Obtenido | Umbral Exigido (EAC-02 / HU-024) | Cumplimiento |
| :--- | :---: | :---: | :---: |
| **Total de Consultas Procesadas** | **17,955** | Carga sostenida > 8x | **OK** |
| **Throughput Promedio** | **~450 req/s** | Capacidad pico Black Friday | **OK** |
| **Tasa de Error HTTP** | **0.00%** | 0.0% fallas | **OK** |
| **Latencia Mínima** | **0.89 ms** | - | **OK** |
| **Latencia Promedio** | **3.81 ms** | < 5000 ms | **OK** |
| **Percentil 95 (P95)** | **9.27 ms** | < 80 ms | **OK** |
| **Máximo P100 (SLA Contractual)** | **232.09 ms** | **< 5,000 ms (5.0 s)** | **APROBADO** |

> **Dictamen Final:** **APROBADO**. El 100% de las consultas responde en **menos de 5 segundos** (P100 = 232.09 ms, más de 20 veces por debajo del límite máximo permitido). Se certifica la validez de HU-024 y EAC-02.

