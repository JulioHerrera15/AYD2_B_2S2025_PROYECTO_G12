// E5 — Estres: rafaga concurrente del mismo cliente (doble gasto).
//
// Pregunta: un cliente abre varias pestanas, un bot o un doble clic dispara
// muchas compras a credito en el mismo instante. Credito protege la linea con
// bloqueo optimista por Version (ServicioEvaluacionCredito: hasta 5 reintentos)
// e Inventario protege el stock con su propio bloqueo optimista (ADR-005).
// Con los dos bajo contencion maxima al mismo tiempo:
//   - ¿algun cliente termina con saldo utilizado mayor que su limite?
//   - ¿las compras rechazadas por cupo devuelven el stock que reservaron?
//   - ¿alguna evaluacion agota los reintentos y deja la orden colgada?
//
// Montaje: 5 clientes REGULAR (limite Q1 500). 100 VUs arrancan a la vez, una
// iteracion cada uno: 20 compras simultaneas de Q200 (2 unidades) por cliente.
// Cada cliente puede pagar como maximo floor(1500/200) = 7. Esperado: <= 35
// aprobadas en total, >= 65 rechazadas por cupo, todas con su stock liberado.
//
//   bash scripts/pruebas-carga/credito-inventario/ejecutar.sh e5

import exec from 'k6/execution';
import {
  OPCIONES_BASE, PERFILES, comprar, crearClientes, resumenFinal, sembrarSku,
} from './lib/comun.js';

const CLIENTES = Number(__ENV.CLIENTES || 5);
const COMPRAS_POR_CLIENTE = Number(__ENV.COMPRAS_POR_CLIENTE || 20);
const UNIDADES_POR_ORDEN = Number(__ENV.UNIDADES_POR_ORDEN || 2);

export const options = Object.assign({}, OPCIONES_BASE, {
  scenarios: {
    rafaga: {
      executor: 'per-vu-iterations',
      exec: 'flujo',
      vus: CLIENTES * COMPRAS_POR_CLIENTE,
      iterations: 1,
      maxDuration: '2m',
    },
  },
  thresholds: {
    'sgo_exito_checkout{ruta:credito}': ['rate>0.99'],
    'sgo_latencia_checkout_ms{ruta:credito}': ['p(95)<3000'],
  },
});

export function setup() {
  sembrarSku(CLIENTES * COMPRAS_POR_CLIENTE * UNIDADES_POR_ORDEN * 2);
  return { regulares: crearClientes(PERFILES.REGULAR, CLIENTES) };
}

export function flujo(data) {
  const cliente = data.regulares[(exec.vu.idInTest - 1) % data.regulares.length];
  comprar(cliente, 'CREDITO', UNIDADES_POR_ORDEN);
}

export function teardown() {
  resumenFinal('E5', `Maximo esperado por cliente: floor(1500 / ${UNIDADES_POR_ORDEN * 100}) aprobadas.`);
}
