// E2 — Estres: pico subito (spike) y recuperacion.
//
// Pregunta: el lanzamiento de la campana "Compra ahora, paga en 30 dias"
// produce una avalancha sin rampa. ¿El sistema la absorbe, cuanto stock queda
// retenido mientras Credito se pone al dia, y vuelve solo a la normalidad?
//
// Perfil de la carga (ordenes/min):
//   60 s a 10  (normal)  ->  salto en 5 s a 600  ->  60 s a 600  ->
//   caida en 5 s a 10    ->  120 s a 10 (recuperacion)
// Mezcla: 70% CREDITO (elegibles), 30% TARJETA.
//
// La etiqueta `fase` (antes, pico, despues) separa las metricas de k6.
// verificar.sh mide la recuperacion: el p95 de la decision de Credito de las
// ordenes creadas en la fase "despues" debe volver a la linea base (<= 5 s).
//
//   bash scripts/pruebas-carga/credito-inventario/ejecutar.sh e2

import exec from 'k6/execution';
import {
  OPCIONES_BASE, PERFILES, clienteTarjeta, comprar, crearClientes, payKlikNormal, resumenFinal, sembrarSku,
} from './lib/comun.js';

const BASE = Number(__ENV.TASA_BASE || 10);
const PICO = Number(__ENV.TASA_PICO || 600);
const ANTES_SEG = Number(__ENV.ANTES_SEG || 60);
const PICO_SEG = Number(__ENV.PICO_SEG || 60);
const DESPUES_SEG = Number(__ENV.DESPUES_SEG || 120);
const CLIENTES = Number(__ENV.CLIENTES || 300);

export const options = Object.assign({}, OPCIONES_BASE, {
  scenarios: {
    pico_subito: {
      executor: 'ramping-arrival-rate',
      exec: 'flujo',
      startRate: BASE,
      timeUnit: '1m',
      preAllocatedVUs: 50,
      maxVUs: Number(__ENV.MAX_VUS || 300),
      stages: [
        { target: BASE, duration: `${ANTES_SEG}s` },
        { target: PICO, duration: '5s' },
        { target: PICO, duration: `${PICO_SEG}s` },
        { target: BASE, duration: '5s' },
        { target: BASE, duration: `${DESPUES_SEG}s` },
      ],
    },
  },
  thresholds: {
    'sgo_latencia_checkout_ms{fase:antes}': ['p(95)<1000'],
    'sgo_latencia_checkout_ms{fase:pico}': ['p(95)<3000'],
    'sgo_latencia_checkout_ms{fase:despues}': ['p(95)<1000'],
    'sgo_exito_checkout{fase:pico}': ['rate>0.95'],
    'sgo_exito_checkout{fase:despues}': ['rate>0.99'],
  },
});

export function setup() {
  payKlikNormal();
  const total = (BASE * (ANTES_SEG + DESPUES_SEG) + PICO * (PICO_SEG + 10)) / 60;
  sembrarSku(Math.ceil(total) * 3);
  return { buenos: crearClientes(PERFILES.BUENO, CLIENTES), inicio: Date.now() };
}

function fase(data) {
  const t = (Date.now() - data.inicio) / 1000;
  if (t < ANTES_SEG) return 'antes';
  if (t < ANTES_SEG + 5 + PICO_SEG + 5) return 'pico';
  return 'despues';
}

export function flujo(data) {
  const i = exec.scenario.iterationInTest;
  const tags = { fase: fase(data) };
  if ((i % 10) < 7) {
    comprar(data.buenos[i % data.buenos.length], 'CREDITO', 1, tags);
  } else {
    comprar(clienteTarjeta(i), 'TARJETA', 1, tags);
  }
}

export function teardown() {
  resumenFinal('E2', `Ventana de recuperacion para verificar.sh: ultimos ${DESPUES_SEG}s.`);
}
