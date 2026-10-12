// C1 — Carga: linea base de la ruta de credito en un dia normal.
//
// Pregunta: en operacion normal, ¿cuanto tarda Credito en decidir y cuanto
// tiempo queda retenido el stock mientras decide?
//
// Volumen: el "dia normal" que deriva PRUEBA-CARGA-EAC01.md (32 tiendas x 1
// orden cada 3 minutos ~= 10.7 ordenes/min), redondeado a 11 ordenes/min, todas
// a CREDITO de clientes elegibles (perfil BUENO). 5 minutos sostenidos.
//
// Lo que se mide aqui (k6): latencia del checkout (crear + confirmar).
// Lo que mide verificar.sh: tiempo de decision de Credito, tiempo de retencion
// del stock, fugas, sobreventa y coherencia de la linea.
//
//   bash scripts/pruebas-carga/credito-inventario/ejecutar.sh c1

import exec from 'k6/execution';
import {
  OPCIONES_BASE, PERFILES, comprar, crearClientes, resumenFinal, sembrarSku,
} from './lib/comun.js';

const TASA = Number(__ENV.ORDENES_POR_MINUTO || 11);
const DURACION_SEG = Number(__ENV.DURACION_SEG || 300);
const CLIENTES = Number(__ENV.CLIENTES || 40);

export const options = Object.assign({}, OPCIONES_BASE, {
  scenarios: {
    credito_dia_normal: {
      executor: 'constant-arrival-rate',
      exec: 'flujo',
      rate: TASA,
      timeUnit: '1m',
      duration: `${DURACION_SEG}s`,
      preAllocatedVUs: 5,
      maxVUs: 20,
    },
  },
  thresholds: {
    'sgo_latencia_checkout_ms{ruta:credito}': ['p(95)<500', 'p(99)<1000'],
    'sgo_exito_checkout{ruta:credito}': ['rate>0.99'],
    'http_req_failed{paso:crear}': ['rate<0.01'],
    'http_req_failed{paso:confirmar}': ['rate<0.01'],
  },
});

export function setup() {
  // Stock de sobra: aqui no se busca contencion, se busca la linea base.
  sembrarSku(Math.ceil((TASA * DURACION_SEG) / 60) * 3);
  return { buenos: crearClientes(PERFILES.BUENO, CLIENTES) };
}

export function flujo(data) {
  const i = exec.scenario.iterationInTest;
  comprar(data.buenos[i % data.buenos.length], 'CREDITO', 1);
}

export function teardown() {
  resumenFinal('C1');
}
