// E1 — Estres: rampa escalonada hasta el punto de quiebre.
//
// Pregunta: ¿a partir de cuantas ordenes por minuto la ruta de credito deja de
// decidir "rapido" (o empieza a vencer por timeout y devolver stock sin vender)?
// El pico esperado es 80/min; aqui se sube por escalones hasta 8 veces eso.
//
// Escalones de 60 s (10 s de rampa + 50 s sostenidos):
//   40 -> 80 -> 160 -> 320 -> 480 -> 640 ordenes/min
// Mezcla: 70% CREDITO (clientes elegibles), 30% TARJETA.
//
// Cada peticion lleva la etiqueta `etapa` (1..6) y hay un umbral por etapa:
// el resumen de k6 muestra en que escalon se rompe la latencia del checkout.
// verificar.sh agrega la vista por minuto de la decision de Credito, los
// timeouts y el stock retenido, que es donde se ve el quiebre de la saga
// (el checkout puede seguir rapido mientras la decision asincrona se atrasa).
//
// Los umbrales NO abortan la corrida: el objetivo es encontrar el limite, no
// aprobar un numero. El veredicto de la prueba es de integridad (0 fugas, 0
// sobreventa, 0 ordenes atascadas tras el drenado) y el punto de quiebre se
// reporta como hallazgo.
//
//   bash scripts/pruebas-carga/credito-inventario/ejecutar.sh e1

import exec from 'k6/execution';
import {
  OPCIONES_BASE, PERFILES, clienteTarjeta, comprar, crearClientes, payKlikNormal, resumenFinal, sembrarSku,
} from './lib/comun.js';

const ESCALONES = (__ENV.ESCALONES || '40,80,160,320,480,640').split(',').map(Number);
const SEG_POR_ESCALON = Number(__ENV.SEG_POR_ESCALON || 60);
const CLIENTES = Number(__ENV.CLIENTES || 300);
const PROPORCION_CREDITO = Number(__ENV.PROPORCION_CREDITO || 0.7);

const etapas = [];
ESCALONES.forEach((tasa) => {
  etapas.push({ target: tasa, duration: '10s' });
  etapas.push({ target: tasa, duration: `${SEG_POR_ESCALON - 10}s` });
});

const umbrales = {
  'http_req_failed{paso:crear}': [{ threshold: 'rate<0.05', abortOnFail: false }],
};
ESCALONES.forEach((tasa, k) => {
  umbrales[`sgo_latencia_checkout_ms{etapa:${k + 1}}`] = ['p(95)<1000'];
  umbrales[`sgo_exito_checkout{etapa:${k + 1}}`] = ['rate>0.99'];
});

export const options = Object.assign({}, OPCIONES_BASE, {
  scenarios: {
    rampa: {
      executor: 'ramping-arrival-rate',
      exec: 'flujo',
      startRate: 0,
      timeUnit: '1m',
      preAllocatedVUs: 40,
      maxVUs: Number(__ENV.MAX_VUS || 300),
      stages: etapas,
    },
  },
  thresholds: umbrales,
});

export function setup() {
  payKlikNormal();
  const total = ESCALONES.reduce((s, t) => s + t, 0) * (SEG_POR_ESCALON / 60);
  sembrarSku(Math.ceil(total) * 3);
  return { buenos: crearClientes(PERFILES.BUENO, CLIENTES), inicio: Date.now() };
}

export function flujo(data) {
  const i = exec.scenario.iterationInTest;
  const etapa = Math.min(ESCALONES.length, Math.floor((Date.now() - data.inicio) / (SEG_POR_ESCALON * 1000)) + 1);
  const tags = { etapa: String(etapa) };
  // Reparto determinista 70/30 sobre bloques de 10 iteraciones.
  if ((i % 10) < Math.round(PROPORCION_CREDITO * 10)) {
    comprar(data.buenos[i % data.buenos.length], 'CREDITO', 1, tags);
  } else {
    comprar(clienteTarjeta(i), 'TARJETA', 1, tags);
  }
}

export function teardown() {
  resumenFinal('E1', `Escalones: ${ESCALONES.join(' -> ')} ordenes/min de ${SEG_POR_ESCALON}s.`);
}
