// =============================================================================
// EAC-02 / HU-024 — Rendimiento de Consulta de Estado bajo Carga de Black Friday
// Verifica que el 100% (P100) de las consultas responda en menos de 5 segundos (< 5000 ms)
// bajo carga pico simulada de hasta 8x la demanda normal.
//
// Corre con k6 en contenedor (sin dependencias en la maquina local):
//   docker run --rm --network sgo-logic_default \
//     -v "${PWD}/scripts/pruebas-carga:/scripts" \
//     -e VUS=25 -e DURACION_SEG=30 \
//     grafana/k6 run /scripts/eac02-hu024-carga.js
// =============================================================================

import http from 'k6/http';
import { check, sleep } from 'k6';
import { Trend, Rate, Counter } from 'k6/metrics';

// Metricas personalizadas
const latenciaConsulta = new Trend('latencia_consulta_estado_ms', true);
const tasaExito = new Rate('tasa_exito_consultas');
const contadorConsultas = new Counter('total_consultas_realizadas');

// Parametros de ejecucion ajustables por variables de entorno
const VUS = parseInt(__ENV.VUS || '25', 10);
const DURACION_SEG = parseInt(__ENV.DURACION_SEG || '30', 10);
const GATEWAY_HOST = __ENV.GATEWAY_HOST || 'api-gateway:8080';

// Pool de 50 ordenes sembradas para pruebas de alta concurrencia
const POOL_ORDENES = Array.from({ length: 50 }, (_, i) => `ORD-2026-${String(i + 1).padStart(3, '0')}`);

// Clientes para prueba de historial paginado
const CLIENTES = ['CLI-001', 'CLI-002', 'CLI-003', 'CLI-004'];

export const options = {
  scenarios: {
    // Escenario principal: Carga concurrente sostenida de Black Friday (pico 8x)
    carga_black_friday: {
      executor: 'ramping-vus',
      startVUs: 5,
      stages: [
        { duration: '5s', target: VUS },               // Rampa ascendente rapida
        { duration: `${DURACION_SEG}s`, target: VUS }, // Pico sostenido de Black Friday
        { duration: '5s', target: 0 },                 // Rampa descendente / enfriamiento
      ],
      gracefulRampDown: '5s',
    },
  },
  thresholds: {
    // CRITERIO CONTRACTUAL DE ACEPTACION (EAC-02 / HU-024):
    // El P100 (100% de consultas) DEBE responder en menos de 5000 ms (< 5 s)
    'http_req_duration': [
      'max < 5000',     // P100 < 5.0 s (SLA contractual de HU-024)
      'p(99) < 250',    // P99 < 250 ms (alto rendimiento CQRS)
      'p(95) < 80',     // P95 < 80 ms (objetivo sub-100ms de Redis)
      'p(50) < 30',     // Mediana esperada sub-30ms
    ],
    // Cero tasa de error bajo carga
    'http_req_failed': ['rate == 0.0'],
    'tasa_exito_consultas': ['rate == 1.0'],
  },
};

export default function () {
  const iterId = __ITER;
  const aleatorio = Math.random();

  if (aleatorio < 0.80) {
    // 1. Consulta consolidada de orden (Cache Hit en Redis - 80% del trafico)
    const ordenId = POOL_ORDENES[iterId % POOL_ORDENES.length];
    const url = `http://${GATEWAY_HOST}/api/estado/ordenes/${ordenId}`;
    
    const inicio = Date.now();
    const res = http.get(url, {
      tags: { tipo: 'consulta_orden_caliente' },
      timeout: '6s',
    });
    const duracion = Date.now() - inicio;

    latenciaConsulta.add(duracion);
    contadorConsultas.add(1);

    const ok = check(res, {
      'status es 200': (r) => r.status === 200,
      'tiempo < 5s (EAC-02)': (r) => r.timings.duration < 5000,
      'respuesta valida': (r) => {
        try {
          const body = JSON.parse(r.body);
          return body.ordenId === ordenId;
        } catch (_) {
          return false;
        }
      },
    });

    tasaExito.add(ok);

  } else {
    // 2. Consulta de historial de cliente paginado (Vista 360 - 20% del trafico)
    const clienteId = CLIENTES[iterId % CLIENTES.length];
    const url = `http://${GATEWAY_HOST}/api/estado/clientes/${clienteId}/ordenes?pagina=1&tamano=5`;
    
    const inicio = Date.now();
    const res = http.get(url, {
      tags: { tipo: 'historial_cliente' },
      timeout: '6s',
    });
    const duracion = Date.now() - inicio;

    latenciaConsulta.add(duracion);
    contadorConsultas.add(1);

    const ok = check(res, {
      'historial status es 200': (r) => r.status === 200,
      'historial tiempo < 5s': (r) => r.timings.duration < 5000,
      'historial contiene lista': (r) => {
        try {
          const body = JSON.parse(r.body);
          return Array.isArray(body.ordenes);
        } catch (_) {
          return false;
        }
      },
    });

    tasaExito.add(ok);
  }

  // Pausa breve entre peticiones para simular comportamiento realista
  sleep(0.02 + Math.random() * 0.05);
}

export function handleSummary(data) {
  const reqDuration = data.metrics['http_req_duration'] ? data.metrics['http_req_duration'].values : {};
  const maxDur = reqDuration.max ? reqDuration.max.toFixed(2) : '0';
  const p99Dur = reqDuration['p(99)'] ? reqDuration['p(99)'].toFixed(2) : '0';
  const p95Dur = reqDuration['p(95)'] ? reqDuration['p(95)'].toFixed(2) : '0';
  const p50Dur = reqDuration['p(50)'] ? reqDuration['p(50)'].toFixed(2) : '0';
  const avgDur = reqDuration.avg ? reqDuration.avg.toFixed(2) : '0';
  const totalReqs = data.metrics['http_reqs'] ? data.metrics['http_reqs'].values.count : 0;
  const rateFail = data.metrics['http_req_failed'] ? (data.metrics['http_req_failed'].values.rate * 100).toFixed(2) : '0';

  const cumpleSla = reqDuration.max < 5000 && (data.metrics['http_req_failed'].values.rate === 0);

  const textoResumen = `
===============================================================================
 RESULTADOS DE PRUEBA DE CARGA EAC-02 / HU-024 (BLACK FRIDAY)
===============================================================================
 Total Peticiones Ejecutadas : ${totalReqs}
 Tasa de Error               : ${rateFail}%
 
 Metricas de Latencia End-to-End (ms):
   Minimo      : ${reqDuration.min ? reqDuration.min.toFixed(2) : 0} ms
   Promedio    : ${avgDur} ms
   Mediana P50 : ${p50Dur} ms
   Percentil 95: ${p95Dur} ms
   Percentil 99: ${p99Dur} ms
   Maximo P100 : ${maxDur} ms  <--- Criterio de Aceptacion HU-024 (< 5000 ms)

 EVALUACION DE CRITERIO DE ACEPTACION:
   [${cumpleSla ? 'APROBADO' : 'FALLIDO'}] P100 = ${maxDur} ms < 5000 ms (SLA EAC-02 / EV-33)
===============================================================================
`;

  return {
    'stdout': textoResumen,
    '/scripts/eac02-resultado.json': JSON.stringify(data, null, 2),
    '/scripts/eac02-resultado.txt': textoResumen,
  };
}
