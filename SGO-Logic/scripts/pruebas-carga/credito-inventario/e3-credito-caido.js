// E3 — Estres: Credito se cae en pleno pico.
//
// Pregunta (EAC-06 y el enunciado: "La orden no puede quedar congelada en el
// checkout esperando una respuesta"): si credito-api deja de responder durante
// el pico,
//   1) ¿las ordenes a credito terminan RECHAZADA por timeout de la saga
//      (Credito:TimeoutEvaluacionSegundos = 30) y devuelven su stock?
//   2) ¿la ruta de tarjeta sigue intacta (ningun timeout de Credito impide
//      comprar con PayKlik)?
//   3) cuando Credito vuelve y procesa la cola atrasada, ¿anula los cargos de
//      las ordenes que ya habian vencido (cero cargos huerfanos)?
//
// La caida la provoca ejecutar.sh (k6 no puede detener contenedores):
//   t = 0     carga constante, Credito arriba
//   t = 90 s  docker compose stop credito-api
//   t = 180 s docker compose start credito-api
//   t = 300 s fin de la carga
// Mezcla: 50% CREDITO (elegibles), 50% TARJETA, 80 ordenes/min.
//
// Importante: los historiales se cargan en setup(), antes de la caida.
//
//   bash scripts/pruebas-carga/credito-inventario/ejecutar.sh e3

import exec from 'k6/execution';
import {
  OPCIONES_BASE, PERFILES, clienteTarjeta, comprar, crearClientes, payKlikNormal, resumenFinal, sembrarSku,
} from './lib/comun.js';

const TASA = Number(__ENV.ORDENES_POR_MINUTO || 80);
const DURACION_SEG = Number(__ENV.DURACION_SEG || 300);
const CAIDA_INICIO_SEG = Number(__ENV.CAIDA_INICIO_SEG || 90);
const CAIDA_SEG = Number(__ENV.CAIDA_SEG || 90);
const CLIENTES = Number(__ENV.CLIENTES || 150);

export const options = Object.assign({}, OPCIONES_BASE, {
  scenarios: {
    pico_con_credito_caido: {
      executor: 'constant-arrival-rate',
      exec: 'flujo',
      rate: TASA,
      timeUnit: '1m',
      duration: `${DURACION_SEG}s`,
      preAllocatedVUs: 10,
      maxVUs: 60,
    },
  },
  thresholds: {
    // La creacion de la orden no depende de Credito (la evaluacion es
    // asincrona): el checkout no debe notar la caida en ninguna ruta.
    'sgo_exito_checkout{ruta:tarjeta}': ['rate>0.99'],
    'sgo_exito_checkout{ruta:credito}': ['rate>0.99'],
    'sgo_latencia_checkout_ms{credito:caido}': ['p(95)<1000'],
    'sgo_latencia_checkout_ms{credito:arriba}': ['p(95)<1000'],
  },
});

export function setup() {
  payKlikNormal();
  sembrarSku(Math.ceil((TASA * DURACION_SEG) / 60) * 3);
  return { buenos: crearClientes(PERFILES.BUENO, CLIENTES), inicio: Date.now() };
}

export function flujo(data) {
  const i = exec.scenario.iterationInTest;
  const t = (Date.now() - data.inicio) / 1000;
  const tags = { credito: t >= CAIDA_INICIO_SEG && t < CAIDA_INICIO_SEG + CAIDA_SEG ? 'caido' : 'arriba' };
  if (i % 2 === 0) {
    comprar(data.buenos[i % data.buenos.length], 'CREDITO', 1, tags);
  } else {
    comprar(clienteTarjeta(i), 'TARJETA', 1, tags);
  }
}

export function teardown() {
  resumenFinal('E3', 'Credito debe estar de nuevo arriba (ejecutar.sh lo levanta).');
}
