// C4 — Carga: checkout de lectura (elegibilidad + disponibilidad) en el pico.
//
// Pregunta (EAC-06 del DDA, hoy "PENDIENTE DE EVIDENCIA"): en el checkout el
// cliente pide al mismo tiempo la oferta de credito (Credito, perfil local
// precalculado) y la disponibilidad del producto (Inventario). Con las compras
// del pico escribiendo en las mismas bases, ¿las dos lecturas siguen
// respondiendo "en milisegundos"?
//
// Carga:
//   - lectores: 30 usuarios concurrentes en checkout durante 4 minutos; cada
//     uno consulta elegibilidad (GET /api/credito/elegibilidad?monto=...),
//     existencias del SKU y precio del producto, y piensa 1 s.
//   - escritores: en paralelo, 80 ordenes/min a CREDITO (mitad elegibles, mitad
//     rechazadas) que reservan y liberan stock del MISMO SKU y cargan la linea
//     de los MISMOS clientes que los lectores consultan.
//
//   bash scripts/pruebas-carga/credito-inventario/ejecutar.sh c4

import http from 'k6/http';
import { check, sleep } from 'k6';
import exec from 'k6/execution';
import { Trend } from 'k6/metrics';
import {
  OPCIONES_BASE, PERFILES, PRECIO_UNITARIO, SKU, URLS, comprar, crearClientes, resumenFinal, sembrarSku,
} from './lib/comun.js';

const LECTORES = Number(__ENV.LECTORES || 30);
const TASA_ESCRITURA = Number(__ENV.ORDENES_POR_MINUTO || 80);
const DURACION_SEG = Number(__ENV.DURACION_SEG || 240);
const CLIENTES = Number(__ENV.CLIENTES || 60);

const latenciaElegibilidad = new Trend('sgo_latencia_elegibilidad_ms', true);
const latenciaDisponibilidad = new Trend('sgo_latencia_disponibilidad_ms', true);

export const options = Object.assign({}, OPCIONES_BASE, {
  scenarios: {
    lectores_checkout: {
      executor: 'constant-vus',
      exec: 'leerCheckout',
      vus: LECTORES,
      duration: `${DURACION_SEG}s`,
    },
    escritores_credito: {
      executor: 'constant-arrival-rate',
      exec: 'comprarCredito',
      rate: TASA_ESCRITURA,
      timeUnit: '1m',
      duration: `${DURACION_SEG}s`,
      preAllocatedVUs: 10,
      maxVUs: 60,
    },
  },
  thresholds: {
    // EAC-06: "la consulta local responde en milisegundos en condiciones normales".
    sgo_latencia_elegibilidad_ms: ['p(95)<300', 'p(99)<800'],
    sgo_latencia_disponibilidad_ms: ['p(95)<300', 'p(99)<800'],
    'http_req_failed{paso:elegibilidad}': ['rate<0.01'],
    'http_req_failed{paso:disponibilidad}': ['rate<0.01'],
    'sgo_exito_checkout{ruta:credito}': ['rate>0.99'],
  },
});

export function setup() {
  sembrarSku(Math.ceil((TASA_ESCRITURA * DURACION_SEG) / 60) * 3);
  return {
    buenos: crearClientes(PERFILES.BUENO, CLIENTES),
    noElegibles: crearClientes(PERFILES.NO_ELEGIBLE, CLIENTES),
  };
}

export function leerCheckout(data) {
  const todos = data.buenos.concat(data.noElegibles);
  const cliente = todos[(exec.vu.idInTest + exec.vu.iterationInScenario) % todos.length];
  const monto = PRECIO_UNITARIO * (1 + (exec.vu.iterationInScenario % 5));

  const respuestas = http.batch([
    {
      method: 'GET',
      url: `${URLS.credito}/api/credito/elegibilidad?monto=${monto}`,
      params: { headers: { Authorization: `Bearer ${cliente.token}` }, tags: { paso: 'elegibilidad' } },
    },
    {
      method: 'GET',
      url: `${URLS.inventario}/api/inventario/existencias/${SKU}`,
      params: { tags: { paso: 'disponibilidad' } },
    },
    {
      method: 'GET',
      url: `${URLS.inventario}/api/inventario/productos/${SKU}`,
      params: { tags: { paso: 'precio' } },
    },
  ]);
  latenciaElegibilidad.add(respuestas[0].timings.duration);
  latenciaDisponibilidad.add(respuestas[1].timings.duration);
  check(respuestas[0], { 'elegibilidad 200': (r) => r.status === 200 });
  check(respuestas[1], { 'disponibilidad 200': (r) => r.status === 200 });
  sleep(1);
}

export function comprarCredito(data) {
  const i = exec.scenario.iterationInTest;
  const grupo = i % 2 === 0 ? data.buenos : data.noElegibles;
  comprar(grupo[Math.floor(i / 2) % grupo.length], 'CREDITO', 1);
}

export function teardown() {
  resumenFinal('C4');
}
