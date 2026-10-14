// C5 — Carga: agotamiento progresivo del cupo de credito.
//
// Pregunta: el negocio espera que el ticket suba ~40% con credito. Clientes
// REGULAR (limite Q1 500) compran Q500 una y otra vez hasta quedarse sin cupo.
// ¿Credito corta exactamente en el limite, y cada compra rechazada por cupo
// devuelve el stock que ya habia reservado?
//
// Volumen: 40 ordenes/min durante 4 minutos (~160 ordenes) repartidas en
// round-robin entre 20 clientes REGULAR: ~8 compras de Q500 (5 unidades) por
// cliente. Cada cliente alcanza 3 (Q1 500); las ~5 restantes se rechazan por cupo.
// Esperado: ~60 aprobadas, ~100 rechazadas, 0 lineas sobre el limite. (Si el
// entorno calificara a estos clientes como BUENO, Q3 500, igual se agota: 7 + 1.)
//
//   bash scripts/pruebas-carga/credito-inventario/ejecutar.sh c5

import exec from 'k6/execution';
import {
  OPCIONES_BASE, PERFILES, comprar, crearClientes, resumenFinal, sembrarSku,
} from './lib/comun.js';

const TASA = Number(__ENV.ORDENES_POR_MINUTO || 40);
const DURACION_SEG = Number(__ENV.DURACION_SEG || 240);
const CLIENTES = Number(__ENV.CLIENTES || 20);
export const UNIDADES_POR_ORDEN = Number(__ENV.UNIDADES_POR_ORDEN || 5);

export const options = Object.assign({}, OPCIONES_BASE, {
  scenarios: {
    agotar_cupo: {
      executor: 'constant-arrival-rate',
      exec: 'flujo',
      rate: TASA,
      timeUnit: '1m',
      duration: `${DURACION_SEG}s`,
      preAllocatedVUs: 10,
      maxVUs: 40,
    },
  },
  thresholds: {
    'sgo_latencia_checkout_ms{ruta:credito}': ['p(95)<1000'],
    'sgo_exito_checkout{ruta:credito}': ['rate>0.99'],
  },
});

export function setup() {
  sembrarSku(Math.ceil((TASA * DURACION_SEG) / 60) * UNIDADES_POR_ORDEN * 2);
  return { regulares: crearClientes(PERFILES.REGULAR, CLIENTES) };
}

export function flujo(data) {
  const i = exec.scenario.iterationInTest;
  comprar(data.regulares[i % data.regulares.length], 'CREDITO', UNIDADES_POR_ORDEN);
}

export function teardown() {
  resumenFinal('C5', `Esperado por cliente: floor(1500 / ${UNIDADES_POR_ORDEN * 100}) compras aprobadas como maximo.`);
}
