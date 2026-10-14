// C3 — Carga: pico 8x con rechazos de credito.
//
// Pregunta: cuando Credito dice que no, la orden ya tiene stock reservado (la
// saga bifurca DESPUES de la reserva, ManejadorEventosInventario). ¿Ese stock
// vuelve al disponible siempre, y en cuanto tiempo, aun en el pico?
//
// Volumen: 80 ordenes/min a CREDITO durante 5 minutos; la mitad de clientes
// elegibles (BUENO) y la mitad con un incidente grave (NO_ELEGIBLE: descarte
// directo de la politica). Cada rechazo dispara LiberacionReservaSolicitada con
// CREDITO_RECHAZADO; verificar.sh cruza cada orden rechazada con su reserva en
// Inventario y con el evento ReservaInventarioLiberada en Auditoria.
//
//   bash scripts/pruebas-carga/credito-inventario/ejecutar.sh c3

import exec from 'k6/execution';
import {
  OPCIONES_BASE, PERFILES, comprar, crearClientes, resumenFinal, sembrarSku,
} from './lib/comun.js';

const TASA = Number(__ENV.ORDENES_POR_MINUTO || 80);
const DURACION_SEG = Number(__ENV.DURACION_SEG || 300);
const CLIENTES = Number(__ENV.CLIENTES || 60);

export const options = Object.assign({}, OPCIONES_BASE, {
  scenarios: {
    credito_con_rechazos: {
      executor: 'ramping-arrival-rate',
      exec: 'flujo',
      startRate: 0,
      timeUnit: '1m',
      preAllocatedVUs: 10,
      maxVUs: 60,
      stages: [
        { target: TASA, duration: '30s' },
        { target: TASA, duration: `${Math.max(1, DURACION_SEG - 30)}s` },
      ],
    },
  },
  thresholds: {
    'sgo_latencia_checkout_ms{ruta:credito}': ['p(95)<1000'],
    'sgo_exito_checkout{ruta:credito}': ['rate>0.99'],
    'http_req_failed{paso:crear}': ['rate<0.01'],
  },
});

export function setup() {
  sembrarSku(Math.ceil((TASA * DURACION_SEG) / 60) * 3);
  return {
    buenos: crearClientes(PERFILES.BUENO, CLIENTES),
    noElegibles: crearClientes(PERFILES.NO_ELEGIBLE, CLIENTES),
  };
}

export function flujo(data) {
  const i = exec.scenario.iterationInTest;
  // Alterna 1 a 1: la mitad de las ordenes termina RECHAZADA por Credito.
  const grupo = i % 2 === 0 ? data.buenos : data.noElegibles;
  const perfil = i % 2 === 0 ? 'bueno' : 'no_elegible';
  comprar(grupo[Math.floor(i / 2) % grupo.length], 'CREDITO', 1, { perfil });
}

export function teardown() {
  resumenFinal('C3');
}
