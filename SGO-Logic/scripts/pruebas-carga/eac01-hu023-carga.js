// EAC-01 / HU-023 — cero ordenes perdidas por indisponibilidad de PayKlik
// durante el pico de Black Friday (8x). Corre con k6 en contenedor (no
// requiere instalar nada en la maquina del desarrollador):
//
//   docker run --rm --network sgo-logic_default -v "${PWD}/scripts/pruebas-carga:/scripts" -e ORDENES_POR_MINUTO=80 grafana/k6 run /scripts/eac01-hu023-carga.js
//
// (la red se confirmo con `docker network ls`: el compose de este repo la crea
// como sgo-logic_default; si tu docker compose usara otro nombre de proyecto,
// ajusta el --network).
//
// ---------------------------------------------------------------------------
// Volumen de la prueba (punto 1 del encargo)
// ---------------------------------------------------------------------------
// El DDA (EV-27, EAC-01, EAC-04, HU-023) fija el factor de escalamiento en
// "hasta ocho veces la demanda de un dia normal", pero en ningun punto del
// documento da ese "dia normal" en unidades absolutas (ordenes por minuto,
// por hora, etc.). No hay un numero oficial que usar tal cual.
//
// Ante esa ausencia, este script deriva uno defendible en vez de inventarlo
// a ciegas, apoyandose en la unica magnitud de escala que el DDA si declara:
// las 32 tiendas POS del canal fisico (EV-16).
//
//   - Piso conservador por tienda en la hora pico de un dia normal:
//     1 orden cada 3 minutos (~20 ordenes/hora/tienda). Se ignora a proposito
//     el canal digital para no inflar el piso.
//   - 32 tiendas x 20 ordenes/hora = 640 ordenes/hora ~= 10.7 ordenes/minuto
//     como piso de "dia normal".
//   - Aplicando el factor 8x declarado: ~85 ordenes/minuto de pico.
//   - Se redondea hacia abajo (conservador) a 80 ordenes/minuto
//     (~1.33 ordenes/seg) sostenidas durante toda la ventana de la prueba.
//
// Es una estimacion de ingenieria, no un dato del cliente: queda documentada
// aca y en el guion (documentacion/docs/PRUEBA-CARGA-EAC01.md) para que el
// equipo la reemplace si aparece una cifra oficial. Se puede ajustar sin
// tocar el script via la variable de entorno ORDENES_POR_MINUTO.
//
// ---------------------------------------------------------------------------
// Flujo por orden virtual (punto 3 del encargo)
// ---------------------------------------------------------------------------
// La saga ya integra Inventario (consumidor de eventos en Ordenes):
//
//   1) POST /api/ordenes                    -> CREADA
//   2) POST /api/ordenes/{id}/confirmar     -> RESERVA_SOLICITADA
//   3) Saga avanza sola:
//      - Ordenes emite ReservaInventarioSolicitada (outbox)
//      - Inventario reserva y emite InventarioReservado
//      - Ordenes consume InventarioReservado -> INVENTARIO_RESERVADO
//      - Ordenes emite CobroSolicitado (outbox)
//      - Pagos procesa el cobro contra PayKlik (ServicioCobro)
//
// La prueba EAC-01 no necesita forzar reserva ni cobro: la saga los maneja.
// Solo se fuerza DESPACHO_SOLICITADO porque el servicio de Logistica aun no
// existe. El VU de k6 no espera el resultado del cobro: la secuencia
// asincrona via outbox es lo que EAC-01 exige validar. La verificacion de
// que el cobro se resolvio (o quedo auditado como fallido) se hace aparte,
// contra las bases, con el segundo script.
//
// ---------------------------------------------------------------------------
// Tres fases (punto 4 del encargo)
// ---------------------------------------------------------------------------
// La carga (ORDENES_POR_MINUTO) es constante durante toda la prueba; lo que
// cambia es el modo del mock de PayKlik, contra su endpoint de administracion
// PUT /admin/modo (ver Program.cs de SgoLogic.Mocks.PayKlik):
//
//   t = 0s                              -> PayKlik en modo NORMAL (calentamiento)
//   t = CALENTAMIENTO_SEG                -> PayKlik pasa a modo CAIDO
//   t = CALENTAMIENTO_SEG + CAIDA_SEG    -> PayKlik vuelve a modo NORMAL
//   fin = CALENTAMIENTO_SEG + CAIDA_SEG + RECUPERACION_SEG
//
// Los tres cambios de modo se agendan como escenarios propios de k6 con
// startTime exacto, para que quede clara la marca de tiempo de cada
// transicion en el resumen de la corrida.

import http from 'k6/http';
import { check } from 'k6';
import { Counter } from 'k6/metrics';

const ORDENES_URL = (__ENV.ORDENES_URL || 'http://ordenes-api:8080').replace(/\/+$/, '');
const INVENTARIO_URL = (__ENV.INVENTARIO_URL || 'http://inventario-api:8080').replace(/\/+$/, '');
const PAYKLIK_ADMIN_URL = (__ENV.PAYKLIK_ADMIN_URL || 'http://payklik-mock:8080').replace(/\/+$/, '');

const ORDENES_POR_MINUTO = Number(__ENV.ORDENES_POR_MINUTO || 80);
const CALENTAMIENTO_SEG = Number(__ENV.CALENTAMIENTO_SEG || 90);
const CAIDA_SEG = Number(__ENV.CAIDA_SEG || 120);
const RECUPERACION_SEG = Number(__ENV.RECUPERACION_SEG || 90);

// Marca de tiempo de carga del script (compartida por todos los escenarios:
// el codigo de alcance de modulo corre una vez en el contexto init y k6 lo
// reusa para cada VU). Sirve solo para loguear el "t=" de cada transicion de
// modo; no participa en la logica de la prueba.
const INICIO_MS = Date.now();

// Identifica las ordenes de esta corrida para que el script de verificacion
// pueda filtrarlas sin tocar datos de otras pruebas o de la demo manual.
const RUN_ID = __ENV.RUN_ID || `${INICIO_MS}`;
export const PREFIJO_CLIENTE = `CARGA-EAC01-${RUN_ID}`;

const DURACION_TOTAL_SEG = CALENTAMIENTO_SEG + CAIDA_SEG + RECUPERACION_SEG;
const ORDENES_TOTALES = ORDENES_POR_MINUTO * Math.ceil(DURACION_TOTAL_SEG / 60);
const INVENTARIO_POR_ORDEN = Number(__ENV.INVENTARIO_POR_ORDEN || 5);
const STOCK_TOTAL = ORDENES_TOTALES * INVENTARIO_POR_ORDEN;

const TASA_POR_SEGUNDO = Math.max(1, Math.round(ORDENES_POR_MINUTO / 60));
const RAMPA_INICIAL_SEG = Math.min(30, CALENTAMIENTO_SEG);
const RESTO_CALENTAMIENTO_SEG = Math.max(1, CALENTAMIENTO_SEG - RAMPA_INICIAL_SEG);

const JSON_HEADERS = { headers: { 'Content-Type': 'application/json' } };

const ordenesCreadas = new Counter('sgo_ordenes_creadas');
const ordenesConCobroSolicitado = new Counter('sgo_ordenes_cobro_solicitado');
const fallosSincronos = new Counter('sgo_fallos_sincronos');

export const options = {
  scenarios: {
    // Fase unica de carga: la tasa no cambia entre calentamiento, caida y
    // recuperacion (solo cambia el modo de PayKlik, via los escenarios de
    // abajo). El primer stage es una rampa corta para no golpear la tasa
    // objetivo de un solo salto.
    carga_ordenes: {
      executor: 'ramping-arrival-rate',
      exec: 'flujoOrden',
      startRate: 0,
      timeUnit: '1s',
      preAllocatedVUs: 20,
      maxVUs: 60,
      stages: [
        { target: TASA_POR_SEGUNDO, duration: `${RAMPA_INICIAL_SEG}s` },
        { target: TASA_POR_SEGUNDO, duration: `${RESTO_CALENTAMIENTO_SEG}s` },
        { target: TASA_POR_SEGUNDO, duration: `${CAIDA_SEG}s` },
        { target: TASA_POR_SEGUNDO, duration: `${RECUPERACION_SEG}s` },
      ],
    },
    // Los tres cambios de modo del mock, agendados con startTime absoluto
    // sobre la misma linea de tiempo que carga_ordenes.
    payklik_normal_inicio: {
      executor: 'shared-iterations',
      exec: 'ponerModoPayKlik',
      vus: 1,
      iterations: 1,
      startTime: '0s',
      env: { SGO_MODO: 'NORMAL' },
    },
    payklik_caida: {
      executor: 'shared-iterations',
      exec: 'ponerModoPayKlik',
      vus: 1,
      iterations: 1,
      startTime: `${CALENTAMIENTO_SEG}s`,
      env: { SGO_MODO: 'CAIDO' },
    },
    payklik_recuperacion: {
      executor: 'shared-iterations',
      exec: 'ponerModoPayKlik',
      vus: 1,
      iterations: 1,
      startTime: `${CALENTAMIENTO_SEG + CAIDA_SEG}s`,
      env: { SGO_MODO: 'NORMAL' },
    },
  },
};

function sembrarExistencias() {
  const body = JSON.stringify({
    sku: 'SKU-CARGA-EAC01',
    ubicacionId: 'CD-01',
    tipoUbicacion: 'CENTRO_DISTRIBUCION',
    cantidadFisica: STOCK_TOTAL,
  });
  const res = http.put(`${INVENTARIO_URL}/api/inventario/existencias`, body, JSON_HEADERS);
  if (res.status !== 200) {
    console.error(`[eac01-hu023] FALLO siembra de existencias: status=${res.status} body=${res.body}`);
    throw new Error(`Siembra de inventario fallo con status ${res.status}`);
  }
  console.log(`[eac01-hu023] existencias sembradas: SKU-CARGA-EAC01 x${STOCK_TOTAL} en CD-01`);
}

export function setup() {
  sembrarExistencias();
  console.log(
    `[eac01-hu023] arrancando: ${TASA_POR_SEGUNDO} ordenes/seg (~${ORDENES_POR_MINUTO}/min), ` +
    `calentamiento=${CALENTAMIENTO_SEG}s caida=${CAIDA_SEG}s recuperacion=${RECUPERACION_SEG}s, ` +
    `stock=${STOCK_TOTAL} unidades, prefijo=${PREFIJO_CLIENTE}`
  );
  return { inicio: Date.now() };
}

export function ponerModoPayKlik() {
  const modo = __ENV.SGO_MODO;
  const segundoTranscurrido = Math.round((Date.now() - INICIO_MS) / 1000);

  const res = http.put(
    `${PAYKLIK_ADMIN_URL}/admin/modo`,
    JSON.stringify({ modo, latenciaMs: null, probabilidadFallo: null }),
    JSON_HEADERS
  );

  check(res, { [`PayKlik -> ${modo} (200)`]: (r) => r.status === 200 });
  console.log(`[eac01-hu023] t=${segundoTranscurrido}s PayKlik admin/modo -> ${modo} (status ${res.status})`);
}

export function flujoOrden() {
  const clienteId = `${PREFIJO_CLIENTE}-${__VU}-${__ITER}`;

  const crearBody = JSON.stringify({
    clienteId,
    canal: 'WEB',
    metodoEntrega: 'DOMICILIO',
    moneda: 'GTQ',
    medioPago: 'TARJETA',
    impuestos: 12.0,
    lineas: [
      {
        sku: 'SKU-CARGA-EAC01',
        descripcion: 'Producto de prueba de carga EAC-01/HU-023',
        cantidad: 1,
        precioUnitario: 100.0,
      },
    ],
  });

  const resCrear = http.post(`${ORDENES_URL}/api/ordenes`, crearBody, {
    ...JSON_HEADERS,
    tags: { paso: 'crear' },
  });

  if (!check(resCrear, { 'orden creada (201)': (r) => r.status === 201 })) {
    fallosSincronos.add(1);
    return;
  }
  ordenesCreadas.add(1);

  const ordenId = JSON.parse(resCrear.body).id;

  const resConfirmar = http.post(
    `${ORDENES_URL}/api/ordenes/${ordenId}/confirmar`,
    JSON.stringify({ motivo: 'confirmacion automatica - prueba de carga EAC-01/HU-023' }),
    { ...JSON_HEADERS, tags: { paso: 'confirmar' } }
  );
  if (!check(resConfirmar, { 'orden confirmada (200)': (r) => r.status === 200 })) {
    fallosSincronos.add(1);
    return;
  }

  // La saga avanza sola desde aqui: RESERVA_SOLICITADA -> INVENTARIO_RESERVADO
  // -> COBRO_SOLICITADO -> PAGO_APLICADO -> CONFIRMADA_PENDIENTE_FISCAL ->
  // CONFIRMADA. No se fuerza ninguna de estas transiciones.

  // Forzada: DESPACHO_SOLICITADO. Temporal hasta que exista el servicio de
  // Logistica; sin esto la orden queda en CONFIRMADA y no avanza mas.
  const resDespacho = http.post(
    `${ORDENES_URL}/api/ordenes/${ordenId}/transiciones`,
    JSON.stringify({
      estadoDestino: 'DESPACHO_SOLICITADO',
      pasoSaga: 'DESPACHO',
      motivo: 'logistica no existe como servicio: transicion forzada temporal',
    }),
    { ...JSON_HEADERS, tags: { paso: 'despacho_solicitado' } }
  );
  if (check(resDespacho, { 'despacho solicitado forzado (200)': (r) => r.status === 200 })) {
    ordenesConCobroSolicitado.add(1);
  } else {
    fallosSincronos.add(1);
  }
}

export function teardown(data) {
  const duracionSeg = Math.round((Date.now() - data.inicio) / 1000);
  console.log(
    `[eac01-hu023] fin de la generacion de carga tras ~${duracionSeg}s. ` +
    `Espera al menos 60s a que drene el pipeline asincrono (outbox + consumidor de Pagos) ` +
    `y corre eac01-hu023-verificar.sh con RUN_ID=${RUN_ID} (prefijo ${PREFIJO_CLIENTE}).`
  );
}
