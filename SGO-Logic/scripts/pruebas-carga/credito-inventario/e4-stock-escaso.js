// E4 — Estres: SKU escaso, credito y tarjeta compitiendo por las ultimas unidades.
//
// Esta es la prueba central del "impacto del credito en Inventario".
//
// La saga reserva stock ANTES de preguntarle a Credito (ADR-016, CU-18: la
// precondicion de la evaluacion es "Ordenes tiene una reserva confirmada"). Una
// compra a credito que al final se rechaza tuvo esas unidades apartadas
// mientras Credito decidia. Con stock de sobra eso no se nota; con un producto
// escaso en una avalancha (Black Friday, lanzamiento), esas unidades apartadas
// pueden hacer que compradores con tarjeta reciban "sin stock" para unidades
// que despues vuelven al disponible y nadie compra.
//
// Montaje:
//   - SKU con STOCK unidades (40 por defecto).
//   - En 30 s llegan 60 compras a CREDITO de clientes que Credito rechazara
//     (perfil NO_ELEGIBLE; en produccion el equivalente son rechazos por cupo,
//     linea en pausa o timeout) y, 3 s despues, 60 compras con TARJETA.
//   - Demanda total 120 contra 40 unidades.
//
// Control: con SOLO_TARJETA=1 corre solo las 60 de tarjeta. Sin interferencia,
// deberian venderse exactamente STOCK unidades a tarjeta. La diferencia entre
// la corrida normal y el control es el impacto medido del credito.
//
// verificar.sh reporta: tarjetas rechazadas por stock, unidades disponibles al
// final (stock que nadie compro), y "ventas de tarjeta perdidas por retencion
// de credito" = min(tarjetas rechazadas por stock, disponible final).
// La integridad (0 sobreventa, 0 fugas) si es un criterio de aprobacion.
//
//   bash scripts/pruebas-carga/credito-inventario/ejecutar.sh e4
//   SOLO_TARJETA=1 bash scripts/pruebas-carga/credito-inventario/ejecutar.sh e4

import exec from 'k6/execution';
import {
  OPCIONES_BASE, PERFILES, clienteTarjeta, comprar, crearClientes, payKlikNormal, resumenFinal, sembrarSku,
} from './lib/comun.js';

const STOCK = Number(__ENV.STOCK || 40);
const POR_RUTA = Number(__ENV.ORDENES_POR_RUTA || 60);
const VENTANA_SEG = Number(__ENV.VENTANA_SEG || 30);
const SOLO_TARJETA = __ENV.SOLO_TARJETA === '1';

const tasaPorMinuto = Math.ceil((POR_RUTA * 60) / VENTANA_SEG);

const escenarios = {
  compras_tarjeta: {
    executor: 'constant-arrival-rate',
    exec: 'flujoTarjeta',
    rate: tasaPorMinuto,
    timeUnit: '1m',
    duration: `${VENTANA_SEG}s`,
    startTime: SOLO_TARJETA ? '0s' : '3s',
    preAllocatedVUs: 20,
    maxVUs: 80,
  },
};
if (!SOLO_TARJETA) {
  escenarios.compras_credito_rechazadas = {
    executor: 'constant-arrival-rate',
    exec: 'flujoCredito',
    rate: tasaPorMinuto,
    timeUnit: '1m',
    duration: `${VENTANA_SEG}s`,
    preAllocatedVUs: 20,
    maxVUs: 80,
  };
}

export const options = Object.assign({}, OPCIONES_BASE, {
  scenarios: escenarios,
  thresholds: {
    // Con stock escaso el checkout igual debe responder: el "sin stock" llega
    // despues, por la saga, no como error HTTP.
    'sgo_exito_checkout{ruta:tarjeta}': ['rate>0.99'],
    'sgo_latencia_checkout_ms{ruta:tarjeta}': ['p(95)<1000'],
  },
});

export function setup() {
  payKlikNormal();
  sembrarSku(STOCK);
  return {
    noElegibles: SOLO_TARJETA ? [] : crearClientes(PERFILES.NO_ELEGIBLE, 30),
    stock: STOCK,
  };
}

export function flujoCredito(data) {
  const i = exec.scenario.iterationInTest;
  comprar(data.noElegibles[i % data.noElegibles.length], 'CREDITO', 1);
}

export function flujoTarjeta() {
  comprar(clienteTarjeta(exec.scenario.iterationInTest), 'TARJETA', 1);
}

export function teardown(data) {
  resumenFinal('E4', `Stock inicial ${data.stock}. Modo ${SOLO_TARJETA ? 'CONTROL (solo tarjeta)' : 'credito + tarjeta'}.`);
}
