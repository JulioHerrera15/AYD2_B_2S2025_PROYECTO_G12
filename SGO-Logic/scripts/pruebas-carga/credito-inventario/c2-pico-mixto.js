// C2 — Carga: pico 8x con las dos rutas de pago a la vez.
//
// Pregunta (enunciado Fase 3, EV-58): "sin que ninguna de las dos rutas se
// convierta en un obstaculo para la otra". Con credito y tarjeta compitiendo por
// el mismo SKU y los mismos servicios en el pico, ¿se degrada alguna?
//
// Volumen: 80 ordenes/min (pico 8x de PRUEBA-CARGA-EAC01.md), mitad CREDITO
// (clientes elegibles) y mitad TARJETA, 5 minutos. Las dos rutas corren como
// escenarios separados con la misma tasa para poder comparar sus metricas.
//
//   bash scripts/pruebas-carga/credito-inventario/ejecutar.sh c2

import exec from 'k6/execution';
import {
  OPCIONES_BASE, PERFILES, clienteTarjeta, comprar, crearClientes, payKlikNormal, resumenFinal, sembrarSku,
} from './lib/comun.js';

const TASA_TOTAL = Number(__ENV.ORDENES_POR_MINUTO || 80);
const DURACION_SEG = Number(__ENV.DURACION_SEG || 300);
const CLIENTES = Number(__ENV.CLIENTES || 100);
const TASA_RUTA = Math.max(1, Math.round(TASA_TOTAL / 2));

function escenario(execNombre) {
  return {
    executor: 'ramping-arrival-rate',
    exec: execNombre,
    startRate: 0,
    timeUnit: '1m',
    preAllocatedVUs: 10,
    maxVUs: 60,
    stages: [
      { target: TASA_RUTA, duration: '30s' },
      { target: TASA_RUTA, duration: `${Math.max(1, DURACION_SEG - 30)}s` },
    ],
  };
}

export const options = Object.assign({}, OPCIONES_BASE, {
  scenarios: {
    ruta_credito: escenario('flujoCredito'),
    ruta_tarjeta: escenario('flujoTarjeta'),
  },
  thresholds: {
    'sgo_latencia_checkout_ms{ruta:credito}': ['p(95)<1000'],
    'sgo_latencia_checkout_ms{ruta:tarjeta}': ['p(95)<1000'],
    'sgo_exito_checkout{ruta:credito}': ['rate>0.99'],
    'sgo_exito_checkout{ruta:tarjeta}': ['rate>0.99'],
    'http_req_failed{paso:crear}': ['rate<0.01'],
  },
});

export function setup() {
  payKlikNormal();
  sembrarSku(Math.ceil((TASA_TOTAL * DURACION_SEG) / 60) * 3);
  return { buenos: crearClientes(PERFILES.BUENO, CLIENTES) };
}

export function flujoCredito(data) {
  const i = exec.scenario.iterationInTest;
  comprar(data.buenos[i % data.buenos.length], 'CREDITO', 1);
}

export function flujoTarjeta() {
  comprar(clienteTarjeta(exec.scenario.iterationInTest), 'TARJETA', 1);
}

export function teardown() {
  resumenFinal('C2');
}
